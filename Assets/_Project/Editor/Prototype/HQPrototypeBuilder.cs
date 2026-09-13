using System;
using System.IO;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    public static class HQPrototypeBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Prototype/HQPrototype.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PrototypePlayer.prefab";
        private const string BallPrefabPath = "Assets/_Project/Prefabs/Interaction/Basketball.prefab";
        private const string SteamTransportPrefabPath = "Assets/_Project/Prefabs/Net/SteamTransport.prefab";
        private const string MaterialPath = "Assets/_Project/Art/Prototype/Materials";

        [MenuItem("Sunk Cost/Prototype/Create or Update HQ")]
        public static void CreateOrUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the HQ scene.");

            EnsureFolder("Assets/_Project/Scenes/Prototype");
            EnsureFolder("Assets/_Project/Prefabs/Player");
            EnsureFolder("Assets/_Project/Prefabs/Interaction");
            EnsureFolder("Assets/_Project/Prefabs/Net");
            EnsureFolder(MaterialPath);

            Material floorMaterial = GetOrCreateMaterial(MaterialPath + "/HQFloor.mat", new Color(0.19f, 0.22f, 0.25f));
            Material wallMaterial = GetOrCreateMaterial(MaterialPath + "/HQWall.mat", new Color(0.34f, 0.38f, 0.42f));
            Material ballMaterial = GetOrCreateMaterial(MaterialPath + "/BallOrange.mat", new Color(0.95f, 0.28f, 0.035f));
            Material playerMaterial = GetOrCreateMaterial(MaterialPath + "/PlayerBase.mat", Color.white);

            GameObject playerPrefab = CreatePlayerPrefab(playerMaterial);
            GameObject ballPrefab = CreateBallPrefab(ballMaterial);
            GameObject steamTransportPrefab = CreateSteamTransportPrefab();
            AssetDatabase.SaveAssets();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "HQPrototype";

            CreateRoom(floorMaterial, wallMaterial);
            Transform[] spawnPoints = CreateSpawnPoints();
            Camera preview = CreatePreviewCamera();
            CreateLight();
            CreateBallInstance(ballPrefab);
            CreateNetworkAndUI(playerPrefab, spawnPoints, preview, steamTransportPrefab);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save " + ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            HQPrototypeValidator.ValidateOrThrow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("HQ prototype created and validated at " + ScenePath);
        }

        private static GameObject CreatePlayerPrefab(Material material)
        {
            GameObject root = new("PrototypePlayer");
            try
            {
                root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                networkTransform.SetSynchronizeScale(false);
                CharacterController character = root.AddComponent<CharacterController>();
                character.height = 1.8f;
                character.radius = 0.3f;
                character.center = new Vector3(0f, 0.9f, 0f);
                character.stepOffset = 0.25f;
                character.slopeLimit = 45f;
                character.skinWidth = 0.03f;
                HQPlayerController controller = root.AddComponent<HQPlayerController>();

                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = material;

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "ForwardMarker";
                marker.transform.SetParent(body.transform, false);
                marker.transform.localPosition = new Vector3(0f, 0.35f, 0.55f);
                marker.transform.localScale = new Vector3(0.2f, 0.16f, 0.2f);
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.GetComponent<Renderer>().sharedMaterial = material;

                GameObject pivot = new("ViewPivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                GameObject cameraObject = new("PlayerCamera", typeof(Camera), typeof(AudioListener));
                cameraObject.transform.SetParent(pivot.transform, false);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.fieldOfView = 75f;
                camera.enabled = false;
                cameraObject.GetComponent<AudioListener>().enabled = false;

                GameObject hold = new("HoldPoint");
                hold.transform.SetParent(pivot.transform, false);
                hold.transform.localPosition = new Vector3(0.35f, -0.25f, 1.1f);

                SerializedObject serialized = new(controller);
                serialized.FindProperty("playerCamera").objectReferenceValue = camera;
                serialized.FindProperty("holdPoint").objectReferenceValue = hold.transform;
                serialized.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateBallPrefab(Material material)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "Basketball";
            try
            {
                root.transform.localScale = Vector3.one * 0.24f;
                root.GetComponent<Renderer>().sharedMaterial = material;
                SphereCollider collider = root.GetComponent<SphereCollider>();
                collider.radius = 0.5f;
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 0.62f;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.1f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                networkTransform.SetSynchronizeScale(false);
                root.AddComponent<Basketball>();
                return PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateSteamTransportPrefab()
        {
            GameObject root = new("Steam Transport");
            try
            {
                Type fishyType = FindType("FishySteamworks.FishySteamworks");
                if (fishyType == null || !typeof(Transport).IsAssignableFrom(fishyType))
                    throw new InvalidOperationException("FishySteamworks transport type was not imported.");
                Transport fishy = (Transport)root.AddComponent(fishyType);
                SetPrivate(fishy, "_maximumClients", 2);
                SetPrivate(fishy, "_peerToPeer", true);
                return PrefabUtility.SaveAsPrefabAsset(root, SteamTransportPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CreateRoom(Material floor, Material wall)
        {
            GameObject room = new("HQ Room");
            CreateBlock("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(12f, 0.5f, 12f), floor, room.transform);
            CreateBlock("North Wall", new Vector3(0f, 1.75f, 6f), new Vector3(12f, 3.5f, 0.3f), wall, room.transform);
            CreateBlock("South Wall", new Vector3(0f, 1.75f, -6f), new Vector3(12f, 3.5f, 0.3f), wall, room.transform);
            CreateBlock("East Wall", new Vector3(6f, 1.75f, 0f), new Vector3(0.3f, 3.5f, 12f), wall, room.transform);
            CreateBlock("West Wall", new Vector3(-6f, 1.75f, 0f), new Vector3(0.3f, 3.5f, 12f), wall, room.transform);
            CreateBlock("Ceiling", new Vector3(0f, 3.65f, 0f), new Vector3(12f, 0.3f, 12f), wall, room.transform);
        }

        private static void CreateBlock(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent);
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Transform[] CreateSpawnPoints()
        {
            GameObject root = new("Spawn Points");
            Vector3[] positions = { new(-3f, 0f, -3f), new(3f, 0f, -3f), new(-3f, 0f, 3f), new(3f, 0f, 3f) };
            Transform[] result = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject marker = new("Spawn " + (i + 1));
                marker.transform.SetParent(root.transform);
                marker.transform.position = positions[i];
                marker.transform.LookAt(Vector3.zero);
                result[i] = marker.transform;
            }
            return result;
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

        private static void CreateLight()
        {
            GameObject go = new("HQ Light", typeof(Light));
            go.transform.SetPositionAndRotation(new Vector3(0f, 3.2f, 0f), Quaternion.Euler(90f, 0f, 0f));
            Light light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.range = 18f;
            light.intensity = 3f;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateBallInstance(GameObject prefab)
        {
            GameObject ball = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ball.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
        }

        private static void CreateNetworkAndUI(GameObject playerPrefab, Transform[] spawns, Camera preview, GameObject steamTransportPrefab)
        {
            GameObject networkRoot = new("Prototype Network Root");
            networkRoot.SetActive(false);
            NetworkManager manager = networkRoot.AddComponent<NetworkManager>();
            TransportManager transportManager = networkRoot.AddComponent<TransportManager>();
            GameObject localTransportObject = new("Local Transport");
            localTransportObject.transform.SetParent(networkRoot.transform, false);
            Tugboat tugboat = localTransportObject.AddComponent<Tugboat>();
            transportManager.Transport = tugboat;
            PlayerSpawner spawner = networkRoot.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab.GetComponent<NetworkObject>());
            spawner.Spawns = spawns;

            SetPrivate(tugboat, "_maximumClients", 2);

            GameObject uiObject = new("Prototype Session UI");
            PrototypeSessionUI ui = uiObject.AddComponent<PrototypeSessionUI>();
            SerializedObject serialized = new(ui);
            serialized.FindProperty("networkRoot").objectReferenceValue = networkRoot;
            serialized.FindProperty("networkManager").objectReferenceValue = manager;
            serialized.FindProperty("transportManager").objectReferenceValue = transportManager;
            serialized.FindProperty("localTransport").objectReferenceValue = tugboat;
            serialized.FindProperty("steamTransportPrefab").objectReferenceValue = steamTransportPrefab;
            serialized.FindProperty("previewCamera").objectReferenceValue = preview;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            const string defaultsPath = "Assets/_Project/Settings/Prototype/PrototypePrefabObjects.asset";
            EnsureFolder("Assets/_Project/Settings/Prototype");
            DefaultPrefabObjects defaults = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(defaultsPath);
            if (defaults == null)
            {
                defaults = ScriptableObject.CreateInstance<DefaultPrefabObjects>();
                AssetDatabase.CreateAsset(defaults, defaultsPath);
            }
            defaults.Clear();
            defaults.AddObject(playerPrefab.GetComponent<NetworkObject>(), checkForDuplicates: true, initializeAdded: true);
            defaults.AddObject(AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath).GetComponent<NetworkObject>(), checkForDuplicates: true, initializeAdded: true);
            EditorUtility.SetDirty(defaults);
            manager.SpawnablePrefabs = defaults;
        }

        private static Type FindType(string fullName)
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Transport>())
            {
                if (type.FullName == fullName) return type;
            }
            return null;
        }

        private static void SetPrivate(Object target, string name, int value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(name).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPrivate(Object target, string name, bool value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(name).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material GetOrCreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string current = "Assets";
            foreach (string part in path.Substring("Assets/".Length).Split('/'))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }
    }
}
