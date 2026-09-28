using System;
using System.Collections.Generic;
using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.Sites;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // Targeted, repeatable setup for docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md: the
    // cabin water on the car prefab, the submersion indicator on the player
    // prefab (both patched in place, GUIDs kept), then the dive site regenerated
    // by its builder (the tube, the gate leaves, the water surface, the volume
    // top). Run twice: the prefabs report no change; the scene is rebuilt again.
    public static class ShaftTubeSetup
    {
        public const string CabinWaterSurfaceName = "Cabin Water";

        [MenuItem("Sunk Cost/Prototype/Apply shaft tube setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply(bool rebuildSite = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the shaft tube setup.");
            var changes = new List<string>();
            changes.AddRange(PatchCarPrefab());
            changes.AddRange(PatchPlayerPrefab());
            AssetDatabase.SaveAssets();
            if (rebuildSite)
            {
                DiveSiteBuilder.CreateOrUpdate();
                changes.Add("DiveSite01 rebuilt");
            }
            return changes.Count == 0 ? "Shaft tube already set up" : "Shaft tube: " + string.Join("; ", changes);
        }

        private static IEnumerable<string> PatchCarPrefab()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(ElevatorCabinBuilder.PrefabPath);
            try
            {
                DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
                string change = AddCabinWater(root, settings);
                if (change != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, ElevatorCabinBuilder.PrefabPath);
                    changes.Add(change);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }

        // Also called by ElevatorCabinBuilder while it generates the prefab, after the
        // car's look (ElevatorLook.PlaceCar/PlacePanel) so the nozzles, the drain ring and
        // the panel exist: the CabinWater component wired to the car's controller and its
        // disc (just inside the car's glass band, r = car r - 0.03 = 2.47, inactive until
        // the car is under), the "Cabin Water FX" (streams, splashes, bubbles, the drain
        // swirl) with CabinWaterVisuals, CarWaterSorting on the root, and a Car-mode
        // CabinPanelDisplay on "Panel Look" when the panel is there. Returns what changed,
        // or null when the prefab already had it all.
        public static string AddCabinWater(GameObject root, DiveSiteSettings settings)
        {
            var changes = new List<string>();
            ElevatorController controller = root.GetComponent<ElevatorController>();
            if (controller == null) throw new InvalidOperationException("Elevator prefab has no ElevatorController.");
            CabinWater water = root.GetComponent<CabinWater>();
            if (water == null) { water = root.AddComponent<CabinWater>(); changes.Add("CabinWater added"); }
            float carRadius = settings != null ? settings.CarDiameterMeters / 2f : 2.5f;
            float discRadius = carRadius - CabinWaterInsetMeters;
            Transform surface = root.transform.Find(CabinWaterSurfaceName);
            if (surface == null)
            {
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = CabinWaterSurfaceName;
                disc.transform.SetParent(root.transform, false);
                disc.transform.localPosition = Vector3.zero;
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.SetActive(false);
                surface = disc.transform;
                changes.Add("cabin water disc added");
            }
            Vector3 discScale = new(discRadius * 2f, 0.01f, discRadius * 2f);
            if (surface.localScale != discScale) { surface.localScale = discScale; changes.Add("cabin water disc r " + discRadius.ToString("0.00")); }
            Renderer discRenderer = surface.GetComponent<Renderer>();
            Material surfaceMaterial = CabinWaterArt.Surface();
            if (discRenderer.sharedMaterial != surfaceMaterial) { discRenderer.sharedMaterial = surfaceMaterial; changes.Add("cabin water surface material"); }
            discRenderer.shadowCastingMode = ShadowCastingMode.Off;
            SerializedObject serialized = new(water);
            if (serialized.FindProperty("controller").objectReferenceValue != controller) { serialized.FindProperty("controller").objectReferenceValue = controller; changes.Add("CabinWater controller wired"); }
            if (serialized.FindProperty("surface").objectReferenceValue != surface) { serialized.FindProperty("surface").objectReferenceValue = surface; changes.Add("CabinWater disc wired"); }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (root.transform.Find(CabinWaterFxName) == null || RebuildFx(root)) changes.Add(AddCabinWaterFx(root, water, discRenderer));
            CarWaterSorting sorting = root.GetComponent<CarWaterSorting>();
            if (sorting == null) { sorting = root.AddComponent<CarWaterSorting>(); changes.Add("CarWaterSorting added"); }
            sorting.Configure(controller);
            Transform panelLook = FindDeep(root.transform, PanelLookName);
            if (panelLook != null && panelLook.GetComponent<CabinPanelDisplay>() == null)
            {
                AddPanelDisplay(panelLook.gameObject, CabinPanelDisplay.Mode.Car, water, null);
                changes.Add("car panel display wired");
            }
            return changes.Count == 0 ? null : string.Join(", ", changes);
        }

        public const float CabinWaterInsetMeters = 0.03f;     // the disc stays inside the glass band (inner face 2.475)
        public const string CabinWaterFxName = "Cabin Water FX";
        public const string CarLookName = "Car Look";          // ElevatorLook.CarLookName (models role)
        public const string FloodOutletPrefix = "Flood Outlet ";
        public const string DrainRingName = "Drain Ring";
        public const string PanelLookName = "Panel Look";
        public const string WaterSurfaceRingName = "WaterSurface Ring";
        public const float TubeRingInnerRadius = 2.56f;        // clear of the car's glass band's outer face (2.525)

        // The nozzle mouths in the dive car's root frame (INTERFACES.md §1.1), used until
        // the car's look provides "Car Look/Flood Outlet n".
        private static readonly Vector3[] DefaultOutlets =
        {
            new(1.467f, 2.924f, -0.770f), new(1.487f, 2.908f, 0.738f), new(0.002f, 2.975f, -1.846f),
            new(0.064f, 2.975f, 1.846f), new(-1.470f, 3.051f, -0.928f), new(-1.439f, 3.050f, 0.948f)
        };

        // The FX already on a patched prefab is rebuilt when the car's look now carries
        // nozzles it was not built from.
        private static bool RebuildFx(GameObject root)
        {
            Transform fx = root.transform.Find(CabinWaterFxName);
            CabinWaterVisuals visuals = fx != null ? fx.GetComponent<CabinWaterVisuals>() : null;
            if (visuals == null || visuals.OutletCount != CabinWaterVisuals.NozzleCount) return true;
            Vector3[] wanted = Outlets(root, out _);
            for (int i = 0; i < wanted.Length; i++)
                if ((visuals.OutletLocal(i) - wanted[i]).sqrMagnitude > 1e-6f) return true;
            return false;
        }

        private static Vector3[] Outlets(GameObject root, out bool fromLook)
        {
            var outlets = new Vector3[CabinWaterVisuals.NozzleCount];
            Transform carLook = root.transform.Find(CarLookName);
            fromLook = true;
            for (int i = 0; i < outlets.Length; i++)
            {
                Transform outlet = carLook != null ? FindDeep(carLook, FloodOutletPrefix + (i + 1)) : null;
                if (outlet == null) { fromLook = false; outlets[i] = DefaultOutlets[i]; }
                else outlets[i] = root.transform.InverseTransformPoint(outlet.position);
            }
            return outlets;
        }

        // "Cabin Water FX" at the car root's identity: per nozzle a stream (two crossed
        // quads), a splash (a flat quad) and a bubble column (two crossed quads), and one
        // drain swirl; every renderer without shadows or colliders; everything inactive
        // until CabinWaterVisuals shows it. Rebuilt whole.
        private static string AddCabinWaterFx(GameObject root, CabinWater water, Renderer surface)
        {
            Vector3[] outlets = Outlets(root, out bool fromLook);
            Transform carLook = root.transform.Find(CarLookName);
            Transform drainRing = carLook != null ? FindDeep(carLook, DrainRingName) : null;
            Vector3 drain = drainRing != null ? root.transform.InverseTransformPoint(drainRing.position) : new Vector3(0f, ElevatorCabinBuilder.CarFloorThickness, 0f);

            Transform old = root.transform.Find(CabinWaterFxName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            GameObject fx = new(CabinWaterFxName);
            fx.transform.SetParent(root.transform, false);
            var streams = new Transform[outlets.Length];
            var splashes = new Transform[outlets.Length];
            var bubbles = new Transform[outlets.Length];
            for (int i = 0; i < outlets.Length; i++)
            {
                streams[i] = Crossed(fx.transform, "Stream " + (i + 1), CabinWaterArt.Stream());
                splashes[i] = Flat(fx.transform, "Splash " + (i + 1), CabinWaterArt.Splash());
                bubbles[i] = Crossed(fx.transform, "Bubbles " + (i + 1), CabinWaterArt.Bubbles());
            }
            Transform swirl = Flat(fx.transform, "Drain Swirl", CabinWaterArt.Swirl());
            CabinWaterVisuals visuals = fx.AddComponent<CabinWaterVisuals>();
            visuals.Configure(water, surface, outlets, drain, ElevatorCabinBuilder.CarFloorThickness, streams, splashes, bubbles, swirl);
            EditorUtility.SetDirty(visuals);
            return "cabin water FX built (nozzles from " + (fromLook ? "Car Look" : "the default numbers") + ")";
        }

        private static Transform Crossed(Transform parent, string name, Material material)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);
            for (int k = 0; k < 2; k++)
            {
                Transform quad = Quad(root.transform, k == 0 ? "A" : "B", material);
                quad.localRotation = Quaternion.Euler(0f, k * 90f, 0f);
            }
            root.SetActive(false);
            return root.transform;
        }

        private static Transform Flat(Transform parent, string name, Material material)
        {
            Transform quad = Quad(parent, name, material);
            quad.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.gameObject.SetActive(false);
            return quad;
        }

        private static Transform Quad(Transform parent, string name, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            Renderer renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return quad.transform;
        }

        // The tube's water (called by DiveSiteBuilder.CreateShaftTube): "WaterSurface", the
        // full disc at sea level (r = tube r - 0.03, no collider; DiveSiteValidator reads it),
        // "WaterSurface Ring", the same water outside the car's glass (r 2.56 to the disc's
        // edge, no collider), and TubeWaterSurface, which shows the ring instead of the disc
        // while the car's span is at the surface. Returns the disc.
        public static GameObject AddTubeWater(Transform shaftTube, Vector3 axisPosition, DiveSiteSettings settings, ElevatorController car)
        {
            float radius = settings.TubeRadiusMeters - 0.03f;
            Material material = DiveSiteBuilder.GetOrCreateWaterSurfaceMaterial();
            Transform existing = shaftTube.Find(DiveSiteBuilder.WaterSurfaceName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = DiveSiteBuilder.WaterSurfaceName;
            water.transform.SetParent(shaftTube, false);
            water.transform.position = new Vector3(axisPosition.x, settings.SeaLevelY, axisPosition.z);
            water.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
            water.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(water.GetComponent<Collider>());

            Transform oldRing = shaftTube.Find(WaterSurfaceRingName);
            if (oldRing != null) Object.DestroyImmediate(oldRing.gameObject);
            GameObject ring = new(WaterSurfaceRingName, typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(shaftTube, false);
            ring.transform.position = new Vector3(axisPosition.x, settings.SeaLevelY, axisPosition.z);
            ring.GetComponent<MeshFilter>().sharedMesh = CabinWaterArt.RingMesh(TubeRingInnerRadius, radius);
            MeshRenderer ringRenderer = ring.GetComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = material;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.enabled = false;
            ring.layer = water.layer;
            TubeWaterSurface surface = ring.AddComponent<TubeWaterSurface>();
            surface.Configure(car, water.GetComponent<Renderer>(), ringRenderer);
            EditorUtility.SetDirty(surface);
            return water;
        }

        // The panel's gauge and screen on a "Panel Look" (the car: Mode.Car with its water;
        // the deck: Mode.Deck mirroring the status plate). The screen's words are a TextMesh
        // "Screen Text" at the look's "Screen Anchor" (+Z out of the screen), fitted to the
        // screen by PlateText. Idempotent.
        public static CabinPanelDisplay AddPanelDisplay(GameObject panelLook, CabinPanelDisplay.Mode mode, CabinWater water, TextMesh mirror)
        {
            if (panelLook == null) return null;
            CabinPanelDisplay display = panelLook.GetComponent<CabinPanelDisplay>();
            if (display == null) display = panelLook.AddComponent<CabinPanelDisplay>();
            Transform anchor = FindDeep(panelLook.transform, CabinPanelDisplay.ScreenAnchorName);
            Transform textParent = anchor != null ? anchor : panelLook.transform;
            Transform text = textParent.Find(CabinPanelDisplay.ScreenTextName);
            if (text == null)
            {
                GameObject made = SunkCost.Editor.Look.PropBuilder.Text(textParent.gameObject, CabinPanelDisplay.ScreenTextName,
                    anchor != null ? new Vector3(0f, 0f, 0.004f) : new Vector3(0f, 0.55f, 0.12f), ScreenLineHeight, new Color(0.55f, 0.95f, 1f), TextAnchor.MiddleCenter);
                made.AddComponent<SunkCost.Look.PlateText>().Configure(ScreenTextBox, SunkCost.Look.PlateText.Mode.Fit, null);
                text = made.transform;
            }
            display.SetScreen(text.GetComponent<TextMesh>());
            display.Configure(mode, water, mirror);
            EditorUtility.SetDirty(display);
            return display;
        }

        public const float ScreenLineHeight = 0.045f;
        public static readonly Vector2 ScreenTextBox = new(0.26f, 0.16f);

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<string> PatchPlayerPrefab()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (root.GetComponent<PlayerSubmersion>() == null)
                {
                    root.AddComponent<PlayerSubmersion>();
                    PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
                    changes.Add("PlayerSubmersion added");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }
    }
}
