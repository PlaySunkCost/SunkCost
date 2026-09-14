# Ship departure — implementation handoff

**Status: agreed behavior, implementation plan only.** Written 15 September
2026 on `codex/ship-departure-plan`, starting exactly from `main` at `0bb99c2`
(PR #19, monitor controls; includes PR #18, world scene flow). No gameplay code
or assets were changed in this planning task. Check newer main before building.

Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md),
[CONVENTIONS](CONVENTIONS.md), [WORKFLOW](WORKFLOW.md) and
[WORLD_LOOP_IMPLEMENTATION_PLAN](WORLD_LOOP_IMPLEMENTATION_PLAN.md).
This plan records the user's approval of the departure proposal. Implementation
must update the affected design/contract sections in its PR; no human networking
review, playtest or board completion is claimed by this document.

## 1. Agreed experience

The gangway belongs to the ship: attached at the ship end, resting on the dock
while docked, raised before moving and stowed at sea. It must not float loose
or remain attached to the HQ after departure.

1. A player selects a destination using the existing monitor. Everyone must be
   safely aboard; anyone on the gangway does not count. Name missing players.
2. Pause walking and item actions, but allow looking and talking. Preserve each
   player's deck position; do not relocate everyone to seats or spawn markers.
3. Lift the gangway, start the engine sound, and visibly pull away for about
   **four seconds**. Players and deck cargo ride with the ship.
4. Fade to black in first person, change location using the existing scene flow,
   then fade back aboard the stopped ship. No external cinematic camera and no
   arrival/docking animation.
5. At HQ, lower the gangway before restoring movement. At sea it stays stowed.
   Restore controls only when the local destination is ready and the transition
   has been released by the server. Slow loading extends black, not visible travel.

Same departure presentation on the way home. Future site-to-site travel uses it
too, but the current game has only Site 01 and rejects Sea -> Sea as Already
there. Do not invent another site or overload WorldId to implement that now.

No walking on the moving deck in this version, no steering, bobbing, waves that
affect physics, new boat simulation, skip vote, arrival navigation, or elevator
rewrite. The elevator's local harness does not prove multiplayer riding works.
Voice follows existing session rules; do not implement a new voice system here.

## 2. Current code and gaps

Verified on the baseline above:

- `ShipControls.ServerRequestSail` calls `WorldSceneFlow.ServerSail`; monitor
  buttons are ordinary ship parts and the server decides eligibility.
- `CrewDayState` is a persistent global NetworkObject. It owns phase/world/
  destination; `WorldSceneFlow` is a MonoBehaviour coordinating FishNet loads.
- `ShipParts` is a plain MonoBehaviour. Ships are scene-local copies of
  `Prefabs/World/Ship.prefab`, not objects moved across scenes.
- Sailing currently sets a phase, waits two sync-flush ticks, collects players,
  Held/Stowed items and Free deck cargo, and loads/moves immediately.
- `OnPhaseChanged` starts fade-out immediately; `OnLoadEnd` places the local
  player and immediately fades in. Neither waits for the full fade or a visual
  departure. Fast loads can finish before black.
- Arrival messages contain only WorldId. A delayed reply from an earlier trip
  can match a later trip to the same world. Add a transition identity.
- `PlaceLocalPlayer` recomputes coordinates from the source ship on load-end;
  this becomes unsafe once that ship has moved or stopped updating.
- Player Update mixes looking, walking and item input. SessionInputGate is a
  menu/focus gate, not a look-only travel gate. No existing rule protects item
  RPCs from late requests during travel.
- The stub's AboardVolume includes margin outside the actual deck. It is not
  sufficient to prove a player is safely aboard or off the gangway.
- Released deck items are currently omitted from travel collection. A recently
  thrown ball cannot be allowed to vanish just because travel began before rest.
- CrewSpawner can load a joining connection into CurrentWorld during travel;
  joins and pending spawns must be coordinated with the transition.

Keep the existing FishNet fixes: MovedObjectsHolder keep-alive, destination
observer pre-add, host client-pass placement, pure-client unload protection,
additive scenes and explicit unload. Do not replace this with generic
SceneManager.LoadScene, parenting network objects under the ship, or new prefabs
that respawn everyone's inventory.

## 3. Architecture and ownership

