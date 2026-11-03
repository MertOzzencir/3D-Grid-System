using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 1×1 odunların GPU instancing'li çizimi (kullanıcı: en çok yer kaplayan bu; ayrı shader: Village/Wood Instanced Lit,
// WoodInstanced.hlsl). Kayıtlı odun görselleri (tek duran Wood'un Visual'ı, MergedWood'un parçaları) kendi renderer'larıyla
// çizilmez (forceRenderingOff; renderer açık kalır, collider / bounds / diğer kodlar değişmez), her kare buradan aynı mesh +
// malzeme + katman grupları halinde Graphics.RenderMeshInstanced ile çizilir.
// Sallanma (JellyWobble) ve saydamlık (DitherFade) renderer'lara eskisi gibi MaterialPropertyBlock ile yazılmaya devam eder;
// burada her kare okunup instance başına değere çevrilir (sallanma renderer'ın kendi uzayından dünyaya). Böylece o kodlar
// değişmedi. Shader yoksa (atanmamış) ya da kapalıysa odunlar eski haliyle kendi renderer'larıyla çizilir.
public static class WoodInstancer
{
    private const int BatchSize = 250; // instance başına 2 matris + 5 vektör; sabit tampon sınırının altında kalsın

    private static readonly int WobbleBaseId = Shader.PropertyToID("_WobbleBase");
    private static readonly int WobbleDirId = Shader.PropertyToID("_WobbleDir");
    private static readonly int WobbleUpId = Shader.PropertyToID("_WobbleUp");
    private static readonly int WobbleShapeId = Shader.PropertyToID("_WobbleShape");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int WoodWobbleBaseId = Shader.PropertyToID("_WoodWobbleBase");
    private static readonly int WoodWobbleDirId = Shader.PropertyToID("_WoodWobbleDir");
    private static readonly int WoodWobbleUpId = Shader.PropertyToID("_WoodWobbleUp");
    private static readonly int WoodWobbleShapeId = Shader.PropertyToID("_WoodWobbleShape");
    private static readonly int WoodFadeId = Shader.PropertyToID("_WoodFade");

    private struct Entry
    {
        public MeshRenderer Renderer;
        public MeshFilter Filter;
    }

    private class Group
    {
        public Mesh Mesh;
        public int SubMesh;
        public Material Material; // instanced kopya
        public int Layer;
        public ShadowCastingMode Shadow;
        public bool ReceiveShadows;
        public uint RenderingLayers;
        public readonly List<MeshRenderer> Renderers = new List<MeshRenderer>();
    }

    private class Batch
    {
        public readonly Matrix4x4[] Matrices = new Matrix4x4[BatchSize];
        public readonly Vector4[] WobbleBase = new Vector4[BatchSize];
        public readonly Vector4[] WobbleDir = new Vector4[BatchSize];
        public readonly Vector4[] WobbleUp = new Vector4[BatchSize];
        public readonly Vector4[] WobbleShape = new Vector4[BatchSize];
        public readonly Vector4[] Fade = new Vector4[BatchSize];
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    }

    private static readonly List<Entry> entries = new List<Entry>();
    private static readonly HashSet<MeshRenderer> known = new HashSet<MeshRenderer>();
    private static readonly Dictionary<(Mesh, int, Material, int, ShadowCastingMode, bool, uint), Group> groups =
        new Dictionary<(Mesh, int, Material, int, ShadowCastingMode, bool, uint), Group>();
    private static readonly Dictionary<Material, Material> instancedMaterials = new Dictionary<Material, Material>();
    private static readonly List<Batch> batches = new List<Batch>();
    private static readonly List<Material> materialBuffer = new List<Material>();
    private static MaterialPropertyBlock readBlock;
    private static Shader activeShader;
    private static bool drawing;

