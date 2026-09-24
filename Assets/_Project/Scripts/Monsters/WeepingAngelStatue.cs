using UnityEngine;

namespace SunkCost.Monsters
{
    // The Weeping Angel's statue on every peer (presentation only; the rules never read
    // it). Its Frozen clip is a strip of held poses, one per WeepingAngel.Statue, each
    // constant for a long stretch (tools/blender/clips/WeepingAngel.py). While the
    // replicated pose is Frozen this holds the Animator in the middle of the statue
    // the server chose: a quick blend into it as it freezes, then a seek back to the
    // middle whenever the clip drifts toward the next pose, so nothing moves under a
    // look however long the look lasts. Runs after CreatureRig (which starts the
    // Frozen state) and before CreatureLook.
    [DefaultExecutionOrder(-30)]
    [RequireComponent(typeof(WeepingAngel))]
    public sealed class WeepingAngelStatue : MonoBehaviour
    {
        [Tooltip("The Animator state holding the strip (the Frozen pose's).")]
        [SerializeField] private string frozenState = "Frozen";
        [Tooltip("How many statues the strip holds, in WeepingAngel.Statue's order.")]
        [SerializeField, Min(1)] private int statueCount = WeepingAngel.StatueCount;
        [Tooltip("The blend into the statue as it freezes, seconds: short, it freezes as the look lands.")]
        [SerializeField, Min(0f)] private float blendSeconds = 0.08f;
        [Tooltip("Seek back to the middle once the clip is this far (a fraction of one statue) from it.")]
        [SerializeField, Range(0.05f, 0.45f)] private float driftAllowed = 0.3f;

        private WeepingAngel angel;
        private CreatureRig rig;
        private int frozenHash;
        private int shownStatue = -1;

        // For the checks: the statue this peer shows, or -1 when not frozen.
        public int ShownStatue => shownStatue;
        public int Seeks { get; private set; }

        private void Awake()
        {
            angel = GetComponent<WeepingAngel>();
            rig = GetComponent<CreatureRig>();
            frozenHash = Animator.StringToHash(frozenState);
        }

        private void LateUpdate()
        {
            if (angel == null || rig == null || !rig.HasAnimator) return;
            if (angel.Pose != CreaturePose.Frozen) { shownStatue = -1; return; }
            Animator a = rig.Animator;
            if (!a.isActiveAndEnabled || !a.HasState(0, frozenHash)) return;
            int want = Mathf.Clamp((int)angel.FrozenStatue, 0, statueCount - 1);
            float middle = (want + 0.5f) / statueCount;

            bool inTransition = a.IsInTransition(0);
            AnimatorStateInfo current = a.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = a.GetNextAnimatorStateInfo(0);
            bool toFrozen = inTransition && next.shortNameHash == frozenHash;
            bool atFrozen = !inTransition && current.shortNameHash == frozenHash;

            if (want != shownStatue || (!toFrozen && !atFrozen))
            {
                // Freezing now (or the server chose again): blend from wherever it is into the statue.
                a.CrossFade(frozenHash, blendSeconds, 0, middle);
                shownStatue = want;
                Seeks++;
                return;
            }
            float at = Mathf.Repeat(toFrozen ? next.normalizedTime : current.normalizedTime, 1f);
            if (Mathf.Abs(at - middle) * statueCount <= driftAllowed) return;
            // Drifting toward the neighbouring statue: back to the middle. The strip is
            // constant across a statue, so the seek is invisible.
            if (toFrozen) a.CrossFade(frozenHash, blendSeconds, 0, middle);
            else a.Play(frozenHash, 0, middle);
            Seeks++;
        }
    }
}
