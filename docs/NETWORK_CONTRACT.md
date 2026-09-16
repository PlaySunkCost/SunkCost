# Network contract

**Read this before writing any networked code. Nobody writes a single `[ServerRpc]`
until they have read this file.**

Three people writing netcode with AI assistance will invent three incompatible
conventions in week one unless the rules are written down first. This document is
the rules. It is short on purpose. Change it by agreement, in a PR, never silently.

---

## 1. Topology

- The **host is the server.** One player's machine runs the authoritative simulation
  and also plays the game. There is no dedicated server, ever.
- Transport is **Steam P2P** (relay + NAT punchthrough). No ports, no hosting bill.
- **Gameplay outcomes are server-authoritative.** Clients send intent; the server
  validates it. Client movement and client simulation of granted physics objects
  are explicit exceptions described below, not permission to set health or loot.
- The selected stack is **FishNet**, with FishySteamworks / Steamworks.NET for
  Steam connectivity. Do not substitute Netcode for GameObjects APIs. Verify the
  installed package/import version before implementing any API examples.

## 2. Ownership

Every replicated physics object has **exactly one simulation writer** at a time.
In FishNet, an object may have a client owner or no assigned client owner. In this
document, "server-owned" means server-controlled with no assigned client owner;
the server is not itself a client owner. Ownership changes are server decisions.

- Default simulation writer is the **server**, with no assigned client owner.
- When a player grabs a physics object, **ownership transfers to that client.**
  That client simulates the object locally and broadcasts its transform.
- On release, ownership returns to the server after the object comes to rest.
- Until that handoff, the releasing client remains the simulation writer, including
  during a throw. The server tracks that it is released rather than still held.
  The free-object row below applies after handoff, not to this settling interval.
- Two clients can never own the same object. A grab request on a **Held** object
  is refused, not queued. In the HQ harness, a **Released** object may be caught:
  the server validates reach, line of sight and inventory eligibility, then
  transfers it to the accepted catcher (or stows it directly if their hand is busy
  and a slot is free). Competing catches are serialized by the server.
- **Stowed** items are still spawned, server-owned, hidden, non-colliding and
  kinematic. Only their carrier may equip them. Inventory slots are server-written.
  When the target has no slot (four occupied slots, or a **two-handed** item,
  which never fits a slot) and a slot item is equipped, a valid grab stows the
  equipped item in its existing slot and holds the target as overflow. The server
  performs both transitions in the same request; all four slot ids remain
  unchanged. With an overflow item in the hands, a slot-able target with a free
  slot is stowed directly (no hand change); anything else is refused.
  Held items are kinematic too: their holder writes the transform at the hold
  point for the item's grip (right hand, or the centred two-handed point).
  Kinematic does not by itself mean this peer is not the writer. The releasing
  writer also places a dropped item (radius-aware, clear of its own capsule and
  the floor); the server places disconnect recoveries.

Why: competing physics writers cause conflicting transforms. This project's
ownership model gives one peer responsibility for each replicated body; other
peers render its replicated state. **Do not attempt deterministic physics.**

### Grab and release flow

1. Client requests a grab through its owned player interaction object. Do not
   attach the target before approval. Cosmetic input feedback is allowed.
2. Server identifies the sender from the connection and validates the target,
   range, alive/dive state, carrying eligibility and current ownership.
3. Server accepts only one competing request, records the holder, and grants
   FishNet ownership to that connection. Denied requests get a response.
4. The granted client begins simulation only when ownership and authoritative
   holder state agree. Other peers stop simulating that body and interpolate.
   Ownership and a separate notification must not be assumed to arrive together.
5. On a validated release/throw, mark it released. The releasing client continues
   simulation until the server approves the rest/handoff condition, preserving
   the existing release policy. Replicate the final motion state at transfer.
6. After handoff, the server resumes simulation and clears client ownership.
   On disconnect, the server takes over immediately, even if it is still moving.

