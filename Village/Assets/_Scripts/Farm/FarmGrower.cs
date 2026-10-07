using System.Collections;
using TMPro;
using UnityEngine;

// Tarlanın büyüme döngüsü (DESIGN.md: bütün tarla aynı anda ekilir, aynı anda olgunlaşır). Botun tarla kökünde (Farm)
// durur, Boat kurar. Tarlanın ön sağ köşesinde bir düğme var; boş elle sol tıklanınca, ekili fide varsa:
// kapak tarlanın üstüne kapanır → growDuration saniye geçer (kapağın üstünde kalan süre yazar) → kapak açılır, bütün
// fideler olgunlaşmış çıkar. Kapalıyken tarla kilitli (FarmGrid.Locked: ekilmez, parça konmaz / alınmaz); bot bu
// sırada gezebilir. Kapak ve düğme şimdilik primitif yer tutucu; kapak için model verilebilir.
// Kapak ve düğme tarlanın child'ı: eldiven onların üstünde de yürür (FarmGrid IGloveWalkable).
public class FarmGrower : MonoBehaviour
{
    public enum State { Idle, Closing, Growing, Opening }

    private const float CloseDuration = 0.45f;
    private const float OpenDuration = 0.35f;
    private const float PressDuration = 0.15f;
    private const float PressDepth = 0.05f;

    private FarmGrid grid;
    private float duration;
    private float coverHeight;
    private Transform cover;
    private Transform buttonCap;
    private Vector3 buttonCapRest;
    private TextMeshPro label;
    private State state = State.Idle;
    private float remaining;
    private Coroutine pressRoutine;

    public State Current => state;
    public float Remaining => remaining;

    // Kök = güvertenin üst yüzünün ortası, +Z botun tarafı (ön). baseMaterial her parçaya kopyalanmadan verilir,
    // renkler MaterialPropertyBlock ile.
    public void Setup(FarmGrid farmGrid, int size, float growDuration, float height, Material baseMaterial,
                      Color coverColor, Color buttonColor, GameObject coverModel)
    {
        grid = farmGrid;
        duration = Mathf.Max(0f, growDuration);
        coverHeight = Mathf.Max(0.2f, height);
        float half = size * 0.5f;

        BuildCover(size, baseMaterial, coverColor, coverModel);
        BuildButton(new Vector3(half + 0.3f, 0f, half - 0.3f), baseMaterial, buttonColor);
        BuildLabel();
    }

    // Kapak: tarlayı biraz taşan kutu + üstünde daha küçük çatı. Kök tabanda: Y ölçeği 0 → 1 ile güverteden yükselir.
    // Collider kökte (eldiven kapalı tarlanın üstünde yürür).
    private void BuildCover(int size, Material material, Color color, GameObject coverModel)
    {
        cover = new GameObject("Grow Cover").transform;
        cover.SetParent(transform, false);
        float width = size + 0.12f;

        if (coverModel != null)
        {
            // Model 1×1 alana göre modellenir (pivot tabanın ortasında), tarlanın boyuna X/Z'de ölçeklenir
            Transform model = Instantiate(coverModel, cover).transform;
            model.localScale = new Vector3(width, 1f, width);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);
        }
        else
        {
            AddPart(cover, PrimitiveType.Cube, new Vector3(0f, coverHeight * 0.5f, 0f), new Vector3(width, coverHeight, width), material, color);
            AddPart(cover, PrimitiveType.Cube, new Vector3(0f, coverHeight + 0.07f, 0f), new Vector3(width * 0.8f, 0.14f, width * 0.8f),
                    material, color * 0.92f);
        }

