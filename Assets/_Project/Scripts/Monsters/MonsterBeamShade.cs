using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Monsters
{
    // The DARK beam eats the light round it (Dan, 24 September 2026: "everything near
    // the Listener's beam gets darker, so the beam reads as eating light"). URP has
    // no negative lights, so two things together:
    // - a shadow haze: a wide soft band of darkness laid along the beam (a view-
    //   facing ribbon, darkest on the axis and fading to nothing at its edges), and a
    //   dark blot where the beam meets a wall, drawn under the violet sheath so the
    //   beam itself stays a readable black core in a violet edge;
    // - the lights near it dim: for the render only, every Light within `lightReach`
    //   metres of the beam is turned down by up to `lightDim` (the nearer, the more),
    //   and put back exactly as each camera finishes drawing — never a lamp's
    //   switch (HQPlayerController.LampOn), never a SyncVar, never anything a script
    //   or CreatureSenses reads. A diver's own headlamp near the beam fades, so the
    //   floor and walls round it go dark on every screen.
    // Built by MonsterBeamView and driven by it every frame from the replicated beam
    // (the drawn line and its phase), so the host, a guest, a spectator and the
    // deck TV all see it. Presentation only: nothing here feeds gameplay. The light
    // list is gathered when a dark beam comes up and once a second while it lasts;
    // nothing else allocates per frame. Two beams at once dim by the product.
    [DefaultExecutionOrder(-10000)] // its Update puts back any light a broken render left dimmed, before other scripts run
    public sealed class MonsterBeamShade : MonoBehaviour
    {
        private const float LightRefreshSeconds = 1f;
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static Material hazeMaterial, sinkMaterial;
        private static Texture2D hazeTexture;

        [Tooltip("The shadow haze's full width across the beam, metres.")]
        [SerializeField] private float hazeWidth = 3.4f;
        [Tooltip("How dark the haze is on the beam's axis at the full burn (0 none, 1 black).")]
        [SerializeField] private float hazeDarkness = 0.55f;
        [Tooltip("The dark blot where the beam meets a wall: its diameter, metres, and its darkness at the centre.")]
        [SerializeField] private float sinkSize = 2.4f;
        [SerializeField] private float sinkDarkness = 0.6f;
        [Tooltip("Lights within this many metres of the beam are turned down for the render.")]
        [SerializeField] private float lightReach = 7f;
        [Tooltip("Inside this distance a light is turned down fully.")]
        [SerializeField] private float lightInner = 1.5f;
        [Tooltip("At the full burn, the most a light next to the beam loses (0.8 = down to a fifth).")]
        [Range(0f, 1f)]
        [SerializeField] private float lightDim = 0.8f;

        private LineRenderer haze;
        private Transform sink;
        private Renderer[] sinkShells;
        private MaterialPropertyBlock block;
        private bool shown;
        private Vector3 from, to;
        private float strength;

        // What it shows now (the checks read it).
        public bool Shown => shown;
        public float Strength => shown ? strength : 0f;
        public float HazeDarknessShown => shown ? hazeDarkness * strength : 0f;
        public bool SinkShown => sink != null && sink.gameObject.activeSelf;
        public float LightReach => lightReach;
        // A test seam: the checks film the dark beam with the shade off (the before) and on.
        public static bool OffForChecks;
        // The factor this shade puts on a light at `point` (1 = untouched).
        public float DimAt(Vector3 point)
        {
            if (!shown || strength <= 0f) return 1f;
            float d = Vector3.Distance(point, ClosestOnSegment(point, from, to));
            float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lightInner, lightReach, d));
            return 1f - lightDim * strength * near;
        }

        public static MonsterBeamShade Make(Transform beamRoot)
        {
            var go = new GameObject("Shade");
            go.transform.SetParent(beamRoot, false);
            MonsterBeamShade shade = go.AddComponent<MonsterBeamShade>();
            shade.block = new MaterialPropertyBlock();
            var hazeGo = new GameObject("Shade haze");
            hazeGo.transform.SetParent(go.transform, false);
            shade.haze = hazeGo.AddComponent<LineRenderer>();
            shade.haze.useWorldSpace = true;
            shade.haze.positionCount = 2;
            shade.haze.numCapVertices = 0;
            shade.haze.alignment = LineAlignment.View;
            shade.haze.textureMode = LineTextureMode.Tile; // u counts widths from the mouth: the fade-in there is a fixed length
            shade.haze.textureScale = Vector2.one;
            shade.haze.shadowCastingMode = ShadowCastingMode.Off;
            shade.haze.receiveShadows = false;
            shade.haze.sharedMaterial = HazeMaterial();
            shade.haze.enabled = false;
            shade.sink = SinkShells(go.transform, out shade.sinkShells);
            return shade;
        }

        // Nested blended spheres (whole, 70 %, 45 %): the darkness deepens toward the centre.
        private static readonly float[] ShellScale = { 1f, 0.7f, 0.45f }, ShellShare = { 0.35f, 0.35f, 0.3f };
        private static Transform SinkShells(Transform parent, out Renderer[] shells)
        {
            var root = new GameObject("Shade sink").transform;
            root.SetParent(parent, false);
            shells = new Renderer[ShellScale.Length];
            for (int i = 0; i < ShellScale.Length; i++)
            {
                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Shade sink " + i;
                Destroy(ball.GetComponent<Collider>());
                ball.transform.SetParent(root, false);
                ball.transform.localScale = Vector3.one * ShellScale[i];
                Renderer r = ball.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = SinkMaterial();
                shells[i] = r;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        // Every frame from MonsterBeamView: the drawn line, how strong the darkness is
        // (0..1: the charge gathering, the burn, the fade), and whether it ends on a wall.
        public void Show(Vector3 lineFrom, Vector3 lineTo, float amount, bool onWall)
        {
            from = lineFrom; to = lineTo;
            strength = Mathf.Clamp01(amount);
            if (strength <= 0.001f || OffForChecks) { Hide(); return; }
            if (!shown) { shown = true; Register(this); }
            haze.enabled = true;
            haze.SetPosition(0, from);
            haze.SetPosition(1, to);
            haze.startWidth = haze.endWidth = hazeWidth;
            block.SetColor(BaseColourId, new Color(0f, 0f, 0f, hazeDarkness * strength));
            haze.SetPropertyBlock(block);
            sink.gameObject.SetActive(onWall);
            if (onWall)
            {
                sink.position = to;
                sink.localScale = Vector3.one * sinkSize;
                for (int i = 0; i < sinkShells.Length; i++)
                {
                    block.SetColor(BaseColourId, new Color(0f, 0f, 0f, sinkDarkness * ShellShare[i] * strength));
                    sinkShells[i].SetPropertyBlock(block);
                }
            }
        }

        public void Hide()
        {
            if (!shown) return;
            shown = false;
            strength = 0f;
            if (haze != null) haze.enabled = false;
            if (sink != null) sink.gameObject.SetActive(false);
            Unregister(this);
        }

        private void OnDisable() => Hide();

        private static Vector3 ClosestOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, ab) / len);
            return a + ab * t;
        }

        // ---- the lights, turned down for the render only ------------------------------------
        // Per camera: each camera's render (the diver's view, a spectator's, the deck TV's,
        // a capture) turns the lights down as it begins and puts them back as it ends. Every
        // begin puts back first, so a render that threw cannot compound; the end of the whole
        // context puts back again; and before any script's Update the earliest shade puts
        // back whatever a broken render might have left, so no script ever reads a dimmed value.

        private static readonly List<MonsterBeamShade> active = new();
        private static readonly List<Light> lights = new(64);
        private static readonly List<Light> dimmed = new(32);
        private static readonly List<float> saved = new(32);
        private static float lightsGatheredAt = float.NegativeInfinity;
        private static bool hooked;

        // For the checks: the shades up now, and how many lights the last camera turned down.
        public static int ActiveCount => active.Count;
        public static int LastDimmedCount { get; private set; }
        public static int DimmedNow => dimmed.Count;

        private void Update() => Restore(); // DefaultExecutionOrder(-10000): before every other script's Update

        private static void Register(MonsterBeamShade shade)
        {
            if (!active.Contains(shade)) active.Add(shade);
            lightsGatheredAt = float.NegativeInfinity; // a beam came up: gather the lights afresh
            if (hooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            RenderPipelineManager.endContextRendering += OnEndContext;
            hooked = true;
        }

        private static void Unregister(MonsterBeamShade shade)
        {
            active.Remove(shade);
            if (active.Count > 0 || !hooked) return;
            Restore();
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RenderPipelineManager.endContextRendering -= OnEndContext;
            hooked = false;
            lights.Clear();
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            Restore(); // a render that never ended must not compound
            if (active.Count == 0) return;
            if (Time.unscaledTime - lightsGatheredAt > LightRefreshSeconds)
            {
                lightsGatheredAt = Time.unscaledTime;
                lights.Clear();
                lights.AddRange(Object.FindObjectsByType<Light>(FindObjectsSortMode.None));
            }
            int count = 0;
            foreach (Light light in lights)
            {
                if (light == null || !light.isActiveAndEnabled || light.intensity <= 0f || light.type == LightType.Directional) continue;
                Vector3 at = light.transform.position;
                float factor = 1f;
                foreach (MonsterBeamShade shade in active) factor *= shade.DimAt(at);
                if (factor >= 0.999f) continue;
                dimmed.Add(light);
                saved.Add(light.intensity);
                light.intensity *= Mathf.Max(0f, factor);
                count++;
            }
            LastDimmedCount = count;
        }

        private static void OnEndCamera(ScriptableRenderContext context, Camera camera) => Restore();
        private static void OnEndContext(ScriptableRenderContext context, List<Camera> cameras) => Restore();

        // Every light back exactly as it was (the saved value, not a division).
        private static void Restore()
        {
            if (dimmed.Count == 0) return;
            for (int i = 0; i < dimmed.Count; i++) if (dimmed[i] != null) dimmed[i].intensity = saved[i];
            dimmed.Clear();
            saved.Clear();
        }

        // ---- materials ---------------------------------------------------------------------------

        // A band's darkness: across its width 1 on the axis and a soft fall to nothing at the
        // edges; along it (u, in band widths from the mouth, clamped past 1) a fade-in over
        // the first half-width, so it does not start in a hard edge through the head.
        private static Texture2D HazeTexture()
        {
            if (hazeTexture != null) return hazeTexture;
            const int along = 32, size = 64;
            hazeTexture = new Texture2D(along, size, TextureFormat.RGBA32, false) { name = "Beam shade falloff", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < size; y++)
            {
                float across = Mathf.Abs((y + 0.5f) / size * 2f - 1f); // 0 on the axis, 1 at the edge
                float a = Mathf.Exp(-across * across * 4.5f) * (1f - across * across);
                for (int x = 0; x < along; x++)
                {
                    float u = (x + 0.5f) / along;
                    hazeTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * Mathf.SmoothStep(0f, 1f, u / 0.5f)));
                }
            }
            hazeTexture.Apply(false, true);
            return hazeTexture;
        }

        // Drawn before the beam's own glow and darkness (MonsterBeamView), so the violet
        // sheath lies over the haze and the black core over both.
        private static Material HazeMaterial()
        {
            if (hazeMaterial != null) return hazeMaterial;
            hazeMaterial = Blended("Beam shade haze", (int)RenderQueue.Transparent - 2);
            hazeMaterial.SetTexture(BaseMapId, HazeTexture());
            return hazeMaterial;
        }

        private static Material SinkMaterial() => sinkMaterial != null ? sinkMaterial : sinkMaterial = Blended("Beam shade sink", (int)RenderQueue.Transparent - 1);

        private static Material Blended(string name, int queue)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material m = new(shader != null ? shader : Shader.Find("Sprites/Default")) { name = name, hideFlags = HideFlags.DontSave };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = queue;
            m.SetColor(BaseColourId, Color.black);
            return m;
        }
    }
}
