# HQ prototype verification report

## Monitor branch — 15 September 2026

Tested uncommitted `dan/monitor` based on `dfc6f57` (the scene-flow merge),
same rig as the scene-flow section below (editor host + headless guests,
Local/Tugboat, one machine, build `local-dev:dfc6f57`). The monitor card
replaces the debug sail: E on `MonitorButton_Site01` / `MonitorButton_HQ` →
`ShipControls.RequestSail` (ServerRpc) → `WorldSceneFlow.ServerSail`; refusals
are a server-written `Refusal` on `CrewDayState` that every `ShipMonitor`
shows on its `MonitorStatus` line for `refusalDisplaySeconds` (3 s). No days
yet: the "site between days", "after day 3 only HQ" rules wait for the
day-state card; "dive in progress" already locks it through `ServerCanSail`.

- Pure: validators pass with the new `MonitorStatus` part; the ship prefab
  carries `ShipMonitor` + two `MonitorButton`s, the player prefab
  `ShipControls`; *Check world loop (pure)* passes.
- S2 (through the buttons): idle text `Docked at HQ — E on Site 01 to sail` on
  the host; the guest pressed Site 01 from ashore → both monitors read
  `Not aboard: Player 1` (the presser's own absence, checked before the crew
  rule); host on the deck pressed Site 01 with the guest ashore → both monitors
  `Not aboard: Player 1`; phase stayed `AtHQ`, `ShipAtSea` not loaded; the
  text cleared back to the idle line after 3 s.
- S3 (through the button): host on the deck pressed Site 01 → phase `Sailing`
  → `AtSea`; everything the scene-flow S3 row checks still holds (same spots,
  held and deck balls travelled, guest sees it all, one fade).
- S13 (through the button): monitor at sea reads `At Site 01 — E on HQ to
  sail home`; the **guest** pressed HQ → `SailingHome` → `AtHQ`; the
  scene-flow S13 checks hold.
- S1, S4, S5, S15 unchanged and passing; `MATRIX_PASS`.

