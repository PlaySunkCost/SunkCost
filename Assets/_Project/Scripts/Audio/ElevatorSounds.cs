using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Audio
{
    // What the elevator sounds like (Dan, 17–18 September 2026): the winch for
    // the five seconds the car is near you — below: the last five going down
    // (arriving at you), the first five going up (leaving you); on the deck: the
    // first five going down, the last five going up — quiet but carrying across
    // the whole site for the divers below, low through the deck for the ship; a
    // rider inside the car is with it the whole way and hears it low throughout;
    // and a bell when it arrives and the doors open, below at the car and on the
    // deck at the cabin. Local presentation on
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
        // Seconds until the moving car arrives / since it left, from the replicated phase; 0 when it rests.
        public float SecondsToArrival { get; private set; }
        public float SecondsSinceDeparture { get; private set; }
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
            SecondsSinceDeparture = moving ? Mathf.Max(0f, elapsed) : 0f;
            bool riding = local != null && day.Riding && day.IsRider(local.OwnerId);
            // Near you: the car comes to your end (the last seconds) or leaves it (the first).
            float window = library.WinchSecondsBeforeArrival;
            bool up = state == ElevatorState.Ascending;
            bool towardBelow = !up, towardDeck = up;
            float nearBelow = moving ? Near(towardBelow, window) : 0f;   // 0..1, faded at the edges
            float nearDeck = moving ? Near(towardDeck, window) : 0f;
            carWinch.volume = riding ? library.WinchVolumeInCar : library.WinchVolumeAtCar * nearBelow;
            shipWinch.volume = library.WinchVolumeOnShip * nearDeck;
            Toggle(carWinch, moving && below && car != null && (riding || nearBelow > 0f));
            Toggle(shipWinch, moving && onShip && nearDeck > 0f);

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

        // How near the car is to an end in time: 1 inside the window, fading over
        // WinchFadeSeconds at the window's edge, 0 outside. Toward that end the
        // window is the last `window` seconds; away from it, the first.
        private float Near(bool toward, float window)
        {
            AudioLibrary library = AudioLibrary.Get();
            float fade = Mathf.Max(0.01f, library.WinchFadeSeconds);
            float inside = toward ? window - SecondsToArrival : window - SecondsSinceDeparture; // > 0 inside the window
            return Mathf.Clamp01(inside / fade);
        }

        private static void Toggle(AudioSource source, bool on)
        {
            if (source == null) return;
            if (on && !source.isPlaying) source.Play();
            else if (!on && source.isPlaying) source.Stop();
        }
    }
}
