using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Kesme kütüğü: grid'de taşınan placeable. Üstüne tarifi olan bir parça (1BR odun, yarım odun...) sağ tıkla
// sürüklenip bırakılır (ya da sürüklerken sol tık); balta sağ tıkla alınıp kütüğe getirilir, sol tıkla vurulur
// (ağaç kesmedeki gibi). hitsToSplit vuruşta parça bölünür: tarifteki çıktılar (örn. 2 yarım odun) kütüğün yanındaki
// boş yerlere zıplar.
// Üstteki parça kütüğün çocuğu olur, collider'ları kapanır; yerine kütüğün üst kısmında bir "tepe" collider'ı
// (ChoppingBlockTop) açılır: balta oraya hedeflenir, sağ tıkla tutulunca parça kütükten alınır (yönlendirme).
// Tarifler Inspector'da: girdi SO → çıktı SO × adet. Çıktısı boş tarif: parça konur ama bölünmez.
// Üstteki parça kaydedilmez (kütük kaydedilir).
// Görünüm: Model atanırsa o (pivot hücre ortasında), yoksa kodla geçici silindir kütük (prototip). Kökte collider yoksa
// görünümü saran kutu eklenir (tutulabilsin).
public class ChoppingBlock : GridPlaceable, IInteractable, IStickyToolTarget, IHoldTarget
{
    [Serializable]
    public class Recipe
    {
        [Tooltip("Kütüğe konan parçanın SO'su (örn. Wood_1BR, HalfLog)")]
        public GridPlaceableSO input;
        [Tooltip("Bölününce çıkan parçanın SO'su (örn. HalfLog, QuarterLog). Boşsa parça konur ama bölünmez.")]
        public GridPlaceableSO output;
        [Tooltip("Kaç parça çıkar")]
        public int count = 2;
    }

    [SerializeField] private float followSpeed = 10f;
    [SerializeField] private List<Recipe> recipes = new List<Recipe>();

    [Header("Görünüm")]
    [Tooltip("Kütüğün modeli (prefab ya da FBX; pivot hücrenin ortasında). Boşsa geçici silindir kütük çizilir.")]
    [SerializeField] private GameObject model;
    [Tooltip("Geçici kütüğün rengi (model yokken)")]
    [SerializeField] private Color placeholderColor = new Color(0.55f, 0.38f, 0.24f);

    [Header("Yerleşim")]
    [Tooltip("Kütüğün üst yüzeyinin pivottan yüksekliği (birim). Parça bunun üstüne oturur (parçanın tabanı burada).")]
    [SerializeField] private float topHeight = 0f;

    [Header("Kesme")]
    [Tooltip("Kaç vuruşta bölünür")]
    [SerializeField, Min(1)] private int hitsToSplit = 1;
    [Tooltip("Baltanın parçanın ortasına göre yeri (birim, kameraya göre: X sağ, Y yukarı, Z kameradan uzağa / geriye)")]
    [SerializeField] private Vector3 axeOffset = new Vector3(0f, 0.8f, 0.3f);
    [Tooltip("Baltanın kütüğün üstündeki dönüşü (derece, kameraya göre: kamera döndükçe balta da döner). Play'deyken " +
             "değiştirince anında görünür; istenen duruş bulununca prefab'a yazılır.")]
    [SerializeField] private Vector3 axeRotation = new Vector3(45f, 0f, 0f);
    [Tooltip("Vuruşta oynar (isteğe bağlı). Particle'lar yerel +Z yönüne saçılmalı.")]
    [SerializeField] private ParticleSystem chopEffect;
    [SerializeField] private HitShake hitShake = new HitShake();
    [Tooltip("Çıkan parçaların kütükten yerlerine zıplama süresi (saniye) ve yüksekliği")]
    [SerializeField] private float jumpDuration = 0.35f;
    [SerializeField] private float jumpHeight = 0.6f;

    private GridDragMotor drag;
    private GridPlaceable held;
    private Recipe heldRecipe;
    private int hits;
    private ChoppingBlockTop top;
    private Transform visual; // kütüğün görünümü (model ya da geçici silindir)
    private Quaternion visualRest;
    private readonly List<Collider> disabledColliders = new List<Collider>();

    public GridPlaceable Held => held;

