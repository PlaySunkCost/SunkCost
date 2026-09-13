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

### Still not covered by this session

- Real Steam transport (two Windows computers, two Steam accounts) remains
  untested, as noted above.
- The disconnect path was exercised by directly granting ownership through the
  new `HQPrototypeTestHooks` helper rather than a real second player physically
  grabbing the ball with mouse/keyboard input — appropriate because MCP can only
  drive the connected editor, not a second standalone process's input. The grab
  API itself (`ServerTryGrab`/request plumbing) is unchanged by this fix and was
  already covered by the original automated checks above.
