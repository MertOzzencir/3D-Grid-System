using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Tohum: dünyada placeable (build menüsünden spawnlanır, adada durur), bir ekin türünü (CropSO) gösterir. Bir obje bir
// yığın: içinde Count kadar tohum var.
// - Sağ tık basılı tutulur (GridDragMotor). Tutarken mouse'un altına gelen aynı türden tohumlar toplanır: ele uçar,
//   yığına katılır (hızlı toplama).
// - Botun tarlasının üstünde (FarmGrid IToolTarget) tohum hücrenin üstünde süzülür. Sol tık ekilebilir hücreye bir
//   tohum eker; sol tık basılı sürüklenirse geçilen her boş hücreye eker. Tohum bitince elden çıkar, yok olur.
// - Sağ tık bırakılınca kalan yığın normal placeable gibi yere konur. Sayı ISaveState ile kaydedilir.
public class Seed : GridPlaceable, IInteractable, ISaveState
{
    [SerializeField] private CropSO crop;
    [SerializeField] private float followSpeed = 15f;
    [Tooltip("Yığındaki tohum sayısı (build menüsünden gelen tohum bununla başlar)")]
    [SerializeField, Min(1)] private int count = 1;

    [Header("Toplama")]
    [Tooltip("Tutarken mouse'un bu yarıçapında (birim) kalan aynı tür tohumlar toplanır")]
    [SerializeField] private float collectRadius = 0.35f;
    [Tooltip("Toplanan tohumun ele uçma süresi (saniye)")]
    [SerializeField] private float collectFlyDuration = 0.18f;

    [Header("Görsel")]
    [Tooltip("Toplayınca / ekince kısa bir şişip inme oynatılan obje (modelin kökü). Boşsa oynatılmaz.")]
    [SerializeField] private Transform visual;
    [Tooltip("Sayı yazısının pivot'tan yüksekliği (yazı sadece yığında 1'den fazla tohum varken görünür)")]
    [SerializeField] private float labelHeight = 0.45f;
    [SerializeField] private float labelSize = 2.5f;

    private const float PunchDuration = 0.2f;
    private const float PunchAmount = 0.25f;

    private GridDragMotor drag;
    private bool leftHeld;
    private bool consumed;   // son tohum ekildi: obje yok ediliyor
    private bool collecting; // başka bir tohuma toplanıyor (uçuyor)
    private TextMeshPro label;
    private Vector3 visualRestScale = Vector3.one;
    private float punchStart = -1f;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private readonly List<GameObject> flying = new List<GameObject>();

    public CropSO Crop => crop;
    public int Count => count;

    private void Awake()
    {
        drag = new GridDragMotor(this, followSpeed);
        if (visual != null) visualRestScale = visual.localScale;
        if (GetComponent<Collider>() == null)
        {
            // Tutmak için kökte collider (InteractableController collider'ın objesinde IInteractable arar)
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, 0f);
            box.size = new Vector3(0.5f, 0.5f, 0.5f);
        }
        CreateLabel();
        UpdateLabel();
    }

    private void OnEnable() => InputManager.OnMouseLeft += OnMouseLeft;
    private void OnDisable() => InputManager.OnMouseLeft -= OnMouseLeft;

    private void OnMouseLeft(bool pressed) => leftHeld = pressed;

    private void OnDestroy()
    {
        foreach (GameObject other in flying)
            if (other != null) Destroy(other);
    }

    // --- IInteractable: elde taşınırken ---

    public void InteractContractBeginnig()
    {
        consumed = false;
        drag.Begin();
    }

    public void InteractContract(out bool success)
    {
        success = true;
        drag.Tick();
        if (leftHeld) drag.TryUseOnTarget(out _); // sol tık basılı sürükleyerek ekme: geçilen boş hücrelere
        if (!consumed) CollectUnderMouse();
    }

    // Sol tık: tarlanın üstündeyse gösterilen hücreye ek. Tohum elde kalır.
    public void Interact(out bool finished)
    {
        drag.TryUseOnTarget(out _);
        finished = false;
    }

    public void ContractCancel() => drag.Cancel();

    // FarmGrid bir tohum ekti
    public void OnPlanted()
    {
        count--;
        Punch();
        UpdateLabel();
        if (count > 0) return;

        consumed = true;
        InteractableController.Instance?.Release(this);
        drag.Abort();
        Destroy(gameObject);
    }

    // --- Toplama ---

    private void CollectUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int hits = Physics.SphereCastNonAlloc(ray, collectRadius, hitBuffer, 200f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            Seed other = hitBuffer[i].collider.GetComponentInParent<Seed>();
            if (other == null || other == this || other.collecting || other.crop != crop) continue;
            Absorb(other);
        }
    }

    private void Absorb(Seed other)
    {
        count += other.count;
        other.collecting = true;
        other.StopAllCoroutines(); // yere konma animasyonundaysa yarıda kalsın
        GridManager.Instance?.PlaceableRemoveOn(other, false);
        foreach (Collider collider in other.GetComponentsInChildren<Collider>()) collider.enabled = false;
        if (other.label != null) other.label.gameObject.SetActive(false);

        flying.Add(other.gameObject);
        StartCoroutine(FlyIn(other.transform));
        UpdateLabel();
        Punch();
    }

    // Toplanan tohum ele doğru küçülerek uçar, sonra yok olur (sayısı zaten yığında)
    private IEnumerator FlyIn(Transform other)
    {
        Vector3 start = other.position;
        Vector3 startScale = other.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(collectFlyDuration, 0.0001f))
        {
            if (other == null) yield break;
            float e = t * t; // hızlanarak gelsin
            other.position = Vector3.Lerp(start, transform.position, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.3f;
            other.localScale = startScale * Mathf.Lerp(1f, 0.3f, e);
            yield return null;
        }
        if (other == null) yield break;
        flying.Remove(other.gameObject);
        Destroy(other.gameObject);
    }

    // --- Görsel ---

    private void CreateLabel()
    {
        var labelObject = new GameObject("Count");
        labelObject.transform.SetParent(transform, false);
        label = labelObject.AddComponent<TextMeshPro>();
        label.fontSize = labelSize;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(2f, 1f);
    }

    private void UpdateLabel()
    {
        if (label == null) return;
        label.gameObject.SetActive(count > 1);
        label.text = $"×{count}";
    }

    private void Punch() => punchStart = Time.time;

    private void LateUpdate()
    {
        // Yazı tohumla dönmez, hep kameraya bakar
        if (label != null && label.gameObject.activeSelf && Camera.main != null)
        {
            label.transform.SetPositionAndRotation(transform.position + Vector3.up * labelHeight, Camera.main.transform.rotation);
        }

        if (visual == null || punchStart < 0f) return;
        float t = (Time.time - punchStart) / PunchDuration;
        if (t >= 1f)
        {
            visual.localScale = visualRestScale;
            punchStart = -1f;
            return;
        }
        visual.localScale = visualRestScale * (1f + PunchAmount * Mathf.Sin(t * Mathf.PI) * (1f - t));
    }

    // --- Kayıt ---

    public string CaptureState() => count.ToString();

    public void RestoreState(string state)
    {
        if (int.TryParse(state, out int saved) && saved > 0) count = saved;
        UpdateLabel();
    }
}
