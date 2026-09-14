using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using SunkCost.Player;
using SunkCost.Net;
using UnityEngine;

namespace SunkCost.Interaction
{
    // Four slots plus the hands, per player (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md
    // section 6). The server owns the slots and makes every decision; the client
    // sends requests through the ServerRequest... RPCs and reads the result from
    // the replicated slots and the items' own state. What is "in the hands" is the
    // CarryableItem whose state is Held with this connection as holder, derived
    // from the item's SyncVars (NotifyHeld), never from a reply.
    [RequireComponent(typeof(HQPlayerController))]
    public sealed class PlayerInventory : NetworkBehaviour, INetworkDebugInfo
    {
        private const float ReachTolerance = 0.75f;
        private const float RefusalSeconds = 1.5f;

        private readonly SyncVar<InventorySlots> slots = new(InventorySlots.None);

        private HQPlayerController player;
        private CarryableItem heldItem; // owner-side view
        private string refusal = string.Empty;
        private float refusalUntil;

        public InventorySlots Slots => slots.Value;
        public CarryableItem HeldItem => heldItem;
        public int HeldSlot => heldItem == null ? -1 : slots.Value.IndexOf(heldItem.ObjectId);
        public bool HoldingOverflow => heldItem != null && HeldSlot < 0;
        public string Refusal => Time.unscaledTime < refusalUntil ? refusal : string.Empty;
        public bool? WriterOverride => null;
        public string DebugStatus => "slots=" + slots.Value;

        private void Awake()
        {
            player = GetComponent<HQPlayerController>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
                if (item.IsHeld && item.HolderClientId == Owner.ClientId) heldItem = item;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            slots.Value = InventorySlots.None;
            ServerManager.OnRemoteConnectionState += ServerOnRemoteConnectionState;
        }

        public override void OnStopServer()
        {
            ServerManager.OnRemoteConnectionState -= ServerOnRemoteConnectionState;
            base.OnStopServer();
        }

        // Called by CarryableItem from its SyncVar callbacks on the owning client.
        internal void NotifyHeld(CarryableItem item, bool held)
        {
            if (held) heldItem = item;
            else if (heldItem == item) heldItem = null;
        }

        // Resolve a slot's item on this peer; null when empty or not (yet) spawned.
        public CarryableItem ItemInSlot(int index)
        {
            return Resolve(slots.Value.Get(index));
        }

        // --- Client requests. Pre-checks mirror the server table so certain
        // refusals never leave the machine; the server checks everything again.

        public void RequestGrab(CarryableItem item)
        {
            if (!IsOwner || item == null || !item.CanGrabFromWorld) return;
            if (HoldingOverflow) { ShowRefusal(RefuseReason.HandsFull); return; }
            ServerRequestGrab(item.NetworkObject);
        }

        public void RequestEquip(int slot)
        {
            if (!IsOwner || slot < 0 || slot >= InventorySlots.Count) return;
            if (slots.Value.Get(slot) == InventorySlots.Empty) return;
            if (HoldingOverflow) { ShowRefusal(RefuseReason.HandsFull); return; }
            ServerRequestEquip(slot);
        }

        public void RequestDrop()
        {
            if (!IsOwner || heldItem == null) return;
            ServerRequestDrop();
        }

        public void RequestUse(Vector3 aimDirection)
        {
            if (!IsOwner || heldItem == null) return;
            ServerRequestUse(aimDirection);
        }

        // --- Server decisions.

        [ServerRpc]
        private void ServerRequestGrab(NetworkObject target, NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            CarryableItem item = target == null ? null : target.GetComponent<CarryableItem>();
            if (item == null || !item.IsSpawned) { TargetRefuse(sender, (byte)RefuseReason.NoSuchItem); return; }
            if (!item.CanGrabFromWorld) { TargetRefuse(sender, (byte)RefuseReason.NotFree); return; }
            if (!ServerInReach(item)) { TargetRefuse(sender, (byte)RefuseReason.TooFar); return; }

            CarryableItem held = ServerFindHeld();
            InventorySlots current = slots.Value;
            bool holdingOverflow = held != null && current.IndexOf(held.ObjectId) < 0;
            int free = current.FirstFree();
            switch (InventoryRules.DecideGrab(item.FitsInSlot, free, held == null, holdingOverflow))
            {
                case GrabOutcome.HoldWithSlot:
                    if (item.ServerGrab(sender, player)) slots.Value = current.With(free, item.ObjectId);
                    break;
                case GrabOutcome.StowIntoSlot:
                    if (item.ServerStowFromWorld(sender, player)) slots.Value = current.With(free, item.ObjectId);
                    break;
                case GrabOutcome.HoldOverflow:
                    item.ServerGrab(sender, player);
                    break;
                case GrabOutcome.StowAndHoldOverflow:
                    // The equipped item already owns its slot. Keep all four slot
                    // ids, put it away, and use the separate hand for the new item.
                    if (held != null && held.ServerStow(sender))
                    {
                        if (!item.ServerGrab(sender, player))
                            held.ServerGrab(sender, player);
                    }
                    break;
                default:
                    TargetRefuse(sender, (byte)RefuseReason.HandsFull);
                    break;
            }
        }