The HQ basketball prototype uses linear speed below **0.15 m/s**, angular speed
below **0.5 rad/s**, and **0.5 seconds** continuously below both thresholds as its
rest condition. It forces handoff after **4 seconds** so ownership cannot remain
stuck indefinitely. On 14 September 2026 the user authorized catching moving
balls: a Released item is catchable before rest. Each carry/release transition
advances a server-written motion version. Release impulses and rest requests must
match that version, ownership and holder identity, so a late message from the
previous throw cannot undo a catch. Held/Stowed items cannot be stolen. Moving-elevator
handoff behavior remains undefined.
Two-person carrying has no defined protocol yet: do not assume a shared writer.

## 3. Who decides what

| State | Decided by | Notes |
|---|---|---|
| Player movement | Client, corrected by server | Clients move themselves (weight factor, jump and gravity included); server validates position deltas loosely |
| Stance (crouched / standing) | **Server only** | `PlayerStance`: the owner requests with a serial (`ServerRpc`); the server checks headroom at the position it sees and writes `StanceState { Crouched, Serial }` — the serial advances on refusal too. The owner predicts crouching, never standing: it expands only after the server accepts *and* its own headroom check passes. Every peer applies the accepted posture to its capsule, eyes and body |
| Grabbed object transform | Owning client | Broadcast, not simulated elsewhere |
| Released object before rest/handoff | Releasing client | Server validates release and decides handoff. The start pose is a transaction: the owner proposes a clear pose from `ReleasePlacement`, the server re-checks it from its view (bounded distance, in front, clear of world and other players) before `Held → Released`; the owner re-checks its geometry before activating physics and cancels (`ServerCancelRelease`, same version) if it changed, whereupon the server restores Held, the slot and the mass together. No client writes SyncVars; a stale version is ignored |
| Free object transform | Server | Standard replication |
| Inventory contents and slot assignment | **Server only** | Four slots plus hands; clients request grabs, equips, drops and use |
| Stowed item | Server | Hidden, kinematic; carrier identity retained; equipping grants ownership |
| Carried mass | **Server only** | Sum of the Rigidbody mass of the items a player holds or stows, from the items' server state; clients derive the meter, the speed factor and the "cannot move" state from the shared `WeightSettings` asset |
| Oxygen, health, damage | **Server only** | Clients display, never compute |
| Loot value, quota, funds | **Server only** | Never trust a client number |
| Monster AI and targeting | **Server only** | Clients receive positions and animation state |
| Noise events | **Server only** | See `NoiseSystem` — clients may play the sound, never emit the event |
| Elevator state | **Server only** | Clients send a request, server decides |
| Site seed and dive lifecycle | **Server only** | Replicate current state to supported entrants |
| Scene membership | **Server only** | Which world scene each connection is in and which world scenes are loaded; a client loads and unloads exactly what the server tells it; observers follow scene membership (section 10) |
| Day state | **Server only** | Phase (at HQ, sailing, at sea, dive in progress), current world and destination, and the last refusal (`Refusal { Serial, Text }`, shown by every monitor and the deck cabin's panel for `refusalDisplaySeconds`), on the global `CrewDayState` object; the day counter and the surfaced/dead lists join it with the day-state card |
| Cabin ride | **Server only** | `CabinRideState { Serial, Stage, Direction, StageStartTick, StageDurationTicks }` and the rosters `Riders` (client ids on the ride), `Placements` (`RiderPlacement { ClientId, Local, Yaw }`, each rider's spot in the cabin frame) and `Below` (client ids whose player is at the seafloor) on `CrewDayState`; `ElevatorPhase { Serial, State, Upward, StartTick, DurationTicks }` for the seafloor car (section 9). The deck cabin's doors and panel and the car's transform, doors and gate are derived on every peer from these and the synchronized tick — nothing on the ship is networked, `DiveSite01` holds no NetworkObject |
| Cabin request | **Server only** | E on the deck cabin's button (`RequestCabin`) or the car's panel (`RequestCar`) is a `ServerRpc` on the pressing player's `ShipControls`; the server checks the world, the car's state, that the presser stands inside that cabin, and takes everyone inside it (`WorldSceneFlow.ServerRequestDive` / `ServerRequestSurface`); a client never starts a ride or moves the car itself |
| Sailing request | **Server only** | E on a monitor button is a `ServerRpc` on the pressing player's `ShipControls`; the server checks the presser is aboard, then `WorldSceneFlow.ServerSail` (everyone aboard, phase); nothing on the ship is networked and a client never starts a sail itself |

Rule of thumb: **if getting it wrong would let someone cheat or desync the run,
the server decides.**

## 4. Naming

FishNet attributes, consistent names, no exceptions:

```csharp
[ServerRpc(RequireOwnership = true)]
void ServerRequestGrab(NetworkObject target) { }   // client -> server, a REQUEST

[ObserversRpc]
void RpcPlayGrabEffect(Vector3 at) { }             // server -> everyone, cosmetic

[TargetRpc]
void TargetShowMessage(NetworkConnection conn, string text) { }  // server -> one client
```

- Client-to-server methods begin **`ServerRequest…`** or **`ServerSet…`**. They are
  requests, and the server may say no.
- Server-to-all methods begin **`Rpc…`**.
- Server-to-one methods begin **`Target…`**.
- Synced state uses `SyncVar` / `SyncList` and is **written only on the server**.
- Replicated state must initialize correctly without replaying a past event RPC.
  Use state change callbacks for presentation; RPCs are not durable storage.
- Validate every client request: sender authorization, target existence, range,
  current state and applicable numeric bounds. Do not trust client-computed rewards
  or arbitrary noise positions/radii. Reject stale or duplicate actions safely.
- Requests that can fail have a caller-visible failure path. Do not depend on a
  notification and an ownership update arriving in the same frame.

If a method name does not say who calls it and who runs it, rename it.

## 5. Hard rules

1. **Server decides gameplay outcomes.** Client movement and physics simulation
   follow only the explicit exceptions in sections 2 and 3.
2. **One writer per piece of state.** If two systems write the same SyncVar, one of
   them is wrong — fix the design, don't add a lock.
3. **Never send a value the server can compute.** A client saying "I picked up 1,400
   gold" is a bug waiting to be exploited.
4. **RPCs are not free.** Nothing per-frame. Movement goes through the transform
   syncer; everything else is event-driven.
5. **Nobody merges their own change to this contract.** Gameplay tweaks, fine.
   Anything here gets a second pair of eyes.

## 6. Disconnects

- A disconnected player's body **stays where it fell** and keeps its inventory.
  It can be dragged to the elevator like a corpse.
- Players join or rejoin **at HQ**, and **on the ship at sea between days**
  (design section 1). A joiner is loaded into the crew's current world first and
  spawned at that world's spawn points once the server has confirmed it is in
  the scene. While a dive is in progress the admission handshake refuses with
  `DiveInProgress` ("Dive in progress — join between days"). This does not
  change the HQ-only save point.
- A disconnect while below counts as dead for the day: the harness drop rule
  below applies to the items, the player leaves the seafloor list, and the day
  can end without them (decided 14 September 2026; the day-state card enforces
  it).
- Recovering a disconnected or dead player's body preserves purchased gear under
  the design's recovery rule. Unrecovered purchases are lost; the free base kit
  remains available. Banked loot is unaffected. Held objects must not disappear
  with the connection; the server handles their ownership and inventory state.
- The **host owns the save.** If the host quits, the run ends. Host migration is
  explicitly out of scope for v1 — revisit only if it turns out to be cheap.
- The HQ harness has four inventory slots plus hands, but no death or persistent
  run. In this harness only, disconnect despawns the temporary avatar and drops
  held, stowed and still-released items near that player's last position under
  server simulation. This does not replace the real game's corpse/inventory rule.
- Harness leave semantics: a leaving client stops its own connection (a clean
  disconnect, so the host does not wait for a timeout); a leaving host stops the
  server, which disconnects every client. There is no host migration. The
  transport is bound once per process because FishNet's Server/ClientManagers
  subscribe to `TransportManager.Transport` at initialization; switching
  Local/Steam requires a restart.
- Ordering rule learned from the harness: a `TargetRpc` is written to the
  outgoing buffer immediately, while SyncVars flush at tick end, so a remote
  client can run the RPC before the same tick's SyncVar values arrive (the host
  never sees this because its values are set in-process). Client-side state that
  depends on both must be derived from the SyncVars' `OnChange` (or tolerate
  either order), never from the RPC alone.

