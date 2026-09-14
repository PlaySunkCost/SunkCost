namespace SunkCost.Net
{
    // Implement on a NetworkBehaviour to add a one-line status to its row in the
    // network debug overlay (for example "Held by 1"). Optional; the overlay works
    // without it.
    public interface INetworkDebugInfo
    {
        string DebugStatus { get; }

        // Whether THIS machine is the object's simulation writer, when the overlay's
        // Rigidbody rule (non-kinematic == writer) would be wrong: an object that is
        // kinematic by design, such as a held item snapped to a hand. Null keeps the
        // default rule.
        bool? WriterOverride { get; }
    }
}
