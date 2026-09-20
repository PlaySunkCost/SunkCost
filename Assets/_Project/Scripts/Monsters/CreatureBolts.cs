using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // A beam cue, replicated once per change as a SyncVar with a serial (the
    // repo's one-shot idiom): the aim (a faint line for BeamAimSeconds — the
    // whole dodge window) and then the shot along that same line. Every peer
    // draws the same line; the server alone judges the hit.
    public struct BeamCue
    {
        public int Serial;
        public Vector3 From;
        public Vector3 To;      // the aim point; the beam runs on through it to BeamRangeMeters
        public uint StartTick;
        public bool Dark;
        public bool Fired;
    }

    // The Lure's beam of light and the Listener's dark one (docs/DESIGN.md §6,
    // Dan 20 September 2026: "a laser, not a shot" — the bolts were too easy to
    // dodge). The line is fixed at the aim; BeamAimSeconds later it fires and
    // hits at once: the first diver within BeamHitRadius of the line before the
    // first wall, not on safe ground, with a clear line from the beam to their
    // chest. Off the line during the aim and it passes where you were.
    public sealed class CreatureBolts : NetworkBehaviour
    {
        private readonly SyncVar<BeamCue> cue = new(new BeamCue { Serial = 0 });
        private readonly SyncVar<int> hitSerial = new(0);

        private bool aiming;
        private float fireAt, damage;
        private string cause;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public BeamCue Cue => cue.Value;
        public int HitSerial => hitSerial.Value;
        public bool ServerAiming => aiming;
        public int ServerFired { get; private set; }
        public int ServerHits { get; private set; }
        public event Action<BeamCue> Aimed; // every peer, from the SyncVar (not for a joiner's old value)
        public event Action<BeamCue> Fired;
        public event Action Hit;

        private int clientStartFrame = -1;

        private void Awake()
        {
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
            if (next.Fired) Fired?.Invoke(next); else Aimed?.Invoke(next);
        }

        // The aim: the line is fixed now; the shot comes BeamAimSeconds later.
        [Server]
        public void ServerAim(Vector3 from, Vector3 aim, bool dark, float damage, string cause)
        {
            if (aiming || (aim - from).sqrMagnitude < 0.01f) return;
            uint tick = NetworkManager != null && NetworkManager.TimeManager != null ? NetworkManager.TimeManager.Tick : 0u;
            cue.Value = new BeamCue { Serial = cue.Value.Serial + 1, From = from, To = aim, StartTick = tick, Dark = dark, Fired = false };
            aiming = true;
            fireAt = Time.time + MonsterSettings.Get().BeamAimSeconds;
            this.damage = damage;
            this.cause = cause;
        }

        private void Update()
        {
            if (!IsServerStarted || !aiming || Time.time < fireAt) return;
            aiming = false;
            ServerFire();
        }

        [Server]
        private void ServerFire()
        {
            MonsterSettings settings = MonsterSettings.Get();
            BeamCue c = cue.Value;
            Vector3 dir = (c.To - c.From).normalized;
            float range = BeamEnd(c.From, dir, settings.BeamRangeMeters);
            Vector3 end = c.From + dir * range;
            ServerFired++;
            HQPlayerController victim = null;
            float best = float.PositiveInfinity;
            foreach (HQPlayerController diver in CreatureSenses.Divers())
            {
                if (CreatureSenses.Safe(diver, settings)) continue;
                Vector3 chest = CreatureSenses.Chest(diver);
                Vector3 on = ClosestOnSegment(chest, c.From, end);
                if (Vector3.Distance(chest, on) > settings.BeamHitRadius) continue;
                if (!CreatureSenses.ClearLine(on, chest)) continue; // a wall between the beam and the diver
                float along = Vector3.Distance(c.From, on);
                if (along < best) { best = along; victim = diver; }
            }
            if (victim != null)
            {
                PlayerVitals vitals = victim.Vitals;
                if (vitals != null && vitals.ServerDamage(damage, true, cause))
                {
                    ServerHits++;
                    hitSerial.Value = hitSerial.Value + 1;
                }
            }
            cue.Value = new BeamCue { Serial = c.Serial + 1, From = c.From, To = c.To, StartTick = c.StartTick, Dark = c.Dark, Fired = true };
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
