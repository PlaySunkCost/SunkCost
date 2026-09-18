using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The look kit's textures, from code (Dan, 18 September 2026: "peak style" —
    // no bought assets; everything regenerates with Create or Update HQ). Each
    // set is an albedo, a normal map and a metallic/smoothness mask, written as
    // PNGs under Assets/_Project/Art/HQ/Textures and imported with the right
    // settings. The style is the reference picture's: hard panels, flat colour,
    // a little grime, rust where water runs, worn paint on the edges — a rig of
    // 2300 that has been used. Deterministic: the same seed gives the same
    // pixels, so a rebuild changes nothing Dan did not change.
    public static class ProceduralTextures
    {
        public const string Folder = "Assets/_Project/Art/HQ/Textures";
        public const int Size = 512;

        // One texture set on disk: albedo (sRGB), normal (tangent space), mask
        // (metallic in R, smoothness in A; linear).
        public readonly struct Set
        {
            public readonly Texture2D Albedo, Normal, Mask;
            public Set(Texture2D albedo, Texture2D normal, Texture2D mask) { Albedo = albedo; Normal = normal; Mask = mask; }
        }

        // ---- the sets -----------------------------------------------------------------

        // Deck plate: dark steel with a faint tread, plate seams every tile,
        // rust in the seams and grime pooled in the low spots. One tile = 4 m.
        public static Set DeckPlate() => Build("DeckPlate", 11, (x, y, p) =>
        {
            float seam = Seam(x, y, 512, 4f);
            float tread = Tread(x, y);
            float grime = Fbm(x, y, 6f, 4, 11) * 0.5f + 0.5f;
            float rust = Mathf.Clamp01((Fbm(x + 300, y + 77, 3f, 4, 12) - 0.25f) * 1.6f) * Mathf.Clamp01(seam * 1.5f + 0.15f);
            Color steel = Color.Lerp(new Color(0.20f, 0.21f, 0.23f), new Color(0.27f, 0.28f, 0.30f), grime);
            Color rusty = new Color(0.36f, 0.19f, 0.10f);
            Color c = Color.Lerp(steel, rusty, rust);
            c = Color.Lerp(c, c * 0.75f, tread * 0.5f);
            c = Color.Lerp(c, c * 0.55f, seam);
            p.Albedo = c;
            p.Height = -seam * 0.8f + tread * 0.25f - rust * 0.1f;
            p.Metallic = 0.45f - rust * 0.4f;
            p.Smoothness = 0.30f - rust * 0.2f - grime * 0.1f;
        });

        // Hull panel: riveted plates three metres square, worn navy paint, chips to
        // bare steel on the edges, rust bleeding down from the rivets. One tile = 3 m.
        public static Set HullPanel() => Build("HullPanel", 21, (x, y, p) =>
        {
            float seam = Seam(x, y, 512, 4f);
            float rivet = Rivets(x, y, 512, 26, 7f);
            float wear = Mathf.Clamp01((Fbm(x + 50, y + 900, 5f, 4, 22) - 0.3f) * 2.2f) * Mathf.Clamp01(seam * 2f + 0.25f);
            float drip = Drips(x, y, 23) * Mathf.Clamp01(1f - seam);
            float grime = Fbm(x, y, 3f, 3, 24) * 0.5f + 0.5f;
            Color paint = Color.Lerp(new Color(0.15f, 0.18f, 0.24f), new Color(0.22f, 0.26f, 0.32f), grime);
            Color bare = new Color(0.42f, 0.42f, 0.44f);
            Color rust = new Color(0.40f, 0.20f, 0.09f);
            Color c = Color.Lerp(paint, bare, wear);
            c = Color.Lerp(c, rust, drip * 0.8f);
            c = Color.Lerp(c, c * 0.6f, seam);
            c = Color.Lerp(c, c * 1.15f, rivet * 0.6f);
            p.Albedo = c;
            p.Height = -seam * 0.6f + rivet * 0.9f;
            p.Metallic = 0.2f + wear * 0.6f;
            p.Smoothness = 0.35f + wear * 0.2f - drip * 0.3f;
        });

        // Rusted leg: heavy rust with dark pitting and long vertical streaks, a
        // little of the old red paint left in the dry patches. One tile = 2 m.
        public static Set RustSteel() => Build("RustSteel", 31, (x, y, p) =>
        {
            float body = Fbm(x, y, 4f, 5, 32) * 0.5f + 0.5f;
            float pit = Mathf.Clamp01((Fbm(x + 800, y, 14f, 3, 33) - 0.35f) * 3f);
            float streak = Drips(x, y, 34);
            float paint = Mathf.Clamp01((Fbm(x, y + 400, 2.5f, 3, 35) - 0.45f) * 4f);
            Color rustLight = new Color(0.55f, 0.29f, 0.14f), rustDark = new Color(0.24f, 0.12f, 0.07f), oldPaint = new Color(0.50f, 0.17f, 0.12f);
            Color c = Color.Lerp(rustDark, rustLight, body);
            c = Color.Lerp(c, rustDark * 0.7f, pit);
            c = Color.Lerp(c, rustDark, streak * 0.5f);
            c = Color.Lerp(c, oldPaint, paint * 0.7f);
            p.Albedo = c;
            p.Height = -pit * 0.7f + body * 0.2f;
            p.Metallic = 0.15f + paint * 0.3f;
            p.Smoothness = 0.15f + paint * 0.3f;
        });

        // Hazard stripes: yellow and black diagonals, the yellow scuffed to the
        // steel where feet and crates go. One tile = 1 m.
        public static Set Hazard() => Build("Hazard", 41, (x, y, p) =>
        {
            float diag = Mathf.Repeat((x + y) / 64f, 1f);
            float stripe = diag < 0.5f ? 1f : 0f;
            float scuff = Mathf.Clamp01((Fbm(x, y, 5f, 4, 42) - 0.2f) * 1.8f);
            Color yellow = new Color(0.93f, 0.72f, 0.12f), black = new Color(0.09f, 0.09f, 0.10f), steel = new Color(0.38f, 0.38f, 0.40f);
            Color c = Color.Lerp(black, yellow, stripe);
            c = Color.Lerp(c, steel, scuff * 0.55f);
            p.Albedo = c;
            p.Height = -scuff * 0.3f;
            p.Metallic = 0.3f + scuff * 0.5f;
            p.Smoothness = 0.5f - scuff * 0.25f;
        });

        // Plank wood for the board over the water: grey weathered boards, the
        // grain along x, a gap between boards. One tile = 1 m across four boards.
        public static Set Plank() => Build("Plank", 51, (x, y, p) =>
        {
            int board = Mathf.FloorToInt(y / 128f);
            float gap = Mathf.Abs(Mathf.Repeat(y, 128f) - 64f) > 60f ? 1f : 0f;
            float grain = Fbm(x * 0.15f, y * 2f + board * 131, 4f, 4, 52 + board) * 0.5f + 0.5f;
            float knot = Mathf.Clamp01((Fbm(x + board * 77, y, 9f, 2, 53) - 0.55f) * 6f);
            Color light = new Color(0.52f, 0.45f, 0.36f), dark = new Color(0.33f, 0.28f, 0.22f);
            Color c = Color.Lerp(dark, light, grain);
            c = Color.Lerp(c, dark * 0.8f, knot);
            c = Color.Lerp(c, dark * 0.4f, gap);
            p.Albedo = c;
            p.Height = -gap * 1f + grain * 0.15f;
            p.Metallic = 0f;
            p.Smoothness = 0.2f;
        });

        // Painted panel for crates and booth trim: flat paint with a stencil band
        // and chipped edges; the material's colour tints it. One tile = 1 m.
        public static Set PaintedPanel() => Build("PaintedPanel", 61, (x, y, p) =>
        {
            float edge = Mathf.Max(Edge(x, 256, 10f), Edge(y, 256, 10f));
            float chip = Mathf.Clamp01((Fbm(x, y, 7f, 4, 62) - 0.3f) * 2.5f) * Mathf.Clamp01(edge * 1.3f + 0.1f);
            float band = Mathf.Repeat(y, 256f) > 96f && Mathf.Repeat(y, 256f) < 128f ? 1f : 0f;
            float grime = Fbm(x, y, 3f, 3, 63) * 0.5f + 0.5f;
            Color paint = Color.Lerp(new Color(0.86f, 0.86f, 0.86f), new Color(0.72f, 0.72f, 0.72f), grime);
            Color steel = new Color(0.40f, 0.40f, 0.42f);
            Color c = Color.Lerp(paint, paint * 0.8f, band);
            c = Color.Lerp(c, steel, chip);
            p.Albedo = c;
            p.Height = -chip * 0.4f - edge * 0.5f;
            p.Metallic = 0.1f + chip * 0.6f;
            p.Smoothness = 0.4f;
        });

        // Water normals: faint crossing ripples (whole periods per tile, so it
        // tiles) under a fractal chop; the wave surface scrolls it. Albedo unused.
        // One tile = 12 m; the material keeps the bump scale low.
        public static Set WaterRipple() => Build("WaterRipple", 71, (x, y, p) =>
        {
            float a = Mathf.Sin((2f * x + 3f * y) / 512f * Mathf.PI * 2f);
            float b = Mathf.Sin((-3f * x + 2f * y) / 512f * Mathf.PI * 2f);
            float ripple = Fbm(x, y, 12f, 4, 72);
            p.Albedo = new Color(0.05f, 0.12f, 0.18f);
            p.Height = a * 0.08f + b * 0.06f + ripple * 0.35f;
            p.Metallic = 0f;
            p.Smoothness = 0.95f;
        });

        public static void GenerateAll()
        {
            DeckPlate(); HullPanel(); RustSteel(); Hazard(); Plank(); PaintedPanel(); WaterRipple();
        }

        // ---- the machinery ------------------------------------------------------------

        public sealed class Pixel
        {
            public Color Albedo;
            public float Height;      // relative, any range: the normal map is the gradient
            public float Metallic;
            public float Smoothness;
        }

        private static Set Build(string name, int seed, Action<float, float, Pixel> shade)
        {
            Directory.CreateDirectory(Folder);
            int n = Size;
            var albedo = new Color[n * n];
            var height = new float[n * n];
            var mask = new Color[n * n];
            var p = new Pixel();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    shade(x + seed * 1000f, y + seed * 1000f, p);
                    int i = y * n + x;
                    albedo[i] = new Color(Mathf.Clamp01(p.Albedo.r), Mathf.Clamp01(p.Albedo.g), Mathf.Clamp01(p.Albedo.b), 1f);
                    height[i] = p.Height;
                    mask[i] = new Color(Mathf.Clamp01(p.Metallic), 0f, 0f, Mathf.Clamp01(p.Smoothness));
                }
            var normal = new Color[n * n];
            const float strength = 6f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = height[y * n + (x + 1) % n] - height[y * n + (x + n - 1) % n];
                    float dy = height[((y + 1) % n) * n + x] - height[((y + n - 1) % n) * n + x];
                    Vector3 nrm = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    normal[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                }
            Texture2D a = Write(name + "_A", albedo, sRGB: true, normalMap: false);
            Texture2D nm = Write(name + "_N", normal, sRGB: false, normalMap: true);
            Texture2D m = Write(name + "_M", mask, sRGB: false, normalMap: false);
            return new Set(a, nm, m);
        }

        private static Texture2D Write(string file, Color[] pixels, bool sRGB, bool normalMap)
        {
            string path = Folder + "/" + file + ".png";
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            // Only touch the file when the pixels changed: an unchanged PNG keeps its
            // import and its GUID untouched in git.
            if (!File.Exists(path) || !SameBytes(File.ReadAllBytes(path), png)) File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (importer.sRGBTexture != sRGB) { importer.sRGBTexture = sRGB; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Repeat) { importer.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (importer.mipmapEnabled != true) { importer.mipmapEnabled = true; changed = true; }
            if (importer.maxTextureSize != Size) { importer.maxTextureSize = Size; changed = true; }
            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---- shading helpers (all tile at Size) -----------------------------------------

        // Fractal noise in -0.5..0.5, tiling across the texture by sampling on a torus.
        private static float Fbm(float x, float y, float frequency, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, total = 0f;
            float f = frequency;
            for (int o = 0; o < octaves; o++)
            {
                sum += (TiledPerlin(x, y, f, seed + o * 17) - 0.5f) * amp;
                total += amp;
                amp *= 0.5f;
                f *= 2f;
            }
            return sum / total;
        }

        // Perlin made to tile: two samples blended by position across the seam.
        private static float TiledPerlin(float x, float y, float frequency, int seed)
        {
            float u = Mathf.Repeat(x, Size) / Size, v = Mathf.Repeat(y, Size) / Size;
            float ox = seed * 3.17f, oy = seed * 7.31f;
            float a = Mathf.PerlinNoise(ox + u * frequency, oy + v * frequency);
            float b = Mathf.PerlinNoise(ox + (u + 1f) * frequency, oy + v * frequency);
            float c = Mathf.PerlinNoise(ox + u * frequency, oy + (v + 1f) * frequency);
            float d = Mathf.PerlinNoise(ox + (u + 1f) * frequency, oy + (v + 1f) * frequency);
            float ab = Mathf.Lerp(b, a, u), cd = Mathf.Lerp(d, c, u);
            return Mathf.Lerp(cd, ab, v);
        }

        // 1 inside a groove of `width` px along the lines of a `pitch` px grid.
        private static float Seam(float x, float y, float pitch, float width)
        {
            float sx = Mathf.Abs(Mathf.Repeat(x, pitch) - pitch / 2f), sy = Mathf.Abs(Mathf.Repeat(y, pitch) - pitch / 2f);
            float d = Mathf.Min(pitch / 2f - sx, pitch / 2f - sy);
            return Mathf.Clamp01(1f - d / width);
        }

        // Rivet heads `inset` px inside each plate of a `pitch` grid, `radius` px.
        private static float Rivets(float x, float y, float pitch, float inset, float radius)
        {
            float lx = Mathf.Repeat(x, pitch), ly = Mathf.Repeat(y, pitch);
            float best = 0f;
            foreach (float cx in new[] { inset, pitch - inset })
                foreach (float cy in new[] { inset, pitch - inset })
                {
                    float d = Vector2.Distance(new Vector2(lx, ly), new Vector2(cx, cy));
                    best = Mathf.Max(best, Mathf.Clamp01(1f - (d - radius * 0.5f) / (radius * 0.5f)));
                }
            // and one mid-edge on the long sides, so a 2 m plate reads riveted
            foreach (float cx in new[] { pitch / 2f })
                foreach (float cy in new[] { inset, pitch - inset })
                {
                    float d = Vector2.Distance(new Vector2(lx, ly), new Vector2(cx, cy));
                    best = Mathf.Max(best, Mathf.Clamp01(1f - (d - radius * 0.5f) / (radius * 0.5f)));
                }
            return best;
        }

        // Vertical streaks that start strong and fade down the tile.
        private static float Drips(float x, float y, int seed)
        {
            float column = TiledPerlin(x, 0f, 24f, seed);
            float start = Mathf.Clamp01((column - 0.55f) * 5f);
            float along = 1f - Mathf.Repeat(y, Size) / Size;
            float wobble = TiledPerlin(x, y, 40f, seed + 5) * 0.3f;
            return Mathf.Clamp01(start * along * along + wobble * start) * 0.9f;
        }

        // 1 within `width` px of the edge of a `pitch` cell along one axis.
        private static float Edge(float v, float pitch, float width)
        {
            float d = Mathf.Min(Mathf.Repeat(v, pitch), pitch - Mathf.Repeat(v, pitch));
            return Mathf.Clamp01(1f - d / width);
        }

        // Diamond tread: a faint raised lattice, fine.
        private static float Tread(float x, float y)
        {
            float u = Mathf.Repeat(x + y, 16f), v = Mathf.Repeat(x - y, 16f);
            float a = Mathf.Clamp01(1f - Mathf.Abs(u - 8f) / 2f), b = Mathf.Clamp01(1f - Mathf.Abs(v - 8f) / 2f);
            return Mathf.Max(a, b) * 0.6f;
        }
    }
}
