using System;
using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Observing;
using FishNet.Object;
using SunkCost.Net;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    public static class SessionSceneValidator
    {
        [MenuItem("Sunk Cost/Prototype/Validate Session")]
        public static void ValidateOrThrow()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != WorldScenes.SessionPath) errors.Add("Open scene is not " + WorldScenes.SessionPath);
            if (!System.IO.File.Exists(WorldScenes.SessionPath)) errors.Add("Session scene is not saved on disk.");
            HQPrototypeValidator.CheckCount<NetworkManager>(scene, 1, errors);
            HQPrototypeValidator.CheckCount<PlayerSpawner>(scene, 0, errors);
            HQPrototypeValidator.CheckCount<CrewSpawner>(scene, 1, errors);
            HQPrototypeValidator.CheckCount<WorldSceneFlow>(scene, 1, errors);
            HQPrototypeValidator.CheckCount<ObserverManager>(scene, 1, errors);
            HQPrototypeValidator.CheckCount<PrototypeSessionUI>(scene, 1, errors);
            HQPrototypeValidator.CheckCount<ScreenFade>(scene, 1, errors);
            HQPrototypeValidator.CheckCount<NetworkObject>(scene, 0, errors);
            if (!HQPrototypeValidator.HasRoot(scene, "Prototype Network Root")) errors.Add("Prototype Network Root is missing.");
            CheckObservers(scene, errors);
            CheckFlow(scene, errors);
            CheckDayStatePrefab(errors);
            HQPrototypeValidator.CheckPlayerPrefab(errors);
            HQPrototypeValidator.CheckTransportCaps(scene, errors);
            WorldSceneChecks.CheckBuildList(errors);
            if (errors.Count > 0) throw new InvalidOperationException("Session validation failed:\n- " + string.Join("\n- ", errors));
            Debug.Log("Session validation passed: one network root with scene-conditioned observers, crew spawner, world scene flow, session UI, fade, no world objects, build list in order.");
        }

        private static void CheckObservers(Scene scene, List<string> errors)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ObserverManager observers in root.GetComponentsInChildren<ObserverManager>(true))
                {
                    using var serialized = new SerializedObject(observers);
                    SerializedProperty conditions = serialized.FindProperty("_defaultConditions");
                    bool hasScene = false;
                    for (int i = 0; conditions != null && i < conditions.arraySize; i++)
                    {
                        var condition = conditions.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (condition != null && condition.GetType().Name == "SceneCondition") hasScene = true;
                    }
                    if (!hasScene) errors.Add("ObserverManager has no SceneCondition default condition (players would see every scene).");
                }
            }
        }

        private static void CheckFlow(Scene scene, List<string> errors)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (WorldSceneFlow flow in root.GetComponentsInChildren<WorldSceneFlow>(true))
                {
                    using var serialized = new SerializedObject(flow);
                    var settings = serialized.FindProperty("settings").objectReferenceValue as WorldLoopSettings;
                    if (settings == null) errors.Add("WorldSceneFlow has no WorldLoopSettings asset.");
                    else if (!settings.IsValid) errors.Add("WorldLoopSettings asset has an invalid value.");
                    if (serialized.FindProperty("dayStatePrefab").objectReferenceValue == null) errors.Add("WorldSceneFlow has no CrewDayState prefab.");
                }
                foreach (CrewSpawner spawner in root.GetComponentsInChildren<CrewSpawner>(true))
                {
                    using var serialized = new SerializedObject(spawner);
                    if (serialized.FindProperty("playerPrefab").objectReferenceValue == null) errors.Add("CrewSpawner has no player prefab.");
                }
                foreach (NetworkManager manager in root.GetComponentsInChildren<NetworkManager>(true))
                {
                    if (manager.SpawnablePrefabs == null) errors.Add("NetworkManager has no spawnable prefab collection.");
                }
            }
        }

        private static void CheckDayStatePrefab(List<string> errors)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionSceneBuilder.DayStatePrefabPath);
            NetworkObject nob = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (nob == null || prefab.GetComponent<CrewDayState>() == null) { errors.Add("CrewDayState prefab is missing (run Create or Update Session)."); return; }
            using var serialized = new SerializedObject(nob);
            if (!serialized.FindProperty("_isGlobal").boolValue) errors.Add("CrewDayState prefab must be a global NetworkObject.");
            var collection = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(SessionSceneBuilder.PrefabObjectsPath);
            if (collection == null || !HQPrototypeLootSetup.IsRegistered(collection, nob)) errors.Add("CrewDayState prefab is not in PrototypePrefabObjects.");
        }
    }
}
