using SunkCost.Player;
using SunkCost.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Monsters
{
    // Who sees the Impostor (docs/DESIGN.md §6): its chosen diver, the dead
    // spectating that diver, and the deck TV while it shows that diver — and
    // nobody else. Decided per camera render on every peer, the way
    // SpectatorView hides a head for one camera: the renderers and lights are
    // on only while a camera that may see it renders. It wears the replicated
    // colour (PlayerPalette) and the replicated name over its head, turned to
    // whichever camera looks. Presentation only; the server decides nothing here.
    public sealed class ImpostorLook : MonoBehaviour
    {
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private TextMesh nameText;
        [SerializeField] private TextMesh nameShadow;
        [SerializeField] private Transform namePlate;

        private Impostor impostor;
        private Renderer[] renderers;
        private Light[] lights;
        private bool shown;

        // The last render of the local player's own camera showed it (for the checks and the snapshot).
        public bool ShownToLocal { get; private set; }
        // It is seen by this peer's own eyes: the chosen diver, or a spectator of them.
        public bool AudibleToLocal
        {
            get
            {
                HQPlayerController local = WorldSceneFlow.LocalPlayer();
                return local != null && LocalMaySee(local);
            }
        }

        private void Awake()
        {
            impostor = GetComponent<Impostor>();
            renderers = GetComponentsInChildren<Renderer>(true);
            lights = GetComponentsInChildren<Light>(true);
            Show(false);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            if (impostor != null) impostor.Dressed += Dress;
            Dress();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (impostor != null) impostor.Dressed -= Dress;
        }

        private void Dress()
        {
            if (impostor == null) return;
            if (bodyRenderer != null) bodyRenderer.material.color = PlayerPalette.Get(impostor.WornColourIndex);
            string name = impostor.WornName;
            if (nameText != null) nameText.text = name;
            if (nameShadow != null) nameShadow.text = name;
        }

        private bool LocalMaySee(HQPlayerController local)
        {
            int target = impostor != null ? impostor.TargetId : -1;
            if (target < 0) return false;
            if (local.OwnerId == target) return true;
            CrewDayState day = CrewDayState.Instance;
            return local.IsDead && day != null && day.SpectateTargetOf(local.OwnerId) == target;
        }

        private bool MaySee(Camera camera)
        {
            int target = impostor != null ? impostor.TargetId : -1;
            if (target < 0 || camera == null) return false;
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            if (local != null && camera == local.PlayerCamera) return LocalMaySee(local);
            ShipTV tv = camera.GetComponentInParent<ShipTV>();
            if (tv != null) return tv.Channel == target;
            return false;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            bool visible = MaySee(rendering);
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            if (local != null && rendering == local.PlayerCamera) ShownToLocal = visible;
            if (!visible) { Show(false); return; }
            if (namePlate != null)
            {
                Vector3 toViewer = rendering.transform.position - namePlate.position; toViewer.y = 0f;
                if (toViewer.sqrMagnitude > 0.0001f) namePlate.rotation = Quaternion.LookRotation(toViewer, Vector3.up);
            }
            Show(true);
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (shown) Show(false);
        }

        private void Show(bool on)
        {
            shown = on;
            if (renderers != null) foreach (Renderer r in renderers) if (r != null && r.enabled != on) r.enabled = on;
            if (lights != null) foreach (Light l in lights) if (l != null && l.enabled != on) l.enabled = on;
        }
    }
}