## 7. Tick rate

The server tick is **60 Hz — the physics rate** (Unity `Fixed Timestep` 1/60),
serialized on the Session scene's `TimeManager` and checked by the session
validator, which also fails if the physics step drifts from it. Decided 16
September 2026 (Dan, after the smoothness work): with the tick equal to the
physics step every tick samples exactly one simulation step, so replicated
bodies carry no sampling jitter; input reaches the server within about 8 ms and
remote objects show 33 ms behind (two ticks of interpolation) instead of 66 ms.
60 rather than 50 because the displays we target (60, 120, 144, 240 Hz) are
multiples of it, so physics and rendering do not beat against each other. The
two rates change together or not at all; going above the physics rate is the
one setting that costs bandwidth and host CPU and shows nothing. Rendering is a
separate clock: everything the player sees is interpolated or placed sub-tick,
so frame rate never depends on the tick. (The prototype started at 30 Hz,
FishNet's default, with physics at Unity's default 50.)

---

## 8. Noise

Use `Assets/_Project/Scripts/Noise/NoiseEvent.cs` and `NoiseSystem.Emit`:

```csharp
NoiseEvent(Vector3 position, float radius, NoiseKind kind, int sourceId = 0)
```

- Radius is in **metres**, not arbitrary loudness units. Route gameplay hearing
  through this bus and `INoiseListener`; do not create a parallel hearing system.