Not run: Steam; a person pressing the button with the real prompt ("Press E
to sail to Site 01") — the crosshair turns yellow on a button like on a ball.

## Scene flow branch — 15 September 2026

Tested uncommitted `dan/scene-flow` based on `90b3ab5` (the world-loop plan
merge), Unity 6000.6.0f1, FishNet 4.7.3 over Local/Tugboat: editor as host, one
or two headless standalone guests (Local development build
`local-dev:90b3ab5`, driven through `InventoryVerificationPeer`'s command
directory), all on one machine, so RTT ≈ 0. The rows are the Local matrix of
`docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md` section 9.2 that the scene-flow card
owns; *Sunk Cost > Prototype > Run world loop matrix* runs them from a hosting
editor and writes `Temp/world-loop-matrix.log` (`MATRIX_PASS`, 00:50 on
15 September). These results do not certify Steam.

Scenes: `Session` (menu, network root, `WorldSceneFlow`, `CrewSpawner`,
`ScreenFade`), `HQPrototype` (rebuilt: the room, a dock and the docked ship
stub), `ShipAtSea` (the same ship prefab 500 m out on a water plane). The loot
fixture is spawned at runtime by `LootFixtureSpawner` from
`HQPrototypeLootSetup`'s entries; nothing carryable is a scene object any more.

- Pure: *Check world loop (pure)* passes (load data additive / never
  auto-unloaded / client active scene = world, unload data, ship part names on
  both instances, `ElevatorMath` water curve, settings asset); *Validate
  Session*, *Validate HQ*, *Validate ShipAtSea* pass; the builders are
  idempotent (a second run reports no changes).
- S1 (join at HQ): guest client 1 spawned at an HQ spawn point; both peers see
  both players in `HQPrototype`; the guest has `Session+HQPrototype` loaded and
  nothing else, phase `AtHQ`; one NetworkManager; seven fixture items in HQ on
  the host and seven on the guest.
- S2 (sail refused): with both players ashore `ServerSail(Sea)` → `refused: Not
  aboard: Player 1, Player 0`; host on the deck, guest ashore → `refused: Not
  aboard: Player 1`; phase stayed `AtHQ`, `ShipAtSea` never loaded.
- S3 (sail out): host holding `Basketball`, `Basketball (2)` dropped on the
  docked deck at ship-local (2, 0.5, −3), host at ship-local (−2, 0, 2), guest
  at (2.5, 0, 4). `ServerSail(Sea)` → phase `Sailing` → `AtSea`; the host faded
  exactly once; `HQPrototype` unloaded on the host; host player in `ShipAtSea`
  at the same ship-local spot (< 0.15 m); the held ball still `Held` in
  `ShipAtSea`; the deck ball on the sea deck within 0.6 m of its ship-local
  spot (it settles on the new deck). Guest snapshot: `loaded=Session+ShipAtSea;
  active=ShipAtSea; phase=AtSea; world=Sea`, both players `scene=ShipAtSea`,
  `id=1 state=Held holder=0` and `id=2 state=Free` both in `ShipAtSea`, nothing
  left in HQ; the guest's own position (2.50, 0.00, 504.00) = ship-local
  (2.5, 0, 4), the spot it had at the dock.
- S4 (join at sea): a second guest (client 2) spawned on a sea-deck
  `SpawnPoint_n` at (−3.00, 0.00, 501.00), loaded `Session+ShipAtSea` only, saw
  three players and both balls; it left cleanly (`leave` command) and the host
  went back to two players.
- S5 (join mid-day): `CrewDayState.ServerBeginDay()` → `DiveInProgress` (the
  phase half of the day API; the deck-cabin card calls it when the car
  departs). `ServerSail(HQ)` → `refused: Dive in progress.`. A third process
  joined: admission refused, its snapshot `server=False; client=False;
  clientId=-1; loaded=Session; message=Dive in progress — join between days`,
  no player line, never in `ShipAtSea`. Host unaffected: two players, still at
  sea, phase `DiveInProgress`; the first guest saw `phase=DiveInProgress` and
  two players. `ServerEndDay()` → `AtSea`.
- S13 (sail home, scene part): `ServerSail(HQ)` → `AtHQ`; `ShipAtSea` unloaded
  on the host; host at the same deck spot on the docked ship; the held ball
  `Held` in `HQPrototype`; the deck ball in `HQPrototype`; the HQ fixture
  respawned fresh (7 + the 2 balls that travelled = 9 items on both peers);
  guest `loaded=HQPrototype+Session; phase=AtHQ`, both players in HQ.
- S15 (host leaves, re-hosts): after Leave, no world scene loaded, active scene
  `Session`, `CrewDayState.Instance` null; re-host came up (the Tugboat port
  needs about 2 s to free; the row waits 2 s and retries up to four times);
  fixture fresh (7), one day state, `AtHQ`, host spawned in HQ.
- Findings fixed on the way (all in `WorldSceneFlow`, contract section 10):
  FishNet's `SceneLookupData` `!=` throws on null entries (`is null`); Unity 6's
  `Scene.GetRootGameObjects(List)` does not clear the list, so a load that moved
  nothing re-moved the previous sail's players into the joiner's scene — one
  keep-alive root in FishNet's holder scene fixes it; a pure client
  instantiated the fresh HQ fixture into the sea scene it was about to unload
  (spawned objects land in the active scene) — they are moved to `Session` on
  unload-start; adding the host to the destination before the guests made the
  guest's player blink out of the host's view for two ticks — every traveller
  is added to the destination before the one load, and arrival is the client's
  `WorldArrivedBroadcast` from its own load-end rather than presence.
- Editor log after the run: no exceptions from project code; URP logs
  "Reduced additional punctual light shadows resolution" (six shadow-casting
  lights across the base and the ship in one atlas — cosmetic, not fixed).

Not run: every Steam row (two machines) — the walkthrough card; the four-player
Local sail (the matrix drives two guests, not three); feel (a person has not
yet sailed with the fade in front of them).

## Loot and weight branch — 14 September 2026

Tested uncommitted `dan/loot-weight` based on `a8ffacd` (plan commit `00f3bce`),
Unity 6000.6.0f1, FishNet 4.7.3 over Local/Tugboat: editor as host, one
headless standalone guest (Local development build `local-dev:00f3bce`, driven
through `InventoryVerificationPeer`). These results do not certify Steam.

Fixture after `Sunk Cost > Prototype > Apply loot setup`: three basketballs
(0.62 kg, one hand, slot) and three two-handed heavy balls (blue 6 kg / 0.40 m,
purple 12 kg / 0.55 m, black 20 kg / 0.70 m). `WeightSettings.asset`: meter
scale 12 kg, speed floor 0.35, throw reference 1 kg, throw floor 0.15.

- Pure: `HQPrototypeWeightChecks` (curves, sanitisation, throw factors,
  two-handed grab rows, asset validity) and `HQPrototypeInventoryChecks` pass;
  `HQPrototypeValidator` passes with six items. `Apply loot setup` twice: the
  second run reports no changes.
- Setup finding: FishNet assigns a scene `SceneId` from `OnValidate`, throttled
  to one rebuild per 250 ms, so instantiating three prefabs in one frame left
  two with no id and they were destroyed at runtime ("expected to be initialized
  but was not"). The setup now runs FishNet's scene-wide id rebuild (the same
  call as *Reserialize NetworkObjects*) before saving; two ids were assigned once.
- W1/W2 (host): basketball → 0.62 kg, fill 0.0504, speed 0.9673, `HoldPoint`.
  Blue ball grabbed with the basketball equipped and free slots → basketball
  auto-stowed in slot 0 (`slots=[0,-1,-1,-1]`), blue held as overflow at
  `TwoHandHoldPoint` (offset 0.000), 6.62 kg, fill 0.424, speed 0.7244; F3 rows
  read `Held by 0 6kg 2h` / `Stowed by 0 0.62kg 1h` and the player row
  `6.62kg x0.72`.
- W4 (host): `RequestEquip(0)` while holding the blue ball → refused, prompt
  "Hands full", slots unchanged. Prompt text on a heavy target:
  "Press E to grab Blue ball (two hands)".
- W11 (host): Q with the blue ball facing open floor → it rests at
  (3.00, 0.20, −0.60): 0.6 m in front of the player at floor level (radius
  0.20), Free, server-simulated; mass back to 0.62.
- W3 (host): basketballs (2) and (3) grabbed → `slots=[0,1,2,-1]`, (2) in hand,
  1.86 kg, speed 0.9067. Black ball grabbed → (2) auto-stowed, black overflow,
  21.86 kg, fill 0.8382, speed 0.4551 (section 4 table values exact).
- W6 (host): throw black → `lastLaunchSpeed=1.789` (factor 0.2236), it rolled to
  the south wall (z −5.5) and handed back to the server; mass 1.86. Equip slot 0
  and throw the basketball → `lastLaunchSpeed=8.000`; slot 0 cleared; 1.24 kg.
- W16: Game view capture `Logs/loot-weight-hud.png` with the black ball held:
  ball centred low covering the bottom of the view, slots 2 and 3 with icons,
  "In hand: Black ball (no slot)", grey meter ≈ 83 % with a visible end gap.
- W15: host Leave with the black ball held and two stowed → re-host: slots
  empty, 0 kg, six items Free at their reset positions.
- W2/W5 guest: headless client 1 grabbed Basketball (2) then the blue ball. Its
  own snapshot: `slots=[1,-1,-1,-1]; held=HeavyBallBlue; massKg=6.62;
  speedFactor=0.7244; meterFill=0.424`; blue `writer=True` on the guest. Host
  view: blue `Held by 1`, REPLICATED, position (3.00, 0.80, −0.85) = centred low
  in front of the guest; host player row `6.62kg x0.72` for client 1.
- W14 abrupt loss: the guest was killed (13:05:24) while holding the blue ball
  and stowing Basketball (2). Tugboat reported the loss at about 13:06:50; the
  host then showed both items Free, colliding, server-simulated on the floor
  around the guest's last position (blue at (3.23, 0.20, −1.76), the basketball
  at (2.93, 0.12, −0.68)) and the guest's player gone.

