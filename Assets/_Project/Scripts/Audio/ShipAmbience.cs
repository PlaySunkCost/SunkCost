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
    [DefaultExecutionOrder(-200)] // the engine's clip is in before ShipDepartureVisual looks for it
    public sealed class ShipAmbience : MonoBehaviour
    {
        public const float HearingMetres = 70f; // the listener this near the ship's root hears it (the ship is 48 m long)

        [SerializeField] private AudioSource washPort, washStarboard, wind, tvHiss, engine;
        [SerializeField] private ParticleSystem[] wake = new ParticleSystem[0];

        private ShipParts parts;
        private ShipTV tv;
        private WorldId world;
        private bool hasWorld;
        private AudioListener listener;
        private float nextListenerLookAt;
        private bool sailing;

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
            Toggle(washPort, Heard);
            Toggle(washStarboard, Heard);
            Toggle(wind, Heard);
            if (Heard && wind != null)
            {
                float height = ear.position.y - transform.position.y - 1.6f; // over a standing player's eyes on the deck
                wind.volume = Mathf.Lerp(library.WindVolume, library.WindVolumeHigh, Mathf.Clamp01(height / library.WindHighMetres));
            }
            if (tvHiss != null && parts != null && parts.TvSpeaker != null) tvHiss.transform.position = parts.TvSpeaker.position;
            Toggle(tvHiss, Heard && tv != null && !tv.Live);

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

        private static void Toggle(AudioSource source, bool on)
        {
            if (source == null || source.clip == null) return;
            if (on && !source.isPlaying) source.Play();
            else if (!on && source.isPlaying) source.Stop();
        }
    }
}
