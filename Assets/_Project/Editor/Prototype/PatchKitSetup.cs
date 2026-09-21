using System;
using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using SunkCost.Interaction;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The patch kit prefab (the monsters, 20 September 2026): a copy of the
    // basketball prefab like the air tank — a small red box, 1 kg, one hand, a
    // slot, no value, PatchKitItem for the one use and the two names — and its
    // place in the spawnable collection. The catalogue row is ShopSetup's; the
    // stand is HQPlatformBuilder's.
    public static class PatchKitSetup
    {
        public const string PrefabName = "PatchKit";
        public const string PrefabPath = "Assets/_Project/Prefabs/Interaction/" + PrefabName + ".prefab";
        public const string MaterialPath = HQPrototypeBuilder.MaterialPath + "/PatchKit.mat";
        public const string UsedMaterialPath = HQPrototypeBuilder.MaterialPath + "/PatchKitUsed.mat";
        public static readonly Color KitColour = new(0.85f, 0.18f, 0.14f);
        public static readonly Vector3 SizeMeters = new(0.26f, 0.12f, 0.18f);
        public const float MassKg = 1f;

        [MenuItem("Sunk Cost/Prototype/Apply patch kit setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before applying the patch kit setup.");
            var changes = new List<string>(EnsurePrefab());
            changes.AddRange(Register());
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Patch kit already set up" : "Patch kit: " + string.Join("; ", changes);
        }

        public static IEnumerable<string> EnsurePrefab()
        {
            var changes = new List<string>();
            Material fresh = HQPrototypeBuilder.GetOrCreateMaterial(MaterialPath, KitColour);
            Material usedMaterial = HQPrototypeBuilder.GetOrCreateMaterial(UsedMaterialPath, new Color(0.45f, 0.42f, 0.40f));
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cube == null) throw new InvalidOperationException("The built-in cube mesh was not found.");
            WeightSettings weight = AssetDatabase.LoadAssetAtPath<WeightSettings>(HQPrototypeLootSetup.WeightSettingsPath);
            bool created = false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                if (!AssetDatabase.CopyAsset(HQPrototypeBuilder.BallPrefabPath, PrefabPath))
                    throw new InvalidOperationException("Could not copy the basketball prefab to " + PrefabPath);
                created = true;
            }
            var local = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if (root.name != PrefabName) { root.name = PrefabName; local.Add("name"); }
                MeshFilter filter = root.GetComponent<MeshFilter>();
                if (filter.sharedMesh != cube) { filter.sharedMesh = cube; local.Add("mesh"); }
                Vector3 scale = SizeMeters; // the cube is 1 m
                if (root.transform.localScale != scale) { root.transform.localScale = scale; local.Add("scale"); }
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) { box = root.AddComponent<BoxCollider>(); local.Add("box collider"); }
                SphereCollider sphere = root.GetComponent<SphereCollider>();
                if (sphere != null) { Object.DestroyImmediate(sphere); local.Add("sphere collider removed"); }
                CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
                if (capsule != null) { Object.DestroyImmediate(capsule); local.Add("capsule collider removed"); }
                if (box.size != Vector3.one) { box.size = Vector3.one; box.center = Vector3.zero; local.Add("collider size"); }
                Renderer renderer = root.GetComponent<Renderer>();
                if (renderer.sharedMaterial != fresh) { renderer.sharedMaterial = fresh; local.Add("material"); }
                // A white cross on the lid so it reads as a kit.
                if (root.transform.Find("Cross") == null)
                {
                    foreach (Vector3 bar in new[] { new Vector3(0.6f, 0.06f, 0.18f), new Vector3(0.18f, 0.06f, 0.6f) })
                    {
                        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        part.name = "Cross";
                        Object.DestroyImmediate(part.GetComponent<Collider>());
                        part.transform.SetParent(root.transform, false);
                        part.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                        part.transform.localScale = bar;
                        part.GetComponent<Renderer>().sharedMaterial = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/PatchKitCross.mat", new Color(0.95f, 0.95f, 0.95f));
                    }
                    local.Add("cross");
                }
                Rigidbody body = root.GetComponent<Rigidbody>();
                if (!Mathf.Approximately(body.mass, MassKg)) { body.mass = MassKg; local.Add("mass"); }
                CarryableItem item = root.GetComponent<CarryableItem>();
                using (var serialized = new SerializedObject(item))
                {
                    Set(serialized, "displayName", p => p.stringValue != PatchKitItem.FullName, p => p.stringValue = PatchKitItem.FullName, local);
                    Set(serialized, "fitsInSlot", p => !p.boolValue, p => p.boolValue = true, local);
                    Set(serialized, "grip", p => p.enumValueIndex != (int)CarryGrip.OneHand, p => p.enumValueIndex = (int)CarryGrip.OneHand, local);
                    Set(serialized, "useAction", p => p.enumValueIndex != (int)ItemUseAction.Patch, p => p.enumValueIndex = (int)ItemUseAction.Patch, local);
                    Set(serialized, "valueMin", p => p.intValue != 0, p => p.intValue = 0, local);
                    Set(serialized, "valueMax", p => p.intValue != 0, p => p.intValue = 0, local);
                    if (weight != null) Set(serialized, "weightSettings", p => p.objectReferenceValue != weight, p => p.objectReferenceValue = weight, local);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PatchKitItem kit = root.GetComponent<PatchKitItem>();
                if (kit == null) { kit = root.AddComponent<PatchKitItem>(); local.Add("PatchKitItem"); }
                using (var serialized = new SerializedObject(kit))
                {
                    SerializedProperty used = serialized.FindProperty("usedMaterial");
                    if (used.objectReferenceValue != usedMaterial) { used.objectReferenceValue = usedMaterial; local.Add("used material"); }
                    SerializedProperty tinted = serialized.FindProperty("tinted");
                    if (tinted.arraySize != 1 || tinted.GetArrayElementAtIndex(0).objectReferenceValue != renderer) { tinted.arraySize = 1; tinted.GetArrayElementAtIndex(0).objectReferenceValue = renderer; local.Add("tinted"); }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (created || local.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (created) changes.Add(PrefabName + " created");
            else if (local.Count > 0) changes.Add(PrefabName + " updated: " + string.Join(",", local));
            // Its own inventory icon, and the used one, once the prefab exists.
            CarryableItem saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<CarryableItem>();
            if (saved != null && (saved.Icon == null || saved.Icon.name != PrefabName)) changes.Add("icons: " + ItemIconGenerator.Generate(new[] { PrefabPath }));
            string usedIconPath = ItemIconGenerator.IconFolder + "/" + PrefabName + "Used.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(usedIconPath) == null && ItemIconGenerator.GenerateVariant(PrefabPath, PrefabName + "Used", usedMaterial) != null)
                changes.Add("used kit icon rendered");
            Texture2D usedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(usedIconPath);
            if (usedIcon != null)
            {
                GameObject again = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var serialized = new SerializedObject(again.GetComponent<PatchKitItem>());
                    SerializedProperty icon = serialized.FindProperty("usedIcon");
                    if (icon.objectReferenceValue != usedIcon) { icon.objectReferenceValue = usedIcon; serialized.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(again, PrefabPath); changes.Add("used icon wired"); }
                }
                finally { PrefabUtility.UnloadPrefabContents(again); }
            }
            return changes;
        }

        public static IEnumerable<string> Register()
        {
            var collection = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(HQPrototypeLootSetup.PrefabObjectsPath);
            if (collection == null) throw new InvalidOperationException("Prefab collection missing at " + HQPrototypeLootSetup.PrefabObjectsPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return Array.Empty<string>();
            NetworkObject nob = prefab.GetComponent<NetworkObject>();
            if (HQPrototypeLootSetup.IsRegistered(collection, nob)) return Array.Empty<string>();
            collection.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
            EditorUtility.SetDirty(collection);
            return new[] { "prefab collection: patch kit added" };
        }

        private static void Set(SerializedObject serialized, string name, Func<SerializedProperty, bool> differs, Action<SerializedProperty> apply, List<string> changes)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new InvalidOperationException("CarryableItem has no serialized field '" + name + "'.");
            if (!differs(property)) return;
            apply(property);
            changes.Add(name);
        }
    }
}
