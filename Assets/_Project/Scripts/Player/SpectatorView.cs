using SunkCost.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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

        // The watched player may stand in the other world (a dead diver watching
        // the deck): the owner's camera then renders with that world's fog and
        // ambient (WorldLook) instead of the active scene's, for its own render only.
        private WorldLook.Snapshot? swappedLook;
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }
        private PlayerHeadSplit hiddenHead; // the target's head, out of its own eyes' picture (like ShipTV)
        private CameraClearFlags keptClear; private Color keptBackground; private bool clearSwapped;
        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (!Active || Target == null || rendering != cam) return;
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return;
            WorldId world = day.IsBelow(Target.OwnerId) ? WorldId.Dive : day.World; // the physical world, never the copy's Unity scene
            swappedLook = WorldLook.Begin(WorldScenes.Scene(world));
            // And that world's backdrop: the dark water colour below, the sky above -
            // the owner's own camera is set for the owner's world (PresentSky), which
            // is not the watched one when a dead diver watches the deck or the reverse.
            keptClear = cam.clearFlags; keptBackground = cam.backgroundColor; clearSwapped = true;
            if (world == WorldId.Dive) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor; }
            else cam.clearFlags = CameraClearFlags.Skybox;
            PlayerHeadSplit head = Target.HeadSplit;
            if (head != null && head.HeadShown) { head.SetHeadShown(false); hiddenHead = head; }
        }
        private void OnEndCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering != cam) return;
            WorldLook.Restore(swappedLook);
            swappedLook = null;
            if (clearSwapped) { cam.clearFlags = keptClear; cam.backgroundColor = keptBackground; clearSwapped = false; }
            if (hiddenHead != null) { hiddenHead.SetHeadShown(true); hiddenHead = null; }
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
                Target.EyePose(out Vector3 eye, out Quaternion look);
                cam.transform.SetPositionAndRotation(eye, look);
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
