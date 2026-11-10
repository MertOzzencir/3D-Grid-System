using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Kazılabilir maden bloğu (kullanıcı: blok 3×3×3 = 27 küçük parçaya bölünür). Zemin (base) türü: geliştirici build
// mode'unun Base menüsünden konur; görüntüsü değiştiği için base birleştirmeye girmez (IUnbatchedBase).
// Kazma Astroneer'daki gibi bir alana (kullanıcı: tek tek tıklamak clicker oyunu gibiydi): matkapla nişan alınınca
// kameradan gelen ışının bloğun içinde vurduğu ilk dolu parçanın noktası nişan noktası olur (collider tek, bloğun
// kendisi). Matkap basılı tutuldukça (Drill.InteractContract → DrillTick) nişan noktasının fırça yarıçapındaki parçalar
// tek seferde kopar (kullanıcı: yavaşça küçülmesin), her biri için bir cevher topağı (OreChunk) bloğun yanındaki
// sütunların en üst zemininin üstüne düşer (orada aynı türden yığın varsa ona eklenir). Fırçanın altındaki parçalar
// hafifçe parlar. Bütün parçalar bitince blok yok olur. Kalan parçalar kayda girer (27 bitlik maske).
public class MineableDeposit : GridBase, IToolTarget, ISaveState, IUnbatchedBase
{
    private const int Size = 3;
    private const int Count = Size * Size * Size;
    private const float Cell = 1f / Size;

    [SerializeField] private GridPlaceableSO chunkData;
    [SerializeField] private Material rockMaterial;
    [Tooltip("Parçalar arası boşluk (birim): bloğun bölündüğü görünsün")]
    [SerializeField] private float gap = 0.02f;
    [Tooltip("Cevherin düştüğü hücre bloktan en fazla bu kadar uzakta aranır")]
    [SerializeField] private int dropRadius = 3;
    [Tooltip("Matkabın ucu bloğun yüzeyinden bu kadar geride (kameraya doğru) durur")]
    [SerializeField] private float drillStandOff = 0.3f;
    [Tooltip("Mouse ışını dolu bir parçaya tam değmese de bu kadar (birim) yakınındaki en yakın parça hedef alınır")]
    [SerializeField] private float aimTolerance = 0.5f;
    [Tooltip("Fırçanın altındaki parçaların parlaması")]
    [SerializeField] private Color brushTint = new Color(1f, 0.85f, 0.55f);

