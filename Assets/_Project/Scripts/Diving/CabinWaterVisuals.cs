using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Diving
{
    // What the car's water looks like doing (the new elevator, 28 September 2026; the
    // powerful jets, Dan, 28 September 2026: "keep ~4 s and make them POWERFUL JETS").
    //
    // While the car fills: six thick jets from the roof nozzles' exact mouths (the
    // "Flood Outlet n" empties, their outflow along the empty's -up), each a tapered 3D
    // tube with a spray sheath, following the ballistic path of water leaving the nozzle
    // at jetSpeed (a short arc for a tilted nozzle, then the fall), so it reads from
    // every angle; spray and mist at the mouths and where they land; white churning foam
    // and heaving water on the rising surface; and under each jet a plunge of bubbles
    // that rise back to the surface. After the car is full, a short fizz (fizzSeconds)
    // fades out; while it drains, foam swirls out to the floor grilles and a few bubbles
    // gurgle up from them. A still car (dry, or full: at the bottom, in the ride's still
    // phases) shows nothing moving: no air enters the water, so nothing bubbles.
    //
    // Stateless and shared: every frame is recomputed from CabinWater (the level, the
    // flow), the car's own position and motion profile (ElevatorController, driven on
    // every peer from the server's tick-anchored ElevatorPhase) and the shared network
    // clock (SharedSeconds). A particle's birth, size, path and death are hash functions
    // of (nozzle, slot, cycle) and the ride's history is recovered from the car's
    // position (the profile's speeds), so the diver, a spectator, the ship TV and a late
    // joiner draw the same jets, foam and bubbles. The ParticleSystems only draw what
    // this sets (no emission, no simulation of their own). No per-frame allocation: the
    // meshes, the particle buffers and the property block are made once.
    //
    // On "Cabin Water FX", a direct child of the car root at identity, so its local space
    // is the car root's (ShaftTubeSetup.AddCabinWater builds it).
    [DefaultExecutionOrder(100)]
    public sealed class CabinWaterVisuals : MonoBehaviour
    {
        public const int NozzleCount = 6;
        // The FX layout this code expects; ShaftTubeSetup rebuilds an older one.
        public const int BuildVersion = 2;

        private const int JetRings = 14, JetSides = 10, JetShells = 2;
        private const int ChurnRings = 14, ChurnSegments = 48;
        private const float Gravity = 9.81f;
        private const float CoverMargin = 0.05f;      // a nozzle this far under the level is covered

        [SerializeField] private CabinWater water;
        [SerializeField] private Renderer surfaceRenderer;             // the "Cabin Water" disc (ripples)
        [SerializeField] private Vector3[] outlets = new Vector3[0];    // nozzle mouths, car-root local
        [SerializeField] private Vector3[] outletDirections = new Vector3[0]; // outflow, car-root local (unit)
        [SerializeField] private Vector3 drainCentre = new(0f, 0.10f, 0f);
        [SerializeField] private float floorTop = 0.10f;
        [SerializeField] private MeshFilter[] jets = new MeshFilter[0];   // "Jet n": meshes made at runtime
        [SerializeField] private Transform[] splashes = new Transform[0]; // "Splash n": foam where a jet lands
        [SerializeField] private MeshFilter churn;                        // "Churn": foam and heave on the surface
        [SerializeField] private Transform swirl;                         // "Drain Swirl"
        [SerializeField] private ParticleSystem bubbleParticles;          // "Bubbles": drawn only, never emits
        [SerializeField] private ParticleSystem sprayParticles;           // "Spray": drawn only, never emits
        [SerializeField] private int builtVersion;

        [Header("Jets")]
        [SerializeField] private float jetSpeed = 6.5f;           // m/s out of the mouth
        [SerializeField] private float mouthRadius = 0.048f;      // the pipe's bore (outer r 0.06, measured)
        [SerializeField] private float jetEndRadius = 0.105f;     // where it lands: a thick, breaking jet
        [SerializeField] private float sheathScale = 2.4f;        // the spray sheath at the landing, x the core
        [SerializeField] private float jetStartInside = 0.03f;    // the jet starts this far up the pipe (no gap)
        [SerializeField] private float jetWobble = 0.06f;         // sway of the lower jet, m
        [SerializeField] private float jetTextureRate = 8f;       // texture lengths leaving the nozzle per second
        [SerializeField] private float jetRampSeconds = 0.15f;    // the pour's start and a jet's front

        [Header("Splash and churn")]
        [SerializeField] private float splashSize = 1.35f;
        [SerializeField] private float churnRadius = 2.42f;       // inside the surface disc (2.47)
        [SerializeField] private float churnMound = 0.05f;        // the heave where a jet lands, m
        [SerializeField] private float churnRipple = 0.02f;       // the rings running out from it, m
        [SerializeField] private float churnLift = 0.006f;        // above the surface disc
        [SerializeField] private float rippleScroll = 0.035f;
        [SerializeField] private float rippleTiling = 2.2f;
        [SerializeField] private float bumpCalm = 0.55f;
        [SerializeField] private float bumpPouring = 1.1f;

        [Header("Bubbles")]
        [SerializeField] private int plungeBubblesPerJet = 48;
        [SerializeField] private float plungeMeters = 1.2f;       // how deep a jet drives its bubbles
        [SerializeField] private int fizzBubbles = 140;
        [SerializeField] private float fizzSeconds = 2f;          // the fizz after the car is full
        [SerializeField] private int drainBubbles = 36;
        [SerializeField] private float drainInnerRadius = 1.80f, drainOuterRadius = 2.05f;
        [SerializeField] private float bubbleMinSize = 0.012f, bubbleMaxSize = 0.055f;

        [Header("Spray")]
        [SerializeField] private int mouthMistPerJet = 24;
        [SerializeField] private int dropletsPerJet = 48;
        [SerializeField] private int impactMistPerJet = 28;

        [Header("Drain")]
        [SerializeField] private float swirlSize = 4.5f;
        [SerializeField] private float swirlDegreesPerSecond = 95f;

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BumpScale = Shader.PropertyToID("_BumpScale");

        private MaterialPropertyBlock block;
        private bool runtimeReady, warmPose;
        private Mesh[] jetMeshes;
        private Vector3[] jetVerts;
        private Vector2[] jetUvs;
        private Color32[] jetColors;
        private Mesh churnMesh;
        private Vector3[] churnVerts;
        private Vector2[] churnUvs, churnBase;
        private Color32[] churnColors;
        private Renderer[] splashRenderers;
        private Renderer swirlRenderer;
        private Color swirlColour = Color.white, splashColour = Color.white;
        private ParticleSystem.Particle[] bubbleBuffer, sprayBuffer;
        private int bubblesDrawn, sprayDrawn;
        private readonly Vector3[] impacts = new Vector3[NozzleCount];
        private readonly float[] jetLevels = new float[NozzleCount];   // 0..1 how strongly each jet pours now
        private readonly bool[] landed = new bool[NozzleCount];

        // For the tests' snapshot (pour=, foam=, bubbles=, drain=) — same meanings as before
        // the rework, except Bubbles (below).
        public int ActiveStreams { get; private set; }   // nozzles pouring (in air or under the surface): 6 while filling
        public int VisibleStreams { get; private set; }  // jets drawn through the air
        public float Foam01 { get; private set; }         // filling: the share of jets landing; draining: 0.35 + 0.65 * Drain01
        // Air entering the water: the plunge under the jets while filling, and the fizz for
        // fizzSeconds after full. False in a still car (dry, or full) and while draining.
        public bool Bubbles { get; private set; }
        public float Drain01 { get; private set; }        // 1 - Level01 while draining, else 0
        // New (the rework): what is drawn this frame.
        public int BubbleCount { get; private set; }      // every bubble particle, the drain's included
        public bool DrainBubbles { get; private set; }    // bubbles gurgling up from the grilles
        public int SprayCount { get; private set; }
        public float Churn01 { get; private set; }        // the surface foam's strength
        public float Fizz01 { get; private set; }         // 1 at full, 0 fizzSeconds later
        public CabinWater Water => water;
        public int OutletCount => outlets != null ? outlets.Length : 0;
        public Vector3 OutletLocal(int i) => outlets[i];
        public Vector3 OutletDirectionLocal(int i) =>
            outletDirections != null && i < outletDirections.Length && outletDirections[i].sqrMagnitude > 1e-6f ? outletDirections[i].normalized : Vector3.down;
        public int BuiltVersion => builtVersion;

        public void Configure(CabinWater cabinWater, Renderer surface, Vector3[] outletPoints, Vector3[] outletOutflows, Vector3 drain,
            float floorTopLocal, MeshFilter[] jetFilters, Transform[] splashRoots, MeshFilter churnFilter, Transform swirlRoot,
            ParticleSystem bubbleSystem, ParticleSystem spraySystem)
        {
            water = cabinWater; surfaceRenderer = surface; outlets = outletPoints; outletDirections = outletOutflows;
            drainCentre = drain; floorTop = floorTopLocal; jets = jetFilters; splashes = splashRoots; churn = churnFilter;
            swirl = swirlRoot; bubbleParticles = bubbleSystem; sprayParticles = spraySystem;
            builtVersion = BuildVersion;
            runtimeReady = false;
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

        private void OnDestroy()
        {
            if (jetMeshes != null)
                foreach (Mesh mesh in jetMeshes)
                    if (mesh != null) Destroy(mesh);
            if (churnMesh != null) Destroy(churnMesh);
        }

        // ---- the ride's history, from the car's position -------------------------------

        // The water's history on the way down, recovered from where the car is: the raw
        // depth of sea over the car's floor rises at the profile's crossing speed while the
        // car's span crosses the surface (the pour) and at its travel speed after (full).
        private struct Ride
        {
            public bool Descending;
            public float Raw, Span, Crossing, Travel;
            public float SincePour;   // seconds since the floor went under (< 0: not yet)
            public float SinceFull;   // seconds since the roof went under (< 0: not yet)
            public bool Warm;
            public float WarmLevel;

            // The raw depth `age` seconds ago on this descent (< 0: the car was still dry).
            public float RawAgo(float age)
            {
                if (Warm) return WarmLevel;
                float tb = SincePour - age;
                if (tb < 0f) return -1f;
                float fill = Span / Crossing;
                return tb <= fill ? tb * Crossing : Span + (tb - fill) * Travel;
            }
        }

        private Ride ReadRide(float level, float span, bool all)
        {
            var ride = new Ride { Span = span, Crossing = 1f, Travel = 3f, SincePour = -1f, SinceFull = -1f };
            if (all) { ride.Warm = true; ride.WarmLevel = level; ride.Descending = true; ride.Raw = level; ride.SincePour = 5f; return ride; }
            ElevatorController car = water != null ? water.Controller : null;
            if (car == null) return ride;
            ElevatorMath.Profile profile = car.Profile;
            ride.Crossing = profile.CrossingSpeed;
            ride.Travel = profile.TravelSpeed;
            ride.Raw = car.SeaLevelY - car.transform.position.y;
            ride.Descending = car.State == ElevatorState.Descending;
            if (!ride.Descending || ride.Raw <= 0f) return ride;
            ride.SincePour = ride.Raw <= span ? ride.Raw / ride.Crossing : span / ride.Crossing + (ride.Raw - span) / ride.Travel;
            ride.SinceFull = ride.Raw <= span ? -1f : (ride.Raw - span) / ride.Travel;
            return ride;
        }

        // ---- the frame ------------------------------------------------------------------

        private void Present(float level, CarWaterFlow flow, bool all)
        {
            EnsureRuntime();
            float span = water != null && water.SpanMeters > 0f ? water.SpanMeters : 3.5f;
            float t = SharedSeconds();
            Ride ride = ReadRide(level, span, all);
            bool filling = flow == CarWaterFlow.Filling;
            bool draining = flow == CarWaterFlow.Draining;
            float landing = Mathf.Max(level, floorTop);
            int count = Mathf.Min(OutletCount, NozzleCount);
            float ramp = all ? 1f : ride.SincePour >= 0f ? Mathf.Clamp01(ride.SincePour / Mathf.Max(0.01f, jetRampSeconds)) : 1f;
            float front = all || ride.SincePour < 0f ? float.MaxValue : ride.SincePour;
            int pouring = 0, drawn = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 tip = outlets[i];
                bool covered = level >= tip.y - CoverMargin;
                if (filling) pouring++;
                bool jetOn = all || (filling && !covered);
                jetLevels[i] = jetOn ? ramp : 0f;
                landed[i] = false;
                if (i < jets.Length && jets[i] != null)
                {
                    SetActive(jets[i].transform, jetOn);
                    if (jetOn)
                    {
                        drawn++;
                        landed[i] = BuildJet(i, tip, OutletDirectionLocal(i), landing, front, ramp, t, out impacts[i]);
                    }
                }

                // The foam where it lands: a churning patch, spinning, breathing with the pour.
                bool splashOn = jetOn && landed[i];
                if (i < splashes.Length && splashes[i] != null)
                {
                    SetActive(splashes[i], splashOn);
                    if (splashOn)
                    {
                        float breathe = 1f + 0.14f * Mathf.Sin(t * 9.3f + i * 1.7f);
                        splashes[i].localPosition = new Vector3(impacts[i].x, landing + churnLift + churnMound * 0.6f + 0.001f * i, impacts[i].z); // in the heave's crest
                        splashes[i].localRotation = Quaternion.Euler(90f, t * 140f + i * 60f, 0f);
                        float size = splashSize * breathe * ramp;
                        splashes[i].localScale = new Vector3(size, size, 1f);
                        Tint(i < splashRenderers.Length ? splashRenderers[i] : null, splashColour, level > floorTop ? 1f : 0.75f);
                    }
                }
            }

            float level01 = Mathf.Clamp01(level / span);
            Drain01 = draining ? 1f - level01 : 0f;
            PresentChurn(all || (filling && drawn > 0 && level > ElevatorMath.WaterMarginMeters && level < span - ElevatorMath.WaterMarginMeters), count, level, t, (float)drawn / Mathf.Max(1, count) * ramp);
            PresentSwirl(all || draining, level, t);
            PresentRipples(flow, filling && drawn > 0, t);
            int pourBubbles = PresentBubbles(ride, all, draining, count, level, span, t, out int gurgle);
            PresentSpray(all, count, level, t);

            ActiveStreams = all ? 0 : pouring;
            VisibleStreams = all ? 0 : drawn;
            Bubbles = !all && pourBubbles > 0;
            DrainBubbles = !all && gurgle > 0;
            BubbleCount = all ? 0 : bubblesDrawn;
            SprayCount = all ? 0 : sprayDrawn;
            Foam01 = all ? 0f : filling ? (count > 0 ? (float)drawn / count : 0f) : draining ? 0.35f + 0.65f * Drain01 : 0f;
            Fizz01 = all || ride.SinceFull < 0f ? 0f : Mathf.Clamp01(1f - ride.SinceFull / Mathf.Max(0.01f, fizzSeconds));
            if (all) { Drain01 = 0f; Churn01 = 0f; }
        }

        // ---- the jets -------------------------------------------------------------------

        // One jet's mesh: two shells (the core and a spray sheath) of JetRings rings along the
        // ballistic path from the mouth (started jetStartInside up the pipe) to where it meets
        // the water or the floor, or to its front while the pour is just starting. The rings
        // are spaced in flight time; the texture rides with the water (v = the time the
        // parcel left the nozzle), so it never stretches with the jet's length. True when
        // the jet reaches the water (its landing point in `impact`).
        private bool BuildJet(int i, Vector3 tip, Vector3 dir, float landing, float front, float ramp, float t, out Vector3 impact)
        {
            float vy = dir.y * jetSpeed;
            float h = Mathf.Max(0.02f, tip.y - landing);
            float tLand = (vy + Mathf.Sqrt(vy * vy + 2f * Gravity * h)) / Gravity;
            bool reached = front >= tLand;
            float tEnd = reached ? tLand : Mathf.Max(0.01f, front);
            float vRate = jetTextureRate;
            float baseV = t * vRate - Mathf.Floor(t * vRate);
            int perShell = JetRings * (JetSides + 1);

            for (int j = 0; j < JetRings; j++)
            {
                float f = (float)j / (JetRings - 1);
                float tau = tEnd * Mathf.Pow(f, 1.2f);
                Vector3 centre = JetPoint(i, tip, dir, tau, t);
                if (j == 0) centre -= dir * jetStartInside;
                Vector3 velocity = dir * jetSpeed + new Vector3(0f, -Gravity * tau, 0f);
                Vector3 along = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector3.down;
                Vector3 reference = Mathf.Abs(along.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 n = Vector3.Cross(along, reference).normalized;
                Vector3 b = Vector3.Cross(along, n);

                float flight = Mathf.Clamp01(tau / Mathf.Max(0.001f, tLand));
                float core = Mathf.Lerp(mouthRadius, jetEndRadius, Mathf.Pow(flight, 0.8f));
                bool leading = !reached && j == JetRings - 1;
                if (leading) core *= 0.65f;
                float sheath = core * Mathf.Lerp(1.05f, sheathScale, flight);
                float v = baseV - tau * vRate;
                byte coreAlpha = (byte)(Mathf.Lerp(235f, 195f, flight) * ramp * (leading ? 0.5f : 1f));
                byte sheathAlpha = (byte)(Mathf.Lerp(0f, 85f, flight) * ramp * (leading ? 0.4f : 1f));

                for (int s = 0; s <= JetSides; s++)
                {
                    float u = (float)s / JetSides;
                    float angle = u * Mathf.PI * 2f;
                    Vector3 radial = n * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                    float noiseCore = 1f + 0.14f * Mathf.Sin(3f * angle + t * 11f + tau * 23f + i) + 0.08f * Mathf.Sin(5f * angle - t * 7f + tau * 31f);
                    float noiseSheath = 1f + 0.25f * Mathf.Sin(2f * angle - t * 13f + tau * 17f + i * 2f) + 0.15f * Mathf.Sin(7f * angle + t * 5f);
                    int c = j * (JetSides + 1) + s;
                    int o = perShell + c;
                    jetVerts[c] = centre + radial * (core * noiseCore);
                    jetVerts[o] = centre + radial * (sheath * noiseSheath);
                    jetUvs[c] = new Vector2(u, v);
                    jetUvs[o] = new Vector2(u * 2f + 0.37f, v * 0.7f + 0.21f);
                    jetColors[c] = new Color32(255, 255, 255, coreAlpha);
                    jetColors[o] = new Color32(255, 255, 255, sheathAlpha);
                }
            }

            Mesh mesh = jetMeshes[i];
            int count = perShell * JetShells;
            mesh.SetVertices(jetVerts, 0, count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetUVs(0, jetUvs, 0, count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetColors(jetColors, 0, count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            impact = JetPoint(i, tip, dir, tLand, t);
            impact.y = landing;
            return reached;
        }

        // The water's path out of nozzle i, tau seconds after it left: the ballistic arc
        // plus a sway that grows as the jet falls and breaks up.
        private Vector3 JetPoint(int i, Vector3 tip, Vector3 dir, float tau, float t)
        {
            Vector3 p = tip + dir * (jetSpeed * tau) + new Vector3(0f, -0.5f * Gravity * tau * tau, 0f);
            float grow = Mathf.Clamp01(tau / 0.35f);
            float sway = jetWobble * grow * grow;
            p.x += sway * Mathf.Sin(t * 7.3f + i * 1.9f + tau * 11f);
            p.z += sway * Mathf.Cos(t * 6.1f + i * 2.7f + tau * 9f);
            return p;
        }

        // ---- the surface: churn, swirl, ripples -------------------------------------------

        // White churning foam and a heaving surface while the jets pour: a polar grid over
        // the surface disc, lifted into mounds where the jets land and rings running out from
        // them, a slow slosh over the whole car, and foam thickest round each landing.
        private void PresentChurn(bool on, int count, float level, float t, float strength)
        {
            Churn01 = on ? strength : 0f;
            if (churn == null) return;
            SetActive(churn.transform, on);
            if (!on) return;
            churn.transform.localPosition = new Vector3(0f, level + churnLift, 0f);
            float global = 0.18f + 0.12f * strength;
            for (int v = 0; v < churnBase.Length; v++)
            {
                float x = churnBase[v].x, z = churnBase[v].y;
                float height = 0.012f * strength * Mathf.Sin(1.3f * x + 2.1f * t) * Mathf.Cos(1.7f * z - 1.6f * t);
                float foam = global;
                for (int i = 0; i < count; i++)
                {
                    float pour = jetLevels[i];
                    if (pour <= 0f || !landed[i]) continue;
                    float dx = x - impacts[i].x, dz = z - impacts[i].z;
                    float d2 = dx * dx + dz * dz;
                    float d = Mathf.Sqrt(d2);
                    float mound = Mathf.Exp(-d2 * 6f);
                    height += churnMound * pour * mound * (0.7f + 0.3f * Mathf.Sin(t * 17f + i * 2.1f));
                    height += churnRipple * pour * Mathf.Exp(-d * 1.2f) * Mathf.Sin(d * 9f - t * 11f + i * 1.3f);
                    foam += pour * (mound * 1.6f + Mathf.Exp(-d2 * 1.6f) * 0.35f);
                }
                foam *= 0.72f + 0.28f * Mathf.Sin(x * 7.1f + t * 1.3f) * Mathf.Sin(z * 6.3f - t * 1.1f);
                float r = Mathf.Sqrt(x * x + z * z) / churnRadius;
                float edge = Mathf.Clamp01((1f - r) * 12f);
                churnVerts[v] = new Vector3(x, height, z);
                churnColors[v] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(foam * 1.3f) * Mathf.Clamp01(strength * 1.4f) * edge * 255f));
                churnUvs[v] = new Vector2(x * 0.8f + t * 0.05f + 0.03f * Mathf.Sin(t * 0.7f + z), z * 0.8f - t * 0.035f);
            }
            churnMesh.SetVertices(churnVerts, 0, churnVerts.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            churnMesh.SetUVs(0, churnUvs, 0, churnUvs.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            churnMesh.SetColors(churnColors, 0, churnColors.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        // The drain: foam gathers on the falling surface and swirls out to the grilles.
        private void PresentSwirl(bool on, float level, float t)
        {
            if (swirl == null) return;
            SetActive(swirl, on);
            if (!on) return;
            swirl.localPosition = new Vector3(drainCentre.x, Mathf.Max(level, floorTop) + 0.014f, drainCentre.z);
            swirl.localRotation = Quaternion.Euler(90f, -t * swirlDegreesPerSecond, 0f);
            swirl.localScale = new Vector3(swirlSize, swirlSize, 1f);
            Tint(swirlRenderer, swirlColour, warmPose ? 1f : 0.35f + 0.65f * Drain01);
        }

        // Ripples: the surface's normal map drifts, faster and deeper while the jets pour.
        private void PresentRipples(CarWaterFlow flow, bool pouring, float t)
        {
            if (surfaceRenderer == null || !surfaceRenderer.gameObject.activeInHierarchy) return;
            float speed = rippleScroll * (pouring ? 5f : flow == CarWaterFlow.Still ? 1f : 3f);
            block.Clear();
            block.SetVector(BaseMapSt, new Vector4(rippleTiling, rippleTiling, t * speed, t * speed * 0.6f));
            block.SetFloat(BumpScale, pouring ? bumpPouring : bumpCalm);
            surfaceRenderer.SetPropertyBlock(block);
        }

        // ---- bubbles --------------------------------------------------------------------

        // Every bubble is a slot with a fixed lifetime that restarts each cycle; the cycle's
        // hash picks its size, path and wobble, and it exists only if the water was taking
        // air in where it was born at its birth (the ride's history). Returns how many are
        // the pour's and the fizz's (the Bubbles flag); `gurgle` counts the drain's.
        private int PresentBubbles(Ride ride, bool all, bool draining, int count, float level, float span, float t, out int gurgle)
        {
            int n = 0;
            gurgle = 0;
            if (bubbleParticles == null) { bubblesDrawn = 0; return 0; }
            int max = bubbleBuffer.Length;

            // The plunge: under each jet, bubbles driven down and rising back to the surface;
            // under a covered nozzle still pouring, a plume from its mouth. Born only while
            // the car was filling (so they finish rising after it is full, then stop).
            bool pourWindow = all || (ride.Descending && ride.SincePour >= 0f && (ride.SinceFull < 0f || ride.SinceFull < 1.5f));
            if (pourWindow)
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 tip = outlets[i];
                    for (int k = 0; k < plungeBubblesPerJet && n < max; k++)
                    {
                        uint slot = Hash((uint)(i * 977 + k * 31 + 1));
                        float life = Mathf.Lerp(0.7f, 1.3f, Unit(slot, 0));
                        float cycle = (t + Unit(slot, 1) * life) / life;
                        int number = Mathf.FloorToInt(cycle);
                        float age = (cycle - number) * life;
                        uint seed = Hash(slot ^ (uint)number * 2654435761u);
                        float rawAtBirth = ride.RawAgo(age);
                        if (rawAtBirth <= 0.25f || rawAtBirth >= span - ElevatorMath.WaterMarginMeters) continue;
                        if (Unit(seed, 0) > Mathf.Clamp01((rawAtBirth - 0.25f) / 0.4f)) continue;
                        bool coveredAtBirth = rawAtBirth >= tip.y - CoverMargin;
                        float u = age / life;
                        float size = Mathf.Lerp(bubbleMinSize, bubbleMaxSize, Mathf.Pow(Unit(seed, 1), 2.2f));
                        float depthRoom = Mathf.Max(0.05f, level - floorTop - size);
                        float dive = Mathf.Min(depthRoom, Mathf.Lerp(0.35f, plungeMeters, Unit(seed, 2)) * (coveredAtBirth ? 0.7f : 1f));
                        float depth = u < 0.25f ? dive * (1f - (1f - u / 0.25f) * (1f - u / 0.25f)) : dive * Mathf.Pow(1f - (u - 0.25f) / 0.75f, 1.4f);
                        float y = coveredAtBirth ? Mathf.Min(tip.y - CoverMargin, level) - 0.04f - depth : level - depth;
                        float spread = (0.04f + 0.30f * Unit(seed, 3)) * (0.35f + 0.65f * (dive > 0f ? depth / dive : 0f)) + 0.1f * u;
                        float angle = Unit(seed, 4) * Mathf.PI * 2f + u * 1.5f * (Unit(seed, 5) - 0.5f);
                        float wobble = 0.025f;
                        float x = tip.x + Mathf.Cos(angle) * spread + wobble * Mathf.Sin(age * (9f + 6f * Unit(seed, 6)) + Unit(seed, 7) * 6.28f);
                        float z = tip.z + Mathf.Sin(angle) * spread + wobble * Mathf.Cos(age * (8f + 5f * Unit(seed, 8)) + Unit(seed, 9) * 6.28f);
                        float alpha = 0.85f * Mathf.Min(Mathf.Clamp01(age / 0.06f), Mathf.Clamp01((1f - u) / 0.12f));
                        AddBubble(ref n, x, y, z, size, alpha, level);
                    }
                }
            }

            // The fizz: for fizzSeconds after the roof goes under, fine bubbles rise through the
            // whole car to the roof, fewer and fewer.
            bool fizzWindow = all || (ride.Descending && ride.SinceFull >= 0f && ride.SinceFull < fizzSeconds + 1.6f);
            if (fizzWindow)
            {
                for (int k = 0; k < fizzBubbles && n < max; k++)
                {
                    uint slot = Hash((uint)(7919 + k * 131));
                    float life = Mathf.Lerp(0.8f, 1.5f, Unit(slot, 0));
                    float cycle = (t + Unit(slot, 1) * life) / life;
                    int number = Mathf.FloorToInt(cycle);
                    float age = (cycle - number) * life;
                    uint seed = Hash(slot ^ (uint)number * 2654435761u);
                    float sinceFullAtBirth = all ? 0.5f : ride.SinceFull - age;
                    if (sinceFullAtBirth < 0f || sinceFullAtBirth > fizzSeconds) continue;
                    float keep = 1f - sinceFullAtBirth / fizzSeconds;
                    if (Unit(seed, 0) > keep * keep) continue;
                    float r = 2.1f * Mathf.Sqrt(Unit(seed, 1));
                    float a = Unit(seed, 2) * Mathf.PI * 2f;
                    float size = Mathf.Lerp(0.008f, 0.03f, Mathf.Pow(Unit(seed, 3), 2f));
                    float y = Mathf.Lerp(floorTop + 0.1f, span - 0.4f, Unit(seed, 4)) + Mathf.Lerp(0.25f, 0.7f, Unit(seed, 5)) * age;
                    float x = Mathf.Cos(a) * r + 0.02f * Mathf.Sin(age * 10f + Unit(seed, 6) * 6.28f);
                    float z = Mathf.Sin(a) * r + 0.02f * Mathf.Cos(age * 9f + Unit(seed, 7) * 6.28f);
                    float alpha = 0.75f * Mathf.Min(Mathf.Clamp01(age / 0.08f), Mathf.Clamp01((life - age) / 0.2f));
                    AddBubble(ref n, x, y, z, size, alpha, level);
                }
            }
            int pour = n;

            // The drain: a few bubbles gurgle up from the grille ring while the car drains.
            if ((all || draining) && level > floorTop + 0.08f)
            {
                for (int k = 0; k < drainBubbles && n < max; k++)
                {
                    uint slot = Hash((uint)(104729 + k * 73));
                    float life = Mathf.Lerp(0.6f, 1.2f, Unit(slot, 0));
                    float cycle = (t + Unit(slot, 1) * life) / life;
                    int number = Mathf.FloorToInt(cycle);
                    float age = (cycle - number) * life;
                    uint seed = Hash(slot ^ (uint)number * 2654435761u);
                    if (Unit(seed, 0) > 0.55f) continue;
                    float a = Unit(seed, 1) * Mathf.PI * 2f;
                    float r = Mathf.Lerp(drainInnerRadius, drainOuterRadius, Unit(seed, 2));
                    float size = Mathf.Lerp(0.015f, 0.045f, Unit(seed, 3));
                    float y = floorTop + 0.03f + Mathf.Lerp(0.4f, 0.8f, Unit(seed, 4)) * age;
                    float x = drainCentre.x + Mathf.Cos(a) * r + 0.02f * Mathf.Sin(age * 11f + Unit(seed, 5) * 6.28f);
                    float z = drainCentre.z + Mathf.Sin(a) * r;
                    float alpha = 0.8f * Mathf.Min(Mathf.Clamp01(age / 0.06f), Mathf.Clamp01((life - age) / 0.15f));
                    int before = n;
                    AddBubble(ref n, x, y, z, size, alpha, level);
                    gurgle += n - before;
                }
            }

            if (n > 0 || bubblesDrawn > 0) bubbleParticles.SetParticles(bubbleBuffer, n);
            bubblesDrawn = n;
            return pour;
        }

        // Adds a bubble if it is under the water (none at or above the surface).
        private void AddBubble(ref int n, float x, float y, float z, float size, float alpha, float level)
        {
            if (alpha <= 0.01f || y > level - size * 0.6f) return;
            y = Mathf.Max(y, floorTop + size * 0.5f);
            SetParticle(bubbleBuffer, n++, new Vector3(x, y, z), size, alpha);
        }

        // ---- spray ----------------------------------------------------------------------

        // Mist bursting from each mouth and sliding down the jet; droplets flung up and out
        // where it lands, falling back into the water; mist rolling off the landing.
        private void PresentSpray(bool all, int count, float level, float t)
        {
            int n = 0;
            if (sprayParticles == null) { sprayDrawn = 0; SprayCount = 0; return; }
            int max = sprayBuffer.Length;
            for (int i = 0; i < count; i++)
            {
                float pour = jetLevels[i];
                if (pour <= 0f) continue;
                Vector3 tip = outlets[i];
                Vector3 dir = OutletDirectionLocal(i);
                for (int k = 0; k < mouthMistPerJet && n < max; k++)
                {
                    SprayCycle(i, k, 0, Mathf.Lerp(0.35f, 0.55f, Unit(Hash((uint)(i * 53 + k * 7 + 11)), 0)), t, out float age, out float u, out uint seed);
                    Vector3 p = JetPoint(i, tip, dir, age * 0.25f, t);
                    float a = Unit(seed, 0) * Mathf.PI * 2f;
                    float spread = 0.03f + 0.14f * u;
                    p += new Vector3(Mathf.Cos(a) * spread, 0f, Mathf.Sin(a) * spread);
                    if (p.y <= level) continue;
                    SetParticle(sprayBuffer, n++, p, Mathf.Lerp(0.08f, 0.24f, u), 0.5f * (1f - u) * pour);
                }
                if (!landed[i]) continue;
                Vector3 hit = impacts[i];
                for (int k = 0; k < dropletsPerJet && n < max; k++)
                {
                    uint slot = Hash((uint)(i * 389 + k * 17 + 5));
                    float up = Mathf.Lerp(1.4f, 3.2f, Unit(slot, 2));
                    float life = 2f * up / Gravity;
                    SprayCycle(i, k, 1, life, t, out float age, out float u, out uint seed);
                    float a = Unit(seed, 0) * Mathf.PI * 2f;
                    float outward = Mathf.Lerp(0.5f, 2.0f, Unit(seed, 1));
                    Vector3 p = new(hit.x + Mathf.Cos(a) * outward * age, hit.y + up * age - 0.5f * Gravity * age * age, hit.z + Mathf.Sin(a) * outward * age);
                    if (p.y <= level || p.x * p.x + p.z * p.z > 2.3f * 2.3f) continue;
                    SetParticle(sprayBuffer, n++, p, Mathf.Lerp(0.02f, 0.06f, Unit(seed, 3)), 0.8f * pour);
                }
                for (int k = 0; k < impactMistPerJet && n < max; k++)
                {
                    SprayCycle(i, k, 2, Mathf.Lerp(0.5f, 0.9f, Unit(Hash((uint)(i * 613 + k * 29 + 3)), 0)), t, out float age, out float u, out uint seed);
                    float a = Unit(seed, 0) * Mathf.PI * 2f;
                    float spread = 0.1f + 0.4f * u;
                    Vector3 p = new(hit.x + Mathf.Cos(a) * spread, hit.y + 0.05f + 0.6f * u, hit.z + Mathf.Sin(a) * spread);
                    SetParticle(sprayBuffer, n++, p, Mathf.Lerp(0.35f, 1.1f, u), 0.5f * (1f - u) * pour);
                }
            }
            if (n > 0 || sprayDrawn > 0) sprayParticles.SetParticles(sprayBuffer, n);
            sprayDrawn = n;
        }

        private static void SprayCycle(int i, int k, int kind, float life, float t, out float age, out float u, out uint seed)
        {
            uint slot = Hash((uint)(i * 1543 + k * 97 + kind * 50021 + 17));
            life = Mathf.Max(0.05f, life);
            float cycle = (t + Unit(slot, 1) * life) / life;
            int number = Mathf.FloorToInt(cycle);
            age = (cycle - number) * life;
            u = age / life;
            seed = Hash(slot ^ (uint)number * 2654435761u);
        }

        private static void SetParticle(ParticleSystem.Particle[] buffer, int index, Vector3 position, float size, float alpha)
        {
            ref ParticleSystem.Particle p = ref buffer[index];
            p.position = position;
            p.velocity = Vector3.zero;
            p.startSize = size;
            p.startColor = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            p.rotation = 0f;
            p.startLifetime = 5f;
            p.remainingLifetime = 5f;
        }

        // ---- set-up (once) ----------------------------------------------------------------

        private void EnsureRuntime()
        {
            if (runtimeReady) return;
            runtimeReady = true;
            block ??= new MaterialPropertyBlock();
            Bounds carBounds = new(new Vector3(0f, 1.8f, 0f), new Vector3(5.2f, 4.0f, 5.2f));

            int perShell = JetRings * (JetSides + 1);
            jetVerts = new Vector3[perShell * JetShells];
            jetUvs = new Vector2[jetVerts.Length];
            jetColors = new Color32[jetVerts.Length];
            int[] jetTriangles = JetTriangles();
            jetMeshes = new Mesh[jets.Length];
            for (int i = 0; i < jets.Length; i++)
            {
                if (jets[i] == null) continue;
                var mesh = new Mesh { name = "Cabin Jet " + (i + 1) };
                mesh.MarkDynamic();
                mesh.SetVertices(jetVerts);
                mesh.SetUVs(0, jetUvs);
                mesh.SetColors(jetColors);
                mesh.SetTriangles(jetTriangles, 0, false);
                mesh.bounds = carBounds;
                jetMeshes[i] = mesh;
                jets[i].sharedMesh = mesh;
            }

            if (churn != null)
            {
                int vertices = 1 + ChurnRings * ChurnSegments;
                churnBase = new Vector2[vertices];
                churnVerts = new Vector3[vertices];
                churnUvs = new Vector2[vertices];
                churnColors = new Color32[vertices];
                churnBase[0] = Vector2.zero;
                for (int r = 0; r < ChurnRings; r++)
                {
                    float radius = churnRadius * (r + 1) / ChurnRings;
                    for (int s = 0; s < ChurnSegments; s++)
                    {
                        float a = (s + 0.5f * (r & 1)) * Mathf.PI * 2f / ChurnSegments;
                        churnBase[1 + r * ChurnSegments + s] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                    }
                }
                for (int v = 0; v < vertices; v++) churnVerts[v] = new Vector3(churnBase[v].x, 0f, churnBase[v].y);
                churnMesh = new Mesh { name = "Cabin Churn" };
                churnMesh.MarkDynamic();
                churnMesh.SetVertices(churnVerts);
                churnMesh.SetUVs(0, churnUvs);
                churnMesh.SetColors(churnColors);
                churnMesh.SetTriangles(ChurnTriangles(), 0, false);
                churnMesh.bounds = new Bounds(Vector3.zero, new Vector3(churnRadius * 2f, 0.4f, churnRadius * 2f));
                churn.sharedMesh = churnMesh;
            }

            splashRenderers = new Renderer[splashes.Length];
            for (int i = 0; i < splashes.Length; i++) splashRenderers[i] = splashes[i] != null ? splashes[i].GetComponentInChildren<Renderer>(true) : null;
            swirlRenderer = swirl != null ? swirl.GetComponentInChildren<Renderer>(true) : null;
            if (swirlRenderer != null && swirlRenderer.sharedMaterial != null && swirlRenderer.sharedMaterial.HasProperty(BaseColor)) swirlColour = swirlRenderer.sharedMaterial.GetColor(BaseColor);
            Renderer anySplash = splashRenderers.Length > 0 ? splashRenderers[0] : null;
            if (anySplash != null && anySplash.sharedMaterial != null && anySplash.sharedMaterial.HasProperty(BaseColor)) splashColour = anySplash.sharedMaterial.GetColor(BaseColor);

            bubbleBuffer = new ParticleSystem.Particle[Mathf.Max(1, NozzleCount * plungeBubblesPerJet + fizzBubbles + drainBubbles)];
            sprayBuffer = new ParticleSystem.Particle[Mathf.Max(1, NozzleCount * (mouthMistPerJet + dropletsPerJet + impactMistPerJet))];
            PrepareSystem(bubbleParticles, bubbleBuffer.Length);
            PrepareSystem(sprayParticles, sprayBuffer.Length);
        }

        // The systems only draw what SetParticles gives them: no emission, no motion of
        // their own, local to the car; playing so the renderer draws.
        private static void PrepareSystem(ParticleSystem system, int capacity)
        {
            if (system == null) return;
            var main = system.main;
            if (main.maxParticles < capacity) main.maxParticles = capacity;
            var emission = system.emission;
            if (emission.enabled) emission.enabled = false;
            if (!system.isPlaying) system.Play(false);
        }

        private static int[] JetTriangles()
        {
            int ring = JetSides + 1;
            int perShell = JetRings * ring;
            var triangles = new int[JetShells * (JetRings - 1) * JetSides * 6];
            int t = 0;
            for (int shell = 0; shell < JetShells; shell++)
                for (int j = 0; j < JetRings - 1; j++)
                    for (int s = 0; s < JetSides; s++)
                    {
                        int a = shell * perShell + j * ring + s, b = a + 1, c = a + ring, d = c + 1;
                        triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                        triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                    }
            return triangles;
        }

        private static int[] ChurnTriangles()
        {
            var triangles = new int[(ChurnSegments + (ChurnRings - 1) * ChurnSegments * 2) * 3];
            int t = 0;
            for (int s = 0; s < ChurnSegments; s++)
            {
                triangles[t++] = 0; triangles[t++] = 1 + (s + 1) % ChurnSegments; triangles[t++] = 1 + s;
            }
            for (int r = 0; r < ChurnRings - 1; r++)
                for (int s = 0; s < ChurnSegments; s++)
                {
                    int a = 1 + r * ChurnSegments + s, b = 1 + r * ChurnSegments + (s + 1) % ChurnSegments;
                    int c = a + ChurnSegments, d = b + ChurnSegments;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            return triangles;
        }

        // ---- shared helpers ---------------------------------------------------------------

        // The phases' clock: the network tick every peer shares (the same clock the car's
        // position is driven from), so a jet's texture, a bubble's cycle and a splash's spin
        // match between the diver's screen, a spectator's and the TV. Wrapped each hour to
        // keep float precision; the local clock without a session (the editor, the warm-up
        // in a test).
        public static float SharedSeconds()
        {
            TimeManager time = InstanceFinder.TimeManager;
            if (time != null && time.Tick != 0)
                return (float)((time.TicksToTime(time.Tick) + time.GetTickElapsedAsDouble()) % 3600.0);
            return Time.time;
        }

        // A 32-bit integer hash (lowbias32) and a unit float from a seed and a channel.
        private static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352du;
                x ^= x >> 15; x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }

        private static float Unit(uint seed, int channel)
        {
            unchecked { return (Hash(seed + (uint)channel * 0x9E3779B9u) & 0xFFFFFF) / 16777216f; }
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
