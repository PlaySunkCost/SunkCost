using System;
using System.Collections.Generic;
using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.Sites;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Pure and asset checks for docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md section 8.1:
    // the motion profile arithmetic, the water level, the settings and the prefab
    // invariants. No Play Mode; the scene itself is the dive site validator's.
    public static class ShaftTubeChecks
    {
        [MenuItem("Sunk Cost/Prototype/Run shaft tube checks")]
        public static void RunFromMenu() => Debug.Log(RunOrThrow());

        public static string RunOrThrow()
        {
            var errors = new List<string>();
            DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
            if (settings == null) errors.Add("DiveSiteSettings asset missing at " + DiveSiteBuilder.SettingsPath + ".");
            else if (!settings.IsValid) errors.Add("DiveSiteSettings is invalid.");

            // The profile with the defaults: 45 m deep, surface 1 m down, 3.5 m span, 3 and 1 m/s.
            ElevatorMath.Profile p = ElevatorMath.Profile.Of(45f, 1f, 3.5f, 3f, 1f);
            float total = ElevatorMath.TravelSeconds(p);
            Near(errors, total, 1f / 3f + 3.5f + 40.5f / 3f, 1e-3f, "travel seconds with the defaults (about 17.33)");
            Near(errors, ElevatorMath.DepthAt(p, 0f, false), 0f, 1e-5f, "descent starts at the top");
            Near(errors, ElevatorMath.DepthAt(p, total, false), 45f, 1e-3f, "descent ends at the bottom");
            Near(errors, ElevatorMath.DepthAt(p, total + 5f, false), 45f, 1e-3f, "descent stays at the bottom after the travel time");
            Near(errors, Speed(p, 0.1f, false), 3f, 0.05f, "3 m/s before the surface");
            Near(errors, Speed(p, 2f, false), 1f, 0.05f, "1 m/s while the span crosses the surface");
            Near(errors, Speed(p, 10f, false), 3f, 0.05f, "3 m/s below the band");
            // Monotonic and continuous.
            float previous = -1f;
            for (float t = 0f; t <= total + 0.5f; t += 0.05f)
            {
                float d = ElevatorMath.DepthAt(p, t, false);
                if (d < previous - 1e-5f) { errors.Add($"descent depth went backwards at t={t:0.00}"); break; }
                if (previous >= 0f && d - previous > 3f * 0.05f + 1e-3f) { errors.Add($"descent depth jumped at t={t:0.00}"); break; }
                previous = d;
            }
            // The ascent retraces the descent backwards in time; its slow band is near the top.
            Near(errors, ElevatorMath.DepthAt(p, 0f, true), 45f, 1e-3f, "ascent starts at the bottom");
            Near(errors, ElevatorMath.DepthAt(p, total, true), 0f, 1e-3f, "ascent ends at the top");
            for (float t = 0f; t <= total; t += 0.5f)
                if (Mathf.Abs(ElevatorMath.DepthAt(p, t, true) - ElevatorMath.DepthAt(p, total - t, false)) > 1e-3f) { errors.Add($"ascent does not mirror the descent at t={t:0.0}"); break; }
            Near(errors, Speed(p, total - 2f, true), 1f, 0.05f, "1 m/s while surfacing through the band");
            Near(errors, Speed(p, 5f, true), 3f, 0.05f, "3 m/s at the start of the ascent");
            // A surface above the top or below the bottom: no band, plain travel.
            ElevatorMath.Profile dry = ElevatorMath.Profile.Of(45f, -10f, 3.5f, 3f, 1f);
            Near(errors, ElevatorMath.TravelSeconds(dry), 15f, 1e-3f, "no surface in the shaft: 15 s at 3 m/s");
            // Water in the car.
            Near(errors, ElevatorMath.WaterLevelInCar(-1f, 0f, 3.5f), 0f, 1e-5f, "dry with the floor above sea level");
            Near(errors, ElevatorMath.WaterLevelInCar(-1f, -2.5f, 3.5f), 1.5f, 1e-5f, "1.5 m of water with the floor 1.5 m under");
            Near(errors, ElevatorMath.WaterLevelInCar(-1f, -45f, 3.5f), 3.5f, 1e-5f, "full at the bottom");
            if (ElevatorMath.IsBelowSurface(-1f, -0.999f)) errors.Add("an eye just above the surface counts as below.");
            if (!ElevatorMath.IsBelowSurface(-1f, -1.001f)) errors.Add("an eye just below the surface counts as above.");
            if (settings != null)
            {
                float derived = settings.ElevatorTravelSecondsOneWay(0f);
                if (derived <= 0f || float.IsNaN(derived)) errors.Add("the settings' derived travel time is not positive.");
                if (settings.TubeRadiusMeters <= settings.CarDiameterMeters / 2f) errors.Add("the tube radius must exceed the car's.");
            }

            // Prefab invariants.
            GameObject car = AssetDatabase.LoadAssetAtPath<GameObject>(ElevatorCabinBuilder.PrefabPath);
            if (car == null) errors.Add("elevator prefab missing.");
            else
            {
                CabinWater water = car.GetComponent<CabinWater>();
                if (water == null) errors.Add("elevator prefab has no CabinWater (run Apply shaft tube setup).");
                Transform surface = car.transform.Find(ShaftTubeSetup.CabinWaterSurfaceName);
                if (surface == null) errors.Add("elevator prefab has no cabin water disc.");
                else if (surface.GetComponent<Collider>() != null) errors.Add("the cabin water disc must carry no collider.");
                if (car.GetComponent<ElevatorController>() == null) errors.Add("elevator prefab has no ElevatorController.");
            }
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            if (player == null) errors.Add("player prefab missing.");
            else if (player.GetComponent<PlayerSubmersion>() == null) errors.Add("player prefab has no PlayerSubmersion.");

            if (errors.Count > 0) throw new InvalidOperationException("Shaft tube checks failed:\n- " + string.Join("\n- ", errors));
            return "Shaft tube checks passed: motion profile, water level, settings, prefabs.";
        }

        private static float Speed(ElevatorMath.Profile p, float t, bool upward)
        {
            const float dt = 0.01f;
            return Mathf.Abs(ElevatorMath.DepthAt(p, t + dt, upward) - ElevatorMath.DepthAt(p, t - dt, upward)) / (2f * dt);
        }

        private static void Near(List<string> errors, float actual, float expected, float tolerance, string label)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > tolerance) errors.Add($"{label}: {actual} (expected {expected}).");
        }
    }
}
