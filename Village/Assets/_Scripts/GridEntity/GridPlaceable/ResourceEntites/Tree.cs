using UnityEngine;

public class Tree : ResourceEntity, IToolTarget
{
    [SerializeField] private ArrowBase[] Arrows;
    [SerializeField] private GameObject mainVisual;
    [SerializeField] private GameObject cutedVisual;

    [Header("VFX")]
    [Tooltip("Balta vurunca vuruş noktasında oynar. Particle'lar yerel +Z yönüne saçılmalı: +Z ağaçtan dışarı (baltaya doğru) bakar.")]
    [SerializeField] private ParticleSystem chopEffect;
    [Tooltip("Efekt gövdenin merkezinden bu kadar dışarıda, yani gövdenin yüzeyinde çıkar")]
    [SerializeField] private float trunkRadius = 0.3f;
    [SerializeField] private HitShake hitShake = new HitShake();
    [Tooltip("Vurulan tarafın içeri göçmesi. Ağacın materyali 'Village/Tree Chop Lit' shader'ını kullanmalı.")]
    [SerializeField] private ChopDent chopDent = new ChopDent();

    private Coroutine shakeRoutine;
    private Transform shakingVisual;
    private Quaternion shakeRestRotation;

    private Coroutine dentRoutine;

    public static Vector3 ArrowLocalPosition;
    public static Vector3 ArrowLocalRotation;


    private Vector3 lastRaycastPoint;
    private ArrowBase selectedArrow;

