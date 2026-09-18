using System.Collections.Generic;
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
        // Loot value in dollars, rolled once by the server when the item spawns
        // (docs/VISOR_IMPLEMENTATION_PLAN.md section 3.3; DESIGN.md section 6:
        // "randomised within a fixed range each run, always visible through the
        // visor"). Both zero: the item is not loot (the HQ balls) and shows no value.
        [SerializeField, Min(0)] private int valueMin;
        [SerializeField, Min(0)] private int valueMax;

        private readonly SyncVar<ItemState> state = new();
        // The rolled value; 0 until the server rolls it, and always 0 for non-loot.
        private readonly SyncVar<int> value = new();
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
        private bool transitInCabin;        // cabin cargo keeps its colliders: it can be looked at and grabbed mid-ride
        private Vector3 transitLocalPosition;
        private Quaternion transitLocalRotation;

        public string DisplayName => Tag != null ? Tag.DisplayName : (string.IsNullOrEmpty(displayName) ? name : displayName);
        // An item with a state of its own on the same prefab — a dead player's body
        // (PlayerBody: the player's name), an air tank (AirTankItem: full or empty,
        // breathe or throw) — supplies the name and the use action.
        private IItemTag tag; private bool tagLooked;
        private IItemTag Tag { get { if (!tagLooked) { tag = GetComponent<IItemTag>(); tagLooked = true; } return tag; } }
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
                Vector3 scale = transform.lossyScale;
                float largest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (PrimaryCollider is SphereCollider sphere) return sphere.radius * largest;
                // A flat coin: half its widest extent, so placement keeps it clear of walls.
                if (PrimaryCollider is BoxCollider box)
                    return 0.5f * Mathf.Max(box.size.x * Mathf.Abs(scale.x), box.size.y * Mathf.Abs(scale.y), box.size.z * Mathf.Abs(scale.z));
                return 0.12f;
            }
        }

        // Loot value (docs/VISOR_IMPLEMENTATION_PLAN.md): HasValue is a prefab fact,
        // Value the server's roll for this instance (0 until it lands on a client).
        public bool HasValue => valueMax > 0;
        public int Value => value.Value;
        public int ValueMin => valueMin;
        public int ValueMax => valueMax;
        public Texture2D Icon => Tag != null && Tag.IconOverride != null ? Tag.IconOverride : icon;
        public ItemUseAction UseAction => Tag != null && Tag.UseActionOverride.HasValue ? Tag.UseActionOverride.Value : useAction;
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

        // Every spawned item on this peer, for the per-frame passes (the car
        // carrying loose bodies) that must not search the scene each frame.
        public static readonly HashSet<CarryableItem> Spawned = new();

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Spawned.Add(this);
            ApplyRole();
            ResolveHolder();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            Spawned.Remove(this);
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
            // Rolled once per spawn: the dive site spawns its loot fresh on every load,
            // so every dive re-rolls; nothing re-rolls while the item exists.
            if (HasValue && value.Value == 0)
                value.Value = UnityEngine.Random.Range(Mathf.Min(valueMin, valueMax), valueMax + 1);
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
            // The server's teleport flag for an equip from a slot is not sent once
            // the client owns the transform: the owner's first write carries one
            // instead, when the item comes from far (a stowed item parks where it
            // was stowed, another world even; code check, 18 September 2026).
            if (IsOwner && state.Value == ItemState.Held) teleportOnNextWrite = true;
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
            if (!IsSpawned) return;
            if (state.Value == ItemState.Free) { PinToMovingCar(); return; }
            // Held, Released or Stowed: whatever spot the pin remembered is stale (a
            // grab and a throw mid-ride put it somewhere else; a stale pin snapped the
            // copy back to the old spot when it next came to rest).
            carPinned = false;
            carRestKnown = false;
            if (state.Value != ItemState.Held || !LocalWriter || hasPendingRelease)
                return;
            if (holder == null) ResolveHolder();
            if (holder == null || !TryGetHoldPose(holder, out Vector3 position, out Quaternion rotation)) return;
            bool far = teleportOnNextWrite && (transform.position - position).sqrMagnitude > 1f;
            teleportOnNextWrite = false;
            transform.SetPositionAndRotation(position, rotation);
            if (far) networkTransform?.Teleport();
        }
        private bool teleportOnNextWrite;

        // A loose item on the car's floor rides with the car (Dan, 16 September
        // 2026: "items on the elevator floor should stay on it, even if the
        // elevator is going up and down"). The car is moved by transform on every
        // peer, so physics carries nothing on it, and a replicated copy would lag
        // the floor by the interpolation delay as a remote rider's did. So, like
        // the rider pin (ShipDepartureRider), every peer holds the item at the spot
        // in the car's frame it had while the car stood still — the replicated pose
        // is exact then; a few frames into the ride a copy already lags the floor,
        // and a spot captured then would be held wrong for the whole ride and left
        // wrong at the end (a NetworkTransform never corrects a displaced copy once
        // its last goal has no rate). The server's body is kinematic meanwhile (the
        // contract's writer, still); a client's copy overrides its NetworkTransform
        // for the frame. Released once the car has stood still a moment.
        private bool carPinned;
        private Vector3 carLocalPosition;
        private Quaternion carLocalRotation;
        private bool carRestKnown;                 // the spot recorded while the car was still
        private Vector3 carRestLocalPosition;
        private Quaternion carRestLocalRotation;
        private float carStillSince = -1f;
        private const float CarPinInsideProbeMeters = 0.25f;  // a coin lies 3 cm up; the rider volume starts at the floor
        private const float CarPinReleaseSeconds = 0.25f;     // longer than the transport's interpolation delay

        private void PinToMovingCar()
        {
            if (transitSerial != 0) { carPinned = false; return; } // frozen cargo: the server follows it itself
            SunkCost.Diving.ElevatorController car = SunkCost.World.WorldSceneFlow.FindCarCached();
            bool moving = car != null && (car.State == SunkCost.Diving.ElevatorState.Descending || car.State == SunkCost.Diving.ElevatorState.Ascending);
            if (car == null) { carPinned = false; carRestKnown = false; return; }
            if (!moving && !carPinned)
            {
                // Still car: the pose is the truth; remember the spot for the next ride.
                carRestKnown = car.IsInsideCar(transform.position + Vector3.up * CarPinInsideProbeMeters);
                if (carRestKnown)
                {
                    carRestLocalPosition = car.transform.InverseTransformPoint(transform.position);
                    carRestLocalRotation = Quaternion.Inverse(car.transform.rotation) * transform.rotation;
                }
                return;
            }
            if (!moving)
            {
                if (carStillSince < 0f) carStillSince = Time.unscaledTime;
                if (Time.unscaledTime - carStillSince >= CarPinReleaseSeconds) { UnpinFromCar(); return; }
            }
            else carStillSince = -1f;
            if (!carPinned)
            {
                if (!car.IsInsideCar(transform.position + Vector3.up * CarPinInsideProbeMeters)) return;
                // An item that came to rest under way (thrown mid-ride) has no resting
                // spot on record. The server (its writer) pins it where it lies; a
                // client's copy is left to its NetworkTransform — it lags the floor a
                // little until the car stops, but ends exactly where the server has it,
                // where a pin from the lagged copy would leave it wrong for good.
                if (!carRestKnown && !IsServerStarted) return;
                carLocalPosition = carRestKnown ? carRestLocalPosition : car.transform.InverseTransformPoint(transform.position);
                carLocalRotation = carRestKnown ? carRestLocalRotation : Quaternion.Inverse(car.transform.rotation) * transform.rotation;
                carPinned = true;
                if (body != null && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
                if (body != null) body.interpolation = RigidbodyInterpolation.None; // placed by script each frame, like a held item
            }
            PlaceBody(car.transform.TransformPoint(carLocalPosition), car.transform.rotation * carLocalRotation);
        }

        private void UnpinFromCar()
        {
            carPinned = false;
            carRestKnown = false;
            carStillSince = -1f;
            ApplyRole(); // the server's body simulates again, at rest on the floor
            if (body != null && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.WakeUp(); }
        }

        public bool PinnedToCar => carPinned;
        public bool CarRestKnown => carRestKnown;

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
            if (body != null && !body.isKinematic && body.interpolation == RigidbodyInterpolation.None && Time.unscaledTime > carriedUntil)
                body.interpolation = RigidbodyInterpolation.Interpolate;
            if (state.Value == ItemState.Released && LocalWriter && !hasPendingRelease && !restRequested)
            {
                releaseTime += Time.fixedDeltaTime;
                bool resting = body.linearVelocity.sqrMagnitude < 0.0225f && body.angularVelocity.sqrMagnitude < 0.25f;
                restTime = resting ? restTime + Time.fixedDeltaTime : 0f;
                if (restTime >= 0.5f || releaseTime >= releaseHandoffTimeout)
                {
                    restRequested = true;
                    ServerRequestRest(motionVersion.Value, body.linearVelocity, body.angularVelocity);
                }
            }

            if (IsServerStarted && state.Value == ItemState.Free && transform.position.y < VoidY())
                ServerReset();
        }

        // Below this an item is lost and comes back to its reset spot: the sea at
        // HQ and aboard (y = -2, under the hull), and 20 m under the car's landing
        // in the dive — the seafloor itself is 45 m down, and a rule of "-2"
        // there reset every loose item to its spawn spot every physics step (a
        // coin dropped in the car "disappeared" back onto the seafloor; Dan, 16
        // September 2026).
        private const float SeaVoidY = -2f;
        private const float DiveVoidBelowLandingMeters = 20f;
        private float VoidY()
        {
            if (!SunkCost.World.WorldScenes.TryParse(gameObject.scene.name, out SunkCost.World.WorldId world) || world != SunkCost.World.WorldId.Dive) return SeaVoidY;
            SunkCost.Diving.ElevatorController car = SunkCost.World.WorldSceneFlow.FindCarCached();
            return car != null ? car.BottomPosition.y - DiveVoidBelowLandingMeters : float.NegativeInfinity;
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
            // Cabin cargo picked up mid-ride is cargo no more: it travels in the hand.
            if (InCabinTransit) SunkCost.World.WorldSceneFlow.Instance?.ServerReleaseCabinCargo(this);
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
        private void ServerCancelRelease(uint version, NetworkConnection sender = null) => ServerCancelReleaseFrom(sender, version);

        [Server]
        private void ServerCancelReleaseFrom(NetworkConnection sender, uint version)
        {
            if (sender == null || sender.ClientId != holderClientId.Value) return;
            if (state.Value != ItemState.Released || version != motionVersion.Value) return;
            // The hands were taken in the meantime (a grab granted between the throw
            // and its cancel): back into the hands would be two items held at once
            // (code check, 18 September 2026). It lies where it was placed instead.
            CarryableItem other = PlayerInventory.ServerHeldBy(sender.ClientId);
            if (other != null && other != this) { ServerDropAt(transform.position); return; }
            motionVersion.Value++;
            state.Value = ItemState.Held;
            ApplyRole();
            PlayerInventory.ServerRestoreAfterFailedRelease(this, sender);
        }

#if UNITY_EDITOR
        // Editor checks only: the owner's cancel of the current release, as its RPC would send it.
        public void ServerCancelReleaseForChecks(NetworkConnection sender) => ServerCancelReleaseFrom(sender, motionVersion.Value);
#endif

        // A pose written to the transform and the body both: a transform write alone
        // is undone by an interpolating body on the next frame (the interpolator
        // rewrites the transform from the body's last physics poses before the step
        // that would have synced the change), and the item stays where it was.
        private void PlaceBody(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (body == null) return;
            body.position = position;
            body.rotation = rotation;
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
            PlaceBody(position, Quaternion.identity);
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

        // Cabin cargo (DESIGN: "cabin floor cargo is unlimited and rides up"): a
        // loose item on a cabin's floor is frozen at its spot in the cabin frame
        // when the ride seals, follows that cabin (the car moves; the deck cabin
        // does not), crosses to the other world with the riders and is placed at
        // the same spot in the other cabin. Same freeze as the deck's, a cabin
        // frame instead of a ship.
        [Server]
        public void ServerBeginCabinTransit(int serial, SunkCost.World.CabinFrame frame)
        {
            if (serial == 0 || !frame.IsValid) return;
            motionVersion.Value++;
            state.Value = ItemState.Free;
            holderClientId.Value = -1;
            holder = null;
            if (Owner.IsValid) RemoveOwnership();
            transitSerial = serial;
            transitInCabin = true;
            carPinned = false;
            carRestKnown = false;
            transitLocalPosition = frame.ToLocal(transform.position);
            transitLocalRotation = frame.ToLocalRotation(transform.rotation);
            ApplyRole();
        }

        // Each frame of the ride: the same spot in the cabin it is in now.
        [Server]
        public void ServerFollowCabinTransit(SunkCost.World.CabinFrame frame)
        {
            if (transitSerial == 0 || !frame.IsValid) return;
            PlaceBody(frame.FromLocal(transitLocalPosition), frame.FromLocalRotation(transitLocalRotation));
        }

        // After the scene move: the same spot in the other cabin, one snap.
        [Server]
        public void ServerPlaceAfterCabinTransit(SunkCost.World.CabinFrame frame)
        {
            if (transitSerial == 0 || !frame.IsValid) return;
            ServerFollowCabinTransit(frame);
            networkTransform?.Teleport();
        }

        // Release: back to plain server-simulated Free, at rest, awake.
        [Server]
        public void ServerEndDeckTransit()
        {
            if (transitSerial == 0) return;
            transitSerial = 0;
            transitInCabin = false;
            ApplyRole();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        public bool InCabinTransit => transitSerial != 0 && transitInCabin;

        // ---- the car carries loose bodies ---------------------------------------------

        // The body this peer simulates (the server's Free item, the thrower's Released
        // one) inside the car while it moves: WorldSceneFlow moves it by the car's
        // frame delta, so it flies and lands as in a still room (Dan, 16 September
        // 2026: a ball thrown in the moving car fell through the floor into the
        // tube — it was falling against a floor that had moved on between physics
        // steps). Interpolation stays off while carried: the interpolator would
        // render it a step behind the floor.
        public bool SimulatesHere => body != null && !body.isKinematic && transitSerial == 0 && !carPinned;
        private float carriedUntil = -1f;

        public void CarryWithCar(Vector3 delta)
        {
            if (!SimulatesHere) return;
            body.interpolation = RigidbodyInterpolation.None;
            carriedUntil = Time.unscaledTime + 0.1f;
            body.position += delta;
            transform.position += delta;
        }

        // Runtime-spawned fixture items (LootFixtureSpawner) are told where they
        // rest before the server spawns them; OnStartServer then resets them there.
        public void SetResetPositionBeforeSpawn(Vector3 position)
        {
            if (IsSpawned) { Debug.LogWarning(name + ": reset position set after spawn is ignored."); return; }
            resetPosition = position;
        }

        // The writer's motion crosses with the handoff (contract section 2, step 5):
        // at a true rest it is nothing; at the 4 s timeout a ball still rolling kept
        // rolling on the server instead of stopping dead (code check, 18 September
        // 2026). Bounded: no faster than a throw, no wilder than a fast spin.
        [ServerRpc]
        private void ServerRequestRest(uint version, Vector3 velocity, Vector3 angular, NetworkConnection sender = null)
        {
            if (version != motionVersion.Value || state.Value != ItemState.Released || sender == null || sender.ClientId != holderClientId.Value)
                return;
            state.Value = ItemState.Free;
            holderClientId.Value = -1;
            RemoveOwnership();
            ApplyRole();
            if (body != null && !body.isKinematic && IsFinite(velocity) && IsFinite(angular))
            {
                body.linearVelocity = Vector3.ClampMagnitude(velocity, LaunchSpeed);
                body.angularVelocity = Vector3.ClampMagnitude(angular, 20f);
            }
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

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

            bool physical = (current == ItemState.Free || current == ItemState.Released) && (transitSerial == 0 || transitInCabin);
            foreach (Collider collider in colliders)
                if (collider != null) collider.enabled = physical;

            bool visible = current != ItemState.Stowed;
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.enabled = visible;
        }
    }
}
