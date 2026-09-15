using UnityEngine;

namespace SunkCost.Interaction
{
    public enum FingerPose : byte
    {
        Relaxed = 0,
        BallSmall = 1,
        BallLarge = 2
    }

    // Where the hands go on this item, authored in item-local space
    // (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 6, "Authored grip
    // targets"). Not networked, no physics: hands follow the item, never the
    // other way round. Every item is held with both hands; CarryGrip stays the
    // gameplay classification (slot-able or not), independent of this.
    public sealed class ItemHandPose : MonoBehaviour
    {
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private FingerPose fingers = FingerPose.BallSmall;

        public Transform RightGrip => rightGrip;
        public Transform LeftGrip => leftGrip;
        public FingerPose Fingers => fingers;
        public bool IsComplete => rightGrip != null && leftGrip != null;

        public void Configure(Transform right, Transform left, FingerPose pose)
        {
            rightGrip = right;
            leftGrip = left;
            fingers = pose;
        }
    }
}
