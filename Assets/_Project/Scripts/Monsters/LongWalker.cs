using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Long Walker (docs/DESIGN.md §6): sight. Once it has seen a diver it
    // walks after that diver until they leave the site — at 85 % of walking
    // speed, so you outwalk it unless you are heavy or you stop. It stops at the
    // car's doorway (the safe ground). Reaching you is death.
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
            FaceToward(prey.transform.position);
            if (WithinReach(prey) && StrikeReady) { Strike(prey, 0f); return; }
            MoveToward(prey.transform.position, WalkSpeed * Settings.WalkerSpeedFactor, dt);
        }

        protected override string ServerBrainStatus() => prey != null ? " prey=" + prey.OwnerId : string.Empty;
    }
}
