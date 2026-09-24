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
}
