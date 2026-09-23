using UnityEngine;

namespace SunkCost.Audio
{
    // Where the game's sound effects live (Dan, 17 September 2026: "where will I
    // get the sounds that replace them?"): one asset in Resources (AudioLibrary)
    // with a named slot per sound. Every slot starts with a generated placeholder
    // (PlaceholderSounds) so the game is audible today; replacing one is dropping
    // a .wav into the slot in the Inspector — no code. Licences: CC0 or a paid
    // pack for a sold game; CC-BY needs a credit; never NC. Sources that fit:
    // freesound.org (check the licence per file), the Sonniss GDC bundles
    // (free, commercial), the Asset Store / itch.io packs, or a sound designer
    // for the signature sounds.
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Sunk Cost/Audio Library")]
    public sealed class AudioLibrary : ScriptableObject
    {
        [Header("Elevator")]
        [Tooltip("The winch: loops, heard for the seconds the car is near you (Dan, 18 September 2026) — quiet from very far below, low through the deck, low inside the car the whole way.")]
        [SerializeField] private AudioClip elevatorWinch;
        [Tooltip("Seconds the winch is heard while the car is near your end: the last ones as it arrives at you, the first ones as it leaves you.")]
        [SerializeField] private float winchSecondsBeforeArrival = 5f;
        [Tooltip("The winch fades in and out over this long so it never clicks.")]
        [SerializeField] private float winchFadeSeconds = 0.5f;
        [Tooltip("How far the winch at the car carries, metres — the whole site: 'hear it from very far' (Dan).")]
        [SerializeField] private float winchReachMetres = 250f;
        [Tooltip("The bell when the car arrives and the doors open — the movie elevator's ding.")]
        [SerializeField] private AudioClip elevatorDing;
        [Tooltip("At the car, for a diver on the site: quiet — 'hear from far', not loud (Dan, 18 September 2026).")]
        [Range(0f, 1f)] [SerializeField] private float winchVolumeAtCar = 0.35f;
        [Tooltip("The winch for the riders inside the car — mostly for those left below, so half again lower in here (Dan, 18 September 2026).")]
        [Range(0f, 1f)] [SerializeField] private float winchVolumeInCar = 0.06f;
        [Tooltip("The winch as heard on the ship, through the deck (Dan: 'low').")]
        [Range(0f, 1f)] [SerializeField] private float winchVolumeOnShip = 0.15f;
        [Range(0f, 1f)] [SerializeField] private float dingVolume = 0.8f;
        [Tooltip("How 3D the winch and the bell are on the ship: 'mostly 3D' (Dan, 23 September 2026) - loud near the cabin, the rest heard faintly across the deck.")]
        [Range(0f, 1f)] [SerializeField] private float shipCabinSpatialBlend = 0.7f;
        [Tooltip("Metres from the deck cabin within which the winch and the bell are at full volume.")]
        [SerializeField] private float shipCabinNearMetres = 6f;
        [Tooltip("Metres from the deck cabin where their 3D part has faded out (the ship is 48 m long).")]
        [SerializeField] private float shipCabinFarMetres = 40f;

        [Header("The ship (ship audit SHIP-065, 23 September 2026): ambience, kept quiet")]
        [Tooltip("The sea washing along the hull, heard from both sides, louder at the rails.")]
        [SerializeField] private AudioClip seaWash;
        [Range(0f, 1f)] [SerializeField] private float seaWashVolume = 0.14f;
        [Tooltip("The wind over the deck; it rises with the height above it (the tower roof).")]
        [SerializeField] private AudioClip wind;
        [Range(0f, 1f)] [SerializeField] private float windVolume = 0.035f;
        [Range(0f, 1f)] [SerializeField] private float windVolumeHigh = 0.1f;
        [Tooltip("Metres over the deck where the wind reaches its high volume.")]
        [SerializeField] private float windHighMetres = 6f;
        [Tooltip("The engine: a diesel at the stern while the ship sails (ShipDepartureVisual plays it).")]
        [SerializeField] private AudioClip engineHum;
        [Range(0f, 1f)] [SerializeField] private float engineVolume = 0.3f;
        [Tooltip("A low hiss at the TV speaker while it shows NO SIGNAL.")]
        [SerializeField] private AudioClip tvHiss;
        [Range(0f, 1f)] [SerializeField] private float tvHissVolume = 0.04f;

