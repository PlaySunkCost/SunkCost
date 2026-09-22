using UnityEngine;

namespace SunkCost.Player
{
    // Jump, crouch, release-placement and reticle tuning
    // (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 3). Walk, sprint
    // and weight keep their current homes (the controller and WeightSettings).
    // Provisional numbers; changing them is not a design change.
    [CreateAssetMenu(fileName = "PlayerMovementSettings", menuName = "Sunk Cost/Player Movement Settings")]
    public sealed class PlayerMovementSettings : ScriptableObject
    {
        [Header("Jump")]
        [Tooltip("Feet apex above the takeoff point with nothing carried, metres.")]
        [SerializeField] private float baseJumpHeight = 0.65f;
        [Tooltip("Jump height at full carried capacity, as a fraction of the base height.")]
        [SerializeField] private float minJumpHeightFactor = 0.4f;
        [Tooltip("Seconds after walking off an edge during which a jump still counts.")]
        [SerializeField] private float coyoteTime = 0.10f;
        [Tooltip("Seconds a Space press is remembered before landing.")]
        [SerializeField] private float jumpBufferSeconds = 0.10f;

        [Header("Capsule and eyes (feet at the root)")]
        [SerializeField] private float capsuleRadius = 0.3f;
        [SerializeField] private float standingHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float standingEyeHeight = 1.6f;
        [SerializeField] private float crouchEyeHeight = 0.85f;
        [SerializeField] private float standingStepOffset = 0.25f;
        [SerializeField] private float crouchStepOffset = 0.15f;
        [Tooltip("Seconds the camera and body take to blend between stances, both ways (presentation only).")]
        [SerializeField] private float crouchBlendSeconds = 0.2f;
        [Tooltip("Crouched walking speed as a fraction of walking speed; the weight factor multiplies it.")]
        [SerializeField] private float crouchSpeedFactor = 0.5f;
        [Tooltip("Seconds between stand attempts while the desired stance is standing and the head is blocked.")]
        [SerializeField] private float standRetrySeconds = 0.2f;
        [Tooltip("Extra headroom required above the standing capsule before standing is accepted, metres.")]
        [SerializeField] private float standClearance = 0.05f;

        [Header("Dash (docs/DESIGN.md §3; Dan, 21 September 2026)")]
        [Tooltip("Ground a dash covers with nothing carried, metres; the weight factor scales it (Dan: 6, \"longer, like 150 %\" of the first 4).")]
        [SerializeField] private float dashMeters = 6f;
        [Tooltip("How long the burst lasts, seconds.")]
        [SerializeField] private float dashSeconds = 0.25f;
        [Tooltip("The kick upward at the start of a dash, metres per second — the little lift of a dash through water (Dan); gravity takes it back. 2.5 = about a third of a metre.")]
        [SerializeField] private float dashLiftSpeed = 2.5f;
        [Tooltip("Seconds from one dash to the next (the Charger turns in about 3).")]
        [SerializeField] private float dashCooldownSeconds = 3f;

        [Header("Release placement (section 6A)")]
        [Tooltip("Gap between the player's capsule and the released item's near surface, metres.")]
        [SerializeField] private float releaseClearance = 0.10f;
        [Tooltip("Farthest a released item may be placed in front of the player, metres.")]
        [SerializeField] private float releaseMaxForward = 2.0f;
        [Tooltip("How far below the drop point a supporting surface is looked for, metres.")]
        [SerializeField] private float dropGroundSearch = 1.0f;
        [Tooltip("Skin above the surface a dropped item is placed with, metres.")]
        [SerializeField] private float dropSkin = 0.05f;
        [Tooltip("Lowest allowed launch pitch in degrees: the throw follows the crosshair down to this (the look clamp is -80).")]
        [SerializeField] private float minThrowPitchDegrees = -80f;
        [SerializeField] private float maxThrowPitchDegrees = 80f;
        [Tooltip("Seconds an accepted release may stay unapplied by its owner before the server takes the item back.")]
        [SerializeField] private float releaseTimeoutSeconds = 1f;

