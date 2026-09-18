using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace SunkCost.Interaction
{
    // Spawns a world scene's loot fixture when the server has the scene loaded.
    // FishNet refuses to move scene objects between scenes, so anything a player
    // may carry away has to be a runtime-spawned prefab instance
    // (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 5.6). Not networked itself:
    // the entries are plain scene data written by the editor setup. Nothing
    // survives a server stop, which replaces the old per-item ServerReset on
    // re-host; a fresh load of the scene spawns the fixture again.
    public sealed class LootFixtureSpawner : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string Name;      // instance name, kept for the test hooks
            public GameObject Prefab; // registered in PrototypePrefabObjects
            public Vector3 Position;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private readonly List<NetworkObject> spawned = new();
        private ServerManager server;

        public IReadOnlyList<Entry> Entries => entries;
        public IReadOnlyList<NetworkObject> Spawned => spawned;

        public void SetEntries(Entry[] value) => entries = value ?? Array.Empty<Entry>();

        private void Start()
        {
            server = InstanceFinder.ServerManager;
            if (server == null) return;
            server.OnServerConnectionState += OnServerConnectionState;
            if (server.Started) SpawnAll();
        }

        private void OnDestroy()
        {
            if (server != null) server.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started) SpawnAll();
            else if (args.ConnectionState == LocalConnectionState.Stopped) spawned.Clear();
        }

        // A fresh run (WorldSceneFlow.ServerResetRun, 18 September 2026): the old
        // instances were despawned with everything else loose; the fixture again.
        public void ServerRespawn()
        {
            spawned.Clear();
            SpawnAll();
        }

        private void SpawnAll()
        {
            if (spawned.Count > 0) return; // once per scene load
            foreach (Entry entry in entries)
            {
                if (entry.Prefab == null) { Debug.LogWarning(name + ": fixture entry " + entry.Name + " has no prefab."); continue; }
                GameObject instance = Instantiate(entry.Prefab, entry.Position, Quaternion.identity);
                instance.name = string.IsNullOrEmpty(entry.Name) ? entry.Prefab.name : entry.Name;
                // The site spawns the same "Coin 6" every day: in a world scene the
                // day goes into the name, so one dive's coin is told from the last
                // one's (lying on the deck) in F3 and the logs (full run, 16 September
                // 2026). Server-side names only; clients see the prefab name.
                SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
                if (day != null && day.Day > 0 && SunkCost.World.WorldScenes.TryParse(gameObject.scene.name, out _)) instance.name += " (day " + day.Day + ")";
                CarryableItem item = instance.GetComponent<CarryableItem>();
                if (item != null) item.SetResetPositionBeforeSpawn(entry.Position);
                NetworkObject nob = instance.GetComponent<NetworkObject>();
                if (nob == null) { Debug.LogWarning(name + ": " + entry.Name + " has no NetworkObject."); Destroy(instance); continue; }
                // Into this world scene, not the active (Session) scene.
                server.Spawn(nob, null, gameObject.scene);
                spawned.Add(nob);
            }
        }
    }
}
