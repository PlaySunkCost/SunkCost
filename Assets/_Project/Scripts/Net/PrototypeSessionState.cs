using System.Text;

namespace SunkCost.Net
{
    // Which transport a session uses. Selected in the menu; bound to the process the
    // first time the network root initializes (FishNet subscribes its managers to the
    // transport once), so after that it is immutable until the game restarts.
    public enum SessionMode
    {
        Local,
        Steam
    }

    public enum SessionRole
    {
        None,
        Host,
        Guest
    }

    // docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md section 5. Any starting or active
    // state can go to Leaving; Leaving always ends in Menu.
    public enum SessionState
    {
        Menu,
        InitializingSteam,
        CreatingLobby,
        StartingServer,
        ConnectingHost,
        JoiningLobby,
        ValidatingLobby,
        ConnectingClient,
        Authenticating,
        WaitingForPlayer,
        InRoom,
        Leaving
    }

    // Read-only picture of the session controller for the UI, the test hooks and
    // the debug log. Built on demand; never mutated by consumers.
    public sealed class SessionSnapshot
    {
        public SessionMode SelectedMode;
        public SessionMode? BoundMode;
        public SessionState State;
        public SessionRole Role;
        public int OperationId;
        public string SessionId = string.Empty;
        public ulong LobbyId;
        public ulong OriginalHostSteamId;
        public bool SteamInitialized;
        public bool ServerStarted;
        public bool ClientStarted;
        public bool ClientAuthenticated;
        public bool LocalPlayerReady;
        public int LobbyMembers;
        public int PlayersInHq;
        public int AdmittedPlayers;
        public int TotalPlayers;
        public string Message = string.Empty;

        public bool InRoom => State == SessionState.InRoom;

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("[Session] state=").Append(State)
              .Append(" role=").Append(Role)
              .Append(" mode=").Append(SelectedMode)
              .Append(" bound=").Append(BoundMode.HasValue ? BoundMode.Value.ToString() : "none")
              .Append(" op=").Append(OperationId)
              .Append(" session=").Append(string.IsNullOrEmpty(SessionId) ? "-" : SessionId)
              .Append(" lobby=").Append(LobbyId)
              .Append(" host=").Append(OriginalHostSteamId)
              .Append(" steam=").Append(SteamInitialized)
              .Append(" server=").Append(ServerStarted)
              .Append(" client=").Append(ClientStarted)
              .Append(" auth=").Append(ClientAuthenticated)
              .Append(" player=").Append(LocalPlayerReady)
              .Append(" members=").Append(LobbyMembers)
              .Append(" hq=").Append(PlayersInHq).Append('/').Append(TotalPlayers)
              .Append(" admitted=").Append(AdmittedPlayers)
              .Append(" msg='").Append(Message).Append('\'');
            return sb.ToString();
        }
    }
}
