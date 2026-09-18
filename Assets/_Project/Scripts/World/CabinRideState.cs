using SunkCost.Diving;
using UnityEngine;

namespace SunkCost.World
{
    // The stages of one cabin ride: the deck cabin down to the seafloor car, or
    // the car up to the deck cabin (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // sections 5.3 and 5.4, without the day rules). The server is the only
    // writer; every stage carries its start tick so presentation and gating run
    // from the synchronized clock, like the ship departure.
    public enum CabinRideStage : byte
    {
        Idle = 0,
        Preparing = 1,      // riders locked at their cabin-relative spot; waiting for Prepared acks
        Sealing = 2,        // the doors close (deck cabin going down; the car going up)
        Riding = 3,         // going up only: the car climbs with the riders inside
        FadingOut = 4,      // going down only: the suit fade; waiting for Black acks
        Loading = 5,        // the FishNet scene move: riders placed in the other cabin
        Arriving = 6,       // going down: fade in, then the car descends; going up: the deck cabin opens
        Complete = 7,
        Cancelled = 8
    }

    public enum RideDirection : byte
    {
        Down = 0,   // deck cabin -> car
        Up = 1      // car -> deck cabin
    }

    public struct CabinRideState
    {
        public int Serial;
        public CabinRideStage Stage;
        public RideDirection Direction;
        public uint StageStartTick;
        public uint StageDurationTicks;
        // The deck cabin's doors while Sealing (18 September 2026): they move at
        // door speed from DoorFrom toward open (DoorOpening) or shut, from
        // StageStartTick — a crossing or a touch while they close turns them
        // around, fully open, and they close again; riders are fixed only once shut.
        public float DoorFrom;
        public bool DoorOpening;
        public bool Active => Stage != CabinRideStage.Idle && Stage != CabinRideStage.Complete && Stage != CabinRideStage.Cancelled;
        public WorldId FromWorld => Direction == RideDirection.Down ? WorldId.Sea : WorldId.Dive;
        public WorldId ToWorld => Direction == RideDirection.Down ? WorldId.Dive : WorldId.Sea;
    }

    // The seafloor car's authoritative phase (plan section 6.1, on CrewDayState
    // instead of a scene NetworkObject so DiveSite01 stays a plain scene). Every
    // peer that has the site drives its ElevatorController from this struct and
    // the synchronized tick; a late loader reconstructs the ride from it alone.
    public struct ElevatorPhase
    {
        public int Serial;
        public ElevatorState State;
        public bool Upward;             // which way a Sealing resolves; which way the car moves
        public uint StartTick;
        public uint DurationTicks;      // the seal or the travel; 0 while resting
    }

    // Where a rider stands inside the cabin for the ride, in the cabin frame
    // (doorway along +Z), captured by the server from its copy once the rider is
    // locked. Every peer places the other riders' copies from it while the car
    // moves, so nobody sinks through the floor by a round trip's worth of travel.
    public struct RiderPlacement
    {
        public int ClientId;
        public Vector3 Local;
        public float Yaw;
    }
}
