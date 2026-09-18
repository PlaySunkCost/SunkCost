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
            if (!HasRoot(scene, HQPrototypeBuilder.ShopRoomName)) errors.Add("Shop Room is missing (18 September 2026).");
            var planks = new System.Collections.Generic.List<SunkCost.World.HQPlank>();
            foreach (GameObject root in scene.GetRootGameObjects()) planks.AddRange(root.GetComponentsInChildren<SunkCost.World.HQPlank>(true));
            if (planks.Count != 1) errors.Add("HQ needs exactly one plank (HQPlank; found " + planks.Count + ").");
            else if (planks[0].Base == null || planks[0].End == null) errors.Add("The plank needs its Base and End markers.");
            var stands = new System.Collections.Generic.List<SunkCost.Shop.ShopDisplay>();
            foreach (GameObject root in scene.GetRootGameObjects()) stands.AddRange(root.GetComponentsInChildren<SunkCost.Shop.ShopDisplay>(true));
            var catalog = AssetDatabase.LoadAssetAtPath<SunkCost.Shop.ShopCatalog>(ShopSetup.CatalogPath);
            if (catalog == null) errors.Add("ShopCatalog asset missing (run Sunk Cost/Prototype/Apply shop setup).");
            else
            {
                foreach (SunkCost.Shop.ShopItem item in catalog.Items)
                {
                    if (!stands.Exists(s => s.ItemId == item.Id)) errors.Add("The shop has no stand for " + item.Id + ".");
                    if (item.Kind == SunkCost.Shop.ShopItemKind.Consumable && (item.Prefab == null || item.Prefab.GetComponent<NetworkObject>() == null)) errors.Add("Catalogue item " + item.Id + " needs a networked prefab.");
                    if (item.Kind == SunkCost.Shop.ShopItemKind.Upgrade && item.Upgrade == SunkCost.Shop.PlayerUpgrade.None) errors.Add("Catalogue item " + item.Id + " names no upgrade.");
                }
                foreach (SunkCost.Shop.ShopDisplay stand in stands)
                {
                    if (catalog.Find(stand.ItemId) == null) errors.Add("Shop stand " + stand.name + " sells an id the catalogue does not have: " + stand.ItemId);
                    if (stand.GetComponentInChildren<Collider>() == null) errors.Add("Shop stand " + stand.name + " needs a collider to be looked at.");
                }
            }
            var deliveries = new System.Collections.Generic.List<SunkCost.Shop.ShopDeliveryPoint>();
            foreach (GameObject root in scene.GetRootGameObjects()) deliveries.AddRange(root.GetComponentsInChildren<SunkCost.Shop.ShopDeliveryPoint>(true));
            if (deliveries.Count == 0) errors.Add("The shop needs a ShopDeliveryPoint.");
            var panels = new System.Collections.Generic.List<SunkCost.World.ColourPanel>();
            foreach (GameObject root in scene.GetRootGameObjects()) panels.AddRange(root.GetComponentsInChildren<SunkCost.World.ColourPanel>(true));
            if (panels.Count != 1) errors.Add("HQ needs exactly one Colour Panel (found " + panels.Count + ").");
            var boards = new System.Collections.Generic.List<SunkCost.World.QuotaBoard>();
            foreach (GameObject root in scene.GetRootGameObjects()) boards.AddRange(root.GetComponentsInChildren<SunkCost.World.QuotaBoard>(true));
            if (boards.Count != 1) errors.Add("HQ needs exactly one Quota Board (found " + boards.Count + ").");
            else if (boards[0].GetComponent<Collider>() == null) errors.Add("The Quota Board needs a collider to be looked at.");
            else if (panels[0].GetComponent<Collider>() == null || panels[0].transform.Find(SunkCost.World.ColourPanel.SwatchName) == null) errors.Add("The Colour Panel needs its collider and swatch.");
            if (HasRoot(scene, "Prototype Network Root")) errors.Add("Prototype Network Root belongs in Session.unity, not the HQ world scene.");
            if (CrewSpawner.SpawnPointsIn(scene).Count != new LobbySessionSettings().LocalSocketCap)
                errors.Add($"HQ needs {new LobbySessionSettings().LocalSocketCap} spawn points under 'Spawn Points'.");
            WorldSceneChecks.CheckShip(scene, errors, "HQ");
            CheckPlayerPrefab(errors);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath)?.GetComponent<NetworkObject>() == null)
                errors.Add("Basketball prefab/NetworkObject is missing.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath)?.GetComponent<CarryableItem>() == null)
                errors.Add("Basketball prefab/CarryableItem is missing.");
            // Every carryable prefab in the fixture: grip targets for the hands, the Carryable layer.
            var carryablePaths = new List<string> { HQPrototypeBuilder.BallPrefabPath };
            foreach (HQPrototypeLootSetup.FixtureEntry entry in HQPrototypeLootSetup.Manifest) if (!carryablePaths.Contains(entry.PrefabPath)) carryablePaths.Add(entry.PrefabPath);
            foreach (string path in carryablePaths)
            {
                GameObject carryable = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (carryable == null) continue;
                ItemHandPose pose = carryable.GetComponent<ItemHandPose>();
                if (pose == null || !pose.IsComplete) errors.Add(carryable.name + " has no complete ItemHandPose (run the movement and hands setup).");
                if (CarryableCollisionPolicy.LayersExist && carryable.layer != CarryableCollisionPolicy.CarryableLayer) errors.Add(carryable.name + " is not on the Carryable layer.");
            }
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
                if (body.interpolation != RigidbodyInterpolation.Interpolate) errors.Add(entry.PrefabPath + " Rigidbody must interpolate, or a thrown item steps at the physics rate (run Apply loot setup).");
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
            // Movement and hands card (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md).
            if (prefab.GetComponent<PlayerStance>() == null) errors.Add("Player prefab has no PlayerStance (run Sunk Cost/Prototype/Apply movement and hands setup).");
            if (prefab.GetComponent<PlayerHands>() == null) errors.Add("Player prefab has no PlayerHands (run the movement and hands setup).");
            if (prefab.GetComponent<PlayerSubmersion>() == null) errors.Add("Player prefab has no PlayerSubmersion (run Sunk Cost/Prototype/Apply shaft tube setup).");
            PlayerCameraClearance clearance = prefab.GetComponent<PlayerCameraClearance>();
            if (clearance == null) errors.Add("Player prefab has no PlayerCameraClearance (run Sunk Cost/Prototype/Apply camera clearance setup).");
            else
            {
                SerializedObject clearanceSerialized = new(clearance);
                if (clearanceSerialized.FindProperty("playerCamera").objectReferenceValue == null || clearanceSerialized.FindProperty("viewPivot").objectReferenceValue == null) errors.Add("PlayerCameraClearance is missing its camera or ViewPivot reference.");
                if (clearanceSerialized.FindProperty("settings").objectReferenceValue == null) errors.Add("PlayerCameraClearance has no PlayerCameraSettings.");
            }
            PlayerCameraSettings cameraSettings = AssetDatabase.LoadAssetAtPath<PlayerCameraSettings>(PlayerCameraClearanceSetup.SettingsPath);
            if (cameraSettings == null || !cameraSettings.IsValid) errors.Add("PlayerCameraSettings asset missing or invalid.");
            Camera prefabCamera = prefab.GetComponentInChildren<Camera>(true);
            if (prefabCamera != null && cameraSettings != null && !Mathf.Approximately(prefabCamera.nearClipPlane, cameraSettings.NearClip)) errors.Add($"Player camera near clip is {prefabCamera.nearClipPlane}, expected {cameraSettings.NearClip}.");
            if (prefabCamera != null && prefabCamera.transform.localPosition != Vector3.zero) errors.Add("Player camera must sit on the ViewPivot; the clearance offset is runtime only.");
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                if (t.name == PlayerMovementHandsSetup.ForwardMarkerName) errors.Add("Player prefab still has a ForwardMarker.");
            if (prefab.transform.Find(PlayerHands.ArmRightName) == null || prefab.transform.Find(PlayerHands.ArmLeftName) == null) errors.Add("Player prefab has no arms.");
            SerializedObject controllerSerialized = new(prefab.GetComponent<HQPlayerController>());
            if (controllerSerialized.FindProperty("movement").objectReferenceValue == null) errors.Add("Player prefab HQPlayerController has no PlayerMovementSettings.");
            if (controllerSerialized.FindProperty("viewPivot").objectReferenceValue == null) errors.Add("Player prefab HQPlayerController has no viewPivot.");
            if (!SunkCost.Interaction.CarryableCollisionPolicy.LayersExist) errors.Add("Player/Carryable layers missing (run the movement and hands setup).");
            else if (prefab.layer != SunkCost.Interaction.CarryableCollisionPolicy.PlayerLayer) errors.Add("Player prefab is not on the Player layer.");
            PlayerMovementSettings movement = AssetDatabase.LoadAssetAtPath<PlayerMovementSettings>(PlayerMovementHandsSetup.SettingsPath);
            if (movement == null || !movement.IsValid) errors.Add("PlayerMovementSettings asset missing or invalid.");
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
