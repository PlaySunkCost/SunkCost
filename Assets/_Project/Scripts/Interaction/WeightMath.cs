using System;

namespace SunkCost.Interaction
{
    // Pure weight curves (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md section 4).
    // No Unity types, so the editor checks cover every number without an asset.
    // Mass is in kilograms; a nonfinite or negative mass counts as zero.
    public static class WeightMath
    {
        public const double MaxFill = 0.999999;

        // How much of the meter is filled: 1 - exp(-mass / scale), never 1.
        public static float Fill(float totalMassKg, float meterScaleKg)
        {
            double remaining = Remaining(totalMassKg, meterScaleKg);
            return (float)Math.Min(1.0 - remaining, MaxFill);
        }

        // Walk/sprint multiplier: 1 when empty, approaching minSpeedFactor.
        public static float SpeedFactor(float totalMassKg, float meterScaleKg, float minSpeedFactor)
        {
            double min = Clamp01Positive(minSpeedFactor);
            double factor = min + (1.0 - min) * Remaining(totalMassKg, meterScaleKg);
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

        private static double Remaining(float totalMassKg, float meterScaleKg)
        {
            double scale = Positive(meterScaleKg, 1.0);
            return Math.Exp(-Sanitize(totalMassKg) / scale);
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
