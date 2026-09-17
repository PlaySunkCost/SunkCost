using System.Collections.Generic;
using SunkCost.Audio;
using SunkCost.Noise;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Noise and the sound library: the NoiseSettings and AudioLibrary assets in
    // Resources (Resources.Load finds them; the slots start empty = generated
    // placeholders) and PlayerNoise on the player prefab (the same in-place
    // patch the vitals setup uses). Re-run after HQPrototypeBuilder.RebuildPrefabs.
    public static class NoiseSetup
    {
        public const string NoiseSettingsPath = "Assets/_Project/Resources/NoiseSettings.asset";
        public const string AudioLibraryPath = "Assets/_Project/Resources/AudioLibrary.asset";

        [MenuItem("Sunk Cost/Prototype/Apply noise setup (footsteps, elevator, sound library)")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            var changes = new List<string>();
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Resources");
            if (AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoiseSettingsPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<NoiseSettings>(), NoiseSettingsPath);
                changes.Add("NoiseSettings created");
            }
            if (AssetDatabase.LoadAssetAtPath<AudioLibrary>(AudioLibraryPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AudioLibrary>(), AudioLibraryPath);
                changes.Add("AudioLibrary created (placeholder sounds until its slots are filled)");
            }
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (root.GetComponent<HQPlayerController>() == null) throw new System.InvalidOperationException("Player prefab needs HQPlayerController first.");
                if (root.GetComponent<PlayerNoise>() == null) { root.AddComponent<PlayerNoise>(); changes.Add("PlayerNoise added"); PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Noise already set up" : "Noise: " + string.Join("; ", changes);
        }
    }
}