Use synchronized scripted presentation, **not deterministic physics**. The ship
has no NetworkObject and no dynamic Rigidbody; each peer evaluates the same
authored displacement from server stage/time. Persistent transition state lives
on CrewDayState. One coordinator drives ship, gangway and fade from that state.

| Object/state | Writer during departure |
|---|---|
| Departure transaction, passenger/cargo selection, stages | Server |
| Scene-local ship/gangway | Local presentation driven by replicated stage/time; no network transform |
| Player root | Its existing owner, following a captured ship-relative position |
| Remote player root | Existing NetworkTransform receiver only; never also snapped locally |
| Held item | Existing holder writer/hold-pose logic; follows its player's camera |
| Stowed item | Existing server-owned hidden state; included in moved objects |
| Frozen loose deck cargo | Server scripted position/rotation, kinematic; clients receive NetworkTransform |
| Local camera/fade/audio | Local presentation; look still respects focus/menu |

The server host executes ship presentation once, not once per server/client
callback. Do not snap remote avatars/cargo on every peer to improve appearance:
that would create competing writers. Test observer interpolation while the ship
moves; synchronizing ship time alone is not a guarantee of zero jitter/desync.

## 4. Transaction and synchronized stages

Add a serializable `ShipDepartureState` and `DepartureStage` enum in
`Scripts/World/`. One server-written SyncVar of this struct on CrewDayState:

`Serial`, `Stage`, `FromWorld`, `ToWorld`, `StageStartTick`, `StageDurationTicks`.

Stages: Idle, Preparing, RaisingGangway, PullingAway, FadingOut, Loading,
Arriving, Complete, Cancelled. DayPhase remains Sailing/SailingHome throughout;
do not build a second independent day-state machine. WorldSceneFlow is the only
server orchestrator; presentation never advances gameplay stages on its own.

Use FishNet's installed TimeManager tick/time conversion APIs after inspecting
their source. Convert seconds to ticks at the configured tick rate; use server-
synchronized tick time for evaluation, with wrap-safe subtraction. Never use
Time.time independently on each client as the animation origin. Clamp progress
to [0,1]; a late update evaluates the current point rather than restarting.
Subscribe to state changes and also apply the initial snapshot on spawn/load.

Starting tunables in WorldLoopSettings (serialized, validated, finite):

| Setting | Default | Purpose |
|---|---:|---|
| gangwayRaiseSeconds | 0.8 s | Lift before translation |
| departureSeconds | 4 s | Visible pull-away |
| departureDistanceMeters | 8 m | Short visible translation; tune to pier geometry |
| departureFadeSeconds | 0.75 s | Black/fade-in presentation |
| gangwayLowerSeconds | 0.8 s | Lower on HQ arrival before unlock |
| prepareTimeoutSeconds | 10 s | Passenger preparation acknowledgement |
| arrivalTimeoutSeconds | existing 10 s | Per-load arrival gate; extend if testing warrants |
| syncFlushTicks | existing 2 | State-before-dependent-message ordering |

Keep elevator suit fades/ride timings separate. Do not silently change all world
fades because departure uses a shorter fade. Durations allow nonnegative fade/
gangway timing; translation duration must be positive. An enabled departure needs
a valid finite direction/distance. Invalid content fails validation before use.

### Accepted request through arrival

1. Validate server/session/phase/destination and ship/presentation references.
   Missing ship currently bypasses some aboard checks: change that to refusal.
   Reject while another departure is active or a player is still spawning.
2. Capture the participating active connections and their spawned players. Check
   all eligible current crew are in the source world and safely aboard. Use the
   current alive-player rules when those exist; do not invent a death roster now.
3. Set serial and Preparing synchronously, blocking further sail/item requests.
   Clients capture their own ship-local position, freeze translation, clear
   buffered item input and acknowledge preparation for this serial. Keep ship
   stationary during preparation. Menu/focus loss does not prevent following or
   acknowledgements.
4. After preparation replies and sync-flush allowance for owner transforms,
   revalidate boarding. A player who stepped onto the gangway before receiving
   the lock causes cancellation with a named refusal. Never sail with an invalid
   passenger or force-teleport them aboard. A preparation timeout cancels safely
   while still docked. No voluntary mid-sequence gameplay cancel button.
