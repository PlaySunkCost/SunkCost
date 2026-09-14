namespace SunkCost.Interaction
{
    // How an item is carried (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md section 6).
    // OneHand is the default so existing content keeps its right-hand pose and
    // slot eligibility. TwoHands is hands-only: never stowed, held low and central.
    public enum CarryGrip : byte
    {
        OneHand = 0,
        TwoHands = 1
    }
}
