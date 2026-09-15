using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    public static class HQPrototypeValidator
    {
        // The HQ world scene (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 3.1):
        // room, spawn points, light, loot fixture spawner, stub dock. Nothing that
        // belongs to the Session scene may be here, and no carryable may be a scene
        // object (FishNet will not move those between scenes).
        [MenuItem("Sunk Cost/Prototype/Validate HQ")]
        public static void ValidateOrThrow()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != HQPrototypeBuilder.ScenePath) errors.Add("Open scene is not " + HQPrototypeBuilder.ScenePath);
            if (!System.IO.File.Exists(HQPrototypeBuilder.ScenePath)) errors.Add("HQ scene is not saved on disk.");
            CheckCount<NetworkManager>(scene, 0, errors);
            CheckCount<PrototypeSessionUI>(scene, 0, errors);
            CheckCount<CarryableItem>(scene, 0, errors);
            CheckCount<LootFixtureSpawner>(scene, 1, errors);
            CheckCount<AudioListener>(scene, 0, errors);
            CheckLootFixture(scene, errors);
            if (!HasRoot(scene, "HQ Room")) errors.Add("HQ Room is missing.");
            if (HasRoot(scene, "Prototype Network Root")) errors.Add("Prototype Network Root belongs in Session.unity, not the HQ world scene.");
            if (CrewSpawner.SpawnPointsIn(scene).Count != new LobbySessionSettings().LocalSocketCap)
                errors.Add($"HQ needs {new LobbySessionSettings().LocalSocketCap} spawn points under 'Spawn Points'.");
            WorldSceneChecks.CheckShip(scene, errors, "HQ");
            CheckPlayerPrefab(errors);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath)?.GetComponent<NetworkObject>() == null)
                errors.Add("Basketball prefab/NetworkObject is missing.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath)?.GetComponent<CarryableItem>() == null)
                errors.Add("Basketball prefab/CarryableItem is missing.");
            WorldSceneChecks.CheckBuildList(errors);
            if (errors.Count > 0) throw new InvalidOperationException("HQ validation failed:\n- " + string.Join("\n- ", errors));
            Debug.Log($"HQ validation passed: world scene with room, four spawns, light, a loot fixture spawner for {HQPrototypeLootSetup.SceneItemCount} items and the docked ship.");
        }

        // docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md section 9: every item prefab has a
        // valid mass, a sphere collider and the shared weight settings; two-handed
        // prefabs never claim a slot; the scene matches the manifest by name; heavy
        // prefabs are registered for spawning; the player has the two-hand point.
        private static void CheckLootFixture(Scene scene, List<string> errors)
        {
            var settings = AssetDatabase.LoadAssetAtPath<WeightSettings>(HQPrototypeLootSetup.WeightSettingsPath);
            if (settings == null) errors.Add("WeightSettings asset missing (run Sunk Cost/Prototype/Apply loot setup).");
            else if (!settings.IsValid) errors.Add("WeightSettings asset has an invalid value.");

            var sceneNames = new HashSet<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (LootFixtureSpawner spawner in root.GetComponentsInChildren<LootFixtureSpawner>(true))
                    foreach (LootFixtureSpawner.Entry entry in spawner.Entries)
                    {
                        sceneNames.Add(entry.Name);
                        if (entry.Prefab == null) errors.Add("Loot fixture entry " + entry.Name + " has no prefab.");
                        else if (entry.Prefab.GetComponent<CarryableItem>() == null) errors.Add("Loot fixture entry " + entry.Name + " is not a carryable prefab.");
                    }
            var collection = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.DefaultPrefabObjects>(HQPrototypeLootSetup.PrefabObjectsPath);

            var checkedPrefabs = new HashSet<string>();
            foreach (HQPrototypeLootSetup.FixtureEntry entry in HQPrototypeLootSetup.Manifest)
            {
                if (!sceneNames.Contains(entry.SceneName)) errors.Add("Loot fixture is missing " + entry.SceneName + " (run Apply loot setup).");
                if (!checkedPrefabs.Add(entry.PrefabPath)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                if (prefab == null) { errors.Add("Prefab missing: " + entry.PrefabPath); continue; }
                CarryableItem item = prefab.GetComponent<CarryableItem>();
                Rigidbody body = prefab.GetComponent<Rigidbody>();
                if (item == null || body == null) { errors.Add(entry.PrefabPath + " needs CarryableItem and Rigidbody."); continue; }
                if (!float.IsFinite(body.mass) || body.mass <= 0f) errors.Add(entry.PrefabPath + " has an invalid Rigidbody mass.");
                if (prefab.GetComponent<SphereCollider>() == null) errors.Add(entry.PrefabPath + " needs a SphereCollider (drop placement is sphere-only).");
                using var serialized = new SerializedObject(item);
                bool twoHands = serialized.FindProperty("grip").enumValueIndex == (int)CarryGrip.TwoHands;
                if (twoHands && serialized.FindProperty("fitsInSlot").boolValue) errors.Add(entry.PrefabPath + " is two-handed but claims to fit a slot.");
                if (serialized.FindProperty("weightSettings").objectReferenceValue != settings) errors.Add(entry.PrefabPath + " does not reference the shared WeightSettings.");
                if (!entry.IsBasketball && collection != null && !HQPrototypeLootSetup.IsRegistered(collection, prefab.GetComponent<NetworkObject>()))
                    errors.Add(entry.PrefabPath + " is not in PrototypePrefabObjects (run Apply loot setup).");
            }

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            HQPlayerController controller = player != null ? player.GetComponent<HQPlayerController>() : null;
            PlayerInventory inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
            if (controller != null)
            {
                using var serialized = new SerializedObject(controller);
                var camera = serialized.FindProperty("playerCamera").objectReferenceValue as Camera;
                var point = serialized.FindProperty("twoHandHoldPoint").objectReferenceValue as Transform;
                if (point == null) errors.Add("Player prefab has no TwoHandHoldPoint (run Apply loot setup).");
                else if (camera != null && point.parent != camera.transform) errors.Add("Player prefab TwoHandHoldPoint is not a child of PlayerCamera.");
            }
            if (inventory != null)
            {
                using var serialized = new SerializedObject(inventory);
                if (serialized.FindProperty("weightSettings").objectReferenceValue != settings) errors.Add("Player prefab PlayerInventory does not reference the shared WeightSettings.");
            }
        }

        // docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md section 10: the hold point pitches
        // with the camera and the player carries the inventory and HUD components.
        internal static void CheckPlayerPrefab(List<string> errors)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            HQPlayerController controller = prefab != null ? prefab.GetComponent<HQPlayerController>() : null;
            if (controller == null) { errors.Add("Player prefab/controller is missing."); return; }
            if (prefab.GetComponent<PlayerInventory>() == null) errors.Add("Player prefab has no PlayerInventory (run Sunk Cost/Prototype/Apply inventory setup).");
            if (prefab.GetComponent<PlayerHudUI>() == null) errors.Add("Player prefab has no PlayerHudUI (run Sunk Cost/Prototype/Apply inventory setup).");
            if (prefab.GetComponent<SunkCost.World.ShipControls>() == null) errors.Add("Player prefab has no ShipControls (run Sunk Cost/Prototype/Apply inventory setup).");
            if (prefab.GetComponent<SunkCost.World.ShipDepartureRider>() == null) errors.Add("Player prefab has no ShipDepartureRider (run Sunk Cost/Prototype/Apply inventory setup).");
            using var serialized = new SerializedObject(controller);
            var camera = serialized.FindProperty("playerCamera").objectReferenceValue as Camera;
            var hold = serialized.FindProperty("holdPoint").objectReferenceValue as Transform;
            if (camera == null || hold == null) errors.Add("Player prefab is missing its camera or hold point reference.");
            else if (hold.parent != camera.transform) errors.Add("Player prefab HoldPoint is not a child of PlayerCamera (run Sunk Cost/Prototype/Apply inventory setup).");
        }

        // docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md section 11, step 5: the four-player
        // room needs the corrected transport caps (now in the Session scene).
        internal static void CheckTransportCaps(Scene scene, List<string> errors)
        {
            var settings = new SunkCost.Net.LobbySessionSettings();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transport transport in root.GetComponentsInChildren<Transport>(true))
                {
                    if (transport.GetType().FullName != "FishNet.Transporting.Tugboat.Tugboat") continue;
                    int cap = ReadMaximumClients(transport);
                    if (cap != settings.LocalSocketCap)
                        errors.Add($"Scene Tugboat _maximumClients is {cap}; expected {settings.LocalSocketCap} (run Sunk Cost/Prototype/Apply lobby caps).");
                }
            }
            GameObject steamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.SteamTransportPrefabPath);
            Transport steam = steamPrefab != null ? steamPrefab.GetComponent<Transport>() : null;
            if (steam != null)
            {
                int cap = ReadMaximumClients(steam);
                if (cap != settings.SteamRemoteClientCap)
                    errors.Add($"Steam transport prefab _maximumClients is {cap}; expected {settings.SteamRemoteClientCap} (run Sunk Cost/Prototype/Apply lobby caps).");
            }
        }

        private static int ReadMaximumClients(Transport transport)
        {
            using var serialized = new SerializedObject(transport);
            SerializedProperty cap = serialized.FindProperty("_maximumClients");
            return cap != null ? cap.intValue : -1;
        }

        internal static void CheckCount<T>(Scene scene, int expected, List<string> errors) where T : Component
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<T>(true).Length;
            if (count != expected) errors.Add($"Expected {expected} {typeof(T).Name}; found {count}.");
        }

        internal static bool HasRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return true;
            return false;
        }
    }
}