    private int mask = (1 << Count) - 1; // bit = dolu parça
    private readonly Transform[] parts = new Transform[Count];
    private readonly Renderer[] renderers = new Renderer[Count];
    private BoxCollider box;
    private bool hasAim;
    private Vector3 aimPoint;     // dünya
    private Vector3 drillPoint;   // matkabın durduğu yer: bloğun dışında, ışın üstünde
    private Vector3 aimDirection; // kameradan gelen ışının yönü
    private float brushUntil;     // fırça bu zamana kadar gösterilir
    private float brushRadius;
    private readonly HashSet<int> tinted = new HashSet<int>();
    private static MaterialPropertyBlock block;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
            box.size = Vector3.one;
        }
        for (int i = 0; i < Count; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.name = "Part " + i;
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = PartCenter(i);
            renderers[i] = cube.GetComponent<Renderer>();
            if (rockMaterial != null) renderers[i].sharedMaterial = rockMaterial;
            parts[i] = cube.transform;
        }
        ApplyMask();
    }

    private static Vector3 PartCenter(int index)
    {
        int x = index % Size, y = index / Size % Size, z = index / (Size * Size);
        return new Vector3((x + 0.5f) * Cell - 0.5f, (y + 0.5f) * Cell - 0.5f, (z + 0.5f) * Cell - 0.5f);
    }

    private bool Filled(int index) => (mask & (1 << index)) != 0;

    private void ApplyMask()
    {
        for (int i = 0; i < Count; i++)
        {
            if (parts[i] == null) continue;
            parts[i].gameObject.SetActive(Filled(i));
            parts[i].localScale = Vector3.one * (Cell - gap);
        }
    }

    // Kameradan mouse yönünde vurulan ilk dolu parçanın üstündeki nokta. Işın hiçbir dolu parçaya değmiyorsa (bloğu ya da
    // oyulmuş bir boşluğu birkaç piksel kaçırınca) ışına aimTolerance'tan yakın en yakın parça alınır. drill: matkabın
    // durduğu nokta, bloğun girişinin dışında (oyuk derinse matkap bloğun içine gömülmesin).
    private bool AimUnderMouse(out Vector3 point, out Vector3 direction, out Vector3 drill)
    {
        point = default;
        drill = default;
        direction = default;
        if (box == null || Camera.main == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        direction = ray.direction;
        if (box.Raycast(ray, out RaycastHit hit, 500f) && MarchToPart(hit.point, ray.direction, out point))
        {
            drill = hit.point - direction * drillStandOff;
            return true;
        }
        if (!NearestPartToRay(ray, out int nearest)) return false;
        Vector3 center = transform.TransformPoint(PartCenter(nearest));
        point = center - direction * (Cell * 0.5f);
        drill = (box.Raycast(new Ray(center - direction * 2f, direction), out RaycastHit entry, 4f) ? entry.point : point)
                - direction * drillStandOff;
        return true;
    }

    private bool NearestPartToRay(Ray ray, out int nearest)
    {
        nearest = -1;
        float best = aimTolerance;
        for (int i = 0; i < Count; i++)
        {
            if (!Filled(i)) continue;
            Vector3 center = transform.TransformPoint(PartCenter(i));
            float distance = Vector3.Cross(ray.direction, center - ray.origin).magnitude; // ışına dik uzaklık
            if (distance >= best) continue;
            best = distance;
            nearest = i;
        }
        return nearest >= 0;
    }

    // Işın girişinden içeri küçük adımlarla: ilk dolu parçanın noktası
    private bool MarchToPart(Vector3 entry, Vector3 rayDirection, out Vector3 point)
    {
        point = default;
        Vector3 start = transform.InverseTransformPoint(entry);
        Vector3 local = transform.InverseTransformDirection(rayDirection).normalized;
        for (float s = 0.001f; s < 1.8f; s += 0.02f)
        {
            Vector3 p = start + local * s;
            if (Mathf.Abs(p.x) > 0.5f || Mathf.Abs(p.y) > 0.5f || Mathf.Abs(p.z) > 0.5f) break;
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x + 0.5f) * Size), 0, Size - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((p.y + 0.5f) * Size), 0, Size - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((p.z + 0.5f) * Size), 0, Size - 1);
            if (!Filled(x + y * Size + z * Size * Size)) continue;
            point = transform.TransformPoint(p);
            return true;
        }
        return false;
    }

    // --- IToolTarget: matkap nişan noktasına bakar ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        hasAim = interacted is Drill && AimUnderMouse(out aimPoint, out aimDirection, out drillPoint);
        if (!hasAim) return default;
        accept = true;
        return drillPoint;
    }

    // Matkabın ucu (görselin -Y'si) nişan yönüne baksın
    public Quaternion GetToolTargetRotation() => hasAim ? Quaternion.FromToRotation(Vector3.down, aimDirection) : Quaternion.identity;

    public void OnToolTargetExit()
    {
        hasAim = false;
        ClearTint();
    }

    public bool OnToolUsed(IInteractable tool) => false; // kazma basılı tutarak (DrillTick)

    // Fırçanın altındaki parçaları göster (kazmadan nişan alırken)
    public void ShowBrush(float radius)
    {
        brushRadius = radius;
        brushUntil = Time.time + 0.1f;
    }

    // Matkap basılıyken her kare: fırçadaki parçalar tek seferde kopar
    public void DrillTick(float radius)
    {
        if (!hasAim) return;
        ShowBrush(radius);
        for (int i = 0; i < Count; i++)
        {
            if (!Filled(i)) continue;
            if (Vector3.Distance(transform.TransformPoint(PartCenter(i)), aimPoint) > radius) continue;
            mask &= ~(1 << i);
            StartCoroutine(Pop(parts[i]));
            DropChunk(transform.TransformPoint(PartCenter(i)));
        }
        if (mask == 0) StartCoroutine(Vanish());
    }

    // Kopan parça bir an içinde küçülüp kaybolur
    private IEnumerator Pop(Transform part)
    {
        Vector3 start = Vector3.one * (Cell - gap);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.08f)
        {
            part.localScale = start * (1f - t);
            yield return null;
        }
        part.gameObject.SetActive(false);
        part.localScale = start;
    }

    private void LateUpdate()
    {
        if (!hasAim || Time.time > brushUntil)
        {
            ClearTint();
            return;
        }
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < Count; i++)
        {
            bool inBrush = Filled(i) && Vector3.Distance(transform.TransformPoint(PartCenter(i)), aimPoint) <= brushRadius;
            if (inBrush == tinted.Contains(i)) continue;
            if (inBrush)
            {
                Color baseColor = rockMaterial != null ? rockMaterial.color : Color.gray;
                renderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(baseColor, brushTint, 0.45f));
                renderers[i].SetPropertyBlock(block);
                tinted.Add(i);
            }
            else
            {
                renderers[i].SetPropertyBlock(null);
                tinted.Remove(i);
            }
        }
    }

    private void ClearTint()
    {
        if (tinted.Count == 0) return;
        foreach (int i in tinted)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(null);
        tinted.Clear();
    }

    private bool vanishing;

    private IEnumerator Vanish()
    {
        if (vanishing) yield break;
        vanishing = true;
        yield return new WaitForSeconds(0.2f);
        GridManager.Instance.RemoveBase(this, true);
    }

    // Cevher: yakındaki aynı türden yığına eklenir, yoksa en yakın sütunun en üst zemininin üstündeki boş hücreye yeni topak
    private void DropChunk(Vector3 from)
    {
        if (chunkData == null || GridManager.Instance == null) return;
        Vector3Int origin = OriginWorldPosition;
        var candidates = new List<Vector3Int>();
        for (int r = 1; r <= dropRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == r
                        && GridManager.Instance.TryGetTopBase(origin.x + dx, origin.z + dz, out GridData top))
                        candidates.Add(top.WorldPosition + Vector3Int.up);

        foreach (Vector3Int cell in candidates)
            if (GridManager.Instance.TryGetCell(cell, out GridData data) && data.Placeable is OreChunk pile
                && pile.GetData() == chunkData)
            {
                pile.AddToStack(1);
                return;
            }

        foreach (Vector3Int cell in candidates)
        {
            if (!GridManager.Instance.TryGetCell(cell, out GridData data) || data.Placeable != null || data.Base != null) continue;
            var chunk = chunkData.Spawn() as OreChunk;
            if (chunk == null) return;
            if (!GridManager.Instance.PlaceablePlaceOn(chunk, cell))
            {
                Destroy(chunk.gameObject);
                continue;
            }
            chunk.HopFrom(from);
            return;
        }
    }

    // --- ISaveState: kalan parçalar ---
    public string CaptureState() => mask.ToString();

    public void RestoreState(string state)
    {
        if (int.TryParse(state, out int loaded) && loaded != 0) mask = loaded;
        ApplyMask();
    }
}
