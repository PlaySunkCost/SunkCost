namespace SunkCost.Net
{
    // Implement on a NetworkBehaviour to add a one-line status to its row in the
    // network debug overlay (for example "Held by 1"). Optional; the overlay works
    // without it.
    public interface INetworkDebugInfo
    {
        string DebugStatus { get; }
    }
}
