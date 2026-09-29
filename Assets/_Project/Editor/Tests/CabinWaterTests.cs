using NUnit.Framework;
using SunkCost.Diving;
using UnityEngine;

namespace SunkCost.Editor.Tests
{
    // The car's water (the new elevator, 28 September 2026): one truth, today's level
    // formula, and everything the visuals derive from it. Pure arithmetic on the DiveSite01
    // numbers (top root y 0, 45 m deep, sea level -4.5, span 3.5, 3 and 1 m/s): no scene,
    // no Play Mode. Runs in the Test Runner (Edit Mode; this folder compiles into
    // Assembly-CSharp-Editor, which references nunit) and, for the editor bridge, through
    // RunAll().
    [TestFixture]
    public sealed class CabinWaterTests
    {
        private const float Sea = -4.5f, Span = 3.5f, Depth = 45f;
        private static ElevatorMath.Profile Site => ElevatorMath.Profile.Of(Depth, 0f - Sea, Span, 3f, 1f);

        public static string RunAll()
        {
            var t = new CabinWaterTests();
            t.Level_IsTheSeaAgainstTheRoot();
            t.OneTruth_EyeUnderCarWater_IsEyeUnderSea();
            t.Flood_StartsAsTheCarGoesUnder_FullInAboutFourSeconds();
            t.Drain_StartsAsTheRoofSurfaces_DryBeforeTheTop();
            t.Flow_FollowsTheRideDirection_StillWhenDryOrFull();
            t.Sorting_InsideIsTheReverseOfOutside();
            t.TubeRing_OnlyWhileTheCarIsAtTheSurface();
            t.Screen_SaysFloodingAndDraining();
            t.Screen_WrapsLongLinesToShortOnes();
            return "Cabin water tests passed: 9";
        }

        [Test]
        public void Level_IsTheSeaAgainstTheRoot()
        {
            Assert.AreEqual(0f, ElevatorMath.WaterLevelInCar(Sea, 0f, Span), 1e-6f);
            Assert.AreEqual(0f, ElevatorMath.WaterLevelInCar(Sea, -4.5f, Span), 1e-6f);
            Assert.AreEqual(1.5f, ElevatorMath.WaterLevelInCar(Sea, -6f, Span), 1e-6f);
            Assert.AreEqual(Span, ElevatorMath.WaterLevelInCar(Sea, -8f, Span), 1e-6f);
            Assert.AreEqual(Span, ElevatorMath.WaterLevelInCar(Sea, -45f, Span), 1e-6f);
            for (float root = 0f; root >= -45f; root -= 0.05f)
                Assert.AreEqual(root + ElevatorMath.WaterLevelInCar(Sea, root, Span), ElevatorMath.CarWaterSurfaceY(Sea, root, Span), 1e-5f, "surface y at root " + root);
        }

        // MAP.md §4: for every eye inside the car (floor top root + 0.10 to root + 3.40),
        // being under the car's water is being under the sea, so PlayerSubmersion and the
        // underwater grade (which read sea level) agree with the visible water.
        [Test]
        public void OneTruth_EyeUnderCarWater_IsEyeUnderSea()
        {
            for (float root = 1f; root >= -46f; root -= 0.013f)
            {
                float surface = ElevatorMath.CarWaterSurfaceY(Sea, root, Span);
                for (float eye = root + 0.10f; eye <= root + 3.40f; eye += 0.017f)
                {
                    if (Mathf.Abs(eye - Sea) < 1e-4f) continue; // on the surface itself: float rounding decides
                    Assert.AreEqual(eye < surface, ElevatorMath.IsBelowSurface(Sea, eye), $"root {root:0.000} eye {eye:0.000}");
                }
            }
        }

