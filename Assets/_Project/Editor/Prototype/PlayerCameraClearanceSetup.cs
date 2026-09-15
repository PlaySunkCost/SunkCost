using System;
using System.Collections.Generic;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Targeted, repeatable setup for docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md:
    // the camera settings asset, the explicit near clip plane and the
    // PlayerCameraClearance component on the player prefab, patched in place
    // (GUIDs, the character model and scene overrides stay). Run twice: no
    // duplicates, no changes.
    public static class PlayerCameraClearanceSetup
    {
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/PlayerCameraSettings.asset";

        [MenuItem("Sunk Cost/Prototype/Apply camera clearance setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the camera clearance setup.");
            var changes = new List<string>();
            PlayerCameraSettings settings = EnsureSettings(changes);
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (PatchPlayer(root, settings, changes))
                    PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Camera clearance already set up" : "Camera clearance: " + string.Join("; ", changes);
        }

        public static PlayerCameraSettings EnsureSettings(List<string> changes)
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Settings/Prototype");
            PlayerCameraSettings settings = AssetDatabase.LoadAssetAtPath<PlayerCameraSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PlayerCameraSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            changes?.Add("PlayerCameraSettings created");
            return settings;
        }

        // Shared with the builder's fresh prefab. Returns true when something changed.
        public static bool PatchPlayer(GameObject root, PlayerCameraSettings settings, List<string> changes)
        {
            bool changed = false;
            Transform viewPivot = root.transform.Find("ViewPivot");
            Transform cameraTransform = viewPivot != null ? viewPivot.Find("PlayerCamera") : null;
            Camera camera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
            if (camera == null) throw new InvalidOperationException("Player prefab has no ViewPivot/PlayerCamera.");

            if (!Mathf.Approximately(camera.nearClipPlane, settings.NearClip))
            {
                camera.nearClipPlane = settings.NearClip;
                changes.Add("near clip " + settings.NearClip);
                changed = true;
            }
            if (cameraTransform.localPosition != Vector3.zero)
            {
                cameraTransform.localPosition = Vector3.zero; // the clearance offset is applied at runtime only
                changes.Add("camera recentred on the ViewPivot");
                changed = true;
            }

            PlayerCameraClearance clearance = root.GetComponent<PlayerCameraClearance>();
            if (clearance == null) { clearance = root.AddComponent<PlayerCameraClearance>(); changes.Add("PlayerCameraClearance added"); changed = true; }
            SerializedObject serialized = new(clearance);
            changed |= Assign(serialized, "playerCamera", camera, changes);
            changed |= Assign(serialized, "viewPivot", viewPivot, changes);
            changed |= Assign(serialized, "settings", settings, changes);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        private static bool Assign(SerializedObject serialized, string property, UnityEngine.Object value, List<string> changes)
        {
            SerializedProperty p = serialized.FindProperty(property);
            if (p.objectReferenceValue == value) return false;
            p.objectReferenceValue = value;
            changes.Add(property + " assigned");
            return true;
        }
    }
}
