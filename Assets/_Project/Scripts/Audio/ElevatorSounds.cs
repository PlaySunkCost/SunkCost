using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Audio
{
    // What the elevator sounds like (Dan, 17–18 September 2026): the winch in the
    // car's last seconds before it arrives — going down, before the bottom; going
    // up, before the top — quiet but carrying across the whole site for the
    // divers below, low through the deck for the ship, lower still inside the car
    // (the sound is for those left below) — and a bell when it arrives and the
    // doors open, below at the car and on the deck at the cabin. Local presentation on
    // every peer, read off the replicated elevator phase (CrewDayState.Elevator);
    // attached to the day state when the client starts. Which version plays
    // follows where the local player's object stands (its physical world), not
    // where its eyes are. Clips come from the AudioLibrary (placeholders until
    // recordings replace them).
    public sealed class ElevatorSounds : MonoBehaviour
    {
        public static ElevatorSounds Instance { get; private set; }

        private AudioSource carWinch, shipWinch, carBell, shipBell;
        private ElevatorState lastState;
        private bool primed;

        // For the checks.
        public bool WinchPlayingAtCar => carWinch != null && carWinch.isPlaying;
        public float CarWinchVolume => carWinch != null ? carWinch.volume : 0f;
        public float CarWinchReach => carWinch != null ? carWinch.maxDistance : 0f;
        // Seconds until the moving car arrives, from the replicated phase; 0 when it rests.
        public float SecondsToArrival { get; private set; }
        public bool WinchPlayingOnShip => shipWinch != null && shipWinch.isPlaying;
        public int DingsAtCar { get; private set; }
        public int DingsOnShip { get; private set; }

        private void Awake()
        {
            Instance = this;
            AudioLibrary library = AudioLibrary.Get();
            carWinch = Make("Winch at the car", library.ElevatorWinch, true, spatial: true, library.WinchVolumeAtCar);
            carWinch.maxDistance = library.WinchReachMetres; // the whole site: heard from very far, quietly
            shipWinch = Make("Winch through the deck", library.ElevatorWinch, true, spatial: false, library.WinchVolumeOnShip);
            carBell = Make("Bell at the car", library.ElevatorDing, false, spatial: true, library.DingVolume);
            shipBell = Make("Bell on the deck", library.ElevatorDing, false, spatial: false, library.DingVolume);
        }

        private AudioSource Make(string name, AudioClip clip, bool loop, bool spatial, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip; source.loop = loop; source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f; source.maxDistance = 80f;
            source.dopplerLevel = 0f;
            AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
            if (devices != null) devices.Route(source);
            return source;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return;
            ElevatorState state = day.Elevator.State;
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            bool below = local != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Dive);
            bool onShip = local != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Sea);
            ElevatorController car = WorldSceneFlow.FindCar();
            if (car != null) carWinch.transform.position = carBell.transform.position = car.transform.position + Vector3.up * 1.5f;
            ShipParts ship = ShipParts.InWorld(WorldId.Sea);
            if (ship != null && ship.DeckCabin != null) shipBell.transform.position = shipWinch.transform.position = ship.DeckCabin.position + Vector3.up * 1.5f;

            bool moving = state == ElevatorState.Ascending || state == ElevatorState.Descending;
            // The winch only in the last seconds before the car arrives (Dan, 18
            // September 2026: "5 seconds before arriving, down and up"), faded in and
            // out; at the car for the divers, through the deck for the ship, lower
            // inside the car for the riders (the sound is for those left below).
            AudioLibrary library = AudioLibrary.Get();
            ElevatorPhase phase = day.Elevator;
            float duration = WorldSceneFlow.Instance != null && moving ? (float)WorldSceneFlow.Instance.NetworkManagerTime.TicksToTime(phase.DurationTicks) : 0f;
            float elapsed = WorldSceneFlow.Instance != null && moving ? WorldSceneFlow.Instance.ElapsedSince(phase.StartTick) : 0f;
            SecondsToArrival = moving ? Mathf.Max(0f, duration - elapsed) : 0f;
            bool arriving = moving && SecondsToArrival <= library.WinchSecondsBeforeArrival;
            float fade = library.WinchFadeSeconds <= 0f ? 1f : Mathf.Clamp01((library.WinchSecondsBeforeArrival - SecondsToArrival) / library.WinchFadeSeconds);
            bool riding = local != null && day.Riding && day.IsRider(local.OwnerId);
            carWinch.volume = (riding ? library.WinchVolumeInCar : library.WinchVolumeAtCar) * fade;
            shipWinch.volume = library.WinchVolumeOnShip * fade;
            Toggle(carWinch, arriving && below && car != null);
            Toggle(shipWinch, arriving && onShip);

            if (!primed) { lastState = state; primed = true; return; }
            if (state != lastState)
            {
                // Arrived and opening: the bell. At the bottom for those below; at the
                // top for the deck and for the riders still in the car (their doors open too).
                if (lastState == ElevatorState.Descending && state == ElevatorState.AtBottom && below && car != null) { carBell.Play(); DingsAtCar++; }
                if (lastState == ElevatorState.Ascending && state == ElevatorState.AtTop)
                {
                    if (onShip) { shipBell.Play(); DingsOnShip++; }
                    else if (below && car != null) { carBell.Play(); DingsAtCar++; }
                }
                lastState = state;
            }
        }

        private static void Toggle(AudioSource source, bool on)
        {
            if (source == null) return;
            if (on && !source.isPlaying) source.Play();
            else if (!on && source.isPlaying) source.Stop();
        }
    }
}
