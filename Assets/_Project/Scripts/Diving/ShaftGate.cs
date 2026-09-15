using UnityEngine;

namespace SunkCost.Diving
{
    // The tube's own doorway at the seafloor (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md
    // section 4.2; Idan/Dan, 15 September 2026 elevator rules: "gated shut at the
    // bottom when the cabin is away; a player cannot walk in"). Two curved leaves
    // like the car's and one collider across the opening. The leaves mirror the
    // car's own doors whenever the car is parked at the bottom or sealing there
    // (Dan, 16 September 2026: "make them close at the same time"): they open
    // with the car's doors on arrival and shut with them before the climb; while
    // the car is anywhere else they are shut. The leaves sit in the tube wall,
    // outside the car's footprint, so a rider inside a passing car never meets
    // the collider.
    public sealed class ShaftGate : MonoBehaviour
    {
        [SerializeField] private ElevatorController controller;
        [SerializeField] private Collider gateCollider;
        [SerializeField] private Transform leafLeftPivot;
        [SerializeField] private Transform leafRightPivot;
        [SerializeField] private float doorwayHalfAngleDeg;
        [SerializeField] private float sweepSeconds = 1.5f;

        private float openFraction;
        private ElevatorDoor carDoor;

        // For the validator: the leaves rest closed (no rotation), and the doorway
        // faces where the gate collider was placed.
        public bool ClosedAtRest => leafLeftPivot != null && leafRightPivot != null &&
            Mathf.Abs(Mathf.DeltaAngle(0f, leafLeftPivot.localEulerAngles.y)) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(0f, leafRightPivot.localEulerAngles.y)) < 0.5f;
        public Vector3 DoorwayDirection => gateCollider != null ? Vector3.ProjectOnPlane(gateCollider.transform.position - transform.position, Vector3.up).normalized : Vector3.forward;
        public float OpenFraction => openFraction;

        private void OnEnable()
        {
            if (controller != null) carDoor = controller.GetComponentInChildren<ElevatorDoor>(true);
            openFraction = TargetOpenFraction;
            Apply();
        }

        // The car door's fraction while the car is at the bottom (arriving, resting,
        // sealing to leave); shut otherwise. No car: open, so a site without one is
        // walkable.
        private float TargetOpenFraction
        {
            get
            {
                if (controller == null) return 1f;
                bool atBottom = controller.State == ElevatorState.AtBottom || (controller.State == ElevatorState.Sealing && controller.Upward);
                if (!atBottom) return 0f;
                return carDoor != null ? carDoor.OpenFraction : 1f;
            }
        }

        private void Update()
        {
            if (carDoor == null && controller != null) carDoor = controller.GetComponentInChildren<ElevatorDoor>(true);
            float target = TargetOpenFraction;
            // Mirroring the door exactly; the sweep only smooths a jump (a late join
            // that finds the car already parked).
            float rate = sweepSeconds <= 0f ? 1f : Time.deltaTime / sweepSeconds;
            openFraction = Mathf.Abs(target - openFraction) > 0.2f ? Mathf.MoveTowards(openFraction, target, rate) : target;
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
