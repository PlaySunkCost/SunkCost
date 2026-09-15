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

        // The gate sits across the shaft mouth at the landing's ceiling. It must be
        // out of the way while the car itself occupies it, not only once the car has
        // stopped: riders inside a car passing through it would otherwise be held on
        // it and dropped (cabin ride card, 15 September 2026). Progress is sampled
        // every frame because a driven car changes it without a state change.
        private const float CarAtGateProgress = 0.9f;

        private void Update() => Apply();

        private void Apply()
        {
            if (gateCollider == null) return;
            bool open = controller == null || controller.State == ElevatorState.AtBottom || controller.Progress >= CarAtGateProgress;
            if (gateCollider.enabled == open) gateCollider.enabled = !open;
            // The plug is drawn only while it blocks; open, it would sit inside the car.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled == open) renderer.enabled = !open;
        }
    }
}
