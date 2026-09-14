using System;
using System.Collections.Generic;
using SunkCost.Sites;
using UnityEngine;

namespace SunkCost.Diving
{
    // Local, non-networked harness: DiveSite01 has no networked player yet, so this drives
    // itself directly instead of through the network. The networked version makes this a
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
        [SerializeField] private float travelSecondsOneWay = 15f;
        [SerializeField] private float doorSealSeconds = 1.5f;

        private ElevatorState state = ElevatorState.AtTop;
        private ElevatorState pendingMoveState; // Descending or Ascending; resolved once Sealing completes
        private float progress; // 0 = at topPosition, 1 = at bottomPosition
        private float stateElapsed; // seconds since entering the current state
        private readonly Dictionary<GameObject, DiveSiteDevPlayer> riders = new();
        private readonly List<GameObject> staleRiders = new();

        public event Action<ElevatorState> StateChanged;

        public ElevatorState State => state;
        public float Progress => progress;
        public float StateElapsed => stateElapsed;
        public float TravelSecondsOneWay => travelSecondsOneWay;
        public float DoorSealSeconds => doorSealSeconds;

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
            riders.Add(rider, rider.GetComponent<DiveSiteDevPlayer>());
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
            stateElapsed += Time.deltaTime;

            if (state == ElevatorState.Sealing)
            {
                if (stateElapsed >= doorSealSeconds)
                    SetState(pendingMoveState);
                return;
            }

            if (state != ElevatorState.Descending && state != ElevatorState.Ascending)
                return;

            float distance = Vector3.Distance(topPosition, bottomPosition);
            float speed = travelSecondsOneWay > 0f ? distance / travelSecondsOneWay : distance;
            float progressDelta = distance > 0f ? speed * Time.deltaTime / distance : 1f;

            Vector3 previousPosition = transform.position;
            progress = state == ElevatorState.Descending
                ? Mathf.Clamp01(progress + progressDelta)
                : Mathf.Clamp01(progress - progressDelta);
            transform.position = Vector3.Lerp(topPosition, bottomPosition, progress);

            Vector3 delta = transform.position - previousPosition;
            if (delta != Vector3.zero)
            {
                // DiveSiteDevPlayer.Move() does not track a moving platform on its own (a
                // CharacterController never does), so the elevator pushes its own world-space
                // delta into each rider before that rider's own input movement runs. This
                // class only calls the rider's public hook; it does not reach into
                // DiveSiteDevPlayer's internals.
                foreach (KeyValuePair<GameObject, DiveSiteDevPlayer> entry in riders)
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

            if (state == ElevatorState.Descending && progress >= 1f)
                SetState(ElevatorState.AtBottom);
            else if (state == ElevatorState.Ascending && progress <= 0f)
                SetState(ElevatorState.AtTop);
        }
    }
}