5. Capture/freeze cargo and the final root-object move list. Raise the gangway,
   then publish PullingAway with start tick/duration. Start engine presentation.
6. At translation end, hold the ship at its end pose and publish FadingOut.
   Each client acknowledges black for this serial only after ScreenFade.IsBlack.
   Do not start a visible scene move on the assumption two ticks is enough.
7. After all remaining clients report black, publish Loading and run the existing
   observer-safe FishNet move. Preserve the captured relative poses; do not
   recompute them using the moving source ship. Keep black during loading.
8. Each client waits for its own player and destination ship, places its player
   once and sends serial/world arrival acknowledgement. Load-end alone without
   these objects is not readiness; retry within the load deadline.
9. Server places frozen deck cargo from saved poses, confirms destination and
   arrival gate, then publishes Arriving. At HQ lower gangway while fading in;
   at sea keep it stowed. Release scripted cargo at rest under the server.
10. Complete only after fade-in and applicable gangway lowering duration. Each
    local input lock additionally waits for its own fade to be clear and placement
    finished. Mark DayPhase AtHQ/AtSea and clear transition only through this
    path. Cargo/player lists and callbacks must be cleared.

Extend/reuse the existing WorldArrivedBroadcast and add a preparation/black
acknowledgement type (or a single stage-ack struct): serial, world, ack kind.
Validate active authenticated sender, membership in the captured cohort, expected
world/stage/serial. Ignore stale, duplicate, early and unexpected replies. No
client-supplied destination pose, cargo list, time or stage advancement.

## 5. Passenger following and input

Add `ShipDepartureRider` to the player prefab, with an API used by the controller
and coordinator. On local preparation capture position with ToShipLocal, plus
ship-relative yaw. Keep relative position fixed; accumulate normal mouse look
in relative yaw/pitch. A straight translating ship needs no extra camera rotation.

Evaluate the source ship first, then apply the owner's root pose, then existing
held-item LateUpdate. Disable CharacterController translation/gravity during
the lock and restore its previous enabled state on every exit path. Clear old
verticalSpeed. Keep NetworkTransform running, but do not call TeleportLocal every
frame: use a dedicated continuous owner-follow method. Teleport once at the
cross-world placement boundary. Keep all network objects as roots.

Follow runs even with no keyboard/mouse, Escape menu open, Steam overlay or lost
focus. The current early returns in HQPlayerController must not freeze following.
Look runs only when existing SessionInputGate allows it; ordinary Move and
interaction targeting/actions are skipped during travel. Clear prompts/E buffer.
Require fresh E/Q/click/number presses after unlocking; no buffered throw on arrival.

The server also rejects Grab/Equip/Drop/Use and monitor/cabin actions while this
player participates in travel. Client input gating alone is insufficient for
late RPCs. A release accepted before Preparing is handled as Released cargo,
not retroactively rejected. Inspect direct server helper paths too.

Preserve view direction relative to the ship across placement. Do not reparent
players to the non-network ship: FishNet's MovedNetworkObjects requires roots.
Future free-walking rider support can replace this follower without changing the
scene-transfer protocol; implementing it is explicitly out of scope.

## 6. Deck cargo and release handoffs

Held/Stowed items keep their established ownership and inventory membership.
For loose deck items, capture a unique list of spawned, non-scene root items in
the ship's cargo volume at the freeze boundary, including Free **and Released**.
An airborne item whose center is outside that volume is not onboard cargo.
Stored/held objects travel regardless of their parked hidden transform position.

Add a server-owned transit marker (serial, zero means none) on CarryableItem and
small server Begin/EndDeckTransit methods. Begin converts selected Released
items to Free with no holder, increments motionVersion to invalidate pending
release/rest messages, removes client ownership, and installs server transit
state. Do not reuse ServerDropAt if it also moves the item or loses its pose.
When the transit marker is active, ApplyRole is kinematic and the server's
scripted follower is the sole position writer; item input is refused.

Snapshot each item's ship-local **position and rotation**, not just position.
Zero linear/angular velocity deliberately: departure secures cargo rather than
continuing a pre-departure throw after loading. State this behavior in DESIGN.
Follow source ship on the server, keep colliders from imparting moving-platform
forces (disabled while frozen), and replicate transforms normally. Clear marker
after placement, restore role-derived collider/physics, zero velocity and wake
the body. Preserve mass, slots, item identities and held/stowed presentation.

