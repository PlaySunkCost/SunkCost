# Steam lobby implementation plan: host, invites, four players

**Status: implemented on `dan/steam-lobby`; Local matrix and single-account Steam host path verified, multi-account Steam rows pending (see HQ_PROTOTYPE_TEST_REPORT.md).** Prepared 13 September 2026 from
`dan/steam-lobby` at `3dab326`, whose base is `main` at `1a9bbb8`.
This is an implementation handoff for [the mission brief](STEAM_LOBBY_MISSION.md).
Writing this document does not start implementation, certify multiplayer behavior,
or constitute teammate approval. Owner and implementation branch remain Dan and
`dan/steam-lobby` as recorded in the brief.

Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md),
[CONVENTIONS](CONVENTIONS.md), and [WORKFLOW](WORKFLOW.md) before coding.
Existing gameplay decisions take precedence over historical proposals in the
[HQ plan](HQ_BASKETBALL_IMPLEMENTATION_PLAN.md). The choices below resolve the
brief's implementation questions within its scope; they introduce no new dive rules.

## 1. Outcome and scope

One player hosts an HQ room. Three friends join using Steam's invite overlay or
a copied **lobby ID**. Everyone sees the crew, walks, and passes the existing ball.
A fifth member and a different build receive useful failure messages. Guest Leave,
host Leave, host loss, and hosting again all return to usable states.

| Planning question | Implementation choice |
|---|---|
| Admission handshake? | Include it. Check project, protocol, build, session and transport-proven lobby membership before FishNet authenticates/spawns a player. Human networking review is required before merge. |
| Lobby visibility? | Friends-only when ready. Create private and temporarily non-joinable during setup. No public browser. |
| Manual joining? | Lobby ID in Steam mode; IP in Local mode. Remove user-facing SteamID64 entry. The host SteamID64 remains the internal transport address. |
| Old Steam command line? | Replace `-hq-auto-join-steam <hostSteamId>` with `-hq-auto-join-steam-lobby <lobbyId>`. Old flag prints a migration message; never silently reinterpret its number. |
| Names? | Steam persona names for lobby members, including a `(you)` and host label. Local mode lists replicated player owner IDs, e.g. `Player 1`; no Steam calls. |
| Capacity? | Four total humans: one host plus three guests. Steam lobby limit 4; FishySteamworks remote socket cap 3; Tugboat socket cap 4; authenticator independently caps admitted players at 4. |
| Build compatibility? | Project marker plus integer wire protocol plus exact Git revision of a clean shared build. Bake identity during packaging; do not rely on `Application.version` or a live Git process on players' machines. |
| Where is the lobby screen? | Extend the existing HQ session panel; keep the same scene and basketball harness. No separate waiting-room scene or Ready/Start vote. |
| Invites while in another room? | Keep the current room and display `Leave this room before accepting another invite.` Do not silently close a hosted room. Duplicate invite for the current lobby is harmless. |
| Steam initialization? | Selecting Steam mode initializes and registers the invite listener before Host/Join. First session binds the transport permanently for that process. Local-only use never initializes Steam. |

An ID does not bypass Steam's friends/invite permission rules. Testers joining by
ID must be eligible to enter that friends-only lobby.

Out of scope: matchmaking/browser, voice, host migration, registered App ID,
anti-cheat, underwater entry rules, new player art, and the separately assigned
ten-minute four-client soak. A short four-player functional test is required here.

## 2. Verified starting point and corrections to the brief

The following were inspected in this checkout; they are not assumed installed APIs.

| Area | Actual state and implication |
|---|---|
| Editor | `ProjectSettings/ProjectVersion.txt`: Unity `6000.6.0f1`. Keep it. |
| Packages | FishNet URL tag `4.7.3`, lock hash `73f30cf2425dc808a4f463f0a233d386010810b3`; Steamworks.NET tag `2025.164.1`, hash `c21a8f0e31c56ae8707130967faf491f7dd7c0d8`. FishySteamworks `4.1.1` is vendored. |
| Session | `PrototypeSessionUI` owns Steam lifecycle, transport activation, host coroutine and Leave. Extract these carefully instead of layering another owner over them. |
| Binding | `Prototype Network Root` begins inactive. The chosen transport must be assigned before activation; no switch after FishNet initializes. Public automation methods currently bypass the UI lock, so the new controller must enforce it. |
| Caps | Both serialized `_maximumClients` values are 2, but they do NOT mean the same thing. Fishy counts remote sockets and separately adds the host; Tugboat counts the host's loopback socket. The claim that both already enforce two total players is inaccurate. |
| Fishy setter | `SetMaximumClients(int)` updates its server socket, but `StartServer()` passes the serialized `_maximumClients` again. Changing only the runtime setter before startup is insufficient. Update the Steam prefab default as well. |
| Spawn | Existing FishNet `PlayerSpawner` has four assigned spawn points and spawns after authenticated start-scene loading. Keep it as the single spawner. It cycles points, not free-slot reservations; this card does not promise collision-free spawn positions after arbitrary movement/rejoins. |
| Character | `PrototypePlayer.prefab` already has the Scavenger visual. `HQPrototypeBuilder.CreateOrUpdate()` reconstructs the capsule prefab and scene, so running that menu would overwrite the visual work. Use a targeted lobby upgrade instead. |
| Input | `HQPlayerController.Update()` captures the cursor on any left click while unlocked. This can steal an Invite/Leave click and throw the ball. Add a narrow local input gate for menu/overlay interaction. |
| Evidence | The brief overstates remote Steam verification. `HQ_PROTOTYPE_TEST_REPORT.md` records the original Steam test at `1803cea`, defects, and subsequent Local verification; it explicitly leaves remote Steam retesting of guest throw and Leave pending. Carry those tests forward. |
| Existing docs | README's old starter instructions conflict with current WORKFLOW. Use the existing Unity checkout and exact editor version. Update only the relevant HQ launch instructions for this mission. |

