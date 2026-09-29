using System;
using UnityEngine;

public class InteractableController : MonoBehaviour
{
    public static InteractableController Instance;
    public static event Action<IInteractable> OnNewInteractable;
    // Açık/kapalı durumunu PlacementController yönetir: build mode'da kapalı
    private IInteractable currentInteracted;
    private bool tryingToInteract = false;
    private IHoverable hovered;

    public bool IsHolding => currentInteracted != null;

    void Awake()
    {
        if(Instance == null) Instance = this;
    }
    void Update()
    {
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
        if (currentInteracted == null) return;

        if (obj)
            currentInteracted.Interact(out bool f);
    }

    // Elde bir şey yokken: mouse'un altındaki ilk collider'ın objesi IHoverable ise o hover'da
    private void UpdateHover()
    {
        IHoverable target = null;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
            hit.collider.TryGetComponent(out target);
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
