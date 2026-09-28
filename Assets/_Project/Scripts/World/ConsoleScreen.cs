using System;
using SunkCost.Look;
using UnityEngine;

namespace SunkCost.World
{
    // One painted surface of the shared console (27 September 2026; RENDER.md): the
    // top screen, the bottom screen or the lever sign. Owns an sRGB RenderTexture
    // sized from the glass's metres and the style's pixel density, painted by
    // ScreenPainter only when the content's key changes (never per frame), and shown
    // on the child glass quad as the ConsoleGlass material's emission map through a
    // MaterialPropertyBlock - the shared material asset is never touched, no material
    // instance is ever made, so a prefab built in the editor stays clean. Works in
    // edit mode (the builders' ShowIdle, LookCapture) and in play; a lost texture (a
    // domain reload, a device reset) or a rebuilt font atlas repaints the last content.
    // The root's +Z faces the reader; its +X is the reader's LEFT (ShipMonitor's rule).
    [ExecuteAlways]
    public sealed class ConsoleScreen : MonoBehaviour
    {
        public const string GlassName = "Glass";
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static Mesh quadMesh;

        [SerializeField] private string surfaceName = ConsolePaint.TopSurface;
        [SerializeField] private Vector2 metres = new(1f, 0.5f);
        [SerializeField] private Renderer glass;

        // A painted texture is self-contained: the glyphs were rasterised into it, so a
        // font-atlas rebuild AFTER a paint cannot stale it. Only a rebuild DURING a paint
        // can (glyphs drawn before it moved), and PaintNow runs the pass again for that;
        // a surface whose every pass meets a rebuild (an atlas at its cap, thrashed by the
        // consoles' own glyphs) retries no faster than this, so nine surfaces on a host
        // can never repaint every frame. The rebuild count is the testers' storm gauge.
        public const float RetrySeconds = 0.5f;
        public const float MipBias = -0.5f;
        public static int AtlasRebuilds { get; private set; }      // Font.textureRebuilt events for the console font since the domain loaded
        private static bool counting;
        private static int rebuildsInWindow;
        private static float windowStart;

        private RenderTexture texture;
        private MaterialPropertyBlock block;
        private string lastKey;
        private Action<ScreenPainter> lastDraw;
        private bool dirty, painting;
        private float paintedEmission = -1f;
        private float nextRetry;

        public string SurfaceName => surfaceName;
        public Vector2 Metres => metres;
        public Renderer Glass => glass;
        public RenderTexture Texture => texture;
        public string LastKey => lastKey;
        public int Width { get { Size(out int w, out _); return w; } }
        public int Height { get { Size(out _, out int h); return h; } }

        // The builder's setup: the name, the glass's size in metres, and the child quad
        // wearing the shared glass material (ConsoleBuilder.EnsureAssets makes it).
        public void Configure(string name, Vector2 size, Material glassMaterial)
        {
            surfaceName = name;
            metres = new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
            Transform t = ScreenStyle.Child(transform, GlassName, out _);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, 180f, 0f); // a quad is seen from its -Z: turned to face the root's +Z
            t.localScale = new Vector3(metres.x, metres.y, 1f);
            MeshFilter filter = t.GetComponent<MeshFilter>();
            if (filter == null) filter = t.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = Quad();
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (glassMaterial != null) renderer.sharedMaterial = glassMaterial;
            glass = renderer;
            lastKey = null;
        }

        public static Mesh Quad()
        {
            if (quadMesh == null) quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            return quadMesh;
        }

        // The texture's size: the style's pixels per metre, a multiple of 8, capped.
        private void Size(out int w, out int h)
        {
            ConsoleStyle s = ConsoleStyle.Resolve();
            float aspect = metres.y / Mathf.Max(0.001f, metres.x);
            w = Mathf.Clamp(Mathf.RoundToInt(metres.x * s.PixelsPerMetre / 8f) * 8, Mathf.Max(64, s.MinWidth), Mathf.Max(64, s.MaxWidth));
            h = Mathf.Max(16, Mathf.RoundToInt(w * aspect / 8f) * 8);
        }

        // A pixel rectangle (y down from the reader's top-left) as a centre and a size
        // in metres in this root's space (+X = the reader's left, +Y up).
        public Vector2 LocalCentre(Rect px)
        {
            Size(out int w, out int h);
            return new Vector2((0.5f - px.center.x / w) * metres.x, (0.5f - px.center.y / h) * metres.y);
        }

        public Vector2 LocalSize(Rect px)
        {
            Size(out int w, out int h);
            return new Vector2(px.width / w * metres.x, px.height / h * metres.y);
        }

