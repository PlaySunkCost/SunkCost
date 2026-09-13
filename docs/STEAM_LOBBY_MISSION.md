# Mission brief: Steam lobby — host, join by invite, 4 players connected

**Status: brief for planning, not a plan.** Written 13 September 2026 against
`main` at `1a9bbb8` (`github.com/PlaySunkCost/SunkCost`). Whoever plans this
should produce an implementation document in the style of
`docs/DEBUG_OVERLAY_IMPLEMENTATION_PLAN.md` (verified API names, file list,
verification steps with expected output) before code is written.

Notion card: "Steam lobby — host, join by invite, 4 players connected".
Owner: Dan. Epic: E1 Steam lobby and 4 players. Module: Net. Phase: 02 Prototype.
Branch: `dan/steam-lobby`. Touches the contract: **no** (see section 6 — verify).
Needs two-client test: **yes**.

---

## 1. The goal in one paragraph

Four friends should be able to get into the same HQ room over Steam without
anyone pasting a number. One person clicks **Host**; the others either accept a
Steam invite (friends overlay) or join from a lobby ID; up to **four** players
end up connected, all visible, all able to walk and pass the basketball. Leaving
and re-hosting keeps working. Today only two players can connect, and joining
requires the host to read out their SteamID64 by hand.

## 2. What exists today (verified)

- **Transport works.** Steam P2P over FishySteamworks 4.1.1 / Steamworks.NET
  2025.164.1 connected two real machines with two Steam accounts on 13/09 (App ID
  `480`, join by pasted host SteamID64). Movement, pickup, throw, disconnect and
  leave all verified. Record: `docs/HQ_PROTOTYPE_TEST_REPORT.md`.
- **Session UI:** `Assets/_Project/Scripts/Net/PrototypeSessionUI.cs` owns
  Host/Join (Local or Steam), the Leave/lobby-return flow, **and Steam's lifecycle**
  (`SteamAPI.Init` on first Steam use, `SteamAPI.RunCallbacks()` every `Update`,
  `SteamAPI.Shutdown()` in `OnDestroy`). Steam join = `ClientManager.StartConnection(hostSteamId64)`.
- **Hard cap of 2 remote clients**, in two places: the Steam transport prefab
  (`Assets/_Project/Prefabs/Net/SteamTransport.prefab`, `_maximumClients: 2`) and
  the local Tugboat transport in the scene (`HQPrototype.unity`, `_maximumClients: 2`).
  `HQPrototypeBuilder` writes both values. FishySteamworks counts remote sockets
  only; its local host connection is separate.
- **Four spawn points already exist** (`Spawn Points` in the HQ scene, created by
  `HQPrototypeBuilder.CreateSpawnPoints`), so four players will not stack.
- **Transport is bound once per process** (FishNet subscribes its managers to the
  transport at init). Local/Steam cannot be switched without a restart; the lobby
  work must live with that.
