using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Charger (docs/DESIGN.md §6): sight. Facing a diver it shakes for 1.5 s
    // (the tell), then rushes in a straight line — aimed where the diver stood
    // at the launch — about 12 m at twice sprint speed. Step aside and it
    // passes; it needs 3 s to turn and try again. A hit: 35 HP and a leak.
    public sealed class Charger : Creature
    {
        private enum Phase { Hunting, Windup, Rushing, Turning }

        private Phase phase = Phase.Hunting;
        private HQPlayerController prey;
        private float phaseUntil;
        private Vector3 rushDir;
        private float rushLeft;
        private bool rushHit;
        public int ServerRushes { get; private set; }

        protected override void ServerThink(float dt)
        {
            prey = Living(prey);
            switch (phase)
            {
                case Phase.Hunting:
                    if (prey == null || !CreatureSenses.CanSee(EyePoint, prey, Settings))
                    {
                        prey = null;
                        foreach (HQPlayerController diver in CreatureSenses.Divers())
                            if (CreatureSenses.CanSee(EyePoint, diver, Settings)) { prey = diver; break; }
                    }
                    if (prey == null) { SetPose(CreaturePose.Idle); SetTarget(-1); return; }
                    SetTarget(prey.OwnerId);
                    FaceToward(prey.transform.position);
                    if (!CreatureSenses.Safe(prey, Settings) && CreatureSenses.Flat(transform.position, prey.transform.position) <= Settings.ChargerRushFromMeters)
                    {
                        phase = Phase.Windup;
                        phaseUntil = Now + Settings.ChargerWindupSeconds;
                        SetPose(CreaturePose.Windup);
                        return;
                    }
                    SetPose(CreaturePose.Hunting);
                    MoveToward(prey.transform.position, WalkSpeed * 0.8f, dt);
                    return;
                case Phase.Windup:
                    if (prey == null) { phase = Phase.Hunting; return; }
                    FaceToward(prey.transform.position);
                    if (Now < phaseUntil) return;
                    // The line is fixed at the launch: where the diver stands now.
                    rushDir = prey.transform.position - transform.position; rushDir.y = 0f;
                    rushDir = rushDir.sqrMagnitude > 0.01f ? rushDir.normalized : transform.forward;
                    rushLeft = Settings.ChargerRushMeters;
                    rushHit = false;
                    ServerRushes++;
                    phase = Phase.Rushing;
                    SetPose(CreaturePose.Rushing);
                    return;
                case Phase.Rushing:
                {
                    Vector3 before = transform.position;
                    float speed = SprintSpeed * Settings.ChargerRushSpeedFactor;
                    float step = Mathf.Min(rushLeft, speed * dt);
                    bool blocked = MoveToward(before + rushDir * (step + 2f), speed, dt);
                    rushLeft -= step;
                    Vector3 after = before + rushDir * step;
                    if (!rushHit)
                        foreach (HQPlayerController diver in CreatureSenses.Divers())
                        {
                            if (CreatureSenses.Safe(diver, Settings)) continue;
                            if (DistanceToSegment(diver.transform.position, before, after) > Settings.ChargerHitRadius + 0.3f) continue;
                            if (Strike(diver, Settings.ChargerDamage)) { rushHit = true; break; }
                        }
                    if (rushLeft <= 0.01f || blocked)
                    {
                        phase = Phase.Turning;
                        phaseUntil = Now + Settings.ChargerTurnSeconds;
                        SetPose(CreaturePose.Idle);
                    }
                    return;
                }
                case Phase.Turning:
                    if (prey != null) FaceToward(prey.transform.position);
                    if (Now >= phaseUntil) { phase = Phase.Hunting; }
                    return;
            }
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = 0f; a.y = 0f; b.y = 0f;
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, ab) / len);
            return Vector3.Distance(point, a + ab * t);
        }

        protected override string ServerBrainStatus() => " phase=" + phase + " rushes=" + ServerRushes;
    }
}
