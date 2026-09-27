using System.Reflection;
using NUnit.Framework;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Tests
{
    // The shared console's pure rules (27 September 2026): the lever table the server
    // resolves a pull with and every client draws the sign from, the destination
    // helpers, the catalogue's defaults, the sign's words and the bottom screen's
    // status line. No FishNet, no scene, no Play Mode: a ConsoleFacts struct in, an
    // action out. Runs in the Test Runner (Edit Mode; this folder compiles into
    // Assembly-CSharp-Editor, which references nunit) and, for the editor bridge,
    // through RunAll().
    [TestFixture]
    public sealed class ConsoleRulesTests
    {
        private SiteCatalog sites;

        [SetUp]
        public void SetUp()
        {
            sites = ScriptableObject.CreateInstance<SiteCatalog>();
            sites.hideFlags = HideFlags.HideAndDontSave;
            sites.EnsureDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            if (sites != null) Object.DestroyImmediate(sites);
        }

        // Docked at HQ, day 0, nobody missing.
        private static ConsoleFacts Docked(SiteId selected, int balance = 0, int unlocked = 0) => new()
        {
            Phase = DayPhase.AtHQ, World = WorldId.HQ, CurrentSite = SiteId.HQ, SiteDestination = SiteId.HQ,
            Selected = selected, Balance = balance, UnlockedMask = unlocked, DaysPerCycle = 3, QuotaPerCycle = 500, NotAboard = string.Empty,
        };

        // At Site 01, day 1, nobody missing.
        private static ConsoleFacts AtSea(SiteId selected, int balance = 0, int unlocked = 0) => new()
        {
            Phase = DayPhase.AtSea, World = WorldId.Sea, CurrentSite = SiteId.Site01, SiteDestination = SiteId.Site01, Day = 1,
            Selected = selected, Balance = balance, UnlockedMask = unlocked, DaysPerCycle = 3, QuotaPerCycle = 500, NotAboard = string.Empty,
        };

        private LeverAction Ship(in ConsoleFacts f, out SiteId target, out bool enabled, out string reason) => ConsoleRules.ShipLever(f, sites, out target, out enabled, out reason);

        // ---- the ship lever, row by row (BRIEF "SHIP LEVER") -----------------------

        [Test]
        public void NothingSelected_IsDim_SelectFirst()
        {
            Assert.AreEqual(LeverAction.None, Ship(Docked(SiteId.None), out SiteId target, out bool enabled, out string reason));
            Assert.AreEqual(SiteId.None, target); Assert.IsFalse(enabled); Assert.AreEqual(ConsoleRules.SelectFirst, reason);
        }

        [Test]
        public void Plank_IsRunOver()
        {
            ConsoleFacts f = Docked(SiteId.Site01); f.Phase = DayPhase.Plank;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out bool enabled, out string reason));
            Assert.IsFalse(enabled); Assert.AreEqual(ConsoleRules.RunOver, reason);
        }

        [Test]
        public void LockedSite_ReadsUnlock_EnabledByBalance()
        {
            Assert.AreEqual(LeverAction.Unlock, Ship(Docked(SiteId.Site02, balance: 40), out SiteId target, out bool enabled, out string reason));
            Assert.AreEqual(SiteId.Site02, target); Assert.IsFalse(enabled); Assert.AreEqual("$60 short", reason);
            Assert.AreEqual(LeverAction.Unlock, Ship(Docked(SiteId.Site02, balance: 100), out target, out enabled, out reason));
            Assert.IsTrue(enabled); Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual(LeverAction.Unlock, Ship(Docked(SiteId.Site04, balance: 250), out target, out enabled, out reason));
            Assert.AreEqual(SiteId.Site04, target); Assert.IsFalse(enabled); Assert.AreEqual("$50 short", reason);
        }

        [Test]
        public void LockedSite_AfterDive_StillReadsUnlock()
        {
            ConsoleFacts f = AtSea(SiteId.Site03, balance: 200); f.DiveDone = true;
            Assert.AreEqual(LeverAction.Unlock, Ship(f, out SiteId target, out bool enabled, out _));
            Assert.AreEqual(SiteId.Site03, target); Assert.IsTrue(enabled);
        }

        [Test]
        public void LockedSite_WhileTravelling_StillReadsUnlock()
        {
            ConsoleFacts f = AtSea(SiteId.Site02, balance: 100); f.Travelling = true; f.Phase = DayPhase.SailingHome;
            Assert.AreEqual(LeverAction.Unlock, Ship(f, out _, out bool enabled, out _));
            Assert.IsTrue(enabled);
        }

        [Test]
        public void DiveDone_ReadsEndDay()
        {
            ConsoleFacts f = AtSea(SiteId.HQ); f.DiveDone = true;
            Assert.AreEqual(LeverAction.EndDay, Ship(f, out SiteId target, out bool enabled, out string reason));
            Assert.AreEqual(SiteId.None, target); Assert.IsTrue(enabled); Assert.AreEqual(string.Empty, reason);
            f.Selected = SiteId.Site01;
            Assert.AreEqual(LeverAction.EndDay, Ship(f, out _, out enabled, out _)); Assert.IsTrue(enabled);
        }

        [Test]
        public void Travelling_DiveInProgress_Below_Riding_AreDim()
        {
            ConsoleFacts f = AtSea(SiteId.HQ); f.Travelling = true;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out string reason)); Assert.AreEqual(ConsoleRules.Travelling, reason);
            f = AtSea(SiteId.HQ); f.Phase = DayPhase.Sailing;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out reason)); Assert.AreEqual(ConsoleRules.Travelling, reason);
            f = AtSea(SiteId.HQ); f.Phase = DayPhase.DiveInProgress; f.BelowCount = 2;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out reason)); Assert.AreEqual(ConsoleRules.DiveInProgress, reason);
            f = AtSea(SiteId.HQ); f.BelowCount = 1;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out reason)); Assert.AreEqual(ConsoleRules.DiversBelow, reason);
            f = AtSea(SiteId.HQ); f.CabinAway = true;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out reason)); Assert.AreEqual(ConsoleRules.DiversBelow, reason);
            f = AtSea(SiteId.HQ); f.Riding = true;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out reason)); Assert.AreEqual(ConsoleRules.CabinInUse, reason);
        }

        [Test]
        public void Payday_OnlyHQ()
        {
            ConsoleFacts f = AtSea(SiteId.Site02, unlocked: Destinations.Bit(SiteId.Site02)); f.Payday = true; f.Day = 3;
            Assert.AreEqual(LeverAction.None, Ship(f, out _, out _, out string reason)); Assert.AreEqual(ConsoleRules.PaydayOnlyHQ, reason);
            f.Selected = SiteId.HQ;
            Assert.AreEqual(LeverAction.Confirm, Ship(f, out SiteId target, out bool enabled, out _)); Assert.AreEqual(SiteId.HQ, target); Assert.IsTrue(enabled);
            ConsoleFacts docked = Docked(SiteId.Site01); docked.Payday = true; docked.Day = 3;
            Assert.AreEqual(LeverAction.None, Ship(docked, out _, out _, out reason)); Assert.AreEqual(ConsoleRules.PayQuotaFirst, reason);
        }

        [Test]
        public void AlreadyHere_IsDim()
        {
            Assert.AreEqual(LeverAction.None, Ship(Docked(SiteId.HQ), out _, out _, out string reason)); Assert.AreEqual(ConsoleRules.AlreadyHere, reason);
            Assert.AreEqual(LeverAction.None, Ship(AtSea(SiteId.Site01), out _, out _, out reason)); Assert.AreEqual(ConsoleRules.AlreadyHere, reason);
        }

        [Test]
        public void AtSea_AnotherSite_SailHomeFirst()
        {
            // Until sites have their own worlds (MAP §3): an open other site at sea is refused.
            Assert.AreEqual(LeverAction.None, Ship(AtSea(SiteId.Site02, unlocked: Destinations.Bit(SiteId.Site02)), out _, out _, out string reason));
            Assert.AreEqual(ConsoleRules.SailHomeFirst, reason);
            // A locked one still reads UNLOCK (bought at sea too).
            Assert.AreEqual(LeverAction.Unlock, Ship(AtSea(SiteId.Site02, balance: 100), out _, out bool enabled, out _)); Assert.IsTrue(enabled);
        }

        [Test]
        public void NotAboard_ConfirmDisabled_NamesThem()
        {
            ConsoleFacts f = Docked(SiteId.Site01); f.NotAboard = "Dan, Idan";
            Assert.AreEqual(LeverAction.Confirm, Ship(f, out SiteId target, out bool enabled, out string reason));
            Assert.AreEqual(SiteId.Site01, target); Assert.IsFalse(enabled); Assert.AreEqual("Not aboard: Dan, Idan", reason);
        }

        [Test]
        public void OpenDestination_Confirm()
        {
            Assert.AreEqual(LeverAction.Confirm, Ship(Docked(SiteId.Site01), out SiteId target, out bool enabled, out string reason));
            Assert.AreEqual(SiteId.Site01, target); Assert.IsTrue(enabled); Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual(LeverAction.Confirm, Ship(Docked(SiteId.Site03, unlocked: Destinations.Bit(SiteId.Site03)), out target, out enabled, out _));
            Assert.AreEqual(SiteId.Site03, target); Assert.IsTrue(enabled);
            Assert.AreEqual(LeverAction.Confirm, Ship(AtSea(SiteId.HQ), out target, out enabled, out _));
            Assert.AreEqual(SiteId.HQ, target); Assert.IsTrue(enabled);
        }

        // ---- the HQ lever ----------------------------------------------------------

        [Test]
        public void HQLever_Table()
        {
            ConsoleFacts f = Docked(SiteId.None); f.Phase = DayPhase.Plank;
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(f, out bool enabled, out string reason)); Assert.AreEqual(ConsoleRules.RunOver, reason);
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(AtSea(SiteId.None), out _, out reason)); Assert.AreEqual(ConsoleRules.NotDocked, reason);
            f = Docked(SiteId.None); f.Travelling = true;
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(f, out _, out reason)); Assert.AreEqual(ConsoleRules.NotDocked, reason);
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(Docked(SiteId.None), out enabled, out reason)); Assert.IsFalse(enabled); Assert.AreEqual(ConsoleRules.NothingToPay, reason);
            f = Docked(SiteId.None); f.Day = 1; f.BoxValue = 50;
            Assert.AreEqual(LeverAction.Pay, ConsoleRules.HQLever(f, out enabled, out reason)); Assert.IsTrue(enabled); Assert.AreEqual(string.Empty, reason);
            f = Docked(SiteId.None); f.Day = 3; f.Payday = true; f.BoxValue = 50;
            Assert.AreEqual(LeverAction.Pay, ConsoleRules.HQLever(f, out enabled, out _)); Assert.IsTrue(enabled);
        }

        // An empty room before payday: nothing to hand over, so PAY is dim on every peer
        // and a second PAY after a short sale is refused (no empty sale, no second report).
        [Test]
        public void HQLever_EmptyRoom_NothingToSell_ExceptOnPayday()
        {
            ConsoleFacts f = Docked(SiteId.None); f.Day = 1; f.BoxValue = 0;
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(f, out bool enabled, out string reason)); Assert.IsFalse(enabled); Assert.AreEqual(ConsoleRules.NothingToSell, reason);
            f = Docked(SiteId.None); f.Day = 2; f.CycleSales = 120; f.BoxValue = 0;  // right after a short sale
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(f, out enabled, out reason)); Assert.IsFalse(enabled); Assert.AreEqual(ConsoleRules.NothingToSell, reason);
            f.BoxValue = 1;                                                            // one item in the room again
            Assert.AreEqual(LeverAction.Pay, ConsoleRules.HQLever(f, out enabled, out reason)); Assert.IsTrue(enabled); Assert.AreEqual(string.Empty, reason);
            f = Docked(SiteId.None); f.Day = 3; f.Payday = true; f.BoxValue = 0;      // payday judges the empty room (the plank)
            Assert.AreEqual(LeverAction.Pay, ConsoleRules.HQLever(f, out enabled, out reason)); Assert.IsTrue(enabled); Assert.AreEqual(string.Empty, reason);
            f = Docked(SiteId.None); f.Day = 0; f.BoxValue = 80;                       // nothing dived yet: the older row still wins
            Assert.AreEqual(LeverAction.None, ConsoleRules.HQLever(f, out enabled, out reason)); Assert.AreEqual(ConsoleRules.NothingToPay, reason);
        }

        // ---- destinations ----------------------------------------------------------

        [Test]
        public void Destinations_BitsAndOpenness()
        {
            Assert.AreEqual(5, Destinations.Cards.Length);
            Assert.IsTrue(Destinations.IsOpen(SiteId.HQ, 0)); Assert.IsTrue(Destinations.IsOpen(SiteId.Site01, 0));
            Assert.IsFalse(Destinations.IsOpen(SiteId.Site02, 0)); Assert.IsFalse(Destinations.IsOpen(SiteId.None, 0));
            Assert.IsTrue(Destinations.IsOpen(SiteId.Site02, Destinations.Bit(SiteId.Site02)));
            Assert.IsFalse(Destinations.IsOpen(SiteId.Site03, Destinations.Bit(SiteId.Site02)));
            Assert.AreNotEqual(Destinations.Bit(SiteId.Site02), Destinations.Bit(SiteId.Site03));
            Assert.IsTrue(Destinations.IsPurchasable(SiteId.Site02)); Assert.IsFalse(Destinations.IsPurchasable(SiteId.Site01)); Assert.IsFalse(Destinations.IsPurchasable(SiteId.HQ));
            Assert.IsTrue(Destinations.IsSite(SiteId.Site01)); Assert.IsFalse(Destinations.IsSite(SiteId.HQ));
            Assert.IsTrue(Destinations.IsCard(SiteId.HQ)); Assert.IsTrue(Destinations.IsCard(SiteId.Site04)); Assert.IsFalse(Destinations.IsCard(SiteId.None));
            Assert.AreEqual(WorldId.HQ, Destinations.WorldOf(SiteId.HQ)); Assert.AreEqual(WorldId.Sea, Destinations.WorldOf(SiteId.Site03));
            Assert.AreEqual(SiteId.HQ, Destinations.SiteOf(WorldId.HQ)); Assert.AreEqual(SiteId.Site01, Destinations.SiteOf(WorldId.Sea));
        }

        [Test]
        public void Destinations_TextAndParsing()
        {
            Assert.AreEqual("none", Destinations.MaskText(0));
            Assert.AreEqual("Site02+Site04", Destinations.MaskText(Destinations.Bit(SiteId.Site04) | Destinations.Bit(SiteId.Site02)));
            Assert.AreEqual("SITE 02", Destinations.Label(SiteId.Site02)); Assert.AreEqual("HQ", Destinations.Label(SiteId.HQ)); Assert.AreEqual(string.Empty, Destinations.Label(SiteId.None));
            Assert.AreEqual("Site 01", Destinations.TitleCase(SiteId.Site01));
            foreach (string text in new[] { "Site02", "SITE 02", "site02", "site 2", "3", "Site_02" })
            {
                Assert.IsTrue(Destinations.TryParse(text, out SiteId id), text); Assert.AreEqual(SiteId.Site02, id, text);
            }
            Assert.IsTrue(Destinations.TryParse("hq", out SiteId hq)); Assert.AreEqual(SiteId.HQ, hq);
            Assert.IsTrue(Destinations.TryParse("None", out SiteId none)); Assert.AreEqual(SiteId.None, none);
            Assert.IsFalse(Destinations.TryParse("Basketball", out _)); Assert.IsFalse(Destinations.TryParse("", out _)); Assert.IsFalse(Destinations.TryParse(null, out _));
        }

        // ---- the catalogue's defaults ---------------------------------------------

        [Test]
        public void SiteCatalog_Defaults()
        {
            Assert.AreEqual(5, sites.Entries.Count);
            Assert.AreEqual(100, sites.UnlockPrice(SiteId.Site02)); Assert.AreEqual(200, sites.UnlockPrice(SiteId.Site03)); Assert.AreEqual(300, sites.UnlockPrice(SiteId.Site04));
            Assert.AreEqual(0, sites.UnlockPrice(SiteId.HQ)); Assert.AreEqual(0, sites.UnlockPrice(SiteId.Site01));
            Assert.AreEqual(SiteId.Site01, sites.RouteOf(SiteId.Site02)); Assert.AreEqual(SiteId.Site01, sites.RouteOf(SiteId.Site04));
            Assert.AreEqual(SiteId.Site01, sites.RouteOf(SiteId.Site01)); Assert.AreEqual(SiteId.HQ, sites.RouteOf(SiteId.HQ));
            Assert.AreEqual("SITE 02", sites.NameOf(SiteId.Site02)); Assert.AreEqual("HQ", sites.NameOf(SiteId.HQ));
            Assert.AreEqual("SITE 03 · uncharted", sites.Find(SiteId.Site03).Description[0]);
            Assert.AreEqual(0, sites.Find(SiteId.Site01).Description.Length);
            Assert.IsFalse(sites.EnsureDefaults(), "a second EnsureDefaults adds nothing");
            sites.Find(SiteId.Site02).UnlockPrice = 150;
            Assert.AreEqual(150, sites.UnlockPrice(SiteId.Site02), "Dan's value wins");
            Assert.AreEqual(LeverAction.Unlock, Ship(Docked(SiteId.Site02, balance: 120), out _, out bool enabled, out string reason));
            Assert.IsFalse(enabled); Assert.AreEqual("$30 short", reason);
        }

        // ---- the sign and the status line -----------------------------------------

        [Test]
        public void Sign_WordsAndTones()
        {
            SignModel sign = ConsoleRules.Sign(LeverAction.Confirm, true, 0, false);
            Assert.AreEqual("CONFIRM", sign.Word); Assert.AreEqual(ConsoleTone.Accent, sign.Tone); Assert.IsTrue(sign.Enabled);
            sign = ConsoleRules.Sign(LeverAction.Unlock, true, 100, false);
            Assert.AreEqual("UNLOCK $100", sign.Word); Assert.AreEqual(ConsoleTone.Warn, sign.Tone); Assert.IsTrue(sign.Enabled);
            sign = ConsoleRules.Sign(LeverAction.EndDay, true, 0, false);
            Assert.AreEqual("END DAY", sign.Word); Assert.AreEqual(ConsoleTone.Danger, sign.Tone);
            sign = ConsoleRules.Sign(LeverAction.Pay, true, 0, true);
            Assert.AreEqual("PAY", sign.Word); Assert.AreEqual(ConsoleTone.Good, sign.Tone); Assert.IsTrue(sign.Aimed);
            sign = ConsoleRules.Sign(LeverAction.Confirm, false, 0, false);
            Assert.AreEqual("CONFIRM", sign.Word); Assert.AreEqual(ConsoleTone.Dim, sign.Tone); Assert.IsFalse(sign.Enabled);
            sign = ConsoleRules.Sign(LeverAction.None, true, 0, false, "END DAY");
            Assert.AreEqual("END DAY", sign.Word); Assert.AreEqual(ConsoleTone.Dim, sign.Tone); Assert.IsFalse(sign.Enabled, "None is never lit");
            sign = ConsoleRules.Sign(LeverAction.None, false, 0, false);
            Assert.AreEqual(string.Empty, sign.Word);
        }

        [Test]
        public void ShipStatus_Lines()
        {
            ConsoleFacts f = Docked(SiteId.Site01);
            Assert.AreEqual("READY", ConsoleRules.ShipStatus(f, LeverAction.Confirm, true, string.Empty).Text);
            f.NotAboard = "Dan";
            Assert.AreEqual("WAITING FOR DAN TO BOARD", ConsoleRules.ShipStatus(f, LeverAction.Confirm, false, ConsoleRules.NotAboard("Dan")).Text);
            Assert.AreEqual("PULL TO UNLOCK", ConsoleRules.ShipStatus(f, LeverAction.Unlock, true, string.Empty).Text);
            Assert.AreEqual("$40 SHORT", ConsoleRules.ShipStatus(f, LeverAction.Unlock, false, ConsoleRules.ShortBy(40)).Text);
            Assert.AreEqual("DIVE DONE — END THE DAY FIRST", ConsoleRules.ShipStatus(f, LeverAction.EndDay, true, string.Empty).Text);
            Assert.AreEqual("PAYDAY — ONLY HQ", ConsoleRules.ShipStatus(f, LeverAction.None, false, ConsoleRules.PaydayOnlyHQ).Text);
            Assert.AreEqual(string.Empty, ConsoleRules.ShipStatus(f, LeverAction.None, false, ConsoleRules.SelectFirst).Text);
            f.SiteDestination = SiteId.Site02;
            Assert.AreEqual("SAILING TO SITE 02", ConsoleRules.ShipStatus(f, LeverAction.None, false, ConsoleRules.Travelling).Text);
            f.SiteDestination = SiteId.HQ;
            Assert.AreEqual("SAILING HOME", ConsoleRules.ShipStatus(f, LeverAction.None, false, ConsoleRules.Travelling).Text);
            f.BelowCount = 2;
            Assert.AreEqual("2 BELOW", ConsoleRules.ShipStatus(f, LeverAction.None, false, ConsoleRules.DiveInProgress).Text);
            Assert.AreEqual("THE RUN IS OVER", ConsoleRules.ShipStatus(f, LeverAction.None, false, ConsoleRules.RunOver).Text);
        }

        // ---- the same table from the bridge (no Test Runner needed) ----------------

        // Runs every [Test] of this fixture in order and reports "passed n/n" or the
        // failures, so a RunCommand / `run` in Temp/editor-command.txt can prove the
        // rules without the Test Runner window.
        public static string RunAll()
        {
            var fixture = new ConsoleRulesTests();
            int passed = 0, failed = 0;
            var report = new System.Text.StringBuilder();
            foreach (MethodInfo method in typeof(ConsoleRulesTests).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<TestAttribute>() == null) continue;
                fixture.SetUp();
                try { method.Invoke(fixture, null); passed++; }
                catch (TargetInvocationException e) { failed++; report.Append("FAIL ").Append(method.Name).Append(": ").Append((e.InnerException ?? e).Message.Trim()).Append('\n'); }
                finally { fixture.TearDown(); }
            }
            report.Insert(0, $"ConsoleRulesTests: passed {passed}/{passed + failed}\n");
            return report.ToString().TrimEnd();
        }
    }
}
