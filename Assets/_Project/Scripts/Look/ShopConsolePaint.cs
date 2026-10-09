using System.Text;
using UnityEngine;

namespace SunkCost.Look
{
    // HOW the shop console's top screen is drawn (Dan, 8 October 2026, BRIEF B.1: "the
    // whole catalogue is painted on the console's screens like the navigation console,
    // aim at an item and press E to buy"): the EQUIPMENT CATALOGUE header with the crew
    // pot in the corner over a rule, three equal row cards (the item's name, its price,
    // a state chip reading BUY / OWNED / NO MONEY), and a foot band with the PREV and
    // NEXT zones either side of the page count. A pure function of a view and the
    // style asset over a ScreenPainter, in ConsolePaint's idiom (the same margins,
    // header, rule and card language, so the three HQ consoles read as one
    // installation). The same layout gives the builder (ShopConsoleBuilder) and the
    // composer's LayoutControls the rows' and zones' rectangles, so a hit box sits
    // exactly where its picture is. The bottom screen and the sign are the standard
    // poster and plate (ConsolePaint.Bottom / Sign through ConsoleRig.Show).
    public static class ShopConsolePaint
    {
        public const int RowsPerPage = 3;   // == ShopConsoleRules.RowsPerPage (the pure table; kept in step by HQShopConsole)
        private const FontStyle Style = FontStyle.Bold;

        public struct RowView
        {
            public string Name, Price, State;   // "Air tank", "$40", "BUY" / "OWNED" / "NO MONEY"
            public bool Aimed, Empty;           // Aimed: LOCAL presentation only; Empty: no item on this row (the frame alone)
        }

        public struct TopView
        {
            public string Title, Pot, Page, PrevWord, NextWord;   // "EQUIPMENT CATALOGUE", "CREW POT $500", "PAGE 1/2", "< PREV", "NEXT >"
            public RowView[] Rows;
            public bool PrevEnabled, NextEnabled, PrevAimed, NextAimed;
        }

        // ---- layout (painter pixels, y down from the reader's top-left) -----------------

        private static float Px(float fraction, int h) => Mathf.Max(6f, fraction * h);
        private static float Thick(ConsoleStyle s, int h) => Mathf.Max(1f, s.FrameThickness * h / 500f);
        private static Color Alpha(Color c, float a) => new(c.r, c.g, c.b, a);
        private static Color Scale(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);
        private static Rect Inset(Rect r, float by) => new(r.x + by, r.y + by, Mathf.Max(1f, r.width - 2f * by), Mathf.Max(1f, r.height - 2f * by));

        // The bands: the margin, the rule under the header, the rows' top and bottom
        // (the foot band's room is always kept, so the colliders never move).
        private static void Bands(int w, int h, ConsoleStyle s, out float margin, out float ruleY, out float rowsTop, out float rowsBottom)
        {
            margin = 0.045f * h;
            ruleY = margin + 0.13f * h;
            rowsTop = ruleY + 0.035f * h;
            rowsBottom = h - margin - Px(s.Foot, h) * 1.5f;
        }

        // The three equal row cards, top to bottom (at 1920x1024: x 46..1874, y 215 / 435 / 655, 200 high).
        public static Rect[] RowRects(int w, int h, ConsoleStyle s) => RowRects(w, h, s, RowsPerPage);
        public static Rect[] RowRects(int w, int h, ConsoleStyle s, int rows)
        {
            Bands(w, h, s, out float margin, out _, out float top, out float bottom);
            rows = Mathf.Max(1, rows);
            float gap = 0.02f * h;
            float rh = (bottom - top - (rows - 1) * gap) / rows;
            var rects = new Rect[rows];
            for (int i = 0; i < rows; i++) rects[i] = new Rect(margin, top + i * (rh + gap), w - 2f * margin, rh);
            return rects;
        }

        // The foot band (ConsolePaint.Top's foot line's room) and its two zones.
        public static Rect FootRect(int w, int h, ConsoleStyle s)
        {
            float margin = 0.045f * h, foot = Px(s.Foot, h);
            return new Rect(margin, h - margin - foot * 1.3f, w - 2f * margin, foot * 1.3f);
        }

        public static Rect PrevRect(int w, int h, ConsoleStyle s)
        {
            Rect foot = FootRect(w, h, s);
            return new Rect(foot.x, foot.y, 0.18f * w, foot.height);
        }

        public static Rect NextRect(int w, int h, ConsoleStyle s)
        {
            Rect foot = FootRect(w, h, s);
            return new Rect(foot.xMax - 0.18f * w, foot.y, 0.18f * w, foot.height);
        }

        // ---- the key: everything that changes the picture, nothing that does not --------

        public static string TopKey(in TopView v)
        {
            var sb = new StringBuilder("shop-top:");
            sb.Append(v.Title).Append('|').Append(v.Pot).Append('|').Append(v.Page).Append('|').Append(v.PrevWord).Append('|').Append(v.NextWord);
            sb.Append('|').Append(v.PrevEnabled ? 'e' : 'd').Append(v.NextEnabled ? 'e' : 'd').Append(v.PrevAimed ? 'a' : '-').Append(v.NextAimed ? 'a' : '-');
            if (v.Rows != null)
                foreach (RowView r in v.Rows)
                    sb.Append('|').Append(r.Empty ? "-" : r.Name).Append(';').Append(r.Price).Append(';').Append(r.State).Append(';').Append(r.Aimed ? 'a' : '-');
            return sb.ToString();
        }

        // ---- the top screen ------------------------------------------------------------

