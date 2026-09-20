using UnityEngine;

namespace SunkCost.Player
{
    // The name over a player's head (Dan, 19–20 September 2026: "a way to know
    // who is who — a name tag on him, it should look good"; after his reference:
    // plain floating text, no plate): the display name in soft white with a dark
    // drop shadow, depth-tested, no colour (the colour is for something else).
    // Seen everywhere, at HQ, on the ship and below, by everyone but its owner;
    // it turns to face the viewer, keeps a readable size out to ReadableMeters
    // and fades out over the last FadeMeters; gone on the dead (the body says
    // whose it is) and when the viewer's eyes are at that head (spectating
    // through them). Built onto PrototypePlayer.prefab by PlayerNamePlateSetup.
    public sealed class PlayerNamePlate : MonoBehaviour
    {
        public const string PlateName = "NamePlate";
        public const float HeightMeters = 2.2f;       // a hand's breadth over the 1.8 m capsule
        public const float ReadableMeters = 14f;      // constant size on screen this far
        public const float FadeMeters = 4f;           // then it fades out
        private const float NearestMeters = 1.2f;     // the viewer's own eyes: hidden
        private const float ScaleAtMeters = 5f;       // 1× up to here, then grows with distance
        private const float MaxScale = 2.4f;

        [SerializeField] private TextMesh text;
        [SerializeField] private TextMesh shadow;
        [SerializeField] private Color textColour = new(0.96f, 0.96f, 0.96f, 0.95f);
        [SerializeField] private Color shadowColour = new(0f, 0f, 0f, 0.7f);

        private HQPlayerController player;
        private PlayerIdentity identity;
        private string shown = string.Empty;
        private Renderer[] renderers;

        public void Configure(TextMesh textMesh, TextMesh shadowMesh)
        {
            text = textMesh;
            shadow = shadowMesh;
        }

        private void Awake()
        {
            player = GetComponentInParent<HQPlayerController>();
            identity = GetComponentInParent<PlayerIdentity>();
            renderers = GetComponentsInChildren<Renderer>(true);
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
            float fade = Mathf.SmoothStep(0f, 1f, distance <= ReadableMeters ? 1f : 1f - (distance - ReadableMeters) / FadeMeters);
            transform.localScale = Vector3.one * size;
            if (text != null) text.color = new Color(textColour.r, textColour.g, textColour.b, textColour.a * fade);
            if (shadow != null) shadow.color = new Color(shadowColour.r, shadowColour.g, shadowColour.b, shadowColour.a * fade);
            Show(true);
        }

        // The name written on the text and its shadow.
        private void Fit(string name)
        {
            shown = name;
            if (text != null) text.text = name;
            if (shadow != null) shadow.text = name;
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
