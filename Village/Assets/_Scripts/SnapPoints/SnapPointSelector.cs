using System.Collections.Generic;
using UnityEngine;

// Bağlantı noktalarını ok göstergesiyle gösterir ve mouse'a en yakın olanı seçer.
// Hangi noktaların olduğunu sahibi hesaplar; bu sınıf sadece gösterme + seçme işini yapar.
public class SnapPointSelector
{
    private const float SelectedScale = 1.4f;

    private readonly GameObject indicatorPrefab;
    private readonly Transform parent;
    private readonly List<GameObject> indicators = new List<GameObject>();
    private readonly List<SnapPoint> points = new List<SnapPoint>();
    private int selectedIndex = -1;

    public bool HasSelection => selectedIndex >= 0;
    public SnapPoint Selected => points[selectedIndex];

    // indicatorPrefab null olabilir: o zaman ok gösterilmez ama seçim yine çalışır
    public SnapPointSelector(GameObject indicatorPrefab, Transform parent)
    {
        this.indicatorPrefab = indicatorPrefab;
        this.parent = parent;
    }

    // Noktaları günceller, ray'e (mouse) en yakın olanı seçer. Seçim varsa true döner.
    public bool UpdateSelection(IReadOnlyList<SnapPoint> newPoints, Ray ray)
    {
        points.Clear();
        for (int i = 0; i < newPoints.Count; i++)
            points.Add(newPoints[i]);

        selectedIndex = FindClosest(ray);
        RefreshIndicators();
        return HasSelection;
    }

    public void Hide()
    {
        points.Clear();
        selectedIndex = -1;
        foreach (GameObject indicator in indicators)
            if (indicator != null) indicator.SetActive(false);
    }

    private int FindClosest(Ray ray)
    {
        int closest = -1;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < points.Count; i++)
        {
            // Noktanın mouse ışınına dik uzaklığı
            float distance = Vector3.Cross(ray.direction, points[i].Position - ray.origin).magnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = i;
            }
        }
        return closest;
    }

    private void RefreshIndicators()
    {
        if (indicatorPrefab == null) return;

        while (indicators.Count < points.Count)
            indicators.Add(Object.Instantiate(indicatorPrefab, parent));

        Vector3 baseScale = indicatorPrefab.transform.localScale;
        for (int i = 0; i < indicators.Count; i++)
        {
            bool active = i < points.Count;
            indicators[i].SetActive(active);
            if (!active) continue;

            Transform t = indicators[i].transform;
            t.SetPositionAndRotation(points[i].Position, points[i].Rotation);
            t.localScale = baseScale * (i == selectedIndex ? SelectedScale : 1f);
        }
    }
}
