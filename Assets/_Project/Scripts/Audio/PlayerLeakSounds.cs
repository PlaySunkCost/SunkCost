using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Audio
{
    // The hiss of a leaking suit (docs/DESIGN.md §3, 20 September 2026), on every
    // peer from the replicated leak flag: loops at the diver while PlayerVitals
    // says leaking. Yours is under your own ears; a friend's is heard nearby.
    // Added to the player at runtime by PlayerVitals. Presentation only.
    public sealed class PlayerLeakSounds : MonoBehaviour
    {
        private PlayerVitals vitals;
        private AudioSource hiss;

        public bool Hissing => hiss != null && hiss.isPlaying;

        private void Awake()
        {
            vitals = GetComponent<PlayerVitals>();
            AudioLibrary library = AudioLibrary.Get();
            var go = new GameObject("Leak");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.3f, -0.2f);
            hiss = go.AddComponent<AudioSource>();
            hiss.clip = library.LeakHiss; hiss.loop = true; hiss.playOnAwake = false;
            hiss.volume = library.LeakHissVolume;
            hiss.spatialBlend = 1f; hiss.rolloffMode = AudioRolloffMode.Linear;
            hiss.minDistance = 1.5f; hiss.maxDistance = 18f; hiss.dopplerLevel = 0f;
            AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
            if (devices != null) devices.Route(hiss);
        }

        private void LateUpdate()
        {
            if (vitals == null || hiss == null) return;
            bool on = vitals.Leaking;
            if (on && !hiss.isPlaying) hiss.Play();
            else if (!on && hiss.isPlaying) hiss.Stop();
        }
    }
}
