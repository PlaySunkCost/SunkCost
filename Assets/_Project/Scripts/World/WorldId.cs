namespace SunkCost.World
{
    // The three world scenes of docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 3.
    // The Session scene is not a world: it is never loaded or unloaded by FishNet.
    public enum WorldId : byte
    {
        HQ = 0,
        Sea = 1,
        Dive = 2
    }
}
