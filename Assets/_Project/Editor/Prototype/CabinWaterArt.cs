using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Editor.Prototype
{
    // The car water's art (the new elevator, 28 September 2026): five small procedural
    // textures (ripples, a stream, a splash, bubbles, a drain swirl), their materials and
    // the tube's water ring mesh, under Art/Elevator/Water. The assets are made once and
    // kept (the GUIDs stay), but every call writes the materials' settings and the texture
    // importers' again and rebuilds the ring when its radii differ, so a change here reaches
    // the committed assets on the next build. ShaftTubeSetup wires them onto the car and
    // the tube. New assets only: the
    // shared DiveSiteGlass/DiveSiteWaterSurface materials are not touched (the tube's
    // ring uses DiveSiteWaterSurface as it is, so it matches the disc exactly).
    public static class CabinWaterArt
    {
        public const string Folder = "Assets/_Project/Art/Elevator/Water";
        public const string SurfaceMaterialPath = Folder + "/CabinWaterSurface.mat";
        public const string StreamMaterialPath = Folder + "/CabinWaterStream.mat";
        public const string SplashMaterialPath = Folder + "/CabinWaterSplash.mat";
        public const string BubblesMaterialPath = Folder + "/CabinWaterBubbles.mat";
        public const string SwirlMaterialPath = Folder + "/CabinWaterSwirl.mat";
        public const string RingMeshPath = Folder + "/TubeWaterRing.asset";

        private const string RipplePath = Folder + "/CabinWaterRipples.png";
        private const string StreamPath = Folder + "/CabinWaterStream.png";
        private const string SplashPath = Folder + "/CabinWaterSplash.png";
        private const string BubblesPath = Folder + "/CabinWaterBubbles.png";
        private const string SwirlPath = Folder + "/CabinWaterSwirl.png";

        [MenuItem("Sunk Cost/Prototype/Make cabin water art")]
        public static void MakeFromMenu() => Debug.Log(EnsureAll());

        public static string EnsureAll()
        {
            Surface(); Stream(); Splash(); Bubbles(); Swirl();
            RingMesh(2.56f, 2.97f);
            return "Cabin water art ready in " + Folder;
        }

        // ---- materials ------------------------------------------------------------------

        // The car's water: the sea's colour (DiveSiteWaterSurface), rippled by a drifting
        // normal map, with a faint cyan from the car's ring light.
        // Smoothness 0.65 (was 0.9): with the Cabin Light over it a mirror-smooth surface
        // showed a blown-out white disc from above (the review's F1).
        public static Material Surface() => GetOrMake(SurfaceMaterialPath, "Universal Render Pipeline/Lit", m =>
        {
            SetTransparent(m, new Color(0.25f, 0.55f, 0.6f, 0.55f), cullOff: false);
            m.SetTexture("_BumpMap", TextureAt(RipplePath, RippleTexture, normalMap: true, repeat: true));
            m.SetFloat("_BumpScale", 0.55f);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", 0.65f);
            Emission(m, new Color(0.02f, 0.10f, 0.12f));
        });

        public static Material Stream() => GetOrMake(StreamMaterialPath, "Universal Render Pipeline/Simple Lit", m =>
        {
            SetTransparent(m, new Color(0.82f, 0.95f, 1f, 0.62f), cullOff: true);
            m.SetTexture("_BaseMap", TextureAt(StreamPath, StreamTexture, normalMap: false, repeat: true));
            Emission(m, new Color(0.10f, 0.16f, 0.18f));
        });

        // Foam lies ON the water: the splash and the drain swirl are drawn from above only
        // (their quads face up), so an eye under the surface sees the water's underside,
        // not white foam cut-outs (the dive captures of 28 September 2026).
        public static Material Splash() => GetOrMake(SplashMaterialPath, "Universal Render Pipeline/Simple Lit", m =>
        {
            SetTransparent(m, new Color(0.93f, 0.98f, 1f, 0.8f), cullOff: false);
            m.SetTexture("_BaseMap", TextureAt(SplashPath, SplashTexture, normalMap: false, repeat: false));
            Emission(m, new Color(0.12f, 0.16f, 0.17f));
        });

        public static Material Bubbles() => GetOrMake(BubblesMaterialPath, "Universal Render Pipeline/Simple Lit", m =>
        {
            SetTransparent(m, new Color(0.85f, 0.97f, 1f, 0.6f), cullOff: true);
            m.SetTexture("_BaseMap", TextureAt(BubblesPath, BubblesTexture, normalMap: false, repeat: true));
            Emission(m, new Color(0.06f, 0.12f, 0.14f));
        });

        public static Material Swirl() => GetOrMake(SwirlMaterialPath, "Universal Render Pipeline/Simple Lit", m =>
        {
            SetTransparent(m, new Color(0.9f, 0.97f, 1f, 0.75f), cullOff: false);
            m.SetTexture("_BaseMap", TextureAt(SwirlPath, SwirlTexture, normalMap: false, repeat: false));
            Emission(m, new Color(0.08f, 0.12f, 0.13f));
        });

        // Loads the material (or makes it, once: its GUID stays) and writes its settings
        // every time, like ElevatorLook.LoadOrCreate, so the code and the asset agree.
        private static Material GetOrMake(string path, string shaderName, System.Action<Material> configure)
        {
            Shader shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder();
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader) material.shader = shader;
            configure(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        // URP transparent, alpha blended, ZWrite off, queue 3000 (INTERFACES.md §2.6).
        // Preserve Specular off: URP's validation would otherwise turn the blend
        // premultiplied, and reflections and highlights would not fade with alpha (a
        // milky, blown-out surface under the Cabin Light; the review's F1).
        private static void SetTransparent(Material m, Color colour, bool cullOff)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.SetFloat("_Cull", cullOff ? (float)CullMode.Off : (float)CullMode.Back);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_ReceiveShadows")) m.SetFloat("_ReceiveShadows", 0f);
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        }

        private static void Emission(Material m, Color colour)
        {
            m.SetColor("_EmissionColor", colour);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        // ---- the tube's ring ------------------------------------------------------------

        // A flat annulus, both faces, in the XZ plane, for "WaterSurface Ring". One asset
        // (its GUID stays), rebuilt in place when it was made for other radii (a changed
        // tube radius or TubeRingInnerRadius would otherwise leave a gap or an overlap
        // exactly where the car crosses the surface).
        public static Mesh RingMesh(float inner, float outer)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath);
            if (existing != null && RingMatches(existing, inner, outer)) return existing;
            EnsureFolder();
            Mesh mesh = existing != null ? existing : new Mesh();
            FillRing(mesh, inner, outer);
            if (existing == null) AssetDatabase.CreateAsset(mesh, RingMeshPath);
            else EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        private static bool RingMatches(Mesh mesh, float inner, float outer)
        {
            Vector3[] v = mesh.vertices;
            return v.Length >= 2 && Mathf.Abs(v[0].magnitude - inner) < 0.001f && Mathf.Abs(v[1].magnitude - outer) < 0.001f;
        }

        private static void FillRing(Mesh mesh, float inner, float outer)
        {
            const int segments = 96;
            var vertices = new Vector3[(segments + 1) * 4];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 12];
            for (int face = 0; face < 2; face++)
            {
                int b = face * (segments + 1) * 2;
                for (int s = 0; s <= segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    Vector3 d = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    vertices[b + s * 2] = d * inner;
                    vertices[b + s * 2 + 1] = d * outer;
                    normals[b + s * 2] = normals[b + s * 2 + 1] = face == 0 ? Vector3.up : Vector3.down;
                    uvs[b + s * 2] = new Vector2(0.5f + d.x * inner / (2f * outer), 0.5f + d.z * inner / (2f * outer));
                    uvs[b + s * 2 + 1] = new Vector2(0.5f + d.x * 0.5f, 0.5f + d.z * 0.5f);
                }
                for (int s = 0; s < segments; s++)
                {
                    int t = (face * segments + s) * 6;
                    int i0 = b + s * 2, o0 = i0 + 1, i1 = i0 + 2, o1 = i0 + 3;
                    if (face == 0) { triangles[t] = i0; triangles[t + 1] = i1; triangles[t + 2] = o0; triangles[t + 3] = o0; triangles[t + 4] = i1; triangles[t + 5] = o1; }
                    else { triangles[t] = i0; triangles[t + 1] = o0; triangles[t + 2] = i1; triangles[t + 3] = o0; triangles[t + 4] = o1; triangles[t + 5] = i1; }
                }
            }
            mesh.Clear();
            mesh.name = "TubeWaterRing";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        // ---- textures -------------------------------------------------------------------

        // The pixels are drawn once (a procedural picture; delete the PNG to redraw it);
        // the importer's settings are checked on every call and reimported when they differ.
        private static Texture2D TextureAt(string path, System.Func<Texture2D> make, bool normalMap, bool repeat)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                EnsureFolder();
                Texture2D texture = make();
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureWrapMode wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (importer.textureType != type || importer.alphaIsTransparency != !normalMap || importer.wrapMode != wrap || !importer.mipmapEnabled || importer.sRGBTexture != !normalMap)
            {
                importer.textureType = type;
                importer.alphaIsTransparency = !normalMap;
                importer.wrapMode = wrap;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = !normalMap;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Tileable ripples: a height field of integer-frequency waves, as a normal map.
        private static Texture2D RippleTexture()
        {
            const int n = 256;
            var height = new float[n, n];
            int[,] waves = { { 3, 1 }, { -2, 4 }, { 5, -3 }, { 1, 7 }, { -7, 2 }, { 9, 5 }, { -4, -9 } };
            float[] amp = { 1f, 0.8f, 0.6f, 0.45f, 0.35f, 0.22f, 0.18f };
            float[] phase = { 0.3f, 1.7f, 2.9f, 4.1f, 0.9f, 5.3f, 3.3f };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (float)x / n, v = (float)y / n, h = 0f;
                    for (int w = 0; w < amp.Length; w++) h += amp[w] * Mathf.Sin(2f * Mathf.PI * (waves[w, 0] * u + waves[w, 1] * v) + phase[w]);
                    height[x, y] = h;
                }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = height[(x + 1) % n, y] - height[(x + n - 1) % n, y];
                    float dy = height[x, (y + 1) % n] - height[x, (y + n - 1) % n];
                    Vector3 normal = new Vector3(-dx * 2.2f, -dy * 2.2f, 1f).normalized;
                    texture.SetPixel(x, y, new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f));
                }
            texture.Apply();
            return texture;
        }

        // A falling stream: soft-edged across (u), streaky along (v), tileable in v.
        private static Texture2D StreamTexture()
        {
            const int w = 64, h = 256;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (float)y / h;
                    float edge = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f);
                    float streaks = 0.55f + 0.25f * Mathf.Sin(2f * Mathf.PI * (u * 5f + 0.3f * Mathf.Sin(2f * Mathf.PI * v * 2f)))
                                          + 0.2f * Mathf.Sin(2f * Mathf.PI * (u * 11f + v * 3f));
                    float gaps = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * (v * 4f + u * 1.5f));
                    float a = Mathf.Clamp01(edge * streaks * gaps);
                    float core = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2.4f);
                    texture.SetPixel(x, y, new Color(0.85f + 0.15f * core, 0.95f + 0.05f * core, 1f, a));
                }
            texture.Apply();
            return texture;
        }

        // A splash seen from above: a broken foam ring round a lighter centre.
        private static Texture2D SplashTexture()
        {
            const int n = 128;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float ring = Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.16f, 2f));
                    float centre = 0.45f * Mathf.Exp(-Mathf.Pow(r / 0.3f, 2f));
                    float broken = 0.6f + 0.4f * Mathf.Sin(a * 7f + 1.3f) * Mathf.Sin(a * 3f + r * 9f);
                    float alpha = Mathf.Clamp01((ring * broken + centre) * 1.3f) * Mathf.Clamp01((1f - r) * 5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            return texture;
        }

        // Rising bubbles: small rings scattered over a strip, tileable in v.
        private static Texture2D BubblesTexture()
        {
            const int w = 64, h = 256;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 1f, 1f, 0f);
            var random = new System.Random(28092026);
            for (int b = 0; b < 26; b++)
            {
                float cx = 10f + (float)random.NextDouble() * (w - 20f);
                float cy = (float)random.NextDouble() * h;
                float radius = 2.5f + (float)random.NextDouble() * 5.5f;
                for (int y = (int)(cy - radius - 2); y <= (int)(cy + radius + 2); y++)
                    for (int x = (int)(cx - radius - 2); x <= (int)(cx + radius + 2); x++)
                    {
                        if (x < 0 || x >= w) continue;
                        int wy = ((y % h) + h) % h;
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        float rim = Mathf.Clamp01(1f - Mathf.Abs(d - radius) / 1.3f);
                        float fill = d < radius ? 0.18f : 0f;
                        float glint = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx - radius * 0.35f, cy + radius * 0.35f)) / (radius * 0.3f));
                        float alpha = Mathf.Max(rim * 0.9f, fill, glint);
                        int index = wy * w + x;
                        if (alpha > pixels[index].a) pixels[index] = new Color(1f, 1f, 1f, alpha);
                    }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // The drain from above: foam arms spiralling outward into a band at the grilles.
        private static Texture2D SwirlTexture()
        {
            const int n = 256;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float arms = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(a * 5f + Mathf.Log(Mathf.Max(r, 0.05f)) * 9f), 3f);
                    float grain = 0.75f + 0.25f * Mathf.Sin(a * 23f + r * 40f);
                    float window = Mathf.Clamp01((r - 0.18f) * 4f) * Mathf.Clamp01((1f - r) * 12f);
                    // The grille band (r 1.80-2.05 of 2.25): foam gathers there.
                    float band = Mathf.Exp(-Mathf.Pow((r - 0.85f) / 0.06f, 2f));
                    float alpha = Mathf.Clamp01((arms * grain * 0.8f + band * 0.9f) * window);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            return texture;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/Elevator")) AssetDatabase.CreateFolder("Assets/_Project/Art", "Elevator");
            AssetDatabase.CreateFolder("Assets/_Project/Art/Elevator", "Water");
        }
    }
}
