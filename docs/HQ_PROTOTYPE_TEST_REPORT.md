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
