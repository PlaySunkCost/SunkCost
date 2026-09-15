using System;
using SunkCost.Diving;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SunkCost.Sites
{
    // DiveSite01.unity is generated-only: CreateOrUpdate discards and regenerates the
    // scene from scratch, so any hand-placed object in it is destroyed on next rebuild.
    public static class DiveSiteBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Prototype/DiveSite01.unity";
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/DiveSite01Settings.asset";
        public const string VolumeProfilePath = "Assets/_Project/Settings/Prototype/DiveSite01Volume.asset";
        public const string DeepLayerName = "DiveSiteDeep";
        private const string MaterialPath = "Assets/_Project/Art/Prototype/Materials";
        public const string GlassMaterialPath = MaterialPath + "/DiveSiteGlass.mat";

        private const float PlatformSize = 20f;
        private const float GuideCableRadius = 0.06f;
        private const float SeafloorThickness = 0.5f;
        private const float SeafloorWallHeight = 6f;
        private const float WreckLength = 16f;
        private const float WreckWidth = 6f;
        private const float WreckHeight = 5f;
        private const float WreckWallThickness = 0.4f;

        // The shaft hole a car descends through must never be smaller than the car; derive
        // it from the car's own diameter rather than letting the two drift independently.
        // Kept tiny (well under the 0.6m CharacterController diameter that would drop a
        // player through) because the hole is now built as an actual circle matching the
        // car's own circular footprint (see CreatePlatformRing) — there is no square-vs-round
        // mismatch left to bridge, just a uniform walk-across lip on every bearing.
        public const float ElevatorClearanceMeters = 0.1f;
        private const int PlatformHoleSegmentCount = 32;

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
            if (settings.SeafloorSizeMeters <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.SeafloorSizeMeters must be positive.");
            if (settings.HeadlampIntensity <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.HeadlampIntensity must be positive.");
            if (settings.HeadlampRange <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.HeadlampRange must be positive.");
            if (settings.HeadlampSpotAngle <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.HeadlampSpotAngle must be positive.");
            if (settings.FogDensity <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.FogDensity must be positive.");
            if (settings.SurfaceLightIntensity <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.SurfaceLightIntensity must be positive.");
            if (settings.CarDiameterMeters <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.CarDiameterMeters must be positive.");
            if (settings.CarInteriorHeightMeters <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.CarInteriorHeightMeters must be positive.");
            if (settings.ElevatorTravelSecondsOneWay <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.ElevatorTravelSecondsOneWay must be positive.");
            if (settings.DoorSealSeconds <= 0f)
                throw new InvalidOperationException("DiveSiteSettings.DoorSealSeconds must be positive.");

            Material floorMaterial = LoadMaterial("HQFloor.mat");
            Material wallMaterial = LoadMaterial("HQWall.mat");
            Material accentMaterial = LoadMaterial("BallOrange.mat");
            Material glassMaterial = GetOrCreateGlassMaterial();

            int deepLayer = GetOrCreateLayer(DeepLayerName);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DiveSite01";

            ConfigureAmbience(settings);
            (Transform anchorTop, Vector3 playerSpawnPosition) = CreateSurfacePlatform(floorMaterial, accentMaterial, settings);
            CreateSurfaceLight(deepLayer, settings);
            Transform anchorBottom = CreateSeafloor(floorMaterial, wallMaterial, shaftDepth, deepLayer, settings);
            CreateGuideShaft(anchorTop, anchorBottom, accentMaterial);
            ElevatorController elevatorController = CreateElevator(anchorTop.position, anchorBottom.position, playerSpawnPosition, floorMaterial, wallMaterial, glassMaterial, accentMaterial, settings);
            CreateShaftGate(anchorBottom, elevatorController, wallMaterial, settings);
            CreateUnderwaterVolume(settings);
            CreateHeadlampActivator();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Unity could not save " + ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DiveSiteValidator.ValidateOrThrow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("Dive Site 01 created and validated at " + ScenePath);
        }

        private static void ConfigureAmbience(DiveSiteSettings settings)
        {
            // Near-black cold blue-green ambient. The surface stays sunlit on its own layer
            // (see CreateSurfaceLight); ambient alone should barely read at the seafloor.
            // ExponentialSquared fog tuned for ~15-20m visibility down there.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = settings.AmbientColor;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = settings.FogColor;
            RenderSettings.fogDensity = settings.FogDensity;
        }

        private static (Transform anchorTop, Vector3 playerSpawnPosition) CreateSurfacePlatform(Material floor, Material accent, DiveSiteSettings settings)
        {
            GameObject root = new("Surface Platform");
            float shaftRadius = settings.CarDiameterMeters / 2f + ElevatorClearanceMeters;
            CreatePlatformRing(root.transform, floor, shaftRadius);
            Transform[] spawnPoints = CreateSpawnPoints(root.transform);

            GameObject anchor = new("ElevatorAnchor_Top");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = Vector3.zero;

            return (anchor.transform, spawnPoints[0].position);
        }

        // The car is round; a square hole (the original "picture frame" of 4 blocks) leaves
        // up to ~2m of open shaft at the diagonal corners even when its flat edges are sized
        // to just clear the car. Building the hole as an actual circle — a fan of radial
        // wedges reaching out to the platform's square outer edge — removes that mismatch
        // entirely: the gap between the car and solid floor is the same tiny ElevatorClearanceMeters
        // at every bearing, diagonals included.
        //
        // Each wedge is a true trapezoid (narrow at the hole, wide at the platform's square
        // edge) built as its own flat mesh rather than a rotated box. Two earlier attempts at
        // approximating this with rectangles both shipped real bugs: sizing a box for its own
        // mid-radius left neighbours short of each other near the platform edge (a fall-through
        // gap); sizing it for the wide end to fix that made adjacent boxes overlap heavily near
        // the hole, and two rotated boxes meeting at a seam at a shallow angle can present a
        // vertical edge that blocks a CharacterController walking along that seam ("Sides"
        // collision) even though the floor is solid on both sides of it. An exact trapezoid has
        // no side faces at all near the walkable surface — only a flat top matching its
        // neighbours edge-to-edge — so neither failure mode exists.
        private static void CreatePlatformRing(Transform parent, Material floor, float shaftRadius)
        {
            float half = PlatformSize / 2f;

            for (int i = 0; i < PlatformHoleSegmentCount; i++)
            {
                float angleStartDeg = i * 360f / PlatformHoleSegmentCount;
                float angleEndDeg = (i + 1) * 360f / PlatformHoleSegmentCount;
                Vector3 dirStart = AngleToXZDirection(angleStartDeg);
                Vector3 dirEnd = AngleToXZDirection(angleEndDeg);

                // Distance from centre to the platform's own square outer edge along each ray —
                // varies from `half` at a cardinal bearing to half*sqrt(2) at a diagonal one.
                float outerStart = half / Mathf.Max(Mathf.Abs(dirStart.x), Mathf.Abs(dirStart.z));
                float outerEnd = half / Mathf.Max(Mathf.Abs(dirEnd.x), Mathf.Abs(dirEnd.z));

                Vector3 innerA = dirStart * shaftRadius;
                Vector3 innerB = dirEnd * shaftRadius;
                Vector3 outerA = dirStart * outerStart;
                Vector3 outerB = dirEnd * outerEnd;

                GameObject segment = new("Platform Segment " + (i + 1), typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                segment.transform.SetParent(parent, false);

                Mesh mesh = new() { name = "PlatformSegment" + (i + 1) };
                mesh.vertices = new[] { innerA, innerB, outerB, outerA };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                segment.GetComponent<MeshFilter>().sharedMesh = mesh;
                segment.GetComponent<MeshRenderer>().sharedMaterial = floor;
                segment.GetComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        private static Vector3 AngleToXZDirection(float angleDeg)
        {
            float angleRad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
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

        // The networked player is spawned at runtime by CrewSpawner from
        // PrototypePlayer.prefab, using this scene's own Spawn Points group — this
        // scene must never place one by hand (see DiveSiteValidator's HQPlayerController
        // check). Its headlamp is disabled by default (see HQPrototypeBuilder); this is
        // the one place that turns it on.
        private static void CreateHeadlampActivator()
        {
            new GameObject("Dive Site Headlamp Activator", typeof(DiveSiteHeadlampActivator));
        }

        // The cabin itself now lives in Assets/_Project/Prefabs/World/ElevatorCabin.prefab
        // (ElevatorCabinBuilder, 15 September 2026 elevator rules) instead of being built
        // from primitives here. ElevatorAnchor_Top/_Bottom stay as position markers only;
        // their positions configure the travel endpoints on the instantiated controller.
        //
        // Known issue, deferred: the seafloor sits on DiveSiteDeep so the surface directional
        // light doesn't reach it (see CreateSeafloor/CreateSurfaceLight), but this car stays
        // on the Default layer for its whole 45m descent, so it will read as fully sunlit at
        // the bottom. Switching the car's layer partway down would also need to keep its own
        // collisions (rider trigger, floor, interior walls) working against the physics layer
        // collision matrix — real work, not done here. Revisit before this scene is used for
        // anything beyond a smoke test.
        private static ElevatorController CreateElevator(Vector3 topAnchorPosition, Vector3 bottomAnchorPosition, Vector3 playerSpawnPosition, Material floor, Material frame, Material glass, Material panelAccent, DiveSiteSettings settings)
        {
            GameObject prefab = ElevatorCabinBuilder.EnsurePrefab(floor, frame, glass, panelAccent, settings);

            // The doorway must face the spawn the dev player actually uses, not a fixed
            // bearing — otherwise moving the spawn silently strands the door on the wrong
            // side again. Bearing convention matches ElevatorCabinBuilder's: 0 = +X, 90 = +Z.
            // The prefab is always baked with its doorway at local bearing 0, so rotating the
            // instance by -bearing around Y points that baked doorway at the spawn direction.
            Vector3 toSpawn = playerSpawnPosition - topAnchorPosition;
            float doorwayBearingDeg = Mathf.Atan2(toSpawn.z, toSpawn.x) * Mathf.Rad2Deg;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(topAnchorPosition, Quaternion.Euler(0f, -doorwayBearingDeg, 0f));

            ElevatorController controller = instance.GetComponent<ElevatorController>();
            SerializedObject serialized = new(controller);
            serialized.FindProperty("topPosition").vector3Value = topAnchorPosition;
            serialized.FindProperty("bottomPosition").vector3Value = bottomAnchorPosition;
            serialized.FindProperty("travelSecondsOneWay").floatValue = settings.ElevatorTravelSecondsOneWay;
            serialized.FindProperty("doorSealSeconds").floatValue = settings.DoorSealSeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return controller;
        }

        // The blocker at the bottom landing (Idan/Dan, 15 September 2026 elevator rules:
        // "gated shut at the bottom when the cabin is away; a player cannot walk in"). Solid
        // whenever the cabin is not docked at the bottom; ShaftGate flips it on ElevatorState.
        // Sized to the same shaft radius as the platform hole at the top so it fully covers
        // the car's own footprint. A separate object from the cabin — it has to keep blocking
        // after the car has left, so it cannot live on the (moving) prefab instance.
        private static void CreateShaftGate(Transform anchorBottom, ElevatorController controller, Material wallMaterial, DiveSiteSettings settings)
        {
            float shaftRadius = settings.CarDiameterMeters / 2f + ElevatorClearanceMeters;
            float gateHeight = settings.CarInteriorHeightMeters;

            GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            gate.name = "Shaft Gate";
            gate.transform.position = anchorBottom.position + new Vector3(0f, gateHeight / 2f, 0f);
            gate.transform.localScale = new Vector3(shaftRadius * 2f, gateHeight / 2f, shaftRadius * 2f);
            gate.GetComponent<Renderer>().sharedMaterial = wallMaterial;
            // Same CapsuleCollider-clamp problem as Car Floor/Car Roof (see ElevatorCabinBuilder):
            // swap in a MeshCollider built from the same cylinder mesh so collision matches what
            // is visible instead of an inflated capsule.
            Object.DestroyImmediate(gate.GetComponent<Collider>());
            MeshCollider gateCollider = gate.AddComponent<MeshCollider>();
            gateCollider.sharedMesh = gate.GetComponent<MeshFilter>().sharedMesh;

            ShaftGate shaftGate = gate.AddComponent<ShaftGate>();
            SerializedObject serialized = new(shaftGate);
            serialized.FindProperty("controller").objectReferenceValue = controller;
            serialized.FindProperty("gateCollider").objectReferenceValue = gateCollider;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
            // A guide, not a pole: it runs through the middle of the car, where a rider
            // may stand; with a collider the car would descend without them.
            Object.DestroyImmediate(cable.GetComponent<Collider>());
        }

        private static Transform CreateSeafloor(Material floor, Material wall, float depth, int deepLayer, DiveSiteSettings settings)
        {
            GameObject root = new("Seafloor");
            float y = -depth;
            float seafloorSize = settings.SeafloorSizeMeters;
            float half = seafloorSize / 2f;

            CreateBlock("Seafloor Ground", new Vector3(0f, y - SeafloorThickness / 2f, 0f), new Vector3(seafloorSize, SeafloorThickness, seafloorSize), floor, root.transform);
            CreatePerimeterWalls(root.transform, wall, y, half, seafloorSize);
            CreateWreck(root.transform, wall, y, settings);
            CreateSeafloorLights(root.transform, y, settings);

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
        private static void CreatePerimeterWalls(Transform parent, Material wall, float floorY, float half, float seafloorSize)
        {
            float centerY = floorY + SeafloorWallHeight / 2f;
            float thickness = 0.5f;

            CreateBlock("Boundary_Temp_North", new Vector3(0f, centerY, half), new Vector3(seafloorSize + thickness, SeafloorWallHeight, thickness), wall, parent);
            CreateBlock("Boundary_Temp_South", new Vector3(0f, centerY, -half), new Vector3(seafloorSize + thickness, SeafloorWallHeight, thickness), wall, parent);
            CreateBlock("Boundary_Temp_East", new Vector3(half, centerY, 0f), new Vector3(thickness, SeafloorWallHeight, seafloorSize + thickness), wall, parent);
            CreateBlock("Boundary_Temp_West", new Vector3(-half, centerY, 0f), new Vector3(thickness, SeafloorWallHeight, seafloorSize + thickness), wall, parent);
        }

        private static void CreateWreck(Transform parent, Material wall, float floorY, DiveSiteSettings settings)
        {
            GameObject root = new("Wreck");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, floorY, 0f) + settings.WreckOffset;
            Vector3 center = root.transform.position;

            CreateBlock("Wreck Wall North", center + new Vector3(0f, WreckHeight / 2f, WreckWidth / 2f), new Vector3(WreckLength, WreckHeight, WreckWallThickness), wall, root.transform);
            CreateBlock("Wreck Wall South", center + new Vector3(0f, WreckHeight / 2f, -WreckWidth / 2f), new Vector3(WreckLength, WreckHeight, WreckWallThickness), wall, root.transform);
            CreateBlock("Wreck Roof", center + new Vector3(0f, WreckHeight + WreckWallThickness / 2f, 0f), new Vector3(WreckLength, WreckWallThickness, WreckWidth), wall, root.transform);
            // Both ends of the hull are left open: that is the way in and the way out.
        }

        private static void CreateSurfaceLight(int deepLayer, DiveSiteSettings settings)
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
            light.intensity = settings.SurfaceLightIntensity;
            light.color = new Color(0.95f, 0.97f, 1f);
            light.shadows = LightShadows.Soft;
            light.cullingMask &= ~(1 << deepLayer);
        }

        private static void CreateSeafloorLights(Transform parent, float floorY, DiveSiteSettings settings)
        {
            CreateDimLight(parent, new Vector3(settings.WreckOffset.x, floorY + 3f, settings.WreckOffset.z), 1.8f, 14f);
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
        public const string UnderwaterVolumeName = "Underwater Volume";

        // Local, not global: the site is loaded on the host's machine while the host
        // stands on the deck at sea (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section
        // 6.3), and a global volume would grade the deck underwater. The box covers
        // the shaft and the seafloor from just under the surface platform down.
        public static void ShapeUnderwaterVolume(GameObject volumeObject, DiveSiteSettings settings)
        {
            Volume volume = volumeObject.GetComponent<Volume>();
            if (volume != null) { volume.isGlobal = false; volume.blendDistance = 4f; }
            BoxCollider box = volumeObject.GetComponent<BoxCollider>();
            if (box == null) box = volumeObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            float depth = settings.ShaftDepthMeters + 20f;
            float width = settings.SeafloorSizeMeters * 1.5f;
            volumeObject.transform.position = Vector3.zero;
            box.center = new Vector3(0f, -1f - depth / 2f, 0f);
            box.size = new Vector3(width, depth, width);
        }

        private static void CreateUnderwaterVolume(DiveSiteSettings settings)
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

            GameObject volumeObject = new(UnderwaterVolumeName);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.profile = profile;
            ShapeUnderwaterVolume(volumeObject, settings);
        }

        public static Material GetOrCreateGlassMaterial()
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

        // Internal (not private): HQPrototypeBuilder reads HeadlampIntensity/Range/SpotAngle
        // from here too, so the headlamp it builds onto PrototypePlayer.prefab stays in sync
        // with the one value DiveSiteValidator checks against — one source of truth, not two
        // numbers that can drift apart.
        internal static DiveSiteSettings GetOrCreateSettings()
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
