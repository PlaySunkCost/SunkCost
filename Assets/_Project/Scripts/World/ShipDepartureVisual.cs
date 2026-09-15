using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

namespace SunkCost.World
{
    // Moves this ship instance and its gangway from the replicated departure
    // state (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md sections 3 and 7). Plain
    // presentation: every peer evaluates the same authored displacement from the
    // synchronized tick, nothing here is replicated, and the pose is always
    // computed from the authored rest transform, never integrated frame by frame.
    // Runs before the riders (they follow the ship) and before the held-item
    // LateUpdate.
    [DefaultExecutionOrder(-100)]
    public sealed class ShipDepartureVisual : MonoBehaviour
    {
        // Gangway rotation about the pivot's X axis: 0 = lowered onto the dock.
        public const float RaisedAngleDegrees = 80f;

        [SerializeField] private AudioSource engine; // optional; no clip is a reported gap, not an error

        private ShipParts parts;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private WorldId world;
        private bool hasWorld;
        private float gangwayAngle;     // current, degrees
        private bool enginePlaying;

        public Vector3 RestPosition => restPosition;
        public float GangwayAngle => gangwayAngle;
        public bool GangwayLowered => gangwayAngle <= 0.01f;
        public bool HasEngineClip => engine != null && engine.clip != null;

        private void Awake()
        {
            parts = GetComponent<ShipParts>();
            restPosition = transform.position;
            restRotation = transform.rotation;
            hasWorld = WorldScenes.TryParse(gameObject.scene.name, out world);
            // Docked at HQ the ramp is down; anywhere else it is stowed.
            gangwayAngle = hasWorld && world == WorldId.HQ ? 0f : RaisedAngleDegrees;
            ApplyGangway();
        }

        private void Update()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || !hasWorld) { Hold(0f, RestingGangwayAngle()); return; }
            ShipDepartureState state = day.Departure;
            WorldLoopSettings settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            float u = StageProgress(state);
            bool source = state.FromWorld == world;
            bool destination = state.ToWorld == world;

            switch (state.Stage)
            {
                case DepartureStage.RaisingGangway:
                    Hold(0f, source ? Mathf.Lerp(0f, RaisedAngleDegrees, u) : RestingGangwayAngle());
                    break;
                case DepartureStage.PullingAway:
                    Hold(source ? Mathf.SmoothStep(0f, 1f, u) * settings.DepartureDistanceMeters : 0f, source ? RaisedAngleDegrees : RestingGangwayAngle());
                    Engine(source);
                    break;
                case DepartureStage.FadingOut:
                case DepartureStage.Loading:
                    Hold(source ? settings.DepartureDistanceMeters : 0f, source ? RaisedAngleDegrees : RestingGangwayAngle());
                    Engine(source && state.Stage == DepartureStage.FadingOut);
                    break;
                case DepartureStage.Arriving:
                    // The destination ship is at rest; at HQ its gangway comes down over the stage.
                    Hold(0f, destination && world == WorldId.HQ ? Mathf.Lerp(RaisedAngleDegrees, 0f, u) : RestingGangwayAngle());
                    Engine(false);
                    break;
                default:
                    Hold(0f, RestingGangwayAngle());
                    Engine(false);
                    break;
            }
        }

        private float RestingGangwayAngle() => world == WorldId.HQ ? 0f : RaisedAngleDegrees;

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

        private void Hold(float distance, float angle)
        {
            Transform direction = parts != null ? parts.DepartureDirection : null;
            Vector3 forward = direction != null ? direction.forward : transform.forward;
            transform.SetPositionAndRotation(restPosition + forward.normalized * distance, restRotation);
            if (!Mathf.Approximately(angle, gangwayAngle))
            {
                gangwayAngle = angle;
                ApplyGangway();
            }
        }

        private void ApplyGangway()
        {
            Transform pivot = parts != null ? parts.GangwayPivot : null;
            if (pivot == null) return;
            pivot.localRotation = Quaternion.Euler(gangwayAngle, 0f, 0f);
            // The ramp only carries weight when fully down; while moving it is not a floor.
            Collider ramp = parts.GangwayCollider;
            if (ramp != null) ramp.enabled = GangwayLowered;
        }

        private void Engine(bool on)
        {
            if (engine == null || engine.clip == null || on == enginePlaying) return;
            enginePlaying = on;
            if (on) engine.Play(); else engine.Stop();
        }
    }
}
