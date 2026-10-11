using SunkCost.World;
using UnityEngine;

namespace SunkCost.Audio
{
    // The platform's own ambience (Dan, 8 October 2026, BRIEF B.7 "the silent spot"):
    // until now the only ambience at HQ was the docked ship's (ShipAmbience), heard
    // within 70 m of the ship's root at (-53, -18) and hard-stopped past it, so the
    // arrival pad, the pickup chute, the cargo apron, the crane and the plank were
    // silent. This bed is built into the generated HQ by HQAmbienceSetup: a 2D wind
    // over the whole platform and sea-wash sources round its edge at the waterline,
    // the same AudioLibrary clips and volumes the ship uses.
    //
    // Local presentation on every peer, like the ship's: it plays while this
    // machine's listener is in the HQ scene AND the crew is at HQ (the HQ scene
    // stays loaded on a host while the crew is at sea, and a 2D source is heard at
    // any distance, so without the second test a host at a dive site would hear the
    // HQ wind). While its wind plays it asks ShipAmbience to skip its own wind
    // (ShipAmbience.WindSuppressed), so the docked ship does not double the bed;
    // the ship's washes keep playing from where the hull is.
    //
    // LevelAt computes the bed's level at a point from the sources' settings (no
    // audio needed), so an editor probe can map the platform headless.
    public sealed class HQAmbience : MonoBehaviour
    {
        public const string RootName = "HQ Ambience";
        public const float AudibleLevel = 0.02f;     // the grid's threshold: a point below it is "dead"

        [SerializeField] private AudioSource wind;
        [SerializeField] private AudioSource[] washes = new AudioSource[0];
        [Tooltip("Each edge wash at this share of the library's sea-wash volume: eight of them add up round the platform.")]
        [Range(0f, 1f)] [SerializeField] private float washShare = 0.5f;

        private AudioListener listener;
        private float nextListenerLookAt;
        private readonly System.Collections.Generic.HashSet<AudioSource> started = new();   // sources played once: a pause keeps their place, an UnPause resumes it

        public bool Playing { get; private set; }
        public AudioSource Wind => wind;
        public AudioSource[] Washes => washes;

        public void Configure(AudioSource windSource, AudioSource[] washSources, float washVolumeShare)
        {
            wind = windSource;
            washes = washSources ?? new AudioSource[0];
            washShare = Mathf.Clamp01(washVolumeShare);
        }

        public float WashVolume => AudioLibrary.Get().SeaWashVolume * washShare;

        private void Awake()
        {
            AudioLibrary library = AudioLibrary.Get();
            Fill(wind, library.Wind, library.WindVolume);
            for (int i = 0; i < washes.Length; i++)
            {
                Fill(washes[i], library.SeaWash, WashVolume);
                // Each wash a different way into the loop, so the edges never swell in step.
                if (washes[i] != null && washes[i].clip != null) washes[i].timeSamples = (int)((long)washes[i].clip.samples * i / Mathf.Max(1, washes.Length));
            }
        }

        private static void Fill(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null) return;
            source.clip = clip; source.volume = volume; source.loop = true; source.playOnAwake = false;
            AudioDeviceService.RouteSource(source);
        }

        // THE RULE FOR WHEN: the listener's object is in the HQ scene and the crew's
        // world is HQ (no day state yet counts as HQ: the lobby before a run).
        public static bool ShouldPlay(Transform ear)
        {
            if (ear == null) return false;
            if (ear.gameObject.scene != WorldScenes.Scene(WorldId.HQ)) return false;
            CrewDayState day = CrewDayState.Instance;
            return day == null || day.World == WorldId.HQ;
        }

        private void Update()
        {
            bool on = ShouldPlay(Listener());
            Toggle(wind, on);
            foreach (AudioSource wash in washes) Toggle(wash, on);
            Playing = on;
            ShipAmbience.WindSuppressed = on && wind != null && wind.isPlaying;
        }

        private void OnDisable()
        {
            if (wind != null && wind.isPlaying) wind.Stop();
            foreach (AudioSource wash in washes) if (wash != null && wash.isPlaying) wash.Stop();
            started.Clear();
            Playing = false;
            ShipAmbience.WindSuppressed = false;
        }

        // The bed's level at `ear` from the sources' settings: a 2D source counts in
        // full, a spatial one by its rolloff curve. Pure: it ignores ShouldPlay, so the
        // probe runs in edit mode with no session.
        public float LevelAt(Vector3 ear)
        {
            float level = 0f;
            if (wind != null) level += SourceLevel(wind, ear, AudioLibrary.Get().WindVolume);
            level += WashLevelAt(ear);
            return level;
        }

        // The spatial sea-wash part alone.
        public float WashLevelAt(Vector3 ear)
        {
            float level = 0f;
            foreach (AudioSource wash in washes)
                if (wash != null) level += SourceLevel(wash, ear, WashVolume);
            return level;
        }

        private static float SourceLevel(AudioSource source, Vector3 ear, float volume)
        {
            float spatial = Rolloff(Vector3.Distance(ear, source.transform.position), source.minDistance, source.maxDistance, source.rolloffMode);
            return volume * Mathf.Lerp(1f, spatial, source.spatialBlend);
        }

        // Unity's built-in curves: Linear falls from 1 at min to 0 at max; Logarithmic
        // is 1 within min, then min / d, held at min / max beyond max (the sound never
        // goes silent, it stops attenuating).
        public static float Rolloff(float distance, float min, float max, AudioRolloffMode mode)
        {
            min = Mathf.Max(0.01f, min); max = Mathf.Max(min + 0.01f, max);
            if (mode == AudioRolloffMode.Linear) return 1f - Mathf.Clamp01((distance - min) / (max - min));
            if (distance <= min) return 1f;
            return min / Mathf.Min(distance, max);
        }

        public static HQAmbience InHQ()
        {
            var hq = WorldScenes.Scene(WorldId.HQ);
            HQAmbience any = null;
            foreach (HQAmbience candidate in FindObjectsByType<HQAmbience>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (hq.IsValid() && candidate.gameObject.scene == hq) return candidate;
                any ??= candidate;
            }
            return any;
        }

        private Transform Listener()
        {
            if ((listener == null || !listener.isActiveAndEnabled) && Time.unscaledTime >= nextListenerLookAt)
            {
                nextListenerLookAt = Time.unscaledTime + 1f;
                listener = null;
                foreach (AudioListener candidate in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                    if (candidate.isActiveAndEnabled) { listener = candidate; break; }
            }
            return listener != null && listener.isActiveAndEnabled ? listener.transform : null;
        }

        private void Toggle(AudioSource source, bool on)
        {
            if (source == null || source.clip == null) return;
            if (on && !source.isPlaying) { if (started.Contains(source)) source.UnPause(); else { source.Play(); started.Add(source); } }
            else if (!on && source.isPlaying) source.Pause(); // a pause keeps each wash's place in the loop
        }
    }
}