    // Odun görselinin MeshRenderer'larını kaydeder (silinenler kendiliğinden düşer)
    public static void Register(GameObject visual)
    {
        if (visual == null) return;
        foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!known.Add(renderer) || !renderer.TryGetComponent(out MeshFilter filter)) continue;
            entries.Add(new Entry { Renderer = renderer, Filter = filter });
            if (drawing) renderer.forceRenderingOff = true;
        }
    }

    // Her kare bir kez (WoodInstancerDriver, kare sonunda: odunlar bu karede yer değiştirmiş olsun)
    public static void Draw(Shader shader, bool enabled)
    {
        bool shouldDraw = enabled && shader != null && shader.isSupported;
        if (shouldDraw != drawing || shader != activeShader)
        {
            drawing = shouldDraw;
            activeShader = shader;
            instancedMaterials.Clear();
            groups.Clear();
            foreach (Entry entry in entries)
                if (entry.Renderer != null) entry.Renderer.forceRenderingOff = drawing;
        }
        if (!drawing) return;

        foreach (Group group in groups.Values) group.Renderers.Clear();
        entries.RemoveAll(e => e.Renderer == null);
        known.RemoveWhere(r => r == null);

        foreach (Entry entry in entries)
        {
            MeshRenderer renderer = entry.Renderer;
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Mesh mesh = entry.Filter.sharedMesh;
            if (mesh == null) continue;
            renderer.forceRenderingOff = true;
            renderer.GetSharedMaterials(materialBuffer);
            for (int sub = 0; sub < mesh.subMeshCount && sub < materialBuffer.Count; sub++)
            {
                Material source = materialBuffer[sub];
                if (source == null) continue;
                var key = (mesh, sub, source, renderer.gameObject.layer, renderer.shadowCastingMode, renderer.receiveShadows,
                           renderer.renderingLayerMask);
                if (!groups.TryGetValue(key, out Group group))
                {
                    groups[key] = group = new Group
                    {
                        Mesh = mesh, SubMesh = sub, Material = Instanced(source), Layer = key.Item4,
                        Shadow = key.Item5, ReceiveShadows = key.Item6, RenderingLayers = key.Item7
                    };
                }
                group.Renderers.Add(renderer);
            }
        }

        int used = 0;
        foreach (Group group in groups.Values)
            for (int start = 0; start < group.Renderers.Count; start += BatchSize)
            {
                if (used == batches.Count) batches.Add(new Batch());
                DrawBatch(group, start, Mathf.Min(BatchSize, group.Renderers.Count - start), batches[used++]);
            }
    }

    private static void DrawBatch(Group group, int start, int count, Batch batch)
    {
        readBlock ??= new MaterialPropertyBlock();
        var bounds = new Bounds();
        for (int i = 0; i < count; i++)
        {
            MeshRenderer renderer = group.Renderers[start + i];
            Transform transform = renderer.transform;
            batch.Matrices[i] = transform.localToWorldMatrix;
            Bounds rendererBounds = renderer.bounds;
            if (i == 0) bounds = rendererBounds;
            else bounds.Encapsulate(rendererBounds);

            Vector4 wobbleBase = default, wobbleDir = default, wobbleUp = default, wobbleShape = default, fade = default;
            if (renderer.HasPropertyBlock())
            {
                renderer.GetPropertyBlock(readBlock);
                fade = readBlock.GetVector(FadeId);
                wobbleShape = readBlock.GetVector(WobbleShapeId);
                if (wobbleShape.x > 0f)
                {
                    // JellyWobble renderer'ın kendi uzayında yazar; instanced shader dünya uzayında sallar
                    Vector4 baseOS = readBlock.GetVector(WobbleBaseId);
                    Vector4 dirOS = readBlock.GetVector(WobbleDirId);
                    Vector4 upOS = readBlock.GetVector(WobbleUpId);
                    Vector3 basePoint = transform.TransformPoint(baseOS);
                    Vector3 dir = transform.TransformVector(dirOS);
                    Vector3 up = transform.TransformDirection(upOS);
                    float height = baseOS.w * transform.TransformVector(upOS).magnitude; // yerel boy → dünya boyu
                    wobbleBase = new Vector4(basePoint.x, basePoint.y, basePoint.z, height);
                    wobbleDir = new Vector4(dir.x, dir.y, dir.z, dirOS.w);
                    wobbleUp = new Vector4(up.x, up.y, up.z, upOS.w);
                }
            }
            batch.WobbleBase[i] = wobbleBase;
            batch.WobbleDir[i] = wobbleDir;
            batch.WobbleUp[i] = wobbleUp;
            batch.WobbleShape[i] = wobbleShape;
            batch.Fade[i] = fade;
        }

        // Diziler hep tam boy (MaterialPropertyBlock dizi boyu ilk atamada sabitlenir)
        batch.Block.SetVectorArray(WoodWobbleBaseId, batch.WobbleBase);
        batch.Block.SetVectorArray(WoodWobbleDirId, batch.WobbleDir);
        batch.Block.SetVectorArray(WoodWobbleUpId, batch.WobbleUp);
        batch.Block.SetVectorArray(WoodWobbleShapeId, batch.WobbleShape);
        batch.Block.SetVectorArray(WoodFadeId, batch.Fade);

        var parameters = new RenderParams(group.Material)
        {
            layer = group.Layer,
            shadowCastingMode = group.Shadow,
            receiveShadows = group.ReceiveShadows,
            renderingLayerMask = group.RenderingLayers,
            lightProbeUsage = LightProbeUsage.Off, // ortam ışığı (sahnede ışık probu yok)
            worldBounds = bounds,
            matProps = batch.Block
        };
        Graphics.RenderMeshInstanced(parameters, group.Mesh, group.SubMesh, batch.Matrices, count);
    }

    // Odunun malzemesinin instanced shader'lı kopyası (dokular ve ayarlar aynı)
    private static Material Instanced(Material source)
    {
        if (instancedMaterials.TryGetValue(source, out Material made)) return made;
        made = new Material(source) { name = source.name + " (Instanced)", shader = activeShader, enableInstancing = true };
        made.CopyPropertiesFromMaterial(source);
        made.shaderKeywords = source.shaderKeywords;
        instancedMaterials[source] = made;
        return made;
    }
}

// WoodInstancer'ı her kare en sonda çağırır (WoodMerger kurar; odunların bu karedeki son yeri çizilsin)
[DefaultExecutionOrder(10000)]
public class WoodInstancerDriver : MonoBehaviour
{
    public Shader Shader;
    public bool Enabled = true;

    private void LateUpdate() => WoodInstancer.Draw(Shader, Enabled);
}
