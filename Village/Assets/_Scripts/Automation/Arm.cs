using UnityEngine;
using UnityEngine.InputSystem;

// Kol (kullanıcı: Factorio gibi grid tabanlı): giriş yönündeki komşu makinenin çıkış yuvasından bir parça alır, çıkış
// yönündeki komşu makinenin giriş yuvasına koyar. Önü kabul etmiyorsa parça elinde bekler.
// Yönler elle ayarlanır, dört yönden herhangi biri (kullanıcı): boş elle sol tık çıkış yönünü, Shift + sol tık giriş
// yönünü saat yönünde bir adım çevirir (ikisi aynı yöne gelemez). Yönler kolun kendi dönüşüne göre: R ile kol dönünce
// ikisi birlikte döner. Kolun üstünde giriş yeşil, çıkış turuncu işaretle gösterilir. Yönler kayda girer.
// Döngü: girişte bekler → alır → çıkışa döner (swingSeconds) → bırakır → geri döner (swingSeconds).
public class Arm : MovablePlaceable, IAutomationTick, IClickable, ISaveState
{
    [SerializeField] private float swingSeconds = 0.5f;
    [Tooltip("Dönen kısım (kodla döndürülür; Y ekseninde, yerel +Z'ye bakarken 0°)")]
    [SerializeField] private Transform pivot;
    [Tooltip("Taşınan parçanın küpü (elde parça yokken gizli)")]
    [SerializeField] private Renderer heldCube;
    [Tooltip("Yön: 0 ön (+Z), 1 sağ, 2 arka, 3 sol (kolun kendi dönüşüne göre)")]
    [SerializeField, Range(0, 3)] private int inputSide = 2;
    [SerializeField, Range(0, 3)] private int outputSide = 0;
    [SerializeField] private Color inputColor = new Color(0.35f, 0.9f, 0.4f);
    [SerializeField] private Color outputColor = new Color(1f, 0.55f, 0.15f);

    private enum Phase { Waiting, Carrying, Delivering, Returning }

    private Phase phase = Phase.Waiting;
    private ItemSO held;
    private float travel;     // 0 = girişte, 1 = çıkışta
    private float shownTravel;
    private Transform inputMarker, outputMarker;

    private static readonly Vector3Int[] LocalSides =
        { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1), new Vector3Int(-1, 0, 0) };

    private bool IsPlaced => GridManager.Instance != null && GridManager.Instance.TryGetCell(OriginWorldPosition, out GridData data)
                             && data.Placeable == this;

    protected override void Awake()
    {
        base.Awake();
        inputMarker = CreateMarker("Input Marker", inputColor);
        outputMarker = CreateMarker("Output Marker", outputColor);
        PlaceMarkers();
    }

    private void OnEnable() => AutomationTicker.Register(this, true);
    private void OnDisable() => AutomationTicker.Unregister(this);

    // Kolun yerel yönünün dünyadaki hücre farkı
    private Vector3Int WorldSide(int side)
    {
        Vector3 d = GridRotation * (Vector3)LocalSides[side];
        return new Vector3Int(Mathf.RoundToInt(d.x), 0, Mathf.RoundToInt(d.z));
    }

    // Komşu hücredeki makine; aynı katta yoksa bir üst / alt katta (madenci yatağın üstünde bir kat yukarıda durur)
    private static IItemPort PortAt(Vector3Int cell)
    {
        foreach (Vector3Int offset in new[] { Vector3Int.zero, Vector3Int.up, Vector3Int.down })
            if (GridManager.Instance.TryGetCell(cell + offset, out GridData data) && data.Placeable is IItemPort port)
                return port;
        return null;
    }

    public void AutomationTick(float step)
    {
        if (!IsPlaced) return;
        IItemPort source = PortAt(OriginWorldPosition + WorldSide(inputSide));
        IItemPort target = PortAt(OriginWorldPosition + WorldSide(outputSide));
        float speed = step / Mathf.Max(swingSeconds, 0.01f);

        switch (phase)
        {
            case Phase.Waiting:
                if (source != null && target != null && source.CanTake(out ItemSO item) && target.CanAccept(item))
                {
                    held = source.Take();
                    phase = held != null ? Phase.Carrying : Phase.Waiting;
                }
                break;
            case Phase.Carrying:
                travel = Mathf.Min(1f, travel + speed);
                if (travel >= 1f) phase = Phase.Delivering;
                break;
            case Phase.Delivering:
                if (target != null && target.CanAccept(held))
                {
                    target.Accept(held);
                    held = null;
                    phase = Phase.Returning;
                }
                break;
            case Phase.Returning:
                travel = Mathf.Max(0f, travel - speed);
                if (travel <= 0f) phase = Phase.Waiting;
                break;
        }
    }

    // --- Yön ayarı (boş elle sol tık: çıkış, Shift + sol tık: giriş) ---
    public void OnClicked(RaycastHit hit)
    {
        bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        if (shift) inputSide = NextSide(inputSide, outputSide);
        else outputSide = NextSide(outputSide, inputSide);
        PlaceMarkers();
    }

    private static int NextSide(int side, int other)
    {
        int next = (side + 1) % 4;
        return next == other ? (next + 1) % 4 : next;
    }

    // --- Görünüm ---
    private Transform CreateMarker(string name, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(marker.GetComponent<Collider>());
        marker.name = name;
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = new Vector3(0.3f, 0.06f, 0.12f);
        Renderer renderer = marker.GetComponent<Renderer>();
        renderer.material.color = color;
        if (renderer.material.HasProperty("_BaseColor")) renderer.material.SetColor("_BaseColor", color);
        return marker.transform;
    }

    // İşaretler kolun hücre kenarında, ilgili yönde
    private void PlaceMarkers()
    {
        Place(inputMarker, inputSide);
        Place(outputMarker, outputSide);
    }

    private static void Place(Transform marker, int side)
    {
        if (marker == null) return;
        Vector3 direction = LocalSides[side];
        marker.localPosition = direction * 0.42f + Vector3.up * -0.46f;
        marker.localRotation = Quaternion.LookRotation(direction); // uzun kenarı hücre kenarı boyunca
    }

    private void Update()
    {
        // Kol adımlar arasında yumuşakça girişten çıkışa döner
        shownTravel = Mathf.MoveTowards(shownTravel, travel, Time.deltaTime / Mathf.Max(swingSeconds, 0.01f));
        if (pivot != null)
            pivot.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(inputSide * 90f, outputSide * 90f, shownTravel), 0f);
        if (heldCube != null)
        {
            heldCube.enabled = held != null;
            if (held != null) heldCube.sharedMaterial = Machine.MaterialFor(held, heldCube.sharedMaterial);
        }
    }

    // --- ISaveState: yönler ---
    public string CaptureState() => $"{inputSide},{outputSide}";

    public void RestoreState(string state)
    {
        string[] parts = state.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out int input) && int.TryParse(parts[1], out int output)
            && input is >= 0 and <= 3 && output is >= 0 and <= 3 && input != output)
        {
            inputSide = input;
            outputSide = output;
            PlaceMarkers();
        }
    }
}
