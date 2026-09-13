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

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerManager.OnRemoteConnectionState += ServerOnRemoteConnectionState;
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
            if (state.Value == Held && IsOwner)
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
            TargetConfirmHeld(connection, true);
            RefreshRole();
            return true;
        }

        [Server]
        public void ServerRelease(NetworkConnection connection, Vector3 direction, bool throwBall)
        {
            if (state.Value != Held || connection == null || connection.ClientId != holderClientId.Value)
                return;
            state.Value = Released;
            TargetConfirmHeld(connection, false);
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
        private void TargetConfirmHeld(NetworkConnection connection, bool value)
        {
            ResolveHolder();
            if (holder != null)
                holder.SetHeldBall(value ? this : null);
        }

        [TargetRpc]
        private void TargetApplyRelease(NetworkConnection connection, Vector3 direction, bool throwBall)
        {
            RefreshRole();
            body.useGravity = true;
            if (throwBall)
                body.linearVelocity = direction * throwSpeed;
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
            ResolveHolder();
            RefreshRole();
        }

        private void OnHolderChanged(int previous, int next, bool asServer)
        {
            ResolveHolder();
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
