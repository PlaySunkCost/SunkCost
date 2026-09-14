using System;
using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    public static class HQPrototypeValidator
    {
        [MenuItem("Sunk Cost/Prototype/Validate HQ")]
        public static void ValidateOrThrow()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != HQPrototypeBuilder.ScenePath) errors.Add("Open scene is not " + HQPrototypeBuilder.ScenePath);
            if (!System.IO.File.Exists(HQPrototypeBuilder.ScenePath)) errors.Add("HQ scene is not saved on disk.");
            CheckCount<NetworkManager>(scene, 1, errors);
            CheckCount<PlayerSpawner>(scene, 1, errors);
            CheckCount<PrototypeSessionUI>(scene, 1, errors);
            CheckCount<CarryableItem>(scene, HQPrototypeInventorySetup.SceneItemCount, errors);
            if (!HasRoot(scene, "HQ Room")) errors.Add("HQ Room is missing.");
            if (!HasRoot(scene, "Prototype Network Root")) errors.Add("Prototype Network Root is missing.");
            CheckPlayerPrefab(errors);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath)?.GetComponent<NetworkObject>() == null)
                errors.Add("Basketball prefab/NetworkObject is missing.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath)?.GetComponent<CarryableItem>() == null)
                errors.Add("Basketball prefab/CarryableItem is missing.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Net/SteamTransport.prefab")?.GetComponent<Transport>() == null)
                errors.Add("Configured Steam transport prefab is missing.");
            if (EditorBuildSettings.scenes.Length != 1 || !EditorBuildSettings.scenes[0].enabled || EditorBuildSettings.scenes[0].path != HQPrototypeBuilder.ScenePath)
                errors.Add("Build settings must contain only the enabled HQ prototype scene.");
            CheckLobbyPrerequisites(scene, errors);
            if (errors.Count > 0) throw new InvalidOperationException("HQ validation failed:\n- " + string.Join("\n- ", errors));
            Debug.Log($"HQ validation passed: saved scene, room, one network root, player prefab with inventory, {HQPrototypeInventorySetup.SceneItemCount} carryable items, four spawns and four-player transport caps are ready.");
        }

        // docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md section 10: the hold point pitches
        // with the camera and the player carries the inventory and HUD components.
        private static void CheckPlayerPrefab(List<string> errors)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            HQPlayerController controller = prefab != null ? prefab.GetComponent<HQPlayerController>() : null;
            if (controller == null) { errors.Add("Player prefab/controller is missing."); return; }
            if (prefab.GetComponent<PlayerInventory>() == null) errors.Add("Player prefab has no PlayerInventory (run Sunk Cost/Prototype/Apply inventory setup).");
            if (prefab.GetComponent<PlayerHudUI>() == null) errors.Add("Player prefab has no PlayerHudUI (run Sunk Cost/Prototype/Apply inventory setup).");
            using var serialized = new SerializedObject(controller);
            var camera = serialized.FindProperty("playerCamera").objectReferenceValue as Camera;
            var hold = serialized.FindProperty("holdPoint").objectReferenceValue as Transform;
            if (camera == null || hold == null) errors.Add("Player prefab is missing its camera or hold point reference.");
            else if (hold.parent != camera.transform) errors.Add("Player prefab HoldPoint is not a child of PlayerCamera (run Sunk Cost/Prototype/Apply inventory setup).");
        }

        // docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md section 11, step 5: the four-player
        // room needs four assigned spawn points and the corrected transport caps.
        private static void CheckLobbyPrerequisites(Scene scene, List<string> errors)
        {
            var settings = new SunkCost.Net.LobbySessionSettings();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerSpawner spawner in root.GetComponentsInChildren<PlayerSpawner>(true))
                {
                    int assigned = 0;
                    if (spawner.Spawns != null)
                        foreach (Transform spawn in spawner.Spawns) if (spawn != null) assigned++;
                    if (assigned < settings.LocalSocketCap)
                        errors.Add($"PlayerSpawner has {assigned} assigned spawn points; {settings.LocalSocketCap} are required.");
                }
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

        private static void CheckCount<T>(Scene scene, int expected, List<string> errors) where T : Component
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<T>(true).Length;
            if (count != expected) errors.Add($"Expected {expected} {typeof(T).Name}; found {count}.");
        }

        private static bool HasRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return true;
            return false;
        }
    }
}
