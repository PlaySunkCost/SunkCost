using FishNet.Object;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The owner's side of riding something that moves or changes scene: a
    // departing ship (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md section 5) or a
    // cabin ride (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md sections 5.3 and 5.4).
    // While locked the player keeps its captured spot in the anchor's frame,
    // may look around, cannot walk or use items. The follow runs even with the
    // menu open or focus lost; the client that simulates the player is the only
    // writer of its root, and the NetworkTransform carries it to everyone else.
    // Nothing here is replicated and nothing is parented under the anchor.
    //
    // On the other peers the copy of a rider on a cabin ride is placed from the
    // server's RiderPlacement each frame while the car moves: the NetworkTransform
    // would otherwise trail the car by a round trip and sink the rider through
    // its floor. Deck trips keep the interpolated copy (the ship is slow).
    [DefaultExecutionOrder(-50)]
    public sealed class ShipDepartureRider : NetworkBehaviour
    {
        private HQPlayerController controller;
        private CabinFrame anchor;
        private Vector3 local;
        private float localYaw;
        private bool locked;
        private int serial;
        private bool placed;

        public bool Locked => locked;
        public int Serial => serial;
        public bool Placed => placed;
        public Vector3 ShipLocal => local;
        public Vector3 Local => local;
        public float LocalYaw => localYaw;

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
        }

        // Preparing: capture the spot on the source ship and stop walking. Safe to
        // call again for the same serial.
        public void Lock(ShipParts sourceShip, int tripSerial) => Lock(new CabinFrame(sourceShip != null ? sourceShip.Root : null, 0f), tripSerial);

        public void Lock(CabinFrame frame, int tripSerial)
        {
            if (!IsOwner || !frame.IsValid) return;
            if (locked && serial == tripSerial) return;
            anchor = frame;
            serial = tripSerial;
            local = anchor.ToLocal(transform.position);
            localYaw = anchor.ToYaw(controller != null ? controller.Yaw : transform.eulerAngles.y);
            placed = false;
            locked = true;
            controller?.SetTravelLock(true);
        }

        // Arrival: the same spot on the destination ship, one snap for the observers.
        public void PlaceOn(ShipParts destinationShip) => PlaceOn(new CabinFrame(destinationShip != null ? destinationShip.Root : null, 0f));

        public void PlaceOn(CabinFrame frame)
        {
            if (!IsOwner || !locked || !frame.IsValid) return;
            anchor = frame;
            if (controller != null) controller.TeleportLocal(anchor.FromLocal(local), anchor.FromYaw(localYaw));
            else transform.position = anchor.FromLocal(local);
            placed = true;
        }

        // Free riding: remember where the player stands in the frame every frame
        // without locking, so the spot is known the moment the scene swap comes.
        public void Track(CabinFrame frame, int rideSerial)
        {
            if (!IsOwner || !frame.IsValid || locked) return;
            anchor = frame;
            serial = rideSerial;
            local = anchor.ToLocal(transform.position);
            localYaw = anchor.ToYaw(controller != null ? controller.Yaw : transform.eulerAngles.y);
        }

        // Lock at the last tracked spot (the swap is about to move the player; its
        // current world position may already be in the other scene).
        public void LockTracked(int rideSerial)
        {
            if (!IsOwner || locked) return;
            serial = rideSerial;
            placed = false;
            locked = true;
            controller?.SetTravelLock(true);
        }

        public void Unlock()
        {
            if (!locked) return;
            locked = false;
            anchor = default;
            controller?.SetTravelLock(false);
        }

        // Follow the anchor after whatever moved it this frame (ShipDepartureVisual,
        // the driven ElevatorController). The yaw is the player's own; only the
        // position is written, continuously, without a teleport flag.
        private void LateUpdate()
        {
            if (IsOwner)
            {
                if (!locked || !anchor.IsValid) return;
                controller?.FollowTo(anchor.FromLocal(local));
                return;
            }
            PlaceRemoteCopy();
        }

        // Another peer's rider: while riders are locked its spot in the car frame is
        // known from the server, so the copy is placed there directly. While they are
        // free and the car moves, the copy is pinned to the floor (below).
        private void PlaceRemoteCopy()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || !day.CabinRide.Active || !day.IsRider(OwnerId)) return;
            if (!WorldSceneFlow.RidersLockedDuring(day.CabinRide)) { PinRemoteCopyToMovingFloor(); return; }
            if (!day.TryGetPlacement(OwnerId, out RiderPlacement placement)) return;
            // Which side of the move the rider is on: the server reads its object's
            // scene (the truth, and it flips at the load, before Below does); a
            // client reads the replicated Below, because a copy's Unity scene on a
            // client is not its world (see ProximityVoice.WorldOf; code check, 18
            // September 2026).
            bool below = IsServerStarted ? gameObject.scene == WorldScenes.Scene(WorldId.Dive) : day.IsBelow(OwnerId);
            CabinFrame frame = below ? CabinFrame.Car(WorldSceneFlow.FindCar()) : CabinFrame.DeckCabin(ShipParts.InWorld(WorldId.Sea));
            if (!frame.IsValid) return;
            transform.position = frame.FromLocal(placement.Local);
        }

        // A free rider's copy is replicated in world space with the transport's
        // interpolation delay behind it. Inside a car moving at 3 m/s that delay is
        // 10-20 cm of height error against the floor that changes as packets land:
        // the copy hovered, sank and stepped (Dan, 16 September 2026: "the second
        // player jumps down"). The car's own motion is known exactly on every peer,
        // so while it moves the copy's height above the floor is held at the height
        // it stood at while the car was still; its walking across the floor still
        // comes from the replicated position. A jump higher than the delay could
        // ever fake (0.45 m) is let through.
        private float remoteRestingLocalY = DefaultRestingLocalY;
        private int remoteFramesOffFloor;
        private const float DefaultRestingLocalY = 0.13f; // floor disc top + controller skin, as the local rider settles
        private const float JumpAllowanceMeters = 0.45f;
        private const int JumpFramesBeforeTrusted = 6;   // a jump stays up for many frames; a replication spike for one or two

        private void PinRemoteCopyToMovingFloor()
        {
            SunkCost.Diving.ElevatorController car = WorldSceneFlow.FindCar();
            CrewDayState day = CrewDayState.Instance;
            if (car == null || day == null) return;
            bool below = IsServerStarted ? gameObject.scene == car.gameObject.scene : day.IsBelow(OwnerId); // see PlaceRemoteCopy
            if (!below) return;
            if (!car.IsInsideCar(transform.position + Vector3.up * 0.5f)) return;
            Vector3 local = car.transform.InverseTransformPoint(transform.position);
            bool moving = car.State == SunkCost.Diving.ElevatorState.Descending || car.State == SunkCost.Diving.ElevatorState.Ascending;
            if (!moving)
            {
                // Still car: the replicated height is the truth; remember where it stands.
                if (local.y < DefaultRestingLocalY + JumpAllowanceMeters) remoteRestingLocalY = local.y;
                return;
            }
            // A real jump takes the copy well off the floor and keeps it there for many
            // frames; a one-frame replication spike does not earn its way off the floor.
            bool offFloor = Mathf.Abs(local.y - remoteRestingLocalY) >= JumpAllowanceMeters;
            remoteFramesOffFloor = offFloor ? remoteFramesOffFloor + 1 : 0;
            if (remoteFramesOffFloor >= JumpFramesBeforeTrusted) return;
            local.y = remoteRestingLocalY;
            transform.position = car.transform.TransformPoint(local);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Unlock();
        }
    }
}
