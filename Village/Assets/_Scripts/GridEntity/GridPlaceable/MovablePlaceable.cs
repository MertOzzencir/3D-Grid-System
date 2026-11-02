using UnityEngine;

// Eldivenle tutulup taşınabilen sade grid objesi (örn. kafes): kendi davranışı yok, sadece grid'de durur ve kesme kütüğü
// gibi sürüklenip başka hücreye konur. Tutma / bırakma GridDragMotor'la; collider bu objenin kendisinde olmalı
// (eldiven tuttuğu collider'da IInteractable arar). Taşırken R ile kendi ortası etrafında döner.
public class MovablePlaceable : GridPlaceable, IInteractable
{
    [SerializeField] private float followSpeed = 10f;

    private GridDragMotor drag;

    protected virtual void Awake()
    {
        drag = new GridDragMotor(this, followSpeed, keepCenterOnRotate: true);
    }

    // --- IInteractable ---
    public void Interact(out bool finished)
    {
        if (!drag.TryUseOnTarget(out finished))
            finished = true;
    }

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }
    public void ContractCancel() => drag.Cancel();
}
