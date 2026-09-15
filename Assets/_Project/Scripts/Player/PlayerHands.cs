using FishNet.Object;
using SunkCost.Interaction;
using UnityEngine;

namespace SunkCost.Player
{
    // Two gloved arms from the shoulders to whatever this player holds
    // (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 6). Presentation
    // only: the arms read the held item's replicated pose after CarryableItem's
    // LateUpdate and never write item position, ownership or physics. Owner and
    // remote views run the same code; the owner's item follows its camera, a
    // remote item arrives through its NetworkTransform. The geometry is built by
    // the setup (PlayerMovementHandsSetup) and found here by name.
    [DefaultExecutionOrder(50)]
    public sealed class PlayerHands : NetworkBehaviour
    {
        public const string TorsoName = "Torso";
        public const string ShoulderRightName = "ShoulderR";
        public const string ShoulderLeftName = "ShoulderL";
        public const string ArmRightName = "ArmR";
        public const string ArmLeftName = "ArmL";
        public const string UpperArmName = "UpperArm";
        public const string ForearmName = "Forearm";
        public const string HandName = "Hand";
        public const string FingerPrefix = "Finger";
        public const string ThumbName = "Thumb";

        [SerializeField] private Transform torso;
        [SerializeField] private Transform shoulderRight;
        [SerializeField] private Transform shoulderLeft;
        [SerializeField] private Transform armRight;
        [SerializeField] private Transform armLeft;
        [SerializeField] private float upperArmLength = 0.28f;
        [SerializeField] private float forearmLength = 0.26f;
        [SerializeField] private float blendSeconds = 0.1f;
        // Relaxed hands, torso-relative: in front and low, clear of the crosshair.
        [SerializeField] private Vector3 restWristRight = new(0.24f, -0.32f, 0.30f);
        [SerializeField] private Vector3 restWristLeft = new(-0.24f, -0.32f, 0.30f);

        private PlayerInventory inventory;
        private CarryableItem held;          // what the hands are on this frame
        private ItemHandPose heldPose;
        private float nextRescan;
        private readonly Arm right = new();
        private readonly Arm left = new();

        public CarryableItem HeldForHands => held;
        public bool ReachClamped => right.Clamped || left.Clamped;

        private sealed class Arm
        {
            public Transform Root, Upper, Forearm, Hand, Thumb;
            public Transform[] Fingers = new Transform[0];
            public Vector3 Wrist; public Quaternion WristRotation = Quaternion.identity;
            public Vector3 BlendFromWrist; public Quaternion BlendFromRotation = Quaternion.identity;
            public float BlendStart = float.NegativeInfinity;
            public object Source;             // the item (or null for rest) the current pose comes from
            public bool Initialized;
            public bool Clamped;
        }

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            Bind(right, armRight);
            Bind(left, armLeft);
        }

