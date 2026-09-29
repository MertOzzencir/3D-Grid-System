using System.Collections;
using UnityEngine;

public abstract class ToolBase : GridPlaceable, IInteractable, IGloveGrip
{
    public Transform VisualTransform;
    [SerializeField] private float followSpeed = 10f;

    [Header("Eldiven")]
    [Tooltip("Eldivenin avucunun sapta oturacağı nokta; VisualTransform'un altında olsun ki sallanmayla birlikte dönsün. " +
             "Y avucun baktığı yön (sapa doğru), Z parmak uçları. Boşsa eldiven normal tutar.")]
    [SerializeField] private Transform gloveGrip;
    [Tooltip("Sapı kavrarken parmakların kıvrılması (0 = düz, 1 = tam kıvrık)")]
    [SerializeField, Range(0f, 1f)] private float gloveGripCurl = 0.7f;

    public Transform GripPoint => gloveGrip;
    public float GripCurl => gloveGripCurl;

    [Header("Kullanım")]
    [Tooltip("İki kullanım arasındaki en kısa süre (saniye). Tıklama anından itibaren sayılır.")]
    [SerializeField] private float cooldown = 0.6f;
    [SerializeField] private ToolSwingAnimation swing = new ToolSwingAnimation();

    private GridDragMotor drag;
    private float nextUseTime;
    private Coroutine useRoutine;
    private Quaternion visualRestRotation;

    // Cooldown doldu ve önceki sallanma bitti
    public bool IsReady => Time.time >= nextUseTime && useRoutine == null;

    protected virtual void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        if (VisualTransform != null)
            visualRestRotation = VisualTransform.localRotation;
    }

    // Sol tık: hazırsa sallanmayı başlatır. Asıl kullanım (ağacı kesmek) vuruş anında olur.
    public void Interact(out bool finished)
    {
        finished = false;
        if (!IsReady) return;

        nextUseTime = Time.time + cooldown;
        useRoutine = StartCoroutine(UseRoutine());
    }

    protected abstract bool OnUseWithoutTarget();

    public void InteractContractBeginnig() => drag.Begin();
    public void InteractContract(out bool success) { success = true; drag.Tick(); }

    public void ContractCancel()
    {
        StopUse();
        drag.Cancel();
    }

    private IEnumerator UseRoutine()
    {
        if (VisualTransform != null)
            yield return swing.Play(VisualTransform, visualRestRotation, Use);
        else
            Use();

        useRoutine = null;
    }

    // Vuruş anı: hedef hâlâ varsa ona uygula, yoksa aletin kendi davranışı
    private void Use()
    {
        if (!drag.TryUseOnTarget(out _))
            OnUseWithoutTarget();
    }

    // Sallanırken bırakılırsa animasyonu kes, görseli düzelt
    private void StopUse()
    {
        if (useRoutine != null)
        {
            StopCoroutine(useRoutine);
            useRoutine = null;
        }
        if (VisualTransform != null)
            VisualTransform.localRotation = visualRestRotation;
    }
}
