using System.Collections.Generic;
using SunkCost.Player;
using SunkCost.Shop;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // The shop's data and the player's side of it (18 September 2026): the
    // catalogue asset in Resources (three items, the air tank prefab wired in)
    // and PlayerUpgrades on the player prefab — the same in-place patch the
    // vitals setup does. The shop room itself is built by HQPrototypeBuilder
    // (Create or Update HQ), which validates against this catalogue.
    public static class ShopSetup
    {
        public const string CatalogPath = "Assets/_Project/Resources/" + ShopCatalog.ResourceName + ".asset";

        [MenuItem("Sunk Cost/Prototype/Apply shop setup (catalogue and upgrades)")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            var changes = new List<string>();
            foreach (string change in DiveLootSetup.EnsureAirTankPrefab()) changes.Add(change);
            GameObject airTank = AssetDatabase.LoadAssetAtPath<GameObject>(DiveLootSetup.AirTankPrefabPath);
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Resources");
            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ShopCatalog>();
                catalog.ResetToDefaults(airTank);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                changes.Add("ShopCatalog created with the three items");
            }
            else
            {
                // An existing catalogue keeps Dan's edits; only a missing prefab is filled in.
                ShopItem tank = catalog.Find(ShopCatalog.AirTankId);
                if (tank != null && tank.Prefab == null && airTank != null) { tank.Prefab = airTank; EditorUtility.SetDirty(catalog); changes.Add("air tank prefab wired"); }
            }

            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (root.GetComponent<HQPlayerController>() == null) throw new System.InvalidOperationException("Player prefab needs HQPlayerController first.");
                if (root.GetComponent<PlayerUpgrades>() == null) { root.AddComponent<PlayerUpgrades>(); changes.Add("PlayerUpgrades added"); PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Shop already set up" : "Shop: " + string.Join("; ", changes);
        }

        // Play-testing: $500 in the pot on the running host (Sunk Cost/Prototype menu
        // or the command file's `run`), so the shop can be tried without a payday.
        [MenuItem("Sunk Cost/Prototype/Give the crew $500 (Play Mode, host)")]
        public static string GiveCrewMoney()
        {
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            if (day == null || !day.IsServerStarted) return "Host a session first.";
            day.ServerSetBalanceForChecks(day.Balance + 500);
            Debug.Log("[Shop] +$500 for testing; pot $" + day.Balance);
            return "pot $" + day.Balance;
        }
    }
}
