namespace SunkCost.World
{
    // The shared console (Dan, 27 September 2026; docs/DESIGN.md "The navigation
    // console"): the types every console file compiles against. A card on the ship's
    // navigation screen is a SiteId; HQ and Site01 are always open, Site02..04 are
    // bought at the console. Named SiteId, not Destination: CrewDayState.Destination
    // is the existing WorldId property and an enum of that name would not resolve there.
    public enum SiteId : byte { None = 0, HQ = 1, Site01 = 2, Site02 = 3, Site03 = 4, Site04 = 5 }

    // Which physical console a press comes from. Intent only, like `WorldId to` in
    // RequestSail: the server checks the presser's location per kind (the docked ship
    // and the HQ console share the HQ scene, so the kind cannot be inferred).
    public enum ConsoleKind : byte { Ship = 0, HQ = 1 }

    // What the lever does right now. None = the sign is dim and a pull is refused.
    public enum LeverAction : byte { None = 0, Confirm = 1, Unlock = 2, EndDay = 3, Pay = 4 }

    // What an aimed collider on a console is (ConsoleControl).
    public enum ConsoleControlKind : byte { Card = 0, Lever = 1, GiveUpCard = 2 }

    // One accepted pull, server-written on CrewDayState; every rig swings its lever
    // from it (the serial idiom, tick-anchored like the departure stages). A joiner's
    // initial value is never played.
    public struct LeverPull
    {
        public int Serial;          // 0 = never pulled
        public ConsoleKind Kind;
        public LeverAction Action;
        public uint Tick;           // the server tick the pull was accepted on (TimeManager.Tick)
    }

    public static class Destinations
    {
        // The five cards in reading order (left to right for the reader).
        public static readonly SiteId[] Cards = { SiteId.HQ, SiteId.Site01, SiteId.Site02, SiteId.Site03, SiteId.Site04 };

        public static bool IsCard(SiteId id) => id >= SiteId.HQ && id <= SiteId.Site04;
        public static bool IsSite(SiteId id) => id >= SiteId.Site01;
        public static bool IsPurchasable(SiteId id) => id >= SiteId.Site02;
        public static int Bit(SiteId id) => 1 << (int)id;

        // Open now: HQ and Site01 always; a purchasable site when its bit is in the mask.
        public static bool IsOpen(SiteId id, int unlockedMask) => id == SiteId.HQ || id == SiteId.Site01 || (IsPurchasable(id) && (unlockedMask & Bit(id)) != 0);

        // The world a destination's ship lives in. Every site is the one sea world until
        // real site scenes exist (the extension point: a per-site WorldId on SiteCatalog.Entry).
        public static WorldId WorldOf(SiteId id) => id == SiteId.HQ ? WorldId.HQ : WorldId.Sea;

        // The identity a world implies when a sail is asked by world alone (the compat
        // paths: RequestSail(WorldId), the hooks' ServerSail, the peer's `sail`).
        public static SiteId SiteOf(WorldId world) => world == WorldId.HQ ? SiteId.HQ : SiteId.Site01;

        // "HQ", "SITE 01" ... (the screens); "" for None.
        public static string Label(SiteId id)
        {
            switch (id)
            {
                case SiteId.HQ: return "HQ";
                case SiteId.Site01: return "SITE 01";
                case SiteId.Site02: return "SITE 02";
                case SiteId.Site03: return "SITE 03";
                case SiteId.Site04: return "SITE 04";
                default: return string.Empty;
            }
        }

        // "HQ", "Site 01" ... (the compat status lines the checks read).
        public static string TitleCase(SiteId id)
        {
            switch (id)
            {
                case SiteId.HQ: return "HQ";
                case SiteId.Site01: return "Site 01";
                case SiteId.Site02: return "Site 02";
                case SiteId.Site03: return "Site 03";
                case SiteId.Site04: return "Site 04";
                default: return string.Empty;
            }
        }

        // "HQ", "Site01", "SITE 02", "site02", "2" are all accepted; case-insensitive; spaces ignored.
        public static bool TryParse(string text, out SiteId id)
        {
            id = SiteId.None;
            if (string.IsNullOrEmpty(text)) return false;
            string key = text.Replace(" ", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
            switch (key)
            {
                case "HQ": case "1": id = SiteId.HQ; return true;
                case "SITE01": case "SITE1": case "2": id = SiteId.Site01; return true;
                case "SITE02": case "SITE2": case "3": id = SiteId.Site02; return true;
                case "SITE03": case "SITE3": case "4": id = SiteId.Site03; return true;
                case "SITE04": case "SITE4": case "5": id = SiteId.Site04; return true;
                case "NONE": case "0": id = SiteId.None; return true;
                default: return false;
            }
        }

        // "none" or "Site02+Site04" (ascending), for the hooks and the guest snapshot.
        public static string MaskText(int unlockedMask)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (SiteId id in Cards)
                if (IsPurchasable(id) && (unlockedMask & Bit(id)) != 0) parts.Add(id.ToString());
            return parts.Count == 0 ? "none" : string.Join("+", parts);
        }
    }
}
