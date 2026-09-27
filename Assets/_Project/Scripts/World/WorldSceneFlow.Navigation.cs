using FishNet.Connection;
using UnityEngine;

namespace SunkCost.World
{
    // The ship's navigation console on the server (the shared console, 27 September
    // 2026): the selection, the site unlock transaction and CONFIRM. Foundation body
    // by console_state, written against the interfaces; the ship agent owns this file
    // in the Implement phase and may extend it (a per-site world once real sites
    // exist). Every method is server-side; the dispatcher (WorldSceneFlow.Console)
    // has checked the server, the day state and the presser's location before
    // calling ServerUnlockSite / ServerConfirmSail.
    public sealed partial class WorldSceneFlow
    {
        // E on a card: the crew's selection. Any player aboard may change it; the same
        // card again is not a change and not a refusal.
        public bool ServerSelectSite(NetworkConnection sender, SiteId site, out string why)
        {
            why = string.Empty;
            if (!Destinations.IsCard(site)) { why = ConsoleRules.NoSuchDestination; return false; }
            if (!ServerPresserAboard(sender, out why)) return false;
            if (dayState.Phase == DayPhase.Plank) { why = ConsoleRules.RunOver; return false; }
            if (transitioning || dayState.Travelling) { why = ConsoleRules.Travelling; return false; }
            if (dayState.SelectedSite == site) return true;
            dayState.ServerSetSelectedSite(site);
            Debug.Log($"[Console] {DisplayName(sender)} selects {site}");
            return true;
        }

        // The lever's current action on the ship, from the server's facts (pure).
        internal LeverAction ServerResolveShipLever(out SiteId target, out bool enabled, out string reason)
            => ConsoleRules.ShipLever(ServerFacts(), SiteCatalog.Resolve(), out target, out enabled, out reason);

        // UNLOCK: the price leaves the pot and the site's bit goes into the mask in ONE
        // server call — the server is single-threaded, so two pulls in one tick cannot
        // both charge: the second finds the site open ("Already open"). Any order, any
        // time (after a dive too); the price is the catalogue's (serialized, Dan's to
        // change). Saved at once when docked (every save writes at HQ only).
        public bool ServerUnlockSite(NetworkConnection sender, SiteId site, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (!Destinations.IsPurchasable(site)) { why = ConsoleRules.NoSuchDestination; return false; }
            if (dayState.IsOpen(site)) { why = ConsoleRules.AlreadyOpen; return false; }
            if (dayState.Phase == DayPhase.Plank) { why = ConsoleRules.RunOver; return false; }
            int price = SiteCatalog.Resolve().UnlockPrice(site);
            if (dayState.Balance < price) { why = ConsoleRules.ShortBy(price - dayState.Balance); return false; }
            if (!dayState.ServerSpend(price)) { why = ConsoleRules.ShortBy(price - dayState.Balance); return false; }
            if (!dayState.ServerUnlockSite(site)) { why = ConsoleRules.AlreadyOpen; return false; } // cannot happen: tested open above in this same call
            Debug.Log($"[Console] {DisplayName(sender)} unlocked {site} for ${price}; pot ${dayState.Balance}");
            ServerSaveRun("unlocked " + site);
            return true;
        }

        // CONFIRM: the existing sail, to the world the site routes to (Site02..04 →
        // Site01's world until they are built) with the LOGICAL site riding along.
        public bool ServerConfirmSail(NetworkConnection sender, SiteId target, out string why)
        {
            why = string.Empty;
            if (!Destinations.IsCard(target)) { why = ConsoleRules.SelectFirst; return false; }
            WorldId to = Destinations.WorldOf(SiteCatalog.Resolve().RouteOf(target));
            return ServerSail(to, target, out why);
        }
    }
}
