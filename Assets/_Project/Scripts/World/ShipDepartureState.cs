using FishNet.Broadcast;

namespace SunkCost.World
{
    // The stages of one ship trip (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md
    // section 4). DayPhase stays Sailing/SailingHome for the whole trip; this is
    // the presentation and gating clock inside it. The server is the only writer.
    public enum DepartureStage : byte
    {
        Idle = 0,
        Preparing = 1,      // everyone locked in place, ship still; waiting for Prepared acks
        RaisingGangway = 2, // casting off: the ship still, the moorings coming in (no gangway on the ship since 18 September 2026)
        PullingAway = 3,    // the visible move
        FadingOut = 4,      // waiting for Black acks
        Loading = 5,        // the FishNet scene move, under black
        Arriving = 6,       // fade in; at HQ a moment more while the lines go ashore
        Complete = 7,
        Cancelled = 8
    }

    // One server-written SyncVar on CrewDayState. StageStartTick is the server
    // tick the stage began on; presentation evaluates progress from the
    // synchronized tick, never from a local clock started on receipt.
    public struct ShipDepartureState
    {
        public int Serial;
        public DepartureStage Stage;
        public WorldId FromWorld;
        public WorldId ToWorld;
        public uint StageStartTick;
        public uint StageDurationTicks;

        public bool Active => Stage != DepartureStage.Idle && Stage != DepartureStage.Complete && Stage != DepartureStage.Cancelled;
    }

    public enum DepartureAckKind : byte
    {
        Prepared = 0,   // locked in place at the captured deck spot
        Black = 1,      // the screen is fully black
        Arrived = 2     // placed on the destination ship
    }

    // A client's answer for one stage of one trip. The server ignores anything
    // that is not from a member of the trip's cohort for the current serial.
    public struct DepartureAckBroadcast : IBroadcast
    {
        public int Serial;
        public DepartureAckKind Kind;
        public WorldId World;
    }
}
