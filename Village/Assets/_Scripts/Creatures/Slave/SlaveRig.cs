using System;
using UnityEngine;

// İnsanın prosedürel yürüme animasyonu: klip yok, kemikler koddan döner. Temel duruş modelin kendi pozudur (kambur
// yürüme pozu, Blender'da verildiği gibi): oyun başında her kemiğin yerel dönüşü saklanır, hareket onun üstüne eklenir.
// Kemikler isimle bulunur (büyük/küçük harf duyarsız): Root, Middle-Chest, Neck, Head, UpperArm/BottomArm.L/R,
// UpperLeg/BottomLeg/FootBottom.L/R. Bulunamayan kemik atlanır (oyun bozulmaz).
// Dönüş eksenleri kemiğin kendi eksenlerinden değil gövdeden alınır (sağ, ileri, yukarı): Blender'daki kemik eksenleri
// ne olursa olsun bacak öne / arkaya sallanır. Yürürken: bacaklar ve kollar zıt sallanır, diz öne giden bacakta bükülür,
// gövde iki yana yalpalar ve hafif zıplar, göğüs bacaklara ters döner. Dururken: nefes alıp verir.
public class SlaveRig : MonoBehaviour
{
    [Serializable]
    private class WalkSettings
    {
        [Tooltip("Bir adımın boyu (birim): yürüme döngüsünün hıza göre temposu")]
        public float strideLength = 0.4f;
        [Tooltip("Bacakların öne / arkaya salınımı (derece)")]
        public float legSwing = 26f;
        [Tooltip("Öne giden bacakta diz bükülmesi (derece)")]
        public float kneeBend = 35f;
        [Tooltip("Kolların salınımı (derece, bacaklara zıt)")]
        public float armSwing = 18f;
        [Tooltip("Ön kolun ek salınımı (derece)")]
        public float forearmSwing = 10f;
        [Tooltip("Gövdenin iki yana yalpalaması (derece)")]
        public float sway = 5f;
        [Tooltip("Göğsün bacaklara ters dönmesi (derece)")]
        public float chestTwist = 6f;
        [Tooltip("Adım başına zıplama (birim)")]
        public float bob = 0.04f;
        [Tooltip("Kafanın adımla sallanması (derece)")]
        public float headNod = 4f;
        [Tooltip("Yürümeye başlama / durma yumuşaklığı")]
        public float blendSharpness = 6f;
    }

    [Serializable]
    private class IdleSettings
    {
        [Tooltip("Nefes hızı (saniyedeki nefes)")]
        public float breathRate = 0.3f;
        [Tooltip("Nefeste göğsün öne / arkaya oynaması (derece)")]
        public float breathChest = 2.5f;
        [Tooltip("Nefeste kafanın oynaması (derece)")]
        public float breathHead = 2f;
    }

    [Tooltip("Modelin kökü (Model child'ı); boşsa ilk SkinnedMeshRenderer'ın modelinden bulunur")]
    [SerializeField] private Transform model;
    [SerializeField] private WalkSettings walk = new WalkSettings();
    [SerializeField] private IdleSettings idle = new IdleSettings();

    private class Bone
    {
        public Transform transform;
        public Quaternion baseRotation;
    }

    private Bone root, chest, neck, head, upperArmL, upperArmR, lowerArmL, lowerArmR, upperLegL, upperLegR, lowerLegL, lowerLegR;
    private Vector3 modelBasePosition;
    private float phase;     // yürüme döngüsü (radyan)
    private float walkBlend; // 0 duruyor, 1 yürüyor
    private float breathTime;
    private bool ready;

    private void Start()
    {
        if (model == null)
        {
            var skinned = GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null) model = skinned.transform.parent;
        }
        if (model == null) return;