        [Header("Feet")]
        [Tooltip("A footstep on the seafloor; pitched a little differently left and right, higher sprinting.")]
        [SerializeField] private AudioClip footstep;
        [Tooltip("The thud of a landing.")]
        [SerializeField] private AudioClip land;
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.07f;   // "way lower" (Dan, 18 September 2026): a fifth of the first cut
        [Range(0f, 1f)] [SerializeField] private float sprintFootstepVolume = 0.11f;
        [Range(0f, 1f)] [SerializeField] private float landVolume = 0.3f;
        [Tooltip("Your own steps, jumps and landings play at this fraction of a friend's (they are under your own ears).")]
        [Range(0f, 1f)] [SerializeField] private float ownFootstepScale = 0.6f;

        [Header("Leaks (the monsters, 20 September 2026)")]
        [Tooltip("The hiss of a punctured suit; loops at the diver while the tank leaks.")]
        [SerializeField] private AudioClip leakHiss;
        [Range(0f, 1f)] [SerializeField] private float leakHissVolume = 0.35f;

        [Header("The dash (Dan, 21 September 2026)")]
        [Tooltip("The whoosh at the feet as a diver dashes; heard by everyone near.")]
        [SerializeField] private AudioClip dashWhoosh;
        [Range(0f, 1f)] [SerializeField] private float dashWhooshVolume = 0.5f;

        [Header("Monsters")]
        [Tooltip("A creature's call as it starts to hunt (the Charger's wind-up too).")]
        [SerializeField] private AudioClip monsterCall;
        [Range(0f, 1f)] [SerializeField] private float monsterCallVolume = 0.7f;
        [Tooltip("A strike or a bolt landing on a diver.")]
        [SerializeField] private AudioClip monsterHit;
        [Range(0f, 1f)] [SerializeField] private float monsterHitVolume = 0.8f;
        [Tooltip("A bolt leaving the Lure or the Listener.")]
        [SerializeField] private AudioClip boltShot;
        [Range(0f, 1f)] [SerializeField] private float boltShotVolume = 0.6f;
        [Tooltip("The Elevator Ghost: a hum at the car while its light is green.")]
        [SerializeField] private AudioClip ghostHum;
        [Range(0f, 1f)] [SerializeField] private float ghostHumVolume = 0.5f;
        [Tooltip("The car's doors slamming on someone who walked into the green.")]
        [SerializeField] private AudioClip doorSlam;
        [Range(0f, 1f)] [SerializeField] private float doorSlamVolume = 0.9f;

        public AudioClip ElevatorWinch => elevatorWinch != null ? elevatorWinch : PlaceholderSounds.Winch;
        public AudioClip ElevatorDing => elevatorDing != null ? elevatorDing : PlaceholderSounds.Ding;
        public float WinchVolumeAtCar => winchVolumeAtCar;
        public float WinchVolumeInCar => winchVolumeInCar;
        public float WinchSecondsBeforeArrival => winchSecondsBeforeArrival;
        public float WinchFadeSeconds => winchFadeSeconds;
        public float WinchReachMetres => winchReachMetres;
        public float WinchVolumeOnShip => winchVolumeOnShip;
        public AudioClip Footstep => footstep != null ? footstep : PlaceholderSounds.Step;
        public AudioClip Land => land != null ? land : PlaceholderSounds.Land;
        public float FootstepVolume => footstepVolume;
        public float SprintFootstepVolume => sprintFootstepVolume;
        public float LandVolume => landVolume;
        public float OwnFootstepScale => ownFootstepScale;
        public float DingVolume => dingVolume;
        public float ShipCabinSpatialBlend => shipCabinSpatialBlend;
        public float ShipCabinNearMetres => shipCabinNearMetres;
        public float ShipCabinFarMetres => Mathf.Max(shipCabinFarMetres, shipCabinNearMetres + 1f);
        public AudioClip SeaWash => seaWash != null ? seaWash : PlaceholderSounds.SeaWash;
        public float SeaWashVolume => seaWashVolume;
        public AudioClip Wind => wind != null ? wind : PlaceholderSounds.Wind;
        public float WindVolume => windVolume;
        public float WindVolumeHigh => windVolumeHigh;
        public float WindHighMetres => Mathf.Max(0.5f, windHighMetres);
        public AudioClip EngineHum => engineHum != null ? engineHum : PlaceholderSounds.Engine;
        public float EngineVolume => engineVolume;
        public AudioClip TvHiss => tvHiss != null ? tvHiss : PlaceholderSounds.Static;
        public float TvHissVolume => tvHissVolume;
        public bool WinchIsPlaceholder => elevatorWinch == null;
        public bool DingIsPlaceholder => elevatorDing == null;
        public AudioClip LeakHiss => leakHiss != null ? leakHiss : PlaceholderSounds.Hiss;
        public float LeakHissVolume => leakHissVolume;
        public AudioClip MonsterCall => monsterCall != null ? monsterCall : PlaceholderSounds.Call;
        public float MonsterCallVolume => monsterCallVolume;
        public AudioClip MonsterHit => monsterHit != null ? monsterHit : PlaceholderSounds.Hit;
        public float MonsterHitVolume => monsterHitVolume;
        public AudioClip BoltShot => boltShot != null ? boltShot : PlaceholderSounds.Shot;
        public float BoltShotVolume => boltShotVolume;
        public AudioClip GhostHum => ghostHum != null ? ghostHum : PlaceholderSounds.Hum;
        public float GhostHumVolume => ghostHumVolume;
        public AudioClip DoorSlam => doorSlam != null ? doorSlam : PlaceholderSounds.Slam;
        public float DoorSlamVolume => doorSlamVolume;
        public AudioClip DashWhoosh => dashWhoosh != null ? dashWhoosh : PlaceholderSounds.Whoosh;
        public float DashWhooshVolume => dashWhooshVolume;
        public bool DashWhooshIsPlaceholder => dashWhoosh == null;
        public bool LeakHissIsPlaceholder => leakHiss == null;
        public bool MonsterCallIsPlaceholder => monsterCall == null;

