using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Dan's generated ship parts into the game (docs/reference/"Ship art - what to
    // generate.docx", 23 September 2026): each part arrives as
    // Models/Ship/<Part>/<Part>.fbx — already the right size and the right way
    // round, prepared by tools/blender/prepare_ship_part.py — with its maps beside
    // it in Maps/. This gives every one a URP Lit material built from those maps and
    // leaves a prefab at Prefabs/Ship/<Part>.prefab that the ship builder drops onto
    // the deck. Look only: no colliders here, because the ship's own colliders and
    // trigger volumes stay exactly where the game already put them.
    public static class ShipModelSetup
    {
        public const string ModelRoot = "Assets/_Project/Models/Ship";
        public const string PrefabFolder = "Assets/_Project/Prefabs/Ship";
        public const string TextureFolder = "Assets/_Project/Textures/Ship";

        // The parts the ship uses, in the document's order.
        public static readonly string[] Parts =
        {
            "Hull", "Tower", "Railing", "StorageRoom", "CabinHousing",
            "Crane", "Winch", "Container", "Console", "TvCabinet", "DeckLamp",
            "CabinDoor", "StorageSill",
            "Bollard", "Pipes", "Ladder", "Barrel", "Crate", "CableCoil", "Lifebuoy", "Toolbox",
            "Couch", "Bench", "Table",
            "NamePlate", "Signs",
            "ElevatorCar", "CarPanel", "TubeSection", "TubeFoot",
        };

        [MenuItem("Sunk Cost/Look/Apply ship models (every part with an FBX)")]
        public static void ApplyAllFromMenu() => Debug.Log(ApplyAll());

        public static string ApplyAll()
        {
            var done = new List<string>();
            var missing = new List<string>();
            foreach (string part in Parts)
            {
                if (!File.Exists(FbxPath(part))) { missing.Add(part); continue; }
                done.Add(Apply(part));
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string report = string.Join("\n", done);
            if (missing.Count > 0) report += "\nno FBX yet: " + string.Join(", ", missing);
            return report;
        }

        public static string FbxPath(string part) => $"{ModelRoot}/{part}/{part}.fbx";
        public static string PrefabPath(string part) => $"{PrefabFolder}/{part}.prefab";

        public static string Apply(string part)
        {
            string fbx = FbxPath(part);
            if (!File.Exists(fbx)) return part + ": no FBX";
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null) return part + ": not a model";

            // The mesh is final geometry: no rig, no animation, no extra materials —
            // the prepare script already sized, decimated and flat-shaded it.
            bool reimport = false;
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; reimport = true; }
            if (importer.importAnimation) { importer.importAnimation = false; reimport = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = false; reimport = true; }
            if (!importer.useFileScale) { importer.useFileScale = true; reimport = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; reimport = true; }
            if (!importer.isReadable && (part == "Hull" || part == "TvCabinet")) { importer.isReadable = true; reimport = true; } // read at build time: the hull's outline (and its collider), the TV's screen panel
            if (reimport) importer.SaveAndReimport();

            Material material = BuildMaterial(part);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null) return part + ": the FBX did not import";

            HQPrototypeBuilderFolders(PrefabFolder);
            GameObject root = new(part);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Mesh";
                instance.transform.SetParent(root.transform, false);
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    // The hull's second slot is its deck faces (prepare_ship_part.finish_hull
                    // puts them there with UVs in metres): the tiling deck plate, cut
                    // exactly to the hull and the well.
                    if (part == "Hull" && materials.Length > 1) materials[1] = DeckMaterial();
                    r.sharedMaterials = materials;
                    // The ship is lit by its own lamps and the sky; these meshes are
                    // dressing, and shadow casting on thirty of them is not worth it.
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(part));
            }
            finally { Object.DestroyImmediate(root); }

            long tris = CountTriangles(model);
            return $"{part}: {tris} tris, material {(material != null ? material.name : "none")}";
        }

        private static long CountTriangles(GameObject model)
        {
            long tris = 0;
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null) tris += f.sharedMesh.triangles.Length / 3;
            return tris;
        }

        // One URP Lit material per part, from the maps the prepare script wrote.
        private static Material BuildMaterial(string part)
        {
            string maps = $"{ModelRoot}/{part}/Maps";
            Texture2D baseMap = Find(maps, part + "_BaseColor");
            Texture2D normal = Find(maps, part + "_Normal");
            if (normal != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
            {
                ni.textureType = TextureImporterType.NormalMap;
                ni.SaveAndReimport();
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(normal));
            }
            string path = $"{ModelRoot}/{part}/{part}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = part };
                AssetDatabase.CreateAsset(material, path);
            }
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
            else material.DisableKeyword("_NORMALMAP");
            // No metal on a salvage ship's dressing, and a dull sheen everywhere: the
            // generated roughness maps are not worth their size in the repository, and
            // URP would need them packed into a mask to use them properly.
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.2f);
            // No detail map: a saved material keeps whatever an earlier run set, and a
            // multiplied detail layer once turned every big part navy.
            material.SetTexture("_DetailAlbedoMap", null);
            material.DisableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_DETAIL_SCALED");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D Find(string folder, string name)
        {
            if (!Directory.Exists(folder)) return null;
            return AssetDatabase.FindAssets(name + " t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == name)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .FirstOrDefault(t => t != null);
        }

        private static void HQPrototypeBuilderFolders(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            HQPrototypeBuilderFolders(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // The deck's own material: the tiling texture, not a model.
        public static Material DeckMaterial()
        {
            string path = TextureFolder + "/DeckPlate.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DeckPlate" };
                AssetDatabase.CreateAsset(material, path);
            }
            Texture2D deck = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/DeckPlate.png");
            if (deck != null)
            {
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(deck)) is TextureImporter ti && ti.wrapMode != TextureWrapMode.Repeat)
                {
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.SaveAndReimport();
                }
                material.SetTexture("_BaseMap", deck);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.15f);
            // One tile per two metres of deck (the texture was drawn at that scale).
            material.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f));
            EditorUtility.SetDirty(material);
            return material;
        }

        // The hull's material, used by the tower too: the same steel everywhere.
        public static Material HullMaterial()
        {
            string path = TextureFolder + "/HullSteel.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HullSteel" };
                AssetDatabase.CreateAsset(material, path);
            }
            Texture2D steel = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/HullSteel.png");
            if (steel != null) material.SetTexture("_BaseMap", steel);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f));
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