- Only the server emits gameplay noise and runs monster decisions. For client
  actions, validate intent on the server and derive the noise there. Emit once
  per accepted noise-producing action, not again on every receiving peer.
- Local sound effects for immediate feedback are allowed; they do not emit a
  gameplay event or change a monster's state.
- Server startup/shutdown must set/reset the bus's server guard and clean up
  scene listeners. The current scaffold is not proof of network integration.
- `SourceId` is intended as a network identity; the current emitter uses Unity
  instance IDs. Map it to a stable network identity when integrating networking,
  and do not compare instance IDs across machines.
- Hearing uses the bus. This does not forbid navigation, collision queries, or
  a separately approved creature sense; new senses belong in the design first.

## 9. Elevator, oxygen and spectating

- The server controls elevator requests, boarding/cargo state and motion. It
  carries **any number of the crew and any amount of cargo together**, including
  unattended loot or bodies. Do not impose a one-person-or-one-load limit.
- Travel takes the **same time in each direction**, always, regardless of
  weight; the time is derived on every peer from the site's depth and the
  serialized speed profile (`ElevatorMath`; about 17.3 s on the 45 m prototype
  site, formerly a fixed 15 s), never sent. A rider-controlled return has no added wait; the
  unmanned automatic return additionally waits a tuned delay (a new serialized
  field on `ElevatorController`, default 3 s) at the top before descending,
  and only when the living-players-below check below is true. Keep timing
  configurable.
- Each transition into motion emits authoritative elevator noise. Persistent
  elevator state, motion progress and occupants must be reconstructible from sync
  state. A surfaced diver cannot descend again during that dive.
- The server owns oxygen and item use. Each diver has one equipped tank of a
  selected size. During a dive, air is restored only by using an air-restoring
  item; there are no refill stations or passive refills. Validate and apply item
  consumption/restoration once. Exact item tuning remains open in the design.
- Spectating renders replicated state locally; do not stream another player's
  video. Dead players select divers independently. Surfaced players share one TV
  and one selected diver, with selection coordinated by the server.
- Voice membership is independent of camera/AudioListener position: dead players
  talk only to dead players, surfaced players to surfaced players, and divers use
  proximity voice. A spectating camera must not open access to divers' voice.
  A surface radio remains a proposed upgrade, not an approved exception yet.
