using UnityEngine;

// Kesme kütüğü (Workstation): üstüne konan parça (1BR odun, yarım odun) baltayla bölünür. Balta sağ tıkla alınıp
// kütüğe getirilir, parçanın üstünde (axeOffset, axeRotation; kameraya göre) durur, sol tıkla vurulur (ağaç kesmedeki
// gibi); hitsToSplit vuruşta parça bölünür, çıktılar kütüğün soluna ve sağına zıplar.
// 2×2. Geçici görünüm (model yokken): ayak izinin ortasında silindir kütük, hücrenin alt yarısı, üstü topHeight'ta.
public class ChoppingBlock : Workstation
{
    [Header("Kesme")]
    [Tooltip("Kaç vuruşta bölünür")]
    [SerializeField, Min(1)] private int hitsToSplit = 1;
    [Tooltip("Baltanın parçanın ortasına göre yeri (birim, kameraya göre: X sağ, Y yukarı, Z kameradan uzağa / geriye)")]
    [SerializeField] private Vector3 axeOffset = new Vector3(0f, 0.5f, -1f);
    [Tooltip("Baltanın kütüğün üstündeki dönüşü (derece, kameraya göre: kamera döndükçe balta da döner). Play'deyken " +
             "değiştirince anında görünür; istenen duruş bulununca prefab'a yazılır.")]
    [SerializeField] private Vector3 axeRotation = new Vector3(45f, 0f, 0f);
    [Tooltip("Vuruşta oynar (isteğe bağlı). Particle'lar yerel +Z yönüne saçılmalı.")]
    [SerializeField] private ParticleSystem chopEffect;
    [SerializeField] private HitShake hitShake = new HitShake();

    private int hits;

    protected override void BuildPlaceholder(Transform parent)
    {
        float height = Mathf.Max(0.1f, topHeight + 0.5f); // hücrenin tabanından üst yüzeye
        Primitive(PrimitiveType.Cylinder, parent, LocalCenter + Vector3.up * (-0.5f + height * 0.5f), new Vector3(1.6f, height * 0.5f, 1.6f), placeholderColor);
    }

    protected override void OnHeld() => hits = 0;

    // Ağaçtaki oklar gibi kamerayla 90° adımlarla döner (balta kameraya göre aynı duruşta kalır)
    private static Quaternion AxeFrame() => Quaternion.Euler(0f, (float)CameraController.CurrentFacing, 0f);

    protected override bool ToolPose(IInteractable interacted, out Vector3 position, out bool accept)
    {
        position = default;
        accept = false;
        if (!(interacted is Axe)) return false;
        accept = CanWork;
        position = HoldPoint + AxeFrame() * axeOffset;
        return true;
    }

    public override Quaternion GetToolTargetRotation() => held != null ? AxeFrame() * Quaternion.Euler(axeRotation) : transform.rotation;

    public override bool IsStillTargeted(IInteractable interacted, Ray mouseRay)
    {
        // Balta: mouse kütüğün dikey ekseninden 3 birimden fazla uzaklaşmadıkça bırakılmaz
        if (!(interacted is Axe) || held == null) return false;
        var plane = new Plane(-Camera.main.transform.forward, HoldPoint);
        if (!plane.Raycast(mouseRay, out float enter)) return false;
        Vector3 point = mouseRay.GetPoint(enter);
        return Vector3.Distance(new Vector3(point.x, HoldPoint.y, point.z), HoldPoint) <= 3f;
    }

    protected override bool UseTool(IInteractable tool, out bool used)
    {
        used = false;
        if (!(tool is Axe)) return false;
        used = Chop();
        return true;
    }

    private bool Chop()
    {
        if (!CanWork) return false;

        Vector3 outward = CameraRight(); // sarsılma yana
        if (chopEffect != null)
        {
            chopEffect.transform.SetPositionAndRotation(PieceTop, Quaternion.LookRotation(Vector3.up));
            chopEffect.Play();
        }
        // Bölmeyen vuruşta parça, bölen vuruşta (parça siliniyor) kütüğün kendisi sarsılır
        if (++hits < hitsToSplit)
        {
            StartCoroutine(hitShake.Play(held.transform, held.transform.localRotation, -outward));
            return true;
        }
        if (visual != null) StartCoroutine(hitShake.Play(visual, visualRest, -outward));
        SplitHeld();
        return true;
    }
}
