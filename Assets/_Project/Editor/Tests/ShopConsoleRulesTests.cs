using System.Reflection;
using NUnit.Framework;
using SunkCost.Look;
using SunkCost.Shop;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Tests
{
    // The shop console's pure tables (Dan, 8 October 2026): the paging arithmetic, a row's
    // state and line, the page's rows text, the lever's rule and the page text in
    // ShopConsoleRules, and HQShopConsole's composition of the top screen, the lower
    // poster and the sign — from a catalogue built in memory, no FishNet, no scene, no
    // Play Mode. The OWNED state needs a PlayerUpgrades with a server-written bit, which
    // only Play Mode has: console-shop P3 / G2 cover it live. Runs in the Test Runner
    // (Edit Mode) and, for the editor bridge, through RunAll().
    [TestFixture]
    public sealed class ShopConsoleRulesTests
    {
        private ShopCatalog catalog;
        private HQSigns signs;
        private TopModel top;
        private BottomModel bottom;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<ShopCatalog>();
            catalog.hideFlags = HideFlags.HideAndDontSave;
            catalog.ResetToDefaults(null); // air-tank $40, large-tank $300, bright-headlamp $150, patch-kit $60
            signs = ScriptableObject.CreateInstance<HQSigns>();
            signs.hideFlags = HideFlags.HideAndDontSave;
            signs.EnsureDefaults();
            top = new TopModel();
            bottom = new BottomModel();
        }

        [TearDown]
        public void TearDown()
        {
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (signs != null) Object.DestroyImmediate(signs);
        }

        private ShopItem Item(string id) => catalog.Find(id);

        [Test]
        public void PageCount_ThreeRowsAPage()
        {
            Assert.AreEqual(3, ShopConsoleRules.RowsPerPage);
            int[] expected = { 1, 1, 1, 1, 2, 2, 2, 3 };
            for (int items = 0; items <= 7; items++) Assert.AreEqual(expected[items], ShopConsoleRules.PageCount(items), "items " + items);
        }

        [Test]
        public void ClampPage_InsideTheCatalogue()
        {
            Assert.AreEqual(0, ShopConsoleRules.ClampPage(-1, 4));
            Assert.AreEqual(0, ShopConsoleRules.ClampPage(0, 4));
            Assert.AreEqual(1, ShopConsoleRules.ClampPage(1, 4));
            Assert.AreEqual(1, ShopConsoleRules.ClampPage(5, 4));
            Assert.AreEqual(0, ShopConsoleRules.ClampPage(3, 0));
        }

        [Test]
        public void ItemAt_PagesTheCatalogueInOrder()
        {
            Assert.AreEqual(ShopCatalog.AirTankId, ShopConsoleRules.ItemAt(catalog, 0, 0).Id);
            Assert.AreEqual(ShopCatalog.LargeTankId, ShopConsoleRules.ItemAt(catalog, 0, 1).Id);
            Assert.AreEqual(ShopCatalog.BrightHeadlampId, ShopConsoleRules.ItemAt(catalog, 0, 2).Id);
            Assert.AreEqual(ShopCatalog.PatchKitId, ShopConsoleRules.ItemAt(catalog, 1, 0).Id);
            Assert.IsNull(ShopConsoleRules.ItemAt(catalog, 1, 1));
            Assert.IsNull(ShopConsoleRules.ItemAt(catalog, 2, 0));
            Assert.IsNull(ShopConsoleRules.ItemAt(null, 0, 0));
            Assert.AreEqual(4, ShopConsoleRules.ItemIndex(1, 1));
        }

        [Test]
        public void RowState_ByTheMoney()
        {
            Assert.AreEqual(ShopConsoleRules.StateNoMoney, ShopConsoleRules.RowState(Item(ShopCatalog.AirTankId), null, 0));
            Assert.AreEqual(ShopConsoleRules.StateNoMoney, ShopConsoleRules.RowState(Item(ShopCatalog.AirTankId), null, 39));
            Assert.AreEqual(ShopConsoleRules.StateBuy, ShopConsoleRules.RowState(Item(ShopCatalog.AirTankId), null, 40));
            Assert.AreEqual(ShopConsoleRules.StateBuy, ShopConsoleRules.RowState(Item(ShopCatalog.AirTankId), null, 500));
            // An upgrade with no PlayerUpgrades to ask (a peer without a player yet): by the money alone.
            Assert.AreEqual(ShopConsoleRules.StateNoMoney, ShopConsoleRules.RowState(Item(ShopCatalog.LargeTankId), null, 299));
            Assert.AreEqual(ShopConsoleRules.StateBuy, ShopConsoleRules.RowState(Item(ShopCatalog.LargeTankId), null, 300));
        }

        [Test]
        public void RowLine_NamePriceState()
        {
            Assert.AreEqual("Air tank $40 BUY", ShopConsoleRules.RowLine(Item(ShopCatalog.AirTankId), ShopConsoleRules.StateBuy));
            Assert.AreEqual("Large tank $300 OWNED", ShopConsoleRules.RowLine(Item(ShopCatalog.LargeTankId), ShopConsoleRules.StateOwned));
            Assert.AreEqual("Bright headlamp $150 NO MONEY", ShopConsoleRules.RowLine(Item(ShopCatalog.BrightHeadlampId), ShopConsoleRules.StateNoMoney));
        }

        [Test]
        public void RowsText_FirstAndSecondPage()
        {
            Assert.AreEqual("Air tank $40 BUY|Large tank $300 BUY|Bright headlamp $150 BUY", ShopConsoleRules.RowsText(catalog, 0, null, 500));
            Assert.AreEqual("Air tank $40 NO MONEY|Large tank $300 NO MONEY|Bright headlamp $150 NO MONEY", ShopConsoleRules.RowsText(catalog, 0, null, 0));
            Assert.AreEqual("Air tank $40 BUY|Large tank $300 NO MONEY|Bright headlamp $150 BUY", ShopConsoleRules.RowsText(catalog, 0, null, 160));
            Assert.AreEqual("Patch kit $60 BUY|-|-", ShopConsoleRules.RowsText(catalog, 1, null, 60));
            Assert.AreEqual("Patch kit $60 NO MONEY|-|-", ShopConsoleRules.RowsText(catalog, 1, null, 59));
        }

        [Test]
        public void ShopLever_NextPageOnlyWithMoreThanOnePage()
        {
            Assert.AreEqual(LeverAction.NextPage, ShopConsoleRules.ShopLever(1, out bool one, out string oneReason));
            Assert.IsFalse(one); Assert.AreEqual(ConsoleRules.OnePage, oneReason);
            Assert.AreEqual(LeverAction.NextPage, ShopConsoleRules.ShopLever(2, out bool two, out string twoReason));
            Assert.IsTrue(two); Assert.AreEqual(string.Empty, twoReason);
        }

        [Test]
        public void PageText_OneBased()
        {
            Assert.AreEqual("PAGE 1/2", ShopConsoleRules.PageText(0, 2));
            Assert.AreEqual("PAGE 2/2", ShopConsoleRules.PageText(1, 2));
            Assert.AreEqual("PAGE 1/1", ShopConsoleRules.PageText(0, 1));
        }

        [Test]
        public void Expected_ShopIsNextPage()
        {
            // ConsoleRules.Expected needs a CrewDayState (it answers None without one, so edit
            // mode cannot call it); the shop branch is one line over ShopLever, which the
            // console-shop matrix (P4) exercises live. Here: the rule over the real catalogue.
            int pages = ShopConsoleRules.PageCount(ShopCatalog.Resolve().Items.Count);
            Assert.AreEqual(LeverAction.NextPage, ShopConsoleRules.ShopLever(pages, out bool enabled, out string reason));
            // The real catalogue (Resources) has four items: two pages, the lever lit.
            Assert.AreEqual(pages > 1, enabled, reason);
            Assert.AreEqual(LeverAction.NextPage, ShopConsoleRules.ShopLever(1, out enabled, out reason));
            Assert.IsFalse(enabled); Assert.AreEqual(ConsoleRules.OnePage, reason);
        }

        [Test]
        public void ComposeTop_FirstPageWithMoney()
        {
            HQShopConsole.ComposeTop(catalog, 0, null, 500, top, signs);
            Assert.AreEqual("EQUIPMENT CATALOGUE", top.Title);
            Assert.AreEqual("CREW POT $500", top.Corner);
            Assert.IsNull(top.Cards);
            Assert.AreEqual("PAGE 1/2", top.BigState);
            Assert.AreEqual(ConsoleTone.Accent, top.BigTone);
            Assert.AreEqual("Air tank $40 BUY|Large tank $300 BUY|Bright headlamp $150 BUY", top.Hint);
            Assert.AreEqual("EQUIPMENT CATALOGUE · CREW POT $500 ·  · PAGE 1/2 · Air tank $40 BUY|Large tank $300 BUY|Bright headlamp $150 BUY · " + signs.Get("shop.prev") + " · " + signs.Get("shop.next"), ConsoleModels.Flatten(top));
        }

        [Test]
        public void ComposeTop_SecondPageEmptyPot_AndClamped()
        {
            HQShopConsole.ComposeTop(catalog, 1, null, 0, top, signs);
            Assert.AreEqual("CREW POT $0", top.Corner);
            Assert.AreEqual("PAGE 2/2", top.BigState);
            Assert.AreEqual("Patch kit $60 NO MONEY|-|-", top.Hint);
            HQShopConsole.ComposeTop(catalog, 7, null, 0, top, signs);
            Assert.AreEqual("PAGE 2/2", top.BigState, "a page past the catalogue clamps to the last");
        }

        [Test]
        public void ComposeBottom_PosterAndRefusal()
        {
            HQShopConsole.ComposeBottom(string.Empty, bottom, signs);
            Assert.AreEqual(signs.Get("counter.sub"), bottom.Heading);
            StringAssert.Contains("BROWSE & BUY", bottom.Heading);
            Assert.AreEqual(string.Empty, bottom.Name);
            Assert.IsNull(bottom.VoteCard);
            Assert.AreEqual(2, bottom.Lines.Length);
            Assert.AreEqual(signs.Get("shop.aim"), bottom.Lines[0].Text);
            Assert.AreEqual(signs.Get("shop.pickup"), bottom.Lines[1].Text);
            Assert.AreEqual(string.Empty, bottom.Status.Text);
            Assert.IsFalse(bottom.Notice);
            HQShopConsole.ComposeBottom("Not enough money: $40 needed, $0 in the pot", bottom, signs);
            Assert.AreEqual("Not enough money: $40 needed, $0 in the pot", bottom.Status.Text);
            Assert.AreEqual(ConsoleTone.Warn, bottom.Status.Tone);
            Assert.IsTrue(bottom.Notice);
            StringAssert.EndsWith(" · Not enough money: $40 needed, $0 in the pot · NOTICE", ConsoleModels.Flatten(bottom));
        }

        [Test]
        public void ComposeSign_LitOnTwoPages_DimOnOne()
        {
            SignModel two = HQShopConsole.ComposeSign(2, false, signs);
            Assert.AreEqual(signs.Get("lever.page") + "/on", ConsoleModels.Flatten(two));
            Assert.IsFalse(two.Aimed);
            SignModel one = HQShopConsole.ComposeSign(1, true, signs);
            Assert.AreEqual(signs.Get("lever.page") + "/off", ConsoleModels.Flatten(one));
            Assert.IsTrue(one.Aimed);
        }

        [Test]
        public void ComposeText_TheCompatLine()
        {
            Assert.AreEqual("CATALOGUE PAGE 1/2\nAir tank $40 BUY|-|-\nCREW POT $40", HQShopConsole.ComposeText(0, 2, "Air tank $40 BUY|-|-", 40));
        }

        // For the editor bridge: `run SunkCost.Editor.Tests.ShopConsoleRulesTests.RunAll`.
        public static string RunAll()
        {
            var fixture = new ShopConsoleRulesTests();
            int passed = 0, failed = 0;
            var report = new System.Text.StringBuilder();
            foreach (MethodInfo method in typeof(ShopConsoleRulesTests).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.GetCustomAttribute<TestAttribute>() == null) continue;
                fixture.SetUp();
                try { method.Invoke(fixture, null); passed++; }
                catch (TargetInvocationException e) { failed++; report.Append("FAIL ").Append(method.Name).Append(": ").Append((e.InnerException ?? e).Message.Trim()).Append('\n'); }
                finally { fixture.TearDown(); }
            }
            report.Insert(0, $"ShopConsoleRulesTests: passed {passed}/{passed + failed}\n");
            return report.ToString().TrimEnd();
        }
    }
}
