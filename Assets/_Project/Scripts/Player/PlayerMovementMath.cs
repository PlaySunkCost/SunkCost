using UnityEngine;

namespace SunkCost.Player
{
    // Pure jump and capsule arithmetic (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
    // sections 3 and 4). No Unity objects touched, so the editor checks can cover
    // every row; the controller, the stance and the server clearance all read
    // the same dimensions from here.
    public static class PlayerMovementMath
    {
        // Height above the takeoff feet: linear from the base to the floor across
        // the carried capacity. Beyond capacity stays at the floor.
        public static float JumpHeight(PlayerMovementSettings settings, float carriedMassKg, float capacityKg)
        {
            float load = capacityKg > 0f ? Mathf.Clamp01(carriedMassKg / capacityKg) : 0f;
            float factor = Mathf.Lerp(1f, settings.MinJumpHeightFactor, load);
            return settings.BaseJumpHeight * factor;
        }

        // v = sqrt(2 g h); gravity is passed as the (negative) project value.
        public static float TakeoffSpeed(float jumpHeight, float gravityY)
        {
            float g = Mathf.Abs(gravityY);
            if (g <= 0f || jumpHeight <= 0f) return 0f;
            return Mathf.Sqrt(2f * g * jumpHeight);
        }

        public static float CapsuleHeight(PlayerMovementSettings settings, bool crouched) =>
            crouched ? settings.CrouchHeight : settings.StandingHeight;

        // Feet stay at the root: the centre is half the height up.
        public static Vector3 CapsuleCenter(PlayerMovementSettings settings, bool crouched) =>
            new(0f, CapsuleHeight(settings, crouched) * 0.5f, 0f);

        public static float EyeHeight(PlayerMovementSettings settings, bool crouched) =>
            crouched ? settings.CrouchEyeHeight : settings.StandingEyeHeight;

        public static float StepOffset(PlayerMovementSettings settings, bool crouched) =>
            crouched ? settings.CrouchStepOffset : settings.StandingStepOffset;

        // The extra volume a crouched capsule needs to stand: from the crouched top
        // to the standing top (plus clearance), same radius. Returned as a capsule
        // segment in local (feet-relative) space.
        public static void HeadroomCapsule(PlayerMovementSettings settings, out Vector3 bottom, out Vector3 top, out float radius)
        {
            radius = settings.CapsuleRadius;
            float crouchTop = settings.CrouchHeight;
            float standTop = settings.StandingHeight + settings.StandClearance;
            // Capsule endpoints are sphere centres: the volume spans exactly
            // [crouchTop, standTop] and nothing of the crouched capsule itself.
            bottom = new Vector3(0f, crouchTop + radius, 0f);
            top = new Vector3(0f, Mathf.Max(standTop - radius, bottom.y), 0f);
        }
    }
}
