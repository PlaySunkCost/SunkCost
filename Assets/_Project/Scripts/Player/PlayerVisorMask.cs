using UnityEngine;

namespace SunkCost.Player
{
    // The diving mask you look through (docs/VISOR_IMPLEMENTATION_PLAN.md section
    // 3.5, Dan: "not all the vision is open on the screen, some of it blocked
    // because of the binoculars"): one wide lens with rounded corners, a nose
    // bridge rising from the bottom centre, a thin dark matte rim with a faint
    // cyan line where the glass meets the frame. Pure: a signed distance in screen
    // pixels (negative on the glass, positive on the frame), the readouts' places
    // on the glass, and the frame baked to a texture. PlayerHudUI draws it.
    public static class PlayerVisorMask
    {
        // The frame's proportions: the side rim as a fraction of the width, the rest
        // of the height, so the lens keeps its look at 16:9 and 21:9.
        public const float SideRimFrac = 0.05f;
        public const float TopRimFrac = 0.06f;
        public const float BottomRimFrac = 0.06f;
        public const float CornerRadiusFrac = 0.20f;
        public const float NoseHalfWidthFrac = 0.11f;   // the bridge's half-width at its base
        public const float NoseHeightFrac = 0.10f;      // how far the bridge rises above the bottom rim
        public const float NoseBlendFrac = 0.035f;      // the fillet where the bridge meets the rim

        // Looks (all alphas straight, composited in Bake).
        private static readonly Color RimColor = new(0.02f, 0.03f, 0.04f, 0.94f);
        private static readonly Color GlowColor = new(0.35f, 0.9f, 1f, 1f);
        private const float GlowLineAlpha = 0.75f, GlowLineSigmaPx = 1.6f, GlowLineOffsetPx = 1f;
        private const float GlowBloomAlpha = 0.18f, GlowBloomSigmaPx = 9f;
        private const float ShadowAlpha = 0.30f, ShadowWidthPx = 60f;   // the rim's shadow on the glass

        public static float SideRimPx(float width) => width * SideRimFrac;
        public static float TopRimPx(float height) => height * TopRimFrac;
        public static float BottomRimPx(float height) => height * BottomRimFrac;
        public static float NoseHeightPx(float height) => height * NoseHeightFrac;

        // Signed distance from the lens edge at a GUI-space point (y down), in
        // pixels: negative on the glass, positive on the frame.
        public static float DistancePx(float x, float y, float width, float height)
        {
            float px = x - width * 0.5f, py = y - height * 0.5f;
            float bx = width * 0.5f - SideRimPx(width);
            float by = height * 0.5f - (TopRimPx(height) + BottomRimPx(height)) * 0.5f;
            py -= (TopRimPx(height) - BottomRimPx(height)) * 0.5f;   // centre the lens between the rims
            float r = Mathf.Min(height * CornerRadiusFrac, bx, by);
            float box = RoundBox(px, py, bx, by, r);

            // The nose bridge: an ellipse centred on the bottom edge, taken out of the box.
            float a = height * NoseHalfWidthFrac, b = NoseHeightPx(height);
            float qx = px / a, qy = (py - by) / b;
            float nose = (Mathf.Sqrt(qx * qx + qy * qy) - 1f) * Mathf.Min(a, b);
            return SmoothMax(box, -nose, height * NoseBlendFrac);
        }

