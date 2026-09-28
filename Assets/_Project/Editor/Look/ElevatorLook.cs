using SunkCost.Diving;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Look
{
    // Dan's round glass elevator (28 September 2026) laid over the game's working
    // objects: one place that puts the eight prepared models (ShipModelSetup) under
    // BOTH cabins the same way — the deck cabin on the ship and the dive car — so the
    // swap between them shows the same car in the same place. Look only: every
    // functional object, name, collider and pivot the builders make stays; this adds
    // look children, the post colliders the model's posts need (the player's camera
    // only knows colliders), and the named empties the water and the panel display
    // read. The numbers are the prepared models' own (scratchpad elev/ELEVATOR_MODELS.md,
    // measured on the exported meshes); bearings are the builders' (0 = +X, 90 = +Z,
    // counter-clockwise from above), and a Unity yaw of +a lowers a bearing by a.
    public static class ElevatorLook
    {
        public const string CarLookName = "Car Look";
        public const string LeafLookName = "Leaf Look";
        public const string PanelLookName = "Panel Look";
        public const string ShutterLookName = "Shutter Look";
        public const string GateLeafLookName = "Gate Leaf Look";
        public const string PostColliderPrefix = "Car Post ";      // "Car Post 1".."Car Post 8"
        public const string FloodOutletPrefix = "Flood Outlet ";   // "Flood Outlet 1".."6", empties under Car Look
        public const string DrainRingName = "Drain Ring";          // empty under Car Look at (0, 0.10, 0)
        public const string GaugeBottomName = "Gauge Bottom";      // empties under Panel Look (y along the gauge)
        public const string GaugeTopName = "Gauge Top";
        public const string GaugeMarkerName = "Gauge Marker";      // a small renderer the display moves
        public const string GaugeFillName = "Gauge Fill";          // optional renderer the display scales in y
        public const string ScreenAnchorName = "Screen Anchor";    // at the screen face centre, +Z out of the screen
        public const string PanelPipeName = "Panel Pipe";
        public const float FloorTopAboveRoot = 0.10f;
        public const float DrainInnerRadius = 1.80f, DrainOuterRadius = 2.05f;
        public const int FloodOutletCount = 6;
        public const float ShutterRadius = 3.075f, ShutterSpanDeg = 24.76f;

        public const string CarGlassPath = ShipModelSetup.TextureFolder + "/ElevatorCarGlass.mat";
        public const string CarLightPath = ShipModelSetup.TextureFolder + "/ElevatorCarLight.mat";
        public const string ShutterPath = ShipModelSetup.TextureFolder + "/ElevatorShutter.mat";
        public const string ScreenPath = ShipModelSetup.TextureFolder + "/CarPanelScreen.mat";
        public const string GaugePath = ShipModelSetup.TextureFolder + "/CarPanelGauge.mat";

        // The model's doorway faces its prefab's +Z (bearing 90).
        private const float ModelDoorwayBearing = 90f;
        private static float YawFor(float bearing) => ModelDoorwayBearing - bearing;

        // The six roof outlets (the lowest points of the downturned pipes), Car Look local.
        private static readonly Vector3[] FloodOutlets =
        {
            new(0.770f, 2.924f, 1.467f), new(-0.738f, 2.908f, 1.487f), new(1.846f, 2.975f, 0.002f),
            new(-1.846f, 2.975f, 0.064f), new(0.928f, 3.051f, -1.470f), new(-0.948f, 3.050f, -1.439f),
        };

        // The car model's posts and its two dark glass slabs, measured on the prepared
        // mesh: bearing from the doorway (counter-clockwise), the inner face's radius,
        // and for a slab its half width. Capsules on the posts, thin boxes on the slabs,
        // their inner faces on the model's, from the floor top to 3.10.
        private readonly struct Post
        {
            public readonly float Rel, Inner, SlabHalf;
            public Post(float rel, float inner, float slabHalf = 0f) { Rel = rel; Inner = inner; SlabHalf = slabHalf; }
        }

        private static readonly Post[] Posts =
        {
            new(23.84f, 2.082f), new(-23.69f, 2.086f), new(-57.07f, 2.203f, 0.22f), new(54.70f, 2.203f, 0.22f),
            new(98.55f, 2.122f), new(149.85f, 2.043f), new(-150.63f, 2.048f), new(-99.65f, 2.124f),
        };
        private const float PostRadius = 0.095f, PostHeight = 3.0f, SlabThickness = 0.05f;

        // ---- the car ----------------------------------------------------------------

        public static GameObject PlaceCar(Transform parent, float doorwayBearingDeg, Light ringSource)
        {
            GameObject look = Instance("ElevatorCar", parent, CarLookName, Vector3.zero, YawFor(doorwayBearingDeg));
            if (look == null) return null;
            for (int i = 0; i < FloodOutlets.Length; i++) Empty(look.transform, FloodOutletPrefix + (i + 1), FloodOutlets[i], Quaternion.identity);
            Empty(look.transform, DrainRingName, new Vector3(0f, FloorTopAboveRoot, 0f), Quaternion.identity);
            SetRingSource(look, ringSource);
            return look;
        }

        public static void SetRingSource(GameObject carLook, Light source)
        {
            if (carLook == null) return;
            CarRingLight ring = carLook.GetComponent<CarRingLight>();
            if (ring == null) ring = carLook.AddComponent<CarRingLight>();
            ring.Configure(source);
            EditorUtility.SetDirty(ring);
        }

        // ---- the car doors ----------------------------------------------------------

        public static GameObject PlaceDoorLeaf(Transform pivot, float doorwayBearingDeg, bool right)
        {
            float centre = doorwayBearingDeg + (right ? 19.5f : -19.5f);
            return Instance("CabinDoor", pivot, LeafLookName, new Vector3(0f, FloorTopAboveRoot, 0f), YawFor(centre));
        }

        // ---- the panel --------------------------------------------------------------

        // The panel model's parts (ShipModelSetup: CarPanel's FBX objects).
        private const string PanelCap = "CarPanel_Cap", PanelRim = "CarPanel_Rim";
        // Panel Look local: the cap face, the gauge tube's front, the screen face's centre.
        private static readonly Vector3 GaugeBottomLocal = new(-0.2466f, 0.328f, 0.090f);
        private static readonly Vector3 GaugeTopLocal = new(-0.2466f, 0.5505f, 0.090f);
        private static readonly Vector3 ScreenCentreLocal = new(0.0397f, 0.443f, 0.1402f);
        private static readonly Vector3 ScreenNormalLocal = new(0.0812f, 0f, 0.9967f);
        private static readonly Vector3 PipeBaseLocal = new(0f, 0.936f, 0f);
        private const float PanelLookRadius = 2.375f, PanelLookY = 1.113f, ButtonRadius = 2.197f, ButtonY = 1.300f, PipeTopY = 3.30f;

        public static GameObject PlacePanel(Transform lookParent, Transform buttonRoot, float panelBearingDeg)
        {
            float b = panelBearingDeg * Mathf.Deg2Rad;
            Vector3 dir = new(Mathf.Cos(b), 0f, Mathf.Sin(b));
            GameObject look = Instance("CarPanel", lookParent, PanelLookName, dir * PanelLookRadius + Vector3.up * PanelLookY, 270f - panelBearingDeg);
            if (look == null) return null;

            // The body: solid, not a press target (no marker), behind the button's own box.
            BoxCollider body = look.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.339f, 0.046f);
            body.size = new Vector3(0.70f, 0.678f, 0.21f);

            // The pipe from the panel's top up into the car's top ring beam (hidden there).
            float pipeHeight = PipeTopY - (PanelLookY + PipeBaseLocal.y);
            GameObject pipe = new(PanelPipeName);
            pipe.transform.SetParent(look.transform, false);
            pipe.transform.localPosition = PipeBaseLocal;
            pipe.AddComponent<MeshFilter>().sharedMesh = MeshKit.Cylinder(0.040f, pipeHeight, 12);
            pipe.AddComponent<MeshRenderer>().sharedMaterial = ShipKitMaterials.Bezel();

            // The gauge: its ends, a marker the display moves and a fill it scales.
            Empty(look.transform, GaugeBottomName, GaugeBottomLocal, Quaternion.identity);
            Empty(look.transform, GaugeTopName, GaugeTopLocal, Quaternion.identity);
            GameObject fill = new(GaugeFillName);
            fill.transform.SetParent(look.transform, false);
            fill.transform.localPosition = GaugeBottomLocal - new Vector3(0f, 0f, 0.004f);
            fill.transform.localScale = new Vector3(1f, 0f, 1f);
            fill.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.030f, GaugeTopLocal.y - GaugeBottomLocal.y, 0.006f));
            fill.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.ShipFlat(SunkCost.Look.ScreenStyle.Accent);
            GameObject marker = new(GaugeMarkerName);
            marker.transform.SetParent(look.transform, false);
            marker.transform.localPosition = GaugeBottomLocal;
            marker.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.070f, 0.010f, 0.012f), 0.005f);
            marker.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.ShipFlat(SunkCost.Look.ScreenStyle.Text);

            // The screen's face centre, +Z out of the screen.
            Empty(look.transform, ScreenAnchorName, ScreenCentreLocal, Quaternion.LookRotation(ScreenNormalLocal, Vector3.up));

            if (buttonRoot != null) FitButton(look, buttonRoot, dir);
            return look;
        }

        // The game's button moves onto the model's cap: its root at the cap face facing
        // the car's axis, the model's cap and rim as its "Cap" and "Cap Edge" (the names
        // ButtonLook presses and lights), its "Text" kept on the cap, its box on the cap.
        private static void FitButton(GameObject look, Transform buttonRoot, Vector3 dir)
        {
            buttonRoot.localPosition = dir * ButtonRadius + Vector3.up * ButtonY;
            buttonRoot.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
            if (buttonRoot.parent != look.transform.parent)
            {
                // The button lives beside the look under another parent: place it by world pose.
                Transform lp = look.transform.parent;
                buttonRoot.position = lp.TransformPoint(dir * ButtonRadius + Vector3.up * ButtonY);
                buttonRoot.rotation = lp.rotation * Quaternion.LookRotation(-dir, Vector3.up);
            }
            BoxCollider press = buttonRoot.GetComponent<BoxCollider>();
            if (press != null) { press.center = new Vector3(0f, 0f, -0.02f); press.size = new Vector3(0.21f, 0.21f, 0.08f); }

            MeshRenderer cap = FindRenderer(look.transform, PanelCap);
            MeshRenderer rim = FindRenderer(look.transform, PanelRim);
            if (cap == null || rim == null)
            {
                Debug.LogWarning("ElevatorLook: the CarPanel prefab has no " + PanelCap + "/" + PanelRim + " yet; the button keeps its own cap");
                return;
            }
            foreach (string old in new[] { "Bezel", SunkCost.World.ButtonLook.CapName, SunkCost.World.ButtonLook.RimName })
            {
                Transform t = buttonRoot.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            SunkCost.World.ButtonLook buttonLook = buttonRoot.GetComponent<SunkCost.World.ButtonLook>();
            SunkCost.World.ButtonLook.Role role = buttonLook != null ? buttonLook.Kind : SunkCost.World.ButtonLook.Role.Destination;
            CopyRenderer(cap, buttonRoot, SunkCost.World.ButtonLook.CapName, role == SunkCost.World.ButtonLook.Role.Danger ? LookMaterials.ButtonRed() : LookMaterials.ButtonAccent());
            GameObject edge = CopyRenderer(rim, buttonRoot, SunkCost.World.ButtonLook.RimName, LookMaterials.ShipFlat(Color.Lerp(SunkCost.Look.ScreenStyle.Track, SunkCost.World.ButtonLook.RimColour(role), 0.45f)));
            edge.transform.SetAsLastSibling();
            cap.enabled = false;
            rim.enabled = false;

            // The word rides on the cap (the screen carries it big).
            Transform text = buttonRoot.Find(SunkCost.World.ButtonLook.TextName);
            if (text != null)
            {
                text.localPosition = new Vector3(0f, 0f, 0.006f);
                SunkCost.Look.SignText sign = text.GetComponent<SunkCost.Look.SignText>();
                if (sign != null) sign.Fit(0.085f, 0.035f);
            }
        }

        private static GameObject CopyRenderer(MeshRenderer source, Transform parent, string name, Material material)
        {
            GameObject copy = new(name);
            copy.transform.SetParent(parent, false);
            copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            copy.transform.localScale = Vector3.one;
            copy.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer r = copy.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, source.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return copy;
        }

        private static MeshRenderer FindRenderer(Transform root, string name)
        {
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == name) return r;
            return null;
        }

        // ---- the posts --------------------------------------------------------------

        public static void AddPostColliders(Transform cabinRoot, float doorwayBearingDeg)
        {
            for (int i = 0; i < Posts.Length; i++)
            {
                Post p = Posts[i];
                string name = PostColliderPrefix + (i + 1);
                Transform old = cabinRoot.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                float a = (doorwayBearingDeg + p.Rel) * Mathf.Deg2Rad;
                Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                GameObject go = new(name);
                go.transform.SetParent(cabinRoot, false);
                float centreY = FloorTopAboveRoot + PostHeight / 2f;
                if (p.SlabHalf > 0f)
                {
                    go.transform.localPosition = dir * (p.Inner + SlabThickness / 2f) + Vector3.up * centreY;
                    go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                    go.AddComponent<BoxCollider>().size = new Vector3(p.SlabHalf * 2f, PostHeight, SlabThickness);
                }
                else
                {
                    go.transform.localPosition = dir * (p.Inner + PostRadius) + Vector3.up * centreY;
                    CapsuleCollider c = go.AddComponent<CapsuleCollider>();
                    c.direction = 1;
                    c.radius = PostRadius;
                    c.height = PostHeight;
                }
            }
        }

        // ---- the deck housing's shutters --------------------------------------------

        // The orange plate patch of the baked housing map (UV rect), re-found after
        // every housing bake (prepare_ship_part.py prints it); flat colour there.
        private static readonly Rect ShutterAtlasRect = new(0.2224f, 0.2566f, 0.0040f, 0.0040f);
        private static readonly Color ShutterFallback = new Color32(172, 86, 24, 255);

        public static GameObject BuildShutterLeaf(Transform pivot, float doorwayBearingDeg, bool right, float radius, float bottomY, float topY)
        {
            float from = right ? doorwayBearingDeg : doorwayBearingDeg - ShutterSpanDeg;
            float to = right ? doorwayBearingDeg + ShutterSpanDeg : doorwayBearingDeg;
            GameObject leaf = new(ShutterLookName);
            leaf.transform.SetParent(pivot, false);
            leaf.transform.localPosition = new Vector3(0f, bottomY, 0f);
            leaf.AddComponent<MeshFilter>().sharedMesh = MeshKit.Band(radius, topY - bottomY, 0.05f, from, to, 24);
            MeshRenderer r = leaf.AddComponent<MeshRenderer>();
            r.sharedMaterial = ShutterMaterial();
            // Framed like the housing's own painted panels: dark steel rails top and
            // bottom, a seam at mid height, a stile on the leading (doorway) edge,
            // standing a little proud on the outside.
            float h = topY - bottomY, lead = right ? from : to - ShutterStileDeg;
            Material steel = ShipKitMaterials.Steel();
            FrameBand(leaf.transform, "Rail Bottom", radius, 0f, ShutterRailHeight, from, to, steel);
            FrameBand(leaf.transform, "Rail Top", radius, h - ShutterRailHeight, ShutterRailHeight, from, to, steel);
            FrameBand(leaf.transform, "Seam", radius, h * 0.5f - ShutterSeamHeight / 2f, ShutterSeamHeight, from, to, steel);
            FrameBand(leaf.transform, "Stile", radius, 0f, h, lead, lead + ShutterStileDeg, steel);
            return leaf;
        }

        private const float ShutterRailHeight = 0.14f, ShutterSeamHeight = 0.06f, ShutterStileDeg = 1.9f, ShutterFrameProud = 0.03f;

        private static void FrameBand(Transform leaf, string name, float radius, float y, float height, float from, float to, Material material)
        {
            GameObject band = new(name);
            band.transform.SetParent(leaf, false);
            band.transform.localPosition = new Vector3(0f, y, 0f);
            band.AddComponent<MeshFilter>().sharedMesh = MeshKit.Band(radius + (0.05f + ShutterFrameProud) / 2f, height, ShutterFrameProud + 0.01f, from, to, 24);
            band.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        public static Material ShutterMaterial()
        {
            Material m = LoadOrCreate(ShutterPath, "Universal Render Pipeline/Lit");
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(ShipModelSetup.ModelRoot + "/CabinHousing/Maps/CabinHousing_BaseColor.jpg");
            // MeshKit.Band's UVs are in metres (a 3 m leaf): squeeze them into the patch.
            float span = 3.2f;
            if (atlas != null)
            {
                m.SetTexture("_BaseMap", atlas);
                m.SetTextureScale("_BaseMap", new Vector2(ShutterAtlasRect.width / span, ShutterAtlasRect.height / span));
                m.SetTextureOffset("_BaseMap", ShutterAtlasRect.position);
                m.SetColor("_BaseColor", Color.white);
            }
            else
            {
                m.SetTexture("_BaseMap", null);
                m.SetColor("_BaseColor", ShutterFallback);
            }
            // Grime in metres, as the hull and tower wear it.
            Texture2D detail = ShipModelSetup.Import(ShipModelSetup.TextureFolder + "/HullDetail.png", TextureImporterType.Default, false);
            Texture2D detailNormal = ShipModelSetup.Import(ShipModelSetup.TextureFolder + "/HullDetail_Normal.png", TextureImporterType.NormalMap, false);
            m.SetTexture("_DetailAlbedoMap", detail);
            m.SetTexture("_DetailNormalMap", detailNormal);
            m.SetTextureScale("_DetailAlbedoMap", Vector2.one * 0.5f);
            m.SetFloat("_DetailAlbedoMapScale", 1f);
            m.SetFloat("_DetailNormalMapScale", 0.6f);
            if (detail != null) m.EnableKeyword("_DETAIL_MULX2"); else m.DisableKeyword("_DETAIL_MULX2");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---- the shaft gate ---------------------------------------------------------

        public static GameObject PlaceGateLeaf(Transform pivot, float doorwayBearingDeg, bool right, float thresholdLocalY)
        {
            GameObject look = Instance("GateLeaf", pivot, GateLeafLookName, new Vector3(0f, thresholdLocalY, 0f), YawFor(doorwayBearingDeg));
            if (look == null) return null;
            PrefabUtility.UnpackPrefabInstance(look, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            string drop = right ? "GateLeaf_L" : "GateLeaf";
            foreach (Transform t in look.GetComponentsInChildren<Transform>(true))
                if (t != look.transform && t.name == drop && t.GetComponent<Renderer>() != null) { Object.DestroyImmediate(t.gameObject); break; }
            return look;
        }

        // ---- static parts -----------------------------------------------------------

        public static GameObject PlacePart(string part, Transform parent, string name, Vector3 localPosition, float yawDeg)
            => Instance(part, parent, name, localPosition, yawDeg);

        // ---- materials --------------------------------------------------------------

        // Clear, two-sided glass for the car's slabs and the door leaves' windows: seen
        // from inside and outside the car. Transparent queue, no depth write.
        public static Material CarGlass()
        {
            Material m = LoadOrCreate(CarGlassPath, "Universal Render Pipeline/Lit");
            MakeTransparent(m, new Color(0.62f, 0.86f, 0.92f, 0.20f), 0.92f, twoSided: true);
            return m;
        }

        // The car's light ring: the baked car map, glowing; CarRingLight sets the colour.
        public static Material CarLight()
        {
            Material m = LoadOrCreate(CarLightPath, "Universal Render Pipeline/Lit");
            Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ShipModelSetup.ModelRoot + "/ElevatorCar/Maps/ElevatorCar_BaseColor.jpg");
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_EmissionMap", baseMap);
            m.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.85f) * 2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.4f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // The panel's screen: dark glass the game writes on (CabinPanelDisplay).
        public static Material PanelScreen()
        {
            Material m = LoadOrCreate(ScreenPath, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", SunkCost.Look.ScreenStyle.Back);
            EditorUtility.SetDirty(m);
            return m;
        }

        // The gauge's tube: dark, the fill and the marker in front of it show the level.
        public static Material PanelGauge()
        {
            Material m = LoadOrCreate(GaugePath, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", SunkCost.Look.ScreenStyle.Track);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void MakeTransparent(Material m, Color colour, float smoothness, bool twoSided)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            m.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", colour);
            m.SetTexture("_BaseMap", null);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
        }

        private static Material LoadOrCreate(string path, string shaderName)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException(shaderName + " not found");
            if (m == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                m = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ---- helpers ----------------------------------------------------------------

        private static GameObject Instance(string part, Transform parent, string name, Vector3 localPosition, float yawDeg)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelSetup.PrefabPath(part));
            if (prefab == null)
            {
                Debug.LogWarning("ElevatorLook: no prefab " + ShipModelSetup.PrefabPath(part) + " (run ShipModelSetup.Apply(\"" + part + "\"))");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            go.transform.localScale = Vector3.one;
            return go;
        }

        private static GameObject Empty(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            return go;
        }
    }
}
