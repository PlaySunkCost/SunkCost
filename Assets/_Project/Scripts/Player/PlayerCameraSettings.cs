using UnityEngine;

namespace SunkCost.Player
{
    // First-person camera clip and wall-clearance tuning
    // (docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md section 3). Provisional
    // numbers; changing them is not a design change.
    [CreateAssetMenu(fileName = "PlayerCameraSettings", menuName = "Sunk Cost/Player Camera Settings")]
    public sealed class PlayerCameraSettings : ScriptableObject
    {
        [Header("Clip planes")]
        [Tooltip("Near clip plane in metres. Small enough that the near-plane corners stay inside the movement capsule at a wall or in a corner.")]
        [SerializeField] private float nearClip = 0.05f;

        [Header("Clearance")]
        [Tooltip("Extra metres around the near-plane sphere that must be free of solid geometry.")]
        [SerializeField] private float envelopeMargin = 0.01f;
        [Tooltip("Farthest the view may be moved from the desired eye to clear geometry, metres. Beyond it the obstruction cover shows instead. Normal wall contact needs none; the crouch blend needs up to the eye span.")]
        [SerializeField] private float maxCorrectionMeters = 1.0f;
        [Tooltip("Seconds the view takes to return to the desired eye once it is clear. Corrections inward are immediate.")]
        [SerializeField] private float returnBlendSeconds = 0.10f;
        [Tooltip("Gap kept between the resolved envelope and the geometry it stopped at, metres.")]
        [SerializeField] private float contactSkin = 0.005f;

        public float NearClip => nearClip;
        public float EnvelopeMargin => envelopeMargin;
        public float MaxCorrectionMeters => maxCorrectionMeters;
        public float ReturnBlendSeconds => returnBlendSeconds;
        public float ContactSkin => contactSkin;

        public bool IsValid =>
            Finite(nearClip) && nearClip > 0f && nearClip < 0.3f &&
            Finite(envelopeMargin) && envelopeMargin >= 0f &&
            Finite(maxCorrectionMeters) && maxCorrectionMeters > 0f &&
            Finite(returnBlendSeconds) && returnBlendSeconds >= 0f &&
            Finite(contactSkin) && contactSkin >= 0f;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static PlayerCameraSettings fallback;

        // Code never dereferences a missing asset: the fallback carries the same
        // defaults as a freshly created one. The validator reports a missing asset.
        public static PlayerCameraSettings Resolve(PlayerCameraSettings settings)
        {
            if (settings != null && settings.IsValid) return settings;
            if (fallback == null)
            {
                fallback = CreateInstance<PlayerCameraSettings>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
}
