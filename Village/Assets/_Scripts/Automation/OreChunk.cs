using UnityEngine;

// Matkapla kazılan cevher topağı (kullanıcı: Astroneer gibi yere fiziksel parça düşer, elle makineye götürülür).
// Yığınlanır: aynı türden topaklar tek yığında toplanır (tutarken üstünden geçince ya da kazılan cevher yığının yanına
// düşünce). Elde tutarken bir makinenin üstüne gelip sol tıklayınca bir tanesi makinenin girişine girer (Machine
// IToolTarget). Bu, otomasyonun dışında yerde duran tek parça türü; az sayıda olduğu için performans sorunu değil.
public class OreChunk : Carryable
{
    [SerializeField] private ItemSO item;

    public ItemSO Item => item;
    protected override bool Stackable => true;

    protected override void BuildPlaceholder(Transform parent)
    {
        Color color = item != null ? item.Color : Color.gray;
        Primitive(PrimitiveType.Cube, parent, new Vector3(0f, -0.38f, 0f), new Vector3(0.26f, 0.22f, 0.24f), color,
                  Quaternion.Euler(0f, 20f, 8f));
        Primitive(PrimitiveType.Cube, parent, new Vector3(0.12f, -0.42f, 0.08f), new Vector3(0.14f, 0.13f, 0.15f), color,
                  Quaternion.Euler(10f, -30f, 0f));
    }

    // Makineye bir tane verir; yığın bitince topak yok olur (true)
    public bool GiveOne(IItemPort port)
    {
        if (item == null || port == null || !port.CanAccept(item)) return false;
        port.Accept(item);
        return UseOne();
    }
}
