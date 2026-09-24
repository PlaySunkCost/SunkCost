using SunkCost.Look;
using UnityEngine;

namespace SunkCost.World
{
    // What the deck TV shows round the picture (ship audit SHIP-046, 23 September
    // 2026): the caption as a chip - "● LIVE — NAME" in red while it broadcasts,
    // a quiet "NO SIGNAL" otherwise - and, while nobody is below, an idle channel
    // instead of a dead field: the ship's name over faint moving static. Drawn in
    // the ship's screen style from one palette, so the editor and the game agree
    // (the caption's own colour was red in the editor and grey in the game). The
    // caption label keeps its text - ShipTV.Caption, what the checks and the peer
    // snapshot read - and is hidden behind the chip. The chip sits in the screen's
    // top band and the idle card is off while live, so the picture's middle - what
    // the deck check compares with the diver's render - is only the picture.
    public sealed class TvDisplay
    {
        public const string ChipName = "Tv Chip";
        public const string ChipTextName = "Tv Chip Text";
        public const string IdleName = "Tv Idle";
        private const float NoiseStep = 0.07f; // seconds between static frames

        private readonly TextMesh caption, chipText;
        private readonly Transform chip, idle;
        private readonly float chipLine;
        private Texture2D noise;
        private float nextNoise;

        private TvDisplay(TextMesh caption, TextMesh chipText, Transform chip, Transform idle, float chipLine)
        {
            this.caption = caption; this.chipText = chipText; this.chip = chip; this.idle = idle; this.chipLine = chipLine;
        }

        // Built by ShipScreens in the editor and found (or built) again by ShipTV.
        public static TvDisplay Ensure(ShipParts ship)
        {
            TextMesh caption = ship != null ? ship.TvCaption : null;
            Transform screen = ship != null ? ship.TvScreen : null;
            if (caption == null || screen == null) return null;
            Material textMaterial = ScreenStyle.TextMaterialOf(caption);
            Renderer own = caption.GetComponent<Renderer>();
            if (own != null) own.enabled = false;
            // The chip on the caption's plate root (its +Z faces the couch).
            Transform plate = caption.transform.parent;
            float line = caption.characterSize * 10f;
            Transform chipRoot = ScreenStyle.Child(plate, ChipName, out _);
            chipRoot.localPosition = caption.transform.localPosition;
            chipRoot.localRotation = Quaternion.identity;
            ScreenStyle.Quad(chipRoot, "Fill", Vector3.zero, 1f, line * 0.9f, ScreenStyle.Track);
            TextMesh chipText = ScreenStyle.Line(chipRoot, ChipTextName, new Vector3(0f, 0f, 0.004f), line * 0.8f, ScreenStyle.Dim, TextAnchor.MiddleCenter, textMaterial);
            // The idle card on the screen itself: its reader looks at the quad's -Z side.
            Transform idleRoot = ship.Find(IdleName); // anywhere on the ship (the hierarchy may group it), made beside the screen
            if (idleRoot == null) idleRoot = ScreenStyle.Child(screen.parent, IdleName, out _);
            idleRoot.SetPositionAndRotation(screen.position - screen.forward * 0.006f, screen.rotation * Quaternion.Euler(0f, 180f, 0f));
            idleRoot.localScale = Vector3.one;
            Vector3 size = screen.lossyScale;
            float h = Mathf.Abs(size.y), w = Mathf.Abs(size.x);
            // Sized to read from the couch (fix round 2: the hint was about 7 px tall there at 1080p).
            TextMesh name = ScreenStyle.Line(idleRoot, "Idle Name", new Vector3(0f, h * 0.08f, 0f), h * 0.3f, Color.Lerp(ScreenStyle.Back, ScreenStyle.Accent, 0.55f), TextAnchor.MiddleCenter, textMaterial);
            name.text = HQSigns.Resolve().Get("company");
            TextFit.Fit(name, new Vector2(w * 0.8f, h * 0.32f), h * 0.03f);
            TextMesh sub = ScreenStyle.Line(idleRoot, "Idle Sub", new Vector3(0f, -h * 0.12f, 0f), h * 0.11f, ScreenStyle.Dim, TextAnchor.MiddleCenter, textMaterial);
            sub.text = HQSigns.Resolve().Get("ship.tv.idle");
            TextFit.Fit(sub, new Vector2(w * 0.8f, h * 0.14f), h * 0.011f);
            TextMesh hint = ScreenStyle.Line(idleRoot, "Idle Hint", new Vector3(0f, -h * 0.27f, 0f), h * 0.09f, Color.Lerp(ScreenStyle.Back, ScreenStyle.Dim, 0.75f), TextAnchor.MiddleCenter, textMaterial);
            hint.text = HQSigns.Resolve().Get("ship.tv.idle.hint");
            TextFit.Fit(hint, new Vector2(w * 0.8f, h * 0.11f), h * 0.009f);
            var display = new TvDisplay(caption, chipText, chipRoot, idleRoot, line);
            display.ShowIdleCard();
            return display;
        }

        public void ShowLive(string name)
        {
            SetChip("● LIVE — " + name.ToUpperInvariant(), ScreenStyle.Danger, Color.white);
            if (idle != null) idle.gameObject.SetActive(false);
        }

        public void ShowIdleCard()
        {
            SetChip("NO SIGNAL", ScreenStyle.Track, ScreenStyle.Dim);
            if (idle != null) idle.gameObject.SetActive(true);
        }

        private void SetChip(string text, Color fill, Color ink)
        {
            if (chipText == null) return;
            chipText.text = text;
            chipText.color = ink;
            Renderer back = chip.Find("Fill")?.GetComponent<Renderer>();
            if (back == null) return;
            ScreenStyle.Paint(back, fill);
            // The chip hugs its words: their measured width and a little air.
            float w = chipText.GetComponent<MeshRenderer>().localBounds.size.x + chipLine * 0.9f;
            back.transform.localScale = new Vector3(w, chipLine * 0.9f, 1f);
        }

        // The idle channel's static: a small grey noise tile, stepped a few times a
        // second, dark enough to read as a quiet screen. Play only (a runtime texture).
        public void AnimateIdle(Material screen)
        {
            if (screen == null || Time.unscaledTime < nextNoise) return;
            nextNoise = Time.unscaledTime + NoiseStep;
            if (noise == null)
            {
                noise = new Texture2D(96, 54, TextureFormat.RGB24, false) { name = "TvStatic", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
                var random = new System.Random(7);
                byte[] pixels = new byte[96 * 54 * 3];
                for (int i = 0; i < pixels.Length; i += 3) pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)random.Next(0, 256); // grey
                noise.SetPixelData(pixels, 0);
                noise.Apply();
            }
            if (screen.mainTexture != noise) screen.mainTexture = noise;
            screen.color = Color.Lerp(ScreenStyle.Back, ScreenStyle.Dim, 0.16f); // on average barely above the ship's screen glass
            screen.mainTextureOffset = new Vector2(Random.value, Random.value);
        }

        // The live picture back: the feed's texture, untinted, unshifted.
        public static void ShowPicture(Material screen, Texture picture)
        {
            if (screen == null) return;
            screen.mainTexture = picture;
            screen.mainTextureOffset = Vector2.zero;
            screen.mainTextureScale = Vector2.one;
            screen.color = Color.white;
        }

        public void Release()
        {
            if (noise != null) Object.Destroy(noise);
        }
    }
}
