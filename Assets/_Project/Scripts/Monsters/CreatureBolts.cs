using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    public enum BeamPhase : byte { None = 0, Charging = 1, Firing = 2, Done = 3 }

    // A beam cue, replicated on every phase change as a SyncVar with a serial (the
    // repo's one-shot idiom). The live aim point rides beside it in its own SyncVar
    // while the beam sweeps. Every peer draws the same beam; the server alone
    // judges the hit.
    public struct BeamCue
    {
        public int Serial;
        public Vector3 From;   // where it started charging (the presentation draws from the live origin)
        public Vector3 To;     // the aim at that moment
        public uint StartTick;
        public bool Dark;
        public BeamPhase Phase;
    }

    // The Lure's beam of light and the Listener's dark one (docs/DESIGN.md §6;
    // Dan, 22 September 2026: "a second of loading the laser, then shoot it for 3
    // continuous seconds… 1 hit, not more" and "95 % hit the player"). It charges
    // for BeamChargeSeconds — the tell, a glow at the mouth and a faint line — then
    // burns for BeamSeconds, turning after its target at BeamSweepDegPerSec, fast
    // enough that only a wall between you or a dash across it at the right moment
    // saves you. The first diver on the line takes the hit, once; the beam goes on
    // burning without hurting anyone else. Server only for the judgement; the
    // cues and the aim point replicate for the look.
    //
    // What is drawn is what hurts (24 September 2026): the line starts at the
    // model's BeamOrigin (the mouth, the lamp — CreatureRig keeps it animating on the
    // server), runs to the first wall, and is `halfWidth` thick on every peer; it
    // hurts a diver whose body (the capsule) it touches, with nothing solid between.
    // A dash breaks its turn (lead, 24 September 2026, to Dan as a design note):
    // while it burns, a target who is dashing makes it stop turning after them and
    // hold its heading for the rest of the burn — the dash on the flash.
    public sealed class CreatureBolts : NetworkBehaviour
    {
        [Tooltip("The beam's radius in metres (Dan, 24 September 2026: much bigger, about 70 cm across): drawn this thick, and it hurts a diver whose body it touches.")]
        [SerializeField] private float halfWidth = 0.35f;

        private readonly SyncVar<BeamCue> cue = new(new BeamCue { Serial = 0 });
        // The live aim point while charging and firing, at up to 60 a second so a guest's sweep is a turn, not steps.
        private readonly SyncVar<Vector3> beamAim = new(Vector3.zero, new SyncTypeSettings(1f / 60f));
        private readonly SyncVar<int> hitSerial = new(0);

        private Creature creature;
        private CreatureRig rig;
        private BeamPhase phase = BeamPhase.None;
        private float phaseEndsAt, damage;
        private string cause;
        private int targetOwnerId = -1;
        private Vector3 aimDir;
        private bool hitThisBeam, held;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public float HalfWidth => halfWidth;
        public BeamCue Cue => cue.Value;
        public Vector3 BeamAim => beamAim.Value;
        public int HitSerial => hitSerial.Value;
        public BeamPhase ServerPhase => phase;
        public bool ServerAiming => phase == BeamPhase.Charging || phase == BeamPhase.Firing;
        public bool ServerCharging => phase == BeamPhase.Charging;
        public bool ServerFiring => phase == BeamPhase.Firing;
        public int ServerFired { get; private set; }
        public int ServerHits { get; private set; }
        // The server's view for the brains and the checks.
        public Vector3 ServerAimDirection => aimDir;       // where the beam points now (the brain faces it)
        public int ServerTargetOwnerId => targetOwnerId;
        public bool ServerHeld => held;                    // a dash broke its turn this burn
        public Vector3 ServerFrom { get; private set; }    // the line it judged last frame
        public Vector3 ServerTo { get; private set; }
        public float ServerChargedAt { get; private set; } = float.NegativeInfinity;
        public float ServerFiredAt { get; private set; } = float.NegativeInfinity;
        public float ServerEndedAt { get; private set; } = float.NegativeInfinity;
        public float ServerHitAt { get; private set; } = float.NegativeInfinity;
        public float ServerHitGap { get; private set; } = float.NaN;   // the beam's axis to the victim's body axis at the hit
        public int ServerHitOwnerId { get; private set; } = -1;
        public event Action<BeamCue> Aimed;  // every peer: the charge began (not for a joiner's old value)
        public event Action<BeamCue> Fired;  // every peer: the beam is on
        public event Action<BeamCue> Ended;  // every peer: the beam is off
        public event Action Hit;

        private int clientStartFrame = -1;

        private void Awake()
        {
            creature = GetComponent<Creature>();
            rig = GetComponent<CreatureRig>();
            cue.OnChange += OnCueChanged;
            hitSerial.OnChange += (_, next, asServer) => { if (IsServerStarted && !asServer) return; if (next != 0 && Time.frameCount != clientStartFrame) Hit?.Invoke(); };
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            clientStartFrame = Time.frameCount;
        }

        private void OnCueChanged(BeamCue previous, BeamCue next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer (the host sees both passes)
            if (next.Serial == 0 || Time.frameCount == clientStartFrame) return; // a joiner's old cue is old news
            switch (next.Phase)
            {
                case BeamPhase.Charging: Aimed?.Invoke(next); break;
                case BeamPhase.Firing: Fired?.Invoke(next); break;
                case BeamPhase.Done: Ended?.Invoke(next); break;
            }
        }

        // The charge begins: the tell. The beam follows `targetOwnerId` (a diver) from
        // here on; with no diver (a coin's landing) it stays on the point.
        [Server]
        public void ServerAim(Vector3 from, Vector3 aim, bool dark, float damage, string cause, int targetOwnerId = -1)
        {
            if (ServerAiming || (aim - from).sqrMagnitude < 0.01f) return;
            MonsterSettings settings = MonsterSettings.Get();
            uint tick = NetworkManager != null && NetworkManager.TimeManager != null ? NetworkManager.TimeManager.Tick : 0u;
            aimDir = (aim - from).normalized;
            this.targetOwnerId = targetOwnerId;
            this.damage = damage;
            this.cause = cause;
            hitThisBeam = false;
            held = false;
            phase = BeamPhase.Charging;
            phaseEndsAt = Time.time + settings.BeamChargeSeconds;
            ServerChargedAt = Time.time;
            beamAim.Value = aim;
            cue.Value = new BeamCue { Serial = cue.Value.Serial + 1, From = from, To = aim, StartTick = tick, Dark = dark, Phase = BeamPhase.Charging };
        }

        // Where the beam leaves, on every peer: the model's BeamOrigin (CreatureRig
        // falls back to the eye point when the anchor is missing or implausible).
        public Vector3 Origin => creature == null ? transform.position : rig != null ? rig.BeamOriginPoint(creature.EyePoint) : creature.EyePoint;

        // Where the beam aims on a diver: the middle of the chest, lower when crouched.
        public static Vector3 AimPoint(HQPlayerController diver)
        {
            CharacterController body = diver.GetComponent<CharacterController>();
            if (body == null) return CreatureSenses.Chest(diver);
            return diver.transform.position + Vector3.up * Mathf.Min(1.1f, body.center.y + body.height * 0.5f - body.radius - 0.1f);
        }

        private void Update()
        {
            if (!IsServerStarted || !ServerAiming) return;
            MonsterSettings settings = MonsterSettings.Get();
            float dt = Time.deltaTime;
            Vector3 from = Origin;
            // Turn after the target, charging and firing alike, at the sweep rate —
            // until a dash breaks the turn while it burns (the dash on the flash).
            HQPlayerController target = targetOwnerId >= 0 ? CreatureSenses.DiverOf(targetOwnerId) : null;
            if (phase == BeamPhase.Firing && !held && !hitThisBeam && target != null && target.ServerDashing) held = true;
            if (target != null && !held)
            {
                Vector3 want = (AimPoint(target) - from).normalized;
                aimDir = Vector3.RotateTowards(aimDir, want, settings.BeamSweepDegPerSec * Mathf.Deg2Rad * dt, 0f);
            }
            float range = BeamEnd(from, aimDir, settings.BeamRangeMeters);
            Vector3 end = from + aimDir * range;
            beamAim.Value = end;
            ServerFrom = from; ServerTo = end;
            if (phase == BeamPhase.Charging)
            {
                if (Time.time < phaseEndsAt) return;
                phase = BeamPhase.Firing;
                phaseEndsAt = Time.time + settings.BeamSeconds;
                ServerFired++;
                ServerFiredAt = Time.time;
                BeamCue c = cue.Value;
                cue.Value = new BeamCue { Serial = c.Serial + 1, From = from, To = end, StartTick = c.StartTick, Dark = c.Dark, Phase = BeamPhase.Firing };
                return;
            }
            // Firing: the first diver the line touches takes it, once per beam.
            if (!hitThisBeam)
            {
                HQPlayerController victim = null;
                float best = float.PositiveInfinity, bestGap = float.NaN;
                foreach (HQPlayerController diver in CreatureSenses.Divers())
                {
                    if (CreatureSenses.Safe(diver, settings)) continue;
                    if (!Touches(diver, from, end, out Vector3 onBeam, out Vector3 onBody, out float gap)) continue;
                    if (!CreatureSenses.ClearLine(onBeam, onBody)) continue; // a wall between the beam and the diver
                    float along = Vector3.Distance(from, onBeam);
                    if (along < best) { best = along; victim = diver; bestGap = gap; }
                }
                if (victim != null)
                {
                    PlayerVitals vitals = victim.Vitals;
                    if (vitals != null && vitals.ServerDamage(damage, true, cause))
                    {
                        hitThisBeam = true;
                        ServerHits++;
                        ServerHitAt = Time.time;
                        ServerHitGap = bestGap;
                        ServerHitOwnerId = victim.OwnerId;
                        hitSerial.Value = hitSerial.Value + 1;
                    }
                }
            }
            if (Time.time < phaseEndsAt) return;
            phase = BeamPhase.Done;
            ServerEndedAt = Time.time;
            BeamCue f = cue.Value;
            cue.Value = new BeamCue { Serial = f.Serial + 1, From = from, To = end, StartTick = f.StartTick, Dark = f.Dark, Phase = BeamPhase.Done };
        }

        // The drawn beam touches the diver's body: the beam's axis comes within its
        // radius plus the capsule's of the capsule's own axis (crouched or standing).
        // A diver without a capsule is judged at the chest by the settings' radius.
        public bool Touches(HQPlayerController diver, Vector3 from, Vector3 end, out Vector3 onBeam, out Vector3 onBody, out float gap)
        {
            CharacterController body = diver.GetComponent<CharacterController>();
            if (body == null)
            {
                onBody = CreatureSenses.Chest(diver);
                onBeam = ClosestOnSegment(onBody, from, end);
                gap = Vector3.Distance(onBeam, onBody);
                return gap <= MonsterSettings.Get().BeamHitRadius;
            }
            Vector3 centre = diver.transform.position + body.center;
            float half = Mathf.Max(0f, body.height * 0.5f - body.radius);
            SegmentClosest(from, end, centre - Vector3.up * half, centre + Vector3.up * half, out onBeam, out Vector3 axis);
            gap = Vector3.Distance(onBeam, axis);
            // The body's surface point nearest the beam, for the line-of-sight test.
            onBody = gap > 0.0001f ? axis + (onBeam - axis) / gap * Mathf.Min(body.radius, gap) : axis;
            return gap <= body.radius + halfWidth;
        }

        // How far a beam runs before the world stops it.
        public static float BeamEnd(Vector3 from, Vector3 dir, float range)
        {
            int count = Physics.RaycastNonAlloc(from, dir, Hits, range, CreatureSenses.SightMask, QueryTriggerInteraction.Ignore);
            float nearest = range;
            for (int i = 0; i < count; i++) if (Hits[i].distance < nearest) nearest = Hits[i].distance;
            return nearest;
        }

        private static Vector3 ClosestOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, ab) / len);
            return a + ab * t;
        }

        // The closest points of two segments, p1–q1 and p2–q2 (Ericson, Real-Time Collision Detection §5.1.9).
        private static void SegmentClosest(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-8f && e <= 1e-8f) { c1 = p1; c2 = p2; return; }
            if (a <= 1e-8f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-8f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }
    }
}
