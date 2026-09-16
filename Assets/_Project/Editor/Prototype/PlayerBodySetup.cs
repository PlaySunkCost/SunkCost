using System;
using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The dead player's body prefab (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 1):
    // a copy of the basketball prefab (tested NetworkObject, NetworkTransform,
    // Rigidbody and CarryableItem wiring) turned into a lying, two-handed, heavy
    // body, registered as a spawnable so the server can drop one where a player died.
    public static class PlayerBodySetup
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Player/PlayerBody.prefab";
        public const string MaterialPath = HQPrototypeBuilder.MaterialPath + "/PlayerBody.mat";
        public const float LengthMeters = 1.7f, WidthMeters = 0.5f, HeightMeters = 0.3f, MassKg = 60f;

        [MenuItem("Sunk Cost/Prototype/Apply player body setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the player body setup.");
            var changes = new List<string>();
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Prefabs/Player");
            Material suit = HQPrototypeBuilder.GetOrCreateMaterial(MaterialPath, new Color(0.16f, 0.18f, 0.22f));
            Mesh capsule = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx") ?? Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            if (capsule == null) throw new InvalidOperationException("Unity's built-in capsule mesh was not found.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                if (!AssetDatabase.CopyAsset(HQPrototypeBuilder.BallPrefabPath, PrefabPath))
                    throw new InvalidOperationException("Could not copy the basketball prefab to " + PrefabPath);
                changes.Add("prefab created from the basketball");
            }
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var local = new List<string>();
                if (root.name != PlayerBody.PrefabName) { root.name = PlayerBody.PrefabName; local.Add("name"); }
                MeshFilter filter = root.GetComponent<MeshFilter>();
                if (filter.sharedMesh != capsule) { filter.sharedMesh = capsule; local.Add("mesh"); }
                // The capsule's long axis is Y; a body lies along Z: squash Y, stretch Z.
                Vector3 size = capsule.bounds.size;
                Vector3 scale = new(WidthMeters / size.x, HeightMeters / size.y, LengthMeters / size.z);
                if (root.transform.localScale != scale) { root.transform.localScale = scale; local.Add("scale"); }
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) { box = root.AddComponent<BoxCollider>(); local.Add("box collider"); }
                SphereCollider sphere = root.GetComponent<SphereCollider>();
                if (sphere != null) { Object.DestroyImmediate(sphere); local.Add("sphere collider removed"); }
                if (box.size != size || box.center != capsule.bounds.center) { box.size = size; box.center = capsule.bounds.center; local.Add("box size"); }
                Renderer renderer = root.GetComponent<Renderer>();
                if (renderer.sharedMaterial != suit) { renderer.sharedMaterial = suit; local.Add("material"); }
                Rigidbody body = root.GetComponent<Rigidbody>();
                if (!Mathf.Approximately(body.mass, MassKg)) { body.mass = MassKg; local.Add("mass"); }
                if (body.interpolation != RigidbodyInterpolation.Interpolate) { body.interpolation = RigidbodyInterpolation.Interpolate; local.Add("interpolation"); }
                if (root.GetComponent<PlayerBody>() == null) { root.AddComponent<PlayerBody>(); local.Add("PlayerBody"); }
                CarryableItem item = root.GetComponent<CarryableItem>();
                using (var serialized = new SerializedObject(item))
                {
                    Set(serialized, "displayName", p => p.stringValue != "Body", p => p.stringValue = "Body", local);
                    Set(serialized, "fitsInSlot", p => p.boolValue, p => p.boolValue = false, local);
                    Set(serialized, "grip", p => p.enumValueIndex != (int)CarryGrip.TwoHands, p => p.enumValueIndex = (int)CarryGrip.TwoHands, local);
                    Set(serialized, "useAction", p => p.enumValueIndex != 0, p => p.enumValueIndex = 0, local);
                    Set(serialized, "icon", p => p.objectReferenceValue != null, p => p.objectReferenceValue = null, local);
                    Set(serialized, "valueMin", p => p.intValue != 0, p => p.intValue = 0, local);
                    Set(serialized, "valueMax", p => p.intValue != 0, p => p.intValue = 0, local);
                    Set(serialized, "throwSpeed", p => !Mathf.Approximately(p.floatValue, 2f), p => p.floatValue = 2f, local);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (local.Count > 0) { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); changes.Add("prefab: " + string.Join(", ", local)); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var collection = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(HQPrototypeLootSetup.PrefabObjectsPath);
            if (collection == null) throw new InvalidOperationException("Prefab collection missing at " + HQPrototypeLootSetup.PrefabObjectsPath);
            NetworkObject nob = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<NetworkObject>();
            if (!HQPrototypeLootSetup.IsRegistered(collection, nob))
            {
                collection.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
                EditorUtility.SetDirty(collection);
                changes.Add("registered as a spawnable");
            }
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Player body already set up" : "Player body: " + string.Join("; ", changes);
        }

        private static void Set(SerializedObject serialized, string property, Func<SerializedProperty, bool> differs, Action<SerializedProperty> apply, List<string> changes)
        {
            SerializedProperty p = serialized.FindProperty(property);
            if (p == null) { Debug.LogWarning("PlayerBodySetup: CarryableItem has no property '" + property + "'."); return; }
            if (!differs(p)) return;
            apply(p);
            changes.Add(property);
        }
    }
}
