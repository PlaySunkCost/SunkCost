using System.Reflection;
using NUnit.Framework;
using SunkCost.Look;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Tests
{
    // The HQ quota console's composition (27 September 2026): the board's state, hint,
    // corner and foot, the GIVE UP card and the PAY sign, and the one-line status the
    // checks read, from a ConsoleFacts struct and a QuotaView — no FishNet, no scene,
    // no Play Mode. Runs in the Test Runner (Edit Mode) and, for the editor bridge,
    // through RunAll().
    [TestFixture]
    public sealed class HQQuotaConsoleTests
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

        // Docked at HQ, nothing dived, an empty pot.
        private static ConsoleFacts Docked(int day = 0, bool payday = false, int box = 0, int sales = 0, int balance = 0, int votes = 0, int crew = 0) => new()
        {
            Phase = DayPhase.AtHQ, World = WorldId.HQ, CurrentSite = SiteId.HQ, SiteDestination = SiteId.HQ,
            Day = day, Payday = payday, BoxValue = box, CycleSales = sales, Balance = balance,
            DaysPerCycle = 3, QuotaPerCycle = 500, NotAboard = string.Empty, GiveUpVotes = votes, GiveUpCrew = crew,
        };

        private static QuotaView View(int crew = 3, LeverAction lever = LeverAction.None, bool enabled = false, string reason = "")
        {
            QuotaView v = QuotaView.Empty;
            v.Crew = crew; v.Lever = lever; v.LeverEnabled = enabled; v.LeverReason = reason ?? string.Empty;
            return v;
        }

        [Test]
        public void NewCycle_BoardAndText()
        {
            ConsoleFacts f = Docked();
            QuotaView v = View(reason: ConsoleRules.NothingToPay);
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("QUOTA BOARD", top.Title);
            Assert.AreEqual(string.Empty, top.Corner);
            Assert.AreEqual("NEW CYCLE", top.BigState);
            Assert.AreEqual(HQQuotaConsole.HintNewCycle, top.Hint);
            Assert.AreEqual("QUOTA $0 / $500", top.FootLeft); Assert.AreEqual(ConsoleTone.Warn, top.FootLeftTone);
            Assert.AreEqual("BALANCE $0", top.FootRight);
            Assert.IsNull(top.Cards);
            Assert.AreEqual("NEW CYCLE\nquota $0 / $500 · balance $0\ndive first", HQQuotaConsole.ComposeText(f, v));
        }

        [Test]
        public void Day2_CornerStateAndText_AmberQuota()
        {
            ConsoleFacts f = Docked(day: 2, box: 120, balance: 60);
            QuotaView v = View(lever: LeverAction.Pay, enabled: true);
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("DAY 2/3", top.Corner);
            Assert.AreEqual("DAY 2 OF 3", top.BigState);
            Assert.AreEqual(HQQuotaConsole.HintDay, top.Hint);
            Assert.AreEqual("QUOTA $120 / $500", top.FootLeft); Assert.AreEqual(ConsoleTone.Warn, top.FootLeftTone);
            Assert.AreEqual("BALANCE $60", top.FootRight);
            StringAssert.StartsWith("DAY 2 OF 3\nquota $120 / $500 · balance $60\nE to pay early (sells the box)", HQQuotaConsole.ComposeText(f, v));
        }

        [Test]
        public void Day2_EmptyRoom_HintSaysNothingToSell()
        {
            ConsoleFacts f = Docked(day: 2, sales: 120);
            QuotaView v = View(reason: ConsoleRules.NothingToSell);
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("DAY 2 OF 3", top.BigState);
            Assert.AreEqual(HQQuotaConsole.HintDayNothingToSell, top.Hint);
            Assert.AreEqual("QUOTA $120 / $500", top.FootLeft);
            StringAssert.StartsWith("DAY 2 OF 3\nquota $120 / $500 (handed over $120 + box $0) · balance $0", HQQuotaConsole.ComposeText(f, v));
        }

        [Test]
        public void Payday_GreenWhenMet()
        {
            ConsoleFacts f = Docked(day: 3, payday: true, box: 520, balance: 40);
            QuotaView v = View(lever: LeverAction.Pay, enabled: true);
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("PAYDAY", top.Corner);
            Assert.AreEqual("PAYDAY", top.BigState);
            Assert.AreEqual(HQQuotaConsole.HintPayday, top.Hint);
            Assert.AreEqual("QUOTA $520 / $500", top.FootLeft); Assert.AreEqual(ConsoleTone.Good, top.FootLeftTone);
            StringAssert.StartsWith("PAYDAY\nquota $520 / $500 · balance $40\nE to pay (sells the box)", HQQuotaConsole.ComposeText(f, v));
        }

        [Test]
        public void PayReports_PaidShortLost()
        {
            ConsoleFacts f = Docked(balance: 520);
            QuotaView v = View();
            v.PayShowing = true;
            v.Pay = new PayReport { Serial = 1, Sales = 520, Quota = 500, Had = 520, Balance = 520, Paid = true };
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("PAID", top.BigState); Assert.AreEqual(ConsoleTone.Good, top.BigTone);
            Assert.AreEqual("Handed over $520 · every dollar is yours", top.Hint);
            Assert.AreEqual("PAID $500\nhanded over $520 — every dollar yours · balance $520\nnext dive is day 1", HQQuotaConsole.ComposeText(f, v));

            v.Pay = new PayReport { Serial = 2, Sales = 120, Quota = 500, Had = 420, Balance = 640, Short = true };
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("SHORT BY $80", top.BigState); Assert.AreEqual(ConsoleTone.Warn, top.BigTone);
            Assert.AreEqual("Sold $120 · sail out and dive again", top.Hint);
            Assert.AreEqual("SHORT BY $80\nsold $120, handed over $420 · balance $640\nsail out and dive again", HQQuotaConsole.ComposeText(f, v));

            v.Pay = new PayReport { Serial = 3, Sales = 0, Quota = 500, Had = 420, Balance = 640, Lost = true };
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("THE RUN IS OVER", top.BigState); Assert.AreEqual(ConsoleTone.Danger, top.BigTone);
            Assert.AreEqual("Quota $500 missed · walk the plank", top.Hint); Assert.AreEqual(ConsoleTone.Danger, top.HintTone);
            Assert.AreEqual("THE RUN IS OVER\nquota $500 missed (handed over $420)\nwalk the plank", HQQuotaConsole.ComposeText(f, v));
        }

        [Test]
        public void Plank_NamesTheWalker_ThenTheWater()
        {
            ConsoleFacts f = Docked(); f.Phase = DayPhase.Plank;
            QuotaView v = View(); v.OnPlank = true; v.PlankWalker = "Dan";
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("THE RUN IS OVER", top.BigState); Assert.AreEqual(ConsoleTone.Danger, top.BigTone);
            Assert.AreEqual("Dan walks the plank", top.Hint);
            Assert.AreEqual("THE RUN IS OVER\nDan walks the plank\nthen everything from nothing", HQQuotaConsole.ComposeText(f, v));
            v.PlankWalker = string.Empty;
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual(HQQuotaConsole.HintPlankWater, top.Hint);
            Assert.AreEqual("THE RUN IS OVER\nthe last one is in the water\nthen everything from nothing", HQQuotaConsole.ComposeText(f, v));
            // The card is dark on the plank: no vote while the run is over.
            HQQuotaConsole.ComposeBottom(f, v, bottom, signs);
            Assert.IsFalse(bottom.VoteCard.Enabled);
        }

        [Test]
        public void Refusal_IsTheStateForAWhile_AndTheWholeText()
        {
            ConsoleFacts f = Docked();
            QuotaView v = View(); v.Refusal = ConsoleRules.NothingToPay;
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual(ConsoleRules.NothingToPay, top.BigState); Assert.AreEqual(ConsoleTone.Warn, top.BigTone);
            Assert.AreEqual(string.Empty, top.Hint);
            Assert.AreEqual(ConsoleRules.NothingToPay, HQQuotaConsole.ComposeText(f, v));
            // A pay report on display outranks a refusal from before it (or from its own frame).
            v.PayShowing = true; v.Pay = new PayReport { Serial = 1, Quota = 500, Had = 500, Paid = true };
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("PAID", top.BigState);
            StringAssert.StartsWith("PAID $500", HQQuotaConsole.ComposeText(f, v));
        }

        [Test]
        public void Refusal_AfterThePayReport_TakesTheScreen_ThenTheReportReturns()
        {
            // HQ-2: PAID (or SHORT BY) on display, then a second pull is refused: the reason
            // shows for its window, and the report comes back for the rest of its own.
            ConsoleFacts f = Docked(balance: 500);
            QuotaView v = View();
            v.PayShowing = true; v.Pay = new PayReport { Serial = 1, Sales = 500, Quota = 500, Had = 500, Balance = 500, Paid = true };
            v.Refusal = ConsoleRules.NothingToPay; v.RefusalAfterPay = true;
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual(ConsoleRules.NothingToPay, top.BigState); Assert.AreEqual(ConsoleTone.Warn, top.BigTone);
            Assert.AreEqual(ConsoleRules.NothingToPay, HQQuotaConsole.ComposeText(f, v));
            v.Pay = new PayReport { Serial = 2, Sales = 50, Quota = 500, Had = 450, Balance = 550, Short = true };
            v.Refusal = ConsoleRules.NothingToSell;
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual(ConsoleRules.NothingToSell, top.BigState);
            // The refusal's window over: the report again.
            v.Refusal = string.Empty;
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("SHORT BY $50", top.BigState);
            StringAssert.StartsWith("SHORT BY $50", HQQuotaConsole.ComposeText(f, v));
            // On the plank a later refusal leaves the walker on the board (the run is over either way).
            f.Phase = DayPhase.Plank;
            v.Pay = new PayReport { Serial = 3, Quota = 500, Had = 0, Lost = true };
            v.OnPlank = true; v.PlankWalker = "Dan"; v.Refusal = ConsoleRules.RunOver;
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("THE RUN IS OVER", top.BigState); Assert.AreEqual("Dan walks the plank", top.Hint);
        }

        [Test]
        public void Votes_HintAndTextCount_EveryoneMustPress()
        {
            ConsoleFacts f = Docked(day: 1, box: 50, votes: 1, crew: 3);
            QuotaView v = View(crew: 3, lever: LeverAction.Pay, enabled: true);
            HQQuotaConsole.ComposeTop(f, v, top, signs);
            Assert.AreEqual("GIVE UP 1/3 · EVERYONE MUST PRESS", top.Hint); Assert.AreEqual(ConsoleTone.Danger, top.HintTone);
            Assert.AreEqual("DAY 1 OF 3", top.BigState);
            string text = HQQuotaConsole.ComposeText(f, v);
            StringAssert.StartsWith("DAY 1 OF 3", text);
            StringAssert.Contains("\ngive up 1/3", text);
            // The plank rows' string: two of a crew of two.
            f = Docked(votes: 1, crew: 2);
            StringAssert.Contains("give up 1/2", HQQuotaConsole.ComposeText(f, View(crew: 2)));
            // A vote counted against a crew smaller than the votes never reads over 1.
            f = Docked(votes: 2, crew: 1);
            HQQuotaConsole.ComposeTop(f, View(crew: 2), top, signs);
            Assert.AreEqual("GIVE UP 2/2 · EVERYONE MUST PRESS", top.Hint);
        }

        [Test]
        public void VoteCard_CountFootVotedEnabled()
        {
            ConsoleFacts f = Docked(votes: 0);
            QuotaView v = View(crew: 3);
            HQQuotaConsole.ComposeBottom(f, v, bottom, signs);
            Assert.AreEqual("GIVE UP", bottom.VoteCard.Word);
            Assert.AreEqual("0 / 3", bottom.VoteCard.Count);
            Assert.AreEqual("EVERYONE MUST AGREE", bottom.VoteCard.Foot);
            Assert.IsFalse(bottom.VoteCard.LocalVoted); Assert.IsTrue(bottom.VoteCard.Enabled);
            Assert.AreEqual(string.Empty, bottom.Heading); Assert.IsNull(bottom.Lines); Assert.IsFalse(bottom.Notice);

            f = Docked(votes: 1, crew: 3);
            v.LocalVoted = true; v.CardAimed = true;
            HQQuotaConsole.ComposeBottom(f, v, bottom, signs);
            Assert.AreEqual("1 / 3", bottom.VoteCard.Count);
            Assert.AreEqual("YOU VOTED · E TO TAKE BACK", bottom.VoteCard.Foot);
            Assert.IsTrue(bottom.VoteCard.LocalVoted); Assert.IsTrue(bottom.VoteCard.Aimed);

            // Not docked: the card is dark (ServerToggleGiveUp would refuse).
            f = Docked(); f.Phase = DayPhase.AtSea; f.World = WorldId.Sea;
            HQQuotaConsole.ComposeBottom(f, v, bottom, signs);
            Assert.IsFalse(bottom.VoteCard.Enabled);
            f = Docked(); f.Travelling = true;
            HQQuotaConsole.ComposeBottom(f, v, bottom, signs);
            Assert.IsFalse(bottom.VoteCard.Enabled);
        }

        [Test]
        public void Sign_PayLitOrDimKeepsTheWord()
        {
            SignModel lit = HQQuotaConsole.ComposeSign(View(lever: LeverAction.Pay, enabled: true), signs);
            Assert.AreEqual("PAY", lit.Word); Assert.IsTrue(lit.Enabled); Assert.AreEqual(ConsoleTone.Good, lit.Tone);
            QuotaView dim = View(reason: ConsoleRules.NothingToPay); dim.LeverAimed = true;
            SignModel off = HQQuotaConsole.ComposeSign(dim, signs);
            Assert.AreEqual("PAY", off.Word); Assert.IsFalse(off.Enabled); Assert.AreEqual(ConsoleTone.Dim, off.Tone); Assert.IsTrue(off.Aimed);
        }

        [Test]
        public void QuotaTone_AmberUntilMet()
        {
            Assert.AreEqual(ConsoleTone.Warn, HQQuotaConsole.QuotaTone(0, 500));
            Assert.AreEqual(ConsoleTone.Warn, HQQuotaConsole.QuotaTone(499, 500));
            Assert.AreEqual(ConsoleTone.Good, HQQuotaConsole.QuotaTone(500, 500));
            Assert.AreEqual(ConsoleTone.Good, HQQuotaConsole.QuotaTone(900, 500));
            Assert.AreEqual(ConsoleTone.Warn, HQQuotaConsole.QuotaTone(0, 0));
        }

        // For the editor bridge: `run SunkCost.Editor.Tests.HQQuotaConsoleTests.RunAll`.
        public static string RunAll()
        {
            var fixture = new HQQuotaConsoleTests();
            int passed = 0, failed = 0;
            var report = new System.Text.StringBuilder();
            foreach (MethodInfo method in typeof(HQQuotaConsoleTests).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<TestAttribute>() == null) continue;
                fixture.SetUp();
                try { method.Invoke(fixture, null); passed++; }
                catch (TargetInvocationException e) { failed++; report.Append("FAIL ").Append(method.Name).Append(": ").Append((e.InnerException ?? e).Message.Trim()).Append('\n'); }
                finally { fixture.TearDown(); }
            }
            report.Insert(0, $"HQQuotaConsoleTests: passed {passed}/{passed + failed}\n");
            return report.ToString().TrimEnd();
        }
    }
}