        private static AudioLibrary loaded;
        public static AudioLibrary Get()
        {
            if (loaded != null) return loaded;
            loaded = Resources.Load<AudioLibrary>("AudioLibrary");
            if (loaded == null) { loaded = CreateInstance<AudioLibrary>(); loaded.hideFlags = HideFlags.HideAndDontSave; }
            return loaded;
        }
    }

    // Generated stand-ins until real recordings fill the library's slots: a low
    // grinding winch and a two-note bell. Built once, 48 kHz mono.
    public static class PlaceholderSounds
    {
        private const int Rate = 48000;
        private static AudioClip winch, ding, step, land, hiss, call, hit, shot, hum, slam;

        // ---- the ship's ambience (ship audit SHIP-065, 23 September 2026) ----
        private static AudioClip seaWash, windLoop, engine, tvStatic;

        // The sea along the hull: dull noise swelling with two slow swells, a little
        // foam on the crests; a 12 s loop.
        public static AudioClip SeaWash
        {
            get
            {
                if (seaWash != null) return seaWash;
                const float seconds = 12f;
                seaWash = Loop("Placeholder sea wash", seconds, 31, (t, white, state) =>
                {
                    state[0] = state[0] * 0.995f + white * 0.005f;          // the body of the water: very low
                    state[1] = state[1] * 0.9f + white * 0.1f;              // foam: brighter
                    float swell = 0.55f + 0.3f * Mathf.Sin(2f * Mathf.PI * t * 3f / seconds) + 0.15f * Mathf.Sin(2f * Mathf.PI * t * 5f / seconds + 1.3f);
                    float crest = Mathf.Max(0f, swell - 0.6f) * 2.5f;
                    return state[0] * 9f * swell + state[1] * 0.35f * crest;
                });
                return seaWash;
            }
        }
        // The wind: mid noise in slow gusts; a 10 s loop.
        public static AudioClip Wind
        {
            get
            {
                if (windLoop != null) return windLoop;
                const float seconds = 10f;
                windLoop = Loop("Placeholder wind", seconds, 37, (t, white, state) =>
                {
                    state[0] = state[0] * 0.97f + white * 0.03f;
                    state[1] = state[1] * 0.995f + white * 0.005f;
                    float band = state[0] - state[1];                        // a band: no rumble, no hiss
                    float gust = 0.6f + 0.25f * Mathf.Sin(2f * Mathf.PI * t * 2f / seconds) + 0.15f * Mathf.Sin(2f * Mathf.PI * t * 3f / seconds + 0.7f);
                    return band * 5f * gust;
                });
                return windLoop;
            }
        }
        // The engine: a slow diesel's firing thump (24 Hz and its harmonics) over a
        // low rumble; a 2 s loop.
        public static AudioClip Engine
        {
            get
            {
                if (engine != null) return engine;
                engine = Loop("Placeholder engine", 2f, 41, (t, white, state) =>
                {
                    state[0] = state[0] * 0.98f + white * 0.02f;
                    float firing = 0.4f * Mathf.Sin(2f * Mathf.PI * 24f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 48f * t + 0.4f) + 0.12f * Mathf.Sin(2f * Mathf.PI * 72f * t + 1.1f) + 0.06f * Mathf.Sin(2f * Mathf.PI * 96f * t);
                    float throb = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 4f * t); // the cylinders
                    return firing * throb * 0.8f + state[0] * 2f;
                });
                return engine;
            }
        }
        // A screen with no signal: soft static; a 2 s loop.
        public static AudioClip Static
        {
            get
            {
                if (tvStatic != null) return tvStatic;
                tvStatic = Loop("Placeholder static", 2f, 43, (t, white, state) =>
                {
                    state[0] = state[0] * 0.5f + white * 0.5f;
                    return state[0] * 0.5f;
                });
                return tvStatic;
            }
        }

