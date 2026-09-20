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
        private Vector3 bodyRest;
        private MaterialPropertyBlock block;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            creature = GetComponent<Creature>();
            bolts = GetComponent<CreatureBolts>();
            if (body != null) bodyRest = body.localPosition;
            if (eyes == null || eyes.Length == 0)
            {
                var found = new List<Renderer>();
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Eye")) found.Add(r);
                eyes = found.ToArray();
            }
            block = new MaterialPropertyBlock();
        }

        private void OnEnable() { if (bolts != null) bolts.Fired += OnBolt; }
        private void OnDisable() { if (bolts != null) bolts.Fired -= OnBolt; }

        private void OnBolt(BoltCue cue) => MonsterBoltView.Spawn(cue);

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
            if (body != null)
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

    // A bolt as every peer draws it: a short glowing rod flying from the cue's
    // start point through its aim point at BoltSpeed for BoltLifeSeconds. Made
    // at runtime; two shared materials, light and dark.
    public sealed class MonsterBoltView : MonoBehaviour
    {
        private static Material lightMaterial, darkMaterial;
        private Vector3 from, dir;
        private float startedAt, speed, life;

        public static void Spawn(BoltCue cue)
        {
            MonsterSettings settings = MonsterSettings.Get();
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = cue.Dark ? "Dark bolt" : "Light bolt";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(0.12f, 0.12f, 0.7f);
            go.GetComponent<Renderer>().sharedMaterial = cue.Dark ? DarkMaterial() : LightMaterial();
            MonsterBoltView view = go.AddComponent<MonsterBoltView>();
            view.from = cue.From;
            view.dir = (cue.To - cue.From).normalized;
            view.speed = settings.BoltSpeed;
            view.life = settings.BoltLifeSeconds;
            view.startedAt = Time.time;
            go.transform.SetPositionAndRotation(cue.From, Quaternion.LookRotation(view.dir, Vector3.up));
            if (!cue.Dark)
            {
                Light glow = go.AddComponent<Light>();
                glow.type = LightType.Point; glow.range = 6f; glow.intensity = 4f; glow.color = new Color(1f, 0.92f, 0.55f); glow.shadows = LightShadows.None;
            }
        }

        private void Update()
        {
            float t = Time.time - startedAt;
            if (t > life) { Destroy(gameObject); return; }
            transform.position = from + dir * (speed * t);
        }

        private static Material LightMaterial()
        {
            if (lightMaterial == null) lightMaterial = Make(new Color(1f, 0.92f, 0.55f), 6f);
            return lightMaterial;
        }
        private static Material DarkMaterial()
        {
            if (darkMaterial == null) darkMaterial = Make(new Color(0.35f, 0.1f, 0.6f), 2.5f);
            return darkMaterial;
        }
        private static Material Make(Color colour, float intensity)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material m = new(shader != null ? shader : Shader.Find("Standard"));
            m.SetColor("_BaseColor", colour * 0.4f);
            m.color = colour * 0.4f;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", colour * intensity);
            return m;
        }
    }
}