        [Header("Aiming dot")]
        [SerializeField] private float dotDiameterPx = 4f;      // at 1080p; scaled with the screen height
        [SerializeField] private float dotOutlinePx = 1f;
        [SerializeField] private Color dotColor = Color.white;
        [SerializeField] private Color dotOutlineColor = new(0f, 0f, 0f, 0.8f);
        [SerializeField] private Color dotUsableColor = new(1f, 0.85f, 0.35f);

        public float BaseJumpHeight => baseJumpHeight;
        public float MinJumpHeightFactor => minJumpHeightFactor;
        public float CoyoteTime => coyoteTime;
        public float JumpBufferSeconds => jumpBufferSeconds;
        public float CapsuleRadius => capsuleRadius;
        public float StandingHeight => standingHeight;
        public float CrouchHeight => crouchHeight;
        public float StandingEyeHeight => standingEyeHeight;
        public float CrouchEyeHeight => crouchEyeHeight;
        public float StandingStepOffset => standingStepOffset;
        public float CrouchStepOffset => crouchStepOffset;
        public float CrouchBlendSeconds => crouchBlendSeconds;
        public float CrouchSpeedFactor => crouchSpeedFactor;
        public float StandRetrySeconds => standRetrySeconds;
        public float StandClearance => standClearance;
        public float DashMeters => dashMeters;
        public float DashSeconds => dashSeconds;
        public float DashLiftSpeed => dashLiftSpeed;
        public float DashCooldownSeconds => dashCooldownSeconds;
        public float ReleaseClearance => releaseClearance;
        public float ReleaseMaxForward => releaseMaxForward;
        public float DropGroundSearch => dropGroundSearch;
        public float DropSkin => dropSkin;
        public float MinThrowPitchDegrees => minThrowPitchDegrees;
        public float MaxThrowPitchDegrees => maxThrowPitchDegrees;
        public float ReleaseTimeoutSeconds => releaseTimeoutSeconds;
        public float DotDiameterPx => dotDiameterPx;
        public float DotOutlinePx => dotOutlinePx;
        public Color DotColor => dotColor;
        public Color DotOutlineColor => dotOutlineColor;
        public Color DotUsableColor => dotUsableColor;

        public bool IsValid =>
            Finite(baseJumpHeight) && baseJumpHeight > 0f &&
            minJumpHeightFactor > 0f && minJumpHeightFactor <= 1f &&
            coyoteTime >= 0f && jumpBufferSeconds >= 0f &&
            Finite(capsuleRadius) && capsuleRadius > 0f &&
            Finite(standingHeight) && Finite(crouchHeight) && crouchHeight < standingHeight &&
            crouchHeight >= 2f * capsuleRadius + 0.01f &&
            standingEyeHeight > 0f && standingEyeHeight < standingHeight &&
            crouchEyeHeight > 0f && crouchEyeHeight < crouchHeight &&
            standingStepOffset >= 0f && standingStepOffset < standingHeight &&
            crouchStepOffset >= 0f && crouchStepOffset < crouchHeight &&
            crouchBlendSeconds >= 0f && crouchSpeedFactor > 0f && crouchSpeedFactor <= 1f &&
            standRetrySeconds >= 0f && standClearance >= 0f &&
            Finite(dashMeters) && dashMeters > 0f && dashSeconds > 0f && dashCooldownSeconds >= dashSeconds && dashLiftSpeed >= 0f &&
            releaseClearance >= 0f && releaseMaxForward > 0f && dropGroundSearch >= 0f && dropSkin >= 0f &&
            minThrowPitchDegrees >= -89f && maxThrowPitchDegrees <= 89f && minThrowPitchDegrees <= maxThrowPitchDegrees &&
            releaseTimeoutSeconds > 0f && dotDiameterPx > 0f && dotOutlinePx >= 0f;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static PlayerMovementSettings fallback;

        // Code never dereferences a missing asset: the fallback carries the same
        // defaults as a freshly created one. The validator reports a missing asset.
        public static PlayerMovementSettings Resolve(PlayerMovementSettings settings)
        {
            if (settings != null && settings.IsValid) return settings;
            if (fallback == null)
            {
                fallback = CreateInstance<PlayerMovementSettings>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
}
