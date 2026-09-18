using SunkCost.Look;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SunkCost.Editor.Look
{
    // The look kit's setup steps that touch assets outside the HQ scene: the
    // player camera's post-processing (the bloom that makes the strips glow), the
    // signs asset, and the props' forced rebuild.
    public static class LookSetup
    {
        public const string SignsPath = "Assets/_Project/Resources/HQSigns.asset";

        // The player camera renders post-processing: bloom for the emissive strips
        // and the beacon, tonemapping, the dive site's own grade (built 15 September
        // 2026 for a camera that never had post-processing on — it shows now).
        public static bool PatchPlayerCamera()
        {
            string path = Prototype.HQPrototypeBuilder.PlayerPrefabPath;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera == null) return false;
                UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                bool changed = false;
                if (!data.renderPostProcessing) { data.renderPostProcessing = true; changed = true; }
                if (data.antialiasing != AntialiasingMode.SubpixelMorphologicalAntiAliasing) { data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; changed = true; }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                return changed;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The signs asset in Resources with every default line present.
        public static HQSigns EnsureSigns()
        {
            HQSigns signs = AssetDatabase.LoadAssetAtPath<HQSigns>(SignsPath);
            if (signs == null)
            {
                signs = ScriptableObject.CreateInstance<HQSigns>();
                signs.EnsureDefaults();
                AssetDatabase.CreateAsset(signs, SignsPath);
            }
            else if (signs.EnsureDefaults()) EditorUtility.SetDirty(signs);
            return signs;
        }

        // The lamps are point lights, a hundred of them: the PC pipeline asset must
        // render additional lights per pixel (LightRenderingMode.PerPixel is 1,
        // PerVertex 2). Forward+ takes the count; the per-object limit is moot.
        public static bool EnsurePipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (asset == null) return false;
            var serialized = new SerializedObject(asset);
            bool changed = false;
            SerializedProperty mode = serialized.FindProperty("m_AdditionalLightsRenderingMode");
            if (mode != null && mode.intValue != (int)LightRenderingMode.PerPixel) { mode.intValue = (int)LightRenderingMode.PerPixel; changed = true; }
            SerializedProperty limit = serialized.FindProperty("m_AdditionalLightsPerObjectLimit");
            if (limit != null && limit.intValue < 8) { limit.intValue = 8; changed = true; }
            if (changed) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets(); }
            return changed;
        }

        [MenuItem("Sunk Cost/Look/Rebuild look props (loses edits to the prop prefabs)")]
        public static void RebuildProps()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
            PropBuilder.Force = true;
            try { Prototype.HQPrototypeBuilder.CreateOrUpdate(); }
            finally { PropBuilder.Force = false; }
            Debug.Log("Look props rewritten from code and the HQ rebuilt.");
        }

        [MenuItem("Sunk Cost/Look/Regenerate textures and materials")]
        public static void RegenerateTextures()
        {
            ProceduralTextures.GenerateAll();
            AssetDatabase.SaveAssets();
            Debug.Log("Look textures regenerated under " + ProceduralTextures.Folder);
        }
    }
}
