using UnityEngine;

namespace SunkCost.Interaction
{
    // Shared weight tuning (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md section 4).
    // One asset, referenced by every PlayerInventory and CarryableItem, so all
    // peers of a matching build derive the same speed and throw factors from the
    // server's carried mass. Missing references fall back to Default so no peer
    // ever runs a different curve from the others by accident.
    [CreateAssetMenu(menuName = "Sunk Cost/Prototype/Weight Settings", fileName = "WeightSettings")]
    public sealed class WeightSettings : ScriptableObject
    {
        [Tooltip("Asymptote scale of the meter: fill = 1 - exp(-mass / scale).")]
        [SerializeField, Min(0.01f)] private float meterScaleKg = 12f;
        [Tooltip("Speed multiplier the curve approaches when the meter is nearly full.")]
        [SerializeField, Range(0.01f, 1f)] private float minSpeedFactor = 0.35f;
        [Tooltip("Items at or below this mass throw at full speed.")]
        [SerializeField, Min(0.01f)] private float throwReferenceMassKg = 1f;
        [Tooltip("Floor for the throw multiplier of a very heavy item.")]
        [SerializeField, Range(0.01f, 1f)] private float minThrowFactor = 0.15f;

        public float MeterScaleKg => meterScaleKg;
        public float MinSpeedFactor => minSpeedFactor;
        public float ThrowReferenceMassKg => throwReferenceMassKg;
        public float MinThrowFactor => minThrowFactor;

        public const float DefaultMeterScaleKg = 12f;
        public const float DefaultMinSpeedFactor = 0.35f;
        public const float DefaultThrowReferenceMassKg = 1f;
        public const float DefaultMinThrowFactor = 0.15f;

        private static WeightSettings fallback;

        // The documented defaults, for a component whose reference was never set.
        public static WeightSettings Default
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<WeightSettings>();
                    fallback.hideFlags = HideFlags.HideAndDontSave;
                    fallback.name = "WeightSettings (default)";
                }
                return fallback;
            }
        }

        public static WeightSettings Resolve(WeightSettings assigned) => assigned != null ? assigned : Default;

        public float Fill(float totalMassKg) => WeightMath.Fill(totalMassKg, meterScaleKg);
        public float SpeedFactor(float totalMassKg) => WeightMath.SpeedFactor(totalMassKg, meterScaleKg, minSpeedFactor);
        public float ThrowFactor(float itemMassKg) => WeightMath.ThrowFactor(itemMassKg, throwReferenceMassKg, minThrowFactor);

        public bool IsValid =>
            float.IsFinite(meterScaleKg) && meterScaleKg > 0f &&
            float.IsFinite(minSpeedFactor) && minSpeedFactor > 0f && minSpeedFactor <= 1f &&
            float.IsFinite(throwReferenceMassKg) && throwReferenceMassKg > 0f &&
            float.IsFinite(minThrowFactor) && minThrowFactor > 0f && minThrowFactor <= 1f;
    }
}
