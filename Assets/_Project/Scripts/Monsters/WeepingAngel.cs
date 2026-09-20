using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Weeping Angel (docs/DESIGN.md §6): being watched. Frozen while inside
    // any living diver's view within AngelWatchMeters with a clear line of sight
    // (the TV and spectators do not count: they are not divers below). Unwatched
    // it moves at sprint speed straight at the nearest diver it has noticed.
    // Reaching you is death.
    public sealed class WeepingAngel : Creature
    {
        public bool ServerWatched { get; private set; }
        public int ServerWatcherId { get; private set; } = -1;
        private float lastWatchedAt = float.NegativeInfinity;
        // A remote diver's eyes reach the server a little late: the freeze outlasts the last watched frame by this.
        private const float GraceSeconds = 0.25f;

        protected override void ServerThink(float dt)
        {
            ServerWatched = CreatureSenses.Watched(BodyPoints(), Settings, out HQPlayerController watcher);
            ServerWatcherId = watcher != null ? watcher.OwnerId : -1;
            if (ServerWatched) lastWatchedAt = Now;
            if (ServerWatched || Now - lastWatchedAt < GraceSeconds) { SetPose(CreaturePose.Frozen); return; } // not a turn, not a step
            HQPlayerController prey = CreatureSenses.Nearest(transform.position, Settings.AngelWakeMeters);
            if (prey == null) { SetPose(CreaturePose.Idle); SetTarget(-1); return; }
            SetTarget(prey.OwnerId);
            SetPose(CreaturePose.Hunting);
            FaceToward(prey.transform.position);
            if (WithinReach(prey) && StrikeReady) { Strike(prey, 0f); return; }
            MoveToward(prey.transform.position, SprintSpeed * Settings.AngelSpeedFactor, dt, Settings.TouchStandoffMeters);
        }

        protected override string ServerBrainStatus() => ServerWatched ? " watched by " + ServerWatcherId : " unwatched";
    }
}
