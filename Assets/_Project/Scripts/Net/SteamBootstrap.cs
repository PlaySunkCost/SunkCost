using System;
using System.Collections.Generic;
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

        public struct Friend
        {
            public ulong SteamId;
            public string Name;
            public bool Online;
        }

        // The local user's regular Steam friends, online ones first, for the in-game
        // invite list. This works without the overlay (the editor never has it).
        public List<Friend> Friends()
        {
            var result = new List<Friend>();
            if (!Initialized) return result;
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                CSteamID id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                result.Add(new Friend
                {
                    SteamId = id.m_SteamID,
                    Name = PersonaName(id.m_SteamID),
                    Online = SteamFriends.GetFriendPersonaState(id) != EPersonaState.k_EPersonaStateOffline
                });
            }
            result.Sort((a, b) => a.Online == b.Online ? string.CompareOrdinal(a.Name, b.Name) : (a.Online ? -1 : 1));
            return result;
        }

        private void Update()
        {
            if (!Initialized) return;
            try
            {
                SteamAPI.RunCallbacks();
            }
            catch (InvalidOperationException exception)
            {
                // Steamworks' static dispatcher was reset under us (an editor script
                // reload during a live session). Stop pumping and say so once.
                Initialized = false;
                LastError = "Steam state was lost (script reload during a session?); restart the game to use Steam again.";
                Debug.LogWarning("[Steam] " + LastError + " " + exception.Message);
            }
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
