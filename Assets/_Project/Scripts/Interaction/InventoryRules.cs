namespace SunkCost.Interaction
{
    public enum GrabOutcome : byte
    {
        RefuseHandsFull, // an overflow item is in the hands, or nothing fits and the hands are busy
        HoldWithSlot,    // into the first free slot and into the hands
        StowIntoSlot,    // into the first free slot; a slot item stays in the hands
        HoldOverflow,   // no slot for it; hands only
        StowAndHoldOverflow // full slots: put the equipped slot item away, hold the new item
    }

    public enum EquipOutcome : byte
    {
        Refuse,       // empty slot, or an overflow item blocks the number keys
        PutAway,      // the slot's item is in the hands: stow it
        Equip,        // hands empty: take the slot's item
        SwapAndEquip  // another slot item is in the hands: stow it, take this one
    }

    public enum RefuseReason : byte
    {
        HandsFull = 1,
        TooFar = 2,
        NotFree = 3,
        NoSuchItem = 4
    }

    // The inventory decision table (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md
    // section 6), pure so the editor checks can cover every row. The server is the
    // only caller that acts on the answer; the client uses it to skip requests that
    // would certainly be refused.
    public static class InventoryRules
    {
        public static GrabOutcome DecideGrab(bool fitsInSlot, int firstFreeSlot, bool handsEmpty, bool holdingOverflow)
        {
            if (holdingOverflow) return GrabOutcome.RefuseHandsFull;
            if (fitsInSlot && firstFreeSlot >= 0)
                return handsEmpty ? GrabOutcome.HoldWithSlot : GrabOutcome.StowIntoSlot;
            if (firstFreeSlot < 0 && !handsEmpty)
                return GrabOutcome.StowAndHoldOverflow;
            return handsEmpty ? GrabOutcome.HoldOverflow : GrabOutcome.RefuseHandsFull;
        }

        public static EquipOutcome DecideEquip(int slotObjectId, int heldObjectId, bool holdingOverflow)
        {
            if (slotObjectId == InventorySlots.Empty) return EquipOutcome.Refuse;
            if (holdingOverflow) return EquipOutcome.Refuse;
            if (heldObjectId == slotObjectId) return EquipOutcome.PutAway;
            return heldObjectId == InventorySlots.Empty ? EquipOutcome.Equip : EquipOutcome.SwapAndEquip;
        }

        public static string ReasonText(RefuseReason reason)
        {
            switch (reason)
            {
                case RefuseReason.HandsFull: return "Hands full";
                case RefuseReason.TooFar: return "Too far";
                case RefuseReason.NotFree: return "Someone else has it";
                case RefuseReason.NoSuchItem: return "Nothing to grab";
                default: return string.Empty;
            }
        }
    }
}
