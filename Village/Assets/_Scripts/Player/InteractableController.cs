using System;
using UnityEngine;

public class InteractableController : MonoBehaviour
{
    public static InteractableController Instance;
    public static event Action<IInteractable> OnNewInteractable;
    private IInteractable currentInteracted;
    private bool isActive = false;
    private bool tryingToInteract = false;

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
        if (currentInteracted != null)
        {
            tryingToInteract = false;
            currentInteracted.ContractCancel();
            currentInteracted = null;
        }
    }
    public void HardCancel(IInteractable current)
    {
        if (current != currentInteracted) return;
        CancelInteract();
    }

    private void FinishInteract(bool obj)
    {
        if (currentInteracted == null) return;

        if (obj)
            currentInteracted.Interact(out bool f);
    }

    public void SetEnable()
    {
        isActive = !isActive;
        enabled = isActive;
    }
    void OnEnable()
    {
        InputManager.OnMouseRight += TryToInteract;
        InputManager.OnMouseLeft += FinishInteract;
    }


    void OnDisable()
    {
        InputManager.OnMouseRight -= TryToInteract;
        InputManager.OnMouseLeft -= FinishInteract;
    }
}
