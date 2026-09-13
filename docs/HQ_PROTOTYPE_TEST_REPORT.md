# HQ prototype verification report

## Revision under test

- Branch: `codex/hq-basketball-prototype`
- Unity: `6000.6.0f1`
- FishNet source tag: `4.7.3` (embedded package metadata reports `4.7.2`)
- FishySteamworks source tag: `4.1.1`
- Steamworks.NET source tag: `2025.164.1`

## Automated checks

| Check | Result |
|---|---|
| Unity C# compilation | Passed through Unity MCP |
| HQ scene validator | Passed through Unity MCP |
| Scene visual inspection | Passed; enclosed grey-box room, light and basketball visible |
| In-editor local host startup | Passed; FishNet local server and client startup command executed |
| Windows development build | Passed; Unity BuildPipeline returned success |
| Two-process local host/client | Passed; host accepted connection IDs 0 and 1; both processes reported `players=2, ballSpawned=True`; zero runtime exceptions in either final log |

## Manual validation still required

Steam transport must be tested with two Windows computers and two Steam accounts.
Record the commit, host/client roles, Steam App ID, observed latency, movement,
pickup, contention, throw, rest handoff and disconnect behavior. This report does
not claim that a remote Steam session or teammate network review has happened.

## Fix applied and re-verified in-editor via Unity MCP

