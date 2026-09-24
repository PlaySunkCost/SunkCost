using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // A killer that holds its catch before the kill (Dan, 24 September 2026): the
    // Long Walker grabs you and lifts you to its face for about two seconds, the
    // Weeping Angel embraces you; then the ordinary death (WorldSceneFlow.ServerKill).
    // On the prefab of a kind that grabs; the server's side of the hold is
    // Creature.Grab.cs, which reads these numbers. Every peer reads them too: the
    // held diver's feet and eyes (HQPlayerController.Grabbed) come from HoldPose,
    // so the diver's own screen, a spectator's and the deck TV show one hold. The
    // Grabbing clip is keyed to the same timing and points.
    //
    // All positions are in the creature's own space (x right, y up, z forward, metres
    // from its feet); the held diver's feet follow caught spot → grip → lift. The
    // defaults are the Long Walker's (tools/blender/clips/LongWalker.py keys its
    // Grabbing to them: the palms close on the diver's chest at the grip and carry it
    // up, its Face anchor ends about 0.4 m from the lifted diver's eyes).
    [DisallowMultipleComponent]
    public sealed class CreatureGrab : MonoBehaviour, IGrabHolder
    {
        [Header("Timing, seconds from the catch")]
        [Tooltip("The diver is drawn from where they stood into the grip.")]
        [SerializeField, Min(0.01f)] private float gripSeconds = 0.35f;
        [Tooltip("By then the diver is at the lift point (equal to the grip time: no lift).")]
        [SerializeField, Min(0.01f)] private float liftSeconds = 1.3f;
        [Tooltip("The kill (WorldSceneFlow.ServerKill), after the hold.")]
        [SerializeField, Min(0.1f)] private float holdSeconds = 2f;
        [Tooltip("After the kill the creature stays in its Grabbing pose, still, this long (the body drops, the hands open); then its brain resumes. Key the clip's last part to it.")]
        [SerializeField, Min(0f)] private float releaseSeconds = 0.8f;

        [Header("Where the diver is held (creature space, the diver's feet)")]
        [SerializeField] private Vector3 gripPoint = new(0f, 0f, 0.85f);
        [SerializeField] private Vector3 liftPoint = new(0f, 1.35f, 0.85f);
        [Tooltip("How the lift eases from the grip (0) to the lift point (1): a little over and back reads as weight.")]
        [SerializeField] private AnimationCurve liftEase = new(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.72f, 1.04f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("Where the diver's eyes are turned (creature space)")]
        [SerializeField] private Vector3 faceTarget = new(0.04f, 2.9f, 0.38f);

        [Header("The shake of the held view (degrees)")]
        [Tooltip("At the catch; it grows to the full shake by the kill.")]
        [SerializeField, Min(0f)] private float shakeStartDegrees = 1.2f;
        [SerializeField, Min(0f)] private float shakeDegrees = 3f;
        [SerializeField, Min(0.1f)] private float shakeHz = 7f;
        [Tooltip("A jolt when the grip closes.")]
        [SerializeField, Min(0f)] private float gripJoltDegrees = 5f;

        [Header("The creature")]
        [Tooltip("How fast it turns to face its catch during the grip, degrees per second.")]
        [SerializeField, Min(1f)] private float turnDegPerSec = 360f;

        private Creature creature;

        public float GripSeconds => gripSeconds;
        public float LiftSeconds => Mathf.Max(gripSeconds, liftSeconds);
        public float HoldSeconds => Mathf.Max(LiftSeconds, holdSeconds);
        public float ReleaseSeconds => releaseSeconds;
        public float TurnDegPerSec => turnDegPerSec;
        public Vector3 GripPoint => gripPoint;
        public Vector3 LiftPoint => liftPoint;
        public Vector3 FaceTarget => faceTarget;

        private void Awake() => creature = GetComponent<Creature>();

        public GrabPose HoldPose(Vector3 caughtAt, float seconds)
        {
            Transform t = transform;
            Vector3 grip = t.TransformPoint(gripPoint);
            Vector3 feet;
            if (seconds < gripSeconds) feet = Vector3.Lerp(caughtAt, grip, Mathf.SmoothStep(0f, 1f, seconds / gripSeconds));
            else if (seconds < LiftSeconds && LiftSeconds > gripSeconds)
                feet = Vector3.LerpUnclamped(grip, t.TransformPoint(liftPoint), liftEase.Evaluate((seconds - gripSeconds) / (LiftSeconds - gripSeconds)));
            else feet = LiftSeconds > gripSeconds ? t.TransformPoint(liftPoint) : grip;
            float grow = Mathf.Clamp01(seconds / HoldSeconds);
            float jolt = gripJoltDegrees * Mathf.Max(0f, 1f - Mathf.Abs(seconds - gripSeconds) / 0.18f);
            return new GrabPose
            {
                Feet = feet,
                Face = t.TransformPoint(faceTarget),
                ShakeDegrees = Mathf.Lerp(shakeStartDegrees, shakeDegrees, grow * grow) + jolt,
                ShakeHz = shakeHz
            };
        }

        public bool ServerHolds(HQPlayerController diver)
        {
            if (creature == null) creature = GetComponent<Creature>();
            return creature != null && creature.ServerHolding(diver);
        }

        private void OnDrawGizmosSelected()
        {
            Transform t = transform;
            Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(t.TransformPoint(gripPoint), 0.15f);
            Gizmos.color = new Color(1f, 0.5f, 0f); Gizmos.DrawWireSphere(t.TransformPoint(liftPoint), 0.15f);
            Gizmos.DrawLine(t.TransformPoint(liftPoint), t.TransformPoint(liftPoint) + Vector3.up * 1.6f);
            Gizmos.color = Color.red; Gizmos.DrawWireSphere(t.TransformPoint(faceTarget), 0.1f);
        }
    }
}
