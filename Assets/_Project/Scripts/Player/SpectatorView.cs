using SunkCost.World;
using UnityEngine;

namespace SunkCost.Player
{
    // The dead owner's eyes (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 2, design
    // §4): about a second of its own camera (you see yourself fall), a short fade,
    // then the living player the server chose — the camera sits on that player's
    // eye anchor with their replicated pitch and yaw, every frame, until End day.
    // Nobody living left: the camera hangs over the body (NO SIGNAL). Local
    // presentation only; the target comes from CrewDayState (server-written) and
    // left click asks the server for the next one (HQPlayerController). Added to
    // the owner's player object at OnStartClient.
    [DefaultExecutionOrder(500)] // after the targets' NetworkTransforms have moved
    public sealed class SpectatorView : MonoBehaviour
    {
        public const float DeathCameraSeconds = 1f;
        public const float FadeSeconds = 0.3f;
        private const float OverheadMeters = 3f;

        private HQPlayerController controller;
        private Camera cam;
        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private float deadAt = -1f;
        private bool fading;

        // Watching (the camera is on someone else's eyes, or over the body).
        public bool Active { get; private set; }
        // The living player watched, or null (NO SIGNAL, or the target's object is
        // not on this client yet).
        public HQPlayerController Target { get; private set; }
        public int TargetId => controller != null && CrewDayState.Instance != null ? CrewDayState.Instance.SpectateTargetOf(controller.OwnerId) : -1;
        public string TargetName
        {
            get
            {
                if (Target == null) return string.Empty;
                PlayerIdentity identity = Target.GetComponent<PlayerIdentity>();
                return identity != null ? identity.DisplayName : PlayerIdentity.Fallback(Target.OwnerId);
            }
        }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
        }

        private void Update()
        {
            if (controller == null || !controller.IsOwner) return;
            cam = controller.PlayerCamera;
            bool dead = controller.IsDead;
            if (!dead)
            {
                deadAt = -1f;
                fading = false;
                if (Active) End();
                return;
            }
            if (!Active)
            {
                if (deadAt < 0f) deadAt = Time.unscaledTime;
                ScreenFade fade = ScreenFade.Instance;
                if (fade == null) { Begin(); return; }
                if (!fading && Time.unscaledTime - deadAt >= DeathCameraSeconds) { fading = true; fade.FadeOut(FadeSeconds, string.Empty); }
                if (fading && fade.IsBlack) { Begin(); fade.FadeIn(FadeSeconds); }
                return;
            }
            ResolveTarget();
        }

        private void LateUpdate()
        {
            if (!Active || cam == null) return;
            if (Target != null)
            {
                Transform eye = Target.EyeAnchor;
                cam.transform.SetPositionAndRotation(eye.position, Quaternion.Euler(Target.LookPitch, Target.Yaw, 0f));
            }
            else
            {
                // NO SIGNAL: over the body, looking down at where it lies.
                Vector3 over = transform.position + Vector3.up * OverheadMeters - transform.forward * 1.5f;
                cam.transform.SetPositionAndRotation(over, Quaternion.LookRotation((transform.position + Vector3.up * 0.5f - over).normalized, Vector3.up));
            }
        }

        private void Begin()
        {
            if (Active || cam == null) return;
            restLocalPosition = cam.transform.localPosition;
            restLocalRotation = cam.transform.localRotation;
            Active = true;
            ResolveTarget();
        }

        private void End()
        {
            Active = false;
            Target = null;
            if (cam != null)
            {
                cam.transform.localPosition = restLocalPosition;
                cam.transform.localRotation = restLocalRotation;
            }
            controller.CameraClearance?.ResetView();
        }

        // The server's choice, as an object this client can see.
        private void ResolveTarget()
        {
            int id = TargetId;
            Target = null;
            if (id < 0) return;
            foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (player.IsSpawned && player.OwnerId == id && !player.IsDead) { Target = player; return; }
        }
    }
}
