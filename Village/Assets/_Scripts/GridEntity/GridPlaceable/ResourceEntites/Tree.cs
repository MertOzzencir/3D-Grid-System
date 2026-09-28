using System.Collections.Generic;
using UnityEngine;

// Balta ile kesilen ağaç. Oklar (ArrowBase) kesilebilecek yerleri gösterir: balta yaklaşınca mouse yüksekliğine
// en yakın ok seçilir, her vuruş okun canını düşürür, can bitince ok kesilir ve odun çıkar.
// Dal oku (ArrowChild) sadece üst kısmı keser; gövde oku (ArrowRoot) ağacı tamamen kaldırır.
public class Tree : ResourceEntity, IToolTarget
{
    [SerializeField] private ArrowBase[] Arrows;
    [SerializeField] private GameObject mainVisual;
    [SerializeField] private GameObject cutedVisual;

    [Header("Oklar")]
    [Tooltip("Okların gövde merkezinden (kameraya göre yan tarafa) uzaklığı")]
    [SerializeField] private float arrowDistance = 1f;
    [Tooltip("Balta noktası gövde merkezine bundan uzaksa ok seçilmez")]
    [SerializeField] private float targetRadius = 2f;

    [Header("VFX")]
    [Tooltip("Balta vurunca vuruş noktasında oynar. Particle'lar yerel +Z yönüne saçılmalı: +Z ağaçtan dışarı (baltaya doğru) bakar.")]
    [SerializeField] private ParticleSystem chopEffect;
    [Tooltip("Efekt gövdenin merkezinden bu kadar dışarıda, yani gövdenin yüzeyinde çıkar")]
    [SerializeField] private float trunkRadius = 0.3f;
    [SerializeField] private HitShake hitShake = new HitShake();
    [Tooltip("Vurulan tarafın içeri göçmesi. Ağacın materyali 'Village/Tree Chop Lit' shader'ını kullanmalı.")]
    [SerializeField] private ChopDent chopDent = new ChopDent();

    private ArrowBase selectedArrow;
    private TreeHitFeedback hitFeedback;

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
        if (!(interactable is Axe)) return default;
        accept = true;

        SetArrowsVisible(true);
        PlaceArrows();

        if (!TryGetArrowPlane(out Plane plane))
            return transform.position;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out float enter))
            return transform.position;

        Vector3 point = ray.GetPoint(enter);
        if (Vector3.Distance(point, transform.position + TrunkCenterLocal) > targetRadius)
            return transform.position;

        Select(FindClosestArrow(point));
        return selectedArrow != null ? selectedArrow.GetTransform().position : transform.position;
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
    // örn. 2BR gövde + 1BR dal → dal duruyorsa 2BR, dal kesildiyse 1BR odun çıkar.
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

    // Odun, origin'den (kesilen parçanın yeri) ağacın yanındaki ilk boş hücreye zıplar
    private void SpawnWood(int length, Vector3 origin)
    {
        Wood prefab = WoodMerger.Instance.Catalog.GetWood(length);
        if (prefab == null)
        {
            Debug.LogWarning($"WoodCatalog'da {length} uzunluğunda odun yok", this);
            return;
        }

        Vector3 target = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool found);
        if (!found) return;

        Wood wood = Instantiate(prefab, origin, Quaternion.identity);
        wood.OnSpawned();
        GridManager.Instance.PlaceablePlaceOn(wood, target);
        wood.SpawnAnimation(target, origin);
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
