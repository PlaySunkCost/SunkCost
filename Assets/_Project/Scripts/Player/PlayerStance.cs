using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Interaction;
using UnityEngine;

namespace SunkCost.Player
{
    // Crouching over the network without a movement rewrite
    // (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md section 5). The owner
    // predicts shrinking on Ctrl; standing waits for the server, which checks
    // headroom at the position it sees, and the owner re-checks its own headroom
    // before expanding. The accepted posture is a server-written SyncVar every
    // peer applies to its copy of the capsule and the body; the serial advances
    // on refusals too, so a request never hangs.
    public struct StanceState
    {
        public bool Crouched;
        public int Serial;
    }

    public sealed class PlayerStance : NetworkBehaviour
    {
        private readonly SyncVar<StanceState> accepted = new(new StanceState { Crouched = false, Serial = 0 });
        private static readonly Collider[] Overlaps = new Collider[16];

        private HQPlayerController controller;
        private bool desiredCrouch;   // owner: what Ctrl says
        private bool crouched;        // this peer's applied posture
        private int nextSerial;
        private int pendingSerial;
        private float lastStandRequest = float.NegativeInfinity;

        public bool IsCrouched => crouched;
        public bool DesiredCrouch => desiredCrouch;
        public StanceState Accepted => accepted.Value;
        public bool StandPending => pendingSerial != 0 && pendingSerial > accepted.Value.Serial;

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            accepted.OnChange += OnAcceptedChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late join / spawn: the persistent posture, not a default.
            if (!IsOwner) ApplyLocal(accepted.Value.Crouched);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyLocal(accepted.Value.Crouched);
        }

        // ---- owner ----------------------------------------------------------------

        public void SetDesiredCrouch(bool value)
        {
            if (!IsOwner) return;
            desiredCrouch = value;
        }

        private void Update()
        {
            if (!IsOwner || controller == null) return;
            PlayerMovementSettings settings = controller.Movement;
            if (desiredCrouch && !crouched)
            {
                // Midair Ctrl is latched, applied on landing: no capsule change in flight.
                if (!controller.IsGrounded || controller.TravelLocked) return;
                ApplyLocal(true);
                Request(true);
            }
            else if (!desiredCrouch && crouched)
            {
                if (controller.TravelLocked) return;
                if (StandPending || Time.unscaledTime - lastStandRequest < settings.StandRetrySeconds) return;
                if (!HasHeadroom(transform.position)) return; // stay crouched under the shelf; retried when clear
                lastStandRequest = Time.unscaledTime;
                Request(false);
            }
        }

        private void Request(bool crouch)
        {
            pendingSerial = ++nextSerial;
            ServerRequestStance(pendingSerial, crouch);
        }

        private void OnAcceptedChanged(StanceState previous, StanceState next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer
            if (IsOwner)
            {
                if (next.Serial < pendingSerial) return; // stale
                pendingSerial = 0;
                if (next.Crouched && !crouched) ApplyLocal(true);
                else if (!next.Crouched && crouched)
                {
                    // Accepted from the server's view; never expand into a ceiling here.
                    if (HasHeadroom(transform.position)) ApplyLocal(false);
                    else Request(true); // stay down and tell the server so
                }
                return;
            }
            ApplyLocal(next.Crouched);
        }

        private void ApplyLocal(bool value)
        {
            crouched = value;
            controller?.ApplyStance(value);
        }

        // The additional headroom above the crouched capsule, at a feet position.
        // Fails closed when the buffer fills; ignores this player, triggers and
        // loose cargo (the collision policy: balls are walked through, so they
        // cannot pin you down either).
        public bool HasHeadroom(Vector3 feet)
        {
            PlayerMovementSettings settings = controller != null ? controller.Movement : PlayerMovementSettings.Resolve(null);
            PlayerMovementMath.HeadroomCapsule(settings, out Vector3 bottom, out Vector3 top, out float radius);
            int count = Physics.OverlapCapsuleNonAlloc(feet + bottom, feet + top, radius, Overlaps, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        // ---- server ---------------------------------------------------------------

        [ServerRpc]
        private void ServerRequestStance(int serial, bool crouch, NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            if (serial <= accepted.Value.Serial) return; // stale or replayed
            bool travelling = SunkCost.World.CrewDayState.Instance != null && SunkCost.World.CrewDayState.Instance.Travelling;
            bool result;
            if (travelling) result = accepted.Value.Crouched;      // no posture changes on a moving deck
            else if (crouch) result = true;                        // crouching is always allowed
            else result = HasHeadroom(transform.position) ? false : accepted.Value.Crouched; // standing needs room
            // Advance the serial even on refusal so the owner's pending request resolves.
            accepted.Value = new StanceState { Crouched = result, Serial = serial };
        }
    }
}
