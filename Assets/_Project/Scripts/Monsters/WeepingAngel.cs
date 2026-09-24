using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Weeping Angel (docs/DESIGN.md §6): being watched. Frozen while inside
    // any living diver's view within AngelWatchMeters with a clear line of sight
    // (the TV and spectators do not count: they are not divers below). Unwatched
    // it moves at sprint speed straight at the nearest diver it has noticed.
    // Reaching you is death: it embraces you first (Dan, 24 September 2026) — the
    // CreatureGrab on its prefab, held by Creature.Grab.cs, which runs instead of
    // this brain while it holds, so a look can neither stop an embrace nor start one.
    //
    // Frozen, it is a statue in one of the poses of its Frozen strip (Dan: striking,
    // varied, unnerving). The server picks the statue as it freezes, from what it
    // was doing and how close the one who caught it looking is, and replicates the
    // choice; WeepingAngelStatue holds that pose on every peer.
    public sealed class WeepingAngel : Creature
    {
        // The Frozen strip's poses, in the clip's order (tools/blender/clips/WeepingAngel.py FROZEN_POSES).
        public enum Statue : byte { Lunge = 0, Reach = 1, Weep = 2, Creep = 3, Turn = 4 }
        public const int StatueCount = 5;

        [Header("Statues (the pose it freezes in)")]
        [Tooltip("Caught looking closer than this, it freezes reaching for the watcher's face.")]
        [SerializeField, Min(0f)] private float reachStatueMeters = 4.5f;
        [Tooltip("Caught looking farther than this, it freezes in the lunge or weeping, never mid-crawl.")]
        [SerializeField, Min(0f)] private float farStatueMeters = 16f;

        private readonly SyncVar<byte> statue = new((byte)Statue.Weep);

        public bool ServerWatched { get; private set; }
        public int ServerWatcherId { get; private set; } = -1;
        public float ServerWatcherMeters { get; private set; } = -1f;
        public int ServerFreezes { get; private set; }
        public Statue FrozenStatue => (Statue)statue.Value;
        private float lastWatchedAt = float.NegativeInfinity;
        private bool frozen;
        private bool huntedSinceFreeze;
        // A remote diver's eyes reach the server a little late: the freeze outlasts the last watched frame by this.
        private const float GraceSeconds = 0.25f;

        protected override void ServerThink(float dt)
        {
            ServerWatched = CreatureSenses.Watched(BodyPoints(), Settings, out HQPlayerController watcher);
            ServerWatcherId = watcher != null ? watcher.OwnerId : -1;
            ServerWatcherMeters = watcher != null ? CreatureSenses.Flat(transform.position, watcher.transform.position) : -1f;
            if (ServerWatched) lastWatchedAt = Now;
            if (ServerWatched || Now - lastWatchedAt < GraceSeconds)
            {
                if (!frozen) Freeze(watcher);
                return; // not a turn, not a step, never a touch
            }
            frozen = false;
            HQPlayerController prey = Prey();
            if (prey == null) { SetPose(CreaturePose.Idle); SetTarget(-1); return; }
            huntedSinceFreeze = true;
            SetTarget(prey.OwnerId);
            SetPose(CreaturePose.Hunting);
            FaceToward(prey.transform.position);
            if (WithinReach(prey) && StrikeReady && Strike(prey, 0f)) return;
            MoveToward(prey.transform.position, SprintSpeed * Settings.AngelSpeedFactor, dt, Settings.TouchStandoffMeters);
        }

        // The nearest diver it may reach: one waiting on the safe ground round the shaft
        // is passed over for anyone outside it (the Angel used to stand at the car's
        // edge for a diver inside while another worked in the open). With everyone safe
        // it still goes for the nearest and waits at the edge.
        private HQPlayerController Prey()
        {
            HQPlayerController best = null, bestSafe = null;
            float bestD = Settings.AngelWakeMeters, bestSafeD = Settings.AngelWakeMeters;
            foreach (HQPlayerController p in CreatureSenses.Divers())
            {
                if (p.IsGrabbed) continue; // held by something else
                float d = CreatureSenses.Flat(transform.position, p.transform.position);
                if (CreatureSenses.Safe(p, Settings)) { if (d < bestSafeD) { bestSafeD = d; bestSafe = p; } }
                else if (d < bestD) { bestD = d; best = p; }
            }
            return best != null ? best : bestSafe;
        }

        // The statue is chosen once, as it freezes, before the pose says Frozen (both
        // go in the same tick): close to the one who looked it reaches for them; out of
        // a hunt a lunge, a crawl or a turned look back; standing idle it weeps.
        private void Freeze(HQPlayerController watcher)
        {
            frozen = true;
            ServerFreezes++;
            Statue pick;
            float d = watcher != null ? CreatureSenses.Flat(transform.position, watcher.transform.position) : float.PositiveInfinity;
            if (!huntedSinceFreeze) pick = Statue.Weep;
            else if (d < reachStatueMeters) pick = Statue.Reach;
            else if (d > farStatueMeters) pick = Random.value < 0.65f ? Statue.Lunge : Statue.Weep;
            else
            {
                Statue[] hunt = { Statue.Lunge, Statue.Lunge, Statue.Creep, Statue.Turn };
                pick = hunt[Random.Range(0, hunt.Length)];
                if (pick == FrozenStatue) pick = hunt[(System.Array.IndexOf(hunt, pick) + 2) % hunt.Length];
            }
            huntedSinceFreeze = false;
            if (statue.Value != (byte)pick) statue.Value = (byte)pick;
            SetPose(CreaturePose.Frozen);
        }

        protected override string ServerBrainStatus() =>
            (ServerWatched ? " watched by " + ServerWatcherId + $" at {ServerWatcherMeters:0.0} m" : " unwatched")
            + " statue=" + FrozenStatue + " freezes=" + ServerFreezes
            + (ServerGrabbing ? $" holding={ServerGrabVictim?.OwnerId} t={ServerGrabSeconds:0.00}" : string.Empty)
            + (ServerGrabsStarted > 0 ? $" grabs={ServerGrabsStarted} kills={ServerGrabKills} letGo={ServerGrabsLetGo} last='{ServerGrabOutcome}'" : string.Empty);
    }
}
