using SunkCost.Shop;
using UnityEngine;

namespace SunkCost.World
{
    // The catalogue console's pure table (Dan, 8 October 2026: "the whole catalogue is
    // painted on the console's screens"): how the catalogue is paged, what each row
    // says, and what the lever does. No FishNet type here: the server pages with it,
    // every peer draws from it, and the edit-mode tests pin it. Three rows a page so
    // the four-item catalogue always has two pages (paging is live on the real data).
    public static class ShopConsoleRules
    {
        public const int RowsPerPage = 3;
        public const string StateOwned = "OWNED", StateNoMoney = "NO MONEY", StateBuy = "BUY";
        public const string EmptyRow = "-";

        public static int PageCount(int items) => Mathf.Max(1, (items + RowsPerPage - 1) / RowsPerPage);
        public static int ClampPage(int page, int items) => Mathf.Clamp(page, 0, PageCount(items) - 1);
        public static int ItemIndex(int page, int row) => page * RowsPerPage + row;

        // The item on that page's row, or null beyond the catalogue (or without one).
        public static ShopItem ItemAt(ShopCatalog catalog, int page, int row)
        {
            if (catalog == null || page < 0 || row < 0 || row >= RowsPerPage) return null;
            int i = ItemIndex(page, row);
            return i >= 0 && i < catalog.Items.Count ? catalog.Items[i] : null;
        }

        public static string ItemIdAt(int page, int row) => ItemAt(ShopCatalog.Resolve(), page, row)?.Id;

        // OWNED for an upgrade `local` already has; NO MONEY when the pot is short; else BUY.
        public static string RowState(ShopItem item, Player.PlayerUpgrades local, int balance)
        {
            if (item == null) return string.Empty;
            if (item.Kind == ShopItemKind.Upgrade && local != null && local.Has(item.Upgrade)) return StateOwned;
            return balance < item.Price ? StateNoMoney : StateBuy;
        }

        public static string RowLine(ShopItem item, string state) => item == null ? EmptyRow : $"{item.Name} ${item.Price} {state}";

        // The page's rows joined by "|", an empty row "-": "Patch kit $60 BUY|-|-".
        public static string RowsText(ShopCatalog catalog, int page, Player.PlayerUpgrades local, int balance)
        {
            var parts = new string[RowsPerPage];
            for (int r = 0; r < RowsPerPage; r++)
            {
                ShopItem item = ItemAt(catalog, page, r);
                parts[r] = item == null ? EmptyRow : RowLine(item, RowState(item, local, balance));
            }
            return string.Join("|", parts);
        }

        // The shop lever turns the page (wrapping); dim on a one-page catalogue.
        public static LeverAction ShopLever(int pages, out bool enabled, out string reason)
        {
            enabled = pages > 1;
            reason = enabled ? string.Empty : ConsoleRules.OnePage;
            return LeverAction.NextPage;
        }

        public static string PageText(int page, int pages) => $"PAGE {page + 1}/{pages}";
    }
}
