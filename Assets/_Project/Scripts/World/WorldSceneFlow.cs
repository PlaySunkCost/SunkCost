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
    // Sent by a client from its own OnLoadEnd once its player stands in the new
    // world; the server's arrival gate counts these (FishNet's presence event no
    // longer fires for a connection the flow pre-added to the scene).
    public struct WorldArrivedBroadcast : FishNet.Broadcast.IBroadcast
    {
        public WorldId World;
    }

    // Executes the scene transitions of docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 5 (this card: sail out and sail home, and the join/leave plumbing).
    // Server side it builds the loads, waits the sync-flush ticks, gates on the
    // clients' arrival broadcasts and unloads the scene left behind; client side
    // it fades and places the local player when its own load ends. It decides nothing
    // about gameplay: CrewDayState (and later the monitor and the cabins) ask.
    public sealed class WorldSceneFlow : MonoBehaviour
    {
        [SerializeField] private WorldLoopSettings settings;
        [SerializeField] private NetworkObject dayStatePrefab;

        private NetworkManager networkManager;
        private CrewDayState dayState;
        private WorldId currentWorld = WorldId.HQ; // server view
        private bool transitioning;                 // server view
        private string arrivalScene = string.Empty;
        private readonly HashSet<int> arrivalsExpected = new();
        private readonly HashSet<int> arrivalsSeen = new();
        private Coroutine cleanup;

        public static WorldSceneFlow Instance { get; private set; }

        public WorldLoopSettings Settings => WorldLoopSettings.Resolve(settings);
        public WorldId CurrentWorld => currentWorld;
        public bool Transitioning => transitioning;
        // Diagnostics for the hooks: who has not reported the destination yet.
        public string PendingArrivals
        {
            get
            {
                var late = new List<string>();
                foreach (int id in arrivalsExpected) if (!arrivalsSeen.Contains(id)) late.Add(id.ToString());
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
            networkManager.ServerManager.RegisterBroadcast<WorldArrivedBroadcast>(OnWorldArrived);
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
            networkManager.ServerManager.UnregisterBroadcast<WorldArrivedBroadcast>(OnWorldArrived);
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
                arrivalsExpected.Clear();
                arrivalsSeen.Clear();
                SpawnDayState();
                // Pre-warm HQ on the server so the host's own client join finds it loaded.
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(LoadDataFor(WorldId.HQ, null));
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                ScheduleCleanup();
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started) EnsureHolderKeepAlive();
            if (args.ConnectionState == LocalConnectionState.Stopped) ScheduleCleanup();
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

        // The monitor's request lands here (a debug hook until the monitor card).
        // Refuses with a reason the caller can show; the aboard rule names who is
        // missing (design section 1).
        public bool ServerSail(WorldId to, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (transitioning) { why = "Already moving."; return false; }
            if (!dayState.ServerCanSail(to, out why)) return false;
            ShipParts fromShip = ShipParts.InWorld(currentWorld);
            if (fromShip != null)
            {
                var missing = new List<string>();
                foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>())
                {
                    if (!player.IsSpawned || !player.Owner.IsValid) continue;
                    if (!fromShip.IsAboard(player.transform.position)) missing.Add(DisplayName(player.Owner));
                }
                if (missing.Count > 0) { why = "Not aboard: " + string.Join(", ", missing); return false; }
            }
            StartCoroutine(SailRoutine(to));
            return true;
        }

        public static string DisplayName(NetworkConnection conn) => conn == null ? "?" : "Player " + conn.ClientId;

        private IEnumerator SailRoutine(WorldId to)
        {
            transitioning = true;
            WorldId from = currentWorld;
            dayState.ServerBeginSail(to);
            // The clients react to the SyncVar (fade, capture); the scene broadcast
            // must not overtake it (contract section 6).
            yield return WaitTicks(Settings.SyncFlushTicks);

            var conns = new List<NetworkConnection>();
            var moved = new List<NetworkObject>();
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive) continue;
                conns.Add(conn);
                foreach (NetworkObject nob in conn.Objects)
                {
                    if (nob == null || !nob.IsSpawned || nob.transform.parent != null) continue;
                    if (nob.GetComponent<HQPlayerController>() == null) continue;
                    moved.Add(nob);
                    PlayerInventory inventory = nob.GetComponent<PlayerInventory>();
                    if (inventory != null) inventory.ServerCollectCarried(moved);
                }
            }
            // Loose items on the deck ride along, at the same spot on the new deck.
            ShipParts fromShip = ShipParts.InWorld(from);
            var deckItems = new List<(NetworkObject nob, Vector3 shipLocal)>();
            if (fromShip != null)
            {
                foreach (CarryableItem item in FindObjectsByType<CarryableItem>())
                {
                    if (!item.IsSpawned || item.NetworkObject.IsSceneObject || item.State != ItemState.Free) continue;
                    if (item.transform.parent != null || moved.Contains(item.NetworkObject)) continue;
                    if (!fromShip.IsAboard(item.transform.position)) continue;
                    moved.Add(item.NetworkObject);
                    deckItems.Add((item.NetworkObject, fromShip.ToShipLocal(item.transform.position)));
                }
            }

            arrivalScene = WorldScenes.Name(to);
            arrivalsExpected.Clear();
            arrivalsSeen.Clear();
            foreach (NetworkConnection conn in conns) arrivalsExpected.Add(conn.ClientId);

            // Nobody may stop observing anything during the move. FishNet rebuilds a
            // connection's own objects for every observer the moment that connection
            // is added to the new scene, which would despawn the host's player and
            // held item on a guest that has not loaded the scene yet, and the
            // re-spawn a moment later collides with the client's still-live copies.
            // So the destination is loaded on the server first and every connection
            // is put into it before any object moves; FishNet's own add on the
            // client's report is then a no-op, and the arrival gate uses the
            // clients' WorldArrivedBroadcast instead of the presence event.
            float deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            if (!WorldScenes.IsLoaded(to))
            {
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(LoadDataFor(to, null));
                while (!WorldScenes.IsLoaded(to) && Time.unscaledTime < deadline) yield return null;
            }
            Scene destination = WorldScenes.Scene(to);
            if (destination.isLoaded)
                foreach (NetworkConnection conn in conns) networkManager.SceneManager.AddConnectionToScene(conn, destination);
            EnsureHolderKeepAlive();
            networkManager.SceneManager.LoadConnectionScenes(conns.ToArray(), LoadDataFor(to, moved.ToArray()));
            while (!WorldScenes.IsLoaded(to) && Time.unscaledTime < deadline) yield return null;
            ShipParts toShip = ShipParts.InWorld(to);
            if (toShip != null)
            {
                foreach ((NetworkObject nob, Vector3 shipLocal) in deckItems)
                {
                    if (nob == null) continue;
                    nob.transform.position = toShip.FromShipLocal(shipLocal);
                    FishNet.Component.Transforming.NetworkTransform networkTransform = nob.GetComponent<FishNet.Component.Transforming.NetworkTransform>();
                    if (networkTransform != null) networkTransform.Teleport();
                }
            }
            else Debug.LogWarning("WorldSceneFlow: no ship found in " + WorldScenes.Name(to) + "; deck items keep their old positions.");

            // Arrival gate: every moved connection has reported the new scene, or the
            // timeout passed and the late ones are logged.
            while (Time.unscaledTime < deadline && !AllArrived()) yield return null;
            if (!AllArrived()) Debug.LogWarning("WorldSceneFlow: sailing to " + to + " proceeds without " + PendingArrivals);

            networkManager.SceneManager.UnloadConnectionScenes(conns.ToArray(), UnloadDataFor(from, keepOnServer: false));
            currentWorld = to;
            dayState.ServerArrive(to);
            transitioning = false;
        }

        private bool AllArrived()
        {
            foreach (int id in arrivalsExpected)
            {
                if (arrivalsSeen.Contains(id)) continue;
                // A connection that left mid-sail is not waited for.
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                return false;
            }
            return true;
        }

        private IEnumerator WaitTicks(int ticks)
        {
            int seen = 0;
            void OnTick() => seen++;
            networkManager.TimeManager.OnTick += OnTick;
            while (seen < Mathf.Max(1, ticks)) yield return null;
            networkManager.TimeManager.OnTick -= OnTick;
        }

        private void OnWorldArrived(NetworkConnection conn, WorldArrivedBroadcast msg, Channel channel)
        {
            if (conn == null || WorldScenes.Name(msg.World) != arrivalScene) return;
            arrivalsSeen.Add(conn.ClientId);
        }

        // ---- clients --------------------------------------------------------------

        private void OnDayStateInstance(CrewDayState state)
        {
            if (dayState != null) dayState.PhaseChanged -= OnPhaseChanged;
            dayState = state;
            if (dayState != null) dayState.PhaseChanged += OnPhaseChanged;
        }

        private void OnPhaseChanged(DayPhase previous, DayPhase next)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null) return;
            if (next == DayPhase.Sailing || next == DayPhase.SailingHome)
                ScreenFade.Instance.FadeOut(Settings.SailingFadeSeconds, next == DayPhase.SailingHome ? "Sailing home…" : "Sailing…");
            else if ((next == DayPhase.AtSea || next == DayPhase.AtHQ) && !ScreenFade.Instance.IsClear)
                ScreenFade.Instance.FadeIn(Settings.SailingFadeSeconds); // safety if the load ended before the phase
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

        // The client's own load of a world it is sailing to: its player object has
        // already been moved into the new scene by FishNet, the old scene is still
        // loaded, so the same spot on the new deck can be computed here, with no
        // RPC and no ordering question.
        private void OnLoadEnd(SceneLoadEndEventArgs args)
        {
            EnsureHolderKeepAlive(); // FishNet moved it with this load; the next load needs it home
            if (!networkManager.ClientManager.Started || dayState == null || !dayState.Sailing) return;
            if (args.QueueData == null || args.QueueData.SceneLoadData == null) return;
            // The host raises this once for its server pass and once for its client
            // pass; placing twice would take the ship-relative offset of an already
            // placed player. Only the client pass places.
            if (args.QueueData.AsServer) return;
            bool destinationLoaded = false;
            foreach (SceneLookupData lookup in args.QueueData.SceneLoadData.SceneLookupDatas)
                if (lookup is not null && lookup.Name == WorldScenes.Name(dayState.Destination)) destinationLoaded = true; // FishNet's != misreports null
            if (!destinationLoaded) return;
            PlaceLocalPlayer(dayState.World, dayState.Destination);
            ScreenFade.Instance?.FadeIn(Settings.SailingFadeSeconds);
            networkManager.ClientManager.Broadcast(new WorldArrivedBroadcast { World = dayState.Destination });
        }

        private static void PlaceLocalPlayer(WorldId from, WorldId to)
        {
            HQPlayerController local = LocalPlayer();
            if (local == null) return;
            ShipParts fromShip = ShipParts.InWorld(from);
            ShipParts toShip = ShipParts.InWorld(to);
            if (toShip == null) return;
            if (fromShip != null)
            {
                Vector3 shipLocal = fromShip.ToShipLocal(local.transform.position);
                float shipYaw = fromShip.ToShipYaw(local.Yaw);
                local.TeleportLocal(toShip.FromShipLocal(shipLocal), toShip.FromShipYaw(shipYaw));
            }
            else
            {
                Transform point = toShip.BoardingPoint != null ? toShip.BoardingPoint : toShip.SpawnPoint(0);
                if (point != null) local.TeleportLocal(point.position, point.eulerAngles.y);
            }
        }

        public static HQPlayerController LocalPlayer()
        {
            foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>())
                if (player.IsOwner) return player;
            return null;
        }
    }
}
