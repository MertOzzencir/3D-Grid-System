using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ekin görselinin küçük animasyonları (prosedürel, fizik yok). FarmPiece ekinin görseline ekler.
// - Dokunma: eldiven üstünden geçince bitki tabanından hafifçe eğilir, meyveler sapından (pivot'u tepede) sarkaç gibi
//   sallanır. Yaylı: geri gelip durulur, durunca bileşen kendini kapatır (dururken maliyeti yok).
// - Hasat: meyveler kopup sağa sola savrulur (yay çizip düşer, döner, küçülüp kaybolur), gövde toprağa çekilir.
// Meyveler: modelin içinde adı CropSO.fruitName olan ya da "fruitName.001" gibi devam eden objeler. Yoksa sadece
// bitki eğilir; hasatta FarmPiece eski zıplayıp kaybolma efektini kullanır.
public class CropSway : MonoBehaviour
{
    private const float PlantStiffness = 70f, PlantDamping = 9f, PlantMaxAngle = 10f;
    private const float FruitStiffness = 28f, FruitDamping = 3.5f, FruitMaxAngle = 40f;
    private const float RestEpsilon = 0.05f;

    private struct Spring
    {
        public Transform transform;
        public Quaternion rest;
        public Vector2 angle;     // derece: parent'ın X ve Z eksenleri etrafında
        public Vector2 velocity;
        public float response;    // dokunmaya tepki çarpanı (meyveler biraz farklı sallansın)
    }

    private Spring plant;
    private readonly List<Spring> fruits = new List<Spring>();
    private CropSO crop;
    private bool scattering;

    public bool HasFruits => fruits.Count > 0;

    public void Setup(CropSO cropData)
    {
        crop = cropData;
        plant = new Spring { transform = transform, rest = transform.localRotation, response = 1f };
        string prefix = cropData != null ? cropData.fruitName : null;
        if (!string.IsNullOrEmpty(prefix))
            foreach (Transform child in GetComponentsInChildren<Transform>())
                if (child != transform && (child.name == prefix || child.name.StartsWith(prefix + ".")))
                    fruits.Add(new Spring { transform = child, rest = child.localRotation, response = Random.Range(0.8f, 1.25f) });
        enabled = false; // dokunulana kadar uyur
    }

    // Eldiven dokundu: gidiş yönüne doğru it (strength: 0..1, mesafeyle azalan)
    public void Poke(Vector3 worldVelocity, float strength)
    {
        if (scattering || crop == null) return;
        Vector3 flat = Vector3.ProjectOnPlane(worldVelocity, Vector3.up);
        float speed = Mathf.Min(flat.magnitude, 4f);
        if (speed < 0.05f) return;
        // Gidiş yönüne eğilme = yukarı × yön ekseni etrafında dönüş
        Vector3 axis = Vector3.Cross(Vector3.up, flat / Mathf.Max(flat.magnitude, 0.0001f));
        float impulse = speed * strength;
        Push(ref plant, axis, impulse * crop.touchSway * 40f);
        for (int i = 0; i < fruits.Count; i++)
        {
            Spring s = fruits[i];
            Push(ref s, axis, impulse * crop.fruitSwing * 120f * s.response);
            fruits[i] = s;
        }
        enabled = true;
    }

    private static void Push(ref Spring s, Vector3 worldAxis, float amount)
    {
        Transform parent = s.transform.parent;
        Vector3 local = parent != null ? parent.InverseTransformDirection(worldAxis) : worldAxis;
        s.velocity += new Vector2(local.x, local.z) * amount;
    }

    private void Update()
    {
        if (scattering) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        bool moving = Step(ref plant, PlantStiffness, PlantDamping, PlantMaxAngle, dt);
        for (int i = 0; i < fruits.Count; i++)
        {
            Spring s = fruits[i];
            if (s.transform == null) continue;
            moving |= Step(ref s, FruitStiffness, FruitDamping, FruitMaxAngle, dt);
            fruits[i] = s;
        }
        if (!moving) enabled = false;
    }

    // Yaylı açı; sonucu yazar. Hâlâ hareketliyse true
    private static bool Step(ref Spring s, float stiffness, float damping, float maxAngle, float dt)
    {
        s.velocity += (-stiffness * s.angle - damping * s.velocity) * dt;
        s.angle += s.velocity * dt;
        s.angle = Vector2.ClampMagnitude(s.angle, maxAngle);
        bool moving = s.angle.sqrMagnitude > RestEpsilon * RestEpsilon || s.velocity.sqrMagnitude > RestEpsilon;
        if (!moving)
        {
            s.angle = Vector2.zero;
            s.velocity = Vector2.zero;
        }
        s.transform.localRotation = Quaternion.Euler(s.angle.x, 0f, s.angle.y) * s.rest;
        return moving;
    }

    // Hasat: meyveler savrulur, gövde toprağa çekilir, sonra hepsi yok olur. Görsel parçadan ayrılmış olmalı
    // (parça taşınsa da efekt yerinde kalsın).
    public void Scatter()
    {
        if (scattering) return;
        scattering = true;
        enabled = true;
        StartCoroutine(ScatterRoutine());
    }

    private IEnumerator ScatterRoutine()
    {
        float force = crop != null ? crop.scatterForce : 1f;
        var flying = new List<(Transform t, Vector3 velocity, Vector3 spin, Vector3 scale)>();
        float baseAngle = Random.Range(0f, 360f);
        for (int i = 0; i < fruits.Count; i++)
        {
            Transform fruit = fruits[i].transform;
            if (fruit == null) continue;
            fruit.SetParent(null, true);
            // Etrafa eşit dağılsın (rastgele sapmayla), hafif yukarı
            float angle = (baseAngle + 360f * i / fruits.Count + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 velocity = (outward * Random.Range(0.9f, 1.4f) + Vector3.up * Random.Range(1.6f, 2.2f)) * force;
            Vector3 spin = Random.onUnitSphere * Random.Range(360f, 720f);
            flying.Add((fruit, velocity, spin, fruit.localScale));
        }

        const float duration = 0.6f, shrinkFrom = 0.4f, gravity = 7f;
        Vector3 plantScale = transform.localScale;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            float k = t / duration;
            for (int i = 0; i < flying.Count; i++)
            {
                var f = flying[i];
                if (f.t == null) continue;
                f.velocity += Vector3.down * gravity * dt;
                f.t.position += f.velocity * dt;
                f.t.rotation = Quaternion.Euler(f.spin * dt) * f.t.rotation;
                float shrink = k < shrinkFrom ? 1f : 1f - (k - shrinkFrom) / (1f - shrinkFrom);
                f.t.localScale = f.scale * shrink;
                flying[i] = f;
            }
            // Gövde: önce hafif sıçrar, sonra toprağa çekilir
            float plantK = k < 0.15f ? 1f + k : Mathf.Lerp(1.15f, 0f, (k - 0.15f) / 0.85f);
            transform.localScale = new Vector3(plantScale.x * plantK, plantScale.y * plantK * plantK, plantScale.z * plantK);
            yield return null;
        }

        foreach (var f in flying)
            if (f.t != null) Destroy(f.t.gameObject);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Savrulurken görsel erken silinirse ayrılmış meyveler kalmasın
        if (!scattering) return;
        foreach (Spring s in fruits)
            if (s.transform != null && s.transform.parent == null) Destroy(s.transform.gameObject);
    }
}
