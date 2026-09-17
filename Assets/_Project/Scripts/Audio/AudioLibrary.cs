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
        [Tooltip("The winch: loops, heard only in the car's last seconds before it arrives (Dan, 18 September 2026) — from very far below, low through the deck, lower still inside the car.")]
        [SerializeField] private AudioClip elevatorWinch;
        [Tooltip("Seconds before the car arrives during which the winch is heard (going down: before the bottom; going up: before the top).")]
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

        [Header("Feet")]
        [Tooltip("A footstep on the seafloor; pitched a little differently left and right, higher sprinting.")]
        [SerializeField] private AudioClip footstep;
        [Tooltip("The scuff of a takeoff.")]
        [SerializeField] private AudioClip jump;
        [Tooltip("The thud of a landing.")]
        [SerializeField] private AudioClip land;
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.07f;   // "way lower" (Dan, 18 September 2026): a fifth of the first cut
        [Range(0f, 1f)] [SerializeField] private float sprintFootstepVolume = 0.11f;
        [Range(0f, 1f)] [SerializeField] private float jumpVolume = 0.22f;
        [Range(0f, 1f)] [SerializeField] private float landVolume = 0.3f;
        [Tooltip("Your own steps, jumps and landings play at this fraction of a friend's (they are under your own ears).")]
        [Range(0f, 1f)] [SerializeField] private float ownFootstepScale = 0.6f;

        public AudioClip ElevatorWinch => elevatorWinch != null ? elevatorWinch : PlaceholderSounds.Winch;
        public AudioClip ElevatorDing => elevatorDing != null ? elevatorDing : PlaceholderSounds.Ding;
        public float WinchVolumeAtCar => winchVolumeAtCar;
        public float WinchVolumeInCar => winchVolumeInCar;
        public float WinchSecondsBeforeArrival => winchSecondsBeforeArrival;
        public float WinchFadeSeconds => winchFadeSeconds;
        public float WinchReachMetres => winchReachMetres;
        public float WinchVolumeOnShip => winchVolumeOnShip;
        public AudioClip Footstep => footstep != null ? footstep : PlaceholderSounds.Step;
        public AudioClip Jump => jump != null ? jump : PlaceholderSounds.Jump;
        public AudioClip Land => land != null ? land : PlaceholderSounds.Land;
        public float FootstepVolume => footstepVolume;
        public float SprintFootstepVolume => sprintFootstepVolume;
        public float JumpVolume => jumpVolume;
        public float LandVolume => landVolume;
        public float OwnFootstepScale => ownFootstepScale;
        public float DingVolume => dingVolume;
        public bool WinchIsPlaceholder => elevatorWinch == null;
        public bool DingIsPlaceholder => elevatorDing == null;

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
        private static AudioClip winch, ding, step, jump, land;

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
        // A takeoff: a shorter, brighter scuff.
        public static AudioClip Jump
        {
            get
            {
                if (jump != null) return jump;
                jump = Burst("Placeholder jump", 0.09f, 0.75f, 0.45f, 2);
                return jump;
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
