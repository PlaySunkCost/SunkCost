using System;
using UnityEngine;

namespace SunkCost.Net
{
    // Editable session limits and deadlines (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 5). Serialized on PrototypeSessionUI so they can be tuned in the
    // inspector; the defaults apply when the scene has no override. Timeouts are
    // implementation defaults, not latency guarantees.
    [Serializable]
    public sealed class LobbySessionSettings
    {
        public const int MaxTotalPlayers = 4;

        [Tooltip("Humans in the room including the host. 1..4.")]
        [Range(1, MaxTotalPlayers)] public int totalPlayers = MaxTotalPlayers;

        [Tooltip("Seconds to wait for a Steam lobby create or join result.")]
        public float steamOperationTimeout = 20f;

        [Tooltip("Seconds to wait for the FishNet server to report Started.")]
        public float serverStartTimeout = 5f;

        [Tooltip("Seconds to wait for the transport connection to report Started.")]
        public float connectTimeout = 20f;

        [Tooltip("Seconds a pending connection has to complete the admission handshake.")]
        public float authTimeout = 10f;

        [Tooltip("Seconds, within authTimeout, to wait for Steam lobby membership to catch up.")]
        public float membershipGrace = 2f;

        [Tooltip("Seconds to wait for the owned player object after authentication.")]
        public float playerSpawnTimeout = 10f;

        [Tooltip("Seconds to wait for a clean disconnect before forcing the menu.")]
        public float cleanupTimeout = 5f;

        public int Clamped(int total) => Mathf.Clamp(total, 1, MaxTotalPlayers);

        // Transport caps are not the same number: FishySteamworks counts remote
        // sockets and adds its own host connection separately, Tugboat counts the
        // host's loopback socket as a client.
        public int SteamRemoteClientCap => Clamped(totalPlayers) - 1;
        public int LocalSocketCap => Clamped(totalPlayers);
    }
}
