using UnityEngine;

// Kenar ışığı: kameranın karşısından (objelerin arkasından kameraya doğru) vuran sıcak, gölgesiz ikinci ışık. Kil
// modellerin silüetinde ince parlak bir şerit yapar, arka plandan ayırır. Her kare kameranın yönüne göre döner
// (Q/E ile kamera dönünce de arkadan gelir). Directional Light'ın üstünde durur; ana güneş değildir
// (RenderSettings.sun güneş olmalı). Su shader'ı sadece ana ışığı kullandığı için suda parlama yapmaz.
[RequireComponent(typeof(Light))]
public class RimLight : MonoBehaviour
{
    [Tooltip("Işığın yataydan aşağı eğimi (derece): küçükse silüet şeridi incelir ve yanlara kayar")]
    [SerializeField, Range(0f, 80f)] private float elevation = 25f;

    private Light rimLight;
    private Camera cam;

    private void Awake()
    {
        rimLight = GetComponent<Light>();
        rimLight.type = LightType.Directional;
        rimLight.shadows = LightShadows.None;
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) return;
        forward.Normalize();
        float e = elevation * Mathf.Deg2Rad;
        // Işığın gidiş yönü: kameraya doğru (−ileri) ve aşağı
        Vector3 direction = -forward * Mathf.Cos(e) + Vector3.down * Mathf.Sin(e);
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }
}
