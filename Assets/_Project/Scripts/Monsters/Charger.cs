using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Charger (docs/DESIGN.md §6): sight. Facing a diver it shakes for 1.5 s
    // (the tell), then rushes 20 m in a straight line at three times sprint speed.
    // Step aside or dash and it passes; it needs about 3 s to turn and try again. A
    // hit: 35 HP, a leak and a knock-back of about 3 m along its path with a jolt
    // of the diver's view (Dan, 24 September 2026).
    //
    // The charge, stage by stage (mon-charger, 24 September 2026):
    // - Hunting: it prowls after a diver it can see, always walking where it faces
    //   (it turns as it goes, never crabs); a wall its head meets it follows a while.
    // - Windup: only once it faces the diver. It keeps turning after the diver
    //   through the tell, then for the last aimLockSeconds the aim is fixed (the
    //   coiled, still beat before the launch): the line it will run is the way its
    //   body points. A locked straight line (Dan): never a homing missile.
    // - Rushing: from standing to top speed in accelerationSeconds; the hit is
    //   judged at the ram's face, not at the middle of the body, once per rush.
    // - The end: a miss runs on to 20 m and skids to a stop over the last
    //   missBrakeMeters; a hit stops hard (hitBrakeMeters: the blow took its
    //   momentum); a wall or the safe ground stops it at its face, dazed a little
    //   longer. It never turns while it skids.
    // - Recovering: winded for recoverSeconds, then back to the hunt; the next tell
    //   waits ChargerTurnSeconds after the stop.
    // The server writes the pose; the clips (tools/blender/clips/Charger.py) are
    // authored for these timings and speeds.
    public sealed class Charger : Creature
    {
        public enum ChargePhase { Hunting, Windup, Rushing, Braking, Recovering }

        [Header("The charge (mon-charger, 24 September 2026; the rest is in MonsterSettings)")]
        [Tooltip("How fast it turns while it prowls, degrees per second.")]
        [SerializeField] private float prowlTurnDegPerSec = 200f;
        [Tooltip("How fast its aim follows the diver through the tell, degrees per second (a dash out-turns it).")]
        [SerializeField] private float aimTurnDegPerSec = 150f;
        [Tooltip("It starts the tell only when it faces the diver within this many degrees.")]
        [SerializeField] private float windupFacingDegrees = 25f;
        [Tooltip("The last part of the tell, seconds: coiled and still, the aim fixed.")]
        [SerializeField] private float aimLockSeconds = 0.2f;
        [Tooltip("From standing to the rush's top speed, seconds.")]
        [SerializeField] private float accelerationSeconds = 0.25f;
        [Tooltip("A miss skids to a stop over the last this many metres of the rush.")]
        [SerializeField] private float missBrakeMeters = 3f;
        [Tooltip("A hit stops it within this many metres (the diver took its momentum).")]
        [SerializeField] private float hitBrakeMeters = 1.2f;
        [Tooltip("Winded after it stops, seconds (the Recovering clip), before it hunts again.")]
        [SerializeField] private float recoverSeconds = 1.8f;
        [Tooltip("Extra seconds dazed after it runs into a wall.")]
        [SerializeField] private float wallDazeSeconds = 0.6f;
        [Tooltip("The knock-back along its path on a hit, metres (Dan: about 3).")]
        [SerializeField] private float knockbackMeters = 3f;
        [Tooltip("From its middle to the front of its ram head, metres: where a hit and a wall are judged.")]
        [SerializeField] private float frontReach = 1.55f;
        [Tooltip("From its middle to its tail, metres: a diver beside the body is inside it.")]
        [SerializeField] private float backReach = 1.7f;
        [Tooltip("A diver's own radius, metres, added in front of the face.")]
        [SerializeField] private float diverRadius = 0.35f;
        [Tooltip("It stops prowling this close to its diver (flat, metres from its middle): it does not walk into them.")]
        [SerializeField] private float prowlStopMeters = 2.4f;

        private ChargePhase phase = ChargePhase.Hunting;
        private HQPlayerController prey;
        private float phaseStarted, phaseUntil;
        private float nextWindupAt = float.NegativeInfinity;
        private Vector3 rushDir;
        private float rushLeft, rushSpeed, brakeDecel;
        private bool rushHit;
        private Vector3 rushLast;
        private int rushStalled;
        private float dazeExtra;
        private Vector3 detour;
        private float detourUntil = float.NegativeInfinity;
        private float detourSide;                       // +1 / -1: which way round the wall it chose
        private float detourSideUntil = float.NegativeInfinity;

        private static readonly RaycastHit[] ProbeHits = new RaycastHit[16];

        // The server's view for the checks.
        public ChargePhase Phase => phase;
        public int ServerRushes { get; private set; }
        public int ServerRushHits { get; private set; }
        public int ServerWallStops { get; private set; }
        public Vector3 RushDirection => rushDir;
        public float RushSpeed => rushSpeed;
        public float RushTravelled => Settings.ChargerRushMeters - rushLeft;
        public float LastStopAt { get; private set; } = float.NegativeInfinity;
        public float LastHitFaceMeters { get; private set; } = float.NaN; // how far ahead of its middle the diver was when struck
        public float FrontReach => frontReach;
        public float AimLockSeconds => aimLockSeconds;
        public float RecoverSeconds => recoverSeconds;
        public float KnockbackMeters => knockbackMeters;
        public float TopSpeed => SprintSpeed * Settings.ChargerRushSpeedFactor;
        public float ProwlSpeed => WalkSpeed * 0.8f;

        protected override void ServerThink(float dt)
        {
            prey = Living(prey);
            switch (phase)
            {
                case ChargePhase.Hunting: Hunt(dt); return;
                case ChargePhase.Windup: Windup(); return;
                case ChargePhase.Rushing:
                case ChargePhase.Braking: Rush(dt); return;
                case ChargePhase.Recovering:
                    SetPose(CreaturePose.Recovering);
                    if (Now >= phaseUntil) Enter(ChargePhase.Hunting);
                    return;
            }
        }

        private void Enter(ChargePhase next)
        {
            phase = next;
            phaseStarted = Now;
        }

        private void Hunt(float dt)
        {
            if (prey == null || !CreatureSenses.CanSee(EyePoint, prey, Settings))
            {
                prey = null;
                foreach (HQPlayerController diver in CreatureSenses.Divers())
                    if (CreatureSenses.CanSee(EyePoint, diver, Settings)) { prey = diver; break; }
            }
            if (prey == null) { SetPose(CreaturePose.Idle); SetTarget(-1); return; }
            SetTarget(prey.OwnerId);
            Vector3 here = transform.position;
            Vector3 to = prey.transform.position - here; to.y = 0f;
            float distance = to.magnitude;
            // Round a wall its head met: it follows the wall a while, then looks for the diver again.
            bool detouring = Now < detourUntil;
            Quaternion before = transform.rotation;
            FaceToward(detouring ? here + detour * 4f : prey.transform.position, prowlTurnDegPerSec);
            // Never a turn that swings its head into a wall: it keeps along the wall instead.
            if (Quaternion.Angle(before, transform.rotation) > 0.01f && FreeAhead(here, FlatFacing, 0.02f) <= 0.01f && FreeAhead(here, FlatForward(before), 0.02f) > 0.01f)
            {
                transform.rotation = before;
                if (detouring || detourSide != 0f) { detourUntil = Mathf.Max(detourUntil, Now + 0.4f); detouring = true; }
            }
            float off = distance > 0.01f ? Vector3.Angle(transform.forward, to) : 0f;
            if (!detouring && Now >= nextWindupAt && !CreatureSenses.Safe(prey, Settings) && distance <= Settings.ChargerRushFromMeters && off <= windupFacingDegrees)
            {
                Enter(ChargePhase.Windup);
                phaseUntil = Now + Settings.ChargerWindupSeconds;
                SetPose(CreaturePose.Windup);
                return;
            }
            // It walks where it faces, slower while it is still turning: an arc, never a crab.
            Vector3 forward = transform.forward; forward.y = 0f; forward.Normalize();
            float align = Mathf.Clamp01(Mathf.Cos((detouring ? Vector3.Angle(forward, detour) : off) * Mathf.Deg2Rad));
            float speed = ProwlSpeed * Mathf.Lerp(0.35f, 1f, align);
            bool close = distance <= prowlStopMeters;
            SetPose(CreaturePose.Hunting);
            if (close) return; // it turns on the spot (the rig shows its stand pose)
            if (FreeAhead(here, forward, speed * dt + 0.25f, out Vector3 normal) <= speed * dt + 0.05f)
            {
                // Its head is at a wall: along the wall, on the side of the diver.
                Vector3 along = Vector3.Cross(Vector3.up, normal); along.y = 0f;
                if (along.sqrMagnitude < 0.0001f) along = Vector3.Cross(Vector3.up, forward);
                along.Normalize();
                // The side nearer the diver, kept for a while so it does not dither at the wall's middle.
                if (Now >= detourSideUntil) detourSide = Vector3.Dot(Vector3.Cross(Vector3.up, normal), to) >= 0f ? 1f : -1f;
                if (Vector3.Dot(Vector3.Cross(Vector3.up, normal), along) * detourSide < 0f) along = -along;
                detourSideUntil = Now + 4f;
                if (!detouring || Vector3.Dot(along, detour) < 0.5f) { detour = along; detourUntil = Now + 1.2f; }
                return;
            }
            MoveToward(here + forward * (speed * dt + 1f), speed, dt, 0f);
        }

        private void Windup()
        {
            if (prey == null) { Enter(ChargePhase.Hunting); SetPose(CreaturePose.Hunting); return; }
            // The aim follows the diver through the tell, then locks for the last beat.
            if (Now < phaseUntil - aimLockSeconds) FaceToward(prey.transform.position, aimTurnDegPerSec);
            if (Now < phaseUntil) return;
            // The line is the way its body points at the launch.
            rushDir = transform.forward; rushDir.y = 0f;
            rushDir = rushDir.sqrMagnitude > 0.0001f ? rushDir.normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(rushDir, Vector3.up);
            rushLeft = Settings.ChargerRushMeters;
            rushSpeed = 0f;
            rushHit = false;
            rushLast = transform.position;
            rushStalled = 0;
            dazeExtra = 0f;
            LastHitFaceMeters = float.NaN;
            ServerRushes++;
            Enter(ChargePhase.Rushing);
            SetPose(CreaturePose.Rushing);
        }

        private void Rush(float dt)
        {
            Vector3 here = transform.position;
            float travelled = CreatureSenses.Flat(rushLast, here);
            rushLeft -= travelled;
            rushLast = here;

            // The ram's face meets a diver: once per rush, never behind a wall it stopped at.
            if (!rushHit && rushSpeed > WalkSpeed)
                foreach (HQPlayerController diver in CreatureSenses.Divers())
                {
                    if (!Contact(diver, here, out float along)) continue;
                    if (!Strike(diver, Settings.ChargerDamage)) continue;
                    rushHit = true;
                    ServerRushHits++;
                    LastHitFaceMeters = along;
                    if (!diver.IsDead) diver.ServerKnockback(rushDir * knockbackMeters);
                    Debug.Log($"[Monsters] the Charger hit {World.WorldSceneFlow.DisplayName(diver.Owner)} {along:0.00} m ahead of its middle at {rushSpeed:0.0} m/s");
                    Brake(hitBrakeMeters);
                    break;
                }

            float top = TopSpeed;
            if (phase == ChargePhase.Rushing)
            {
                float t = Now - phaseStarted;
                rushSpeed = accelerationSeconds > 0f ? top * Mathf.Clamp01(t / accelerationSeconds + 0.15f) : top;
                if (rushLeft <= missBrakeMeters) Brake(Mathf.Max(0.1f, rushLeft));
            }
            else
            {
                rushSpeed = Mathf.Max(0f, rushSpeed - brakeDecel * dt);
            }
            if (phase == ChargePhase.Braking && rushSpeed <= 0.3f) { Stop(false); return; }

            float step = rushSpeed * dt;
            // A wall, or the safe ground's edge, ahead of the face: it stops there.
            float free = Mathf.Min(FreeAhead(here, rushDir, step + 0.3f), SafeAhead(here, rushDir));
            if (free <= step)
            {
                if (free > 0.02f) MoveToward(here + rushDir * (free + 1f), free / dt, dt, 0f);
                Stop(true);
                return;
            }
            rushStalled = travelled < step * 0.3f && dt > 0f && Now - phaseStarted > 0.1f ? rushStalled + 1 : 0;
            if (rushStalled >= 4) { Stop(true); return; } // something the probe did not see (a step it cannot climb)
            if (MoveToward(here + rushDir * (step + 1f), rushSpeed, dt, 0f)) { Stop(true); return; }
        }

        private void Brake(float meters)
        {
            if (phase == ChargePhase.Braking) return;
            Enter(ChargePhase.Braking);
            brakeDecel = rushSpeed * rushSpeed / (2f * Mathf.Max(0.1f, meters));
            SetPose(CreaturePose.Recovering);
        }

        private void Stop(bool wall)
        {
            if (wall) { ServerWallStops++; dazeExtra = wallDazeSeconds; }
            rushSpeed = 0f;
            LastStopAt = Now;
            nextWindupAt = Now + Settings.ChargerTurnSeconds;
            Enter(ChargePhase.Recovering);
            phaseUntil = Now + recoverSeconds + dazeExtra;
            SetPose(CreaturePose.Recovering);
        }

        private Vector3 FlatFacing => FlatForward(transform.rotation);
        private static Vector3 FlatForward(Quaternion rotation)
        {
            Vector3 f = rotation * Vector3.forward; f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        // The diver is inside the body or just in front of its face: flat along the
        // line, within the half-width, and not over its back or under the floor.
        private bool Contact(HQPlayerController diver, Vector3 here, out float along)
        {
            along = 0f;
            if (diver == null || CreatureSenses.Safe(diver, Settings)) return false;
            Vector3 rel = diver.transform.position - here;
            float up = rel.y;
            rel.y = 0f;
            along = Vector3.Dot(rel, rushDir);
            float side = Vector3.Cross(rushDir, rel).magnitude;
            return along <= frontReach + diverRadius && along >= -backReach
                && side <= Settings.ChargerHitRadius + 0.3f
                && up > -1f && up < 1.3f;
        }

        // How far its face can go along a direction before the world stops it (a
        // sphere at chest height; the floor and gentle slopes do not count).
        private float FreeAhead(Vector3 here, Vector3 dir, float look) => FreeAhead(here, dir, look, out _);

        private float FreeAhead(Vector3 here, Vector3 dir, float look, out Vector3 normal)
        {
            normal = -dir;
            const float radius = 0.45f;
            Vector3 origin = here + Vector3.up * 0.75f;
            float reach = frontReach - radius;
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, ProbeHits, reach + look, CreatureSenses.SightMask, QueryTriggerInteraction.Ignore);
            float free = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = ProbeHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.normal.y > 0.6f) continue; // ground it can run up
                float f = hit.distance <= 0f ? 0f : hit.distance - reach; // 0: already touching
                if (f < free) { free = f; normal = hit.distance <= 0f ? -dir : hit.normal; }
            }
            return free;
        }

        // How far along the line before its face would reach the safe ground round the shaft.
        private float SafeAhead(Vector3 here, Vector3 dir)
        {
            if (!CreatureSenses.ShaftCentre(out Vector3 centre)) return float.PositiveInfinity;
            Vector3 face = here + dir * frontReach - centre; face.y = 0f;
            float r = Settings.SafeZoneMeters;
            // |face + dir·s| = r, the first s ≥ 0.
            float b = Vector3.Dot(face, dir), c = face.sqrMagnitude - r * r;
            float disc = b * b - c;
            if (disc < 0f) return float.PositiveInfinity;
            float s = -b - Mathf.Sqrt(disc);
            if (s < 0f) return c < 0f ? 0f : float.PositiveInfinity;
            return s;
        }

        protected override string ServerBrainStatus() => $" phase={phase} rushes={ServerRushes} hits={ServerRushHits} speed={rushSpeed:0.0}";
    }
}
