using SunkCost.Noise;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Listener (docs/DESIGN.md §6): sound. Blind; hears exactly what the
    // noise bus says — a walking step 6 m, a sprinting step 15 m, the car's 60 m
    // scream — when the sound's radius reaches it. It turns and walks toward the
    // last sound and shoots a dark bolt at where the sound was: move and it
    // misses. Crouch-walking is silent. It loses interest ListenerForgetSeconds
    // after silence, and never camps the door (the safe ground stops it).
    [RequireComponent(typeof(CreatureBolts))]
    public sealed class Listener : Creature
    {
        private CreatureBolts bolts;
        private Vector3 heardAt;
        private float heardTime = float.NegativeInfinity;
        private NoiseKind heardKind;
        private float nextShotAt;
        private bool unshot; // a sound not yet answered

        public int ServerHeard { get; private set; }
        public NoiseKind ServerLastKind => heardKind;

        protected override void Awake()
        {
            base.Awake();
            bolts = GetComponent<CreatureBolts>();
        }

        public override void OnNoise(in NoiseEvent noise)
        {
            if (!IsServerStarted || !ServerAwake) return;
            if (Vector3.Distance(noise.Position, transform.position) > noise.Radius) return;
            heardAt = noise.Position; heardTime = Now; heardKind = noise.Kind;
            unshot = true;
            ServerHeard++;
        }

        protected override void ServerThink(float dt)
        {
            if (Now - heardTime > Settings.ListenerForgetSeconds)
            {
                SetPose(CreaturePose.Idle);
                return;
            }
            FaceToward(heardAt);
            if (unshot && Now >= nextShotAt && CreatureSenses.Flat(heardAt, transform.position) > 1.5f)
            {
                bolts.ServerFire(EyePoint, heardAt + Vector3.up * 1.1f, dark: true, Settings.ListenerDamage, MonsterCatalog.DisplayName(Kind));
                nextShotAt = Now + Settings.ListenerShotCooldownSeconds;
                unshot = false;
                SetPose(CreaturePose.Shooting);
            }
            else if (Pose != CreaturePose.Shooting || Now > nextShotAt - Settings.ListenerShotCooldownSeconds + 0.6f) SetPose(CreaturePose.Drawn);
            MoveToward(heardAt, WalkSpeed * Settings.ListenerApproachSpeedFactor, dt);
        }

        protected override string ServerBrainStatus() => $" heard={ServerHeard} last={heardKind} ago={(float.IsInfinity(heardTime) ? -1f : Now - heardTime):0.0}s fired={bolts.ServerFired} hits={bolts.ServerHits}";
    }
}
