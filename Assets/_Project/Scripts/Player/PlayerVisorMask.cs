using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Player
{
    // The helmet visor you look through (docs/VISOR_IMPLEMENTATION_PLAN.md section
    // 3.5; Dan's reference picture, 16 September 2026): one wide lens with
    // chamfered corners, a compass housing let into the top edge, a small nose
    // notch in the bottom edge, a dark rim edged by a bright cyan line with a
    // second, fainter line a little way out on the rim. Pure: the lens is a
    // polygon in screen pixels, its signed distance (negative on the glass) is
    // exact along every edge, and the frame is baked once into a texture that
    // PlayerHudUI stretches over the screen. The readouts' places are decided
    // here too, so the checks can pin them.
    public static class PlayerVisorMask
    {
        // Proportions (fractions of the height unless noted) — the lens keeps its
        // look at 16:9 and 21:9 because the rims scale with the height.
        public const float SideRimFrac = 0.045f;        // of the width
        public const float TopRimFrac = 0.065f;
        public const float BottomRimFrac = 0.075f;
        public const float ChamferFrac = 0.10f;         // the cut corners
        public const float HousingDepthFrac = 0.045f;   // the compass housing, below the top rim
        public const float HousingHalfWidthFrac = 0.15f; // of the width, at the housing's floor
        public const float HousingChamferFrac = 0.035f; // of the width
        public const float NoseDepthFrac = 0.035f;      // the notch in the bottom edge
        public const float NoseHalfWidthFrac = 0.05f;   // of the width, at the rim
        public const float NoseChamferFrac = 0.02f;     // of the width

        // Looks (straight alphas, composited in Bake).
        private static readonly Color RimColor = new(0.015f, 0.04f, 0.05f, 0.95f);
        private static readonly Color LineColor = new(0.30f, 0.92f, 1f, 1f);
        private const float LineAlpha = 0.95f, LineSigmaPx = 1.4f, LineOffsetPx = 1.5f;
        private const float SecondLineAlpha = 0.55f, SecondLineSigmaPx = 2.4f, SecondLineOffsetPx = 18f;
        private const float BloomAlpha = 0.22f, BloomSigmaPx = 10f;
        private const float ShadowAlpha = 0.28f, ShadowWidthPx = 50f;

        public static float SideRimPx(float width) => width * SideRimFrac;
        public static float TopRimPx(float height) => height * TopRimFrac;
        public static float BottomRimPx(float height) => height * BottomRimFrac;
        public static float NoseHeightPx(float height) => height * NoseDepthFrac;
        public static float HousingDepthPx(float height) => height * HousingDepthFrac;

        // The lens outline, clockwise from the top-left chamfer, GUI space (y down).
        public static List<Vector2> Outline(float w, float h)
        {
            float sx = SideRimPx(w), ty = TopRimPx(h), by = BottomRimPx(h), c = h * ChamferFrac;
            float hd = HousingDepthPx(h), hw = w * HousingHalfWidthFrac, hc = w * HousingChamferFrac;
            float nd = NoseHeightPx(h), nw = w * NoseHalfWidthFrac, nc = w * NoseChamferFrac;
            float cx = w * 0.5f;
            return new List<Vector2>
            {
                new(sx + c, ty),
                new(cx - hw - hc, ty),          // the compass housing, let into the top edge
                new(cx - hw, ty + hd),
                new(cx + hw, ty + hd),
                new(cx + hw + hc, ty),
                new(w - sx - c, ty),
                new(w - sx, ty + c),
                new(w - sx, h - by - c),
                new(w - sx - c, h - by),
                new(cx + nw + nc, h - by),      // the nose notch
                new(cx + nw, h - by - nd),
                new(cx - nw, h - by - nd),
                new(cx - nw - nc, h - by),
                new(sx + c, h - by),
                new(sx, h - by - c),
                new(sx, ty + c),
            };
        }

        // Signed distance from the lens edge at a GUI-space point, in pixels:
        // negative on the glass, positive on the frame. Exact (nearest edge).
        public static float DistancePx(float x, float y, float width, float height) => DistancePx(new Vector2(x, y), Outline(width, height));

        public static float DistancePx(Vector2 p, List<Vector2> poly)
        {
            float best = float.PositiveInfinity;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 ab = b - a, ap = p - a;
                float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float d = (ap - ab * t).sqrMagnitude;
                if (d < best) best = d;
                if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
            }
            return (inside ? -1f : 1f) * Mathf.Sqrt(best);
        }

        // ---- where the readouts sit --------------------------------------------------------

        // AIR / HP / DEPTH / PRESS, bottom-left, inside the chamfered corner.
        public static Rect VitalsRect(float width, float height)
        {
            float s = height / 1080f;
            float left = SideRimPx(width) + 0.085f * height;   // room for the badge on the left
            float bottom = height - BottomRimPx(height) - 0.045f * height;
            float rows = 2f * 30f * s + 2f * 26f * s;
            return new Rect(left, bottom - rows, (60f + 200f + 12f + 60f) * s, rows);
        }

        // The compass strip: in the housing let into the top edge (on the frame).
        public static Rect CompassRect(float width, float height, float stripWidthPx)
        {
            float top = TopRimPx(height) + 0.006f * height;
            return new Rect(width * 0.5f - stripWidthPx * 0.5f, top, stripWidthPx, HousingDepthPx(height) - 0.01f * height);
        }

        // The heading and HOME lines, on the glass just under the housing.
        public static Rect HeadingRect(float width, float height)
        {
            float s = height / 1080f;
            return new Rect(width * 0.5f - 100f * s, TopRimPx(height) + HousingDepthPx(height) + 4f * s, 200f * s, 40f * s);
        }

        // The slot row with its weight meter, bottom-centre, above the nose notch.
        public static Rect SlotRowRect(float width, float height, float rowWidth, float rowHeight)
        {
            float bottom = height - BottomRimPx(height) - NoseHeightPx(height) - 10f * (height / 1080f);
            return new Rect(width * 0.5f - rowWidth * 0.5f, bottom - rowHeight, rowWidth, rowHeight);
        }

        // The little status labels in the top corners (on the glass, inside the chamfers).
        public static Rect TopLeftLabelRect(float width, float height) => new(SideRimPx(width) + 0.095f * height, TopRimPx(height) + 0.03f * height, 0.22f * height, 40f * (height / 1080f));
        public static Rect TopRightLabelRect(float width, float height) => new(width - SideRimPx(width) - 0.095f * height - 0.22f * height, TopRimPx(height) + 0.03f * height, 0.22f * height, 40f * (height / 1080f));

        // Whether a GUI rectangle lies wholly on the glass with `marginPx` to spare.
        public static bool OnGlass(Rect rect, float width, float height, float marginPx)
        {
            List<Vector2> poly = Outline(width, height);
            return DistancePx(new Vector2(rect.xMin, rect.yMin), poly) < -marginPx && DistancePx(new Vector2(rect.xMax, rect.yMin), poly) < -marginPx
                && DistancePx(new Vector2(rect.xMin, rect.yMax), poly) < -marginPx && DistancePx(new Vector2(rect.xMax, rect.yMax), poly) < -marginPx;
        }

        // Whether a GUI rectangle lies wholly on the frame (the compass housing).
        public static bool OnFrame(Rect rect, float width, float height, float marginPx)
        {
            List<Vector2> poly = Outline(width, height);
            return DistancePx(new Vector2(rect.xMin, rect.yMin), poly) > marginPx && DistancePx(new Vector2(rect.xMax, rect.yMin), poly) > marginPx
                && DistancePx(new Vector2(rect.xMin, rect.yMax), poly) > marginPx && DistancePx(new Vector2(rect.xMax, rect.yMax), poly) > marginPx;
        }

        // ---- the frame as a texture ---------------------------------------------------------

        // The frame for a `screenWidth` × `screenHeight` screen baked at `textureWidth` ×
        // `textureHeight` and stretched over the screen by the caller. Straight alpha:
        // the rim's shadow on the glass, the rim, the bright line at the edge, the
        // fainter second line out on the rim, a soft bloom.
        public static Texture2D Bake(int screenWidth, int screenHeight, int textureWidth, int textureHeight, bool readable = false)
        {
            var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[textureWidth * textureHeight];
            List<Vector2> poly = Outline(screenWidth, screenHeight);
            float s = screenHeight / 1080f;
            float sx = (float)screenWidth / textureWidth, sy = (float)screenHeight / textureHeight;
            for (int ty = 0; ty < textureHeight; ty++)
            {
                float y = (textureHeight - 1 - ty + 0.5f) * sy; // texture rows run bottom-up; GUI space top-down
                for (int tx = 0; tx < textureWidth; tx++)
                {
                    float x = (tx + 0.5f) * sx;
                    float d = DistancePx(new Vector2(x, y), poly);
                    Color c = Color.clear;
                    if (d < 0f) c = Over(c, new Color(0f, 0f, 0f, ShadowAlpha * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-ShadowWidthPx * s, 0f, d))));
                    c = Over(c, new Color(RimColor.r, RimColor.g, RimColor.b, RimColor.a * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-sy, sy, d))));
                    float line = (d - LineOffsetPx * s) / (LineSigmaPx * s);
                    float second = (d - SecondLineOffsetPx * s) / (SecondLineSigmaPx * s);
                    float bloom = d / (BloomSigmaPx * s);
                    float glow = LineAlpha * Mathf.Exp(-line * line) + SecondLineAlpha * Mathf.Exp(-second * second) + BloomAlpha * Mathf.Exp(-bloom * bloom);
                    c = Over(c, new Color(LineColor.r, LineColor.g, LineColor.b, Mathf.Clamp01(glow)));
                    pixels[ty * textureWidth + tx] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, !readable);
            return texture;
        }

        // The vitals badge: a pair of lungs (two lobes and the windpipe) for the ring
        // beside the O2 and HP bars, as the picture.
        public static Texture2D BakeLungs(int size, bool readable = false)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f; // v up
                    float lobeL = Sq((u + 0.2f) / 0.17f) + Sq((v + 0.08f) / 0.3f);
                    float lobeR = Sq((u - 0.2f) / 0.17f) + Sq((v + 0.08f) / 0.3f);
                    bool pipe = Mathf.Abs(u) < 0.035f && v > 0.05f && v < 0.42f;
                    float a = Mathf.Max(Mathf.Clamp01((1.1f - lobeL) * 6f), Mathf.Clamp01((1.1f - lobeR) * 6f), pipe ? 1f : 0f);
                    pixels[y * size + x] = new Color(LineColor.r, LineColor.g, LineColor.b, a);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, !readable);
            return texture;
        }

        private static float Sq(float v) => v * v;

        // A ring with two gaps (top and bottom): the reticle around the aiming dot;
        // with `gaps` false, a full ring (the vitals badge).
        public static Texture2D BakeReticle(int size, bool readable = false, bool gaps = true)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            float half = (size - 1) / 2f, radius = size * 0.42f, thickness = Mathf.Max(1.5f, size / 64f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half, dy = y - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg); // 0 at the bottom, 180 at the top
                    bool inGap = gaps && (angle < 28f || angle > 152f);
                    float a = inGap ? 0f : Mathf.Clamp01(1f - Mathf.Abs(r - radius) / thickness);
                    pixels[y * size + x] = new Color(LineColor.r, LineColor.g, LineColor.b, a);
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
