using UnityEngine;

namespace SunkCost.Player
{
    // A diver held by something (Dan, 24 September 2026: the Long Walker grabs you and
    // lifts you to its face for about two seconds, then you die; the Weeping Angel
    // embraces you). Server-written on the held diver's HQPlayerController; every
    // peer reads it. The holder is a spawned NetworkObject carrying an IGrabHolder,
    // found by its object id; the hold's clock is the server's tick.
    public struct GrabHold
    {
        public int Serial;        // a new one per catch and per release
        public bool Active;
        public int HolderId;      // the holder's NetworkObject.ObjectId, -1 when none
        public uint StartTick;    // the server's tick at the catch
        public Vector3 CaughtAt;  // where the diver's feet were at the catch (the server's view)
    }

    // Where a held diver is, from the holder's own serialized numbers: the same on
    // every peer, so the diver's owner, a spectator and the deck TV see one hold.
    public struct GrabPose
    {
        public Vector3 Feet;        // where the held diver's feet go (world)
        public Vector3 Face;        // the point the diver's eyes are turned to (world)
        public float ShakeDegrees;  // how hard the view shakes now
        public float ShakeHz;
    }

    public interface IGrabHolder
    {
        // `seconds` into the hold, for a diver caught standing at `caughtAt`.
        GrabPose HoldPose(Vector3 caughtAt, float seconds);

        // Server: the holder still holds this diver (else the diver's hold is let go of).
        bool ServerHolds(HQPlayerController diver);
    }
}
