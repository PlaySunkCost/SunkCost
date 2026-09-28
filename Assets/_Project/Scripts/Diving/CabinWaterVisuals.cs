using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

namespace SunkCost.Diving
{
    // What the car's water looks like doing (the new elevator, 28 September 2026): six
    // streams from the roof nozzles while it fills, a splash of foam where each lands,
    // bubbles from a nozzle once the water covers it, and foam swirling out to the floor
    // grilles while it drains; ripples on the surface. Stylised and cheap: textured quads
    // whose size, place and scroll are recomputed every LateUpdate from CabinWater (the
    // level, the flow) and the clock. Nothing accumulates between frames and nothing is
    // local state: every peer, spectator and the ship TV draw the same water from the same
    // replicated car. On "Cabin Water FX", a direct child of the car root at identity, so
    // its local space is the car root's.
    [DefaultExecutionOrder(100)]
    public sealed class CabinWaterVisuals : MonoBehaviour
    {
        public const int NozzleCount = 6;

        [SerializeField] private CabinWater water;
        [SerializeField] private Renderer surfaceRenderer;          // the "Cabin Water" disc (ripples)
        [SerializeField] private Vector3[] outlets = new Vector3[0]; // nozzle mouths, car-root local
        [SerializeField] private Vector3 drainCentre = new(0f, 0.10f, 0f);
        [SerializeField] private float floorTop = 0.10f;
        [SerializeField] private Transform[] streams = new Transform[0];
        [SerializeField] private Transform[] splashes = new Transform[0];
        [SerializeField] private Transform[] bubbles = new Transform[0];
        [SerializeField] private Transform swirl;

        [Header("Look")]
        [SerializeField] private float streamWidth = 0.14f;
        [SerializeField] private float streamScroll = 2.6f;       // texture lengths per second, downward
        [SerializeField] private float streamTextureMeters = 0.9f;
        [SerializeField] private float splashSize = 0.75f;
        [SerializeField] private float bubbleWidth = 0.22f;
        [SerializeField] private float bubbleScroll = 0.55f;
        [SerializeField] private float bubbleTextureMeters = 1.2f;
        [SerializeField] private float plungeMeters = 1.1f;       // how deep a pouring stream drives its bubbles
        [SerializeField] private float swirlSize = 4.5f;
        [SerializeField] private float swirlDegreesPerSecond = 95f;
        [SerializeField] private float rippleScroll = 0.035f;
        [SerializeField] private float rippleTiling = 2.2f;

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;
        private Renderer[][] streamRenderers, bubbleRenderers;
        private Renderer[] splashRenderers;
        private Renderer swirlRenderer;
        private Color swirlColour = Color.white, splashColour = Color.white;
        private bool warmPose;

        // For the tests' snapshot (pour=, foam=, bubbles=, drain=).
        public int ActiveStreams { get; private set; }   // nozzles pouring (in air or under the surface)
        public int VisibleStreams { get; private set; }  // streams drawn through the air
        public float Foam01 { get; private set; }
        public bool Bubbles { get; private set; }
        public float Drain01 { get; private set; }
        public CabinWater Water => water;
        public int OutletCount => outlets != null ? outlets.Length : 0;
        public Vector3 OutletLocal(int i) => outlets[i];

        public void Configure(CabinWater cabinWater, Renderer surface, Vector3[] outletPoints, Vector3 drain, float floorTopLocal,
            Transform[] streamRoots, Transform[] splashRoots, Transform[] bubbleRoots, Transform swirlRoot)
        {
            water = cabinWater; surfaceRenderer = surface; outlets = outletPoints; drainCentre = drain; floorTop = floorTopLocal;
            streams = streamRoots; splashes = splashRoots; bubbles = bubbleRoots; swirl = swirlRoot;
            streamRenderers = null;
        }

