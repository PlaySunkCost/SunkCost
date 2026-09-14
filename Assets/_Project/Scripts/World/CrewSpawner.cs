using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // Replaces FishNet's PlayerSpawner (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 3.3). A joiner is first loaded into the crew's current world, then
    // spawned at that world's next spawn point once the server has confirmed the
    // client is in the scene. Server only; the client side is FishNet's own
    // scene load.
    public sealed class CrewSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject playerPrefab;

        private NetworkManager networkManager;
        private WorldSceneFlow flow;
        private readonly HashSet<int> pending = new(); // client ids waiting for their world
        private int nextSpawn;

        public event System.Action<NetworkObject> OnSpawned;

        public void SetPlayerPrefab(NetworkObject prefab) => playerPrefab = prefab;

        private void Awake()
        {
            networkManager = GetComponentInParent<NetworkManager>();
            flow = GetComponentInParent<WorldSceneFlow>();
            if (networkManager == null) { Debug.LogWarning("CrewSpawner needs a NetworkManager on this object or a parent."); return; }
            networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            networkManager.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChangeEnd;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnDestroy()
        {
            if (networkManager == null) return;
            networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            networkManager.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChangeEnd;
            networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        // There are no global scenes, so this fires right after authentication.
        private void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer || flow == null) return;
            WorldId world = flow.CurrentWorld;
            pending.Add(conn.ClientId);
            flow.EnsureHolderKeepAlive(); // see WorldSceneFlow: a load that moves nothing must not re-move the last sail
            networkManager.SceneManager.LoadConnectionScenes(conn, WorldSceneFlow.LoadDataFor(world, null));
        }

        private void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
        {
            if (!args.Added || args.Connection == null || flow == null) return;
            if (!pending.Contains(args.Connection.ClientId)) return;
            if (args.Scene.name != WorldScenes.Name(flow.CurrentWorld)) return;
            pending.Remove(args.Connection.ClientId);
            Spawn(args.Connection, args.Scene);
        }

        private void OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped) pending.Remove(conn.ClientId);
        }

        private void Spawn(NetworkConnection conn, Scene scene)
        {
            if (playerPrefab == null) { Debug.LogWarning("CrewSpawner has no player prefab."); return; }
            Transform point = NextSpawnPoint(scene);
            Vector3 position = point != null ? point.position : Vector3.zero;
            Quaternion rotation = point != null ? Quaternion.Euler(0f, point.eulerAngles.y, 0f) : Quaternion.identity;
            NetworkObject nob = networkManager.GetPooledInstantiated(playerPrefab, position, rotation, true);
            networkManager.ServerManager.Spawn(nob, conn, scene);
            OnSpawned?.Invoke(nob);
        }

        // HQ has its own "Spawn Points/Spawn n" (players arrive in the base, not
        // on the docked ship); a world without them, the sea, uses the ship's
        // "SpawnPoint_n" on deck.
        private Transform NextSpawnPoint(Scene scene)
        {
            List<Transform> points = SpawnPointsIn(scene);
            if (points.Count == 0) return null;
            Transform result = points[nextSpawn % points.Count];
            nextSpawn = (nextSpawn + 1) % points.Count;
            return result;
        }

        public static List<Transform> SpawnPointsIn(Scene scene)
        {
            var points = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "Spawn Points") continue;
                foreach (Transform child in root.transform) points.Add(child);
            }
            if (points.Count > 0) return points;
            ShipParts ship = ShipParts.InScene(scene);
            if (ship == null) return points;
            for (int i = 0; i < ShipParts.SpawnPointCount; i++)
            {
                Transform point = ship.SpawnPoint(i);
                if (point != null) points.Add(point);
            }
            return points;
        }
    }
}
