using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Odun işleme istasyonlarının ortak tabanı (kesme kütüğü, testere sehpası): grid'de taşınan placeable. Üstüne tarifi
// olan bir parça sağ tıkla sürüklenip bırakılır (ya da sürüklerken sol tık; IHoldTarget). İstasyonun aleti (balta,
// testere) işi bitirince parça bölünür: tarifteki çıktılar istasyonun kameraya göre tam soluna ve sağına (ince yüzü
// istasyona dönük), o hücreler doluysa çevresindeki boş yerlere zıplar.
// Üstteki parça istasyonun çocuğu olur, collider'ları kapanır; yerine üstte "Top" collider'ı (WorkstationTop) açılır:
// aletler ve parçalar oraya hedeflenir, sağ tıkla tutulunca parça istasyondan alınır.
// Yığın: aynı türden parçalar StackLimit kadar üst üste konabilir (kesme kütüğünde 1×1 odun 3'e kadar). Alet hep en
// alttakini (held) işler; o bölününce üsttekiler kayarak bir kat iner. Elle (sağ tık) en üstteki alınır.
// Tarifler Inspector'da: girdi SO → çıktı SO × adet. Çıktısı boş tarif: parça konur ama bölünmez. Üstteki parça kaydedilmez.
// Görünüm: Model atanırsa o (modelin pivotu ayak izinin ortasına konur: 2×2'de dört hücrenin ortası), yoksa alt sınıfın
// kodla kurduğu geçici görünüm (prototip). Kökte collider yoksa görünümü saran kutu eklenir (tutulabilsin).
public abstract class Workstation : GridPlaceable, IInteractable, IStickyToolTarget, IHoldTarget
{
    [Serializable]
    public class Recipe
    {
        [Tooltip("İstasyona konan parçanın SO'su (örn. Wood_1BR, HalfLog)")]
        public GridPlaceableSO input;
        [Tooltip("Bölününce çıkan parçanın SO'su (örn. HalfLog, Plank). Boşsa parça konur ama bölünmez.")]
        public GridPlaceableSO output;
        [Tooltip("Kaç parça çıkar")]
        public int count = 2;
    }

    [SerializeField] private float followSpeed = 10f;
    [SerializeField] private List<Recipe> recipes = new List<Recipe>();

    [Header("Görünüm")]
    [Tooltip("İstasyonun modeli (prefab ya da FBX). Pivotu ayak izinin ortasına (yatayda) konur: modelin pivotu modelin " +
             "ortasında, yüksekliği hücrenin ortasında (taban pivotun 0.5 altında) olmalı. Boşsa geçici görünüm çizilir.")]
    [SerializeField] private GameObject model;
    [Tooltip("Geçici görünümün rengi (model yokken)")]
    [SerializeField] protected Color placeholderColor = new Color(0.55f, 0.38f, 0.24f);

    [Header("Yerleşim")]
    [Tooltip("Üst yüzeyin pivottan yüksekliği (birim). Parça bunun üstüne oturur (parçanın tabanı burada).")]
    [SerializeField] protected float topHeight = 0f;
    [Tooltip("Çıkan parçaların istasyondan yerlerine zıplama süresi (saniye) ve yüksekliği")]
    [SerializeField] private float jumpDuration = 0.35f;
    [SerializeField] private float jumpHeight = 0.6f;

    private GridDragMotor drag;
    private WorkstationTop top;
    private readonly List<GridPlaceable> pieces = new List<GridPlaceable>(); // yığın, alttan üste
    private readonly Dictionary<GridPlaceable, List<Collider>> disabledColliders = new Dictionary<GridPlaceable, List<Collider>>();
    private Coroutine slideRoutine;

    protected GridPlaceable held; // işlenen parça: yığının en altı
    protected Recipe heldRecipe;
    protected Transform visual; // istasyonun görünümü (model ya da geçici)
    protected Quaternion visualRest;

    public GridPlaceable Held => held;
    protected bool CanWork => held != null && heldRecipe != null && heldRecipe.output != null;

    // Ayak izinin ortası (dünya, taban hücrelerinin ortası yüksekliğinde)
    protected Vector3 Center
    {
        get
        {
            Vector3 c = GetData() != null ? GetData().GetFootprint().Center : Vector3.zero;
            return transform.TransformPoint(new Vector3(c.x, 0f, c.z));
        }
    }

