using UnityEngine;

// Bir alet tutma noktasının (GloveGrip) kendine özel ayarları. Noktanın transform'u avucun yerini ve yönünü,
// bu bileşen de o noktada parmakların ne kadar kapanacağını belirler. Yoksa aletin genel değeri kullanılır.
public class GloveGripPoint : MonoBehaviour
{
    [Tooltip("Bu noktada sapı kavrarken parmakların kıvrılması (0 = düz, 1 = tam kıvrık)")]
    [Range(0f, 1f)] public float curl = 0.7f;
}
