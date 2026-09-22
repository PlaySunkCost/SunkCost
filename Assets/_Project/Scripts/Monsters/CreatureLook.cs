using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Monsters
{
    // What every peer sees of a creature beyond its replicated position: the
    // eyes glow by pose (a slow pulse idle, bright on the hunt, steady frozen,
    // dim in flight), the body bobs as it walks and shakes in the Charger's
    // wind-up, and a beam charges, burns and dies when the cue changes.
    // Placeholder until Dan's models: nothing about the rules reads the look.
    // Presentation only.
    public sealed class CreatureLook : MonoBehaviour
    {
        [Tooltip("The part that bobs and shakes (the whole silhouette).")]
        [SerializeField] private Transform body;
        [Tooltip("The glowing parts.")]
        [SerializeField] private Renderer[] eyes;
        [SerializeField] private Color eyeColour = new(1f, 0.85f, 0.25f);

        private Creature creature;
        private CreatureBolts bolts;
        private CreatureRig rig; // a modelled monster: its clips take the poses they cover, the bob the rest
        private Vector3 bodyRest;
        private MaterialPropertyBlock block;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            creature = GetComponent<Creature>();
            bolts = GetComponent<CreatureBolts>();
            rig = GetComponent<CreatureRig>();
            if (body != null) bodyRest = body.localPosition;
            if (eyes == null || eyes.Length == 0)
            {
                var found = new List<Renderer>();
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Eye")) found.Add(r);
                eyes = found.ToArray();
            }
            block = new MaterialPropertyBlock();
        }

        private void OnEnable() { if (bolts != null) { bolts.Aimed += OnCharge; bolts.Fired += OnBeam; bolts.Ended += OnBeamEnd; } }
        private void OnDisable() { if (bolts != null) { bolts.Aimed -= OnCharge; bolts.Fired -= OnBeam; bolts.Ended -= OnBeamEnd; } }

        private MonsterBeamView beam;
        private MonsterBeamView Beam => beam != null ? beam : beam = MonsterBeamView.Make(transform, bolts);
        private void OnCharge(BeamCue cue) => Beam.Charge(cue);
        private void OnBeam(BeamCue cue) => Beam.Fire(cue);
        private void OnBeamEnd(BeamCue cue) => Beam.End();

        private void LateUpdate()
        {
            if (creature == null) return;
            CreaturePose pose = creature.Pose;
            float t = Time.time;
            float glow = pose switch
            {
                CreaturePose.Idle => 0.6f + 0.4f * Mathf.Sin(t * 1.2f),
                CreaturePose.Drawn => 1.6f,
                CreaturePose.Hunting => 3f,
                CreaturePose.Frozen => 2f,
                CreaturePose.Windup => 3f + 1.5f * Mathf.Sin(t * 30f),
                CreaturePose.Rushing => 4f,
                CreaturePose.Shooting => 5f,
                CreaturePose.Fleeing => 0.4f,
                _ => 1f
            };
            if (eyes != null)
            {
                block.SetColor(EmissionId, eyeColour * Mathf.Max(0f, glow));
                foreach (Renderer r in eyes) if (r != null) r.SetPropertyBlock(block);
            }
            if (body != null && !(rig != null && rig.Animated(pose)))
            {
                Vector3 offset = Vector3.zero;
                switch (pose)
                {
                    case CreaturePose.Drawn: offset.y = 0.03f * Mathf.Sin(t * 6f); break;
                    case CreaturePose.Hunting: case CreaturePose.Fleeing: offset.y = 0.05f * Mathf.Abs(Mathf.Sin(t * 9f)); break;
                    case CreaturePose.Rushing: offset.y = 0.04f * Mathf.Abs(Mathf.Sin(t * 16f)); break;
                    case CreaturePose.Windup: offset = new Vector3(0.06f * Mathf.Sin(t * 45f), 0f, 0.04f * Mathf.Sin(t * 37f)); break;
                }
                body.localPosition = bodyRest + offset;
            }
        }
    }

    // A beam as every peer draws it (Dan, 22 September 2026: a second of loading,
    // then three seconds of beam): while it charges, a glow swells at the mouth and
    // a faint thread points where it aims; while it burns, a thick bright line from
    // the mouth to the replicated aim point, easing after it as it sweeps, flaring
    // when the hit lands; then it fades. A LineRenderer and a glow sphere on a child
    // of the creature; two materials, light and dark. Presentation only.
    public sealed class MonsterBeamView : MonoBehaviour
    {
        private const float FadeSeconds = 0.3f, AimEaseSeconds = 0.06f;
        private static Material lightMaterial, darkMaterial;
        private LineRenderer line;
        private Transform glow;
        private Renderer glowRenderer;
        private CreatureBolts bolts;
        private CreatureRig rig; // a modelled monster: the line visibly leaves its BeamOrigin (the server's line still starts at the eye point)
        private Color colour;
        private BeamPhase phase = BeamPhase.None;
        private float phaseAt = float.NegativeInfinity, hitAt = float.NegativeInfinity;
        private Vector3 shownAim;
        private bool aimPrimed;

        public BeamPhase Phase => phase;

        public static MonsterBeamView Make(Transform creature, CreatureBolts bolts)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(creature, false);
            MonsterBeamView view = go.AddComponent<MonsterBeamView>();
            view.bolts = bolts;
            view.rig = creature.GetComponent<CreatureRig>();
            view.line = go.AddComponent<LineRenderer>();
            view.line.useWorldSpace = true;
            view.line.positionCount = 2;
            view.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.line.receiveShadows = false;
            view.line.enabled = false;
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Charge glow";
            Object.Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            view.glow = ball.transform;
            view.glowRenderer = ball.GetComponent<Renderer>();
            view.glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.glowRenderer.receiveShadows = false;
            ball.SetActive(false);
            if (bolts != null) bolts.Hit += view.OnHit;
            return view;
        }

        private void OnDestroy() { if (bolts != null) bolts.Hit -= OnHit; }
        private void OnHit() => hitAt = Time.time;

        private Vector3 Origin
        {
            get
            {
                Vector3 fallback = bolts != null && bolts.Cue.Serial > 0 ? bolts.Cue.From : transform.position;
                return rig != null ? rig.BeamOriginPoint(fallback) : fallback;
            }
        }

        private void Dress(bool dark)
        {
            Material m = dark ? DarkMaterial() : LightMaterial();
            line.sharedMaterial = m;
            glowRenderer.sharedMaterial = m;
            colour = dark ? new Color(0.45f, 0.15f, 0.75f) : new Color(1f, 0.92f, 0.55f);
        }

        public void Charge(BeamCue cue)
        {
            Dress(cue.Dark);
            phase = BeamPhase.Charging;
            phaseAt = Time.time;
            shownAim = cue.To;
            aimPrimed = true;
            line.enabled = true;
            glow.gameObject.SetActive(true);
        }

        public void Fire(BeamCue cue)
        {
            Dress(cue.Dark);
            phase = BeamPhase.Firing;
            phaseAt = Time.time;
            if (!aimPrimed) { shownAim = cue.To; aimPrimed = true; }
            line.enabled = true;
            glow.gameObject.SetActive(true);
        }

        public void End()
        {
            phase = BeamPhase.Done;
            phaseAt = Time.time;
        }

        private void LateUpdate()
        {
            if (phase == BeamPhase.None) return;
            float since = Time.time - phaseAt;
            Vector3 from = Origin;
            // The aim eases after the replicated point so the sweep reads as a turn, not steps.
            Vector3 target = bolts != null ? bolts.BeamAim : shownAim;
            shownAim = aimPrimed ? Vector3.Lerp(shownAim, target, 1f - Mathf.Exp(-Time.deltaTime / AimEaseSeconds)) : target;
            line.SetPosition(0, from);
            line.SetPosition(1, shownAim);
            glow.position = from;
            switch (phase)
            {
                case BeamPhase.Charging:
                {
                    float charge = MonsterSettings.Get().BeamChargeSeconds;
                    float t = charge <= 0f ? 1f : Mathf.Clamp01(since / charge);
                    float pulse = 0.35f + 0.25f * Mathf.Sin(Time.time * 40f);
                    line.startWidth = line.endWidth = 0.015f + 0.02f * t;
                    line.startColor = line.endColor = colour * pulse * (0.4f + 0.6f * t);
                    glow.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.42f, t * t);
                    break;
                }
                case BeamPhase.Firing:
                {
                    float flare = Time.time - hitAt < 0.25f ? 1f + 1.5f * (1f - (Time.time - hitAt) / 0.25f) : 1f;
                    float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 60f);
                    line.startWidth = 0.22f * flare; line.endWidth = 0.14f * flare;
                    line.startColor = line.endColor = colour * pulse * flare;
                    glow.localScale = Vector3.one * (0.5f * flare);
                    break;
                }
                case BeamPhase.Done:
                {
                    float fade = 1f - Mathf.Clamp01(since / FadeSeconds);
                    if (fade <= 0f) { line.enabled = false; glow.gameObject.SetActive(false); phase = BeamPhase.None; aimPrimed = false; return; }
                    line.startWidth = line.endWidth = 0.22f * fade;
                    line.startColor = line.endColor = colour * fade;
                    glow.localScale = Vector3.one * (0.5f * fade);
                    break;
                }
            }
        }

        private static Material LightMaterial()
        {
            if (lightMaterial == null) lightMaterial = Make(new Color(1f, 0.92f, 0.55f), 6f);
            return lightMaterial;
        }
        private static Material DarkMaterial()
        {
            if (darkMaterial == null) darkMaterial = Make(new Color(0.45f, 0.15f, 0.75f), 3f);
            return darkMaterial;
        }
        private static Material Make(Color colour, float intensity)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material m = new(shader != null ? shader : Shader.Find("Sprites/Default"));
            m.SetColor("_BaseColor", colour * intensity);
            m.color = colour;
            return m;
        }
    }
}
