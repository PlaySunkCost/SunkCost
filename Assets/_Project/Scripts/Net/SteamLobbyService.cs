using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace SunkCost.Net
{
    // Create/join/leave one Steam lobby and read its members and metadata
    // (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md sections 4, 5). Owns the retained
    // CallResult/Callback handles for the active room. Operations are serialized:
    // an abandoned create/join stays pending until Steam answers, and a late success
    // leaves the orphan lobby instead of resurrecting a cancelled room.
    public sealed class SteamLobbyService : IDisposable
    {
        public ulong CurrentLobbyId { get; private set; }
        // True while a create or join is outstanding, including abandoned ones that
        // have not been answered yet. Callers must not start another until false.
        public bool Busy => pendingOperation != 0;

        public event Action<ulong> MembersChanged;
        public event Action<ulong> DataChanged;

        private CallResult<LobbyCreated_t> createResult;
        private CallResult<LobbyEnter_t> joinResult;
        private Callback<LobbyEnter_t> lobbyEntered;
        private Callback<LobbyChatUpdate_t> chatUpdated;
        private Callback<LobbyDataUpdate_t> dataUpdated;

        private int pendingOperation;
        private bool pendingAbandoned;
        private ulong pendingLobbyId; // the lobby a join targets, 0 for create
        private Action<int, EResult, ulong> createDone;
        private Action<int, EChatRoomEnterResponse, ulong, bool> joinDone;

        public SteamLobbyService()
        {
            createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            joinResult = CallResult<LobbyEnter_t>.Create(OnLobbyJoined);
            lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEnteredGlobal);
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            dataUpdated = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        }

        public bool BeginCreate(ELobbyType type, int maxMembers, int operationId, Action<int, EResult, ulong> done, out string error)
        {
            if (Busy) { error = "Finishing a previous Steam request..."; return false; }
            SteamAPICall_t call = SteamMatchmaking.CreateLobby(type, maxMembers);
            if (call == SteamAPICall_t.Invalid) { error = "Steam refused to create a lobby."; return false; }
            pendingOperation = operationId;
            pendingAbandoned = false;
            pendingLobbyId = 0;
            createDone = done;
            createResult.Set(call);
            error = string.Empty;
            return true;
        }

        public bool BeginJoin(ulong lobbyId, int operationId, Action<int, EChatRoomEnterResponse, ulong, bool> done, out string error)
        {
            if (Busy) { error = "Finishing a previous Steam request..."; return false; }
            SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
            if (call == SteamAPICall_t.Invalid) { error = "Steam refused the join request."; return false; }
            pendingOperation = operationId;
            pendingAbandoned = false;
            pendingLobbyId = lobbyId;
            joinDone = done;
            joinResult.Set(call);
            error = string.Empty;
            return true;
        }

        // Cancellation is not disposal: the handle stays registered so a late
        // success can be cleaned up. The operation's callback is dropped.
        public void Abandon(int operationId)
        {
            if (pendingOperation != operationId) return;
            pendingAbandoned = true;
            createDone = null;
            joinDone = null;
        }

        public void Leave()
        {
            if (CurrentLobbyId == 0) return;
            SteamMatchmaking.LeaveLobby(new CSteamID(CurrentLobbyId));
            CurrentLobbyId = 0;
        }

        public bool SetData(string key, string value) =>
            CurrentLobbyId != 0 && SteamMatchmaking.SetLobbyData(new CSteamID(CurrentLobbyId), key, value);

        public string GetData(ulong lobbyId, string key) =>
            SteamMatchmaking.GetLobbyData(new CSteamID(lobbyId), key) ?? string.Empty;

        public bool SetType(ELobbyType type) => CurrentLobbyId != 0 && SteamMatchmaking.SetLobbyType(new CSteamID(CurrentLobbyId), type);
        public bool SetJoinable(bool joinable) => CurrentLobbyId != 0 && SteamMatchmaking.SetLobbyJoinable(new CSteamID(CurrentLobbyId), joinable);
        public bool SetMemberLimit(int limit) => CurrentLobbyId != 0 && SteamMatchmaking.SetLobbyMemberLimit(new CSteamID(CurrentLobbyId), limit);
        public bool RequestData(ulong lobbyId) => SteamMatchmaking.RequestLobbyData(new CSteamID(lobbyId));

        public ulong Owner(ulong lobbyId) => SteamMatchmaking.GetLobbyOwner(new CSteamID(lobbyId)).m_SteamID;
        public int MemberCount(ulong lobbyId) => SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobbyId));

        public List<ulong> Members(ulong lobbyId)
        {
            var lobby = new CSteamID(lobbyId);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            var result = new List<ulong>(count);
            for (int i = 0; i < count; i++)
                result.Add(SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID);
            return result;
        }

        public bool IsMember(ulong lobbyId, ulong steamId)
        {
            if (lobbyId == 0 || steamId == 0) return false;
            var lobby = new CSteamID(lobbyId);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == steamId) return true;
            return false;
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            int operation = pendingOperation;
            bool abandoned = pendingAbandoned;
            Action<int, EResult, ulong> done = createDone;
            pendingOperation = 0;
            createDone = null;

            ulong lobbyId = result.m_ulSteamIDLobby;
            EResult code = ioFailure ? EResult.k_EResultIOFailure : result.m_eResult;
            if (abandoned)
            {
                // Late success for a cancelled host: do not keep an orphan lobby.
                if (code == EResult.k_EResultOK && lobbyId != 0)
                    SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
                return;
            }
            if (code == EResult.k_EResultOK && lobbyId != 0)
                CurrentLobbyId = lobbyId;
            done?.Invoke(operation, code, lobbyId);
        }

        private void OnLobbyJoined(LobbyEnter_t result, bool ioFailure)
        {
            int operation = pendingOperation;
            bool abandoned = pendingAbandoned;
            Action<int, EChatRoomEnterResponse, ulong, bool> done = joinDone;
            pendingOperation = 0;
            pendingLobbyId = 0;
            joinDone = null;

            ulong lobbyId = result.m_ulSteamIDLobby;
            var response = (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
            bool success = !ioFailure && response == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            if (abandoned)
            {
                if (success && lobbyId != 0)
                    SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
                return;
            }
            if (success && lobbyId != 0)
                CurrentLobbyId = lobbyId;
            done?.Invoke(operation, response, lobbyId, ioFailure);
        }

        // Creating or joining also raises this global event. Anything for a lobby we
        // did not ask for (and are not in) is an orphan to leave immediately.
        private void OnLobbyEnteredGlobal(LobbyEnter_t callback)
        {
            ulong lobbyId = callback.m_ulSteamIDLobby;
            if (lobbyId == 0) return;
            if (lobbyId == CurrentLobbyId) return;
            if (pendingOperation != 0 && (pendingLobbyId == lobbyId || pendingLobbyId == 0)) return;
            var response = (EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse;
            if (response == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Debug.LogWarning("[SteamLobby] Left unexpected lobby " + lobbyId);
                SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
            }
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
        {
            if (callback.m_ulSteamIDLobby == 0) return;
            MembersChanged?.Invoke(callback.m_ulSteamIDLobby);
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t callback)
        {
            if (callback.m_ulSteamIDLobby == 0) return;
            DataChanged?.Invoke(callback.m_ulSteamIDLobby);
        }

        public void Dispose()
        {
            createResult?.Dispose();
            joinResult?.Dispose();
            lobbyEntered?.Dispose();
            chatUpdated?.Dispose();
            dataUpdated?.Dispose();
            createResult = null;
            joinResult = null;
            lobbyEntered = null;
            chatUpdated = null;
            dataUpdated = null;
        }
    }
}
