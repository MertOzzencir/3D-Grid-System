using System.Collections.Generic;
using UnityEngine;

// Balta ile kesilen ağaç. Oklar (ArrowBase) kesilebilecek yerleri gösterir: balta yaklaşınca mouse yüksekliğine
// en yakın ok seçilir, her vuruş okun canını düşürür, can bitince ok kesilir ve odun çıkar.
// Dal oku (ArrowChild) sadece üst kısmı keser; gövde oku (ArrowRoot) ağacı tamamen kaldırır.
// Yapışkan hedef: balta bir kez yapışınca mouse gövdeden çıksa da kopmaz; ani mouse hareketiyle (GridDragMotor)
// ya da mouse gövdenin ekseninden releaseDistance'tan fazla uzaklaşınca bırakılır.
// Fidan: kedi kakası ekilince (CatPoop) ağaç fidan olarak konur (StartAsSapling): bütün ayak izini hemen kaplar (oraya
// başka bir şey konamaz), görünüşü saplingScale'den growSeconds'ta tam boya büyür; büyürken baltayla kesilemez.
// Büyüme kayda girer (ISaveState).
public class Tree : ResourceEntity, IStickyToolTarget, ISaveState
{
    [SerializeField] private ArrowBase[] Arrows;
    [SerializeField] private GameObject mainVisual;
    [SerializeField] private GameObject cutedVisual;

    [Header("Oklar")]
    [Tooltip("Okların gövde merkezinden (kameraya göre yan tarafa) uzaklığı")]
    [SerializeField] private float arrowDistance = 1f;
    [Tooltip("Balta noktası gövde merkezine bundan uzaksa yeni ok seçilmez (son seçili ok korunur)")]
    [SerializeField] private float targetRadius = 2f;
    [Tooltip("Balta yapıştıktan sonra mouse, okların düzleminde gövde ekseninden bu kadar (birim) uzaklaşınca bırakılır")]
    [SerializeField] private float releaseDistance = 4f;

    [Header("VFX")]
    [Tooltip("Balta vurunca vuruş noktasında oynar. Particle'lar yerel +Z yönüne saçılmalı: +Z ağaçtan dışarı (baltaya doğru) bakar.")]
    [SerializeField] private ParticleSystem chopEffect;
    [Tooltip("Efekt gövdenin merkezinden bu kadar dışarıda, yani gövdenin yüzeyinde çıkar")]
    [SerializeField] private float trunkRadius = 0.3f;
    [SerializeField] private HitShake hitShake = new HitShake();
    [Tooltip("Vurulan tarafın içeri göçmesi. Ağacın materyali 'Village/Tree Chop Lit' shader'ını kullanmalı.")]
    [SerializeField] private ChopDent chopDent = new ChopDent();

    [Header("Fidan (kakadan ekilince)")]
    [Tooltip("Fidandan tam ağaca büyüme süresi (saniye)")]
    [SerializeField] private float growSeconds = 60f;
    [Tooltip("Fidanın boyu (tam ağaca oranla)")]
    [SerializeField, Range(0.05f, 1f)] private float saplingScale = 0.15f;

    private ArrowBase selectedArrow;
    private TreeHitFeedback hitFeedback;
    private float growth = 1f; // 0 fidan, 1 tam ağaç
    private Vector3 mainVisualScale = Vector3.one;
    private bool scaleRead;

    public bool IsGrowing => growth < 1f;

    [System.Serializable]
    private class SaveState
    {
        public float growth = 1f;
    }

    private TreeHitFeedback HitFeedback => hitFeedback ??= new TreeHitFeedback(this, hitShake, chopDent);

    // Gövde, ayak izinin ortasında durur (pivot'a göre, yatay)
    private Vector3 TrunkCenterLocal
    {
        get
        {
            Vector3 center = (PlacedFootprint ?? GetFootprint()).Center;
            return new Vector3(center.x, 0f, center.z);
        }
    }

