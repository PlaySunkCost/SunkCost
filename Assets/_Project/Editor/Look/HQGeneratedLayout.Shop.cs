using SunkCost.Shop;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    public static partial class HQGeneratedLayout
    {
        // The shop (Dan, 8 October 2026, BRIEF B.1–B.3): the shop console IS the shop.
        // The catalogue console stands where the old catalogue counter stood, at (-6, 14)
        // facing the aisle, on its steel footing inside the hazard square the counter had,
        // under the same work light; the catalogue is painted on its screens and a row's
        // E buys (ShopConsoleBuilder, HQShopConsole). Gone: the hanging EQUIPMENT CATALOGUE
        // / BROWSE & BUY sign and its mast (the words are on the screens now), the four
        // featured item stands and their ShopDisplays (buying happens only through the
        // console), and the OFFICE / UPGRADES / GEAR & SUPPLIES boards over the fronts
        // (they pointed at the displays). The four decorative Counter ledges stay in
        // Depot (furniture). `delivery`, `tank` and `lamp` are unused now: the chute is
        // found by ServerBuy itself (ShopDeliveryPoint.Nearest) and the stands' materials
        // have nothing to paint; the parameters stay so Depot's call does not change.
        internal static void Shop(GameObject depot, ShopDeliveryPoint delivery, Material tank, Material lamp)
        {
            ShopConsoleBuilder.Place(depot, new Vector3(-6, 0, 14), 180);
            Frame(depot, new Vector3(-6, 0, 14), 2.8f, 2.4f);
            Lamp(depot, new Vector3(-6, 2.4f, 13.1f), 5, 2.5f);
        }
    }
}