No new dependency, transport package, global gameplay assembly restructuring, or
NetworkTransform/ball ownership change is needed.

## 3. Architecture, files and ownership

All new code below is proposed. Preserve existing `.meta` files; Unity creates
and commits metas for genuinely new assets. Runtime namespaces remain
`SunkCost.Net`; editor code remains `SunkCost.Editor.Prototype`.

```text
Assets/_Project/
  Scripts/Net/
    PrototypeSessionUI.cs                 MODIFY: serialized wiring + rendering + public facade
    PrototypeSessionController.cs         NEW: one operation/state machine, FishNet orchestration
    PrototypeSessionState.cs              NEW: states/role enums and status snapshot
    LobbySessionSettings.cs               NEW: serializable timeout/cap settings with defaults
    SteamBootstrap.cs                     NEW: only Steam init/pump/shutdown owner
    SteamLobbyService.cs                  NEW: retained callbacks, create/join/leave/member reads
    LobbyMetadata.cs                      NEW: keys, parsing and validation (no Steam calls)
    PrototypeAuthenticator.cs             NEW: pre-spawn challenge/response and admission ledger
    PrototypeAdmissionMessages.cs         NEW: small IBroadcast structs and result enums
    PrototypeBuildIdentity.cs             NEW: manifest DTO/loading/compatibility checks
    SessionInputGate.cs                   NEW: local menu/overlay/focus gate
  Scripts/Player/HQPlayerController.cs     MODIFY: consult input gate, explicit Resume
  Editor/Prototype/
    HQPrototypeLobbySetup.cs              NEW: targeted asset upgrade; never rebuild the room
    HQPrototypeLobbyChecks.cs             NEW: repeatable metadata/admission/state checks
    HQPrototypeLobbyTestHooks.cs          NEW: MCP inspection/verification helpers
    PrototypeBuildIdentityEditor.cs       NEW: editor Git identity, play-mode identity provider
    HQPrototypeBuilder.cs                 MODIFY: future-generation cap defaults (3 Steam, 4 Local)
    HQPrototypeValidator.cs               MODIFY: limits, spawns, references, identity prerequisites
    HQPrototypeBuild.cs                   MODIFY: package build identity with existing Windows build
  Prefabs/Net/SteamTransport.prefab        MODIFY: only remote cap 2 -> 3
  Scenes/Prototype/HQPrototype.unity       MODIFY: only Tugboat cap 2 -> 4
docs/
  STEAM_LOBBY_IMPLEMENTATION_PLAN.md       this handoff; update status/evidence after implementation
  HQ_PROTOTYPE_TEST_REPORT.md              append actual results, including remaining gaps
  DESIGN.md                               update implemented HQ connection/cap description
  HQ_BASKETBALL_IMPLEMENTATION_PLAN.md     update current status paragraph; preserve history
README.md                                 update HQ start/join/build instructions
Builds/HQPrototype/                        ignored complete distributable
  SunkCostHQ.exe
  sunkcost-build.json                     generated next to executable, not committed
  steam_appid.txt                         existing generated 480 file
  ...existing Unity player files...
```

Do not add all files as empty scaffolding. Create each when its implementation
stage needs it. Related broadcast DTOs belong together; avoid extra manager layers.
Keep the existing F3/F4 overlay and `Basketball` unchanged.

### Runtime assembly and wiring

Keep the existing predefined Unity assemblies. The UI's existing serialized
references remain intact, including `networkRoot`, `networkManager`,
`transportManager`, local transport, Steam prefab and preview camera.

In UI initialization, create/configure exactly one controller, bootstrap and
lobby service on the always-active session UI object. The controller explicitly
initializes dependencies before use; do not rely on the relative `Awake` order of
several `AddComponent` calls. Do not place the callback pump on the inactive
network root or on a spawned avatar. Do not add a competing SteamManager.

For a fresh connection: configure mode/context, choose transport while root is
inactive, then activate it. Once `NetworkManager.Initialized` is true, add/get the
authenticator and call `ServerManager.SetAuthenticator(auth)` once, before either
manager starts a connection. Configure the auth context for each new room without
re-registering handlers. Disable any auto-start path that could start before this.

Retain UI public entry points used by existing tools:
`StartLocalHost()`, `JoinLocal(string)`, `StartSteamHost()`, `LeaveSession()`,
`RuntimeDiagnostics` and `SessionReady`. Add `JoinSteamLobby(string)`,
`CopyLobbyId()`, `InviteFriends()` and read-only session diagnostics. Update callers
of old `JoinSteam(string)` explicitly. Replace helper reflection into removed UI
private fields with the controller's read-only snapshot. `SessionReady` retains
its existing host-ready meaning; expose `InRoom` for both roles instead of
mistaking `ServerManager.Started` for a guest requirement.