- Decided 14 September 2026, implemented 15 September 2026 as the **cabin
  ride** (`WorldSceneFlow.Cabin`, `docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md`
  sections 5.3, 5.4 and 6, without the day rules — Dan: "start without day
  state, just going up and down as we wish"): the only moving cabin is in the
  dive scene; the cabin on the ship's deck is a static object with doors.
  Riders change scene at the button on the way down (behind sealed doors,
  under the suit fade) and at the top on the way up (once the car is dry and
  stopped, doors still shut). The car is driven on every peer from the
  server's `ElevatorPhase` and the synchronized tick (`ElevatorController`
  driven mode: state, direction and elapsed in; transform, door and gate
  events out); its own state machine only runs in Idan's local harness. The
  water level will be derived the same way. Riders are **locked** for the ride
  (input off, look free): the owner keeps its captured cabin-frame spot every
  frame (`ShipDepartureRider`, the ship-trip rider generalised to a
  `CabinFrame`), and the other peers place its copy from the server's
  `RiderPlacement` while the car moves, so a round trip's worth of
  `NetworkTransform` lag never sinks a friend through the floor. Nothing is
  parented under the car. Free walking inside the moving cabin (the
  moving-platform handoff under Open) stays open. Not yet: the all-aboard rule,
  once-per-day, the day counter — they arrive with the day-state card; until
  then the deck button takes whoever stands in the cabin, at sea, whenever the
  car is up, and the car button takes whoever stands in the car at the bottom.
- Decided jointly by Idan and Dan on a call (15 September 2026), not yet
  implemented — this section still needs the PR review `CONVENTIONS.md`
  requires for contract changes before it counts as reviewed:
  - **Living players below.** The server tracks whether at least one living
    player remains at the dive site; a disconnected player does not count as
    living. This single check gates both rows below.
  - **Automatic empty return.** Gated on the living-players-below check above
    (see the timing bullet earlier in this section).
  - **Day-end monitor unlock.** The day no longer ends automatically when
    everyone is up or dead. The crew ends it with a deck/monitor action, and
    the monitor unlocks on exactly the same living-players-below check — one
    check, not two.

## 10. Scene flow

Decided 14 September 2026 with the world-loop plan; the scene-flow card makes
the sailing part true, the elevator and deck-cabin cards the rest.

- One persistent `Session` scene holds the network root; FishNet never loads or
  unloads it. The three world scenes (`HQPrototype`, `ShipAtSea`, `DiveSite01`)
  are loaded **per connection**, additively (`ReplaceOption.None`), never
  auto-unloaded, and unloaded explicitly: `KeepUnused` when other connections
  stay in the scene, `UnloadUnused` when the last ones leave (HQ on sailing out,
  the sea on sailing home, the site at day end). The ship at sea sits 500 m from
  the site so both can be loaded on the host during a day.
- Observers follow scene membership through an `ObserverManager` whose default
  condition is FishNet's `SceneCondition`. A player observes only the objects in
  the scene it is in, its own objects always, and global objects everywhere.
- Only spawned root objects travel between scenes (`MovedNetworkObjects`). Scene
  objects never do, so every carryable that may leave a scene is a
  runtime-spawned prefab instance; a player's Held and Stowed items are listed
  next to it in every move, and loose items inside the ship's `AboardVolume` sail
  with the ship at the same ship-relative spot.
- A scene broadcast that depends on a SyncVar the clients react to (a phase
  change, a cabin state) is sent at least `syncFlushTicks` (2) ticks after the
  SyncVar is set — the ordering rule of section 6.
- Positions after a move are computed by the client that simulates the player
  (section 3), on its own load-end event, from the source and destination
  transforms that are both still loaded at that moment; no RPC carries them.
  The `NetworkTransform` snap (`Teleport()`) keeps observers from interpolating
  across the world.
