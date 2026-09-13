using System;
using System.Globalization;
using Steamworks;

namespace SunkCost.Net
{
    // Lobby key schema, parsing and validation (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 6). Pure: no Steam API calls. CSteamID is used only for bit checks.
    public static class LobbyMetadata
    {
        public const string KeyGame = "sc_game";
        public const string KeyProtocol = "sc_protocol";
        public const string KeyBuild = "sc_build";
        public const string KeyHost = "sc_host";
        public const string KeySession = "sc_session";
        public const string KeyReady = "sc_ready";
        public const string KeyCapacity = "sc_capacity";

        public const int SessionLength = 32;
        private const int MaxValueLength = 128;

        public struct Parsed
        {
            public string Game;
            public int Protocol;
            public string Build;
            public ulong HostSteamId;
            public string Session;
            public bool Ready;
            public int Capacity;
        }

        public static string NewSessionId() => Guid.NewGuid().ToString("N");

        public static bool IsValidSessionId(string value)
        {
            if (value == null || value.Length != SessionLength) return false;
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex) return false;
            }
            return true;
        }

        // Trimmed unsigned decimal that is a Steam lobby id. A user's SteamID64 is a
        // valid CSteamID but not a lobby, so it is rejected here with a clear reason.
        public static bool TryParseLobbyId(string text, out ulong lobbyId, out string error)
        {
            lobbyId = 0;
            string trimmed = text?.Trim() ?? string.Empty;
            if (trimmed.Length == 0) { error = "Enter a Steam lobby ID"; return false; }
            if (!ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) || value == 0)
            {
                error = "Enter a Steam lobby ID";
                return false;
            }
            var id = new CSteamID(value);
            if (!id.IsValid() || !id.IsLobby())
            {
                error = id.BIndividualAccount() ? "That is a user ID, not a lobby ID" : "Enter a Steam lobby ID";
                return false;
            }
            lobbyId = value;
            error = string.Empty;
            return true;
        }

        public static bool IsIndividualAccount(ulong steamId)
        {
            var id = new CSteamID(steamId);
            return id.IsValid() && id.BIndividualAccount();
        }

        // Reads every key through `read` (usually SteamMatchmaking.GetLobbyData bound
        // to a lobby). Fails closed on anything missing, oversized or malformed.
        public static bool TryParse(Func<string, string> read, out Parsed parsed, out string error)
        {
            parsed = default;
            if (!ReadBounded(read, KeyGame, PrototypeBuildIdentity.MaxProjectLength, out parsed.Game, out error)) return false;
            if (!ReadBounded(read, KeyProtocol, 10, out string protocolText, out error)) return false;
            if (!int.TryParse(protocolText, NumberStyles.None, CultureInfo.InvariantCulture, out parsed.Protocol) || parsed.Protocol <= 0)
            {
                error = "Lobby protocol is malformed"; return false;
            }
            if (!ReadBounded(read, KeyBuild, PrototypeBuildIdentity.MaxBuildLength, out parsed.Build, out error)) return false;
            if (!ReadBounded(read, KeyHost, 20, out string hostText, out error)) return false;
            if (!ulong.TryParse(hostText, NumberStyles.None, CultureInfo.InvariantCulture, out parsed.HostSteamId) || !IsIndividualAccount(parsed.HostSteamId))
            {
                error = "Lobby host id is malformed"; return false;
            }
            if (!ReadBounded(read, KeySession, SessionLength, out parsed.Session, out error)) return false;
            if (!IsValidSessionId(parsed.Session)) { error = "Lobby session id is malformed"; return false; }
            if (!ReadBounded(read, KeyReady, 1, out string readyText, out error)) return false;
            if (readyText != "0" && readyText != "1") { error = "Lobby ready flag is malformed"; return false; }
            parsed.Ready = readyText == "1";
            if (!ReadBounded(read, KeyCapacity, 2, out string capacityText, out error)) return false;
            if (!int.TryParse(capacityText, NumberStyles.None, CultureInfo.InvariantCulture, out parsed.Capacity) ||
                parsed.Capacity < 1 || parsed.Capacity > LobbySessionSettings.MaxTotalPlayers)
            {
                error = "Lobby capacity is malformed"; return false;
            }
            error = string.Empty;
            return true;
        }

        // Null when the lobby is one we can join with this build; otherwise a reason
        // for the player. Membership and ownership are checked by the caller with
        // live Steam data.
        public static string Validate(Parsed parsed, PrototypeBuildIdentity local)
        {
            if (local == null) return "No build identity";
            switch (local.Compare(parsed.Game, parsed.Protocol, parsed.Build))
            {
                case AdmissionRejection.WrongProject: return "Wrong game (another App ID 480 lobby)";
                case AdmissionRejection.WrongProtocol: return "Wrong protocol";
                case AdmissionRejection.WrongBuild: return "Wrong build";
            }
            if (!parsed.Ready) return "Host is not ready yet";
            return null;
        }

        private static bool ReadBounded(Func<string, string> read, string key, int maxLength, out string value, out string error)
        {
            value = read(key) ?? string.Empty;
            if (value.Length == 0) { error = "Lobby is missing " + key; return false; }
            if (value.Length > maxLength || value.Length > MaxValueLength) { error = "Lobby value too long: " + key; return false; }
            foreach (char c in value)
            {
                if (char.IsControl(c)) { error = "Lobby value malformed: " + key; return false; }
            }
            error = string.Empty;
            return true;
        }

        public static string FormatBool(bool value) => value ? "1" : "0";
        public static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);
        public static string FormatULong(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
