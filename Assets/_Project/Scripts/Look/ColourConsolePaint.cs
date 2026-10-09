using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Look
{
    // HOW the crew colour console's two screens are drawn (Dan, 8 October 2026;
    // docs/reference/hq/colour-console-concept.webp): the top screen's framed title
    // over a ring of the sixteen palette swatches as glowing discs - white at the
    // top-left of red, as the palette's own order and PlayerPalette.WheelPosition
    // put them - the shown one ringed, and in the middle the classic round brass
    // diving helmet as line art in the shown colour; the bottom screen's colour
    // name in its colour over PULL THE LEVER TO TAKE IT (or <NAME> IS NOW <COLOUR>
    // for a few seconds after a pull, or a refusal). Pure functions of a view and the
    // style asset over a ScreenPainter, drawn at the screen's own resolution (no
    // bitmap); SwatchRects is the one layout the painter and the builder's hit
    // boxes share, so a swatch is where its disc is. Colours: ScreenStyle's accent
    // for lettering and frames, the palette's own for the discs, the helmet and
    // the name.
    public static class ColourConsolePaint
    {
        public struct TopView { public string Title; public int Shown; public int Aimed; }      // Aimed -1 = none (LOCAL)
        public struct BottomView
        {
            public string Name;      // the shown colour's name, large, in its colour
            public int Swatch;       // the shown swatch (the name's colour)
            public int TakenSwatch;  // the colour word's swatch when Taken
            public string Line;      // PULL THE LEVER TO TAKE IT / SKIPPER IS NOW BLUE / a refusal
            public bool Taken;       // the line's last word is a colour, drawn in TakenSwatch
            public bool Notice;      // the line is a refusal: the framed block
        }

        private const FontStyle Style = FontStyle.Bold;

        // ---- the ring's layout (shared with the builder) -------------------------------

        // The ring is the picture's: an ellipse wider than it is tall (the screen is
        // wide), the discs small enough to stand apart with the dotted links between
        // them showing (the first pass, 9 October 2026, packed them shoulder to shoulder
        // on a circle and their glows ran together into one band).
        public static Vector2 RingCentre(int w, int h) => new(w * 0.5f, h * 0.575f);
        public static float RingRadius(int h) => 0.33f * h;      // the disc centres' ellipse: its vertical half-axis
        public static float RingRadiusX(int h) => 0.40f * h;     // its horizontal half-axis
        public static float SwatchRadius(int h) => 0.046f * h;   // the glowing disc
        public static float HelmetRadius(int h) => 0.16f * h;
        public const float HitBoxSides = 2.25f;                  // the hit box's side in disc radii: covers the disc and its halo, short of the next box (swatches 2-3 sit 110 px apart at 1024 px tall)

        // Index 0 at the top, clockwise (PlayerPalette.WheelPosition, its y flipped for
        // the painter's y-down pixels).
        public static Vector2 SwatchCentre(int w, int h, int i)
        {
            Vector2 u = PlayerPalette.WheelPosition(i);
            Vector2 c = RingCentre(w, h);
            return new Vector2(c.x + u.x * RingRadiusX(h), c.y - u.y * RingRadius(h));
        }

        // Sixteen squares of side HitBoxSides r centred on the discs: the hit boxes AND the discs' boxes.
        public static Rect[] SwatchRects(int w, int h)
        {
            float side = HitBoxSides * SwatchRadius(h);
            var rects = new Rect[PlayerPalette.Count];
            for (int i = 0; i < rects.Length; i++)
            {
                Vector2 c = SwatchCentre(w, h, i);
                rects[i] = new Rect(c.x - side / 2f, c.y - side / 2f, side, side);
            }
            return rects;
        }

        // ---- keys: everything that changes the picture ---------------------------------

        public static string TopKey(in TopView v) => "colour-top:" + v.Title + "|" + v.Shown + "|" + v.Aimed;
        public static string BottomKey(in BottomView v) => "colour-bottom:" + v.Name + "|" + v.Swatch + "|" + v.TakenSwatch + "|" + v.Line + "|" + (v.Taken ? 't' : '-') + (v.Notice ? 'n' : '-');

        // ---- helpers -------------------------------------------------------------------

        private static float Px(float fraction, int h) => Mathf.Max(6f, fraction * h);
        private static float Thick(ConsoleStyle s, int h) => Mathf.Max(1f, s.FrameThickness * h / 500f);
        private static Color Alpha(Color c, float a) => new(c.r, c.g, c.b, a);

        // A palette colour as the glass shows it: the palette is tuned for a lit suit,
        // and on the emissive glass under the bloom its pastels wash to near white
        // (the first capture, 9 October 2026). Deeper and a little more saturated so
        // the disc, the helmet and the name read as the colour the suit will wear.
        public const float ScreenGain = 0.72f, ScreenSaturation = 1.35f;
        public static Color OnGlass(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float r = Mathf.Clamp01(max + (c.r - max) * ScreenSaturation);
            float g = Mathf.Clamp01(max + (c.g - max) * ScreenSaturation);
            float b = Mathf.Clamp01(max + (c.b - max) * ScreenSaturation);
            return new Color(r * ScreenGain, g * ScreenGain, b * ScreenGain, c.a);
        }
        private static Rect Inset(Rect r, float by) => new(r.x + by, r.y + by, Mathf.Max(1f, r.width - 2f * by), Mathf.Max(1f, r.height - 2f * by));
        private static Rect Grow(Rect r, float by) => Inset(r, -by);

        // ---- the top screen ------------------------------------------------------------

        public static void Top(ScreenPainter p, in TopView v, ConsoleStyle s)
        {
            int w = p.Width, h = p.Height;
            float margin = 0.045f * h;
            p.Grid(new Rect(0f, 0f, w, h), Mathf.Max(8f, s.GridStep * h), Alpha(ScreenStyle.Accent, s.GridAlpha));
            // The thin outer frame with corner ticks (the picture's rounded frame).
            Rect frame = Inset(new Rect(0f, 0f, w, h), margin * 0.5f);
            p.Frame(frame, 1.5f, Alpha(ScreenStyle.Accent, 0.55f));
            p.Brackets(frame, 0.05f * h, Mathf.Max(2f, 0.004f * h), ScreenStyle.Accent);

            // The title in the middle three fifths of the width, the picture's long thin
            // lines at its mid-height running out to the frame's edge, each ending in a
            // corner that drops to the rule under the band; the rule has end ticks.
            int titlePx = (int)Px(s.TopTitle, h);
            Rect band = new(0.2f * w, margin, 0.6f * w, 0.12f * h);
            int drawnPx = p.TextIn(v.Title, titlePx, Style, band, TextAnchor.MiddleCenter, ScreenStyle.Accent, s.TitleTracking);
            float titleW = p.Estimate(v.Title, drawnPx, Style, s.TitleTracking);
            float lineY = margin + 0.06f * h, lineT = Mathf.Max(2f, 0.003f * h);
            float gap = 0.025f * w, edge = margin + 0.015f * w;
            float ruleY = 0.165f * h;
            float titleLeft = w * 0.5f - titleW * 0.5f, titleRight = w * 0.5f + titleW * 0.5f;
            Color lines = Alpha(ScreenStyle.Accent, 0.8f);
            if (titleLeft - gap > edge)
            {
                p.Rect(new Rect(edge, lineY - lineT / 2f, titleLeft - gap - edge, lineT), lines);
                p.Rect(new Rect(titleRight + gap, lineY - lineT / 2f, w - edge - titleRight - gap, lineT), lines);
                p.Rect(new Rect(edge, lineY - lineT / 2f, lineT, ruleY - lineY + lineT), lines);
                p.Rect(new Rect(w - edge - lineT, lineY - lineT / 2f, lineT, ruleY - lineY + lineT), lines);
            }
            float tick = 0.018f * h;
            p.Rect(new Rect(margin, ruleY, w - 2f * margin, lineT), Alpha(ScreenStyle.Accent, 0.6f));
            p.Rect(new Rect(margin, ruleY - tick, lineT, tick + lineT), Alpha(ScreenStyle.Accent, 0.6f));
            p.Rect(new Rect(w - margin - lineT, ruleY - tick, lineT, tick + lineT), Alpha(ScreenStyle.Accent, 0.6f));

            // The ring of swatches: the dim links between neighbours first, then every
            // disc with its glow, then the shown one's bright outline and the aimed one's brackets.
            int n = PlayerPalette.Count;
            float r = SwatchRadius(h);
            Vector2[] centres = new Vector2[n];
            for (int i = 0; i < n; i++) centres[i] = SwatchCentre(w, h, i);
            float dash = Mathf.Max(2f, 0.006f * h);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Color link = Alpha(OnGlass(Color.Lerp(PlayerPalette.Get(i), PlayerPalette.Get(j), 0.5f)), 0.6f);
                Links(p, centres[i], centres[j], r + 0.018f * h, 3, dash, link);
            }
            for (int i = 0; i < n; i++)
            {
                // A tight halo (the picture's glow stays round each disc, the gaps dark),
                // the disc, then a dark rim so a pale swatch (white, yellow) reads as a disc
                // and the halo reads as light round it, not as a bigger disc.
                Color colour = OnGlass(PlayerPalette.Get(i));
                Disc(p, centres[i], r * 1.55f, Alpha(colour, 0.10f), 2f);
                Disc(p, centres[i], r * 1.32f, Alpha(colour, 0.20f), 1f);
                Disc(p, centres[i], r * 1.14f, Alpha(colour, 0.36f), 1f);
                Disc(p, centres[i], r, colour, 1f);
                Circle(p, centres[i], r - 0.001f * h, Mathf.Max(2f, 0.004f * h), Alpha(ScreenStyle.Back, 0.55f), 56);
            }
            if (PlayerPalette.IsValid(v.Shown))
            {
                Vector2 c = centres[v.Shown];
                Color shown = OnGlass(PlayerPalette.Get(v.Shown));
                Circle(p, c, r + 0.026f * h, 0.005f * h, Alpha(shown, 0.35f), 56);
                Circle(p, c, r + 0.011f * h, 0.006f * h, Color.Lerp(shown, Color.white, 0.8f), 56);
            }
            if (PlayerPalette.IsValid(v.Aimed))
            {
                Rect[] rects = SwatchRects(w, h);
                p.Brackets(Grow(rects[v.Aimed], 0.01f * h), 0.03f * h, Mathf.Max(2f, 0.003f * h), Alpha(ScreenStyle.Text, 0.75f));
            }

            // The helmet in the middle, in the shown colour: a soft glow pass, then the lines.
            Color helmet = OnGlass(PlayerPalette.Get(v.Shown));
            Vector2 centre = RingCentre(w, h);
            float hr = HelmetRadius(h), stroke = Mathf.Max(2f, 0.0085f * h);
            Helmet(p, centre, hr, Alpha(helmet, 0.16f), stroke * 2.6f, true);
            Helmet(p, centre, hr, helmet, stroke, false);
        }

        // Dots along the arc between two neighbouring discs' edges (the picture's dotted links).
        private static void Links(ScreenPainter p, Vector2 a, Vector2 b, float clear, int dots, float size, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len <= 2f * clear) return;
            Vector2 dir = d / len;
            Vector2 from = a + dir * clear, to = b - dir * clear;
            for (int k = 0; k < dots; k++)
            {
                float t = (k + 0.5f) / dots;
                Vector2 at = Vector2.Lerp(from, to, t);
                p.Rect(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), c);
            }
        }

        // ---- the helmet (line art) -----------------------------------------------------

        // The classic round diving helmet seen from the front: the bowl, the round
        // faceplate with its grille, four bolts round it, the two side ports, the valve
        // on top and the bolted collar under it. `glow` is the soft pass drawn first
        // (thicker, faint): its bolts are grown by the stroke so they glow too.
        internal static void Helmet(ScreenPainter p, Vector2 c, float r, Color colour, float thickness, bool glow)
        {
            // The collar hides the bowl's lower arc: the glass colour under its frame.
            Rect collar = new(c.x - 1.05f * r, c.y + 0.78f * r, 2.1f * r, 0.42f * r);
            Circle(p, c, r, thickness, colour, 96);
            if (!glow) p.Rect(Inset(collar, thickness * 0.5f), ScreenStyle.Back);
            p.Frame(collar, thickness, colour);
            // The faceplate and its grille, clipped to the plate's circle.
            float fr = 0.55f * r;
            Circle(p, c, fr, thickness, colour, 64);
            foreach (float off in new[] { -0.18f * r, 0.18f * r })
            {
                float half = Mathf.Sqrt(Mathf.Max(0f, fr * fr - off * off));
                p.Line(new Vector2(c.x + off, c.y - half), new Vector2(c.x + off, c.y + half), thickness, colour);
                p.Line(new Vector2(c.x - half, c.y + off), new Vector2(c.x + half, c.y + off), thickness, colour);
            }
            // The four bolts at 45 degrees round the faceplate.
            float boltR = 0.05f * r + (glow ? thickness * 0.5f : 0f);
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
            {
                float rad = a * Mathf.Deg2Rad;
                Disc(p, new Vector2(c.x + Mathf.Cos(rad) * 0.72f * r, c.y + Mathf.Sin(rad) * 0.72f * r), boltR, colour, 1f);
            }
            // The side ports.
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector2 pc = new(c.x + sx * 0.98f * r, c.y);
                if (!glow) Disc(p, pc, 0.22f * r - thickness * 0.5f, ScreenStyle.Back, 1f);
                Circle(p, pc, 0.22f * r, thickness, colour, 40);
                Circle(p, pc, 0.11f * r, thickness * 0.7f, colour, 24);
            }
            // The valve on top, with its short neck.
            Rect valve = new(c.x - 0.18f * r, c.y - 1.22f * r, 0.36f * r, 0.2f * r);
            p.Frame(valve, thickness, colour);
            p.Rect(new Rect(c.x - 0.08f * r, c.y - 1.04f * r, 0.16f * r, 0.06f * r), colour);
            // Six bolts along the collar.
            float cy = collar.center.y;
            for (int k = 0; k < 6; k++)
            {
                float x = c.x + (-0.85f + k * 0.34f) * r;
                Disc(p, new Vector2(x, cy), 0.04f * r + (glow ? thickness * 0.5f : 0f), colour, 1f);
            }
        }

        // ---- shapes the painter lacks ---------------------------------------------------

        // A ring of `thickness` round the circumference, from line segments.
        internal static void Circle(ScreenPainter p, Vector2 c, float r, float thickness, Color colour, int segments = 48)
        {
            if (p.Collecting || colour.a <= 0f || r <= 0f) return;
            segments = Mathf.Max(12, segments);
            Vector2 previous = new(c.x, c.y - r);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector2 next = new(c.x + Mathf.Sin(a) * r, c.y - Mathf.Cos(a) * r);
                p.Line(previous, next, thickness, colour);
                previous = next;
            }
        }

        // A filled disc from horizontal scanlines `step` pixels tall.
        internal static void Disc(ScreenPainter p, Vector2 c, float r, Color colour, float step = 1f)
        {
            if (p.Collecting || colour.a <= 0f || r <= 0f) return;
            step = Mathf.Max(1f, step);
            for (float y = -r; y < r; y += step)
            {
                float mid = y + step * 0.5f;
                float half = Mathf.Sqrt(Mathf.Max(0f, r * r - mid * mid));
                if (half < 0.5f) continue;
                p.Rect(new Rect(c.x - half, c.y + y, 2f * half, step), colour);
            }
        }

        // ---- the bottom screen ---------------------------------------------------------

        public static void Bottom(ScreenPainter p, in BottomView v, ConsoleStyle s)
        {
            int w = p.Width, h = p.Height;
            float t = Thick(s, h);
            p.Grid(new Rect(0f, 0f, w, h), Mathf.Max(8f, s.GridStep * h), Alpha(ScreenStyle.Accent, s.GridAlpha));
            float margin = 0.06f * h;
            Rect content = new(margin, margin, w - 2f * margin, h - 2f * margin);
            p.Frame(content, 1f, Alpha(ScreenStyle.Accent, 0.5f));
            p.Brackets(content, 0.07f * h, t, ScreenStyle.Accent);
            Color swatch = OnGlass(PlayerPalette.Get(v.Swatch));

            // The name, large, in its colour, a soft glow behind it.
            int namePx = (int)Px(0.30f, h);
            Rect nameBox = new(0.06f * w, 0.12f * h, 0.88f * w, 0.42f * h);
            float est = p.Estimate(v.Name, namePx, Style, s.TitleTracking);
            if (est > nameBox.width * 0.98f && est > 0f) { namePx = Mathf.Max(8, Mathf.FloorToInt(namePx * nameBox.width * 0.98f / est)); est = nameBox.width * 0.98f; }
            Rect glowBox = new(nameBox.center.x - est / 2f - 0.02f * h, nameBox.center.y - namePx * 0.42f, est + 0.04f * h, namePx * 0.84f);
            p.Halo(glowBox, 0.03f * h, Alpha(swatch, 0.25f));
            p.Rect(glowBox, Alpha(swatch, 0.06f));
            p.TextIn(v.Name, namePx, Style, nameBox, TextAnchor.MiddleCenter, swatch, s.TitleTracking);

            // The line under it: the pull, the take (its colour word in that colour), or the refusal.
            Rect lineBox = new(0.06f * w, 0.60f * h, 0.88f * w, 0.18f * h);
            if (v.Notice)
            {
                Color c = ScreenStyle.Warn;
                Rect block = new(lineBox.x + 0.04f * w, lineBox.y, lineBox.width - 0.08f * w, lineBox.height);
                p.Rect(block, Alpha(c, 0.10f));
                p.Frame(block, t * 1.5f, c);
                p.TextIn(v.Line, (int)Px(s.BottomNotice, h), Style, Inset(block, 0.03f * h), TextAnchor.MiddleCenter, c, s.TitleTracking * 0.5f);
                return;
            }
            int linePx = (int)Px(0.11f, h);
            float tracking = s.TitleTracking * 0.5f;
            int split = v.Taken && !string.IsNullOrEmpty(v.Line) ? v.Line.LastIndexOf(' ') : -1;
            if (split <= 0)
            {
                p.TextIn(v.Line, linePx, Style, lineBox, TextAnchor.MiddleCenter, v.Taken ? ScreenStyle.Text : ScreenStyle.Accent, tracking);
                return;
            }
            string head = v.Line.Substring(0, split + 1), word = v.Line.Substring(split + 1);
            float total = p.Estimate(head, linePx, Style, tracking) + p.Estimate(word, linePx, Style, tracking);
            if (total > lineBox.width * 0.98f && total > 0f) linePx = Mathf.Max(8, Mathf.FloorToInt(linePx * lineBox.width * 0.98f / total));
            float headW = p.Measure(head, linePx, Style, tracking).x, wordW = p.Measure(word, linePx, Style, tracking).x;
            float x0 = lineBox.center.x - (headW + wordW) / 2f;
            p.TextIn(head, linePx, Style, new Rect(x0, lineBox.y, headW + 4f, lineBox.height), TextAnchor.MiddleLeft, ScreenStyle.Text, tracking, false);
            p.TextIn(word, linePx, Style, new Rect(x0 + headW, lineBox.y, wordW + 4f, lineBox.height), TextAnchor.MiddleLeft, OnGlass(PlayerPalette.Get(v.TakenSwatch)), tracking, false);
        }
    }
}
