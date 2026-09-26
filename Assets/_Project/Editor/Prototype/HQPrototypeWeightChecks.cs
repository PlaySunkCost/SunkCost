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
        private const float Capacity = WeightSettings.DefaultCapacityKg;
        private const float MinSpeed = WeightSettings.DefaultMinSpeedFactor;
        private const float Crawl = WeightSettings.DefaultOverloadSpeedFactor;
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

        // Capacity 25 kg, floor 0.35: fill = mass / 25, speed = 1 - 0.65 * fill, a 0.1 crawl when full.
        private static void Curves()
        {
            Expect(WeightMath.Fill(0f, Capacity) == 0f, "empty meter is 0");
            Expect(WeightMath.SpeedFactor(0f, Capacity, MinSpeed, Crawl) == 1f, "empty speed factor is 1");
            Expect(!WeightMath.IsOverloaded(0f, Capacity), "empty is not overloaded");
            Near(WeightMath.Fill(1.24f, Capacity), 0.0496f, "basketball fill");
            Near(WeightMath.SpeedFactor(1.24f, Capacity, MinSpeed, Crawl), 0.96776f, "basketball speed");
            Near(WeightMath.Fill(6f, Capacity), 0.24f, "blue fill");
            Near(WeightMath.SpeedFactor(6f, Capacity, MinSpeed, Crawl), 0.844f, "blue speed");
            Near(WeightMath.Fill(7.24f, Capacity), 0.2896f, "basketball + blue fill");
            Near(WeightMath.SpeedFactor(7.24f, Capacity, MinSpeed, Crawl), 0.81176f, "basketball + blue speed");
            Near(WeightMath.Fill(12f, Capacity), 0.48f, "purple fill");
            Near(WeightMath.SpeedFactor(12f, Capacity, MinSpeed, Crawl), 0.688f, "purple speed");
            Near(WeightMath.Fill(20f, Capacity), 0.8f, "black fill");
            Near(WeightMath.SpeedFactor(20f, Capacity, MinSpeed, Crawl), 0.48f, "black speed");
            Near(WeightMath.Fill(23.72f, Capacity), 0.9488f, "three basketballs + black fill");
            Near(WeightMath.SpeedFactor(23.72f, Capacity, MinSpeed, Crawl), 0.38328f, "three basketballs + black speed");
            Near(WeightMath.SpeedFactor(24.99f, Capacity, MinSpeed, Crawl), 0.3503f, "just under full is the floor");
            Expect(WeightMath.IsOverloaded(25f, Capacity), "exactly capacity is overloaded");
            Expect(WeightMath.SpeedFactor(25f, Capacity, MinSpeed, Crawl) == Crawl, "full meter is a crawl, not a stop");
            Expect(WeightMath.Fill(25f, Capacity) == 1f && WeightMath.Fill(400f, Capacity) == 1f, "fill caps at 1 beyond capacity");
            Expect(WeightMath.SpeedFactor(400f, Capacity, MinSpeed, Crawl) == Crawl, "way over capacity is still the crawl");

            float previousFill = -1f, previousSpeed = 2f;
            for (float mass = 0f; mass <= 60f; mass += 0.25f)
            {
                float fill = WeightMath.Fill(mass, Capacity);
                float speed = WeightMath.SpeedFactor(mass, Capacity, MinSpeed, Crawl);
                Expect(fill >= previousFill, "meter never decreases at " + mass);
                Expect(speed <= previousSpeed, "speed never increases at " + mass);
                Expect(fill >= 0f && fill <= 1f, "fill within bounds at " + mass);
                Expect(speed == Crawl || (speed >= MinSpeed && speed <= 1f), "speed is the crawl or within bounds at " + mass);
                previousFill = fill; previousSpeed = speed;
            }
        }

        private static void Sanitisation()
        {
            Expect(WeightMath.Fill(-5f, Capacity) == 0f, "negative mass counts as zero");
            Expect(WeightMath.Fill(float.NaN, Capacity) == 0f, "NaN mass counts as zero");
            Expect(WeightMath.Fill(float.PositiveInfinity, Capacity) == 0f && !WeightMath.IsOverloaded(float.PositiveInfinity, Capacity), "nonfinite mass sanitises to zero (invalid content never freezes a player)");
            Expect(float.IsFinite(WeightMath.SpeedFactor(5f, 0f, MinSpeed, Crawl)), "zero capacity falls back");
            Expect(float.IsFinite(WeightMath.SpeedFactor(5f, float.NaN, float.NaN, float.NaN)), "NaN settings fall back");
            Expect(WeightMath.SpeedFactor(5f, Capacity, 0f, Crawl) > 0f, "zero min speed becomes a small positive floor");
        }

        private static void ThrowFactors()
        {
            Near(WeightMath.ThrowFactor(1.24f, Reference, MinThrow), 0.8980265f, "heavier basketball throw factor");
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
            Expect(InventoryRules.DecideGrab(true, 0, false, true) == GrabOutcome.StowIntoSlot, "slot item while holding overflow -> straight into the slot");
            Expect(InventoryRules.DecideGrab(true, -1, false, true) == GrabOutcome.RefuseHandsFull, "slot item while holding overflow with full slots -> refuse");
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
