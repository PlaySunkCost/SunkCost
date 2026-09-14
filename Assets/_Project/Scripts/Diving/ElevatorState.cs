namespace SunkCost.Diving
{
    // Sealing is shared by both directions; ElevatorController stores which direction is
    // pending on its own field rather than splitting this into SealingForDescent/Ascent.
    // Opening is not a gate, so it gets no state: on arrival the state becomes
    // AtBottom/AtTop immediately and the doors open as presentation only.
    public enum ElevatorState
    {
        AtTop,
        Sealing,
        Descending,
        AtBottom,
        Ascending
    }
}
