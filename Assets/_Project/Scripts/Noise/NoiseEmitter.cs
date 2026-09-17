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

        // NoiseEvent.SourceId is a FishNet ObjectId - stable across machines - not a local
        // Unity handle. A bare NoiseEmitter sits on plain objects (a crate, a torch) with no
        // NetworkObject, so it reports 0: "the world". This previously passed
        // gameObject.GetInstanceID(), which is per-machine and would have compared wrong the
        // moment a SourceId crossed the wire. Unity 6.6 turning that into a compile error is
        // the only reason we caught it.
        // TODO: when FishNet is wired in, take an optional NetworkObject and report its
        // ObjectId here. Do NOT use GetEntityId() - EntityId is a 64-bit local handle with
        // no lossless int conversion (see Unity's InstanceID-to-EntityId migration guide).

        /// <summary>Make the noise now.</summary>
        public void Fire()
        {
            NoiseSystem.Emit(transform.position, radius, kind);
        }

        /// <summary>Make the noise at a custom volume — a heavy drop is louder than a nudge.</summary>
        public void Fire(float radiusOverride)
        {
            NoiseSystem.Emit(transform.position, radiusOverride, kind);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!NoiseSystem.IsServer) return; // a client's physics is a copy; the server's collision is the noise
            if (impactVelocityThreshold <= 0f) return;
            if (Time.time < nextAllowedTime) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < impactVelocityThreshold) return;

            nextAllowedTime = Time.time + cooldown;

            // Louder the harder it lands, capped at 2x so nothing gets silly.
            float scale = Mathf.Min(speed / impactVelocityThreshold, 2f);
            NoiseSystem.Emit(transform.position, radius * scale, kind);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
