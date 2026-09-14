using System;
using System.Collections.Generic;
using System.IO;
using SunkCost.Interaction;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Renders every CarryableItem prefab to a small PNG and assigns it as the
    // item's slot picture (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md section 11).
    // Regenerating overwrites; hand-made icons can replace these later by simply
    // assigning a different texture on the prefab.
    public static class ItemIconGenerator
    {
        public const string PrefabFolder = "Assets/_Project/Prefabs/Interaction";
        public const string IconFolder = "Assets/_Project/Art/Prototype/ItemIcons";
        private const int Size = 128;

        [MenuItem("Sunk Cost/Prototype/Generate item icons")]
        public static void GenerateFromMenu()
        {
            Debug.Log(GenerateAll());
        }

        public static string GenerateAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before generating icons.");
            if (!AssetDatabase.IsValidFolder(IconFolder))
            {
                string parent = Path.GetDirectoryName(IconFolder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(IconFolder));
            }

            var results = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                CarryableItem item = prefab != null ? prefab.GetComponent<CarryableItem>() : null;
                if (item == null) continue;
                results.Add(Generate(prefab, item));
            }
            AssetDatabase.SaveAssets();
            return results.Count == 0 ? "No CarryableItem prefabs under " + PrefabFolder : string.Join("; ", results);
        }

        private static string Generate(GameObject prefab, CarryableItem item)
        {
            string pngPath = IconFolder + "/" + prefab.name + ".png";
            Texture2D rendered = RenderPreview(prefab) ?? WaitForAssetPreview(prefab);
            if (rendered == null) return prefab.name + ": no preview could be rendered";

            File.WriteAllBytes(pngPath, rendered.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(rendered);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            bool importChanged = false;
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; importChanged = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; importChanged = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; importChanged = true; }
            if (importer.maxTextureSize != Size) { importer.maxTextureSize = Size; importChanged = true; }
            if (importChanged) importer.SaveAndReimport();

            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            using var serialized = new SerializedObject(item);
            SerializedProperty iconProperty = serialized.FindProperty("icon");
            if (iconProperty.objectReferenceValue != icon)
            {
                iconProperty.objectReferenceValue = icon;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return prefab.name + " -> " + pngPath;
        }

        // Three-quarter view from 30 degrees above, framed on the renderer bounds,
        // through the scriptable pipeline so URP materials render correctly.
        private static Texture2D RenderPreview(GameObject prefab)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = 50f;
                preview.camera.fieldOfView = 30f;
                preview.lights[0].intensity = 1.4f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.ambientColor = new Color(0.3f, 0.3f, 0.3f);

                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                preview.AddSingleGO(instance);
                Bounds bounds = RendererBounds(instance);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
                Vector3 direction = Quaternion.Euler(30f, -35f, 0f) * Vector3.back;
                float distance = radius / Mathf.Sin(preview.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
                preview.camera.transform.position = bounds.center + direction * distance;
                preview.camera.transform.LookAt(bounds.center);

                preview.BeginStaticPreview(new Rect(0f, 0f, Size, Size));
                preview.Render(true, true);
                Texture2D result = preview.EndStaticPreview();
                // EndStaticPreview returns a texture owned by the utility; copy it so it
                // survives Cleanup and can be encoded.
                return result == null ? null : ReadableCopy(result);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Icon preview render failed, falling back to AssetPreview: " + exception.Message);
                return null;
            }
            finally { preview.Cleanup(); }
        }

        private static Texture2D WaitForAssetPreview(GameObject prefab)
        {
            Texture2D preview = null;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                preview = AssetPreview.GetAssetPreview(prefab);
                if (preview != null && !AssetPreview.IsLoadingAssetPreview(prefab.GetEntityId())) break;
                System.Threading.Thread.Sleep(25);
            }
            return preview == null ? null : ReadableCopy(preview);
        }

        // Copies through a RenderTexture so the source need not be CPU-readable.
        private static Texture2D ReadableCopy(Texture2D source)
        {
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Bounds RendererBounds(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(instance.transform.position, Vector3.one * 0.2f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
