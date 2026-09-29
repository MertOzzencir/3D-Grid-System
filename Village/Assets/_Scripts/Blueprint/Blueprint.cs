using System;
using System.Collections.Generic;
using UnityEngine;

// Blueprint Creator'ın ürettiği "boyama kitabı" objesi. Grid'de tüm slot'larının hücrelerini kaplar,
// böylece üstüne başka bir parça konamaz. Slot'lar child olarak durur ve oyuncu uyan parçaları getirerek doldurur
// (bkz. BlueprintSlot). Bütün slot'lar dolunca "tamamlandı" efekti oynar. Hangi slot'ların dolu olduğu kaydedilir.
public class Blueprint : GridPlaceable, ISaveState
{
    [Serializable]
    private class State
    {
        public List<int> filled = new List<int>(); // dolu slot'ların sırası (Slots dizisindeki)
    }

    // Tüm slot'ların kapladığı hücreler, blueprint pivot'una göre, döndürülmemiş
    [SerializeField] private List<Vector3Int> cells = new List<Vector3Int>();

    [Header("Görünüm")]
    [Tooltip("Boş slot: noktalı yarı saydam")]
    [SerializeField] private DitherFade emptyFade = new DitherFade(0.3f, 0.15f);
    [Tooltip("Uyan parça boş slot'un üstündeyken")]
    [SerializeField] private DitherFade hoverFade = new DitherFade(0.6f, 0.1f);
    [Tooltip("Elde bir şey yokken mouse boş slot'un üstündeyken (hangi parça gerekiyor, net görünsün)")]
    [SerializeField] private DitherFade previewFade = new DitherFade(0.9f, 0.12f);
    [Tooltip("Tek slot dolunca o parça sallanır")]
    [SerializeField] private JellyWobble fillWobble = new JellyWobble();
    [Tooltip("Blueprint tamamlanınca bütünü sallanır")]
    [SerializeField] private JellyWobble completeWobble = new JellyWobble();
    [Tooltip("Tamamlanınca ortada oynayan efekt (isteğe bağlı)")]
    [SerializeField] private ParticleSystem completeEffect;

    private GridFootprint[] footprints;
    private BlueprintSlot[] slots;

    public IReadOnlyList<BlueprintSlot> Slots => slots ??= GetComponentsInChildren<BlueprintSlot>(true);
    public DitherFade EmptyFade => emptyFade;
    public DitherFade HoverFade => hoverFade;
    public DitherFade PreviewFade => previewFade;

    public bool IsComplete
    {
        get
        {
            foreach (BlueprintSlot slot in Slots)
                if (!slot.IsFilled) return false;
            return Slots.Count > 0;
        }
    }

    public override GridFootprint GetFootprint()
    {
        footprints ??= GridMaskRotator.AllRotations(new GridFootprint(cells.ToArray()));
        return footprints[(int)Rotation];
    }

    public void OnSlotFilled(BlueprintSlot slot)
    {
        if (IsComplete) PlayComplete();
        else slot.PlayFillWobble(fillWobble);
    }

    private void PlayComplete()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        Bounds bounds = CombinedBounds(renderers);
        var basePoint = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        StartCoroutine(completeWobble.Play(renderers, basePoint, bounds.size.y, Vector3.zero));

        if (completeEffect != null)
            VfxPool.Play(completeEffect, bounds.center, Quaternion.identity);
    }

    public static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            if (any) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            any = true;
        }
        return bounds;
    }

    // --- ISaveState ---
    public string CaptureState()
    {
        var state = new State();
        for (int i = 0; i < Slots.Count; i++)
            if (Slots[i].IsFilled) state.filled.Add(i);
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string json)
    {
        State state = JsonUtility.FromJson<State>(json);
        var filled = new HashSet<int>(state.filled);
        for (int i = 0; i < Slots.Count; i++)
            Slots[i].SetFilledSilently(filled.Contains(i));
    }

#if UNITY_EDITOR
    public void EditorSetCells(IEnumerable<Vector3Int> newCells)
    {
        cells = new List<Vector3Int>(newCells);
        footprints = null;
    }
#endif
}