        [ServerRpc]
        private void ServerRequestEquip(int slot, NetworkConnection sender = null)
        {
            if (sender != Owner || slot < 0 || slot >= InventorySlots.Count) return;
            InventorySlots current = slots.Value;
            int id = current.Get(slot);
            CarryableItem item = Resolve(id);
            bool carriedByMe = item != null && (item.State == ItemState.Stowed || item.State == ItemState.Held) && item.HolderClientId == sender.ClientId;
            if (!carriedByMe)
            {
                // Stale slot (item despawned or taken away by a server decision).
                if (id != InventorySlots.Empty) slots.Value = current.With(slot, InventorySlots.Empty);
                return;
            }

            CarryableItem held = ServerFindHeld();
            int heldId = held == null ? InventorySlots.Empty : held.ObjectId;
            bool holdingOverflow = held != null && current.IndexOf(heldId) < 0;
            switch (InventoryRules.DecideEquip(id, heldId, holdingOverflow))
            {
                case EquipOutcome.PutAway:
                    item.ServerStow(sender);
                    break;
                case EquipOutcome.Equip:
                    item.ServerGrab(sender, player);
                    break;
                case EquipOutcome.SwapAndEquip:
                    held.ServerStow(sender);
                    item.ServerGrab(sender, player);
                    break;
                default:
                    if (holdingOverflow) TargetRefuse(sender, (byte)RefuseReason.HandsFull);
                    break;
            }
        }

        [ServerRpc]
        private void ServerRequestDrop(NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            CarryableItem held = ServerFindHeld();
            if (held == null) return;
            if (held.ServerRelease(sender, Vector3.zero, false))
                ServerClearSlotOf(held);
        }

        [ServerRpc]
        private void ServerRequestUse(Vector3 aimDirection, NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            CarryableItem held = ServerFindHeld();
            if (held == null || held.UseAction != ItemUseAction.Throw) return;
            if (held.ServerRelease(sender, aimDirection, true))
                ServerClearSlotOf(held);
        }

        [TargetRpc]
        private void TargetRefuse(NetworkConnection connection, byte reason)
        {
            ShowRefusal((RefuseReason)reason);
        }

        // Everything carried, held or stowed, is dropped where the player stood.
        [Server]
        public void ServerDropEverything()
        {
            Vector3 origin = transform.position;
            int dropped = 0;
            CarryableItem held = ServerFindHeld();
            if (held != null) held.ServerDropAt(Scatter(origin, dropped++));
            InventorySlots current = slots.Value;
            for (int i = 0; i < InventorySlots.Count; i++)
            {
                CarryableItem item = Resolve(current.Get(i));
                if (item != null && item != held && item.HolderClientId == Owner.ClientId)
                    item.ServerDropAt(Scatter(origin, dropped++));
            }
            slots.Value = InventorySlots.None;
        }

        private void ServerOnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (!Owner.IsValid || connection.ClientId != Owner.ClientId) return;
            ServerDropEverything();
        }

        [Server]
        private void ServerClearSlotOf(CarryableItem item)
        {
            InventorySlots current = slots.Value;
            int index = current.IndexOf(item.ObjectId);
            if (index >= 0) slots.Value = current.With(index, InventorySlots.Empty);
        }

        // The hands, from the items' authoritative state: whichever item this
        // connection currently holds. A scan of a handful of objects per request.
        [Server]
        private CarryableItem ServerFindHeld()
        {
            int clientId = Owner.ClientId;
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
                if (item.IsSpawned && item.State == ItemState.Held && item.HolderClientId == clientId)
                    return item;
            return null;
        }

        [Server]
        private bool ServerInReach(CarryableItem item)
        {
            Vector3 eye = player.EyePosition;
            Collider collider = item.PrimaryCollider;
            Vector3 closest = collider != null && collider.enabled ? collider.ClosestPoint(eye) : item.transform.position;
            return Vector3.Distance(eye, closest) <= player.InteractReach + ReachTolerance &&
                   InteractionTargeting.HasLineOfSight(eye, closest, transform, item);
        }

        private CarryableItem Resolve(int objectId)
        {
            if (objectId == InventorySlots.Empty || NetworkManager == null) return null;
            var spawned = IsServerStarted ? ServerManager.Objects.Spawned : ClientManager.Objects.Spawned;
            return spawned.TryGetValue(objectId, out NetworkObject nob) && nob != null ? nob.GetComponent<CarryableItem>() : null;
        }

        private void ShowRefusal(RefuseReason reason)
        {
            refusal = InventoryRules.ReasonText(reason);
            refusalUntil = Time.unscaledTime + RefusalSeconds;
        }

        private static Vector3 Scatter(Vector3 origin, int index)
        {
            float angle = index * 1.7f;
            return origin + new Vector3(Mathf.Cos(angle) * 0.4f, 0.3f, Mathf.Sin(angle) * 0.4f);
        }
    }
}
