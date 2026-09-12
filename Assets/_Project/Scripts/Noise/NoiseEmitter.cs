using UnityEngine;

namespace SunkCost.Noise
{
    /// <summary>
    /// Drop this on anything that should make noise without needing its own script:
    /// the elevator, a loose cargo crate, a cutting torch. Call <see cref="Fire"/> from
    /// an animation event, a collision, or another system.
    ///
    /// Server only — the guard lives in NoiseSystem.Emit, so a stray client call is loud
    /// in the console rather than silently desyncing the creatures.
    /// </summary>
    public class NoiseEmitter : MonoBehaviour
    {
        [Header("What this sounds like")]
        [SerializeField] NoiseKind kind = NoiseKind.Impact;

        [Tooltip("How far this sound carries, in metres.")]
        [SerializeField, Min(0f)] float radius = 12f;

        [Header("Impact response")]
        [Tooltip("Fire automatically when this object is hit hard enough. 0 disables.")]
        [SerializeField, Min(0f)] float impactVelocityThreshold = 3f;

        [Tooltip("Shortest gap between automatic emits, in seconds.")]
        [SerializeField, Min(0f)] float cooldown = 0.25f;

        float nextAllowedTime;

        /// <summary>Make the noise now.</summary>
        public void Fire()
        {
            NoiseSystem.Emit(transform.position, radius, kind, gameObject.GetInstanceID());
        }

        /// <summary>Make the noise at a custom volume — a heavy drop is louder than a nudge.</summary>
        public void Fire(float radiusOverride)
        {
            NoiseSystem.Emit(transform.position, radiusOverride, kind, gameObject.GetInstanceID());
        }

        void OnCollisionEnter(Collision collision)
        {
            if (impactVelocityThreshold <= 0f) return;
            if (Time.time < nextAllowedTime) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < impactVelocityThreshold) return;

            nextAllowedTime = Time.time + cooldown;

            // Louder the harder it lands, capped at 2x so nothing gets silly.
            float scale = Mathf.Min(speed / impactVelocityThreshold, 2f);
            NoiseSystem.Emit(transform.position, radius * scale, kind, gameObject.GetInstanceID());
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