        // Paints `draw` when `key` differs from the last one painted (or the texture was
        // remade, or the font atlas was rebuilt). Returns whether it painted.
        public bool Paint(string key, Action<ScreenPainter> draw)
        {
            if (draw == null) return false;
            bool fresh = EnsureTexture();
            ConsoleStyle s = ConsoleStyle.Resolve();
            if (!fresh && !dirty && key == lastKey && Mathf.Approximately(paintedEmission, s.Emission)) return false;
            lastDraw = draw;
            lastKey = key;
            return PaintNow();
        }

        // The last content again (a lost texture, a rebuilt atlas).
        public bool RepaintNow()
        {
            if (lastDraw == null) return false;
            EnsureTexture();
            return PaintNow();
        }

        private bool PaintNow()
        {
            if (texture == null || lastDraw == null) return false;
            ConsoleStyle s = ConsoleStyle.Resolve();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                dirty = false;
                painting = true;
                bool ok;
                try { ok = ScreenPainter.Paint(texture, ScreenStyle.Back, s.ResolvedFont, s.PaintShader, lastDraw); }
                finally { painting = false; }
                if (!ok) { lastKey = null; return false; }
                if (!dirty) break; // the atlas was rebuilt under the first pass: once more, with every glyph in
            }
            if (dirty) nextRetry = Time.realtimeSinceStartup + RetrySeconds; // rebuilt under both passes: Update tries again, no sooner than this
            Apply(s);
            return true;
        }

        private void Apply(ConsoleStyle s)
        {
            if (glass == null) return;
            block ??= new MaterialPropertyBlock();
            block.SetTexture(EmissionMapId, texture != null ? (Texture)texture : Texture2D.blackTexture);
            block.SetColor(EmissionColorId, texture != null ? Color.white * s.Emission : Color.black);
            glass.SetPropertyBlock(block);
            paintedEmission = s.Emission;
        }

        // The texture at the current size; true when it was (re)made or (re)created.
        private bool EnsureTexture()
        {
            Size(out int w, out int h);
            if (texture != null && texture.width == w && texture.height == h)
            {
                if (texture.IsCreated()) return false;
                texture.Create();
                return true;
            }
            Release();
            texture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = surfaceName,
                hideFlags = HideFlags.DontSave,
                useMipMap = true,
                autoGenerateMips = false,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                // The polish pass (28 September 2026; Dan: "blurry from the side"): the
                // strongest anisotropic filtering, whatever the quality level asks (the
                // Mobile level filters per texture), so a screen seen at 45-60 degrees
                // keeps its vertical detail; and a half-mip sharpening bias, because from
                // the standing distance the top screen is minified ~2.7x and trilinear
                // blended half of a 480 px mip into 700 screen pixels.
                anisoLevel = 16,
                mipMapBias = MipBias,
            };
            texture.Create();
            return true;
        }

        private void Release()
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
            texture = null;
            lastKey = null;
            Apply(ConsoleStyle.Resolve()); // the glass goes dark, never a dangling map
        }

        private void OnEnable()
        {
            lastKey = null;
            Font.textureRebuilt += OnFontRebuilt;
            if (!counting) { counting = true; Font.textureRebuilt += CountRebuild; }
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            Release();
        }

        // Only a rebuild under this surface's own paint matters (see RetrySeconds).
        private void OnFontRebuilt(Font font)
        {
            if (painting && font == ConsoleStyle.Resolve().ResolvedFont) dirty = true;
        }

        // The storm gauge: every rebuild of the console font, and a warning when they
        // come faster than a handful in five seconds (the atlas is at its cap).
        private static void CountRebuild(Font font)
        {
            if (font != ConsoleStyle.Resolve().ResolvedFont) return;
            AtlasRebuilds++;
            float now = Time.realtimeSinceStartup;
            if (now - windowStart > 5f) { windowStart = now; rebuildsInWindow = 0; }
            if (++rebuildsInWindow == 6)
                Debug.LogWarning($"[ConsoleScreen] the font atlas was rebuilt {rebuildsInWindow} times in 5 s ({AtlasRebuilds} in all): the console text is thrashing it — {(font.material != null && font.material.mainTexture != null ? font.material.mainTexture.width + "x" + font.material.mainTexture.height : "?")}");
        }

        private void Update()
        {
            if (lastDraw == null || painting) return;
            if (texture == null || !texture.IsCreated()) { EnsureTexture(); PaintNow(); }
            else if (dirty && Time.realtimeSinceStartup >= nextRetry) PaintNow();
        }
    }
}