    // Üst yüzeyin üstündeki 1 birimlik yerin ortası (Top collider'ı, aletlerin referansı)
    protected Vector3 HoldPoint => Center + Vector3.up * (topHeight + 0.5f);

    // Parçanın yüksekliği (birim): ince parçalar kendi boyunda (kalas yarım birim), diğerleri 1
    protected static float HeightOf(GridPlaceable item) => item is FinePlaceable fine ? fine.Height : 1f;

    // Yığındaki parçaların toplam yüksekliği (birim)
    private float StackHeight
    {
        get
        {
            float height = 0f;
            foreach (GridPlaceable piece in pieces) height += HeightOf(piece);
            return height;
        }
    }

    // Yığının tepesine konacak parçanın yeri (tabanı yığının üstünde, pivotu kutu ortasında)
    protected Vector3 PiecePoint(GridPlaceable item) => Center + Vector3.up * (topHeight + StackHeight + HeightOf(item) * 0.5f);

    // Aynı türden kaç parça üst üste konabilir (alt sınıf: kesme kütüğünde 1×1 odun 3)
    protected virtual int StackLimit(GridPlaceable item) => 1;

    // Üstteki parçanın tepesi (alet buraya iner)
    protected Vector3 PieceTop => Center + Vector3.up * (topHeight + (held != null ? HeightOf(held) : 1f));