## 4. Verified API map

Local source roots for this inspection:

- `Library/PackageCache/com.firstgeargames.fishnet@382980e7eed4/` (F below).
- `Library/PackageCache/com.rlabrecque.steamworks.net@6fb66c768572/` (S below).
- `Assets/ThirdParty/FishySteamworks/FishySteamworks/` (T below).

Cache folder suffixes may differ on another machine; locate the package from its
resolved version, never hard-code the cache path in runtime code.

| Need | Exact API / source |
|---|---|
| Steam lifecycle | `SteamAPI.Init()`, `SteamAPI.RunCallbacks()`, `SteamAPI.Shutdown()`; already used in `PrototypeSessionUI`. |
| Create/join | `SteamMatchmaking.CreateLobby(ELobbyType, int)` and `JoinLobby(CSteamID)` return `SteamAPICall_t`; S `Runtime/autogen/isteammatchmaking.cs`. |
| Async results | `CallResult<LobbyCreated_t>.Create(...)`, `.Set(handle)`; join uses `CallResult<LobbyEnter_t>`. Delegates receive `(result, bool ioFailure)`. S `Runtime/CallbackDispatcher.cs`. |
| Global events | `Callback<T>.Create(handler)` for `GameLobbyJoinRequested_t`, `LobbyEnter_t`, `LobbyChatUpdate_t`, `LobbyDataUpdate_t`, `GameOverlayActivated_t`, `PersonaStateChange_t`; retain all handles. |
| Lobby IDs/results | `LobbyCreated_t.m_eResult`, `.m_ulSteamIDLobby`; `LobbyEnter_t.m_EChatRoomEnterResponse` cast to `EChatRoomEnterResponse`, `.m_ulSteamIDLobby`; `GameLobbyJoinRequested_t.m_steamIDLobby`. |
| Metadata/membership | `SetLobbyData`, `GetLobbyData`, `RequestLobbyData`, `GetLobbyOwner`, `GetNumLobbyMembers`, `GetLobbyMemberByIndex`. Check bool-returning mutations. |
| Availability | `SetLobbyType`, `SetLobbyJoinable`, `SetLobbyMemberLimit`, `LeaveLobby`; S matchmaking wrapper. |
| Invites/names | `SteamFriends.ActivateGameOverlayInviteDialog(CSteamID)`, `GetPersonaName()`, `GetFriendPersonaName(CSteamID)`; `SteamUtils.IsOverlayEnabled()`. |
| ID validation | `new CSteamID(ulong)`, `.IsValid()`, `.IsLobby()`; host identity uses `.BIndividualAccount()`. Preserve full 64-bit values. |
| Optional launch argument | `SteamApps.GetLaunchCommandLine(out string, int)` exists. `+connect_lobby` may be parsed, but cold-launch invite support under 480 is not acceptance for this card. |
| FishNet auth | F `Runtime/Authenticating/Authenticator.cs`: override `InitializeOnce(NetworkManager)`, `OnRemoteConnection(NetworkConnection)`, event `Action<NetworkConnection,bool> OnAuthenticationResult`. |
| Registration | Server `RegisterBroadcast<T>(Action<NetworkConnection,T,Channel>, bool requireAuthentication = true)`; Client `RegisterBroadcast<T>(Action<T,Channel>)`; unregister matching delegates on destruction. |
| Before-auth messages | Server `Broadcast(conn, message, false, Channel.Reliable)`; client `Broadcast(message, Channel.Reliable)`. Only the handshake server handler is registered with authentication disabled. |
| Completion | Send the result BEFORE `OnAuthenticationResult?.Invoke(conn, accepted)`. F `Demos/Authenticator/Scripts/PasswordAuthenticator.cs` demonstrates this. Failure causes `conn.Disconnect(false)` in ServerManager, allowing buffered response delivery. |
| Client readiness | `ClientManager.OnAuthenticated`; then the owned `HQPlayerController` must exist. A transport Started event alone is not authentication or scene readiness. |
| Identity | T `FishySteamworks.GetConnectionAddress(int)` -> `Core/ServerSocket.GetConnectionAddress`: stored peer Steam ID. `OnClientHostState` also adds the local host to that mapping. No need to trust a client-supplied Steam ID. |
| Caps | T `Core/ServerSocket.OnRemoteConnectionState` counts `_steamConnections`; F Tugboat `Core/ServerSocket` counts `NetManager.ConnectedPeersCount`. |
| Spawn gate | F `Runtime/Generated/Component/Spawning/PlayerSpawner.cs` listens to `SceneManager.OnClientLoadedStartScenes`; server authentication precedes initial scene handling. |

