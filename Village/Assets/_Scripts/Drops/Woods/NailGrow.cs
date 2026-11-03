using System;
using System.Collections.Generic;
using UnityEngine;

// Çivilerin çakılma animasyonu (WoodNail kurar, MergedWood birleştirme anında Play'i çağırır). Sadece yeni çiviler oynar,
// sırayla: çivi gövdesinin gerisinden hızlanarak gelip yerine çakılır, kafası bir an şişip oturur. Bittiğinde son hali
// yazılıp bileşen kapanır; oynatılmazsa çiviler hazır görünür.
public class NailGrow : MonoBehaviour
{
    [Serializable]
    public struct Settings
    {
        [Tooltip("Birleştirmeden sonra bekleme (saniye)")] public float delay;
        [Tooltip("Çiviler arası (saniye)")] public float stagger;
        [Tooltip("Çivinin geldiği uzaklık (birim, çakılma yönünün tersinden)")] public float rise;
        [Tooltip("Çakılma süresi (saniye)")] public float duration;
        [Tooltip("Çakılınca bir an şişme oranı")] public float punch;
    }

    private const float PunchDuration = 0.15f;

    private class Entry
    {
        public Transform Nail;
        public Vector3 Drive;
        public Vector3 Position;
        public Vector3 Scale;
        public float Start;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly List<Entry> playing = new List<Entry>();
    private Settings settings;
    private float time;

    public void Add(Transform nail, Vector3 drive)
        => entries.Add(new Entry { Nail = nail, Drive = drive, Position = nail.localPosition, Scale = nail.localScale });

    // Silinen çivi (ek yeri artık yok)
    public void Remove(Transform nail)
    {
        entries.RemoveAll(e => e.Nail == nail);
        playing.RemoveAll(e => e.Nail == nail);
    }

    // nails: oynayacak çiviler; diğerleri yerinde durur
    public void Play(List<Transform> nails, Settings settings)
    {
        this.settings = settings;
        playing.Clear();
        foreach (Entry entry in entries)
        {
            if (!nails.Contains(entry.Nail)) continue;
            entry.Start = settings.delay + playing.Count * settings.stagger;
            playing.Add(entry);
        }
        time = 0f;
        enabled = playing.Count > 0;
        if (enabled) Apply();
    }

    private void Update()
    {
        time += Time.deltaTime;
        if (!Apply()) enabled = false;
    }

    // Oynayanları şu anki zamana göre yerleştirir; hâlâ oynayan varsa true
    private bool Apply()
    {
        bool running = false;
        float end = settings.duration + PunchDuration;
        foreach (Entry entry in playing)
        {
            if (entry.Nail == null) continue;
            float t = time - entry.Start;
            if (t < end) running = true;
            if (t < 0f)
            {
                entry.Nail.localScale = Vector3.zero;
                continue;
            }
            float u = Mathf.Clamp01(t / Mathf.Max(settings.duration, 0.001f));
            entry.Nail.localPosition = entry.Position - entry.Drive * (settings.rise * (1f - u * u)); // hızlanarak
            float k = Mathf.Clamp01((t - settings.duration) / PunchDuration);
            float bump = u < 1f ? 0f : settings.punch * Mathf.Sin(k * Mathf.PI) * (1f - k);
            entry.Nail.localScale = entry.Scale * (1f + bump);
        }

        if (!running)
            foreach (Entry entry in playing)
            {
                if (entry.Nail == null) continue;
                entry.Nail.localPosition = entry.Position;
                entry.Nail.localScale = entry.Scale;
            }
        return running;
    }
}
