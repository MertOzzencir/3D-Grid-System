using System.Collections;
using UnityEngine;

// Blueprint'teki tek bir parça yeri: hangi parçayı istediğini (imza) ve parçanın yönünü tutar.
// Pozisyonu ve rotasyonu kendi transform'unda; görseli ve collider'ları child/component olarak Blueprint Creator ekler.
//
// Doldurma (IToolTarget): oyuncu uyan bir parçayı taşıyıp slot'un üstüne getirince parça slot'a yapışır ve
// doğru yöne kendisi döner (oyuncunun R ile çevirmesi gerekmez). Tıklanınca parça yok olur, slot'un görseli gerçek görünüme döner.
// Boşken görsel noktalı yarı saydam (Blueprint.EmptyFade), uyan parça üstündeyken biraz daha belirgin (HoverFade),
// elde bir şey yokken mouse üstüne gelince hangi parçanın gerektiği net görünsün diye neredeyse tam (PreviewFade).
public class BlueprintSlot : MonoBehaviour, IToolTarget, IHoverable
{
    [SerializeField] private string signature;
    [SerializeField] private GridMaskRotator.Rotation rotation;
    [Tooltip("Parçanın standart (imzadaki) halinin blueprint içindeki yönü. Gelen parça bu yöne + kendi adımına döndürülür.")]
    [SerializeField] private GridMaskRotator.Rotation canonicalRotation;

    private Blueprint blueprint;
    private Renderer[] renderers;
    private Coroutine wobbleRoutine;
    private GridMaskRotator.Rotation incomingRotation; // son kabul edilen parçanın dünya rotasyonu
    private bool hovered;     // uyan parça taşınırken üstünde
    private bool previewing;  // elde bir şey yokken mouse üstünde
    private DitherFade currentFade;
    private bool started;

    public string Signature => signature;
    public GridMaskRotator.Rotation Rotation => rotation;
    public bool IsFilled { get; private set; }
    public Renderer[] Renderers => renderers ??= GetComponentsInChildren<Renderer>(true);

    // Bu parça slot'a uyar mı
    public bool Accepts(IBlueprintPiece piece) => piece != null && piece.BlueprintSignature == signature;

    private void Awake() => blueprint = GetComponentInParent<Blueprint>();

    // Kayıttan yükleme (RestoreState) Start'tan önce olur; görsel burada duruma göre kurulur
    private void Start()
    {
        started = true;
        RefreshFade();
    }

    // Kayıttan: efektsiz
    public void SetFilledSilently(bool filled)
    {
        IsFilled = filled;
        if (!started) return;
        if (filled)
        {
            currentFade = null;
            DitherFade.Clear(Renderers);
        }
        else RefreshFade();
    }

    // Boş slot'un görünürlüğü: taşınan parça üstünde > önizleme > boş. Sadece değişince animasyon başlar.
    private void RefreshFade()
    {
        if (IsFilled) return;
        DitherFade target = hovered ? blueprint.HoverFade : previewing ? blueprint.PreviewFade : blueprint.EmptyFade;
        if (target == currentFade) return;
        currentFade = target;
        target.FadeOut(Renderers);
    }

    // --- IHoverable: elde bir şey yokken mouse üstünde ---
    public void OnHoverEnter()
    {
        previewing = true;
        RefreshFade();
    }

    public void OnHoverExit()
    {
        previewing = false;
        RefreshFade();
    }

    // --- IToolTarget ---
    public Vector3 GetToolTargetPosition(IInteractable interacted, out bool accept)
    {
        accept = !IsFilled && interacted is IBlueprintPiece piece && Accepts(piece);
        if (!accept) return default;

        // Standart hal blueprint içinde canonicalRotation yönünde duruyor; parça kendi şeklini standarda
        // getiren adım kadar ileri döndürülünce slot'takiyle birebir aynı yöne bakar. Blueprint'in kendi dönüşü de eklenir.
        int steps = (int)blueprint.Rotation + (int)canonicalRotation + ((IBlueprintPiece)interacted).BlueprintRotationSteps;
        incomingRotation = (GridMaskRotator.Rotation)(steps % 4);

        SetHovered(true);
        return transform.position;
    }

    public Quaternion GetToolTargetRotation() => GridMaskRotator.ToQuaternion(incomingRotation);

    public void OnToolTargetExit() => SetHovered(false);

    public bool OnToolUsed(IInteractable tool)
    {
        if (IsFilled || !(tool is IBlueprintPiece piece) || !Accepts(piece) || !(tool is Component component)) return false;

        // Parça yok olacak: geri yerleştirilmeye çalışılmasın diye iptal değil, sadece bırakıyoruz
        InteractableController.Instance.Release(tool);
        Destroy(component.gameObject);

        IsFilled = true;
        hovered = false;
        previewing = false;
        currentFade = null;
        DitherFade.Clear(Renderers); // birden gerçek görünüme döner, üstüne sallanır
        blueprint.OnSlotFilled(this);
        return true;
    }

    // Slot dolunca tek başına sallanma (blueprint tamamlanıyorsa onun efekti oynar, bu oynamaz)
    public void PlayFillWobble(JellyWobble wobble)
    {
        if (wobbleRoutine != null) StopCoroutine(wobbleRoutine);
        Bounds bounds = Blueprint.CombinedBounds(Renderers);
        var basePoint = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        wobbleRoutine = StartCoroutine(wobble.Play(Renderers, basePoint, bounds.size.y, Vector3.zero));
    }

    private void SetHovered(bool value)
    {
        if (IsFilled || hovered == value) return;
        hovered = value;
        RefreshFade();
    }

#if UNITY_EDITOR
    public void EditorSetup(string signature, GridMaskRotator.Rotation rotation, GridMaskRotator.Rotation canonicalRotation)
    {
        this.signature = signature;
        this.rotation = rotation;
        this.canonicalRotation = canonicalRotation;
    }
#endif
}
