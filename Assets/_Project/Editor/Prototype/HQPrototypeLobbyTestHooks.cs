using SunkCost.Net;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // MCP/editor verification helpers for the lobby work
    // (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md section 12). Every operation goes
    // through PrototypeSessionController's public API; nothing here bypasses the
    // admission handshake or writes session state directly.
    public static class HQPrototypeLobbyTestHooks
    {
        private static PrototypeSessionController Controller()
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            return ui != null ? ui.Controller : null;
        }

        public static string Snapshot()
        {
            PrototypeSessionController c = Controller();
            return c == null ? "No session controller." : c.Snapshot().ToText();
        }

        public static string SelectMode(bool steam)
        {
            PrototypeSessionController c = Controller();
            if (c == null) return "No session controller.";
            bool ok = c.SelectMode(steam ? SessionMode.Steam : SessionMode.Local, out string error);
            return ok ? "selected " + c.SelectedMode : "refused: " + error;
        }

        public static string StartLocalHost()
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            if (ui == null) return "No PrototypeSessionUI.";
            ui.StartLocalHost();
            return Snapshot();
        }

        public static string JoinLocal(string address)
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            if (ui == null) return "No PrototypeSessionUI.";
            ui.JoinLocal(address);
            return Snapshot();
        }

        public static string StartSteamHost()
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            if (ui == null) return "No PrototypeSessionUI.";
            ui.StartSteamHost();
            return Snapshot();
        }

        public static string JoinSteamLobby(string lobbyId)
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            if (ui == null) return "No PrototypeSessionUI.";
            ui.JoinSteamLobby(lobbyId);
            return Snapshot();
        }

        public static string Leave()
        {
            PrototypeSessionController c = Controller();
            if (c == null) return "No session controller.";
            c.Leave();
            return Snapshot();
        }

        public static string Members()
        {
            PrototypeSessionController c = Controller();
            if (c == null) return "No session controller.";
            c.RefreshMembers();
            var sb = new System.Text.StringBuilder();
            foreach (PrototypeSessionController.MemberInfo m in c.Members)
                sb.Append(m.Name).Append(m.IsHost ? " (host)" : "").Append(m.IsSelf ? " (you)" : "")
                  .Append(" steam=").Append(m.SteamId).Append(" owner=").Append(m.OwnerClientId).Append('\n');
            return sb.Length == 0 ? "(no members)" : sb.ToString().TrimEnd();
        }

        // Editor-only: the identity Play Mode is running with.
        public static string BuildIdentity()
        {
            PrototypeBuildIdentity id = PrototypeBuildIdentity.Current;
            return id == null ? "none: " + PrototypeBuildIdentity.LoadError : id.ToJson();
        }

        // Simulates a wrong build on this peer for the next handshake, editor only.
        // Restores with RestoreBuildIdentity.
        private static PrototypeBuildIdentity savedIdentity;

        public static string UseWrongBuildIdentity()
        {
            savedIdentity = PrototypeBuildIdentity.Current;
            if (savedIdentity == null) return "No identity to alter.";
            PrototypeBuildIdentity.Set(new PrototypeBuildIdentity
            {
                project = savedIdentity.project,
                protocol = savedIdentity.protocol,
                revision = "ffffffffffffffffffffffffffffffffffffffff",
                localOnly = savedIdentity.localOnly
            });
            return "identity revision replaced";
        }

        public static string RestoreBuildIdentity()
        {
            if (savedIdentity == null) return "nothing saved";
            PrototypeBuildIdentity.Set(savedIdentity);
            savedIdentity = null;
            return "identity restored";
        }
    }
}