        var box = cover.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, coverHeight * 0.5f, 0f);
        box.size = new Vector3(width, coverHeight, width);
        cover.gameObject.SetActive(false);
    }

    // Düğme: tarlanın dışında küçük bir taban, direk ve üstünde basılan yuvarlak kapak. Kapak parçaların (1 birim)
    // ve fidelerin üstünde kalsın diye direk uzun.
    private void BuildButton(Vector3 position, Material material, Color color)
    {
        var button = new GameObject("Grow Button").transform;
        button.SetParent(transform, false);
        button.localPosition = position;

        Color wood = new Color(0.62f, 0.45f, 0.32f);
        AddPart(button, PrimitiveType.Cube, new Vector3(0f, -0.15f, 0f), new Vector3(0.42f, 0.3f, 0.42f), material, wood);
        AddPart(button, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 0.6f, 0.12f), material, wood);
        AddPart(button, PrimitiveType.Cylinder, new Vector3(0f, 1.22f, 0f), new Vector3(0.3f, 0.03f, 0.3f), material, wood * 0.85f);
        buttonCap = AddPart(button, PrimitiveType.Cylinder, new Vector3(0f, 1.28f, 0f), new Vector3(0.22f, 0.05f, 0.22f), material, color);
        buttonCapRest = buttonCap.localPosition;

        var box = button.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(0.42f, 1.5f, 0.42f);
        button.gameObject.AddComponent<FarmGrowButton>().Grower = this;
    }

    private void BuildLabel()
    {
        var labelObject = new GameObject("Grow Timer");
        labelObject.transform.SetParent(transform, false);
        label = labelObject.AddComponent<TextMeshPro>();
        label.fontSize = 4f;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(3f, 1f);
        labelObject.SetActive(false);
    }

    private static Transform AddPart(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var renderer = part.GetComponent<Renderer>();
        if (material != null) renderer.sharedMaterial = material;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
        return part.transform;
    }

    // Düğmeye basıldı: her basışta kapak iner kalkar; boştaysa ve fide varsa büyüme başlar
    public void Press()
    {
        if (pressRoutine != null) StopCoroutine(pressRoutine);
        pressRoutine = StartCoroutine(PressAnimation());
        if (state != State.Idle || grid == null || !grid.HasSeedlings) return;
        StartCoroutine(GrowRoutine());
    }

    private IEnumerator PressAnimation()
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / PressDuration)
        {
            buttonCap.localPosition = buttonCapRest + Vector3.down * (PressDepth * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        buttonCap.localPosition = buttonCapRest;
        pressRoutine = null;
    }

    private IEnumerator GrowRoutine()
    {
        state = State.Closing;
        grid.Locked = true;
        cover.gameObject.SetActive(true);
        for (float t = 0f; t < 1f; t += Time.deltaTime / CloseDuration)
        {
            // Hafif taşarak kapanır (kil gibi)
            float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.12f * (1f - t);
            SetCoverHeight(Mathf.SmoothStep(0f, 1f, t) * overshoot);
            yield return null;
        }
        SetCoverHeight(1f);

        state = State.Growing;
        label.gameObject.SetActive(true);
        remaining = duration;
        while (remaining > 0f)
        {
            label.text = FormatTime(remaining);
            remaining -= Time.deltaTime;
            yield return null;
        }
        remaining = 0f;
        label.gameObject.SetActive(false);

        state = State.Opening;
        for (float t = 0f; t < 1f; t += Time.deltaTime / OpenDuration)
        {
            SetCoverHeight(1f - Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        cover.gameObject.SetActive(false);
        grid.Locked = false;
        grid.MatureAll();
        state = State.Idle;
    }

    private void SetCoverHeight(float k) => cover.localScale = new Vector3(1f, Mathf.Max(0.001f, k), 1f);

    private static string FormatTime(float seconds)
    {
        int total = Mathf.CeilToInt(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    // Süre yazısı tarlayla dönmez, kapağın üstünde kameraya bakar
    private void LateUpdate()
    {
        if (label == null || !label.gameObject.activeSelf || Camera.main == null) return;
        label.transform.SetPositionAndRotation(transform.position + Vector3.up * (coverHeight + 0.55f), Camera.main.transform.rotation);
    }
}
