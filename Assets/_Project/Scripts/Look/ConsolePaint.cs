using System.Text;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Look
{
    // HOW the console's three surfaces are drawn (the shared console, 27 September
    // 2026; the render scout's RENDER.md, the reference concept): the top screen's
    // header over a rule and its five equal destination cards (or the quota board's
    // big state), the bottom screen's heading, picture and facts with a prominent
    // status (or the red GIVE UP card), and the lever sign's illuminated word over a
    // hazard strip. Pure functions of a content model and the style asset over a
    // ScreenPainter; the same layout gives the builder the card and card colliders'
    // rectangles, so a card is where its picture is. Colours are ScreenStyle's.
    public static class ConsolePaint
    {
        public const string TopSurface = "ConsoleTop", BottomSurface = "ConsoleBottom", SignSurface = "ConsoleSign";
        private const FontStyle Style = FontStyle.Bold;
        private const int CardCount = 5;

        public static Color Tone(ConsoleTone tone)
        {
            switch (tone)
            {
                case ConsoleTone.Accent: return ScreenStyle.Accent;
                case ConsoleTone.Dim: return ScreenStyle.Dim;
                case ConsoleTone.Warn: return ScreenStyle.Warn;
                case ConsoleTone.Good: return ScreenStyle.Good;
                case ConsoleTone.Danger: return ScreenStyle.Danger;
                default: return ScreenStyle.Text;
            }
        }

        // ---- keys: everything that changes the picture, nothing that does not ---------

        public static string Key(TopModel m) => Key(m, m != null ? ConsoleModels.Flatten(m) : null);
        public static string Key(TopModel m, string flat)
        {
            if (m == null) return "top:none";
            var sb = new StringBuilder("top:").Append(flat ?? ConsoleModels.Flatten(m));
            sb.Append('|').Append((int)m.BigTone).Append((int)m.HintTone).Append((int)m.FootLeftTone);
            if (m.Cards != null)
                foreach (CardModel c in m.Cards)
                    sb.Append('|').Append(c != null && c.Picture != null ? c.Picture.GetEntityId().ToString() : "0").Append(':').Append(c != null ? c.Tag : string.Empty);
            return sb.ToString();
        }

        public static string Key(BottomModel m) => Key(m, m != null ? ConsoleModels.Flatten(m) : null);
        public static string Key(BottomModel m, string flat)
        {
            if (m == null) return "bottom:none";
            var sb = new StringBuilder("bottom:").Append(flat ?? ConsoleModels.Flatten(m));
            sb.Append('|').Append((int)m.Status.Tone).Append((int)m.Status.Size).Append('|').Append(m.Picture != null ? m.Picture.GetEntityId().ToString() : "0");
            if (m.Lines != null) foreach (ConsoleLine line in m.Lines) sb.Append('|').Append((int)line.Tone).Append((int)line.Size);
            if (m.VoteCard != null) sb.Append("|vote").Append(m.VoteCard.Enabled ? 'e' : 'd').Append(m.VoteCard.Aimed ? 'a' : '-').Append(m.VoteCard.Foot);
            return sb.ToString();
        }

        public static string Key(SignModel m) => Key(m, ConsoleModels.Flatten(m));
        public static string Key(SignModel m, string flat) => "sign:" + (flat ?? ConsoleModels.Flatten(m)) + "|" + (int)m.Tone + (m.Aimed ? "a" : "-");

        // A picture's tint: the accent (or a card's own variant) when the style paints
        // silhouettes, else white so Dan's own colours show (a locked card's are dimmed).
        private static Color PictureTint(ConsoleStyle s, Color silhouette, bool locked)
            => s.PictureTintOnly ? silhouette : locked ? Scale(Color.white, s.LockedDim) : Color.white;

        // ---- layout ------------------------------------------------------------------

        private static float Px(float fraction, int h) => Mathf.Max(6f, fraction * h);
        private static float Thick(ConsoleStyle s, int h) => Mathf.Max(1f, s.FrameThickness * h / 500f);
        private static Color Alpha(Color c, float a) => new(c.r, c.g, c.b, a);
        private static Color Scale(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);
        private static Rect Inset(Rect r, float by) => new(r.x + by, r.y + by, Mathf.Max(1f, r.width - 2f * by), Mathf.Max(1f, r.height - 2f * by));

        // The top screen's bands: the margin, the rule under the header, the cards' top
        // and bottom (room for the foot line is always kept, so the colliders never move).
        private static void TopBands(int w, int h, ConsoleStyle s, out float margin, out float ruleY, out float cardsTop, out float cardsBottom)
        {
            margin = 0.045f * h;
            ruleY = margin + 0.12f * h + 0.01f * h;
            cardsTop = ruleY + 0.035f * h;
            cardsBottom = h - margin - Px(s.Foot, h) * 1.5f;
        }

        // The five equal cards, left to right for the reader.
        public static Rect[] CardRects(int w, int h, ConsoleStyle s)
        {
            TopBands(w, h, s, out float margin, out _, out float top, out float bottom);
            float gap = s.CardGap * w;
            float cw = (w - 2f * margin - (CardCount - 1) * gap) / CardCount;
            var rects = new Rect[CardCount];
            for (int i = 0; i < CardCount; i++) rects[i] = new Rect(margin + i * (cw + gap), top, cw, bottom - top);
            return rects;
        }

        // The bottom screen's content area under its heading (or the whole glass without one).
        private static Rect ContentRect(int w, int h, bool withHeading)
        {
            float margin = 0.06f * h;
            return withHeading ? new Rect(margin, margin + 0.16f * h, w - 2f * margin, h - 2f * margin - 0.16f * h) : new Rect(margin, margin, w - 2f * margin, h - 2f * margin);
        }

        // The GIVE UP card: the HQ bottom screen's whole content area, inset a little.
        public static Rect GiveUpRect(int w, int h, ConsoleStyle s) => Inset(ContentRect(w, h, false), 0.03f * h);

        // ---- the top screen ------------------------------------------------------------

        public static void Top(ScreenPainter p, TopModel m, ConsoleStyle s)
        {
            if (m == null) return;
            int w = p.Width, h = p.Height;
            TopBands(w, h, s, out float margin, out float ruleY, out _, out _);
            p.Grid(new Rect(0f, 0f, w, h), Mathf.Max(8f, s.GridStep * h), Alpha(ScreenStyle.Accent, s.GridAlpha));
            float headH = 0.12f * h;
            p.TextIn(m.Title, (int)Px(s.TopTitle, h), Style, new Rect(margin, margin, w * 0.62f - margin, headH), TextAnchor.MiddleLeft, ScreenStyle.Accent, s.TitleTracking);
            p.TextIn(m.Corner, (int)Px(s.TopCorner, h), Style, new Rect(w * 0.62f, margin, w * 0.38f - margin, headH), TextAnchor.MiddleRight, ScreenStyle.Accent, s.TitleTracking * 0.5f);
            Color rule = Color.Lerp(ScreenStyle.Track, ScreenStyle.Accent, 0.5f);
            p.Rect(new Rect(margin, ruleY, w - 2f * margin, Mathf.Max(2f, 0.004f * h)), rule);
            bool cards = m.Cards != null && m.Cards.Length > 0;
            if (cards) Cards(p, m, s); else State(p, m, s, margin, rule);
            float footPx = Px(s.Foot, h);
            Rect foot = new(margin, h - margin - footPx * 1.3f, w - 2f * margin, footPx * 1.3f);
            if (!string.IsNullOrEmpty(m.FootLeft))
                p.TextIn(m.FootLeft, (int)footPx, Style, new Rect(foot.x, foot.y, foot.width * 0.6f, foot.height), TextAnchor.MiddleLeft, Tone(m.FootLeftTone), s.TitleTracking * 0.3f);
            if (!string.IsNullOrEmpty(m.FootRight))
                p.TextIn(m.FootRight, (int)footPx, Style, new Rect(foot.x + foot.width * 0.4f, foot.y, foot.width * 0.6f, foot.height), TextAnchor.MiddleRight, cards ? ScreenStyle.Dim : ScreenStyle.Text, s.TitleTracking * 0.3f);
        }

        private static void Cards(ScreenPainter p, TopModel m, ConsoleStyle s)
        {
            int h = p.Height;
            Rect[] rects = CardRects(p.Width, h, s);
            float t = Thick(s, h);
            // One name size for the group, shrunk until the longest name fits its card.
            int namePx = (int)Px(s.CardName, h);
            for (int i = 0; i < CardCount && i < m.Cards.Length; i++)
            {
                CardModel c = m.Cards[i];
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                float need = p.Estimate(c.Name, namePx, Style, s.TitleTracking), room = rects[i].width * 0.86f;
                if (need > room && need > 0f) namePx = Mathf.Max(8, Mathf.FloorToInt(namePx * room / need));
            }
            for (int i = 0; i < CardCount && i < m.Cards.Length; i++)
                if (m.Cards[i] != null) Card(p, rects[i], m.Cards[i], s, t, namePx);
        }

        private static void Card(ScreenPainter p, Rect r, CardModel c, ConsoleStyle s, float t, int namePx)
        {
            int h = p.Height;
            Color locked = Color.Lerp(ScreenStyle.Back, ScreenStyle.Accent, s.LockedDim);
            Color frame = c.Selected ? Color.Lerp(ScreenStyle.Accent, ScreenStyle.Text, 0.35f) : c.Locked ? locked : ScreenStyle.Accent;
            Color picture = c.Selected ? Color.Lerp(ScreenStyle.Accent, ScreenStyle.Text, 0.2f) : c.Locked ? locked : ScreenStyle.Accent;
            Color name = c.Locked ? ScreenStyle.Dim : ScreenStyle.Text;
            p.Rect(r, Alpha(ScreenStyle.Accent, c.Selected ? s.SelectedFillAlpha : s.CardFillAlpha));
            if (c.Locked) p.Rect(r, new Color(0f, 0f, 0f, 0.30f)); // darker than an open one, still a card
            if (c.Selected) p.Halo(r, 0.025f * h, Alpha(ScreenStyle.Accent, s.HaloAlpha));
            p.Frame(r, c.Selected ? t * s.SelectedFrameScale : t, frame);
            if (c.Aimed) p.Brackets(Inset(r, 3f * t), 0.06f * h, t, Alpha(ScreenStyle.Text, 0.75f));
            // The picture in its own box, the top of the card (the reference's inner frame).
            float pad = 0.06f * r.width;
            Rect box = new(r.x + pad, r.y + pad, r.width - 2f * pad, r.height * 0.58f - pad);
            p.Frame(box, 1f, Alpha(frame, 0.45f));
            p.Picture(Inset(box, 0.08f * box.width), c.Picture, PictureTint(s, picture, c.Locked), s.PictureTintOnly);
            p.TextIn(c.Name, namePx, Style, new Rect(r.x, r.y + r.height * 0.61f, r.width, r.height * 0.13f), TextAnchor.MiddleCenter, name, s.TitleTracking);
            if (!string.IsNullOrEmpty(c.Tag))
                p.TextIn(c.Tag, (int)Px(s.CardTag, h), Style, new Rect(r.x, r.y + r.height * 0.735f, r.width, r.height * 0.09f), TextAnchor.MiddleCenter, c.Locked ? Alpha(ScreenStyle.Warn, 0.85f) : ScreenStyle.Dim, s.TitleTracking * 0.5f);
            // The status row: the lock, and the small HERE chip (secondary by design).
            float rowY = r.y + r.height * 0.84f, rowH = r.height * 0.12f;
            if (c.Locked)
            {
                float g = Px(s.LockGlyph, h);
                p.Glyph(ScreenPainter.GlyphKind.Lock, new Rect(r.center.x - g * 0.4f, rowY + (rowH - g) / 2f, g * 0.8f, g), ScreenStyle.Dim);
            }
            if (c.Here)
            {
                const string word = "HERE";
                int px = (int)Px(s.HereChip, h);
                float chipW = p.Estimate(word, px, Style, 0.08f) + px * 1.2f, chipH = px * 1.6f;
                Rect chip = new(c.Locked ? r.xMax - pad - chipW : r.center.x - chipW / 2f, rowY + (rowH - chipH) / 2f, chipW, chipH);
                p.Frame(chip, 1.5f, ScreenStyle.Dim);
                p.TextIn(word, px, Style, chip, TextAnchor.MiddleCenter, ScreenStyle.Text, 0.08f);
            }
        }

        // The quota board: the state large in the middle, the hint under it, a second
        // rule over the QUOTA / BALANCE foot.
        private static void State(ScreenPainter p, TopModel m, ConsoleStyle s, float margin, Color rule)
        {
            int w = p.Width, h = p.Height;
            // A long state (a refusal: "Nothing to sell — dive again") breaks in two rather than shrinking small.
            Fitted(p, m.BigState, (int)Px(s.BigState, h), new Rect(margin, 0.22f * h, w - 2f * margin, 0.40f * h), TextAnchor.MiddleCenter, Tone(m.BigTone), s.TitleTracking * 0.5f);
            p.TextIn(m.Hint, (int)Px(s.Hint, h), Style, new Rect(margin, 0.63f * h, w - 2f * margin, 0.11f * h), TextAnchor.MiddleCenter, Tone(m.HintTone), s.TitleTracking * 0.2f);
            p.Rect(new Rect(margin, h - margin - Px(s.Foot, h) * 1.3f - 0.03f * h, w - 2f * margin, Mathf.Max(2f, 0.004f * h)), rule);
        }

        // ---- the bottom screen ---------------------------------------------------------

        public static void Bottom(ScreenPainter p, BottomModel m, ConsoleStyle s)
        {
            if (m == null) return;
            int w = p.Width, h = p.Height;
            float t = Thick(s, h);
            p.Grid(new Rect(0f, 0f, w, h), Mathf.Max(8f, s.GridStep * h), Alpha(ScreenStyle.Accent, s.GridAlpha));
            if (m.VoteCard != null) { VoteCard(p, GiveUpRect(w, h, s), m.VoteCard, s, t); return; }
            bool poster = string.IsNullOrEmpty(m.Name);
            float margin = 0.06f * h;
            Rect content = ContentRect(w, h, !poster);
            if (!poster)
                p.TextIn(m.Heading, (int)Px(s.BottomHeading, h), Style, new Rect(margin, margin, w - 2f * margin, 0.13f * h), TextAnchor.MiddleLeft, ScreenStyle.Accent, s.TitleTracking);
            p.Frame(content, 1f, Alpha(ScreenStyle.Accent, 0.5f));
            p.Brackets(content, 0.07f * h, t, ScreenStyle.Accent);
            float pad = 0.05f * h;
            float linePx = Px(s.BottomLine, h);
            if (poster)
            {
                // Nothing to picture: the heading is the large line, the facts under it.
                // With no status under them the facts are the only other thing on the glass:
                // larger, and the block sits lower so the poster fills the screen (the polish pass).
                bool roomy = string.IsNullOrEmpty(m.Status.Text) && !m.Notice;
                float posterLine = roomy ? linePx * 1.25f : linePx;
                p.TextIn(m.Heading, (int)Px(s.PosterHeading, h), Style, new Rect(content.x + pad, content.y + content.height * (roomy ? 0.12f : 0.08f), content.width - 2f * pad, content.height * 0.30f), TextAnchor.MiddleCenter, ScreenStyle.Accent, s.TitleTracking);
                float y = content.y + content.height * (roomy ? 0.50f : 0.44f);
                if (m.Lines != null)
                    foreach (ConsoleLine line in m.Lines)
                    {
                        if (string.IsNullOrEmpty(line.Text)) continue;
                        p.TextIn(line.Text, (int)(line.Size == ConsoleTextSize.Small ? posterLine * 0.85f : posterLine), Style, new Rect(content.x + pad, y, content.width - 2f * pad, posterLine * 1.3f), TextAnchor.MiddleCenter, Tone(line.Tone), s.TitleTracking * 0.3f);
                        y += posterLine * 1.35f;
                    }
                if (m.Notice) Notice(p, new Rect(content.x + pad * 2f, content.yMax - pad - Px(s.BottomNotice, h) * 1.7f, content.width - 4f * pad, Px(s.BottomNotice, h) * 1.7f), m.Status, s, t);
                else if (!string.IsNullOrEmpty(m.Status.Text))
                {
                    float px = Px(m.Status.Size == ConsoleTextSize.Huge ? s.BottomNotice : s.BottomStatus, h);
                    Fitted(p, m.Status.Text, (int)px, new Rect(content.x + pad, content.yMax - pad - px * 1.3f, content.width - 2f * pad, px * 1.3f), TextAnchor.MiddleCenter, Tone(m.Status.Tone), s.TitleTracking * 0.5f);
                }
                return;
            }
            // The picture left, the facts right of a thin divider (the reference's layout).
            Rect picture = new(content.x + pad, content.y + pad, content.width * 0.36f - pad, content.height - 2f * pad);
            p.Frame(picture, 1f, Alpha(ScreenStyle.Accent, 0.35f));
            p.Picture(Inset(picture, 0.08f * picture.width), m.Picture, PictureTint(s, ScreenStyle.Accent, false), s.PictureTintOnly);
            p.Rect(new Rect(picture.xMax + pad * 0.6f, content.y + pad, 1.5f, content.height - 2f * pad), Alpha(ScreenStyle.Accent, 0.5f));
            float colX = picture.xMax + pad * 1.4f;
            Rect col = new(colX, content.y + pad, content.xMax - pad - colX, content.height - 2f * pad);
            float cy = col.y;
            float namePx = Px(s.BottomName, h);
            p.TextIn(m.Name, (int)namePx, Style, new Rect(col.x, cy, col.width, namePx * 1.1f), TextAnchor.MiddleLeft, ScreenStyle.Text, s.TitleTracking);
            cy += namePx * 1.15f;
            if (!string.IsNullOrEmpty(m.SubName))
            {
                float subPx = Px(s.BottomSub, h);
                p.TextIn(m.SubName, (int)subPx, Style, new Rect(col.x, cy, col.width, subPx * 1.2f), TextAnchor.MiddleLeft, ScreenStyle.Accent, s.TitleTracking * 0.5f);
                cy += subPx * 1.3f;
            }
            cy += 0.015f * h;
            if (m.Notice)
            {
                // The refusal takes the facts' place: one block the eye lands on, nothing under it.
                Notice(p, new Rect(col.x, cy + 0.01f * h, col.width, col.yMax - cy - 0.01f * h), m.Status, s, t);
                return;
            }
            // The status line keeps its band at the foot; the facts fit above it, smaller when
            // there are many (a locked site lists its description, the balance and the cost).
            bool status = !string.IsNullOrEmpty(m.Status.Text);
            float statusPx = status ? Px(m.Status.Size == ConsoleTextSize.Huge ? s.BottomNotice : s.BottomStatus, h) : 0f;
            // A long reason (DIVE DONE — END THE DAY FIRST) takes two lines rather than
            // shrinking to a size smaller than the facts (the polish pass: the reason must
            // be the prominent line); the facts above give up the room.
            float tracking = s.TitleTracking * 0.5f;
            int twoPx = 0;
            int statusLines = status ? Lines(p, m.Status.Text, (int)statusPx, col.width, tracking, out _, out _, out twoPx) : 1;
            float statusBand = !status ? 0f : statusLines == 2 ? twoPx * 2.3f : statusPx * 1.15f;
            int count = 0;
            if (m.Lines != null) foreach (ConsoleLine line in m.Lines) if (!string.IsNullOrEmpty(line.Text)) count++;
            float room = col.yMax - statusBand - cy;
            float fit = count > 0 ? Mathf.Min(linePx, room / (count * 1.25f)) : linePx;
            if (m.Lines != null)
                foreach (ConsoleLine line in m.Lines)
                {
                    if (string.IsNullOrEmpty(line.Text)) continue;
                    float px = line.Size == ConsoleTextSize.Small ? fit * 0.85f : fit;
                    p.TextIn(line.Text, (int)px, Style, new Rect(col.x, cy, col.width, fit * 1.2f), TextAnchor.MiddleLeft, Tone(line.Tone), s.TitleTracking * 0.3f);
                    cy += fit * 1.25f;
                }
            if (status)
                Fitted(p, m.Status.Text, (int)statusPx, new Rect(col.x, col.yMax - statusBand, col.width, statusBand), TextAnchor.MiddleLeft, Tone(m.Status.Tone), tracking);
        }

        // How a line is best set in `width`: 1 when it fits (or shrinks only a little),
        // else 2 when breaking it keeps it clearly larger - at " — " (the dash dropped,
        // as the brief writes "DIVE DONE / END THE DAY FIRST"), otherwise at the space
        // nearest the middle. `twoPx` is the size of the two-line setting.
        private static int Lines(ScreenPainter p, string text, int px, float width, float tracking, out string first, out string second, out int twoPx)
        {
            first = text; second = null; twoPx = px;
            float one = p.Estimate(text, px, Style, tracking);
            if (one <= width * 0.98f || !Split(text, out first, out second)) { first = text; second = null; return 1; }
            float oneScale = width * 0.98f / one;
            float wide = Mathf.Max(p.Estimate(first, px, Style, tracking), p.Estimate(second, px, Style, tracking));
            float twoScale = Mathf.Min(1f, wide > 0f ? width * 0.98f / wide : 1f);
            if (twoScale < oneScale * 1.2f) { first = text; second = null; return 1; }
            twoPx = Mathf.Max(6, Mathf.FloorToInt(px * twoScale));
            return 2;
        }

        private static bool Split(string text, out string first, out string second)
        {
            first = text; second = null;
            int dash = text.IndexOf(" — ", System.StringComparison.Ordinal);
            if (dash > 0) { first = text.Substring(0, dash).Trim(); second = text.Substring(dash + 3).Trim(); return first.Length > 0 && second.Length > 0; }
            int best = -1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - text.Length / 2) < Mathf.Abs(best - text.Length / 2))) best = i;
            if (best <= 0) return false;
            first = text.Substring(0, best).Trim(); second = text.Substring(best + 1).Trim();
            return first.Length > 0 && second.Length > 0;
        }

        // One line, or two stacked in the box's height when that keeps the text larger.
        private static void Fitted(ScreenPainter p, string text, int px, Rect box, TextAnchor anchor, Color c, float tracking)
        {
            if (Lines(p, text, px, box.width, tracking, out string first, out string second, out int twoPx) == 1)
            {
                p.TextIn(text, px, Style, box, anchor, c, tracking);
                return;
            }
            twoPx = Mathf.Min(twoPx, Mathf.FloorToInt(box.height / 2.3f));
            float oneWide = p.Estimate(text, px, Style, tracking);
            if (twoPx < px * box.width * 0.98f / Mathf.Max(1f, oneWide) * 1.1f) // the box is too low for two: one line after all
            {
                p.TextIn(text, px, Style, box, anchor, c, tracking);
                return;
            }
            float lineH = twoPx * 1.15f, top = box.center.y - lineH;
            p.TextIn(first, twoPx, Style, new Rect(box.x, top, box.width, lineH), anchor, c, tracking);
            p.TextIn(second, twoPx, Style, new Rect(box.x, top + lineH, box.width, lineH), anchor, c, tracking);
        }

        // A refusal, or any reason an action cannot happen: a framed block the eye lands on.
        private static void Notice(ScreenPainter p, Rect r, ConsoleLine status, ConsoleStyle s, float t)
        {
            Color c = Tone(status.Tone);
            p.Rect(r, Alpha(c, 0.10f));
            p.Frame(r, t * 1.5f, c);
            Fitted(p, status.Text, (int)Px(s.BottomNotice, p.Height), Inset(r, 0.03f * p.Height), TextAnchor.MiddleCenter, c, s.TitleTracking * 0.5f);
        }

        // The red GIVE UP card: the same card language, deliberately dangerous.
        private static void VoteCard(ScreenPainter p, Rect r, VoteCardModel v, ConsoleStyle s, float t)
        {
            int h = p.Height;
            float k = v.Enabled ? 1f : 0.45f;
            Color danger = Scale(ScreenStyle.Danger, k), text = Scale(ScreenStyle.Text, k), dim = Scale(ScreenStyle.Dim, k);
            p.Rect(r, Alpha(danger, v.LocalVoted ? 0.25f : 0.08f));
            if (v.Aimed) p.Halo(r, 0.03f * h, Alpha(danger, s.HaloAlpha));
            p.Frame(r, t * 2f, danger);
            p.Glyph(ScreenPainter.GlyphKind.Chevrons, new Rect(r.x + 2f * t, r.y + 2f * t, r.width - 4f * t, 0.045f * h), Alpha(danger, 0.85f));
            p.TextIn(v.Word, (int)Px(s.VoteWord, h), Style, new Rect(r.x, r.y + r.height * 0.16f, r.width, r.height * 0.32f), TextAnchor.MiddleCenter, v.LocalVoted ? Color.Lerp(danger, text, 0.4f) : danger, s.TitleTracking);
            p.TextIn(v.Count, (int)Px(s.VoteCount, h), Style, new Rect(r.x, r.y + r.height * 0.50f, r.width, r.height * 0.24f), TextAnchor.MiddleCenter, text, s.TitleTracking * 0.5f);
            p.TextIn(v.Foot, (int)Px(s.VoteFoot, h), Style, new Rect(r.x, r.y + r.height * 0.76f, r.width, r.height * 0.16f), TextAnchor.MiddleCenter, v.LocalVoted ? Scale(ScreenStyle.Warn, k) : dim, s.TitleTracking * 0.5f);
        }

        // ---- the lever sign ------------------------------------------------------------

        public static void Sign(ScreenPainter p, SignModel m, ConsoleStyle s)
        {
            int w = p.Width, h = p.Height;
            float k = m.Enabled ? 1f : s.DisabledSign;
            Color tone = Scale(Tone(m.Tone), k), warn = Scale(ScreenStyle.Warn, k);
            float inset = 0.05f * h, t = Mathf.Max(2f, s.SignFrame * h);
            Rect frame = new(inset, inset, w - 2f * inset, h - 2f * inset);
            p.Rect(frame, Alpha(tone, m.Enabled ? 0.12f : 0.05f)); // a lit plate glows a little behind its word
            if (m.Aimed) p.Halo(frame, 0.05f * h, Alpha(tone, s.HaloAlpha));
            p.Frame(frame, t, tone);
            float chev = s.SignChevrons * h;
            p.Glyph(ScreenPainter.GlyphKind.Chevrons, new Rect(frame.x + t, frame.yMax - t - chev, frame.width - 2f * t, chev), warn);
            // The word between two end bars, as the reference's plate reads. A long word
            // (UNLOCK $100) is width-limited on this 0.25 x 0.11 m plate: then the bars go
            // and the tracking tightens so the word keeps as much height as the width allows.
            int wordPx = (int)Px(s.SignWord, h);
            float barW = Mathf.Max(2f, 0.025f * h), barPad = 0.10f * h;
            Rect word = new(frame.x + barPad + barW + 0.04f * h, frame.y + t, frame.width - 2f * (barPad + barW + 0.04f * h), frame.yMax - t - chev - frame.y - t);
            float tracking = s.TitleTracking;
            bool bars = p.Estimate(m.Word, wordPx, Style, tracking) <= word.width;
            if (bars)
            {
                p.Rect(new Rect(frame.x + barPad, word.y + word.height * 0.25f, barW, word.height * 0.5f), warn);
                p.Rect(new Rect(frame.xMax - barPad - barW, word.y + word.height * 0.25f, barW, word.height * 0.5f), warn);
            }
            else
            {
                float edge = t + 0.03f * h;
                word = new Rect(frame.x + edge, word.y, frame.width - 2f * edge, word.height);
                tracking *= 0.4f;
            }
            p.TextIn(m.Word, wordPx, Style, word, TextAnchor.MiddleCenter, tone, tracking);
            if (!m.Enabled) p.Rect(new Rect(0f, 0f, w, h), Alpha(ScreenStyle.Back, 0.55f));
        }
    }
}