        [Test]
        public void Flood_StartsAsTheCarGoesUnder_FullInAboutFourSeconds()
        {
            ElevatorMath.Profile p = Site;
            float total = ElevatorMath.TravelSeconds(p);
            Assert.AreEqual(17.333f, total, 0.01f, "the ride's timing is unchanged");
            float t0 = -1f, tWet = -1f, tFull = -1f;
            for (float t = 0f; t <= total + 0.5f; t += 0.005f)
            {
                float root = -ElevatorMath.DepthAt(p, t, false);
                float level = ElevatorMath.WaterLevelInCar(Sea, root, Span);
                if (t0 < 0f && root < Sea) t0 = t;
                if (tWet < 0f && level > ElevatorMath.WaterMarginMeters) tWet = t;
                if (tFull < 0f && level >= Span - ElevatorMath.WaterMarginMeters) tFull = t;
                CarWaterFlow flow = ElevatorMath.WaterFlowInCar(ElevatorState.Descending, level, Span);
                if (tWet >= 0f && tFull < 0f) Assert.AreEqual(CarWaterFlow.Filling, flow, "pouring at t " + t);
                if (t0 < 0f) Assert.AreEqual(CarWaterFlow.Still, flow, "dry above the surface at t " + t);
            }
            Assert.AreEqual(1.5f, t0, 0.01f, "the root meets the sea 1.5 s after leaving the top");
            Assert.LessOrEqual(tWet - t0, 0.1f, "wet within 0.1 s of going under");
            Assert.That(tFull - t0, Is.InRange(3.0f, 4.5f), "full in about four seconds");
            Assert.AreEqual(3.48f, tFull - t0, 0.02f, "full 3.48 s after going under (1 m/s through the band)");
        }

        [Test]
        public void Drain_StartsAsTheRoofSurfaces_DryBeforeTheTop()
        {
            ElevatorMath.Profile p = Site;
            float total = ElevatorMath.TravelSeconds(p);
            float t0 = -1f, tDry = -1f;
            for (float t = 0f; t <= total; t += 0.005f)
            {
                float root = -ElevatorMath.DepthAt(p, t, true);
                float level = ElevatorMath.WaterLevelInCar(Sea, root, Span);
                if (t0 < 0f && root + Span > Sea) t0 = t;
                if (t0 >= 0f && tDry < 0f && level <= 0.001f) tDry = t;
                CarWaterFlow flow = ElevatorMath.WaterFlowInCar(ElevatorState.Ascending, level, Span);
                if (level > ElevatorMath.WaterMarginMeters && level < Span - ElevatorMath.WaterMarginMeters) Assert.AreEqual(CarWaterFlow.Draining, flow, "draining at t " + t);
            }
            Assert.Greater(t0, 0f);
            Assert.Greater(tDry, t0);
            Assert.AreEqual(3.5f, tDry - t0, 0.02f, "drains through the 1 m/s band");
            Assert.GreaterOrEqual(total - tDry, 1.0f, "dry at least a second before the top");
            Assert.AreEqual(1.5f, total - tDry, 0.02f, "dry 1.5 s before the top");
        }

        [Test]
        public void Flow_FollowsTheRideDirection_StillWhenDryOrFull()
        {
            Assert.AreEqual(CarWaterFlow.Filling, ElevatorMath.WaterFlowInCar(ElevatorState.Descending, 1f, Span));
            Assert.AreEqual(CarWaterFlow.Draining, ElevatorMath.WaterFlowInCar(ElevatorState.Ascending, 1f, Span));
            foreach (ElevatorState still in new[] { ElevatorState.AtTop, ElevatorState.Sealing, ElevatorState.AtBottom })
                Assert.AreEqual(CarWaterFlow.Still, ElevatorMath.WaterFlowInCar(still, 1f, Span), still.ToString());
            Assert.AreEqual(CarWaterFlow.Still, ElevatorMath.WaterFlowInCar(ElevatorState.Descending, 0f, Span), "dry");
            Assert.AreEqual(CarWaterFlow.Still, ElevatorMath.WaterFlowInCar(ElevatorState.Descending, 0.02f, Span), "at the margin");
            Assert.AreEqual(CarWaterFlow.Still, ElevatorMath.WaterFlowInCar(ElevatorState.Ascending, Span, Span), "full");
            Assert.AreEqual(CarWaterFlow.Still, ElevatorMath.WaterFlowInCar(ElevatorState.Ascending, Span - 0.01f, Span), "within the margin of full");
        }