- A sail is a **trip** (`WorldSceneFlow.TripRoutine`, decided 15 September
  2026 with `docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md`): one server-written
  `ShipDepartureState { Serial, Stage, FromWorld, ToWorld, StageStartTick,
  StageDurationTicks }` SyncVar on `CrewDayState`, stages `Preparing →
  RaisingGangway → PullingAway → FadingOut → Loading → Arriving → Complete`
  (or `Cancelled`). `DayPhase` stays `Sailing`/`SailingHome` throughout. Each
  stage carries the server tick it started on; every peer evaluates the ship's
  displacement and the gangway angle from the synchronized tick
  (`ShipDepartureVisual`), so the ship itself is never replicated and nothing
  is parented under it. Clients answer stages with a `DepartureAckBroadcast
  { Serial, Kind, World }` — `Prepared` (locked at the captured deck spot),
  `Black` (screen fully black), `Arrived` (placed on the destination ship); the
  server ignores anything from outside the trip's cohort or with another serial.
- Order on the server: refuse unless every active connection's player stands on
  the deck proper (`SafeDeckVolume` minus `GangwayExclusionVolume`; the
  refusal names who is not) and no join is pending; `Preparing` and wait for
  every `Prepared` (`prepareTimeoutSeconds`, else cancel) and `syncFlushTicks`;
  re-check aboard (a player who stepped onto the gangway before the lock
  cancels the trip, nobody is teleported aboard); freeze loose deck cargo
  (Free and Released items in `AboardVolume`, position and rotation, kinematic,
  server-followed) and close the root move list (players, Held, Stowed, cargo);
  `RaisingGangway`, `PullingAway` (the server drags the cargo with the moving
  ship); `FadingOut` and wait for every `Black` (a guest that never answers is
  disconnected with a reason, never shown a half-loaded world); `Loading`: load
  the destination server-side if needed, add **every** traveller to the
  destination before the one `LoadConnectionScenes` with the moved objects
  (observers rebuilt once, nobody blinks out of view), place the cargo on the
  destination ship, wait for every `Arrived`; `Arriving`: fade in, at HQ also
  the gangway lowering, unload the source scene; only then `ServerArrive`,
  `Complete`. FishNet raises load-end twice on a host (server pass, client
  pass); placement runs on the client pass only.