        // DiveSiteWarmup: show every element at a mid-flood pose for one off-screen render,
        // so the materials' first use does not stall a frame mid-shaft; false restores the
        // normal derivation (the next LateUpdate would anyway).
        public void WarmPose(bool on)
        {
            warmPose = on;
            Present(on ? 1.2f : (water != null ? water.LevelMeters : 0f), on ? CarWaterFlow.Filling : (water != null ? water.Flow : CarWaterFlow.Still), on);
        }

        private void LateUpdate()
        {
            if (water == null || warmPose) return;
            Present(water.LevelMeters, water.Flow, false);
        }

        private void Cache()
        {
            if (streamRenderers != null) return;
            block ??= new MaterialPropertyBlock();
            streamRenderers = new Renderer[streams.Length][];
            for (int i = 0; i < streams.Length; i++) streamRenderers[i] = streams[i] != null ? streams[i].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            bubbleRenderers = new Renderer[bubbles.Length][];
            for (int i = 0; i < bubbles.Length; i++) bubbleRenderers[i] = bubbles[i] != null ? bubbles[i].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            splashRenderers = new Renderer[splashes.Length];
            for (int i = 0; i < splashes.Length; i++) splashRenderers[i] = splashes[i] != null ? splashes[i].GetComponentInChildren<Renderer>(true) : null;
            swirlRenderer = swirl != null ? swirl.GetComponentInChildren<Renderer>(true) : null;
            if (swirlRenderer != null && swirlRenderer.sharedMaterial != null && swirlRenderer.sharedMaterial.HasProperty(BaseColor)) swirlColour = swirlRenderer.sharedMaterial.GetColor(BaseColor);
            Renderer anySplash = splashRenderers.Length > 0 ? splashRenderers[0] : null;
            if (anySplash != null && anySplash.sharedMaterial != null && anySplash.sharedMaterial.HasProperty(BaseColor)) splashColour = anySplash.sharedMaterial.GetColor(BaseColor);
        }

