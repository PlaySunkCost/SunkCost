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

        public float SailingFadeSeconds => sailingFadeSeconds;
        public float ArrivalTimeoutSeconds => arrivalTimeoutSeconds;
        public float UnparentTimeoutSeconds => unparentTimeoutSeconds;
        public int SyncFlushTicks => syncFlushTicks;
        public float RefusalDisplaySeconds => refusalDisplaySeconds;
        public Vector3 ShipAtSeaOrigin => shipAtSeaOrigin;
        public int DaysPerCycle => daysPerCycle;
        public bool LockRidersDuringRide => lockRidersDuringRide;

        public bool IsValid =>
            sailingFadeSeconds >= 0f && arrivalTimeoutSeconds > 0f && unparentTimeoutSeconds > 0f &&
            syncFlushTicks >= 1 && refusalDisplaySeconds >= 0f && daysPerCycle >= 1 &&
            float.IsFinite(shipAtSeaOrigin.x) && float.IsFinite(shipAtSeaOrigin.y) && float.IsFinite(shipAtSeaOrigin.z);

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
