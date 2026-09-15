using UnityEngine;

namespace SunkCost.Diving
{
    // A physical blocker across the shaft opening at the bottom landing. Solid whenever the
    // cabin is anywhere but docked at the bottom, so nobody can walk into an empty shaft —
    // docs/DESIGN.md, "Elevator rules" (15 September 2026, Idan and Dan): "gated shut at the
    // bottom when the cabin is away; a player cannot walk in." Open only in
    // ElevatorState.AtBottom, matching "the cabin parks open and waits" there.
    //
    // A separate component from ElevatorDoor rather than reusing it: this sits at the
    // stationary bottom landing, not on the moving car, so it needs its own
    // ElevatorController reference (wired externally, not GetComponentInParent<>()) and its
    // presentation is binary (blocked/clear), not a door sweep.
    public sealed class ShaftGate : MonoBehaviour
    {
        [SerializeField] private ElevatorController controller;
        [SerializeField] private Collider gateCollider;

        private void OnEnable()
        {
            if (controller != null)
                controller.StateChanged += HandleStateChanged;
            Apply();
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(ElevatorState _) => Apply();

        private void Apply()
        {
            if (gateCollider != null)
                gateCollider.enabled = controller == null || controller.State != ElevatorState.AtBottom;
        }
    }
}
