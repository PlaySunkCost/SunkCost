using System.IO;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The deck TV (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 3, design §4): one
    // diver at a time, chosen by the server (CrewDayState.TvChannel — the first
    // living diver below while someone stands on the deck, E on the screen for
    // the next), shown from that diver's eyes: a camera this component owns
    // renders their view, and the diver's visor is drawn over it by the local
    // player's PlayerHudUI — the same drawing path as the owner's own screen and a
    // spectator's, so a change to the visor's look reaches the TV untouched
    // (docs/CONVENTIONS.md, "one path for anything shown twice"). "LIVE · name"
    // over the screen, or "NO SIGNAL". Local presentation on the ship root: it
    // reads the channel and the diver's replicated pitch and yaw, decides
    // nothing. The diver's object is on this client because the server loads
    // the dive world for ship clients while a channel exists (WorldSceneFlow.Watch).
    [DefaultExecutionOrder(500)] // after the divers' NetworkTransforms have moved
    public sealed class ShipTV : MonoBehaviour
    {
        private static readonly Color DarkScreen = new(0.02f, 0.03f, 0.04f);

        private ShipParts ship;
        private Renderer screen;
        private Material material;
        private TextMesh caption;
        // The camera draws into `picture`; each frame the picture is copied to
        // `shown` and the visor drawn over it, and the screen quad shows `shown`:
        // the quad never sees a half-drawn frame. Both are screen-sized so the
        // visor's screen-space layout and projections map one to one.
        private RenderTexture picture, shown;
        private Camera cam;
        private HQPlayerController diver;
        private Transform speaker;
        private PlayerHudUI hud;
        private readonly PlayerHudUI.VisorFrame frame = new();

        // What the screen shows this frame (for the checks and the peer snapshot).
        public int Channel { get; private set; } = -1;
        public bool Live => diver != null;
        public string Caption { get; private set; } = string.Empty;
        public HQPlayerController Diver => diver;
        public Vector3 SpeakerPosition => speaker != null ? speaker.position : transform.position;
        public PlayerHudUI.VisorFrame Frame => frame;

        private void Awake()
        {
            ship = GetComponent<ShipParts>();
            Transform quad = ship != null ? ship.TvScreen : null;
            screen = quad != null ? quad.GetComponent<Renderer>() : null;
            caption = ship != null ? ship.TvCaption : null;
            speaker = ship != null ? ship.TvSpeaker : null;
            if (screen == null) return;
            material = screen.material; // an instance: the shared asset keeps its colour
            GameObject go = new("TvCamera");
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.enabled = false;
            EnsureTextures();
            ShowNoSignal();
        }

        private void EnsureTextures()
        {
            int w = Mathf.Max(Screen.width, 2), h = Mathf.Max(Screen.height, 2);
            if (picture != null && picture.width == w && picture.height == h) return;
            ReleaseTextures();
            picture = new RenderTexture(w, h, 16) { name = "TvPicture" };
            shown = new RenderTexture(w, h, 0) { name = "TvShown" };
            if (cam != null) cam.targetTexture = picture;
            if (material != null && diver != null) material.mainTexture = shown;
        }

        private void ReleaseTextures()
        {
            if (picture != null) { picture.Release(); Destroy(picture); picture = null; }
            if (shown != null) { shown.Release(); Destroy(shown); shown = null; }
        }

        private void OnDestroy()
        {
            ReleaseTextures();
            if (material != null) Destroy(material);
        }

        private void LateUpdate()
        {
            EnsureTextures();
            CrewDayState day = CrewDayState.Instance;
            int channel = day != null ? day.TvChannel : -1;
            Channel = channel;
            HQPlayerController next = channel >= 0 ? FindDiver(channel) : null;
            if (next != diver)
            {
                diver = next;
                if (diver == null) ShowNoSignal();
                else ShowLive(diver);
            }
            if (diver == null) return;
            Transform eye = diver.EyeAnchor;
            cam.transform.SetPositionAndRotation(eye.position, Quaternion.Euler(diver.LookPitch, diver.Yaw, 0f));
            if (hud == null) { HQPlayerController local = WorldSceneFlow.LocalPlayer(); hud = local != null ? local.GetComponent<PlayerHudUI>() : null; }
            if (hud != null) hud.Compute(frame, diver, diver.Inventory, cam, watching: true);
        }

        // The visor over the picture, drawn into the shown texture by the local
        // HUD's own drawing path (IMGUI draws into RenderTexture.active on repaint).
        private void OnGUI()
        {
            if (diver == null || hud == null || shown == null || Event.current.type != EventType.Repaint) return;
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(picture, shown);
            RenderTexture.active = shown;
            hud.DrawVisor(frame, diver.Inventory, maskOn: frame.Readout.On, readoutsOn: frame.Readout.On && !diver.TravelLocked, onAir: true);
            RenderTexture.active = previous;
        }

        private static HQPlayerController FindDiver(int clientId)
        {
            foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (player.IsSpawned && player.OwnerId == clientId && !player.IsDead) return player;
            return null;
        }

        private void ShowLive(HQPlayerController who)
        {
            PlayerIdentity identity = who.GetComponent<PlayerIdentity>();
            Caption = "LIVE · " + (identity != null ? identity.DisplayName : PlayerIdentity.Fallback(who.OwnerId));
            if (caption != null) { caption.text = Caption; caption.color = new Color(1f, 0.35f, 0.3f); }
            if (material != null) { material.mainTexture = shown; material.color = Color.white; }
            if (cam != null) cam.enabled = true;
        }

        private void ShowNoSignal()
        {
            Caption = "NO SIGNAL";
            if (caption != null) { caption.text = Caption; caption.color = new Color(0.7f, 0.7f, 0.7f); }
            if (material != null) { material.mainTexture = null; material.color = DarkScreen; }
            if (cam != null) cam.enabled = false;
        }

        // For the checks: the shown picture as a PNG, and how much is in it (the
        // standard deviation of its brightness: a dark or blank screen reads ~0).
        public float SavePicture(string path)
        {
            if (shown == null) return 0f;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = shown;
            var tex = new Texture2D(shown.width, shown.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, shown.width, shown.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = tex.GetPixels32();
            double sum = 0, sumSq = 0;
            for (int i = 0; i < pixels.Length; i += 7)
            {
                float v = (pixels[i].r + pixels[i].g + pixels[i].b) / (3f * 255f);
                sum += v; sumSq += v * v;
            }
            int n = (pixels.Length + 6) / 7;
            double mean = sum / n;
            float deviation = (float)System.Math.Sqrt(System.Math.Max(0, sumSq / n - mean * mean));
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            Destroy(tex);
            return deviation;
        }
    }
}
