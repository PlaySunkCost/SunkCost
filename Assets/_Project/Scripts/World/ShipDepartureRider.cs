using FishNet.Object;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The owner's side of riding a departing ship (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md
    // section 5). While locked the player keeps its captured ship-relative spot,
    // may look around, cannot walk or use items. The follow runs even with the
    // menu open or focus lost; the client that simulates the player is the only
    // writer of its root, and the NetworkTransform carries it to everyone else.
    // Nothing here is replicated and nothing is parented under the ship.
    [DefaultExecutionOrder(-50)]
    public sealed class ShipDepartureRider : NetworkBehaviour
    {
        private HQPlayerController controller;
        private ShipParts ship;
        private Vector3 shipLocal;
        private float shipYaw;
        private bool locked;
        private int serial;
        private bool placed;

        public bool Locked => locked;
        public int Serial => serial;
        public bool Placed => placed;
        public Vector3 ShipLocal => shipLocal;

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
        }

        // Preparing: capture the spot on the source ship and stop walking. Safe to
        // call again for the same serial.
        public void Lock(ShipParts sourceShip, int tripSerial)
        {
            if (!IsOwner || sourceShip == null) return;
            if (locked && serial == tripSerial) return;
            ship = sourceShip;
            serial = tripSerial;
            shipLocal = ship.ToShipLocal(transform.position);
            shipYaw = ship.ToShipYaw(controller != null ? controller.Yaw : transform.eulerAngles.y);
            placed = false;
            locked = true;
            controller?.SetTravelLock(true);
        }

        // Arrival: the same spot on the destination ship, one snap for the observers.
        public void PlaceOn(ShipParts destinationShip)
        {
            if (!IsOwner || !locked || destinationShip == null) return;
            ship = destinationShip;
            if (controller != null) controller.TeleportLocal(ship.FromShipLocal(shipLocal), ship.FromShipYaw(shipYaw));
            else transform.position = ship.FromShipLocal(shipLocal);
            placed = true;
        }

        public void Unlock()
        {
            if (!locked) return;
            locked = false;
            ship = null;
            controller?.SetTravelLock(false);
        }

        // Follow the ship after ShipDepartureVisual moved it this frame. The yaw is
        // the player's own (the ship does not rotate in this version); only the
        // position is written, continuously, without a teleport flag.
        private void LateUpdate()
        {
            if (!locked || !IsOwner || ship == null) return;
            controller?.FollowTo(ship.FromShipLocal(shipLocal));
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Unlock();
        }
    }
}
