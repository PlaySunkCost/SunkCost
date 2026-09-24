using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Editor.Look
{
    // The ship kit's worn materials (ship audit SHIP-059/060, 23 September 2026): the
    // code-built parts that stay on the ship - the crew screen's frame, the buttons'
    // bezels, the well's grate, pedestal and wall, the cabin's cap - were flat Ink and
    // Trim beside Dan's textured generated models, and the ship had two hazard
    // stripes. These dress them in the models' own palette (its charcoal steel, worn
    // to bare metal at the chips; its worn orange-and-black stripe), from the tiling
    // maps tools/art/make_ship_kit.py paints. The ship's own: LookMaterials stays the
    // HQ's kit and is not touched.
    //
    // Their UVs are in metres, like MeshKit's meshes: one tile per metre (the bezel
    // half a metre). A Unity primitive stretches one tile over each face, so a
    // primitive takes Tiled(...), a twin tiled for its face's size.
    public static class ShipKitMaterials
    {
        public const string Folder = "Assets/_Project/Art/Ship";
        public const string TextureFolder = Folder + "/Textures";
        public const string MaterialFolder = Folder + "/Materials";

        // Frames, rails, grates, the pedestal and the well's wall.
        public static Material Steel() => Lit("KitSteel", 1f, 0.28f);
        // The surround of a screen or a button: darker, cleaner, a little more sheen.
        public static Material Bezel() => Lit("KitBezel", 0.5f, 0.36f);
        // The one hazard stripe: the models' worn orange and black, 10 cm stripes.
        public static Material Hazard() => Lit("KitHazard", 1f, 0.25f);

        // The storage room's painted floor mark: a cutout, 2.4 x 2.0 m on a 0..1 quad.
        public static Material DropZone()
        {
            Material m = Get("StorageDropZone");
            var before = new Material(m);
            Texture2D tex = Texture("StorageDropZone_BaseColor", false, TextureWrapMode.Clamp);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.18f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Surface", 0f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return Settle(m, before);
        }

        // A kit material tiled for a Unity primitive whose face is `metres` across (a
        // cube's front face: its x and y scale; a cylinder's side: its circumference and
        // height), so the wear keeps its size on a stretched box. Saved as its own asset
        // (a property block would not survive the prefab), one per size.
        public static Material Tiled(Material kit, Vector2 metres)
        {
            string name = kit.name + "_" + F(metres.x) + "x" + F(metres.y);
            string path = MaterialFolder + "/Tiled/" + name + ".mat";
            var want = new Material(kit) { name = name };
            want.SetTextureScale("_BaseMap", Vector2.Scale(kit.GetTextureScale("_BaseMap"), metres));
            MainTexInStep(want);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                System.IO.Directory.CreateDirectory(MaterialFolder + "/Tiled");
                AssetDatabase.CreateAsset(want, path);
                return want;
            }
            // Only a real change is written: every build calls this, and a material
            // re-saved with the same values still churned the tree (round 2, 24 Sep).
            if (!Same(m, want)) { m.CopyPropertiesFromMaterial(want); EditorUtility.SetDirty(m); }
            Object.DestroyImmediate(want);
            return m;
        }

        // ---- the machinery ----------------------------------------------------------

        private static Material Lit(string name, float metresPerTile, float smoothness)
        {
            Material m = Get(name);
            var before = new Material(m);
            m.SetTexture("_BaseMap", Texture(name + "_BaseColor", false, TextureWrapMode.Repeat));
            m.SetColor("_BaseColor", Color.white);
            Texture2D normal = Texture(name + "_Normal", true, TextureWrapMode.Repeat);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Metallic", 0f); // no metal on the salvage ship's dressing, as the models (ShipModelSetup)
            m.SetFloat("_Smoothness", smoothness);
            m.SetTextureScale("_BaseMap", new Vector2(1f / metresPerTile, 1f / metresPerTile));
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetFloat("_Surface", 0f);
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = -1;
            return Settle(m, before);
        }

        // URP keeps the legacy _MainTex in step with _BaseMap and re-saves a material
        // whose two differ (the guest build did, for every tiled twin, SHIP round 2):
        // set them the same here, so there is nothing left for it to change.
        private static void MainTexInStep(Material m)
        {
            if (!m.HasProperty("_MainTex")) return;
            m.SetTexture("_MainTex", m.GetTexture("_BaseMap"));
            m.SetTextureScale("_MainTex", m.GetTextureScale("_BaseMap"));
            m.SetTextureOffset("_MainTex", m.GetTextureOffset("_BaseMap"));
        }

        // Marks the material for saving only if this run changed it; `before` is a
        // throwaway copy taken before the changes.
        private static Material Settle(Material m, Material before)
        {
            MainTexInStep(m);
            if (!Same(m, before)) EditorUtility.SetDirty(m);
            Object.DestroyImmediate(before);
            return m;
        }

        // Whether two materials would serialise the same: shader, queue, keywords,
        // tags that matter here, and every property the shader declares.
        private static bool Same(Material a, Material b)
        {
            if (a.shader != b.shader || a.renderQueue != b.renderQueue || a.enableInstancing != b.enableInstancing) return false;
            if (a.GetTag("RenderType", false) != b.GetTag("RenderType", false)) return false;
            if (!new System.Collections.Generic.HashSet<string>(a.shaderKeywords).SetEquals(b.shaderKeywords)) return false;
            Shader shader = a.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string p = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color: if (a.GetColor(p) != b.GetColor(p)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: if (a.GetVector(p) != b.GetVector(p)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: if (a.GetFloat(p) != b.GetFloat(p)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int: if (a.GetInteger(p) != b.GetInteger(p)) return false; break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        if (a.GetTexture(p) != b.GetTexture(p) || a.GetTextureScale(p) != b.GetTextureScale(p) || a.GetTextureOffset(p) != b.GetTextureOffset(p)) return false;
                        break;
                }
            }
            return true;
        }

        private static Material Get(string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Lit shader not found.");
            System.IO.Directory.CreateDirectory(MaterialFolder);
            m = new Material(shader) { name = name, enableInstancing = true };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Texture2D Texture(string name, bool normal, TextureWrapMode wrap)
        {
            string path = TextureFolder + "/" + name + ".png";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool changed = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                if (ti.wrapMode != wrap) { ti.wrapMode = wrap; changed = true; }
                if (!normal && name.StartsWith("StorageDropZone") && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogWarning("Ship kit: no " + path + " (run tools/art/make_ship_kit.py)");
            return t;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
