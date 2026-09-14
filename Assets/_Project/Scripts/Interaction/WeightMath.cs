using System;

namespace SunkCost.Interaction
{
    // Pure weight rules (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md section 4, as
    // amended 14 September 2026): a hard capacity. The meter is mass / capacity;
    // speed falls in a straight line from 1 to minSpeedFactor as the meter fills,
    // and at a full meter the player cannot move at all. No Unity types, so the
    // editor checks cover every number without an asset. Mass is in kilograms; a
    // nonfinite or negative mass counts as zero.
    public static class WeightMath
    {
        // How much of the meter is filled, 0..1 (1 = full or beyond).
        public static float Fill(float totalMassKg, float capacityKg)
        {
            double capacity = Positive(capacityKg, 1.0);
            return (float)Math.Min(1.0, Sanitize(totalMassKg) / capacity);
        }

        // At or over capacity: the bar is full and movement stops.
        public static bool IsOverloaded(float totalMassKg, float capacityKg)
        {
            return Sanitize(totalMassKg) >= Positive(capacityKg, 1.0);
        }

        // Walk/sprint multiplier: 1 when empty, minSpeedFactor just under full,
        // overloadSpeedFactor (a crawl) at or beyond full.
        public static float SpeedFactor(float totalMassKg, float capacityKg, float minSpeedFactor, float overloadSpeedFactor)
        {
            if (IsOverloaded(totalMassKg, capacityKg)) return (float)Clamp01Positive(overloadSpeedFactor);
            double min = Clamp01Positive(minSpeedFactor);
            double fill = Fill(totalMassKg, capacityKg);
            double factor = 1.0 - (1.0 - min) * fill;
            return (float)Math.Max(min, Math.Min(1.0, factor));
        }

        // Launch multiplier for one item: sqrt(reference / mass), clamped.
        public static float ThrowFactor(float itemMassKg, float throwReferenceMassKg, float minThrowFactor)
        {
            double reference = Positive(throwReferenceMassKg, 1.0);
            double mass = Math.Max(Sanitize(itemMassKg), reference);
            double factor = Math.Sqrt(reference / mass);
            double min = Clamp01Positive(minThrowFactor);
            return (float)Math.Max(min, Math.Min(1.0, factor));
        }

        private static double Sanitize(float mass)
        {
            return float.IsFinite(mass) && mass > 0f ? mass : 0.0;
        }

        private static double Positive(float value, double fallback)
        {
            return float.IsFinite(value) && value > 0f ? value : fallback;
        }

        private static double Clamp01Positive(float value)
        {
            if (!float.IsFinite(value) || value <= 0f) return 0.01;
            return Math.Min(1.0, value);
        }
    }
}
