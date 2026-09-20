using SunkCost.Audio;
using SunkCost.Diving;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Elevator Ghost's window on the car (docs/DESIGN.md §6): tick-anchored
    // like ElevatorPhase — a serial, a start tick and a duration — so every peer
    // and a late joiner agree on the green. Written only by the server
    // (WorldSceneFlow.Ghost.cs) into CrewDayState. SlamSerial counts the doors
    // slamming on someone (the sound).
    public struct GhostPhase
    {
        public int Serial;
        public bool Active;
        public uint StartTick;
        public uint DurationTicks;
        public int SlamSerial;
    }

    // Every client's side of the Ghost: the car's interior light goes green and
    // pulses while the phase is active, a hum plays at the car, and the doors'
    // slam is heard when the serial moves. Rides on CrewDayState like
    // ElevatorSounds; reads the replicated phase, replicates nothing.
    public sealed class ElevatorGhostLight : MonoBehaviour
    {
        public static ElevatorGhostLight Instance { get; private set; }
        public static readonly Color Green = new(0.25f, 1f, 0.35f);
        public static readonly Color Warm = new(1f, 0.95f, 0.85f);

        private Light lamp;
        private ElevatorController lightCar;
        private float baseIntensity = 6f;
        private AudioSource hum, slam;
        private int seenSlam = -1;

        public bool IsGreen { get; private set; }
        public int SlamsHeard { get; private set; }

        private void Awake()
        {
            Instance = this;
            AudioLibrary library = AudioLibrary.Get();
            hum = Make("Ghost hum", library.GhostHum, true, library.GhostHumVolume, 6f, 60f);
            slam = Make("Door slam", library.DoorSlam, false, library.DoorSlamVolume, 4f, 120f);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private AudioSource Make(string name, AudioClip clip, bool loop, float volume, float min, float max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip; source.loop = loop; source.playOnAwake = false;
            source.volume = volume; source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = min; source.maxDistance = max; source.dopplerLevel = 0f;
            AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
            if (devices != null) devices.Route(source);
            return source;
        }

        private void LateUpdate()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) { IsGreen = false; Toggle(hum, false); return; }
            GhostPhase phase = day.Ghost;
            ElevatorController car = WorldSceneFlow.FindCarCached();
            // The slam: counted from the phase's serial even where the car is gone
            // already (the site closes right after a death), heard at the car when held.
            if (seenSlam < 0) seenSlam = phase.SlamSerial; // a joiner's old slams are old news
            else if (phase.SlamSerial != seenSlam)
            {
                seenSlam = phase.SlamSerial;
                if (car != null) slam.transform.position = car.transform.position + Vector3.up * 1.5f;
                slam.Play();
                SlamsHeard++;
            }
            if (car == null) { IsGreen = false; Toggle(hum, false); return; }
            if (lightCar != car)
            {
                lightCar = car;
                Transform bulb = car.transform.Find("Cabin Light");
                lamp = bulb != null ? bulb.GetComponent<Light>() : null;
                if (lamp != null) baseIntensity = lamp.intensity;
            }
            bool green = phase.Active && car.State == ElevatorState.AtBottom;
            IsGreen = green;
            if (lamp != null)
            {
                if (green)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3.5f);
                    lamp.color = Green;
                    lamp.intensity = baseIntensity * pulse;
                }
                else if (lamp.color != Warm) { lamp.color = Warm; lamp.intensity = baseIntensity; }
            }
            hum.transform.position = car.transform.position + Vector3.up * 1.5f;
            Toggle(hum, green);
        }

        private static void Toggle(AudioSource source, bool on)
        {
            if (source == null) return;
            if (on && !source.isPlaying) source.Play();
            else if (!on && source.isPlaying) source.Stop();
        }
    }
}