    // Şu an görünen ağaç görseli (sallanan)
    private Transform CurrentVisual
    {
        get
        {
            GameObject visual = mainVisual != null && mainVisual.activeSelf ? mainVisual : cutedVisual;
            return visual != null ? visual.transform : null;
        }
    }

    // --- IToolTarget ---

    public Vector3 GetToolTargetPosition(IInteractable interactable, out bool accept)
    {
        accept = false;
        if (!(interactable is Axe) || IsGrowing) return default; // fidan kesilmez
        accept = true;

        SetArrowsVisible(true);
        PlaceArrows();

        if (!TryGetArrowPlane(out Plane plane))
            return transform.position;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out float enter))
            return transform.position;

        Vector3 point = ray.GetPoint(enter);
        // Seçim yarıçapının dışında: yeni ok seçilmez, seçili ok varsa balta onda kalır (kaymasın)
        if (Vector3.Distance(point, transform.position + TrunkCenterLocal) <= targetRadius)
            Select(FindClosestArrow(point));
        return selectedArrow != null ? selectedArrow.GetTransform().position : transform.position;
    }

    // Kilit: mouse okların düzleminde gövdenin dikey ekseninden releaseDistance'tan fazla uzaklaşmadıkça devam
    public bool IsStillTargeted(IInteractable interactable, Ray mouseRay)
    {
        if (!(interactable is Axe)) return false;
        if (!TryGetArrowPlane(out Plane plane) || !plane.Raycast(mouseRay, out float enter)) return false;

        Vector3 point = mouseRay.GetPoint(enter);
        Vector3 trunk = transform.position + TrunkCenterLocal;
        float height = (PlacedFootprint ?? GetFootprint()).Size.y;
        Vector3 closestOnAxis = new Vector3(trunk.x, Mathf.Clamp(point.y, trunk.y, trunk.y + height), trunk.z);
        return Vector3.Distance(point, closestOnAxis) <= releaseDistance;
    }

    public Quaternion GetToolTargetRotation()
        => selectedArrow != null ? selectedArrow.GetTransform().rotation : transform.rotation;

    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Axe)) return false;
        if (selectedArrow == null || selectedArrow.isUsed) return false;

        ArrowBase hitArrow = selectedArrow;

        // Her vuruşta: talaş efekti + göçük + sarsılma
        GetHitFrame(hitArrow.GetTransform().position, out Vector3 trunkCenter, out Vector3 outward);
        HitFeedback.Play(trunkCenter + outward * trunkRadius, outward, chopEffect, CurrentVisual, CollectVisualRenderers());

        // Okun canı bitmediyse sadece vuruş sayılır, ok seçili kalır
        if (!hitArrow.TakeHit()) return true;

        selectedArrow = null;
        Cut(hitArrow, -outward); // kesilen parça baltadan uzağa devrilir
        return true;
    }

    public void OnToolTargetExit()
    {
        SetArrowsVisible(false);
        Select(null);
    }

    // --- Kesme ve odun ---

    // Oku keser: parçanın animasyonu oynar, bitince odun çıkar. Gövde kesildiyse ağaç grid'den kalkar.
    private void Cut(ArrowBase arrow, Vector3 fallDirection)
    {
        int woodLength = WoodLengthFor(arrow);

        arrow.FallDirection = fallDirection;
        arrow.transform.parent = null; // ağaç kalksa da okun animasyonu bitebilsin
        arrow.OnUsed(cutArrow => OnCutFinished(cutArrow, woodLength));

        if (arrow is ArrowRoot)
            GridManager.Instance.PlaceableRemoveOn(this, true);
    }

    // Dal oku kendi uzunluğunu verir. Gövde oku, önceden kesilmiş dalların uzunluğu düşülmüş gövdeyi verir:
    // örn. 2 boy gövde + 1 boy dal → dal duruyorsa iple bağlı iki 1×1, dal kesildiyse tek 1×1 çıkar.
    private int WoodLengthFor(ArrowBase arrow)
    {
        if (!(arrow is ArrowRoot)) return arrow.WoodLength;

        int length = arrow.WoodLength;
        foreach (ArrowBase other in Arrows)
            if (other != null && other != arrow && other.isUsed)
                length -= other.WoodLength;
        return length;
    }

    private void OnCutFinished(ArrowBase arrow, int woodLength)
    {
        SpawnWood(woodLength, arrow.SpawnOrigin);
        if (arrow != null) arrow.gameObject.SetActive(false);
    }

    // Odun origin'den (kesilen parçanın yeri) ağacın yanındaki, sığdığı ilk boş yere zıplar. Bütün odunlar 1×1: uzunluk
    // 1'den büyükse (örn. gövde dipten kesilince 2) iple bağlı 1×1'lerden dik yığın çıkar (WoodMerger.SpawnWoodStack).
    private void SpawnWood(int length, Vector3 origin)
    {
        GridPlaceable wood = WoodMerger.Instance.SpawnWoodStack(length);
        if (!TryFindSpawnCell(wood, out Vector3Int target))
        {
            Destroy(wood.gameObject);
            return;
        }

        GridManager.Instance.PlaceablePlaceOn(wood, target);
        if (wood is Wood single) single.SpawnAnimation(target, origin);
        else WoodMerger.Instance.PlayHop(wood.transform, origin, wood.transform.rotation);
    }

    // Ağacın ayak izinin çevresindeki (bir hücre dışı) zemin katında, odunun bütün boyunun sığdığı ilk yer
    private bool TryFindSpawnCell(GridPlaceable wood, out Vector3Int cell)
    {
        GridFootprint footprint = PlacedFootprint ?? GetFootprint();
        for (int z = footprint.Min.z - 1; z <= footprint.Max.z + 1; z++)
            for (int x = footprint.Min.x - 1; x <= footprint.Max.x + 1; x++)
            {
                cell = OriginWorldPosition + new Vector3Int(x, 0, z);
                if (GridManager.Instance.CanPlaceablePlaceOn(wood, cell)) return true;
            }
        cell = default;
        return false;
    }

    // --- Oklar ---

    // Oklar gövdenin kameraya göre sol yanında durur ve kamerayla birlikte 90° adımlarla döner.
    // 3x3 ağaçta eski sabit tablonun aynısını verir: 0°→(0,0,1), -90°→(1,0,0), -180°→(2,0,1), 90°→(1,0,2)
    private void GetArrowFrame(out Vector3 localPosition, out Quaternion localRotation)
    {
        localRotation = Quaternion.Euler(0f, (float)CameraController.CurrentFacing, 0f);
        localPosition = TrunkCenterLocal + localRotation * (Vector3.left * arrowDistance);
    }

    // Kesilmemiş okları gövdenin yanına dizer; her ok kendi yüksekliğini korur
    private void PlaceArrows()
    {
        GetArrowFrame(out Vector3 position, out Quaternion rotation);
        foreach (ArrowBase arrow in Arrows)
        {
            if (arrow == null || arrow.isUsed) continue;
            arrow.transform.localRotation = rotation;
            if (arrow == selectedArrow) continue; // seçili ok Selected() ile biraz dışarı kaydırıldı
            arrow.transform.localPosition = position + Vector3.up * arrow.transform.localPosition.y;
        }
    }

    private void Select(ArrowBase arrow)
    {
        if (arrow == selectedArrow) return;

        GetArrowFrame(out Vector3 position, out _);
        if (selectedArrow != null) selectedArrow.Selected(position, false);
        selectedArrow = arrow;
        if (selectedArrow != null) selectedArrow.Selected(position, true);
    }

    private void SetArrowsVisible(bool visible)
    {
        foreach (ArrowBase arrow in Arrows)
        {
            if (arrow == null || arrow.isUsed) continue;
            arrow.gameObject.SetActive(visible);
        }
    }

    // Mouse ışınının kestiği, okların önündeki dikey düzlem
    private bool TryGetArrowPlane(out Plane plane)
    {
        foreach (ArrowBase arrow in Arrows)
        {
            if (arrow == null || arrow.isUsed) continue;
            plane = new Plane(-arrow.transform.forward, arrow.transform.position);
            return true;
        }
        plane = default;
        return false;
    }

    // Mouse noktasının yüksekliğine en yakın kesilmemiş ok
    private ArrowBase FindClosestArrow(Vector3 point)
    {
        ArrowBase closest = null;
        float closestDistance = float.MaxValue;

        foreach (ArrowBase arrow in Arrows)
        {
            if (arrow == null || arrow.isUsed) continue;

            float distance = Mathf.Abs(point.y - arrow.transform.position.y);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = arrow;
            }
        }
        return closest;
    }

    // --- Yardımcılar ---

    // Vuruş yüksekliğindeki gövde merkezi ve gövdeden dışarı (baltaya doğru) yatay yön
    private void GetHitFrame(Vector3 hitPoint, out Vector3 trunkCenter, out Vector3 outward)
    {
        trunkCenter = transform.position + TrunkCenterLocal;
        trunkCenter.y = hitPoint.y;

        outward = hitPoint - trunkCenter;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
            outward = Vector3.ProjectOnPlane(-Camera.main.transform.forward, Vector3.up); // tam merkezdeyse kameraya doğru
        outward.Normalize();
    }

    // Kesilmemiş ve kesilmiş görselin şu anki renderer'ları (oklar hariç).
    // Her vuruşta yeniden toplanır: kesilen dallar görselden koparılıp siliniyor, saklanan liste bayatlardı.
    private Renderer[] CollectVisualRenderers()
    {
        var renderers = new List<Renderer>();
        if (mainVisual != null) renderers.AddRange(mainVisual.GetComponentsInChildren<Renderer>(true));
        if (cutedVisual != null) renderers.AddRange(cutedVisual.GetComponentsInChildren<Renderer>(true));
        return renderers.ToArray();
    }

    // --- Yaşam döngüsü ---

    // --- Fidan ---

    // Ekilince: fidan boyunda başlar, Update'te büyür
    public void StartAsSapling()
    {
        growth = 0f;
        ApplyGrowth();
    }

    private void Update()
    {
        if (!IsGrowing) return;
        growth = Mathf.Min(1f, growth + Time.deltaTime / Mathf.Max(growSeconds, 0.01f));
        ApplyGrowth();
    }

    // Ana görünüş fidan boyundan tam boya (yumuşak hızlanıp yavaşlayarak)
    private void ApplyGrowth()
    {
        if (mainVisual == null) return;
        if (!scaleRead)
        {
            mainVisualScale = mainVisual.transform.localScale;
            scaleRead = true;
        }
        float t = Mathf.SmoothStep(0f, 1f, growth);
        mainVisual.transform.localScale = mainVisualScale * Mathf.Lerp(saplingScale, 1f, t);
    }

    public string CaptureState() => JsonUtility.ToJson(new SaveState { growth = growth });

    public void RestoreState(string state)
    {
        SaveState saved = JsonUtility.FromJson<SaveState>(state);
        if (saved == null) return;
        growth = Mathf.Clamp01(saved.growth);
        if (IsGrowing) ApplyGrowth();
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        PlaceArrows(); // eskiden burada ortak (static) ok pozisyonu sıfırlanıyordu; artık her ağaç kendi hesaplar
    }

    // Güvenlik ağı: ağaç başka bir yoldan kapanırsa grid'de kayıtlı kalmasın
    void OnDisable()
    {
        if (GridManager.Instance != null)
            GridManager.Instance.PlaceableRemoveOn(this);
    }

    // Kesilen oklar ağaçtan koparılıyor (parent = null), bu yüzden ağaçla birlikte otomatik silinmiyorlar.
    // Ağaç yok olurken tüm oklarını da temizler.
    private void OnDestroy()
    {
        if (!gameObject.scene.isLoaded) return; // sahne kapanıyorsa Unity zaten her şeyi siliyor

        foreach (ArrowBase arrow in Arrows)
            if (arrow != null) arrow.DestroyWithParts();
    }
}
