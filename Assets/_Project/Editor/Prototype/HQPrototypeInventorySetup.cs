using System;
using System.Collections.Generic;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    // Targeted asset upgrade for the holding/inventory work
    // (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md section 10). Idempotent; every
    // write goes through SerializedObject or PrefabUtility so the diff is only what
    // changed. Never regenerates the room, player or ball.
    public static class HQPrototypeInventorySetup
    {
        // One original ball plus four more so "slots full" and "overflow" can be
        // exercised with the only carryable prefab that exists.
        public const int SceneItemCount = 5;

        private static readonly Vector3[] ExtraBallPositions =
        {
            new(1.5f, 1f, 1.5f),
            new(-1.5f, 1f, 1.5f),
            new(1.5f, 1f, -1.5f),
            new(-1.5f, 1f, -1.5f)
        };

        [MenuItem("Sunk Cost/Prototype/Apply inventory setup")]
        public static void ApplyFromMenu()
        {
            Debug.Log(Apply());
        }

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the inventory setup.");
            string player = ApplyPlayerPrefab();
            string ball = ApplyBallPrefab();
            string scene = ApplySceneItems();
            AssetDatabase.SaveAssets();
            return player + "; " + ball + "; " + scene;
        }

        private static string ApplyPlayerPrefab()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                HQPlayerController controller = root.GetComponent<HQPlayerController>();
                if (controller == null) throw new InvalidOperationException("Player prefab has no HQPlayerController.");
                using var serialized = new SerializedObject(controller);
                var camera = serialized.FindProperty("playerCamera").objectReferenceValue as Camera;
                var hold = serialized.FindProperty("holdPoint").objectReferenceValue as Transform;
                if (camera == null || hold == null) throw new InvalidOperationException("Player prefab is missing its camera or hold point reference.");

                if (hold.parent != camera.transform)
                {
                    hold.SetParent(camera.transform, false);
                    changes.Add("HoldPoint re-parented under PlayerCamera");
                }
                if (hold.localPosition != HQPrototypeBuilder.HoldPointLocalPosition || hold.localRotation != Quaternion.identity)
                {
                    hold.localPosition = HQPrototypeBuilder.HoldPointLocalPosition;
                    hold.localRotation = Quaternion.identity;
                    changes.Add("HoldPoint moved to " + HQPrototypeBuilder.HoldPointLocalPosition);
                }
                if (root.GetComponent<PlayerInventory>() == null)
                {
                    root.AddComponent<PlayerInventory>();
                    changes.Add("PlayerInventory added");
                }
                if (root.GetComponent<PlayerHudUI>() == null)
                {
                    root.AddComponent<PlayerHudUI>();
                    changes.Add("PlayerHudUI added");
                }
                if (changes.Count > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes.Count == 0 ? "Player prefab already set up" : "Player prefab: " + string.Join(", ", changes);
        }

        private static string ApplyBallPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath);
            CarryableItem item = prefab != null ? prefab.GetComponent<CarryableItem>() : null;
            if (item == null) throw new InvalidOperationException("Basketball prefab has no CarryableItem at " + HQPrototypeBuilder.BallPrefabPath);
            using var serialized = new SerializedObject(item);
            var changes = new List<string>();
            SerializedProperty displayName = serialized.FindProperty("displayName");
            if (displayName.stringValue != "Basketball") { displayName.stringValue = "Basketball"; changes.Add("displayName"); }
            SerializedProperty fits = serialized.FindProperty("fitsInSlot");
            if (!fits.boolValue) { fits.boolValue = true; changes.Add("fitsInSlot"); }
            SerializedProperty use = serialized.FindProperty("useAction");
            if (use.enumValueIndex != (int)ItemUseAction.Throw) { use.enumValueIndex = (int)ItemUseAction.Throw; changes.Add("useAction=Throw"); }
            if (changes.Count == 0) return "Ball prefab already set up";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return "Ball prefab: " + string.Join(", ", changes);
        }

        private static string ApplySceneItems()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != HQPrototypeBuilder.ScenePath)
                throw new InvalidOperationException("Open " + HQPrototypeBuilder.ScenePath + " before applying the inventory setup (active: " + scene.path + ").");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath);
            if (prefab == null) throw new InvalidOperationException("Basketball prefab missing.");

            var items = new List<CarryableItem>();
            foreach (GameObject root in scene.GetRootGameObjects())
                items.AddRange(root.GetComponentsInChildren<CarryableItem>(true));
            int existing = items.Count;

            int added = 0;
            for (int i = existing; i < SceneItemCount; i++)
            {
                Vector3 position = ExtraBallPositions[(i - 1) % ExtraBallPositions.Length];
                var ball = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                ball.name = "Basketball (" + (i + 1) + ")";
                ball.transform.SetPositionAndRotation(position, Quaternion.identity);
                using var serialized = new SerializedObject(ball.GetComponent<CarryableItem>());
                serialized.FindProperty("resetPosition").vector3Value = position;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(ball.transform);
                items.Add(ball.GetComponent<CarryableItem>());
                added++;
            }
            // Repair existing instances too. Scene prefab names need an explicit
            // override; setting GameObject.name alone did not survive the save.
            int renamed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                string expected = i == 0 ? "Basketball" : "Basketball (" + (i + 1) + ")";
                if (items[i].name == expected) continue;
                using var serialized = new SerializedObject(items[i].gameObject);
                serialized.FindProperty("m_Name").stringValue = expected;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(items[i].gameObject);
                renamed++;
            }
            if (added == 0 && renamed == 0) return "Scene already has uniquely named carryable items";
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Unity could not save " + scene.path);
            return "Scene: added " + added + ", renamed " + renamed + " basketball instances";
        }
    }
}