        private static void Bind(Arm arm, Transform root)
        {
            arm.Root = root;
            if (root == null) return;
            arm.Upper = root.Find(UpperArmName);
            arm.Forearm = root.Find(ForearmName);
            arm.Hand = root.Find(HandName);
            if (arm.Hand != null)
            {
                arm.Thumb = arm.Hand.Find(ThumbName);
                var fingers = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in arm.Hand) if (child.name.StartsWith(FingerPrefix)) fingers.Add(child);
                arm.Fingers = fingers.ToArray();
            }
        }

        private void LateUpdate()
        {
            if (torso == null || shoulderRight == null || shoulderLeft == null) return;
            ResolveHeld();
            FingerPose fingers = heldPose != null ? heldPose.Fingers : FingerPose.Relaxed;
            Transform rightGrip = heldPose != null ? heldPose.RightGrip : null;
            Transform leftGrip = heldPose != null ? heldPose.LeftGrip : null;
            Vector3 down = -torso.up;
            Pose(right, shoulderRight.position, rightGrip, torso.TransformPoint(restWristRight), torso.rotation * Quaternion.Euler(0f, 0f, -80f), torso.right + down * 0.6f, fingers, held);
            Pose(left, shoulderLeft.position, leftGrip, torso.TransformPoint(restWristLeft), torso.rotation * Quaternion.Euler(0f, 0f, 80f), -torso.right + down * 0.6f, fingers, held);
        }

        // The item this player holds, from the replicated state: the owner's own
        // view first, otherwise the unique Held item whose holder is this player.
        // Rescans only when the cache is stale, at most four times a second.
        private void ResolveHeld()
        {
            int ownerId = Owner != null && Owner.IsValid ? Owner.ClientId : -1;
            if (held != null && held.IsSpawned && held.State == ItemState.Held && held.HolderClientId == ownerId) return;
            held = null; heldPose = null;
            if (IsOwner && inventory != null && inventory.HeldItem != null && inventory.HeldItem.State == ItemState.Held)
            {
                Set(inventory.HeldItem);
                return;
            }
            if (Time.unscaledTime < nextRescan) return;
            nextRescan = Time.unscaledTime + 0.25f;
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>())
            {
                if (item.IsSpawned && item.State == ItemState.Held && item.HolderClientId == ownerId) { Set(item); return; }
            }
        }

        private void Set(CarryableItem item)
        {
            held = item;
            heldPose = item != null ? item.GetComponent<ItemHandPose>() : null;
            if (held != null && heldPose == null) Debug.LogWarning(held.name + " has no ItemHandPose; hands rest (run the movement/hands setup).");
        }

        private void Pose(Arm arm, Vector3 shoulder, Transform grip, Vector3 restWrist, Quaternion restRotation, Vector3 elbowHint, FingerPose fingers, object source)
        {
            if (arm.Root == null) return;
            Vector3 targetWrist = grip != null ? grip.position : restWrist;
            Quaternion targetRotation = grip != null ? grip.rotation : restRotation;
            object targetSource = grip != null ? source : null;
            if (!arm.Initialized)
            {
                // First valid pose (spawn, late join): no blend from nowhere.
                arm.Wrist = targetWrist; arm.WristRotation = targetRotation; arm.Source = targetSource; arm.Initialized = true;
            }
            else if (!ReferenceEquals(arm.Source, targetSource))
            {
                arm.BlendFromWrist = arm.Wrist; arm.BlendFromRotation = arm.WristRotation;
                arm.BlendStart = Time.unscaledTime;
                arm.Source = targetSource;
            }
            float t = blendSeconds <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - arm.BlendStart) / blendSeconds);
            arm.Wrist = Vector3.Lerp(arm.BlendFromWrist, targetWrist, t);
            arm.WristRotation = Quaternion.Slerp(arm.BlendFromRotation, targetRotation, t);
            if (t >= 1f) { arm.BlendFromWrist = targetWrist; arm.BlendFromRotation = targetRotation; }

            ArmPoseSolver.Result solved = ArmPoseSolver.Solve(shoulder, arm.Wrist, elbowHint, upperArmLength, forearmLength);
            arm.Clamped = solved.Clamped;
            Segment(arm.Upper, shoulder, solved.Elbow);
            Segment(arm.Forearm, solved.Elbow, solved.Wrist);
            if (arm.Hand != null) arm.Hand.SetPositionAndRotation(solved.Wrist, arm.WristRotation);
            Curl(arm, fingers);
        }

        // A unit-length box along its local Z, stretched between two joints.
        private static void Segment(Transform segment, Vector3 from, Vector3 to)
        {
            if (segment == null) return;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f) return;
            segment.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(delta / length, Vector3.up));
            Vector3 scale = segment.localScale;
            segment.localScale = new Vector3(scale.x, scale.y, length);
        }

        // Cosmetic finger curl about the knuckle (local X), by pose.
        private static void Curl(Arm arm, FingerPose pose)
        {
            float curl = pose switch { FingerPose.BallSmall => 55f, FingerPose.BallLarge => 30f, _ => 15f };
            foreach (Transform finger in arm.Fingers) if (finger != null) finger.localRotation = Quaternion.Euler(curl, 0f, 0f); // toward the palm (-Y)
            if (arm.Thumb != null) arm.Thumb.localRotation = Quaternion.Euler(curl * 0.5f, 0f, 0f);
        }

        public void Configure(Transform torsoAnchor, Transform shoulderR, Transform shoulderL, Transform armR, Transform armL)
        {
            torso = torsoAnchor; shoulderRight = shoulderR; shoulderLeft = shoulderL; armRight = armR; armLeft = armL;
        }
    }
}
