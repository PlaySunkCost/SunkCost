using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Object;
using SunkCost.Diving;
using SunkCost.Editor.Prototype;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Sites
{
    public static class DiveSiteValidator
    {
        private const float DepthTolerance = 0.01f;
        private const float SizeTolerance = 0.01f;
        private const float WreckDistanceTolerance = 0.01f;
        private const float LightParamTolerance = 0.01f;
        private const float MaxDeepAmbientChannel = 0.05f;

        [MenuItem("Sunk Cost/Prototype/Validate Dive Site 01")]
        public static void ValidateOrThrow()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DiveSiteBuilder.ScenePath) errors.Add("Open scene is not " + DiveSiteBuilder.ScenePath);
            if (!File.Exists(DiveSiteBuilder.ScenePath)) errors.Add("Dive site scene is not saved on disk.");

            DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
            if (settings == null) errors.Add("DiveSiteSettings asset is missing at " + DiveSiteBuilder.SettingsPath);

            if (!HasRoot(scene, "Surface Platform")) errors.Add("Surface Platform is missing.");
            if (!HasRoot(scene, "Shaft")) errors.Add("Shaft is missing.");
            if (!HasRoot(scene, "Seafloor")) errors.Add("Seafloor is missing.");

            Transform top = FindByName(scene, "ElevatorAnchor_Top");
            Transform bottom = FindByName(scene, "ElevatorAnchor_Bottom");
            if (top == null) errors.Add("ElevatorAnchor_Top is missing.");
            if (bottom == null) errors.Add("ElevatorAnchor_Bottom is missing.");

            Transform spawnGroup = FindByName(scene, "Spawn Points");
            if (spawnGroup == null || spawnGroup.childCount != 4)
                errors.Add("Expected a Spawn Points group with 4 spawn markers.");

            if (FindByName(scene, "Guide Cable") == null)
                errors.Add("Guide Cable is missing between the elevator anchors.");

            GameObject elevatorRootObject = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Elevator");
            if (elevatorRootObject == null)
            {
                errors.Add("Elevator root is missing.");
            }
            else
            {
                Transform elevatorRoot = elevatorRootObject.transform;
                if (top != null && Vector3.Distance(elevatorRoot.position, top.position) > DepthTolerance)
                    errors.Add("Elevator should sit at the top anchor (ElevatorAnchor_Top) on a fresh build.");

                if (elevatorRoot.GetComponent<ElevatorController>() == null)
                    errors.Add("Elevator is missing its ElevatorController component.");

                Transform carFloor = FindByName(scene, "Car Floor");
                if (carFloor == null || carFloor.GetComponent<Collider>() == null)
                    errors.Add("Elevator car floor collider is missing.");

                Transform glassShell = FindByName(scene, "Glass Shell");
                Transform interiorWalls = FindByName(scene, "Interior Walls");
                if (glassShell == null)
                {
                    errors.Add("Elevator glass shell is missing.");
                }
                else
                {
                    if (glassShell.childCount == 0)
                        errors.Add("Elevator glass shell has no panes.");
                    foreach (Transform pane in glassShell)
                    {
                        if (pane.GetComponent<Collider>() != null)
                            errors.Add(pane.name + " should have no collider (it would seal the car shut).");
                    }
                    // Interior Walls can have fewer segments than Glass Shell (the control
                    // panel's own arc is skipped for walls only, see below) but never more —
                    // both come from the same ring over the same doorway gap.
                    if (interiorWalls != null && interiorWalls.childCount > glassShell.childCount)
                        errors.Add($"Interior Walls has {interiorWalls.childCount} segments but Glass Shell only has {glassShell.childCount} — they must come from the same ring over the same angles.");
                }

                if (interiorWalls == null)
                    errors.Add("Elevator interior wall ring is missing.");

                Transform riderTrigger = FindByName(scene, "Rider Trigger");
                if (riderTrigger == null)
                {
                    errors.Add("Elevator rider trigger is missing.");
                }
                else
                {
                    Collider triggerCollider = riderTrigger.GetComponent<Collider>();
                    if (triggerCollider == null || !triggerCollider.isTrigger)
                        errors.Add("Elevator rider trigger must have a trigger collider.");
                    if (riderTrigger.GetComponent<ElevatorRiderTrigger>() == null)
                        errors.Add("Elevator rider trigger is missing its ElevatorRiderTrigger component.");
                }

                Transform panel = FindByName(scene, "Control Panel");
                if (panel == null)
                {
                    errors.Add("Elevator control panel is missing.");
                }
                else
                {
                    Collider panelCollider = panel.GetComponent<Collider>();
                    if (panelCollider == null)
                        errors.Add("Elevator control panel must have a collider for the interact raycast to hit.");
                    if (panel.GetComponent<ElevatorControlPanel>() == null)
                        errors.Add("Elevator control panel is missing its ElevatorControlPanel component.");

                    // A wall segment sharing the panel's arc would physically overlap its
                    // collider, so a raycast aimed at the panel could hit the wall instead —
                    // reachable but not operable. Compare bearings from the car's centre
                    // rather than Collider.bounds: bounds is an axis-aligned box around each
                    // rotated segment, so neighbouring (non-overlapping) wedges around a ring
                    // routinely have overlapping AABBs and would false-positive here.
                    if (panelCollider != null && interiorWalls != null)
                    {
                        Vector2 panelBearing = new(panel.position.x - elevatorRoot.position.x, panel.position.z - elevatorRoot.position.z);
                        float panelAngle = Mathf.Atan2(panelBearing.y, panelBearing.x) * Mathf.Rad2Deg;
                        foreach (Transform wallSegment in interiorWalls)
                        {
                            Vector2 wallBearing = new(wallSegment.position.x - elevatorRoot.position.x, wallSegment.position.z - elevatorRoot.position.z);
                            float wallAngle = Mathf.Atan2(wallBearing.y, wallBearing.x) * Mathf.Rad2Deg;
                            if (Mathf.Abs(Mathf.DeltaAngle(wallAngle, panelAngle)) < 5f)
                                errors.Add(wallSegment.name + " sits at the same bearing as the control panel — a raycast aimed at the panel could hit the wall instead.");
                        }
                    }
                }

                Transform doorRoot = FindByName(scene, "Elevator Door");
                if (doorRoot == null)
                {
                    errors.Add("Elevator door is missing.");
                }
                else
                {
                    if (doorRoot.GetComponent<ElevatorDoor>() == null)
                        errors.Add("Elevator Door is missing its ElevatorDoor component.");

                    Transform leafLeft = FindByName(scene, "Leaf Left");
                    Transform leafRight = FindByName(scene, "Leaf Right");
                    if (leafLeft == null || leafLeft.childCount == 0)
                        errors.Add("Elevator door's left leaf is missing its panels.");
                    if (leafRight == null || leafRight.childCount == 0)
                        errors.Add("Elevator door's right leaf is missing its panels.");

                    foreach (Transform leaf in new[] { leafLeft, leafRight })
                    {
                        if (leaf == null) continue;
                        foreach (Transform leafPanel in leaf)
                        {
                            if (leafPanel.GetComponent<Collider>() != null)
                                errors.Add(leafPanel.name + " should have no collider (blocking comes from the single Door Collider, not the leaves).");
                        }
                    }

                    Transform doorCollider = FindByName(scene, "Door Collider");
                    if (doorCollider == null || doorCollider.GetComponent<Collider>() == null)
                        errors.Add("Elevator door collider is missing.");
                }

                Transform roof = FindByName(scene, "Car Roof");
                if (roof == null)
                {
                    errors.Add("Elevator roof is missing.");
                }
                else
                {
                    if (roof.GetComponent<Collider>() == null)
                        errors.Add("Elevator roof must have a collider.");
                    if (settings != null && Mathf.Abs(roof.lossyScale.x - settings.CarDiameterMeters) > SizeTolerance)
                        errors.Add($"Elevator roof diameter should match the car ({settings.CarDiameterMeters}m); found {roof.lossyScale.x}m.");
                }
            }

            CheckCount<ElevatorController>(scene, 1, errors);
            CheckCount<ElevatorDoor>(scene, 1, errors);

            // Idan/Dan, 15 September 2026 elevator rules: "gated shut at the bottom when the
            // cabin is away; a player cannot walk in."
            Transform shaftGate = FindByName(scene, "Shaft Gate");
            if (shaftGate == null)
            {
                errors.Add("Shaft gate is missing.");
            }
            else
            {
                if (shaftGate.GetComponent<Collider>() == null)
                    errors.Add("Shaft gate must have a collider.");
                if (shaftGate.GetComponent<ShaftGate>() == null)
                    errors.Add("Shaft gate is missing its ShaftGate component.");
            }

            // The platform hole is now a circle (a fan of radial wedges — see
            // CreatePlatformRing), not a square, so there is no single "half-width" to check.
            // Instead confirm the inner (car-facing) edge of a sampled wedge sits at the
            // expected radius, and that the configured clearance itself satisfies the "no
            // gap over 0.15m at any bearing" requirement — the geometry is circular by
            // construction, so one sample proves the radius for every bearing.
            if (DiveSiteBuilder.ElevatorClearanceMeters > 0.15f)
                errors.Add($"ElevatorClearanceMeters is {DiveSiteBuilder.ElevatorClearanceMeters}m; must be at most 0.15m so no bearing has an unbridged gap wider than that.");

            Transform sampleSegment = FindByName(scene, "Platform Segment 1");
            if (sampleSegment == null)
            {
                errors.Add("Platform Segment 1 is missing (expected a ring of radial platform segments around the shaft hole).");
            }
            else if (settings != null)
            {
                // Segments are flat trapezoid meshes at an identity transform, not scaled/
                // rotated boxes — read the inner-edge radius from the mesh's own vertices
                // (vertex 0 is the "innerA" corner, at exactly shaftRadius by construction).
                MeshFilter meshFilter = sampleSegment.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0)
                {
                    errors.Add("Platform Segment 1 is missing its mesh.");
                }
                else
                {
                    Vector3 innerVertexWorld = sampleSegment.TransformPoint(meshFilter.sharedMesh.vertices[0]);
                    float actualInnerRadius = new Vector2(innerVertexWorld.x, innerVertexWorld.z).magnitude;
                    float expectedInnerRadius = settings.CarDiameterMeters / 2f + DiveSiteBuilder.ElevatorClearanceMeters;
                    if (Mathf.Abs(actualInnerRadius - expectedInnerRadius) > SizeTolerance)
                        errors.Add($"Platform hole radius should be car radius + {DiveSiteBuilder.ElevatorClearanceMeters}m ({expectedInnerRadius}m); found {actualInnerRadius}m.");
                }
            }

            // Regression check for a real shipped bug: the platform hole ring is a fan of
            // rectangular wedges approximating a circle, and a wedge sized for its own
            // mid-radius falls short of its neighbours out near the platform's edge — gaps a
            // player walking toward the car would fall through. Sweep the walkable band around
            // the shaft (out to 8m, well past where anyone actually walks to board) and fail
            // loudly if any sample point isn't solid platform.
            if (sampleSegment != null && settings != null)
            {
                Physics.SyncTransforms();
                float shaftRadius = settings.CarDiameterMeters / 2f + DiveSiteBuilder.ElevatorClearanceMeters;
                int gapCount = 0;
                float firstGapAngle = 0f, firstGapRadius = 0f;
                for (int a = 0; a < 360 && gapCount < 1; a += 5)
                {
                    float angleRad = a * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                    for (float r = shaftRadius + 0.05f; r < 8f; r += 0.3f)
                    {
                        Vector3 point = dir * r + new Vector3(0f, 5f, 0f);
                        bool hit = Physics.Raycast(point, Vector3.down, out RaycastHit hitInfo, 10f, ~0, QueryTriggerInteraction.Ignore);
                        bool onPlatform = hit && hitInfo.collider.name.StartsWith("Platform Segment") && Mathf.Abs(hitInfo.point.y) < 0.1f;
                        if (!onPlatform)
                        {
                            gapCount++;
                            firstGapAngle = a;
                            firstGapRadius = r;
                            break;
                        }
                    }
                }
                if (gapCount > 0)
                    errors.Add($"Platform ring has a gap near bearing {firstGapAngle} degrees at radius {firstGapRadius:F2}m (within the 8m a player walks through to board) — someone walking there falls through.");
            }

            if (settings != null)
            {
                if (settings.CarDiameterMeters <= 0f) errors.Add("DiveSiteSettings.CarDiameterMeters must be positive.");
                if (settings.CarInteriorHeightMeters <= 0f) errors.Add("DiveSiteSettings.CarInteriorHeightMeters must be positive.");
                if (settings.ElevatorTravelSecondsOneWay <= 0f) errors.Add("DiveSiteSettings.ElevatorTravelSecondsOneWay must be positive.");
                if (settings.DoorSealSeconds <= 0f) errors.Add("DiveSiteSettings.DoorSealSeconds must be positive.");
            }

            foreach (string boundaryName in new[] { "Boundary_Temp_North", "Boundary_Temp_South", "Boundary_Temp_East", "Boundary_Temp_West" })
            {
                if (FindByName(scene, boundaryName) == null)
                    errors.Add(boundaryName + " is missing.");
            }

            Transform seafloorGround = FindByName(scene, "Seafloor Ground");
            if (seafloorGround == null)
                errors.Add("Seafloor Ground is missing.");
            else if (settings != null
                     && (Mathf.Abs(seafloorGround.lossyScale.x - settings.SeafloorSizeMeters) > SizeTolerance
                         || Mathf.Abs(seafloorGround.lossyScale.z - settings.SeafloorSizeMeters) > SizeTolerance))
                errors.Add($"Seafloor should be {settings.SeafloorSizeMeters}x{settings.SeafloorSizeMeters}m; found {seafloorGround.lossyScale.x}x{seafloorGround.lossyScale.z}.");

            Transform wreck = FindByName(scene, "Wreck");
            if (wreck == null) errors.Add("Wreck is missing.");
            else if (bottom != null && settings != null
                     && Mathf.Abs(Vector3.Distance(wreck.position, bottom.position) - settings.WreckOffset.magnitude) > WreckDistanceTolerance)
                errors.Add($"Wreck should sit roughly {settings.WreckOffset.magnitude}m from the elevator anchor; found {Vector3.Distance(wreck.position, bottom.position)}m.");

            // DiveSite01 is a world scene like HQ and ShipAtSea
            // (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 9.1): it carries no
            // session machinery of its own — Session.unity owns the one network root —
            // and no placed player; CrewSpawner spawns the networked player at runtime
            // from PrototypePlayer.prefab using the Spawn Points group required above.
            WorldSceneChecks.CheckNoSessionMachinery(scene, errors, "DiveSite01");
            // The underwater grade must be local (a box), never global: the host on the
            // deck at sea has this scene loaded too (cabin ride card).
            foreach (UnityEngine.Rendering.Volume volume in FindComponentsInScene<UnityEngine.Rendering.Volume>(scene))
            {
                if (volume.isGlobal) errors.Add("'" + volume.name + "' is a global Volume; the dive site's grade must be a local box (run Apply deck cabin ride setup).");
                else if (volume.GetComponent<Collider>() == null) errors.Add("'" + volume.name + "' is local but has no collider.");
            }
            HQPrototypeValidator.CheckCount<NetworkObject>(scene, 0, errors);
            if (AnyComponentInScene<HQPlayerController>(scene))
                errors.Add("Dive site must not contain a placed HQPlayerController — the player spawns at runtime from PrototypePlayer.prefab.");
            WorldSceneChecks.CheckBuildList(errors);

            // --- Lighting: the sun must not reach the seafloor's own layer ---
            int deepLayer = LayerMask.NameToLayer(DiveSiteBuilder.DeepLayerName);
            if (deepLayer < 0)
            {
                errors.Add("Layer '" + DiveSiteBuilder.DeepLayerName + "' does not exist.");
            }
            else
            {
                GameObject seafloorRoot = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Seafloor");
                if (seafloorRoot != null && seafloorRoot.layer != deepLayer)
                    errors.Add("Seafloor root is not on the " + DiveSiteBuilder.DeepLayerName + " layer.");

                Transform surfaceLight = FindByName(scene, "Surface Light");
                if (surfaceLight == null)
                {
                    errors.Add("Surface Light is missing.");
                }
                else
                {
                    Light light = surfaceLight.GetComponent<Light>();
                    if (light == null || (light.cullingMask & (1 << deepLayer)) != 0)
                        errors.Add("Surface Light must exclude the " + DiveSiteBuilder.DeepLayerName + " layer from its culling mask.");
                }
            }

            if (RenderSettings.ambientLight.maxColorComponent > MaxDeepAmbientChannel)
                errors.Add("Ambient light is too bright for the seafloor to read as dark (expected near-black).");
            if (!RenderSettings.fog) errors.Add("Fog must be enabled.");
            if (RenderSettings.fogMode != FogMode.ExponentialSquared) errors.Add("Fog mode should be ExponentialSquared.");
            if (RenderSettings.fogDensity <= 0f) errors.Add("Fog density must be positive.");

            // The headlamp lives on PrototypePlayer.prefab (disabled by default; a dive
            // site enables it at runtime via DiveSiteHeadlampActivator) rather than as a
            // scene object, so it's checked against the prefab asset, not this scene.
            GameObject playerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            if (playerPrefabAsset == null)
            {
                errors.Add("PrototypePlayer.prefab is missing at " + HQPrototypeBuilder.PlayerPrefabPath + ".");
            }
            else
            {
                Transform headlamp = FindByName(playerPrefabAsset.transform, "Headlamp");
                if (headlamp == null)
                {
                    errors.Add("Headlamp is missing from PrototypePlayer.prefab.");
                }
                else
                {
                    Light light = headlamp.GetComponent<Light>();
                    if (light == null || light.type != LightType.Spot)
                        errors.Add("Headlamp must be a spot light.");
                    else if (settings != null)
                    {
                        if (light.enabled)
                            errors.Add("Headlamp should start disabled on PrototypePlayer.prefab; the dive site enables it at runtime.");
                        if (Mathf.Abs(light.intensity - settings.HeadlampIntensity) > LightParamTolerance)
                            errors.Add("Headlamp intensity does not match the expected " + settings.HeadlampIntensity + " lumens.");
                        if (Mathf.Abs(light.range - settings.HeadlampRange) > LightParamTolerance)
                            errors.Add("Headlamp range does not match the expected " + settings.HeadlampRange + "m.");
                        if (Mathf.Abs(light.spotAngle - settings.HeadlampSpotAngle) > LightParamTolerance)
                            errors.Add("Headlamp spot angle does not match the expected " + settings.HeadlampSpotAngle + " degrees.");
                    }
                }
            }

            if (settings != null)
            {
                float depth = settings.ShaftDepthMeters;
                if (depth <= 0f) errors.Add("DiveSiteSettings.ShaftDepthMeters must be positive.");
                if (top != null && Mathf.Abs(top.position.y) > DepthTolerance)
                    errors.Add("ElevatorAnchor_Top should sit at platform level (y = 0).");
                if (bottom != null && Mathf.Abs(bottom.position.y - -depth) > DepthTolerance)
                    errors.Add("ElevatorAnchor_Bottom does not match the configured shaft depth.");
            }

            if (!MaterialExists("HQFloor.mat")) errors.Add("Reused floor material HQFloor.mat is missing.");
            if (!MaterialExists("HQWall.mat")) errors.Add("Reused wall material HQWall.mat is missing.");
            if (!MaterialExists("BallOrange.mat")) errors.Add("Reused accent material BallOrange.mat is missing.");
            if (!MaterialExists("PlayerBase.mat")) errors.Add("Reused player material PlayerBase.mat is missing.");
            if (!MaterialExists("DiveSiteGlass.mat")) errors.Add("Elevator car glass material DiveSiteGlass.mat is missing.");

            if (errors.Count > 0) throw new InvalidOperationException("Dive Site 01 validation failed:\n- " + string.Join("\n- ", errors));
            Debug.Log("Dive Site 01 validation passed: saved scene, platform/shaft/seafloor, elevator anchors, car, guide cable and lighting are ready as a world scene (no session machinery, no placed player).");
        }

        private static void CheckCount<T>(Scene scene, int expected, List<string> errors) where T : Component
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<T>(true).Length;
            if (count != expected) errors.Add($"Expected {expected} {typeof(T).Name}; found {count}.");
        }

        private static IEnumerable<T> FindComponentsInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        private static bool AnyComponentInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<T>(true).Length > 0);
        }

        private static bool HasRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return true;
            return false;
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                    if (candidate.name == name) return candidate;
            }
            return null;
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == name) return candidate;
            return null;
        }

        private static bool MaterialExists(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Prototype/Materials/" + fileName) != null;
        }
    }
}
