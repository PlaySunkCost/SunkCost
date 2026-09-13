using System.Linq;
using FishNet.Connection;
using FishNet.Managing;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Verification-only helpers for exercising server-authoritative paths from MCP/editor
    // commands without driving real input in a second standalone process. Section 13/16 of
    // docs/HQ_BASKETBALL_IMPLEMENTATION_PLAN.md still require the real runtime checks below;
    // these hooks make that possible for a single connected editor plus one joined build.
    public static class HQPrototypeTestHooks
    {
        // Grants the ball to the first connected client that is not the local host
        // connection, so its disconnect-handoff behavior can be exercised without
        // remote-controlling the standalone build's input.
        public static string ServerGrabForNonHostClient()
        {
            NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            HQPlayerController[] players = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None);
            if (nm == null || ball == null) return "Missing NetworkManager or Basketball in the loaded scene.";
            if (!nm.IsServerStarted) return "Server is not started; run this from the host.";

            int hostClientId = nm.ClientManager.Connection.ClientId;
            NetworkConnection joiner = nm.ServerManager.Clients.Values.FirstOrDefault(c => c.ClientId != hostClientId);
            if (joiner == null) return "No non-host client connection found.";

            HQPlayerController joinerPlayer = players.FirstOrDefault(p => p.Owner.ClientId == joiner.ClientId);
            if (joinerPlayer == null) return $"No player controller owned by connection {joiner.ClientId}.";

            bool grabbed = ball.ServerTryGrab(joiner, joinerPlayer);
            return $"grabbed={grabbed}; holderClientId={ball.HolderClientId}; isHeld={ball.IsHeld}";
        }

        public static string ConnectionDiagnostics()
        {
            NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
            HQPlayerController[] players = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None);
            if (nm == null) return "No NetworkManager found.";
            string clients = string.Join(",", nm.ServerManager.Clients.Keys);
            string owners = string.Join(",", players.Select(p => p.Owner.ClientId));
            return $"localClientId={nm.ClientManager.Connection.ClientId}; serverClientsKeys=[{clients}]; playerOwnerIds=[{owners}]";
        }

        // Moves the host's own player next to the ball's current position and issues a
        // normal grab request on its behalf, exercising the same ServerTryGrab path a
        // real client's input would use, to confirm the ball is actually recoverable
        // (not just "state == Free") after its previous holder disconnected.
        public static string RecoverBallForHost()
        {
            NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            HQPlayerController[] players = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None);
            if (nm == null || ball == null) return "Missing NetworkManager or Basketball in the loaded scene.";

            int hostClientId = nm.ClientManager.Connection.ClientId;
            HQPlayerController host = players.FirstOrDefault(p => p.Owner.ClientId == hostClientId);
            if (host == null) return $"No player controller owned by host connection {hostClientId}.";

            host.transform.position = ball.transform.position + new Vector3(0.5f, 0f, 0f);
            bool grabbed = ball.ServerTryGrab(host.Owner, host);
            return $"grabbed={grabbed}; isHeld={ball.IsHeld}; holderClientId={ball.HolderClientId}";
        }

        public static string BallState()
        {
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            if (ball == null) return "No Basketball found (despawned or scene not loaded).";
            return $"spawned={ball.IsSpawned}; isHeld={ball.IsHeld}; holderClientId={ball.HolderClientId}; position={ball.transform.position}";
        }
    }
}
