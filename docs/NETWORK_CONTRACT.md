# Network contract

## Proximity voice transport (16 September 2026)

- Voice uses authenticated FishNet broadcasts on the existing connection. Protocol
  version is **2**. It does not change gameplay ownership, physics or Noise events.
- The client requests start/stop reliably. The host issues a fresh 64-bit stream
  generation and acknowledges the request; no frames are sent before acknowledgement.
  Speaker identity comes from the authenticated sending connection, never the
  claimed frame ID. Stops discard local capture and queued playback.
- Frames carry a generation, 16-bit sequence and at most 400 Opus bytes (48 kHz
  mono, 960 samples / 20 ms, 24 kbit/s constrained VBR). The custom serializer
  bounds headers and payload lengths before slicing; the callback copies only
  accepted bounded payloads. Frames use Unreliable, control uses Reliable.
- Host checks generation, physical player/world membership, a 60 frame/s token
  bucket with a 10-frame burst, and a 16-frame duplicate/reorder window. Start
  requests are limited to 8/s; stops are never suppressed. The host relays only
  to authenticated other players in the same eligible world within 22 m by
  default. The receiver fades to zero at 20 m. No local self-playback.
- Scene membership and CrewDayState Below/rider state decide eligibility;
  cameras never grant cross-world access. Sailing, a rider in transit, unknown
  scenes and contradictory membership deny delivery. Travel invalidates the
  stream and creates a fresh generation after arrival while retaining the user's
  requested mic state. Join/rejoin/re-host always reset that state to OFF.
