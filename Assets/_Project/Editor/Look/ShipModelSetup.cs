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

        // What casts no shadow: flat plates on a wall or the deck, where a shadow adds
        // nothing (ship audit SHIP-016). Everything else does: the sun is the ship's
        // light, and props without shadows looked pasted onto the deck.
        private static readonly HashSet<string> NoShadow = new() { "NamePlate", "Signs", "StorageSill", "CarPanel" };

        // The parts the deck repeats, whose materials draw instanced (SHIP-062).
        private static readonly HashSet<string> Repeated = new()
        {
            "DeckLamp", "Bollard", "Barrel", "Crate", "CableCoil", "Lifebuoy", "Toolbox", "Pipes", "Couch", "Bench", "NamePlate",
        };

        // The big parts whose one 2048 map is spread over tens of metres: a tiling
        // detail layer, in metres like the deck and the bulwark, so they are not
        // smeared up close (SHIP-062). Metres per detail tile.
        private static readonly Dictionary<string, float> DetailTile = new() { ["Hull"] = 2f, ["Tower"] = 2f };

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
            // the prepare script already sized, decimated and shaded it smooth by
            // angle. Its normals are imported as they are and the tangents made the
            // way Blender's bake made them (MikkTSpace): the baked normal maps are
            // relative to exactly those, so Calculate here would undo them.
            bool reimport = false;
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; reimport = true; }
            if (importer.importAnimation) { importer.importAnimation = false; reimport = true; }
            if (importer.importBlendShapes) { importer.importBlendShapes = false; reimport = true; }
            if (!importer.useFileScale) { importer.useFileScale = true; reimport = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; reimport = true; }
            if (!importer.isReadable && (part == "Hull" || part == "TvCabinet")) { importer.isReadable = true; reimport = true; } // read at build time: the hull's outline (and its collider), the TV's screen panel
            if (importer.importNormals != ModelImporterNormals.Import) { importer.importNormals = ModelImporterNormals.Import; reimport = true; }
            if (importer.importTangents != ModelImporterTangents.CalculateMikk) { importer.importTangents = ModelImporterTangents.CalculateMikk; reimport = true; }
            // Y-up in the mesh itself: the prepare script bakes the axis change into
            // the vertices (bake_space_transform), so no mesh sits under a (270.02, 0,
            // 0) transform with the deck 8 mm off level (SHIP-078). Unity's own
            // bakeAxisConversion stays off: on top of that it turns every model half
            // round (the storage room's doorway went to the starboard wall).
            if (importer.bakeAxisConversion) { importer.bakeAxisConversion = false; reimport = true; }
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
                    r.shadowCastingMode = NoShadow.Contains(part) ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
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
            material.enableInstancing = Repeated.Contains(part);
            // A saved material keeps whatever an earlier run set (a multiplied detail
            // layer once turned every big part navy), so the detail slots and their
            // keywords are set here every time, on or off.
            SetDetail(material, part);
            SetGlow(material, part, maps);
            EditorUtility.SetDirty(material);
            return material;
        }

        // What of a part glows, by the colour its baked map gives it (SHIP-015: the
        // deck lamps light the deck, and their heads did not look lit): the lamp's lens
        // is one flat warm-pale island in DeckLamp_BaseColor. Only the largest patch
        // of that colour is the lens (the hazard paint is a darker, deeper yellow).
        private static readonly Dictionary<string, Color32> GlowIsland = new() { ["DeckLamp"] = new Color32(197, 167, 118, 255) };
        private const int GlowTolerance = 26;
        private static readonly Color GlowColour = new Color(1f, 0.62f, 0.32f) * 1.5f; // HDR: reads as lit in daylight, still warm, not blown white

        // The emission mask (Maps/<Part>_Emission.png, white on the lens) is made from
        // the colour map whenever that is newer, so a rebake carries it along.
        private static void SetGlow(Material material, string part, string maps)
        {
            Texture2D mask = GlowIsland.TryGetValue(part, out Color32 lens) ? GlowMask(part, maps, lens) : null;
            material.SetTexture("_EmissionMap", mask);
            material.SetColor("_EmissionColor", mask != null ? GlowColour : Color.black);
            if (mask != null) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = mask != null ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        private static Texture2D GlowMask(string part, string maps, Color32 lens)
        {
            string source = Directory.Exists(maps) ? Directory.GetFiles(maps, part + "_BaseColor.*").FirstOrDefault(f => !f.EndsWith(".meta")) : null;
            if (source == null) return null;
            string path = $"{maps}/{part}_Emission.png";
            if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(source))
            {
                var read = new Texture2D(2, 2);
                if (!read.LoadImage(File.ReadAllBytes(source))) { Object.DestroyImmediate(read); return null; }
                int w = read.width, h = read.height;
                Color32[] px = read.GetPixels32();
                Object.DestroyImmediate(read);
                var near = new bool[px.Length];
                for (int i = 0; i < px.Length; i++)
                    near[i] = Mathf.Abs(px[i].r - lens.r) < GlowTolerance && Mathf.Abs(px[i].g - lens.g) < GlowTolerance && Mathf.Abs(px[i].b - lens.b) < GlowTolerance;
                // The largest 4-connected patch of it.
                var label = new int[px.Length];
                int best = 0, bestSize = 0, next = 0;
                var stack = new Stack<int>();
                for (int i = 0; i < px.Length; i++)
                {
                    if (!near[i] || label[i] != 0) continue;
                    int id = ++next, size = 0;
                    label[i] = id; stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int p = stack.Pop(); size++;
                        int x = p % w, y = p / w;
                        foreach (int q in new[] { x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1 })
                            if (q >= 0 && near[q] && label[q] == 0) { label[q] = id; stack.Push(q); }
                    }
                    if (size > bestSize) { bestSize = size; best = id; }
                }
                var outPx = new Color32[px.Length];
                for (int i = 0; i < px.Length; i++) outPx[i] = label[i] == best && best != 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                var write = new Texture2D(w, h, TextureFormat.RGB24, false);
                write.SetPixels32(outPx);
                write.Apply();
                File.WriteAllBytes(path, write.EncodeToPNG());
                Object.DestroyImmediate(write);
                AssetDatabase.ImportAsset(path);
            }
            return Import(path, TextureImporterType.Default, true);
        }

        // The detail layer: HullDetail is the grime tile as neutral grey (0.5, no
        // change under URP's x2 multiply, imported linear so it stays 0.5) with a
        // normal map from its lightness. The part's UVs are a fresh unwrap, not
        // metres, so the tiling is the mesh's own metres per UV unit over the tile.
        private static void SetDetail(Material material, string part)
        {
            Texture2D albedo = null, normal = null;
            float tiling = 1f;
            if (DetailTile.TryGetValue(part, out float tile))
            {
                albedo = Import(TextureFolder + "/HullDetail.png", TextureImporterType.Default, false);
                normal = Import(TextureFolder + "/HullDetail_Normal.png", TextureImporterType.NormalMap, false);
                tiling = MetresPerUv(part) / tile;
            }
            bool on = albedo != null;
            material.SetTexture("_DetailAlbedoMap", on ? albedo : null);
            material.SetTexture("_DetailNormalMap", on ? normal : null);
            material.SetTexture("_DetailMask", null);
            material.SetTextureScale("_DetailAlbedoMap", Vector2.one * (on ? tiling : 1f)); // the leftover 24 goes with it
            material.SetTextureOffset("_DetailAlbedoMap", Vector2.zero);
            material.SetFloat("_DetailAlbedoMapScale", 1f);
            material.SetFloat("_DetailNormalMapScale", on ? 0.6f : 1f);
            // URP's own rule (LitDetailGUI): MULX2 at scale one, SCALED otherwise.
            if (on) material.EnableKeyword("_DETAIL_MULX2"); else material.DisableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_DETAIL_SCALED");
        }

        // Metres of the model per unit of its first UV set, over its first submesh
        // (the hull's second is the deck, in metres already).
        private static float MetresPerUv(string part)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(part));
            double area = 0, uvArea = 0;
            if (model != null)
                foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    UnityEngine.Mesh mesh = f.sharedMesh;
                    if (mesh == null) continue;
                    Matrix4x4 m = f.transform.localToWorldMatrix;
                    Vector3[] v = mesh.vertices;
                    var uv = new List<Vector2>();
                    mesh.GetUVs(0, uv);
                    if (uv.Count != v.Length) continue;
                    int[] tri = mesh.GetTriangles(0);
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                        area += Vector3.Cross(b - a, c - a).magnitude / 2.0;
                        Vector2 p = uv[tri[i]], q = uv[tri[i + 1]], r = uv[tri[i + 2]];
                        uvArea += Mathf.Abs((q.x - p.x) * (r.y - p.y) - (r.x - p.x) * (q.y - p.y)) / 2.0;
                    }
                }
            return uvArea > 1e-9 ? (float)System.Math.Sqrt(area / uvArea) : 1f;
        }

        // A tiling texture with the import settings the code relies on.
        public static Texture2D Import(string path, TextureImporterType type, bool sRGB, bool alpha = false)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) return null;
            bool changed = false;
            if (ti.textureType != type) { ti.textureType = type; changed = true; }
            if (type == TextureImporterType.Default && ti.sRGBTexture != sRGB) { ti.sRGBTexture = sRGB; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
            TextureImporterAlphaSource source = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            if (ti.alphaSource != source) { ti.alphaSource = source; changed = true; }
            if (ti.alphaIsTransparency != alpha) { ti.alphaIsTransparency = alpha; changed = true; }
            if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (changed) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            Texture2D deck = Import(TextureFolder + "/DeckPlate.png", TextureImporterType.Default, true);
            if (deck != null) material.SetTexture("_BaseMap", deck);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.15f);
            // The plate is quieter than it was drawn (SHIP-055: the floor out-shouted
            // the props): contrast and colour pulled in, and a 2 x 2 tile of the
            // original 2 m one, its orange corner chevrons kept only where four tiles
            // meet and worn back - one painted diamond every 4 m, not every 2 m. The
            // walkways and zones are ShipDeckMarkings, on top.
            material.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f));
            EditorUtility.SetDirty(material);
            return material;
        }

        // The ship's side (ShipDeckDressing's bulwark): the tiling steel, in metres.
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
