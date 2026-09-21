using System.Linq;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Monsters;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Editor-only hooks for the monsters matrix and the command file's `run`:
    // spawn a chosen creature where a row needs it, read the server's view of
    // every creature, clear them, put a patch kit at the host's feet, aim the
    // host at a teammate. Host (server) only where they say so.
    public static class MonsterTestHooks
    {
        public static string ServerSpawnMonster(MonsterKind kind, Vector3 at)
        {
            Creature c = MonsterRoster.ServerSpawnForChecks(kind, at);
            return c == null ? "not spawned" : "#" + c.ObjectId + " " + c.Kind + " at " + at;
        }

        public static string ServerDespawnMonsters()
        {
            int n = 0;
            foreach (Creature c in Creature.All.ToArray())
            {
                if (c == null || !c.IsServerStarted || !c.IsSpawned) continue;
                c.NetworkObject.Despawn();
                n++;
            }
            return "despawned " + n;
        }

        public static string MonstersText()
        {
            if (Creature.All.Count == 0) return "no monsters";
            return string.Join(" | ", Creature.All.Where(c => c != null).Select(c => c.ServerStatus));
        }

        // A patch kit spawned at a spot in the dive scene (server only).
        public static CarryableItem ServerSpawnPatchKit(Vector3 at) => ServerSpawnItem(PatchKitSetup.PrefabName, at, "Patch kit (checks)");

        // Any registered carryable prefab by name, spawned into the dive scene (the
        // dash checks throw a coin, hold a heavy ball).
        public static CarryableItem ServerSpawnItem(string prefabName, Vector3 at, string instanceName = null)
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null || !nm.IsServerStarted) return null;
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == prefabName) { prefab = candidate; break; }
            }
            if (prefab == null) return null;
            NetworkObject instance = Object.Instantiate(prefab, at, Quaternion.identity);
            instance.name = instanceName ?? prefabName + " (checks)";
            CarryableItem item = instance.GetComponent<CarryableItem>();
            item.SetResetPositionBeforeSpawn(at);
            nm.ServerManager.Spawn(instance, null, WorldScenes.Scene(WorldId.Dive));
            return item;
        }

        // The host's eyes on a teammate's chest, so the dot lands on them.
        public static string ClientLookAtPlayer(HQPlayerController other)
        {
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            if (other == null || local == null) return "Missing player.";
            Vector3 chest = other.transform.position + Vector3.up * 1.0f;
            Vector3 to = chest - local.EyePosition;
            Vector3 flat = new(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.0001f) local.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            local.SetPitchForChecks(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
            return $"looking at {other.OwnerId}: distance={to.magnitude:0.00}";
        }

        // The host looks at a world point.
        public static void ClientLookAt(Vector3 point)
        {
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            if (local == null) return;
            Vector3 to = point - local.EyePosition;
            Vector3 flat = new(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.0001f) local.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            local.SetPitchForChecks(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
        }
    }
}
