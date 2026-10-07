using UnityEngine;

// Orak: hasat aleti (DESIGN.md: hasat yolu bulmacası). Balta gibi sağ tık basılı tutulur. Botun tarlasının üstünde
// hücrenin üstünde süzülür (FarmGrid IToolTarget); sol tık basılıyken geçilen olgun kareler hasat edilir, yol FarmGrid'de
// sayılır (kombo). Sol tık bırakılınca ya da tarladan çıkınca yol biter. Baltadaki gibi sallanma yok: her karede kısa
// bir savrulma (görsel hafifçe döner).
public class Sickle : ToolBase
{
    [Tooltip("Her hasat edilen karede görselin savrulma açısı (derece)")]
    [SerializeField] private float swishAngle = 25f;
    [SerializeField] private float swishDuration = 0.12f;

    private bool leftHeld;
    private Quaternion visualRest;
    private float swishStart = -1f;

    protected override void Awake()
    {
        base.Awake();
        if (VisualTransform != null) visualRest = VisualTransform.localRotation;
    }

    private void OnEnable() => InputManager.OnMouseLeft += OnMouseLeft;
    private void OnDisable() => InputManager.OnMouseLeft -= OnMouseLeft;

    private void OnMouseLeft(bool pressed)
    {
        leftHeld = pressed;
        if (!pressed) FarmGrid.EndActiveHarvest(); // yol bitti
    }

    protected override bool OnUseWithoutTarget() => false;

    // Sol tık: baltadaki sallanma yerine hemen ilk kare (yol başlangıcı)
    public override void Interact(out bool finished)
    {
        finished = false;
        Drag.TryUseOnTarget(out _);
    }

    public override void InteractContract(out bool success)
    {
        base.InteractContract(out success);
        if (leftHeld) Drag.TryUseOnTarget(out _); // basılı sürükleme: geçilen kareler
    }

    public override void ContractCancel()
    {
        FarmGrid.EndActiveHarvest();
        base.ContractCancel();
    }

    // FarmGrid bir kareyi hasat etti
    public void OnHarvested() => swishStart = Time.time;

    private void LateUpdate()
    {
        if (VisualTransform == null || swishStart < 0f) return;
        float t = (Time.time - swishStart) / Mathf.Max(swishDuration, 0.0001f);
        if (t >= 1f)
        {
            VisualTransform.localRotation = visualRest;
            swishStart = -1f;
            return;
        }
        VisualTransform.localRotation = visualRest * Quaternion.Euler(0f, swishAngle * Mathf.Sin(t * Mathf.PI), 0f);
    }
}
