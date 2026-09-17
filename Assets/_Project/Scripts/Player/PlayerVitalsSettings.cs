using UnityEngine;

namespace SunkCost.Player
{
    // Air and health tuning (docs/DESIGN.md §3, "Air"; decided with Dan,
    // 17 September 2026). Provisional numbers; changing them is not a design
    // change. One asset, created by PlayerVitalsSetup, referenced by the player
    // prefab's PlayerVitals; a missing reference falls back to these defaults.
    [CreateAssetMenu(fileName = "PlayerVitalsSettings", menuName = "Sunk Cost/Player Vitals Settings")]
    public sealed class PlayerVitalsSettings : ScriptableObject
    {
        [Header("Air")]
        [Tooltip("Seconds a full tank lasts while walking. 5 minutes for the prototype site (Dan); the design's base target for a real site is 12–15.")]
        [SerializeField] private float tankSeconds = 300f;
        [Tooltip("Air drains this many times faster while sprinting (1 = sprinting is free). Carried weight no longer costs air (Dan, 17 September 2026).")]
        [SerializeField] private float sprintDrainMultiplier = 1.5f;
        [Tooltip("The visor blinks and says AIR LOW under this fraction of the tank.")]
        [SerializeField] private float lowAirFraction = 0.2f;

        [Header("Health")]
        [SerializeField] private int maxHealth = 100;
        [Tooltip("Health lost per second with the tank empty. 8 = a dozen seconds from empty to dead (between Minecraft's 10 s and Dan's first thought of 20).")]
        [SerializeField] private float suffocationDamagePerSecond = 8f;
        [Tooltip("The visor blinks the HP bar under this fraction.")]
        [SerializeField] private float lowHealthFraction = 0.25f;

        [Header("Debug")]
        [Tooltip("L in a development build or the editor: the owner's tank loses this fraction per press, below only.")]
        [SerializeField] private float debugAirStepFraction = 0.05f;

        public float TankSeconds => tankSeconds;
        public float SprintDrainMultiplier => sprintDrainMultiplier;
        public float LowAirFraction => lowAirFraction;
        public int MaxHealth => maxHealth;
        public float SuffocationDamagePerSecond => suffocationDamagePerSecond;
        public float LowHealthFraction => lowHealthFraction;
        public float DebugAirStepFraction => debugAirStepFraction;

        // Editor checks: a short tank so a row can watch it empty. Null = the asset's value.
        public static float? TankSecondsOverrideForTests;
        public float EffectiveTankSeconds => TankSecondsOverrideForTests ?? tankSeconds;

        private static PlayerVitalsSettings fallback;
        public static PlayerVitalsSettings Resolve(PlayerVitalsSettings assigned)
        {
            if (assigned != null) return assigned;
            if (fallback == null) { fallback = CreateInstance<PlayerVitalsSettings>(); fallback.hideFlags = HideFlags.HideAndDontSave; }
            return fallback;
        }
    }
}
