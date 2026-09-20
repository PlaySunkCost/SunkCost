using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Lure (docs/DESIGN.md §6): light. A lit headlamp within LureSeeMeters
    // draws it; when it sees a lamp it shoots a bolt of light at it (at where the
    // lamp is now — move and it misses). Lamps off: it loses you about 8 s after
    // the beam goes dark. A bolt that lands: 35 HP and a leak. It never touches.
    [RequireComponent(typeof(CreatureBolts))]
    public sealed class Lure : Creature
    {
        private CreatureBolts bolts;
        private HQPlayerController seen;
        private Vector3 seenAt;
        private float seenTime = float.NegativeInfinity;
        private float nextShotAt;

        protected override void Awake()
        {
            base.Awake();
            bolts = GetComponent<CreatureBolts>();
        }

        protected override void ServerThink(float dt)
        {
            HQPlayerController lit = null;
            float best = Settings.LureSeeMeters;
            foreach (HQPlayerController diver in CreatureSenses.Divers())
            {
                if (!CreatureSenses.LampLit(diver) || CreatureSenses.Safe(diver, Settings)) continue;
                Vector3 chest = CreatureSenses.Chest(diver);
                float d = Vector3.Distance(EyePoint, chest);
                if (d >= best || !CreatureSenses.ClearLine(EyePoint, chest)) continue;
                best = d; lit = diver;
            }
            if (lit != null)
            {
                seen = lit; seenAt = lit.transform.position; seenTime = Now;
                SetTarget(lit.OwnerId);
                FaceToward(seenAt);
                if (Now >= nextShotAt)
                {
                    bolts.ServerFire(EyePoint, CreatureSenses.Chest(lit), dark: false, Settings.LureDamage, MonsterCatalog.DisplayName(Kind));
                    nextShotAt = Now + Settings.LureShotCooldownSeconds;
                    SetPose(CreaturePose.Shooting);
                }
                else if (Pose != CreaturePose.Shooting || Now > nextShotAt - Settings.LureShotCooldownSeconds + 0.6f) SetPose(CreaturePose.Hunting);
                MoveToward(seenAt, WalkSpeed * Settings.LureApproachSpeedFactor, dt);
                return;
            }
            if (Now - seenTime < Settings.LureForgetSeconds)
            {
                SetPose(CreaturePose.Drawn);
                FaceToward(seenAt);
                MoveToward(seenAt, WalkSpeed * Settings.LureApproachSpeedFactor, dt);
                return;
            }
            seen = null;
            SetPose(CreaturePose.Idle);
            SetTarget(-1);
        }

        protected override string ServerBrainStatus() => $" seen={(seen != null ? seen.OwnerId : -1)} ago={(float.IsInfinity(seenTime) ? -1f : Now - seenTime):0.0}s fired={bolts.ServerFired} hits={bolts.ServerHits}";
    }
}
