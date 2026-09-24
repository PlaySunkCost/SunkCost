using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Which set of plates a Signs prop carries: one per place it hangs.
    public enum ShipSign
    {
        Storage,   // STORAGE / SALVAGE HOLD · DROP LOOT INSIDE · KEEP DOORWAY CLEAR
        Bridge,    // BRIDGE / CREW ONLY · SAILING CONSOLE · ALL CREW ABOARD BEFORE SAILING
        Lounge,    // LOUNGE / CREW REST · DIVE FEED ON THE TV · MUSTER STATION
        DiveCage,  // the model's creature warning · DIVE CAGE / MIND THE GAP · the model's helmet sign
    }

    // The ship's signs (ship audit SHIP-057, 23 September 2026): the same generated
    // set of three plates hung in four places, one plate a "no diving" sign on a
    // diving ship. Each place now has its own faces on the same model, painted by
    // tools/art/make_ship_signs.py in the model's own style (its plates, stripes and
    // frames kept, the middles repainted); the default map carries the DiveCage set,
    // so an unwired sign says nothing wrong. A variant is a material: the mesh, the
    // prefab and its placement stay the dressing's.
    public static class ShipSignVariants
    {
        public const string Folder = "Assets/_Project/Models/Ship/Signs/Variants";

        // The sign as placed, wearing the plates of `variant`; the prop is returned, so
        // a Place(...) call can be wrapped. A null prop (no prefab) stays null.
        public static GameObject Apply(GameObject sign, ShipSign variant)
        {
            if (sign == null) return null;
            Material material = MaterialFor(variant);
            foreach (Renderer r in sign.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                r.sharedMaterials = materials;
            }
            return sign;
        }

        // The variant's material, made or refreshed beside its maps: the model's own
        // Lit settings (ShipModelSetup: no metal, a dull sheen), instanced.
        public static Material MaterialFor(ShipSign variant)
        {
            string name = "Signs_" + variant;
            string path = Folder + "/" + name + ".mat";
            Texture2D baseMap = Texture(Folder + "/" + name + "_BaseColor.jpg", false);
            Texture2D normal = Texture(Folder + "/" + name + "_Normal.png", true);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.2f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D Texture(string path, bool normal)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool changed = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                if (!normal && ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; changed = true; } // the words need the texels
                if (changed) ti.SaveAndReimport();
            }
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) Debug.LogWarning("Ship signs: no " + path + " (run tools/art/make_ship_signs.py)");
            return t;
        }

        // The Signs mesh in its prefab's space, for tools/art/make_ship_signs.py: which
        // texel lands where on the plates.
        public static void DumpMesh(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelSetup.PrefabPath("Signs"));
            MeshFilter mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) throw new System.InvalidOperationException("No Signs prefab mesh (run the ship model setup).");
            Matrix4x4 m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            UnityEngine.Mesh mesh = mf.sharedMesh;
            Vector3[] v = mesh.vertices, n = mesh.normals; Vector2[] uv = mesh.uv; int[] tri = mesh.triangles;
            var sb = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(v[i]), q = m.MultiplyVector(n[i]).normalized;
                sb.Append("v ").Append(p.x.ToString("R", c)).Append(' ').Append(p.y.ToString("R", c)).Append(' ').Append(p.z.ToString("R", c))
                  .Append(' ').Append(q.x.ToString("F4", c)).Append(' ').Append(q.y.ToString("F4", c)).Append(' ').Append(q.z.ToString("F4", c))
                  .Append(' ').Append(uv[i].x.ToString("R", c)).Append(' ').Append(uv[i].y.ToString("R", c)).Append('\n');
            }
            for (int i = 0; i < tri.Length; i += 3) sb.Append("f ").Append(tri[i]).Append(' ').Append(tri[i + 1]).Append(' ').Append(tri[i + 2]).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }
    }
}
