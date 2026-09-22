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
    public sealed class CreatureBolts : NetworkBehaviour
    {
        private readonly SyncVar<BeamCue> cue = new(new BeamCue { Serial = 0 });
        private readonly SyncVar<Vector3> beamAim = new(Vector3.zero); // the live aim point while charging and firing
        private readonly SyncVar<int> hitSerial = new(0);

        private Creature creature;
        private BeamPhase phase = BeamPhase.None;
        private float phaseEndsAt, damage;
        private string cause;
        private int targetOwnerId = -1;
        private Vector3 aimDir;
        private bool hitThisBeam;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public BeamCue Cue => cue.Value;
        public Vector3 BeamAim => beamAim.Value;
        public int HitSerial => hitSerial.Value;
        public BeamPhase ServerPhase => phase;
        public bool ServerAiming => phase == BeamPhase.Charging || phase == BeamPhase.Firing;
        public bool ServerCharging => phase == BeamPhase.Charging;
        public bool ServerFiring => phase == BeamPhase.Firing;
        public int ServerFired { get; private set; }
        public int ServerHits { get; private set; }
        public event Action<BeamCue> Aimed;  // every peer: the charge began (not for a joiner's old value)
        public event Action<BeamCue> Fired;  // every peer: the beam is on
        public event Action<BeamCue> Ended;  // every peer: the beam is off
        public event Action Hit;

        private int clientStartFrame = -1;

        private void Awake()
        {
            creature = GetComponent<Creature>();
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
            phase = BeamPhase.Charging;
            phaseEndsAt = Time.time + settings.BeamChargeSeconds;
            beamAim.Value = aim;
            cue.Value = new BeamCue { Serial = cue.Value.Serial + 1, From = from, To = aim, StartTick = tick, Dark = dark, Phase = BeamPhase.Charging };
        }

        private Vector3 Origin => creature != null ? creature.EyePoint : transform.position;

        private void Update()
        {
            if (!IsServerStarted || !ServerAiming) return;
            MonsterSettings settings = MonsterSettings.Get();
            float dt = Time.deltaTime;
            Vector3 from = Origin;
            // Turn after the target, charging and firing alike, at the sweep rate.
            HQPlayerController target = targetOwnerId >= 0 ? CreatureSenses.DiverOf(targetOwnerId) : null;
            if (target != null)
            {
                Vector3 want = (CreatureSenses.Chest(target) - from).normalized;
                aimDir = Vector3.RotateTowards(aimDir, want, settings.BeamSweepDegPerSec * Mathf.Deg2Rad * dt, 0f);
            }
            float range = BeamEnd(from, aimDir, settings.BeamRangeMeters);
            Vector3 end = from + aimDir * range;
            beamAim.Value = end;
            if (phase == BeamPhase.Charging)
            {
                if (Time.time < phaseEndsAt) return;
                phase = BeamPhase.Firing;
                phaseEndsAt = Time.time + settings.BeamSeconds;
                ServerFired++;
                BeamCue c = cue.Value;
                cue.Value = new BeamCue { Serial = c.Serial + 1, From = from, To = end, StartTick = c.StartTick, Dark = c.Dark, Phase = BeamPhase.Firing };
                return;
            }
            // Firing: the first diver on the line takes it, once per beam.
            if (!hitThisBeam)
            {
                HQPlayerController victim = null;
                float best = float.PositiveInfinity;
                foreach (HQPlayerController diver in CreatureSenses.Divers())
                {
                    if (CreatureSenses.Safe(diver, settings)) continue;
                    Vector3 chest = CreatureSenses.Chest(diver);
                    Vector3 on = ClosestOnSegment(chest, from, end);
                    if (Vector3.Distance(chest, on) > settings.BeamHitRadius) continue;
                    if (!CreatureSenses.ClearLine(on, chest)) continue; // a wall between the beam and the diver
                    float along = Vector3.Distance(from, on);
                    if (along < best) { best = along; victim = diver; }
                }
                if (victim != null)
                {
                    PlayerVitals vitals = victim.Vitals;
                    if (vitals != null && vitals.ServerDamage(damage, true, cause))
                    {
                        hitThisBeam = true;
                        ServerHits++;
                        hitSerial.Value = hitSerial.Value + 1;
                    }
                }
            }
            if (Time.time < phaseEndsAt) return;
            phase = BeamPhase.Done;
            BeamCue f = cue.Value;
            cue.Value = new BeamCue { Serial = f.Serial + 1, From = from, To = end, StartTick = f.StartTick, Dark = f.Dark, Phase = BeamPhase.Done };
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
    }
}
