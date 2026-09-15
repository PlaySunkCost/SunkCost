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
        // Used only when a prefab's Rigidbody mass is invalid; the validator
        // rejects such content before it ships.
        private const float FallbackMassKg = 0.62f;

        [SerializeField] private string displayName = "Item";
        [SerializeField] private bool fitsInSlot = true;
        [SerializeField] private Texture2D icon;
        [SerializeField] private ItemUseAction useAction = ItemUseAction.None;
        // OneHand: right-hand pose, may use a slot. TwoHands: centred low pose,
        // never a slot (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md sections 3, 6).
        [SerializeField] private CarryGrip grip = CarryGrip.OneHand;
        [Tooltip("Extra distance along the hold pose's forward, for an item that needs to sit further out.")]
        [SerializeField] private float holdDistanceOffset = 0f;
        [SerializeField] private WeightSettings weightSettings;
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
        private Vector3 pendingReleasePose;
        private bool pendingReleaseThrow;
        private uint pendingReleaseVersion;
        private float pendingReleaseSince;
        private bool restRequested;
        // Deck cargo frozen for a ship trip (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md
        // section 6): server only, zero = none. While set the server's scripted
        // follow is the only position writer and the body is kinematic.
        private int transitSerial;
        private Vector3 transitLocalPosition;
        private Quaternion transitLocalRotation;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        // A two-handed item never fits a slot, whatever the serialized flag says.
        public bool FitsInSlot => grip != CarryGrip.TwoHands && fitsInSlot;
        public CarryGrip Grip => grip;
        public float HoldDistanceOffset => holdDistanceOffset;
        public WeightSettings Weight => WeightSettings.Resolve(weightSettings);
        // Weight is the Rigidbody mass, in kg: one number for physics and the meter.
        public float MassKg => body != null && float.IsFinite(body.mass) && body.mass > 0f ? body.mass : FallbackMassKg;
        // World radius of the sphere, valid while the collider is disabled.
        public float Radius
        {
            get
            {
                var sphere = PrimaryCollider as SphereCollider;
                if (sphere == null) return 0.12f;
                Vector3 scale = transform.lossyScale;
                return sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }
        }
        public Texture2D Icon => icon;
        public ItemUseAction UseAction => useAction;
        public ItemState State => state.Value;
        public int TransitSerial => transitSerial;
        public bool InTransit => transitSerial != 0;
        public uint MotionVersion => motionVersion.Value;
        public bool CanGrabFromWorld => state.Value == ItemState.Free || state.Value == ItemState.Released;
        private bool LocalWriter => IsOwner && ClientManager.Connection.ClientId == holderClientId.Value;
        public bool IsHeld => state.Value == ItemState.Held;
        public int HolderClientId => holderClientId.Value;
        public Collider PrimaryCollider => colliders != null && colliders.Length > 0 ? colliders[0] : null;

        // Read-only status for the F3 network debug overlay.
        public string DebugStatus =>
            (state.Value == ItemState.Held ? $"Held by {holderClientId.Value}" :
             state.Value == ItemState.Released ? $"Released by {holderClientId.Value}" :
             state.Value == ItemState.Stowed ? $"Stowed by {holderClientId.Value}" : "Free") +
            $" {MassKg:0.##}kg {(grip == CarryGrip.TwoHands ? "2h" : "1h")}";

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
            // A despawned item no longer weighs on its carrier.
            if (ServerManager != null && ServerManager.Started) PlayerInventory.ServerRecomputeAll();
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
            float ring = 0.4f + Radius;
            ServerDropAt(origin + new Vector3(Mathf.Cos(angle) * ring, Radius + 0.05f, Mathf.Sin(angle) * ring));
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
            if (holder == null || !TryGetHoldPose(holder, out Vector3 position, out Quaternion rotation)) return;
            transform.SetPositionAndRotation(position, rotation);
        }

        // The pose for this item's grip on a given player: the right hand or the
        // two-handed centre point, plus the item's own forward offset. One
        // calculation for the writer's snap, the server's equip teleport and hooks.
        public bool TryGetHoldPose(HQPlayerController player, out Vector3 position, out Quaternion rotation)
        {
            Transform point = player != null ? player.HoldPointFor(grip) : null;
            if (point == null) { position = Vector3.zero; rotation = Quaternion.identity; return false; }
            position = point.position + point.forward * holdDistanceOffset;
            rotation = point.rotation;
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;
            // An accepted release whose state never arrived: give it back rather
            // than leave a ghost in the hands (plan section 6A, releaseTimeoutSeconds).
            if (hasPendingRelease && LocalWriter && Time.unscaledTime - pendingReleaseSince > ReleaseTimeoutSeconds())
            {
                hasPendingRelease = false;
                ServerCancelRelease(pendingReleaseVersion);
            }
            // A body this peer simulates has now stepped from wherever ApplyRole put it:
            // interpolate its rendering between steps (see ApplyRole).
            if (body != null && !body.isKinematic && body.interpolation == RigidbodyInterpolation.None)
                body.interpolation = RigidbodyInterpolation.Interpolate;
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
            if (fromInventory && TryGetHoldPose(player, out Vector3 pose, out Quaternion poseRotation))
            {
                // Still server-controlled here, so the teleport flag is honoured and
                // spectators do not interpolate it from wherever it was parked.
                transform.SetPositionAndRotation(pose, poseRotation);
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

        // Held -> Released at a start pose the server has validated
        // (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 6A). The owner
        // stays the writer; it places the item at the pose, re-checks its own
        // geometry, and either activates physics or cancels (ServerCancelRelease),
        // in which case the server puts the item back in the hands.
        [Server]
        public bool ServerRelease(NetworkConnection connection, Vector3 pose, Vector3 direction, bool throwIt)
        {
            if (state.Value != ItemState.Held || connection == null || connection.ClientId != holderClientId.Value)
                return false;
            if (!float.IsFinite(pose.x) || !float.IsFinite(pose.y) || !float.IsFinite(pose.z)) return false;
            if (throwIt && (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z) || direction.sqrMagnitude < 0.01f))
                return false;
            motionVersion.Value++;
            state.Value = ItemState.Released;
            Vector3 safeDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            TargetApplyRelease(connection, pose, safeDirection, throwIt && useAction == ItemUseAction.Throw, motionVersion.Value);
            return true;
        }

        // The owner could not apply the accepted release (its geometry changed, or
        // the accepted state never replicated in time): back into the hands, same
        // holder, same ownership, as long as this is still the current release.
        [ServerRpc]
        private void ServerCancelRelease(uint version, NetworkConnection sender = null)
        {
            if (sender == null || sender.ClientId != holderClientId.Value) return;
            if (state.Value != ItemState.Released || version != motionVersion.Value) return;
            motionVersion.Value++;
            state.Value = ItemState.Held;
            ApplyRole();
            PlayerInventory.ServerRestoreAfterFailedRelease(this, sender);
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
            // Dropped while the ship is under way (a passenger disconnected): it
            // joins the frozen cargo instead of being left behind or unloaded.
            SunkCost.World.WorldSceneFlow.Instance?.ServerEnrollLooseCargo(this);
        }

        [Server]
        public void ServerReset() => ServerDropAt(resetPosition);

        // Freeze this loose item where it lies on the ship: Free, server-owned,
        // kinematic, colliders off so the moving deck imparts nothing. A Released
        // item's pending throw/rest is invalidated by the version bump; departure
        // secures cargo, it does not continue a throw across worlds (DESIGN).
        [Server]
        public void ServerBeginDeckTransit(int serial, SunkCost.World.ShipParts ship)
        {
            if (serial == 0 || ship == null) return;
            motionVersion.Value++;
            state.Value = ItemState.Free;
            holderClientId.Value = -1;
            holder = null;
            if (Owner.IsValid) RemoveOwnership();
            transitSerial = serial;
            transitLocalPosition = ship.ToShipLocal(transform.position);
            transitLocalRotation = Quaternion.Inverse(ship.transform.rotation) * transform.rotation;
            ApplyRole();
        }

        // Each frame of the pull-away: the same spot on the moving source ship.
        [Server]
        public void ServerFollowDeckTransit(SunkCost.World.ShipParts ship)
        {
            if (transitSerial == 0 || ship == null) return;
            transform.SetPositionAndRotation(ship.FromShipLocal(transitLocalPosition), ship.transform.rotation * transitLocalRotation);
        }

        // After the scene move: the same spot on the destination ship, one snap.
        [Server]
        public void ServerPlaceAfterTransit(SunkCost.World.ShipParts ship)
        {
            if (transitSerial == 0 || ship == null) return;
            transform.SetPositionAndRotation(ship.FromShipLocal(transitLocalPosition), ship.transform.rotation * transitLocalRotation);
            networkTransform?.Teleport();
        }

        // Release: back to plain server-simulated Free, at rest, awake.
        [Server]
        public void ServerEndDeckTransit()
        {
            if (transitSerial == 0) return;
            transitSerial = 0;
            ApplyRole();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        // Runtime-spawned fixture items (LootFixtureSpawner) are told where they
        // rest before the server spawns them; OnStartServer then resets them there.
        public void SetResetPositionBeforeSpawn(Vector3 position)
        {
            if (IsSpawned) { Debug.LogWarning(name + ": reset position set after spawn is ignored."); return; }
            resetPosition = position;
        }

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
        private void TargetApplyRelease(NetworkConnection connection, Vector3 pose, Vector3 direction, bool throwIt, uint version)
        {
            if (version < motionVersion.Value) return;
            hasPendingRelease = true;
            pendingReleaseVersion = version;
            pendingReleasePose = pose;
            pendingReleaseDirection = direction;
            pendingReleaseThrow = throwIt;
            pendingReleaseSince = Time.unscaledTime;
            TryApplyPendingRelease();
        }

        // Runs from both the TargetRpc and the state OnChange; whichever arrives
        // second applies the release, exactly once: the item is placed at the
        // accepted pose, the owner's own geometry is re-checked, then physics and
        // the impulse start. A blocked pose cancels instead of releasing anywhere.
        private void TryApplyPendingRelease()
        {
            if (!hasPendingRelease || pendingReleaseVersion != motionVersion.Value || state.Value != ItemState.Released || !LocalWriter)
                return;
            hasPendingRelease = false;
            if (holder == null) ResolveHolder();
            Transform holderRoot = holder != null ? holder.transform : null;
            if (!ReleasePlacement.IsClear(pendingReleasePose, Radius, holderRoot, transform))
            {
                ServerCancelRelease(pendingReleaseVersion);
                return;
            }
            transform.position = pendingReleasePose;
            networkTransform?.Teleport();
            ApplyRole();
            body.useGravity = true;
            body.angularVelocity = Vector3.zero;
            if (pendingReleaseThrow)
            {
                // Launch speed scales with this item's own mass; a heavy ball lobs.
                LastLaunchSpeed = throwSpeed * Weight.ThrowFactor(MassKg);
                body.linearVelocity = pendingReleaseDirection * LastLaunchSpeed;
            }
            else
            {
                body.linearVelocity = Vector3.zero;
            }
        }

        // Launch speed of the most recent throw applied on this peer (diagnostics).
        public float LastLaunchSpeed { get; private set; }
        // The speed a throw of this item leaves the hands with (mass-scaled).
        public float LaunchSpeed => throwSpeed * Weight.ThrowFactor(MassKg);

        private float ReleaseTimeoutSeconds()
        {
            if (holder == null) ResolveHolder();
            return holder != null ? holder.Movement.ReleaseTimeoutSeconds : 1f;
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
            // Carried mass is a server sum over items; rest handoff, disconnect
            // drops and resets change it without an inventory request.
            if (asServer) PlayerInventory.ServerRecomputeAll();
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
            if (transitSerial != 0) simulates = false; // frozen cargo: scripted, kinematic, no collider
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
            // Interpolation is off at every role change: a held or replicated (kinematic)
            // item is placed by script each frame and Unity's interpolator would drag its
            // transform a physics step behind that; a just-released item was teleported
            // to the hand pose this frame and the interpolator would lerp it from the old
            // pose. FixedUpdate turns it on once the body has simulated a step from the
            // release pose, so a flying ball renders every frame from then on.
            body.interpolation = RigidbodyInterpolation.None;

            bool physical = (current == ItemState.Free || current == ItemState.Released) && transitSerial == 0;
            foreach (Collider collider in colliders)
                if (collider != null) collider.enabled = physical;

            bool visible = current != ItemState.Stowed;
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.enabled = visible;
        }
    }
}