        public static void Top(ScreenPainter p, in TopView v, ConsoleStyle s)
        {
            int w = p.Width, h = p.Height;
            Bands(w, h, s, out float margin, out float ruleY, out _, out _);
            float t = Thick(s, h);
            p.Grid(new Rect(0f, 0f, w, h), Mathf.Max(8f, s.GridStep * h), Alpha(ScreenStyle.Accent, s.GridAlpha));
            // The header as ConsolePaint.Top draws it: the title left, the pot right, a rule.
            float headH = 0.12f * h;
            p.TextIn(v.Title, (int)Px(s.TopTitle, h), Style, new Rect(margin, margin, w * 0.62f - margin, headH), TextAnchor.MiddleLeft, ScreenStyle.Accent, s.TitleTracking);
            p.TextIn(v.Pot, (int)Px(s.TopCorner, h), Style, new Rect(w * 0.62f, margin, w * 0.38f - margin, headH), TextAnchor.MiddleRight, ScreenStyle.Accent, s.TitleTracking * 0.5f);
            Color rule = Color.Lerp(ScreenStyle.Track, ScreenStyle.Accent, 0.5f);
            p.Rect(new Rect(margin, ruleY, w - 2f * margin, Mathf.Max(2f, 0.004f * h)), rule);
            // The rows.
            int rows = v.Rows != null ? v.Rows.Length : 0;
            Rect[] rects = RowRects(w, h, s, Mathf.Max(rows, 1));
            for (int i = 0; i < rows && i < rects.Length; i++) Row(p, rects[i], v.Rows[i], s, t, w, h);
            // The foot: PREV and NEXT at the ends, the page count between them.
            Rect foot = FootRect(w, h, s), prev = PrevRect(w, h, s), next = NextRect(w, h, s);
            Zone(p, prev, v.PrevWord, v.PrevEnabled, v.PrevAimed, s, t, h);
            Zone(p, next, v.NextWord, v.NextEnabled, v.NextAimed, s, t, h);
            p.TextIn(v.Page, (int)Px(0.065f, h), Style, new Rect(prev.xMax, foot.y, next.xMin - prev.xMax, foot.height), TextAnchor.MiddleCenter, ScreenStyle.Text, s.TitleTracking * 0.5f);
        }

        // One row card: the card language of the ship's destination cards (fill, frame,
        // a halo and brackets while aimed), the name left, the price, the state chip right.
        private static void Row(ScreenPainter p, Rect r, in RowView row, ConsoleStyle s, float t, int w, int h)
        {
            if (row.Empty)
            {
                p.Frame(r, t, Alpha(ScreenStyle.Accent, 0.25f));
                return;
            }
            p.Rect(r, Alpha(ScreenStyle.Accent, s.CardFillAlpha));
            if (row.Aimed) p.Halo(r, 0.025f * h, Alpha(ScreenStyle.Accent, s.HaloAlpha));
            p.Frame(r, row.Aimed ? t * s.SelectedFrameScale : t, ScreenStyle.Accent);
            if (row.Aimed) p.Brackets(Inset(r, 3f * t), 0.06f * h, t, Alpha(ScreenStyle.Text, 0.75f));
            p.TextIn(row.Name, (int)Px(0.085f, h), Style, new Rect(r.x + 0.03f * w, r.y, 0.50f * w, r.height), TextAnchor.MiddleLeft, ScreenStyle.Text, s.TitleTracking);
            p.TextIn(row.Price, (int)Px(0.085f, h), Style, new Rect(r.x + 0.55f * w, r.y, 0.18f * w, r.height), TextAnchor.MiddleRight, ScreenStyle.Accent, s.TitleTracking * 0.5f);
            // The state chip: BUY in the accent, OWNED green and filled, NO MONEY amber.
            Rect chip = new(r.xMax - 0.02f * w - 0.18f * w, r.y + 0.2f * r.height, 0.18f * w, 0.6f * r.height);
            Color c; float fill;
            switch (row.State)
            {
                case "OWNED": c = ScreenStyle.Good; fill = 0.15f; break;
                case "NO MONEY": c = ScreenStyle.Warn; fill = 0.08f; break;
                case "BUY": c = ScreenStyle.Accent; fill = 0f; break;
                default: c = ScreenStyle.Dim; fill = 0f; break;
            }
            if (fill > 0f) p.Rect(chip, Alpha(c, fill));
            p.Frame(chip, t, c);
            p.TextIn(row.State, (int)Px(0.06f, h), Style, Inset(chip, 0.01f * h), TextAnchor.MiddleCenter, c, s.TitleTracking * 0.5f);
        }

        // A paging zone: a framed word, lit in the accent while a page lies that way,
        // dim (the sign's dimming) at the end of the catalogue; brackets while aimed.
        private static void Zone(ScreenPainter p, Rect r, string word, bool enabled, bool aimed, ConsoleStyle s, float t, int h)
        {
            Color c = enabled ? ScreenStyle.Accent : Scale(ScreenStyle.Accent, s.DisabledSign);
            if (enabled) p.Rect(r, Alpha(c, s.CardFillAlpha));
            if (aimed) p.Halo(r, 0.02f * h, Alpha(c, s.HaloAlpha));
            p.Frame(r, aimed && enabled ? t * s.SelectedFrameScale : t, c);
            if (aimed) p.Brackets(Inset(r, 3f * t), 0.04f * h, t, Alpha(ScreenStyle.Text, 0.75f));
            p.TextIn(word, (int)Px(0.065f, h), Style, Inset(r, 0.01f * h), TextAnchor.MiddleCenter, c, s.TitleTracking * 0.5f);
        }
    }
}
