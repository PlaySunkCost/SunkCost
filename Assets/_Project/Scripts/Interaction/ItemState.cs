namespace SunkCost.Interaction
{
    // Replicated item state (docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md section 5).
    // Free: server simulates. Held: the holder owns it and writes its transform from
    // the hold point. Released: the holder still simulates until rest handoff.
    // Stowed: server-owned, hidden, carried in a player's inventory.
    public enum ItemState : byte
    {
        Free = 0,
        Held = 1,
        Released = 2,
        Stowed = 3
    }

    // What left click does with the item in the hands.
    public enum ItemUseAction : byte
    {
        None = 0,
        Throw = 1,
        Breathe = 2, // an air tank: left click refills the holder's tank (AirTankItem)
        Patch = 3    // a patch kit: left click closes the holder's own leak (PatchKitItem)
    }
}
