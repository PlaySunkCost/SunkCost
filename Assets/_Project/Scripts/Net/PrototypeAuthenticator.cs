using System;
using System.Collections.Generic;
using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using Random = System.Random;

namespace SunkCost.Net
{
    // One server decision before a player spawns (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 9). The server challenges every new connection with the room's
    // identity; the client answers with its own; the server checks project,
    // protocol, build, session, lobby, nonce, transport-proven Steam membership and
    // the seat ledger, then tells the client the result and lets FishNet finish
    // authentication. The host's own connection goes through the same handshake.
    public sealed class PrototypeAuthenticator : Authenticator
    {
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        // Admitted seats keyed by connection id, each with the identity that holds
        // it (Steam id, or the connection id itself in Local mode). Pure, so the
        // editor checks can exercise it.
        public sealed class SeatLedger
        {
            private readonly int capacity;
            private readonly Dictionary<int, ulong> seats = new();

            public SeatLedger(int capacity) { this.capacity = Mathf.Max(1, capacity); }
            public int Count => seats.Count;
            public int Capacity => capacity;

            public bool TryReserve(int connectionId, ulong identity)
            {
                if (seats.ContainsKey(connectionId)) return false;
                if (seats.ContainsValue(identity)) return false;
                if (seats.Count >= capacity) return false;
                seats[connectionId] = identity;
                return true;
            }

            public bool HoldsIdentity(ulong identity) => seats.ContainsValue(identity);
            public bool TryGetIdentity(int connectionId, out ulong identity) => seats.TryGetValue(connectionId, out identity);
            public void Release(int connectionId) => seats.Remove(connectionId);
            public void Clear() => seats.Clear();
            public IEnumerable<KeyValuePair<int, ulong>> Seats => seats;
        }

        public sealed class ServerContext
        {
            public string Session;
            public ulong LobbyId;          // 0 in Local mode
            public bool Steam;
            public int TotalPlayers;
            public ulong HostSteamId;      // Steam mode only
            public Func<int, ulong> PeerSteamId;   // connection id -> transport-stored Steam id
            public Func<ulong, bool> IsLobbyMember;
        }

        public sealed class ClientExpectation
        {
            public string Session;   // null in Local mode: learned from the challenge
            public ulong LobbyId;
        }

        private sealed class Pending
        {
            public NetworkConnection Connection;
            public uint Nonce;
            public float Deadline;
            public bool HasHeldResponse;
            public AdmissionResponse HeldResponse;
            public float GraceDeadline;
        }

        public float AuthTimeout = 10f;
        public float MembershipGrace = 2f;

        // Server-side state.
        private ServerContext server;
        private SeatLedger ledger = new(1);
        private readonly Dictionary<int, Pending> pending = new();
        private readonly Random random = new();
        private bool hostSeatPending;
        private bool acceptingGuests;
        private int hostConnectionId = -1;

        // Client-side state.
        private ClientExpectation expected;
        public string LearnedSession { get; private set; } = string.Empty;
        public AdmissionRejection LastClientRejection { get; private set; }
        public bool ClientResultReceived { get; private set; }
        public bool ClientAccepted { get; private set; }
        public event Action<bool, AdmissionRejection> OnClientAdmission;

        public int AdmittedCount => ledger.Count;
        public int PendingCount => pending.Count;
        public bool AcceptingGuests => acceptingGuests;
        public IEnumerable<KeyValuePair<int, ulong>> AdmittedSeats => ledger.Seats;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            NetworkManager.ServerManager.RegisterBroadcast<AdmissionResponse>(OnServerReceivedResponse, requireAuthentication: false);
            NetworkManager.ClientManager.RegisterBroadcast<AdmissionChallenge>(OnClientReceivedChallenge);
            NetworkManager.ClientManager.RegisterBroadcast<AdmissionResult>(OnClientReceivedResult);
            NetworkManager.ServerManager.OnRemoteConnectionState += OnServerRemoteConnectionState;
        }