    public Vector3 GetToolTargetPosition(IInteractable interactable, out bool accept)
    {
        accept = false;
        if (!(interactable is Axe)) return default;
        accept = true;
        SetArrows(true);
        SetArrowLocalPositions();
        if (!TryGetArrowPlane(out Plane plane))
            return transform.position;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out float enter))
            return transform.position;
        Vector3 point = ray.GetPoint(enter);

        float maxRadius = 2f;
        Vector3 footprintCenter = PlacedFootprint.Center;
        Vector3 center = transform.position + new Vector3(footprintCenter.x, 0, footprintCenter.z);
        if (Vector3.Distance(point, center) > maxRadius)
            return transform.position;

        lastRaycastPoint = point;
        ArrowBase newSelected = FindClosestArrow(lastRaycastPoint);

        if (newSelected != selectedArrow)
        {
            if (selectedArrow != null) selectedArrow.Selected(ArrowLocalPosition, false);
            selectedArrow = newSelected;
            if (selectedArrow != null) selectedArrow.Selected(ArrowLocalPosition, true);
        }

        return selectedArrow != null ? selectedArrow.GetTransform().position : transform.position;
    }
    public Quaternion GetToolTargetRotation()
    {
        return selectedArrow != null ? selectedArrow.GetTransform().rotation : transform.rotation;
    }

    public bool OnToolUsed(IInteractable tool)
    {
        if (!(tool is Axe)) return false;
        if (selectedArrow == null || selectedArrow.isUsed) return false;

        ArrowBase hitArrow = selectedArrow;

        // Her vuruşta: talaş efekti + göçük + sarsılma
        GetHitFrame(hitArrow.GetTransform().position, out Vector3 trunkCenter, out Vector3 outward);
        PlayChopEffect(trunkCenter, outward);
        StopShake(); // göçük noktası, ağacın düz (sallanmayan) haline göre hesaplansın
        Dent(trunkCenter + outward * trunkRadius, -outward);
        Shake(-outward); // baltadan uzağa doğru yatar

        // Okun canı bitmediyse sadece vuruş sayılır, ok seçili kalır
        if (!hitArrow.TakeHit()) return true;

        selectedArrow = null;
        hitArrow.FallDirection = -outward; // kesilen parça baltadan uzağa devrilir
        hitArrow.transform.parent = null;
        SpawnLogic(hitArrow, out bool success);
        return success;
    }

    // Vuruş yüksekliğindeki gövde merkezi ve gövdeden dışarı (baltaya doğru) yatay yön
    private void GetHitFrame(Vector3 hitPoint, out Vector3 trunkCenter, out Vector3 outward)
    {
        // Gövde footprint'in ortasında duruyor
        trunkCenter = PlacedFootprint != null ? transform.position + PlacedFootprint.Center : transform.position;
        trunkCenter.y = hitPoint.y;

        outward = hitPoint - trunkCenter;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
            outward = Vector3.ProjectOnPlane(-Camera.main.transform.forward, Vector3.up); // tam merkezdeyse kameraya doğru
        outward.Normalize();
    }

    // Gövdenin yüzeyinde; +Z gövdeden dışarı bakar
    private void PlayChopEffect(Vector3 trunkCenter, Vector3 outward)
    {
        if (chopEffect == null) return;
        VfxPool.Play(chopEffect, trunkCenter + outward * trunkRadius, Quaternion.LookRotation(outward, Vector3.up));
    }

    // Sadece görsel sallanır; ağacın root'u grid pozisyonu olduğu için ona dokunulmaz
    private void Shake(Vector3 pushDirection)
    {
        GameObject visual = mainVisual != null && mainVisual.activeSelf ? mainVisual : cutedVisual;
        if (visual == null) return;

        // Önceki sallanma bitmeden yeni vuruş gelirse: önce düz haline döndür, sonra yeniden başlat
        StopShake();

        shakingVisual = visual.transform;
        shakeRestRotation = shakingVisual.localRotation;
        shakeRoutine = StartCoroutine(ShakeRoutine(pushDirection));
    }

    private void StopShake()
    {
        if (shakeRoutine == null) return;
        StopCoroutine(shakeRoutine);
        shakeRoutine = null;
        shakingVisual.localRotation = shakeRestRotation;
    }

    private System.Collections.IEnumerator ShakeRoutine(Vector3 pushDirection)
    {
        yield return hitShake.Play(shakingVisual, shakeRestRotation, pushDirection);
        shakeRoutine = null;
    }

    // Yeni vuruş öncekinin yerine geçer; göçükler üst üste binmez
    private void Dent(Vector3 surfacePoint, Vector3 inward)
    {
        if (dentRoutine != null) StopCoroutine(dentRoutine);
        dentRoutine = StartCoroutine(chopDent.Play(CollectVisualRenderers(), surfacePoint, inward));
    }

    // Kesilmemiş ve kesilmiş görselin şu anki renderer'ları (oklar hariç).
    // Her vuruşta yeniden toplanır: kesilen dallar görselden koparılıp siliniyor, saklanan liste bayatlardı.
    private Renderer[] CollectVisualRenderers()
    {
        var renderers = new System.Collections.Generic.List<Renderer>();
        if (mainVisual != null) renderers.AddRange(mainVisual.GetComponentsInChildren<Renderer>(true));
        if (cutedVisual != null) renderers.AddRange(cutedVisual.GetComponentsInChildren<Renderer>(true));
        return renderers.ToArray();
    }

    public void OnToolTargetExit()
    {
        SetArrows(false);
        if (selectedArrow != null)
        {
            selectedArrow.Selected(ArrowLocalPosition, false);
            selectedArrow = null;
        }
    }

    private bool TryGetArrowPlane(out Plane plane)
    {
        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;
            plane = new Plane(-a.transform.forward, a.transform.position);
            return true;
        }
        plane = default;
        return false;
    }

    private ArrowBase FindClosestArrow(Vector3 point)
    {
        ArrowBase closest = null;
        float closestDist = float.MaxValue;

        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;

            float dist = Mathf.Abs(point.y - a.transform.position.y);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = a;
            }
        }

        return closest;
    }
    public void SpawnLogic(ArrowBase arrow, out bool success)
    {
        success = false;
        switch (arrow.GetLength())
        {
            case Lenghts.BR_1:
                success = true;
                arrow.OnUsed(OnSpawnLogicFinished);
                break;

            case Lenghts.BR_2:
                arrow.OnUsed(OnSpawnLogicFinished);
                gameObject.SetActive(false);
                success = true;
                break;
        }
    }

    public void OnSpawnLogicFinished(ArrowBase arrow)
    {
        switch (arrow.GetLength())
        {
            case Lenghts.BR_1:
                Vector3 spawnPosition = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool positionSuccess);
                if (positionSuccess)
                {
                    // Odun, devrilen parçanın düştüğü yerden çıkar
                    Vector3 spawnOrigin = arrow.SpawnOrigin;
                    SourceBase wood = Instantiate(SourceBaseStaticManager.GetItemByIndex(0).Prefab, spawnOrigin, Quaternion.identity);
                    wood.OnSpawned();
                    GridManager.Instance.PlaceablePlaceOn(wood, spawnPosition);
                    wood.SpawnAnimation(spawnPosition, spawnOrigin);
                }
                break;
            case Lenghts.BR_2:
                bool anyUsed = false;
                foreach (var a in Arrows)
                {
                    if (a == null || a == arrow) continue;
                    if (a.isUsed)
                        anyUsed = a.isUsed;
                }
                if (anyUsed)
                {
                    Vector3 spawnPosition2 = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool positionSuccess2);
                    if (positionSuccess2)
                    {
                        ArrowRoot tempArrowChild = arrow as ArrowRoot;
                        Transform spawnedPosition = tempArrowChild.VisualPart();
                        SourceBase wood = Instantiate(SourceBaseStaticManager.GetItemByIndex(0).Prefab, spawnedPosition.transform.position, Quaternion.identity);
                        wood.OnSpawned();
                        GridManager.Instance.PlaceablePlaceOn(wood, spawnPosition2);
                        wood.SpawnAnimation(spawnPosition2, spawnedPosition.position);
                    }
                }
                else
                {
                    Vector3 spawnPosition3 = GridManager.Instance.GetEmptyGridFromEntityPosition(this, out bool positionSuccess2);
                    if (positionSuccess2)
                    {
                        ArrowRoot tempArrowChild = arrow as ArrowRoot;
                        Transform spawnedPosition = tempArrowChild.VisualPart();
                        SourceBase wood = Instantiate(SourceBaseStaticManager.GetItemByIndex(1).Prefab, spawnedPosition.transform.position, Quaternion.identity);
                        wood.OnSpawned();
                        GridManager.Instance.PlaceablePlaceOn(wood, spawnPosition3);
                        wood.SpawnAnimation(spawnPosition3, spawnedPosition.position);
                    }
                }
                break;
        }
        arrow.gameObject.SetActive(false);
    }

    private void SetArrows(bool active)
    {
        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;
            a.gameObject.SetActive(active);
        }
    }

    private void SetArrowLocalPositions()
    {
        foreach (var a in Arrows)
        {
            if (a == null || a.isUsed) continue;
            if (a == selectedArrow) continue;
            a.transform.localPosition = ArrowLocalPosition + Vector3.up * a.transform.localPosition.y;
        }
    }
    private void SetArrowLocalRotations()
    {
        foreach (var a in Arrows)
        {
            if (a == null) continue;
            a.transform.localEulerAngles = ArrowLocalRotation;
        }
    }

    public static void ArrowStaticTransforms(float xRotationFloat)
    {
        Vector3 localPosition;
        Vector3 localRotation;

        switch (xRotationFloat)
        {
            case 0f: localPosition = new Vector3(0, 0, 1); localRotation = Vector3.zero; break;
            case -90f: localPosition = new Vector3(1, 0, 0); localRotation = new Vector3(0, -90, 0); break;
            case -180f: localPosition = new Vector3(2, 0, 1); localRotation = new Vector3(0, -180, 0); break;
            case 90f: localPosition = new Vector3(1, 0, 2); localRotation = new Vector3(0, 90, 0); break;
            default: localPosition = new Vector3(0, 0, 1); localRotation = Vector3.zero; break;
        }

        ArrowLocalPosition = localPosition;
        ArrowLocalRotation = localRotation;
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        ArrowLocalPosition = new Vector3(0, 0, 1);
        ArrowLocalRotation = Vector3.zero;
        SetArrowLocalPositions();
        SetArrowLocalRotations();
    }

    private void CameraRotated(CameraFacing facing)
    {
        ArrowStaticTransforms((float)facing);
        SetArrowLocalPositions();
        SetArrowLocalRotations();
    }

    void OnEnable()
    {
        CameraController.OnCameraRotation += CameraRotated;
    }

    void OnDisable()
    {
        CameraController.OnCameraRotation -= CameraRotated;

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

    void OnDrawGizmos()
    {
        Gizmos.DrawSphere(lastRaycastPoint, 0.2f);
    }
}