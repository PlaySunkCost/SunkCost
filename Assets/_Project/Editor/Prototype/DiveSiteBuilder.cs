using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SunkCost.Sites
{
    public static class DiveSiteBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Prototype/DiveSite01.unity";
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/DiveSite01Settings.asset";
        public const string VolumeProfilePath = "Assets/_Project/Settings/Prototype/DiveSite01Volume.asset";
        public const string DeepLayerName = "DiveSiteDeep";
        private const string MaterialPath = "Assets/_Project/Art/Prototype/Materials";
        private const string GlassMaterialPath = MaterialPath + "/DiveSiteGlass.mat";

        private const float PlatformSize = 20f;
        private const float PlatformThickness = 0.5f;
        private const float ShaftHalfWidth = 3f;
        private const float GuideCableRadius = 0.06f;
        public const float SeafloorSize = 150f;
        private const float SeafloorThickness = 0.5f;
        private const float SeafloorWallHeight = 6f;
        private const float WreckLength = 16f;
        private const float WreckWidth = 6f;
        private const float WreckHeight = 5f;
        private const float WreckWallThickness = 0.4f;
        public static readonly Vector3 WreckOffset = new(35f, 0f, 0f);

        private const float CarDiameter = 3f;
        private const float CarHeight = 3f;
        private const float CarFloorThickness = 0.1f;
        private const float CarFrameRadius = 0.06f;

        public const float HeadlampIntensity = 15f;
        public const float HeadlampRange = 25f;
        public const float HeadlampSpotAngle = 35f;

        [MenuItem("Sunk Cost/Prototype/Create or Update Dive Site 01")]
        public static void CreateOrUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the dive site scene.");

            EnsureFolder("Assets/_Project/Scenes/Prototype");
            EnsureFolder("Assets/_Project/Settings/Prototype");

            DiveSiteSettings settings = GetOrCreateSettings();
            float shaftDepth = settings.ShaftDepthMeters;
            if (shaftDepth <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.ShaftDepthMeters must be positive.");

            Material floorMaterial = LoadMaterial("HQFloor.mat");
            Material wallMaterial = LoadMaterial("HQWall.mat");
            Material accentMaterial = LoadMaterial("BallOrange.mat");
            Material playerMaterial = LoadMaterial("PlayerBase.mat");
            Material glassMaterial = GetOrCreateGlassMaterial();

            int deepLayer = GetOrCreateLayer(DeepLayerName);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DiveSite01";

            ConfigureAmbience();
            Transform anchorTop = CreateSurfacePlatform(floorMaterial, accentMaterial, playerMaterial, wallMaterial, glassMaterial);
            CreateSurfaceLight(deepLayer);
            Transform anchorBottom = CreateSeafloor(floorMaterial, wallMaterial, shaftDepth, deepLayer);
            CreateGuideShaft(anchorTop, anchorBottom, accentMaterial);
            CreateUnderwaterVolume();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save " + ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DiveSiteValidator.ValidateOrThrow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("Dive Site 01 created and validated at " + ScenePath);
        }

        private static void ConfigureAmbience()
        {
            // Near-black cold blue-green ambient. The surface stays sunlit on its own layer
            // (see CreateSurfaceLight); ambient alone should barely read at the seafloor.
            // ExponentialSquared fog tuned for ~15-20m visibility down there.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.015f, 0.03f, 0.028f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.02f, 0.05f, 0.045f);
            RenderSettings.fogDensity = 0.09f;
        }

        private static Transform CreateSurfacePlatform(Material floor, Material accent, Material playerMaterial, Material frame, Material glass)
        {
            GameObject root = new("Surface Platform");
            CreatePlatformRing(root.transform, floor);
            CreateLever(root.transform, accent);
            Transform[] spawnPoints = CreateSpawnPoints(root.transform);
            CreateDevPlayer(spawnPoints[0], playerMaterial);

            GameObject anchor = new("ElevatorAnchor_Top");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = Vector3.zero;

            // Placeholder car only — no movement, no interaction, no scripts. The elevator
            // task replaces this with the real thing; it exists so we can judge the shape.
            CreateElevatorCarPlaceholder(anchor.transform, floor, frame, glass);
            return anchor.transform;
        }

        private static void CreatePlatformRing(Transform parent, Material floor)
        {
            float half = PlatformSize / 2f;
            float hole = ShaftHalfWidth;
            float centerY = -PlatformThickness / 2f;
            float farEdge = (hole + half) / 2f;
            float farSpan = half - hole;

            CreateBlock("Platform North", new Vector3(0f, centerY, farEdge), new Vector3(PlatformSize, PlatformThickness, farSpan), floor, parent);
            CreateBlock("Platform South", new Vector3(0f, centerY, -farEdge), new Vector3(PlatformSize, PlatformThickness, farSpan), floor, parent);
            CreateBlock("Platform East", new Vector3(farEdge, centerY, 0f), new Vector3(farSpan, PlatformThickness, hole * 2f), floor, parent);
            CreateBlock("Platform West", new Vector3(-farEdge, centerY, 0f), new Vector3(farSpan, PlatformThickness, hole * 2f), floor, parent);
        }

        private static void CreateLever(Transform parent, Material accent)
        {
            Vector3 basePos = new(0f, 0f, 9.5f);
            CreateBlock("Lever Base", basePos + new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.5f), accent, parent);

            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.name = "Lever Arm";
            arm.transform.SetParent(parent);
            arm.transform.SetPositionAndRotation(basePos + new Vector3(0f, 1f, 0f), Quaternion.Euler(0f, 0f, 35f));
            arm.transform.localScale = new Vector3(0.12f, 0.7f, 0.12f);
            arm.GetComponent<Renderer>().sharedMaterial = accent;
        }

        private static Transform[] CreateSpawnPoints(Transform parent)
        {
            GameObject root = new("Spawn Points");
            root.transform.SetParent(parent, false);
            Vector3[] positions = { new(-8f, 0f, -8f), new(8f, 0f, -8f), new(-8f, 0f, 8f), new(8f, 0f, 8f) };
            Transform[] result = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject marker = new("Spawn " + (i + 1));
                marker.transform.SetParent(root.transform);
                marker.transform.position = positions[i];
                marker.transform.LookAt(new Vector3(0f, positions[i].y, 0f));
                result[i] = marker.transform;
            }
            return result;
        }

        // Non-networked walk-around harness — see DiveSiteDevPlayer. Delete this method's
        // output (the "DevHarnessPlayer_DeleteWhenNetworkedPlayerLands" GameObject) once a
        // real networked player is spawned into dive sites instead.
        private static void CreateDevPlayer(Transform spawnPoint, Material bodyMaterial)
        {
            GameObject root = new("DevHarnessPlayer_DeleteWhenNetworkedPlayerLands");
            root.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.25f;
            controller.slopeLimit = 45f;
            controller.skinWidth = 0.03f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

            GameObject pivot = new("ViewPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            GameObject cameraObject = new("DevCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(pivot.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 75f;
            // URP cameras don't render post-processing by default; without this the
            // Underwater Volume's Exposure/Tonemapping/ColorAdjustments never apply.
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            CreateHeadlamp(cameraObject.transform);

            DiveSiteDevPlayer devPlayer = root.AddComponent<DiveSiteDevPlayer>();
            SerializedObject serialized = new(devPlayer);
            serialized.FindProperty("playerCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Exists so the darkness can be judged during greybox testing. No battery, no
        // per-player colour, no toggle — just a headlamp that follows where you look.
        private static void CreateHeadlamp(Transform cameraTransform)
        {
            GameObject go = new("Headlamp", typeof(Light));
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            Light light = go.GetComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = HeadlampIntensity; // tuned empirically against real game-camera renders (see below)
            light.range = HeadlampRange;
            light.spotAngle = HeadlampSpotAngle;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.None;
        }

        // Placeholder elevator car: glass shell, floor and corner frame only. Geometry
        // only — no movement, no interaction, no scripts. Replaced by the elevator task.
        private static void CreateElevatorCarPlaceholder(Transform anchorTop, Material floor, Material frame, Material glass)
        {
            GameObject root = new("ElevatorCar_Placeholder");
            root.transform.SetParent(anchorTop, false);
            root.transform.localPosition = Vector3.zero;

            GameObject carFloor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            carFloor.name = "Car Floor";
            carFloor.transform.SetParent(root.transform, false);
            carFloor.transform.localPosition = new Vector3(0f, CarFloorThickness / 2f, 0f);
            carFloor.transform.localScale = new Vector3(CarDiameter, CarFloorThickness / 2f, CarDiameter);
            carFloor.GetComponent<Renderer>().sharedMaterial = floor;

            GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shell.name = "Glass Shell";
            shell.transform.SetParent(root.transform, false);
            shell.transform.localPosition = new Vector3(0f, CarHeight / 2f, 0f);
            shell.transform.localScale = new Vector3(CarDiameter, CarHeight / 2f, CarDiameter);
            shell.GetComponent<Renderer>().sharedMaterial = glass;

            const int postCount = 4;
            float postRadius = CarDiameter / 2f - CarFrameRadius;
            for (int i = 0; i < postCount; i++)
            {
                float angle = i * Mathf.PI * 2f / postCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * postRadius;
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Frame Post " + (i + 1);
                post.transform.SetParent(root.transform, false);
                post.transform.localPosition = offset + new Vector3(0f, CarHeight / 2f, 0f);
                post.transform.localScale = new Vector3(CarFrameRadius * 2f, CarHeight / 2f, CarFrameRadius * 2f);
                post.GetComponent<Renderer>().sharedMaterial = frame;
            }
        }

        // The elevator descends through open water: no enclosing tube, just a guide
        // cable marking the line of descent between the two anchors.
        private static void CreateGuideShaft(Transform anchorTop, Transform anchorBottom, Material cableMaterial)
        {
            GameObject root = new("Shaft");
            Vector3 top = anchorTop.position;
            Vector3 bottom = anchorBottom.position;
            Vector3 mid = (top + bottom) / 2f;
            float length = Vector3.Distance(top, bottom);

            GameObject cable = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cable.name = "Guide Cable";
            cable.transform.SetParent(root.transform, false);
            cable.transform.position = mid;
            cable.transform.rotation = Quaternion.FromToRotation(Vector3.up, (bottom - top).normalized);
            cable.transform.localScale = new Vector3(GuideCableRadius * 2f, length / 2f, GuideCableRadius * 2f);
            cable.GetComponent<Renderer>().sharedMaterial = cableMaterial;
        }

        private static Transform CreateSeafloor(Material floor, Material wall, float depth, int deepLayer)
        {
            GameObject root = new("Seafloor");
            float y = -depth;
            float half = SeafloorSize / 2f;

            CreateBlock("Seafloor Ground", new Vector3(0f, y - SeafloorThickness / 2f, 0f), new Vector3(SeafloorSize, SeafloorThickness, SeafloorSize), floor, root.transform);
            CreatePerimeterWalls(root.transform, wall, y, half);
            CreateWreck(root.transform, wall, y);
            CreateSeafloorLights(root.transform, y);

            GameObject anchor = new("ElevatorAnchor_Bottom");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = new Vector3(0f, y, 0f);

            // Keep the sun off everything down here (see CreateSurfaceLight) — a directional
            // light ignores distance, so without this the seafloor reads fully sunlit 45m down.
            SetLayerRecursively(root, deepLayer);
            return anchor.transform;
        }

        // Placeholders for a natural boundary (vegetation, wreckage, a drop-off) — not the
        // shipping design. DESIGN.md §6: the built area should simply stop containing
        // anything, not hit a wall. Named _Temp so nobody mistakes these for final art.
        private static void CreatePerimeterWalls(Transform parent, Material wall, float floorY, float half)
        {
            float centerY = floorY + SeafloorWallHeight / 2f;
            float thickness = 0.5f;

            CreateBlock("Boundary_Temp_North", new Vector3(0f, centerY, half), new Vector3(SeafloorSize + thickness, SeafloorWallHeight, thickness), wall, parent);
            CreateBlock("Boundary_Temp_South", new Vector3(0f, centerY, -half), new Vector3(SeafloorSize + thickness, SeafloorWallHeight, thickness), wall, parent);
            CreateBlock("Boundary_Temp_East", new Vector3(half, centerY, 0f), new Vector3(thickness, SeafloorWallHeight, SeafloorSize + thickness), wall, parent);
            CreateBlock("Boundary_Temp_West", new Vector3(-half, centerY, 0f), new Vector3(thickness, SeafloorWallHeight, SeafloorSize + thickness), wall, parent);
        }

        private static void CreateWreck(Transform parent, Material wall, float floorY)
        {
            GameObject root = new("Wreck");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, floorY, 0f) + WreckOffset;
            Vector3 center = root.transform.position;

            CreateBlock("Wreck Wall North", center + new Vector3(0f, WreckHeight / 2f, WreckWidth / 2f), new Vector3(WreckLength, WreckHeight, WreckWallThickness), wall, root.transform);
            CreateBlock("Wreck Wall South", center + new Vector3(0f, WreckHeight / 2f, -WreckWidth / 2f), new Vector3(WreckLength, WreckHeight, WreckWallThickness), wall, root.transform);
            CreateBlock("Wreck Roof", center + new Vector3(0f, WreckHeight + WreckWallThickness / 2f, 0f), new Vector3(WreckLength, WreckWallThickness, WreckWidth), wall, root.transform);
            // Both ends of the hull are left open: that is the way in and the way out.
        }

        private static void CreateSurfaceLight(int deepLayer)
        {
            GameObject go = new("Surface Light", typeof(Light));
            // Steep angle so light spills straight down the open shaft column and is
            // visible as a glow looking up from below — but the culling mask below keeps
            // it from ever actually illuminating the seafloor 45m down (directional lights
            // ignore distance, so without this the depths would read fully sunlit).
            go.transform.SetPositionAndRotation(new Vector3(0f, 10f, -6f), Quaternion.Euler(78f, 35f, 0f));
            Light light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            // This URP project does not use physical light units — despite the numeric
            // scale suggesting lux, these are plain relative intensities. Verified against
            // actual game-camera renders (Scene View alone is not a reliable judge of
            // exposure): 20000 here blew every sunlit surface to solid white.
            light.intensity = 2f;
            light.color = new Color(0.95f, 0.97f, 1f);
            light.shadows = LightShadows.Soft;
            light.cullingMask &= ~(1 << deepLayer);
        }

        private static void CreateSeafloorLights(Transform parent, float floorY)
        {
            CreateDimLight(parent, new Vector3(WreckOffset.x, floorY + 3f, WreckOffset.z), 1.8f, 14f);
            CreateDimLight(parent, new Vector3(-15f, floorY + 3f, 15f), 1f, 10f);
            CreateDimLight(parent, new Vector3(-15f, floorY + 3f, -15f), 1f, 10f);
        }

        private static void CreateDimLight(Transform parent, Vector3 position, float intensity, float range)
        {
            GameObject go = new("Seafloor Light");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(0.55f, 0.75f, 0.95f);
            light.shadows = LightShadows.None;
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

        // Optional: nudges the scene's global look toward teal with slightly lifted
        // blacks, to help sell "underwater" beyond raw fog/ambient. Data-only — no script.
        private static void CreateUnderwaterVolume()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (!profile.TryGet(out ColorAdjustments colorAdjustments))
                colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.active = true;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = new Color(0.78f, 0.97f, 0.95f);
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = -12f;

            if (!profile.TryGet(out LiftGammaGain liftGammaGain))
                liftGammaGain = profile.Add<LiftGammaGain>(true);
            liftGammaGain.active = true;
            liftGammaGain.lift.overrideState = true;
            liftGammaGain.lift.value = new Vector4(0.95f, 1.02f, 1.03f, 0.04f);

            // This URP version has no Exposure volume override, so there is no automatic
            // exposure compensation for physical light units — light intensities below are
            // chosen empirically (via actual game-camera screenshots) to read correctly
            // without one. Tonemapping still helps roll off the brightest highlights.
            if (!profile.TryGet(out Tonemapping tonemapping))
                tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            EditorUtility.SetDirty(profile);

            GameObject volumeObject = new("Underwater Volume");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;
        }

        private static Material GetOrCreateGlassMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f); // Alpha
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.65f, 0.88f, 0.92f, 0.22f));
            AssetDatabase.CreateAsset(material, GlassMaterialPath);
            return material;
        }

        private static int GetOrCreateLayer(string layerName)
        {
            SerializedObject tagManager = new(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                    return i;
            }
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }
            throw new InvalidOperationException("No free layer slot available for " + layerName);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static DiveSiteSettings GetOrCreateSettings()
        {
            DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<DiveSiteSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                EditorUtility.SetDirty(settings);
            }
            return settings;
        }

        private static Material LoadMaterial(string fileName)
        {
            string path = MaterialPath + "/" + fileName;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                throw new InvalidOperationException("Expected existing material not found: " + path);
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