        private void OnDestroy()
        {
            if (NetworkManager == null) return;
            NetworkManager.ServerManager.UnregisterBroadcast<AdmissionResponse>(OnServerReceivedResponse);
            NetworkManager.ClientManager.UnregisterBroadcast<AdmissionChallenge>(OnClientReceivedChallenge);
            NetworkManager.ClientManager.UnregisterBroadcast<AdmissionResult>(OnClientReceivedResult);
            NetworkManager.ServerManager.OnRemoteConnectionState -= OnServerRemoteConnectionState;
        }

        // ---- server configuration -------------------------------------------------

        public void ConfigureServer(ServerContext context)
        {
            server = context;
            ledger = new SeatLedger(context.TotalPlayers);
            pending.Clear();
            hostSeatPending = true;
            acceptingGuests = false;
            hostConnectionId = -1;
        }

        public void SetAcceptingGuests(bool value) => acceptingGuests = value;

        public void ClearServer()
        {
            server = null;
            pending.Clear();
            ledger.Clear();
            hostSeatPending = false;
            acceptingGuests = false;
            hostConnectionId = -1;
        }

        // ---- client configuration -------------------------------------------------

        public void ConfigureClient(ClientExpectation expectation)
        {
            expected = expectation;
            LearnedSession = string.Empty;
            LastClientRejection = AdmissionRejection.None;
            ClientResultReceived = false;
            ClientAccepted = false;
        }

        public void ClearClient()
        {
            expected = null;
            LearnedSession = string.Empty;
        }

