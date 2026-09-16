using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using Steamworks;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Net
{
    // The one owner of a session: mode binding, host and guest flows, admission
    // configuration, lobby membership, leave and final shutdown
    // (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md sections 5, 7, 8, 10). The UI only
    // renders its snapshot and calls its public operations. Every asynchronous step
    // carries an operation id and gives up if a newer operation has started.
    public sealed class PrototypeSessionController : MonoBehaviour
    {
        public struct MemberInfo
        {
            public ulong SteamId;   // 0 in Local mode
            public int OwnerClientId; // -1 when unknown
            public string Name;
            public bool IsHost;
            public bool IsSelf;
        }

        private GameObject networkRoot;
        private NetworkManager networkManager;
        private TransportManager transportManager;
        private Transport localTransport;
        private GameObject steamTransportPrefab;
        private Camera previewCamera;
        private LobbySessionSettings settings = new();

        private SteamBootstrap steam;
        private SteamLobbyService lobby;
        private PrototypeAuthenticator auth;
        private Transport steamTransport;

        private SessionMode selectedMode;
        private SessionMode? boundMode;
        private SessionState state = SessionState.Menu;
        private SessionRole role = SessionRole.None;
        private int operationId;
        private string sessionId = string.Empty;
        private ulong lobbyId;
        private ulong originalHostSteamId;
        private string message = "Choose Local or Steam, then Host or Join.";
        private Coroutine activeFlow;
        private Coroutine membershipCheck;
        private bool clientAuthenticated;
        private bool wired;
        private readonly List<MemberInfo> members = new();

        public SessionState State => state;
        public SessionRole Role => role;
        public SessionMode SelectedMode => selectedMode;
        public SessionMode? BoundMode => boundMode;
        public bool InRoom => state == SessionState.InRoom;
        public bool Busy => state != SessionState.Menu && state != SessionState.InRoom;
        public string Message => message;
        public ulong LobbyId => lobbyId;
        public string LobbyIdText => lobbyId == 0 ? string.Empty : LobbyMetadata.FormatULong(lobbyId);
        public bool SteamInitialized => steam != null && steam.Initialized;
        public bool SteamOverlayAvailable => steam != null && steam.IsOverlayEnabled;
        // The default display name: the Steam persona while Steam is up.
        public string LocalPersonaName => steam != null ? steam.LocalPersonaName : string.Empty;
        public IReadOnlyList<MemberInfo> Members => members;
        public LobbySessionSettings Settings => settings;
        public NetworkManager NetworkManager => networkManager;

        // ---- wiring ---------------------------------------------------------------

        public void Configure(GameObject root, NetworkManager manager, TransportManager transports, Transport local,
            GameObject steamPrefab, Camera preview, LobbySessionSettings sessionSettings)
        {
            networkRoot = root;
            networkManager = manager;
            transportManager = transports;
            localTransport = local;
            steamTransportPrefab = steamPrefab;
            previewCamera = preview;
            settings = sessionSettings ?? new LobbySessionSettings();
            steam = GetComponent<SteamBootstrap>() ?? gameObject.AddComponent<SteamBootstrap>();
            steam.OnInviteRequested -= OnInviteRequested;
            steam.OnInviteRequested += OnInviteRequested;
            steam.OnOverlayActivated -= OnOverlayActivated;
            steam.OnOverlayActivated += OnOverlayActivated;
        }

        private void OnApplicationFocus(bool focused) => SessionInputGate.SetApplicationFocus(focused);

        private void OnOverlayActivated(bool active)
        {
            if (InRoom) SessionInputGate.SetOverlay(active);
        }

        // ---- mode and Steam -------------------------------------------------------

        public bool SelectMode(SessionMode mode, out string error)
        {
            if (boundMode.HasValue && boundMode.Value != mode)
            {
                error = "Transport is locked to " + boundMode.Value + " for this run; restart the game to change.";
                return false;
            }
            selectedMode = mode;
            error = string.Empty;
            if (mode == SessionMode.Steam)
                return InitializeSteam(out error);
            return true;
        }

        public bool InitializeSteam(out string error)
        {
            if (!steam.TryInitialize(out error))
            {
                message = error;
                return false;
            }
            if (lobby == null)
            {
                lobby = new SteamLobbyService();
                lobby.MembersChanged += OnLobbyMembersChanged;
                lobby.DataChanged += OnLobbyDataChanged;
            }
            if (state == SessionState.Menu)
                message = "Steam ready — accept an invite or enter a lobby ID.";
            return true;
        }

        // ---- public operations ----------------------------------------------------

        public void StartHost()
        {
            if (!CanStartOperation(out string why)) { message = why; return; }
            if (!PrototypeBuildIdentity.EnsureLoaded()) { message = PrototypeBuildIdentity.LoadError; return; }
            role = SessionRole.Host;
            int op = BeginOperation();
            activeFlow = StartCoroutine(HostFlow(op, selectedMode));
        }

        public void JoinLocal(string address)
        {
            if (boundMode.HasValue && boundMode.Value != SessionMode.Local) { message = "Transport is locked to Steam for this run; restart the game to change."; return; }
            selectedMode = SessionMode.Local;
            if (!CanStartOperation(out string why)) { message = why; return; }
            if (!PrototypeBuildIdentity.EnsureLoaded()) { message = PrototypeBuildIdentity.LoadError; return; }
            string target = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            role = SessionRole.Guest;
            int op = BeginOperation();
            activeFlow = StartCoroutine(GuestFlow(op, SessionMode.Local, target, 0));
        }

        public bool JoinSteamLobby(string lobbyIdText)
        {
            if (!LobbyMetadata.TryParseLobbyId(lobbyIdText, out ulong id, out string error)) { message = error; return false; }
            return JoinSteamLobby(id);
        }

        public bool JoinSteamLobby(ulong id)
        {
            if (boundMode.HasValue && boundMode.Value != SessionMode.Steam) { message = "Transport is locked to Local for this run; restart the game to change."; return false; }
            selectedMode = SessionMode.Steam;
            if (!CanStartOperation(out string why)) { message = why; return false; }
            if (!InitializeSteam(out string steamError)) { message = steamError; return false; }
            if (!PrototypeBuildIdentity.EnsureLoaded()) { message = PrototypeBuildIdentity.LoadError; return false; }
            if (!PrototypeBuildIdentity.Current.IsUsableForSteam(out string reason)) { message = reason; return false; }
            role = SessionRole.Guest;
            int op = BeginOperation();
            activeFlow = StartCoroutine(GuestFlow(op, SessionMode.Steam, null, id));
            return true;
        }

        public void Leave() => Leave(role == SessionRole.Host ? "Room closed. Host or join again." : "Left the room. Host or join again.");

        public bool CopyLobbyId(out string error)
        {
            if (lobbyId == 0) { error = "No lobby yet."; return false; }
            GUIUtility.systemCopyBuffer = LobbyIdText;
            error = string.Empty;
            message = "Lobby ID copied.";
            return true;
        }

        public bool InviteFriends(out string error)
        {
            if (!InRoom || boundMode != SessionMode.Steam || lobbyId == 0) { error = "Invite needs a ready Steam room."; return false; }
            if (!steam.IsOverlayEnabled) { error = "Steam overlay unavailable here; share the lobby ID instead."; return false; }
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobbyId));
            error = string.Empty;
            return true;
        }

        // Direct invite through Steam's friends system; no overlay needed. The friend
        // gets a Steam notification and, with the build running, our invite listener
        // receives the lobby id when they accept.
        public bool InviteFriend(ulong friendSteamId, out string error)
        {
            if (!InRoom || boundMode != SessionMode.Steam || lobbyId == 0) { error = "Invite needs a ready Steam room."; return false; }
            if (!SteamMatchmaking.InviteUserToLobby(new CSteamID(lobbyId), new CSteamID(friendSteamId)))
            {
                error = "Steam did not accept the invite.";
                return false;
            }
            error = string.Empty;
            message = "Invite sent to " + steam.PersonaName(friendSteamId) + ".";
            return true;
        }

        public List<SteamBootstrap.Friend> Friends() => steam != null ? steam.Friends() : new List<SteamBootstrap.Friend>();

        private void OnInviteRequested(ulong invitedLobbyId)
        {
            if (state != SessionState.Menu)
            {
                if (invitedLobbyId != lobbyId)
                    message = "Leave this room before accepting another invite.";
                return;
            }
            if (boundMode.HasValue && boundMode.Value != SessionMode.Steam)
            {
                message = "Transport is locked to Local for this run; restart the game to accept Steam invites.";
                return;
            }
            JoinSteamLobby(invitedLobbyId);
        }

        // ---- flows ----------------------------------------------------------------

        private bool CanStartOperation(out string why)
        {
            if (state != SessionState.Menu) { why = InRoom ? "Leave the current room first." : "A session operation is already in progress."; return false; }
            if (lobby != null && lobby.Busy) { why = "Finishing cancelled Steam request..."; return false; }
            if (networkRoot == null || networkManager == null || transportManager == null) { why = "Scene setup is incomplete."; return false; }
            why = string.Empty;
            return true;
        }

        private int BeginOperation()
        {
            operationId++;
            clientAuthenticated = false;
            members.Clear();
            return operationId;
        }

        private bool IsCurrent(int op) => op == operationId && state != SessionState.Leaving && state != SessionState.Menu;

        private IEnumerator HostFlow(int op, SessionMode mode)
        {
            sessionId = LobbyMetadata.NewSessionId();
            lobbyId = 0;
            originalHostSteamId = 0;
            message = "Starting host...";

            if (mode == SessionMode.Steam)
            {
                state = SessionState.InitializingSteam;
                if (!InitializeSteam(out string steamError)) { Fail(op, steamError); yield break; }
                if (!PrototypeBuildIdentity.Current.IsUsableForSteam(out string reason)) { Fail(op, reason); yield break; }
                originalHostSteamId = steam.LocalSteamId;

                state = SessionState.CreatingLobby;
                message = "Creating Steam lobby...";
                bool done = false; EResult code = EResult.k_EResultFail; ulong created = 0;
                if (!lobby.BeginCreate(ELobbyType.k_ELobbyTypePrivate, settings.Clamped(settings.totalPlayers), op,
                        (o, result, id) => { if (o == op) { done = true; code = result; created = id; } }, out string createError))
                {
                    Fail(op, createError); yield break;
                }
                float deadline = Time.unscaledTime + settings.steamOperationTimeout;
                while (!done && Time.unscaledTime < deadline && IsCurrent(op)) yield return null;
                if (!IsCurrent(op)) yield break;
                if (!done) { lobby.Abandon(op); Fail(op, "Steam lobby creation timed out."); yield break; }
                if (code != EResult.k_EResultOK || created == 0) { Fail(op, "Could not create Steam lobby (" + code + ")."); yield break; }
                lobbyId = created;
                if (!WriteHostMetadata(ready: false)) { Fail(op, "Could not configure the Steam lobby."); yield break; }
            }

            if (!BindTransport(mode, out string bindError)) { Fail(op, bindError); yield break; }
            ConfigureServerAdmission(mode);

            state = SessionState.StartingServer;
            message = "Starting server...";
            if (!networkManager.ServerManager.StartConnection()) { Fail(op, "Server failed to start."); yield break; }
            float serverDeadline = Time.unscaledTime + settings.serverStartTimeout;
            while (!networkManager.ServerManager.Started && Time.unscaledTime < serverDeadline && IsCurrent(op)) yield return null;
            if (!IsCurrent(op)) yield break;
            if (!networkManager.ServerManager.Started) { Fail(op, "Server did not become ready."); yield break; }
            ApplyTransportCap(mode);

            state = SessionState.ConnectingHost;
            message = "Connecting host client...";
            auth.ConfigureClient(new PrototypeAuthenticator.ClientExpectation { Session = sessionId, LobbyId = lobbyId });
            string hostAddress = mode == SessionMode.Steam ? LobbyMetadata.FormatULong(originalHostSteamId) : "127.0.0.1";
            if (!networkManager.ClientManager.StartConnection(hostAddress)) { Fail(op, "Host client failed to start."); yield break; }

            yield return WaitForAdmission(op);
            if (!IsCurrent(op)) yield break;

            if (mode == SessionMode.Steam)
            {
                if (!lobby.SetType(ELobbyType.k_ELobbyTypeFriendsOnly) || !WriteHostMetadata(ready: true) || !lobby.SetJoinable(true))
                {
                    Fail(op, "Could not publish the Steam lobby."); yield break;
                }
            }
            auth.SetAcceptingGuests(true);
            EnterRoom(mode == SessionMode.Steam
                ? "Hosting over Steam. Invite friends or share the lobby ID."
                : "Hosting locally/LAN on UDP port 7770.");
        }

        private IEnumerator GuestFlow(int op, SessionMode mode, string localAddress, ulong targetLobby)
        {
            sessionId = string.Empty;
            lobbyId = 0;
            originalHostSteamId = 0;
            string connectAddress = localAddress;

            if (mode == SessionMode.Steam)
            {
                state = SessionState.JoiningLobby;
                message = "Joining Steam lobby...";
                bool done = false; var response = EChatRoomEnterResponse.k_EChatRoomEnterResponseError; ulong entered = 0; bool ioFailure = false;
                if (!lobby.BeginJoin(targetLobby, op, (o, r, id, io) => { if (o == op) { done = true; response = r; entered = id; ioFailure = io; } }, out string joinError))
                {
                    Fail(op, joinError); yield break;
                }
                float deadline = Time.unscaledTime + settings.steamOperationTimeout;
                while (!done && Time.unscaledTime < deadline && IsCurrent(op)) yield return null;
                if (!IsCurrent(op)) yield break;
                if (!done) { lobby.Abandon(op); Fail(op, "Steam lobby join timed out."); yield break; }
                if (ioFailure || response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess || entered == 0)
                {
                    Fail(op, JoinFailureMessage(response, ioFailure)); yield break;
                }
                lobbyId = entered;

                state = SessionState.ValidatingLobby;
                message = "Checking the room...";
                LobbyMetadata.Parsed parsed = default;
                string validation = null;
                float metaDeadline = Time.unscaledTime + settings.steamOperationTimeout;
                while (IsCurrent(op))
                {
                    if (LobbyMetadata.TryParse(key => lobby.GetData(lobbyId, key), out parsed, out string parseError))
                    {
                        validation = LobbyMetadata.Validate(parsed, PrototypeBuildIdentity.Current);
                        // Definitive mismatches fail now; "not ready yet" may still resolve.
                        if (validation == null || !validation.StartsWith("Host is not ready", StringComparison.Ordinal)) break;
                    }
                    else if (!parseError.StartsWith("Lobby is missing", StringComparison.Ordinal))
                    {
                        validation = parseError; break;
                    }
                    if (Time.unscaledTime >= metaDeadline) { validation ??= "Room is not ready."; break; }
                    yield return null;
                }
                if (!IsCurrent(op)) yield break;
                if (validation != null) { Fail(op, validation); yield break; }
                if (lobby.Owner(lobbyId) != parsed.HostSteamId) { Fail(op, "Room changed or closed."); yield break; }
                if (lobby.MemberCount(lobbyId) > parsed.Capacity) { Fail(op, "Room full (" + parsed.Capacity + " players)."); yield break; }
                originalHostSteamId = parsed.HostSteamId;
                sessionId = parsed.Session;
                connectAddress = LobbyMetadata.FormatULong(originalHostSteamId);
            }

            if (!BindTransport(mode, out string bindError)) { Fail(op, bindError); yield break; }
            auth.ClearServer();
            auth.ConfigureClient(new PrototypeAuthenticator.ClientExpectation
            {
                Session = mode == SessionMode.Steam ? sessionId : null,
                LobbyId = lobbyId
            });

            state = SessionState.ConnectingClient;
            message = "Connecting to " + (mode == SessionMode.Steam ? "host" : connectAddress) + "...";
            if (!networkManager.ClientManager.StartConnection(connectAddress)) { Fail(op, "Client failed to start."); yield break; }

            yield return WaitForAdmission(op);
            if (!IsCurrent(op)) yield break;
            if (mode == SessionMode.Local) sessionId = auth.LearnedSession;
            EnterRoom(mode == SessionMode.Steam ? "In the room." : "Connected to " + connectAddress + ".");
        }

        // Transport Started -> admission result -> owned player, each with a deadline.
        private IEnumerator WaitForAdmission(int op)
        {
            float connectDeadline = Time.unscaledTime + settings.connectTimeout;
            while (!networkManager.ClientManager.Started && Time.unscaledTime < connectDeadline && IsCurrent(op)) yield return null;
            if (!IsCurrent(op)) yield break;
            if (!networkManager.ClientManager.Started) { Fail(op, "Could not connect: room may be full or unavailable."); yield break; }

            state = SessionState.Authenticating;
            message = "Checking build and admission...";
            float authDeadline = Time.unscaledTime + settings.authTimeout + 1f;
            while (!clientAuthenticated && !auth.ClientResultReceived && Time.unscaledTime < authDeadline && IsCurrent(op)) yield return null;
            if (!IsCurrent(op)) yield break;
            if (auth.ClientResultReceived && !auth.ClientAccepted) { Fail(op, AdmissionRejectionText.ToMessage(auth.LastClientRejection)); yield break; }
            while (!clientAuthenticated && Time.unscaledTime < authDeadline && IsCurrent(op)) yield return null;
            if (!IsCurrent(op)) yield break;
            if (!clientAuthenticated) { Fail(op, AdmissionRejectionText.ToMessage(AdmissionRejection.Timeout)); yield break; }

            state = SessionState.WaitingForPlayer;
            message = "Spawning...";
            float spawnDeadline = Time.unscaledTime + settings.playerSpawnTimeout;
            while (LocalPlayer() == null && Time.unscaledTime < spawnDeadline && IsCurrent(op)) yield return null;
            if (!IsCurrent(op)) yield break;
            if (LocalPlayer() == null) Fail(op, "Player did not spawn.");
        }

        private void EnterRoom(string text)
        {
            state = SessionState.InRoom;
            message = text;
            RefreshMembers();
            SetPreviewCameraActive(false);
            SessionInputGate.EnterRoom();
        }

        private void Fail(int op, string why)
        {
            if (op != operationId) return;
            Leave(why);
        }

        // ---- leave ----------------------------------------------------------------

        public void Leave(string finalMessage)
        {
            if (state == SessionState.Menu || state == SessionState.Leaving) return;
            SessionRole leavingRole = role;
            state = SessionState.Leaving;
            operationId++;
            if (activeFlow != null) { StopCoroutine(activeFlow); activeFlow = null; }
            if (membershipCheck != null) { StopCoroutine(membershipCheck); membershipCheck = null; }
            message = finalMessage;
            StartCoroutine(LeaveFlow(leavingRole));
        }

        private IEnumerator LeaveFlow(SessionRole leavingRole)
        {
            if (auth != null) auth.SetAcceptingGuests(false);
            if (leavingRole == SessionRole.Host && lobby != null && lobby.CurrentLobbyId != 0)
            {
                lobby.SetJoinable(false);
                lobby.SetData(LobbyMetadata.KeyReady, "0");
            }
            if (networkManager != null)
            {
                var clientState = networkManager.ClientManager.Connection != null ? networkManager.ClientManager.Started : false;
                if (clientState || ClientStarting()) networkManager.ClientManager.StopConnection();
                if (leavingRole == SessionRole.Host && (networkManager.ServerManager.Started || ServerStarting()))
                    networkManager.ServerManager.StopConnection(true);
            }
            if (lobby != null) lobby.Leave();
            if (auth != null) { auth.ClearServer(); auth.ClearClient(); }

            float deadline = Time.unscaledTime + settings.cleanupTimeout;
            while (networkManager != null && (networkManager.ClientManager.Started || networkManager.ServerManager.Started) && Time.unscaledTime < deadline)
                yield return null;
            // The spawned player's camera/listener goes away with the connection.
            yield return null;

            role = SessionRole.None;
            sessionId = string.Empty;
            lobbyId = 0;
            originalHostSteamId = 0;
            clientAuthenticated = false;
            members.Clear();
            SetPreviewCameraActive(true);
            SessionInputGate.ExitRoom();
            state = SessionState.Menu;
        }

        private bool ClientStarting() =>
            transportManager != null && transportManager.Transport != null &&
            transportManager.Transport.GetConnectionState(false) == LocalConnectionState.Starting;

        private bool ServerStarting() =>
            transportManager != null && transportManager.Transport != null &&
            transportManager.Transport.GetConnectionState(true) == LocalConnectionState.Starting;

        // ---- FishNet wiring -------------------------------------------------------

        private bool BindTransport(SessionMode mode, out string error)
        {
            if (boundMode.HasValue)
            {
                if (boundMode.Value != mode) { error = "Transport is locked to " + boundMode.Value + " for this run; restart the game to change."; return false; }
                error = string.Empty;
                return true;
            }
            Transport selected;
            if (mode == SessionMode.Steam)
            {
                // Never bind the Steam transport without Steam: FishySteamworks's
                // Update and IterateIncoming throw every frame on an uninitialised
                // socket (seen 16 September 2026 with Steam closed).
                if (!steam.Initialized) { error = "Steam is not running; start Steam or use Local."; return false; }
                if (steamTransport == null)
                {
                    if (steamTransportPrefab == null) { error = "Steam transport prefab is missing."; return false; }
                    steamTransport = Instantiate(steamTransportPrefab, networkRoot.transform).GetComponent<Transport>();
                }
                selected = steamTransport;
            }
            else selected = localTransport;
            if (selected == null) { error = "Selected transport is missing."; return false; }

            transportManager.Transport = selected;
            networkRoot.SetActive(true);
            if (!networkManager.Initialized) { error = "Network manager did not initialize."; return false; }
            // FishNet initialises only the transport it finds when the NetworkManager
            // wakes; one assigned afterwards (the Steam transport, instantiated on
            // demand) must be initialised the same way or its sockets stay null.
            if (selected.NetworkManager == null) selected.Initialize(networkManager, 0);
            boundMode = mode;

            if (!wired)
            {
                auth = networkRoot.GetComponent<PrototypeAuthenticator>() ?? networkRoot.AddComponent<PrototypeAuthenticator>();
                auth.AuthTimeout = settings.authTimeout;
                auth.MembershipGrace = settings.membershipGrace;
                networkManager.ServerManager.SetAuthenticator(auth);
                networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
                networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
                networkManager.ClientManager.OnAuthenticated += OnClientAuthenticated;
                networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
                wired = true;
            }
            error = string.Empty;
            return true;
        }

        private void ConfigureServerAdmission(SessionMode mode)
        {
            auth.ConfigureServer(new PrototypeAuthenticator.ServerContext
            {
                Session = sessionId,
                LobbyId = lobbyId,
                Steam = mode == SessionMode.Steam,
                TotalPlayers = settings.Clamped(settings.totalPlayers),
                HostSteamId = originalHostSteamId,
                PeerSteamId = PeerSteamId,
                IsLobbyMember = id => lobby != null && lobby.IsMember(lobbyId, id)
            });
        }

        private ulong PeerSteamId(int connectionId)
        {
            if (transportManager == null || transportManager.Transport == null) return 0;
            string text = transportManager.Transport.GetConnectionAddress(connectionId);
            return ulong.TryParse(text, out ulong id) ? id : 0;
        }

        private void ApplyTransportCap(SessionMode mode)
        {
            // The prefab/scene values are the source of truth (the transport re-reads
            // them at StartServer); this keeps a mismatched asset from silently
            // admitting more sockets than the ledger allows.
            int cap = mode == SessionMode.Steam ? settings.SteamRemoteClientCap : settings.LocalSocketCap;
            transportManager.Transport.SetMaximumClients(cap);
        }

        private bool WriteHostMetadata(bool ready)
        {
            return lobby.SetData(LobbyMetadata.KeyGame, PrototypeBuildIdentity.Current.project)
                && lobby.SetData(LobbyMetadata.KeyProtocol, LobbyMetadata.FormatInt(PrototypeBuildIdentity.Current.protocol))
                && lobby.SetData(LobbyMetadata.KeyBuild, PrototypeBuildIdentity.Current.revision)
                && lobby.SetData(LobbyMetadata.KeyHost, LobbyMetadata.FormatULong(originalHostSteamId))
                && lobby.SetData(LobbyMetadata.KeySession, sessionId)
                && lobby.SetData(LobbyMetadata.KeyCapacity, LobbyMetadata.FormatInt(settings.Clamped(settings.totalPlayers)))
                && lobby.SetData(LobbyMetadata.KeyReady, LobbyMetadata.FormatBool(ready))
                && lobby.SetMemberLimit(settings.Clamped(settings.totalPlayers))
                && (ready || lobby.SetJoinable(false));
        }

        private void OnClientAuthenticated() => clientAuthenticated = true;

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            if (state == SessionState.Menu || state == SessionState.Leaving) return;
            if (role == SessionRole.Guest)
            {
                string why;
                if (auth != null && auth.ClientResultReceived && !auth.ClientAccepted)
                    why = AdmissionRejectionText.ToMessage(auth.LastClientRejection);
                else if (state == SessionState.ConnectingClient)
                    // Dropped before the transport ever reported Started: the socket
                    // cap or an unreachable host, and the transport does not say which.
                    why = "Could not connect: room may be full or unavailable.";
                else
                    why = "Disconnected from the host. Host or join again.";
                Leave(why);
            }
            else if (role == SessionRole.Host)
            {
                // A host without its own client is a headless zombie room; close it.
                Leave("Host connection lost. Host or join again.");
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            if (role == SessionRole.Host && state != SessionState.Menu && state != SessionState.Leaving)
                Leave("Server stopped. Host or join again.");
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (InRoom) RefreshMembers();
        }

        // ---- lobby membership -----------------------------------------------------

        private void OnLobbyMembersChanged(ulong changedLobby)
        {
            if (changedLobby != lobbyId || lobbyId == 0) return;
            if (role == SessionRole.Guest && (state == SessionState.InRoom || state == SessionState.ConnectingClient ||
                                              state == SessionState.Authenticating || state == SessionState.WaitingForPlayer))
            {
                if (!lobby.IsMember(lobbyId, originalHostSteamId) || lobby.Owner(lobbyId) != originalHostSteamId)
                {
                    Leave("Host closed the room. Host or join again.");
                    return;
                }
            }
            if (role == SessionRole.Host && InRoom)
            {
                if (membershipCheck != null) StopCoroutine(membershipCheck);
                membershipCheck = StartCoroutine(DisconnectDepartedMembers());
            }
            RefreshMembers();
        }

        private IEnumerator DisconnectDepartedMembers()
        {
            yield return new WaitForSecondsRealtime(settings.membershipGrace);
            membershipCheck = null;
            if (!InRoom || role != SessionRole.Host || auth == null) yield break;
            foreach (int connectionId in auth.AdmittedConnectionsNotInLobby())
            {
                if (networkManager.ServerManager.Clients.TryGetValue(connectionId, out NetworkConnection connection) &&
                    PeerSteamId(connectionId) != originalHostSteamId)
                    connection.Disconnect(false);
            }
        }

        private void OnLobbyDataChanged(ulong changedLobby)
        {
            if (changedLobby != lobbyId || lobbyId == 0 || role != SessionRole.Guest || !InRoom) return;
            string session = lobby.GetData(lobbyId, LobbyMetadata.KeySession);
            string ready = lobby.GetData(lobbyId, LobbyMetadata.KeyReady);
            string host = lobby.GetData(lobbyId, LobbyMetadata.KeyHost);
            if (session != sessionId || ready != "1" || host != LobbyMetadata.FormatULong(originalHostSteamId))
                Leave("Room changed or closed. Host or join again.");
        }

        public void RefreshMembers()
        {
            members.Clear();
            if (boundMode == SessionMode.Steam && lobbyId != 0 && lobby != null)
            {
                ulong self = steam.LocalSteamId;
                foreach (ulong id in lobby.Members(lobbyId))
                {
                    members.Add(new MemberInfo
                    {
                        SteamId = id,
                        OwnerClientId = -1,
                        Name = SanitizeName(steam.PersonaName(id)),
                        IsHost = id == originalHostSteamId,
                        IsSelf = id == self
                    });
                }
                return;
            }
            foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
            {
                int owner = player.OwnerId;
                SunkCost.Player.PlayerIdentity identity = player.GetComponent<SunkCost.Player.PlayerIdentity>();
                members.Add(new MemberInfo
                {
                    SteamId = 0,
                    OwnerClientId = owner,
                    Name = identity != null ? $"<color={identity.ColourHex}>■</color> {identity.DisplayName}" : SunkCost.Player.PlayerIdentity.Fallback(owner),
                    IsHost = false,
                    IsSelf = player.IsOwner
                });
            }
            members.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Steam user";
            var chars = new List<char>(name.Length);
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                chars.Add(c);
                if (chars.Count >= 64) break;
            }
            return chars.Count == 0 ? "Steam user" : new string(chars.ToArray());
        }

        // ---- snapshot ---------------------------------------------------------------

        public int PlayersInHq => FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).Length;

        public SessionSnapshot Snapshot()
        {
            return new SessionSnapshot
            {
                SelectedMode = selectedMode,
                BoundMode = boundMode,
                State = state,
                Role = role,
                OperationId = operationId,
                SessionId = sessionId,
                LobbyId = lobbyId,
                OriginalHostSteamId = originalHostSteamId,
                SteamInitialized = SteamInitialized,
                ServerStarted = networkManager != null && networkManager.ServerManager != null && networkManager.ServerManager.Started,
                ClientStarted = networkManager != null && networkManager.ClientManager != null && networkManager.ClientManager.Started,
                ClientAuthenticated = clientAuthenticated,
                LocalPlayerReady = LocalPlayer() != null,
                LobbyMembers = lobbyId != 0 && lobby != null ? lobby.MemberCount(lobbyId) : 0,
                PlayersInHq = PlayersInHq,
                AdmittedPlayers = auth != null ? auth.AdmittedCount : 0,
                TotalPlayers = settings.Clamped(settings.totalPlayers),
                Message = message
            };
        }

        private static HQPlayerController LocalPlayer()
        {
            foreach (HQPlayerController player in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
                if (player.IsOwner) return player;
            return null;
        }

        private void SetPreviewCameraActive(bool active)
        {
            if (previewCamera == null) return;
            previewCamera.enabled = active;
            AudioListener listener = previewCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }

        private static string JoinFailureMessage(EChatRoomEnterResponse response, bool ioFailure)
        {
            if (ioFailure) return "Could not join Steam lobby (I/O failure).";
            switch (response)
            {
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull: return "Room full.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist: return "Room no longer exists.";
                // Friends-only lobby: Steam refuses anyone not on the host's friends
                // list, even with the right code. Say so, or the playtest evening turns
                // into "the code doesn't work".
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed: return "Not allowed: you need to be a Steam friend of the host, or ask them for an invite.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned: return "This room is not available to this account.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseCommunityBan:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember: return "An account restriction prevents joining this room.";
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseRatelimitExceeded: return "Too many join attempts; try again shortly.";
                default: return "Could not join Steam lobby (" + response + ").";
            }
        }

        // ---- final shutdown -------------------------------------------------------

        private void OnApplicationQuit() => FinalShutdown();
        private void OnDestroy() => FinalShutdown();

        private bool shutDown;

        // Network first, then the transport's own shutdown, then Steam. Fishy closes
        // Steam sockets in its Shutdown(), which needs a live Steam API.
        private void FinalShutdown()
        {
            if (shutDown) return;
            shutDown = true;
            try
            {
                if (networkManager != null)
                {
                    if (networkManager.ClientManager != null && networkManager.ClientManager.Started) networkManager.ClientManager.StopConnection();
                    if (networkManager.ServerManager != null && networkManager.ServerManager.Started) networkManager.ServerManager.StopConnection(true);
                }
                if (steamTransport != null) steamTransport.Shutdown();
                lobby?.Leave();
                lobby?.Dispose();
                lobby = null;
            }
            finally
            {
                if (steam != null) steam.ShutdownFinal();
            }
        }
    }
}
