using UnityEngine;

namespace SunkCost.Diving
{
    // The tube's own doorway at the seafloor (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md
    // section 4.2; Idan/Dan, 15 September 2026 elevator rules: "gated shut at the
    // bottom when the cabin is away; a player cannot walk in"). Two curved leaves
    // like the car's and one collider across the opening: closed while the car is
    // away, open while the car is parked or occupies the bottom of the tube — not
    // only once it has stopped, or riders inside a car passing through would be
    // held on the collider and dropped. Progress is sampled every frame because a
    // driven car changes it without a state change.
    public sealed class ShaftGate : MonoBehaviour
    {
        [SerializeField] private ElevatorController controller;
        [SerializeField] private Collider gateCollider;
        [SerializeField] private Transform leafLeftPivot;
        [SerializeField] private Transform leafRightPivot;
        [SerializeField] private float doorwayHalfAngleDeg;
        [SerializeField] private float sweepSeconds = 1.5f;

        private const float CarAtGateProgress = 0.9f;
        private float openFraction;

        // For the validator: the leaves rest closed (no rotation), and the doorway
        // faces where the gate collider was placed.
        public bool ClosedAtRest => leafLeftPivot != null && leafRightPivot != null &&
            Mathf.Abs(Mathf.DeltaAngle(0f, leafLeftPivot.localEulerAngles.y)) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(0f, leafRightPivot.localEulerAngles.y)) < 0.5f;
        public Vector3 DoorwayDirection => gateCollider != null ? Vector3.ProjectOnPlane(gateCollider.transform.position - transform.position, Vector3.up).normalized : Vector3.forward;
        public float OpenFraction => openFraction;

        private void OnEnable()
        {
            openFraction = ShouldBeOpen ? 1f : 0f;
            Apply();
        }

        private bool ShouldBeOpen => controller == null || controller.State == ElevatorState.AtBottom || controller.Progress >= CarAtGateProgress;

        private void Update()
        {
            float target = ShouldBeOpen ? 1f : 0f;
            float rate = sweepSeconds <= 0f ? 1f : Time.deltaTime / sweepSeconds;
            openFraction = Mathf.MoveTowards(openFraction, target, rate);
            Apply();
        }

        private void Apply()
        {
            float sweepDeg = doorwayHalfAngleDeg * openFraction;
            if (leafRightPivot != null) leafRightPivot.localRotation = Quaternion.Euler(0f, -sweepDeg, 0f);
            if (leafLeftPivot != null) leafLeftPivot.localRotation = Quaternion.Euler(0f, sweepDeg, 0f);
            if (gateCollider != null)
            {
                bool blocks = openFraction <= 0.01f;
                if (gateCollider.enabled != blocks) gateCollider.enabled = blocks;
            }
        }
    }
}
