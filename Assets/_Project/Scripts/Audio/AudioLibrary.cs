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
        [Tooltip("The winch while the car moves: loops. Loud at the car, low through the deck on the ship.")]
        [SerializeField] private AudioClip elevatorWinch;
        [Tooltip("The bell when the car arrives and the doors open — the movie elevator's ding.")]
        [SerializeField] private AudioClip elevatorDing;
        [Range(0f, 1f)] [SerializeField] private float winchVolumeAtCar = 1f;
        [Tooltip("The winch as heard on the ship, through the deck (Dan: 'low').")]
        [Range(0f, 1f)] [SerializeField] private float winchVolumeOnShip = 0.15f;
        [Range(0f, 1f)] [SerializeField] private float dingVolume = 0.8f;

        public AudioClip ElevatorWinch => elevatorWinch != null ? elevatorWinch : PlaceholderSounds.Winch;
        public AudioClip ElevatorDing => elevatorDing != null ? elevatorDing : PlaceholderSounds.Ding;
        public float WinchVolumeAtCar => winchVolumeAtCar;
        public float WinchVolumeOnShip => winchVolumeOnShip;
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
        private static AudioClip winch, ding;

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