    protected virtual void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        BuildVisual();
        var topObject = new GameObject("Top");
        topObject.transform.SetParent(transform, false);
        topObject.transform.position = HoldPoint;
        var box = topObject.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        top = topObject.AddComponent<WorkstationTop>();
        top.Init(this, box);
        box.enabled = false;
    }

    // Model ya da geçici görünüm; kökte collider yoksa görünümü saran kutu
    private void BuildVisual()
    {
        GameObject built;
        if (model != null)
        {
            built = Instantiate(model, transform, false);
            built.transform.localPosition += LocalCenter;
        }
        else
        {
            built = new GameObject("Placeholder");
            built.transform.SetParent(transform, false);
            BuildPlaceholder(built.transform);
        }
        built.name = "Model";
        visual = built.transform;
        visualRest = built.transform.localRotation;

        if (GetComponent<Collider>() != null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer r in built.GetComponentsInChildren<Renderer>())
        {
            var local = new Bounds(transform.InverseTransformPoint(r.bounds.center), r.bounds.size);
            if (any) bounds.Encapsulate(local);
            else bounds = local;
            any = true;
        }
        box.center = any ? bounds.center : Vector3.down * 0.25f;
        box.size = any ? bounds.size : new Vector3(0.9f, 0.5f, 0.9f);
    }

    // Model yokken geçici görünüm (parent'ın altına, yerel uzayda: pivot ilk hücrenin ortası)
    protected abstract void BuildPlaceholder(Transform parent);

    // Geçici görünüm için ışık alan kutu / silindir (collider'sız)
    protected GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().material.color = color;
        return part;
    }

    // Ayak izinin ortası, yerel uzayda (geçici görünümleri ortalamak için)
    protected Vector3 LocalCenter
    {
        get
        {
            Vector3 c = GetData() != null ? GetData().GetFootprint().Center : Vector3.zero;
            return new Vector3(c.x, 0f, c.z);
        }
    }

    private Recipe RecipeFor(GridPlaceable item)
    {
        if (item == null) return null;
        GridEntitySOBase data = item.GetData();
        foreach (Recipe recipe in recipes)
            if (recipe.input != null && recipe.input == data) return recipe;
        return null;
    }

    // --- Parça koyma (IHoldTarget) ---

    // Boşsa tarifi olan her parça; doluysa aynı türden ve yığın sınırı dolmadıysa
    public bool CanHold(GridPlaceable item)
    {
        if (item == null || item == this || pieces.Contains(item) || RecipeFor(item) == null) return false;
        if (pieces.Count == 0) return true;
        return item.GetData() == pieces[0].GetData() && pieces.Count < StackLimit(item);
    }

    public bool TryHold(GridPlaceable item)
    {
        if (!CanHold(item)) return false;
        item.transform.SetParent(transform, true);
        item.transform.SetPositionAndRotation(PiecePoint(item), transform.rotation);
        var colliders = new List<Collider>();
        foreach (Collider c in item.GetComponentsInChildren<Collider>())
            if (c.enabled)
            {
                c.enabled = false;
                colliders.Add(c);
            }
        disabledColliders[item] = colliders;
        pieces.Add(item);
        bool first = pieces.Count == 1;
        RefreshStack(false);
        if (first) OnHeld();
        return true;
    }

    // Elle alındı (sağ tıkla tutuldu): en üstteki parça bırakılır, collider'ları açılır
    public GridPlaceable Release()
    {
        if (pieces.Count == 0) return null;
        bool wasWorked = pieces.Count == 1; // tek parçaysa işlenen parça gidiyor
        GridPlaceable item = TakeAt(pieces.Count - 1);
        RefreshStack(false);
        if (wasWorked) OnReleased();
        return item;
    }

    // İşlenen (en alttaki) parça bölünmek üzere bırakılır; üsttekiler kayarak bir kat iner
    private GridPlaceable ReleaseBottom()
    {
        if (pieces.Count == 0) return null;
        GridPlaceable item = TakeAt(0);
        RefreshStack(true);
        if (pieces.Count == 0) OnReleased();
        else OnHeld(); // sıradaki parça baştan işlenir
        return item;
    }

    private GridPlaceable TakeAt(int index)
    {
        GridPlaceable item = pieces[index];
        pieces.RemoveAt(index);
        item.transform.SetParent(null, true);
        if (disabledColliders.TryGetValue(item, out List<Collider> colliders))
            foreach (Collider c in colliders)
                if (c != null) c.enabled = true;
        disabledColliders.Remove(item);
        return item;
    }

    // held / tarif ve Top collider'ı yığına göre; parçalar yerlerine (slide: bir kat inen parçalar kayarak)
    private void RefreshStack(bool slide)
    {
        held = pieces.Count > 0 ? pieces[0] : null;
        heldRecipe = RecipeFor(held);
        if (pieces.Count == 0)
        {
            top.SetActive(false);
            return;
        }
        float height = StackHeight;
        top.Fit(Center + Vector3.up * (topHeight + height * 0.5f), height);
        top.SetActive(true);
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        if (slide) slideRoutine = StartCoroutine(SlideToPlaces());
        else
        {
            slideRoutine = null;
            for (int i = 0; i < pieces.Count; i++) pieces[i].transform.position = StackPoint(i);
        }
    }

    // Yığındaki i. parçanın yeri
    private Vector3 StackPoint(int index)
    {
        float below = 0f;
        for (int i = 0; i < index; i++) below += HeightOf(pieces[i]);
        return Center + Vector3.up * (topHeight + below + HeightOf(pieces[index]) * 0.5f);
    }

    // Bölünen parçanın yerine üsttekiler kayarak iner (hızlanarak düşer gibi)
    private IEnumerator SlideToPlaces()
    {
        var from = new Vector3[pieces.Count];
        for (int i = 0; i < pieces.Count; i++) from[i] = pieces[i].transform.position;
        const float duration = 0.18f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            t *= t;
            for (int i = 0; i < pieces.Count && i < from.Length; i++)
                if (pieces[i] != null) pieces[i].transform.position = Vector3.Lerp(from[i], StackPoint(i), t);
            yield return null;
        }
        for (int i = 0; i < pieces.Count; i++) pieces[i].transform.position = StackPoint(i);
        slideRoutine = null;
    }

    protected virtual void OnHeld() { }
    protected virtual void OnReleased() { }

    // --- IToolTarget: parça getirilince (koyma) ya da istasyonun aleti getirilince ---

    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        if (ToolPose(interacted, out Vector3 position, out accept)) return position;
        if (interacted is GridPlaceable item && CanHold(item))
        {
            accept = true;
            return PiecePoint(item);
        }
        accept = false;
        return default;
    }

    // Alet bu istasyonun aletiyse true (accept: şu an kabul ediyor mu, position: aletin duracağı yer)
    protected virtual bool ToolPose(IInteractable interacted, out Vector3 position, out bool accept)
    {
        position = default;
        accept = false;
        return false;
    }

    public virtual Quaternion GetToolTargetRotation() => transform.rotation;
    public virtual bool IsStillTargeted(IInteractable interacted, Ray mouseRay) => false;
    public virtual void OnToolTargetExit() { }

    public bool OnToolUsed(IInteractable tool)
    {
        if (UseTool(tool, out bool used)) return used;
        return tool is GridPlaceable item && TryHold(item);
    }

    // Alet bu istasyonun aletiyse true (used: iş yapıldı mı)
    protected virtual bool UseTool(IInteractable tool, out bool used)
    {
        used = false;
        return false;
    }

    // Kameranın sağı, grid eksenine yuvarlanmış (parçalar sağa / sola gider)
    protected static Vector3 CameraRight()
    {
        Vector3 right = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        return Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? new Vector3(Mathf.Sign(right.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(right.z));
    }

    // --- Bölme ---

    // Parça bölünür: ilk iki çıktı kameraya göre tam sola ve sağa (ayak izinin yanındaki hücrelere, ince yüzü istasyona
    // dönük), diğerleri ya da o hücreler doluysa ayak izinin çevresindeki boş yerlere zıplar; yer yoksa ilki istasyonda kalır
    protected void SplitHeld()
    {
        Recipe recipe = heldRecipe;
        GridPlaceable item = ReleaseBottom();
        Destroy(item.gameObject);

        Vector3 start = HoldPoint;
        Vector3 right = CameraRight();
        List<Vector3> ring = RingCells();
        for (int i = 0; i < recipe.count; i++)
        {
            if (!(recipe.output.Prefab is GridPlaceable prefab)) break;
            GridPlaceable piece = Instantiate(prefab, start, transform.rotation);
            piece.AssignData(recipe.output);

            // Tercih: 0. sol, 1. sağ, diğerleri kameraya bakan taraf; hücreler bu yöne yakınlığa göre sıralı
            Vector3 toCamera = Camera.main != null ? Vector3.ProjectOnPlane(-Camera.main.transform.forward, Vector3.up).normalized : Vector3.back;
            Vector3 want = i == 0 ? -right : i == 1 ? right : toCamera;
            // Parçanın kesik yüzü istasyonun ortasına (gittiği yönün tersine) bakar: soldaki sağa, sağdaki sola, ikiye
            // ayrılmış gibi. Parçalar Deg0'da X'te ince olduğu için bu dönüş onları sağ / sol eksenine de oturtur.
            piece.Rotation = FacingRotation(piece is FinePlaceable fine ? fine.InnerFace : Vector3.right, -want);
            Vector3 center = Center;
            ring.Sort((a, b) => Vector3.Dot((b - center).normalized, want).CompareTo(Vector3.Dot((a - center).normalized, want)));

            // Önce sağ / sol (TryPlaceBeside: iki taraf aynı aday sırasını ayna görüntüsüyle dener, eşit dağılır),
            // olmazsa çevredeki hücreler
            bool placed = i < 2 && TryPlaceBeside(piece, want, toCamera);
            if (!placed)
                foreach (Vector3 cell in ring)
                    if (TryPlaceAt(piece, cell))
                    {
                        placed = true;
                        break;
                    }
            if (placed)
            {
                StartCoroutine(Jump(piece.transform, start, piece.transform.position, i));
                continue;
            }
            if (held == null && TryHold(piece)) continue;
            Debug.LogWarning($"{name}: {recipe.output.name} için yer yok", this);
            Destroy(piece.gameObject);
        }
    }

    // Yerel yön (döndürülmemiş halde) dünyada hedef yöne en çok bakacak 90° adımlı dönüş
    private static GridMaskRotator.Rotation FacingRotation(Vector3 localDirection, Vector3 target)
    {
        var best = GridMaskRotator.Rotation.Deg0;
        float bestDot = float.MinValue;
        for (int r = 0; r < 4; r++)
        {
            var rotation = (GridMaskRotator.Rotation)r;
            float dot = Vector3.Dot(GridMaskRotator.ToQuaternion(rotation) * localDirection, target);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = rotation;
            }
        }
        return best;
    }

    // Ayak izini çevreleyen hücrelerin ortaları (taban katında)
    private List<Vector3> RingCells()
    {
        var cells = new List<Vector3>();
        GridFootprint footprint = GetData() != null ? GetData().GetFootprint() : null;
        Vector3Int min = footprint != null ? footprint.Min : Vector3Int.zero;
        Vector3Int max = footprint != null ? footprint.Max : Vector3Int.zero;
        for (int x = min.x - 1; x <= max.x + 1; x++)
            for (int z = min.z - 1; z <= max.z + 1; z++)
            {
                bool inside = x >= min.x && x <= max.x && z >= min.z && z <= max.z;
                if (!inside) cells.Add(transform.TransformPoint(new Vector3(x, 0f, z)));
            }
        return cells;
    }

    // Aday sırası (dış sıra, yana kayma): yakından uzağa; eşitse önce dış sıra yakın olan, sonra kameraya doğru kayan.
    // İki taraf aynı sırayı ayna görüntüsüyle dener: etraf aynıysa sağ ve sol hep eşit düşer.
    private static readonly (int row, int lateral)[] BesideOrder = BuildBesideOrder();

    private static (int, int)[] BuildBesideOrder()
    {
        var order = new List<(int row, int lateral)>();
        for (int row = 0; row < 5; row++)
            foreach (int lateral in new[] { 0, 1, -1, 2, -2 })
                order.Add((row, lateral));
        order.Sort((a, b) =>
        {
            int score = (a.row + Mathf.Abs(a.lateral)).CompareTo(b.row + Mathf.Abs(b.lateral));
            if (score != 0) return score;
            int row = a.row.CompareTo(b.row);
            return row != 0 ? row : b.lateral.CompareTo(a.lateral); // kameraya doğru (+) önce
        });
        return order.ToArray();
    }

    // İnce parçayı istasyonun direction yanına koyar: ilk aday tam yanı (ortasından geçen çizgide, kenarına yaslı; yarım
    // hücre adımla konabildiği için 2×2'de de ortaya hizalı), sonra BesideOrder: dışa doğru parçanın kalınlığı kadar
    // sıralar, yana doğru parçanın derinliği kadar (önce kameraya doğru). Kutunun ortası tam o noktaya (PlaceCentered).
    private bool TryPlaceBeside(GridPlaceable piece, Vector3 direction, Vector3 toCamera)
    {
        if (!(piece is FinePlaceable fine)) return false;
        Vector3 center = Center;
        float edge = 0f; // istasyonun kenarı (ortadan, bu yönde)
        GridFootprint footprint = GetData() != null ? GetData().GetFootprint() : null;
        if (footprint != null)
            foreach (Vector3Int cell in footprint.FilledCells())
                edge = Mathf.Max(edge, Vector3.Dot(transform.TransformPoint(cell) - center, direction) + 0.5f);

        // Yana kayma yönü: direction'a dik, kameraya doğru (iki tarafta da aynı yön)
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (Vector3.Dot(side, toCamera) < 0f) side = -side;
        Vector3 size = fine.WorldSize;
        float thickness = Mathf.Abs(direction.x) * size.x + Mathf.Abs(direction.z) * size.z;
        float depth = Mathf.Abs(side.x) * size.x + Mathf.Abs(side.z) * size.z;

        Vector3 floor = new Vector3(center.x, transform.position.y, center.z);
        foreach ((int row, int lateral) in BesideOrder)
        {
            Vector3 position = floor + direction * (edge + thickness * (row + 0.5f)) + side * (depth * lateral);
            if (fine.PlaceCentered(position)) return true;
        }
        return false;
    }

    // Bir hücreye (ortası, sonra hücre içinde çeyrek birim kaydırarak)
    private static bool TryPlaceAt(GridPlaceable piece, Vector3 cell)
    {
        Vector3[] offsets = { Vector3.zero, new Vector3(0.25f, 0, 0), new Vector3(-0.25f, 0, 0), new Vector3(0, 0, 0.25f), new Vector3(0, 0, -0.25f) };
        foreach (Vector3 offset in offsets)
            if (GridManager.Instance.CanPlaceablePlaceOn(piece, cell + offset) && GridManager.Instance.PlaceablePlaceOn(piece, cell + offset))
                return true;
        return false;
    }

    // İstasyondan yerine kavisle zıplar (yeri grid'de zaten tutuldu, sadece görüntü)
    private IEnumerator Jump(Transform piece, Vector3 from, Vector3 to, int index)
    {
        float delay = index * 0.06f;
        piece.position = from;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        float time = 0f;
        while (time < jumpDuration && piece != null)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / jumpDuration);
            piece.position = Vector3.Lerp(from, to, t) + Vector3.up * (jumpHeight * 4f * t * (1f - t));
            yield return null;
        }
        if (piece != null) piece.position = to;
    }

    // --- IInteractable: istasyonun kendisi taşınırken (üstündeki parçayla birlikte) ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();
}
