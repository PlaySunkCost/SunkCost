using System;
using System.Collections.Generic;
using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.Sites;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // Targeted, repeatable setup for docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md: the
    // cabin water on the car prefab, the submersion indicator on the player
    // prefab (both patched in place, GUIDs kept), then the dive site regenerated
    // by its builder (the tube, the gate leaves, the water surface, the volume
    // top). Run twice: the prefabs report no change; the scene is rebuilt again.
    public static class ShaftTubeSetup
    {
        public const string CabinWaterSurfaceName = "Cabin Water";

        [MenuItem("Sunk Cost/Prototype/Apply shaft tube setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply(bool rebuildSite = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the shaft tube setup.");
            var changes = new List<string>();
            changes.AddRange(PatchCarPrefab());
            changes.AddRange(PatchPlayerPrefab());
            AssetDatabase.SaveAssets();
            if (rebuildSite)
            {
                DiveSiteBuilder.CreateOrUpdate();
                changes.Add("DiveSite01 rebuilt");
            }
            return changes.Count == 0 ? "Shaft tube already set up" : "Shaft tube: " + string.Join("; ", changes);
        }

        private static IEnumerable<string> PatchCarPrefab()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(ElevatorCabinBuilder.PrefabPath);
            try
            {
                DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
                string change = AddCabinWater(root, settings);
                if (change != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, ElevatorCabinBuilder.PrefabPath);
                    changes.Add(change);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }

        // Also called by ElevatorCabinBuilder while it generates the prefab: the
        // CabinWater component wired to the car's controller and its disc, a thin
        // cylinder just inside the wall ring, inactive until the car is under.
        // Returns what changed, or null when the prefab already had it all.
        public static string AddCabinWater(GameObject root, DiveSiteSettings settings)
        {
            var changes = new List<string>();
            ElevatorController controller = root.GetComponent<ElevatorController>();
            if (controller == null) throw new InvalidOperationException("Elevator prefab has no ElevatorController.");
            CabinWater water = root.GetComponent<CabinWater>();
            if (water == null) { water = root.AddComponent<CabinWater>(); changes.Add("CabinWater added"); }
            Transform surface = root.transform.Find(CabinWaterSurfaceName);
            if (surface == null)
            {
                float radius = (settings != null ? settings.CarDiameterMeters / 2f : 2.5f) - 0.17f; // just inside the wall ring
                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = CabinWaterSurfaceName;
                disc.transform.SetParent(root.transform, false);
                disc.transform.localPosition = Vector3.zero;
                disc.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
                disc.GetComponent<Renderer>().sharedMaterial = DiveSiteBuilder.GetOrCreateWaterSurfaceMaterial();
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.SetActive(false);
                surface = disc.transform;
                changes.Add("cabin water disc added");
            }
            SerializedObject serialized = new(water);
            if (serialized.FindProperty("controller").objectReferenceValue != controller) { serialized.FindProperty("controller").objectReferenceValue = controller; changes.Add("CabinWater controller wired"); }
            if (serialized.FindProperty("surface").objectReferenceValue != surface) { serialized.FindProperty("surface").objectReferenceValue = surface; changes.Add("CabinWater disc wired"); }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return changes.Count == 0 ? null : string.Join(", ", changes);
        }

        private static IEnumerable<string> PatchPlayerPrefab()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (root.GetComponent<PlayerSubmersion>() == null)
                {
                    root.AddComponent<PlayerSubmersion>();
                    PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
                    changes.Add("PlayerSubmersion added");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }
    }
}
