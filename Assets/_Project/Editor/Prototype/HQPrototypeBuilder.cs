using System;
using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Transporting;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.Sites;
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
        // Centred: every held item sits in both hands in front of the body
        // (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 6).
        public static readonly Vector3 HoldPointLocalPosition = PlayerMovementHandsSetup.CenteredHoldPointLocalPosition;
        public const string SteamTransportPrefabPath = "Assets/_Project/Prefabs/Net/SteamTransport.prefab";
        public const string MaterialPath = "Assets/_Project/Art/Prototype/Materials";

        public const float PlankLength = 6f;   // historical: the old base-owned plank; the ship's gangway is this long now
        public const float PlankWidth = 1.6f;
        public const float PierLength = 3f;
        public const float PierOverlap = 1f;   // how far the gangway tip reaches onto the pier
        public const float DoorwayWidth = 2.4f;
        public const string ShopRoomName = "Shop Room"; // the Gear & Supplies booth (HQPlatformBuilder), where the hooks look

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
            // The look kit first (textures, materials, prop prefabs are assets the
            // scene references), then the platform (docs/DESIGN.md §2, 18 September 2026).
            SunkCost.Editor.Look.ProceduralTextures.GenerateAll();
            SunkCost.Editor.Look.LookSetup.PatchPlayerCamera();
            SunkCost.Editor.Look.LookSetup.EnsurePipeline();
            SunkCost.Editor.Look.LookSetup.EnsureSigns();
            Material tankMaterial = GetOrCreateMaterial(DiveLootSetup.AirTankFullMaterialPath, new Color(0.95f, 0.75f, 0.12f)); // the tank prefab's own look
            Material lampMaterial = GetOrCreateMaterial(MaterialPath + "/ShopLamp.mat", new Color(0.95f, 0.95f, 0.8f));
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "HQPrototype";
            // NewScene unloads assets nothing references; take the references again.
            ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            shipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipStubBuilder.PrefabPath);
            tankMaterial = GetOrCreateMaterial(DiveLootSetup.AirTankFullMaterialPath, new Color(0.95f, 0.75f, 0.12f));
            lampMaterial = GetOrCreateMaterial(MaterialPath + "/ShopLamp.mat", new Color(0.95f, 0.95f, 0.8f));
            SunkCost.Editor.Look.HQPlatformBuilder.Build(scene, ballPrefab, shipPrefab, tankMaterial, lampMaterial);
            CreateSpawnPoints();
            CreateLootFixture(ballPrefab);
            WorldLookSetup.WriteIntoOpenScene(scene);
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
            EnsureFolder("Assets/_Project/Settings/Prototype");
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
                root.AddComponent<SunkCost.World.ShipControls>();
                root.AddComponent<SunkCost.World.ShipDepartureRider>();

                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = material;

                GameObject pivot = new("ViewPivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                GameObject cameraObject = new("PlayerCamera", typeof(Camera), typeof(AudioListener));
                cameraObject.transform.SetParent(pivot.transform, false);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.fieldOfView = 75f;
                camera.nearClipPlane = PlayerCameraSettings.Resolve(null).NearClip;
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

                Light headlamp = CreateHeadlamp(cameraObject.transform);

                SerializedObject serialized = new(controller);
                serialized.FindProperty("playerCamera").objectReferenceValue = camera;
                serialized.FindProperty("holdPoint").objectReferenceValue = hold.transform;
                serialized.FindProperty("twoHandHoldPoint").objectReferenceValue = twoHand.transform;
                serialized.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
                serialized.FindProperty("headlamp").objectReferenceValue = headlamp;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // Stance, hands, arms, settings, layers: the same patch the setup applies.
                PlayerMovementHandsSetup.PatchPlayer(root, PlayerMovementHandsSetup.EnsureSettings(null),
                    GetOrCreateMaterial(PlayerMovementHandsSetup.GloveMaterialPath, new Color(0.85f, 0.72f, 0.25f)), new List<string>());
                // Explicit near clip and the wall clearance: regeneration must not
                // bring back Unity's 0.3 m default (see-through corners).
                PlayerCameraClearanceSetup.PatchPlayer(root, PlayerCameraClearanceSetup.EnsureSettings(null), new List<string>());
                if (root.GetComponent<PlayerSubmersion>() == null) root.AddComponent<PlayerSubmersion>(); // the underwater indicator (shaft tube card)
                return PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Lives on the shared player prefab (not a dive-site-only object) so both HQ and
        // every dive site follow the same prefab; disabled by default so HQ stays
        // behaviourally unchanged. A dive site turns it on via
        // HQPlayerController.SetHeadlampEnabled (see DiveSiteHeadlampActivator). Tuning
        // values come from DiveSiteSettings — the only place they're set — so this and
        // DiveSiteValidator's check against that same asset can never drift apart.
        private static Light CreateHeadlamp(Transform cameraTransform)
        {
            DiveSiteSettings settings = DiveSiteBuilder.GetOrCreateSettings();

            GameObject go = new("Headlamp", typeof(Light));
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            Light light = go.GetComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = settings.HeadlampIntensity;
            light.range = settings.HeadlampRange;
            light.spotAngle = settings.HeadlampSpotAngle;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
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
            // On the crew's mark (HQPlatformBuilder.CrewMark), facing the booths.
            Vector3 c = SunkCost.Editor.Look.HQPlatformBuilder.CrewMark;
            Vector3[] positions = { c + new Vector3(-3f, 0f, -2f), c + new Vector3(3f, 0f, -2f), c + new Vector3(-3f, 0f, 2f), c + new Vector3(3f, 0f, 2f) };
            Transform[] result = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject marker = new("Spawn " + (i + 1));
                marker.transform.SetParent(root.transform);
                marker.transform.SetPositionAndRotation(positions[i], Quaternion.Euler(0f, -20f, 0f));
                result[i] = marker.transform;
            }
            return result;
        }

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
