using System;
using SunkCost.Interaction;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Repeatable pure checks for the loot/weight work
    // (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md section 9): the curves, their
    // sanitisation, the two-handed grab rule and the shipped settings asset.
    // Throws on the first failure with the case name.
    public static class HQPrototypeWeightChecks
    {
        private const float Scale = WeightSettings.DefaultMeterScaleKg;
        private const float MinSpeed = WeightSettings.DefaultMinSpeedFactor;
        private const float Reference = WeightSettings.DefaultThrowReferenceMassKg;
        private const float MinThrow = WeightSettings.DefaultMinThrowFactor;

        [MenuItem("Sunk Cost/Prototype/Run weight checks")]
        public static void RunFromMenu()
        {
            RunOrThrow();
            Debug.Log("Weight checks passed.");
        }

        public static void RunOrThrow()
        {
            Curves();
            Sanitisation();
            ThrowFactors();
            GrabTable();
            Asset();
        }

        private static void Curves()
        {
            Expect(WeightMath.Fill(0f, Scale) == 0f, "empty meter is 0");
            Expect(WeightMath.SpeedFactor(0f, Scale, MinSpeed) == 1f, "empty speed factor is 1");
            Near(WeightMath.Fill(0.62f, Scale), 0.0504f, "basketball fill");
            Near(WeightMath.SpeedFactor(0.62f, Scale, MinSpeed), 0.9673f, "basketball speed");
            Near(WeightMath.Fill(6f, Scale), 0.3935f, "blue fill");
            Near(WeightMath.SpeedFactor(6f, Scale, MinSpeed), 0.7442f, "blue speed");
            Near(WeightMath.Fill(12f, Scale), 0.6321f, "purple fill");
            Near(WeightMath.SpeedFactor(12f, Scale, MinSpeed), 0.5891f, "purple speed");
            Near(WeightMath.Fill(20f, Scale), 0.8111f, "black fill");
            Near(WeightMath.SpeedFactor(20f, Scale, MinSpeed), 0.4728f, "black speed");
            Near(WeightMath.Fill(21.86f, Scale), 0.8382f, "three basketballs + black fill");
            Near(WeightMath.SpeedFactor(21.86f, Scale, MinSpeed), 0.4551f, "three basketballs + black speed");

            float previousFill = -1f, previousSpeed = 2f;
            for (float mass = 0f; mass <= 200f; mass += 0.5f)
            {
                float fill = WeightMath.Fill(mass, Scale);
                float speed = WeightMath.SpeedFactor(mass, Scale, MinSpeed);
                Expect(fill >= previousFill, "meter never decreases at " + mass);
                Expect(speed <= previousSpeed, "speed never increases at " + mass);
                Expect(fill < 1f, "meter never full at " + mass);
                Expect(speed >= MinSpeed && speed <= 1f, "speed within bounds at " + mass);
                previousFill = fill; previousSpeed = speed;
            }
            Expect(WeightMath.Fill(1000f, Scale) < 1f, "meter never full at 1000 kg");
            Expect(WeightMath.Fill(float.MaxValue, Scale) < 1f, "meter never full at float.MaxValue");
            Expect(WeightMath.SpeedFactor(float.MaxValue, Scale, MinSpeed) >= MinSpeed, "speed floor at float.MaxValue");
        }

        private static void Sanitisation()
        {
            Expect(WeightMath.Fill(-5f, Scale) == 0f, "negative mass counts as zero");
            Expect(WeightMath.Fill(float.NaN, Scale) == 0f, "NaN mass counts as zero");
            Expect(WeightMath.Fill(float.PositiveInfinity, Scale) < 1f && float.IsFinite(WeightMath.Fill(float.PositiveInfinity, Scale)), "infinite mass stays finite and below 1");
            Expect(float.IsFinite(WeightMath.SpeedFactor(5f, 0f, MinSpeed)), "zero scale falls back");
            Expect(float.IsFinite(WeightMath.SpeedFactor(5f, float.NaN, float.NaN)), "NaN settings fall back");
            Expect(WeightMath.SpeedFactor(5f, Scale, 0f) > 0f, "zero min speed becomes a small positive floor");
        }

        private static void ThrowFactors()
        {
            Near(WeightMath.ThrowFactor(0.62f, Reference, MinThrow), 1f, "basketball throws at full speed");
            Near(WeightMath.ThrowFactor(6f, Reference, MinThrow), 0.40825f, "blue throw factor");
            Near(WeightMath.ThrowFactor(12f, Reference, MinThrow), 0.28868f, "purple throw factor");
            Near(WeightMath.ThrowFactor(20f, Reference, MinThrow), 0.22361f, "black throw factor");
            Expect(WeightMath.ThrowFactor(10000f, Reference, MinThrow) == MinThrow, "throw factor floors at the minimum");
            Expect(WeightMath.ThrowFactor(float.NaN, Reference, MinThrow) == 1f, "NaN item mass throws at full speed (fallback)");
        }

        private static void GrabTable()
        {
            // Two-handed items report FitsInSlot false, so the table never gives them a slot.
            Expect(InventoryRules.DecideGrab(false, 0, true, false) == GrabOutcome.HoldOverflow, "hands-only + hands empty + free slots -> overflow");
            Expect(InventoryRules.DecideGrab(false, -1, true, false) == GrabOutcome.HoldOverflow, "hands-only + hands empty + full slots -> overflow");
            Expect(InventoryRules.DecideGrab(false, 0, false, false) == GrabOutcome.StowAndHoldOverflow, "hands-only + equipped slot item + free slots -> auto-stow then hold");
            Expect(InventoryRules.DecideGrab(false, -1, false, false) == GrabOutcome.StowAndHoldOverflow, "hands-only + equipped slot item + full slots -> auto-stow then hold");
            Expect(InventoryRules.DecideGrab(false, 0, false, true) == GrabOutcome.RefuseHandsFull, "hands-only while holding overflow -> refuse");
            Expect(InventoryRules.DecideGrab(true, 0, false, true) == GrabOutcome.RefuseHandsFull, "slot item while holding overflow -> refuse");
        }

        private static void Asset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WeightSettings>(HQPrototypeLootSetup.WeightSettingsPath);
            if (settings == null) return; // the validator reports a missing asset; the curves above cover the defaults
            Expect(settings.IsValid, "WeightSettings asset values are valid");
            Expect(WeightSettings.Default.IsValid, "WeightSettings.Default is valid");
            Expect(WeightSettings.Resolve(null) == WeightSettings.Default, "missing reference resolves to Default");
            Expect(WeightSettings.Resolve(settings) == settings, "assigned reference resolves to itself");
        }

        private static void Near(float actual, float expected, string what)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
                throw new InvalidOperationException($"Weight check failed: {what} expected {expected} got {actual}");
        }

        private static void Expect(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("Weight check failed: " + what);
        }
    }
}
