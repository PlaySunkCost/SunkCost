using System.Collections.Generic;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Monsters
{
    // A LIGHT beam lights the dark dive site (Dan, 24 September 2026): the floor, the
    // walls and the diver along its path, and a splash where it lands. Point lights
    // are spread evenly down the drawn line, and there is one more just short of
    // the wall it meets. In the charge's last third they come up faintly, so the
    // seabed shows where it aims (the tell). While the beam burns they shimmer with
    // it, flash as it lights, flare when the hit lands, then dim with the fade. The
    // creature's own lamp (the Lure's LureLantern) carries the charge glow at the
    // mouth. A DARK beam casts no light; MonsterBeamShade darkens round it instead.
    //
    // Presentation only, from exactly what MonsterBeamView draws (Phase, Dark,
    // ShownFrom, ShownTo, ShownHalfWidth, ShownImpact) and CreatureBolts.Hit. Every
    // peer builds it the same way, so the host, the guests, the spectators and the TV
    // all see it. It runs after the view in the frame (DefaultExecutionOrder), so no
    // hook is needed. It never feeds gameplay: the monsters' senses read the divers'
    // replicated lamp bit (CreatureSenses.LampLit), never a Light.
    //
    // Cost: the lights are rented from a shared pool for one beam and returned
    // when the view goes out (or this is disabled). They cast no shadows, and
    // nothing is allocated per frame. The pool grows only to the most lights ever
    // lit at once: two beams at once are 2 × (pathLights + 1).
    [DefaultExecutionOrder(50)] // after MonsterBeamView (0) has drawn this frame's beam
    public sealed class MonsterBeamLight : MonoBehaviour
    {
        [SerializeField] private Color colour = new(1f, 0.87f, 0.58f);
        [Tooltip("The most lights along the path; a short beam uses fewer (one per pathSpacing).")]
        [SerializeField, Range(1, 8)] private int pathLights = 6;
        [Tooltip("One path light about every this many metres of drawn beam, up to pathLights.")]
        [SerializeField] private float pathSpacing = 3f;
        [Tooltip("Each path light's reach at least, metres. It grows with the spacing on a long beam so the pools of light meet.")]
        [SerializeField] private float pathRange = 4.5f;
        [SerializeField] private float pathIntensity = 3f;
        [Tooltip("In the charge's last third, the path glows this fraction of the burn: the seabed shows where it aims.")]
        [SerializeField, Range(0f, 1f)] private float chargeFraction = 0.12f;
        [Tooltip("The splash where the beam meets a wall: reach (metres) and brightness.")]
        [SerializeField] private float splashRange = 5f;
        [SerializeField] private float splashIntensity = 6f;
        [Tooltip("Extra brightness as the beam lights (falls off over 0.12 s) and when the hit lands (over 0.25 s).")]
        [SerializeField] private float flashBoost = 0.8f;
        [SerializeField] private float hitBoost = 1.5f;

        private const float FlashSeconds = 0.12f, HitSeconds = 0.25f;
        private MonsterBeamView view;
        private CreatureBolts bolts;
        private Light[] path;
        private Light splash;
        private bool held;
        private BeamPhase lastPhase = BeamPhase.None;
        private float phaseAt, hitAt = float.NegativeInfinity, seed;

        // For the checks: what is lit this frame.
        public bool Holding => held;
        public int LitCount { get; private set; }
        public int PathLitCount { get; private set; }
        public float PathIntensityShown { get; private set; }
        public float SplashIntensityShown => held && splash != null && splash.enabled ? splash.intensity : 0f;
        public Vector3 SplashPoint => splash != null ? splash.transform.position : Vector3.zero;
        public int PathCapacity => pathLights;
        public Light PathLight(int i) => held && path != null && i >= 0 && i < path.Length ? path[i] : null;
        public Light SplashLight => held ? splash : null;

        private void Awake()
        {
            bolts = GetComponent<CreatureBolts>();
            path = new Light[pathLights];
            seed = Random.value * 10f;
        }

        private void OnEnable() { if (bolts != null) bolts.Hit += OnHit; }
        private void OnDisable()
        {
            if (bolts != null) bolts.Hit -= OnHit;
            Release();
        }

        private void OnHit() => hitAt = Time.time;

        private void LateUpdate()
        {
            if (view == null) view = GetComponentInChildren<MonsterBeamView>();
            BeamPhase phase = view != null ? view.Phase : BeamPhase.None;
            if (phase == BeamPhase.None || view.Dark) { Release(); lastPhase = phase; return; }
            float now = Time.time;
            if (phase != lastPhase) { lastPhase = phase; phaseAt = now; }
            Vector3 from = view.ShownFrom, to = view.ShownTo;
            Vector3 along = to - from;
            float length = along.magnitude;
            Vector3 dir = length > 1e-4f ? along / length : transform.forward;
            if (!held) Acquire(from);

            float since = now - phaseAt;
            float full = bolts != null && bolts.HalfWidth > 0f ? bolts.HalfWidth : Mathf.Max(0.01f, view.ShownHalfWidth);
            float shimmer = 0.9f + 0.1f * Mathf.Sin(now * 60f + seed);
            float lit, splashLit;
            switch (phase)
            {
                case BeamPhase.Charging:
                {
                    // The ghost's timing (MonsterBeamView): the last third, and the tell's flicker in the last tenth.
                    float charge = MonsterSettings.Get().BeamChargeSeconds;
                    float t = charge <= 0f ? 1f : Mathf.Clamp01(since / charge);
                    float tell = t > 0.88f ? 0.6f + 0.4f * Mathf.Sin(now * 70f) : 1f;
                    float ghost = chargeFraction * Mathf.Clamp01((t - 0.62f) / 0.3f) * tell;
                    lit = pathIntensity * ghost;
                    // The view shows no impact while it charges; a wall within reach still takes a faint glow where it aims.
                    bool onWall = length < MonsterSettings.Get().BeamRangeMeters - 0.05f;
                    splashLit = onWall ? splashIntensity * ghost : 0f;
                    break;
                }
                case BeamPhase.Firing:
                {
                    float flash = since < FlashSeconds ? 1f + flashBoost * (1f - since / FlashSeconds) : 1f;
                    float flare = now - hitAt < HitSeconds ? 1f + hitBoost * (1f - (now - hitAt) / HitSeconds) : 1f;
                    lit = pathIntensity * flash * flare * shimmer;
                    splashLit = view.ShownImpact ? splashIntensity * flash * flare * shimmer : 0f;
                    break;
                }
                default:
                {
                    // Done: dims with the drawn width.
                    float fade = Mathf.Clamp01(view.ShownHalfWidth / full);
                    lit = pathIntensity * fade;
                    splashLit = view.ShownImpact ? splashIntensity * fade : 0f;
                    break;
                }
            }

            int n = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(0.5f, pathSpacing)), 1, pathLights);
            float step = length / n;
            float reach = Mathf.Max(pathRange, step * 0.75f + 2.5f);
            int count = 0;
            for (int i = 0; i < path.Length; i++)
            {
                Light l = path[i];
                if (l == null) continue;
                bool on = i < n && lit > 0.01f;
                if (on)
                {
                    l.transform.position = from + dir * (step * (i + 0.5f));
                    l.range = reach;
                    l.intensity = lit;
                    count++;
                }
                if (l.enabled != on) l.enabled = on;
            }
            PathLitCount = count;
            PathIntensityShown = count > 0 ? lit : 0f;
            if (splash != null)
            {
                bool on = splashLit > 0.01f;
                if (on)
                {
                    // Just short of the face it meets, so it lights the wall rather than sitting inside it.
                    splash.transform.position = to - dir * Mathf.Min(0.4f, length * 0.5f);
                    splash.range = splashRange;
                    splash.intensity = splashLit;
                    count++;
                }
                if (splash.enabled != on) splash.enabled = on;
            }
            LitCount = count;
        }

        private void Acquire(Vector3 at)
        {
            for (int i = 0; i < path.Length; i++) path[i] = Pool.Rent(colour, at);
            splash = Pool.Rent(colour, at);
            held = true;
        }

        private void Release()
        {
            if (!held) return;
            for (int i = 0; i < path.Length; i++) { Pool.Return(path[i]); path[i] = null; }
            Pool.Return(splash);
            splash = null;
            held = false;
            LitCount = PathLitCount = 0;
            PathIntensityShown = 0f;
        }

        // ---- the shared pool ----------------------------------------------------------

        public static int PoolCreated => Pool.Created;
        public static int PoolRented => Pool.Rented;
        public static int PoolFree => Pool.FreeCount;
        public static int PoolLit => Pool.LitCount();

        private static class Pool
        {
            private static readonly Stack<Light> free = new();
            private static readonly List<Light> all = new();
            private static Transform root;
            public static int Created, Rented;
            public static int FreeCount => free.Count;

            // Play Mode starts without a domain reload in this project: forget the last session's lights.
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStatics()
            {
                free.Clear();
                all.Clear();
                root = null;
                Created = Rented = 0;
            }

            public static Light Rent(Color colour, Vector3 at)
            {
                Light l = null;
                while (l == null && free.Count > 0) l = free.Pop();
                if (l == null)
                {
                    if (root == null)
                    {
                        var holder = new GameObject("Beam lights (pool)");
                        Object.DontDestroyOnLoad(holder);
                        root = holder.transform;
                    }
                    var go = new GameObject("Beam light");
                    go.transform.SetParent(root, false);
                    l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.shadows = LightShadows.None;
                    l.renderMode = LightRenderMode.Auto;
                    l.enabled = false;
                    all.Add(l);
                    Created++;
                }
                l.color = colour;
                l.intensity = 0f;
                l.enabled = false;
                l.transform.position = at;
                // The world's rendering layers (WorldLightLayers), by where the beam is: a light
                // without them would not reach the site's stamped seafloor, walls and divers.
                WorldLightLayers.StampMover(l.gameObject);
                Rented++;
                return l;
            }

            public static void Return(Light l)
            {
                Rented--;
                if (l == null) return; // destroyed with the session
                l.enabled = false;
                free.Push(l);
            }

            public static int LitCount()
            {
                int n = 0;
                foreach (Light l in all) if (l != null && l.enabled) n++;
                return n;
            }
        }
    }
}
