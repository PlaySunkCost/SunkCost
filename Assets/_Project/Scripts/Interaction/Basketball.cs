using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Interaction
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class Basketball : NetworkBehaviour
    {
        private const int Free = 0;
        private const int Held = 1;
        private const int Released = 2;

        [SerializeField] private float throwSpeed = 8f;
        [SerializeField] private float releaseHandoffTimeout = 4f;
        [SerializeField] private Vector3 resetPosition = new(0f, 1f, 0f);
        private readonly SyncVar<int> state = new();
        private readonly SyncVar<int> holderClientId = new(-1);
        private Rigidbody body;
        private HQPlayerController holder;
        private float restTime;
        private float releaseTime;
        // Release impulse from the server, applied once the Released state has also
        // replicated. TargetRpcs reach a remote client before the same tick's
        // SyncVar flush, so neither may assume the other has arrived.
        private bool hasPendingRelease;
        private Vector3 pendingReleaseDirection;
        private bool pendingReleaseThrow;

        public bool IsHeld => state.Value == Held;
        public int HolderClientId => holderClientId.Value;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            state.OnChange += OnStateChanged;
            holderClientId.OnChange += OnHolderChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            RefreshRole();
            ResolveHolder();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late joiners and reconnecting clients get their initial SyncVar values
            // without OnChange callbacks, so derive the local held state here too.
            SyncLocalHeldState();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerManager.OnRemoteConnectionState += ServerOnRemoteConnectionState;
            // A scene object keeps its last transform across sessions; a newly opened
            // room should not inherit where the previous room's last throw ended.
            ServerReset();
        }

        public override void OnStopServer()
        {
            ServerManager.OnRemoteConnectionState -= ServerOnRemoteConnectionState;
            base.OnStopServer();
        }

        // The contract requires disconnect to return a held/released ball to server
        // simulation immediately, without waiting for its rest timeout. FishNet does
        // not clear ownership on disconnect by itself (the prefab only opts out of
        // its default despawn-on-disconnect), so this is the one place that happens.
        private void ServerOnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (state.Value == Free || connection.ClientId != holderClientId.Value) return;
            RemoveOwnership();
        }

        public override void OnOwnershipClient(NetworkConnection previousOwner)
        {
            base.OnOwnershipClient(previousOwner);
            RefreshRole();
        }

        public override void OnOwnershipServer(NetworkConnection previousOwner)
        {
            base.OnOwnershipServer(previousOwner);
            if (IsServerStarted && previousOwner.IsValid && !Owner.IsValid)
            {
                state.Value = Free;
                holderClientId.Value = -1;
            }
            RefreshRole();
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;
            if (state.Value == Held && IsOwner && !hasPendingRelease)
            {
                ResolveHolder();
                if (holder != null && holder.HoldPoint != null)
                {
                    Vector3 target = holder.HoldPoint.position;
                    Vector3 acceleration = (target - body.position) * 80f - body.linearVelocity * 18f;
                    body.AddForce(Vector3.ClampMagnitude(acceleration, 80f), ForceMode.Acceleration);
                }
            }
            else if (state.Value == Released && IsOwner)
            {
                releaseTime += Time.fixedDeltaTime;
                bool resting = body.linearVelocity.sqrMagnitude < 0.0225f && body.angularVelocity.sqrMagnitude < 0.25f;
                restTime = resting ? restTime + Time.fixedDeltaTime : 0f;
                if (restTime >= 0.5f || releaseTime >= releaseHandoffTimeout)
                {
                    restTime = -100f;
                    ServerRequestRest();
                }
            }

            if (IsServerStarted && transform.position.y < -2f)
                ServerReset();
        }

        [Server]
        public bool ServerTryGrab(NetworkConnection connection, HQPlayerController player)
        {
            if (state.Value != Free || connection == null || !connection.IsValid)
                return false;
            holder = player;
            holderClientId.Value = connection.ClientId;
            state.Value = Held;
            GiveOwnership(connection);
            RefreshRole();
            return true;
        }

        [Server]
        public void ServerRelease(NetworkConnection connection, Vector3 direction, bool throwBall)
        {
            if (state.Value != Held || connection == null || connection.ClientId != holderClientId.Value)
                return;
            state.Value = Released;
            Vector3 safeDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            TargetApplyRelease(connection, safeDirection, throwBall);
        }

        [ServerRpc]
        private void ServerRequestRest(NetworkConnection sender = null)
        {
            if (state.Value != Released || sender == null || sender.ClientId != holderClientId.Value)
                return;
            state.Value = Free;
            holderClientId.Value = -1;
            RemoveOwnership();
            RefreshRole();
        }

        [TargetRpc]
        private void TargetApplyRelease(NetworkConnection connection, Vector3 direction, bool throwBall)
        {
            hasPendingRelease = true;
            pendingReleaseDirection = direction;
            pendingReleaseThrow = throwBall;
            TryApplyPendingRelease();
        }

        // Runs from both the TargetRpc and the state OnChange; whichever arrives
        // second applies the impulse, exactly once.
        private void TryApplyPendingRelease()
        {
            if (!hasPendingRelease || state.Value != Released || !IsOwner)
                return;
            hasPendingRelease = false;
            RefreshRole();
            body.useGravity = true;
            if (pendingReleaseThrow)
                body.linearVelocity = pendingReleaseDirection * throwSpeed;
        }

        // The local player's held flag comes from the replicated SyncVars, not from
        // an RPC, so it cannot observe a half-applied grab or release.
        private void SyncLocalHeldState()
        {
            if (!IsClientStarted || ClientManager?.Connection == null)
                return;
            bool localHolds = state.Value == Held && holderClientId.Value == ClientManager.Connection.ClientId;
            foreach (HQPlayerController candidate in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
            {
                if (candidate.IsOwner)
                {
                    candidate.SetHeldBall(localHolds ? this : null);
                    break;
                }
            }
        }

        [Server]
        public void ServerReset()
        {
            state.Value = Free;
            holderClientId.Value = -1;
            if (Owner.IsValid) RemoveOwnership();
            transform.SetPositionAndRotation(resetPosition, Quaternion.identity);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            RefreshRole();
        }

        private void OnStateChanged(int previous, int next, bool asServer)
        {
            restTime = 0f;
            releaseTime = 0f;
            if (next != Released)
                hasPendingRelease = false;
            ResolveHolder();
            RefreshRole();
            SyncLocalHeldState();
            TryApplyPendingRelease();
        }

        private void OnHolderChanged(int previous, int next, bool asServer)
        {
            ResolveHolder();
            SyncLocalHeldState();
        }

        private void ResolveHolder()
        {
            holder = null;
            if (holderClientId.Value < 0) return;
            foreach (HQPlayerController candidate in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
            {
                if (candidate.Owner.ClientId == holderClientId.Value) { holder = candidate; break; }
            }
        }

        private void RefreshRole()
        {
            if (body == null) return;
            bool writer = (state.Value == Free && IsServerStarted && !Owner.IsValid) ||
                          ((state.Value == Held || state.Value == Released) && IsOwner);
            body.isKinematic = !writer;
            body.useGravity = writer && state.Value != Held;
            if (!writer) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
    }
}
