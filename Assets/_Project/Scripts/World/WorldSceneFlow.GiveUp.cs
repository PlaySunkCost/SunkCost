using FishNet.Connection;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // GIVE UP at the HQ board (Dan, 27 September 2026): every player in the crew must
    // press it; a press toggles that player's vote and the board shows "GIVE UP 1/3".
    // All agreed: the run is lost as a missed quota is, the crew walks the plank. The
    // votes clear when the ship sails or anyone joins or leaves. Server only.
    public sealed partial class WorldSceneFlow
    {
        public bool ServerToggleGiveUp(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (transitioning || dayState.Travelling) { why = "Ship travelling"; return false; }
            if (currentWorld != WorldId.HQ || dayState.Phase != DayPhase.AtHQ) { why = "Not docked at HQ"; return false; }
            HQPlayerController presser = PlayerOf(sender);
            if (presser == null || presser.gameObject.scene != WorldScenes.Scene(WorldId.HQ)) { why = "Not at HQ"; return false; }
            int crew = ServerCrewCount();
            bool voted = dayState.ServerToggleGiveUp(sender.ClientId, crew);
            Debug.Log($"[GiveUp] {DisplayName(sender)} {(voted ? "votes to give up" : "takes the vote back")}: {dayState.GiveUpVotes}/{crew}");
            if (crew > 0 && dayState.GiveUpVotes >= crew)
            {
                Debug.Log($"[GiveUp] The whole crew agreed ({crew}): the run is over");
                dayState.ServerGiveUp();
                ServerBeginPlank();
            }
            return true;
        }

        // Everyone connected with a diver counts: the vote needs them all.
        private int ServerCrewCount()
        {
            int crew = 0;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
                if (conn.IsActive && PlayerOf(conn) != null) crew++;
            return crew;
        }

        private void ServerClearGiveUp(string reason)
        {
            if (dayState == null || networkManager == null || !networkManager.IsServerStarted) return;
            if (dayState.GiveUpVotes == 0 && dayState.GiveUpCrew == 0) return;
            dayState.ServerClearGiveUp();
            Debug.Log("[GiveUp] Votes cleared: " + reason);
        }
    }
}
