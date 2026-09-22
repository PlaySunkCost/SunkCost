using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Monsters
{
    // What every peer sees of a creature beyond its replicated position: the
    // eyes glow by pose (a slow pulse idle, bright on the hunt, steady frozen,
    // dim in flight), the body bobs as it walks and shakes in the Charger's
    // wind-up, and a bolt flies when the cue changes. Placeholder until Dan's
    // models: nothing about the rules reads the look. Presentation only.
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

        private void OnEnable() { if (bolts != null) { bolts.Aimed += OnAim; bolts.Fired += OnBeam; } }
        private void OnDisable() { if (bolts != null) { bolts.Aimed -= OnAim; bolts.Fired -= OnBeam; } }

        private MonsterBeamView beam;
        private void OnAim(BeamCue cue) { if (beam == null) beam = MonsterBeamView.Make(transform); beam.Aim(cue); }
        private void OnBeam(BeamCue cue) { if (beam == null) beam = MonsterBeamView.Make(transform); beam.Fire(cue); }

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

    // A beam as every peer draws it: a faint thin line while the monster aims (the
    // tell), then the line thick and bright for a moment, fading. A LineRenderer
    // on a child of the creature; two materials, light and dark. Presentation only.
    public sealed class MonsterBeamView : MonoBehaviour
    {
        private const float FlashSeconds = 0.25f;
        private static Material lightMaterial, darkMaterial;
        private LineRenderer line;
        private float firedAt = float.NegativeInfinity;
        private bool aiming;
        private Color colour;

        public static MonsterBeamView Make(Transform creature)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(creature, false);
            MonsterBeamView view = go.AddComponent<MonsterBeamView>();
            view.rig = creature.GetComponent<CreatureRig>();
            view.line = go.AddComponent<LineRenderer>();
            view.line.useWorldSpace = true;
            view.line.positionCount = 2;
            view.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.line.receiveShadows = false;
            view.line.enabled = false;
            return view;
        }

        private CreatureRig rig; // a modelled monster: the line visibly leaves its BeamOrigin (the server's line still starts at the eye point)

        private void Lay(BeamCue cue)
        {
            Vector3 dir = (cue.To - cue.From).normalized;
            float range = CreatureBolts.BeamEnd(cue.From, dir, MonsterSettings.Get().BeamRangeMeters);
            line.SetPosition(0, rig != null ? rig.BeamOriginPoint(cue.From) : cue.From);
            line.SetPosition(1, cue.From + dir * range);
            line.sharedMaterial = cue.Dark ? DarkMaterial() : LightMaterial();
            colour = cue.Dark ? new Color(0.45f, 0.15f, 0.75f) : new Color(1f, 0.92f, 0.55f);
        }

        public void Aim(BeamCue cue)
        {
            Lay(cue);
            aiming = true;
            line.startWidth = line.endWidth = 0.03f;
            line.startColor = line.endColor = colour * 0.5f;
            line.enabled = true;
        }

        public void Fire(BeamCue cue)
        {
            Lay(cue);
            aiming = false;
            firedAt = Time.time;
            line.startWidth = line.endWidth = 0.18f;
            line.startColor = line.endColor = colour;
            line.enabled = true;
        }

        private void LateUpdate()
        {
            if (aiming) { float pulse = 0.4f + 0.2f * Mathf.Sin(Time.time * 30f); line.startColor = line.endColor = colour * pulse; return; }
            float t = Time.time - firedAt;
            if (t > FlashSeconds) { line.enabled = false; return; }
            float fade = 1f - t / FlashSeconds;
            line.startWidth = line.endWidth = 0.18f * fade;
            line.startColor = line.endColor = colour * fade;
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
