using FishNet.Connection;
using SunkCost.Shop;
using UnityEngine;

namespace SunkCost.World
{
    // The catalogue console's paging on the server (Dan, 8 October 2026; docs/DESIGN.md
    // "The shop"): one shared page per console (no operator lock, last press wins). The
    // PREV / NEXT zones ask for an ABSOLUTE page, so two same-frame NEXTs agree; the
    // lever turns the page and wraps, dim on a one-page catalogue. Buying is the
    // unchanged ServerBuy (WorldSceneFlow.Shop) with the reach test on this rig; the page
    // shown is presentation only and never a condition of a buy.
    public sealed partial class WorldSceneFlow
    {
        private static int ServerShopPages()
        {
            ShopCatalog catalog = ShopCatalog.Resolve();
            return ShopConsoleRules.PageCount(catalog != null ? catalog.Items.Count : 0);
        }

        // E on PREV / NEXT: the page wanted.
        public bool ServerShopSetPage(NetworkConnection sender, int page, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (!ServerPresserAtRig(sender, ConsoleKind.Shop, out why)) return false;
            if (page < 0 || page >= ServerShopPages()) { why = ConsoleRules.NoSuchPage; return false; }
            dayState.ServerSetShopPage(page);
            return true;
        }

        // The shop lever: NEXT PAGE, dim when the catalogue fits one page.
        internal LeverAction ServerResolveShopLever(out bool enabled, out string reason)
            => ShopConsoleRules.ShopLever(ServerShopPages(), out enabled, out reason);

        // The pull: the next page, wrapping round.
        internal bool ServerShopNextPage(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            int pages = ServerShopPages();
            if (pages <= 1) { why = ConsoleRules.OnePage; return false; }
            int next = (Mathf.Clamp(dayState.ShopPage, 0, pages - 1) + 1) % pages;
            dayState.ServerSetShopPage(next);
            Debug.Log($"[Console] {DisplayName(sender)} turned the catalogue to page {next + 1}/{pages}");
            return true;
        }
    }
}
