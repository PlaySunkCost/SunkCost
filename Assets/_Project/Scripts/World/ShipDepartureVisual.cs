using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

namespace SunkCost.World
{
    // Moves this ship instance from the replicated departure state
    // (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md sections 3 and 7). Plain
    // presentation: every peer evaluates the same authored displacement from the
    // synchronized tick, nothing here is replicated, and the pose is always
    // computed from the authored rest transform, never integrated frame by frame.
    // Runs before the riders (they follow the ship) and before the held-item
    // LateUpdate.
    [DefaultExecutionOrder(-100)]
    public sealed class ShipDepartureVisual : MonoBehaviour
    {
        [SerializeField] private AudioSource engine; // optional; no clip is a reported gap, not an error

        private ShipParts parts;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private WorldId world;
        private bool hasWorld;
        private bool enginePlaying;

        public Vector3 RestPosition => restPosition;
        public bool HasEngineClip => engine != null && engine.clip != null;

        // SHIP-034: the ship's ~90 colliders (the hull and tower meshes among them) are
        // children of this root. Without a body they are static colliders, and moving
        // static colliders makes the physics scene move each one; with one kinematic
        // body on the root they are one compound actor that moves as a whole. Added at
        // runtime so the builders and the prefab stay as they are. The pose is still
        // written to the transform (a kinematic teleport), not by MovePosition: the
        // riders read this transform in LateUpdate every rendered frame and the pose
        // is evaluated sub-tick from the synchronized clock, so a physics-step
        // MovePosition would step the ship at 60 Hz under riders following it
        // smoothly. Nothing dynamic rides the deck during a trip (loose cargo is frozen
        // kinematic), so no contact needs the body's velocity.
        private Rigidbody body;
        public Rigidbody Body => body;

        private void Awake()
        {
            if (engine != null) SunkCost.Audio.AudioDeviceService.RouteSource(engine);
            parts = GetComponent<ShipParts>();
            restPosition = transform.position;
            restRotation = transform.rotation;
            hasWorld = WorldScenes.TryParse(gameObject.scene.name, out world);
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None; // the transform is the pose (see above)
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        private void Update()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || !hasWorld) { Hold(0f); return; }
            ShipDepartureState state = day.Departure;
            WorldLoopSettings settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            float u = StageProgress(state);
            bool source = state.FromWorld == world;

            switch (state.Stage)
            {
                case DepartureStage.RaisingGangway:
                    Hold(0f); // casting off: still, the moorings coming in
                    break;
                case DepartureStage.PullingAway:
                    Hold(source ? Mathf.SmoothStep(0f, 1f, u) * settings.DepartureDistanceMeters : 0f);
                    Engine(source);
                    break;
                case DepartureStage.FadingOut:
                case DepartureStage.Loading:
                    Hold(source ? settings.DepartureDistanceMeters : 0f);
                    Engine(source && state.Stage == DepartureStage.FadingOut);
                    break;
                default:
                    Hold(0f); // arriving and idle: at rest
                    Engine(false);
                    break;
            }
        }

        // Progress of the current stage in [0, 1] from the synchronized tick; a late
        // update lands on the current point rather than restarting the motion.
        public static float StageProgress(ShipDepartureState state)
        {
            if (state.StageDurationTicks == 0) return 1f;
            TimeManager time = InstanceFinder.TimeManager;
            if (time == null) return 1f;
            uint elapsedTicks = unchecked(time.Tick - state.StageStartTick); // wrap-safe
            if (elapsedTicks > int.MaxValue) return 0f;                       // stage start is still in the future for this peer
            double elapsed = elapsedTicks + time.GetTickPercentAsDouble();
            return Mathf.Clamp01((float)(elapsed / state.StageDurationTicks));
        }

        private void Hold(float distance)
        {
            Transform direction = parts != null ? parts.DepartureDirection : null;
            Vector3 forward = direction != null ? direction.forward : transform.forward;
            Vector3 position = restPosition + forward.normalized * distance;
            // At rest (almost always) nothing is written: every write re-poses the whole
            // ship in the physics scene, and this ran every frame at the pier and at sea.
            if (transform.position == position && transform.rotation == restRotation) return;
            transform.SetPositionAndRotation(position, restRotation);
        }

        private void Engine(bool on)
        {
            if (engine == null || engine.clip == null || on == enginePlaying) return;
            enginePlaying = on;
            if (on) engine.Play(); else engine.Stop();
        }
    }
}
