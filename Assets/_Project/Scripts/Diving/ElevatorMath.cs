using UnityEngine;

namespace SunkCost.Diving
{
    // The car's motion profile and the water it meets (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md
    // section 5). Pure arithmetic shared by the controller's own state machine,
    // the driven mode, the cabin water, the submersion indicator and the editor
    // checks. Depth is the car floor's distance below its top position.
    public static class ElevatorMath
    {
        public struct Profile
        {
            public float DepthMeters;          // top position to bottom position
            public float SurfaceDepthMeters;   // top position to sea level (the floor meets the water here)
            public float SpanMeters;           // floor to roof: the slow band's length
            public float TravelSpeed;          // m/s outside the band
            public float CrossingSpeed;        // m/s while the span crosses the surface

            public static Profile Of(float depth, float surfaceDepth, float span, float travelSpeed, float crossingSpeed) => new()
            {
                DepthMeters = Mathf.Max(0f, depth),
                SurfaceDepthMeters = surfaceDepth,
                SpanMeters = Mathf.Max(0f, span),
                TravelSpeed = Mathf.Max(0.001f, travelSpeed),
                CrossingSpeed = Mathf.Max(0.001f, crossingSpeed)
            };
        }

        // The band of floor depths in which the car's span crosses the surface,
        // clipped to the shaft. Empty when the surface is above the top position
        // by more than the span or below the bottom.
        public static void SlowBand(Profile p, out float start, out float end)
        {
            start = Mathf.Clamp(p.SurfaceDepthMeters, 0f, p.DepthMeters);
            end = Mathf.Clamp(p.SurfaceDepthMeters + p.SpanMeters, 0f, p.DepthMeters);
            if (end < start) end = start;
        }

        public static float TravelSeconds(Profile p)
        {
            SlowBand(p, out float start, out float end);
            return start / p.TravelSpeed + (end - start) / p.CrossingSpeed + (p.DepthMeters - end) / p.TravelSpeed;
        }

        // Floor depth after t seconds of a descent from the top.
        public static float DescentDepthAt(Profile p, float t)
        {
            if (t <= 0f) return 0f;
            SlowBand(p, out float start, out float end);
            float t1 = start / p.TravelSpeed;
            if (t <= t1) return t * p.TravelSpeed;
            float t2 = t1 + (end - start) / p.CrossingSpeed;
            if (t <= t2) return start + (t - t1) * p.CrossingSpeed;
            return Mathf.Min(p.DepthMeters, end + (t - t2) * p.TravelSpeed);
        }

        // The ascent retraces the descent backwards in time: the slow band is
        // near the top, where the roof surfaces first.
        public static float DepthAt(Profile p, float t, bool upward)
        {
            if (!upward) return DescentDepthAt(p, t);
            float total = TravelSeconds(p);
            return DescentDepthAt(p, Mathf.Max(0f, total - t));
        }

        // 0 at the top, 1 at the bottom; what the controller lerps its transform with.
        public static float ProgressAt(Profile p, float t, bool upward) =>
            p.DepthMeters <= 0f ? (upward ? 0f : 1f) : Mathf.Clamp01(DepthAt(p, t, upward) / p.DepthMeters);

        // Water inside the car: the tube's water stands at sea level, the cabin is
        // open to it, so the level inside is the level outside, clamped to the cabin.
        public static float WaterLevelInCar(float seaLevelY, float carFloorY, float interiorHeight) =>
            Mathf.Clamp(seaLevelY - carFloorY, 0f, Mathf.Max(0f, interiorHeight));

        public static bool IsBelowSurface(float seaLevelY, float y) => y < seaLevelY;

        // ---- the car's own water (the new elevator, 28 September 2026) ----------------
        // One truth: the level above stays the only level. The car is a sealed glass car
        // whose roof nozzles pour while it sinks through the surface and whose floor
        // grilles drain while it rises through it; the rule is that its water stands where
        // the sea outside stands while its span crosses the surface. Everything below is
        // derived from that level and the car's replicated state: nothing new is synced.

        // Below this the car counts as dry, and within this of the roof as full (the same
        // threshold CabinWater.IsWet has always used).
        public const float WaterMarginMeters = 0.02f;

        // The world y of the water inside the car: its floor datum (the root) plus the level.
        // For any eye inside the car, eye < this ⇔ eye < sea level (docs: MAP.md §4), which
        // is why PlayerSubmersion and the underwater grade can keep reading sea level.
        public static float CarWaterSurfaceY(float seaLevelY, float carRootY, float interiorHeight) =>
            carRootY + WaterLevelInCar(seaLevelY, carRootY, interiorHeight);

        // Which way the water moves: the nozzles pour only while the car descends and the
        // level is between dry and full; the grilles drain only while it ascends likewise.
        public static CarWaterFlow WaterFlowInCar(ElevatorState state, float level, float interiorHeight)
        {
            if (level <= WaterMarginMeters || level >= interiorHeight - WaterMarginMeters) return CarWaterFlow.Still;
            return state switch
            {
                ElevatorState.Descending => CarWaterFlow.Filling,
                ElevatorState.Ascending => CarWaterFlow.Draining,
                _ => CarWaterFlow.Still
            };
        }
    }

    // The car water's direction of change, derived per frame (ElevatorMath.WaterFlowInCar).
    public enum CarWaterFlow : byte { Still = 0, Filling = 1, Draining = 2 }
}