If a passenger disconnects mid-departure, their normal recovery drops Held/Stowed
items. Enroll those recovered items into this trip's frozen cargo before the move
list closes; after movement, recover into the destination world. Update both
existing item and inventory disconnect callbacks so source unload cannot delete
dropped cargo. Deduplicate by NetworkObject identity, not name. Despawned items
leave all transit lists. Never invent a second physics writer to solve this.

## 7. Gangway, boarding and departure path

Add named parts to ShipParts and the ship prefab:

- `GangwayPivot` at ship attachment; child `Gangway` mesh/collider.
- `GangwayExclusionVolume` covering the ramp and its outer lip.
- `DepartureDirection` marker defining a straight local outward direction.

Store lowered/raised local poses and collider references in ShipDepartureVisual.
Docked HQ means lowered/collidable; sea means raised/stowed with ramp collider
off. Raised shape must not block deck circulation or the camera. During lift the
ramp collider is off only after the preparation gate confirms it is unoccupied.
On return, movement stays locked until lowered and collidable.

Boarding validation requires the player's footprint on the actual deck, excluding
the gangway volume. Use a dedicated safe deck boarding volume or tighten the
current one only after auditing cargo/spawn callers; do not silently break cargo
capture by changing the shared AboardVolume. Reject players at an unsupported
edge, above the ramp, or in the wrong scene. Use explicit small tolerances and
diagnostic volumes. Loose cargo on the gangway should refuse departure with
`Clear the gangway` rather than be flung by its collider or lost invisibly.

Ship pose uses a stored source resting transform and a straight displacement:
`position = restPosition + worldDepartureDirection * distance * SmoothStep(0,1,u)`.
Keep rotation fixed in this version. Never integrate displacement frame by frame;
never update the saved rest transform from the animated pose. Both worlds need
their own validated direction away from dock/geometry. Destination starts at its
authored resting transform; no docking navigation or arrival translation.

HQ needs a continuous sea surface/horizon along the short path. If the current
stub exposes void, add a minimal matching water surface through targeted scene
setup, coordinated with the pier/ship owner; do not build a new ocean system.
Verify the whole hull/gangway path clears the pier. Engine loop must be a supplied
or existing licensed clip, locally played/faded once per transition; missing clip
is an explicitly reported audio gap, not a reason to download arbitrary assets.
No authoritative noise-bus event is added for ship audio in this task.

## 8. Failures, joining and cleanup

- Keep everyone in their source position during Preparing. Failed readiness or
  revalidation cancels without motion and restores input/cargo/gangway.
- Before scene movement begins, a timeout after visible departure may restore
  the source rest pose **under black**, lower the HQ gangway and fade back with
  a clear failure message. Do not show the ship snapping backward.
- Destination pre-load failure must not continue with a null ship. Before object
  movement, return to source as above. After partial movement, do not try an
  improvised rollback: end the room with a clear transition-failed message and
  clean up world scenes. No persistent economy is implemented by this slice.
- Black/arrival timeout for an unresponsive guest: disconnect that guest with a
  reason before proceeding; never expose a partially loaded world or silently
  unlock them. Host/local failure ends the session. Validate load messages cannot
  resurrect timed-out operations later.
- A guest leaving is removed from readiness/arrival sets; its cargo recovery
  follows section 6. Host Leave always ends the room, including during animation.
- While travelling, no new player may spawn into the departing world. Add a
  travel refusal to admission and recheck in CrewSpawner for authentication races.
  If a join was already loading/spawning, refuse departure until it completes or
  disconnects; new requests after Preparing get `Ship travelling; try again on
  arrival`. This is a temporary transition lock, not a new permanent join policy.
- Pause-menu Leave must remain accessible even under black. ScreenFade currently
  draws in front of the menu; explicitly adjust ordering or provide a Leave
  action above it. Resume must not clear the separate travel lock.
- Stop coroutine handles and unsubscribe tick/state handlers in finally/teardown.
  Clear fade, input locks, follow mode and transit markers when offline. Check
  host double callbacks, destroyed source objects and disabled-domain-reload Play.

## 9. File map and asset migration

Paths below are under `Assets/_Project/` unless prefixed with docs/.

