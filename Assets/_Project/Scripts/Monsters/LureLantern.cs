using UnityEngine;

namespace SunkCost.Monsters
{
    // The Lure's lantern as real light (mon-lure, 24 September 2026). The beam itself
    // is drawn by MonsterBeamView (the LIGHT laser: a white-gold core in a warm halo);
    // this is what makes it a beam of light rather than a line: the lantern lights the
    // seabed round it — a slow lure's pulse idle, warmer when it has seen a lamp,
    // swelling and flickering through the charge (the tell), blazing while it burns,
    // guttering when spent. It is the charge's glow at the mouth: the Lure itself and
    // the seabed round it come up with it. The beam's own light (down its path, and
    // the splash where it lands) is MonsterBeamLight's. Every peer, from the
    // replicated pose and beam. Presentation only; the senses never read a light.
    public sealed class LureLantern : MonoBehaviour
    {
        [SerializeField] private Color colour = new(1f, 0.86f, 0.52f);
        [Tooltip("The lantern light's reach, metres.")]
        [SerializeField] private float range = 6f;
        [Tooltip("The lantern's intensity idle; the states scale it.")]
        [SerializeField] private float baseIntensity = 0.9f;
        [SerializeField] private float drawnScale = 1.4f;
        [SerializeField] private float chargePeakScale = 6f;
        [SerializeField] private float firingScale = 7f;
        [SerializeField] private float spentScale = 0.35f;

        private Creature creature;
        private CreatureBolts bolts;
        private CreatureRig rig;
        private MonsterBeamView view;
        private Light lantern;
        private float shown, seed;

        public float LanternIntensity => lantern != null && lantern.enabled ? lantern.intensity : 0f;

        private void Awake()
        {
            creature = GetComponent<Creature>();
            bolts = GetComponent<CreatureBolts>();
            rig = GetComponent<CreatureRig>();
            seed = Random.value * 10f;
            lantern = MakeLight("Lantern light", range);
        }

        private Light MakeLight(string name, float reach)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.range = reach;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            return l;
        }

        private void LateUpdate()
        {
            if (creature == null || lantern == null) return;
            float now = Time.time, dt = Time.deltaTime;
            if (view == null) view = GetComponentInChildren<MonsterBeamView>();
            Vector3 at = rig != null && rig.BeamOrigin != null ? rig.BeamOrigin.position : creature.EyePoint;
            lantern.transform.position = at;

            BeamPhase phase = view != null ? view.Phase : BeamPhase.None;
            float want;
            switch (phase)
            {
                case BeamPhase.Charging:
                {
                    // Swelling to the burn, and in the last tenth a hard flicker: the tell.
                    float charge = MonsterSettings.Get().BeamChargeSeconds;
                    float t = Mathf.Clamp01(charge <= 0f ? 1f : ChargeProgress(charge));
                    float flicker = t > 0.88f ? 0.55f + 0.45f * Mathf.Sin(now * 70f) : 0.9f + 0.1f * Mathf.Sin(now * 23f + seed);
                    want = Mathf.Lerp(drawnScale, chargePeakScale, t * t) * flicker;
                    break;
                }
                case BeamPhase.Firing:
                    want = firingScale * (0.9f + 0.1f * Mathf.Sin(now * 60f));
                    break;
                default:
                    want = creature.Pose switch
                    {
                        CreaturePose.Recovering => spentScale,
                        CreaturePose.Hunting or CreaturePose.Aiming or CreaturePose.Shooting => drawnScale * 1.15f,
                        CreaturePose.Drawn => drawnScale,
                        // A lure's slow pulse, uneven, like a lamp in the current.
                        _ => 0.75f + 0.25f * Mathf.PerlinNoise(now * 0.6f + seed, 0.3f)
                    };
                    break;
            }
            // Up fast, down slower (the charge snaps on; the spent lamp gutters out).
            float k = 1f - Mathf.Exp(-dt / (want > shown ? 0.05f : 0.25f));
            shown = Mathf.Lerp(shown, want, k);
            lantern.intensity = baseIntensity * shown;
            lantern.enabled = lantern.intensity > 0.01f;
        }

        private float chargeStartedAt = float.NegativeInfinity;
        private int chargeSerial;

        // How far into the charge, from when this peer saw it begin.
        private float ChargeProgress(float charge)
        {
            int serial = bolts != null ? bolts.Cue.Serial : 0;
            if (serial != chargeSerial) { chargeSerial = serial; chargeStartedAt = Time.time; }
            return (Time.time - chargeStartedAt) / charge;
        }
    }
}
