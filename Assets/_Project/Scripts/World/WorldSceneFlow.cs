using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace SunkCost.World
{
    // Executes the scene transitions of docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 5 as the ship trip of docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md:
    // lock the passengers, raise the gangway, pull away, fade, move the scene
    // under black, fade in on the stopped ship. Server side it is the only
    // writer of the trip state on CrewDayState and the only orchestrator; client
    // side it answers each stage for its own player (lock, black, placed). It
    // decides nothing about gameplay: CrewDayState (and the monitor) ask.
    public sealed partial class WorldSceneFlow : MonoBehaviour
    {
        [SerializeField] private WorldLoopSettings settings;
        [SerializeField] private NetworkObject dayStatePrefab;

        private NetworkManager networkManager;
        private CrewDayState dayState;
        private WorldId currentWorld = WorldId.HQ; // server view
        private bool transitioning;                 // server view
        private Coroutine cleanup;
        private Coroutine trip;

        // ---- the trip (server) ----
        private int serial;
        private readonly HashSet<int> cohort = new();      // client ids on this trip
        private readonly HashSet<int> prepared = new();
        private readonly HashSet<int> black = new();
        private readonly HashSet<int> arrived = new();
        private readonly List<CarryableItem> cargo = new(); // frozen deck items
        private readonly List<NetworkObject> moved = new(); // the root move list
        private bool moveListClosed;
        private string lastFailure = string.Empty;

        // ---- the trip (client) ----
        private Coroutine clientStage;

        public static WorldSceneFlow Instance { get; private set; }

        public WorldLoopSettings Settings => WorldLoopSettings.Resolve(settings);
        public WorldId CurrentWorld => currentWorld;
        public bool Transitioning => transitioning;
        public string LastFailure => lastFailure;
        // Diagnostics for the hooks: who has not answered the current stage yet.
        public string PendingAcks
        {
            get
            {
                if (dayState == null) return string.Empty;
                HashSet<int> seen = dayState.Departure.Stage switch
                {
                    DepartureStage.Preparing => prepared,
                    DepartureStage.FadingOut => black,
                    DepartureStage.Loading => arrived,
                    _ => null
                };
                if (seen == null) return string.Empty;
                var late = new List<string>();
                foreach (int id in cohort) if (!seen.Contains(id)) late.Add(id.ToString());
                return string.Join(",", late);
            }
        }

        private void Awake()
        {
            Instance = this;
            networkManager = GetComponentInParent<NetworkManager>();
            if (networkManager == null) { Debug.LogWarning("WorldSceneFlow needs a NetworkManager on this object or a parent."); return; }
            networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.SceneManager.OnLoadEnd += OnLoadEnd;
            networkManager.SceneManager.OnUnloadStart += OnUnloadStart;
            networkManager.ServerManager.RegisterBroadcast<DepartureAckBroadcast>(OnDepartureAck);
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            CrewDayState.InstanceChanged += OnDayStateInstance;
            OnDayStateInstance(CrewDayState.Instance);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CrewDayState.InstanceChanged -= OnDayStateInstance;
            OnDayStateInstance(null);
            if (networkManager == null) return;
            networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            networkManager.SceneManager.OnLoadEnd -= OnLoadEnd;
            networkManager.SceneManager.OnUnloadStart -= OnUnloadStart;
            networkManager.ServerManager.UnregisterBroadcast<DepartureAckBroadcast>(OnDepartureAck);
            networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        // ---- FishNet holder keep-alive ---------------------------------------------

        // FishNet 4.7.3 refills its "objects to move into the loaded scene" list with
        // Scene.GetRootGameObjects(list) on its MovedObjectsHolder scene, and Unity 6
        // does not clear that list when the scene has no roots. So a load that moves
        // nothing re-moves whatever the previous load moved — a server-only pre-load
        // or a joiner's load would yank every player of the last sail into its scene.
        // One harmless root object kept in the holder makes the list refill every
        // time; FishNet moves it along with the load, and it is put back after each.
        private const string HolderSceneName = "MovedObjectsHolder";
        private const string HolderKeepAliveName = "FishNetHolderKeepAlive";
        private GameObject holderKeepAlive;

        public void EnsureHolderKeepAlive()
        {
            if (!Application.isPlaying) return;
            Scene holder = UnitySceneManager.GetSceneByName(HolderSceneName);
            if (!holder.IsValid()) holder = UnitySceneManager.CreateScene(HolderSceneName);
            if (holderKeepAlive == null) holderKeepAlive = new GameObject(HolderKeepAliveName);
            if (holderKeepAlive.scene != holder) UnitySceneManager.MoveGameObjectToScene(holderKeepAlive, holder);
        }

        // ---- load data ------------------------------------------------------------

        // Every world load: additive, never auto-unloaded, the client's active scene
        // follows its world, the server's does not change (section 3.2).
        public static SceneLoadData LoadDataFor(WorldId world, NetworkObject[] moved)
        {
            SceneLookupData lookup = WorldScenes.Lookup(world);
            return new SceneLoadData(lookup)
            {
                MovedNetworkObjects = moved ?? new NetworkObject[0],
                ReplaceScenes = ReplaceOption.None,
                Options = new LoadOptions { AutomaticallyUnload = false },
                PreferredActiveScene = new PreferredScene(lookup, null)
            };
        }

        public static SceneUnloadData UnloadDataFor(WorldId world, bool keepOnServer)
        {
            return new SceneUnloadData(WorldScenes.Lookup(world))
            {
                Options = new UnloadOptions
                {
                    Mode = keepOnServer ? UnloadOptions.ServerUnloadMode.KeepUnused : UnloadOptions.ServerUnloadMode.UnloadUnused
                }
            };
        }

        // ---- session start and stop ------------------------------------------------

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                currentWorld = WorldId.HQ;
                transitioning = false;
                ResetTrip();
                SpawnDayState();
                // Pre-warm HQ on the server so the host's own client join finds it loaded.
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(LoadDataFor(WorldId.HQ, null));
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (trip != null) { StopCoroutine(trip); trip = null; }
                if (ride != null) { StopCoroutine(ride); ride = null; }
                riding = false;
                ResetTrip();
                ScheduleCleanup();
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started) EnsureHolderKeepAlive();
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (clientStage != null) { StopCoroutine(clientStage); clientStage = null; }
                ScheduleCleanup();
            }
        }

        private void ResetTrip()
        {
            cohort.Clear(); prepared.Clear(); black.Clear(); arrived.Clear();
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerEndDeckTransit();
            cargo.Clear();
            moved.Clear();
            moveListClosed = false;
        }

        private void SpawnDayState()
        {
            if (dayStatePrefab == null) { Debug.LogWarning("WorldSceneFlow has no CrewDayState prefab; sailing is unavailable."); return; }
            NetworkObject nob = Instantiate(dayStatePrefab);
            nob.name = dayStatePrefab.name;
            // Global objects live in DontDestroyOnLoad and have no observer conditions;
            // the prefab flag is set by the builder, this only guards a stale prefab.
            if (!nob.IsGlobal) nob.SetIsGlobal(true);
            networkManager.ServerManager.Spawn(nob);
        }

        private void ScheduleCleanup()
        {
            if (cleanup != null) StopCoroutine(cleanup);
            cleanup = StartCoroutine(CleanupWhenOffline());
        }

        // FishNet forgets its scene bookkeeping when the server stops but unloads no
        // Unity scene; a guest keeps what it was told to load. Once neither role is
        // running, drop every world scene so the menu is back on the Session scene.
        private IEnumerator CleanupWhenOffline()
        {
            float deadline = Time.unscaledTime + 5f;
            while ((networkManager.ServerManager.Started || networkManager.ClientManager.Started) && Time.unscaledTime < deadline)
                yield return null;
            yield return null;
            if (networkManager.ServerManager.Started || networkManager.ClientManager.Started) yield break;
            transitioning = false;
            currentWorld = WorldId.HQ;
            ScreenFade.Instance?.ClearNow();
            for (int i = UnitySceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = UnitySceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !WorldScenes.TryParse(scene.name, out _)) continue;
                UnitySceneManager.UnloadSceneAsync(scene);
            }
            cleanup = null;
        }

        // ---- sailing (server) -----------------------------------------------------

        // The monitor's request lands here. Refuses with a reason the caller can
        // show; the aboard rule names who is missing (design section 1). Everything
        // that can be checked before locking anyone is checked here.
        public bool ServerSail(WorldId to, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (transitioning || dayState.Travelling) { why = "Ship travelling; try again on arrival"; return false; }
            if (riding) { why = "Cabin in use"; return false; }
            if (dayState.Below.Count > 0 || dayState.CabinAway) { why = "Divers below"; return false; }
            if (!dayState.ServerCanSail(to, out why)) return false;
            ShipParts fromShip = ShipParts.InWorld(currentWorld);
            if (fromShip == null) { why = "No ship in " + WorldScenes.Name(currentWorld) + "."; return false; }
            if (fromShip.GetComponent<ShipDepartureVisual>() == null) { why = "The ship cannot sail (no ShipDepartureVisual)."; return false; }
            CrewSpawner spawner = FindAnyObjectByType<CrewSpawner>();
            if (spawner != null && spawner.PendingCount > 0) { why = "Someone is still joining."; return false; }
            if (!ServerEveryoneAboard(fromShip, out why)) return false;
            if (ServerCargoOnGangway(fromShip)) { why = "Clear the gangway"; return false; }
            trip = StartCoroutine(TripRoutine(to, fromShip));
            return true;
        }

        public static string DisplayName(NetworkConnection conn) => conn == null ? "?" : "Player " + conn.ClientId;

        // Every player of every active connection stands on the deck proper, in the
        // current world. A player still spawning counts as missing.
        private bool ServerEveryoneAboard(ShipParts ship, out string why)
        {
            var missing = new List<string>();
            Scene world = WorldScenes.Scene(currentWorld);
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null || player.gameObject.scene != world || !ship.IsSafelyAboard(player.transform.position))
                    missing.Add(DisplayName(conn));
            }
            why = missing.Count > 0 ? "Not aboard: " + string.Join(", ", missing) : string.Empty;
            return missing.Count == 0;
        }

        private bool ServerCargoOnGangway(ShipParts ship)
        {
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>())
                if (item.IsSpawned && item.CanGrabFromWorld && ship.IsOnGangway(item.transform.position)) return true;
            return false;
        }

        private static HQPlayerController PlayerOf(NetworkConnection conn)
        {
            foreach (NetworkObject nob in conn.Objects)
            {
                if (nob == null || !nob.IsSpawned || nob.transform.parent != null) continue;
                HQPlayerController player = nob.GetComponent<HQPlayerController>();
                if (player != null) return player;
            }
            return null;
        }

        private void SetStage(DepartureStage stage, WorldId from, WorldId to, float seconds)
        {
            uint ticks = seconds <= 0f ? 0 : networkManager.TimeManager.TimeToTicks(seconds);
            dayState.ServerSetDeparture(new ShipDepartureState
            {
                Serial = serial,
                Stage = stage,
                FromWorld = from,
                ToWorld = to,
                StageStartTick = networkManager.TimeManager.Tick,
                StageDurationTicks = ticks
            });
        }

        // The trip, stage by stage (plan section 4, "Accepted request through arrival").
        private IEnumerator TripRoutine(WorldId to, ShipParts fromShip)
        {
            transitioning = true;
            WorldId from = currentWorld;
            serial++;
            ResetTrip();
            lastFailure = string.Empty;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
                if (conn.IsActive) cohort.Add(conn.ClientId);

            // 3. Lock everyone where they stand; the ship does not move yet.
            dayState.ServerBeginSail(to);
            SetStage(DepartureStage.Preparing, from, to, Settings.PrepareTimeoutSeconds);
            float deadline = Time.unscaledTime + Settings.PrepareTimeoutSeconds;
            while (Time.unscaledTime < deadline && !AllAcked(prepared)) yield return null;
            if (!AllAcked(prepared)) { yield return Cancel(from, "Not ready: " + Missing(prepared)); yield break; }
            yield return WaitTicks(Settings.SyncFlushTicks); // the owners' last free transforms

            // 4. Someone may have stepped onto the gangway before the lock arrived.
            if (!ServerEveryoneAboard(fromShip, out string why)) { yield return Cancel(from, why); yield break; }
            if (ServerCargoOnGangway(fromShip)) { yield return Cancel(from, "Clear the gangway"); yield break; }

            // 5. Freeze the deck cargo, close the move list, lift the gangway.
            ServerFreezeCargo(fromShip);
            ServerBuildMoveList();
            SetStage(DepartureStage.RaisingGangway, from, to, Settings.GangwayRaiseSeconds);
            yield return WaitSeconds(Settings.GangwayRaiseSeconds);

            // The visible pull-away. ShipDepartureVisual moves the source ship on
            // every peer; the server drags the frozen cargo along with it.
            SetStage(DepartureStage.PullingAway, from, to, Settings.DepartureSeconds);
            float end = Time.unscaledTime + Settings.DepartureSeconds;
            while (Time.unscaledTime < end) { ServerFollowCargo(fromShip); yield return null; }
            ServerFollowCargo(fromShip);

            // 6. Black on every screen before anything is swapped.
            SetStage(DepartureStage.FadingOut, from, to, Settings.DepartureFadeSeconds);
            deadline = Time.unscaledTime + Settings.DepartureFadeSeconds + Settings.ArrivalTimeoutSeconds;
            while (Time.unscaledTime < deadline && !AllAcked(black)) { ServerFollowCargo(fromShip); yield return null; }
            if (!AllAcked(black)) ServerKickUnresponsive(black, "Ship departure: no black acknowledgement");

            // 7. The scene move, under black (the observer-safe order of the scene-flow card).
            SetStage(DepartureStage.Loading, from, to, 0f);
            deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            if (!WorldScenes.IsLoaded(to))
            {
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(LoadDataFor(to, null));
                while (!WorldScenes.IsLoaded(to) && Time.unscaledTime < deadline) yield return null;
            }
            ShipParts toShip = ShipParts.InWorld(to);
            if (toShip == null)
            {
                // 13. Nothing has moved yet: everyone is still on the source ship.
                lastFailure = "No ship in " + WorldScenes.Name(to);
                yield return Cancel(from, "Transition failed: " + lastFailure);
                yield break;
            }
            Scene destination = WorldScenes.Scene(to);
            // Nobody may stop observing anything during the move: every traveller is
            // added to the destination before the one load (see the scene-flow card).
            var conns = ActiveCohort();
            foreach (NetworkConnection conn in conns) networkManager.SceneManager.AddConnectionToScene(conn, destination);
            EnsureHolderKeepAlive();
            networkManager.SceneManager.LoadConnectionScenes(conns.ToArray(), LoadDataFor(to, moved.ToArray()));
            while (!WorldScenes.IsLoaded(to) && Time.unscaledTime < deadline) yield return null;

            // 9. Cargo at its saved spots on the destination ship, then the arrival gate.
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerPlaceAfterTransit(toShip);
            while (Time.unscaledTime < deadline && !AllAcked(arrived)) yield return null;
            if (!AllAcked(arrived)) ServerKickUnresponsive(arrived, "Ship departure: never arrived");

            // Fade in; at HQ the gangway comes down before anyone may walk.
            float arriving = Settings.DepartureFadeSeconds + (to == WorldId.HQ ? Settings.GangwayLowerSeconds : 0f);
            SetStage(DepartureStage.Arriving, from, to, arriving);
            networkManager.SceneManager.UnloadConnectionScenes(ActiveCohort().ToArray(), UnloadDataFor(from, keepOnServer: false));
            yield return WaitSeconds(arriving);
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerEndDeckTransit();
            cargo.Clear();

            // 10. Only here does the world change for gameplay.
            currentWorld = to;
            dayState.ServerArrive(to);
            SetStage(DepartureStage.Complete, from, to, 0f);
            transitioning = false;
            trip = null;
        }

        // A trip that stops before the scene moved: nothing has changed for gameplay.
        private IEnumerator Cancel(WorldId from, string why)
        {
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerEndDeckTransit();
            cargo.Clear();
            moved.Clear();
            moveListClosed = false;
            dayState.ServerReportRefusal(why);
            dayState.ServerCancelSail();
            SetStage(DepartureStage.Cancelled, from, from, 0f);
            Debug.LogWarning("WorldSceneFlow: departure cancelled: " + why);
            yield return null;
            transitioning = false;
            trip = null;
        }

        private List<NetworkConnection> ActiveCohort()
        {
            var conns = new List<NetworkConnection>();
            foreach (int id in cohort)
                if (networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) && conn.IsActive) conns.Add(conn);
            return conns;
        }

        private bool AllAcked(HashSet<int> seen)
        {
            foreach (int id in cohort)
            {
                if (seen.Contains(id)) continue;
                // A connection that left mid-trip is not waited for.
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                return false;
            }
            return true;
        }

        private string Missing(HashSet<int> seen)
        {
            var late = new List<string>();
            foreach (int id in cohort) if (!seen.Contains(id)) late.Add("Player " + id);
            return string.Join(", ", late);
        }

        // A guest that never answers must not stall the crew or be shown a half
        // loaded world: it leaves with a reason (plan section 8).
        private void ServerKickUnresponsive(HashSet<int> seen, string reason)
        {
            foreach (int id in new List<int>(cohort))
            {
                if (seen.Contains(id)) continue;
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                if (conn == networkManager.ClientManager.Connection) continue; // the host answers itself; never kick it
                Debug.LogWarning("WorldSceneFlow: disconnecting Player " + id + " (" + reason + ")");
                conn.Disconnect(true);
            }
        }

        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            cohort.Remove(conn.ClientId);
            prepared.Remove(conn.ClientId); black.Remove(conn.ClientId); arrived.Remove(conn.ClientId);
            if (dayState != null && networkManager.IsServerStarted) dayState.ServerRemoveEverywhere(conn.ClientId);
        }

        private void OnDepartureAck(NetworkConnection conn, DepartureAckBroadcast msg, Channel channel)
        {
            if (conn == null || !conn.IsActive || !conn.IsAuthenticated || dayState == null) return;
            if (riding && msg.Serial == dayState.CabinRide.Serial) { OnRideAck(conn, msg); return; }
            ShipDepartureState state = dayState.Departure;
            if (msg.Serial != state.Serial || !cohort.Contains(conn.ClientId)) return; // stale, foreign or not on this trip
            switch (msg.Kind)
            {
                case DepartureAckKind.Prepared:
                    if (state.Stage == DepartureStage.Preparing && msg.World == state.FromWorld) prepared.Add(conn.ClientId);
                    break;
                case DepartureAckKind.Black:
                    if (state.Stage == DepartureStage.FadingOut && msg.World == state.FromWorld) black.Add(conn.ClientId);
                    break;
                case DepartureAckKind.Arrived:
                    if (state.Stage == DepartureStage.Loading && msg.World == state.ToWorld) arrived.Add(conn.ClientId);
                    break;
            }
        }

        // ---- cargo (server) -------------------------------------------------------

        // Loose items on the ship, Free or Released, frozen where they lie with
        // position and rotation. Airborne items outside the aboard volume are not
        // cargo; carried items travel with their carrier regardless.
        private void ServerFreezeCargo(ShipParts ship)
        {
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>())
            {
                if (!item.IsSpawned || item.NetworkObject.IsSceneObject || item.transform.parent != null) continue;
                if (!item.CanGrabFromWorld || cargo.Contains(item)) continue;
                if (!ship.IsAboard(item.transform.position)) continue;
                item.ServerBeginDeckTransit(serial, ship);
                cargo.Add(item);
            }
        }

        private void ServerBuildMoveList()
        {
            moved.Clear();
            foreach (NetworkConnection conn in ActiveCohort())
            {
                HQPlayerController player = PlayerOf(conn);
                if (player == null) continue;
                moved.Add(player.NetworkObject);
                PlayerInventory inventory = player.GetComponent<PlayerInventory>();
                if (inventory != null) inventory.ServerCollectCarried(moved);
            }
            foreach (CarryableItem item in cargo)
                if (item != null && item.IsSpawned && !moved.Contains(item.NetworkObject)) moved.Add(item.NetworkObject);
            moveListClosed = true;
        }

        private void ServerFollowCargo(ShipParts ship)
        {
            foreach (CarryableItem item in cargo) if (item != null && item.IsSpawned) item.ServerFollowDeckTransit(ship);
        }

        // An item that became loose while the ship is under way (a passenger left):
        // it joins the frozen cargo at its spot on whichever ship the trip is on.
        // Before the move list closes it also travels; after, it is already in the
        // destination with its carrier.
        public void ServerEnrollLooseCargo(CarryableItem item)
        {
            if (networkManager == null || !networkManager.IsServerStarted) return;
            if (!transitioning || dayState == null || item == null || !item.IsSpawned || item.InTransit) return;
            ShipDepartureState state = dayState.Departure;
            if (!state.Active) return;
            bool afterMove = state.Stage >= DepartureStage.Loading && WorldScenes.IsLoaded(state.ToWorld) && item.gameObject.scene == WorldScenes.Scene(state.ToWorld);
            ShipParts ship = ShipParts.InWorld(afterMove ? state.ToWorld : state.FromWorld);
            if (ship == null || !ship.IsAboard(item.transform.position)) return;
            item.ServerBeginDeckTransit(serial, ship);
            cargo.Add(item);
            if (!moveListClosed && !moved.Contains(item.NetworkObject)) moved.Add(item.NetworkObject);
        }

        private IEnumerator WaitTicks(int ticks)
        {
            int seen = 0;
            void OnTick() => seen++;
            networkManager.TimeManager.OnTick += OnTick;
            while (seen < Mathf.Max(1, ticks)) yield return null;
            networkManager.TimeManager.OnTick -= OnTick;
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.unscaledTime + Mathf.Max(0f, seconds);
            while (Time.unscaledTime < end) yield return null;
        }

        // ---- clients --------------------------------------------------------------

        private void OnDayStateInstance(CrewDayState state)
        {
            if (dayState != null) { dayState.DepartureChanged -= OnDepartureChanged; dayState.CabinRideChanged -= OnCabinRideChanged; }
            dayState = state;
            if (dayState != null) { dayState.DepartureChanged += OnDepartureChanged; dayState.CabinRideChanged += OnCabinRideChanged; }
        }

        // Each stage on this peer, once (the host's client pass; a pure client's
        // OnChange). The local player answers for itself; a peer without a player
        // in the trip (a spectator, a joiner) just watches.
        private void OnDepartureChanged(ShipDepartureState previous, ShipDepartureState next)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null) return;
            ShipDepartureRider rider = LocalRider();
            switch (next.Stage)
            {
                case DepartureStage.Preparing:
                {
                    ShipParts ship = ShipParts.InWorld(next.FromWorld);
                    HQPlayerController local = LocalPlayer();
                    if (rider == null || ship == null || local == null || local.gameObject.scene != WorldScenes.Scene(next.FromWorld)) return;
                    rider.Lock(ship, next.Serial);
                    Ack(next.Serial, DepartureAckKind.Prepared, next.FromWorld);
                    break;
                }
                case DepartureStage.FadingOut:
                    ScreenFade.Instance.FadeOut(Settings.DepartureFadeSeconds, next.ToWorld == WorldId.HQ ? "Sailing home…" : "Sailing…");
                    RunClientStage(WaitBlackThenAck(next));
                    break;
                case DepartureStage.Loading:
                    ScreenFade.Instance.HoldBlack(next.ToWorld == WorldId.HQ ? "Sailing home…" : "Sailing…");
                    break;
                case DepartureStage.Arriving:
                    ScreenFade.Instance.FadeIn(Settings.DepartureFadeSeconds);
                    break;
                case DepartureStage.Complete:
                    RunClientStage(UnlockWhenClear(rider));
                    break;
                case DepartureStage.Cancelled:
                    rider?.Unlock();
                    if (!ScreenFade.Instance.IsClear) ScreenFade.Instance.FadeIn(Settings.DepartureFadeSeconds);
                    break;
            }
        }

        private void RunClientStage(IEnumerator routine)
        {
            if (clientStage != null) StopCoroutine(clientStage);
            clientStage = StartCoroutine(routine);
        }

        private IEnumerator WaitBlackThenAck(ShipDepartureState state)
        {
            while (ScreenFade.Instance != null && !ScreenFade.Instance.IsBlack) yield return null;
            if (LocalRider() != null && LocalRider().Locked) Ack(state.Serial, DepartureAckKind.Black, state.FromWorld);
            clientStage = null;
        }

        private IEnumerator UnlockWhenClear(ShipDepartureRider rider)
        {
            while (ScreenFade.Instance != null && !ScreenFade.Instance.IsClear) yield return null;
            rider?.Unlock();
            clientStage = null;
        }

        private void Ack(int tripSerial, DepartureAckKind kind, WorldId world)
        {
            networkManager.ClientManager.Broadcast(new DepartureAckBroadcast { Serial = tripSerial, Kind = kind, World = world });
        }

        // A client instantiates a spawned prefab into whatever scene is active at
        // that moment, so an object the server spawned into the world the client is
        // sailing to (the fresh HQ fixture) can land in the world it is leaving and
        // die with it. FishNet moves such objects out for the host
        // (MoveClientHostObjects) but not for a pure client; this does it here.
        // The server's despawn messages, not Unity's scene unload, end their life.
        private void OnUnloadStart(SceneUnloadStartEventArgs args)
        {
            if (args.QueueData == null || args.QueueData.AsServer || networkManager.IsServerStarted) return;
            if (args.QueueData.SceneUnloadData == null) return;
            Scene session = UnitySceneManager.GetSceneByName(WorldScenes.SessionName);
            if (!session.IsValid() || !session.isLoaded) return;
            foreach (SceneLookupData lookup in args.QueueData.SceneUnloadData.SceneLookupDatas)
            {
                if (lookup is null) continue;
                Scene unloading = UnitySceneManager.GetSceneByName(lookup.Name);
                if (!unloading.IsValid() || !unloading.isLoaded) continue;
                foreach (NetworkObject nob in networkManager.ClientManager.Objects.Spawned.Values)
                {
                    if (nob == null || nob.IsSceneObject || nob.transform.parent != null) continue;
                    if (nob.gameObject.scene != unloading) continue;
                    UnitySceneManager.MoveGameObjectToScene(nob.gameObject, session);
                }
            }
        }

        // The client's own load of the world it is sailing to, under black: its
        // player was moved into the new scene by FishNet; it is placed at the
        // captured ship-relative spot on the destination ship and the server is
        // told. Load-end alone is not readiness: the player and the ship must both
        // be there, so this retries within the arrival deadline.
        private void OnLoadEnd(SceneLoadEndEventArgs args)
        {
            EnsureHolderKeepAlive(); // FishNet moved it with this load; the next load needs it home
            if (!networkManager.ClientManager.Started || dayState == null) return;
            if (args.QueueData == null || args.QueueData.SceneLoadData == null) return;
            // The host raises this once for its server pass and once for its client
            // pass; placing twice would take the ship-relative offset of an already
            // placed player. Only the client pass places.
            if (args.QueueData.AsServer) return;
            if (dayState.CabinRide.Stage == CabinRideStage.Loading) { OnRideLoadEnd(args); return; }
            ShipDepartureState state = dayState.Departure;
            if (state.Stage != DepartureStage.Loading) return;
            bool destinationLoaded = false;
            foreach (SceneLookupData lookup in args.QueueData.SceneLoadData.SceneLookupDatas)
                if (lookup is not null && lookup.Name == WorldScenes.Name(state.ToWorld)) destinationLoaded = true; // FishNet's != misreports null
            if (!destinationLoaded) return;
            RunClientStage(PlaceThenAck(state));
        }

        private IEnumerator PlaceThenAck(ShipDepartureState state)
        {
            float deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                ShipDepartureRider rider = LocalRider();
                ShipParts ship = ShipParts.InWorld(state.ToWorld);
                if (rider != null && rider.Locked && rider.Serial == state.Serial && ship != null)
                {
                    rider.PlaceOn(ship);
                    Ack(state.Serial, DepartureAckKind.Arrived, state.ToWorld);
                    clientStage = null;
                    yield break;
                }
                if (dayState == null || dayState.Departure.Serial != state.Serial || dayState.Departure.Stage > DepartureStage.Arriving) break;
                yield return null;
            }
            clientStage = null;
        }

        public static HQPlayerController LocalPlayer()
        {
            foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>())
                if (player.IsOwner) return player;
            return null;
        }

        public static ShipDepartureRider LocalRider()
        {
            HQPlayerController local = LocalPlayer();
            return local != null ? local.GetComponent<ShipDepartureRider>() : null;
        }
    }
}
