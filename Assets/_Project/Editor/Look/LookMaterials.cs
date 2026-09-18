using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Editor.Look
{
    // The look kit's materials (Dan, 18 September 2026): URP Lit over the
    // procedural sets, plus the few flat ones (light strips, sign boards, the
    // beacon) that need no texture. Created or refreshed by name under
    // Assets/_Project/Art/HQ/Materials; a prop prefab references them, so
    // swapping a texture set here re-dresses every prop that wears it.
    public static class LookMaterials
    {
        public const string Folder = "Assets/_Project/Art/HQ/Materials";

        public static Material DeckPlate() => Lit("DeckPlate", ProceduralTextures.DeckPlate(), 4f, Color.white);
        public static Material HullPanel() => Lit("HullPanel", ProceduralTextures.HullPanel(), 3f, Color.white);
        public static Material HullPanelDark() => Lit("HullPanelDark", ProceduralTextures.HullPanel(), 3f, new Color(0.55f, 0.58f, 0.62f));
        public static Material RustSteel() => Lit("RustSteel", ProceduralTextures.RustSteel(), 2f, Color.white);
        public static Material Hazard() => Lit("Hazard", ProceduralTextures.Hazard(), 1f, Color.white);
        public static Material Plank() => Lit("Plank", ProceduralTextures.Plank(), 1f, Color.white);
        public static Material CrateGrey() => Lit("CrateGrey", ProceduralTextures.PaintedPanel(), 1f, new Color(0.45f, 0.47f, 0.50f));
        public static Material CrateRed() => Lit("CrateRed", ProceduralTextures.PaintedPanel(), 1f, new Color(0.75f, 0.22f, 0.16f));
        public static Material CrateGreen() => Lit("CrateGreen", ProceduralTextures.PaintedPanel(), 1f, new Color(0.30f, 0.42f, 0.24f));
        public static Material CrateYellow() => Lit("CrateYellow", ProceduralTextures.PaintedPanel(), 1f, new Color(0.85f, 0.62f, 0.14f));
        public static Material RailYellow() => Lit("RailYellow", ProceduralTextures.PaintedPanel(), 1f, new Color(0.88f, 0.66f, 0.12f));
        public static Material RailDark() => Lit("RailDark", ProceduralTextures.PaintedPanel(), 1f, new Color(0.16f, 0.17f, 0.19f));
        public static Material Fender() => Lit("Fender", ProceduralTextures.PaintedPanel(), 1f, new Color(0.80f, 0.18f, 0.12f));

        // Warm light strips and lamps: the emission carries the look (HDR, so the
        // bloom volume picks them up); the base stays dim so an unlit strip reads as glass.
        public static Material LampWarm() => Emissive("LampWarm", new Color(1.0f, 0.70f, 0.35f), 1.9f);
        public static Material LampCool() => Emissive("LampCool", new Color(0.55f, 0.85f, 1.0f), 1.7f);
        public static Material SignGlow() => Emissive("SignGlow", new Color(1.0f, 0.58f, 0.22f), 1.5f);
        public static Material BeaconRed() => Emissive("BeaconRed", new Color(1.0f, 0.15f, 0.10f), 3.5f);
        public static Material BeaconWhite() => Emissive("BeaconWhite", new Color(1.0f, 0.95f, 0.85f), 3f);
        public static Material ScreenTeal() => Emissive("ScreenTeal", new Color(0.25f, 0.90f, 0.80f), 1.1f);
        public static Material SignBoard() => Flat("SignBoard", new Color(0.05f, 0.05f, 0.06f), 0.25f, 0.35f);
        public static Material DeckMarking() => Flat("DeckMarking", new Color(0.86f, 0.84f, 0.78f), 0.0f, 0.2f);
        public static Material CourtPaint() => Flat("CourtPaint", new Color(0.62f, 0.32f, 0.20f), 0.0f, 0.25f);
        public static Material Backboard() => Flat("Backboard", new Color(0.82f, 0.84f, 0.86f), 0.1f, 0.5f);
        public static Material HoopOrange() => Flat("HoopOrange", new Color(0.95f, 0.45f, 0.10f), 0.6f, 0.6f);
        public static Material Net() => Flat("Net", new Color(0.9f, 0.9f, 0.9f), 0f, 0.1f);
        public static Material Flag() => Flat("Flag", new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f);
        public static Material FlagSkull() => Flat("FlagSkull", new Color(0.85f, 0.85f, 0.88f), 0f, 0.2f);
        public static Material Seabed() => Flat("Seabed", new Color(0.10f, 0.15f, 0.18f), 0f, 0.1f);

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

        // The sea: a transparent dark blue over the wave normals, glossy for the
        // dawn's specular; WaveSurface scrolls the normals and moves the mesh.
        public static Material Water()
        {
            ProceduralTextures.Set set = ProceduralTextures.WaterRipple();
            Material m = Get("Water");
            m.SetColor("_BaseColor", new Color(0.03f, 0.13f, 0.22f, 0.94f));
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_BumpMap", set.Normal);
            m.SetFloat("_BumpScale", 0.35f);
            m.SetTextureScale("_BaseMap", new Vector2(1f / 12f, 1f / 12f));
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 0.0f);
            m.SetFloat("_Smoothness", 0.90f);
            Transparent(m);
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
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", set.Mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f); // the mask's alpha scales this
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetTextureScale("_BaseMap", new Vector2(1f / metresPerTile, 1f / metresPerTile)); // the kit's meshes carry UVs in metres (MeshKit): tiles per metre
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
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            Opaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Emissive(string name, Color color, float intensity)
        {
            Material m = Flat(name, color * 0.25f, 0f, 0.6f);
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

        private static void Transparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