| File | Planned change |
|---|---|
| Scripts/World/ShipDepartureState.cs | Stage enum and serialized state/ack payload definitions |
| Scripts/World/CrewDayState.cs | Server transition SyncVar, snapshot notification and lifecycle |
| Scripts/World/WorldSceneFlow.cs | Stage orchestration, cohort, cached poses, ack validation, guarded load/arrival/error paths |
| Scripts/World/ShipDepartureVisual.cs | Plain ship/gangway path evaluation, engine presentation |
| Scripts/World/ShipDepartureRider.cs | Owner-only lock/follow and cross-world cached pose |
| Scripts/World/WorldLoopSettings.cs | Validated timing/distance/defaults |
| Scripts/World/ShipParts.cs | Named gangway/path parts and safe boarding queries |
| Scripts/World/ShipControls.cs, ShipMonitor.cs | Repeated-request refusal and readable stage/status labels |
| Scripts/World/ScreenFade.cs | Black readiness and menu-safe overlay; no premature fade-in |
| Scripts/World/CrewSpawner.cs and actual admission entry point | Reject/coordinate mid-travel joining races |
| Scripts/Player/HQPlayerController.cs | Look-only input split, continuous follow, fresh-input resume |
| Scripts/Player/PlayerHudUI.cs | Hide interaction prompt while travel-locked |
| Scripts/Interaction/PlayerInventory.cs, CarryableItem.cs | Server action gate, versioned deck freeze/recovery, writer diagnostics |
| Scripts/Net/InventoryVerificationPeer.cs | Read-only departure diagnostics and existing opt-in request hooks |
| Editor/Prototype/ShipDepartureSetup.cs | Targeted idempotent gangway/player/settings/scene migration |
| Editor/Prototype/ShipDepartureChecks.cs, ShipDepartureRuntimeChecks.cs | Pure/asset and separate-peer test matrix |
| Editor/Prototype/WorldLoopChecks.cs, WorldLoopRuntimeChecks.cs, WorldSceneChecks.cs | Extend travel regression checks; replace immediate-fade assumptions |
| Editor/Prototype/ShipAtSeaValidator.cs, SessionSceneValidator.cs and ship builder | Validate required parts/refs; preserve fresh-build compatibility |
| Prefabs/World/Ship.prefab, CrewDayState.prefab; Prefabs/Player/PrototypePlayer.prefab | Required components/refs, existing GUIDs preserved |
| Existing WorldLoopSettings asset; Scenes/Prototype/HQPrototype.unity, ShipAtSea.unity | Settings and narrowly scoped dock/ramp/path/water setup |
| docs/DESIGN.md, NETWORK_CONTRACT.md, WORLD_LOOP_IMPLEMENTATION_PLAN.md, README.md, HQ_PROTOTYPE_TEST_REPORT.md | Agreed behavior, protocol, usage and actual evidence |

Discover the actual admission/ship-builder owner at implementation time. The
baseline ShipStubBuilder is explicitly temporary; do not overwrite a newer ship
prefab or create a second competing builder. Read scene/prefab modifications
already underway, coordinate with owner, and use SerializedObject/PrefabUtility.
Do not manually rewrite Unity YAML or regenerate .meta GUIDs.

Setup preflights HQ/sea, replaces only the identified old dock plank with the
ship-owned gangway, preserves other geometry, and saves explicit assets only.
Run twice and reopen scenes: no duplicate ramps/components or changed tuned
values on repeat. Do not call a whole-world rebuild to add three ship parts.
Do not modify the unrelated untracked SteamManager.unitypackage.meta.

## 10. Implementation order

1. Verify current main contains monitor/world-flow work; task branch; preserve
   unrelated work. Read current contracts and capture baseline travel results.
2. Update design/contract wording in implementation PR. Review the writer table,
   new SyncVars/messages, admission and disconnect changes with a teammate.
3. Implement snapshot/stages/serial validation and separate input lock first,
   with stationary ship. Test look/menu/late RPCs, cancellation and re-host.
4. Implement cached passenger/cargo poses, versioned deck freeze and destination
   placement. Test real non-host and carried/loose/released items before animation.
5. Add visual evaluator/gangway/path and targeted setup. Validate geometry/water
   and idempotence. Keep pose updates root-safe and single-writer.
