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
    // settings.
    //
    // The style is the reference picture's, 1:1 as far as code can go: chunky
    // and toy-like — flat saturated colour, bevelled tiles and panels, a rust
    // seam where plates meet, crisp hazard bands, blocky water — not
    // photographic grime. Deterministic: the same seed gives the same pixels.
    public static class ProceduralTextures
    {
        public const string Folder = "Assets/_Project/Art/HQ/Textures";
        public const int Size = 512;

        public readonly struct Set
        {
            public readonly Texture2D Albedo, Normal, Mask;
            public Set(Texture2D albedo, Texture2D normal, Texture2D mask) { Albedo = albedo; Normal = normal; Mask = mask; }
        }

        // The picture's palette.
        public static readonly Color Navy = new(0.19f, 0.23f, 0.33f);
        public static readonly Color NavyLight = new(0.25f, 0.30f, 0.41f);
        public static readonly Color NavyDark = new(0.11f, 0.14f, 0.22f);
        public static readonly Color Rust = new(0.66f, 0.30f, 0.12f);
        public static readonly Color RustDark = new(0.42f, 0.18f, 0.08f);
        public static readonly Color HazardYellow = new(0.95f, 0.70f, 0.10f);
        public static readonly Color Ink = new(0.07f, 0.08f, 0.11f);

        // ---- the sets -----------------------------------------------------------------

        // Deck tiles: 2 m bevelled plates, two tones in a checker, a little noise
        // and a dark seam. One tile = 4 m (two plates).
        public static Set DeckTile() => Build("DeckTile", 11, (x, y, p) =>
        {
            const float pitch = 256f;
            float bevel = Bevel(x, y, pitch, 14f);        // 1 at the plate's middle, 0 at its edge
            float seam = 1f - Mathf.Clamp01(bevel * 4f);   // the gap between plates
            int cx = Mathf.FloorToInt(x / pitch), cy = Mathf.FloorToInt(y / pitch);
            bool alt = ((cx + cy) & 1) == 0;
            float noise = Fbm(x, y, 5f, 3, 12) * 0.5f + 0.5f;
            float scuff = Mathf.Clamp01((Fbm(x + 40, y + 900, 3f, 3, 13) - 0.32f) * 2.5f);
            Color c = Color.Lerp(alt ? Navy : NavyLight, alt ? NavyLight : Navy, noise * 0.35f);
            c = Color.Lerp(c, NavyDark, scuff * 0.35f);
            c = Color.Lerp(c, NavyDark * 0.7f, seam);
            c = Color.Lerp(c, c * 1.12f, Mathf.Clamp01((bevel - 0.7f) * 3f) * 0.25f); // the plate's crown catches light
            p.Albedo = c;
            p.Height = bevel * 1.2f;
            p.Metallic = 0f;
            p.Smoothness = 0.14f - scuff * 0.05f;
        });

        // Wall panels: 3 m bevelled plates, a darker seam where they meet, a bolt
        // in each corner; navy for the walls, rust for the structure (the picture's
        // girders, tower and legs are orange). One tile = 3 m.
        public static Set Panel() => PanelSet("Panel", 21, Navy, NavyLight, NavyDark, Rust);
        public static Set RustPanel() => PanelSet("RustPanel", 25, Rust, new Color(0.78f, 0.38f, 0.16f), RustDark, NavyDark);
        private static Set PanelSet(string name, int seed, Color body, Color light, Color dark, Color seamTint) => Build(name, seed, (x, y, p) =>
        {
            const float pitch = 512f;
            float bevel = Bevel(x, y, pitch, 18f);
            float seam = 1f - Mathf.Clamp01(bevel * 3f);
            float bolt = Rivets(x, y, pitch, 34f, 9f);
            float noise = Fbm(x, y, 4f, 3, seed + 1) * 0.5f + 0.5f;
            float seamLine = Mathf.Clamp01((seam - 0.3f) * 1.6f) * Mathf.Clamp01((Fbm(x + 300, y, 6f, 2, seed + 2) + 0.55f));
            Color c = Color.Lerp(body, light, noise * 0.3f);
            c = Color.Lerp(c, dark, seam * 0.8f);
            c = Color.Lerp(c, seamTint, seamLine * 0.6f);
            c = Color.Lerp(c, light * 1.1f, bolt * 0.7f);
            p.Albedo = c;
            p.Height = bevel * 1.0f + bolt * 0.8f;
            p.Metallic = 0f;
            p.Smoothness = 0.12f;
        });

        // Rust steel for the legs and piles: orange rust with darker bands every
        // metre and mottling. One tile = 2 m.
        public static Set RustSteel() => Build("RustSteel", 31, (x, y, p) =>
        {
            float body = Fbm(x, y, 5f, 4, 32) * 0.5f + 0.5f;
            float band = Mathf.Abs(Mathf.Repeat(y, 256f) - 128f) < 14f ? 1f : 0f;
            float mottle = Mathf.Clamp01((Fbm(x + 800, y, 9f, 3, 33) - 0.3f) * 2.2f);
            Color c = Color.Lerp(RustDark, Rust, body);
            c = Color.Lerp(c, RustDark * 0.8f, mottle * 0.5f);
            c = Color.Lerp(c, NavyDark, band * 0.85f);
            p.Albedo = c;
            p.Height = -band * 0.6f + body * 0.15f;
            p.Metallic = 0f;
            p.Smoothness = 0.1f;
        });

        // Hazard stripes: crisp yellow and ink diagonals. One tile = 1 m.
        public static Set Hazard() => Build("Hazard", 41, (x, y, p) =>
        {
            float diag = Mathf.Repeat((x + y) / 72f, 1f);
            float stripe = diag < 0.5f ? 1f : 0f;
            float wear = Mathf.Clamp01((Fbm(x, y, 5f, 3, 42) - 0.38f) * 3f);
            Color c = Color.Lerp(Ink, HazardYellow, stripe);
            c = Color.Lerp(c, c * 0.75f, wear * 0.5f);
            p.Albedo = c;
            p.Height = 0f;
            p.Metallic = 0f;
            p.Smoothness = 0.15f;
        });

        // Crate side: a bevelled panel with an inset field and a stencil band; the
        // material's colour tints it. One tile = 1 m (one crate face).
        public static Set Crate() => Build("Crate", 61, (x, y, p) =>
        {
            const float pitch = 512f;
            float bevel = Bevel(x, y, pitch, 26f);
            float inset = Bevel(x, y, pitch, 90f);
            float field = Mathf.Clamp01((inset - 0.2f) * 6f); // 1 inside the inset field
            float band = Mathf.Repeat(y, pitch) > 200f && Mathf.Repeat(y, pitch) < 240f ? 1f : 0f;
            float noise = Fbm(x, y, 4f, 3, 62) * 0.5f + 0.5f;
            Color c = Color.Lerp(new Color(0.78f, 0.78f, 0.78f), new Color(0.92f, 0.92f, 0.92f), noise);
            c = Color.Lerp(c, c * 0.72f, field * 0.55f);
            c = Color.Lerp(c, c * 0.55f, band * field);
            c = Color.Lerp(c, c * 0.5f, 1f - Mathf.Clamp01(bevel * 5f));
            p.Albedo = c;
            p.Height = bevel * 0.8f - field * 0.5f;
            p.Metallic = 0f;
            p.Smoothness = 0.12f;
        });

        // Container side: vertical corrugation; the material's colour tints it. One tile = 1 m.
        public static Set Container() => Build("Container", 71, (x, y, p) =>
        {
            float wave = Mathf.Sin(x / 512f * Mathf.PI * 2f * 6f);           // six ribs per metre
            float rib = Mathf.Clamp01(wave * 0.5f + 0.5f);
            float noise = Fbm(x, y, 3f, 3, 72) * 0.5f + 0.5f;
            float wear = Mathf.Clamp01((Fbm(x + 70, y + 40, 6f, 3, 73) - 0.4f) * 3f);
            Color c = Color.Lerp(new Color(0.72f, 0.72f, 0.72f), new Color(0.9f, 0.9f, 0.9f), noise);
            c = Color.Lerp(c, c * 0.7f, (1f - rib) * 0.5f);
            c = Color.Lerp(c, c * 0.6f, wear * 0.4f);
            p.Albedo = c;
            p.Height = rib * 0.8f;
            p.Metallic = 0f;
            p.Smoothness = 0.12f;
        });

        // The sea: blocky patches — a quantised noise on 1 m cells, deep and
        // lighter blue — the picture's water. One tile = 24 m.
        public static Set BlockWater() => Build("BlockWater", 81, (x, y, p) =>
        {
            const float cell = 512f / 24f; // 1 m cells
            float qx = Mathf.Floor(x / cell) * cell + cell / 2f, qy = Mathf.Floor(y / cell) * cell + cell / 2f;
            float n = Fbm(qx, qy, 7f, 3, 82) + Fbm(qx + 900, qy + 300, 14f, 2, 83) * 0.5f;
            float level = n > 0.15f ? 2f : n > 0.0f ? 1f : 0f;
            Color deep = new(0.05f, 0.16f, 0.40f), mid = new(0.09f, 0.25f, 0.52f), light = new(0.20f, 0.40f, 0.68f);
            p.Albedo = level == 2f ? light : level == 1f ? mid : deep;
            p.Height = level * 0.15f;
            p.Metallic = 0f;
            p.Smoothness = 0.2f;
        });

        // The company's mark for the flag and the office: a pale skull with three
        // tentacles on transparent. One tile = the emblem.
        public static Set Skull() => Build("Skull", 91, (x, y, p) =>
        {
            float u = Mathf.Repeat(x, Size) / Size - 0.5f, v = Mathf.Repeat(y, Size) / Size - 0.5f;
            float head = Mathf.Clamp01(1f - (Mathf.Sqrt(u * u * 1.15f + (v - 0.08f) * (v - 0.08f)) - 0.20f) / 0.02f);
            float jaw = (Mathf.Abs(u) < 0.12f && v < -0.02f && v > -0.20f) ? 1f : 0f;
            float eyeL = Mathf.Clamp01((0.055f - Vector2.Distance(new Vector2(u, v), new Vector2(-0.085f, 0.10f))) / 0.01f);
            float eyeR = Mathf.Clamp01((0.055f - Vector2.Distance(new Vector2(u, v), new Vector2(0.085f, 0.10f))) / 0.01f);
            float nose = (Mathf.Abs(u) < 0.02f + (0.02f - v) * 0.3f && v < 0.02f && v > -0.03f) ? 1f : 0f;
            float teeth = (jaw > 0f && v < -0.06f && Mathf.Repeat(u + 0.12f, 0.06f) < 0.045f) ? 0f : 1f;
            float tentacles = 0f;
            for (int i = -1; i <= 1; i++)
            {
                float cx = i * 0.16f + Mathf.Sin(v * 22f + i) * 0.03f;
                if (v < -0.18f && v > -0.42f && Mathf.Abs(u - cx) < 0.035f) tentacles = 1f;
            }
            float mark = Mathf.Max(head, jaw, tentacles) * (1f - eyeL) * (1f - eyeR) * (1f - nose) * teeth;
            p.Albedo = new Color(0.86f, 0.87f, 0.9f, mark);
            p.Height = 0f; p.Metallic = 0f; p.Smoothness = 0.2f;
        });

        public static void GenerateAll()
        {
            DeckTile(); Panel(); RustPanel(); RustSteel(); Hazard(); Crate(); Container(); BlockWater(); Skull();
        }

        // ---- the machinery ------------------------------------------------------------

        public sealed class Pixel
        {
            public Color Albedo = Color.white;
            public float Height;
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
                    p.Albedo = Color.white;
                    shade(x + seed * 1024f, y + seed * 1024f, p);
                    int i = y * n + x;
                    albedo[i] = new Color(Mathf.Clamp01(p.Albedo.r), Mathf.Clamp01(p.Albedo.g), Mathf.Clamp01(p.Albedo.b), Mathf.Clamp01(p.Albedo.a));
                    height[i] = p.Height;
                    mask[i] = new Color(Mathf.Clamp01(p.Metallic), 0f, 0f, Mathf.Clamp01(p.Smoothness));
                }
            var normal = new Color[n * n];
            const float strength = 5f;
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
            if (importer.alphaIsTransparency != !normalMap) { importer.alphaIsTransparency = !normalMap; changed = true; }
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

        private static float Fbm(float x, float y, float frequency, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, total = 0f, f = frequency;
            for (int o = 0; o < octaves; o++)
            {
                sum += (TiledPerlin(x, y, f, seed + o * 17) - 0.5f) * amp;
                total += amp; amp *= 0.5f; f *= 2f;
            }
            return sum / total;
        }

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

        // 0 at a cell's edge rising to 1 `width` px in: a rounded bevel profile.
        private static float Bevel(float x, float y, float pitch, float width)
        {
            float dx = Mathf.Min(Mathf.Repeat(x, pitch), pitch - Mathf.Repeat(x, pitch));
            float dy = Mathf.Min(Mathf.Repeat(y, pitch), pitch - Mathf.Repeat(y, pitch));
            float d = Mathf.Min(dx, dy);
            float t = Mathf.Clamp01(d / width);
            return Mathf.Sin(t * Mathf.PI * 0.5f);
        }

        private static float Rivets(float x, float y, float pitch, float inset, float radius)
        {
            float lx = Mathf.Repeat(x, pitch), ly = Mathf.Repeat(y, pitch);
            float best = 0f;
            foreach (float cx in new[] { inset, pitch - inset })
                foreach (float cy in new[] { inset, pitch - inset })
                {
                    float d = Vector2.Distance(new Vector2(lx, ly), new Vector2(cx, cy));
                    best = Mathf.Max(best, Mathf.Clamp01(1f - (d - radius * 0.6f) / (radius * 0.4f)));
                }
            return best;
        }
    }
}
