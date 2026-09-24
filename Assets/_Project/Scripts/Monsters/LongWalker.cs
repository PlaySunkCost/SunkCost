using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Long Walker (docs/DESIGN.md §6): sight. Once it has seen a diver it
    // walks after that diver until they leave the site — at WalkerSpeedFactor of
    // walking speed (0.6, Dan, 20 September 2026), so you outwalk it unless you are
    // heavy or you stop. It stops at the car's doorway (the safe ground). Reaching
    // you is death: it grabs you and lifts you to its face for about two seconds
    // first (Dan, 24 September 2026) — the CreatureGrab on its prefab, held by
    // Creature.Grab.cs, which runs instead of this brain while it holds.
    public sealed class LongWalker : Creature
    {
        private HQPlayerController prey;

        protected override void ServerThink(float dt)
        {
            prey = Living(prey);
            if (prey == null)
            {
                foreach (HQPlayerController diver in CreatureSenses.Divers())
                    if (CreatureSenses.CanSee(EyePoint, diver, Settings)) { prey = diver; break; }
                if (prey == null) { SetPose(CreaturePose.Idle); SetTarget(-1); return; }
                Debug.Log($"[Monsters] the Long Walker saw {World.WorldSceneFlow.DisplayName(prey.Owner)} and walks");
            }
            SetTarget(prey.OwnerId);
            SetPose(CreaturePose.Hunting);
            if (WithinReach(prey) && StrikeReady) { FaceToward(prey.transform.position); Strike(prey, 0f); return; }
            MoveToward(prey.transform.position, WalkSpeed * Settings.WalkerSpeedFactor, dt, Settings.TouchStandoffMeters);
            // It faces where it walks: its prey, or along the wall it is working round.
            Vector3 heading = ServerSidestepHeading;
            FaceToward(heading != Vector3.zero ? transform.position + heading : prey.transform.position, heading != Vector3.zero ? 240f : 540f);
        }

        // A wall between it and its prey: it keeps to one side and works round it.
        protected override bool FollowsWalls => true;

        protected override string ServerBrainStatus() =>
            (prey != null ? " prey=" + prey.OwnerId : string.Empty) + (ServerGrabbing ? $" holding={ServerGrabVictim?.OwnerId} t={ServerGrabSeconds:0.00}" : string.Empty) + (ServerGrabsStarted > 0 ? $" grabs={ServerGrabsStarted} kills={ServerGrabKills} letGo={ServerGrabsLetGo} last='{ServerGrabOutcome}'" : string.Empty);
    }
}
