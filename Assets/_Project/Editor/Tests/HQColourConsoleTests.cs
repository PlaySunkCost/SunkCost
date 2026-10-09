using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SunkCost.Look;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Tests
{
    // The crew colour console's composition (Dan, 8 October 2026): the palette's names,
    // the top screen (title, the shown colour's name, SWATCH n), the lower screen's three
    // states (the pull line, <NAME> IS NOW <COLOUR>, a refusal notice), the TAKE sign,
    // the rule table's answer for the Colour lever, and the painter's ring layout (sixteen
    // hit boxes, index 0 at the top, clockwise, none outside the screen, none overlapping)
    // — no FishNet, no scene, no Play Mode. Runs in the Test Runner (Edit Mode) and, for
    // the editor bridge, through RunAll().
    [TestFixture]
    public sealed class HQColourConsoleTests
    {
        private HQSigns signs;
        private TopModel top;
        private BottomModel bottom;

        [SetUp]
        public void SetUp()
        {
            signs = ScriptableObject.CreateInstance<HQSigns>();
            signs.hideFlags = HideFlags.HideAndDontSave;
            signs.EnsureDefaults();
            top = new TopModel();
            bottom = new BottomModel();
        }

        [TearDown]
        public void TearDown()
        {
            if (signs != null) Object.DestroyImmediate(signs);
        }

        private static ColourTake Take(int serial, int taker, int swatch) => new() { Serial = serial, Taker = taker, Swatch = (byte)swatch, Tick = 0 };

        [Test]
        public void Names_SixteenUpperDistinct()
        {
            Assert.AreEqual(PlayerPalette.Count, PlayerPalette.Names.Length);
            Assert.AreEqual(16, PlayerPalette.Names.Length);
            foreach (string n in PlayerPalette.Names)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(n));
                Assert.AreEqual(n.ToUpperInvariant(), n, n + " is not upper case");
            }
            Assert.AreEqual(16, PlayerPalette.Names.Distinct().Count());
            Assert.AreEqual("RED", PlayerPalette.Name(0));
            Assert.AreEqual("WHITE", PlayerPalette.Name(15));
            Assert.AreEqual("RED", PlayerPalette.Name(-1));
            Assert.AreEqual("RED", PlayerPalette.Name(99));
        }

        [Test]
        public void ComposeTop_RedAndWhite()
        {
            HQColourConsole.ComposeTop(0, top, signs);
            Assert.AreEqual("CHOOSE YOUR CREW COLOUR", top.Title);
            Assert.AreEqual("RED", top.BigState);
            Assert.AreEqual("SWATCH 0", top.Hint);
            Assert.IsNull(top.Cards);
            Assert.AreEqual("CHOOSE YOUR CREW COLOUR ·  ·  · RED · SWATCH 0 ·  · BLACK TIDE", ConsoleModels.Flatten(top));
            HQColourConsole.ComposeTop(15, top, signs);
            Assert.AreEqual("CHOOSE YOUR CREW COLOUR ·  ·  · WHITE · SWATCH 15 ·  · BLACK TIDE", ConsoleModels.Flatten(top));
        }

        [Test]
        public void ComposeBottom_PullLine()
        {
            HQColourConsole.ComposeBottom(0, Take(0, 0, 0), false, string.Empty, string.Empty, bottom, signs);
            Assert.AreEqual("RED", bottom.Heading);
            Assert.AreEqual("PULL THE LEVER TO TAKE IT", bottom.Status.Text);
            Assert.IsFalse(bottom.Notice);
            Assert.IsNull(bottom.VoteCard);
            Assert.AreEqual("RED ·  ·  ·  · PULL THE LEVER TO TAKE IT", ConsoleModels.Flatten(bottom));
        }

        [Test]
        public void ComposeBottom_TakenNamesTheTakerUpperCased()
        {
            HQColourConsole.ComposeBottom(9, Take(1, 3, 9), true, "dan", string.Empty, bottom, signs);
            Assert.AreEqual("BLUE", bottom.Heading);
            Assert.AreEqual("DAN IS NOW BLUE", bottom.Status.Text);
            Assert.IsFalse(bottom.Notice);
            Assert.AreEqual("BLUE ·  ·  ·  · DAN IS NOW BLUE", ConsoleModels.Flatten(bottom));
            // The line names the TAKEN swatch, even if the shown one moved on since.
            HQColourConsole.ComposeBottom(2, Take(2, 3, 9), true, "Skipper", string.Empty, bottom, signs);
            Assert.AreEqual("AMBER", bottom.Heading);
            Assert.AreEqual("SKIPPER IS NOW BLUE", bottom.Status.Text);
        }

        [Test]
        public void ComposeBottom_RefusalIsANotice()
        {
            HQColourConsole.ComposeBottom(4, Take(1, 0, 4), true, "dan", ConsoleRules.NotAtConsole, bottom, signs);
            Assert.AreEqual("LIME", bottom.Heading);
            Assert.AreEqual(ConsoleRules.NotAtConsole, bottom.Status.Text);
            Assert.IsTrue(bottom.Notice);
            Assert.AreEqual(ConsoleTone.Warn, bottom.Status.Tone);
        }

        [Test]
        public void ComposeSign_TakeOn()
        {
            SignModel idle = HQColourConsole.ComposeSign(false, signs);
            Assert.AreEqual("TAKE", idle.Word); Assert.IsTrue(idle.Enabled); Assert.IsFalse(idle.Aimed); Assert.AreEqual(ConsoleTone.Good, idle.Tone);
            Assert.AreEqual("TAKE/on", ConsoleModels.Flatten(idle));
            SignModel aimed = HQColourConsole.ComposeSign(true, signs);
            Assert.IsTrue(aimed.Aimed);
        }

        [Test]
        public void Rules_ColourLeverAlwaysTakes()
        {
            LeverAction action = ConsoleRules.Expected(ConsoleKind.Colour, null, out SiteId target, out bool enabled, out string reason);
            Assert.AreEqual(LeverAction.TakeColour, action);
            Assert.AreEqual(SiteId.None, target);
            Assert.IsTrue(enabled);
            Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual(LeverAction.TakeColour, ConsoleRules.Expected(ConsoleKind.Colour, null, out _));
            SignModel sign = ConsoleRules.Sign(LeverAction.TakeColour, true, 0, false, signs.Get("lever.take"));
            Assert.AreEqual("TAKE", sign.Word);
        }

        [Test]
        public void Ring_SixteenRectsClockwiseFromTheTop()
        {
            const int w = 1920, h = 1024;
            Rect[] rects = ColourConsolePaint.SwatchRects(w, h);
            Assert.AreEqual(16, rects.Length);
            Rect screen = new(0, 0, w, h);
            for (int i = 0; i < rects.Length; i++)
            {
                Assert.IsTrue(rects[i].xMin >= 0 && rects[i].yMin >= 0 && rects[i].xMax <= w && rects[i].yMax <= h, $"rect {i} {rects[i]} leaves the screen {screen}");
                for (int j = i + 1; j < rects.Length; j++)
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), $"rects {i} and {j} overlap: {rects[i]} / {rects[j]}");
            }
            // Index 0 at the top (centre (960, 251)), 4 at the right (1370, 589): clockwise, PlayerPalette.WheelPosition's order
            // with y flipped for the painter, on an ellipse wider than it is tall (0.40 h by 0.33 h, the picture's ring).
            Assert.AreEqual(960f, rects[0].center.x, 1f);
            Assert.AreEqual(251f, rects[0].center.y, 1f);
            Assert.AreEqual(1370f, rects[4].center.x, 1f);
            Assert.AreEqual(589f, rects[4].center.y, 1f);
            Assert.Greater(rects[1].center.x, rects[0].center.x, "index 1 is to the right of index 0");
            Assert.Greater(rects[1].center.y, rects[0].center.y, "index 1 is below index 0 (y down)");
            Vector2 ring = ColourConsolePaint.RingCentre(w, h);
            float radiusY = ColourConsolePaint.RingRadius(h), radiusX = ColourConsolePaint.RingRadiusX(h);
            Assert.Greater(radiusX, radiusY, "the ring is wider than it is tall");
            for (int i = 0; i < rects.Length; i++)
            {
                Vector2 wheel = PlayerPalette.WheelPosition(i);
                Vector2 expected = ring + new Vector2(wheel.x * radiusX, -wheel.y * radiusY);
                Assert.AreEqual(expected.x, rects[i].center.x, 1f, $"rect {i} x");
                Assert.AreEqual(expected.y, rects[i].center.y, 1f, $"rect {i} y");
                Assert.AreEqual(rects[i].width, rects[i].height, 0.01f, $"rect {i} is square");
                Assert.AreEqual(ColourConsolePaint.HitBoxSides * ColourConsolePaint.SwatchRadius(h), rects[i].width, 0.5f, $"rect {i} side");
            }
        }

        [Test]
        public void Keys_ChangeWithTheState()
        {
            string a = ColourConsolePaint.TopKey(new ColourConsolePaint.TopView { Title = "T", Shown = 0, Aimed = -1 });
            string b = ColourConsolePaint.TopKey(new ColourConsolePaint.TopView { Title = "T", Shown = 1, Aimed = -1 });
            string c = ColourConsolePaint.TopKey(new ColourConsolePaint.TopView { Title = "T", Shown = 1, Aimed = 3 });
            Assert.AreNotEqual(a, b); Assert.AreNotEqual(b, c);
            string d = ColourConsolePaint.BottomKey(new ColourConsolePaint.BottomView { Name = "RED", Swatch = 0, Line = "x", Taken = false, Notice = false });
            string e = ColourConsolePaint.BottomKey(new ColourConsolePaint.BottomView { Name = "RED", Swatch = 0, Line = "x", Taken = true, Notice = false });
            Assert.AreNotEqual(d, e);
        }

        // For the editor bridge: `run SunkCost.Editor.Tests.HQColourConsoleTests.RunAll`.
        public static string RunAll()
        {
            var fixture = new HQColourConsoleTests();
            int passed = 0, failed = 0;
            var report = new System.Text.StringBuilder();
            foreach (MethodInfo method in typeof(HQColourConsoleTests).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<TestAttribute>() == null) continue;
                fixture.SetUp();
                try { method.Invoke(fixture, null); passed++; }
                catch (TargetInvocationException e) { failed++; report.Append("FAIL ").Append(method.Name).Append(": ").Append((e.InnerException ?? e).Message.Trim()).Append('\n'); }
                finally { fixture.TearDown(); }
            }
            report.Insert(0, $"HQColourConsoleTests: passed {passed}/{passed + failed}\n");
            return report.ToString().TrimEnd();
        }
    }
}