- Amendment (Dan, same day): hard capacity replaces the asymptote. Capacity
  25 kg, linear slowdown to 0.35, full = red bar + no movement; blue ball is now
  one-handed and slot-able, two in the room (7 items). Re-verified on the host:
  blue (2) + blue → 12 kg in slots, fill 0.48, speed 0.688; + purple in hands →
  24 kg, fill 0.96, speed 0.376, `CanMove=True`; drop purple, take black →
  32 kg, fill 1, speed 0, `CanMove=False`, F3 row `32kg x0.00 OVERLOADED`,
  Game view capture `Logs/loot-weight-overloaded.png` (red bar, "Too heavy to
  move" line, two blue icons in slots 1–2); Q while overloaded → 12 kg,
  `CanMove=True`. Pure checks updated to the linear table. Validator passes
  with seven items. The setup's scene-id rebuild assigned the id of the new
  blue instance (one more case of the OnValidate throttle).

- Second amendment (Dan): overloaded is a crawl, not a stop (`overloadSpeedFactor`,
  first 0.01, raised live to 0.1 = 0.4 m/s walk after Dan tried it), and with a two-handed item in the
  hands E on a slot-able item stores it straight into a free slot. Verified on
  the host: holding black, E on Basketball → prompt "Press E to store
  Basketball", `slots=[0,-1,-1,-1]`, black still in hand, 20.62 kg; E on purple →
  "Hands full", nothing changes; E on blue → stored in slot 2, 26.62 kg,
  `speedFactor=0.01` at the time, F3 `26.62kg x0.01 OVERLOADED`. Pure checks updated.

Not run: W7 timed walk with real input (the multiplier is one line in
`HQPlayerController.Move`; `SpeedFactor` values verified), W9 (needs three
machines), W12 late join with heavy items stowed, Steam rows. Feel gate: a
person has not yet carried each load; tuning is provisional.

## Hold/inventory branch — 14 September 2026

Tested uncommitted `dan/hold-feel` based on `a4ddc45`, Unity 6000.6.0f1,
FishNet 4.7.3 over Local/Tugboat. These results do not certify Steam transport.

- Compilation, pure inventory checks and scene/prefab validator passed. Windows
  Local development build succeeded. The subsequent automatic fifth-pickup rule
  compiled and passed pure checks in the MCP-connected editor.
- H1–H6: separate standalone host and editor guest passed rigid hold while moving
  and pitching, range refusal, stow/equip/swap, silent storage, four slots plus
  overflow, blocked equip during overflow, Q/throw slot clearing and rest handoff.
- H7: a third, late-joining standalone client observed the existing guest's slot
  ids and hidden, non-colliding Stowed items. This was read on the joining peer.
- H8 clean leave: host observed held and stowed items become Free, visible,
  server-simulated near the departing client's last position.
- Moving catches: real E input on the editor guest caught the host's flying ball
  after 1.27 m of travel. A return throw was caught by the host while Released.
  Both peers agreed on the new holder; only that holder wrote the held transform.
- HUD: fresh Game-view capture inspected: four slots, icon, equipped highlight,
  held ball and center dot are visible (`Logs/inventory-hud-final.png`).
- Automatic fifth pickup: MCP editor host with a separate standalone guest
  (client 1). Before: slots `[0,1,2,3]`, item 0 Held. After grabbing item 4:
  the same slots, item 0 Stowed/hidden, item 4 Held by client 1 as overflow.
  The host independently observed those states and the guest as sole held writer.
  Equip stayed blocked while overflow was held; dropping it preserved all four
  slots and allowed item 0 to be equipped again. The client build predates this
  server decision change; the updated decision ran on the editor host.

Reproducible checks: `HQPrototypeInventoryChecks`, `HQInventoryRuntimeChecks`
and `HQCatchRuntimeChecks`. The development-only `InventoryVerificationPeer`
accepts whitelisted commands through an explicitly selected local directory
(`-hq-inventory-test-dir`); normal launches do not enable it. This is a Local
test aid, not a Steam bypass. Session logs/screenshots under Temp/Logs are local
artifacts and are not guaranteed to survive Unity cleanup.

- H8 abrupt loss: killed the standalone guest while it held item 0 and stowed
  items 1–3. After the transport detected loss, its player despawned and all four
  items became Free, visible, colliding and server-simulated within the scatter
  radius of its last position `(-0.9, 0, -1.5)`. The host remained in the room.

- H9: host Leave returned to Menu with zero spawned players/items. Re-host in the
  same Play session created empty inventory and all five Free balls at their
  original floor positions, with server ownership and simulation.

Outstanding: two-machine
Steam rows H1/H3/H5/H6/H8 with matching builds and recorded RTT, and teammate
review of `PlayerInventory.cs`, `CarryableItem.cs` and the contract. Dor should
review `PlayerHudUI.cs` and item icon integration. No human approval or Notion
completion is claimed.

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

### Two-account Steam session (14 September 2026, reported by Dan)

Dan hosted and a friend joined over Steam using the shared build from
`db33884` (`Builds/HQPrototype`, `localOnly=false`). Dan reported it worked: the
friend entered the room. Not recorded: which join path was used (in-game invite,
overlay invite, or lobby ID), per-machine F3/F4 snapshots, latency, or whether
guest throw and Leave were exercised on Steam. Treat this as a pass for "a second
account can enter a friends-only room through the lobby" (S1 or S2, path
unspecified), not for S3–S9, S11 or S12, which stay pending.
