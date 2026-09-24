using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Lure (docs/DESIGN.md §6): light. A lit headlamp within LureSeeMeters
    // draws it; it drifts toward the lamp, and when its shot is ready it plants
    // itself, turns its lantern on the lamp and charges (Aiming) — a glow at the
    // lantern and a faint thread, the tell — then burns a beam of light for
    // BeamSeconds that turns after that diver (Shooting), and hurts once. Then it
    // folds, spent, for a beat (Recovering) before it drifts again. Lamps off: it
    // loses you LureForgetSeconds after it last saw the lamp. A beam that lands:
    // 35 HP and a leak. It never touches.
    //
    // Body language (mon-lure, 24 September 2026): it drifts like a moth at a
    // window — easing up to speed and down to its standoff, weaving a little from
    // side to side, turning gently — and it moves where it faces. While a beam is
    // up it is planted, whatever the lamps do, and faces along the beam, so its
    // body, its lantern and the beam agree; the beam leaves the lantern
    // (CreatureBolts.Origin: the rig's BeamOrigin), which is where its damage starts too.
    [RequireComponent(typeof(CreatureBolts))]
    public sealed class Lure : Creature
    {
        [Header("Body language (the tuning of the moves; the rules are in MonsterSettings)")]
        [Tooltip("It turns its lantern onto the lamp before it starts charging, until it faces within this many degrees.")]
        [SerializeField] private float aimStartDegrees = 12f;
        [Tooltip("How fast it turns while it drifts, degrees per second.")]
        [SerializeField] private float driftTurnDegPerSec = 200f;
        [Tooltip("How fast it turns while planted: onto the lamp before the charge, and along the beam.")]
        [SerializeField] private float aimTurnDegPerSec = 360f;
        [Tooltip("How quickly its drift builds and falls, metres per second squared.")]
        [SerializeField] private float driftAcceleration = 2.5f;
        [Tooltip("How hard it brakes to plant itself for a shot, metres per second squared.")]
        [SerializeField] private float plantDeceleration = 8f;
        [Tooltip("It weaves this far either side of its line toward the light (a moth's drift); 0 = straight.")]
        [SerializeField] private float weaveMetres = 0.2f;
        [SerializeField] private float weaveSeconds = 3.2f;
        [Tooltip("After a beam it stays planted, folded and spent, this long (the Recovering pose).")]
        [SerializeField] private float recoverSeconds = 0.9f;

        private CreatureBolts bolts;
        private HQPlayerController seen;
        private Vector3 seenAt;
        private float seenTime = float.NegativeInfinity;
        private float nextShotAt;
        private float driftSpeed;
        private float recoverUntil = float.NegativeInfinity;
        private bool beamWasUp;
        private float weavePhase;

        public float ServerDriftSpeed => driftSpeed;
        public bool ServerRecovering => Now < recoverUntil;
        public int ServerAims { get; private set; }
        public float ServerAimStartYawError { get; private set; }
        public string ServerDriftNote { get; private set; } = string.Empty; // the last drift step, for the checks

        protected override void Awake()
        {
            base.Awake();
            bolts = GetComponent<CreatureBolts>();
            weavePhase = Random.value;
        }


        private HQPlayerController LitDiverInSight()
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
            return lit;
        }

        protected override void ServerThink(float dt)
        {
            HQPlayerController lit = LitDiverInSight();
            if (lit != null)
            {
                seen = lit; seenAt = lit.transform.position; seenTime = Now;
                SetTarget(lit.OwnerId);
            }

            // A beam is up: planted, facing along it, whatever the lamps do now (once it
            // charges only a wall or a dash saves you).
            if (bolts.ServerAiming)
            {
                beamWasUp = true;
                driftSpeed = 0f;
                SetPose(bolts.ServerCharging ? CreaturePose.Aiming : CreaturePose.Shooting);
                Turn(transform.position + bolts.ServerAimDirection, aimTurnDegPerSec, dt);
                return;
            }
            if (beamWasUp)
            {
                beamWasUp = false;
                recoverUntil = Now + recoverSeconds;
            }
            if (Now < recoverUntil)
            {
                // Spent: it folds where it stands, turning only a little after the lamp.
                SetPose(CreaturePose.Recovering);
                driftSpeed = 0f;
                if (lit != null) Turn(seenAt, driftTurnDegPerSec * 0.25f, dt);
                return;
            }

            if (lit != null)
            {
                SetPose(CreaturePose.Hunting);
                if (Now >= nextShotAt)
                {
                    // Plant, turn the lantern onto the lamp, then charge.
                    driftSpeed = Mathf.MoveTowards(driftSpeed, 0f, plantDeceleration * dt);
                    if (driftSpeed > 0.01f) Coast(dt);
                    Turn(seenAt, aimTurnDegPerSec, dt);
                    float off = YawOff(seenAt);
                    if (driftSpeed <= 0.25f && off <= aimStartDegrees)
                    {
                        // The charge begins at the lamp; the beam follows that diver for BeamSeconds after it.
                        // From the lantern (CreatureBolts.Origin, the rig's BeamOrigin), where its damage starts too.
                        bolts.ServerAim(bolts.Origin, CreatureBolts.AimPoint(lit), dark: false, Settings.LureDamage, MonsterCatalog.DisplayName(Kind), lit.OwnerId);
                        if (bolts.ServerAiming)
                        {
                            ServerAims++;
                            ServerAimStartYawError = off;
                            nextShotAt = Now + Settings.BeamChargeSeconds + Settings.BeamSeconds + Settings.LureShotCooldownSeconds;
                            SetPose(CreaturePose.Aiming);
                        }
                    }
                    return;
                }
                Drift(seenAt, dt);
                return;
            }
            if (Now - seenTime < Settings.LureForgetSeconds)
            {
                SetPose(CreaturePose.Drawn);
                Drift(seenAt, dt);
                return;
            }
            seen = null;
            SetPose(CreaturePose.Idle);
            SetTarget(-1);
            driftSpeed = Mathf.MoveTowards(driftSpeed, 0f, driftAcceleration * dt);
            if (driftSpeed > 0.01f) Coast(dt);
        }

        // Toward a point the way a moth goes to a lamp: up to speed and down to the
        // standoff smoothly, weaving a little either side, turning gently, and moving
        // along where it faces (slower while it still turns) so it never crabs.
        private void Drift(Vector3 point, float dt)
        {
            Vector3 here = transform.position;
            Vector3 to = point - here; to.y = 0f;
            float distance = to.magnitude;
            float standoff = Settings.ShooterStandoffMeters;
            float remaining = Mathf.Max(0f, distance - standoff);
            float cruise = WalkSpeed * Settings.LureApproachSpeedFactor;
            float want = Mathf.Min(cruise, Mathf.Sqrt(2f * driftAcceleration * remaining));
            driftSpeed = Mathf.MoveTowards(driftSpeed, want, driftAcceleration * dt);
            Vector3 goal = point;
            if (weaveMetres > 0f && weaveSeconds > 0f && distance > 0.01f)
            {
                // It steers for a point a few metres ahead on its line, swung a little to either
                // side, so the weave shows at any distance (a moth's wander, not a zigzag).
                Vector3 along = to / distance;
                Vector3 side = Vector3.Cross(Vector3.up, along);
                float fade = Mathf.Clamp01(remaining / 4f);
                goal = here + along * Mathf.Min(distance, 3f) + side * (weaveMetres * fade * Mathf.Sin((Now / weaveSeconds + weavePhase) * Mathf.PI * 2f));
            }
            Turn(goal, driftTurnDegPerSec, dt);
            Vector3 toGoal = goal - here; toGoal.y = 0f;
            float align = toGoal.sqrMagnitude > 1e-6f ? Vector3.Dot(transform.forward, toGoal.normalized) : 1f;
            // Facing well away, it turns where it stands (and the base's unstick, which sidesteps
            // a creature that wants to move but barely does, is not fooled into a crab); it steps
            // off as it comes round.
            float scale = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.9f, align));
            ServerDriftNote = $"align {align:0.00} scale {scale:0.00} remaining {remaining:0.00} want {want:0.00} speed {driftSpeed:0.00}";
            if (scale <= 0.01f) { driftSpeed = Mathf.MoveTowards(driftSpeed, 0f, plantDeceleration * dt); return; }
            // There: it stops rather than inching (a creature that keeps asking to move and barely
            // does is sidestepped by the base's unstick, which reads as a crab).
            if (remaining < 0.2f) { driftSpeed = 0f; return; }
            Coast(dt, distance, standoff, scale);
        }

        // A step along its facing at the drift speed, slowed while it still turns away
        // from where it wants to go; MoveToward keeps the standoff and the safe ground.
        private float slowSince = -1f;

        private void Coast(float dt, float distance = float.PositiveInfinity, float standoff = 0.3f, float scale = 1f)
        {
            if (driftSpeed <= 0f) return;
            // A crawl of more than half a second is a stop: it stands and turns instead.
            if (driftSpeed * scale < 0.35f)
            {
                if (slowSince < 0f) slowSince = Now;
                if (Now - slowSince > 0.5f) return;
            }
            else slowSince = -1f;
            Vector3 forward = transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();
            float reach = float.IsPositiveInfinity(distance) ? driftSpeed * dt + 1f : distance;
            MoveToward(transform.position + forward * reach, driftSpeed * scale, dt, float.IsPositiveInfinity(distance) ? 0.3f : standoff);
        }

        private float YawOff(Vector3 point)
        {
            Vector3 to = point - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(transform.forward, to);
        }

        private void Turn(Vector3 point, float degreesPerSecond, float dt)
        {
            Vector3 to = point - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to, Vector3.up), degreesPerSecond * dt);
        }

        protected override string ServerBrainStatus() => $" seen={(seen != null ? seen.OwnerId : -1)} ago={(float.IsInfinity(seenTime) ? -1f : Now - seenTime):0.0}s drift={driftSpeed:0.00} recovering={ServerRecovering} fired={bolts.ServerFired} hits={bolts.ServerHits}";
    }
}
