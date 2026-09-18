using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Shop;
using UnityEngine;

namespace SunkCost.Player
{
    // What this player bought at the shop (docs/DESIGN.md §8, built 18
    // September 2026): the upgrade bits, server-written, one of each; and the
    // buyer's side of the counter — E on a ShopDisplay asks the server to sell.
    // Consumables never touch this: they are spawned as items. Upgrades are
    // lost with an unrescued body (WorldSceneFlow.ServerReviveAll) and with the
    // player object (a leaver's are gone with it, like everything else today).
    public sealed class PlayerUpgrades : NetworkBehaviour
    {
        private const float RefusalSeconds = 2.5f;

        private readonly SyncVar<byte> owned = new(0);
        private HQPlayerController controller;
        private string refusal = string.Empty;
        private float refusalUntil;

        public PlayerUpgrade Owned => (PlayerUpgrade)owned.Value;
        public bool Has(PlayerUpgrade upgrade) => upgrade != PlayerUpgrade.None && (Owned & upgrade) == upgrade;
        // The tank's size against the plain one (PlayerVitals reads it on the server).
        public float TankMultiplier => Has(PlayerUpgrade.LargeTank) ? ShopCatalog.Resolve().LargeTankMultiplier : 1f;
        // The last refusal from the counter, for the owner's prompt.
        public string Refusal => Time.unscaledTime < refusalUntil ? refusal : string.Empty;

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            owned.OnChange += OnOwnedChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply();
        }

        private void OnOwnedChanged(byte previous, byte next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer
            Apply();
        }

        // Every peer shows what it can: the headlamp's beam (others see it too).
        private void Apply()
        {
            if (controller == null) return;
            ShopCatalog catalog = ShopCatalog.Resolve();
            bool bright = Has(PlayerUpgrade.BrightHeadlamp);
            controller.SetHeadlampUpgrade(bright ? catalog.BrightHeadlampRange : 1f, bright ? catalog.BrightHeadlampIntensity : 1f);
        }

        // ---- the counter (owner) ----------------------------------------------------

        // E on a shop display: ask the server to sell the item it names.
        public void RequestBuy(string itemId)
        {
            if (!IsOwner || string.IsNullOrEmpty(itemId)) return;
            refusalUntil = 0f; // a new press: the old answer is stale
            ServerRequestBuy(itemId);
        }

        [ServerRpc]
        private void ServerRequestBuy(string itemId, NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            if (flow == null) { TargetRefuse(sender, "No shop"); return; }
            if (!flow.ServerBuy(sender, itemId, out string why)) TargetRefuse(sender, why);
        }

        [TargetRpc]
        private void TargetRefuse(NetworkConnection connection, string why)
        {
            refusal = why ?? string.Empty;
            refusalUntil = Time.unscaledTime + RefusalSeconds;
        }

        // ---- server --------------------------------------------------------------------

        [Server]
        public void ServerGrant(PlayerUpgrade upgrade)
        {
            if (upgrade == PlayerUpgrade.None) return;
            byte next = (byte)(owned.Value | (byte)upgrade);
            if (next != owned.Value) owned.Value = next;
        }

        // Dying costs you everything you bought unless the body comes back (design §8).
        [Server]
        public void ServerClearAll()
        {
            if (owned.Value != 0) owned.Value = 0;
        }
    }
}
