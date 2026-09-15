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

        // Another peer's rider while the car moves: its spot in the car frame is
        // known from the server, so the copy is placed there directly.
        private void PlaceRemoteCopy()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || !day.CabinRide.Active || !day.IsRider(OwnerId)) return;
            if (!WorldSceneFlow.RidersLockedDuring(day.CabinRide)) return; // free riders walk; their copies interpolate
            if (!day.TryGetPlacement(OwnerId, out RiderPlacement placement)) return;
            CabinFrame frame = WorldSceneFlow.RideFrameFor(day.CabinRide, gameObject.scene);
            if (!frame.IsValid) return;
            transform.position = frame.FromLocal(placement.Local);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Unlock();
        }
    }
}
