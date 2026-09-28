using System.Collections.Generic;
using SunkCost.Diving;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SunkCost.Sites
{
    // Pays the dive site's first-use rendering costs while the rider's screen is
    // still black. Measured on the way down (frame recorder, 15 September 2026):
    // the water surface disc, the cabin water disc and the underwater grade each
    // stalled a frame for 20-47 ms the first time they came into view, mid-shaft,
    // in the player's face. Here a throwaway camera renders them once, off screen,
    // with the grade forced on, so the pipeline's shader variants, render targets
    // and material setups exist before the fade-in. Presentation only; nothing
    // networked, nothing left behind: every object is put back as it was.
    public static class DiveSiteWarmup
    {
        // Loaded scenes already warmed; an entry leaves when its scene unloads, so
        // the next load of the site (fresh every time the seafloor empties) warms again.
        private static readonly HashSet<Scene> warmedScenes = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => SceneManager.sceneUnloaded += unloaded => warmedScenes.Remove(unloaded);

        public static bool Warmed(Scene scene) => warmedScenes.Contains(scene);

        // Renders twice through a copy of the player's camera: from just above the
        // water surface looking down the tube, and from inside the car looking at
        // its (temporarily raised) water. Safe to call more than once; the second
        // call for the same loaded scene does nothing.
        public static void RenderOnce(Camera reference, Scene scene)
        {
            if (reference == null || !scene.isLoaded || warmedScenes.Contains(scene)) return;
            warmedScenes.Add(scene);

            Transform surface = null;
            Volume volume = null;
            ElevatorController car = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (surface == null) surface = FindChild(root.transform, "WaterSurface");
                if (volume == null) volume = root.GetComponentInChildren<Volume>(true);
                if (car == null) car = root.GetComponentInChildren<ElevatorController>(true);
            }
            Transform cabinWater = car != null ? car.transform.Find("Cabin Water") : null;
            // The car's water effects (streams, splashes, bubbles, the drain swirl) in a
            // mid-flood pose, and the tube's water ring (28 September 2026).
            CabinWaterVisuals waterFx = car != null ? car.GetComponentInChildren<CabinWaterVisuals>(true) : null;
            TubeWaterSurface tubeRing = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (tubeRing == null) tubeRing = root.GetComponentInChildren<TubeWaterSurface>(true);
            Renderer ringRenderer = tubeRing != null ? tubeRing.GetComponent<Renderer>() : null;
            bool ringWasEnabled = ringRenderer != null && ringRenderer.enabled;
            bool volumeWasGlobal = volume != null && volume.isGlobal;
            float volumeWeight = volume != null ? volume.weight : 1f;
            bool cabinWaterWasActive = cabinWater != null && cabinWater.gameObject.activeSelf;
            Vector3 cabinWaterLocal = cabinWater != null ? cabinWater.localPosition : Vector3.zero;

            GameObject go = null;
            RenderTexture target = null;
            try
            {
                // Every change below is undone in the finally, whatever throws: a warm
                // pose left on would freeze this peer's water effects for the scene's life.
                if (ringRenderer != null) ringRenderer.enabled = true;
                if (waterFx != null) waterFx.WarmPose(true);
                // Force what the ride will show: the grade everywhere, the cabin half full.
                if (volume != null) { volume.isGlobal = true; volume.weight = 1f; }
                if (cabinWater != null) { cabinWater.gameObject.SetActive(true); cabinWater.localPosition = new Vector3(0f, 1f, 0f); }

                go = new GameObject("DiveSiteWarmupCamera");
                Camera camera = go.AddComponent<Camera>();
                camera.CopyFrom(reference);
                camera.enabled = false;
                var referenceData = reference.GetComponent<UniversalAdditionalCameraData>();
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = referenceData == null || referenceData.renderPostProcessing;
                data.renderType = CameraRenderType.Base;
                SceneManager.MoveGameObjectToScene(go, scene);
                target = RenderTexture.GetTemporary(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height), 24);
                camera.targetTexture = target;

                if (surface != null)
                {
                    camera.transform.position = surface.position + Vector3.up * 1.2f + Vector3.forward * 0.5f;
                    camera.transform.rotation = Quaternion.LookRotation(Vector3.down + Vector3.forward * 0.3f, Vector3.forward);
                    camera.Render();
                    camera.transform.position = surface.position - Vector3.up * 1.2f;
                    camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                    camera.Render();
                }
                if (car != null)
                {
                    camera.transform.position = car.transform.position + Vector3.up * 1.6f;
                    camera.transform.rotation = Quaternion.LookRotation(Vector3.down + car.transform.forward * 0.5f, car.transform.forward);
                    camera.Render();
                    if (waterFx != null && waterFx.OutletCount > 0)
                    {
                        // From across the car at the first nozzle: its stream, splash and
                        // bubbles, the swirl and the surface in one view.
                        Vector3 outlet = car.transform.TransformPoint(waterFx.OutletLocal(0));
                        Vector3 across = car.transform.position + (car.transform.position - new Vector3(outlet.x, car.transform.position.y, outlet.z)).normalized * 1.8f + Vector3.up * 1.7f;
                        camera.transform.position = across;
                        camera.transform.rotation = Quaternion.LookRotation(outlet + Vector3.down * 1.4f - across, Vector3.up);
                        camera.Render();
                    }
                }
            }
            finally
            {
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (go != null) Object.Destroy(go);
                if (volume != null) { volume.isGlobal = volumeWasGlobal; volume.weight = volumeWeight; }
                if (cabinWater != null) { cabinWater.localPosition = cabinWaterLocal; cabinWater.gameObject.SetActive(cabinWaterWasActive); }
                if (waterFx != null) waterFx.WarmPose(false);
                if (ringRenderer != null) ringRenderer.enabled = ringWasEnabled;
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindChild(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
