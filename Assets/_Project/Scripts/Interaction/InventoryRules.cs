namespace SunkCost.Interaction
{
    public enum GrabOutcome : byte
    {
        RefuseHandsFull, // an overflow item is in the hands and the target cannot be stored
        HoldWithSlot,    // into the first free slot and into the hands
        StowIntoSlot,    // into the first free slot; a slot item stays in the hands
        HoldOverflow,   // no slot for it; hands only
        StowAndHoldOverflow // no slot for the target: put the equipped slot item away, hold the new item
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
        NoSuchItem = 4,
        Travelling = 5,  // the ship is under way: no item actions until arrival
        NoRoom = 6       // no clear space in front to drop or throw into
    }

    // The inventory decision table (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md
    // section 6), pure so the editor checks can cover every row. The server is the
    // only caller that acts on the answer; the client uses it to skip requests that
    // would certainly be refused.
    public static class InventoryRules
    {
        public static GrabOutcome DecideGrab(bool fitsInSlot, int firstFreeSlot, bool handsEmpty, bool holdingOverflow)
        {
            // Hands busy with an overflow item (a heavy two-handed ball): a slot-able
            // target still goes straight into a free slot; anything else is refused.
            if (holdingOverflow)
                return fitsInSlot && firstFreeSlot >= 0 ? GrabOutcome.StowIntoSlot : GrabOutcome.RefuseHandsFull;
            if (fitsInSlot && firstFreeSlot >= 0)
                return handsEmpty ? GrabOutcome.HoldWithSlot : GrabOutcome.StowIntoSlot;
            // No slot for the target (slots full, or a two-handed item): it goes to
            // the hands. An equipped slot item already owns its slot, so it is put
            // away first rather than blocking the pickup (LOOT_WEIGHT plan section 3).
            return handsEmpty ? GrabOutcome.HoldOverflow : GrabOutcome.StowAndHoldOverflow;
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
                case RefuseReason.Travelling: return "Hold on — the ship is moving";
                case RefuseReason.NoRoom: return "Not enough room to drop/throw";
                default: return string.Empty;
            }
        }
    }
}
