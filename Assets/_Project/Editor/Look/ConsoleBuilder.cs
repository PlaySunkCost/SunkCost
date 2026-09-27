using System.IO;
using System.Text;
using SunkCost.Look;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Editor.Look
{
    // The shared console's rig from the NavConsole model (Dan, 27 September 2026;
    // INTERFACES §10): one call places the look prefab under a rig root, gives the
    // body its mesh collider, lays the three painted surfaces on the measured glass
    // (the model inspector's rectangles, MODEL.md §5, in rig space at the prepare
    // table's scale), hangs the lever's handle on a hinge at the drum's axis, builds
    // the controls the player aims at with their ConsoleControls (five cards and the
    // lever on the ship; the GIVE UP card and the lever at HQ) and leaves the idle
    // screens painted. The composer (ShipNavigationConsole / HQQuotaConsole) is the
    // caller's to add. The assets every rig shares - the glass material, the touch
    // material, the style and the site catalogue in Resources, the placeholder
    // picture's import - are made or refreshed by EnsureAssets.
    public static class ConsoleBuilder
    {
        public const string ModelPart = "NavConsole";             // ShipModelSetup.Parts entry; Prefabs/Ship/NavConsole.prefab; Models/Ship/NavConsole/NavConsole.fbx
        public const float Scale = 1f;                            // real-world metres (BRIEF); adapt in the prepare table, not here
        public const string MaterialFolder = "Assets/_Project/Materials/Console";
        public const string GlassPath = MaterialFolder + "/ConsoleGlass.mat";
        public const string TouchPath = MaterialFolder + "/ConsoleTouch.mat";
        public const string StylePath = "Assets/_Project/Resources/" + ConsoleStyle.ResourceName + ".asset";
        public const string CatalogPath = "Assets/_Project/Resources/" + SiteCatalog.ResourceName + ".asset";
        public const string PlaceholderPath = "Assets/_Project/Textures/Console/SitePlaceholder.png";
        public const string ShaderPath = "Assets/_Project/Shaders/ScreenPaint.shader";

        // The measured surfaces in rig space (yaw 0, the root on the floor, front = +Z,
        // the lever housing at negative x), at the prepare table's 2.38 x 1.20 x 2.17 m.
        // Each overlay sits a few millimetres proud of its (flattened) glass, inset 2 cm
        // from the glass's edge so it stays inside the bezel. Scale these with the
        // TABLE entry if the console's size changes.
        // The top and desk overlays run to the glass's full extent plus a centimetre
        // (the bezels stand in front of the overlay planes, so the extra is hidden under
        // them): an inset overlay left the model's own painted lock icons showing
        // under it (proof capture, 27 Sep 14:42). The top overlay stands 26 mm proud of
        // the flattened glass (z -0.258) and 11 mm behind the bezel (z -0.221): the
        // model's embossed lock icons reach z -0.238 and fought a plane at -0.239.
        public static readonly Vector3 TopCentre = new(0.049f, 1.550f, -0.232f);
        public static readonly Vector2 TopSize = new(1.60f, 0.85f);
        public static readonly Vector3 BottomCentre = new(0.212f, 0.700f, 0.269f);
        public static readonly Vector3 BottomNormal = new(0f, 0.7135f, 0.7007f);
        public static readonly Vector2 BottomSize = new(1.35f, 0.65f);
        public static readonly Vector3 SignCentre = new(-0.771f, 0.621f, 0.454f);
        public static readonly Vector3 SignNormal = new(0f, 0.6894f, 0.7244f);
        public static readonly Vector2 SignSize = new(0.25f, 0.11f);
        // The drum's axis the lever turns about (the FBX's NavConsole_Lever origin).
        public static readonly Vector3 LeverPivot = new(-0.780f, 0.786f, 0.103f);
        // The grip's travel from the hinge: the bar at rest (0, 0.163, 0.157), 30 degrees
        // down (0, 0.063, 0.217), radius 0.053, the red 0.40 m long - one static box.
        public static readonly Vector3 LeverBoxCentre = new(0f, 0.115f, 0.19f);
        public static readonly Vector3 LeverBoxSize = new(0.44f, 0.26f, 0.24f);
        public const float ControlProud = 0.01f, ControlDepth = 0.02f;

        // Instantiates the look prefab under a new rig root named ShipRootName/HQRootName
        // at localPosition/yaw (front = +Z of the root), adds a MeshCollider on the body
        // (walk against it, the aim reaches the controls), the three surfaces, the lever
        // handle + hinge, the control colliders with their ConsoleControls, the
        // ConsoleLever, and returns the ConsoleRig. Does NOT add the composer.
        public static ConsoleRig Place(Transform parent, Vector3 localPosition, float yaw, ConsoleKind kind)
        {
            EnsureAssets();
            string prefabPath = ShipModelSetup.PrefabPath(ModelPart);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.Log("Console: " + ShipModelSetup.Apply(ModelPart));
                AssetDatabase.SaveAssets();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            if (prefab == null) throw new System.InvalidOperationException("Missing art: " + prefabPath + " (prepare Models/Ship/NavConsole/NavConsole.fbx, then ShipModelSetup.Apply(\"NavConsole\"))");
            ConsoleStyle style = ConsoleStyle.Resolve();
            Material glass = GlassMaterial(), touch = TouchMaterial();

            var root = new GameObject(kind == ConsoleKind.Ship ? ConsoleRig.ShipRootName : ConsoleRig.HQRootName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = Vector3.one * Scale;

            // The model, unpacked: its lever leaves the instance for the hinge.
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = ConsoleRig.BodyName;
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            Transform handle = null;
            MeshFilter bodyMesh = null;
            foreach (MeshFilter mf in body.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.name.Contains("Lever")) handle = mf.transform;
                else if (bodyMesh == null || mf.sharedMesh.vertexCount > bodyMesh.sharedMesh.vertexCount) bodyMesh = mf;
            }
            if (bodyMesh != null && bodyMesh.GetComponent<Collider>() == null) bodyMesh.gameObject.AddComponent<MeshCollider>().sharedMesh = bodyMesh.sharedMesh;
            else if (bodyMesh == null) Debug.LogWarning("Console: the model has no body mesh");

            var hinge = new GameObject(ConsoleRig.LeverHingeName).transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = handle != null ? root.transform.InverseTransformPoint(handle.position) : LeverPivot;
            hinge.localRotation = Quaternion.identity;
            if (handle != null)
            {
                if (Vector3.Distance(hinge.localPosition, LeverPivot) > 0.02f)
                    Debug.LogWarning($"Console: the lever's pivot sits at {hinge.localPosition:F3}, expected {LeverPivot:F3}; the swing turns about the model's own point");
                handle.SetParent(hinge, true);
                handle.name = ConsoleRig.LeverHandleName;
            }
            else Debug.LogWarning("Console: the model has no separate lever (NavConsole_Lever); the lever will not move");

            ConsoleScreen top = Surface(root.transform, ConsoleRig.TopScreenName, ConsolePaint.TopSurface, TopCentre, Vector3.forward, TopSize, glass);
            ConsoleScreen bottom = Surface(root.transform, ConsoleRig.BottomScreenName, ConsolePaint.BottomSurface, BottomCentre, BottomNormal, BottomSize, glass);
            ConsoleScreen sign = Surface(root.transform, ConsoleRig.SignName, ConsolePaint.SignSurface, SignCentre, SignNormal, SignSize, glass);

            Collider[] cards = null;
            Collider giveUp = null;
            if (kind == ConsoleKind.Ship)
            {
                Rect[] rects = ConsolePaint.CardRects(top.Width, top.Height, style);
                cards = new Collider[rects.Length];
                for (int i = 0; i < rects.Length && i < Destinations.Cards.Length; i++)
                    cards[i] = Control(top, ConsoleRig.CardNamePrefix + Destinations.Cards[i], rects[i], kind, ConsoleControlKind.Card, Destinations.Cards[i], touch);
            }
            else giveUp = Control(bottom, ConsoleRig.GiveUpCardName, ConsolePaint.GiveUpRect(bottom.Width, bottom.Height, style), kind, ConsoleControlKind.GiveUpCard, SiteId.None, touch);

            // The lever's control: a static box around the grip's travel on the rig, not
            // on the moving handle, so a half-pulled lever is still the target.
            var leverGo = new GameObject(kind == ConsoleKind.Ship ? ConsoleRig.ShipLeverName : ConsoleRig.HQLeverName);
            leverGo.transform.SetParent(root.transform, false);
            leverGo.transform.localPosition = hinge.localPosition + LeverBoxCentre;
            leverGo.transform.localRotation = Quaternion.identity;
            BoxCollider leverBox = leverGo.AddComponent<BoxCollider>();
            leverBox.center = Vector3.zero;
            leverBox.size = LeverBoxSize;
            leverGo.AddComponent<ConsoleControl>().Configure(kind, ConsoleControlKind.Lever, SiteId.None);
            Touch(leverGo.transform, Quaternion.LookRotation(-SignNormal, Vector3.ProjectOnPlane(Vector3.up, SignNormal).normalized), new Vector2(LeverBoxSize.x, 0.28f), touch);

            ConsoleRig rig = root.AddComponent<ConsoleRig>();
            rig.Configure(kind, top, bottom, sign, hinge, handle, leverBox, giveUp, cards);
            root.AddComponent<ConsoleLever>();
            rig.ShowIdle();
            return rig;
        }

        // A painted surface root on the measured plane: +Z toward the reader, +Y up the glass.
        private static ConsoleScreen Surface(Transform root, string name, string surface, Vector3 centre, Vector3 normal, Vector2 size, Material glass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = centre;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal.normalized).normalized;
            if (up.sqrMagnitude < 0.5f) up = Vector3.back;
            go.transform.localRotation = Quaternion.LookRotation(normal.normalized, up);
            ConsoleScreen screen = go.AddComponent<ConsoleScreen>();
            screen.Configure(surface, size, glass);
            return screen;
        }

        // A control on a surface: its collider where its picture is (the same layout the
        // painter uses), its transform at the collider's centre (the hooks aim there).
        private static Collider Control(ConsoleScreen screen, string name, Rect px, ConsoleKind kind, ConsoleControlKind role, SiteId payload, Material touch)
        {
            Vector2 centre = screen.LocalCentre(px), size = screen.LocalSize(px);
            var go = new GameObject(name);
            go.transform.SetParent(screen.transform, false);
            go.transform.localPosition = new Vector3(centre.x, centre.y, ControlProud);
            go.transform.localRotation = Quaternion.identity;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(size.x, size.y, ControlDepth);
            box.isTrigger = false;
            go.AddComponent<ConsoleControl>().Configure(kind, role, payload);
            Touch(go.transform, Quaternion.Euler(0f, 180f, 0f), size, touch);
            return box;
        }

        // An invisible quad the size of the control: InteractHighlight outlines only
        // MeshRenderers, and gives a Quad the panel-edge frame.
        private static void Touch(Transform control, Quaternion localRotation, Vector2 size, Material touch)
        {
            var go = new GameObject("Touch");
            go.transform.SetParent(control, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = localRotation;
            go.transform.localScale = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), 1f);
            go.AddComponent<MeshFilter>().sharedMesh = ConsoleScreen.Quad();
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = touch;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---- the shared assets ---------------------------------------------------------

        public static string EnsureAssets()
        {
            Folders(MaterialFolder);
            GlassMaterial();
            TouchMaterial();
            Texture2D placeholder = ImportPlaceholder();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            ConsoleStyle style = AssetDatabase.LoadAssetAtPath<ConsoleStyle>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<ConsoleStyle>();
                style.PaintShader = shader;
                AssetDatabase.CreateAsset(style, StylePath);
            }
            else if (style.PaintShader == null && shader != null) { style.PaintShader = shader; EditorUtility.SetDirty(style); }
            SiteCatalog catalog = AssetDatabase.LoadAssetAtPath<SiteCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SiteCatalog>();
                catalog.EnsureDefaults();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else if (catalog.EnsureDefaults()) EditorUtility.SetDirty(catalog);
            if (catalog.SharedPicture == null && placeholder != null)
            {
                var so = new SerializedObject(catalog);
                so.FindProperty("sharedPicture").objectReferenceValue = placeholder;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            return $"glass {GlassPath}; touch {TouchPath}; style {StylePath} shader={(style.PaintShader != null ? style.PaintShader.name : "none")}; catalog {CatalogPath} entries={catalog.Entries.Count} picture={(catalog.SharedPicture != null ? catalog.SharedPicture.name : "none")}; placeholder {(placeholder != null ? placeholder.width + "x" + placeholder.height : "missing")}";
        }

        // The glass every surface wears: URP Lit, a base darker than the paint's black so
        // the paint sets the black level, a faint reflection, the emission map and colour
        // left to each surface's MaterialPropertyBlock (an unpainted glass stays dark).
        public static Material GlassMaterial()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new System.InvalidOperationException("URP Lit shader not found.");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            if (m == null)
            {
                Folders(MaterialFolder);
                m = new Material(lit) { name = "ConsoleGlass" };
                AssetDatabase.CreateAsset(m, GlassPath);
            }
            if (m.shader != lit) m.shader = lit;
            m.SetColor("_BaseColor", new Color(0.010f, 0.015f, 0.020f, 1f));
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.5f);
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Back);
            Opaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // The controls' invisible quads: URP Unlit, transparent, alpha zero.
        public static Material TouchMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) throw new System.InvalidOperationException("URP Unlit shader not found.");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(TouchPath);
            if (m == null)
            {
                Folders(MaterialFolder);
                m = new Material(unlit) { name = "ConsoleTouch" };
                AssetDatabase.CreateAsset(m, TouchPath);
            }
            if (m.shader != unlit) m.shader = unlit;
            m.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            m.SetTexture("_BaseMap", null);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void Opaque(Material m)
        {
            m.SetFloat("_Surface", 0f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = -1;
        }

        // The site picture (a silhouette on transparent): sRGB, its alpha transparency,
        // clamped, mipmapped for the two sizes it is drawn at.
        private static Texture2D ImportPlaceholder()
        {
            if (AssetImporter.GetAtPath(PlaceholderPath) is not TextureImporter ti) return AssetDatabase.LoadAssetAtPath<Texture2D>(PlaceholderPath);
            bool changed = false;
            if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
            if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
            if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; changed = true; }
            if (ti.maxTextureSize != 512) { ti.maxTextureSize = 512; changed = true; }
            if (changed) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PlaceholderPath);
        }

        private static void Folders(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            Folders(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---- the proof (Foundation): both rigs in a temporary object, sample screens, captures ----

        public static string Prove()
        {
            Directory.CreateDirectory(LookCapture.Folder);
            var temp = new GameObject("Console Proof (temporary)");
            var sb = new StringBuilder();
            try
            {
                Vector3 at = new(0f, 40f, -30f); // in the open scene's air, under its own sky and post-processing
                ConsoleRig ship = Place(temp.transform, at, 0f, ConsoleKind.Ship);
                ConsoleRig hq = Place(temp.transform, at + new Vector3(6f, 0f, 0f), 0f, ConsoleKind.HQ);
                sb.Append(Describe(ship)).Append('\n').Append(Describe(hq)).Append('\n');
                SiteCatalog sites = SiteCatalog.Resolve();
                HQSigns signs = HQSigns.Resolve();

                // The ship, an open site selected and ready.
                ConsoleRig.Idle(ConsoleKind.Ship, out TopModel top, out BottomModel bottom, out SignModel sign);
                top.Corner = "DAY 2/3";
                top.Cards[1].Selected = true;
                top.Cards[2].Aimed = true;
                top.FootLeft = "QUOTA $120 / $500"; top.FootLeftTone = ConsoleTone.Warn;
                bottom = new BottomModel
                {
                    Heading = signs.Get("nav.selected"), Picture = sites.PictureOf(SiteId.Site01), Name = "SITE 01", SubName = string.Empty,
                    Lines = new[] { new ConsoleLine("DAY 2 OF 3", ConsoleTextSize.Medium, ConsoleTone.Text), new ConsoleLine("QUOTA $120 / $500", ConsoleTextSize.Medium, ConsoleTone.Warn) },
                    Status = new ConsoleLine("READY", ConsoleTextSize.Large, ConsoleTone.Good),
                };
                sign = new SignModel { Word = signs.Get("lever.confirm"), Tone = ConsoleTone.Accent, Enabled = true };
                ship.Show(top, bottom, sign);
                Shots(ship, "console-ship");
                sb.Append(Pixels(ship)).Append('\n');

                // A locked site selected with a short balance: the dim UNLOCK sign.
                top.Cards[1].Selected = false; top.Cards[2].Selected = true; top.Cards[2].Aimed = false;
                bottom.Picture = sites.PictureOf(SiteId.Site02); bottom.Name = "SITE 02";
                bottom.Lines = new[] { new ConsoleLine("SITE 02 · uncharted", ConsoleTextSize.Medium, ConsoleTone.Dim), new ConsoleLine("BALANCE $60", ConsoleTextSize.Medium, ConsoleTone.Text), new ConsoleLine("COST $100", ConsoleTextSize.Medium, ConsoleTone.Warn) };
                bottom.Status = new ConsoleLine("$40 SHORT", ConsoleTextSize.Large, ConsoleTone.Warn);
                sign = new SignModel { Word = string.Format(signs.Get("lever.unlock"), 100), Tone = ConsoleTone.Dim, Enabled = false, Aimed = true };
                ship.Show(top, bottom, sign);
                Shoot("console-ship-locked", ship, new Vector3(0f, 1.6f, 1.8f), new Vector3(0f, 1.15f, 0f), 60f);
                Shoot("console-ship-sign-dim", ship, new Vector3(-0.55f, 1.35f, 1.05f), SignCentre, 40f);

                // A refusal on the bottom screen.
                top.Cards[2].Selected = false; top.Cards[1].Selected = true;
                bottom.Picture = sites.PictureOf(SiteId.Site01); bottom.Name = "SITE 01";
                bottom.Lines = new[] { new ConsoleLine("DAY 2 OF 3", ConsoleTextSize.Medium, ConsoleTone.Text), new ConsoleLine("QUOTA $120 / $500", ConsoleTextSize.Medium, ConsoleTone.Warn) };
                bottom.Status = new ConsoleLine("NOT ABOARD: DAN", ConsoleTextSize.Large, ConsoleTone.Warn); bottom.Notice = true;
                sign = new SignModel { Word = signs.Get("lever.confirm"), Tone = ConsoleTone.Dim, Enabled = false };
                ship.Show(top, bottom, sign);
                Shoot("console-ship-notice", ship, new Vector3(0.2f, 1.6f, 1.2f), BottomCentre, 55f);

                // Nothing selected (the idle poster).
                ship.ShowIdle();
                Shoot("console-ship-idle", ship, new Vector3(0f, 1.6f, 1.8f), new Vector3(0f, 1.15f, 0f), 60f);

                // The lever pulled: the hinge turned 30 degrees, seen from the side.
                if (ship.LeverHinge != null)
                {
                    Quaternion rest = ship.LeverHinge.localRotation;
                    Shoot("console-lever-rest", ship, new Vector3(-2.2f, 1.25f, 0.6f), LeverPivot + new Vector3(0f, 0.12f, 0.15f), 35f);
                    ship.LeverHinge.localRotation = rest * Quaternion.AngleAxis(ConsoleLever.SwingDegrees, Vector3.right);
                    Shoot("console-lever-pulled", ship, new Vector3(-2.2f, 1.25f, 0.6f), LeverPivot + new Vector3(0f, 0.12f, 0.15f), 35f);
                    Shoot("console-lever-pulled-front", ship, new Vector3(-0.8f, 1.5f, 1.4f), LeverPivot + new Vector3(0f, 0.1f, 0.2f), 40f);
                    ship.LeverHinge.localRotation = rest;
                }

                // HQ on payday, one vote in.
                ConsoleRig.Idle(ConsoleKind.HQ, out TopModel hqTop, out BottomModel hqBottom, out SignModel hqSign);
                hqTop.Corner = "DAY 3/3"; hqTop.BigState = "PAYDAY"; hqTop.BigTone = ConsoleTone.Warn;
                hqTop.Hint = "Return to HQ and pay."; hqTop.HintTone = ConsoleTone.Dim;
                hqTop.FootLeft = "QUOTA $420 / $500"; hqTop.FootLeftTone = ConsoleTone.Warn; hqTop.FootRight = "BALANCE $230";
                hqBottom.VoteCard = new VoteCardModel { Word = signs.Get("giveup.card"), Count = "1 / 3", Foot = signs.Get("giveup.foot"), Enabled = true };
                hqSign = new SignModel { Word = signs.Get("lever.pay"), Tone = ConsoleTone.Good, Enabled = true };
                hq.Show(hqTop, hqBottom, hqSign);
                Shots(hq, "console-hq");
                sb.Append(Pixels(hq)).Append('\n');

                // HQ paid, this player voted, the lever dim.
                hqTop.BigState = "PAID"; hqTop.BigTone = ConsoleTone.Good;
                hqTop.Hint = "GIVE UP 1/3 · EVERYONE MUST PRESS"; hqTop.HintTone = ConsoleTone.Warn;
                hqTop.FootLeft = "QUOTA $520 / $500"; hqTop.FootLeftTone = ConsoleTone.Good;
                hqBottom.VoteCard.LocalVoted = true; hqBottom.VoteCard.Foot = signs.Get("giveup.voted"); hqBottom.VoteCard.Aimed = true;
                hqSign = new SignModel { Word = signs.Get("lever.pay"), Tone = ConsoleTone.Dim, Enabled = false };
                hq.Show(hqTop, hqBottom, hqSign);
                Shoot("console-hq-voted", hq, new Vector3(0f, 1.6f, 1.8f), new Vector3(0f, 1.15f, 0f), 60f);
                Shoot("console-hq-voted-bottom", hq, new Vector3(0.2f, 1.6f, 1.2f), BottomCentre, 55f);
            }
            finally { Object.DestroyImmediate(temp); }
            return sb.ToString();
        }

        private static void Shots(ConsoleRig rig, string prefix)
        {
            Shoot(prefix + "-eye", rig, new Vector3(0f, 1.6f, 1.8f), new Vector3(0f, 1.15f, 0f), 60f);      // the standing player's eyes, 1.8 m back
            Shoot(prefix + "-play", rig, new Vector3(0f, 1.6f, 1.3f), new Vector3(0f, 1.2f, 0f), 75f);      // the game's FOV, at the desk
            Shoot(prefix + "-top", rig, new Vector3(0.05f, 1.6f, 0.95f), TopCentre, 45f);
            Shoot(prefix + "-bottom", rig, new Vector3(0.2f, 1.6f, 1.2f), BottomCentre, 55f);
            Shoot(prefix + "-sign", rig, new Vector3(-0.55f, 1.35f, 1.05f), SignCentre, 40f);
            Shoot(prefix + "-side", rig, new Vector3(2.6f, 1.5f, 2.2f), new Vector3(0f, 1.1f, 0f), 50f);
        }

        // A capture in rig space: `from` and `at` are rig-local metres.
        private static void Shoot(string name, ConsoleRig rig, Vector3 from, Vector3 at, float fov)
            => LookCapture.Shoot(name, rig.transform.TransformPoint(from), rig.transform.TransformPoint(at), fov);

        private static string Describe(ConsoleRig rig)
        {
            var sb = new StringBuilder();
            sb.Append(rig.name).Append(": kind=").Append(rig.Kind);
            sb.Append(" hinge=").Append(rig.LeverHinge != null ? rig.LeverHinge.localPosition.ToString("F3") : "none");
            sb.Append(" handle=").Append(rig.LeverHandle != null ? rig.LeverHandle.name : "none");
            foreach (ConsoleScreen s in new[] { rig.TopSurface, rig.BottomSurface, rig.SignSurface })
                if (s != null) sb.Append(" | ").Append(s.SurfaceName).Append(' ').Append(s.Metres.x.ToString("F2")).Append('x').Append(s.Metres.y.ToString("F2")).Append("m ").Append(s.Width).Append('x').Append(s.Height).Append("px at ").Append(s.transform.localPosition.ToString("F3")).Append(" n=").Append(s.transform.localRotation * Vector3.forward).Append(" key=").Append(s.LastKey != null ? "painted" : "unpainted");
            sb.Append(" | lever=").Append(rig.LeverCollider != null ? rig.LeverCollider.name + "@" + rig.LeverCollider.transform.localPosition.ToString("F3") : "none");
            sb.Append(" giveUp=").Append(rig.GiveUpCollider != null ? rig.GiveUpCollider.name : "none");
            sb.Append(" cards=");
            foreach (Collider c in rig.CardColliders) if (c != null) sb.Append(c.name).Append('@').Append(c.transform.localPosition.ToString("F3")).Append(' ');
            MeshCollider body = rig.GetComponentInChildren<MeshCollider>();
            sb.Append(" body=").Append(body != null ? body.sharedMesh.name + " v" + body.sharedMesh.vertexCount + " t" + body.sharedMesh.triangles.Length / 3 : "none");
            return sb.ToString();
        }

        // One known pixel per surface (the polish and test agents' assertion pattern):
        // the top's grid area (the glass colour), a selected card's frame, the sign's word.
        private static string Pixels(ConsoleRig rig)
        {
            var sb = new StringBuilder("pixels ").Append(rig.name).Append(": ");
            ConsoleStyle style = ConsoleStyle.Resolve();
            if (rig.TopSurface != null && rig.TopSurface.Texture != null)
            {
                Rect[] cards = ConsolePaint.CardRects(rig.TopSurface.Width, rig.TopSurface.Height, style);
                sb.Append("top corner=").Append(Read(rig.TopSurface.Texture, 4, 4));
                sb.Append(" card1frame=").Append(Read(rig.TopSurface.Texture, Mathf.RoundToInt(cards[1].xMin + 2f), Mathf.RoundToInt(cards[1].center.y)));
                sb.Append(" card1mid=").Append(Read(rig.TopSurface.Texture, Mathf.RoundToInt(cards[1].center.x), Mathf.RoundToInt(cards[1].yMin + cards[1].height * 0.3f)));
            }
            if (rig.SignSurface != null && rig.SignSurface.Texture != null)
                sb.Append(" sign=").Append(Read(rig.SignSurface.Texture, rig.SignSurface.Width / 2, Mathf.RoundToInt(rig.SignSurface.Height * 0.42f)));
            if (rig.BottomSurface != null && rig.BottomSurface.Texture != null)
                sb.Append(" bottom=").Append(Read(rig.BottomSurface.Texture, rig.BottomSurface.Width / 2, rig.BottomSurface.Height / 2));
            return sb.ToString();
        }

        // The pixel at (x, y down from the top) of a render texture, as bytes.
        private static string Read(RenderTexture rt, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                Color32 c = tex.GetPixel(Mathf.Clamp(x, 0, rt.width - 1), Mathf.Clamp(rt.height - 1 - y, 0, rt.height - 1));
                return $"({c.r},{c.g},{c.b})";
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(tex); }
        }
    }
}
