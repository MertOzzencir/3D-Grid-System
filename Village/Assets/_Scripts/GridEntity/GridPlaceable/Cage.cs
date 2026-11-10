using UnityEngine;

// İnsan kafesi: taşınabilir (MovablePlaceable) ve içinde tek bir insan tutar (kullanıcı 2026-11-02: başkası sığmaz).
// İnsan taşınırken kafesin üstüne bırakılınca içine girer (Slave.EnterCage): kafesin child'ı olur, kafesle birlikte
// taşınır ve döner; grid'de ayrı hücre kaplamaz (kafesin hücreleri zaten dolu). Kafese sol tıklayınca içindeki çıkıp
// ele gelir (eldivenin altındaki collider kafesinki de olabilir, insanınki de: ikisi de çıkarır).
// Kayıt: içinde insan var mı (ISaveState); yüklemede occupantData'dan yaratılıp içine konur.
public class Cage : MovablePlaceable, ISaveState, IClickable
{
    [Tooltip("Kayıttan yüklerken kafesin içine yaratılacak insanın verisi (Slave-Placeable Data)")]
    [SerializeField] private GridEntitySOBase occupantData;
    [Tooltip("Kafesin tabanının (ahşap zemin) hücre tabanından yüksekliği (birim): içerideki insan bunun üstünde durur")]
    [SerializeField] private float floorHeight = 0.23f;

    private bool restoreOccupant;

    public Slave Occupant { get; private set; }
    public bool IsEmpty => Occupant == null;

    // İçerinin ortası, kafesin yerel uzayında: ayak izinin ortası, kafesin tabanı kadar yukarıda (insanın kökü; ayakları
    // kökün 0.5 altında)
    public Vector3 LocalCenter
    {
        get
        {
            Vector3 c = GetData() != null ? GetData().GetFootprint().Center : Vector3.zero;
            return new Vector3(c.x, floorHeight, c.z);
        }
    }

    public void SetOccupant(Slave slave) => Occupant = slave;

    // Sol tık (elde bir şey yokken): içindeki insan çıkıp ele gelir
    public void OnClicked(RaycastHit hit)
    {
        if (Occupant != null) Occupant.TakeOutOfCage();
    }

    public override void OnPlaced(Vector3Int origin)
    {
        base.OnPlaced(origin);
        if (!restoreOccupant) return;
        restoreOccupant = false;
        if (occupantData != null && occupantData.Spawn() is Slave slave) slave.EnterCage(this, instant: true);
    }

    // --- ISaveState ---
    public string CaptureState() => Occupant != null ? "occupied" : "";
    public void RestoreState(string state) => restoreOccupant = state == "occupied";
}
