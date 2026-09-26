using UnityEngine;

namespace SunkCost.World
{
    // HQ-owned hinge, driven by the existing server departure clock. This never
    // moves players or cargo and contributes no network state or authority.
    [DefaultExecutionOrder(-90)]
    public sealed class DockGangway : MonoBehaviour
    {
        [SerializeField, Range(60f, 90f)] private float raisedAngle = 85f;
        private Quaternion restRotation;
        public float RaisedFraction { get; private set; }

        private void Awake()
        {
            restRotation = transform.localRotation;
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // Gates protect this moving surface; it carries no travelling bodies.
            Apply(1f);
        }

        private void Update()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) { Apply(1f); return; }
            Apply(Fraction(day.Departure, day.World, day.Travelling,
                ShipDepartureVisual.StageProgress(day.Departure)));
        }

        public static float Fraction(ShipDepartureState state, WorldId world, bool travelling, float progress)
        {
            if (!travelling) return world == WorldId.HQ ? 0f : 1f;
            switch (state.Stage)
            {
                case DepartureStage.Preparing:
                    return state.FromWorld == WorldId.HQ ? 0f : 1f;
                case DepartureStage.RaisingGangway:
                    return state.FromWorld == WorldId.HQ ? Mathf.SmoothStep(0f, 1f, progress) : 1f;
                case DepartureStage.Arriving:
                    return state.ToWorld == WorldId.HQ ? 1f - Mathf.SmoothStep(0f, 1f, progress) : 1f;
                default:
                    return 1f;
            }
        }

        private void Apply(float fraction)
        {
            RaisedFraction = fraction;
            // Authored leaf extends west (-X); negative Z rotation raises its tip.
            Quaternion pose = restRotation * Quaternion.AngleAxis(-raisedAngle * fraction, Vector3.forward);
            if (transform.localRotation != pose) transform.localRotation = pose;
        }
    }
}
