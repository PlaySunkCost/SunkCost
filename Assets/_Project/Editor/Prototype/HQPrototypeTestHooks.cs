using System.Linq;
using System.Reflection;
using FishNet.Connection;
using FishNet.Managing;
using SunkCost.Interaction;
using SunkCost.Net;
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

        // --- Remote-client checks. Run these with the EDITOR JOINED AS A CLIENT to a
        // separately running host build, so they exercise the real client request path
        // (ServerRpc over the transport), not in-process host shortcuts.

        public static string ClientMoveLocalPlayerToBall()
        {
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            HQPlayerController local = LocalPlayer();
            if (ball == null || local == null) return "Missing ball or local player.";
            CharacterController cc = local.GetComponent<CharacterController>();
            Vector3 target = ball.transform.position + new Vector3(0.6f, 0.9f, 0f);
            // CharacterController overrides direct transform writes unless disabled.
            cc.enabled = false;
            local.transform.position = target;
            cc.enabled = true;
            return $"moved local player to {target}";
        }

        public static string ClientRequestGrab()
        {
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            HQPlayerController local = LocalPlayer();
            if (ball == null || local == null) return "Missing ball or local player.";
            MethodInfo rpc = typeof(HQPlayerController).GetMethod("ServerRequestGrab", BindingFlags.Instance | BindingFlags.NonPublic);
            if (rpc == null) return "ServerRequestGrab not found.";
            rpc.Invoke(local, new object[] { ball.NetworkObject, null });
            return "grab requested";
        }

        public static string ClientRequestRelease(bool throwBall)
        {
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            HQPlayerController local = LocalPlayer();
            if (ball == null || local == null) return "Missing ball or local player.";
            MethodInfo rpc = typeof(HQPlayerController).GetMethod("ServerRequestRelease", BindingFlags.Instance | BindingFlags.NonPublic);
            if (rpc == null) return "ServerRequestRelease not found.";
            rpc.Invoke(local, new object[] { ball.NetworkObject, local.transform.forward, throwBall, null });
            return throwBall ? "throw requested" : "drop requested";
        }

        // What the local player's input code will actually act on.
        public static string ClientHeldBallField()
        {
            HQPlayerController local = LocalPlayer();
            if (local == null) return "No local player.";
            FieldInfo field = typeof(HQPlayerController).GetField("heldBall", BindingFlags.Instance | BindingFlags.NonPublic);
            object value = field?.GetValue(local);
            return $"heldBall={(value == null ? "null" : "set")}; localClientId={local.Owner.ClientId}";
        }

        public static string SessionUiState()
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            if (ui == null) return "No PrototypeSessionUI.";
            System.Type t = typeof(PrototypeSessionUI);
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            Camera preview = (Camera)t.GetField("previewCamera", f).GetValue(ui);
            return $"sessionActive={t.GetField("sessionActive", f).GetValue(ui)}; isHost={t.GetField("isHost", f).GetValue(ui)}; " +
                   $"transportLocked={t.GetField("transportLocked", f).GetValue(ui)}; previewCamera={(preview == null ? "none" : preview.enabled.ToString())}; " +
                   $"status='{t.GetField("status", f).GetValue(ui)}'; {ui.RuntimeDiagnostics}";
        }

        private static HQPlayerController LocalPlayer()
        {
            return Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
        }

        public static string BallState()
        {
            Basketball ball = Object.FindFirstObjectByType<Basketball>();
            if (ball == null) return "No Basketball found (despawned or scene not loaded).";
            return $"spawned={ball.IsSpawned}; isHeld={ball.IsHeld}; holderClientId={ball.HolderClientId}; position={ball.transform.position}";
        }
    }
}
