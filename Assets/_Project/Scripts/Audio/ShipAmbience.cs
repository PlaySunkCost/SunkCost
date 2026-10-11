using SunkCost.World;
using UnityEngine;

namespace SunkCost.Audio
{
    // The ship at sea is no longer silent and still (ship audit SHIP-065, 23
    // September 2026): the sea washing along both sides of the hull, the wind over
    // the deck rising with the height above it, a low hiss at the TV speaker while
    // it shows NO SIGNAL, and the bow spray and the stern's wake while the ship
    // sails. The engine is ShipDepartureVisual's (it plays the source this fills).
    // Built into the ship by ShipAmbienceSetup; clips from the AudioLibrary
    // (placeholders until recordings replace them), kept quiet: it is ambience.
    //
    // Local presentation on every peer, from what every peer already has: the
    // sounds play while this machine's listener is near the ship (a spectator's
    // eyes on the deck hear it too), the wake from the replicated departure stage,
    // so every screen that shows the ship shows it sailing the same way.
    //
    // At HQ the platform has its own bed (HQAmbience, 8 October 2026): while that
    // bed's wind plays on this peer it sets WindSuppressed and this ship's wind
    // stays quiet (one wind, not two); the washes keep coming from the hull. The
    // 70 m hearing edge fades over half a second instead of cutting.
    [DefaultExecutionOrder(-200)] // the engine's clip is in before ShipDepartureVisual looks for it
    public sealed class ShipAmbience : MonoBehaviour
    {
        public const float HearingMetres = 70f; // the listener this near the ship's root hears it (the ship is 48 m long)
        public const float FadeSeconds = 0.5f;  // in and out at the hearing edge

        // Set by HQAmbience while its 2D wind plays on this peer; it clears it every
        // frame it does not play and when it is disabled. Nothing else writes it.
        public static bool WindSuppressed;

        [SerializeField] private AudioSource washPort, washStarboard, wind, tvHiss, engine;
        [SerializeField] private ParticleSystem[] wake = new ParticleSystem[0];

        private ShipParts parts;
        private ShipTV tv;
        private WorldId world;
        private bool hasWorld;
        private AudioListener listener;
        private float nextListenerLookAt;
        private bool sailing;
        private float fade;       // 0 silent .. 1 full, moved toward Heard at the hearing edge
        private float windFade;   // the wind's own, so the HQ bed's hand-over fades too
        private bool portStarted, starboardStarted, windStarted, hissStarted;   // one per source: a pause keeps its place, an UnPause resumes it

        public bool Sailing => sailing;              // for the checks
        public bool Heard { get; private set; }

        public void Configure(AudioSource portWash, AudioSource starboardWash, AudioSource windSource, AudioSource hissSource, AudioSource engineSource, ParticleSystem[] wakeSystems)
        {
            washPort = portWash; washStarboard = starboardWash; wind = windSource; tvHiss = hissSource; engine = engineSource;
            wake = wakeSystems ?? new ParticleSystem[0];
        }

        private void Awake()
        {
            parts = GetComponent<ShipParts>();
            tv = GetComponent<ShipTV>();
            hasWorld = WorldScenes.TryParse(gameObject.scene.name, out world);
            AudioLibrary library = AudioLibrary.Get();
            Fill(washPort, library.SeaWash, library.SeaWashVolume);
            Fill(washStarboard, library.SeaWash, library.SeaWashVolume);
            Fill(wind, library.Wind, library.WindVolume);
            Fill(tvHiss, library.TvHiss, library.TvHissVolume);
            Fill(engine, library.EngineHum, library.EngineVolume);
            // The two sides a little apart in the loop, so they never wash in step.
            if (washStarboard != null && washStarboard.clip != null) washStarboard.timeSamples = washStarboard.clip.samples / 2;
            foreach (ParticleSystem system in wake) if (system != null) { var emission = system.emission; emission.enabled = false; }
        }

        private static void Fill(AudioSource source, AudioClip clip, float volume)
        {
            if (source == null) return;
            source.clip = clip; source.volume = volume; source.loop = true; source.playOnAwake = false;
            AudioDeviceService.RouteSource(source);
        }

        private void Update()
        {
            AudioLibrary library = AudioLibrary.Get();
            Transform ear = Listener();
            Heard = ear != null && Vector3.Distance(ear.position, transform.position) <= HearingMetres;
            float step = Time.unscaledDeltaTime / FadeSeconds;
            fade = Mathf.MoveTowards(fade, Heard ? 1f : 0f, step);
            windFade = Mathf.MoveTowards(windFade, Heard && !WindSuppressed ? 1f : 0f, step);
            Level(washPort, library.SeaWashVolume * fade, ref portStarted);
            Level(washStarboard, library.SeaWashVolume * fade, ref starboardStarted);
            float windVolume = library.WindVolume;
            if (ear != null)
            {
                float height = ear.position.y - transform.position.y - 1.6f; // over a standing player's eyes on the deck
                windVolume = Mathf.Lerp(library.WindVolume, library.WindVolumeHigh, Mathf.Clamp01(height / library.WindHighMetres));
            }
            Level(wind, windVolume * windFade, ref windStarted);
            if (tvHiss != null && parts != null && parts.TvSpeaker != null) tvHiss.transform.position = parts.TvSpeaker.position;
            Level(tvHiss, tv != null && !tv.Live ? library.TvHissVolume * fade : 0f, ref hissStarted);

            // Sailing: the stages in which ShipDepartureVisual moves this ship.
            CrewDayState day = CrewDayState.Instance;
            DepartureStage stage = day != null ? day.Departure.Stage : DepartureStage.Idle;
            bool now = hasWorld && day != null && day.Departure.FromWorld == world && (stage == DepartureStage.PullingAway || stage == DepartureStage.FadingOut);
            if (now != sailing)
            {
                sailing = now;
                foreach (ParticleSystem system in wake)
                {
                    if (system == null) continue;
                    var emission = system.emission;
                    emission.enabled = now;
                    if (now && !system.isPlaying) system.Play();
                }
            }
        }

        // What this ship's sources give at `ear` from their settings (the probe's share
        // of the docked ship): the washes by their rolloff, the wind in full, within
        // HearingMetres; nothing beyond it.
        public float LevelAt(Vector3 ear, bool includeWind = true)
        {
            if (Vector3.Distance(ear, transform.position) > HearingMetres) return 0f;
            AudioLibrary library = AudioLibrary.Get();
            float level = 0f;
            foreach (AudioSource wash in new[] { washPort, washStarboard })
                if (wash != null) level += library.SeaWashVolume * HQAmbience.Rolloff(Vector3.Distance(ear, wash.transform.position), wash.minDistance, wash.maxDistance, wash.rolloffMode);
            if (includeWind && wind != null) level += library.WindVolume;
            return level;
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

        // A source at `volume`: playing while it is above zero, paused at zero (a pause
        // keeps the starboard wash's half-loop offset; the first start is a Play).
        private static void Level(AudioSource source, float volume, ref bool started)
        {
            if (source == null || source.clip == null) return;
            if (volume > 0.0005f)
            {
                source.volume = volume;
                if (!source.isPlaying) { if (started) source.UnPause(); else source.Play(); started = true; }
            }
            else if (source.isPlaying) source.Pause();
        }
    }
}
