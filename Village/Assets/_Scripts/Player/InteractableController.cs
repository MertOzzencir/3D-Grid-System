using System;
using UnityEngine;

// Ne zaman tutulur / tıklanır / hover olur. Eldiven varsa hedef eldivenin altındaki obje (GloveCursor.TargetCollider:
// avucun en çok kapladığı etkileşimli collider): el neyin üstündeyse onunla etkileşilir. Eldiven yoksa mouse ışını.
public class InteractableController : MonoBehaviour
{
    public static InteractableController Instance;
    public static event Action<IInteractable> OnNewInteractable;
    // Açık/kapalı durumunu PlacementController yönetir: build mode'da kapalı
    [Tooltip("Mouse bu hızı (ekran yüksekliği / saniye) geçerse ani hareket sayılır: yapışkan hedefler (ağaç, odun) bırakılır. " +
             "Mouse görünmediği için küçük kaymalar etkileşimi kesmesin, sadece bilinçli bir savurma kessin.")]
    [SerializeField] private float flickReleaseSpeed = 3f;

    private IInteractable currentInteracted;
    private bool tryingToInteract = false;
    private IHoverable hovered;
    private Vector3 lastMousePosition;
    private float mouseSpeed; // ekran yüksekliği / saniye, kısa süreli yumuşatılmış

    // Mouse şu an ani (savurma) hareket mi yapıyor
    public bool IsMouseFlicking => mouseSpeed > flickReleaseSpeed;

    public bool IsHolding => currentInteracted != null;
    public IInteractable Held => currentInteracted;

    void Awake()
    {
        if(Instance == null) Instance = this;
    }
    void Update()
    {
        UpdateMouseSpeed();

        if (currentInteracted == null)
        {
            if (tryingToInteract)
            {
                TryToInteract(true);
            }

            // Tutma bu karede başladıysa hover temizlenir, yoksa mouse'un altı takip edilir
            if (currentInteracted == null) UpdateHover();
            else SetHovered(null);
            return;
        }
        currentInteracted.InteractContract(out bool s);
        if (!s)
        {
            CancelInteract();
        }
    }
    private void TryToInteract(bool obj)
    {
        if (obj)
        {
            tryingToInteract = true;
            if (UsesGlove(out GloveCursor glove))
            {
                if (glove.TargetCollider != null && glove.TargetCollider.TryGetComponent(out IInteractable target))
                {
                    currentInteracted = target;
                    currentInteracted.InteractContractBeginnig();
                    OnNewInteractable?.Invoke(currentInteracted);
                }
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider.TryGetComponent(out IInteractable interacted))
                {
                    currentInteracted = interacted;
                    currentInteracted.InteractContractBeginnig();
                    OnNewInteractable?.Invoke(currentInteracted);
                    break;
                }
            }
        }
        else
        {
            CancelInteract();
        }
    }

    private void CancelInteract()
    {
        // Elde bir şey olmasa da sıfırlanmalı: yoksa boşluğa sağ tıklayıp bırakınca Update,
        // mouse'un altından geçen her objeyi tıklamadan tutmaya çalışır
        tryingToInteract = false;

        if (currentInteracted == null) return;
        currentInteracted.ContractCancel();
        currentInteracted = null;
    }
    public void HardCancel(IInteractable current)
    {
        if (current != currentInteracted) return;
        CancelInteract();
    }

    // Elde tutulanı ContractCancel çağırmadan bırakır. Obje yok edilecekse kullan
    // (iptal, objeyi grid'e geri yerleştirmeye çalışır).
    public void Release(IInteractable current)
    {
        if (current != currentInteracted) return;
        currentInteracted = null;
        tryingToInteract = false;
    }

    private void FinishInteract(bool obj)
    {
        // Elde bir şey yokken sol tık: mouse'un altındaki ilk obje IClickable ise ona iletilir (örn. kediyi sevmek)
        if (currentInteracted == null)
        {
            if (obj) TryClick();
            return;
        }

        if (obj)
            currentInteracted.Interact(out bool f);
    }

    // Eldiven sahnede ve açık: hedefi o seçer
    private static bool UsesGlove(out GloveCursor glove)
    {
        glove = GloveCursor.Instance;
        return glove != null && glove.isActiveAndEnabled;
    }

    private void TryClick()
    {
        if (UsesGlove(out GloveCursor glove))
        {
            if (glove.TargetCollider != null && glove.TargetCollider.TryGetComponent(out IClickable target))
                target.OnClicked(glove.TargetHit);
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit) && hit.collider.TryGetComponent(out IClickable clickable))
            clickable.OnClicked(hit);
    }

    // Ekran yüksekliğine göre (çözünürlükten bağımsız) hız; tek karelik sıçramalar ani hareket sayılmasın diye
    // ~0.05 sn'lik yumuşatma
    private void UpdateMouseSpeed()
    {
        Vector3 mouse = Input.mousePosition;
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        float speed = (mouse - lastMousePosition).magnitude / Mathf.Max(Screen.height, 1) / dt;
        lastMousePosition = mouse;
        mouseSpeed = Mathf.Lerp(mouseSpeed, speed, 1f - Mathf.Exp(-dt / 0.05f));
    }

    // Elde bir şey yokken: mouse'un altındaki ilk collider'ın objesi IHoverable ise o hover'da
    private void UpdateHover()
    {
        IHoverable target = null;
        if (UsesGlove(out GloveCursor glove))
        {
            if (glove.TargetCollider != null) glove.TargetCollider.TryGetComponent(out target);
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
                hit.collider.TryGetComponent(out target);
        }
        SetHovered(target);
    }

    private void SetHovered(IHoverable target)
    {
        if (ReferenceEquals(target, hovered)) return;
        // Önceki obje bu arada yok edilmiş olabilir (Unity null'u)
        bool previousDestroyed = hovered is UnityEngine.Object previous && previous == null;
        if (hovered != null && !previousDestroyed) hovered.OnHoverExit();
        hovered = target;
        hovered?.OnHoverEnter();
    }

    // Elde tutulan grid objesini 90° döndürür. Görsel dönüşü GridDragMotor her karede takip eder.
    private void RotateHeld()
    {
        if (currentInteracted is GridEntity entity)
            entity.RotateFootprint();
    }

    void OnEnable()
    {
        InputManager.OnMouseRight += TryToInteract;
        InputManager.OnMouseLeft += FinishInteract;
        InputManager.OnR += RotateHeld;
    }


    void OnDisable()
    {
        // Kapalıyken mouse'un bırakılmasını duyamayız: elde ne varsa bırak, durumu sıfırla.
        // Yoksa "tutmaya çalışıyorum" takılı kalır ve tekrar açılınca tıklamadan obje tutulur.
        CancelInteract();
        SetHovered(null); // build mode'a girince önizleme kalmasın

        InputManager.OnMouseRight -= TryToInteract;
        InputManager.OnMouseLeft -= FinishInteract;
        InputManager.OnR -= RotateHeld;
    }
}
