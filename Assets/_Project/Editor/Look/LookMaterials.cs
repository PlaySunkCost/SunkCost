using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Editor.Look
{
    // The look kit's materials (Dan, 18 September 2026): URP Lit over the
    // procedural sets, plus the flat and the glowing ones. Created or refreshed
    // by name under Assets/_Project/Art/HQ/Materials; a prop prefab references
    // them, so swapping a texture set here re-dresses every prop that wears it.
    // The palette is the reference picture's: navy steel, rust, hazard yellow,
    // orange lamps, teal screens, red/green/yellow crates.
    public static class LookMaterials
    {
        public const string Folder = "Assets/_Project/Art/HQ/Materials";

        public static Material DeckTile() => Lit("DeckTile", ProceduralTextures.DeckTile(), 4f, Color.white);
        public static Material DeckTileWarm() => Lit("DeckTileWarm", ProceduralTextures.DeckTileWarm(), 4f, Color.white); // the picture's warm inner floor
        public static Material Panel() => Lit("Panel", ProceduralTextures.Panel(), 3f, Color.white);
        public static Material PanelDark() => Lit("PanelDark", ProceduralTextures.Panel(), 3f, new Color(0.62f, 0.64f, 0.70f));
        public static Material RustPanel() => Lit("RustPanel", ProceduralTextures.RustPanel(), 3f, Color.white);
        public static Material RustSteel() => Lit("RustSteel", ProceduralTextures.RustSteel(), 2f, Color.white);
        public static Material Hazard() => Lit("Hazard", ProceduralTextures.Hazard(), 1f, Color.white);
        public static Material ButtonRed() => Flat("ButtonRed", new Color(0.85f, 0.12f, 0.10f), 0f, 0.45f); // every button in the game (Dan, 19 September 2026)
        public static Material Trim() => Flat("Trim", new Color(0.95f, 0.72f, 0.18f), 0f, 0.3f); // the picture's plain yellow lines
        public static Material CrateGrey() => Lit("CrateGrey", ProceduralTextures.Crate(), 1.4f, new Color(0.42f, 0.45f, 0.50f));
        public static Material CrateRed() => Lit("CrateRed", ProceduralTextures.Crate(), 1.4f, new Color(0.80f, 0.20f, 0.14f));
        public static Material CrateGreen() => Lit("CrateGreen", ProceduralTextures.Crate(), 1.4f, new Color(0.36f, 0.50f, 0.26f));
        public static Material CrateYellow() => Lit("CrateYellow", ProceduralTextures.Crate(), 1.4f, new Color(0.92f, 0.66f, 0.12f));
        public static Material CrateNavy() => Lit("CrateNavy", ProceduralTextures.Crate(), 1.4f, new Color(0.22f, 0.28f, 0.42f));
        public static Material ContainerRed() => Lit("ContainerRed", ProceduralTextures.Container(), 1f, new Color(0.78f, 0.18f, 0.12f));
        public static Material ContainerGreen() => Lit("ContainerGreen", ProceduralTextures.Container(), 1f, new Color(0.30f, 0.46f, 0.24f));
        public static Material ContainerGrey() => Lit("ContainerGrey", ProceduralTextures.Container(), 1f, new Color(0.40f, 0.44f, 0.50f));
        public static Material ContainerYellow() => Lit("ContainerYellow", ProceduralTextures.Container(), 1f, new Color(0.90f, 0.62f, 0.12f));
        public static Material BarrelRed() => Lit("BarrelRed", ProceduralTextures.Container(), 0.8f, new Color(0.76f, 0.16f, 0.12f));
        public static Material BarrelRust() => Lit("BarrelRust", ProceduralTextures.RustSteel(), 1f, Color.white);
        public static Material Ink() => Flat("Ink", ProceduralTextures.Ink, 0f, 0.15f);
        public static Material Fender() => Flat("Fender", new Color(0.85f, 0.16f, 0.12f), 0f, 0.15f);
        public static Material FenderBand() => Flat("FenderBand", new Color(0.92f, 0.92f, 0.92f), 0f, 0.15f);

        // The glow: the lamps and the sign frames carry the night. HDR, so the
        // bloom volume picks them up; the base stays dim so an unlit one reads as glass.
        public static Material LampOrange() => Emissive("LampOrange", new Color(1.0f, 0.46f, 0.10f), 1.25f);
        public static Material LampWarm() => Emissive("LampWarm", new Color(1.0f, 0.68f, 0.30f), 1.4f);
        public static Material SignGlow() => Emissive("SignGlow", new Color(1.0f, 0.58f, 0.18f), 1.5f);
        public static Material WindowGlow() => Emissive("WindowGlow", new Color(1.0f, 0.62f, 0.24f), 1.1f);
        public static Material BeaconRed() => Emissive("BeaconRed", new Color(1.0f, 0.15f, 0.10f), 4f);
        public static Material BeaconWhite() => Emissive("BeaconWhite", new Color(1.0f, 0.95f, 0.85f), 3f);
        public static Material ScreenTeal() => Emissive("ScreenTeal", new Color(0.25f, 0.92f, 0.82f), 1.4f);
        public static Material SignBoard() => Flat("SignBoard", new Color(0.06f, 0.06f, 0.08f), 0f, 0.2f);
        // A destination's button: the ship's accent, darkened for white words on it;
        // red stays for what cannot be undone (ship audit SHIP-048, 23 September 2026).
        public static Material ButtonAccent() => Flat("ButtonAccent", SunkCost.World.ButtonLook.DestinationCap, 0f, 0.45f);

        // The ship's screens (SHIP-044/059): unlit flat colours of the one screen
        // style (SunkCost.Look.ScreenStyle), so the glass never glows brighter than
        // its words and the deck's sun never lifts it. The HQ's ScreenTeal is left as it is.
        public const string ScreenFolder = Folder + "/ShipScreens";
        public static Material ShipScreen() => ShipFlat(SunkCost.Look.ScreenStyle.Back);
        public static Material ShipFlat(Color c)
        {
            string path = ScreenFolder + "/ScreenFlat_" + ColorUtility.ToHtmlStringRGB(c) + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) throw new System.InvalidOperationException("URP Unlit shader not found.");
            if (m == null)
            {
                System.IO.Directory.CreateDirectory(ScreenFolder);
                m = new Material(unlit);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.shader != unlit) m.shader = unlit;
            if (m.GetColor("_BaseColor") != c) { m.SetColor("_BaseColor", c); EditorUtility.SetDirty(m); }
            return m;
        }
        public static Material DeckMarking() => Flat("DeckMarking", new Color(0.82f, 0.80f, 0.74f), 0.0f, 0.2f);
        public static Material CourtPaint() => Flat("CourtPaint", new Color(0.72f, 0.34f, 0.20f), 0.0f, 0.12f);
        public static Material Backboard() => Flat("Backboard", new Color(0.85f, 0.87f, 0.90f), 0f, 0.2f);
        public static Material HoopOrange() => Flat("HoopOrange", new Color(0.95f, 0.45f, 0.10f), 0f, 0.2f);
        public static Material Net() => Flat("Net", new Color(0.9f, 0.9f, 0.9f), 0f, 0.1f);
        public static Material Flag() => Flat("Flag", new Color(0.06f, 0.06f, 0.08f), 0f, 0.2f);
        public static Material Seabed() => Flat("Seabed", new Color(0.04f, 0.10f, 0.20f), 0f, 0.1f);
        // The monsters (20 September 2026): a body darker than the water, and the eyes that give it away.
        public static Material Creature() => Flat("Creature", new Color(0.04f, 0.045f, 0.06f), 0f, 0.12f);
        public static Material EyeGlow() => Emissive("EyeGlow", new Color(1f, 0.85f, 0.25f), 4f);

        // Chain-link fencing: the wire's tile cut out, seen from both sides.
        public static Material ChainLink()
        {
            Material m = Lit("ChainLink", ProceduralTextures.ChainLink(), 0.5f, Color.white);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        // The company's mark: a cutout over the skull texture.
        public static Material Skull()
        {
            Material m = Flat("Skull", Color.white, 0f, 0.2f);
            m.SetTexture("_BaseMap", ProceduralTextures.Skull().Albedo);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Text that stays behind walls: the depth-tested text shader over the
        // default font's atlas (DepthText keeps the atlas current).
        public const string DepthTextPath = Folder + "/DepthText.mat";
        public static Material DepthText()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(DepthTextPath);
            Shader shader = Shader.Find("Sunk Cost/Depth Text");
            if (shader == null) throw new System.InvalidOperationException("Sunk Cost/Depth Text shader not found (Assets/_Project/Art/HQ/Shaders).");
            if (m == null)
            {
                System.IO.Directory.CreateDirectory(Folder);
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, DepthTextPath);
            }
            if (m.shader != shader) m.shader = shader;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null && font.material != null && font.material.mainTexture != null) m.mainTexture = font.material.mainTexture;
            EditorUtility.SetDirty(m);
            return m;
        }

        // The sea: the blocky patches, self-lit a little so they read at night
        // (the picture's water is a vivid blue in the dark); WaveSurface drifts
        // the patches and moves the mesh.
        public static Material Water()
        {
            ProceduralTextures.Set set = ProceduralTextures.BlockWater();
            Material m = Get("Water");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", set.Albedo);
            m.SetTexture("_BumpMap", set.Normal);
            m.SetFloat("_BumpScale", 0.25f);
            m.SetTextureScale("_BaseMap", new Vector2(1f / 24f, 1f / 24f));
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 0.0f);
            m.SetFloat("_Smoothness", 0.25f);
            m.SetTexture("_EmissionMap", set.Albedo);
            m.SetColor("_EmissionColor", new Color(0.45f, 0.5f, 0.6f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Opaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---- the machinery ------------------------------------------------------------

        private static Material Lit(string name, ProceduralTextures.Set set, float metresPerTile, Color tint)
        {
            Material m = Get(name);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", set.Albedo);
            m.SetTexture("_BumpMap", set.Normal);
            m.SetFloat("_BumpScale", 0.45f); // a hint of the bevel, not a relief: the picture is flat
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", set.Mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetTextureScale("_BaseMap", new Vector2(1f / metresPerTile, 1f / metresPerTile)); // the kit's meshes carry UVs in metres (MeshKit)
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Back);
            Opaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Flat(string name, Color color, float metallic, float smoothness)
        {
            Material m = Get(name);
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Back);
            Opaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Emissive(string name, Color color, float intensity)
        {
            Material m = Flat(name, color * 0.3f, 0f, 0.2f);
            m.SetColor("_EmissionColor", color * intensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Get(string name)
        {
            string path = Folder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new System.InvalidOperationException("URP Lit shader not found.");
                System.IO.Directory.CreateDirectory(Folder);
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
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
    }
}
