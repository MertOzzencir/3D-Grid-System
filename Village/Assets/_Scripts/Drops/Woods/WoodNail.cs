using System;
using System.Collections.Generic;
using UnityEngine;

// Kol ile ondan çıkan derinlik odunu arasındaki ek yerine çakılan çivi (kullanıcı 2026-11-03; her ek yerine kalas / çivi
// kaotikti, sal bağı bantları + sadece bu eklere çivi). Her ek yerinde yan yana iki çivi (ek yeri çizgisi boyunca),
// eğik çakılır (varsayılan 20°): kafası koldaki odunun yüzünde, gövdesi
// ek yerinin altından derinlik oduna doğru (iki odunu birbirine tutturmuş gibi). Ek yerinin açıkta kalan bir yüzünde (önce
// üst, yoksa yapının dışına en çok bakan yan; hiçbiri açık değilse çivi yok); hangi ek yeri: MergedWood.BuildCells.
// Ayarları WoodMerger'da; çiviler ek yeri ek yeri kurulur (değişmeyen ek yerinin çivisi yerinde kalır). Birleştirme anında sadece yeni çiviler çakılarak gelir (NailGrow). Model şimdilik prototip
// (silindirler); kullanıcı sonra modelleyecek: prefab atanırsa o kullanılır.
[Serializable]
public class WoodNail
{
    // Bir ek yeri: iki hücrenin ortası, çivinin çakıldığı yüzün dışa yönü, kafanın olduğu taraftan öbür parçaya yön.
    // Hepsi MergedWood'un yerel uzayında.
    public struct Joint
    {
        public Vector3 Center;
        public Vector3Int Normal;
        public Vector3Int Along;
    }

    [Tooltip("Açıksa kol ile derinlik odunu arasındaki ek yerlerine çivi çakılır")]
    [SerializeField] private bool enabled = true;
    [Tooltip("İsteğe bağlı çivi modeli. Pivot çivi kafasının ortasında; +Z gövde boyunca (çakılma yönü). Boşsa prototip silindirler.")]
    [SerializeField] private GameObject nailPrefab;
    [Tooltip("Çivinin açısı (derece): yüzeyle gövde arası. 90 = dik çakılır, 0 = yüzeye yatık")]
    [SerializeField, Range(0f, 90f)] private float angle = 20f;
    [Tooltip("Ek yeri başına çivi sayısı (ek yeri çizgisi boyunca yan yana)")]
    [SerializeField, Range(1, 4)] private int count = 2;
    [Tooltip("Yan yana çivilerin arası (birim)")]
    [SerializeField] private float spacing = 0.35f;
    [Tooltip("Kafanın ek yerinden uzaklığı (birim, koldaki odunun üstünde)")]
    [SerializeField] private float headOffset = 0.1f;
    [Tooltip("Kafanın yüzeye oturduğu yer: hücre sınırından bu kadar (birim) dışarı (odunun hücreden taşan kısmı)")]
    [SerializeField] private float surface = 0.03f;
    [Tooltip("Prototip: gövdenin boyu ve yarıçapı, kafanın yarıçapı ve kalınlığı (birim)")]
    [SerializeField] private float length = 0.3f;
    [SerializeField] private float radius = 0.02f;
    [SerializeField] private float headRadius = 0.045f;
    [SerializeField] private float headHeight = 0.02f;
    [Tooltip("İsteğe bağlı malzeme ('Village/Wood Wobble Lit' olmalı ki odunla sallansın). Boşsa odunun malzemesinin " +
             "dokusuz kopyası çivi rengiyle.")]
    [SerializeField] private Material material;
    [SerializeField] private Color color = new Color(0.32f, 0.32f, 0.35f);

    [Header("Çakılma animasyonu (birleştirme anında, sadece yeni çiviler)")]
    [SerializeField] private NailGrow.Settings animation = new NailGrow.Settings
    {
        delay = 0.15f,
        stagger = 0.08f,
        rise = 0.3f,
        duration = 0.09f,
        punch = 0.4f,
    };