        // ---- server flow ----------------------------------------------------------

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            if (server == null)
            {
                Finish(connection, false, AdmissionRejection.Internal, 0);
                return;
            }
            var entry = new Pending
            {
                Connection = connection,
                Nonce = (uint)random.Next(1, int.MaxValue),
                Deadline = Time.unscaledTime + AuthTimeout
            };
            pending[connection.ClientId] = entry;
            PrototypeBuildIdentity local = PrototypeBuildIdentity.Current;
            var challenge = new AdmissionChallenge
            {
                Project = local?.project ?? string.Empty,
                Protocol = local?.protocol ?? 0,
                Build = local?.revision ?? string.Empty,
                Session = server.Session,
                LobbyId = server.LobbyId,
                Nonce = entry.Nonce
            };
            NetworkManager.ServerManager.Broadcast(connection, challenge, requireAuthenticated: false, Channel.Reliable);
        }

        private void OnServerReceivedResponse(NetworkConnection connection, AdmissionResponse response, Channel channel)
        {
            if (!pending.TryGetValue(connection.ClientId, out Pending entry) || entry.Connection != connection)
                return; // not a live pending connection: ignore duplicates and strays
            if (entry.HasHeldResponse)
                return; // one response per challenge
            Evaluate(entry, response, firstAttempt: true);
        }

        private void Evaluate(Pending entry, AdmissionResponse response, bool firstAttempt)
        {
            NetworkConnection connection = entry.Connection;
            if (server == null) { Finish(connection, false, AdmissionRejection.Internal, entry.Nonce); return; }
            if (response.Nonce != entry.Nonce) { Finish(connection, false, AdmissionRejection.BadNonce, entry.Nonce); return; }
            if (!Bounded(response.Project, PrototypeBuildIdentity.MaxProjectLength) ||
                !Bounded(response.Build, PrototypeBuildIdentity.MaxBuildLength) ||
                !Bounded(response.Session, LobbyMetadata.SessionLength))
            {
                Finish(connection, false, AdmissionRejection.WrongProject, entry.Nonce);
                return;
            }
            PrototypeBuildIdentity local = PrototypeBuildIdentity.Current;
            AdmissionRejection compat = local == null ? AdmissionRejection.Internal : local.Compare(response.Project, response.Protocol, response.Build);
            if (compat != AdmissionRejection.None) { Finish(connection, false, compat, entry.Nonce); return; }
            if (!string.Equals(response.Session, server.Session, StringComparison.Ordinal)) { Finish(connection, false, AdmissionRejection.WrongSession, entry.Nonce); return; }
            if (response.LobbyId != server.LobbyId) { Finish(connection, false, AdmissionRejection.WrongLobby, entry.Nonce); return; }

            ulong identity = (ulong)connection.ClientId;
            bool isHostCandidate = false;
            if (server.Steam)
            {
                identity = server.PeerSteamId != null ? server.PeerSteamId(connection.ClientId) : 0;
                if (identity == 0 || !LobbyMetadata.IsIndividualAccount(identity))
                {
                    Finish(connection, false, AdmissionRejection.NotLobbyMember, entry.Nonce);
                    return;
                }
                isHostCandidate = identity == server.HostSteamId;
                bool member = server.IsLobbyMember != null && server.IsLobbyMember(identity);
                if (!member)
                {
                    // Membership can lag the transport connect; hold within the grace
                    // window and retry from Update, never admit first.
                    if (firstAttempt)
                    {
                        entry.HasHeldResponse = true;
                        entry.HeldResponse = response;
                        entry.GraceDeadline = Mathf.Min(Time.unscaledTime + MembershipGrace, entry.Deadline);
                        return;
                    }
                    Finish(connection, false, AdmissionRejection.NotLobbyMember, entry.Nonce);
                    return;
                }
            }
            else
            {
                isHostCandidate = hostSeatPending;
            }

            if (isHostCandidate && hostSeatPending)
            {
                if (!ledger.TryReserve(connection.ClientId, identity)) { Finish(connection, false, AdmissionRejection.Internal, entry.Nonce); return; }
                hostSeatPending = false;
                hostConnectionId = connection.ClientId;
                Finish(connection, true, AdmissionRejection.None, entry.Nonce);
                return;
            }
            if (server.Steam && isHostCandidate)
            {
                // A second connection claiming the host's identity is never the host.
                Finish(connection, false, AdmissionRejection.DuplicateIdentity, entry.Nonce);
                return;
            }
            if (!acceptingGuests) { Finish(connection, false, AdmissionRejection.HostNotReady, entry.Nonce); return; }
            // Joining is between days only (design section 1); the day state is the
            // server's, so this is the one gameplay rule the handshake consults.
            if (SunkCost.World.CrewDayState.Instance != null && SunkCost.World.CrewDayState.Instance.RefusesJoins)
            { Finish(connection, false, AdmissionRejection.DiveInProgress, entry.Nonce); return; }
            // A temporary transition lock while the ship is under way, not a join policy.
            if (SunkCost.World.CrewDayState.Instance != null && SunkCost.World.CrewDayState.Instance.RefusesJoinsForTravel)
            { Finish(connection, false, AdmissionRejection.ShipTravelling, entry.Nonce); return; }
            if (ledger.HoldsIdentity(identity)) { Finish(connection, false, AdmissionRejection.DuplicateIdentity, entry.Nonce); return; }
            if (!ledger.TryReserve(connection.ClientId, identity)) { Finish(connection, false, AdmissionRejection.RoomFull, entry.Nonce); return; }
            Finish(connection, true, AdmissionRejection.None, entry.Nonce);
        }

        private void Finish(NetworkConnection connection, bool accepted, AdmissionRejection reason, uint nonce)
        {
            pending.Remove(connection.ClientId);
            var result = new AdmissionResult
            {
                Session = server?.Session ?? string.Empty,
                Nonce = nonce,
                Accepted = accepted,
                Reason = reason
            };
            // Result first, then FishNet's verdict: a rejected connection is
            // disconnected by ServerManager after buffered sends flush.
            NetworkManager.ServerManager.Broadcast(connection, result, requireAuthenticated: false, Channel.Reliable);
            OnAuthenticationResult?.Invoke(connection, accepted);
        }

        private void Update()
        {
            if (pending.Count == 0) return;
            float now = Time.unscaledTime;
            List<Pending> expired = null;
            List<Pending> retry = null;
            foreach (Pending entry in pending.Values)
            {
                if (now >= entry.Deadline) (expired ??= new List<Pending>()).Add(entry);
                else if (entry.HasHeldResponse) (retry ??= new List<Pending>()).Add(entry);
            }
            if (retry != null)
            {
                foreach (Pending entry in retry)
                {
                    bool member = server?.IsLobbyMember != null && server.PeerSteamId != null &&
                                  server.IsLobbyMember(server.PeerSteamId(entry.Connection.ClientId));
                    if (member || now >= entry.GraceDeadline)
                        Evaluate(entry, entry.HeldResponse, firstAttempt: false);
                }
            }
            if (expired != null)
                foreach (Pending entry in expired)
                    if (pending.ContainsKey(entry.Connection.ClientId))
                        Finish(entry.Connection, false, AdmissionRejection.Timeout, entry.Nonce);
        }

        private void OnServerRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            pending.Remove(connection.ClientId);
            ledger.Release(connection.ClientId);
            if (connection.ClientId == hostConnectionId) hostConnectionId = -1;
        }

        // Host-side membership refresh: an admitted Steam identity that is no longer
        // in the lobby is disconnected by the caller (the session controller).
        public List<int> AdmittedConnectionsNotInLobby()
        {
            var result = new List<int>();
            if (server == null || !server.Steam || server.IsLobbyMember == null) return result;
            foreach (KeyValuePair<int, ulong> seat in ledger.Seats)
                if (!server.IsLobbyMember(seat.Value)) result.Add(seat.Key);
            return result;
        }

        // ---- client flow ----------------------------------------------------------

        private void OnClientReceivedChallenge(AdmissionChallenge challenge, Channel channel)
        {
            if (expected == null) return;
            if (!Bounded(challenge.Project, PrototypeBuildIdentity.MaxProjectLength) ||
                !Bounded(challenge.Build, PrototypeBuildIdentity.MaxBuildLength) ||
                !LobbyMetadata.IsValidSessionId(challenge.Session))
            {
                ClientFail(AdmissionRejection.WrongProject);
                return;
            }
            PrototypeBuildIdentity local = PrototypeBuildIdentity.Current;
            if (local == null) { ClientFail(AdmissionRejection.Internal); return; }
            AdmissionRejection compat = local.Compare(challenge.Project, challenge.Protocol, challenge.Build);
            if (compat != AdmissionRejection.None) { ClientFail(compat); return; }
            if (expected.Session != null && !string.Equals(expected.Session, challenge.Session, StringComparison.Ordinal))
            {
                ClientFail(AdmissionRejection.WrongSession);
                return;
            }
            if (challenge.LobbyId != expected.LobbyId) { ClientFail(AdmissionRejection.WrongLobby); return; }
            LearnedSession = challenge.Session;
            var response = new AdmissionResponse
            {
                Project = local.project,
                Protocol = local.protocol,
                Build = local.revision,
                Session = challenge.Session,
                LobbyId = challenge.LobbyId,
                Nonce = challenge.Nonce
            };
            NetworkManager.ClientManager.Broadcast(response, Channel.Reliable);
        }

        private void OnClientReceivedResult(AdmissionResult result, Channel channel)
        {
            if (expected == null || ClientResultReceived) return;
            ClientResultReceived = true;
            ClientAccepted = result.Accepted;
            LastClientRejection = result.Accepted ? AdmissionRejection.None : result.Reason;
            OnClientAdmission?.Invoke(result.Accepted, LastClientRejection);
        }

        private void ClientFail(AdmissionRejection reason)
        {
            if (ClientResultReceived) return;
            ClientResultReceived = true;
            ClientAccepted = false;
            LastClientRejection = reason;
            OnClientAdmission?.Invoke(false, reason);
        }

        private static bool Bounded(string value, int max) => value != null && value.Length > 0 && value.Length <= max;
    }
}
