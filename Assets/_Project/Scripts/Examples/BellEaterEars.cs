using UnityEngine;
using SunkCost.Noise;

namespace SunkCost.Examples
{
    /// <summary>
    /// A worked example, not finished code — delete it once the real creature exists.
    ///
    /// It shows the intended shape of a creature's hearing: subscribe on enable, do the
    /// cheap checks first, decide for yourself what counts as interesting. The Bell Eater
    /// is the right first creature to build because it is drawn to the elevator, which
    /// means it tests the core loop directly rather than testing combat.
    /// </summary>
    public class BellEaterEars : MonoBehaviour, INoiseListener
    {
        [Header("Hearing")]
        [Tooltip("Extra reach on top of the noise's own radius. The Bell Eater has very good ears.")]
        [SerializeField, Min(0f)] float hearingBonus = 40f;

        [Tooltip("Noises quieter than this are ignored entirely.")]
        [SerializeField, Min(0f)] float ignoreBelowRadius = 6f;

        [Header("Interest")]
        [Tooltip("How long it keeps investigating after the last thing it heard.")]
        [SerializeField, Min(0f)] float interestDuration = 25f;

        Vector3 investigating;
        float loseInterestAt;

        public bool IsInvestigating => Time.time < loseInterestAt;
        public Vector3 InvestigationTarget => investigating;

        void OnEnable() => NoiseSystem.Register(this);
        void OnDisable() => NoiseSystem.Unregister(this);

        public void OnNoise(in NoiseEvent noise)
        {
            // Cheapest rejections first — this runs for every listener on every noise.
            if (noise.Radius < ignoreBelowRadius) return;

            // This creature only cares about the extraction winch. Footsteps are beneath it.
            if (noise.Kind != NoiseKind.Elevator) return;

            float audible = noise.Radius + hearingBonus;
            if ((noise.Position - transform.position).sqrMagnitude > audible * audible) return;

            investigating = noise.Position;
            loseInterestAt = Time.time + interestDuration;
        }
    }
}
