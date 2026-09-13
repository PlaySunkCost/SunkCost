using System;
using System.Collections.Generic;
using SunkCost.Net;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Repeatable pure checks for the lobby work (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 12): metadata parsing, build identity comparison, lobby id parsing,
    // seat accounting. No live Steam, no network. Throws on the first failure with
    // the case name; run from the menu or from an MCP command.
    public static class HQPrototypeLobbyChecks
    {
        [MenuItem("Sunk Cost/Prototype/Run lobby checks")]
        public static void RunFromMenu()
        {
            RunOrThrow();
            Debug.Log("Lobby checks passed.");
        }

        public static void RunOrThrow()
        {
            LobbyIdParsing();
            SessionIds();
            MetadataParsing();
            MetadataValidation();
            IdentityComparison();
            SettingsCaps();
            SeatLedger();
        }

        private static readonly PrototypeBuildIdentity CleanIdentity = new()
        {
            project = PrototypeBuildIdentity.Project,
            protocol = PrototypeBuildIdentity.Protocol,
            revision = "0123456789abcdef0123456789abcdef01234567",
            localOnly = false
        };

        private const ulong SomeLobbyId = 109775241000000001UL; // lobby universe/type bits set
        private const ulong SomeUserId = 76561198224265932UL;

        private static void LobbyIdParsing()
        {
            Expect("lobby id: whitespace", !LobbyMetadata.TryParseLobbyId("   ", out _, out _));
            Expect("lobby id: zero", !LobbyMetadata.TryParseLobbyId("0", out _, out _));
            Expect("lobby id: overflow", !LobbyMetadata.TryParseLobbyId("99999999999999999999999", out _, out _));
            Expect("lobby id: negative", !LobbyMetadata.TryParseLobbyId("-5", out _, out _));
            Expect("lobby id: user steam id rejected", !LobbyMetadata.TryParseLobbyId(SomeUserId.ToString(), out _, out string userError));
            Expect("lobby id: user steam id names the problem", userError.Contains("user ID"));
            Expect("lobby id: valid with padding", LobbyMetadata.TryParseLobbyId("  " + SomeLobbyId + "\n", out ulong parsed, out _) && parsed == SomeLobbyId);
        }

        private static void SessionIds()
        {
            string id = LobbyMetadata.NewSessionId();
            Expect("session id: generated is valid", LobbyMetadata.IsValidSessionId(id));
            Expect("session id: two differ", id != LobbyMetadata.NewSessionId());
            Expect("session id: uppercase rejected", !LobbyMetadata.IsValidSessionId(id.ToUpperInvariant()));
            Expect("session id: short rejected", !LobbyMetadata.IsValidSessionId(id.Substring(1)));
        }

        private static Dictionary<string, string> GoodLobby(bool ready = true) => new()
        {
            [LobbyMetadata.KeyGame] = PrototypeBuildIdentity.Project,
            [LobbyMetadata.KeyProtocol] = "1",
            [LobbyMetadata.KeyBuild] = CleanIdentity.revision,
            [LobbyMetadata.KeyHost] = SomeUserId.ToString(),
            [LobbyMetadata.KeySession] = "0123456789abcdef0123456789abcdef",
            [LobbyMetadata.KeyReady] = ready ? "1" : "0",
            [LobbyMetadata.KeyCapacity] = "4"
        };

        private static Func<string, string> Reader(Dictionary<string, string> data) =>
            key => data.TryGetValue(key, out string value) ? value : string.Empty;

        private static void MetadataParsing()
        {
            Expect("metadata: good parses", LobbyMetadata.TryParse(Reader(GoodLobby()), out LobbyMetadata.Parsed good, out _));
            Expect("metadata: host parsed", good.HostSteamId == SomeUserId && good.Ready && good.Capacity == 4);

            foreach (string key in GoodLobby().Keys)
            {
                var missing = GoodLobby();
                missing.Remove(key);
                Expect("metadata: missing " + key, !LobbyMetadata.TryParse(Reader(missing), out _, out _));
            }

            var oversized = GoodLobby();
            oversized[LobbyMetadata.KeyBuild] = new string('a', PrototypeBuildIdentity.MaxBuildLength + 1);
            Expect("metadata: oversized build", !LobbyMetadata.TryParse(Reader(oversized), out _, out _));

            var control = GoodLobby();
            control[LobbyMetadata.KeyGame] = "sunk\ncost";
            Expect("metadata: control chars", !LobbyMetadata.TryParse(Reader(control), out _, out _));

            var lobbyAsHost = GoodLobby();
            lobbyAsHost[LobbyMetadata.KeyHost] = SomeLobbyId.ToString();
            Expect("metadata: lobby id as host rejected", !LobbyMetadata.TryParse(Reader(lobbyAsHost), out _, out _));

            var badCapacity = GoodLobby();
            badCapacity[LobbyMetadata.KeyCapacity] = "9";
            Expect("metadata: capacity above max", !LobbyMetadata.TryParse(Reader(badCapacity), out _, out _));

            var badReady = GoodLobby();
            badReady[LobbyMetadata.KeyReady] = "yes";
            Expect("metadata: ready flag malformed", !LobbyMetadata.TryParse(Reader(badReady), out _, out _));
        }

        private static void MetadataValidation()
        {
            LobbyMetadata.TryParse(Reader(GoodLobby()), out LobbyMetadata.Parsed good, out _);
            Expect("validate: good is null", LobbyMetadata.Validate(good, CleanIdentity) == null);

            var foreign = GoodLobby();
            foreign[LobbyMetadata.KeyGame] = "other-game";
            LobbyMetadata.TryParse(Reader(foreign), out LobbyMetadata.Parsed foreignParsed, out _);
            Expect("validate: foreign 480 marker", (LobbyMetadata.Validate(foreignParsed, CleanIdentity) ?? "").StartsWith("Wrong game"));

            var otherBuild = GoodLobby();
            otherBuild[LobbyMetadata.KeyBuild] = "fedcba9876543210fedcba9876543210fedcba98";
            LobbyMetadata.TryParse(Reader(otherBuild), out LobbyMetadata.Parsed otherParsed, out _);
            Expect("validate: wrong build", LobbyMetadata.Validate(otherParsed, CleanIdentity) == "Wrong build");

            var otherProtocol = GoodLobby();
            otherProtocol[LobbyMetadata.KeyProtocol] = "2";
            LobbyMetadata.TryParse(Reader(otherProtocol), out LobbyMetadata.Parsed protoParsed, out _);
            Expect("validate: wrong protocol", LobbyMetadata.Validate(protoParsed, CleanIdentity) == "Wrong protocol");

            LobbyMetadata.TryParse(Reader(GoodLobby(ready: false)), out LobbyMetadata.Parsed notReady, out _);
            Expect("validate: not ready", LobbyMetadata.Validate(notReady, CleanIdentity) != null);
            Expect("validate: null identity", LobbyMetadata.Validate(good, null) != null);
        }

        private static void IdentityComparison()
        {
            Expect("identity: clean usable for steam", CleanIdentity.IsUsableForSteam(out _));
            var localDev = new PrototypeBuildIdentity { revision = PrototypeBuildIdentity.LocalDevPrefix + CleanIdentity.revision, localOnly = true };
            Expect("identity: local-dev refused for steam", !localDev.IsUsableForSteam(out string reason) && reason.Contains("local-development"));
            var shortSha = new PrototypeBuildIdentity { revision = "abc123" };
            Expect("identity: short revision refused", !shortSha.IsUsableForSteam(out _));
            Expect("identity: compare same", CleanIdentity.Compare(PrototypeBuildIdentity.Project, 1, CleanIdentity.revision) == AdmissionRejection.None);
            Expect("identity: compare project", CleanIdentity.Compare("x", 1, CleanIdentity.revision) == AdmissionRejection.WrongProject);
            Expect("identity: compare protocol", CleanIdentity.Compare(PrototypeBuildIdentity.Project, 2, CleanIdentity.revision) == AdmissionRejection.WrongProtocol);
            Expect("identity: compare build case-sensitive", CleanIdentity.Compare(PrototypeBuildIdentity.Project, 1, CleanIdentity.revision.ToUpperInvariant()) == AdmissionRejection.WrongBuild);
            string json = CleanIdentity.ToJson();
            var roundTrip = JsonUtility.FromJson<PrototypeBuildIdentity>(json);
            Expect("identity: json round trip", roundTrip.revision == CleanIdentity.revision && roundTrip.StructuralError() == null);
        }

        private static void SettingsCaps()
        {
            var settings = new LobbySessionSettings { totalPlayers = 4 };
            Expect("settings: steam remote cap 3", settings.SteamRemoteClientCap == 3);
            Expect("settings: local socket cap 4", settings.LocalSocketCap == 4);
            settings.totalPlayers = 9;
            Expect("settings: clamped", settings.SteamRemoteClientCap == 3 && settings.LocalSocketCap == 4);
            settings.totalPlayers = 1;
            Expect("settings: solo host", settings.SteamRemoteClientCap == 0 && settings.LocalSocketCap == 1);
        }

        private static void SeatLedger()
        {
            var ledger = new PrototypeAuthenticator.SeatLedger(4);
            Expect("ledger: reserve four", ledger.TryReserve(1, 100) && ledger.TryReserve(2, 200) && ledger.TryReserve(3, 300) && ledger.TryReserve(4, 400));
            Expect("ledger: fifth refused", !ledger.TryReserve(5, 500));
            Expect("ledger: count", ledger.Count == 4);
            Expect("ledger: duplicate identity refused", !ledger.TryReserve(6, 100));
            Expect("ledger: duplicate connection refused", !ledger.TryReserve(1, 999));
            ledger.Release(2);
            Expect("ledger: released frees exactly one", ledger.Count == 3 && ledger.TryReserve(7, 700) && !ledger.TryReserve(8, 800));
            ledger.Release(2);
            Expect("ledger: double release harmless", ledger.Count == 4);
            ledger.Clear();
            Expect("ledger: clear", ledger.Count == 0 && ledger.TryReserve(1, 100));
        }

        private static void Expect(string caseName, bool condition)
        {
            if (!condition) throw new InvalidOperationException("Lobby check failed: " + caseName);
        }
    }
}
