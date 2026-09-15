using System;
using System.Collections.Generic;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Diving
{
    // Local, non-networked harness: this still drives itself directly instead of through the
    // network, running independently on every peer. DiveSite01 now spawns a networked
    // HQPlayerController (via CrewSpawner, docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 3.3),
    // but that only made the RIDER networked — TryStartMove and the state machine below are
    // unchanged and are not yet server-authoritative. The networked version makes this a
    // NetworkBehaviour with state as SyncVars and TryStartMove behind a ServerRpc named
    // ServerRequestElevatorMove per NETWORK_CONTRACT.md §4 and §9, and emits authoritative
    // noise on each transition into motion. None of that is implemented here.
    //
    // State machine: AtTop -> Sealing -> Descending -> AtBottom -> Sealing -> Ascending ->
    // AtTop. Sealing is one state shared by both directions; which way it resolves once the
    // seal completes is tracked separately in pendingMoveState. Opening is not a gate — on
    // arrival the state becomes AtBottom/AtTop immediately, so it gets no state of its own;
    // ElevatorDoor treats arrival as the start of an opening presentation. Progress and
    // stateElapsed are both plain derived values from state + elapsed time, exactly like a
    // networked version would reconstruct them from SyncVars for a late joiner — nothing
    // here depends on an event a joiner could have missed.
    [DefaultExecutionOrder(-100)]
    public sealed class ElevatorController : MonoBehaviour
    {
        [SerializeField] private Vector3 topPosition;
        [SerializeField] private Vector3 bottomPosition;
        [SerializeField] private float doorSealSeconds = 1.5f;
        // The motion profile (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md section 5): 3 m/s,
        // 1 m/s while the floor-to-roof span crosses the water at seaLevelY, 3 m/s
        // again. Set by DiveSiteBuilder from DiveSiteSettings; the travel time is
        // derived, not a setting.
        [SerializeField] private float seaLevelY = -1f;
        [SerializeField] private float spanMeters = 3.5f;
        [SerializeField] private float travelSpeed = 3f;
        [SerializeField] private float crossingSpeed = 1f;

        // Tuned wait at the top before the cabin closes and starts its automatic empty
        // return, gated on at least one living player remaining at the dive site (a
        // disconnected player does not count as living) — docs/DESIGN.md, "Elevator rules",
        // 15 September 2026 (Idan and Dan). Field only: the living-players-below check and
        // the auto-return trigger itself are Dan's (day state / world loop), not implemented
        // here.
        [SerializeField] private float autoReturnDelaySeconds = 3f;

        private ElevatorState state = ElevatorState.AtTop;
        private ElevatorState pendingMoveState; // Descending or Ascending; resolved once Sealing completes
        private float progress; // 0 = at topPosition, 1 = at bottomPosition
        private float stateElapsed; // seconds since entering the current state
        private readonly Dictionary<GameObject, HQPlayerController> riders = new();
        private readonly List<GameObject> staleRiders = new();

        public event Action<ElevatorState> StateChanged;

        public ElevatorState State => state;
        public float Progress => progress;
        public float StateElapsed => stateElapsed;
        public ElevatorMath.Profile Profile => ElevatorMath.Profile.Of(Vector3.Distance(topPosition, bottomPosition), topPosition.y - seaLevelY, spanMeters, travelSpeed, crossingSpeed);
        public float TravelSecondsOneWay => ElevatorMath.TravelSeconds(Profile);
        public float SeaLevelY => seaLevelY;
        public float SpanMeters => spanMeters;
        public float DoorSealSeconds => doorSealSeconds;
        public float AutoReturnDelaySeconds => autoReturnDelaySeconds;
        public Vector3 TopPosition => topPosition;
        public Vector3 BottomPosition => bottomPosition;
        // Driven mode (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 6.1): the state and
        // its elapsed time come from the server's ElevatorPhase every frame; this class only
        // applies the transform, the door/gate events and the local rider push. Once driven,
        // TryStartMove no longer starts anything itself (the car button goes to the server).
        public bool Driven { get; private set; }
        public bool Upward => pendingMoveState == ElevatorState.Ascending;
        // The rider trigger's box, for the server's "who is inside the car" test.
        private Collider carVolume;
        public bool IsInsideCar(Vector3 worldPosition)
        {
            if (carVolume == null) foreach (ElevatorRiderTrigger trigger in GetComponentsInChildren<ElevatorRiderTrigger>(true)) { carVolume = trigger.GetComponent<Collider>(); break; }
            if (carVolume == null) return false;
            // The car is rotated to face its landing: test in the box's own space, not
            // its world-aligned bounds (which grow by 40 % at 45 degrees).
            if (carVolume is BoxCollider box)
            {
                Vector3 local = box.transform.InverseTransformPoint(worldPosition) - box.center;
                Vector3 half = box.size * 0.5f;
                return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            }
            return carVolume.bounds.Contains(worldPosition);
        }

        public void SetDrivenPhase(ElevatorState newState, bool upward, float elapsed)
        {
            Driven = true;
            pendingMoveState = upward ? ElevatorState.Ascending : ElevatorState.Descending;
            if (newState != state) SetState(newState);
            stateElapsed = Mathf.Max(0f, elapsed);
            progress = newState switch
            {
                ElevatorState.AtTop => 0f,
                ElevatorState.AtBottom => 1f,
                ElevatorState.Sealing => upward ? 1f : 0f,
                ElevatorState.Descending => ElevatorMath.ProgressAt(Profile, stateElapsed, false),
                ElevatorState.Ascending => ElevatorMath.ProgressAt(Profile, stateElapsed, true),
                _ => progress
            };
            transform.position = Vector3.Lerp(topPosition, bottomPosition, progress);
        }

        private void Awake()
        {
            transform.position = Vector3.Lerp(topPosition, bottomPosition, progress);
        }

        // The only externally callable state mutator. Validates state and boarding, then
        // writes state via SetState. Update() below also calls SetState, but only to
        // complete a transition this method already started — that is internal bookkeeping
        // for the running move, not a second entry point into the state machine.
        //
        // Requester-aboard is the only validation path today, because the only way to call
        // this is the interior control panel. An external control (a platform lever, a
        // seafloor call button) will drive the same AtTop/AtBottom -> Sealing transitions
        // later through a different validation path (no rider required) — that path does
        // not exist yet and is not built here.
        public bool TryStartMove(GameObject requester)
        {
            if (Driven) return false; // the server owns the state machine; see WorldSceneFlow
            if (requester == null || !riders.ContainsKey(requester))
                return false;

            if (state == ElevatorState.AtTop)
            {
                pendingMoveState = ElevatorState.Descending;
                SetState(ElevatorState.Sealing);
                return true;
            }

            if (state == ElevatorState.AtBottom)
            {
                pendingMoveState = ElevatorState.Ascending;
                SetState(ElevatorState.Sealing);
                return true;
            }

            return false;
        }

        internal void RegisterRider(GameObject rider)
        {
            if (rider == null || riders.ContainsKey(rider))
                return;
            // Resolved once at registration rather than GetComponent<>() every rider every
            // frame in Update(). A rider without the component (shouldn't happen given
            // ElevatorRiderTrigger only registers GameObjects it found one on, but cheap to
            // guard) is still tracked for TryStartMove's boarding check, just never moved.
            riders.Add(rider, rider.GetComponent<HQPlayerController>());
        }

        internal void UnregisterRider(GameObject rider) => riders.Remove(rider);

        private void SetState(ElevatorState newState)
        {
            state = newState;
            stateElapsed = 0f;
            StateChanged?.Invoke(state);
        }

        private void Update()
        {
            if (Driven) return; // SetDrivenPhase already applied this frame's state and transform
            stateElapsed += Time.deltaTime;

            if (state == ElevatorState.Sealing)
            {
                if (stateElapsed >= doorSealSeconds)
                    SetState(pendingMoveState);
                return;
            }

            if (state != ElevatorState.Descending && state != ElevatorState.Ascending)
                return;

            // Time-based, from the same profile the driven mode uses, so both modes put
            // the car in the same place at the same elapsed time.
            Vector3 previousPosition = transform.position;
            progress = ElevatorMath.ProgressAt(Profile, stateElapsed, state == ElevatorState.Ascending);
            transform.position = Vector3.Lerp(topPosition, bottomPosition, progress);

            Vector3 delta = transform.position - previousPosition;
            if (delta != Vector3.zero)
            {
                // HQPlayerController.Move() does not track a moving platform on its own (a
                // CharacterController never does), so the elevator pushes its own world-space
                // delta into each rider before that rider's own input movement runs. This
                // class only calls the rider's public hook; it does not reach into
                // HQPlayerController's internals. Only the local owner's Move() actually
                // applies it (see HQPlayerController.Update()'s IsOwner gate) — a remote
                // rider's copy just accumulates the delta harmlessly until this class becomes
                // server-authoritative.
                foreach (KeyValuePair<GameObject, HQPlayerController> entry in riders)
                {
                    if (entry.Key == null)
                    {
                        staleRiders.Add(entry.Key);
                        continue;
                    }
                    if (entry.Value != null)
                        entry.Value.AddExternalMotion(delta);
                }

                if (staleRiders.Count > 0)
                {
                    foreach (GameObject stale in staleRiders)
                        riders.Remove(stale);
                    staleRiders.Clear();
                }
            }

            if (state == ElevatorState.Descending && stateElapsed >= TravelSecondsOneWay)
                SetState(ElevatorState.AtBottom);
            else if (state == ElevatorState.Ascending && stateElapsed >= TravelSecondsOneWay)
                SetState(ElevatorState.AtTop);
        }
    }
}