    // Parçanın tabanı üst yüzeyde: pivotu hücre / kutu ortasında olan parçalar için yarım birim yukarı
    private Vector3 HoldPoint => transform.position + Vector3.up * (topHeight + 0.5f);

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        BuildVisual();
        var topObject = new GameObject("Top");
        topObject.transform.SetParent(transform, false);
        topObject.transform.localPosition = Vector3.up * (topHeight + 0.5f);
        var box = topObject.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        top = topObject.AddComponent<ChoppingBlockTop>();
        top.Init(this, box);
        box.enabled = false;
    }

    // Model ya da geçici silindir (hücrenin alt yarısı, üst yüzeyi topHeight'ta); kökte collider yoksa görünümü saran kutu
    private void BuildVisual()
    {
        GameObject visual;
        if (model != null)
        {
            visual = Instantiate(model, transform, false);
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(transform, false);
            float height = Mathf.Max(0.1f, topHeight + 0.5f); // hücrenin tabanından üst yüzeye
            visual.transform.localPosition = Vector3.up * (-0.5f + height * 0.5f);
            visual.transform.localScale = new Vector3(0.8f, height * 0.5f, 0.8f); // silindir 2 birim boyunda
            var renderer = visual.GetComponent<Renderer>();
            renderer.material.color = placeholderColor;
        }
        visual.name = "Model";
        this.visual = visual.transform;
        visualRest = visual.transform.localRotation;

        if (GetComponent<Collider>() != null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
        {
            Bounds local = new Bounds(transform.InverseTransformPoint(r.bounds.center), r.bounds.size);
            if (any) bounds.Encapsulate(local);
            else bounds = local;
            any = true;
        }
        box.center = any ? bounds.center : Vector3.down * 0.25f;
        box.size = any ? bounds.size : new Vector3(0.9f, 0.5f, 0.9f);
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

    public bool CanHold(GridPlaceable item) => held == null && item != null && item != this && RecipeFor(item) != null;

    public bool TryHold(GridPlaceable item)
    {
        if (!CanHold(item)) return false;
        held = item;
        heldRecipe = RecipeFor(item);
        hits = 0;
        item.transform.SetParent(transform, true);
        item.transform.SetPositionAndRotation(HoldPoint, transform.rotation);
        disabledColliders.Clear();
        foreach (Collider c in item.GetComponentsInChildren<Collider>())
            if (c.enabled)
            {
                c.enabled = false;
                disabledColliders.Add(c);
            }
        top.SetActive(true);
        return true;
    }

    // Parça kütükten alındı (sağ tıkla tutuldu): bırakılır, collider'ları açılır
    public GridPlaceable Release()
    {
        GridPlaceable item = held;
        if (item == null) return null;
        held = null;
        heldRecipe = null;
        item.transform.SetParent(null, true);
        foreach (Collider c in disabledColliders)
            if (c != null) c.enabled = true;
        disabledColliders.Clear();
        top.SetActive(false);
        return item;
    }

    // --- IToolTarget: parça getirilince (koyma) ya da balta getirilince (kesme) ---

    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = false;
        if (interacted is Axe)
        {
            if (held == null || heldRecipe == null || heldRecipe.output == null) return default;
            accept = true;
            return HoldPoint + AxeFrame() * axeOffset;
        }
        if (interacted is GridPlaceable item && CanHold(item))
        {
            accept = true;
            return HoldPoint;
        }
        return default;
    }

    public Quaternion GetToolTargetRotation() => held != null ? AxeFrame() * Quaternion.Euler(axeRotation) : transform.rotation;

    // Ağaçtaki oklar gibi kamerayla 90° adımlarla döner (balta kameraya göre aynı duruşta kalır)
    private static Quaternion AxeFrame() => Quaternion.Euler(0f, (float)CameraController.CurrentFacing, 0f);

    // Kameranın sağı, grid eksenine yuvarlanmış (parçalar sağa / sola gider)
    private static Vector3 CameraRight()
    {
        Vector3 right = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        return Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? new Vector3(Mathf.Sign(right.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(right.z));
    }

    public bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
    {
        // Balta: mouse kütüğün dikey ekseninden 3 birimden fazla uzaklaşmadıkça bırakılmaz
        if (!(interacted is Axe) || held == null) return false;
        var plane = new Plane(-Camera.main.transform.forward, HoldPoint);
        if (!plane.Raycast(mouseRay, out float enter)) return false;
        Vector3 point = mouseRay.GetPoint(enter);
        return Vector3.Distance(new Vector3(point.x, HoldPoint.y, point.z), HoldPoint) <= 3f;
    }

    public void OnToolTargetExit() { }

    public bool OnToolUsed(IInteractable tool)
    {
        if (tool is Axe) return Chop();
        if (tool is GridPlaceable item) return TryHold(item);
        return false;
    }

    private bool Chop()
    {
        if (held == null || heldRecipe == null || heldRecipe.output == null) return false;

        Vector3 outward = CameraRight(); // sarsılma yana
        if (chopEffect != null)
        {
            chopEffect.transform.SetPositionAndRotation(HoldPoint + Vector3.up * 0.5f, Quaternion.LookRotation(Vector3.up));
            chopEffect.Play();
        }
        // Bölmeyen vuruşta parça, bölen vuruşta (parça siliniyor) kütüğün kendisi sarsılır
        if (++hits < hitsToSplit)
        {
            StartCoroutine(hitShake.Play(held.transform, held.transform.localRotation, -outward));
            return true;
        }
        if (visual != null) StartCoroutine(hitShake.Play(visual, visualRest, -outward));
        Split();
        return true;
    }

    // Parça bölünür: ilk iki çıktı kameraya göre tam sola ve sağa (yanındaki hücreye, ince yüzü kütüğe dönük), diğerleri
    // ya da o hücreler doluysa yakındaki boş yerlere zıplar; yer yoksa ilki kütükte kalır
    private void Split()
    {
        Recipe recipe = heldRecipe;
        GridPlaceable item = Release();
        Destroy(item.gameObject);

        Vector3 start = HoldPoint;
        for (int i = 0; i < recipe.count; i++)
        {
            if (!(recipe.output.Prefab is GridPlaceable prefab)) break;
            GridPlaceable piece = Instantiate(prefab, start, transform.rotation);
            piece.AssignData(recipe.output);
            Vector3 right = CameraRight();
            // Yarım odun Deg0'da X'te ince: sağ / sol X eksenindeyse Deg0, Z'deyse Deg90
            piece.Rotation = Mathf.Abs(right.x) > 0.5f ? GridMaskRotator.Rotation.Deg0 : GridMaskRotator.Rotation.Deg90;
            bool placed = i < 2 && TryPlaceAt(piece, transform.position + (i == 0 ? -right : right));
            if (placed || TryPlaceNearby(piece))
            {
                StartCoroutine(Jump(piece.transform, start, piece.transform.position, i));
                continue;
            }
            if (held == null && TryHold(piece)) continue;
            Debug.LogWarning($"{name}: {recipe.output.name} için yer yok", this);
            Destroy(piece.gameObject);
        }
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

    // Kütüğün yanındaki hücrelerde (önce kameraya bakan taraf) ilk uygun yer
    private bool TryPlaceNearby(GridPlaceable piece)
    {
        Vector3 center = transform.position;
        Vector3[] directions =
        {
            Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
            new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1)
        };
        Vector3 toCamera = Camera.main != null ? -Camera.main.transform.forward : Vector3.back;
        Array.Sort(directions, (a, b) => Vector3.Dot(b, toCamera).CompareTo(Vector3.Dot(a, toCamera)));
        Vector3[] offsets = { Vector3.zero, new Vector3(0.25f, 0, 0), new Vector3(-0.25f, 0, 0), new Vector3(0, 0, 0.25f), new Vector3(0, 0, -0.25f) };
        foreach (Vector3 direction in directions)
            foreach (Vector3 offset in offsets)
            {
                Vector3 position = center + direction + offset;
                if (!GridManager.Instance.CanPlaceablePlaceOn(piece, position)) continue;
                if (GridManager.Instance.PlaceablePlaceOn(piece, position)) return true;
            }
        return false;
    }

    // Kütükten yerine kavisle zıplar (yeri grid'de zaten tutuldu, sadece görüntü)
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

    // --- IInteractable: kütüğün kendisi taşınırken (üstündeki parçayla birlikte) ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();
}
