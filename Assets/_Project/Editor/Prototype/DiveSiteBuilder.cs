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
        private const string GlassMaterialPath = MaterialPath + "/DiveSiteGlass.mat";

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
        private const float CarFloorThickness = 0.1f;
        private const float CarFrameRadius = 0.06f;
        private const float CarWallInset = 0.15f;
        private const float CarDoorwayWidthMeters = 2f;
        private const float PostDoorwayOffsetDeg = 45f; // posts sit this far off the doorway centre, evenly spaced every 90 degrees
        private const float PanelWidthMeters = 0.6f;
        private const float PanelHeightMeters = 0.4f;
        private const float PanelThicknessMeters = 0.08f;
        private const float PanelChestHeightMeters = 1.3f;
        private const float PanelDoorwayOffsetDeg = 75f; // 60-90 degrees off the doorway centre: visible on entry, clear of a post at +45
        private const float DoorThicknessMeters = 0.1f;
        private const int DoorLeafPanelCount = 3; // small flat panels per leaf, enough to read as curved

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
            Material playerMaterial = LoadMaterial("PlayerBase.mat");
            Material glassMaterial = GetOrCreateGlassMaterial();

            int deepLayer = GetOrCreateLayer(DeepLayerName);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DiveSite01";

            ConfigureAmbience(settings);
            (Transform anchorTop, Vector3 playerSpawnPosition) = CreateSurfacePlatform(floorMaterial, accentMaterial, playerMaterial, settings);
            CreateSurfaceLight(deepLayer, settings);
            Transform anchorBottom = CreateSeafloor(floorMaterial, wallMaterial, shaftDepth, deepLayer, settings);
            CreateGuideShaft(anchorTop, anchorBottom, accentMaterial);
            CreateElevator(anchorTop.position, anchorBottom.position, playerSpawnPosition, floorMaterial, wallMaterial, glassMaterial, accentMaterial, settings);
            CreateUnderwaterVolume();

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

        private static (Transform anchorTop, Vector3 playerSpawnPosition) CreateSurfacePlatform(Material floor, Material accent, Material playerMaterial, DiveSiteSettings settings)
        {
            GameObject root = new("Surface Platform");
            float shaftRadius = settings.CarDiameterMeters / 2f + ElevatorClearanceMeters;
            CreatePlatformRing(root.transform, floor, shaftRadius);
            Transform[] spawnPoints = CreateSpawnPoints(root.transform);
            CreateDevPlayer(spawnPoints[0], playerMaterial, settings);

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

        // Non-networked walk-around harness — see DiveSiteDevPlayer. Delete this method's
        // output (the "DevHarnessPlayer_DeleteWhenNetworkedPlayerLands" GameObject) once a
        // real networked player is spawned into dive sites instead.
        private static void CreateDevPlayer(Transform spawnPoint, Material bodyMaterial, DiveSiteSettings settings)
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
            CreateHeadlamp(cameraObject.transform, settings);

            DiveSiteDevPlayer devPlayer = root.AddComponent<DiveSiteDevPlayer>();
            SerializedObject serialized = new(devPlayer);
            serialized.FindProperty("playerCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ElevatorInteractor interactor = root.AddComponent<ElevatorInteractor>();
            SerializedObject interactorSerialized = new(interactor);
            interactorSerialized.FindProperty("interactCamera").objectReferenceValue = camera;
            interactorSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Exists so the darkness can be judged during greybox testing. No battery, no
        // per-player colour, no toggle — just a headlamp that follows where you look.
        private static void CreateHeadlamp(Transform cameraTransform, DiveSiteSettings settings)
        {
            GameObject go = new("Headlamp", typeof(Light));
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            Light light = go.GetComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = settings.HeadlampIntensity; // tuned empirically against real game-camera renders (see below)
            light.range = settings.HeadlampRange;
            light.spotAngle = settings.HeadlampSpotAngle;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.None;
        }

        // Real elevator car: its own scene root (not a child of the anchors, so it can move
        // independently), sized for the whole crew plus cargo. ElevatorAnchor_Top/_Bottom
        // stay as position markers only; their positions configure the travel endpoints.
        //
        // Known issue, deferred: the seafloor sits on DiveSiteDeep so the surface directional
        // light doesn't reach it (see CreateSeafloor/CreateSurfaceLight), but this car stays
        // on the Default layer for its whole 45m descent, so it will read as fully sunlit at
        // the bottom. Switching the car's layer partway down would also need to keep its own
        // collisions (rider trigger, floor, interior walls) working against the physics layer
        // collision matrix — real work, not done here. Revisit before this scene is used for
        // anything beyond a smoke test.
        private static void CreateElevator(Vector3 topAnchorPosition, Vector3 bottomAnchorPosition, Vector3 playerSpawnPosition, Material floor, Material frame, Material glass, Material panelAccent, DiveSiteSettings settings)
        {
            float carRadius = settings.CarDiameterMeters / 2f;
            float interiorHeight = settings.CarInteriorHeightMeters;
            float interiorRadius = carRadius - CarWallInset;

            // The doorway must face the spawn the dev player actually uses, not a fixed
            // bearing — otherwise moving the spawn silently strands the door on the wrong
            // side again. Bearing convention matches the offset math below: 0 = +X, 90 = +Z.
            Vector3 toSpawn = playerSpawnPosition - topAnchorPosition;
            float doorwayCenterAngleDeg = Mathf.Atan2(toSpawn.z, toSpawn.x) * Mathf.Rad2Deg;
            float panelAngleDeg = doorwayCenterAngleDeg + PanelDoorwayOffsetDeg;

            GameObject root = new("Elevator");
            root.transform.position = topAnchorPosition;

            GameObject carFloor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            carFloor.name = "Car Floor";
            carFloor.transform.SetParent(root.transform, false);
            carFloor.transform.localPosition = new Vector3(0f, CarFloorThickness / 2f, 0f);
            carFloor.transform.localScale = new Vector3(settings.CarDiameterMeters, CarFloorThickness / 2f, settings.CarDiameterMeters);
            carFloor.GetComponent<Renderer>().sharedMaterial = floor;
            // CreatePrimitive(Cylinder) attaches a CapsuleCollider in this Unity version, not a
            // MeshCollider — and CapsuleCollider silently clamps its own height to at least
            // 2*radius. For a short, wide disc like this floor (thickness 0.1m, radius up to a
            // few metres) that inflates the "floor" into a multi-metre sphere bulging both above
            // and below it, which is exactly what blocked boarding: a player approaching from
            // the platform hits the sphere's rounded flank well outside the visible floor and
            // can never climb it. Swap in a MeshCollider built from the same primitive mesh so
            // collision actually matches the thin disc you see.
            Object.DestroyImmediate(carFloor.GetComponent<Collider>());
            carFloor.AddComponent<MeshCollider>().sharedMesh = carFloor.GetComponent<MeshFilter>().sharedMesh;
            // The floor stays a full circle (no doorway gap) — it's walkable surface, not a wall.

            CreateElevatorFramePosts(root.transform, carRadius, interiorHeight, frame, doorwayCenterAngleDeg);
            float doorwayHalfAngleDeg = CreateElevatorShell(root.transform, carRadius, interiorRadius, interiorHeight, glass, doorwayCenterAngleDeg, panelAngleDeg);
            CreateElevatorControlPanel(root.transform, interiorRadius, panelAccent, panelAngleDeg);
            CreateElevatorRiderTrigger(root.transform, interiorRadius, interiorHeight);
            CreateElevatorRoof(root.transform, settings.CarDiameterMeters, interiorHeight, glass);

            ElevatorController controller = root.AddComponent<ElevatorController>();
            SerializedObject serialized = new(controller);
            serialized.FindProperty("topPosition").vector3Value = topAnchorPosition;
            serialized.FindProperty("bottomPosition").vector3Value = bottomAnchorPosition;
            serialized.FindProperty("travelSecondsOneWay").floatValue = settings.ElevatorTravelSecondsOneWay;
            serialized.FindProperty("doorSealSeconds").floatValue = settings.DoorSealSeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            CreateElevatorDoor(root.transform, interiorRadius, interiorHeight, frame, doorwayCenterAngleDeg, doorwayHalfAngleDeg);
        }

        private static void CreateElevatorFramePosts(Transform parent, float carRadius, float interiorHeight, Material frameMaterial, float doorwayCenterAngleDeg)
        {
            const int postCount = 4;
            float postRadius = carRadius - CarFrameRadius;
            for (int i = 0; i < postCount; i++)
            {
                float angle = (doorwayCenterAngleDeg + PostDoorwayOffsetDeg + i * 360f / postCount) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * postRadius;
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Frame Post " + (i + 1);
                post.transform.SetParent(parent, false);
                post.transform.localPosition = offset + new Vector3(0f, interiorHeight / 2f, 0f);
                post.transform.localScale = new Vector3(CarFrameRadius * 2f, interiorHeight / 2f, CarFrameRadius * 2f);
                post.GetComponent<Renderer>().sharedMaterial = frameMaterial;
                // Default CapsuleCollider kept: unlike Car Floor, these posts are tall and
                // thin (height 3.5m vs radius 0.06m), so Unity's height>=2*radius clamp never
                // engages and the capsule matches the visible post exactly. Thin corner beams
                // are fine to feel solid, and they're derived from the doorway angle (always
                // PostDoorwayOffsetDeg away, every 90 degrees) so they can never end up
                // standing in the opening.
            }
        }

        // Visible glass shell and invisible wall colliders, generated from ONE loop over the
        // same ring of angles so the doorway gap in what you SEE and what BLOCKS you can never
        // drift apart. Segmented panes (rather than one smooth cylinder) are the tradeoff that
        // buys an exact, provably-matching opening.
        private static float CreateElevatorShell(Transform parent, float glassRadius, float wallRadius, float height, Material glass, float doorwayCenterAngleDeg, float panelAngleDeg)
        {
            const int segmentCount = 24;
            const float wallThickness = 0.15f;
            const float glassThickness = 0.05f;

            GameObject shellRoot = new("Glass Shell");
            shellRoot.transform.SetParent(parent, false);
            GameObject wallsRoot = new("Interior Walls");
            wallsRoot.transform.SetParent(parent, false);

            // Doorway width is measured at the glass (the actual opening a player walks
            // through); the same angular range is then skipped for the inset wall ring too.
            float doorwayHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(CarDoorwayWidthMeters / 2f / glassRadius)) * Mathf.Rad2Deg;
            // The panel sits on the wall ring's own radius, so without a matching gap here a
            // wall segment lands physically inside the panel and a raycast aimed at the panel
            // can hit the wall instead — a real, previously-shipped bug (a panel you can walk
            // up to and still can't reliably press E on). The glass stays solid at this
            // bearing; only the wall collider needs to step aside for the panel's own collider.
            float panelHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(PanelWidthMeters / 2f / wallRadius)) * Mathf.Rad2Deg + 3f;
            float glassSegmentArcLength = Mathf.PI * 2f * glassRadius / segmentCount * 1.05f;
            float wallSegmentArcLength = Mathf.PI * 2f * wallRadius / segmentCount * 1.05f;

            for (int i = 0; i < segmentCount; i++)
            {
                float angleDeg = i * 360f / segmentCount;
                bool inDoorway = Mathf.Abs(Mathf.DeltaAngle(angleDeg, doorwayCenterAngleDeg)) <= doorwayHalfAngleDeg;
                bool inPanel = Mathf.Abs(Mathf.DeltaAngle(angleDeg, panelAngleDeg)) <= panelHalfAngleDeg;
                if (inDoorway)
                    continue; // doorway gap: no glass pane, no wall collider here

                float angleRad = angleDeg * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

                GameObject glassPane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                glassPane.name = "Glass Pane " + (i + 1);
                glassPane.transform.SetParent(shellRoot.transform, false);
                glassPane.transform.localPosition = direction * glassRadius + new Vector3(0f, height / 2f, 0f);
                glassPane.transform.localRotation = rotation;
                glassPane.transform.localScale = new Vector3(glassSegmentArcLength, height, glassThickness);
                glassPane.GetComponent<Renderer>().sharedMaterial = glass;
                // CreatePrimitive(Cube) attaches a BoxCollider that would seal the car shut
                // (this was the original placeholder's bug: nobody could walk in). The shell
                // is a visual shroud only — collision comes from the floor and the wall ring.
                Object.DestroyImmediate(glassPane.GetComponent<Collider>());

                if (inPanel)
                    continue; // the control panel's own collider covers this arc instead

                GameObject wallSegment = new("Wall Segment " + (i + 1), typeof(BoxCollider));
                wallSegment.transform.SetParent(wallsRoot.transform, false);
                wallSegment.transform.localPosition = direction * wallRadius + new Vector3(0f, height / 2f, 0f);
                wallSegment.transform.localRotation = rotation;
                wallSegment.GetComponent<BoxCollider>().size = new Vector3(wallSegmentArcLength, height, wallThickness);
            }

            return doorwayHalfAngleDeg;
        }

        // Two curved leaves built from small flat panels, plus one collider spanning the
        // full doorway gap at the wall ring's own radius. Reuses doorwayHalfAngleDeg computed
        // above in CreateElevatorShell (same formula, same radius) so the doorway arc stays a
        // single source of truth between glass, walls and door.
        private static void CreateElevatorDoor(Transform parent, float wallRadius, float height, Material doorMaterial, float doorwayCenterAngleDeg, float doorwayHalfAngleDeg)
        {
            GameObject doorRoot = new("Elevator Door");
            doorRoot.transform.SetParent(parent, false);

            Transform leafRightPivot = CreateDoorLeaf(doorRoot.transform, "Leaf Right", wallRadius, height, doorwayCenterAngleDeg, doorwayHalfAngleDeg, doorMaterial, rightSide: true);
            Transform leafLeftPivot = CreateDoorLeaf(doorRoot.transform, "Leaf Left", wallRadius, height, doorwayCenterAngleDeg, doorwayHalfAngleDeg, doorMaterial, rightSide: false);

            float doorwayCenterRad = doorwayCenterAngleDeg * Mathf.Deg2Rad;
            Vector3 doorwayDirection = new Vector3(Mathf.Cos(doorwayCenterRad), 0f, Mathf.Sin(doorwayCenterRad));
            float fullDoorwayArcLength = wallRadius * (doorwayHalfAngleDeg * 2f * Mathf.Deg2Rad);

            GameObject colliderObject = new("Door Collider", typeof(BoxCollider));
            colliderObject.transform.SetParent(doorRoot.transform, false);
            colliderObject.transform.localPosition = doorwayDirection * wallRadius + new Vector3(0f, height / 2f, 0f);
            colliderObject.transform.localRotation = Quaternion.LookRotation(doorwayDirection, Vector3.up);
            BoxCollider doorBoxCollider = colliderObject.GetComponent<BoxCollider>();
            doorBoxCollider.size = new Vector3(fullDoorwayArcLength, height, DoorThicknessMeters);
            doorBoxCollider.enabled = false; // starts open, resting AtTop; ElevatorDoor takes over every frame at runtime

            ElevatorDoor door = doorRoot.AddComponent<ElevatorDoor>();
            SerializedObject serializedDoor = new(door);
            serializedDoor.FindProperty("leafLeftPivot").objectReferenceValue = leafLeftPivot;
            serializedDoor.FindProperty("leafRightPivot").objectReferenceValue = leafRightPivot;
            serializedDoor.FindProperty("doorCollider").objectReferenceValue = doorBoxCollider;
            serializedDoor.FindProperty("doorwayHalfAngleDeg").floatValue = doorwayHalfAngleDeg;
            serializedDoor.ApplyModifiedPropertiesWithoutUndo();
        }

        // A leaf is a pivot at the car's own local origin carrying DoorLeafPanelCount small
        // flat panels, built at the leaf's CLOSED angular span (from the doorway edge on this
        // side to the doorway centre). ElevatorDoor rotates the pivot at runtime to sweep this
        // whole rigid set further around the circumference as the door opens — see its
        // comment for the sign convention.
        private static Transform CreateDoorLeaf(Transform parent, string name, float radius, float height, float doorwayCenterAngleDeg, float doorwayHalfAngleDeg, Material material, bool rightSide)
        {
            GameObject pivot = new(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;

            float startAngleDeg = rightSide ? doorwayCenterAngleDeg : doorwayCenterAngleDeg - doorwayHalfAngleDeg;
            float endAngleDeg = rightSide ? doorwayCenterAngleDeg + doorwayHalfAngleDeg : doorwayCenterAngleDeg;
            float segmentArcLength = radius * (doorwayHalfAngleDeg * Mathf.Deg2Rad) / DoorLeafPanelCount * 1.05f;

            for (int i = 0; i < DoorLeafPanelCount; i++)
            {
                float segStartDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, (float)i / DoorLeafPanelCount);
                float segEndDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, (float)(i + 1) / DoorLeafPanelCount);
                float angleRad = (segStartDeg + segEndDeg) / 2f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

                GameObject panelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panelObject.name = name + " Panel " + (i + 1);
                panelObject.transform.SetParent(pivot.transform, false);
                panelObject.transform.localPosition = direction * radius + new Vector3(0f, height / 2f, 0f);
                panelObject.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
                panelObject.transform.localScale = new Vector3(segmentArcLength, height, DoorThicknessMeters);
                panelObject.GetComponent<Renderer>().sharedMaterial = material;
                // Blocking comes from the single Door Collider on the parent, not the leaves —
                // same reasoning as the glass panes: a per-panel BoxCollider here would seal
                // (or half-seal) the car regardless of door state.
                Object.DestroyImmediate(panelObject.GetComponent<Collider>());
            }

            return pivot.transform;
        }

        // Ceiling disc matching the floor's own diameter, with a collider so nothing enters
        // or exits from above. Sides stay fully transparent (the glass shell); only the roof
        // needs to block, since the Bell Eater is drawn to the elevator by design and a
        // reachable open top would make a docked car unsurvivable.
        private static void CreateElevatorRoof(Transform parent, float carDiameterMeters, float interiorHeight, Material glass)
        {
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            roof.name = "Car Roof";
            roof.transform.SetParent(parent, false);
            roof.transform.localPosition = new Vector3(0f, interiorHeight - CarFloorThickness / 2f, 0f);
            roof.transform.localScale = new Vector3(carDiameterMeters, CarFloorThickness / 2f, carDiameterMeters);
            roof.GetComponent<Renderer>().sharedMaterial = glass;
            // Same CapsuleCollider-clamp problem as Car Floor (see its comment): a thin wide
            // disc gets its default capsule inflated to a multi-metre sphere. Swap in a
            // MeshCollider built from the same disc mesh so collision matches what's visible.
            Object.DestroyImmediate(roof.GetComponent<Collider>());
            roof.AddComponent<MeshCollider>().sharedMesh = roof.GetComponent<MeshFilter>().sharedMesh;
        }

        private static void CreateElevatorControlPanel(Transform parent, float radius, Material accent, float panelAngleDeg)
        {
            float angleRad = panelAngleDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * radius;

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Control Panel";
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = offset + new Vector3(0f, PanelChestHeightMeters, 0f);
            panel.transform.localRotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
            panel.transform.localScale = new Vector3(PanelWidthMeters, PanelHeightMeters, PanelThicknessMeters);
            panel.GetComponent<Renderer>().sharedMaterial = accent;
            panel.AddComponent<ElevatorControlPanel>();
            // Cube's default BoxCollider is exactly what the interactor's raycast needs to
            // hit, and it visually stands out from the frame posts via the accent material.
        }

        private static void CreateElevatorRiderTrigger(Transform parent, float radius, float height)
        {
            // A box, not a capsule: a CapsuleCollider clamps its own height to at least
            // 2*radius, which for this car (radius comparable to height) silently balloons
            // the trigger well past the intended ceiling/floor. A box matches the requested
            // footprint exactly, no hidden geometry surprises.
            GameObject go = new("Rider Trigger", typeof(BoxCollider), typeof(ElevatorRiderTrigger));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            BoxCollider collider = go.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(radius * 2f, height, radius * 2f);
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