        modelBasePosition = model.localPosition;
        root = Find("Root");
        chest = Find("Middle-Chest");
        neck = Find("Neck");
        head = Find("Head");
        upperArmL = Find("UpperArm.L");
        upperArmR = Find("UpperArm.R");
        lowerArmL = Find("BottomArm.L");
        lowerArmR = Find("BottomArm.R");
        upperLegL = Find("UpperLeg.L");
        upperLegR = Find("UpperLeg.R");
        lowerLegL = Find("BottomLeg.L");
        lowerLegR = Find("BottomLeg.R");
        ready = true;
    }

    private Bone Find(string boneName)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, boneName, StringComparison.OrdinalIgnoreCase))
                return new Bone { transform = t, baseRotation = t.localRotation };
        return null;
    }

    // Slave her kare çağırır: speed o anki yürüme hızı (dururken 0)
    public void Tick(float speed)
    {
        if (!ready) return;

        walkBlend = Mathf.Lerp(walkBlend, speed > 0.01f ? 1f : 0f, 1f - Mathf.Exp(-walk.blendSharpness * Time.deltaTime));
        phase += speed / Mathf.Max(walk.strideLength, 0.01f) * Mathf.PI * Time.deltaTime;
        breathTime += Time.deltaTime;
    }

    // Kemikler Animator'dan sonra yazılsın diye LateUpdate'te
    private void LateUpdate()
    {
        if (!ready) return;

        Vector3 right = transform.right, forward = transform.forward, up = transform.up;
        float w = walkBlend;
        float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
        float breath = Mathf.Sin(breathTime * idle.breathRate * Mathf.PI * 2f) * (1f - w);

        // Bacaklar: açı + → geriye (sağ ekseni etrafında). Sol bacak öne giderken (cos > 0) dizi bükülür
        float legL = -s * walk.legSwing * w;
        Rotate(upperLegL, right, legL);
        Rotate(upperLegR, right, -legL);
        Rotate(lowerLegL, right, Mathf.Max(0f, c) * walk.kneeBend * w);
        Rotate(lowerLegR, right, Mathf.Max(0f, -c) * walk.kneeBend * w);

        // Kollar bacaklara zıt
        Rotate(upperArmL, right, -legL / Mathf.Max(walk.legSwing, 0.01f) * walk.armSwing);
        Rotate(upperArmR, right, legL / Mathf.Max(walk.legSwing, 0.01f) * walk.armSwing);
        Rotate(lowerArmL, right, -Mathf.Max(0f, s) * walk.forearmSwing * w);
        Rotate(lowerArmR, right, -Mathf.Max(0f, -s) * walk.forearmSwing * w);

        // Gövde: yana yalpalama, göğüs ters dönüş, nefes
        Rotate(root, forward, s * walk.sway * w);
        Rotate(chest, up, -s * walk.chestTwist * w, right, breath * idle.breathChest);
        Rotate(neck, up, s * walk.chestTwist * 0.5f * w);
        Rotate(head, right, Mathf.Cos(phase * 2f) * walk.headNod * w + breath * idle.breathHead);

        // Adım ortasında (bacaklar dikken) en yüksek
        model.localPosition = modelBasePosition + Vector3.up * (walk.bob * w * (0.5f + 0.5f * Mathf.Cos(phase * 2f)));
    }

    // Temel duruşun üstüne, dünya eksenleri etrafında (kemiğin ebeveyninin uzayına çevrilip) döndürür
    private static void Rotate(Bone bone, Vector3 axis, float degrees, Vector3 axis2 = default, float degrees2 = 0f)
    {
        if (bone == null) return;
        Transform parent = bone.transform.parent;
        Quaternion offset = Quaternion.AngleAxis(degrees, parent != null ? parent.InverseTransformDirection(axis) : axis);
        if (degrees2 != 0f)
            offset = Quaternion.AngleAxis(degrees2, parent != null ? parent.InverseTransformDirection(axis2) : axis2) * offset;
        bone.transform.localRotation = offset * bone.baseRotation;
    }
}
