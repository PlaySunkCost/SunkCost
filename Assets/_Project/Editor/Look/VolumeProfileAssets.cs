using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Editor.Look
{
    // A VolumeProfile built by code keeps its overrides only if each one is saved
    // inside the profile asset. VolumeProfile.Add creates them in memory, and a
    // profile saved without them holds {fileID: 0} entries (ship audit SHIP-021:
    // the dive's grade never existed). Worse, such a profile can later carry a null
    // entry in memory, and URP's build step reads every profile in Assets: its
    // ShaderBuildPreprocessor threw on one (VolumeProfile.TryGet, 23 September
    // 2026), and the build compiled with scriptable shader stripping off - 7,776 Lit
    // variants per pass instead of 1,296, 1 h 53 min instead of about 9.
    public static class VolumeProfileAssets
    {
        // Call after the builder has added and set the profile's overrides.
        public static void SaveOverrides(VolumeProfile profile)
        {
            if (profile == null) return;
            profile.components.RemoveAll(component => component == null);
            if (AssetDatabase.Contains(profile))
                foreach (VolumeComponent component in profile.components)
                    if (!AssetDatabase.Contains(component))
                    {
                        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; // as URP's own profile editor adds them
                        AssetDatabase.AddObjectToAsset(component, profile);
                    }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        // Removes missing overrides from every profile in Assets (what URP's build step
        // reads). Returns how many profiles changed. Safe to run before any build.
        public static int RemoveMissingOverrides()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets" }))
            {
                VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || profile.components.RemoveAll(component => component == null) == 0) continue;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                changed++;
            }
            return changed;
        }
    }
}
