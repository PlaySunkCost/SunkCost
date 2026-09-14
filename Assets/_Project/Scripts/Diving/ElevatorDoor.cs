using UnityEngine;

namespace SunkCost.Diving
{
    // Presentation and blocking only — derives everything from ElevatorController's state,
    // never from the rider set or a "player pressed the panel" signal. An external control
    // (platform lever, seafloor call button) will drive the same controller states later
    // without this component changing at all.
    //
    // Open fraction is computed fresh every frame from controller.State/StateElapsed/
    // DoorSealSeconds rather than accumulated locally from the StateChanged event, so it is
    // derived — exactly like ElevatorController.Progress — and never depends on this
    // component having been alive to observe a past transition.
    public sealed class ElevatorDoor : MonoBehaviour
    {
        [SerializeField] private Transform leafLeftPivot;
        [SerializeField] private Transform leafRightPivot;
        [SerializeField] private Collider doorCollider;
        [SerializeField] private float doorwayHalfAngleDeg;

        private ElevatorController controller;

        private void Awake()
        {
            controller = GetComponentInParent<ElevatorController>();
        }

        private void OnEnable()
        {
            if (controller != null)
                controller.StateChanged += HandleStateChanged;
            Apply(ComputeOpenFraction());
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(ElevatorState _) => Apply(ComputeOpenFraction());

        private void Update() => Apply(ComputeOpenFraction());

        private float ComputeOpenFraction()
        {
            if (controller == null)
                return 1f;

            float sealSeconds = Mathf.Max(controller.DoorSealSeconds, 0.0001f);
            switch (controller.State)
            {
                case ElevatorState.AtTop:
                case ElevatorState.AtBottom:
                    return Mathf.Clamp01(controller.StateElapsed / sealSeconds); // opens on arrival, saturates at 1 while resting
                case ElevatorState.Sealing:
                    return 1f - Mathf.Clamp01(controller.StateElapsed / sealSeconds);
                default: // Descending, Ascending
                    return 0f;
            }
        }

        private void Apply(float openFraction)
        {
            float sweepDeg = doorwayHalfAngleDeg * openFraction;
            // Each leaf's child panels are built at their CLOSED angular span (see
            // DiveSiteBuilder.CreateElevatorDoor); rotating the pivot sweeps that whole rigid
            // set further around the car's circumference, away from the doorway centre, as
            // the door opens. Signs are opposite so the two leaves part symmetrically.
            if (leafRightPivot != null)
                leafRightPivot.localRotation = Quaternion.Euler(0f, -sweepDeg, 0f);
            if (leafLeftPivot != null)
                leafLeftPivot.localRotation = Quaternion.Euler(0f, sweepDeg, 0f);

            // Known limitation (greybox): a CharacterController is never displaced by a moving
            // collider. A player straddling the threshold at the exact instant this flips ends
            // up on whichever side their capsule centre already is — no crush, push-out or
            // blocked-close behaviour is attempted here.
            if (doorCollider != null)
                doorCollider.enabled = openFraction <= 0f;
        }
    }
}
