using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.World
{
    // The five destinations as data (the shared console, 27 September 2026; Dan:
    // "placeholder prices, to be discussed", "each site's picture is its own editable
    // setting"): the card's name, the bottom screen's sub line and placeholder
    // description, the picture (one shared cyan wreck silhouette for now), the unlock
    // price of Site02..04 and where each one's ship actually sails until its own scene
    // exists (RoutesTo Site01). Not replicated: both sides read the same asset, the
    // server's copy decides the price. Assets/_Project/Resources/SiteCatalog.asset;
    // without it a hidden instance with the defaults answers, so code never nulls.
    [CreateAssetMenu(menuName = "Sunk Cost/Site catalog", fileName = "SiteCatalog")]
    public sealed class SiteCatalog : ScriptableObject
    {
        public const string ResourceName = "SiteCatalog";          // Assets/_Project/Resources/SiteCatalog.asset
        public const int DefaultPriceSite02 = 100, DefaultPriceSite03 = 200, DefaultPriceSite04 = 300;

        [System.Serializable]
        public sealed class Entry
        {
            public SiteId Id;
            public string Name = "SITE 02";                        // the card's word; Label(Id) by default
            public string SubName = string.Empty;                  // second line on the bottom screen ("" for Site 01: game facts only)
            public Texture2D Picture;                              // null = the catalogue's SharedPicture
            [TextArea(1, 3)] public string[] Description = System.Array.Empty<string>(); // placeholder lines for a locked site ("SITE 02 · uncharted")
            [Min(0)] public int UnlockPrice;                       // 0 for HQ/Site01
            public SiteId RoutesTo = SiteId.None;                  // None = itself; Site02..04 → Site01 until built
        }

        [SerializeField] private List<Entry> entries = new();
        [SerializeField] private Texture2D sharedPicture;          // the one cyan wreck silhouette for now

        public IReadOnlyList<Entry> Entries => entries;
        public Texture2D SharedPicture => sharedPicture;

        public Entry Find(SiteId id)
        {
            foreach (Entry entry in entries) if (entry != null && entry.Id == id) return entry;
            return null;
        }

        // The entry's name, or the plain label when the asset has none.
        public string NameOf(SiteId id)
        {
            Entry entry = Find(id);
            return entry != null && !string.IsNullOrEmpty(entry.Name) ? entry.Name : Destinations.Label(id);
        }

        // 0 when the site is not purchasable or absent.
        public int UnlockPrice(SiteId id)
        {
            if (!Destinations.IsPurchasable(id)) return 0;
            Entry entry = Find(id);
            return entry != null ? Mathf.Max(0, entry.UnlockPrice) : DefaultPrice(id);
        }

        // RoutesTo, or the id itself when None. The LOGICAL id stays `id` (the HERE
        // marker, the sailing line); only the world/scene follows the route.
        public SiteId RouteOf(SiteId id)
        {
            Entry entry = Find(id);
            return entry != null && entry.RoutesTo != SiteId.None ? entry.RoutesTo : id;
        }

        // The entry's picture, else the shared one (may be null in a bare fallback).
        public Texture2D PictureOf(SiteId id)
        {
            Entry entry = Find(id);
            return entry != null && entry.Picture != null ? entry.Picture : sharedPicture;
        }

        public static int DefaultPrice(SiteId id)
        {
            switch (id)
            {
                case SiteId.Site02: return DefaultPriceSite02;
                case SiteId.Site03: return DefaultPriceSite03;
                case SiteId.Site04: return DefaultPriceSite04;
                default: return 0;
            }
        }

        // Adds every missing entry with its defaults; never overwrites Dan's values. Returns whether anything was added.
        public bool EnsureDefaults()
        {
            bool changed = false;
            foreach (SiteId id in Destinations.Cards)
            {
                if (Find(id) != null) continue;
                var entry = new Entry { Id = id, Name = Destinations.Label(id), UnlockPrice = DefaultPrice(id) };
                if (Destinations.IsPurchasable(id))
                {
                    entry.Description = new[] { Destinations.Label(id) + " · uncharted" };
                    entry.RoutesTo = SiteId.Site01;
                }
                entries.Add(entry);
                changed = true;
            }
            return changed;
        }

        private static SiteCatalog loaded;
        private static SiteCatalog fallback;

        // The asset in Resources, else a hidden instance with the defaults (the asset may
        // appear after the first ask: the console-model setup creates it).
        public static SiteCatalog Resolve()
        {
            if (loaded != null) return loaded;
            loaded = Resources.Load<SiteCatalog>(ResourceName);
            if (loaded != null) return loaded;
            if (fallback == null) { fallback = CreateInstance<SiteCatalog>(); fallback.hideFlags = HideFlags.HideAndDontSave; fallback.EnsureDefaults(); }
            return fallback;
        }
    }
}
