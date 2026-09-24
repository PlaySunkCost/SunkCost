using SunkCost.Noise;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The Listener (docs/DESIGN.md §6): sound. Blind; hears exactly what the
    // noise bus says — a walking step 6 m, a sprinting step 15 m, the car's 60 m
    // scream — when the sound's radius reaches it. It turns and walks toward the
    // last sound and shoots a dark beam at where the sound was, which then turns
    // after whoever made it. Crouch-walking is silent. It loses interest
    // ListenerForgetSeconds after silence, drifts back to where it appeared, and
    // never camps the door (the safe ground stops it).
    //
    // Its body takes part in every shot (24 September 2026): it plants itself and
    // charges in the Aiming pose (the ears flare, the head aims), fires in the
    // Shooting pose with its body turned along the beam as the beam turns, then
    // recovers for a beat before it listens again. It walks Drawn toward a thing's
    // sound and home, Hunting toward a diver's, and stands (Idle) when it has
    // arrived or is only turning to the car.
    [RequireComponent(typeof(CreatureBolts))]
    public sealed class Listener : Creature
    {
        [Tooltip("It drifts home after silence at this fraction of a diver's walking speed.")]
        [SerializeField] private float homeSpeedFactor = 0.35f;
        [Tooltip("After a beam it stands this long in the Recovering pose before it listens and walks again.")]
        [SerializeField] private float recoverSeconds = 0.9f;

        private CreatureBolts bolts;
        private Vector3 heardAt;
        private float heardTime = float.NegativeInfinity;
        private NoiseKind heardKind;
        private float nextShotAt;
        private bool unshot; // a sound not yet answered
        private bool heardDiver; // the last sound was a diver's (a step, a dash), not a thing's
        private bool wasAiming;
        private float recoverUntil = float.NegativeInfinity;

        public int ServerHeard { get; private set; }
        public NoiseKind ServerLastKind => heardKind;
        public Vector3 ServerHeardAt => heardAt;
        public float RecoverSeconds => recoverSeconds;

        // A test seam (the checks): forget every sound heard so far, so no old sound draws a
        // shot the check did not ask for. Server only; nothing in play calls it.
        public void ServerForgetForChecks()
        {
            if (!IsServerStarted) return;
            unshot = false;
            heardTime = float.NegativeInfinity;
        }
        // A test seam (the checks): deaf while set, for rows that aim the beam themselves (a
        // teleported diver's landing is a sound it would shoot at first).
        public bool DeafForChecks { get; set; }

        protected override void Awake()
        {
            base.Awake();
            bolts = GetComponent<CreatureBolts>();
        }

        public override void OnNoise(in NoiseEvent noise)
        {
            if (!IsServerStarted || !ServerAwake || DeafForChecks) return;
            if (Vector3.Distance(noise.Position, transform.position) > noise.Radius) return;
            heardAt = noise.Position; heardTime = Now; heardKind = noise.Kind; heardSource = noise.SourceId;
            heardDiver = DiverBySource(heardSource) != null;
            unshot = true;
            ServerHeard++;
        }

        private int heardSource; // the noise's SourceId: a diver's ObjectId, an item's, or 0 for the world

        private static HQPlayerController DiverBySource(int objectId)
        {
            if (objectId <= 0) return null;
            foreach (HQPlayerController diver in CreatureSenses.Divers()) if (diver.ObjectId == objectId) return diver;
            return null;
        }

        // Turn the body along a direction, flat (the beam's, while it charges and burns).
        private void FaceAlong(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) FaceToward(transform.position + direction.normalized * 10f);
        }

        protected override void ServerThink(float dt)
        {
            // The shot: planted, its body along the beam as the beam turns; its head on the diver it follows.
            bool aiming = bolts.ServerAiming;
            if (wasAiming && !aiming) recoverUntil = Now + recoverSeconds;
            wasAiming = aiming;
            if (aiming)
            {
                SetTarget(bolts.ServerTargetOwnerId);
                FaceAlong(bolts.ServerAimDirection);
                SetPose(bolts.ServerCharging ? CreaturePose.Aiming : CreaturePose.Shooting);
                return;
            }
            SetTarget(-1); // blind: between shots it knows a sound, not a diver
            if (Now < recoverUntil) { SetPose(CreaturePose.Recovering); return; }

            if (Now - heardTime > Settings.ListenerForgetSeconds)
            {
                // Silence: it drifts back to where it appeared, so the car's call never leaves it at the door.
                bool home = AtHome || MoveToward(Home, WalkSpeed * homeSpeedFactor, dt, 1.5f);
                if (!home) FaceToward(Home);
                SetPose(home ? CreaturePose.Idle : CreaturePose.Drawn);
                return;
            }
            FaceToward(heardAt);
            // The car's scream turns it and draws a beam, but does not walk it up to the shaft's doorway.
            bool walk = heardKind != NoiseKind.Elevator || !CreatureSenses.ShaftCentre(out Vector3 shaft) || CreatureSenses.Flat(transform.position, shaft) > Settings.SafeZoneMeters * 3f;
            if (unshot && Now >= nextShotAt && CreatureSenses.Flat(heardAt, transform.position) > 1.5f)
            {
                // The charge begins at the sound; the beam then follows the diver who made
                // it (the noise's source), or the nearest one in range when the sound was a
                // thing (a coin's landing) — blind, it still turns after what breathes.
                HQPlayerController who = DiverBySource(heardSource) ?? CreatureSenses.Nearest(transform.position, Settings.BeamRangeMeters);
                bolts.ServerAim(bolts.Origin, heardAt + Vector3.up * 1.1f, dark: true, Settings.ListenerDamage, MonsterCatalog.DisplayName(Kind), who != null ? who.OwnerId : -1);
                nextShotAt = Now + Settings.BeamChargeSeconds + Settings.BeamSeconds + Settings.ListenerShotCooldownSeconds;
                unshot = false;
                if (bolts.ServerAiming)
                {
                    wasAiming = true;
                    SetTarget(bolts.ServerTargetOwnerId);
                    SetPose(CreaturePose.Aiming); // it plants itself to fire
                    return;
                }
            }
            // Walking to the sound: Hunting after a diver's, Drawn toward a thing's; standing when there.
            bool there = !walk || MoveToward(heardAt, WalkSpeed * Settings.ListenerApproachSpeedFactor, dt, Settings.ShooterStandoffMeters);
            SetPose(there ? CreaturePose.Idle : heardDiver ? CreaturePose.Hunting : CreaturePose.Drawn);
        }

        protected override string ServerBrainStatus() => $" heard={ServerHeard} last={heardKind} ago={(float.IsInfinity(heardTime) ? -1f : Now - heardTime):0.0}s fired={bolts.ServerFired} hits={bolts.ServerHits} phase={bolts.ServerPhase} held={bolts.ServerHeld} broken={bolts.ServerLockBroken}";
    }
}