        private static float RoundBox(float px, float py, float bx, float by, float r)
        {
            float qx = Mathf.Abs(px) - bx + r, qy = Mathf.Abs(py) - by + r;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        // max(a, b) with a rounded junction of width k.
        private static float SmoothMax(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        // ---- where the readouts sit on the glass ---------------------------------------

        // The AIR / HP / DEPTH block, bottom-left inside the rounded corner.
        public static Rect VitalsRect(float width, float height)
        {
            float s = height / 1080f;
            float left = SideRimPx(width) + 0.09f * height;
            float bottom = height - BottomRimPx(height) - 0.07f * height;
            float rows = 3f * 30f * s;
            return new Rect(left, bottom - rows - 2f * s, (44f + 220f + 8f + 50f) * s, rows + 6f * s);
        }

        // The compass strip and the two lines under it, top-centre under the top rim.
        public static Rect CompassRect(float width, float height, float stripWidthPx)
        {
            float s = height / 1080f;
            float top = TopRimPx(height) + 0.02f * height;
            return new Rect(width * 0.5f - stripWidthPx * 0.5f, top - 6f * s, stripWidthPx, 6f * s + 26f * s + 36f * s);
        }

        // The slot row with its weight meter, bottom-centre just above the nose bridge.
        public static Rect SlotRowRect(float width, float height, float rowWidth, float rowHeight)
        {
            float bottom = height - BottomRimPx(height) - NoseHeightPx(height) - 12f * (height / 1080f);
            return new Rect(width * 0.5f - rowWidth * 0.5f, bottom - rowHeight, rowWidth, rowHeight);
        }

        // Whether a GUI rectangle lies wholly on the glass with `marginPx` to spare.
        public static bool OnGlass(Rect rect, float width, float height, float marginPx)
        {
            return DistancePx(rect.xMin, rect.yMin, width, height) < -marginPx && DistancePx(rect.xMax, rect.yMin, width, height) < -marginPx
                && DistancePx(rect.xMin, rect.yMax, width, height) < -marginPx && DistancePx(rect.xMax, rect.yMax, width, height) < -marginPx;
        }

        // ---- the frame as a texture -----------------------------------------------------

        // The frame for a screen of `screenWidth` × `screenHeight`, baked at
        // `textureWidth` × `textureHeight` (half the screen is plenty: the edge is
        // soft anyway) and stretched over the screen by the caller. Straight alpha:
        // the rim's shadow on the glass, the rim, the glow line on top.
        public static Texture2D Bake(int screenWidth, int screenHeight, int textureWidth, int textureHeight, bool readable = false)
        {
            var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[textureWidth * textureHeight];
            float s = screenHeight / 1080f;
            float sx = (float)screenWidth / textureWidth, sy = (float)screenHeight / textureHeight;
            for (int ty = 0; ty < textureHeight; ty++)
            {
                // Texture rows run bottom-up; GUI space runs top-down.
                float y = (textureHeight - 1 - ty + 0.5f) * sy;
                for (int tx = 0; tx < textureWidth; tx++)
                {
                    float x = (tx + 0.5f) * sx;
                    float d = DistancePx(x, y, screenWidth, screenHeight);
                    Color c = Color.clear;
                    if (d < 0f) c = Over(c, new Color(0f, 0f, 0f, ShadowAlpha * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-ShadowWidthPx * s, 0f, d))));
                    c = Over(c, new Color(RimColor.r, RimColor.g, RimColor.b, RimColor.a * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-sy, sy, d))));
                    float line = (d - GlowLineOffsetPx * s) / (GlowLineSigmaPx * s);
                    float bloom = d / (GlowBloomSigmaPx * s);
                    float glow = GlowLineAlpha * Mathf.Exp(-line * line) + GlowBloomAlpha * Mathf.Exp(-bloom * bloom);
                    c = Over(c, new Color(GlowColor.r, GlowColor.g, GlowColor.b, Mathf.Clamp01(glow)));
                    pixels[ty * textureWidth + tx] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, !readable);
            return texture;
        }

        // Straight-alpha "over" compositing.
        private static Color Over(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0f) return Color.clear;
            return new Color(
                (over.r * over.a + under.r * under.a * (1f - over.a)) / a,
                (over.g * over.a + under.g * under.a * (1f - over.a)) / a,
                (over.b * over.a + under.b * under.a * (1f - over.a)) / a,
                a);
        }
    }
}
