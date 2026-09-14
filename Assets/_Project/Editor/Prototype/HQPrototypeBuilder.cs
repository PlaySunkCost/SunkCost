using System;
using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Transporting;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.World;
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
        public const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PrototypePlayer.prefab";
        public const string BallPrefabPath = "Assets/_Project/Prefabs/Interaction/Basketball.prefab";
        // Lower-right of the camera, 60 cm out: the right hand (plan section 10).
        public static readonly Vector3 HoldPointLocalPosition = new(0.30f, -0.22f, 0.60f);
        public const string SteamTransportPrefabPath = "Assets/_Project/Prefabs/Net/SteamTransport.prefab";
        public const string MaterialPath = "Assets/_Project/Art/Prototype/Materials";

        public const float PlankLength = 6f;
        public const float PlankWidth = 1.6f;
        public const float DoorwayWidth = 2.4f;

        // The HQ world scene: the room, its spawn points, the light, the loot fixture
        // spawner and the stub dock (plank + docked ship). No network root, no UI, no
        // camera: those live in Session.unity (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
        // section 3.1). Prefabs are (re)written here because the loot setup and the
        // Session builder both need them.
        [MenuItem("Sunk Cost/Prototype/Create or Update HQ")]
        public static void CreateOrUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the HQ scene.");
            EnsurePrefabs(out _, out GameObject ballPrefab, out _);
            GameObject shipPrefab = ShipStubBuilder.EnsurePrefab();
            Material floorMaterial = GetOrCreateMaterial(MaterialPath + "/HQFloor.mat", new Color(0.19f, 0.22f, 0.25f));
            Material wallMaterial = GetOrCreateMaterial(MaterialPath + "/HQWall.mat", new Color(0.34f, 0.38f, 0.42f));
            Material plankMaterial = GetOrCreateMaterial(MaterialPath + "/HQPlank.mat", new Color(0.42f, 0.33f, 0.22f));
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "HQPrototype";
            // NewScene unloads assets nothing references; take the references again.
            ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipStubBuilder.PrefabPath);
            floorMaterial = GetOrCreateMaterial(MaterialPath + "/HQFloor.mat", new Color(0.19f, 0.22f, 0.25f));
            wallMaterial = GetOrCreateMaterial(MaterialPath + "/HQWall.mat", new Color(0.34f, 0.38f, 0.42f));
            plankMaterial = GetOrCreateMaterial(MaterialPath + "/HQPlank.mat", new Color(0.42f, 0.33f, 0.22f));
            CreateRoom(floorMaterial, wallMaterial);
            CreateSpawnPoints();
            CreateLight();
            CreateLootFixture(ballPrefab);
            CreateDock(shipPrefab, plankMaterial);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save " + ScenePath);
            SessionSceneBuilder.WriteBuildList();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            HQPrototypeValidator.ValidateOrThrow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("HQ world scene created and validated at " + ScenePath);
        }

        // Player, basketball and Steam transport prefabs, written from code only
        // when missing: the setups (inventory, loot, lobby) patch the existing ones
        // and a scene rebuild must not undo their tested settings. "Rebuild
        // prototype prefabs" forces a rewrite.
        internal static void EnsurePrefabs(out GameObject playerPrefab, out GameObject ballPrefab, out GameObject steamTransportPrefab, bool force = false)
        {
            EnsureFolder("Assets/_Project/Scenes/Prototype");
            EnsureFolder("Assets/_Project/Prefabs/Player");
            EnsureFolder("Assets/_Project/Prefabs/Interaction");
            EnsureFolder("Assets/_Project/Prefabs/Net");
            EnsureFolder(MaterialPath);
            Material ballMaterial = GetOrCreateMaterial(MaterialPath + "/BallOrange.mat", new Color(0.95f, 0.28f, 0.035f));
            Material playerMaterial = GetOrCreateMaterial(MaterialPath + "/PlayerBase.mat", Color.white);
            playerPrefab = force ? null : AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null) playerPrefab = CreatePlayerPrefab(playerMaterial);
            ballPrefab = force ? null : AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            if (ballPrefab == null) ballPrefab = CreateBallPrefab(ballMaterial);
            steamTransportPrefab = force ? null : AssetDatabase.LoadAssetAtPath<GameObject>(SteamTransportPrefabPath);
            if (steamTransportPrefab == null) steamTransportPrefab = CreateSteamTransportPrefab();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Sunk Cost/Prototype/Rebuild prototype prefabs")]
        public static void RebuildPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before rebuilding prefabs.");
            EnsurePrefabs(out _, out _, out _, force: true);
            Debug.Log("Player, basketball and Steam transport prefabs rewritten; re-run the inventory, loot and lobby setups.");
        }

        internal static GameObject CreatePlayerPrefab(Material material)
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
                root.AddComponent<PlayerInventory>();
                root.AddComponent<PlayerHudUI>();

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

                // Under the camera so it pitches with the view; a held item is
                // snapped here every frame by CarryableItem.
                GameObject hold = new("HoldPoint");
                hold.transform.SetParent(cameraObject.transform, false);
                hold.transform.localPosition = HoldPointLocalPosition;
                GameObject twoHand = new("TwoHandHoldPoint");
                twoHand.transform.SetParent(cameraObject.transform, false);
                twoHand.transform.localPosition = HQPrototypeLootSetup.TwoHandHoldPointLocalPosition;

                SerializedObject serialized = new(controller);
                serialized.FindProperty("playerCamera").objectReferenceValue = camera;
                serialized.FindProperty("holdPoint").objectReferenceValue = hold.transform;
                serialized.FindProperty("twoHandHoldPoint").objectReferenceValue = twoHand.transform;
                serialized.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        internal static GameObject CreateBallPrefab(Material material)
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
                CarryableItem item = root.AddComponent<CarryableItem>();
                SerializedObject serialized = new(item);
                serialized.FindProperty("displayName").stringValue = "Basketball";
                serialized.FindProperty("fitsInSlot").boolValue = true;
                serialized.FindProperty("useAction").enumValueIndex = (int)ItemUseAction.Throw;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        internal static GameObject CreateSteamTransportPrefab()
        {
            GameObject root = new("Steam Transport");
            try
            {
                Type fishyType = FindType("FishySteamworks.FishySteamworks");
                if (fishyType == null || !typeof(Transport).IsAssignableFrom(fishyType))
                    throw new InvalidOperationException("FishySteamworks transport type was not imported.");
                Transport fishy = (Transport)root.AddComponent(fishyType);
                SetPrivate(fishy, "_maximumClients", new SunkCost.Net.LobbySessionSettings().SteamRemoteClientCap); // 3 remote + host = 4
                SetPrivate(fishy, "_peerToPeer", true);
                return PrefabUtility.SaveAsPrefabAsset(root, SteamTransportPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CreateRoom(Material floor, Material wall)
        {
            GameObject room = new("HQ Room");
            CreateBlock("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(12f, 0.5f, 12f), floor, room.transform);
            // The north wall has a doorway onto the plank to the docked ship.
            float side = (12f - DoorwayWidth) / 2f;
            CreateBlock("North Wall West", new Vector3(-(DoorwayWidth / 2f + side / 2f), 1.75f, 6f), new Vector3(side, 3.5f, 0.3f), wall, room.transform);
            CreateBlock("North Wall East", new Vector3(DoorwayWidth / 2f + side / 2f, 1.75f, 6f), new Vector3(side, 3.5f, 0.3f), wall, room.transform);
            CreateBlock("North Wall Lintel", new Vector3(0f, 3.0f, 6f), new Vector3(DoorwayWidth, 1.0f, 0.3f), wall, room.transform);
            CreateBlock("South Wall", new Vector3(0f, 1.75f, -6f), new Vector3(12f, 3.5f, 0.3f), wall, room.transform);
            CreateBlock("East Wall", new Vector3(6f, 1.75f, 0f), new Vector3(0.3f, 3.5f, 12f), wall, room.transform);
            CreateBlock("West Wall", new Vector3(-6f, 1.75f, 0f), new Vector3(0.3f, 3.5f, 12f), wall, room.transform);
            CreateBlock("Ceiling", new Vector3(0f, 3.65f, 0f), new Vector3(12f, 0.3f, 12f), wall, room.transform);
        }

        internal static void CreateBlock(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent);
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
        }

        internal static Transform[] CreateSpawnPoints()
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

        // The seven balls are spawned at runtime by LootFixtureSpawner (FishNet will
        // not move scene objects between scenes). The builder seeds the basketballs;
        // Apply loot setup reconciles the entries with the full manifest once the
        // heavy prefabs exist.
        private static void CreateLootFixture(GameObject ballPrefab)
        {
            GameObject fixture = new("Loot Fixture");
            LootFixtureSpawner spawner = fixture.AddComponent<LootFixtureSpawner>();
            var entries = new List<LootFixtureSpawner.Entry>();
            foreach (HQPrototypeLootSetup.FixtureEntry entry in HQPrototypeLootSetup.Manifest)
            {
                GameObject prefab = entry.IsBasketball ? ballPrefab : AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                if (prefab == null) continue;
                entries.Add(new LootFixtureSpawner.Entry { Name = entry.SceneName, Prefab = prefab, Position = entry.ResetPosition });
            }
            spawner.SetEntries(entries.ToArray());
        }

        // Stub dock until Idan's pier card: a plank through the north doorway onto
        // the docked ship, whose BoardingPoint meets the plank's far end.
        private static void CreateDock(GameObject shipPrefab, Material plankMaterial)
        {
            GameObject dock = new("Dock");
            float plankStart = 6f;
            CreateBlock("Plank", new Vector3(0f, -0.05f, plankStart + PlankLength / 2f), new Vector3(PlankWidth, 0.1f, PlankLength), plankMaterial, dock.transform);
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
            ship.transform.SetParent(dock.transform, true);
            ShipParts parts = ship.GetComponent<ShipParts>();
            Transform boarding = parts != null ? parts.BoardingPoint : null;
            Vector3 boardingLocal = boarding != null ? boarding.localPosition : Vector3.zero;
            ship.transform.SetPositionAndRotation(new Vector3(0f, 0f, plankStart + PlankLength) - boardingLocal, Quaternion.identity);
        }

        internal static Type FindType(string fullName)
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<Transport>())
            {
                if (type.FullName == fullName) return type;
            }
            return null;
        }

        internal static void SetPrivate(Object target, string name, int value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(name).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetPrivate(Object target, string name, bool value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(name).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static Material GetOrCreateMaterial(string path, Color color)
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

        internal static void EnsureFolder(string path)
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
