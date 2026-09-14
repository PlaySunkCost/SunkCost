using System;
using SunkCost.Interaction;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Repeatable pure checks for the holding/inventory work
    // (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md section 12): the slot struct and
    // the grab/equip decision table. No network. Throws on the first failure with
    // the case name; run from the menu or from an MCP command.
    public static class HQPrototypeInventoryChecks
    {
        [MenuItem("Sunk Cost/Prototype/Run inventory checks")]
        public static void RunFromMenu()
        {
            RunOrThrow();
            Debug.Log("Inventory checks passed.");
        }

        public static void RunOrThrow()
        {
            Slots();
            GrabTable();
            EquipTable();
            ReasonTexts();
        }

        private static void Slots()
        {
            InventorySlots none = InventorySlots.None;
            Expect(none.IsEmpty(), "None is empty");
            Expect(none.FirstFree() == 0, "None first free is 0");
            Expect(none.IndexOf(7) == -1, "None has no 7");
            Expect(none.IndexOf(InventorySlots.Empty) == -1, "Empty id is never found");

            InventorySlots one = none.With(0, 7);
            Expect(none.IsEmpty(), "With does not mutate the original");
            Expect(one.Get(0) == 7 && one.Get(1) == InventorySlots.Empty, "With sets only the index");
            Expect(one.IndexOf(7) == 0, "IndexOf finds the id");
            Expect(one.FirstFree() == 1, "First free skips the used slot");

            InventorySlots full = none.With(0, 1).With(1, 2).With(2, 3).With(3, 4);
            Expect(full.FirstFree() == -1, "Full has no free slot");
            Expect(full.IndexOf(4) == 3, "Last slot is found");
            Expect(full.With(3, InventorySlots.Empty).FirstFree() == 3, "Clearing frees the slot");
            Expect(full.Get(9) == InventorySlots.Empty && full.With(9, 5).IndexOf(5) == -1, "Out-of-range index is ignored");
            Expect(default(InventorySlots).Get(0) == 0, "default struct is NOT None (object id 0 is valid), which is why the SyncVar starts from None");
        }

        private static void GrabTable()
        {
            Expect(InventoryRules.DecideGrab(true, 0, true, false) == GrabOutcome.HoldWithSlot, "fits + free slot + hands empty -> hold with slot");
            Expect(InventoryRules.DecideGrab(true, 2, false, false) == GrabOutcome.StowIntoSlot, "fits + free slot + holding slot item -> stow silently");
            Expect(InventoryRules.DecideGrab(true, -1, true, false) == GrabOutcome.HoldOverflow, "fits + slots full + hands empty -> overflow");
            Expect(InventoryRules.DecideGrab(false, 0, true, false) == GrabOutcome.HoldOverflow, "hands-only + hands empty -> overflow");
            Expect(InventoryRules.DecideGrab(true, -1, false, false) == GrabOutcome.StowAndHoldOverflow, "slots full + holding slot item -> stow equipped item and hold fifth");
            Expect(InventoryRules.DecideGrab(false, -1, false, false) == GrabOutcome.StowAndHoldOverflow, "slots full + holding slot item + hands-only target -> stow and hold target");
            Expect(InventoryRules.DecideGrab(true, -1, false, true) == GrabOutcome.RefuseHandsFull, "four slots plus overflow blocks a sixth item");
            Expect(InventoryRules.DecideGrab(false, 0, false, false) == GrabOutcome.StowAndHoldOverflow, "hands-only + holding slot item + free slots -> stow equipped item and hold target");
            Expect(InventoryRules.DecideGrab(true, 0, false, true) == GrabOutcome.RefuseHandsFull, "holding overflow blocks E even with a free slot");
        }

        private static void EquipTable()
        {
            const int empty = InventorySlots.Empty;
            Expect(InventoryRules.DecideEquip(empty, empty, false) == EquipOutcome.Refuse, "empty slot -> nothing");
            Expect(InventoryRules.DecideEquip(5, empty, false) == EquipOutcome.Equip, "hands empty -> equip");
            Expect(InventoryRules.DecideEquip(5, 5, false) == EquipOutcome.PutAway, "held slot's number -> put away");
            Expect(InventoryRules.DecideEquip(5, 6, false) == EquipOutcome.SwapAndEquip, "other slot item held -> stow it and equip");
            Expect(InventoryRules.DecideEquip(5, 9, true) == EquipOutcome.Refuse, "overflow item held -> number keys blocked");
            Expect(InventoryRules.DecideEquip(empty, 9, true) == EquipOutcome.Refuse, "overflow + empty slot -> nothing");
        }

        private static void ReasonTexts()
        {
            foreach (RefuseReason reason in Enum.GetValues(typeof(RefuseReason)))
                Expect(!string.IsNullOrEmpty(InventoryRules.ReasonText(reason)), "reason text for " + reason);
            Expect(InventoryRules.ReasonText(RefuseReason.HandsFull) == "Hands full", "Hands full text");
        }

        private static void Expect(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("Inventory check failed: " + what);
        }
    }
}
