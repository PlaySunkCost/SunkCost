using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Sites
{
    public static class DiveSiteValidator
    {
        private const float DepthTolerance = 0.01f;
        private const float SizeTolerance = 1f;
        private const float WreckDistanceTolerance = 5f;
        private const float LightParamTolerance = 0.01f;
        private const float MaxDeepAmbientChannel = 0.05f;

        [MenuItem("Sunk Cost/Prototype/Validate Dive Site 01")]
        public static void ValidateOrThrow()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != DiveSiteBuilder.ScenePath) errors.Add("Open scene is not " + DiveSiteBuilder.ScenePath);
            if (!File.Exists(DiveSiteBuilder.ScenePath)) errors.Add("Dive site scene is not saved on disk.");

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

            Transform car = FindByName(scene, "ElevatorCar_Placeholder");
            if (car == null) errors.Add("ElevatorCar_Placeholder is missing.");
            else if (top != null && car.parent != top) errors.Add("ElevatorCar_Placeholder should be parented to ElevatorAnchor_Top.");

            foreach (string boundaryName in new[] { "Boundary_Temp_North", "Boundary_Temp_South", "Boundary_Temp_East", "Boundary_Temp_West" })
            {
                if (FindByName(scene, boundaryName) == null)
                    errors.Add(boundaryName + " is missing.");
            }

            Transform seafloorGround = FindByName(scene, "Seafloor Ground");
            if (seafloorGround == null)
                errors.Add("Seafloor Ground is missing.");
            else if (Mathf.Abs(seafloorGround.lossyScale.x - DiveSiteBuilder.SeafloorSize) > SizeTolerance
                     || Mathf.Abs(seafloorGround.lossyScale.z - DiveSiteBuilder.SeafloorSize) > SizeTolerance)
                errors.Add($"Seafloor should be {DiveSiteBuilder.SeafloorSize}x{DiveSiteBuilder.SeafloorSize}m; found {seafloorGround.lossyScale.x}x{seafloorGround.lossyScale.z}.");

            Transform wreck = FindByName(scene, "Wreck");
            if (wreck == null) errors.Add("Wreck is missing.");
            else if (bottom != null && Mathf.Abs(Vector3.Distance(wreck.position, bottom.position) - DiveSiteBuilder.WreckOffset.magnitude) > WreckDistanceTolerance)
                errors.Add($"Wreck should sit roughly {DiveSiteBuilder.WreckOffset.magnitude}m from the elevator anchor; found {Vector3.Distance(wreck.position, bottom.position)}m.");

            CheckCount<DiveSiteDevPlayer>(scene, 1, errors);
            if (AnyComponentInScene<NetworkManager>(scene) || AnyComponentInScene<NetworkObject>(scene))
                errors.Add("Dive site must not contain networking components (this is a single-player greybox).");

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

            Transform headlamp = FindByName(scene, "Headlamp");
            if (headlamp == null)
            {
                errors.Add("Headlamp is missing from the dev harness camera.");
            }
            else
            {
                Light light = headlamp.GetComponent<Light>();
                if (light == null || light.type != LightType.Spot)
                    errors.Add("Headlamp must be a spot light.");
                else
                {
                    if (Mathf.Abs(light.intensity - DiveSiteBuilder.HeadlampIntensity) > LightParamTolerance)
                        errors.Add("Headlamp intensity does not match the expected " + DiveSiteBuilder.HeadlampIntensity + " lumens.");
                    if (Mathf.Abs(light.range - DiveSiteBuilder.HeadlampRange) > LightParamTolerance)
                        errors.Add("Headlamp range does not match the expected " + DiveSiteBuilder.HeadlampRange + "m.");
                    if (Mathf.Abs(light.spotAngle - DiveSiteBuilder.HeadlampSpotAngle) > LightParamTolerance)
                        errors.Add("Headlamp spot angle does not match the expected " + DiveSiteBuilder.HeadlampSpotAngle + " degrees.");
                }
            }

            DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
            if (settings == null)
            {
                errors.Add("DiveSiteSettings asset is missing at " + DiveSiteBuilder.SettingsPath);
            }
            else
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
            Debug.Log("Dive Site 01 validation passed: saved scene, platform/shaft/seafloor, elevator anchors, car, guide cable, dev player and lighting are ready.");
        }

        private static void CheckCount<T>(Scene scene, int expected, List<string> errors) where T : Component
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<T>(true).Length;
            if (count != expected) errors.Add($"Expected {expected} {typeof(T).Name}; found {count}.");
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

        private static bool MaterialExists(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Prototype/Materials/" + fileName) != null;
        }
    }
}