    private Material generated;
    private Material generatedFrom;

    public NailGrow.Settings Animation => animation;
    public int Count => count;

    public bool Enabled => enabled;

    // Çivilerin kökü ("Nails" + NailGrow), MergedWood bir kez kurar; çiviler BuildJoint ile ek yeri ek yeri eklenir
    // (kullanıcı: her birleştirmede hepsini baştan kurmak spike yapıyordu)
    public NailGrow CreateRoot(Transform parent)
    {
        var root = new GameObject("Nails");
        root.transform.SetParent(parent, false);
        var grow = root.AddComponent<NailGrow>();
        grow.enabled = false;
        return grow;
    }

    // Bir ek yerinin çivileri (count tane), grow'un objesinin altında; eklenen çivileri döndürür
    public Transform[] BuildJoint(NailGrow grow, Joint joint, Material woodMaterial)
    {
        Material nailMaterial = Material(woodMaterial);
        Transform root = grow.transform;
        Vector3 normal = joint.Normal, along = joint.Along;
        // Kafa koldaki odunun yüzünde, ek yerinden headOffset geride; gövde içeri ve ek yerine doğru eğik
        Vector3 lateral = Vector3.Cross(normal, along); // ek yeri çizgisi boyunca
        float radians = angle * Mathf.Deg2Rad;
        Vector3 drive = (along * Mathf.Cos(radians) - normal * Mathf.Sin(radians)).normalized;
        Quaternion rotation = Quaternion.LookRotation(drive, angle < 89f ? normal : along); // dikken normal gövdeye paralel

        var made = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 head = joint.Center + normal * (0.5f + surface) - along * headOffset
                         + lateral * ((i - (count - 1) * 0.5f) * spacing);
            Transform nail;
            if (nailPrefab != null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(nailPrefab);
                StripColliders(instance);
                nail = instance.transform;
                nail.name = "Nail";
                nail.SetParent(root, false);
            }
            else
            {
                nail = new GameObject("Nail").transform;
                nail.SetParent(root, false);
                // Silindirin ekseni y, 2 birim boy: 90° yatırılınca ekseni z (gövde boyunca)
                Quaternion lying = Quaternion.Euler(90f, 0f, 0f);
                Primitive("Shank", nail, new Vector3(0f, 0f, length * 0.5f), lying,
                          new Vector3(radius * 2f, length * 0.5f, radius * 2f), nailMaterial);
                Primitive("Head", nail, Vector3.zero, lying,
                          new Vector3(headRadius * 2f, headHeight * 0.5f, headRadius * 2f), nailMaterial);
            }
            nail.localPosition = head;
            nail.localRotation = rotation;
            grow.Add(nail, drive);
            made[i] = nail;
        }
        return made;
    }

    private static void Primitive(string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
    {
        GameObject made = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        made.name = name;
        StripColliders(made);
        if (material != null) made.GetComponent<Renderer>().sharedMaterial = material;
        Transform transform = made.transform;
        transform.SetParent(parent, false);
        transform.localPosition = position;
        transform.localRotation = rotation;
        transform.localScale = scale;
    }

    // Collider olmasın: tıklama / tutma ışınları odunun kendi collider'larına gitsin
    private static void StripColliders(GameObject made)
    {
        foreach (Collider collider in made.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
    }

    private Material Material(Material woodMaterial)
    {
        if (material != null) return material;
        if (woodMaterial == null) return null;
        if (generated == null || generatedFrom != woodMaterial)
        {
            generated = new Material(woodMaterial) { name = "Nail (generated)" };
            generated.SetTexture("_BaseMap", null);
            generated.SetTexture("_BumpMap", null);
            generated.DisableKeyword("_NORMALMAP");
            generated.SetColor("_BaseColor", color);
            generatedFrom = woodMaterial;
        }
        return generated;
    }
}