6. Integrate fade/black/arrival gates and failure paths; remove old phase-driven
   immediate fades and unconditional OnLoadEnd fade-in. One fade coordinator.
7. Run the full matrix, inspect actual Game-view video/screenshots and logs.
   Record remaining machine/audio/latency/human-review gaps honestly.
8. Commit/push/open implementation PR when implementation is requested. Build
   matching clean-revision binaries using the project's existing build flow;
   do not relax revision admission or discard unrelated changes to build.

This document does not request building now. No Notion card creation or teammate
message is implied by the planning task. Human network review remains required
before merge; an AI checklist is not sign-off.

## 11. Verification matrix and definition of done

Use Unity MCP if connected: read current scene/Play state, stop Play for script/
asset edits, refresh/compile and use typed editor hooks for FishNet operations.
Separate request from assertions by ticks; poll bounded expected conditions.
Never certify a remote client solely from the host's view. Local editor+build
checks precede matching builds on two computers over Steam, with RTT recorded.

| ID | Scenario | Required evidence |
|---|---|---|
| D1 | HQ -> sea, host presses; repeat guest presses | Ramp rises then ~4 s visible pull-away; full black before transfer; first-person stopped arrival |
| D2 | Sea -> HQ | Same departure; no arrival translation; lowered usable gangway before unlock |
| D3 | Missing player, gangway player, edge player, pending join | Named refusal, no motion/cargo mutation; ramp cargo gives Clear the gangway |
| D4 | WASD/sprint/E/Q/click/1–4 during travel | No movement/actions; look works; direct late RPCs rejected; no action fires on unlock |
| D5 | Escape, overlay, focus loss, no input devices | Following continues; look obeys menu/focus; Leave remains usable under black |
| D6 | All four players at different spots looking around | Relative positions retained, one owner writer each, no duplicate host update or observer double motion |
| D7 | Held light/heavy, stowed items, rotated loose stack, Released deck ball | Identity/slot/mass preserved; cargo position AND rotation retained; no pending throw/rest resumes |
| D8 | Item off ship/on dock/outside airborne capture | Not silently collected; gangway obstruction refused; carried items still travel |
| D9 | Fast loads vs deliberately delayed guest | Never expose scene swap; early peer waits; black/arrival serial gates verified |
| D10 | Duplicate/stale/foreign ack, repeated destination press | One transition; old replies never release later trip |
| D11 | Guest disconnect at preparation, translation, black and load | Remaining trip resolves; recovered cargo survives; no dangling expected arrivals |
| D12 | Join during trip and join racing preparation | Clear refusal or departure waits for pending spawn; no spawn into unloading world |
| D13 | Missing destination/ship, load timeout | Defined return-under-black before move or clean room failure after partial move; no endless black |
| D14 | Host Leave in every stage, re-host and Play restart | Clean menu, restored input/fade, no old handlers/poses/transit flags |
| D15 | Repeated HQ/sea trips with source rotation | No accumulated ship offsets, same relative yaw/positions, observer visibility and cargo intact |
| D16 | Gangway/path/visual inspection | Hull clears pier, sea continues under route, ramp is ship-owned and stable at dock; engine audio once |

Pure/asset checks: pose endpoints/progress clamping, rotated ship conversions,
serial/stage rejection, unique root move lists, safe boarding vs cargo volumes,
required settings/components/refs, exact source/destination marker relationships,
setup twice/reopen and no unintended prefab changes. Preserve existing inventory,
weight, catch, world-load observer and monitor refusal regression checks.

Record trip serial/stage/tick, source/destination, expected/received cohort IDs,
player/item IDs with local poses and writers, fade alpha, lock state and load
results on both peers. Steam repeats D1/D2/D4/D7/D9/D11/D14 with roles reversed;
four-player D6 needs four actual peers. Latency/loss profiles are recorded only
if actually applied. Do not call local elapsed-time math a network test.

Visual acceptance needs a person or inspected Game-view capture showing the
moving ship against HQ, not just transform logs or a scene camera. The short
pause should read as departure and leave you oriented at arrival. Document clip
availability and observed timing; no promise of zero desync or finished polish.

Ready to hand off when this plan is available with the repository. Feature Done
requires implementation, applicable passing checks, recorded Steam/visual
evidence and human network review; this planning pass claims none of those.
