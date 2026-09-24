using System;
using System.Collections.Generic;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Monsters
{
    // A modelled monster's rig (docs/MONSTER_MODELS.md; Dan's Blender models, 22
    // September 2026): the Animator on the model under "Model" is driven from the
    // replicated pose and the copy's own speed — an int Pose (CreaturePose), a
    // float Speed (flat metres per second) and a bool Moving — and the model's
    // named anchors are where the beam leaves (BeamOrigin) and the voice sits
    // (Voice). A pose the model has no clip for falls back to CreatureLook's code
    // bob, so a half-finished model still reads. Wired onto the prefab by
    // MonsterModelSetup.
    //
    // The runtime animation layer (24 September 2026), every part serialized per
    // prefab and off until a prefab turns it on:
    // - speed-matched playback: a walking pose plays at the ground speed over the
    //   speed its clip was authored at, so the feet neither slide nor moonwalk; a
    //   walking pose standing still shows the stand pose instead of marching;
    // - transitions the rig drives itself: a blend time per pose, no restarts, and a
    //   walk that hands over to another walk keeps its step;
    // - the head (a share down the spine) turns toward the target, and while a beam
    //   charges or burns it aims so the BeamOrigin looks straight down the beam;
    // - a lean into turns (the lateral acceleration's angle);
    // - breathing on the chest, a random start and a small rate difference per copy.
    // Every peer runs it from replicated state (the pose, the target, the beam's cue
    // and aim, the transform). The server reads one thing back: the BeamOrigin, where
    // the beam leaves on every screen and where its damage starts, which is why a
    // beam monster animates off-screen too.
    [DefaultExecutionOrder(-40)] // after the Animator, before CreatureLook and the beam view read the bones
    public sealed class CreatureRig : MonoBehaviour
    {
        public const string ModelName = "Model", BeamOriginName = "BeamOrigin", VoiceName = "Voice";
        public static readonly int PoseId = Animator.StringToHash("Pose");
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int MovingId = Animator.StringToHash("Moving");
        private const float TeleportMetres = 3f;

        [Serializable]
        public struct PoseStride
        {
            public CreaturePose pose;
            [Tooltip("The flat speed the clip's planted foot travels back at, metres per second (Sunk Cost → Look → Measure monster strides).")]
            public float metresPerSecond;
        }

        [Serializable]
        public struct PoseBlend
        {
            public CreaturePose pose;
            [Tooltip("Blend into this pose over this many seconds.")]
            public float seconds;
        }

        [Tooltip("The model's Animator (found under Model when empty).")]
        [SerializeField] private Animator animator;
        [Tooltip("Where a beam leaves the model (found by name when empty): the drawn beam and the server's damaging line both start here.")]
        [SerializeField] private Transform beamOrigin;
        [Tooltip("Where the voice sits (found by name when empty).")]
        [SerializeField] private Transform voice;
        [Tooltip("Which poses the model has a clip for, by clip name (filled by the setup); the others use the code bob.")]
        [SerializeField] private string[] animatedPoses = new string[0];

        [Header("Speed-matched playback (off by default)")]
        [Tooltip("Play each walking pose at the ground speed over its clip's authored speed.")]
        [SerializeField] private bool matchSpeed;
        [Tooltip("The walking poses and the speed each clip was authored at.")]
        [SerializeField] private PoseStride[] strides = new PoseStride[0];
        [Tooltip("The playback rate stays inside this range (x slowest, y fastest).")]
        [SerializeField] private Vector2 playbackRange = new(0.45f, 1.8f);
        [Tooltip("A walking pose slower than this (m/s) shows the stand pose instead; 0 = never.")]
        [SerializeField] private float standBelow;
        [Tooltip("What a walking pose standing still shows.")]
        [SerializeField] private CreaturePose standPose = CreaturePose.Idle;

        [Header("Transitions (off by default: the controller's Any State, 0.15 s)")]
        [Tooltip("The rig cross-fades the Animator itself: a blend time per pose, no restarts, walks keep their step.")]
        [SerializeField] private bool driveTransitions;
        [Tooltip("The blend into any pose without its own time below, seconds.")]
        [SerializeField] private float blendSeconds = 0.2f;
        [SerializeField] private PoseBlend[] blends = new PoseBlend[0];
        [Tooltip("A walk handing over to another walk (or to its stand) starts at the same point of the step.")]
        [SerializeField] private bool keepStridePhase = true;

        [Header("Head look-at (off: weight 0)")]
        [Range(0f, 1f)]
        [SerializeField] private float lookWeight;
        [Tooltip("The chain that turns, root first (found as Spine, Neck, Head when empty).")]
        [SerializeField] private Transform[] lookBones = new Transform[0];
        [Tooltip("Each bone's share of the turn, in the order above.")]
        [SerializeField] private float[] lookShares = { 0.15f, 0.3f, 0.55f };
        [SerializeField] private float lookMaxYaw = 70f;
        [SerializeField] private float lookMaxPitch = 35f;
        [Tooltip("How fast the look follows, degrees per second.")]
        [SerializeField] private float lookDegPerSec = 240f;
        [Tooltip("Where on a diver it looks, metres over their feet.")]
        [SerializeField] private float lookHeight = 1.5f;
        [Tooltip("Poses in which the head keeps to the clip (the Angel frozen, a grab).")]
        [SerializeField] private CreaturePose[] lookOffPoses = { CreaturePose.Frozen };
        [Tooltip("Poses in which this layer adds nothing at all — no look, aim, breath or lean — and plays the clip at 1× (the Angel frozen: nothing may move it while it is watched).")]
        [SerializeField] private CreaturePose[] stillPoses = { CreaturePose.Frozen };
        [Tooltip("While its beam charges or burns, the head turns so the BeamOrigin looks straight down the beam (off by default; it moves where the beam leaves).")]
        [SerializeField] private bool aimAtBeam;
        [SerializeField] private float aimMaxDegrees = 75f;

        [Header("Lean into turns (off: 0)")]
        [Tooltip("The most it leans, degrees; 0 = off.")]
        [SerializeField] private float leanMaxDegrees;
        [Tooltip("1 = the true angle of the turn's lateral acceleration; less is subtler.")]
        [SerializeField] private float leanStrength = 0.6f;
        [Tooltip("What leans (the model when empty; it pivots at the feet).")]
        [SerializeField] private Transform leanPivot;

        [Header("Breathing and variation")]
        [Tooltip("The chest rises this many degrees on a breath; 0 = off.")]
        [SerializeField] private float breathDegrees;
        [SerializeField] private float breathSeconds = 3.4f;
        [Tooltip("The bone that breathes (found as Spine when empty).")]
        [SerializeField] private Transform breathBone;
        [Tooltip("Each copy plays its non-walking clips up to this fraction faster or slower.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float rateVariation;
        [Tooltip("Each copy starts its first clip at a random point, so two never move in step.")]
        [SerializeField] private bool randomStart = true;
        [Tooltip("Animate while off every screen. A beam monster always does: its BeamOrigin is where the damage starts.")]
        [SerializeField] private bool alwaysAnimate;
        [Tooltip("A BeamOrigin further than this from the eye point (a broken rig) is not trusted.")]
        [SerializeField] private float maxBeamOriginFromEye = 1.5f;

        private Creature creature;
        private CreatureBolts bolts;
        private Vector3 lastPosition;
        private float lastYaw;
        private bool primed;
        private float speed, yawRate, rateJitter = 1f, breathPhase;
        private bool[] animated;
        private bool standing;
        private int shownState = -1;
        private readonly Dictionary<int, float> clipLengths = new();
        private float lookYaw, lookPitch, lookBlend, aimBlend, lean;
        private Vector3 lastAim;
        private HQPlayerController lookDiver;
        private int lookDiverId = -2;
        private float lookDiverCheckedAt = float.NegativeInfinity;
        private Quaternion leanRest = Quaternion.identity;

        // A bone this layer turns: when the Animator did not write it this frame (culled,
        // or a pose it holds), last frame's turn is taken off before this frame's goes on.
        private sealed class Edit { public Transform Bone; public Quaternion Before, Written; public bool Has; }
        private readonly List<Edit> edits = new();

        public Animator Animator => animator != null ? animator : animator = GetComponentInChildren<Animator>(true);
        public bool HasAnimator => Animator != null && Animator.runtimeAnimatorController != null;
        public Transform BeamOrigin => beamOrigin != null ? beamOrigin : beamOrigin = Find(BeamOriginName);
        public Transform VoiceAnchor => voice != null ? voice : voice = Find(VoiceName);
        public float Speed => speed;
        public float YawRate => yawRate;
        public float PlaybackRate => HasAnimator ? Animator.speed : 1f;
        public CreaturePose ShownPose { get; private set; }
        public float Lean => lean;

        // The model carries a clip for this pose: the code bob stays out of its way.
        public bool Animated(CreaturePose pose)
        {
            if (!HasAnimator) return false;
            if (animated == null)
            {
                animated = new bool[16];
                foreach (string name in animatedPoses)
                    if (Enum.TryParse(name, out CreaturePose p) && (int)p < animated.Length) animated[(int)p] = true;
            }
            return (int)pose < animated.Length && animated[(int)pose];
        }

        // Where a beam leaves: the BeamOrigin, or the fallback (the eye point) when the
        // model has none, it is implausibly far from the eyes, or a wall stands between
        // the eyes and it (a lantern through a wall must not shoot from the far side).
        // The drawn beam and the server's damaging line both start here.
        public Vector3 BeamOriginPoint(Vector3 fallback)
        {
            Transform origin = BeamOrigin;
            if (origin == null) return fallback;
            Vector3 point = origin.position;
            if (creature == null) return point;
            Vector3 eye = creature.EyePoint;
            if ((point - eye).sqrMagnitude > maxBeamOriginFromEye * maxBeamOriginFromEye) return eye;
            if (!CreatureSenses.ClearLine(eye, point)) return eye;
            return point;
        }

        public bool IsStride(CreaturePose pose) => AuthoredSpeed(pose) > 0f;

        public float AuthoredSpeed(CreaturePose pose)
        {
            if (strides == null) return 0f;
            foreach (PoseStride s in strides) if (s.pose == pose) return s.metresPerSecond;
            return 0f;
        }

        private Transform Find(string name)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private void Awake()
        {
            creature = GetComponent<Creature>();
            bolts = GetComponent<CreatureBolts>();
            Animator a = Animator;
            if (a != null)
            {
                a.applyRootMotion = false;
                a.cullingMode = alwaysAnimate || bolts != null ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
            }
            if (lookBones == null || lookBones.Length == 0)
            {
                var found = new List<Transform>();
                foreach (string n in new[] { "Spine", "Neck", "Head" }) { Transform t = Find(n); if (t != null) found.Add(t); }
                lookBones = found.ToArray();
            }
            if (breathBone == null) breathBone = Find("Spine");
            if (leanPivot == null && a != null) leanPivot = a.transform;
            if (leanPivot != null) leanRest = leanPivot.localRotation;
            rateJitter = 1f + UnityEngine.Random.Range(-rateVariation, rateVariation);
            breathPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            if (a != null && a.runtimeAnimatorController != null)
                foreach (AnimationClip clip in a.runtimeAnimatorController.animationClips)
                    if (clip != null) clipLengths[Animator.StringToHash(clip.name)] = clip.length;
        }

        private void LateUpdate()
        {
            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;
            float dt = Time.deltaTime;
            if (!primed) { lastPosition = position; lastYaw = yaw; primed = true; return; }
            Vector3 flat = position - lastPosition; flat.y = 0f;
            float turned = Mathf.DeltaAngle(lastYaw, yaw);
            lastPosition = position; lastYaw = yaw;
            if (dt > 0f)
            {
                bool jumped = flat.magnitude > TeleportMetres;
                float now = jumped ? speed : flat.magnitude / dt;
                float k = 1f - Mathf.Exp(-dt / 0.12f);
                speed = Mathf.Lerp(speed, now, k);
                yawRate = Mathf.Lerp(yawRate, jumped ? 0f : turned / dt, 1f - Mathf.Exp(-dt / 0.15f));
            }
            if (!HasAnimator || creature == null) return;
            Animator a = Animator;
            CreaturePose pose = creature.Pose;

            // What to show: a walking pose standing still shows the stand pose (with a
            // little hysteresis so a creature easing to a stop does not flicker).
            if (matchSpeed && standBelow > 0f && IsStride(pose))
                standing = standing ? speed < standBelow * 1.6f : speed < standBelow;
            else standing = false;
            CreaturePose shown = standing && Animated(standPose) ? standPose : pose;
            ShownPose = shown;

            if (driveTransitions) Drive(a, shown);
            else a.SetInteger(PoseId, (int)shown);
            a.SetFloat(SpeedId, speed);
            a.SetBool(MovingId, speed > 0.15f);

            // The playback rate: a walk at the ground speed over its authored one; the rest at the copy's own rate.
            bool still = InList(stillPoses, pose);
            float authored = AuthoredSpeed(shown);
            a.speed = still ? 1f
                : matchSpeed && authored > 0f ? Mathf.Clamp(speed / authored, playbackRange.x, playbackRange.y)
                : rateJitter;

            UndoUnwrittenEdits();
            if (still)
            {
                // Nothing added: the look, the aim and the lean let go at once, so the clip alone holds it.
                lookBlend = aimBlend = 0f; lookYaw = lookPitch = 0f; lean = 0f;
                if (leanPivot != null && leanMaxDegrees > 0f) leanPivot.localRotation = leanRest;
            }
            else
            {
                Breathe(dt);
                LookAndAim(pose, dt);
                LeanIntoTurns(dt);
            }
            RecordEdits();
        }

        // ---- transitions -----------------------------------------------------------------

        private void Drive(Animator a, CreaturePose shown)
        {
            a.SetInteger(PoseId, -1); // no Any State transition matches: the rig cross-fades
            int hash = Animator.StringToHash(shown.ToString());
            if (hash == shownState || !a.HasState(0, hash)) return;
            float length = clipLengths.TryGetValue(hash, out float l) ? l : 0f;
            if (shownState == -1)
            {
                float offset = randomStart && length > 0f ? UnityEngine.Random.value * length : 0f;
                a.PlayInFixedTime(hash, 0, offset);
                shownState = hash;
                return;
            }
            float blend = blendSeconds;
            if (blends != null) foreach (PoseBlend b in blends) if (b.pose == shown) blend = b.seconds;
            float start = 0f;
            AnimatorStateInfo current = a.GetCurrentAnimatorStateInfo(0);
            bool walkToWalk = keepStridePhase && IsStride(shown) && ShownWasStride();
            if (walkToWalk && length > 0f) start = Mathf.Repeat(current.normalizedTime, 1f) * length;
            a.CrossFadeInFixedTime(hash, Mathf.Max(0f, blend), 0, start);
            shownState = hash;
        }

        private bool ShownWasStride()
        {
            foreach (PoseStride s in strides) if (Animator.StringToHash(s.pose.ToString()) == shownState) return true;
            return false;
        }

        // ---- bones -------------------------------------------------------------------------

        private Edit EditOf(Transform bone)
        {
            foreach (Edit e in edits) if (e.Bone == bone) return e;
            var made = new Edit { Bone = bone, Before = bone.localRotation };
            edits.Add(made);
            return made;
        }

        private void UndoUnwrittenEdits()
        {
            foreach (Edit e in edits)
            {
                if (e.Bone == null) continue;
                if (e.Has && e.Bone.localRotation == e.Written) e.Bone.localRotation = e.Before;
                e.Before = e.Bone.localRotation;
            }
        }

        private void RecordEdits()
        {
            foreach (Edit e in edits) if (e.Bone != null) { e.Written = e.Bone.localRotation; e.Has = true; }
        }

        private void Turn(Transform bone, Quaternion world)
        {
            if (bone == null) return;
            EditOf(bone);
            bone.rotation = world * bone.rotation;
        }

        private void Breathe(float dt)
        {
            if (breathDegrees <= 0f || breathBone == null || breathSeconds <= 0f) return;
            breathPhase += dt * Mathf.PI * 2f / breathSeconds;
            // In slowly, out a little quicker: a breath, not a metronome.
            float s = Mathf.Sin(breathPhase);
            float shaped = s >= 0f ? s : s * 0.8f;
            Turn(breathBone, Quaternion.AngleAxis(-breathDegrees * shaped, transform.right));
        }

        private bool LookOff(CreaturePose pose) => InList(lookOffPoses, pose) || InList(stillPoses, pose);

        private static bool InList(CreaturePose[] list, CreaturePose pose)
        {
            if (list == null) return false;
            foreach (CreaturePose p in list) if (p == pose) return true;
            return false;
        }

        private HQPlayerController DiverById(int id)
        {
            if (id < 0) return null;
            if (id == lookDiverId && lookDiver != null && Time.time - lookDiverCheckedAt < 1f) return lookDiver;
            lookDiverId = id;
            lookDiverCheckedAt = Time.time;
            lookDiver = null;
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (p != null && p.IsSpawned && p.OwnerId == id) { lookDiver = p; break; }
            return lookDiver;
        }

        private bool BeamUp(out Vector3 aim)
        {
            aim = default;
            if (bolts == null) return false;
            BeamCue cue = bolts.Cue;
            if (cue.Serial == 0 || (cue.Phase != BeamPhase.Charging && cue.Phase != BeamPhase.Firing)) return false;
            aim = bolts.BeamAim;
            return aim != Vector3.zero;
        }

        private void LookAndAim(CreaturePose pose, float dt)
        {
            if (lookBones == null || lookBones.Length == 0) return;
            Transform head = lookBones[lookBones.Length - 1];
            if (head == null) return;
            float fade = 1f - Mathf.Exp(-dt / 0.18f);

            // While a beam is up: the BeamOrigin looks straight down it.
            Vector3 aim = default;
            bool aiming = aimAtBeam && BeamUp(out aim) && BeamOrigin != null && !LookOff(pose);
            if (aiming) lastAim = aim;
            aimBlend = Mathf.Lerp(aimBlend, aiming ? 1f : 0f, fade);
            if (aimBlend > 0.01f && lastAim != Vector3.zero && BeamOrigin != null) AimChain(head, lastAim, aimBlend);
            else if (!aiming) aimBlend = 0f;

            // Otherwise (and fading under the aim): the head turns toward the target.
            float want = 0f;
            float wantYaw = 0f, wantPitch = 0f;
            HQPlayerController diver = lookWeight > 0f && !LookOff(pose) ? DiverById(creature.TargetId) : null;
            if (diver != null)
            {
                Vector3 target = diver.transform.position + Vector3.up * lookHeight;
                Vector3 local = transform.InverseTransformDirection(target - head.position);
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                if (Mathf.Abs(yaw) < lookMaxYaw + 40f)
                {
                    want = lookWeight;
                    wantYaw = Mathf.Clamp(yaw, -lookMaxYaw, lookMaxYaw);
                    wantPitch = Mathf.Clamp(pitch, -lookMaxPitch, lookMaxPitch);
                }
            }
            float step = lookDegPerSec * dt;
            lookYaw = Mathf.MoveTowards(lookYaw, want > 0f ? wantYaw : 0f, step);
            lookPitch = Mathf.MoveTowards(lookPitch, want > 0f ? wantPitch : 0f, step);
            lookBlend = Mathf.Lerp(lookBlend, want, fade);
            float weight = lookBlend * (1f - aimBlend);
            if (weight < 0.001f) return;
            for (int i = 0; i < lookBones.Length; i++)
            {
                float share = i < lookShares.Length ? lookShares[i] : 0f;
                if (share <= 0f) continue;
                Quaternion q = Quaternion.AngleAxis(lookYaw * share * weight, transform.up) * Quaternion.AngleAxis(lookPitch * share * weight, transform.right);
                Turn(lookBones[i], q);
            }
        }

        // Turn the chain (by share) so the line from the head to the BeamOrigin points at
        // the aim, then the head alone takes up what the chain's moved pivots left over.
        private void AimChain(Transform head, Vector3 aim, float weight)
        {
            Transform origin = BeamOrigin;
            Vector3 have = origin.position - head.position;
            Vector3 want = aim - head.position;
            if (have.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            Quaternion full = Quaternion.FromToRotation(have, want);
            full.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(angle) < 0.01f) return;
            angle = Mathf.Clamp(angle, -aimMaxDegrees, aimMaxDegrees) * weight;
            float total = 0f;
            foreach (float s in lookShares) total += Mathf.Max(0f, s);
            if (total <= 0f) total = 1f;
            for (int i = 0; i < lookBones.Length; i++)
            {
                float share = i < lookShares.Length ? Mathf.Max(0f, lookShares[i]) / total : 0f;
                if (share > 0f) Turn(lookBones[i], Quaternion.AngleAxis(angle * share, axis));
            }
            // The residual, on the head: its pivot moved with the spine and the neck.
            have = origin.position - head.position;
            Quaternion rest = Quaternion.FromToRotation(have, want);
            rest.ToAngleAxis(out float left, out Vector3 leftAxis);
            if (left > 180f) left -= 360f;
            if (!float.IsNaN(leftAxis.x) && Mathf.Abs(left) > 0.01f)
                Turn(head, Quaternion.AngleAxis(Mathf.Clamp(left, -10f, 10f) * weight, leftAxis));
        }

        private void LeanIntoTurns(float dt)
        {
            if (leanPivot == null) return;
            float want = 0f;
            if (leanMaxDegrees > 0f)
            {
                // The lateral acceleration of the turn, v·ω, as an angle from upright.
                float lateral = speed * yawRate * Mathf.Deg2Rad;
                want = Mathf.Clamp(-Mathf.Atan2(lateral, 9.81f) * Mathf.Rad2Deg * leanStrength, -leanMaxDegrees, leanMaxDegrees);
            }
            lean = Mathf.Lerp(lean, want, 1f - Mathf.Exp(-dt / 0.2f));
            if (leanMaxDegrees <= 0f && Mathf.Abs(lean) < 0.01f) return;
            leanPivot.localRotation = leanRest * Quaternion.Euler(0f, 0f, lean);
        }
    }
}
