using System.Collections.Generic;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Air and health on the player prefab: the PlayerVitals component and its
    // settings asset (the same in-place patch the loot setup uses for
    // PlayerIdentity — the prefab is not regenerated). Re-run after
    // HQPrototypeBuilder.RebuildPrefabs.
    public static class PlayerVitalsSetup
    {
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/PlayerVitalsSettings.asset";

        [MenuItem("Sunk Cost/Prototype/Apply vitals setup (air and health)")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            var changes = new List<string>();
            PlayerVitalsSettings settings = EnsureSettings(changes);
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (root.GetComponent<HQPlayerController>() == null) throw new System.InvalidOperationException("Player prefab needs HQPlayerController first.");
                PlayerVitals vitals = root.GetComponent<PlayerVitals>();
                if (vitals == null) { vitals = root.AddComponent<PlayerVitals>(); changes.Add("PlayerVitals added"); }
                using var serialized = new SerializedObject(vitals);
                SerializedProperty property = serialized.FindProperty("settings");
                if (property.objectReferenceValue != settings) { property.objectReferenceValue = settings; serialized.ApplyModifiedPropertiesWithoutUndo(); changes.Add("settings assigned"); }
                if (changes.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Vitals already set up" : "Vitals: " + string.Join("; ", changes);
        }

        public static PlayerVitalsSettings EnsureSettings(List<string> changes)
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Settings/Prototype");
            PlayerVitalsSettings settings = AssetDatabase.LoadAssetAtPath<PlayerVitalsSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PlayerVitalsSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            changes?.Add("PlayerVitalsSettings created");
            return settings;
        }
    }
}
