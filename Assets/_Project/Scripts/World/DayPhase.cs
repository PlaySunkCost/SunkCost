namespace SunkCost.World
{
    // Where the crew is in the cycle (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 4.1). Server-written; clients derive fades and refusals from it.
    public enum DayPhase : byte
    {
        AtHQ = 0,
        Sailing = 1,
        AtSea = 2,
        DiveInProgress = 3,
        SailingHome = 4
    }
}
