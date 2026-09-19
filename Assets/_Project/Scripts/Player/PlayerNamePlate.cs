using UnityEngine;

namespace SunkCost.Player
{
    // The name over a player's head (Dan, 19 September 2026: "a way to know who is
    // who — a name tag on him, it should look good"): a small sign plate in the
    // game's kit — ink plate, glowing frame lines, depth-tested white text — the
    // display name only, no colour (the colour is for something else). Seen
    // everywhere, at HQ, on the ship and below, by everyone but its owner; it
    // turns to face the viewer, keeps a readable size out to ReadableMeters and
    // shrinks away over the last FadeMeters; gone on the dead (the body says
    // whose it is) and when the viewer's eyes are at that head (spectating
    // through them). Built onto PrototypePlayer.prefab by PlayerNamePlateSetup;
    // the plate widens to the name.
    public sealed class PlayerNamePlate : MonoBehaviour
    {
        public const string PlateName = "NamePlate";
        public const float HeightMeters = 2.15f;      // over the 1.8 m capsule
        public const float ReadableMeters = 14f;      // constant size on screen this far
        public const float FadeMeters = 4f;           // then it shrinks to nothing
        private const float NearestMeters = 1.2f;     // the viewer's own eyes: hidden
        private const float ScaleAtMeters = 5f;       // 1× up to here, then grows with distance
        private const float MaxScale = 2.4f;
        private const float PadMeters = 0.24f;

        [SerializeField] private TextMesh text;
        [SerializeField] private Transform[] widthParts;   // the plate and its frame lines: scaled to the name
        [SerializeField] private float baseWidth = 1f;

        private HQPlayerController player;
        private PlayerIdentity identity;
        private string shown = string.Empty;
        private Renderer[] renderers;
        private MeshRenderer textRenderer;

        public void Configure(TextMesh textMesh, Transform[] parts, float width)
        {
            text = textMesh;
            widthParts = parts;
            baseWidth = width;
        }

        private void Awake()
        {
            player = GetComponentInParent<HQPlayerController>();
            identity = GetComponentInParent<PlayerIdentity>();
            renderers = GetComponentsInChildren<Renderer>(true);
            textRenderer = text != null ? text.GetComponent<MeshRenderer>() : null;
            Show(false);
        }

        private void LateUpdate()
        {
            if (player == null || identity == null) { Show(false); return; }
            if (player.IsOwner || player.IsDead) { Show(false); return; }
            Camera eyes = ViewerCamera();
            if (eyes == null) { Show(false); return; }
            Vector3 plate = player.transform.position + Vector3.up * HeightMeters;
            float distance = Vector3.Distance(eyes.transform.position, plate);
            if (distance < NearestMeters || distance > ReadableMeters + FadeMeters) { Show(false); return; }

            string name = identity.DisplayName;
            if (name != shown) Fit(name);

            transform.position = plate;
            Vector3 toViewer = eyes.transform.position - plate;
            toViewer.y = 0f;
            if (toViewer.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toViewer, Vector3.up); // the front (+Z) toward the viewer, upright
            float size = Mathf.Clamp(distance / ScaleAtMeters, 1f, MaxScale);
            float fade = distance <= ReadableMeters ? 1f : 1f - (distance - ReadableMeters) / FadeMeters;
            transform.localScale = Vector3.one * (size * Mathf.SmoothStep(0f, 1f, fade));
            Show(true);
        }

        // The name written, the plate widened to it.
        private void Fit(string name)
        {
            shown = name;
            if (text == null) return;
            text.text = name;
            float textWidth = textRenderer != null ? textRenderer.bounds.size.x / Mathf.Max(0.01f, transform.lossyScale.x) : baseWidth;
            float width = Mathf.Max(baseWidth, textWidth + PadMeters);
            if (widthParts == null) return;
            foreach (Transform part in widthParts)
            {
                if (part == null) continue;
                Vector3 s = part.localScale;
                part.localScale = new Vector3(width / baseWidth, s.y, s.z);
            }
        }

        private void Show(bool on)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers) if (r != null && r.enabled != on) r.enabled = on;
        }

        // Whose eyes render: the local player's camera (a spectator's is aimed
        // through another's head, which the nearest-metres rule hides), or the
        // main camera in the menu.
        private static Camera ViewerCamera()
        {
            HQPlayerController local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            Camera camera = local != null ? local.PlayerCamera : null;
            if (camera == null || !camera.isActiveAndEnabled) camera = Camera.main;
            return camera;
        }
    }
}