Valve confirms creation also generates a host lobby-enter event, friends-only
eligibility, and automatic lobby owner replacement; account for these separately
from the game host. See [Valve matchmaking API](https://partner.steamgames.com/doc/api/ISteamMatchmaking)
and [invite dialog](https://partner.steamgames.com/doc/api/ISteamFriends#ActivateGameOverlayInviteDialog),
checked 13 September 2026. The installed C# wrapper, not C++ snippets, determines
method signatures. The application flow below is this project's design.

## 5. State machine and lifetimes

Controller states:

```text
Menu -> InitializingSteam -> CreatingLobby -> StartingServer
     -> ConnectingHost -> Authenticating -> WaitingForPlayer -> InRoom

Menu -> InitializingSteam -> JoiningLobby -> ValidatingLobby
     -> ConnectingClient -> Authenticating -> WaitingForPlayer -> InRoom

Local Host: Menu -> StartingServer -> ConnectingHost -> ... -> InRoom
Local Join: Menu -> ConnectingClient -> ... -> InRoom

Any starting/active state -> Leaving -> Menu (keep the failure/leave message)
```

`SelectedMode` is a UI preference; `BoundMode` is immutable after network root
initialization. Every public method and invite callback enforces the same guard.
Keep role (`Host`/`Guest`), `operationId`, original host, lobby ID and session ID
separate from transport Started booleans. Set role/state before calling APIs that
can synchronously invoke callbacks. Reject duplicate Host/Join during an operation.

Each start owns one increasing operation ID and fresh session context. All
coroutines, delayed results, auth replies and cleanup check their context before
acting. Never let a failed old request overwrite a newer room's UI or stop it.

Suggested editable defaults in `LobbySessionSettings`: total players 4 (allowed
range 1..4), Steam create/join timeout 20 s, server startup 5 s, transport connect
20 s, auth timeout 10 s, member-cache grace 2 s within that auth deadline, player
spawn timeout 10 s, clean disconnect timeout 5 s. Use unscaled monotonic time.
Timeouts are implementation defaults to verify, not latency guarantees.

### Steam lifetime

Initialize when Steam is selected, or before a Steam auto-host/join request.
Display `Steam ready — accept an invite or enter a lobby ID` after callbacks are
registered. At initial menu, instruct an invite recipient to select Steam first;
simply having an untouched Local-mode menu open is insufficient to receive our
Steam callbacks. Do not initialize Steam in a purely Local session.

Retain bootstrap and global invite listener between Leave and re-host, while
continuing to pump callbacks exactly once per frame. Active-lobby observers are
cleared/rebound with the room; global invite/overlay listeners survive in the menu.
Dispose handles once on final shutdown. This refines the brief's broad
"unsubscribe on leave": removing the invite listener would break the next invite.

### Cancellation is not disposal

Steamworks.NET `CallResult.Cancel()` unregisters the local result handler; it does
not prove Steam cancelled a join/create operation. Keep abandoned operations and
their handles until completion so a late successful result can `LeaveLobby` the
orphan. Also observe global `LobbyEnter_t` for successful unexpected entry and
leave it. Deduplicate processing by operation/lobby; a host's enter event must
never take the guest-connect path.

Serialize create/join operations. After cancellation/timeout, block another Steam
create/join until the previous result is drained; show `Finishing cancelled Steam
request...`. If it cannot resolve, present a restart instruction rather than
overlapping Steam calls and accidentally evicting the new room. Local use is still
possible if no transport was bound. Test late success, failure and callback order.

## 6. Metadata and build identity

Use a fixed schema, exact ordinal comparisons and invariant numeric formatting.
Only the original host writes these lobby keys:

| Key | Value / validation |
|---|---|
| `sc_game` | literal `sunkcost-hq` (separates this project from other App ID 480 games) |
| `sc_protocol` | decimal integer, initially `1`; increment for incompatible wire schema changes |
| `sc_build` | clean source revision: full 40-character Git SHA; equality required for shared Steam builds |
| `sc_host` | original host SteamID64 in decimal; valid individual Steam account |
| `sc_session` | new `Guid.NewGuid().ToString("N")` per Host; never reused on re-host |
| `sc_ready` | `0` during creation/closing; `1` only after host is authenticated and spawned |
| `sc_capacity` | `4`, or the configured lower total limit if intentionally testing it |

Reject missing/oversized/malformed fields. Do not use display names as identities.
Allow only the expected schema/endpoint type; never redirect from arbitrary lobby
text to a local IP or URL. Cache original host and session immutably after joining.
If they change, fail closed with `Room changed or closed` rather than following
new metadata. The current Steam owner must still equal that original host.

### Build identity implementation

`PrototypeBuildIdentityEditor` obtains `git rev-parse HEAD` and checks tracked
changes plus untracked source/assets/packages/settings before a shared build.
Exclude ignored build outputs. Fail the shared Steam build with a specific
`Commit/stash project changes before building a shared Steam test` message if
dirty; do not auto-commit or stash. Git failures are visible build failures.

After a successful existing Windows BuildPipeline call, package
`sunkcost-build.json` next to the executable, containing project, protocol, full
source revision, UTC build time, Unity version and package identifiers. Do not
mark the artifact distributable until that write succeeds. Remove/overwrite an
old manifest at build start so a failed build cannot masquerade as the new one.
No absolute developer paths or credentials in the manifest.

Runtime Windows loading resolves the executable directory from
`Application.dataPath/..`; no Git invocation and no Editor API in player code.
Missing/invalid manifest blocks Steam Host/Join with a rebuild message. In the
editor, the editor helper supplies the same identity at play entry from a clean
checkout. This supports a clean editor plus a build of the same revision.
`Application.version` is optional display text, not the compatibility check.

For uncommitted Local development only, the helper may use explicit
`local-dev:<HEAD>` identity; the local development build path must label the
manifest `localOnly=true` and the runtime must reject Steam use of that artifact.
Expose this as `HQPrototypeBuild.BuildWindowsLocalDevelopment()` with its own
`Builds/HQPrototypeLocal/` output directory; retain the existing Windows build
method for clean, shareable Steam builds. Include the protocol in both manifests.
Both local peers still compare their supplied identity; this is not evidence of
matching uncommitted content. Final Steam acceptance always uses one clean build
folder copied unchanged to every machine. Do not add a runtime skip-auth switch.

## 7. Host flow

1. Validate mode/idle state and build identity. Initialize Steam/callbacks if needed.
2. Allocate operation and fresh session; issue `CreateLobby(k_ELobbyTypePrivate, 4)`.
3. On success, store lobby/original host and set joinable false, ready 0, immutable
   metadata, and member limit. Check every bool-returning mutation; failure rolls
   back instead of advertising a half-configured room.
4. Initialize/bind FishySteamworks once; cap must come from the corrected prefab.
   Activate root, attach/configure authenticator, then start the server.
5. Wait for server Started with deadline. Start the local client ONCE with
   `SteamUser.GetSteamID().m_SteamID.ToString()`; never use the lobby ID.
6. Authenticate the host through the same challenge/response. Its transport peer
   mapping must match the original host and its own lobby membership. Do not
   special-case `clientId == 0`: Fishy uses its own host connection ID.
7. Wait for client authentication and one owned HQ player; existing PlayerSpawner
   supplies that player. Keep the preview until there is a usable player camera.
8. Set lobby type friends-only, then ready 1 and joinable true; check results.
   Mark InRoom, expose Copy Lobby ID and Invite, and render roster.
9. Any failure calls the same cleanup routine and leaves a readable reason.

Host creation raises `LobbyEnter_t` too. Role plus operation context prevents it
from calling `JoinLobby` or starting a second client. Guests are never admitted
while host startup is incomplete; host authentication alone is allowed during
StartingServer/ConnectingHost. Reserve the host seat before publishing readiness.

## 8. Join, invites and roster

1. Manual field accepts a trimmed unsigned decimal value; reject empty, overflow,
   a non-lobby `CSteamID`, or a host user ID with `Enter a Steam lobby ID`.
2. Invite listener feeds `m_steamIDLobby` into exactly the same join function.
   Do not connect to the invite sender's user ID; an invitation may be forwarded.
3. Check transport lock and current room guard. Initialize Steam/build identity,
   retain join call result, start deadline, then call `JoinLobby` once.
4. Translate `EChatRoomEnterResponse` to messages: Full -> `Room full (4 players)`;
   DoesntExist -> `Room no longer exists`; NotAllowed/Banned -> `This room is not
   available to this account`; Limited/CommunityBan/blocked-member results ->
   account-restriction message; RatelimitExceeded -> retry-later message;
   I/O/unknown -> `Could not join Steam lobby` plus diagnostic result code.
5. On successful entry, validate all metadata, membership, original host and
   readiness. Wait only a bounded time for incomplete initial metadata. Listen to
   `LobbyDataUpdate_t`; outside membership `RequestLobbyData` can fetch metadata.
   Do not poll Steam once per GUI repaint. Wrong marker/build/readiness has a
   caller-visible result and immediately leaves the lobby on definitive failure.
6. **Do not reject a successful fourth entrant because member count is now 4.**
   That count includes this joiner. Enforce `count <= capacity` after entry;
   the server makes the authoritative final admission decision.
7. Configure auth with validated lobby/session/original-host/build, bind transport,
   then `ClientManager.StartConnection(sc_host)` and wait for auth/player readiness.
8. On failed auth/connection or timeout, stop pending client state, leave lobby,
   restore menu and keep the specific rejection message.

Lobby member list comes from Steam membership and persona lookup on entry and
member/name changes. Disable rich text or escape/control-limit user names (e.g.
64 visible characters); fall back to `Steam user ...` when names are unavailable.
Show `Lobby members N/4` separately from `Players in HQ M/4`, the latter based on
replicated `HQPlayerController` objects, so joining members are not falsely shown
as spawned. Local lists those player owner IDs and labels the local owner `(you)`.
No extra network roster protocol or client-written SyncVars are necessary.

On original host departure, changed Steam ownership, closed metadata, or FishNet
disconnect, guests leave and return to Menu. Never become game host automatically.
If a guest ceases to be a lobby member but remains on FishNet, the original server
disconnects that identity after a short membership refresh grace; ordinary Leave
also stops the gameplay connection immediately. Apply only to this HQ harness.

Invite is enabled only with a valid ready lobby and available Steam overlay.
Use `ActivateGameOverlayInviteDialog(currentLobbyId)`. If unavailable (including
editor/headless), keep Copy Lobby ID and explain the fallback. App ID 480 tests
start the build and select Steam on both machines before sending/accepting invites.

## 9. Admission protocol: one server decision before spawn

Derive from FishNet `Authenticator`, not the example password component. Retain
the verified registration/signature pattern, not its password bypass. Model
pending connections by connection object plus operation/nonce, not just reusable
client ID. Only server code owns admitted identities and the seat ledger.

Proposed reliable `IBroadcast` DTOs in `PrototypeAdmissionMessages.cs`:

| Message | Fields | Direction |
|---|---|---|
| `AdmissionChallenge` | project, protocol, build, session, lobbyId (0 Local), random challenge nonce | server -> this pending connection, auth not required |
| `AdmissionResponse` | client's project/protocol/build, session, lobbyId, echoed nonce | client -> server, the only pre-auth client broadcast handler |
| `AdmissionResult` | session, nonce, success, small rejection enum | server -> this connection before invoking FishNet auth result |

Use bounded identifiers (project <=32, build <=64, session exactly 32 hex),
fixed-size numeric nonce/lobby/protocol fields and an enum for reasons. Validate
lengths before string comparisons; enforce one outstanding challenge and one
terminal result per connection. Respect FishNet packet bounds. Membership proof
comes from the transport; never send/trust a client-provided Steam ID.

Server flow:

1. `OnRemoteConnection` captures immutable current session, sets auth deadline,
   sends challenge via `ServerManager.Broadcast(conn, challenge, false)`.
2. Client checks server challenge against its expected metadata (Steam) and local
   build; sends its own identity, not values blindly copied to bypass checks.
   Local joins learn the current session from this challenge, since no lobby
   exists. Record that expected session for this connection only.
3. Server accepts a response only for a live pending connection, correct nonce,
   correct session/lobby/project/protocol/build, and an accepting session state.
4. Steam: parse `GetConnectionAddress(conn.ClientId)` and confirm current membership
   of our lobby. Reject invalid mapping. If membership propagation is behind,
   hold the response for up to the 2 s grace within the 10 s auth deadline and
   retry from member callbacks; do not admit first. Reject duplicate admitted
   Steam identities, including a second connection claiming the host's identity.
5. Enforce capacity from admitted plus reserved seats, not
   `ServerManager.Clients.Count` (that includes unauthenticated sockets). Reserve
   a seat atomically on the Unity thread before reporting success. The host is
   part of the total. Reject new entrants when host is absent/not ready.
6. Send accepted/rejected result with `requireAuthenticated:false`; invoke
   `OnAuthenticationResult(conn, result)` once. Let FishNet complete authentication
   and the existing PlayerSpawner handle spawn. Do not write `IsAuthenticated`
   manually or spawn from a lobby callback.
7. Remove pending/reserved state on failure, disconnect and room end. If an
   admitted client never reaches initial player readiness, disconnect it by the
   spawn deadline and release its seat. A duplicate response never reserves again.

Client displays enum-derived reasons (`Wrong build`, `Wrong protocol`, `Room
closed`, `Not a lobby member`, `Room full`, `Authentication timed out`). Preserve
that reason through later generic Stopped events. Auth success alone is not
InRoom; wait for FishNet's authenticated event and the owned player.

The lobby's limit rejects the ordinary fifth Steam join before gameplay transport.
Transport-level rejection may precede our handshake; in that case display a
bounded connection-failed message, not an invented precise reason. In Local mode
the fifth socket is rejected by Tugboat at cap 4, whose public connection state
does not promise a distinct full-room reason. Label it `Could not connect: room
may be full or unavailable` and test the precise Steam-full path separately.
Do not increase transport caps just to admit a fifth gameplay player for messaging.

## 10. Leave, error recovery and input

One idempotent controller operation handles Cancel, Leave, startup failure,
auth rejection, remote loss and app exit. Transition to Leaving and invalidate
the active operation before callbacks can reenter cleanup.

Host: stop new admissions; best-effort ready 0/joinable false while still owner;
stop local client and server; leave Steam lobby. Guest: stop its own client and
leave lobby. Stop Starting as well as Started managers using actual transport
state; a false `.Started` is not proof that no async start is in progress. Await
Stopped or a bounded cleanup deadline before enabling another network operation.
On unexpected host-local-client failure, close the server too, rather than leaving
a headless zombie HQ. Subscribe to both server and client state changes.

Clear member and auth state, hide old ID, restore preview camera/AudioListener
only after the player listener is gone, unlock/show cursor, and retain the final
message. Existing server-side ball disconnect recovery must still run. Re-host
uses a fresh session ID and resets the scene ball through the existing lifecycle.

Steam stays initialized between rooms. On final exit: finish network shutdown
while Steam is alive, call Fishy transport `Shutdown()` once to set its idempotent
shutdown guard before `SteamAPI.Shutdown()`, dispose our callback handles, then
shut Steam down once. Do not rely on arbitrary GameObject `OnDestroy` order or
shut Steam down while Fishy still needs it. Inspect this version's destruction
and finalizer behavior; test exit from Menu, Starting and InRoom with and without
Steam initialized. Keep fixes in project lifecycle code, not third-party source.
Do not call the final `Shutdown()` path for ordinary Leave/re-host.

`SessionInputGate` suppresses local look, planar movement and grab/throw when
the session menu, overlay or application focus blocks play. Gravity/replication
must continue appropriately; never pause the server with `Time.timeScale = 0`.
Escape opens the panel; add an explicit Resume button to recapture cursor. Remove
unconditional recapture on any unlocked mouse click. On overlay close remain in
the session menu until Resume, and suppress the click that activated Resume for
that frame to prevent a throw. Apply gate at HQPlayerController input entry only;
no gameplay RPC or ownership rule changes.

## 11. Implementation sequence and checkpoints

1. **Baseline and coordination.** Inspect current diff/head and reread the brief.
   Coordinate the small scene/prefab change with the owner per CONVENTIONS. Verify
   Unity version, package lock, bridge connection if available, and character
   import (Blender currently needed to author/import the existing `.blend`).
   Record baseline console errors separately. Never regenerate the HQ to fix them.
2. **Types, identity, pure checks.** Add state/settings/metadata/identity/message
   types and editor helpers. Check bad IDs, metadata limits, exact build mismatch,
   host/session changes and defaults without live Steam. Preserve main gameplay.
3. **Extract controller/lifecycle.** Make UI delegate to the single controller;
   implement bootstrap initialization-on-Steam-selection and immutable transport
   binding. Retest existing Local host/join/Leave before adding lobby behavior.
4. **Authentication.** Attach before StartConnection; implement challenge/deadlines,
   seat accounting, visible rejection and lifecycle disposal. Verify genuine
   separate Local processes and an intentionally wrong identity. No player may
   spawn on rejected or silent auth.
5. **Capacity assets.** Add targeted `HQPrototypeLobbySetup.Apply()` using Unity
   serialized APIs; change only Steam cap and scene Tugboat cap, with Undo and
   dirty-scene handling. Update builder defaults but do not execute its full
   CreateOrUpdate. Validator checks four assigned spawns and both correct caps.
   Review YAML diff and unchanged Scavenger GUID/reference. Four Local processes
   must connect; fifth fails visibly without disturbing the existing four.
6. **Steam service and flow.** Implement callbacks/handles, metadata, staged host,
   guest validation, membership-based auth, invitations, late-result draining and
   no-host-migration behavior. Compile and run scripted lifecycle checks first.
7. **UI/input.** Add mode-specific fields, ID copy, roster/counts, progress/Cancel,
   useful failures, Invite/Resume and input gate. Preserve diagnostics and F3/F4.
8. **Package and remote acceptance.** Build clean revision, ship complete folder,
   run the matrix below; record actual evidence and review. Update only now the
   docs that say lobby work is pending. Do not claim completion while invites or
   four-machine tests are pending.

Check compilation after meaningful code stages. Do not bundle gameplay cleanup,
Blender-to-FBX conversion or a replacement player-spawner system into this task.

## 12. MCP workflow and evidence

MCP is an optional editor executor, not a second Steam account. Tools exposed in
this session include `Unity_RunCommand` and `Unity_GetConsoleLogs`; confirm they
still connect to THIS project's editor before implementation. No editor changes
or playtests were performed merely to write this plan.

Keep helpers under Editor. For repeatable pure checks, the existing predefined
editor assembly can expose `HQPrototypeLobbyChecks.RunOrThrow()` with table-driven
inputs and explicit assertion failures; no new NUnit/runtime assembly migration
is needed solely for this task. Check behavior and failure cases, not file text.
Keep Steam calls behind a small adapter/fake boundary where callback ordering
must be simulated; do not mock away the actual transport acceptance tests.

Proposed MCP hooks (implement these before invoking them):

| Hook | Expected observation |
|---|---|
| `HQPrototypeLobbySetup.Apply()` | Only the two serialized caps updated; running twice has no further diff |
| `HQPrototypeValidator.ValidateOrThrow()` | Correct caps, four non-null spawns, current player/ball, complete network/UI wiring |
| `HQPrototypeLobbyChecks.RunOrThrow()` | Metadata, identity, duplicate callbacks, late cancellation, capacity and rejection checks pass with named cases |
| `HQPrototypeLobbyTestHooks.Snapshot()` | Mode/bound mode, state/role, operation/session, lobby/original host, Steam init, server/client/auth readiness, member/player/admitted counts |
| `HQPrototypeLobbyTestHooks.StartSteamHost()` / `JoinSteamLobby(id)` | Controller-driven operations; no bypass of auth |
| `HQPrototypeLobbyTestHooks.Leave()` | Menu restored, memberships/seat ledger empty, old callbacks cannot revive session |
| `HQPrototypeBuild.BuildWindowsDevelopment()` | Successful BuildReport, current identity manifest and complete player folder |

Wrap calls in the bridge's required `internal class CommandScript : IRunCommand`
with `Execute(ExecutionResult result)`. Example after helpers exist:

```csharp
using UnityEngine;
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        SunkCost.Editor.Prototype.HQPrototypeLobbyChecks.RunOrThrow();
        result.Log("Steam lobby pure checks passed.");
    }
}
```

For Local, run one editor and separate standalone processes on `127.0.0.1` using
the existing `-hq-auto-host-local` / `-hq-auto-join-local` flags. Capture separate
logs and PIDs; terminate only owned test processes. Headless builds are useful
for connection tests but cannot prove visible characters or invite UI. Steam
requires separate accounts/machines; don't run several copies under one account
and call that four Steam players.

Expected four-player diagnostics: host `server=True client=True players=4`, each
guest `server=False client=True players=4`; four unique player owner IDs and one
ball in F3. Steam host ID need not be 0. Ball writer depends on Free/Held/Released;
require exactly one simulator under the existing contract, not all rows showing
SIM-HERE. Roster should settle at lobby 4/4 and HQ players 4/4.

Take actual Game view screenshots (e.g. `ScreenCapture.CaptureScreenshot` after a
frame) for the IMGUI panel; a scene/camera capture may omit it. Inspect clicks,
input suppression and names visually. Tool property calls do not prove physical
Steam invite acceptance. If a build/import tool times out, inspect completion
evidence before starting a duplicate operation.

## 13. Verification matrix and acceptance

All rows start **pending**. Record result, revision, mode, role and evidence path.
An existing two-player report is baseline evidence only, not a pass for this code.

| ID | Test | Required result |
|---|---|---|
| P1 | Metadata parser: whitespace ID, zero, overflow, user SteamID, foreign 480 marker, missing keys, oversized values | Explicit failure; no gameplay connection |
| P2 | Fourth member finishes entering; two applicants race for final auth seat | Fourth accepted; ledger never exceeds total; extra rejected once |
| P3 | Wrong build/protocol/session/nonce, missing membership, duplicate identity, no response | No spawn; specific rejection or bounded timeout; no seat leak |
| P4 | Create callback and enter callback in either order / duplicate | One server and one local player |
| P5 | Cancel create/join, timeout then late success, old callback during retry | Orphan left; no resurrected room; new operation unaffected |
| P6 | Steam member update delayed within/beyond grace | Admit once if valid within deadline; otherwise clean rejection |
| L1 | Local host plus three standalone clients; fifth attempt | Exactly four players; fifth fails visibly; no Steam initialization |
| L2 | Guest drops/throws; reconnect after guest Leave | Real non-host RPC path works, one player per connection, current ball state preserved |
| L3 | Host Leave and re-host repeatedly; cancel during connect | Every guest returns to menu, old server stops, fresh ball/session on re-host |
| L4 | Local/Steam API invoked after opposite transport locked | Restart message; no attempted transport rebinding |
| L5 | Invite/menu/Resume clicks and focus loss while holding ball | No unintended move/look/throw, one active listener, usable cursor |
| S1 | Clean Windows build on two accounts, both select Steam before invite | Host ID/roster visible; overlay Invite joins without typing |
| S2 | Paste eligible lobby ID instead of invite | Same metadata/auth route and successful room entry |
| S3 | Four builds on four machines/accounts | Four visible players, all walk; each of three guests grabs/drops/throws to another player |
| S4 | Fifth account attempts full lobby | Visible Room full; original four unaffected; requires fifth account/device or a separately recorded equivalent Steam test |
| S5 | Old/new clean builds; foreign App ID 480 lobby | Wrong build/game visible; no player spawned; retry usable |
| S6 | Direct host-address connection outside lobby (editor test helper only) | Auth rejects non-member even if transport connects |
| S7 | Guest Leave, join again; host Leave, host crash/loss; host re-host | Intended menus; held/released ball returns to server; no migration or duplicate players |
| S8 | Steam changes lobby owner after original host leaves | Guests exit; no replacement server starts; stale ID fails safely |
| S9 | Steam unavailable, overlay unavailable, invalid/deleted ID, denied friends-only permission | Useful reason; ID fallback where applicable; no endless progress |
| S10 | App quit / exit Play from Steam menu, startup and room | Network stops before Steam shutdown; no new Steam-not-initialized destruction exception |
| S11 | Accept another lobby's invite while already hosting/playing | Existing room remains; leave-first message |
| S12 | Simultaneous final-seat joins and one member never completes auth | No fifth player; failed client leaves; a freed seat can be joined again |

Where latency/loss tooling is available, repeat joining, guest throw, cancellation
and host-loss tests; record actual conditions. Do not invent simulated latency or
infer WAN success from Local loopback. Leave the separately assigned soak pending.

Append to `HQ_PROTOTYPE_TEST_REPORT.md`:

```text
Revision/build manifest:
Date and Unity/package versions:
Host and guests A/B/C (tester aliases; no credentials):
Steam App ID / mode / machine roles:
Join path for each (invite / lobby ID):
Per-machine client ID, lobby members, HQ player count, RTT:
Guest grab/drop/throw and disconnect outcomes:
Fifth-account and wrong-build rejection evidence:
Host Leave/crash/re-host outcomes:
Console/build results and evidence paths:
Missing tests / defects:
Teammate reviewer and review link (pending until actually reviewed):
```

## 14. Final implementation review and delivery

Apply the repository netcode and plan-auditor checklists as review criteria;
their Claude YAML does not authorize delegation. Check: one Steam lifetime owner,
one spawner, authentic transport identity, no client-written gameplay state,
bounded async cleanup, correct 3/4 transport caps, build identity in the artifact,
four real players, preserved character assets, and useful failures.

Contract rules do not need rewriting for these session mechanics. Update the
design's implemented harness paragraph to four players and lobby/invite joining
only when implemented; keep HQ-only entry and no migration unchanged. A human
must review the new network handshake before merge even though the card's
"touches contract" checkbox is no. Never self-certify that review.

Deliver a scoped PR from `dan/steam-lobby` after code is authorized, with the
complete build location, current revision, completed test rows, remaining Steam
tests and review status. This planning task delivers only this document.
