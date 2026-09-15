using UnityEngine;

namespace SunkCost.World
{
    // Tuning of the scene flow (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 7).
    // Provisional numbers; changing them is not a design change.
    [CreateAssetMenu(fileName = "WorldLoopSettings", menuName = "Sunk Cost/World Loop Settings")]
    public sealed class WorldLoopSettings : ScriptableObject
    {
        [Tooltip("Seconds the screen takes to go black when the ship sails, and to come back.")]
        [SerializeField] private float sailingFadeSeconds = 1.5f;
        [Tooltip("Seconds the server waits for every moved connection to report the new scene before it proceeds anyway.")]
        [SerializeField] private float arrivalTimeoutSeconds = 10f;
        [Tooltip("Seconds the server waits for a rider's NetworkTransform to deliver the unparent before forcing it.")]
        [SerializeField] private float unparentTimeoutSeconds = 1f;
        [Tooltip("Ticks between a SyncVar change and the scene broadcast that depends on it (contract section 6).")]
        [SerializeField] private int syncFlushTicks = 2;
        [Tooltip("Seconds a refusal (who is missing) stays on the monitor or cabin panel.")]
        [SerializeField] private float refusalDisplaySeconds = 3f;
        [Tooltip("World position of the ship in ShipAtSea; the site sits at the origin and both are loaded during a day.")]
        [SerializeField] private Vector3 shipAtSeaOrigin = new(0f, 0f, 500f);
        [SerializeField] private int daysPerCycle = 3;
        [Tooltip("Fallback if the parent switch pops on Steam: riders stand still for the ride.")]
        [SerializeField] private bool lockRidersDuringRide;

        [Header("Ship departure (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md)")]
        [Tooltip("Seconds the gangway takes to lift before the ship moves.")]
        [SerializeField] private float gangwayRaiseSeconds = 0.8f;
        [Tooltip("Seconds of visible pull-away before the fade.")]
        [SerializeField] private float departureSeconds = 4f;
        [Tooltip("Metres the ship travels along its DepartureDirection during the pull-away.")]
        [SerializeField] private float departureDistanceMeters = 8f;
        [Tooltip("Seconds of the departure fade to black and of the fade back in.")]
        [SerializeField] private float departureFadeSeconds = 0.75f;
        [Tooltip("Seconds the gangway takes to lower on arrival at HQ before controls return.")]
        [SerializeField] private float gangwayLowerSeconds = 0.8f;
        [Tooltip("Seconds the server waits for every passenger to acknowledge the lock before cancelling.")]
        [SerializeField] private float prepareTimeoutSeconds = 10f;

        public float SailingFadeSeconds => sailingFadeSeconds;
        public float ArrivalTimeoutSeconds => arrivalTimeoutSeconds;
        public float UnparentTimeoutSeconds => unparentTimeoutSeconds;
        public int SyncFlushTicks => syncFlushTicks;
        public float RefusalDisplaySeconds => refusalDisplaySeconds;
        public Vector3 ShipAtSeaOrigin => shipAtSeaOrigin;
        public int DaysPerCycle => daysPerCycle;
        public bool LockRidersDuringRide => lockRidersDuringRide;
        public float GangwayRaiseSeconds => gangwayRaiseSeconds;
        public float DepartureSeconds => departureSeconds;
        public float DepartureDistanceMeters => departureDistanceMeters;
        public float DepartureFadeSeconds => departureFadeSeconds;
        public float GangwayLowerSeconds => gangwayLowerSeconds;
        public float PrepareTimeoutSeconds => prepareTimeoutSeconds;

        public bool IsValid =>
            sailingFadeSeconds >= 0f && arrivalTimeoutSeconds > 0f && unparentTimeoutSeconds > 0f &&
            syncFlushTicks >= 1 && refusalDisplaySeconds >= 0f && daysPerCycle >= 1 &&
            float.IsFinite(shipAtSeaOrigin.x) && float.IsFinite(shipAtSeaOrigin.y) && float.IsFinite(shipAtSeaOrigin.z) &&
            gangwayRaiseSeconds >= 0f && departureSeconds > 0f && float.IsFinite(departureDistanceMeters) && departureDistanceMeters > 0f &&
            departureFadeSeconds >= 0f && gangwayLowerSeconds >= 0f && prepareTimeoutSeconds > 0f;

        private static WorldLoopSettings fallback;

        // Code never dereferences a missing asset: the fallback carries the same
        // defaults as a freshly created one.
        public static WorldLoopSettings Resolve(WorldLoopSettings settings)
        {
            if (settings != null && settings.IsValid) return settings;
            if (fallback == null)
            {
                fallback = CreateInstance<WorldLoopSettings>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
}
