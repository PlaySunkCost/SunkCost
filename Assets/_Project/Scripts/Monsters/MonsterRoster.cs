using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Monsters
{
    // The day's monsters (docs/DESIGN.md §6): MonstersPerDive distinct walkers
    // drawn at random when the site is fresh, spawned on the seabed once the
    // first diver has landed and WakeDelaySeconds have passed, at least
    // SpawnMinMeters from the shaft and clear of the divers and the wreck. They
    // are runtime spawns of registered prefabs into the dive scene (DiveSite01
    // holds no NetworkObject) and go away with the site. Server only; rides on
    // CrewDayState like ElevatorNoise. The checks force the draw through
    // MonsterSettings.RosterOverrideForTests (an empty array: no monsters).
    public sealed class MonsterRoster : MonoBehaviour
    {
        public static MonsterRoster Instance { get; private set; }

        private bool rolled;
        private float firstDiverAt = -1f;
        private readonly List<MonsterKind> lastRoll = new();
        private readonly List<(MonsterKind kind, Vector3 at)> lastSpawns = new();
        private Vector3? wreck;

        public IReadOnlyList<MonsterKind> LastRoll => lastRoll;
        // Where the day's creatures appeared (the checks: they walk from there at once).
        public IReadOnlyList<(MonsterKind kind, Vector3 at)> LastSpawns => lastSpawns;
        public bool Rolled => rolled;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null || !nm.IsServerStarted) return;
            Scene dive = WorldScenes.Scene(WorldId.Dive);
            if (!dive.IsValid() || !dive.isLoaded) { rolled = false; firstDiverAt = -1f; wreck = null; return; }
            if (rolled) return;
            if (CreatureSenses.Divers().Count == 0) { firstDiverAt = -1f; return; }
            if (firstDiverAt < 0f) firstDiverAt = Time.time;
            if (Time.time - firstDiverAt < MonsterSettings.Get().WakeDelaySeconds) return;
            Roll(nm, dive);
        }

        private void Roll(NetworkManager nm, Scene dive)
        {
            rolled = true;
            lastRoll.Clear();
            lastSpawns.Clear();
            MonsterSettings settings = MonsterSettings.Get();
            MonsterKind[] draw = MonsterSettings.RosterOverrideForTests ?? Draw(settings.MonstersPerDive);
            foreach (MonsterKind kind in draw)
            {
                if (!TryRandomSpot(settings, out Vector3 at)) { Debug.LogWarning("[Monsters] no clear spot for " + kind); continue; }
                Creature spawned = ServerSpawn(nm, kind, at, dive);
                if (spawned != null) { lastRoll.Add(kind); lastSpawns.Add((kind, at)); }
            }
            Debug.Log($"[Monsters] day {(CrewDayState.Instance != null ? CrewDayState.Instance.Day : 0)}: {(lastRoll.Count == 0 ? "no monsters" : string.Join(", ", lastRoll))}");
        }

        // Distinct kinds among the six walkers.
        private static MonsterKind[] Draw(int count)
        {
            var pool = new List<MonsterKind>(MonsterCatalog.Walkers);
            var picked = new List<MonsterKind>();
            while (picked.Count < count && pool.Count > 0)
            {
                int i = Random.Range(0, pool.Count);
                picked.Add(pool[i]);
                pool.RemoveAt(i);
            }
            return picked.ToArray();
        }

        private bool TryRandomSpot(MonsterSettings settings, out Vector3 at)
        {
            at = Vector3.zero;
            if (!CreatureSenses.ShaftCentre(out Vector3 centre)) return false;
            Vector3? wreckAt = WreckPosition();
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(settings.SpawnMinMeters, settings.RoamHalfMeters);
                Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                p.y = centre.y + 0.5f;
                bool clear = true;
                foreach (HQPlayerController diver in CreatureSenses.Divers())
                    if (CreatureSenses.Flat(p, diver.transform.position) < settings.SpawnClearOfDiversMeters) { clear = false; break; }
                if (clear && wreckAt.HasValue && CreatureSenses.Flat(p, wreckAt.Value) < 12f) clear = false;
                foreach (Creature other in Creature.All)
                    if (clear && other != null && CreatureSenses.Flat(p, other.transform.position) < 6f) { clear = false; break; }
                if (!clear) continue;
                at = p;
                return true;
            }
            return false;
        }

        private Vector3? WreckPosition()
        {
            if (wreck.HasValue) return wreck;
            Scene dive = WorldScenes.Scene(WorldId.Dive);
            if (!dive.isLoaded) return null;
            foreach (GameObject root in dive.GetRootGameObjects())
                if (root.name == "Wreck") { wreck = root.transform.position; return wreck; }
            return null;
        }

        // A registered monster prefab spawned into the dive scene, server-owned.
        public static Creature ServerSpawn(NetworkManager nm, MonsterKind kind, Vector3 at, Scene scene)
        {
            string name = MonsterCatalog.PrefabName(kind);
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == name) { prefab = candidate; break; }
            }
            if (prefab == null) { Debug.LogError("[Monsters] prefab '" + name + "' is not a registered spawnable; run Sunk Cost > Prototype > Apply monster setup."); return null; }
            NetworkObject instance = Object.Instantiate(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            instance.name = name;
            nm.ServerManager.Spawn(instance, null, scene);
            return instance.GetComponent<Creature>();
        }

        // The checks: a chosen kind at a chosen spot, awake at once.
        public static Creature ServerSpawnForChecks(MonsterKind kind, Vector3 at)
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null || !nm.IsServerStarted) return null;
            Scene dive = WorldScenes.Scene(WorldId.Dive);
            if (!dive.isLoaded) return null;
            Creature c = ServerSpawn(nm, kind, at, dive);
            if (c != null) c.ServerWakeNow();
            return c;
        }
    }
}
