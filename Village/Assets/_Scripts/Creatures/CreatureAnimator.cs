using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Animasyon köprüsü: durumlar klipleri isimle ister ("Idle", "HeadPat"...). Klipler Animator Controller gerektirmeden
// Playables ile doğrudan oynatılır (iki girişli mixer: eski klipten yenisine kısa geçiş).
// Klip adı FBX'teki gibi "Armature|Idle" olsa da "Idle" ile eşleşir. Listede olmayan bir isim istenirse sessizce
// yok sayılır: eksik animasyon (ya da hiç model yokken yer tutucu) oyunu bozmaz.
// Döngü klipleri burada döngülenir; içe aktarmada Loop Time açmaya gerek yok.
public class CreatureAnimator : MonoBehaviour
{
    [Serializable]
    private class Entry
    {
        public AnimationClip clip;
        [Tooltip("Bitince başa sarsın (Idle gibi); kapalıysa son karede kalır (tepki animasyonları)")]
        public bool loop = true;
    }

    [SerializeField] private Animator animator;
    [SerializeField] private Entry[] clips = new Entry[0];
    [Tooltip("Klipler arası geçiş süresi (saniye)")]
    [SerializeField] private float crossFade = 0.2f;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable currentPlayable, previousPlayable;
    private Entry currentEntry, previousEntry;
    private float fadeProgress = 1f;
    private bool ready;

    public string Current { get; private set; }

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    // Model sonradan kurulursa (Cat modeli kodla yerleştirir) onun Animator'ı buradan verilir
    public void Setup(Animator target)
    {
        animator = target;
        BuildGraph();
    }

#if UNITY_EDITOR
    // Klipler FBX'in alt asset'leri; çalışma anında okunamaz, editörde listeye yazılır (Cat'in menü komutu)
    public void EditorSetClips(AnimationClip[] modelClips, Func<string, bool> isLoop)
    {
        clips = new Entry[modelClips.Length];
        for (int i = 0; i < modelClips.Length; i++)
            clips[i] = new Entry { clip = modelClips[i], loop = isLoop(ShortName(modelClips[i].name)) };
    }
#endif

    private void Start()
    {
        if (!ready && animator != null) BuildGraph();
    }

    private void BuildGraph()
    {
        if (ready || animator == null) return;
        animator.runtimeAnimatorController = null; // controller'ı biz sürüyoruz
        graph = PlayableGraph.Create($"{name} Creature Animator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        AnimationPlayableOutput.Create(graph, "Animation", animator).SetSourcePlayable(mixer);
        graph.Play();
        ready = true;

        if (!string.IsNullOrEmpty(Current))
        {
            string pending = Current;
            Current = null;
            Play(pending);
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    // "Armature|Idle" → "Idle"
    private static string ShortName(string clipName)
    {
        int bar = clipName.LastIndexOf('|');
        return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
    }

    private Entry Find(string stateName)
    {
        foreach (Entry entry in clips)
            if (entry.clip != null && ShortName(entry.clip.name) == stateName) return entry;
        return null;
    }

    public bool Has(string stateName) => Find(stateName) != null;

    public void Play(string stateName, bool restart = false)
    {
        if (!restart && stateName == Current) return;
        Current = stateName;
        if (!ready) return;

        Entry entry = Find(stateName);
        if (entry == null) return;

        // Eski klip ikinci girişe, yenisi birinciye; ağırlık fadeProgress ile kayar
        if (previousPlayable.IsValid()) { mixer.DisconnectInput(1); previousPlayable.Destroy(); }
        if (currentPlayable.IsValid())
        {
            mixer.DisconnectInput(0);
            previousPlayable = currentPlayable;
            previousEntry = currentEntry;
            graph.Connect(previousPlayable, 0, mixer, 1);
        }

        currentPlayable = AnimationClipPlayable.Create(graph, entry.clip);
        currentPlayable.SetApplyFootIK(false);
        currentEntry = entry;
        graph.Connect(currentPlayable, 0, mixer, 0);
        fadeProgress = previousPlayable.IsValid() ? 0f : 1f;
        ApplyWeights();
    }

    // Geriye uyumluluk: yürüme artık prosedürel, animasyon hızı değiştirilmiyor
    public void SetMoveSpeed(float speed) { }

    private void Update()
    {
        if (!ready) return;

        if (fadeProgress < 1f)
        {
            fadeProgress = Mathf.Min(1f, fadeProgress + Time.deltaTime / Mathf.Max(crossFade, 0.0001f));
            ApplyWeights();
            if (fadeProgress >= 1f && previousPlayable.IsValid())
            {
                mixer.DisconnectInput(1);
                previousPlayable.Destroy();
            }
        }

        KeepLooping(currentPlayable, currentEntry);
        KeepLooping(previousPlayable, previousEntry);
    }

    private void ApplyWeights()
    {
        mixer.SetInputWeight(0, fadeProgress);
        mixer.SetInputWeight(1, 1f - fadeProgress);
    }

    // Döngü: süre klibi geçince başa sar; döngü değilse son karede tut
    private static void KeepLooping(AnimationClipPlayable playable, Entry entry)
    {
        if (!playable.IsValid() || entry == null) return;
        double length = entry.clip.length;
        double time = playable.GetTime();
        if (time < length) return;
        playable.SetTime(entry.loop && length > 0 ? time % length : length);
    }
}
