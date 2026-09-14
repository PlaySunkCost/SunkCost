using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using SunkCost.Net;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Interaction
{
    // One carryable physics object (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md
    // section 5). The server writes the SyncVars; every peer derives what it
    // simulates, collides and renders from them plus ownership, never from an RPC.
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class CarryableItem : NetworkBehaviour, INetworkDebugInfo
    {
        [SerializeField] private string displayName = "Item";
        [SerializeField] private bool fitsInSlot = true;
        [SerializeField] private Texture2D icon;
        [SerializeField] private ItemUseAction useAction = ItemUseAction.None;
        [SerializeField] private float throwSpeed = 8f;
        [SerializeField] private float releaseHandoffTimeout = 4f;
        [SerializeField] private Vector3 resetPosition = new(0f, 1f, 0f);

        private readonly SyncVar<ItemState> state = new();
        // Holder while Held/Released, carrier while Stowed, -1 while Free.
        private readonly SyncVar<int> holderClientId = new(-1);
        private readonly SyncVar<uint> motionVersion = new();

        private Rigidbody body;
        private NetworkTransform networkTransform;
        private Collider[] colliders;
        private Renderer[] renderers;
        private HQPlayerController holder;
        private float restTime;
        private float releaseTime;
        // Release impulse from the server, applied once the Released state has also
        // replicated. TargetRpcs reach a remote client before the same tick's
        // SyncVar flush, so neither may assume the other has arrived.
        private bool hasPendingRelease;
        private Vector3 pendingReleaseDirection;
        private bool pendingReleaseThrow;
        private uint pendingReleaseVersion;
        private bool restRequested;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public bool FitsInSlot => fitsInSlot;
        public Texture2D Icon => icon;
        public ItemUseAction UseAction => useAction;
        public ItemState State => state.Value;
        public uint MotionVersion => motionVersion.Value;
        public bool CanGrabFromWorld => state.Value == ItemState.Free || state.Value == ItemState.Released;
        private bool LocalWriter => IsOwner && ClientManager.Connection.ClientId == holderClientId.Value;
        public bool IsHeld => state.Value == ItemState.Held;
        public int HolderClientId => holderClientId.Value;
        public Collider PrimaryCollider => colliders != null && colliders.Length > 0 ? colliders[0] : null;

        // Read-only status for the F3 network debug overlay.
        public string DebugStatus =>
            state.Value == ItemState.Held ? $"Held by {holderClientId.Value}" :
            state.Value == ItemState.Released ? $"Released by {holderClientId.Value}" :
            state.Value == ItemState.Stowed ? $"Stowed by {holderClientId.Value}" : "Free";

        // A held item is kinematic on its holder, so the overlay's Rigidbody rule
        // would call the holder REPLICATED. This is the contract's writer, stated.
        public bool? WriterOverride =>
            state.Value == ItemState.Held || state.Value == ItemState.Released
                ? LocalWriter
                : IsServerStarted && !Owner.IsValid;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkTransform = GetComponent<NetworkTransform>();
            colliders = GetComponentsInChildren<Collider>(true);
            renderers = GetComponentsInChildren<Renderer>(true);
            state.OnChange += OnStateChanged;
            holderClientId.OnChange += OnHolderChanged;
            motionVersion.OnChange += OnMotionVersionChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            ApplyRole();
            ResolveHolder();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late joiners and reconnecting clients get their initial SyncVar values
            // without OnChange callbacks, so derive the local held state here too.
            ApplyRole();
            SyncLocalHeldState();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerManager.OnRemoteConnectionState += ServerOnRemoteConnectionState;
            // A scene object keeps its last transform across sessions; a newly opened
            // room should not inherit where the previous room's last throw ended.
            ServerReset();
        }

        public override void OnStopServer()
        {
            ServerManager.OnRemoteConnectionState -= ServerOnRemoteConnectionState;
            base.OnStopServer();
        }

        // The contract requires disconnect to return a held/released item to server
        // simulation when disconnect is detected, without waiting for rest. Stowed
        // items also drop here, independently of player callback ordering.
        private void ServerOnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (connection.ClientId != holderClientId.Value) return;
            if (state.Value == ItemState.Free) return;
            ResolveHolder();
            Vector3 origin = holder != null ? holder.transform.position : transform.position;
            float angle = ObjectId * 1.7f;
            ServerDropAt(origin + new Vector3(Mathf.Cos(angle) * 0.4f, 0.3f, Mathf.Sin(angle) * 0.4f));
        }

        public override void OnOwnershipClient(NetworkConnection previousOwner)
        {
            base.OnOwnershipClient(previousOwner);
            ApplyRole();
            SyncLocalHeldState();
            TryApplyPendingRelease();
        }

        public override void OnOwnershipServer(NetworkConnection previousOwner)
        {
            base.OnOwnershipServer(previousOwner);
            // Ownership removed from a holder means the item is loose again, unless
            // it was stowed, which removes ownership on purpose and keeps the state.
            if (IsServerStarted && previousOwner.IsValid && !Owner.IsValid && state.Value != ItemState.Stowed)
            {
                state.Value = ItemState.Free;
                holderClientId.Value = -1;
            }
            ApplyRole();
        }

        // The rigid hold: the holder writes the transform from the hold point after
        // the controller has moved the camera this frame. Nothing else moves a held
        // item; the client-authoritative NetworkTransform sends whatever is here.
        private void LateUpdate()
        {
            if (!IsSpawned || state.Value != ItemState.Held || !LocalWriter || hasPendingRelease)
                return;
            if (holder == null) ResolveHolder();
            if (holder == null || holder.HoldPoint == null) return;
            transform.SetPositionAndRotation(holder.HoldPoint.position, holder.HoldPoint.rotation);
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;
            if (state.Value == ItemState.Released && LocalWriter && !hasPendingRelease && !restRequested)
            {
                releaseTime += Time.fixedDeltaTime;
                bool resting = body.linearVelocity.sqrMagnitude < 0.0225f && body.angularVelocity.sqrMagnitude < 0.25f;
                restTime = resting ? restTime + Time.fixedDeltaTime : 0f;
                if (restTime >= 0.5f || releaseTime >= releaseHandoffTimeout)
                {
                    restRequested = true;
                    ServerRequestRest(motionVersion.Value);
                }
            }

            if (IsServerStarted && state.Value == ItemState.Free && transform.position.y < -2f)
                ServerReset();
        }

        // From Free (a world grab) or from Stowed by the same connection (equip).
        [Server]
        public bool ServerGrab(NetworkConnection connection, HQPlayerController player)
        {
            if (connection == null || !connection.IsValid)
                return false;
            bool fromWorld = CanGrabFromWorld;
            bool fromInventory = state.Value == ItemState.Stowed && holderClientId.Value == connection.ClientId;
            if (!fromWorld && !fromInventory)
                return false;
            if (fromInventory && player != null && player.HoldPoint != null)
            {
                // Still server-controlled here, so the teleport flag is honoured and
                // spectators do not interpolate it from wherever it was parked.
                transform.SetPositionAndRotation(player.HoldPoint.position, player.HoldPoint.rotation);
                networkTransform?.Teleport();
            }
            holder = player;
            motionVersion.Value++;
            holderClientId.Value = connection.ClientId;
            state.Value = ItemState.Held;
            GiveOwnership(connection);
            ApplyRole();
            return true;
        }

        // Free -> Stowed without passing through the hands (E while already holding
        // a slot item). No ownership change: the server keeps it.
        [Server]
        public bool ServerStowFromWorld(NetworkConnection connection, HQPlayerController player)
        {
            if (!CanGrabFromWorld || connection == null || !connection.IsValid)
                return false;
            holder = player;
            motionVersion.Value++;
            holderClientId.Value = connection.ClientId;
            state.Value = ItemState.Stowed;
            if (Owner.IsValid) RemoveOwnership();
            ApplyRole();
            return true;
        }

        // Held by this connection -> Stowed: hidden, server-owned, not moving.
        [Server]
        public bool ServerStow(NetworkConnection connection)
        {
            if (state.Value != ItemState.Held || connection == null || connection.ClientId != holderClientId.Value)
                return false;
            // State first so OnOwnershipServer sees Stowed and does not reset to Free.
            motionVersion.Value++;
            state.Value = ItemState.Stowed;
            RemoveOwnership();
            ApplyRole();
            return true;
        }

        [Server]
        public bool ServerRelease(NetworkConnection connection, Vector3 direction, bool throwIt)
        {
            if (state.Value != ItemState.Held || connection == null || connection.ClientId != holderClientId.Value)
                return false;
            if (throwIt && (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z) || direction.sqrMagnitude < 0.01f))
                return false;
            motionVersion.Value++;
            state.Value = ItemState.Released;
            Vector3 safeDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            TargetApplyRelease(connection, safeDirection, throwIt && useAction == ItemUseAction.Throw, motionVersion.Value);
            return true;
        }

        // Server decision from any state: loose, server-simulated, at a position.
        [Server]
        public void ServerDropAt(Vector3 position)
        {
            motionVersion.Value++;
            state.Value = ItemState.Free;
            holderClientId.Value = -1;
            holder = null;
            if (Owner.IsValid) RemoveOwnership();
            transform.SetPositionAndRotation(position, Quaternion.identity);
            networkTransform?.Teleport();
            ApplyRole();
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        [Server]
        public void ServerReset() => ServerDropAt(resetPosition);

        [ServerRpc]
        private void ServerRequestRest(uint version, NetworkConnection sender = null)
        {
            if (version != motionVersion.Value || state.Value != ItemState.Released || sender == null || sender.ClientId != holderClientId.Value)
                return;
            state.Value = ItemState.Free;
            holderClientId.Value = -1;
            RemoveOwnership();
            ApplyRole();
        }

        [TargetRpc]
        private void TargetApplyRelease(NetworkConnection connection, Vector3 direction, bool throwIt, uint version)
        {
            if (version < motionVersion.Value) return;
            hasPendingRelease = true;
            pendingReleaseVersion = version;
            pendingReleaseDirection = direction;
            pendingReleaseThrow = throwIt;
            TryApplyPendingRelease();
        }

        // Runs from both the TargetRpc and the state OnChange; whichever arrives
        // second applies the impulse, exactly once.
        private void TryApplyPendingRelease()
        {
            if (!hasPendingRelease || pendingReleaseVersion != motionVersion.Value || state.Value != ItemState.Released || !LocalWriter)
                return;
            hasPendingRelease = false;
            ApplyRole();
            body.useGravity = true;
            body.angularVelocity = Vector3.zero;
            if (pendingReleaseThrow)
                body.linearVelocity = pendingReleaseDirection * throwSpeed;
            else
            {
                // Q is a drop at the feet, not a throw from camera height.
                if (holder == null) ResolveHolder();
                if (holder != null)
                    transform.position = holder.transform.position + holder.transform.forward * 0.5f + Vector3.up * 0.3f;
                body.linearVelocity = Vector3.zero;
                networkTransform?.Teleport();
            }
        }

        // The local player's held item comes from the replicated SyncVars, not from
        // an RPC, so it cannot observe a half-applied grab or release.
        private void SyncLocalHeldState()
        {
            if (!IsClientStarted || ClientManager?.Connection == null)
                return;
            bool localHolds = state.Value == ItemState.Held && holderClientId.Value == ClientManager.Connection.ClientId;
            foreach (PlayerInventory candidate in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
            {
                if (candidate.IsOwner)
                {
                    candidate.NotifyHeld(this, localHolds);
                    break;
                }
            }
        }

        private void OnStateChanged(ItemState previous, ItemState next, bool asServer)
        {
            restTime = 0f;
            releaseTime = 0f;
            restRequested = false;
            if (next != ItemState.Released && pendingReleaseVersion <= motionVersion.Value)
                hasPendingRelease = false;
            ResolveHolder();
            ApplyRole();
            SyncLocalHeldState();
            TryApplyPendingRelease();
        }

        private void OnHolderChanged(int previous, int next, bool asServer)
        {
            ResolveHolder();
            ApplyRole();
            SyncLocalHeldState();
            TryApplyPendingRelease();
        }

        private void OnMotionVersionChanged(uint previous, uint next, bool asServer)
        {
            if (hasPendingRelease && pendingReleaseVersion < next) hasPendingRelease = false;
            restTime = 0f;
            releaseTime = 0f;
            restRequested = false;
            TryApplyPendingRelease();
        }

        private void ResolveHolder()
        {
            holder = null;
            if (holderClientId.Value < 0) return;
            foreach (HQPlayerController candidate in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
            {
                if (candidate.Owner.ClientId == holderClientId.Value) { holder = candidate; break; }
            }
        }

        // The whole presentation row from (state, ownership, role), section 5 table:
        // Free -> server simulates; Held -> holder snaps it, kinematic, no collider;
        // Released -> holder simulates; Stowed -> kinematic, hidden, no collider.
        private void ApplyRole()
        {
            if (body == null) return;
            ItemState current = state.Value;
            bool simulates = (current == ItemState.Free && IsServerStarted && !Owner.IsValid) ||
                             (current == ItemState.Released && LocalWriter);
            if (simulates)
            {
                body.isKinematic = false;
                body.useGravity = true;
            }
            else
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
                body.useGravity = false;
            }

            bool physical = current == ItemState.Free || current == ItemState.Released;
            foreach (Collider collider in colliders)
                if (collider != null) collider.enabled = physical;

            bool visible = current != ItemState.Stowed;
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.enabled = visible;
        }
    }
}
