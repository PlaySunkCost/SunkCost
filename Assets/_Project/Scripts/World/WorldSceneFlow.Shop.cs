using FishNet.Connection;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.Shop;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The shop at HQ (docs/DESIGN.md §8; Dan, 18 September 2026): a room of
    // things on display, look at one and press E to buy it from the crew's pot.
    // The server decides everything here — the world, the buyer, the reach,
    // the money — and either spawns the consumable at the shop's delivery point
    // (it falls to the floor; anyone may take it) or sets the upgrade bit on
    // the buyer (PlayerUpgrades; one each; lost with an unrescued body).
    public sealed partial class WorldSceneFlow
    {
        private const float ShopReachMargin = 1.5f; // the server sees a copy a tick behind the owner

        public bool ServerBuy(NetworkConnection conn, string itemId, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null || conn == null) { why = "No day state."; return false; }
            HQPlayerController buyer = PlayerOf(conn);
            if (buyer == null) { why = "No player."; return false; }
            if (buyer.IsDead || dayState.IsDead(conn.ClientId)) { why = "The dead buy nothing"; return false; }
            if (transitioning || dayState.Travelling) { why = "Ship travelling"; return false; }
            if (dayState.Phase == DayPhase.Plank) { why = "The run is over"; return false; }
            Scene hq = WorldScenes.Scene(WorldId.HQ);
            if (currentWorld != WorldId.HQ || !hq.IsValid() || !hq.isLoaded || buyer.gameObject.scene != hq) { why = "The shop is at HQ"; return false; }
            ShopCatalog catalog = ShopCatalog.Resolve();
            ShopItem item = catalog.Find(itemId);
            if (item == null) { why = "No such item"; return false; }
            ShopDisplay display = ServerNearestDisplay(itemId, hq, buyer.EyePosition, out float distance);
            if (display == null || distance > buyer.InteractReach + ShopReachMargin) { why = "Step up to the shelf"; return false; }
            PlayerUpgrades upgrades = buyer.Upgrades;
            if (item.Kind == ShopItemKind.Upgrade)
            {
                if (upgrades == null) { why = "No upgrades on the player"; return false; }
                if (upgrades.Has(item.Upgrade)) { why = "You already have a " + item.Name.ToLowerInvariant(); return false; }
            }
            else if (item.Prefab == null || item.Prefab.GetComponent<NetworkObject>() == null) { why = "Nothing to sell (no prefab)"; return false; }
            if (dayState.Balance < item.Price) { why = $"Not enough money: ${item.Price} needed, ${dayState.Balance} in the pot"; return false; }
            if (!dayState.ServerSpend(item.Price)) { why = "Not enough money"; return false; }

            if (item.Kind == ShopItemKind.Upgrade)
            {
                upgrades.ServerGrant(item.Upgrade);
            }
            else
            {
                ShopDeliveryPoint delivery = display.DeliveryPoint;
                Vector3 at = delivery != null ? delivery.DropPosition : display.transform.position + display.transform.forward * 0.8f + Vector3.up * 0.3f;
                GameObject instance = Instantiate(item.Prefab, at, Quaternion.identity);
                instance.name = item.Name + " (bought)";
                CarryableItem carryable = instance.GetComponent<CarryableItem>();
                if (carryable != null) carryable.SetResetPositionBeforeSpawn(at);
                networkManager.ServerManager.Spawn(instance.GetComponent<NetworkObject>(), null, hq); // into the HQ scene, not the session scene
            }
            Debug.Log($"[Shop] {DisplayName(conn)} bought {item.Name} for ${item.Price}; pot ${dayState.Balance}");
            return true;
        }

        // The stand for this item nearest the buyer's eyes, in the HQ scene.
        private static ShopDisplay ServerNearestDisplay(string itemId, Scene scene, Vector3 from, out float distance)
        {
            ShopDisplay best = null;
            distance = float.MaxValue;
            foreach (ShopDisplay display in FindObjectsByType<ShopDisplay>(FindObjectsInactive.Exclude))
            {
                if (display.gameObject.scene != scene || display.ItemId != itemId) continue;
                Collider collider = display.GetComponentInChildren<Collider>();
                Vector3 point = collider != null ? collider.ClosestPoint(from) : display.transform.position;
                float d = Vector3.Distance(from, point);
                if (d < distance) { best = display; distance = d; }
            }
            return best;
        }
    }
}
