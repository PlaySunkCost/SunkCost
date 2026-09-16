using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Observing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Observing;
using FishNet.Transporting.Tugboat;
using SunkCost.Net;
using SunkCost.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The persistent Session scene (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section
    // 3.1): the network root with its managers, the crew spawner and the scene
    // flow, the session UI, the preview camera and the fade. No world geometry and
    // no NetworkObjects: FishNet never loads or unloads this scene.
    public static class SessionSceneBuilder
    {
        // The network tick equals the physics rate (Fixed Timestep 1/60), so every
        // tick samples exactly one physics step (docs/NETWORK_CONTRACT.md section 7,
        // decided 16 September 2026). FishNet's default is 30.
        public const ushort NetworkTickRate = 60;

        public const string DayStatePrefabPath = "Assets/_Project/Prefabs/World/CrewDayState.prefab";
        public const string SceneConditionAssetPath = "Packages/com.firstgeargames.fishnet/Runtime/Observing/Conditions/ScriptableObjects/SceneCondition.asset";
        public const string PrefabObjectsPath = "Assets/_Project/Settings/Prototype/PrototypePrefabObjects.asset";

        [MenuItem("Sunk Cost/Prototype/Create or Update Session")]
        public static void CreateOrUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the Session scene.");
            HQPrototypeBuilder.EnsurePrefabs(out GameObject playerPrefab, out GameObject ballPrefab, out GameObject steamTransportPrefab);
            GameObject dayStatePrefab = EnsureDayStatePrefab();
            WorldLoopSettings settings = ShipStubBuilder.EnsureSettings();
            RegisterSpawnablePrefabs(playerPrefab, ballPrefab, dayStatePrefab);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = WorldScenes.SessionName;
            // NewScene unloads assets nothing references; take the references again.
            playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            steamTransportPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.SteamTransportPrefabPath);
            dayStatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DayStatePrefabPath);
            settings = AssetDatabase.LoadAssetAtPath<WorldLoopSettings>(ShipStubBuilder.SettingsPath);
            Camera preview = CreatePreviewCamera();
            CreateNetworkAndUI(playerPrefab, steamTransportPrefab, dayStatePrefab, settings, preview);
            GameObject fade = new("Screen Fade");
            fade.AddComponent<ScreenFade>();

            if (!EditorSceneManager.SaveScene(scene, WorldScenes.SessionPath))
                throw new InvalidOperationException("Unity could not save " + WorldScenes.SessionPath);
            WriteBuildList();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SessionSceneValidator.ValidateOrThrow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldScenes.SessionPath);
            Debug.Log("Session scene created and validated at " + WorldScenes.SessionPath);
        }

        // Session first (index 0), then every world scene that exists on disk.
        public static void WriteBuildList()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string path in new[] { WorldScenes.SessionPath, WorldScenes.HQPath, WorldScenes.SeaPath, WorldScenes.DivePath })
                if (System.IO.File.Exists(path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // Global object: DontDestroyOnLoad, no observer conditions (section 4.1).
        private static GameObject EnsureDayStatePrefab()
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Prefabs/World");
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(DayStatePrefabPath);
            if (existing != null && existing.GetComponent<CrewDayState>() != null && existing.GetComponent<NetworkObject>() != null)
            {
                using var serialized = new SerializedObject(existing.GetComponent<NetworkObject>());
                if (serialized.FindProperty("_isGlobal").boolValue) return existing;
            }
            GameObject root = new("CrewDayState");
            try
            {
                NetworkObject nob = root.AddComponent<NetworkObject>();
                HQPrototypeBuilder.SetPrivate(nob, "_isGlobal", true);
                root.AddComponent<CrewDayState>();
                return PrefabUtility.SaveAsPrefabAsset(root, DayStatePrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // The spawnable collection keeps what the loot setup added (heavy balls);
        // only missing entries are appended.
        private static void RegisterSpawnablePrefabs(params GameObject[] prefabs)
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Settings/Prototype");
            DefaultPrefabObjects defaults = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(PrefabObjectsPath);
            if (defaults == null)
            {
                defaults = ScriptableObject.CreateInstance<DefaultPrefabObjects>();
                AssetDatabase.CreateAsset(defaults, PrefabObjectsPath);
            }
            foreach (GameObject prefab in prefabs)
            {
                NetworkObject nob = prefab.GetComponent<NetworkObject>();
                if (nob != null && !HQPrototypeLootSetup.IsRegistered(defaults, nob))
                    defaults.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
            }
            EditorUtility.SetDirty(defaults);
        }

        private static Camera CreatePreviewCamera()
        {
            GameObject go = new("Preview Camera", typeof(Camera), typeof(AudioListener));
            go.transform.SetPositionAndRotation(new Vector3(0f, 7f, -10f), Quaternion.Euler(25f, 0f, 0f));
            Camera camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f);
            return camera;
        }

        private static void CreateNetworkAndUI(GameObject playerPrefab, GameObject steamTransportPrefab, GameObject dayStatePrefab, WorldLoopSettings settings, Camera preview)
        {
            GameObject networkRoot = new("Prototype Network Root");
            networkRoot.SetActive(false);
            NetworkManager manager = networkRoot.AddComponent<NetworkManager>();
            FishNet.Managing.Timing.TimeManager timeManager = networkRoot.AddComponent<FishNet.Managing.Timing.TimeManager>();
            HQPrototypeBuilder.SetPrivate(timeManager, "_tickRate", NetworkTickRate);
            TransportManager transportManager = networkRoot.AddComponent<TransportManager>();
            GameObject localTransportObject = new("Local Transport");
            localTransportObject.transform.SetParent(networkRoot.transform, false);
            Tugboat tugboat = localTransportObject.AddComponent<Tugboat>();
            transportManager.Transport = tugboat;
            HQPrototypeBuilder.SetPrivate(tugboat, "_maximumClients", new LobbySessionSettings().LocalSocketCap); // Tugboat counts the host loopback socket

            // Observers follow scene membership: a connection sees an object only when
            // it is in the object's Unity scene (section 2). Global objects are exempt.
            ObserverManager observers = networkRoot.AddComponent<ObserverManager>();
            ObserverCondition sceneCondition = AssetDatabase.LoadAssetAtPath<ObserverCondition>(SceneConditionAssetPath);
            if (sceneCondition == null) throw new InvalidOperationException("FishNet SceneCondition asset not found at " + SceneConditionAssetPath);
            using (var serialized = new SerializedObject(observers))
            {
                SerializedProperty conditions = serialized.FindProperty("_defaultConditions");
                conditions.ClearArray();
                conditions.InsertArrayElementAtIndex(0);
                conditions.GetArrayElementAtIndex(0).objectReferenceValue = sceneCondition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            WorldSceneFlow flow = networkRoot.AddComponent<WorldSceneFlow>();
            using (var serialized = new SerializedObject(flow))
            {
                serialized.FindProperty("settings").objectReferenceValue = settings;
                serialized.FindProperty("dayStatePrefab").objectReferenceValue = dayStatePrefab.GetComponent<NetworkObject>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            CrewSpawner spawner = networkRoot.AddComponent<CrewSpawner>();
            spawner.SetPlayerPrefab(playerPrefab.GetComponent<NetworkObject>());

            GameObject uiObject = new("Prototype Session UI");
            PrototypeSessionUI ui = uiObject.AddComponent<PrototypeSessionUI>();
            using (var serialized = new SerializedObject(ui))
            {
                serialized.FindProperty("networkRoot").objectReferenceValue = networkRoot;
                serialized.FindProperty("networkManager").objectReferenceValue = manager;
                serialized.FindProperty("transportManager").objectReferenceValue = transportManager;
                serialized.FindProperty("localTransport").objectReferenceValue = tugboat;
                serialized.FindProperty("steamTransportPrefab").objectReferenceValue = steamTransportPrefab;
                serialized.FindProperty("previewCamera").objectReferenceValue = preview;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            manager.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(PrefabObjectsPath);
        }
    }
}