Auditing the holder-disconnect path against `NETWORK_CONTRACT.md` section 6 found
that `Basketball.prefab`'s `NetworkObject` had `_preventDespawnOnDisconnect: 0`
(FishNet's default), and `Basketball.cs` never cleared ownership on disconnect.
Together this meant a holder's disconnect would despawn the ball outright (via
FishNet's default `ClientDisconnected` despawn-of-owned-objects behavior) instead
of returning it to server simulation, contradicting both the contract and the
"remaining player can recover it" requirement in section 1 of the implementation
plan.

Fixed by:
- Setting `_preventDespawnOnDisconnect: 1` on `Basketball.prefab`'s `NetworkObject`.
- Subscribing `Basketball` to `ServerManager.OnRemoteConnectionState` in
  `OnStartServer`/`OnStopServer`, and calling `RemoveOwnership()` immediately when
  the disconnecting connection is the current holder. The existing
  `OnOwnershipServer` handler already resets `state`/`holderClientId` to Free
  whenever ownership is removed, so no duplicate reset logic was needed.

### Re-verification (Unity MCP reconnected same day)

- Compilation: clean (`isCompilationSuccessful: true`, no console errors) both for
  the `Basketball.cs`/prefab fix and for a new editor-only verification helper,
  `Assets/_Project/Editor/Prototype/HQPrototypeTestHooks.cs`.
- `HQPrototypeValidator.ValidateOrThrow()`: passed against the saved HQ scene.
- Two-process local host/client (editor host, `Builds/HQPrototype/SunkCostHQ.exe`
  as joining client via `-hq-auto-join-local 127.0.0.1`): host reported
  `players=2, ballSpawned=True` after join, matching the earlier automated-checks
  table above.
- **Disconnect-while-holding case**, run twice for reproducibility: granted the
  ball to the joining client's connection (`ServerTryGrab`), confirmed
  `isHeld=True, holderClientId=1`, then killed the joining client process
  (`taskkill`). FishNet's transport took roughly 35–75 seconds of wall-clock time
  to detect the dropped connection in this local run (no clean disconnect packet
  from a killed process) — after that:
  - `ServerManager.Clients` dropped to just the host; the joiner's player object
    was despawned (matches the contract's "disconnect despawns that player's
    temporary avatar").
  - The ball stayed **spawned** (`spawned=True` — confirms `_preventDespawnOnDisconnect`
    fixed the despawn bug) and reverted to **Free** (`isHeld=False,
    holderClientId=-1`), falling from held height to the ground under
    server-authoritative gravity in the same frame ownership cleared — matches
    "the server takes over immediately, even if it is still moving."
  - The remaining (host) player was then moved next to the ball and issued a
    normal `ServerTryGrab`, which succeeded (`grabbed=True, holderClientId=0`),
    confirming the ball is actually recoverable, not just internally marked Free.
- No console errors were logged during either disconnect run. Two harmless
  pre-existing warnings appeared each time a non-writer client's `RefreshRole`
  zeroes velocity on an already-kinematic Rigidbody
  (`Basketball.cs`'s `RefreshRole`); cosmetic, unrelated to this fix, not fixed
  here to keep this change scoped.

## First two-computer Steam session and the bugs it found (13 September 2026)

Two players on separate Windows computers with separate Steam accounts connected
over Steam P2P (App ID 480, host SteamID64 join) on the build from commit
`1803cea`. Movement, both players visible, and the **host** picking up, dropping
and throwing the ball all worked. Two defects were reported by the players:

1. **The non-host player could not drop or throw after picking the ball up.**
   Root cause (confirmed in FishNet source): `TargetConfirmHeld` was a
   `TargetRpc`, which FishNet writes to the outgoing buffer immediately, while the
   `holderClientId`/`state` SyncVars are flushed at tick end. The remote client
   therefore ran the RPC while `holderClientId` was still `-1`, `ResolveHolder`
   found nobody, `SetHeldBall` never ran, and the client's input code saw
   `heldBall == null`. The host never hit this because its SyncVar values are set
   in-process. Fixed by deriving the local held flag from the SyncVars' `OnChange`
   (`SyncLocalHeldState`) and making the throw impulse tolerate either arrival
   order (`TargetApplyRelease` stores a pending release that is applied once the
   `Released` state has also replicated).
2. **Leave did not do anything useful.** It only stopped the connection and left a
   dead screen. Per the user's follow-up decision it now returns to the host/join
   menu: host leave closes the room and returns every client to their menu;
   client leave returns only that client and the host keeps playing. A re-hosted
   room resets the ball to its spawn. The transport is locked after the first
   session of a run (FishNet binds its managers to the transport once).

### Re-verification with a genuine remote client (Unity MCP)

The earlier local checks drove grabs from the host side, which is exactly why
they missed defect 1. This pass used the **standalone build as the host, launched
headless (`-batchmode -nographics -hq-auto-host-local`) so its own player cannot
receive stray keyboard/mouse input**, and the editor as a pure joining client
(`server=False`, client id 1). A first attempt with a windowed host was discarded
because the host window had focus and its player grabbed/dropped the ball on its
own three times, contaminating the run.

- Client grab via the real `ServerRequestGrab` ServerRpc: server accepted; the
  client's private `heldBall` field became **set** (previously null); the ball
  lifted to the client's hold point.
- Client throw via the real `ServerRequestRelease` ServerRpc: ball travelled
  about 7 m, settled, handed off (`Free`, `holderClientId=-1`), `heldBall`
  cleared. Host log shows exactly one grab, one release and one rest, in order,
  with no exceptions.
- Client Leave: editor returned to the lobby (preview camera/listener re-enabled,
  cursor released); host received an **immediate** clean disconnect and kept
  running with `players=1`. Rejoining the same host afterwards worked (new client
  id, ball received at its current position).
- Host loss: when the host process was killed, the client returned to the lobby
  once the transport timeout fired.
- Host Leave (editor hosting, headless standalone client): server stopped, the
  standalone client received a peer-disconnect immediately and its process stayed
  alive in its lobby; hosting again from the same editor process worked, with the
  ball reset to spawn and no stale held state even though the room had been
  closed while the ball was held.
- Console: zero errors throughout; the "2 audio listeners" flood is fixed by
  disabling the preview camera's `AudioListener` together with the camera.

These hooks (`ClientRequestGrab`, `ClientRequestRelease`, `ClientHeldBallField`,
`SessionUiState`, …) live in `HQPrototypeTestHooks.cs` and reach the private RPC
methods by reflection so the game code needs no test-only entry points.

### Still not covered by this session

- The two fixes above were verified over the local transport with a real remote
  client; they have **not yet** been re-tested over Steam on two computers. The
  next two-computer Steam session should specifically re-check the non-host
  player's drop/throw and both Leave paths.
- The disconnect path was exercised by directly granting ownership through the
  new `HQPrototypeTestHooks` helper rather than a real second player physically
  grabbing the ball with mouse/keyboard input — appropriate because MCP can only
  drive the connected editor, not a second standalone process's input. The grab
  API itself (`ServerTryGrab`/request plumbing) is unchanged by this fix and was
  already covered by the original automated checks above.

## Network debug overlay (13 September 2026, branch `dan/debug-overlay`)

Implemented per `docs/DEBUG_OVERLAY_IMPLEMENTATION_PLAN.md`: `NetworkDebugSnapshot`,
`NetworkDebugOverlay` (F3 toggle, F4 dump), `INetworkDebugInfo` (implemented by
`Basketball`), and verification hooks in `HQPrototypeTestHooks`. No scene, prefab,
RPC or SyncVar changes; the overlay creates itself through
`RuntimeInitializeOnLoadMethod` when `Debug.isDebugBuild` is true.

Verification through Unity MCP, spec section 7. Actual snapshot text:

| Step | Result |
|---|---|
| 1 Compile | Clean; zero console errors after import (pre-existing `CS0618` warnings only) |
| 2 Bootstrap | Play Mode with no session: `Network Debug Overlay` object exists; snapshot reads `[NetDebug] OFFLINE` |
| 3 Host alone | `HOST  client=0  transport=Tugboat  rtt=33ms  tick=30Hz/161  clients=1` / `PrototypePlayer me SIM-HERE` / `Basketball server SIM-HERE Free`. Host RTT is one loopback tick, not 0; spec corrected. |
| 4 Remote client | Headless standalone host + editor client: `CLIENT  client=1  transport=Tugboat  rtt=25ms  tick=30Hz/601` / `PrototypePlayer client 0 REPLICATED` / `PrototypePlayer me SIM-HERE` / `Basketball server REPLICATED Free` |
| 5 Ownership transfer | After `ClientRequestGrab`: `Basketball me SIM-HERE Held by 1` (row moves to the top as an owned object; `heldBall=set` agrees). After `ClientRequestRelease(true)`: ball travelled to (-5.72, 0.12, 4.32) and the overlay read `Basketball server REPLICATED Free`, `holderClientId=-1`. The transient `Released by 1` phase (about two seconds) fell between commands and was not captured. |
| 6 Drawing | `SetOverlayVisible(true)`: no exceptions from `OnGUI`. A Scene View capture does not include IMGUI, as expected; the on-screen panel still needs a human F3 check on the next two-computer session. |
| 7 Dump | `DumpSnapshot()` wrote the `[NetDebug]` block to the console (and therefore `Player.log`) with the same content as the hook. |
| 8 Standalone | Windows development build succeeded; the headless host built from this branch logged zero exceptions across a full join / grab / throw / leave cycle. |

One incidental finding, not fixed here: a grab request issued in the same command as
a player teleport was refused because the server had not yet received the new
position, and the client received no feedback. That is the existing "denied
requests get a response" gap (contract section 4), already noted as open work.

## Steam lobby, admission and four players (13 September 2026, branch `dan/steam-lobby`)

Implemented per `docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md`: `PrototypeSessionController`
(state machine and host/guest/leave flows), `SteamBootstrap` (single Steam lifetime
owner), `SteamLobbyService`, `LobbyMetadata`, `PrototypeBuildIdentity` +
`sunkcost-build.json` manifest, `PrototypeAuthenticator` (challenge/response with
seat ledger and transport-proven Steam membership), `SessionInputGate`, and the
editor helpers `HQPrototypeLobbySetup`, `HQPrototypeLobbyChecks`,
`HQPrototypeLobbyTestHooks`, `PrototypeBuildIdentityEditor`. Transport caps raised
2 -> 3 (Steam remote) and 2 -> 4 (Tugboat) through the targeted setup helper; the
player prefab (Scavenger) was not touched.

Baseline before this work: one pre-existing console error at 22:07, `Steamworks is
not initialized` thrown from `FishySteamworks.OnDestroy` (Steam shut down before the
transport). Addressed by the controller's ordered final shutdown (network stop,
transport `Shutdown()`, then `SteamAPI.Shutdown()`); see S10 below.

Verification through Unity MCP. All Local rows used the local-development build
(`Builds/HQPrototypeLocal`, manifest `local-dev:3dab326…`, `localOnly=true`) as the
standalone peer and the editor as the other peer, with Play Mode identity matching.

| ID | Result |
|---|---|
| P1 | `HQPrototypeLobbyChecks.RunOrThrow()` passed: whitespace/zero/overflow/negative/user-SteamID lobby ids, every missing key, oversized build, control characters, lobby id as host, capacity above max, malformed ready flag, foreign 480 marker, wrong build/protocol, not-ready, null identity. |
| P2 | Seat ledger (same run): four reserved, fifth refused, duplicate identity and duplicate connection refused, release frees exactly one seat, double release harmless. |
| P3 | Editor guest with a replaced build revision joined a headless host: host rejected (`Remote connection started` then `stopped`, host players stayed 1), guest ended in `Menu` with `msg='Wrong build'`, no player spawned. |
| L1 | Editor host + three headless standalone clients: host snapshot `hq=4/4 admitted=4`; F3 overlay listed four player rows (one `me SIM-HERE`, three `REPLICATED`) and one ball with one writer. Fifth client: peer-disconnected at the transport before Started, no spawn, host unchanged at 4/4, zero console errors. |
| L2 | Editor as guest of a headless host: session id learned from the challenge; grab through the real ServerRpc (`heldBall=set`, ball lifted); throw landed at (-5.72, 0.12, 4.34) and handed back (`Free`); guest Leave returned to `Menu`; rejoin into the same session succeeded with the ball still at (-5.72, 0.12, 4.34). |
| L3 | Host Leave with three guests connected: all three logged a clean `Local client is stopped`, no exceptions, processes stayed alive in their menus; host reached `Menu` with counters cleared; re-host produced a new session id (`61e8…` vs `d970…`), `admitted=1`, ball back at spawn. |
| L4 | With the transport bound to Local: `SelectMode(Steam)` refused and `JoinSteamLobby(...)` refused, both with "Transport is locked to Local for this run; restart the game to change." No rebinding attempted. |
| Host loss | Headless host killed while the editor guest was in the room: after the transport timeout the guest was in `Menu` with "Disconnected from the host." No errors. |
| Build gating | `BuildWindowsDevelopment()` on the dirty tree threw "Commit/stash project changes before building a shared Steam test"; `BuildWindowsLocalDevelopment()` succeeded and wrote the `localOnly` manifest. |
| L5 | Not exercised: MCP cannot press keys or move the mouse. The gate logic is exercised indirectly (rooms enter with the cursor captured and leave with it released); a human must check Escape/Resume/overlay/focus. |
| P4–P6, S1–S12 | Pending: they need Steam accounts, a second machine, or a real overlay. See the section below for what was run on Steam from this machine. |

Message wording: a guest dropped before the transport reports Started now shows
"Could not connect: room may be full or unavailable." (Tugboat does not expose a
distinct full-room reason).

### Steam, from this machine only (one account)

Run from the editor on a clean checkout (`6f3283a`, `localOnly=false`) with the
real Steam client and App ID 480. Evidence is the controller snapshot and the F3
overlay; no second account or machine was involved, so nothing below is a pass
for S1–S9, S11 or S12.

- Selecting Steam initialized the API and registered the invite listener
  (`steam=True`, "Steam ready — accept an invite or enter a lobby ID.").
- Steam host: lobby `109775241328479431` created, metadata written, server up on
  FishySteamworks, the host's own client admitted through the challenge using
  the transport-stored peer Steam id (`admitted=1`, host connection id 32767 =
  Fishy's host id, not 0), lobby set friends-only/ready, `InRoom`; roster showed
  the persona name with "(host) (you)"; F3 showed `HOST transport=FishySteamworks`.
- Host Leave: lobby left, server and client stopped, back to `Menu`, counters
  cleared. Re-host: new session id and a new lobby (`109775241328479798`).
- **S10:** exiting Play Mode from inside the second Steam room produced no
  `Steamworks is not initialized` exception. The editor log's only occurrence
  (line 2557) belongs to the 22:07 baseline session that predates this code.
- Dirty-tree refusal: with uncommitted changes the Play Mode identity was
  `local-dev:<sha>` and Steam host/join would have been refused; the shared build
  refused with "Commit/stash project changes before building a shared Steam test".

Still pending (need people and machines): overlay invite (S1), lobby-ID join by a
second account (S2), four machines (S3), fifth account (S4), old/new/foreign-480
lobby (S5), non-member direct connect (S6), guest/host leave and loss over Steam
(S7), Steam owner change after host departure (S8), Steam/overlay unavailable
cases (S9), invite while in a room (S11), simultaneous final-seat joins (S12),
and the human input-gate check (L5). Teammate review of the admission handshake:
pending.
