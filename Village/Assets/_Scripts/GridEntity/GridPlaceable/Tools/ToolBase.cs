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
    [Tooltip("Açıksa gloveGrip'ten sapın etrafında 90°'lik 3 kopya daha üretilir; kameraya göre eldivenin önde kaldığı seçilir")]
    [SerializeField] private bool gloveGripFourSides = true;
    [Tooltip("gloveGrip'ten sapın eksenine uzaklık (avuç yönünde). Sap ekseni gloveGrip'in X'i. Sahnede gizmo ile kontrol et.")]
    [SerializeField] private float gloveGripHandleRadius = 0.11f;

    private const float GripSwitchMargin = 0.15f; // sınır açılarda iki yön arasında gidip gelmesin

    private Transform[] gloveGrips;
    private int currentGrip = -1;

    public Transform GripPoint => gloveGrips == null ? gloveGrip : SelectGrip();
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
        CreateGloveGrips();
    }

    // Dört yön: gloveGrip ve onun sap ekseni etrafında 90°, 180°, 270° döndürülmüş kopyaları.
    // Kopyalar gloveGrip'le aynı parent'ta (görsel altında), vuruşta aletle birlikte savrulurlar.
    private void CreateGloveGrips()
    {
        if (gloveGrip == null || !gloveGripFourSides) return;

        GetGripAxis(gloveGrip, out Vector3 center, out Vector3 axis);
        gloveGrips = new Transform[4];
        gloveGrips[0] = gloveGrip;
        for (int k = 1; k < 4; k++)
        {
            Quaternion turn = Quaternion.AngleAxis(90f * k, axis);
            var copy = new GameObject($"{gloveGrip.name} {90 * k}").transform;
            copy.SetParent(gloveGrip.parent, false);
            copy.SetPositionAndRotation(center + turn * (gloveGrip.position - center), turn * gloveGrip.rotation);
            gloveGrips[k] = copy;
        }
    }

    // Sap ekseni: gloveGrip'in X'i, avuç yönünde (Y) handleRadius kadar içeriden geçer
    private void GetGripAxis(Transform grip, out Vector3 center, out Vector3 axis)
    {
        center = grip.position + grip.up * gloveGripHandleRadius * grip.lossyScale.x;
        axis = grip.right;
    }

    // Avucu kameradan en çok uzağa bakan (eldivenin sapın kamera tarafında kaldığı) yön.
    // Mevcut yön, yenisi belirgin şekilde daha iyi olmadıkça korunur.
    private Transform SelectGrip()
    {
        // Sallanırken görsel (ve grip'ler) dönüyor: yön seçimi vuruş bitene kadar sabit, eldiven sıçramasın
        if (useRoutine != null && currentGrip >= 0) return gloveGrips[currentGrip];

        Vector3 view = Camera.main.transform.forward;
        int best = 0;
        for (int i = 1; i < gloveGrips.Length; i++)
            if (Vector3.Dot(gloveGrips[i].up, view) > Vector3.Dot(gloveGrips[best].up, view)) best = i;

        if (currentGrip < 0 ||
            Vector3.Dot(gloveGrips[best].up, view) > Vector3.Dot(gloveGrips[currentGrip].up, view) + GripSwitchMargin)
            currentGrip = best;
        return gloveGrips[currentGrip];
    }

    private void OnDrawGizmosSelected()
    {
        if (gloveGrip == null || !gloveGripFourSides) return;

        GetGripAxis(gloveGrip, out Vector3 center, out Vector3 axis);
        float size = 0.03f * gloveGrip.lossyScale.x;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(center - axis * size * 5f, center + axis * size * 5f); // sap ekseni
        for (int k = 0; k < 4; k++)
        {
            Quaternion turn = Quaternion.AngleAxis(90f * k, axis);
            Vector3 point = center + turn * (gloveGrip.position - center);
            Gizmos.color = k == 0 ? Color.green : Color.yellow;
            Gizmos.DrawSphere(point, size);
            Gizmos.DrawLine(point, point + turn * gloveGrip.up * size * 3f); // avucun baktığı yön
        }
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

    public void InteractContractBeginnig()
    {
        currentGrip = -1; // her tutmada yön baştan seçilsin
        drag.Begin();
    }
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