- Death exists (17 September 2026): `ProximityVoice.WorldOf` puts the dead in a
  world of their own (`DeadWorld`), and the server routes by state — never by
  camera or by a loaded scene. Each relayed frame carries a **route** byte
  (protocol 3): `Direct` (a living speaker to the living listeners in its
  proximity set, mixed at the listener's head), `Spectate` (a living speaker to
  every dead listener whose spectate target is the speaker or is in the speaker's
  proximity set, mixed at the target's head), `Dead` (a dead speaker to every
  dead listener anywhere, full gain, no place) and `TV` (a diver whose frame the
  deck TV's channel diver would hear, to every living client on the ship, mixed
  at the TV's speaker: the gain the channel diver would hear it at, times the
  listener's distance to the TV). A living client discards `Spectate`/`Dead`
  frames and takes `TV` only on the ship; a dead client discards `Direct`/`TV`.
  The "who is talking" indicator (17 September 2026, design §5) is local
  presentation of the same byte: `ProximityVoice.Heard` lists each receiver
  the mixer gave a gain above zero whose last decoded frame was louder than
  `SpeechLevel`, with its route; nothing is sent for it.
- Decoder reorder buffering starts at 60 ms; packet/capture queues are bounded,
  stale capture older than 200 ms is discarded, and a long receive gap silences
  output. Codec workers own their handles; stop joins workers before releasing
  capture/context. Audio callbacks use bounded rings, without Unity API calls,
  managed allocations or locks. No speech is recorded to disk.
- Human networking review is required before merging. Automated local tests do
  not substitute for audible two-machine Steam checks. See
  [voice test report](PROXIMITY_VOICE_TEST_REPORT.md).

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
  **Same world (17 September 2026):** the server also refuses a grab of an item
  that does not stand in the requester's world scene (or in the session scene,
  where server spawns land) — the worlds share one physics space, so reach and
  line of sight alone would let a modified client take an item from the other
  world's ship or seafloor.
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
| Item value (loot) | Server, rolled once when the item spawns from the prefab's `valueMin..valueMax` | Clients only display it (the visor). `SyncVar<int>`, written once; never re-rolled while the item exists; the dive site spawns its loot fresh on every load, so every dive re-rolls. Non-loot (the HQ balls) has no range and no value. |
| Player display name and colour | Server: seeds `Diver N` and the seat's swatch when the player spawns, then writes the owner's request, sanitised (`PlayerIdentity.Sanitize`: trimmed, printable, no `<>`, at most 16 characters, empty falls back; `SanitizeColour`: an index into `PlayerPalette`'s 16 swatches, anything else falls back to the seat) | The owner's client sends `ServerRequestIdentity(name, colour)` on its player's `OnStartClient` from what is saved on that machine (PlayerPrefs; the Steam persona and a random swatch by default; the name edited in the lobby before entering a room, the colour at the HQ wall's colour panel, `ServerRequestColour(index)`). `SyncVar<string>` + `SyncVar<byte>` on `PlayerIdentity`; clients only display them (the body, the visor's crew tags, the roster, the F3 square). Neither need be unique; the server never trusts what is sent. |
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
| Scene membership | **Server only** | Which world scene each connection is in and which world scenes are loaded; a client loads and unloads exactly what the server tells it; observers follow scene membership (section 10). **Physical world vs watched world (17 September 2026, spectating card 2):** a client loads the world its player object stands in and, while it spectates a living player in the other world, that world too — `WorldSceneFlow.ServerWatch` adds the connection to the scene and loads it with `WatchDataFor` (no preferred active scene, nothing moved), `ServerUnwatch` unloads it (`KeepUnused` on the server) when the reason ends; the player object never moves for watching. Eligibility for voice, items and the cabin keeps using the physical world; a watched scene grants nothing. A site unload takes its watchers with it; a dead player carried to the ship drops a watched ship first; living ship clients watch the site while the TV has a channel (card 3) |
| Day state | **Server only** | Phase (at HQ, sailing, at sea, dive in progress), current world and destination, and the last refusal (`Refusal { Serial, Text }`, shown by every monitor and the deck cabin's panel for `refusalDisplaySeconds`), on the global `CrewDayState` object. **Day and payday (16 September 2026):** `Day` (0 at HQ, 1..`daysPerCycle` at sea) and `Payday` are written only by the server: day 1 on arrival from HQ; the phase becomes *dive in progress* when the riders stand in the car below (`ServerBeginDay`); when `Below` is empty the dive is done (`ServerEndDayIfDone` — the last rider up, or the last one below disconnecting: phase back to at-sea, `DiveDone` set); the crew's End day button on the monitor (`ShipControls` `[ServerRpc]` → `WorldSceneFlow.ServerEndDay` → `CrewDayState.ServerEndDay`, refused unless at sea with `DiveDone` and nobody below) advances `Day` or sets `Payday` after the last day and clears `DiveDone`; paying the quota resets the count. The deck cabin's server check refuses on payday, during a day in progress (naming who is below), once today's dive is done ("Dive done — end the day at the monitor") and unless every authenticated connection's player stands in the cabin (naming who is missing); the monitor's `ServerCanSail` refuses every destination but HQ on payday. Clients only read these for the panels, the visor's `DAY n/N` corner label and F3; a dead list is still to come with the death system (living = connected until then). **Money (16 September 2026):** `Balance`, `BoxValue` (the loose items inside the ship's storage room, summed by the server four times a second) and `LastPay` (`PayReport { Serial, Sales, Quota, Had, Balance, Paid, Lost }`) are server-written on the same object; the pay button is a `[ServerRpc]` on the presser's `ShipControls` → `WorldSceneFlow.ServerPay` (docked at HQ, a cycle to pay for), which despawns every loose item inside the docked ship's storage volume and adds their values to the balance — all of them, nothing charged (Dan, 17 September 2026: every dollar handed over is the crew's); the quota (`WorldLoopSettings.quotaPerCycle`) is judged against `CycleSales`, the server-written sum handed over this cycle (`PayReport.Had`), which resets with the day count; short before payday means `Short` (the sales are banked and count toward the cycle, the count goes on), short at payday means `Lost` and a balance of zero. `ServerCanSail` refuses HQ while `DiveDone` ("Dive done — End day first"; Dan, 17 September 2026). Clients read all of it for the box readout, the visor's `BOX $x/$q · ON ME $y` line (what is on you is summed locally from the replicated item values) and the HQ board **Death (17 September 2026, card 1):** `Dead` (client ids) is server-written by `ServerPlayerDied` (out of `Below`, riders and placements; `HQPlayerController.dead` set so every peer hides the body and drops the capsule) and cleared by `ServerRevive` at End day; the debug kill is a `[ServerRpc]` refused unless the requester is alive and below; the body is a server-spawned `PlayerBody` carryable; the dead never count as living for the all-aboard check, the car's return or the dive's end, ride to the ship with the site's unload (`ServerMoveDeadToShip`, moved objects in the connection's load) and are placed by `TargetPlace` at revival. **Spectating (17 September 2026, card 2):** `Spectate` (`SpectateEntry { Dead, Target }`, one per dead player) is written only by `WorldSceneFlow` — the nearest living player at death, the next living client id on the dead owner's left click (`HQPlayerController` `[ServerRpc] ServerRequestNextSpectate` → `ServerSpectateNext`), re-validated four times a second (a target that died or left is replaced, none → −1) and cleared at revival; `WatchersOf` is derived from it on every peer for the ON AIR mark. `HQPlayerController.lookPitch` is a server-written `SyncVar<sbyte>` fed by the owner's `[ServerRpc]` at most 20×/s when it moved by 1° (one byte; remote copies ease toward it over ~80 ms), so a spectator camera can sit on a remote copy's eye anchor with the owner's pitch; the spectator camera is local presentation (`SpectatorView`) and decides nothing. **The TV (17 September 2026, card 3):** `TvChannel` (a living diver below, or −1) is written only by `WorldSceneFlow` — the first living diver below whenever the current channel is not one (re-validated four times a second), the next by client id on E at the screen (`ShipControls` `[ServerRpc] ServerRequestTvNext`, presser aboard and alive → `ServerTvNext`); `WatchersOf` counts it. While a channel exists every living client whose object is on the ship holds the dive world as a watcher (the same `ServerWatch` path; a rider drops it before the ride's load); the screen (`ShipTV`, a render-texture camera on the channel diver's eye anchor) and its caption are local presentation |
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

**Interpolation buffer (17 September 2026):** the player prefab's
`NetworkTransform` keeps FishNet's 2-tick (33 ms) buffer. Dan and Idan saw a
spectator's view and the deck TV step over Steam (Build 78); the `smooth`
matrix (`RemoteSmoothnessRuntimeChecks`: a guest build turning while its
outgoing packets go through FishNet's latency simulator with ±40 ms jitter,
3 % loss, 5 % reordering) reproduced it on the raw transform — about 5 % of
frames frozen, catch-ups of 20 frames' worth in one frame — and a 6-tick
buffer did not remove it (bursts still overflow FishNet's queue, which snaps),
so the buffer stays. What watchers see is fixed in presentation instead: a
remote copy's eyes (`HQPlayerController.EyePose`, the one place a spectator
camera and the TV camera are placed from) ease over 100 ms behind the
transform, turning a late packet and its catch-up into a slow and a quick turn
of the head; on the same link the eased eyes measure 0 frozen frames and a
worst catch-up under 2. Nothing replicated changes.

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
  proximity voice. A spectating camera does not open access by itself: the dead
  hear a living speaker only because the server routes by their spectate target
  (the `Spectate` route above), never because their client has a scene loaded.
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
  below the car seals and descends again, empty. **Cabin cargo** (built 16
  September 2026, Dan: "items on the elevator floor should stay on it"): a
  loose item on the cabin's floor when the ride's placements are captured (the
  deck cabin going down, the car going up) — and again whatever lies loose
  there when the move list closes at `Loading` (thrown or put down during the
  ride; Dan: "some of the items on the ground disappeared" at the top) — is
  frozen exactly as deck cargo is
  (`ServerBeginCabinTransit`: Free, server-owned, kinematic, colliders off, its
  spot kept in the cabin frame), server-followed through the ride, moved with
  the riders in the same `LoadConnectionScenes`, placed by the server at the
  same cabin-frame spot in the other cabin (one `Teleport()`), and released
  when the ride completes. Unlike deck cargo it **keeps its colliders** (it is
  kinematic and server-followed, so nothing is imparted), so a rider can look
  at it and grab it mid-ride: a grab takes it out of the cargo list and ends
  its transit; it then travels in the hand like anything held. A body a peer
  simulates inside the moving car (the thrower's Released item, the server's
  Free one before rest) is moved by the car's frame delta every frame by that
  peer (`CarryWithCar`), so it flies and lands as in a still room and cannot
  fall through a floor that moved on between physics steps — presentation of
  the writer's own simulation, not a second writer. So what lies on the floor going down lies on the
  car's floor at the bottom, and what lies on the car's floor going up lies on
  the deck cabin's floor. Anything loose that is inside the car while it moves
  and is not frozen cargo (dropped mid-ride, or riding an empty car) is
  **pinned to the car's frame on every peer** for presentation, like a remote
  rider's copy: the server's body is kinematic meanwhile and the server is
  still the only writer; a client's copy overrides its `NetworkTransform` for
  the frame and lets it back in a quarter second after the car stops. No new
  sync state. The deck button refuses at HQ
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
