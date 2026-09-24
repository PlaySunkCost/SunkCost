using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace SunkCost.Player
{
    // A knock-back the server dealt (the one-shot idiom: a serial in a SyncVar).
    public struct KnockbackCue
    {
        public int Serial;
        public Vector3 Push; // flat, metres: the direction and the distance
        public uint Tick;    // the server's tick at the blow: every peer times the jolt from it
    }

    // Knocked back by a monster (Dan, 24 September 2026: the Charger's hit throws
    // you about 3 m along its path, with a jolt of the view). The server decides it
    // (ServerKnockback, from the Charger's brain) and writes knockbackCue; the owner,
    // who simulates its own movement (contract section 3), is told by TargetRpc and
    // moves itself: the push goes through the motor's external-motion sweep (walls
    // stop it, gravity goes on), with a small lift, easing out over
    // knockbackSeconds, and its own steering is held back meanwhile
    // (KnockbackInputScale). The NetworkTransform carries the shove to every other
    // screen. The jolt of the view is a decaying snap of pitch and roll that every
    // peer times from the cue's server tick (KnockbackJolt): the owner's camera
    // wears it, and EyePose adds it for a spectator and the deck TV watching.
    public sealed partial class HQPlayerController
    {
        [Header("Knocked back (mon-charger, 24 September 2026)")]
        [Tooltip("How long the shove takes, seconds; most of it lands in the first third.")]
        [SerializeField] private float knockbackSeconds = 0.4f;
        [Tooltip("The little lift a blow gives, metres per second upward.")]
        [SerializeField] private float knockbackLiftSpeed = 2.2f;
        [Tooltip("How hard the view snaps on a blow, degrees: the peak of the kick (about 35 ms in), before the spring swings it back.")]
        [SerializeField] private float knockbackJoltDegrees = 14f;
        [Tooltip("How long the jolt takes to settle completely, seconds.")]
        [SerializeField] private float knockbackJoltSeconds = 0.6f;
        [Tooltip("The swing back after the kick, cycles per second (one overshoot, then it settles).")]
        [SerializeField] private float knockbackJoltSpring = 3f;
        [Tooltip("The impact's short tremor on top of the kick, degrees.")]
        [SerializeField] private float knockbackJoltTremor = 1.4f;

        private readonly SyncVar<KnockbackCue> knockbackCue = new(new KnockbackCue { Serial = 0 });
        private float knockOwnerAt = float.NegativeInfinity;
        private Vector3 knockOwnerPush;     // the owner's own shove (the cue may land after the RPC)
        private Coroutine knockRoutine;

        public KnockbackCue LastKnockback => knockbackCue.Value;
        public int ServerKnockbacks { get; private set; }
        // Owner diagnostics for the checks: shoves felt and how far the last one moved the capsule.
        public int KnockbacksFelt { get; private set; }
        public float LastKnockbackMoved { get; private set; }
        public bool KnockedBack => Time.time - knockOwnerAt < knockbackSeconds;

        // The server's word: push this diver (flat metres along the direction).
        [Server]
        public void ServerKnockback(Vector3 push)
        {
            push.y = 0f;
            if (dead.Value || IsGrabbed || push.sqrMagnitude < 0.0001f) return; // a diver held by a monster stays in its hold
            uint tick = NetworkManager != null && NetworkManager.TimeManager != null ? NetworkManager.TimeManager.Tick : 0u;
            knockbackCue.Value = new KnockbackCue { Serial = knockbackCue.Value.Serial + 1, Push = push, Tick = tick };
            ServerKnockbacks++;
            TargetKnockback(Owner, push);
        }

        [TargetRpc]
        private void TargetKnockback(NetworkConnection target, Vector3 push)
        {
            if (!IsOwner || dead.Value) return;
            if (knockRoutine != null) StopCoroutine(knockRoutine);
            knockOwnerPush = push;
            knockRoutine = StartCoroutine(Knocked(push));
        }

        // The owner's shove: fed to the motor's external sweep a frame at a time.
        private IEnumerator Knocked(Vector3 push)
        {
            knockOwnerAt = Time.time;
            KnockbacksFelt++;
            float total = push.magnitude;
            Vector3 dir = push / total;
            if (knockbackLiftSpeed > 0f && !travelLocked) { verticalSpeed = Mathf.Max(verticalSpeed, knockbackLiftSpeed); grounded = false; }
            Vector3 start = transform.position;
            float given = 0f;
            float seconds = Mathf.Max(0.05f, knockbackSeconds);
            while (true)
            {
                float t = Time.time - knockOwnerAt;
                float x = Mathf.Clamp01(t / seconds);
                float want = total * (1f - (1f - x) * (1f - x) * (1f - x)); // ease out: a blow, then the slide
                if (!dead.Value && !travelLocked && !grabbedLocal && controller != null && controller.enabled) AddExternalMotion(dir * (want - given));
                given = want;
                JoltOwnCamera();
                if (x >= 1f && t >= knockbackJoltSeconds) break;
                yield return null;
            }
            Vector3 moved = transform.position - start; moved.y = 0f;
            LastKnockbackMoved = moved.magnitude;
            if (playerCamera != null && !grabbedLocal && !dead.Value) playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            knockRoutine = null;
        }

        // After Update's look (the coroutine runs after every Update): the owner's view wears the jolt.
        private void JoltOwnCamera()
        {
            if (playerCamera == null || grabbedLocal || dead.Value) return;
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f) * KnockbackJolt();
        }

        // 0 while the shove lands, back to 1 as it ends: the diver's own steering is
        // held back so the push is the blow's, not the keys'.
        public float KnockbackInputScale()
        {
            float t = Time.time - knockOwnerAt;
            float seconds = Mathf.Max(0.05f, knockbackSeconds);
            return t < seconds * 0.6f ? 0f : Mathf.Clamp01((t - seconds * 0.6f) / (seconds * 0.4f));
        }

        // The jolt of the view, the same on every peer: timed from the cue's server
        // tick (the owner from its own shove's start, so its camera and its body move
        // together). The shape of a hit: the head snaps within 35 ms the way the blow
        // throws it (a blow from the front kicks the view up, from behind down, from
        // the side rolls it), then a damped spring swings it back once past centre and
        // it settles exactly to nothing by knockbackJoltSeconds; a short tremor rides
        // on the first 0.15 s. Identity when none.
        public Quaternion KnockbackJolt()
        {
            bool own = IsOwner && Time.time - knockOwnerAt < 1f;
            KnockbackCue cue = knockbackCue.Value;
            if (cue.Serial == 0 && !own) return Quaternion.identity;
            float t = own ? Time.time - knockOwnerAt : KnockSince(cue.Tick);
            float seconds = Mathf.Max(0.1f, knockbackJoltSeconds);
            if (t < 0f || t > seconds || knockbackJoltDegrees <= 0f) return Quaternion.identity;
            // The blow in the diver's own frame: +z is the way it faces, +x its right.
            Vector3 push = own ? knockOwnerPush : cue.Push; push.y = 0f;
            Vector3 local = push.sqrMagnitude > 1e-6f ? Quaternion.Inverse(Quaternion.Euler(0f, Yaw, 0f)) * push.normalized : Vector3.back;
            float back = -local.z, aside = local.x;
            // Thrown backward (struck in the face): the view kicks up; thrown forward: down.
            float pitchAmp = knockbackJoltDegrees * Mathf.Lerp(0.55f, 1f, Mathf.Abs(back)) * (back >= -0.2f ? -1f : 1f);
            // Thrown to a side, the view rolls with it; a head-on blow still rolls a little.
            float rollAmp = knockbackJoltDegrees * (0.3f + 0.4f * Mathf.Abs(aside)) * (aside >= 0f ? -1f : 1f);
            float fade = t < seconds * 0.6f ? 1f : Mathf.Pow(Mathf.Clamp01(1f - (t - seconds * 0.6f) / (seconds * 0.4f)), 2f);
            float pitchDeg = pitchAmp * JoltSpring(t, knockbackJoltSpring, 0.14f, 0.035f) * fade;
            float rollDeg = rollAmp * JoltSpring(t, knockbackJoltSpring * 0.8f, 0.16f, 0.045f) * fade;
            float tremor = knockbackJoltTremor * Mathf.Exp(-t / 0.07f) * fade;
            pitchDeg += tremor * Mathf.Sin(t * 2f * Mathf.PI * 21f);
            float yawDeg = tremor * 0.7f * Mathf.Sin(t * 2f * Mathf.PI * 17f + 1.1f) + aside * knockbackJoltDegrees * 0.25f * JoltSpring(t, knockbackJoltSpring, 0.12f, 0.04f) * fade;
            return Quaternion.Euler(pitchDeg, yawDeg, rollDeg);
        }

        // A snap to 1 over riseSeconds, then a damped swing back (one overshoot past 0).
        private static float JoltSpring(float t, float hz, float decaySeconds, float riseSeconds)
        {
            float rise = Mathf.Sin(Mathf.Clamp01(t / riseSeconds) * Mathf.PI * 0.5f);
            float after = Mathf.Max(0f, t - riseSeconds);
            return rise * Mathf.Exp(-after / decaySeconds) * Mathf.Cos(after * 2f * Mathf.PI * hz);
        }

        private float KnockSince(uint tick)
        {
            if (NetworkManager == null || NetworkManager.TimeManager == null) return float.PositiveInfinity;
            FishNet.Managing.Timing.TimeManager time = NetworkManager.TimeManager;
            uint now = time.Tick;
            if (now < tick) return 0f;
            return (float)(time.TicksToTime(now - tick) + time.GetTickElapsedAsDouble());
        }
    }
}
