using FishNet.Object;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The server's side of a grab (Dan, 24 September 2026). A killer whose prefab
    // carries a CreatureGrab does not kill on the touch: Strike catches the diver
    // (only when WorldSceneFlow.ServerCanKill would take them), writes the hold on
    // the diver (HQPlayerController.ServerGrab) and holds: its brain does not run, it
    // does not move, it turns to its catch and keeps the Grabbing pose. At
    // HoldSeconds the ordinary death (ServerKill) and the hold is let go of; the
    // creature stays still in its pose for ReleaseSeconds, then its brain resumes
    // after the strike cooldown. One hold at a time, one kill per hold; a diver
    // already held cannot be struck by anyone. A lost catch (a beam or the tank
    // killed them, they left) or a refused kill lets go without a second try.
    public abstract partial class Creature
    {
        // The checks refuse the hold's kill to see a diver let go of (reset on every Play Mode entry).
        public static bool RefuseGrabKillForChecks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGrabForPlayMode() => RefuseGrabKillForChecks = false;

        private CreatureGrab grabCore;
        private bool grabCoreLooked;
        private HQPlayerController grabVictim;
        private float grabStartedAt = -1f;
        private bool grabLetGo;          // the hold is over (a kill, a refusal, a lost catch); the release pose runs out
        private Vector3 grabFacing;

        // This kind holds its catch (its prefab carries an enabled CreatureGrab).
        public CreatureGrab GrabCore
        {
            get
            {
                if (!grabCoreLooked) { grabCoreLooked = true; grabCore = GetComponent<CreatureGrab>(); }
                return grabCore;
            }
        }
        public bool Grabs => GrabCore != null && GrabCore.enabled;
        // The server's view, for the brains and the checks.
        public bool ServerGrabbing => grabStartedAt >= 0f;
        public float ServerGrabSeconds => ServerGrabbing ? Now - grabStartedAt : 0f;
        public HQPlayerController ServerGrabVictim => ServerGrabbing ? grabVictim : null;
        public int ServerGrabsStarted { get; private set; }
        public int ServerGrabKills { get; private set; }
        public int ServerGrabsLetGo { get; private set; }
        public string ServerGrabOutcome { get; private set; } = string.Empty;
        public bool ServerHolding(HQPlayerController diver) => ServerGrabbing && !grabLetGo && diver != null && diver == grabVictim;

        // The catch: true when the hold began.
        [Server]
        private bool ServerBeginGrab(HQPlayerController diver, string name)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            string why = "no flow";
            if (flow == null || !flow.ServerCanKill(diver.Owner, out why))
            {
                Debug.Log($"[Monsters] {name} reached {WorldSceneFlow.DisplayName(diver.Owner)} but would not be let kill ({why}): no grab");
                return false;
            }
            grabVictim = diver;
            grabStartedAt = Now;
            grabLetGo = false;
            grabFacing = diver.transform.position;
            diver.ServerGrab(NetworkObject, diver.transform.position);
            SetTarget(diver.OwnerId);
            SetPose(CreaturePose.Grabbing);
            ServerGrabsStarted++;
            ServerGrabOutcome = "holding";
            Debug.Log($"[Monsters] {name} caught {WorldSceneFlow.DisplayName(diver.Owner)} at {CreatureSenses.Flat(transform.position, diver.transform.position):0.00} m: the hold, {GrabCore.HoldSeconds:0.0} s");
            return true;
        }

        // Every server frame while holding, instead of the brain.
        [Server]
        private void ServerTickGrab()
        {
            CreatureGrab core = GrabCore;
            float t = Now - grabStartedAt;
            SetPose(CreaturePose.Grabbing);
            if (!grabLetGo)
            {
                HQPlayerController victim = grabVictim;
                string name = MonsterCatalog.DisplayName(Kind);
                if (victim == null || !victim.IsSpawned || victim.IsDead || !victim.IsGrabbed)
                {
                    LetGo(victim, victim != null && victim.IsDead ? "died in the hold" : "lost");
                }
                else
                {
                    if (t < core.GripSeconds + 0.3f) FaceToward(grabFacing, core.TurnDegPerSec);
                    if (t >= core.HoldSeconds)
                    {
                        // The kill judges the catch, not the capsule: a dash pressed before the
                        // hold reached the diver, or a stale position, cannot save them (Dan,
                        // 24 September 2026). The body lies on the hold's point.
                        WorldSceneFlow flow = WorldSceneFlow.Instance;
                        string why = "refused for the checks";
                        Vector3 heldAt = core.HoldPose(victim.Grab.CaughtAt, t).Feet;
                        bool killed = !RefuseGrabKillForChecks && flow != null && flow.ServerKillHeld(victim.Owner, heldAt, out why, "taken by " + name);
                        if (killed)
                        {
                            ServerGrabKills++;
                            LetGo(victim, "killed");
                        }
                        else
                        {
                            Debug.Log($"[Monsters] {name} held {WorldSceneFlow.DisplayName(victim.Owner)} but the kill was refused ({why}): let go");
                            LetGo(victim, "kill refused: " + why);
                        }
                    }
                }
            }
            if (t >= core.HoldSeconds + core.ReleaseSeconds || (grabLetGo && t < core.HoldSeconds && t >= core.GripSeconds + core.ReleaseSeconds)) EndGrab();
        }

        private void LetGo(HQPlayerController victim, string outcome)
        {
            grabLetGo = true;
            ServerGrabOutcome = outcome;
            if (outcome != "killed") ServerGrabsLetGo++;
            if (victim != null && victim.IsSpawned) victim.ServerReleaseGrab();
        }

        private void EndGrab()
        {
            if (!grabLetGo) LetGo(grabVictim, "ended");
            grabStartedAt = -1f;
            grabVictim = null;
            strikeReadyAt = Now + Settings.StrikeCooldownSeconds;
        }

        // A despawn (the site going) mid-hold lets the diver go.
        private void ServerGrabStopped()
        {
            if (ServerGrabbing && !grabLetGo) LetGo(grabVictim, "the holder went");
            grabStartedAt = -1f;
            grabVictim = null;
        }
    }
}
