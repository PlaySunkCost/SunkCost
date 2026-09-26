using System;
using System.IO;
using SunkCost.Interaction;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Reproducible, original leather surface; no downloaded artwork or runtime
    // texture generation. One material and sphere mesh shared by every basketball.
    public static class BasketballSetup
    {
        public const string Folder = "Assets/_Project/Art/Prototype/Basketball";

        [MenuItem("Sunk Cost/Prototype/Apply basketball look and physics")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            WriteTextures();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Basketball.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, Folder + "/Basketball.mat");
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Leather.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/LeatherNormal.png"));
            material.EnableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale", .65f);
            material.SetFloat("_Smoothness", .22f); material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            Mesh mesh = MakeSphere();
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Basketball.asset");
            if (saved == null) { AssetDatabase.CreateAsset(mesh, Folder + "/Basketball.asset"); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); }
            PhysicsMaterial rubber = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder + "/Basketball.physicMaterial");
            if (rubber == null) { rubber = new PhysicsMaterial("Basketball rubber"); AssetDatabase.CreateAsset(rubber, Folder + "/Basketball.physicMaterial"); }
            rubber.bounciness = .795f; rubber.bounceCombine = PhysicsMaterialCombine.Maximum;
            rubber.dynamicFriction = .55f; rubber.staticFriction = .65f; rubber.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(rubber);
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.BallPrefabPath);
            try
            {
                root.transform.localScale = Vector3.one * .26f;
                root.GetComponent<MeshFilter>().sharedMesh = saved;
                root.GetComponent<Renderer>().sharedMaterial = material;
                root.GetComponent<Collider>().sharedMaterial = rubber;
                Rigidbody body = root.GetComponent<Rigidbody>();
                body.mass = 1.24f; body.linearDamping = .02f; body.angularDamping = .18f;
                body.maxAngularVelocity = 40f; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                using var so = new SerializedObject(root.GetComponent<CarryableItem>());
                so.FindProperty("throwBackspin").floatValue = 12f;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.BallPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            ItemIconGenerator.Generate(new[] { HQPrototypeBuilder.BallPrefabPath });
            Debug.Log("Basketball: leather/seams, 0.26 m / 1.24 kg, rubber rebound and throw backspin applied.");
        }

        private static Vector3 Sphere(float u, float v)
        {
            float a = u * Mathf.PI * 2, b = v * Mathf.PI;
            return new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a));
        }

        private static Mesh MakeSphere()
        {
            const int columns = 64, rows = 32;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length];
            var triangles = new int[columns * rows * 6]; int index = 0;
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                int i = y * (columns + 1) + x;
                uv[i] = new Vector2(x / (float)columns, y / (float)rows);
                normals[i] = Sphere(uv[i].x, uv[i].y); vertices[i] = normals[i] * .5f;
                if (x == columns || y == rows) continue;
                int below = i + columns + 1;
                triangles[index++] = i; triangles[index++] = i + 1; triangles[index++] = below;
                triangles[index++] = i + 1; triangles[index++] = below + 1; triangles[index++] = below;
            }
            var mesh = new Mesh { name = "Basketball", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }

        private static void WriteTextures()
        {
            const int w = 1024, h = 512;
            var colors = new Color[w * h]; var heights = new float[w * h]; var normal = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                Vector3 p = Sphere(x / (float)w, y / (float)(h - 1));
                float curve = .6f * (1 - p.y * p.y);
                float seam = Mathf.Min(Mathf.Abs(p.y), Mathf.Abs(p.x), Mathf.Abs(p.z - curve) * .8f, Mathf.Abs(p.z + curve) * .8f);
                float leather = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.016f, .025f, seam));
                float grain = Mathf.PerlinNoise(x * .37f, y * .37f);
                float wear = Mathf.PerlinNoise(p.x * 9 + 21, p.y * 9 + p.z * 2 + 13);
                Color orange = Color.Lerp(new Color(.48f,.17f,.045f), new Color(.76f,.34f,.10f), .35f + wear * .5f + grain * .15f);
                int i = y * w + x;
                colors[i] = Color.Lerp(new Color(.025f,.02f,.016f), orange, leather);
                heights[i] = leather * .6f + grain * .28f * leather;
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float dx = heights[y*w+(x+1)%w] - heights[y*w+(x+w-1)%w];
                float dy = heights[Mathf.Min(h-1,y+1)*w+x] - heights[Mathf.Max(0,y-1)*w+x];
                Vector3 n = new Vector3(-dx * 2f, -dy * 2f, 1).normalized;
                normal[y*w+x] = new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
            }
            Save("Leather", colors, w, h, false); Save("LeatherNormal", normal, w, h, true);
        }

        private static void Save(string name, Color[] pixels, int width, int height, bool normal)
        {
            string path = Folder + "/" + name + ".png";
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, normal);
            texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture); AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal; importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp; importer.anisoLevel = 4;
            importer.maxTextureSize = 1024; importer.SaveAndReimport();
        }
    }
}