- **Debug overlay** (F3/F4, merged in PR #2) shows role, client id, RTT and per
  object owner/writer. Use it to watch four clients come in.
- **No Steam lobby code exists.** Nothing calls `SteamMatchmaking`. No FishNet
  `Authenticator` is attached; anyone who can reach the host's SteamID is admitted.

## 3. What has to be done

### A. A real Steam lobby
- Host creates a Steam lobby when hosting and leaves it when closing the room.
- Lobby carries metadata that identifies this game and this build, and the
  original host's SteamID64 (the address clients connect to). The lobby ID is
  **not** the connect address.
- Joining client reads the metadata, checks it, then connects via FishySteamworks
  to the advertised host SteamID64.
- Steam may hand lobby ownership to someone else when the host leaves. That is
  not game host migration: if the original host is gone, guests go back to their
  menu.

### B. Two ways to join, neither of them "paste a SteamID64"
- **Invite through the Steam friends overlay** from inside the room (Invite
  button → Steam's invite dialog). The invitee, with the build already running,
  gets the join request and connects.
- **Lobby ID as the manual fallback** (copyable from the host's screen, pasteable
  on the join screen). Keep it: under the shared test App ID `480`, Steam cannot
  launch our build from an invite when it is not running, so "start the game
  first, then accept the invite / paste the ID" is the dependable flow.

### C. Four players
- Raise the connection cap from 2 to 4 total (host plus three) on both transports,
  and cap the Steam lobby to 4 members.
- The session UI shows who is in the room (names from Steam, count), on host and
  on clients.
- A "room full" or "wrong build" join fails with a visible reason, not a hang.

### D. Admission (recommended, decide during planning)
- The original plan (`docs/HQ_BASKETBALL_IMPLEMENTATION_PLAN.md` section 9)
  specifies a FishNet `Authenticator` handshake before a player spawns: project
  marker, protocol/build id, and — in Steam mode — checking that the connecting
  peer's Steam ID is actually a member of the lobby (FishySteamworks exposes the
  peer Steam ID via `GetConnectionAddress(connectionId)`). This is what stops a
  stale build or a stranger from spawning a player. FishNet supports attaching
  an authenticator at runtime (`ServerManager.SetAuthenticator`), so it does not
  require a scene edit.
- Without it, "4 players" works but admission is by obscurity only. The planner
  should decide whether it is in this card or its own card; if in, note that it
  adds a network handshake (see section 6).

### E. Keep the lifecycle single-owned
- One place owns `SteamAPI.Init` / `RunCallbacks` / `Shutdown` and the lobby
  callbacks. Either extend `PrototypeSessionUI` or extract a `SteamBootstrap` as
  the original plan suggested; do not add a second Steam manager.
- Keep Steam callback / call-result handles alive for the session; unsubscribe on
  leave. Creating a lobby also raises a lobby-enter event on the host — that must
  not start a second local client.

## 4. Done means

- Host clicks Host: server up, lobby created, lobby ID and member list visible.
- Invite from the overlay: a friend with the build running joins without typing.
- Paste lobby ID: joins.
- Four machines, four Steam accounts, all four in the room and passing the ball;
  F3 on each machine shows four player rows with the right owner/writer columns.
- Fifth join attempt is refused with a message. Wrong build is refused with a message.
- Host Leave closes the room and every guest returns to their menu; guest Leave
  removes only that guest. Host can host again in the same run.
- Recorded in `docs/HQ_PROTOTYPE_TEST_REPORT.md` with build revision, roles,
  Steam accounts and observed latency.

## 5. Explicitly not this card

- The **ten-minute four-client soak** is its own card ("Four clients connected at
  once for ten minutes", touches the contract). This card gets them connected.
- Proximity voice (rides on the same connection; separate card, after this).
- Any Internet lobby browser or matchmaking beyond friends-only lobbies.
- A registered Steam App ID; keep `480` and `steam_appid.txt` for now.
- Anti-cheat.

## 6. Contract and review notes

- Lobby membership is discovery/metadata only. Gameplay still goes through FishNet;
  nothing about ownership, RPC naming or the one-writer rule changes.
- The card says "touches the contract: no". Raising the player cap and adding an
  admission handshake are session-level, not gameplay rules — but an authenticator
  handshake *is* bytes crossing the network, and `CONVENTIONS.md` wants a human
  to read everything that crosses the network before it merges. Plan a second
  pair of eyes on that part regardless of the checkbox.
- `docs/DESIGN.md` already says 1–4 players; `NETWORK_CONTRACT.md` section 1
  already says Steam P2P and host-is-server. The only doc that may need an edit
  is the design's "Steam mode connects by the host's SteamID64. A lobby and
  invite browser remain follow-up work" line, which becomes outdated.

## 7. Facts the planner can rely on (checked in the installed packages)

- Steamworks.NET has `SteamMatchmaking.CreateLobby / JoinLobby / LeaveLobby /
  SetLobbyData / GetLobbyData / RequestLobbyData / GetLobbyOwner /
  GetNumLobbyMembers / GetLobbyMemberByIndex / SetLobbyType / SetLobbyJoinable /
  SetLobbyMemberLimit / InviteUserToLobby`, `SteamFriends.ActivateGameOverlayInviteDialog`,
  and the callbacks `LobbyCreated_t`, `LobbyEnter_t`, `LobbyDataUpdate_t`,
  `LobbyChatUpdate_t`, `GameLobbyJoinRequested_t` (`Callback<T>.Create` /
  `CallResult<T>.Create`). `SteamApps.GetLaunchCommandLine` exists for `+connect_lobby`.
- FishySteamworks: `SetMaximumClients(int)`, `SetClientAddress(string)`,
  `GetConnectionAddress(connectionId)` returns the peer Steam ID as text.
- FishNet: `Authenticator` base class (`InitializeOnce`, `OnRemoteConnection`,
  `OnAuthenticationResult` event), `ServerManager.SetAuthenticator`,
  `RegisterBroadcast<T>` / `Broadcast<T>` on both managers,
  `NetworkConnection.IsAuthenticated`. The demo `PasswordAuthenticator` shows the
  exact broadcast pattern.
- Unity MCP can drive the editor as host or client and can run a headless
  standalone as the other peer; it cannot drive Steam invites or a second Steam
  account. Four-player and invite checks need real people and machines.

## 8. Open questions for the planner

1. Is the admission handshake (section 3D) in this card or split out?
2. Lobby type: friends-only (invite + ID) or private (invite only)? The original
   plan said friends-only.
3. Does the host's own display name / member list come from Steam
   (`SteamFriends.GetFriendPersonaName`) — yes for Steam mode; what shows in
   Local mode?
4. Do we keep the pasted-SteamID64 join at all once lobby ID join exists?
5. Where does the build identifier for "wrong build" come from — a constant, the
   git hash baked at build time, or `Application.version`?
