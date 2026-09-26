using System;
using System.IO;
using SunkCost.Editor.Prototype;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Look
{
    // The generated kit is visual-only. Layout supplies simple, deliberate collision
    // volumes; a portal/window must never acquire a solid bounding-box collider.
    public static class HQModelSetup
    {
        public const string Models = "Assets/_Project/Models/HQ";
        public const string Prefabs = "Assets/_Project/Prefabs/HQ";
        public static readonly string[] Parts = { "LegSection", "Girder", "Brace", "Fascia", "WallPanel", "Pillar", "Portal", "RoofCassette", "Window", "DisplayPanel", "DisplayHook", "Shelf", "TankCradle", "Pedestal", "CagePanel", "Counter", "PickupChute", "IntakeHopper", "ArrivalGantry", "BasketHoop", "CourtFence", "Fender", "UtilityCabinet", "Pallet" };

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            HQPrototypeBuilder.EnsureFolder(Prefabs);
            foreach (string part in Parts)
            {
                string path = $"{Models}/{part}/{part}.fbx";
                if (!File.Exists(path)) throw new FileNotFoundException("Prepare the HQ kit first", path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importBlendShapes = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.bakeAxisConversion = false;
                importer.isReadable = false;
                importer.SaveAndReimport();
                Texture2D colour = ShipModelSetup.Import($"{Models}/{part}/Maps/{part}_BaseColor.jpg", TextureImporterType.Default, true);
                Texture2D normal = ShipModelSetup.Import($"{Models}/{part}/Maps/{part}_Normal.png", TextureImporterType.NormalMap, false);
                Material mat = HQPrototypeBuilder.GetOrCreateMaterial($"{Models}/{part}/{part}.mat", Color.white);
                mat.SetTexture("_BaseMap", colour); mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Smoothness", .2f);
                mat.enableInstancing = true;
                GameObject root = new(part);
                try
                {
                    GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                    model.transform.SetParent(root.transform, false);
                    foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/{part}.prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            return "24 HQ models imported with URP materials and reusable visual prefabs.";
        }
    }
}
