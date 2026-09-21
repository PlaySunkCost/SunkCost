using UnityEngine;

namespace SunkCost.Noise
{
    /// <summary>
    /// What kind of sound was made. Creatures care about the kind, not just the volume:
    /// the Siltcrawler listens for footsteps, the Bell Eater only for the elevator.
    /// </summary>
    public enum NoiseKind
    {
        Footstep,
        Sprint,
        Thruster,
        Elevator,
        Impact,      // a dropped object, a body hitting the floor
        Tool,        // cutting torch, tank swap, winch
        Voice,       // a scream through the helmet radio
        Beacon,      // deliberate distraction: a noisemaker
        Dash         // a diver's burst on Alt (Dan, 21 September 2026): louder than a sprinting step
    }

    /// <summary>
    /// One sound made in the world. Emitted on the server only, via <see cref="NoiseSystem"/>.
    ///
    /// Loudness is expressed in METRES — the distance the sound carries — not in decibels
    /// or a 0..1 value. That makes it directly comparable to a creature's hearing range and
    /// keeps the numbers designers tune in units they can measure in the scene.
    /// </summary>
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly NoiseKind Kind;

        /// <summary>NetworkObject id of whoever made it, or 0 for the world.</summary>
        public readonly int SourceId;

        public NoiseEvent(Vector3 position, float radius, NoiseKind kind, int sourceId = 0)
        {
            Position = position;
            Radius = radius;
            Kind = kind;
            SourceId = sourceId;
        }

        public override string ToString() => $"{Kind} @ {Position} r={Radius}m src={SourceId}";
    }
}
