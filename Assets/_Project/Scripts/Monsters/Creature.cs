using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Noise;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Monsters
{
    // The creature skeleton every walker is built on (docs/DESIGN.md §6 "The
    // monsters", 20 September 2026; docs/NETWORK_CONTRACT.md §3: monster AI and
    // targeting are server only, clients receive positions and a pose). The
    // server runs the brain (ServerThink), moves the body with a
    // CharacterController on the world's colliders only — the Monster layer
    // touches neither players nor cargo, a touch is a distance — hears through
    // the noise bus and sees through CreatureSenses. The NetworkTransform
    // carries the position; one byte of pose and the target's id carry the
    // rest; CreatureLook and CreatureSounds present them on every peer. A copy
    // on a client has its controller off: the transform is written for it.
    //
    // The deck is always safe: a creature never comes within SafeZoneMeters of the
    // shaft, never strikes a diver in the car or on the tube's floor, and every
    // death goes through WorldSceneFlow.ServerKill, which refuses anyone not below.
    [RequireComponent(typeof(CharacterController))]
    public abstract class Creature : NetworkBehaviour, INoiseListener
    {
        // Every spawned creature on this peer (server and client), for the roster and the checks.
        public static readonly List<Creature> All = new();

        [SerializeField] private MonsterKind kind;
        [Tooltip("Where it looks from and where it is looked at: metres over its feet.")]
        [SerializeField] private float eyeHeight = 1.5f;

        private readonly SyncVar<byte> pose = new(0);
        private readonly SyncVar<int> targetId = new(-1);
        private readonly SyncVar<int> strikeSerial = new(0);

        protected CharacterController mover;
        private float verticalSpeed;
        private float strikeReadyAt;
        private Vector3 wantedMove;
        private bool wantedToMove;
        private Vector3 stuckFrom;
        private float stuckSince = -1f;
        private Vector3 sidestep;
        private float sidestepUntil = -1f;
        private bool awake;
        private float wakeAt = -1f;

        public MonsterKind Kind => kind;
        public CreaturePose Pose => (CreaturePose)pose.Value;
        public int TargetId => targetId.Value;
        public int StrikeSerial => strikeSerial.Value;
        public Vector3 EyePoint => transform.position + Vector3.up * eyeHeight;
        public float EyeHeight => eyeHeight;
        public event Action<CreaturePose> PoseChanged;  // every peer, from the SyncVar
        public event Action Struck;                     // every peer, from the SyncVar
        protected static MonsterSettings Settings => MonsterSettings.Get();
        protected static float Now => Time.time;
        // The server's view for the checks.
        public bool ServerAwake => awake;
        public string ServerStatus => $"{kind} pose={Pose} target={TargetId} at={transform.position:F1}{ServerBrainStatus()}";
        protected virtual string ServerBrainStatus() => string.Empty;

        protected virtual void Awake()
        {
            mover = GetComponent<CharacterController>();
            pose.OnChange += (_, next, _) => PoseChanged?.Invoke((CreaturePose)next);
            strikeSerial.OnChange += (_, next, _) => { if (next != 0) Struck?.Invoke(); };
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (!All.Contains(this)) All.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            All.Remove(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NoiseSystem.Register(this);
            wakeAt = Now + Settings.WakeDelaySeconds;
            if (mover != null) mover.enabled = true;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            NoiseSystem.Unregister(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // A copy: the NetworkTransform writes its position; the capsule must not fight it.
            if (!IsServerStarted && mover != null) mover.enabled = false;
        }

        private void Update()
        {
            if (!IsServerStarted || !IsSpawned) return;
            ServerTick(Time.deltaTime);
        }

        [Server]
        private void ServerTick(float dt)
        {
            if (dt <= 0f || mover == null || !mover.enabled) return;
            if (!awake) { if (Now < wakeAt) { Fall(dt); return; } awake = true; }
            wantedMove = Vector3.zero;
            wantedToMove = false;
            ServerThink(dt);
            Vector3 before = transform.position;
            Fall(dt, wantedMove);
            Unstick(before, dt);
        }

        // Gravity and the frame's move in one sweep (a CharacterController wants one Move).
        private void Fall(float dt, Vector3 horizontal = default)
        {
            verticalSpeed = mover.isGrounded ? -1f : verticalSpeed - 9.81f * dt;
            mover.Move(horizontal + Vector3.up * verticalSpeed * dt);
        }

        // Wanted to move but did not for a second: a wall or the wreck. Sidestep for a while.
        private void Unstick(Vector3 before, float dt)
        {
            if (!wantedToMove) { stuckSince = -1f; return; }
            Vector3 now = transform.position;
            if (stuckSince < 0f) { stuckSince = Now; stuckFrom = now; return; }
            if (CreatureSenses.Flat(stuckFrom, now) > 0.6f) { stuckSince = Now; stuckFrom = now; return; }
            if (Now - stuckSince < 1f) return;
            Vector3 dir = wantedMove; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            sidestep = Vector3.Cross(Vector3.up, dir.normalized) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
            sidestepUntil = Now + 1.5f;
            stuckSince = Now; stuckFrom = now;
        }

        // ---- the brain's vocabulary --------------------------------------------------

        protected abstract void ServerThink(float dt);

        public virtual void OnNoise(in NoiseEvent noise) { }

        protected void SetPose(CreaturePose value)
        {
            if (pose.Value != (byte)value) pose.Value = (byte)value;
        }

        protected void SetTarget(int clientId)
        {
            if (targetId.Value != clientId) targetId.Value = clientId;
        }

        // A diver's walk and sprint speeds, from any diver on the site (they share the prefab).
        protected float WalkSpeed
        {
            get { IReadOnlyList<HQPlayerController> d = CreatureSenses.Divers(); return d.Count > 0 ? d[0].WalkSpeed : Settings.FallbackWalkSpeed; }
        }
        protected float SprintSpeed
        {
            get { IReadOnlyList<HQPlayerController> d = CreatureSenses.Divers(); return d.Count > 0 ? d[0].SprintSpeed : Settings.FallbackSprintSpeed; }
        }

        // Walk toward a point at a speed this frame; never into the safe ground round
        // the shaft. True when standing at it (or at the safe ground's edge before it).
        protected bool MoveToward(Vector3 point, float speed, float dt)
        {
            Vector3 here = transform.position;
            Vector3 goal = point;
            if (CreatureSenses.ShaftCentre(out Vector3 centre))
            {
                float safe = Settings.SafeZoneMeters;
                Vector3 fromCentre = goal - centre; fromCentre.y = 0f;
                if (fromCentre.magnitude < safe)
                {
                    // The goal lies on the safe ground: stop at its edge, on the side nearest us.
                    Vector3 toUs = here - centre; toUs.y = 0f;
                    Vector3 edge = (toUs.sqrMagnitude > 0.01f ? toUs.normalized : Vector3.forward) * safe;
                    goal = centre + edge;
                }
            }
            Vector3 delta = goal - here; delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.3f) return true;
            Vector3 dir = delta / distance;
            if (Now < sidestepUntil) dir = (dir + sidestep).normalized;
            float step = Mathf.Min(distance, speed * dt);
            Vector3 move = dir * step;
            // Never a step onto the safe ground.
            if (CreatureSenses.ShaftCentre(out centre))
            {
                Vector3 next = here + move - centre; next.y = 0f;
                if (next.magnitude < Settings.SafeZoneMeters) return true;
            }
            wantedMove += move;
            wantedToMove = true;
            return false;
        }

        // Turn to face a point, flat.
        protected void FaceToward(Vector3 point)
        {
            Vector3 to = point - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to, Vector3.up), 540f * Time.deltaTime);
        }

        protected bool StrikeReady => Now >= strikeReadyAt;

        // Close enough to touch a diver who is not on safe ground.
        protected bool WithinReach(HQPlayerController diver) =>
            diver != null && !CreatureSenses.Safe(diver, Settings) && CreatureSenses.Flat(transform.position, diver.transform.position) <= Settings.ReachMeters;

        // The strike this kind makes: death for the killers (the ordinary death
        // through WorldSceneFlow.ServerKill, which keeps the deck safe), health and a
        // leak for the rest. True when it landed.
        protected bool Strike(HQPlayerController diver, float damage)
        {
            if (diver == null || !StrikeReady || diver.IsDead || CreatureSenses.Safe(diver, Settings)) return false;
            strikeReadyAt = Now + Settings.StrikeCooldownSeconds;
            string name = MonsterCatalog.DisplayName(kind);
            bool landed;
            if (MonsterCatalog.Kills(kind))
            {
                WorldSceneFlow flow = WorldSceneFlow.Instance;
                landed = flow != null && flow.ServerKill(diver.Owner, out string why, "taken by " + name);
                if (!landed) Debug.Log($"[Monsters] {name} reached {WorldSceneFlow.DisplayName(diver.Owner)} but the kill was refused");
            }
            else
            {
                PlayerVitals vitals = diver.Vitals;
                landed = vitals != null && vitals.ServerDamage(damage, true, name);
            }
            if (landed) strikeSerial.Value = strikeSerial.Value + 1;
            return landed;
        }

        // A diver still living and below, or null.
        protected static HQPlayerController Living(HQPlayerController diver) =>
            diver != null && !diver.IsDead && CreatureSenses.DiverOf(diver.OwnerId) == diver ? diver : null;

        // The checks put a creature where a row needs it (server only).
        [Server]
        public void ServerPlaceForChecks(Vector3 position, float yawDegrees = 0f)
        {
            bool was = mover != null && mover.enabled;
            if (mover != null) mover.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            Physics.SyncTransforms();
            if (mover != null) mover.enabled = was;
            verticalSpeed = 0f;
            awake = true;
            FishNet.Component.Transforming.NetworkTransform networkTransform = GetComponent<FishNet.Component.Transforming.NetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport();
        }

        [Server]
        public void ServerWakeNow() { awake = true; wakeAt = Now; }
    }
}
