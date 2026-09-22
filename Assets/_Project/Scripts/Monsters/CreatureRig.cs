using UnityEngine;

namespace SunkCost.Monsters
{
    // A modelled monster's rig (docs/MONSTER_MODELS.md; Dan's Blender models, 22
    // September 2026): the Animator on the model under "Model" is driven from the
    // replicated pose and the copy's own speed — an int Pose (CreaturePose), a
    // float Speed (flat metres per second) and a bool Moving — and the model's
    // named anchors are where the beam leaves (BeamOrigin) and the voice sits
    // (Voice). A pose the model has no clip for falls back to CreatureLook's code
    // bob, so a half-finished model still reads. Presentation only: the rules never
    // read this. Wired onto the prefab by MonsterModelSetup.
    public sealed class CreatureRig : MonoBehaviour
    {
        public const string ModelName = "Model", BeamOriginName = "BeamOrigin", VoiceName = "Voice";
        public static readonly int PoseId = Animator.StringToHash("Pose");
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int MovingId = Animator.StringToHash("Moving");
        private const float TeleportMetres = 3f;

        [Tooltip("The model's Animator (found under Model when empty).")]
        [SerializeField] private Animator animator;
        [Tooltip("Where a beam visibly leaves the model (found by name when empty); the server's line still starts at the eye point.")]
        [SerializeField] private Transform beamOrigin;
        [Tooltip("Where the voice sits (found by name when empty).")]
        [SerializeField] private Transform voice;
        [Tooltip("Which poses the model has a clip for, by clip name (filled by the setup); the others use the code bob.")]
        [SerializeField] private string[] animatedPoses = new string[0];

        private Creature creature;
        private Vector3 lastPosition;
        private bool primed;
        private float speed;
        private bool[] animated;

        public Animator Animator => animator != null ? animator : animator = GetComponentInChildren<Animator>(true);
        public bool HasAnimator => Animator != null && Animator.runtimeAnimatorController != null;
        public Transform BeamOrigin => beamOrigin != null ? beamOrigin : beamOrigin = Find(BeamOriginName);
        public Transform VoiceAnchor => voice != null ? voice : voice = Find(VoiceName);
        public float Speed => speed;

        // The model carries a clip for this pose: the code bob stays out of its way.
        public bool Animated(CreaturePose pose)
        {
            if (!HasAnimator) return false;
            if (animated == null)
            {
                animated = new bool[16];
                foreach (string name in animatedPoses)
                    if (System.Enum.TryParse(name, out CreaturePose p) && (int)p < animated.Length) animated[(int)p] = true;
            }
            return (int)pose < animated.Length && animated[(int)pose];
        }

        public Vector3 BeamOriginPoint(Vector3 fallback) => BeamOrigin != null ? BeamOrigin.position : fallback;

        private Transform Find(string name)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private void Awake()
        {
            creature = GetComponent<Creature>();
            Animator a = Animator;
            if (a != null) { a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
        }

        private void LateUpdate()
        {
            Vector3 position = transform.position;
            float dt = Time.deltaTime;
            if (!primed) { lastPosition = position; primed = true; return; }
            Vector3 flat = position - lastPosition; flat.y = 0f;
            lastPosition = position;
            if (dt > 0f)
            {
                float now = flat.magnitude > TeleportMetres ? speed : flat.magnitude / dt;
                speed = Mathf.Lerp(speed, now, 1f - Mathf.Exp(-dt / 0.12f));
            }
            if (!HasAnimator || creature == null) return;
            Animator a = Animator;
            a.SetInteger(PoseId, (int)creature.Pose);
            a.SetFloat(SpeedId, speed);
            a.SetBool(MovingId, speed > 0.15f);
        }
    }
}
