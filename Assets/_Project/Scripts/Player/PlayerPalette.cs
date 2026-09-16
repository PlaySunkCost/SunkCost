using UnityEngine;

namespace SunkCost.Player
{
    // The fixed swatches a player can be (Dan, 16 September 2026: a wheel of
    // fixed colours, click to choose; default random; the pick is saved). Sixteen
    // that stay apart from each other and read in the dark: fifteen hues round
    // the wheel at full value plus white. A player is an index into this list,
    // never a free colour, so the wire carries one byte and the lamp card can
    // tint from the same table later.
    public static class PlayerPalette
    {
        public static readonly Color[] Colors =
        {
            new(1.00f, 0.25f, 0.25f), // red
            new(1.00f, 0.50f, 0.15f), // orange
            new(1.00f, 0.78f, 0.10f), // amber
            new(0.95f, 0.95f, 0.20f), // yellow
            new(0.65f, 0.95f, 0.20f), // lime
            new(0.25f, 0.90f, 0.30f), // green
            new(0.20f, 0.95f, 0.70f), // mint
            new(0.20f, 0.95f, 0.95f), // cyan
            new(0.30f, 0.70f, 1.00f), // sky
            new(0.30f, 0.40f, 1.00f), // blue
            new(0.55f, 0.35f, 1.00f), // indigo
            new(0.80f, 0.35f, 1.00f), // violet
            new(1.00f, 0.35f, 0.90f), // magenta
            new(1.00f, 0.45f, 0.65f), // pink
            new(1.00f, 0.65f, 0.55f), // coral
            new(0.95f, 0.95f, 0.95f), // white
        };

        public static int Count => Colors.Length;
        public static bool IsValid(int index) => index >= 0 && index < Colors.Length;
        public static Color Get(int index) => IsValid(index) ? Colors[index] : Colors[0];
        public static string Hex(int index) => "#" + ColorUtility.ToHtmlStringRGB(Get(index));

        // Where swatch `index` sits on the wheel: unit circle, 0 at the top,
        // clockwise. The panel plate, the picker and the checks agree through this.
        public static Vector2 WheelPosition(int index)
        {
            float angle = (index / (float)Colors.Length) * Mathf.PI * 2f;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
        }

        // The swatch under a point on the wheel (unit circle coordinates, y up), or
        // -1 outside the ring band. The band is radii 0.55 .. 1.05; each swatch owns
        // its sector.
        public static int SwatchAt(Vector2 unit)
        {
            float r = unit.magnitude;
            if (r < 0.55f || r > 1.05f) return -1;
            float angle = Mathf.Atan2(unit.x, unit.y); // 0 at the top, clockwise positive
            if (angle < 0f) angle += Mathf.PI * 2f;
            int index = Mathf.RoundToInt(angle / (Mathf.PI * 2f) * Colors.Length) % Colors.Length;
            return index;
        }

        // The wheel as a texture: the swatches as discs round a ring on a clear
        // background (the panel plate and the picker draw it).
        public static Texture2D BakeWheel(int size, bool readable = false)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            float half = size * 0.5f, ring = size * 0.38f, disc = size * 0.075f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new(x + 0.5f - half, y + 0.5f - half); // y up in texture space
                    Color c = Color.clear;
                    for (int i = 0; i < Colors.Length; i++)
                    {
                        Vector2 centre = WheelPosition(i) * ring;
                        float d = Vector2.Distance(p, centre);
                        if (d <= disc) { c = Colors[i]; c.a = 1f; break; }
                        if (d <= disc + 1.5f) { c = Color.Lerp(c, Colors[i], 1f - (d - disc) / 1.5f); c.a = Mathf.Max(c.a, 1f - (d - disc) / 1.5f); }
                    }
                    pixels[y * size + x] = c;
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, !readable);
            return texture;
        }
    }

    // The saved pick on this machine: a random swatch the first time, kept after.
    public static class PlayerColourPrefs
    {
        public const string Key = "sunkcost.playerColour";

        public static int Load()
        {
            int saved = PlayerPrefs.GetInt(Key, -1);
            if (PlayerPalette.IsValid(saved)) return saved;
            int random = Random.Range(0, PlayerPalette.Count);
            Save(random);
            return random;
        }

        public static void Save(int index)
        {
            PlayerPrefs.SetInt(Key, index);
            PlayerPrefs.Save();
        }

        public static bool HasSaved => PlayerPalette.IsValid(PlayerPrefs.GetInt(Key, -1));
    }
}