        // A seamless loop of `seconds`: the sample function runs on past the end and
        // the overrun is faded into the start, so the loop point never clicks.
        private static AudioClip Loop(string name, float seconds, int seed, System.Func<float, float, float[], float> sample)
        {
            int n = (int)(Rate * seconds), fade = Rate / 2;
            var raw = new float[n + fade];
            var state = new float[4];
            System.Random random = new(seed);
            for (int i = 0; i < raw.Length; i++)
                raw[i] = sample(i / (float)Rate, (float)(random.NextDouble() * 2 - 1), state);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = raw[i];
                if (i < fade) { float k = i / (float)fade; v = raw[i] * k + raw[n + i] * (1f - k); }
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A leak: a 2 s loop of bright noise, seamless (the monsters, 20 September 2026).
        public static AudioClip Hiss
        {
            get
            {
                if (hiss != null) return hiss;
                int n = Rate * 2;
                var data = new float[n];
                System.Random random = new(11);
                float high = 0f, prev = 0f;
                for (int i = 0; i < n; i++)
                {
                    float white = (float)(random.NextDouble() * 2 - 1);
                    high = 0.6f * (high + white - prev); prev = white; // a high-pass: the bright part of the noise
                    float flutter = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 7f * i / Rate);
                    data[i] = Mathf.Clamp(high * 0.55f * flutter, -1f, 1f);
                }
                hiss = AudioClip.Create("Placeholder hiss", n, 1, Rate, false);
                hiss.SetData(data, 0);
                return hiss;
            }
        }
        // A creature's call: a low growl — two detuned sines with a rasp — 0.9 s.
        public static AudioClip Call
        {
            get
            {
                if (call != null) return call;
                int n = (int)(Rate * 0.9f);
                var data = new float[n];
                System.Random random = new(5);
                float rasp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate, u = i / (float)n;
                    float envelope = Mathf.Min(1f, u * 12f) * (1f - u) * (1f - u);
                    float tone = Mathf.Sin(2f * Mathf.PI * 62f * t) + 0.8f * Mathf.Sin(2f * Mathf.PI * 67f * t + 0.5f) + 0.3f * Mathf.Sin(2f * Mathf.PI * 190f * t);
                    rasp = rasp * 0.9f + (float)(random.NextDouble() * 2 - 1) * 0.1f;
                    data[i] = Mathf.Clamp((tone * 0.35f + rasp * 2f) * envelope, -1f, 1f);
                }
                call = AudioClip.Create("Placeholder call", n, 1, Rate, false);
                call.SetData(data, 0);
                return call;
            }
        }
        // A hit on a diver: a heavy, dull thud.
        public static AudioClip Hit
        {
            get
            {
                if (hit != null) return hit;
                hit = Burst("Placeholder hit", 0.22f, 0.97f, 0.9f, 9);
                return hit;
            }
        }
        // A bolt leaving: a short bright crack.
        public static AudioClip Shot
        {
            get
            {
                if (shot != null) return shot;
                shot = Burst("Placeholder shot", 0.12f, 0.6f, 0.6f, 13);
                return shot;
            }
        }
        // The Ghost's hum: two close low tones beating, a 2 s loop.
        public static AudioClip Hum
        {
            get
            {
                if (hum != null) return hum;
                int n = Rate * 2;
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    float a = Mathf.Sin(2f * Mathf.PI * 55f * t), b = Mathf.Sin(2f * Mathf.PI * 56f * t), c = 0.25f * Mathf.Sin(2f * Mathf.PI * 165f * t);
                    data[i] = Mathf.Clamp((a + b + c) * 0.25f, -1f, 1f);
                }
                hum = AudioClip.Create("Placeholder hum", n, 1, Rate, false);
                hum.SetData(data, 0);
                return hum;
            }
        }
        // A dash's whoosh: filtered noise that swells and fades over a third of a second.
        private static AudioClip whoosh;
        public static AudioClip Whoosh
        {
            get
            {
                if (whoosh != null) return whoosh;
                int n = (int)(Rate * 0.35f);
                var data = new float[n];
                System.Random random = new(21);
                float low = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    low = low * 0.9f + (float)(random.NextDouble() * 2 - 1) * 0.1f;
                    float envelope = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.6f));
                    data[i] = Mathf.Clamp(low * 5f * envelope, -1f, 1f);
                }
                whoosh = AudioClip.Create("Placeholder whoosh", n, 1, Rate, false);
                whoosh.SetData(data, 0);
                return whoosh;
            }
        }
        // The doors slamming: a very heavy short bang.
        public static AudioClip Slam
        {
            get
            {
                if (slam != null) return slam;
                slam = Burst("Placeholder slam", 0.35f, 0.98f, 1f, 17);
                return slam;
            }
        }

        public static AudioClip Winch
        {
            get
            {
                if (winch != null) return winch;
                int n = Rate * 2; // a 2 s loop
                var data = new float[n];
                System.Random random = new(7);
                float rumble = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    // A 42 Hz motor with its harmonics, a slow 3 Hz grind on top, and filtered noise.
                    float motor = 0.35f * Mathf.Sin(2f * Mathf.PI * 42f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 84f * t) + 0.08f * Mathf.Sin(2f * Mathf.PI * 126f * t);
                    float grind = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 3f * t);
                    rumble = rumble * 0.96f + (float)(random.NextDouble() * 2 - 1) * 0.04f; // low-passed noise
                    data[i] = Mathf.Clamp(motor * (0.7f + 0.3f * grind) + rumble * 2.5f, -1f, 1f) * 0.6f;
                }
                winch = AudioClip.Create("Placeholder winch", n, 1, Rate, false);
                winch.SetData(data, 0);
                return winch;
            }
        }

        // A footstep: a 70 ms burst of low-passed noise with a soft attack — a boot on silt.
        public static AudioClip Step
        {
            get
            {
                if (step != null) return step;
                step = Burst("Placeholder footstep", 0.07f, 0.90f, 0.5f, 1);
                return step;
            }
        }
        // A landing: a heavier, longer thud.
        public static AudioClip Land
        {
            get
            {
                if (land != null) return land;
                land = Burst("Placeholder landing", 0.16f, 0.95f, 0.7f, 3);
                return land;
            }
        }
        // Low-passed noise (filter 0..1: higher = duller) with an attack and a decay.
        private static AudioClip Burst(string name, float seconds, float filter, float gain, int seed)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            System.Random random = new(seed);
            float low = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                low = low * filter + (float)(random.NextDouble() * 2 - 1) * (1f - filter);
                float envelope = Mathf.Min(1f, t * 25f) * Mathf.Exp(-t * 6f);
                data[i] = Mathf.Clamp(low * 4f * envelope * gain, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip Ding
        {
            get
            {
                if (ding != null) return ding;
                int n = (int)(Rate * 1.6f);
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    // Two struck notes a fifth apart, each with a bright partial and a long decay.
                    float a = Mathf.Exp(-t * 2.2f), b = Mathf.Exp(-t * 3.5f);
                    float note1 = (Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * 2640f * t)) * a;
                    float note2 = t > 0.18f ? (Mathf.Sin(2f * Mathf.PI * 1318.5f * (t - 0.18f)) + 0.35f * Mathf.Sin(2f * Mathf.PI * 3955f * (t - 0.18f))) * Mathf.Exp(-(t - 0.18f) * 2.2f) : 0f;
                    data[i] = Mathf.Clamp((note1 + note2 * 0.9f) * 0.35f * (0.6f + 0.4f * b), -1f, 1f);
                }
                ding = AudioClip.Create("Placeholder ding", n, 1, Rate, false);
                ding.SetData(data, 0);
                return ding;
            }
        }
    }
}
