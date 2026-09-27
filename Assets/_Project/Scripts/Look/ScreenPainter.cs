using System;
using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Look
{
    // A pixel canvas for the console's screens (the shared console, 27 September
    // 2026; the render scout's RENDER.md): fills, frames, halos, lines, a grid,
    // corner brackets, pictures, glyphs and text, drawn with immediate-mode GL into
    // an sRGB RenderTexture, y down from the top-left, in ScreenStyle's linear
    // colours (the sRGB target encodes them, so the screen shows exactly the
    // palette every other display uses). Text comes from a dynamic font's atlas at
    // the exact pixel size asked for, with letter-spacing the TextMeshes cannot do.
    // Paint() runs the drawing twice: first collecting every string so the atlas
    // holds all of them before a glyph is drawn (a rebuild mid-paint would move the
    // ones already on the canvas), then for real. Works in edit mode and in play,
    // outside any camera.
    public sealed class ScreenPainter : IDisposable
    {
        public enum GlyphKind { Lock, Here, Chevrons, Dot }

        public const string ShaderName = "Sunk Cost/Screen Paint";
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int AlphaOnlyId = Shader.PropertyToID("_AlphaOnly");

        public int Width { get; }
        public int Height { get; }
        public Font Font { get; }

        // True on the first pass: nothing is drawn, strings are only requested.
        public bool Collecting { get; private set; }

        private readonly Material shapes, text, picture;
        private readonly RenderTexture previousActive;
        private readonly bool previousSrgb;
        private readonly RenderTexture target;
        private Material current;
        private Texture currentTexture;
        private float currentMode;
        private bool inBatch;
        private readonly HashSet<string> requested = new();

        private static Material shapesShared, textShared, pictureShared;
        private static Shader shaderShared;

        // Paints `draw` onto `target`: a collecting pass, then the real one.
        public static bool Paint(RenderTexture target, Color clear, Font font, Shader shader, Action<ScreenPainter> draw)
        {
            if (target == null || draw == null) return false;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) { Debug.LogWarning("[ScreenPainter] no font"); return false; }
            if (!EnsureMaterials(shader)) return false;
            // Pass one: every string of this paint into the atlas.
            var collector = new ScreenPainter(target, font, true);
            draw(collector);
            // Pass two: the drawing.
            using (var painter = new ScreenPainter(target, font, false))
            {
                painter.Begin(clear);
                draw(painter);
            }
            return true;
        }

        private static bool EnsureMaterials(Shader shader)
        {
            if (shader == null) shader = shaderShared != null ? shaderShared : Shader.Find(ShaderName);
            if (shader == null) { Debug.LogWarning("[ScreenPainter] shader " + ShaderName + " not found"); return false; }
            if (shapesShared == null || shapesShared.shader != shader)
            {
                shaderShared = shader;
                shapesShared = new Material(shader) { name = "Screen paint shapes", hideFlags = HideFlags.HideAndDontSave };
                textShared = new Material(shader) { name = "Screen paint text", hideFlags = HideFlags.HideAndDontSave };
                pictureShared = new Material(shader) { name = "Screen paint picture", hideFlags = HideFlags.HideAndDontSave };
            }
            return true;
        }

        private ScreenPainter(RenderTexture target, Font font, bool collecting)
        {
            this.target = target;
            Width = target.width;
            Height = target.height;
            Font = font;
            Collecting = collecting;
            shapes = shapesShared; text = textShared; picture = pictureShared;
            if (!collecting)
            {
                previousActive = RenderTexture.active;
                previousSrgb = GL.sRGBWrite;
            }
        }

        private void Begin(Color clear)
        {
            RenderTexture.active = target;
            GL.sRGBWrite = true;
            GL.Clear(true, true, clear);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Width, Height, 0f); // y down, pixel units
        }

        public void Dispose()
        {
            if (Collecting) return;
            EndBatch();
            GL.PopMatrix();
            GL.sRGBWrite = previousSrgb;
            RenderTexture.active = previousActive;
            if (target.useMipMap && !target.autoGenerateMips) target.GenerateMips();
        }

        // ---- batches -------------------------------------------------------------------

        private void Use(Material material, Texture texture, float mode)
        {
            if (inBatch && current == material && currentTexture == texture && Mathf.Approximately(currentMode, mode)) return;
            EndBatch();
            material.SetTexture(MainTexId, texture);
            material.SetFloat(AlphaOnlyId, mode);
            material.SetPass(0);
            GL.Begin(GL.QUADS);
            inBatch = true; current = material; currentTexture = texture; currentMode = mode;
        }

        private void EndBatch()
        {
            if (!inBatch) return;
            GL.End();
            inBatch = false; current = null; currentTexture = null;
        }

        private static void Quad(float x0, float y0, float x1, float y1, Color c, float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
        {
            GL.Color(c);
            GL.TexCoord2(u0, v0); GL.Vertex3(x0, y0, 0f);
            GL.TexCoord2(u1, v0); GL.Vertex3(x1, y0, 0f);
            GL.TexCoord2(u1, v1); GL.Vertex3(x1, y1, 0f);
            GL.TexCoord2(u0, v1); GL.Vertex3(x0, y1, 0f);
        }

        // ---- shapes --------------------------------------------------------------------

        public void Rect(Rect r, Color c)
        {
            if (Collecting || c.a <= 0f || r.width <= 0f || r.height <= 0f) return;
            Use(shapes, Texture2D.whiteTexture, 0f);
            Quad(r.xMin, r.yMin, r.xMax, r.yMax, c);
        }

        // Four strips inside the rect's edge.
        public void Frame(Rect r, float thickness, Color c)
        {
            if (Collecting || c.a <= 0f) return;
            float t = Mathf.Max(1f, thickness);
            Rect(new Rect(r.xMin, r.yMin, r.width, t), c);
            Rect(new Rect(r.xMin, r.yMax - t, r.width, t), c);
            Rect(new Rect(r.xMin, r.yMin + t, t, r.height - 2f * t), c);
            Rect(new Rect(r.xMax - t, r.yMin + t, t, r.height - 2f * t), c);
        }

        // The painted glow: frames outside the rect fading out over `spread` pixels.
        public void Halo(Rect r, float spread, Color c)
        {
            if (Collecting || c.a <= 0f) return;
            const int steps = 4;
            float t = Mathf.Max(1f, spread / steps);
            for (int i = 0; i < steps; i++)
            {
                float a = c.a * (1f - (float)i / steps) * (1f - (float)i / steps);
                Rect grown = new(r.xMin - t * (i + 1), r.yMin - t * (i + 1), r.width + 2f * t * (i + 1), r.height + 2f * t * (i + 1));
                Frame(grown, t, new Color(c.r, c.g, c.b, a));
            }
        }

        // A line of `thickness` pixels between two points (a quad, so any direction works).
        public void Line(Vector2 a, Vector2 b, float thickness, Color c)
        {
            if (Collecting || c.a <= 0f) return;
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (Mathf.Max(1f, thickness) / 2f);
            Use(shapes, Texture2D.whiteTexture, 0f);
            GL.Color(c);
            GL.TexCoord2(0f, 0f); GL.Vertex3(a.x + n.x, a.y + n.y, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3(b.x + n.x, b.y + n.y, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(b.x - n.x, b.y - n.y, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(a.x - n.x, a.y - n.y, 0f);
        }

        // The subtle technical background: one-pixel lines every `stepPx`.
        public void Grid(Rect r, float stepPx, Color c)
        {
            if (Collecting || c.a <= 0f || stepPx < 2f) return;
            for (float x = r.xMin + stepPx; x < r.xMax; x += stepPx) Rect(new Rect(x, r.yMin, 1f, r.height), c);
            for (float y = r.yMin + stepPx; y < r.yMax; y += stepPx) Rect(new Rect(r.xMin, y, r.width, 1f), c);
        }

        // Corner ticks, `arm` pixels long, like the HUD's prompt panel.
        public void Brackets(Rect r, float arm, float thickness, Color c)
        {
            if (Collecting || c.a <= 0f) return;
            float t = Mathf.Max(1f, thickness);
            arm = Mathf.Min(arm, r.width / 2f, r.height / 2f);
            Rect(new Rect(r.xMin, r.yMin, arm, t), c); Rect(new Rect(r.xMin, r.yMin, t, arm), c);
            Rect(new Rect(r.xMax - arm, r.yMin, arm, t), c); Rect(new Rect(r.xMax - t, r.yMin, t, arm), c);
            Rect(new Rect(r.xMin, r.yMax - t, arm, t), c); Rect(new Rect(r.xMin, r.yMax - arm, t, arm), c);
            Rect(new Rect(r.xMax - arm, r.yMax - t, arm, t), c); Rect(new Rect(r.xMax - t, r.yMax - arm, t, arm), c);
        }

        // A picture fitted inside `r`, keeping its aspect. `tintOnly`: the picture's alpha
        // in the tint's colour (a silhouette); else its own colours times the tint.
        public void Picture(Rect r, Texture2D tex, Color tint, bool tintOnly)
        {
            if (Collecting || tex == null || tint.a <= 0f || r.width <= 1f || r.height <= 1f) return;
            float aspect = (float)tex.width / Mathf.Max(1, tex.height);
            float w = r.width, h = w / aspect;
            if (h > r.height) { h = r.height; w = h * aspect; }
            float x0 = r.center.x - w / 2f, y0 = r.center.y - h / 2f;
            Use(picture, tex, tintOnly ? 1f : 0f);
            Quad(x0, y0, x0 + w, y0 + h, tint, 0f, 1f, 1f, 0f); // texture v runs up; our y runs down
        }

        // ---- text ----------------------------------------------------------------------

        private void Request(string s, int px, FontStyle style)
        {
            if (string.IsNullOrEmpty(s) || px < 1) return;
            string key = px + ":" + (int)style + ":" + s;
            if (requested.Add(key)) Font.RequestCharactersInTexture(s, px, style);
        }

        // Width, the tallest glyph above the baseline, the deepest below (pixels).
        public Vector3 MeasureExtents(string s, int px, FontStyle style, float tracking = 0f)
        {
            if (string.IsNullOrEmpty(s) || px < 1) return Vector3.zero;
            Request(s, px, style);
            float w = 0f, above = 0f, below = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                if (!Font.GetCharacterInfo(s[i], out CharacterInfo ci, px, style)) continue;
                w += ci.advance + tracking * px;
                above = Mathf.Max(above, ci.maxY);
                below = Mathf.Max(below, -ci.minY);
            }
            if (s.Length > 0) w -= tracking * px;
            return new Vector3(Mathf.Max(0f, w), above, below);
        }

        public Vector2 Measure(string s, int px, FontStyle style, float tracking = 0f)
        {
            Vector3 e = MeasureExtents(s, px, style, tracking);
            return new Vector2(e.x, e.y + e.z);
        }

        // Draws from the baseline's left end; returns the advance.
        public float Text(string s, int px, FontStyle style, Vector2 baselineLeft, Color c, float tracking = 0f)
        {
            if (string.IsNullOrEmpty(s) || px < 1) return 0f;
            Request(s, px, style);
            if (Collecting || c.a <= 0f) return MeasureExtents(s, px, style, tracking).x;
            Texture atlas = Font.material != null ? Font.material.mainTexture : null;
            if (atlas == null) return 0f;
            Use(text, atlas, 1f);
            float x = baselineLeft.x, y = baselineLeft.y;
            for (int i = 0; i < s.Length; i++)
            {
                if (!Font.GetCharacterInfo(s[i], out CharacterInfo ci, px, style)) continue;
                float x0 = x + ci.minX, x1 = x + ci.maxX, top = y - ci.maxY, bottom = y - ci.minY;
                GL.Color(c);
                GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x0, top, 0f);
                GL.TexCoord(ci.uvTopRight); GL.Vertex3(x1, top, 0f);
                GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x1, bottom, 0f);
                GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x0, bottom, 0f);
                x += ci.advance + tracking * px;
            }
            return x - baselineLeft.x;
        }

        // Text placed in a box by anchor, shrunk to fit its width when asked. Returns the pixel size used.
        public int TextIn(string s, int px, FontStyle style, Rect box, TextAnchor anchor, Color c, float tracking = 0f, bool shrinkToFit = true)
        {
            if (string.IsNullOrEmpty(s) || px < 1) return px;
            Vector3 e = MeasureExtents(s, px, style, tracking);
            if (shrinkToFit && e.x > box.width && e.x > 0f)
            {
                px = Mathf.Max(6, Mathf.FloorToInt(px * box.width / e.x));
                e = MeasureExtents(s, px, style, tracking);
                if (e.x > box.width && px > 6) { px = Mathf.Max(6, Mathf.FloorToInt(px * box.width / e.x)); e = MeasureExtents(s, px, style, tracking); }
            }
            float x = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft ? box.xMin
                : anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight ? box.xMax - e.x
                : box.center.x - e.x / 2f;
            float baseline = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.UpperRight ? box.yMin + e.y
                : anchor == TextAnchor.LowerLeft || anchor == TextAnchor.LowerCenter || anchor == TextAnchor.LowerRight ? box.yMax - e.z
                : box.center.y + (e.y - e.z) / 2f;
            Text(s, px, style, new Vector2(x, baseline), c, tracking);
            return px;
        }

        // ---- glyphs (vector shapes, no textures) -----------------------------------------

        public void Glyph(GlyphKind kind, Rect r, Color c)
        {
            if (Collecting || c.a <= 0f) return;
            switch (kind)
            {
                case GlyphKind.Lock:
                {
                    // The body, and the shackle standing on it.
                    float bodyH = r.height * 0.55f, bodyW = r.width;
                    Rect body = new(r.xMin, r.yMax - bodyH, bodyW, bodyH);
                    Rect(body, c);
                    float t = Mathf.Max(1.5f, r.height * 0.14f);
                    float sw = r.width * 0.6f, sh = r.height * 0.55f;
                    Rect shackle = new(r.center.x - sw / 2f, r.yMin, sw, sh);
                    Rect(new Rect(shackle.xMin, shackle.yMin, sw, t), c);
                    Rect(new Rect(shackle.xMin, shackle.yMin, t, sh), c);
                    Rect(new Rect(shackle.xMax - t, shackle.yMin, t, sh), c);
                    // The keyhole, cut in the glass colour.
                    float k = Mathf.Max(2f, r.width * 0.16f);
                    Rect(new Rect(r.center.x - k / 2f, body.center.y - k * 0.8f, k, k * 1.6f), new Color(ScreenStyle.Back.r, ScreenStyle.Back.g, ScreenStyle.Back.b, c.a));
                    break;
                }
                case GlyphKind.Dot:
                {
                    float s = Mathf.Min(r.width, r.height);
                    Rect(new Rect(r.center.x - s / 2f, r.center.y - s / 2f, s, s), c);
                    break;
                }
                case GlyphKind.Chevrons:
                {
                    // Alternating parallelograms leaning 45 degrees along the band.
                    float h = r.height, step = h;
                    Color dark = new(0.02f, 0.02f, 0.02f, c.a);
                    Use(shapes, Texture2D.whiteTexture, 0f);
                    int i = 0;
                    for (float x = r.xMin - h; x < r.xMax; x += step, i++)
                    {
                        Color col = (i & 1) == 0 ? c : dark;
                        float xa0 = Mathf.Clamp(x, r.xMin, r.xMax), xa1 = Mathf.Clamp(x + step, r.xMin, r.xMax);
                        float xb0 = Mathf.Clamp(x + h, r.xMin, r.xMax), xb1 = Mathf.Clamp(x + h + step, r.xMin, r.xMax);
                        GL.Color(col);
                        GL.TexCoord2(0f, 0f); GL.Vertex3(xb0, r.yMin, 0f);
                        GL.TexCoord2(1f, 0f); GL.Vertex3(xb1, r.yMin, 0f);
                        GL.TexCoord2(1f, 1f); GL.Vertex3(xa1, r.yMax, 0f);
                        GL.TexCoord2(0f, 1f); GL.Vertex3(xa0, r.yMax, 0f);
                    }
                    break;
                }
                case GlyphKind.Here:
                    // A chip: drawn by the console paint with its word; here only the frame.
                    Frame(r, Mathf.Max(1f, r.height * 0.08f), c);
                    break;
            }
        }
    }
}
