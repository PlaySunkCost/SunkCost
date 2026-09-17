using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Scened;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The cabin ride (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md sections 5.3, 5.4 and
    // 6, without the day rules: Dan, 15 September 2026, "start without day state,
    // just going up and down as we wish"). Down: E on the deck cabin's button at
    // sea takes everyone standing in the cabin — doors close, the suit fade, the
    // riders wake inside the seafloor car at the top of its shaft, the car
    // descends, doors open. Up: E on the car's panel at the bottom takes everyone
    // inside the car — doors close, the car climbs, at the top the riders are
    // moved into the deck cabin (same spot, dry, doors still shut) and its doors
    // open. The car then goes back down empty if anyone is still below.
    //
    // Server: the only writer of CabinRideState, ElevatorPhase and the rosters on
    // CrewDayState, and the only orchestrator; the same cohort/ack machinery as
    // the ship trip. Every peer: drives its copy of the car from ElevatorPhase and
    // sweeps the deck cabin's doors from the ride state. Owner clients: lock at
    // the captured cabin-frame spot, follow the moving car, place themselves in
    // the other cabin after the scene move.
    public sealed partial class WorldSceneFlow
    {
        private Coroutine ride;
        private bool riding; // server view
        private ElevatorController cachedCar;
        private float deckDoorHalfAngle = float.NaN;

        public bool Riding => riding;

        // ---- shared -----------------------------------------------------------------

        public static ElevatorController FindCar()
        {
            Scene dive = WorldScenes.Scene(WorldId.Dive);
            if (!dive.IsValid() || !dive.isLoaded) return null;
            foreach (GameObject root in dive.GetRootGameObjects())
            {
                ElevatorController car = root.GetComponentInChildren<ElevatorController>(true);
                if (car != null) return car;
            }
            return null;
        }

        private ElevatorController Car()
        {
            if (cachedCar == null || !cachedCar.gameObject.scene.isLoaded) cachedCar = FindCar();
            return cachedCar;
        }

        // FindCar for every loose item every frame: found once per frame.
        private static ElevatorController frameCar;
        private static int frameCarFrame = -1;
        public static ElevatorController FindCarCached()
        {
            if (frameCarFrame != Time.frameCount || (frameCar != null && !frameCar.gameObject.scene.isLoaded)) { frameCar = FindCar(); frameCarFrame = Time.frameCount; }
            return frameCar;
        }

        // The frame a rider of this ride stands in, decided by the scene its player
        // object is in: the deck cabin on the ship, the car at the seafloor.
        public static CabinFrame RideFrameFor(CabinRideState state, Scene playerScene)
        {
            if (playerScene == WorldScenes.Scene(WorldId.Dive)) return CabinFrame.Car(FindCar());
            return CabinFrame.DeckCabin(ShipParts.InWorld(WorldId.Sea));
        }

        // Seconds since a tick, read every frame: whole ticks plus the time into the
        // current one, so a car or a door driven from it glides instead of stepping
        // once per tick (Dan, 15 September 2026: "everything jumps in place").
        private float ElapsedSince(uint startTick)
        {
            if (networkManager == null || networkManager.TimeManager == null) return 0f;
            FishNet.Managing.Timing.TimeManager time = networkManager.TimeManager;
            uint now = time.Tick;
            if (now < startTick) return 0f;
            return (float)(time.TicksToTime(now - startTick) + time.GetTickElapsedAsDouble());
        }

        private float CarSealSeconds => Car() != null ? Car().DoorSealSeconds : Settings.CarSealSecondsFallback;
        private float CarTravelSeconds => Car() != null ? Car().TravelSecondsOneWay : Settings.CarTravelSecondsFallback;

        private void Update()
        {
            if (dayState == null || networkManager == null) return;
            if (networkManager.IsServerStarted) { ServerTickElevator(); ServerTickCarReturn(); ServerSumBox(); ServerTickSpectators(); }
            DriveCar();
            if (networkManager.IsServerStarted && riding) ServerFollowCabinCargo();
            PresentDeckCabin();
            PresentSky();
        }

        // ---- the storage room and the pay button (server) -------------------------------

        private float nextBoxSum;

        // What lies loose in the storage room of the ship in the current world, four
        // times a second, into the day state for every peer's readouts.
        private void ServerSumBox()
        {
            if (Time.unscaledTime < nextBoxSum) return;
            nextBoxSum = Time.unscaledTime + 0.25f;
            ShipParts ship = ShipParts.InWorld(currentWorld);
            dayState.ServerSetBoxValue(ship != null ? StorageReadout.SumInside(ship) : 0);
        }

        // E on the monitor's End day (Dan, 16 September 2026): the crew ends the day
        // once everyone is up; the day state refuses otherwise.
        public bool ServerEndDay(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (transitioning || dayState.Travelling) { why = "Ship travelling"; return false; }
            if (riding) { why = "Cabin in use"; return false; }
            if (currentWorld != WorldId.Sea) { why = "Not at sea"; return false; }
            if (dayState.Below.Count > 0) { why = DiveInProgressText(); return false; }
            if (!dayState.ServerEndDay(Settings.DaysPerCycle, out why)) return false;
            Debug.Log($"[WorldSceneFlow] Day ended by {DisplayName(sender)}: day {dayState.Day}{(dayState.Payday ? " PAYDAY" : string.Empty)}");
            ServerReviveAll(); // the dead stand up on the deck (card 1)
            return true;
        }

        // E on the HQ board (Dan, 16 September 2026): sell the box, pay the quota.
        // Only at the dock, only with a cycle to pay for; the day state decides
        // paid or lost and resets the count either way.
        public bool ServerPay(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (transitioning || dayState.Travelling) { why = "Ship travelling"; return false; }
            if (currentWorld != WorldId.HQ || dayState.Phase != DayPhase.AtHQ) { why = "Not docked at HQ"; return false; }
            HQPlayerController presser = PlayerOf(sender);
            if (presser == null || presser.gameObject.scene != WorldScenes.Scene(WorldId.HQ)) { why = "Not at HQ"; return false; }
            if (dayState.Day == 0 && !dayState.Payday) { why = "Nothing to pay yet — dive first"; return false; }
            ShipParts ship = ShipParts.InWorld(WorldId.HQ);
            if (ship == null) { why = "No ship at the dock."; return false; }
            int sales = ServerSellStorage(ship);
            PayReport report = dayState.ServerPay(sales, Settings.QuotaPerCycle);
            dayState.ServerSetBoxValue(0);
            Debug.Log($"[WorldSceneFlow] Pay: sold ${report.Sales}, quota ${report.Quota}, had ${report.Had} — {(report.Paid ? "paid, balance $" + report.Balance : "GAME LOST")} (pressed by {DisplayName(sender)})");
            return true;
        }

        // Every loose item inside the room goes: its value is the sale.
        private int ServerSellStorage(ShipParts ship)
        {
            int sum = 0;
            var sold = new List<CarryableItem>();
            foreach (CarryableItem item in CarryableItem.Spawned)
            {
                if (item == null || !item.IsSpawned || !item.CanGrabFromWorld) continue;
                if (item.gameObject.scene != ship.gameObject.scene || !ship.IsInStorageRoom(item.transform.position)) continue;
                sold.Add(item);
            }
            foreach (CarryableItem item in sold)
            {
                sum += item.Value;
                item.NetworkObject.Despawn();
            }
            return sum;
        }

        // ---- cabin cargo (server) ------------------------------------------------------

        // The cabin the frozen cargo is in: the one it was frozen in until the
        // scene move, the other one after. Riders are placed by themselves; cargo
        // is placed by the server, here.
        private CabinFrame cargoFrame;

        private void ServerFreezeCabinCargo(CabinFrame frame, System.Func<Vector3, bool> inside)
        {
            cargoFrame = frame;
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude))
            {
                if (!item.IsSpawned || item.NetworkObject.IsSceneObject || item.transform.parent != null) continue;
                if (!item.CanGrabFromWorld || item.InTransit || cargo.Contains(item)) continue;
                if (!inside(item.transform.position)) continue;
                item.ServerBeginCabinTransit(serial, frame);
                cargo.Add(item);
            }
        }

        private void ServerFollowCabinCargo()
        {
            if (!cargoFrame.IsValid) return;
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerFollowCabinTransit(cargoFrame);
        }

        private void ServerPlaceCabinCargo(CabinFrame frame)
        {
            cargoFrame = frame;
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerPlaceAfterCabinTransit(frame);
        }

        // A loose item on the car's floor, for the freeze: the rider volume starts at
        // the floor, so probe a little above where it lies.
        private static bool InsideCarForCargo(ElevatorController car, Vector3 position) => car != null && car.IsInsideCar(position + Vector3.up * 0.25f);

        // Under water there is no sky: while the local player is in the dive world
        // its camera clears to the fog colour instead of the skybox (fog never
        // touches a skybox, so the seafloor would otherwise sit under a bright
        // blue sky). The world scenes' own fog and ambient come with the active
        // scene as before.
        private void PresentSky()
        {
            HQPlayerController local = LocalPlayer();
            Camera camera = local != null ? local.PlayerCamera : null;
            if (camera == null) return;
            bool underWater = local.gameObject.scene == WorldScenes.Scene(WorldId.Dive);
            CameraClearFlags flags = underWater ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (camera.clearFlags != flags) camera.clearFlags = flags;
            if (underWater && camera.backgroundColor != RenderSettings.fogColor) camera.backgroundColor = RenderSettings.fogColor;
            // The headlamp belongs to the dive: on below, off again on the deck
            // (DiveSiteHeadlampActivator only ever switches it on, for a player that
            // spawns at the site).
            local.SetHeadlampEnabled(underWater);
        }

        // Riders are locked only across the scene swaps and the fades; once the
        // ride is under way they walk inside the moving car (design: "you can walk
        // around inside the cabin the whole way").
        public static bool RidersLockedDuring(CabinRideState state)
        {
            if (!state.Active) return false;
            if (state.Direction == RideDirection.Down) return state.Stage != CabinRideStage.Riding;
            return state.Stage == CabinRideStage.Preparing || state.Stage >= CabinRideStage.Loading;
        }

        // Every peer with the site loaded moves its car from the authoritative phase;
        // the local player inside it is carried along (the CharacterController does
        // not follow a moving floor on its own).
        //
        // Physics.autoSyncTransforms is off, so the car's colliders stay where the last
        // physics step left them until something syncs them. A rider swept against that
        // stale floor sank into it going up (the collider lagged below the visual floor,
        // the ground stick pushed the capsule into the gap, the next step popped it out:
        // "the floor is jumping", Dan, 16 September 2026) and floated above it going
        // down. So the order per direction keeps every sweep against free space: up,
        // carry the rider first (the stale floor is below), then sync; down, sync first
        // (the floor is gone from under the feet), then carry the rider down onto it.
        private void DriveCar()
        {
            ElevatorController car = Car();
            if (car == null) return;
            ElevatorPhase phase = dayState.Elevator;
            Vector3 before = car.transform.position;
            car.SetDrivenPhase(phase.State, phase.Upward, ElapsedSince(phase.StartTick));
            Vector3 delta = car.transform.position - before;
            if (delta == Vector3.zero) return;
            HQPlayerController local = LocalPlayer();
            bool carried = local != null && !local.TravelLocked && local.gameObject.scene == car.gameObject.scene && car.IsInsideCar(local.transform.position + Vector3.up * 0.5f);
            if (delta.y > 0f)
            {
                if (carried) local.CarryNow(delta);
                CarryLooseBodies(car, delta);
                Physics.SyncTransforms();
            }
            else
            {
                Physics.SyncTransforms();
                if (carried) local.CarryNow(delta);
                CarryLooseBodies(car, delta);
            }
        }

        // Bodies this peer simulates inside the moving car (a ball just thrown by
        // the local player; on the server a loose item not yet at rest) move with
        // the car's frame, in the rider's order against the floor.
        private static void CarryLooseBodies(ElevatorController car, Vector3 delta)
        {
            foreach (CarryableItem item in CarryableItem.Spawned)
            {
                if (item == null || !item.SimulatesHere) continue;
                Vector3 p = item.transform.position;
                if (car.IsInsideCar(p) || car.IsInsideCar(p + Vector3.up * 0.25f)) item.CarryWithCar(delta);
            }
        }

        // Cabin cargo grabbed mid-ride: out of the cargo list, transit over, so the
        // follow no longer fights the hand. Server only (ServerGrab calls it).
        public void ServerReleaseCabinCargo(CarryableItem item)
        {
            if (networkManager == null || !networkManager.IsServerStarted || item == null) return;
            cargo.Remove(item);
            item.ServerEndDeckTransit();
        }

        // The deck cabin's doors on the ship at sea: open while the car is up and
        // idle, closing as a ride seals or the car leaves, opening as riders arrive.
        private void PresentDeckCabin()
        {
            PresentDeckCabin(ShipParts.InWorld(WorldId.Sea), DeckCabinOpenFraction());
            PresentDeckCabin(ShipParts.InWorld(WorldId.HQ), 1f); // docked: a cabin that never leaves
        }

        private void PresentDeckCabin(ShipParts ship, float open)
        {
            if (ship == null) return;
            Transform doorL = ship.DeckCabinDoorL, doorR = ship.DeckCabinDoorR;
            if (doorL == null || doorR == null) return;
            if (float.IsNaN(deckDoorHalfAngle)) deckDoorHalfAngle = Mathf.Abs(Mathf.DeltaAngle(0f, doorR.localEulerAngles.y)); // parked open by the builder
            // The car itself is only on the deck while it is up: its doors and its own
            // glass show then; away, the glass housing stands empty and see-through.
            bool present = DeckCabinCarPresent(ship);
            SetVisible(doorL, present);
            SetVisible(doorR, present);
            SetVisible(ship.DeckCabinCarGlass, present);
            doorR.localRotation = Quaternion.Euler(0f, -deckDoorHalfAngle * open, 0f);
            doorL.localRotation = Quaternion.Euler(0f, deckDoorHalfAngle * open, 0f);
            // The doorway is passable only with the doors fully open: nobody walks into
            // the housing while the car is away or the doors are moving.
            Collider doorway = ship.DeckCabinDoorCollider;
            if (doorway != null)
            {
                bool blocks = open < 0.999f;
                if (doorway.enabled != blocks) doorway.enabled = blocks;
            }
            TextMesh panel = ship.DeckCabinPanel;
            if (panel != null)
            {
                string text = DeckCabinText();
                if (panel.text != text) panel.text = text;
            }
        }

        private static void SetVisible(Transform part, bool visible)
        {
            if (part == null) return;
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled != visible) renderer.enabled = visible;
        }

        // The docked ship's cabin never leaves. At sea the car is on the deck while
        // it is up, while the deck doors are sealing for a ride down, and once it
        // is back up with riders arriving.
        private bool DeckCabinCarPresent(ShipParts ship)
        {
            if (dayState == null || ship != ShipParts.InWorld(WorldId.Sea)) return true;
            CabinRideState state = dayState.CabinRide;
            if (state.Active)
                return state.Direction == RideDirection.Down ? state.Stage <= CabinRideStage.Sealing : state.Stage >= CabinRideStage.Loading;
            ElevatorPhase car = dayState.Elevator;
            return car.State == ElevatorState.AtTop || (car.State == ElevatorState.Sealing && !car.Upward);
        }

        public float DeckCabinOpenFraction()
        {
            if (dayState == null) return 1f;
            CabinRideState state = dayState.CabinRide;
            float seal = Mathf.Max(Settings.CabinSealSeconds, 0.0001f);
            if (state.Active)
            {
                float t = ElapsedSince(state.StageStartTick);
                if (state.Direction == RideDirection.Down)
                    return state.Stage == CabinRideStage.Preparing ? 1f : state.Stage == CabinRideStage.Sealing ? 1f - Mathf.Clamp01(t / seal) : 0f;
                return state.Stage == CabinRideStage.Arriving ? Mathf.Clamp01(t / seal) : 0f;
            }
            ElevatorPhase car = dayState.Elevator;
            float e = ElapsedSince(car.StartTick);
            switch (car.State)
            {
                case ElevatorState.AtTop: return Mathf.Clamp01(e / seal);
                case ElevatorState.Sealing: return car.Upward ? 0f : 1f - Mathf.Clamp01(e / seal);
                default: return 0f;
            }
        }

        private string DeckCabinText()
        {
            if (dayState == null) return string.Empty;
            if (Time.unscaledTime - dayState.LastRefusalAt < Settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(dayState.LastRefusal.Text))
                return dayState.LastRefusal.Text;
            if (dayState.Riding) return dayState.CabinRide.Direction == RideDirection.Down ? "Going down…" : "Coming up…";
            if (dayState.Payday) return "Payday — sail home";
            if (dayState.Phase == DayPhase.DiveInProgress && dayState.Elevator.State == ElevatorState.AtTop && dayState.Below.Count > 0) return "Step out — the car is needed below";
            if (dayState.Phase == DayPhase.DiveInProgress) return DiveInProgressText();
            if (dayState.DiveDone) return "Dive done — end the day at the monitor";
            if (dayState.Elevator.State != ElevatorState.AtTop) return "Cabin below";
            return dayState.World == WorldId.Sea ? $"Day {dayState.Day} of {Settings.DaysPerCycle} — all in, press E to descend" : "Not at sea";
        }

        // Mid-day the deck button is dead: once you are up you cannot go down
        // again until the next day (design section 1); the panel says who is below.
        private string DiveInProgressText()
        {
            var names = new List<string>();
            foreach (int id in dayState.Below) names.Add(DisplayName(id));
            return $"Dive in progress — {names.Count} below: {string.Join(", ", names)}";
        }

        // ---- server: the car's clock ---------------------------------------------------

        private void ServerSetElevator(ElevatorState state, bool upward)
        {
            float seconds = state == ElevatorState.Sealing ? CarSealSeconds
                : state == ElevatorState.Descending || state == ElevatorState.Ascending ? CarTravelSeconds : 0f;
            dayState.ServerSetElevator(new ElevatorPhase
            {
                Serial = dayState.Elevator.Serial + 1,
                State = state,
                Upward = upward,
                StartTick = networkManager.TimeManager.Tick,
                DurationTicks = seconds <= 0f ? 0 : networkManager.TimeManager.TimeToTicks(seconds)
            });
        }

        // Timed transitions of the car: the seal resolves into the move, the move
        // ends at the landing. Requests come from the ride routines only.
        private void ServerTickElevator()
        {
            ElevatorPhase phase = dayState.Elevator;
            float elapsed = ElapsedSince(phase.StartTick);
            switch (phase.State)
            {
                case ElevatorState.Sealing:
                    if (elapsed < CarSealSeconds) break;
                    // An empty car going back down: someone who stepped in while the
                    // doors closed opens them again — it never leaves with anyone inside.
                    if (!phase.Upward && !riding && CabinOccupied(ShipParts.InWorld(WorldId.Sea))) { ServerSetElevator(ElevatorState.AtTop, true); break; }
                    ServerSetElevator(phase.Upward ? ElevatorState.Ascending : ElevatorState.Descending, phase.Upward);
                    break;
                case ElevatorState.Descending:
                    if (elapsed >= CarTravelSeconds) ServerSetElevator(ElevatorState.AtBottom, false);
                    break;
                case ElevatorState.Ascending:
                    if (elapsed >= CarTravelSeconds) ServerSetElevator(ElevatorState.AtTop, true);
                    break;
            }
        }

        private IEnumerator WaitForCar(ElevatorState state, float timeoutSeconds)
        {
            float deadline = Time.unscaledTime + timeoutSeconds;
            while (Time.unscaledTime < deadline && dayState.Elevator.State != state) yield return null;
        }

        // ---- server: requests ------------------------------------------------------------

        private void SetRide(CabinRideStage stage, RideDirection direction, float seconds)
        {
            uint ticks = seconds <= 0f ? 0 : networkManager.TimeManager.TimeToTicks(seconds);
            dayState.ServerSetCabinRide(new CabinRideState
            {
                Serial = serial,
                Stage = stage,
                Direction = direction,
                StageStartTick = networkManager.TimeManager.Tick,
                StageDurationTicks = ticks
            });
        }

        private bool ServerRideAllowed(out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (transitioning || dayState.Travelling) { why = "Ship travelling"; return false; }
            if (riding) { why = "Cabin in use"; return false; }
            CrewSpawner spawner = FindAnyObjectByType<CrewSpawner>();
            if (spawner != null && spawner.PendingCount > 0) { why = "Someone is still joining."; return false; }
            return true;
        }

        // E on the deck cabin's button: everyone standing in the cabin goes down.
        public bool ServerRequestDive(NetworkConnection sender, out string why)
        {
            if (!ServerRideAllowed(out why)) return false;
            if (currentWorld != WorldId.Sea) { why = "Not at sea"; return false; }
            ShipParts ship = ShipParts.InWorld(WorldId.Sea);
            if (ship == null || ship.DeckCabin == null) { why = "No deck cabin."; return false; }
            HQPlayerController presser = PlayerOf(sender);
            if (presser == null || presser.gameObject.scene != WorldScenes.Scene(WorldId.Sea) || !ship.IsInDeckCabin(presser.transform.position)) { why = "Step inside the cabin first"; return false; }
            // The day rules (Dan, 16 September 2026): no dive on payday; none while
            // a day is in progress (whoever surfaced waits for the others); and the
            // day starts only with everyone in the cabin, named otherwise.
            if (dayState.Payday) { why = "Payday — sail home"; return false; }
            if (dayState.Phase == DayPhase.DiveInProgress) { why = DiveInProgressText(); return false; }
            if (dayState.DiveDone) { why = "Dive done — end the day at the monitor"; return false; }
            var missing = new List<string>();
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || !conn.IsAuthenticated || dayState.IsDead(conn.ClientId)) continue; // the dead are not waited for
                HQPlayerController player = PlayerOf(conn);
                if (player == null || player.gameObject.scene != WorldScenes.Scene(WorldId.Sea) || !ship.IsInDeckCabin(player.transform.position)) missing.Add(DisplayName(conn));
            }
            if (missing.Count > 0) { why = "Waiting for: " + string.Join(", ", missing); return false; }
            if (dayState.Elevator.State != ElevatorState.AtTop)
            {
                // The car is away. Empty at the bottom with nobody below: call it up.
                if (dayState.Elevator.State == ElevatorState.AtBottom && dayState.Below.Count == 0) { ServerSetElevator(ElevatorState.Ascending, true); why = "Cabin coming up"; return false; }
                why = "Cabin below";
                return false;
            }
            var riders = new List<int>();
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || dayState.IsDead(conn.ClientId)) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player != null && player.gameObject.scene == WorldScenes.Scene(WorldId.Sea) && ship.IsInDeckCabin(player.transform.position)) riders.Add(conn.ClientId);
            }
            Debug.Log("Cabin ride: down with " + string.Join(",", riders) + " requested by " + DisplayName(sender) + "\n" + System.Environment.StackTrace);
            ride = StartCoroutine(DiveRoutine(riders, ship));
            return true;
        }

        // E on the car's panel at the bottom: everyone inside the car comes up.
        public bool ServerRequestSurface(NetworkConnection sender, out string why)
        {
            if (!ServerRideAllowed(out why)) return false;
            if (dayState.Elevator.State != ElevatorState.AtBottom) { why = dayState.Elevator.State == ElevatorState.AtTop ? "Cabin is up" : "Cabin moving"; return false; }
            ElevatorController car = Car();
            if (car == null) { why = "No car."; return false; }
            HQPlayerController presser = PlayerOf(sender);
            if (presser == null || presser.gameObject.scene != WorldScenes.Scene(WorldId.Dive) || !car.IsInsideCar(presser.transform.position)) { why = "Step inside the cabin first"; return false; }
            var riders = new List<int>();
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player != null && player.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && car.IsInsideCar(player.transform.position)) riders.Add(conn.ClientId);
            }
            ride = StartCoroutine(SurfaceRoutine(riders, car));
            return true;
        }

        private void ServerCapturePlacements(CabinFrame frame)
        {
            foreach (NetworkConnection conn in ActiveCohort())
            {
                HQPlayerController player = PlayerOf(conn);
                if (player == null || !frame.IsValid) continue;
                dayState.ServerSetPlacement(new RiderPlacement { ClientId = conn.ClientId, Local = frame.ToLocal(player.transform.position), Yaw = frame.ToYaw(player.Yaw) });
            }
        }

        private IEnumerator BeginRide(List<int> riders, RideDirection direction)
        {
            riding = true;
            serial++;
            ResetTrip();
            lastFailure = string.Empty;
            foreach (int id in riders) cohort.Add(id);
            dayState.ServerSetRiders(riders);
            SetRide(CabinRideStage.Preparing, direction, Settings.PrepareTimeoutSeconds);
            float deadline = Time.unscaledTime + Settings.PrepareTimeoutSeconds;
            while (Time.unscaledTime < deadline && !AllAcked(prepared)) yield return null;
        }

        private IEnumerator CancelRide(RideDirection direction, string why)
        {
            dayState.ServerReportRefusal(why);
            lastFailure = why;
            SetRide(CabinRideStage.Cancelled, direction, 0f);
            dayState.ServerClearRiders();
            ResetTrip();
            riding = false;
            ride = null;
            yield break;
        }

        private void EndRide(RideDirection direction)
        {
            SetRide(CabinRideStage.Complete, direction, 0f);
            dayState.ServerClearRiders();
            ResetTrip();
            riding = false;
            ride = null;
        }

        // Down (plan section 5.3, no day begun).
        private IEnumerator DiveRoutine(List<int> riders, ShipParts ship)
        {
            yield return BeginRide(riders, RideDirection.Down);
            if (!AllAcked(prepared)) { yield return CancelRide(RideDirection.Down, "Not ready: " + Missing(prepared)); yield break; }
            yield return WaitTicks(Settings.SyncFlushTicks);
            ServerCapturePlacements(CabinFrame.DeckCabin(ship));
            ServerFreezeCabinCargo(CabinFrame.DeckCabin(ship), p => ship.IsInDeckCabin(p));

            SetRide(CabinRideStage.Sealing, RideDirection.Down, Settings.CabinSealSeconds);
            yield return WaitSeconds(Settings.CabinSealSeconds);

            SetRide(CabinRideStage.FadingOut, RideDirection.Down, Settings.SuitFadeSeconds);
            float deadline = Time.unscaledTime + Settings.SuitFadeSeconds + Settings.ArrivalTimeoutSeconds;
            while (Time.unscaledTime < deadline && !AllAcked(black)) yield return null;
            if (!AllAcked(black)) ServerKickUnresponsive(black, "Cabin ride: no black acknowledgement");

            // The site on the server first (fresh when nobody was below), then the car.
            SetRide(CabinRideStage.Loading, RideDirection.Down, 0f);
            deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            if (!WorldScenes.IsLoaded(WorldId.Dive))
            {
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(LoadDataFor(WorldId.Dive, null));
                while (!WorldScenes.IsLoaded(WorldId.Dive) && Time.unscaledTime < deadline) yield return null;
            }
            cachedCar = null;
            ElevatorController car = Car();
            if (car == null) { yield return CancelRide(RideDirection.Down, "No car in " + WorldScenes.DiveName); yield break; }
            if (dayState.Elevator.State != ElevatorState.AtTop) ServerSetElevator(ElevatorState.AtTop, true); // a fresh site: the car waits at the top, closed
            // Whatever became loose on the cabin's floor since the seal (dropped, put
            // down after a grab) crosses too: freeze again just before the move list closes.
            ServerFreezeCabinCargo(CabinFrame.DeckCabin(ship), p => ship.IsInDeckCabin(p));
            ServerBuildMoveList();
            Scene destination = WorldScenes.Scene(WorldId.Dive);
            var conns = ActiveCohort();
            foreach (NetworkConnection conn in conns) ServerUnwatch(conn); // a TV viewer drops the watched site first: the move is a plain load (card 3)
            foreach (NetworkConnection conn in conns) networkManager.SceneManager.AddConnectionToScene(conn, destination);
            EnsureHolderKeepAlive();
            networkManager.SceneManager.LoadConnectionScenes(conns.ToArray(), LoadDataFor(WorldId.Dive, moved.ToArray()));
            while (Time.unscaledTime < deadline && !AllAcked(arrived)) yield return null;
            if (!AllAcked(arrived)) ServerKickUnresponsive(arrived, "Cabin ride: never arrived in the car");
            ServerPlaceCabinCargo(CabinFrame.Car(car)); // the deck cabin's floor cargo, now on the car's floor
            foreach (NetworkConnection conn in ActiveCohort()) dayState.ServerSetBelow(conn.ClientId, true);
            if (!dayState.ServerBeginDay(out string dayWhy)) Debug.LogWarning("[WorldSceneFlow] The day did not begin: " + dayWhy);
            networkManager.SceneManager.UnloadConnectionScenes(ActiveCohort().ToArray(), UnloadDataFor(WorldId.Sea, keepOnServer: true));

            // Eyes open inside the car at the top; then the ride itself.
            SetRide(CabinRideStage.Arriving, RideDirection.Down, Settings.SuitFadeSeconds);
            ServerCapturePlacements(CabinFrame.Car(car));
            yield return WaitSeconds(Settings.SuitFadeSeconds);
            SetRide(CabinRideStage.Riding, RideDirection.Down, CarTravelSeconds); // riders walk in the descending car
            ServerSetElevator(ElevatorState.Descending, false);
            yield return WaitForCar(ElevatorState.AtBottom, CarTravelSeconds + Settings.ArrivalTimeoutSeconds);
            EndRide(RideDirection.Down);
        }

        // Up (plan section 5.4, no day ended).
        private IEnumerator SurfaceRoutine(List<int> riders, ElevatorController car)
        {
            yield return BeginRide(riders, RideDirection.Up);
            if (!AllAcked(prepared)) { yield return CancelRide(RideDirection.Up, "Not ready: " + Missing(prepared)); yield break; }
            yield return WaitTicks(Settings.SyncFlushTicks);
            ServerCapturePlacements(CabinFrame.Car(car));
            ServerFreezeCabinCargo(CabinFrame.Car(car), p => InsideCarForCargo(car, p));

            SetRide(CabinRideStage.Sealing, RideDirection.Up, CarSealSeconds);
            ServerSetElevator(ElevatorState.Sealing, true);
            yield return WaitForCar(ElevatorState.Ascending, CarSealSeconds + Settings.ArrivalTimeoutSeconds);
            // The doors are shut and the car is climbing: whoever pressed the button
            // and then stepped out while the doors were closing is not aboard. They
            // stay below (the car comes back for them) instead of being moved to the
            // deck cabin from the seafloor (Dan, 15 September 2026).
            ServerDropRidersOutside(car);
            SetRide(CabinRideStage.Riding, RideDirection.Up, CarTravelSeconds);
            yield return WaitForCar(ElevatorState.AtTop, CarTravelSeconds + Settings.ArrivalTimeoutSeconds);

            // Dry, stopped, doors shut: the riders change scene into the deck cabin.
            SetRide(CabinRideStage.Loading, RideDirection.Up, 0f);
            ServerCapturePlacements(CabinFrame.Car(car)); // where everyone ended up after walking about
            float deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            ShipParts ship = ShipParts.InWorld(WorldId.Sea);
            if (ship == null) { yield return CancelRide(RideDirection.Up, "No ship at sea"); yield break; }
            var conns = ActiveCohort();
            if (conns.Count > 0) // an empty car (everyone stepped out at the seal) moves nobody
            {
                // Items thrown or put down during the ride are loose on the car's floor
                // now, not cargo: freeze them too, or they stay in the site and go with it
                // when it unloads (Dan, 16 September 2026: "some of the items on the
                // ground disappeared" at the top).
                ServerFreezeCabinCargo(CabinFrame.Car(car), p => InsideCarForCargo(car, p));
                ServerBuildMoveList();
                Scene destination = WorldScenes.Scene(WorldId.Sea);
                foreach (NetworkConnection conn in conns) ServerUnwatch(conn); // nobody living below watches the ship; a no-op kept symmetric with the ride down
                foreach (NetworkConnection conn in conns) networkManager.SceneManager.AddConnectionToScene(conn, destination);
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(conns.ToArray(), LoadDataFor(WorldId.Sea, moved.ToArray()));
                while (Time.unscaledTime < deadline && !AllAcked(arrived)) yield return null;
                if (!AllAcked(arrived)) ServerKickUnresponsive(arrived, "Cabin ride: never arrived in the deck cabin");
                ServerPlaceCabinCargo(CabinFrame.DeckCabin(ship)); // the car's floor cargo, now on the deck cabin's floor
                foreach (NetworkConnection conn in ActiveCohort()) dayState.ServerSetBelow(conn.ClientId, false);
            }
            bool othersBelow = dayState.Below.Count > 0;
            if (!othersBelow) dayState.ServerEndDayIfDone(Settings.DaysPerCycle); // the last living one up: the dive is done
            // The dead in the site ride to the ship first, hidden; their clients join the unload.
            if (!othersBelow) yield return ServerMoveDeadToShip();
            NetworkConnection[] unloaders = SiteUnloaders(ActiveCohort(), closing: !othersBelow);
            if (conns.Count > 0 || unloaders.Length > 0) networkManager.SceneManager.UnloadConnectionScenes(unloaders, UnloadDataFor(WorldId.Dive, keepOnServer: othersBelow));
            if (!othersBelow) cachedCar = null;

            SetRide(CabinRideStage.Arriving, RideDirection.Up, Settings.CabinSealSeconds);
            ServerCapturePlacements(CabinFrame.DeckCabin(ship));
            yield return WaitSeconds(Settings.CabinSealSeconds);
            EndRide(RideDirection.Up);
            // Someone is still down there: the car goes back for them, empty — once
            // the riders have stepped out of the deck cabin, whose doors are shut
            // while the car is away (Dan, 17 September 2026: it sealed them in).
            // ServerTickCarReturn picks it up from here.
        }

        // The car never leaves with a living player in the deck cabin (Dan, 17
        // September 2026: "what can happen, and how to deny it"). It waits up for
        // the grace period; whoever is still inside then is put out on the deck
        // (a server placement, nobody is ever carried by accident) and once the
        // cabin reads clear it seals and goes down for those below. Someone who
        // steps in while the doors close makes them open again (ServerTickElevator).
        private bool carReturnPending;
        private IEnumerator ServerReturnCarWhenClear(ShipParts ship)
        {
            if (carReturnPending) yield break;
            carReturnPending = true;
            try
            {
                float grace = Time.unscaledTime + Settings.CarReturnGraceSeconds;
                while (CarReturnWanted() && Time.unscaledTime < grace && CabinOccupied(ship)) yield return null;
                if (!CarReturnWanted()) yield break;
                if (CabinOccupied(ship))
                {
                    ServerPutCabinOccupantsOut(ship);
                    float settle = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
                    while (CarReturnWanted() && Time.unscaledTime < settle && CabinOccupied(ship)) yield return null; // their replicated positions catch up
                }
                if (!CarReturnWanted() || CabinOccupied(ship)) yield break; // still someone in there: try again from the tick
                ServerSetElevator(ElevatorState.Sealing, false);
            }
            finally { carReturnPending = false; }
        }

        // Divers below, no ride running, the car up and not sealed for a ride: it is owed below.
        private bool CarReturnWanted() => dayState != null && dayState.Below.Count > 0 && !riding && !siteClosing && dayState.Elevator.State == ElevatorState.AtTop;

        private bool CabinOccupied(ShipParts ship)
        {
            if (ship == null) return false;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || dayState.IsDead(conn.ClientId)) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player != null && player.gameObject.scene == ship.gameObject.scene && ship.IsInDeckCabin(player.transform.position)) return true;
            }
            return false;
        }

        // Everyone living still in the deck cabin is placed at a deck spawn point.
        private void ServerPutCabinOccupantsOut(ShipParts ship)
        {
            int k = 0;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || dayState.IsDead(conn.ClientId)) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null || player.gameObject.scene != ship.gameObject.scene || !ship.IsInDeckCabin(player.transform.position)) continue;
                Transform point = ship.SpawnPoint(k++ % ShipParts.SpawnPointCount) ?? ship.SpawnPoint(0);
                if (point == null) continue;
                player.TargetPlace(conn, point.position, point.eulerAngles.y);
                Debug.Log($"[WorldSceneFlow] {DisplayName(conn)} put out of the deck cabin: the car is needed below");
            }
        }

        // Every server tick while the car is up and owed below: keep the return
        // going (a wait that gave up because someone kept stepping in starts over).
        private void ServerTickCarReturn()
        {
            if (!CarReturnWanted() || carReturnPending) return;
            ShipParts ship = ShipParts.InWorld(WorldId.Sea);
            if (ship != null) StartCoroutine(ServerReturnCarWhenClear(ship));
        }

        // Everyone in the cohort who is not standing inside the sealed car leaves the
        // ride: out of the cohort, the rider list and the placements; still listed
        // below. The client sees itself unlisted and stops tracking the car.
        private void ServerDropRidersOutside(ElevatorController car)
        {
            foreach (NetworkConnection conn in ActiveCohort())
            {
                HQPlayerController player = PlayerOf(conn);
                bool aboard = player != null && player.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && car.IsInsideCar(player.transform.position);
                if (aboard) continue;
                cohort.Remove(conn.ClientId);
                dayState.ServerRemoveRider(conn.ClientId);
                Debug.Log($"[WorldSceneFlow] Cabin ride {serial}: client {conn.ClientId} stepped out before the doors shut; left below.");
            }
        }

        private void OnRideAck(NetworkConnection conn, DepartureAckBroadcast msg)
        {
            CabinRideState state = dayState.CabinRide;
            if (!cohort.Contains(conn.ClientId)) return;
            switch (msg.Kind)
            {
                case DepartureAckKind.Prepared:
                    if (state.Stage == CabinRideStage.Preparing && msg.World == state.FromWorld) prepared.Add(conn.ClientId);
                    break;
                case DepartureAckKind.Black:
                    if (state.Stage == CabinRideStage.FadingOut && msg.World == state.FromWorld) black.Add(conn.ClientId);
                    break;
                case DepartureAckKind.Arrived:
                    if (state.Stage == CabinRideStage.Loading && msg.World == state.ToWorld) arrived.Add(conn.ClientId);
                    break;
            }
        }

        // ---- clients --------------------------------------------------------------------

        private bool LocalIsRider(CabinRideState state)
        {
            if (!networkManager.ClientManager.Started) return false;
            return dayState != null && dayState.IsRider(networkManager.ClientManager.Connection.ClientId);
        }

        private void OnCabinRideChanged(CabinRideState previous, CabinRideState next)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null) return;
            ShipDepartureRider rider = LocalRider();
            switch (next.Stage)
            {
                case CabinRideStage.Preparing:
                    // The rider list and the stage travel in the same tick but may
                    // apply in either order: lock as soon as this peer is listed.
                    RunClientStage(LockWhenListed(next));
                    break;
                case CabinRideStage.Sealing:
                    // Going up the doors close with everyone free inside; the spot is
                    // tracked from here for the swap at the top.
                    if (next.Direction == RideDirection.Up && rider != null && rider.Serial == next.Serial) { rider.Unlock(); RunClientStage(TrackInCar(next)); }
                    break;
                case CabinRideStage.Riding:
                    if (next.Direction == RideDirection.Down && rider != null && rider.Locked && rider.Serial == next.Serial) RunClientStage(UnlockWhenClear(rider));
                    break;
                case CabinRideStage.FadingOut:
                    if (!LocalIsRider(next)) return;
                    ScreenFade.Instance.FadeOut(Settings.SuitFadeSeconds, "Putting on the suit…");
                    RunClientStage(WaitBlackThenAckRide(next));
                    break;
                case CabinRideStage.Loading:
                    if (next.Direction == RideDirection.Down && LocalIsRider(next)) ScreenFade.Instance.HoldBlack("Putting on the suit…");
                    if (next.Direction == RideDirection.Up && LocalIsRider(next) && rider != null && !rider.Locked) rider.LockTracked(next.Serial);
                    break;
                case CabinRideStage.Arriving:
                    if (next.Direction == RideDirection.Down && LocalIsRider(next)) ScreenFade.Instance.FadeIn(Settings.SuitFadeSeconds);
                    break;
                case CabinRideStage.Complete:
                    if (rider != null && rider.Locked && rider.Serial == next.Serial) RunClientStage(UnlockWhenClear(rider));
                    break;
                case CabinRideStage.Cancelled:
                    if (rider != null && rider.Serial == next.Serial) rider.Unlock();
                    if (!ScreenFade.Instance.IsClear) ScreenFade.Instance.FadeIn(Settings.SuitFadeSeconds);
                    break;
            }
        }

        private IEnumerator LockWhenListed(CabinRideState state)
        {
            float deadline = Time.unscaledTime + Settings.PrepareTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (dayState == null || dayState.CabinRide.Serial != state.Serial || dayState.CabinRide.Stage != CabinRideStage.Preparing) break;
                ShipDepartureRider rider = LocalRider();
                HQPlayerController local = LocalPlayer();
                if (rider != null && local != null && LocalIsRider(state) && local.gameObject.scene == WorldScenes.Scene(state.FromWorld))
                {
                    CabinFrame frame = RideFrameFor(state, local.gameObject.scene);
                    if (frame.IsValid)
                    {
                        rider.Lock(frame, state.Serial);
                        Ack(state.Serial, DepartureAckKind.Prepared, state.FromWorld);
                        break;
                    }
                }
                yield return null;
            }
            clientStage = null;
        }

        // Going up: the rider's car-frame spot is refreshed every frame until the
        // Loading stage locks it (that stage can arrive after the swap itself).
        private IEnumerator TrackInCar(CabinRideState state)
        {
            while (dayState != null && dayState.CabinRide.Serial == state.Serial && dayState.CabinRide.Active && dayState.CabinRide.Stage < CabinRideStage.Loading && LocalIsRider(state))
            {
                ShipDepartureRider rider = LocalRider();
                HQPlayerController local = LocalPlayer();
                if (rider != null && local != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) rider.Track(CabinFrame.Car(Car()), state.Serial);
                yield return null;
            }
            clientStage = null;
        }

        private IEnumerator WaitBlackThenAckRide(CabinRideState state)
        {
            while (ScreenFade.Instance != null && !ScreenFade.Instance.IsBlack) yield return null;
            if (LocalRider() != null && LocalRider().Locked) Ack(state.Serial, DepartureAckKind.Black, state.FromWorld);
            clientStage = null;
        }

        // The rider's own load of the other cabin's scene: place at the same
        // cabin-frame spot, then tell the server. Retries until the car (or the
        // ship) is actually there, within the arrival deadline.
        private void OnRideLoadEnd(SceneLoadEndEventArgs args)
        {
            CabinRideState state = dayState.CabinRide;
            bool destinationLoaded = false;
            foreach (SceneLookupData lookup in args.QueueData.SceneLoadData.SceneLookupDatas)
                if (lookup is not null && lookup.Name == WorldScenes.Name(state.ToWorld)) destinationLoaded = true;
            if (!destinationLoaded || !LocalIsRider(state)) return;
            RunClientStage(PlaceInCabinThenAck(state));
        }

        private IEnumerator PlaceInCabinThenAck(CabinRideState state)
        {
            float deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                ShipDepartureRider rider = LocalRider();
                HQPlayerController local = LocalPlayer();
                if (rider != null && !rider.Locked && state.Direction == RideDirection.Up && rider.Serial == state.Serial) rider.LockTracked(state.Serial);
                if (rider != null && local != null && rider.Locked && rider.Serial == state.Serial && local.gameObject.scene == WorldScenes.Scene(state.ToWorld))
                {
                    cachedCar = null;
                    CabinFrame frame = RideFrameFor(state, local.gameObject.scene);
                    if (frame.IsValid)
                    {
                        // Still black: pay the site's first-render costs now, not mid-shaft.
                        if (state.ToWorld == WorldId.Dive)
                        {
                            try { SunkCost.Sites.DiveSiteWarmup.RenderOnce(local.PlayerCamera, local.gameObject.scene); }
                            catch (System.Exception e) { Debug.LogWarning("[WorldSceneFlow] dive site warm-up skipped: " + e.Message); }
                        }
                        rider.PlaceOn(frame);
                        Ack(state.Serial, DepartureAckKind.Arrived, state.ToWorld);
                        clientStage = null;
                        yield break;
                    }
                }
                if (dayState == null || dayState.CabinRide.Serial != state.Serial || dayState.CabinRide.Stage > CabinRideStage.Arriving) break;
                yield return null;
            }
            clientStage = null;
        }
    }
}
