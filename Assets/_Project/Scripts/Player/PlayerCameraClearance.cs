using UnityEngine;
using SunkCost.Interaction;

namespace SunkCost.Player
{
    // Keeps the owner's near clip plane out of walls
    // (docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md sections 3-4). The
    // desired eye is the stance-blended ViewPivot; the camera renders from a
    // corrected pose when a sphere around the near plane would overlap solid
    // geometry. The correction is a sweep from the safe point inside the movement
    // capsule (its top sphere centre) toward the desired eye, so the view can be
    // pushed back into the body but never through a wall. Inward moves are
    // immediate; the return is blended and checked. The player root, the server
    // pose and held items are never moved by this: it is presentation only.
    // When even the safe point is inside geometry (a door closing on the head, a
    // spawn inside a wall) the view is obstructed: the HUD covers the screen and
    // the controller drops its target until a clear pose exists again.
    [DisallowMultipleComponent]
    public sealed class PlayerCameraClearance : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform viewPivot;
        [SerializeField] private PlayerCameraSettings settings;

        private static readonly Collider[] Overlaps = new Collider[8];
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private CharacterController controller;
        private Vector3 offset;       // final eye - desired eye, world space
        private float returnSpan;     // the offset the current return blend started from
        private bool hasSolved;

        public PlayerCameraSettings Settings => PlayerCameraSettings.Resolve(settings);
        // True while no clear pose exists within the correction budget.
        public bool Obstructed { get; private set; }
        // World-space displacement applied to the camera this frame (zero when clear).
        public Vector3 Offset => offset;
        public float LastEnvelopeRadius { get; private set; }
        public Vector3 DesiredEye => viewPivot != null ? viewPivot.position : transform.position;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (playerCamera != null) playerCamera.nearClipPlane = Settings.NearClip;
        }

        // Radius of a sphere around the eye that contains the whole near plane
        // (its corners are the farthest points), plus the margin.
        public static float EnvelopeRadius(float near, float verticalFovDegrees, float aspect, float margin)
        {
            float halfHeight = near * Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * aspect;
            return Mathf.Sqrt(near * near + halfHeight * halfHeight + halfWidth * halfWidth) + margin;
        }

        // The safe point: the centre of the capsule's top sphere, in world space.
        public static Vector3 SafePoint(CharacterController controller)
        {
            float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
            return controller.transform.TransformPoint(controller.center + Vector3.up * half);
        }

        // Resolves a clear eye for this frame. Returns false when no clear pose
        // exists within maxCorrection of the desired eye (the caller shows a cover).
        // `safe` must lie inside the player's accepted body envelope. Fails closed:
        // a full query buffer counts as blocked.
        public static bool TryResolve(Vector3 safe, Vector3 desired, float radius, float maxCorrection, float skin, Transform ignoreSelf, out Vector3 eye)
        {
            eye = desired;
            if (IsClear(desired, radius, ignoreSelf)) return true;
            if (!IsClear(safe, radius, ignoreSelf)) { eye = safe; return false; }
            Vector3 delta = desired - safe;
            float distance = delta.magnitude;
            if (distance < 1e-4f) { eye = safe; return true; }
            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(safe, radius, direction, Hits, distance, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) { eye = safe; return false; }
            float travel = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.collider == null || (ignoreSelf != null && hit.collider.transform.IsChildOf(ignoreSelf))) continue;
                travel = Mathf.Min(travel, hit.distance);
            }
            travel = Mathf.Max(0f, travel - skin);
            eye = safe + direction * travel;
            if (Vector3.Distance(eye, desired) > maxCorrection) { return false; }
            // The sweep stops touching; the skin keeps it clear. Verify anyway.
            return IsClear(eye, radius, ignoreSelf);
        }

        public static bool IsClear(Vector3 center, float radius, Transform ignoreSelf)
        {
            int count = Physics.OverlapSphereNonAlloc(center, radius, Overlaps, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (hit == null) continue;
                if (ignoreSelf != null && hit.transform.IsChildOf(ignoreSelf)) continue;
                return false;
            }
            return true;
        }

        // Forget the last pose: a teleport, a world change, a travel unlock. The
        // next Solve starts from the new capsule with no blend from the old view.
        public void ResetView()
        {
            offset = Vector3.zero;
            returnSpan = 0f;
            hasSolved = false;
            Obstructed = false;
            Apply();
        }

        // Owner only, once per frame after movement and the stance blend and
        // before targeting, so the crosshair ray starts from the rendered eye.
        public void Solve()
        {
            if (playerCamera == null || viewPivot == null || controller == null) return;
            PlayerCameraSettings s = Settings;
            playerCamera.nearClipPlane = s.NearClip;
            float radius = EnvelopeRadius(playerCamera.nearClipPlane, playerCamera.fieldOfView, playerCamera.aspect, s.EnvelopeMargin);
            LastEnvelopeRadius = radius;
            Vector3 desired = viewPivot.position;
            Vector3 safe = SafePoint(controller);
            bool ok = TryResolve(safe, desired, radius, s.MaxCorrectionMeters, s.ContactSkin, transform, out Vector3 resolved);
            Obstructed = !ok;
            Vector3 target = ok ? resolved - desired : Vector3.zero;
            if (!ok || !hasSolved || target.sqrMagnitude >= offset.sqrMagnitude)
            {
                // Inward (or first) correction: immediate. Obstructed: the cover is
                // up; hold the safe point so nothing renders from inside a wall.
                offset = ok ? target : safe - desired;
                returnSpan = offset.magnitude;
            }
            else
            {
                // Returning toward the desired eye: a constant-speed blend over
                // returnBlendSeconds from where the return started (a step that
                // shrank with the remaining offset would never arrive), and the
                // blended pose must itself be clear or the resolved pose is used.
                float span = Mathf.Max(returnSpan, offset.magnitude, 1e-4f);
                float step = s.ReturnBlendSeconds <= 0f ? span : span * Time.unscaledDeltaTime / s.ReturnBlendSeconds;
                Vector3 candidate = Vector3.MoveTowards(offset, target, step);
                offset = IsClear(desired + candidate, radius, transform) ? candidate : target;
            }
            hasSolved = true;
            Apply();
        }

        private void Apply()
        {
            if (playerCamera == null || viewPivot == null) return;
            playerCamera.transform.position = viewPivot.position + offset;
        }
    }
}