- Writers during a trip: the server writes the trip state, the cohort, the
  cargo poses and the day state; the owning client writes its own player root
  (`ShipDepartureRider` keeps the captured ship-relative spot every frame,
  one `Teleport()` at the cross-world placement); remote players arrive through
  their `NetworkTransform` only; frozen cargo is server-written and replicated
  normally. Item requests, monitor presses and joins are refused while
  `CrewDayState.Travelling` (`RefuseReason.Travelling`, "Ship travelling; try
  again on arrival", `AdmissionRejection.ShipTravelling`); a connection that
  authenticated before the lock but loads after it is disconnected rather than
  spawned into a departing world. A disconnected passenger's dropped items join
  the frozen cargo. A trip that cancels before `Loading` changes nothing.
- A cabin ride reuses the trip machinery (cohort, `DepartureAckBroadcast` with
  the ride's serial, kick of an unresponsive guest) with its own state. Down:
  `Preparing` (riders lock at their deck-cabin spot, `Prepared`), capture of
  the placements after `syncFlushTicks`, `Sealing` (`cabinSealSeconds`),
  `FadingOut` (`suitFadeSeconds`, "Putting on the suit…", `Black`), `Loading`
  (the site loads server-side if nobody was below — fresh every time the
  seafloor empties; the car is put `AtTop`; every rider is added to the site
  before the one `LoadConnectionScenes` with players and carried items; each
  rider places itself at the same cabin-frame spot in the car, `Arrived`;
  `ShipAtSea` is unloaded for the riders only), `Arriving` (fade in inside the
  closed car), then `ElevatorPhase` `Descending`; `Complete` when the car is
  `AtBottom` — riders unlock, the car's doors and the shaft gate open. Up:
  `Preparing` (lock at the car spot), `Sealing` (the car's doors, `Upward`;
  riders are free and may still step out), then once the car is `Ascending`
  the server drops from the ride everyone not standing inside the sealed car —
  they stay listed below and the car comes back for them; an empty car moves
  nobody — `Riding` (`Ascending`, riders follow the car), `Loading` at `AtTop` (riders
  moved into `ShipAtSea`, placed in the deck cabin, `DiveSite01` unloaded for
  them and from the server when nobody remains below), `Arriving` (the deck
  cabin's doors open over `cabinSealSeconds`), `Complete`. If someone is still
  below the car seals and descends again, empty. The deck button refuses at HQ
  ("Not at sea"), outside the cabin, while a ride runs ("Cabin in use") and
  while the car is away ("Cabin below"; an empty car at the bottom with nobody
  below is called up instead); the monitor refuses to sail while anyone is
  below or the car is away ("Divers below"); joins are refused during a ride.
  A rider that disconnects leaves every roster. The shaft tube adds **no sync
  state**: the car's position is the only replicated motion; the water standing
  in the car (`CabinWater`: sea level minus the car floor, clamped to the
  cabin), the tube gate's leaves and each player's own submersion
  (`PlayerSubmersion`, eye below sea level, local presentation and an event
  hook for the air card) are computed per peer from that position and the
  site's `seaLevelY`, so a late joiner is right on its first frame. The driven
  car is placed every frame from the authoritative `StartTick` plus the time
  into the current tick (`TimeManager.GetTickElapsedAsDouble`), so it glides
  between ticks on every peer without any extra state; the local rider is
  carried by the car's frame delta with the car's colliders synced in the
  direction's safe order (up: rider then sync; down: sync then rider) — a
  local `CharacterController` concern, nothing replicated.
- When neither the server nor the client is running any more, every world scene
  is unloaded locally; the menu is the Session scene.
- Workarounds for FishNet 4.7.3 on Unity 6, kept in `WorldSceneFlow` and to be
  re-checked on a FishNet upgrade:
  - `SceneLookupData`'s `!=` dereferences its operands, so comparing an entry
    of `SceneLookupDatas` with `null` must use `is null`.
  - Unity 6's `Scene.GetRootGameObjects(List)` does not clear the list for an
    empty scene, so FishNet's refill of its `MovedObjectsHolder` scene re-moves
    the previous load's objects on any later load that moves nothing (a
    server-only pre-load, a joiner's load) — every player of the last sail would
    be yanked into that scene. `EnsureHolderKeepAlive()` keeps one dummy root
    (`FishNetHolderKeepAlive`) in the holder before every load and puts it back
    after each load-end; every code path that calls `LoadConnectionScenes` must
    call it first.
  - A pure client instantiates spawned prefabs into its active scene, so an
    object the server spawns into the destination while a sail is under way (the
    fresh HQ loot fixture on the way home) can land in the scene the client is
    about to unload and die with it. On unload-start a pure client moves spawned
    non-scene root network objects out of the unloading scene into `Session`;
    FishNet does this for the host only. Their lifetime stays the server's
    despawn.

## 11. Verification and changes

For networking changes, record a host and separate non-host reproduction, actual
results, and any missing runtime validation. Validate Steam transport using builds
on separate machines. Test contention, release/handoff, disconnect and applicable
gameplay rules under latency/packet loss where possible. Compilation is not proof
of synchronization. See [WORKFLOW.md](WORKFLOW.md) and the repository's
[review prompts](../.claude/agents/) for the process and reusable checklists.

Contract changes normally land through a PR with a teammate's review before
dependent implementation. An AI report does not replace that review. Do not mark
this document "signed off" unless that review actually occurred.

## Open

- Interest management (do distant players need updates?) — defer until a real map exists.
- Anti-cheat systems — out of scope for v1. Friends-only lobbies do not remove
  the need to validate requests for stale state, bugs and unexpected inputs.
- Moving-elevator physics handoff; see section 2. Riders walk inside the moving
  cabin (decided 14 September 2026), so the world-loop plan has to define it.
- Two-person carrying protocol, if included; a single simulation writer still holds.
- Movement correction and transform interpolation settings for the selected
  FishNet version; the tick is 60 Hz (section 7).

API reference: [FishNet ownership](https://fish-networking.gitbook.io/docs/guides/features/ownership).
