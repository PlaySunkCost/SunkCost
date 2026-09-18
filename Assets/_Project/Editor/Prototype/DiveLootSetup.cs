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
    // The dive site's loot: coins of three sizes lying on the seafloor between the
    // tube's doorway and the wreck (Dan, 16 September 2026: "put several coins
    // down, different sizes"; the balls are the HQ game's and carry no value).
    // Coin prefabs are copies of the basketball prefab with a cylinder mesh, a box
    // collider, a gold material and a value range; DiveSiteBuilder places them
    // through a LootFixtureSpawner so the server spawns them fresh on every site
    // load, and every dive re-rolls their values.
    public static class DiveLootSetup
    {
        public sealed class CoinType
        {
            public string PrefabName;
            public string DisplayName;
            public float DiameterMeters;
            public float ThicknessMeters;
            public float MassKg;
            public int ValueMin;
            public int ValueMax;
            public string PrefabPath => "Assets/_Project/Prefabs/Interaction/" + PrefabName + ".prefab";
        }

        public static readonly CoinType[] Coins =
        {
            new() { PrefabName = "CoinSmall", DisplayName = "Small coin", DiameterMeters = 0.16f, ThicknessMeters = 0.02f, MassKg = 0.4f, ValueMin = 10, ValueMax = 30 },
            new() { PrefabName = "CoinMedium", DisplayName = "Coin", DiameterMeters = 0.26f, ThicknessMeters = 0.03f, MassKg = 1.5f, ValueMin = 40, ValueMax = 90 },
            new() { PrefabName = "CoinLarge", DisplayName = "Large coin", DiameterMeters = 0.42f, ThicknessMeters = 0.05f, MassKg = 5f, ValueMin = 150, ValueMax = 300 },
        };

        public const string GoldMaterialPath = HQPrototypeBuilder.MaterialPath + "/Gold.mat";
        // The air tanks (AirTankItem; Dan, 17 September 2026): a small capsule with a
        // valve, safety-yellow while full, grey once breathed from. Two per site.
        public const string AirTankPrefabName = "AirTank";
        public const string AirTankPrefabPath = "Assets/_Project/Prefabs/Interaction/" + AirTankPrefabName + ".prefab";
        public const string AirTankFullMaterialPath = HQPrototypeBuilder.MaterialPath + "/AirTankFull.mat";
        public const string AirTankEmptyMaterialPath = HQPrototypeBuilder.MaterialPath + "/AirTankEmpty.mat";
        public const float AirTankDiameterMeters = 0.14f, AirTankLengthMeters = 0.42f, AirTankMassKg = 4f;
        public const string FixtureName = "Dive Loot";

        public static bool IsCoin(string prefabName)
        {
            foreach (CoinType coin in Coins) if (coin.PrefabName == prefabName) return true;
            return false;
        }

        // The largest coin's bounding radius: every coin's icon is framed to it.
        public static float IconFrameRadiusMeters
        {
            get
            {
                float radius = 0f;
                foreach (CoinType coin in Coins)
                    radius = Mathf.Max(radius, new Vector3(coin.DiameterMeters / 2f, coin.ThicknessMeters / 2f, coin.DiameterMeters / 2f).magnitude);
                return radius;
            }
        }

        // Where the coins lie, in the seafloor's frame: `along` metres out from the
        // tube's doorway along its bearing, `across` metres to the side. A trail
        // from the landing toward the wreck, one large coin at the far end.
        public struct Placement { public string Coin; public float Along; public float Across; public string Name; }
        public static readonly Placement[] Placements =
        {
            new() { Coin = "CoinSmall", Along = 5f, Across = 1.5f, Name = "Coin 1" },
            new() { Coin = "CoinSmall", Along = 7f, Across = -2f, Name = "Coin 2" },
            new() { Coin = "CoinMedium", Along = 9f, Across = 0.5f, Name = "Coin 3" },
            new() { Coin = "CoinSmall", Along = 12f, Across = 3f, Name = "Coin 4" },
            new() { Coin = "CoinMedium", Along = 15f, Across = -3f, Name = "Coin 5" },
            new() { Coin = "CoinMedium", Along = 19f, Across = 1f, Name = "Coin 6" },
            new() { Coin = "CoinLarge", Along = 24f, Across = -1f, Name = "Coin 7" },
            // Air tanks lie among the coins: one near the landing, one out by the wreck.
            new() { Coin = AirTankPrefabName, Along = 6f, Across = -4f, Name = "Air tank 1" },
            new() { Coin = AirTankPrefabName, Along = 21f, Across = 4f, Name = "Air tank 2" },
        };

        [MenuItem("Sunk Cost/Prototype/Apply dive loot setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply(bool rebuildSite = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the dive loot setup.");
            var changes = new List<string>();
            var prefabChanges = new List<string>(EnsureCoinPrefabs());
            prefabChanges.AddRange(EnsureAirTankPrefab());
            changes.AddRange(prefabChanges);
            changes.AddRange(RegisterPrefabs());
            changes.AddRange(EnsureCoinIcons(prefabChanges.Count > 0));
            AssetDatabase.SaveAssets();
            if (rebuildSite) { SunkCost.Sites.DiveSiteBuilder.CreateOrUpdate(); changes.Add("DiveSite01 rebuilt"); }
            return changes.Count == 0 ? "Dive loot already set up" : "Dive loot: " + string.Join("; ", changes);
        }

        public static Material GetOrCreateGoldMaterial() =>
            HQPrototypeBuilder.GetOrCreateMaterial(GoldMaterialPath, new Color(1f, 0.78f, 0.22f));

        // Copies of the basketball prefab (its NetworkObject, NetworkTransform,
        // Rigidbody and CarryableItem wiring are the tested ones) turned into coins.
        public static IEnumerable<string> EnsureCoinPrefabs()
        {
            var changes = new List<string>();
            Material gold = GetOrCreateGoldMaterial();
            // The primitive cylinder (1 wide, 2 tall); the legacy "Cylinder.fbx" is 2 wide.
            Mesh cylinder = Resources.GetBuiltinResource<Mesh>("New-Cylinder.fbx");
            if (cylinder == null) throw new InvalidOperationException("Unity's built-in cylinder mesh was not found.");
            Vector3 meshSize = cylinder.bounds.size;
            WeightSettings weight = AssetDatabase.LoadAssetAtPath<WeightSettings>(HQPrototypeLootSetup.WeightSettingsPath);
            foreach (CoinType coin in Coins)
            {
                bool created = false;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(coin.PrefabPath) == null)
                {
                    if (!AssetDatabase.CopyAsset(HQPrototypeBuilder.BallPrefabPath, coin.PrefabPath))
                        throw new InvalidOperationException("Could not copy the basketball prefab to " + coin.PrefabPath);
                    created = true;
                }
                var local = new List<string>();
                GameObject root = PrefabUtility.LoadPrefabContents(coin.PrefabPath);
                try
                {
                    if (root.name != coin.PrefabName) { root.name = coin.PrefabName; local.Add("name"); }
                    MeshFilter filter = root.GetComponent<MeshFilter>();
                    if (filter.sharedMesh != cylinder) { filter.sharedMesh = cylinder; local.Add("mesh"); }
                    // Scaled from the mesh's own size to the coin's diameter and thickness.
                    Vector3 scale = new(coin.DiameterMeters / meshSize.x, coin.ThicknessMeters / meshSize.y, coin.DiameterMeters / meshSize.z);
                    if (root.transform.localScale != scale) { root.transform.localScale = scale; local.Add("scale"); }
                    // The box first: CarryableItem requires a collider, so the sphere can
                    // only go once another one is there.
                    BoxCollider box = root.GetComponent<BoxCollider>();
                    if (box == null) { box = root.AddComponent<BoxCollider>(); local.Add("box collider"); }
                    SphereCollider sphere = root.GetComponent<SphereCollider>();
                    if (sphere != null) { Object.DestroyImmediate(sphere); local.Add("sphere collider removed"); }
                    Vector3 boxSize = meshSize, boxCentre = cylinder.bounds.center; // the mesh's own box, scaled with it
                    if (box.size != boxSize || box.center != boxCentre) { box.size = boxSize; box.center = boxCentre; local.Add("box size"); }
                    Renderer renderer = root.GetComponent<Renderer>();
                    if (renderer.sharedMaterial != gold) { renderer.sharedMaterial = gold; local.Add("material"); }
                    Rigidbody body = root.GetComponent<Rigidbody>();
                    if (!Mathf.Approximately(body.mass, coin.MassKg)) { body.mass = coin.MassKg; local.Add("mass"); }
                    if (body.interpolation != RigidbodyInterpolation.Interpolate) { body.interpolation = RigidbodyInterpolation.Interpolate; local.Add("interpolation"); }
                    CarryableItem item = root.GetComponent<CarryableItem>();
                    using (var serialized = new SerializedObject(item))
                    {
                        Set(serialized, "displayName", p => p.stringValue != coin.DisplayName, p => p.stringValue = coin.DisplayName, local);
                        Set(serialized, "fitsInSlot", p => !p.boolValue, p => p.boolValue = true, local);
                        Set(serialized, "grip", p => p.enumValueIndex != (int)CarryGrip.OneHand, p => p.enumValueIndex = (int)CarryGrip.OneHand, local);
                        Set(serialized, "useAction", p => p.enumValueIndex != (int)ItemUseAction.Throw, p => p.enumValueIndex = (int)ItemUseAction.Throw, local);
                        Set(serialized, "valueMin", p => p.intValue != coin.ValueMin, p => p.intValue = coin.ValueMin, local);
                        Set(serialized, "valueMax", p => p.intValue != coin.ValueMax, p => p.intValue = coin.ValueMax, local);
                        if (weight != null) Set(serialized, "weightSettings", p => p.objectReferenceValue != weight, p => p.objectReferenceValue = weight, local);
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    if (created || local.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, coin.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                if (created) changes.Add(coin.PrefabName + " created");
                else if (local.Count > 0) changes.Add(coin.PrefabName + " updated: " + string.Join(",", local));
            }
            return changes;
        }

        private static void Set(SerializedObject serialized, string name, Func<SerializedProperty, bool> differs, Action<SerializedProperty> apply, List<string> changes)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new InvalidOperationException("CarryableItem has no serialized field '" + name + "'.");
            if (!differs(property)) return;
            apply(property);
            changes.Add(name);
        }

        // The air tank: a copy of the basketball prefab like the coins, a capsule
        // with a small valve cube, safety yellow, 4 kg, one hand, a slot, no value,
        // AirTankItem for the breathe/throw state and the two names.
        public static IEnumerable<string> EnsureAirTankPrefab()
        {
            var changes = new List<string>();
            Material full = HQPrototypeBuilder.GetOrCreateMaterial(AirTankFullMaterialPath, new Color(0.95f, 0.75f, 0.12f));
            Material emptyMaterial = HQPrototypeBuilder.GetOrCreateMaterial(AirTankEmptyMaterialPath, new Color(0.45f, 0.46f, 0.48f));
            Mesh capsule = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx");
            if (capsule == null) throw new InvalidOperationException("The built-in capsule mesh was not found.");
            WeightSettings weight = AssetDatabase.LoadAssetAtPath<WeightSettings>(HQPrototypeLootSetup.WeightSettingsPath);
            bool created = false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(AirTankPrefabPath) == null)
            {
                if (!AssetDatabase.CopyAsset(HQPrototypeBuilder.BallPrefabPath, AirTankPrefabPath))
                    throw new InvalidOperationException("Could not copy the basketball prefab to " + AirTankPrefabPath);
                created = true;
            }
            // The empty icon is rendered before the prefab is opened for editing.
            string emptyIconPath = ItemIconGenerator.IconFolder + "/" + AirTankPrefabName + "Empty.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(emptyIconPath) == null && ItemIconGenerator.GenerateVariant(AirTankPrefabPath, AirTankPrefabName + "Empty", emptyMaterial) != null)
                changes.Add("empty tank icon rendered");
            var local = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(AirTankPrefabPath);
            try
            {
                if (root.name != AirTankPrefabName) { root.name = AirTankPrefabName; local.Add("name"); }
                MeshFilter filter = root.GetComponent<MeshFilter>();
                if (filter.sharedMesh != capsule) { filter.sharedMesh = capsule; local.Add("mesh"); }
                Vector3 meshSize = capsule.bounds.size; // 1 wide, 2 tall
                Vector3 scale = new(AirTankDiameterMeters / meshSize.x, AirTankLengthMeters / meshSize.y, AirTankDiameterMeters / meshSize.z);
                if (root.transform.localScale != scale) { root.transform.localScale = scale; local.Add("scale"); }
                CapsuleCollider capsuleCollider = root.GetComponent<CapsuleCollider>();
                if (capsuleCollider == null) { capsuleCollider = root.AddComponent<CapsuleCollider>(); local.Add("capsule collider"); }
                SphereCollider sphere = root.GetComponent<SphereCollider>();
                if (sphere != null) { Object.DestroyImmediate(sphere); local.Add("sphere collider removed"); }
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box != null) { Object.DestroyImmediate(box); local.Add("box collider removed"); }
                if (capsuleCollider.height != meshSize.y || capsuleCollider.radius != meshSize.x * 0.5f) { capsuleCollider.height = meshSize.y; capsuleCollider.radius = meshSize.x * 0.5f; capsuleCollider.center = capsule.bounds.center; local.Add("collider size"); }
                Renderer renderer = root.GetComponent<Renderer>();
                if (renderer.sharedMaterial != full) { renderer.sharedMaterial = full; local.Add("material"); }
                // The valve: a small dark cube on top, in the capsule's own (scaled) space.
                Transform valve = root.transform.Find("Valve");
                if (valve == null)
                {
                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Valve";
                    Object.DestroyImmediate(cube.GetComponent<Collider>());
                    cube.transform.SetParent(root.transform, false);
                    cube.transform.localPosition = new Vector3(0f, meshSize.y * 0.5f + 0.15f, 0f);
                    cube.transform.localScale = new Vector3(0.35f, 0.18f, 0.35f);
                    cube.GetComponent<Renderer>().sharedMaterial = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/PlayerHelmet.mat", new Color(0.16f, 0.17f, 0.19f));
                    local.Add("valve");
                }
                Rigidbody body = root.GetComponent<Rigidbody>();
                if (!Mathf.Approximately(body.mass, AirTankMassKg)) { body.mass = AirTankMassKg; local.Add("mass"); }
                CarryableItem item = root.GetComponent<CarryableItem>();
                using (var serialized = new SerializedObject(item))
                {
                    Set(serialized, "displayName", p => p.stringValue != AirTankItem.FullName, p => p.stringValue = AirTankItem.FullName, local);
                    Set(serialized, "fitsInSlot", p => !p.boolValue, p => p.boolValue = true, local);
                    Set(serialized, "grip", p => p.enumValueIndex != (int)CarryGrip.OneHand, p => p.enumValueIndex = (int)CarryGrip.OneHand, local);
                    Set(serialized, "useAction", p => p.enumValueIndex != (int)ItemUseAction.Breathe, p => p.enumValueIndex = (int)ItemUseAction.Breathe, local);
                    Set(serialized, "valueMin", p => p.intValue != 0, p => p.intValue = 0, local);
                    Set(serialized, "valueMax", p => p.intValue != 0, p => p.intValue = 0, local);
                    if (weight != null) Set(serialized, "weightSettings", p => p.objectReferenceValue != weight, p => p.objectReferenceValue = weight, local);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AirTankItem tank = root.GetComponent<AirTankItem>();
                if (tank == null) { tank = root.AddComponent<AirTankItem>(); local.Add("AirTankItem"); }
                using (var serialized = new SerializedObject(tank))
                {
                    SerializedProperty empty = serialized.FindProperty("emptyMaterial");
                    if (empty.objectReferenceValue != emptyMaterial) { empty.objectReferenceValue = emptyMaterial; local.Add("empty material"); }
                    SerializedProperty tinted = serialized.FindProperty("tinted");
                    if (tinted.arraySize != 1 || tinted.GetArrayElementAtIndex(0).objectReferenceValue != renderer) { tinted.arraySize = 1; tinted.GetArrayElementAtIndex(0).objectReferenceValue = renderer; local.Add("tinted"); }
                    // The empty tank's own inventory icon (Dan, 18 September 2026: the two
                    // looked the same in the slots): the prefab rendered in the empty look.
                    SerializedProperty emptyIcon = serialized.FindProperty("emptyIcon");
                    if (emptyIcon.objectReferenceValue == null)
                    {
                        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ItemIconGenerator.IconFolder + "/" + AirTankPrefabName + "Empty.png");
                        if (icon != null) { emptyIcon.objectReferenceValue = icon; local.Add("empty icon"); }
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (created || local.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, AirTankPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (created) changes.Add(AirTankPrefabName + " created");
            else if (local.Count > 0) changes.Add(AirTankPrefabName + " updated: " + string.Join(",", local));
            return changes;
        }

        // The coins and the air tank must be in the spawnable prefab collection, like the heavy balls.
        public static IEnumerable<string> RegisterPrefabs()
        {
            var collection = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(HQPrototypeLootSetup.PrefabObjectsPath);
            if (collection == null) throw new InvalidOperationException("Prefab collection missing at " + HQPrototypeLootSetup.PrefabObjectsPath);
            int added = 0;
            var paths = new List<string>();
            foreach (CoinType coin in Coins) paths.Add(coin.PrefabPath);
            paths.Add(AirTankPrefabPath);
            foreach (string path in paths)
            {
                NetworkObject nob = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>();
                if (HQPrototypeLootSetup.IsRegistered(collection, nob)) continue;
                collection.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
                added++;
            }
            if (added == 0) return Array.Empty<string>();
            EditorUtility.SetDirty(collection);
            return new[] { "prefab collection: " + added + " loot prefabs added" };
        }

        // A coin copied from the basketball carries the ball's icon until its own is
        // rendered (Dan: "the gold coins should have a different icon in the
        // inventory"). Rendered when missing or when a coin prefab changed; the icon
        // menu regenerates them all.
        public static IEnumerable<string> EnsureCoinIcons(bool prefabsChanged)
        {
            var stale = new List<string>();
            foreach (CoinType coin in Coins)
            {
                CarryableItem item = AssetDatabase.LoadAssetAtPath<GameObject>(coin.PrefabPath).GetComponent<CarryableItem>();
                if (prefabsChanged || item.Icon == null || item.Icon.name != coin.PrefabName) stale.Add(coin.PrefabPath);
            }
            CarryableItem tank = AssetDatabase.LoadAssetAtPath<GameObject>(AirTankPrefabPath)?.GetComponent<CarryableItem>();
            if (tank != null && (prefabsChanged || tank.Icon == null || tank.Icon.name != AirTankPrefabName)) stale.Add(AirTankPrefabPath);
            if (stale.Count == 0) return Array.Empty<string>();
            return new[] { "icons: " + ItemIconGenerator.Generate(stale) };
        }

        public static CoinType Coin(string prefabName)
        {
            foreach (CoinType coin in Coins) if (coin.PrefabName == prefabName) return coin;
            if (prefabName == AirTankPrefabName) return AirTankAsPlacement;
            throw new InvalidOperationException("No coin type named " + prefabName);
        }
        // The air tank stands on its end: its "thickness" for the floor offset is its length.
        private static readonly CoinType AirTankAsPlacement = new() { PrefabName = AirTankPrefabName, DisplayName = AirTankItem.FullName, DiameterMeters = AirTankDiameterMeters, ThicknessMeters = AirTankLengthMeters, MassKg = AirTankMassKg, ValueMin = 0, ValueMax = 0 };

        // World position of a placement: from the tube doorway's foot, out along the
        // doorway bearing (the builder's degrees, x = cos, z = sin), lying on the floor.
        public static Vector3 PlacementPosition(Placement placement, Vector3 doorwayFoot, float doorwayBearingDeg, float floorY)
        {
            float rad = doorwayBearingDeg * Mathf.Deg2Rad;
            Vector3 along = new(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 across = Vector3.Cross(Vector3.up, along);
            CoinType coin = Coin(placement.Coin);
            Vector3 p = doorwayFoot + along * placement.Along + across * placement.Across;
            return new Vector3(p.x, floorY + coin.ThicknessMeters / 2f + 0.02f, p.z);
        }
    }
}
