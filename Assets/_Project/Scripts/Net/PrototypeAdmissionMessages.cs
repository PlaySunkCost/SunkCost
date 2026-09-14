using FishNet.Broadcast;

namespace SunkCost.Net
{
    // Why the server refused a connection. Sent to the client as a small enum so it
    // can show a specific reason; the server never sends free text.
    public enum AdmissionRejection : byte
    {
        None = 0,
        WrongProject,
        WrongProtocol,
        WrongBuild,
        WrongSession,
        WrongLobby,
        BadNonce,
        NotLobbyMember,
        DuplicateIdentity,
        RoomFull,
        HostNotReady,
        Timeout,
        Internal,
        DiveInProgress
    }

    public static class AdmissionRejectionText
    {
        public static string ToMessage(AdmissionRejection reason)
        {
            switch (reason)
            {
                case AdmissionRejection.None: return "Admitted";
                case AdmissionRejection.WrongProject: return "Wrong game";
                case AdmissionRejection.WrongProtocol: return "Wrong protocol";
                case AdmissionRejection.WrongBuild: return "Wrong build";
                case AdmissionRejection.WrongSession: return "Room closed";
                case AdmissionRejection.WrongLobby: return "Room changed or closed";
                case AdmissionRejection.BadNonce: return "Handshake mismatch";
                case AdmissionRejection.NotLobbyMember: return "Not a lobby member";
                case AdmissionRejection.DuplicateIdentity: return "Already connected";
                case AdmissionRejection.RoomFull: return "Room full";
                case AdmissionRejection.HostNotReady: return "Host is not ready";
                case AdmissionRejection.Timeout: return "Authentication timed out";
                case AdmissionRejection.DiveInProgress: return "Dive in progress — join between days";
                default: return "Could not join";
            }
        }
    }

    // docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md section 9. Reliable broadcasts, sent
    // before FishNet authentication. Strings are bounded by the validator on both
    // sides before any comparison.

    // server -> pending connection
    public struct AdmissionChallenge : IBroadcast
    {
        public string Project;
        public int Protocol;
        public string Build;
        public string Session;
        public ulong LobbyId; // 0 in Local mode
        public uint Nonce;
    }

    // client -> server; the only pre-authentication client broadcast the server accepts
    public struct AdmissionResponse : IBroadcast
    {
        public string Project;
        public int Protocol;
        public string Build;
        public string Session;
        public ulong LobbyId;
        public uint Nonce;
    }

    // server -> connection, sent before the FishNet authentication result is raised
    public struct AdmissionResult : IBroadcast
    {
        public string Session;
        public uint Nonce;
        public bool Accepted;
        public AdmissionRejection Reason;
    }
}
