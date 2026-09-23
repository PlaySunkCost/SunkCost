using System.Collections.Generic;
using FishNet.Object;
using SunkCost.Diving;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // Keeps the worlds' lights apart while two are loaded at once (SHIP-003, 23
    // September 2026): the host has the ship and the site together, and so does a
    // ship client while the TV has a channel. Forward+ ignores Light.cullingMask on
    // every light but the main one (tested: a masked light lit the seafloor as much
    // as an unmasked one), so the ship's Sun lit the seafloor and the site's
    // Surface Light, the brightest directional light, became the ship's main light.
    //
    // URP rendering layers do what the culling masks cannot: every renderer and
    // light is stamped with its world's layer at run time — a world scene's own
    // things when it loads, and the things that move between worlds (every network
    // object: players, carried items, monsters; and the site's car) four times a
    // second, by where they stand: the site sits at the origin and the ship 3 km
    // away (WorldLoopSettings.shipAtSeaOrigin). In the site, what moves deeper
    // than WorldLook.DeepBelowY, and the seafloor's own things, take the deep
    // layer the Surface Light does not reach. Lights keep the Default layer too,
    // so anything never stamped is lit as before. And each camera gets the main
    // light of the world it stands in (RenderSettings.sun, read by URP first).
    // Local presentation only.
    public static class WorldLightLayers
    {
        // Bits of the rendering layers named in the Tags and Layers settings (WorldLookSetup names them).
        public const int SeaBit = 1, DiveBit = 2, DiveDeepBit = 3, HQBit = 4;
        public const string DeepPhysicsLayerName = "DiveSiteDeep"; // the site builder's layer for the seafloor
        private const uint DefaultMask = 1u;
        private const uint AllWorldBits = DefaultMask | (1u << SeaBit) | (1u << DiveBit) | (1u << DiveDeepBit) | (1u << HQBit);
        private const float MoverSeconds = 0.25f;

        public static uint RendererMask(WorldId world, bool deep)
        {
            switch (world)
            {
                case WorldId.Sea: return 1u << SeaBit;
                case WorldId.Dive: return deep ? 1u << DiveDeepBit : 1u << DiveBit;
                default: return 1u << HQBit;
            }
        }

        public static uint LightMask(WorldId world, bool reachesDeep)
        {
            uint mask = DefaultMask | RendererMask(world, false);
            if (world == WorldId.Dive && reachesDeep) mask |= 1u << DiveDeepBit;
            return mask;
        }

        // ---- the loaded worlds --------------------------------------------------------

        private static readonly List<WorldLook> looks = new();
        private static int looksFrame = -1;

        public static List<WorldLook> Loaded()
        {
            if (Application.isPlaying && looksFrame == Time.frameCount) return looks;
            looksFrame = Time.frameCount;
            looks.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!WorldScenes.TryParse(scene.name, out _)) continue;
                WorldLook look = WorldLook.InScene(scene);
                if (look != null) looks.Add(look);
            }
            return looks;
        }

        public static bool TryWorldOf(WorldLook look, out WorldId world) => WorldScenes.TryParse(look.gameObject.scene.name, out world);

        // The loaded world a point stands in: the one whose things are nearest.
        public static WorldLook WorldAt(Vector3 position)
        {
            WorldLook best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (WorldLook look in Loaded())
            {
                if (!look.HasArea) MeasureArea(look);
                float distance = look.HasArea ? (look.Area.ClosestPoint(position) - position).sqrMagnitude : float.PositiveInfinity;
                if (best == null || distance < bestDistance) { best = look; bestDistance = distance; }
            }
            return best;
        }

        private static readonly List<Renderer> renderers = new();
        private static readonly List<Light> lights = new();

        private static void MeasureArea(WorldLook look)
        {
            look.HasArea = false;
            foreach (GameObject root in look.gameObject.scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, renderers);
                foreach (Renderer r in renderers)
                {
                    if (r.GetComponentInParent<NetworkObject>(true) != null) continue; // a thing that moves, not the world
                    if (!look.HasArea) { look.Area = r.bounds; look.HasArea = true; }
                    else look.Area.Encapsulate(r.bounds);
                }
            }
        }

        // ---- stamping -----------------------------------------------------------------

        // Everything a world scene holds, by its scene: the site's seafloor (its own
        // physics layer, or wholly under the deep line) on the deep layer, and the
        // site's lights that cull that physics layer (the Surface Light) off it.
        public static void StampScene(WorldLook look)
        {
            if (look == null || !TryWorldOf(look, out WorldId world)) return;
            MeasureArea(look);
            int deepLayer = LayerMask.NameToLayer(DeepPhysicsLayerName);
            foreach (GameObject root in look.gameObject.scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(true, renderers);
                foreach (Renderer r in renderers)
                {
                    bool deep = world == WorldId.Dive && ((deepLayer >= 0 && r.gameObject.layer == deepLayer) || r.bounds.max.y < look.DeepBelowY);
                    SetMask(r, RendererMask(world, deep));
                }
                root.GetComponentsInChildren(true, lights);
                foreach (Light light in lights)
                    SetMask(light, LightMask(world, deepLayer < 0 || (light.cullingMask & (1 << deepLayer)) != 0));
            }
        }

        // One thing that moves between worlds, by where its root stands.
        public static void StampMover(GameObject root)
        {
            if (root == null) return;
            Vector3 position = root.transform.position;
            WorldLook look = WorldAt(position);
            if (look == null || !TryWorldOf(look, out WorldId world)) return;
            bool deep = world == WorldId.Dive && position.y < look.DeepBelowY;
            uint rendererMask = RendererMask(world, deep), lightMask = LightMask(world, true);
            root.GetComponentsInChildren(true, renderers);
            foreach (Renderer r in renderers) SetMask(r, rendererMask);
            root.GetComponentsInChildren(true, lights);
            foreach (Light light in lights) SetMask(light, lightMask);
        }

        public static void StampMovers()
        {
            if (Loaded().Count == 0) return;
            foreach (NetworkObject nob in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude))
                if (nob.transform.parent == null || nob.transform.parent.GetComponentInParent<NetworkObject>() == null) StampMover(nob.gameObject);
            ElevatorController car = WorldSceneFlow.FindCarCached();
            if (car != null) StampMover(car.gameObject);
        }

        // Every loaded world's own things and then the movers (the edit-mode checks call this).
        public static void StampAll()
        {
            foreach (WorldLook look in Loaded()) StampScene(look);
            StampMovers();
        }

        private static void SetMask(Renderer r, uint mask)
        {
            uint value = (r.renderingLayerMask & ~AllWorldBits) | mask;
            if (r.renderingLayerMask != value) r.renderingLayerMask = value;
        }

        private static void SetMask(Light light, uint mask)
        {
            UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
            uint value = (data.renderingLayers.value & ~AllWorldBits) | mask;
            if (data.renderingLayers.value != value) data.renderingLayers = value;
        }

        // ---- the main light per camera ------------------------------------------------

        // URP takes RenderSettings.sun as the main light, else the brightest
        // directional light it sees; with two worlds loaded that was the site's for
        // everyone. Set before each camera renders, so nothing needs restoring.
        public static void PrepareCamera(Camera camera)
        {
            if (camera == null) return;
            WorldLook look = WorldAt(camera.transform.position);
            Light sun = look != null ? look.Sun : null;
            if (sun != null && RenderSettings.sun != sun) RenderSettings.sun = sun;
        }

        // ---- run time -----------------------------------------------------------------

        private static float nextMoversAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            RenderPipelineManager.beginContextRendering += OnBeginContext;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            nextMoversAt = 0f;
            looksFrame = -1;
        }

        private static void OnBeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!Application.isPlaying || Time.unscaledTime < nextMoversAt) return;
            nextMoversAt = Time.unscaledTime + MoverSeconds;
            StampMovers();
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!Application.isPlaying || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            if (Loaded().Count > 1) PrepareCamera(camera);
        }
    }
}
