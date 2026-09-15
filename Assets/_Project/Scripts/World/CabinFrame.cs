using UnityEngine;

namespace SunkCost.World
{
    // The two cabins are the same round car built at different doorway bearings
    // (the deck cabin's doorway is along its local +Z, DeckCabinBuilder; the
    // seafloor car's along its local +X, ElevatorCabinBuilder). A rider's spot is
    // carried between them in a frame whose +Z is the doorway, so "by the panel,
    // facing the door" means the same thing in both. The validator checks the
    // two offsets against the built geometry.
    public readonly struct CabinFrame
    {
        public const float DeckCabinDoorwayYaw = 0f;   // doorway at local +Z
        public const float CarDoorwayYaw = 90f;        // doorway at local +X (Euler(0, 90, 0) * forward)

        public readonly Transform Root;
        public readonly float DoorwayYaw;

        public CabinFrame(Transform root, float doorwayYaw)
        {
            Root = root;
            DoorwayYaw = doorwayYaw;
        }

        public bool IsValid => Root != null;

        public Vector3 ToLocal(Vector3 world) => Quaternion.Euler(0f, -DoorwayYaw, 0f) * Root.InverseTransformPoint(world);
        public Vector3 FromLocal(Vector3 local) => Root.TransformPoint(Quaternion.Euler(0f, DoorwayYaw, 0f) * local);
        public float ToYaw(float worldYaw) => worldYaw - Root.eulerAngles.y - DoorwayYaw;
        public float FromYaw(float localYaw) => localYaw + Root.eulerAngles.y + DoorwayYaw;

        public static CabinFrame DeckCabin(ShipParts ship) => new(ship != null ? ship.DeckCabin : null, DeckCabinDoorwayYaw);
        public static CabinFrame Car(SunkCost.Diving.ElevatorController car) => new(car != null ? car.transform : null, CarDoorwayYaw);
    }
}