        private void Present(float level, CarWaterFlow flow, bool all)
        {
            Cache();
            float span = water != null ? water.SpanMeters : 3.5f;
            float t = SharedSeconds();
            float landing = Mathf.Max(level, floorTop);
            bool filling = flow == CarWaterFlow.Filling;
            bool draining = flow == CarWaterFlow.Draining;
            int pouring = 0, drawn = 0;
            bool anyBubbles = false;
            int count = Mathf.Min(outlets.Length, NozzleCount);
            for (int i = 0; i < count; i++)
            {
                Vector3 o = outlets[i];
                bool covered = level >= o.y - 0.05f;
                if (filling) pouring++;

                // The stream: from the nozzle down to the water (or the floor), in the air only.
                bool streamOn = all || (filling && !covered);
                float length = Mathf.Max(0.05f, o.y - landing);
                if (i < streams.Length && streams[i] != null)
                {
                    SetActive(streams[i], streamOn);
                    if (streamOn)
                    {
                        drawn++;
                        streams[i].localPosition = new Vector3(o.x, landing + length * 0.5f, o.z);
                        streams[i].localScale = new Vector3(streamWidth, length, streamWidth);
                        Scroll(streamRenderers[i], new Vector4(1f, length / streamTextureMeters, 0.13f * i, t * streamScroll + 0.37f * i));
                    }
                }

                // The splash where it lands: foam spinning slowly, breathing with the pour.
                bool splashOn = streamOn;
                if (i < splashes.Length && splashes[i] != null)
                {
                    SetActive(splashes[i], splashOn);
                    if (splashOn)
                    {
                        float breathe = 1f + 0.12f * Mathf.Sin(t * 7.3f + i * 1.7f);
                        splashes[i].localPosition = new Vector3(o.x, landing + 0.012f + 0.001f * i, o.z);
                        splashes[i].localRotation = Quaternion.Euler(90f, t * 80f + i * 60f, 0f);
                        splashes[i].localScale = new Vector3(splashSize * breathe, splashSize * breathe, 1f);
                        Tint(splashRenderers[i], splashColour, level > floorTop ? 1f : 0.7f);
                    }
                }

                // Bubbles from a covered nozzle while it pours or while the car stands full;
                // and under a stream still pouring through the air, the plunge it drives
                // into the water (what an eye under the surface sees of the pour).
                bool fromNozzle = all || (covered && !draining && level > 0.5f);
                bool plunge = !fromNozzle && streamOn && level > floorTop + 0.3f;
                bool bubblesOn = fromNozzle || plunge;
                if (fromNozzle) anyBubbles = true;
                if (i < bubbles.Length && bubbles[i] != null)
                {
                    SetActive(bubbles[i], bubblesOn);
                    if (bubblesOn)
                    {
                        float top = Mathf.Min(all ? landing : level, span) - 0.04f;
                        float bottom = plunge ? Mathf.Max(floorTop + 0.05f, level - plungeMeters) : floorTop + 0.05f;
                        float h = Mathf.Max(0.1f, top - bottom);
                        bubbles[i].localPosition = new Vector3(o.x, bottom + h * 0.5f, o.z);
                        bubbles[i].localScale = new Vector3(bubbleWidth, h, bubbleWidth);
                        Scroll(bubbleRenderers[i], new Vector4(1f, h / bubbleTextureMeters, 0.21f * i, -t * bubbleScroll + 0.29f * i));
                    }
                }
            }

            // The drain: foam gathers on the falling surface and swirls out to the grilles.
            float level01 = span > 0f ? Mathf.Clamp01(level / span) : 0f;
            Drain01 = draining ? 1f - level01 : 0f;
            bool swirlOn = all || draining;
            if (swirl != null)
            {
                SetActive(swirl, swirlOn);
                if (swirlOn)
                {
                    swirl.localPosition = new Vector3(drainCentre.x, Mathf.Max(level, floorTop) + 0.014f, drainCentre.z);
                    swirl.localRotation = Quaternion.Euler(90f, -t * swirlDegreesPerSecond, 0f);
                    swirl.localScale = new Vector3(swirlSize, swirlSize, 1f);
                    Tint(swirlRenderer, swirlColour, all ? 1f : 0.35f + 0.65f * Drain01);
                }
            }

            // Ripples: the surface's normal map drifts, faster while the water moves.
            if (surfaceRenderer != null && surfaceRenderer.gameObject.activeInHierarchy)
            {
                float speed = rippleScroll * (flow == CarWaterFlow.Still ? 1f : 3f);
                block.Clear();
                block.SetVector(BaseMapSt, new Vector4(rippleTiling, rippleTiling, t * speed, t * speed * 0.6f));
                surfaceRenderer.SetPropertyBlock(block);
            }

            ActiveStreams = all ? 0 : pouring;
            VisibleStreams = all ? 0 : drawn;
            Bubbles = !all && anyBubbles;
            Foam01 = all ? 0f : filling ? (count > 0 ? (float)drawn / count : 0f) : draining ? 0.35f + 0.65f * Drain01 : 0f;
            if (all) Drain01 = 0f;
        }

        // The phases' clock: the network tick every peer shares (the same clock the car's
        // position is driven from), so a stream's scroll and a splash's spin match between
        // the diver's screen, a spectator's and the TV. Wrapped each hour to keep float
        // precision; the local clock without a session (the editor, the warm-up in a test).
        public static float SharedSeconds()
        {
            TimeManager time = InstanceFinder.TimeManager;
            if (time != null && time.Tick != 0)
                return (float)((time.TicksToTime(time.Tick) + time.GetTickElapsedAsDouble()) % 3600.0);
            return Time.time;
        }

        private void Scroll(Renderer[] renderers, Vector4 st)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                block.Clear();
                block.SetVector(BaseMapSt, st);
                renderer.SetPropertyBlock(block);
            }
        }

        private void Tint(Renderer renderer, Color colour, float alpha)
        {
            if (renderer == null) return;
            block.Clear();
            colour.a *= Mathf.Clamp01(alpha);
            block.SetColor(BaseColor, colour);
            renderer.SetPropertyBlock(block);
        }

        private static void SetActive(Transform element, bool on)
        {
            if (element.gameObject.activeSelf != on) element.gameObject.SetActive(on);
        }
    }
}
