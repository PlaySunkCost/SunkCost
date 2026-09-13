using System;
using Steamworks;
using UnityEngine;

namespace SunkCost.Net
{
    // The only owner of the Steam client lifetime: initialization, the once-per-frame
    // callback pump, the global invite/overlay listeners that must survive between
    // rooms, and the final shutdown (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 5, "Steam lifetime"). Nothing else calls SteamAPI.Init/RunCallbacks/
    // Shutdown. Lives on the always-active session UI object.
    public sealed class SteamBootstrap : MonoBehaviour
    {
        public bool Initialized { get; private set; }
        public bool OverlayActive { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        // Raised on the Unity thread with the lobby id from a Steam invite or a
        // friends-list join. The session controller decides what to do with it.
        public event Action<ulong> OnInviteRequested;
        public event Action<bool> OnOverlayActivated;

        private Callback<GameLobbyJoinRequested_t> joinRequested;
        private Callback<GameOverlayActivated_t> overlayActivated;
        private bool shutDown;

        public ulong LocalSteamId => Initialized ? SteamUser.GetSteamID().m_SteamID : 0;
        public string LocalPersonaName => Initialized ? SteamFriends.GetPersonaName() : string.Empty;

        public bool TryInitialize(out string error)
        {
            if (Initialized) { error = string.Empty; return true; }
            if (shutDown) { error = "Steam was shut down; restart the game to use Steam again."; LastError = error; return false; }
            try
            {
                Initialized = SteamAPI.Init();
            }
            catch (Exception exception)
            {
                Initialized = false;
                error = "Steam initialization failed: " + exception.Message;
                LastError = error;
                return false;
            }
            if (!Initialized)
            {
                error = "Steam initialization failed. Ensure Steam is running and steam_appid.txt exists.";
                LastError = error;
                return false;
            }
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            overlayActivated = Callback<GameOverlayActivated_t>.Create(OnGameOverlayActivated);
            LastError = string.Empty;
            error = string.Empty;
            return true;
        }

        public bool IsOverlayEnabled => Initialized && SteamUtils.IsOverlayEnabled();

        public string PersonaName(ulong steamId)
        {
            if (!Initialized) return "Steam user";
            string name = SteamFriends.GetFriendPersonaName(new CSteamID(steamId));
            return string.IsNullOrEmpty(name) ? "Steam user" : name;
        }

        private void Update()
        {
            if (Initialized) SteamAPI.RunCallbacks();
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            // Only the lobby matters; an invitation may be forwarded, so the sender's
            // user id is never treated as a host address.
            OnInviteRequested?.Invoke(callback.m_steamIDLobby.m_SteamID);
        }

        private void OnGameOverlayActivated(GameOverlayActivated_t callback)
        {
            OverlayActive = callback.m_bActive != 0;
            OnOverlayActivated?.Invoke(OverlayActive);
        }

        // Final process exit only. The session controller stops FishNet and calls the
        // transport's own Shutdown() first, because FishySteamworks closes its Steam
        // sockets on shutdown and that needs a live Steam API.
        public void ShutdownFinal()
        {
            if (!Initialized) return;
            joinRequested?.Dispose();
            overlayActivated?.Dispose();
            joinRequested = null;
            overlayActivated = null;
            SteamAPI.Shutdown();
            Initialized = false;
            shutDown = true;
        }
    }
}