        // docs/ELEVATOR_LOOK.md §5: lower draws first. From inside the car the far tube draws
        // first and the car's own water last; from outside, the reverse.
        [Test]
        public void Sorting_InsideIsTheReverseOfOutside()
        {
            int In(CarWaterSorting.Group g) => CarWaterSorting.OrderOf(g, true);
            int Out(CarWaterSorting.Group g) => CarWaterSorting.OrderOf(g, false);
            Assert.Less(In(CarWaterSorting.Group.TubeGlass), In(CarWaterSorting.Group.TubeWater));
            Assert.Less(In(CarWaterSorting.Group.TubeWater), In(CarWaterSorting.Group.CarGlass));
            Assert.Less(In(CarWaterSorting.Group.CarGlass), In(CarWaterSorting.Group.CarWaterSurface));
            Assert.Less(In(CarWaterSorting.Group.CarWaterSurface), In(CarWaterSorting.Group.CarWaterFX));
            Assert.Less(Out(CarWaterSorting.Group.CarWaterSurface), Out(CarWaterSorting.Group.CarWaterFX));
            Assert.Less(Out(CarWaterSorting.Group.CarWaterFX), Out(CarWaterSorting.Group.CarGlass));
            Assert.Less(Out(CarWaterSorting.Group.CarGlass), Out(CarWaterSorting.Group.TubeWater));
            Assert.Less(Out(CarWaterSorting.Group.TubeWater), Out(CarWaterSorting.Group.TubeGlass));
            for (int g = 0; g < 5; g++)
            {
                Assert.Less(CarWaterSorting.OrderOf((CarWaterSorting.Group)g, true), 0, "draws before the other transparents");
                Assert.Less(CarWaterSorting.OrderOf((CarWaterSorting.Group)g, false), 0, "draws before the other transparents");
            }
        }

        [Test]
        public void TubeRing_OnlyWhileTheCarIsAtTheSurface()
        {
            Assert.IsFalse(TubeWaterSurface.CarAtSurface(Sea, 0f, Span, 0.3f, 0.2f), "car at the top");
            Assert.IsFalse(TubeWaterSurface.CarAtSurface(Sea, -45f, Span, 0.3f, 0.2f), "car at the bottom");
            Assert.IsTrue(TubeWaterSurface.CarAtSurface(Sea, -4.3f, Span, 0.3f, 0.2f), "the underside at the surface");
            Assert.IsTrue(TubeWaterSurface.CarAtSurface(Sea, -6f, Span, 0.3f, 0.2f), "half under");
            Assert.IsTrue(TubeWaterSurface.CarAtSurface(Sea, -8.1f, Span, 0.3f, 0.2f), "the roof at the surface");
            Assert.IsFalse(TubeWaterSurface.CarAtSurface(Sea, -8.3f, Span, 0.3f, 0.2f), "the roof under");
        }

        [Test]
        public void Screen_SaysFloodingAndDraining()
        {
            Assert.AreEqual("FLOODING", CabinPanelDisplay.CarLine(ElevatorState.Descending, false, CarWaterFlow.Filling));
            Assert.AreEqual("DRAINING", CabinPanelDisplay.CarLine(ElevatorState.Ascending, true, CarWaterFlow.Draining));
            Assert.AreEqual("ON THE SEABED", CabinPanelDisplay.CarLine(ElevatorState.AtBottom, false, CarWaterFlow.Still));
            Assert.AreEqual("AT THE SURFACE", CabinPanelDisplay.CarLine(ElevatorState.AtTop, false, CarWaterFlow.Still));
        }

        // DECK-PANEL-TEXT: the drawn screen wraps a long line to short ones (at the writer's
        // wide gaps, " — " and ": " first) and keeps every word; short lines stay as they are.
        [Test]
        public void Screen_WrapsLongLinesToShortOnes()
        {
            Assert.AreEqual("DESCEND\nDay 1 of 3\nall in, press E\nto descend", CabinPanelDisplay.Layout("DESCEND\nDay 1 of 3 — all in, press E to descend", 16));
            Assert.AreEqual("DESCEND\nWaiting for:\nMate bof the", CabinPanelDisplay.Layout("DESCEND\nWaiting for: Mate bof the", 16));
            Assert.AreEqual("SURFACE\nFLOODING\nDEPTH 12 m\nWATER 40%", CabinPanelDisplay.Layout("SURFACE\nFLOODING\nDEPTH 12 m   WATER 40%", 16));
            Assert.AreEqual("SURFACE", CabinPanelDisplay.Layout("SURFACE", 16));
            string layout = CabinPanelDisplay.Layout("DESCEND\nDive in progress — 3 below: Alice, Bob, Carol", 16);
            foreach (string line in layout.Split('\n')) Assert.LessOrEqual(line.Length, 16, line);
            Assert.AreEqual("DESCEND Dive in progress 3 below: Alice, Bob, Carol", layout.Replace("\n", " "));
        }
    }
}
