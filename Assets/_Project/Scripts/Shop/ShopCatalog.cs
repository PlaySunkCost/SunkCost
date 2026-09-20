using System;
using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Shop
{
    // What an upgrade is: a bit on the player (PlayerUpgrades), one each, lost
    // with an unrescued body (docs/DESIGN.md §8).
    [Flags]
    public enum PlayerUpgrade : byte
    {
        None = 0,
        LargeTank = 1,      // +50 % air (PlayerVitals reads the multiplier)
        BrightHeadlamp = 2  // a longer, stronger beam (HQPlayerController applies it)
    }

    public enum ShopItemKind : byte
    {
        Consumable = 0, // a real item, spawned at the shop's delivery point
        Upgrade = 1     // a flag on the buyer
    }

    [Serializable]
    public sealed class ShopItem
    {
        [Tooltip("The id a ShopDisplay names (stable: the art pass keeps it).")]
        public string Id = "air-tank";
        public string Name = "Air tank";
        [Min(0)] public int Price = 40;
        public ShopItemKind Kind = ShopItemKind.Consumable;
        [Tooltip("Consumable: the networked prefab spawned on purchase.")]
        public GameObject Prefab;
        [Tooltip("Upgrade: which flag the buyer gets.")]
        public PlayerUpgrade Upgrade = PlayerUpgrade.None;
    }

    // The shop's catalogue: data, so the art pass changes prices, names and
    // prefabs here and the stands anywhere (docs/DESIGN.md §8, built 18
    // September 2026 — Dan: "in the future I will build the shop, new look, new
    // place, so it needs to be built good for it"). Lives in Resources like the
    // noise settings; the server reads it for every purchase, clients for the
    // display labels and prompts.
    [CreateAssetMenu(menuName = "Sunk Cost/Shop catalog", fileName = "ShopCatalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        public const string ResourceName = "ShopCatalog";
        public const string AirTankId = "air-tank", LargeTankId = "large-tank", BrightHeadlampId = "bright-headlamp";
        public const string PatchKitId = "patch-kit"; // the monsters, 20 September 2026: one use, closes your own leak
        public const int PatchKitPrice = 60;

        [SerializeField] private List<ShopItem> items = new();
        [Tooltip("How much more air the large tank holds (1.5 = +50 %).")]
        [SerializeField, Min(1f)] private float largeTankMultiplier = 1.5f;
        [Tooltip("The bright headlamp's beam range and intensity, as multipliers of the plain lamp.")]
        [SerializeField, Min(1f)] private float brightHeadlampRange = 1.6f;
        [SerializeField, Min(1f)] private float brightHeadlampIntensity = 1.5f;

        public IReadOnlyList<ShopItem> Items => items;
        public float LargeTankMultiplier => largeTankMultiplier;
        public float BrightHeadlampRange => brightHeadlampRange;
        public float BrightHeadlampIntensity => brightHeadlampIntensity;

        public ShopItem Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (ShopItem item in items) if (item != null && item.Id == id) return item;
            return null;
        }

        // The first cut's three items (Dan, 18 September 2026): an air tank to
        // carry down, the large tank, the bright headlamp — $40 / $300 / $150.
        public void ResetToDefaults(GameObject airTankPrefab, GameObject patchKitPrefab = null)
        {
            items = new List<ShopItem>
            {
                new() { Id = AirTankId, Name = "Air tank", Price = 40, Kind = ShopItemKind.Consumable, Prefab = airTankPrefab },
                new() { Id = LargeTankId, Name = "Large tank", Price = 300, Kind = ShopItemKind.Upgrade, Upgrade = PlayerUpgrade.LargeTank },
                new() { Id = BrightHeadlampId, Name = "Bright headlamp", Price = 150, Kind = ShopItemKind.Upgrade, Upgrade = PlayerUpgrade.BrightHeadlamp },
                new() { Id = PatchKitId, Name = "Patch kit", Price = PatchKitPrice, Kind = ShopItemKind.Consumable, Prefab = patchKitPrefab },
            };
            largeTankMultiplier = 1.5f;
            brightHeadlampRange = 1.6f;
            brightHeadlampIntensity = 1.5f;
        }

        // The patch kit's row, added to an existing catalogue that predates it
        // (the setup; Dan's edits to the other rows are kept). True if added or wired.
        public bool EnsurePatchKit(GameObject patchKitPrefab)
        {
            ShopItem row = Find(PatchKitId);
            if (row == null)
            {
                items.Add(new ShopItem { Id = PatchKitId, Name = "Patch kit", Price = PatchKitPrice, Kind = ShopItemKind.Consumable, Prefab = patchKitPrefab });
                return true;
            }
            if (row.Prefab == null && patchKitPrefab != null) { row.Prefab = patchKitPrefab; return true; }
            return false;
        }

        private static ShopCatalog loaded;
        private static bool looked;
        public static ShopCatalog Resolve()
        {
            if (loaded != null) return loaded;
            if (!looked) { looked = true; loaded = Resources.Load<ShopCatalog>(ResourceName); }
            if (loaded == null) { loaded = CreateInstance<ShopCatalog>(); loaded.hideFlags = HideFlags.HideAndDontSave; loaded.ResetToDefaults(null); }
            return loaded;
        }

        // What an upgrade means for a player who has it.
        public static string UpgradeMark(PlayerUpgrade upgrade) => upgrade switch
        {
            PlayerUpgrade.LargeTank => "L-TANK",
            PlayerUpgrade.BrightHeadlamp => "LAMP",
            _ => string.Empty
        };
    }
}
