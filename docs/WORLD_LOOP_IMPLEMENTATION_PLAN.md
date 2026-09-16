# World loop — implementation plan

**Status: plan, nothing built.** Written 14 September 2026 against `main` at
`aaa7807` (PR #16 merged: three scenes, the monitor, the flooding cabin). The
rules come from [DESIGN.md](DESIGN.md) section 1 and the brief
[WORLD_LOOP_MISSION.md](WORLD_LOOP_MISSION.md); the networking consequences are
recorded as decided-not-implemented in [NETWORK_CONTRACT.md](NETWORK_CONTRACT.md)
section 9. Every FishNet claim below was checked in the installed package
(`com.firstgeargames.fishnet` 4.7.3, `Library/PackageCache/com.firstgeargames.fishnet@382980e7eed4`);
line numbers refer to that copy. Before building, re-read newer `main` — Idan's
ship, sea and water cards land in parallel.

Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md),
[CONVENTIONS](CONVENTIONS.md), [WORKFLOW](WORKFLOW.md), the inventory code and
`Assets/_Project/Scripts/Diving/`. Human network review is still required before
any of this merges; this document claims no approval and no test result.

## 1. Outcome and scope

After the six code cards on the board are done, a crew hosts in a persistent
Session scene, stands in HQ, walks onto the docked ship, presses `Site 01` on the
monitor, and every player is on the same spot of the same ship at sea. They walk
into the deck cabin, press the button, the doors seal, their screens go black
with "Putting on suit…", and they fade back in standing inside the elevator car
at the top of the shaft in `DiveSite01`, seeing each other. When the last rider
has appeared the car descends, the water rises over their heads, and fifteen
seconds after departure the door opens on the seafloor. Anyone rides up whenever
they want: fifteen seconds, the water drains, the car stops dry at the top, and
they step out of the deck cabin on the ship. When the last diver is up the day
ends, the site is thrown away, the crew dives again or sails home. Four players
over FishNet, Local and Steam, every rule server-decided, all grey boxes.

**Planner decisions** (Dan, 14 September 2026, this document):

| Question | Decision |
|---|---|
| Scene files | A persistent `Session` scene holds the network root, session UI, preview camera and fade overlay; `HQPrototype`, `ShipAtSea`, `DiveSite01` are plain world scenes FishNet loads per connection. |
| Ship before Idan's prefab | Scene flow builds and tests against a generated stub ship and sea scene that use the part names in section 4.4; Idan's prefab replaces the stub by name. |
| Slow rider on the way down | Riders fade in inside the car at rest as each finishes loading and see each other; the car departs when every rider has arrived (10 s timeout). Air starts at departure. |
| Disconnect mid-dive | Counts as dead for the day: held/stowed items drop where they were (the contract's harness rule), the player leaves `below`, the day can end without them; a rejoin between days spawns on deck empty-handed. |
| Offline menu | No HQ room preview any more; the menu renders over the Session scene's own backdrop. |

Two nuances against the brief, both inside its intent: (1) riders fade in with
the car *at rest* and it departs when everyone is there, instead of fading in
already descending — with Local loads under a second it looks identical, and
nobody fades in alone mid-shaft; (2) the water starts `fillDelaySeconds` (3 s,
the suit time) after departure rather than after each rider's own fade, so every
client agrees on one moment.

Excluded here: art, real water and sound, oxygen, damage, the quota, the site
vote, the noise event, monsters, voice, saving, corpse dragging, host migration.
The spectator card is scoped in section 5.7 only as hooks.

## 2. FishNet facts this plan stands on

| Fact | Where verified |
|---|---|
| A connection-scoped load is `SceneManager.LoadConnectionScenes(NetworkConnection[] conns, SceneLoadData)`; with an empty connection array it loads on the server only (pre-warm). | `SceneManager.cs` 920–946 |
| `SceneLoadData.MovedNetworkObjects` are moved to the *first* scene of the load, on the server and on every receiving client. They must be spawned, networked, **not scene objects**, **root** (no parent), and not global. Anything else is skipped with a warning. | `SceneManager.cs` 961–990, 1092–1100, 1296–1350 |
| `ReplaceOption.None` loads additively and unloads nothing; `OnlineOnly`/`All` unload the connection's other scenes. This plan uses `None` everywhere plus explicit unloads, so the Session scene is never touched. | `ReplaceOption.cs`; `SceneManager.cs` 1168–1216 |
| `LoadOptions.AutomaticallyUnload = false` marks a scene manual-unload: the server keeps it when it becomes empty (disconnects included). `SceneUnloadData.Options.Mode = KeepUnused` unloads it for the named clients but keeps it on the server; `UnloadUnused` unloads it on the server once no connection remains. | `LoadOptions.cs` 14; `UnloadOptions.cs`; `SceneManager.cs` 624–655 |
| When a client finishes a load it reports back; the server then adds it to the scene, rebuilds observers, and raises `OnClientPresenceChangeStart/End` (`Scene`, `Connection`, `Added`). | `SceneManager.cs` 671–705, 1921–1947; `ClientPresenceChangeEventArgs.cs` |
| `OnLoadEnd` on a client gives `LoadedScenes`, `SkippedSceneNames`, `UnloadedSceneNames` and the queue data (scope, connections). | `LoadSceneEventArgs.cs` |
| `PreferredActiveScene` has separate `Client` and `Server` lookups; with both unset the server keeps its active scene on a connection load. Unity `RenderSettings` (fog, ambient) follow the active scene, so each client's active scene must be the world it is in. | `SceneManager.cs` 1416–1436; `PreferredActiveScenes.cs` |
| The project has **no** `ObserverManager` component and no observer conditions, so today every connection observes every object regardless of scene. Adding the shipped `SceneCondition` asset as a default condition limits observers to connections that are in the object's Unity scene; the owner always observes its own objects; a child is observed only if its parent is. | `HQPrototype.unity` (components listed in the builder); `ObserverManager.cs` 133–213; `SceneCondition.cs` 14–19; `NetworkObserver.cs` 309–383; asset `Runtime/Observing/Conditions/ScriptableObjects/SceneCondition.asset` (guid `2033f54fd2794464bae08fa5a55c8996`) |
| A NetworkObject prefab with `_isGlobal` lives in DontDestroyOnLoad, gets no observer conditions and survives every scene change. Only instantiated objects can be global. | `NetworkObject.cs` 215–251; `ObserverManager.cs` 135–170 |
| `ServerManager.Spawn(nob, owner, scene)` spawns straight into a scene. | `ServerManager.QOL.cs` 135 |
| `NetworkTransform` synchronises **local** position and rotation, can synchronise its parent (`_synchronizeParent`, off in our prefabs today), applies a received parent through `NetworkObject.SetParent`, and `Teleport()` sends a snap flag regardless of the teleport-threshold setting. With `_clientAuthoritative` the owner is the controller. | `NetworkTransform.cs` 362, 1050–1053, 1181, 1307–1325, 1396–1402, 1502, 1590–1601, 1661, 2029; prefab `PrototypePlayer.prefab` (`_clientAuthoritative: 1`, `_synchronizeParent: 0`, `_enableTeleport: 0`) |
| `NetworkObject.SetParent(NetworkBehaviour)` / `UnsetParent()` are allowed on spawned prefab instances (not on nested prefabs); the parent must carry a `NetworkBehaviour`. | `NetworkObject.cs` 703–809 |
| FishNet's `PlayerSpawner` spawns on `OnClientLoadedStartScenes` and, with no global scenes, adds the owner to the scene the player was instantiated in (the active scene). We replace it. | `Generated/Component/Spawning/PlayerSpawner.cs`; scene `_addToDefaultScene: 1` |
| Server `TimeManager.Tick` and `TicksToTime(uint)` let every client derive elapsed time from a synced start tick. | `TimeManager.cs` 132, 909 |
| A `TargetRpc`/broadcast is written immediately while SyncVars flush at tick end (contract section 6). Any scene broadcast that depends on a SyncVar state must be sent **at least one tick later**. | `NETWORK_CONTRACT.md` section 6 |
| Stopping the server clears the SceneManager's bookkeeping but unloads no Unity scene. | `SceneManager.cs` 430–447 |
| The additive-scenes demo is the reference for the pattern used here (moved player, `AutomaticallyUnload=false`, `PreferredActiveScene`, `KeepUnused`). | `Demos/SceneManager/Additive Scenes/Scripts/LevelLoader.cs` |

Project facts that matter as much: the seven loot items in `HQPrototype.unity`
are **scene objects** (prefab instances with `SceneId`), so FishNet will refuse to
move them between scenes — carried items must become runtime-spawned (section
5.6). `EditorBuildSettings` lists only `HQPrototype`; scenes load by name, so
every world scene must be in the build list. `DiveSite01`'s `ElevatorAnchor_Top`
is at the world origin with the platform ±10 m around it and the shaft 45 m
deep (`DiveSiteSettings.shaftDepthMeters`); its "Underwater Volume" is a
**global** URP volume (`DiveSiteBuilder.cs` 676–712), and its fog is
`RenderSettings` of that scene (117–128). `ElevatorController` is a plain
`MonoBehaviour` whose header comment asks for exactly the wrapper in section 6.

## 3. Scenes

### 3.1 The four scene files

| Scene | Contents | Loaded by |
|---|---|---|
| `Session.unity` (new, build index 0) | Network root (NetworkManager, TransportManager, Tugboat, Steam transport prefab ref, `PrototypeAuthenticator`, **`ObserverManager` with `SceneCondition` as its only default condition**, `CrewSpawner`), `Prototype Session UI` (+ controller, Steam bootstrap), preview camera with a plain backdrop, `ScreenFade` canvas, `WorldSceneFlow`. No world geometry, no spawn points, no NetworkObjects. | Unity, at start; never unloaded |
| `HQPrototype.unity` | The room, the four spawn points, the light, the loot fixture **spawner** (section 5.6), Idan's pier + bridge + docked ship instance. No network root, no UI, no camera. | FishNet, per connection, while the crew is at HQ |
| `ShipAtSea.unity` (new) | Water plane, fog, the ship prefab instance at `WorldLoopSettings.shipAtSeaOrigin` (default `(0, 0, 500)`), nothing else. Stub first (section 4.4), Idan's scene later. | FishNet, per connection, while at sea |
| `DiveSite01.unity` | Idan's site; the car gets a `NetworkObject` (section 6.1). | FishNet, per connection, during a day only |

World offsets: HQ and the dive site both sit at the origin and are **never
loaded together** (the cabin is dead at HQ; sailing home waits for the day to
end). The sea scene sits 500 m away on +Z so that during a day the ship (server,
deck players) and the site (server, divers) share one physics world without
overlapping; interaction raycasts keep using the default physics scene, which is
why `LocalPhysicsMode` is not used. During the sail transitions HQ and the sea
scene overlap in time but not in space.

### 3.2 What is loaded when

| Phase | Server | Client on deck / at HQ | Client below |
|---|---|---|---|
| Menu (offline) | Session | Session | — |
| At HQ | Session + HQ | Session + HQ | — |
| Sailing (transient) | Session + HQ + Sea | Session + HQ + Sea, then HQ unloaded | — |
| At sea, between days | Session + Sea | Session + Sea | — |
| Day in progress | Session + Sea + Dive | Session + Sea | Session + Dive |
| Day end | Dive unloaded everywhere (fresh next day) | | |
| Sail home (transient) | Session + Sea + HQ | Session + Sea + HQ, then Sea unloaded | — |
| Leave / re-host | `WorldSceneFlow` unloads every world scene with Unity's API; server start pre-warms HQ | same | |

Every world load uses `ReplaceScenes = None`, `Options.AutomaticallyUnload =
false`, `PreferredActiveScene = new(client: destination, server: null)`. The
host's own active scene changes only through the host-client pass of a load
that includes the host's connection, so the host renders the world its player
is in. Unloads are always explicit: `KeepUnused` when others stay in the scene,
`UnloadUnused` when the last connections leave (HQ on sailing out, Sea on
sailing home, Dive at day end).

### 3.3 Session start, join and leave

- **Server start** (`ServerManager.OnServerConnectionState` Started): spawn the
  global `CrewDayState` prefab; pre-warm HQ with `LoadConnectionScenes(sld(HQ))`
  and no connections, so the host's own client join finds it loaded.
- **Admission** (`PrototypeAuthenticator`, server side): refuse with reason
  `"dive in progress"` while `CrewDayState.Phase == DiveInProgress`; the client
  shows the reason in the session UI like the existing refusals.
- **Join** (`CrewSpawner`, replaces FishNet's `PlayerSpawner`): on
  `OnClientLoadedStartScenes(conn, asServer: true)` load the current world for
  that connection; on `OnClientPresenceChangeEnd` with `Added` for that
  connection and world, instantiate the player prefab at the next spawn point
  of that world (HQ points, or the ship's `SpawnPoint_1..4` at sea), move it
  into the world scene, `ServerManager.Spawn(nob, conn, worldScene)`, add the
  roster entry (client id, display name from the lobby or `Player <id>`).
- **Leave / host stop**: existing `LeaveFlow`; then `WorldSceneFlow` unloads every
  loaded scene except Session with `UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync`,
  and the menu is back on the Session backdrop. The global day-state object is
  despawned with the server.

## 4. Objects and ownership

### 4.1 `CrewDayState` (global NetworkObject, server-written)

| Field | Type | Meaning |
|---|---|---|
| `Phase` | `SyncVar<DayPhase>` | `AtHQ`, `Sailing`, `AtSea`, `DiveInProgress`, `SailingHome` |
| `SiteIndex` | `SyncVar<int>` | 0 = Site 01 (the only site) |
| `Day` | `SyncVar<int>` | 0 at HQ; 1–3 at sea |
| `Below` | `SyncList<int>` | client ids on the seafloor (alive) |
| `SurfacedToday` | `SyncList<int>` | client ids that rode up this day; cannot go down |
| `Dead` | `SyncList<int>` | client ids dead this day (spectating) |
| `Roster` | `SyncList<CrewMember>` | `(clientId, name)` for panels and refusals |
| `LastRefusal` | `SyncVar<Refusal>` | `(tick, kind, names)` shown for `refusalDisplaySeconds` by the monitor or cabin panel |

Server methods (all `void Server…`, called only from server code):
`ServerSail(destination)`, `ServerBeginDay(riders)`, `ServerRiderSurfaced(id)`,
`ServerPlayerDied(id)`, `ServerRemove(conn)`, `ServerEndDayIfDone()`. Rules:
a day starts when the car departs with everyone; `Day` increments then; the day
ends when `Below ∩ living == ∅`; after `Day == 3` the deck cabin refuses with
`"cycle over — sail home"` and the monitor offers only HQ; `ServerSail(HQ)` is
allowed whenever `Phase == AtSea`.

### 4.2 `WorldSceneFlow` (Session scene, server logic + client hooks)

Owns the transition protocols of section 5: builds `SceneLoadData`s, waits the
sync-flush ticks, listens to `OnClientPresenceChangeEnd` for arrival gates, and
on clients listens to `OnLoadEnd` to place the local player and drive the fade.
It never decides gameplay; it executes what `CrewDayState`, `ShipMonitor`,
`DeckCabin` and `NetworkElevator` ask for.

### 4.3 `ScreenFade` (Session scene, local)

`FadeOut(seconds, text)`, `HoldBlack(text)`, `FadeIn(seconds)`. Driven only by
SyncVar `OnChange` plus the local `OnLoadEnd` — never by an RPC alone (contract
ordering rule).

### 4.4 The ship prefab — part names the code binds to

`Ship` root with: `AboardVolume` (box trigger covering deck and interior),
`Monitor` with `MonitorButton_Site01` and `MonitorButton_HQ` (colliders the
existing E-interaction hits; `ShipMonitor` NetworkBehaviour on `Monitor`),
`DeckCabin` root (`DeckCabin` NetworkBehaviour) with `DeckCabinVolume` (box
trigger), `DeckCabinDoorL`, `DeckCabinDoorR`, `DeckCabinButton`,
`DeckCabinPanel` (a TextMesh the panel writes to), `StorageArea`,
`SpawnPoint_1..4`, `BoardingPoint`. The docked and the at-sea instances are the
same prefab; positions are always taken relative to the instance's transforms,
never hard-coded. The stub builder (`ShipStubBuilder`, editor) generates a
prefab with exactly these names and a `ShipAtSea` scene around it; Idan's
`ShipBuilder` replaces the prefab asset at the same path and the stub builder is
deleted in that PR.

### 4.5 `ShipMonitor` (on both ship instances, server-written)

`ServerRequestSail(int destination)` from the presser. Server validates: phase
allows it (`AtHQ` → any site; `AtSea` → any site or HQ; refused during a dive);
after day 3 only HQ; every connected player is inside `AboardVolume` (bounds
test on server-side positions — robust to teleports, no trigger events); on
failure `LastRefusal = (Sail, missing names)`; on success `CrewDayState.ServerSail`.
Choosing the current site at sea between days is accepted and changes nothing.

### 4.6 `DeckCabin` (on both ship instances, server-written)

| Field | Meaning |
|---|---|
| `Phase` | `SyncVar<CabinPhase>`: `(state, startTick)` with state `Idle`, `Sealing`, `Suiting`, `Away`, `Opening` |
| `Riders` | `SyncList<int>` client ids that departed in it / are arriving into it |

`ServerRequestDepart()` from a player inside `DeckCabinVolume`: refused at HQ
(`"not at sea"`), during a dive (`"cabin below"`), after day 3, or when any
living connected player is outside the volume (`LastRefusal = (Cabin, names)`,
the panel shows the names for `refusalDisplaySeconds`). On success the sequence
in section 5.3 runs. Doors: `Idle`/`Opening` open, `Sealing` closes over
`doorSealSeconds`, `Suiting`/`Away` closed. The docked instance never gets past
the refusal.

### 4.7 `NetworkElevator` (on the car in `DiveSite01`, server-written) — section 6

### 4.8 Cargo

An item inside `CarVolume` when the car seals is **mounted**: server sets
`CarryableItem.State = Riding`, kinematic, colliders off, `NetworkObject.SetParent(car)`
(server is the controller of an unowned item, so its `NetworkTransform` carries
the parent). On arrival the server unmounts it in place (unparent, `Free`,
physics on). For the ride up it is moved with the riders (section 5.4). This is
the "Elevator: cargo rides with the car" card; the elevator card only needs the
`Riding` state to exist so items are never simulated inside a moving car.

**As built (16 September 2026, `dan/fixes`):** no `Riding` state and no
parenting — the deck cargo's freeze was reused. `WorldSceneFlow` freezes every
loose item on the cabin's floor when the placements are captured
(`CarryableItem.ServerBeginCabinTransit`, the spot kept in the `CabinFrame`),
follows it every frame through the ride (the car moves; the deck cabin does
not), moves it with the riders and places it at the same spot in the other
cabin (`ServerPlaceAfterCabinTransit`, one teleport); `ResetTrip` releases it.
Loose items that are inside the car while it moves and are not frozen (dropped
mid-ride, an empty car's floor) are pinned to the car's frame on every peer in
`CarryableItem.LateUpdate` (`PinToMovingCar`) — the same idea as the remote
rider's pin; the server's body is kinematic while pinned. Rows C1–C4 and D in
the deck cabin ride matrix.

**Mid-ride (16 September 2026, `dan/car-bugs`, Dan's three bugs):** cabin
cargo keeps its colliders (`transitInCabin`), so the dot finds it and the
visor brackets it; `ServerGrab` on cabin cargo calls
`WorldSceneFlow.ServerReleaseCabinCargo` (out of the list, transit over) so
the follow never fights the hand; `DriveCar` moves every body this peer
simulates inside the car by the car's delta (`CarryLooseBodies` →
`CarryWithCar`, in the rider's order against the floor, interpolation held
off while carried) so a thrown ball lands on the car's floor instead of
falling through it into the tube; a client pins only from a resting spot it
recorded while the car was still (a thrown ball's copy is left to its
NetworkTransform until the stop, and ends where the server has it). Rows E1–E3
on host and guest.

Found while building it: `CarryableItem` reset any Free item below y = −2
("fell overboard") to its reset spot every physics step — on the seafloor,
45 m down, that was every loose item, every step: a coin dropped in the car
snapped back to its spawn spot (Dan: "dropping items on the bottom of the
elevator makes them disappear"), and nothing loose on the site could ever be
moved. The void line is now per world: −2 m at HQ and aboard, 20 m under the
car's landing in the dive (`CarryableItem.VoidY`).

### 4.9 Contract rows (written in the same PRs)

- Section 3: **Scene membership** — server only; a client loads and unloads
  exactly what it is told; observers follow scene membership. **Day state** —
  server only. **Cargo mount** — server only.
- Section 6: a disconnect while below counts as dead for the day; joining is
  refused while a dive is in progress; a rejoin between days spawns on deck.
- Section 9: the section-9 "decided" bullet becomes rules: static deck cabin,
  scene change at the button / after the dry stop, water derived from state +
  elapsed, riders and held items parented to the car during motion through
  `NetworkTransform` parent sync, the unparent gate before any scene move.
- New section **Scene flow**: only spawned root objects travel (scene objects
  never do); every scene broadcast that depends on a SyncVar waits
  `syncFlushTicks`; the active scene is the client's world.

## 5. Transitions — exact protocols

Conventions: "wait N ticks" = `syncFlushTicks` (2) so the SyncVar the clients
react to is flushed before the scene broadcast (contract section 6). "Arrival
gate" = every listed connection has raised `OnClientPresenceChangeEnd(Added)` for
the scene, or `arrivalTimeoutSeconds` (10) passed — the gate then proceeds and
logs who was late. Positions are always **captured before** a scene is unloaded
and **applied on** the client's own `OnLoadEnd`, so no RPC ordering is involved.

### 5.1 Sail out (HQ → sea)

1. `ShipMonitor.ServerRequestSail(Site01)`; validation (4.5).
2. `CrewDayState.Phase = Sailing` (+ `SiteIndex`). Clients: `ScreenFade.FadeOut(sailingFadeSeconds, "Sailing…")`; the owner client stores `shipLocal = hqShip.InverseTransformPoint(player.position)` and its yaw relative to the ship.
3. Server waits 2 ticks, then `LoadConnectionScenes(allConns, sld(ShipAtSea))` with `MovedNetworkObjects = all players ∪ their Held/Stowed items ∪ Free items whose position is inside the HQ ship's `AboardVolume``. Right after the call the server sets each moved unowned item to `seaShip.TransformPoint(hqShip.InverseTransformPoint(p))` and calls `Teleport()` on its NetworkTransform.
4. Each client, on `OnLoadEnd` containing `ShipAtSea`: teleport the local player to `seaShip.TransformPoint(shipLocal)` (CharacterController disabled for the assignment), `nt.Teleport()`, `ScreenFade.FadeIn`.
5. Arrival gate for all connections, then `UnloadConnectionScenes(allConns, sud(HQ) { Mode = UnloadUnused })` — clients drop HQ, the server unloads it once empty.
6. `Phase = AtSea`, `Day = 0` stays until the first descent.

### 5.2 Sail home (sea → HQ)

Same as 5.1 with the scenes swapped; allowed only in `Phase == AtSea`; moved
items are those inside the sea ship's `AboardVolume`; after the arrival gate
`UnloadConnectionScenes(allConns, sud(ShipAtSea) { UnloadUnused })`;
`Phase = AtHQ`, `Day = 0`, `SurfacedToday`/`Dead` cleared. HQ loads fresh, so the
loot fixture respawns (a harness effect, accepted).

### 5.3 Down (deck cabin → car)

1. `DeckCabin.ServerRequestDepart()`; validation (4.6). `Riders = all living connected client ids`.
2. `DeckCabin.Phase = Sealing(tick)`; doors close over `doorSealSeconds`.
3. At `doorSealSeconds`: `Phase = Suiting(tick)`. Rider clients: `HoldBlack("Putting on suit…")` after a `suitFadeSeconds` fade; capture `cabinLocal = deckCabin.InverseTransformPoint(player.position)` and relative yaw.
4. Server waits 2 ticks; `CrewDayState.ServerBeginDay(riders)` sets `Phase = DiveInProgress`, `Day += 1`, `Below = riders`, clears `SurfacedToday`/`Dead`; then `LoadConnectionScenes(riderConns, sld(DiveSite01))` with `MovedNetworkObjects = riders ∪ their Held/Stowed items` (the first load of the day also loads the scene on the server), then `UnloadConnectionScenes(riderConns, sud(ShipAtSea) { KeepUnused })`.
5. Each rider client, on `OnLoadEnd` containing `DiveSite01`: find the `NetworkElevator` car; teleport the local player to `car.TransformPoint(cabinLocal)`; `NetworkObject.SetParent(car)` for the player **and** for its Held item; `nt.Teleport()`; `FadeIn`. The car is `AtTop`, doors closed; riders see each other as they arrive.
6. Arrival gate for the riders, then `NetworkElevator.ServerDepart()`: `Phase = Descending(tick)`; the `AirStarted` event fires (oxygen hook). Water rises from `fillDelaySeconds` over `fillSeconds`; the car reaches the bottom at `travelSecondsOneWay`.
7. `AtBottom(tick)`: doors open over `doorSealSeconds`. On this state change each rider's owner client `UnsetParent()`s its player and Held item (the car is stationary, nothing drifts). Riders walk out.

### 5.4 Up (car → deck cabin)

1. `NetworkElevator.ServerRequestMove()` from a player inside `CarVolume` while `AtBottom`. `Riders = living players inside CarVolume` (server bounds test); cargo = Free items inside it → mounted (4.8).
2. `Phase = Sealing(tick, upward)`: rider owner clients `SetParent(car)` (player + Held item) on the state change; doors close.
3. `Ascending(tick)` for `travelSecondsOneWay`; water full until `travel − drainSeconds`, then drains to 0 at arrival.
4. `AtTop(tick)` (car stopped, dry): each rider's owner client captures `carLocal = car.InverseTransformPoint(player.position)` and yaw, then `UnsetParent()` (player + Held item); the server unmounts cargo but keeps it kinematic.
5. **Unparent gate**: the server waits until every rider's server copy has `RuntimeParentNetworkBehaviour == null` (their NetworkTransform delivered the unparent) and at least 2 ticks passed; after `unparentTimeoutSeconds` (1) it calls `UnsetParent()` on its copies itself and logs it.
6. `CrewDayState.ServerRiderSurfaced(id)` for each rider (`Below −= riders`, `SurfacedToday += riders`); `DeckCabin.Riders = riders`; `LoadConnectionScenes(riderConns, sld(ShipAtSea))` with `MovedNetworkObjects = riders ∪ Held/Stowed ∪ cargo`; `UnloadConnectionScenes(riderConns, sud(DiveSite01) { Mode = KeepUnused })` — the site stays for the others and for spectators. Cargo is placed by the server at `deckCabin.TransformPoint(carLocalOfCargo)`, `Teleport()`, then released (`Free`, physics on).
7. Rider clients on `OnLoadEnd(ShipAtSea)`: teleport the local player to `deckCabin.TransformPoint(carLocal)`, `nt.Teleport()`. No fade (the doors were closed the whole time).
8. Arrival gate, then `DeckCabin.Phase = Opening(tick)`: doors open; the panel says `"cabin below"` to anyone who tries the button now.
9. `NetworkElevator`: `Descending(tick)` empty right after step 6 (no seal at the top — the doors never opened there), `AtBottom` 15 s later, doors open. A diver below waits seal 1.5 s + 15 s + 15 s ≈ 31.5 s from the button; the contract's "30 s" counts the two rides only.
10. `CrewDayState.ServerEndDayIfDone()` → section 5.5.

### 5.5 Day end

When `Below` holds no living player: for every connection still in `DiveSite01`
(dead spectators) move their player object to the deck — teleport to a
`SpawnPoint_n`, revive (input on, renderer on) — with a `LoadConnectionScenes(conns, sld(ShipAtSea))`
carrying them in `MovedNetworkObjects`; then `UnloadConnectionScenes(everyoneWhoHadIt, sud(DiveSite01) { UnloadUnused })`
so the server drops the site. `Phase = AtSea`, `SurfacedToday`, `Dead` cleared.
Items left on the seafloor go with the scene. If `Day == 3` the monitor offers
only HQ and the cabin refuses.

### 5.6 What travels and what cannot

FishNet refuses to move scene objects, so every item that may be carried across a
transition is a **runtime-spawned prefab instance**. The HQ fixture changes from
seven saved prefab instances to a `LootFixtureSpawner` scene component holding
the manifest (`HQPrototypeLootSetup.Manifest`: prefab, position, name); on
`OnStartServer` it spawns them (named as today so the test hooks keep working);
nothing survives a server stop, which replaces `ServerReset` on re-host. Idan's
loot spawn points in the site do the same with `CarryableItem` prefabs. Deck
drops are moved by ship-relative offset on every sail (5.1 step 3). `Held` items
are owned by the holder, `Stowed` items by the server; both are listed in
`MovedNetworkObjects` next to their carrier; `Released` items are left where
they fly. The `PlayerInventory` slots hold object ids, which do not change on a
scene move.

### 5.7 Dead players (hooks only; the spectator card fills them)

`CrewDayState.ServerPlayerDied(id)`: `PlayerInventory.ServerDropEverything()`,
the player object stays where it is with input and renderer off (the body), the
connection stays in `DiveSite01`, `Dead += id`, `Below −= id`, `ServerEndDayIfDone()`.
A debug key (`F9`, Local only) kills the local player until oxygen exists. The
spectator camera and the shared TV selection are the later card.

### 5.8 Disconnects and the host

`PlayerInventory.ServerOnRemoteConnectionState` already drops everything at the
last position; `CrewDayState.ServerRemove(conn)` then removes the id from every
list and runs `ServerEndDayIfDone()`. `WorldSceneFlow` ignores a departed
connection in every gate. The host leaving ends the session (contract).

## 6. The elevator over the network

### 6.1 Wrapping `ElevatorController`

Idan's controller keeps its state machine, rider push, door and self-check. The
wrapper adds a **driven mode**: `NetworkElevator : NetworkBehaviour` on the same
GameObject (which gains a `NetworkObject`, a scene object — the builder must
create its `SceneId` the way `HQPrototypeLootSetup.RebuildMissingSceneIds`
does) owns

| Field | Meaning |
|---|---|
| `Phase` | `SyncVar<ElevatorPhase>`: `(ElevatorState State, bool Upward, uint StartTick)` — one struct so state and time never arrive apart |
| `Riders` | `SyncList<int>` client ids inside for this ride |
| `Cargo` | `SyncList<int>` object ids mounted for this ride |
| `Settings` | reference to Idan's `ElevatorSettings` asset |

Every peer computes `elapsed = TimeManager.TicksToTime(TimeManager.Tick − StartTick)`
and feeds `controller.SetDrivenPhase(State, elapsed)` each frame; the controller
in driven mode stops advancing state on its own and only applies transform,
rider push and events, so `ElevatorDoor` and the water read it unchanged. State
transitions happen on the server only (`ServerDepart`, `ServerRequestMove`,
timers on elapsed). `TryStartMove(GameObject)` routes to `ServerRequestMove` when
driven. Late-loading clients reconstruct the ride from the struct alone.

> **Superseded 15 September 2026 by docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md:**
> the water is geometry, not timers. `ElevatorMath` as built gives the depth
> profile (`DepthAt`, `ProgressAt`, `TravelSeconds` from depth and speeds) and
> `WaterLevelInCar(seaLevelY, carFloorY, h)`; `fillDelay`/`fill`/`drain` and
> `travelSecondsOneWay` do not exist. The table below is kept as the record of
> what was planned.

Pure functions in `ElevatorMath` (no Unity dependencies, checked by the pure
tests): `Progress(state, upward, t, travel)`, `WaterLevel(state, upward, t,
settings)`, `DoorOpenFraction(state, t, seal)`, `IsDry(...)`:

```
WaterLevel: AtTop/Sealing-down: 0
            Descending: clamp01((t − fillDelay) / fill)
            AtBottom, Sealing-up: 1
            Ascending: t < travel − drain ? 1 : clamp01((travel − t) / drain)
Bottom door opens when state == AtBottom, which the server enters at t ≥ travel
and WaterLevel == 1 (with the defaults, 7 s < 15 s, so always the ride).
```

### 6.2 Riding

Riders and their Held items are parented to the car by their **owner** client
on the `Sealing`/arrival state changes (5.3 step 5, 5.4 step 2) and unparented
on `AtBottom`/`AtTop`; the player and item prefabs get `_synchronizeParent`
on, so every peer applies the same parent with the same local position — the
car moves at 3 m/s and an unparented remote rider would trail it by RTT × 3 m/s
and float through the floor. The owner's CharacterController keeps moving in
world space; the parent transform carries it. Idan's per-frame push stays for
`DiveSiteDevPlayer` only.

Fallback if the two-machine test shows a visible pop at the parent switch: lock
riders in place for the ride (input off, camera free) — a settings flag
(`lockRidersDuringRide`), not a redesign.

### 6.3 What the wrapper needs from `DiveSite01` (Idan)

- `NetworkObject` on the car root with a valid `SceneId`; `CarVolume` box trigger
  named exactly that; `ElevatorLever` at the bottom landing (his lever card).
- `ElevatorController.SetDrivenPhase(...)` and a `Driven` flag; `ElevatorRiderTrigger`
  no longer the only rider source (the wrapper's list is).
- The "Underwater Volume" made **local** (a box covering seafloor and shaft) or
  layer-masked, so the host on deck does not get the underwater grade while the
  site is loaded on its machine.
- `DiveSiteDevPlayer` (and its camera/listener) disables itself when a
  `NetworkManager` is running, until the retire card deletes it.
- `DiveSiteValidator` allowing NetworkObjects (his card).
- `ElevatorSettings` asset with `travelSecondsOneWay 15`, `doorSealSeconds 1.5`,
  `fillDelaySeconds 3`, `fillSeconds 4`, `drainSeconds 4`, `suitFadeSeconds 3`.

## 7. Settings

`Assets/_Project/Settings/Prototype/WorldLoopSettings.asset` (Dan):
`sailingFadeSeconds 1.5`, `arrivalTimeoutSeconds 10`, `unparentTimeoutSeconds 1`,
`syncFlushTicks 2`, `refusalDisplaySeconds 3`, `shipAtSeaOrigin (0, 0, 500)`,
`daysPerCycle 3`, `lockRidersDuringRide false`. All provisional; changing them is
not a design change.

## 8. File map

Relative to `Assets/_Project/` unless stated.

| File | Work | Card |
|---|---|---|
| Scenes/Prototype/Session.unity (new), Editor/Prototype/SessionSceneBuilder.cs (new) | Network root, UI, preview backdrop, fade, flow; ObserverManager + SceneCondition; CrewSpawner | Scene flow |
| Editor/Prototype/HQPrototypeBuilder.cs, HQPrototypeValidator.cs, HQPrototypeLobbySetup.cs, HQPrototypeInventorySetup.cs, HQPrototypeLootSetup.cs, HQPrototypeBuild.cs | HQ becomes a world scene: room, spawn points, light, `LootFixtureSpawner`; validators check "no network root here"; build list = Session, HQ, ShipAtSea, DiveSite01; setup menus open Session/HQ as needed | Scene flow |
| Scripts/World/CrewSpawner.cs (new), WorldSceneFlow.cs (new), ScreenFade.cs (new), WorldLoopSettings.cs (new), WorldId.cs, WorldScenes.cs, ShipParts.cs, Settings/Prototype/WorldLoopSettings.asset | Section 3.3, 4.2, 4.3, 7 (built: the server-start pre-warm, the day-state spawn and the leave cleanup live in `WorldSceneFlow`, not the session controller) | Scene flow |
| Scripts/Net/PrototypeAuthenticator.cs, PrototypeAdmissionMessages.cs | `"dive in progress"` refusal reason | Scene flow |
| Scripts/Interaction/LootFixtureSpawner.cs (new), CarryableItem.cs, PlayerInventory.cs | Runtime fixture; `Riding` state; helpers listing a player's carried NetworkObjects | Scene flow / cargo |
| Prefabs/Player/PrototypePlayer.prefab, Prefabs/Interaction/*.prefab | `_synchronizeParent` on | Elevator |
| Prefabs/World/Ship.prefab (stub then Idan's), Scenes/Prototype/ShipAtSea.unity, Editor/Prototype/ShipStubBuilder.cs (temporary) | Section 4.4 | Scene flow (stub) / Idan |
| Scripts/World/CrewDayState.cs (new), DayPhase.cs, CrewMember.cs, Refusal.cs, Prefabs/World/CrewDayState.prefab (global) | Section 4.1 — **built 16 September 2026 (`dan/day-state`)** as `Day` + `Payday` on `CrewDayState`, `ServerBeginDay` when the riders stand below, `ServerEndDayIfDone(daysPerCycle)` on the last one up or a disconnect below; the deck cabin's all-aboard / dive-in-progress / payday refusals in `WorldSceneFlow.ServerRequestDive`; the monitor's payday lock in `ServerCanSail`. No `SurfacedToday` (the dive-in-progress lock covers once-per-day), no `Dead`/`Roster` (no death system yet; refusals name `Player N`) | Day state |
| Scripts/World/ShipMonitor.cs (new) | Section 4.5, sailing fade | Monitor |
| Scripts/World/DeckCabin.cs (new), CabinPhase.cs | Section 4.6, 5.3, 5.4 steps 6–8 | Deck cabin |
| Scripts/Diving/NetworkElevator.cs (new), ElevatorMath.cs (new), ElevatorPhase.cs (new); ElevatorController.cs (driven mode, with Idan) | Section 6 | Elevator |
| Scripts/Net/InventoryVerificationPeer.cs | New actions `sail`, `cabin`, `car`, `lever`, `die`, `snapshot` fields `scene`, `phase`, `day`, `elevator`, `water` | every card |
| Editor/Prototype/WorldLoopChecks.cs (new, pure), WorldLoopRuntimeChecks.cs (new, Local matrix), HQPrototypeTestHooks.cs | Section 9 | every card |
| docs/NETWORK_CONTRACT.md, README.md, docs/HQ_PROTOTYPE_TEST_REPORT.md, docs/DESIGN.md (only if a rule changes) | Section 4.9, walkthrough, evidence | every card |

Include `.meta` files; preserve GUIDs; no package edits; `DiveSite01.unity` and
`Scripts/Diving/` change only with Idan and in his or a jointly reviewed PR.

## 9. Verification

### 9.1 Pure / asset (editor checks, no Play Mode)

- `ElevatorMath`: progress 0→1 over 15 s; water 0 until 3 s, 1 at 7 s, stays 1 to the bottom; on the way up 1 until 11 s, 0 at 15 s; dry exactly at arrival; door fraction 0 while moving; no NaN for negative or huge `t`.
- `WorldLoopSettings` / `ElevatorSettings` validation: positive durations, `fillDelay + fill ≤ travel`, `drain ≤ travel`.
- Session scene: exactly one NetworkManager, ObserverManager with the SceneCondition asset, no NetworkObjects, CrewSpawner present, FishNet `PlayerSpawner` absent. HQ / ShipAtSea / DiveSite01: no NetworkManager, no session UI, no camera with an AudioListener; ship parts present by name; spawn points 4; `CarVolume`, `DeckCabinVolume`, `AboardVolume` are triggers.
- Build list order and presence of the four scenes.
- Player and item prefabs: `_synchronizeParent` on, `_clientAuthoritative` unchanged.
- Loot fixture manifest still names the seven items; `LootFixtureSpawner` spawns each once (idempotent on re-host).

### 9.2 Local matrix (host + headless peers through the verification peer; MCP for editor state)

Record build revision, roles, client and object ids, scene of every player and
item on **both** peers (`snapshot.scene`), phases, timings. Bounded polling (5 s
normal), never immediate reads after a request. A missing reply is a failure.

| ID | Scenario | Acceptance |
|---|---|---|
| S1 | Host, then peer joins at HQ | Both in `HQPrototype`; players at HQ spawn points; F3 shows scene; Session objects not duplicated |
| S2 | Debug `sail` with one peer off the ship | Refused; refusal names the peer on both peers' snapshots |
| S3 | All aboard, `sail` | Fade on both; both players and carried items in `ShipAtSea` at the same ship-relative spots (±0.05 m); HQ not loaded on either; a ball left on the docked deck is on the sea deck |
| S4 | Peer joins at sea between days | Spawns on `SpawnPoint_n` of the sea ship; sees the host |
| S5 | Peer tries to join during a day | Refused with `"dive in progress"`; host unaffected |
| S6 | `cabin` with one rider outside | Refused; panel text lists the name for 3 s on both |
| S7 | All in cabin, `cabin` | Sealing 1.5 s → black + text on both → both in `DiveSite01`, `ShipAtSea` unloaded on the peer, still loaded on host; both parented to the car, inventories intact, slots identical on both peers |
| S8 | Departure gate | Car departs only after both presence events (or 10 s); `Day` = 1; `Below` = both; water 1.0 at 7 s ±1 tick on both; door opens at 15 s; both unparented within 2 ticks |
| S9 | Peer rides up alone | Sealing → 15 s → dry stop → peer in `ShipAtSea` at the deck-cabin-relative spot; host still below and still sees the car; peer `SurfacedToday`; `cabin` from the peer refused with `"cabin below"`; car back `AtBottom` ≈31.5 s after the button (seal + up + down), nothing left parented under the car |
| S10 | Remote rider during the ride (host watches the peer, peer watches the host, each direction) | Remote rider stays inside the cabin the whole ride, no floor clipping beyond 5 cm, at most one visible pop at the parent switch; else the lock fallback is flagged |
| S11 | Cargo: ball left in the car, `lever` from outside | Ball mounted, rides, appears in the deck cabin, becomes Free; both peers agree on its scene and state |
| S12 | Last diver up | Day end: `DiveSite01` unloaded on both and on the server; `Phase = AtSea`; `cabin` again starts day 2 with a fresh site (the ball dropped on the seafloor on day 1 is gone) |
| S13 | Day 3 ends | Monitor: `Site01` refused, `HQ` sails; both in HQ with carried items and the deck ball; `Day = 0` |
| S14 | Disconnect mid-dive (kill the peer process) | Peer's items drop at its last position under server simulation; `Below` shrinks; day ends when the host rides up; the peer rejoins between days on deck |
| S15 | Host Leave at sea / mid-day, re-host | Every world scene unloaded, menu shown; re-host loads HQ fresh; fixture respawned; no leaked global object |
| S16 | Debug `die` below | Items drop, body stays, `Dead` lists the id; at day end the body is back on deck alive |

### 9.3 Steam (two machines, both roles)

S3, S7, S8, S9, S10, S11, S12, S14 with RTT recorded; S10 is the row that
decides the parent-sync approach. Record revision, roles, RTT in
`docs/HQ_PROTOTYPE_TEST_REPORT.md`. The inventory and weight Steam rows still
owed from PR #11/#13 run in the same session.

### 9.4 Feel gate

A person walks the whole loop with a friend: the sail fade is short enough, the
suit black is not annoying when a friend is slow, the water reaching the visor
reads, the 30 s wait below is felt, nothing pops. Tune settings only.

## 10. Build sequence and delivery

One PR per card, each compiled in Unity 6000.6.0f1, each with its S-rows run
Local, each read by Idan or Dor before merge (they all cross the network):

1. **Scene flow** — Session scene split, ObserverManager + SceneCondition,
   CrewSpawner, WorldSceneFlow with sail out/home only (a debug `sail` command
   stands in for the monitor), ScreenFade, runtime loot fixture, ship stub +
   ShipAtSea stub, admission refusal, build list. Rows S1–S5, S13 (with the
   debug command), S15. Contract: section 3 rows, section 6 lines, the new
   scene-flow section.
2. **Day state** — `CrewDayState`, roster, phases, day counter, the debug `die`
   key; rows S12, S14, S16 as far as they do not need the cabin.
3. **The monitor** — `ShipMonitor` replaces the debug `sail`; refusal panel;
   rows S2, S3, S13 proper.
4. **Elevator over the network** — with Idan: driven mode, `NetworkElevator`,
   `ElevatorMath`, `Riding` state, parent sync on prefabs; tested first in a
   Local session that loads `DiveSite01` directly through a debug command; rows
   S8 (car part), S9 (car part), S10.
   *Built 15 September 2026 on `dan/deck-cabin`, together with 5 and ahead of
   2 (Dan: "start without day state"): the car's phase lives on `CrewDayState`
   (`ElevatorPhase`) instead of a scene `NetworkObject`, `ElevatorController`
   gained the driven mode, riders are locked and placed from `RiderPlacement`
   instead of parented (the section 6.2 fallback, chosen up front), water and
   `ElevatorMath` not yet.*
5. **The deck cabin** — `DeckCabin`, 5.3 and 5.4 end to end; rows S6–S9, S12.
   *Built with 4 (`WorldSceneFlow.Cabin`, `CabinRideState`): the ride both ways
   without the day rules — no all-aboard, no once-per-day, no day counter; the
   deck cabin stays a plain part of the ship prefab, its doors and panel driven
   from the ride state on every peer. Rows R0–R5 and G0–G3 of
   `DeckCabinRideRuntimeChecks` stand in for S6–S9 until the day state lands.*
6. **Cargo** — mount/unmount, lever; row S11.
7. **Spectator** — separate card; hooks from 5.7 already in place.
8. **Steam walk-through** — section 9.3; README walkthrough; report rows;
   Notion closes.

Documentation edits per PR: the contract rows of section 4.9 with the code
that makes them true; README controls (`E` on monitor buttons, cabin and car
buttons, the lever, `F9` debug kill in Local); the report's rows with revision,
roles and RTT.

## 11. Asks of Idan, in order of need

1. Before card 4: the `DiveSite01` items in section 6.3 (car `NetworkObject` +
   `SceneId`, `CarVolume`, driven mode on `ElevatorController`, local underwater
   volume, dev player self-disable, validator).
2. Any time: the `ElevatorSettings` asset with the six numbers of section 6.3,
   and the water plane driven by `ElevatorMath.WaterLevel` (his water card).
3. Any time: the ship prefab with the part names of section 4.4, the pier and
   bridge in the HQ world scene (no network root there any more), and the
   `ShipAtSea` scene with the ship at `shipAtSeaOrigin` — replacing the stub by
   asset path.
4. Before the Steam walk-through: the outside lever.

## 12. Risks the rows are there to catch

- **Parent switch pop** (S10): mitigated by NetworkTransform parent sync; the
  lock flag is the fallback.
- **Unparent gate timing** (S9): a rider whose client never reports the unparent
  is forced after 1 s and logged; the row checks nothing is left under the car.
- **Scene broadcast before SyncVar** (S7, S9): the 2-tick wait; if a client ever
  fades or teleports before its state changes, the wait is too short.
- **A scene unloaded under someone** (S12, S14, S15): every unload is explicit;
  `AutomaticallyUnload` is false everywhere; the day-end unload lists every
  connection that had the site.
- **Host rendering** (S8, S9 on the host): host visibility updates hide dive
  objects on the deck host; the local underwater volume keeps the deck clear.
- **Scene objects in `MovedNetworkObjects`**: the fixture change removes the only
  case; the runtime check logs any refusal from FishNet as a failure.

## 13. Final acceptance

This plan fixes the scene structure, the object model, the six transition
protocols, the riding approach and the verification rows so that the six cards
can be built without new design decisions. Nothing here is implemented. Call a
card implemented when its code compiles, its S-rows pass Local with evidence in
the report, and its contract text is in; certify Steam only with the two-machine
session of section 9.3. Human review and Notion closure remain separate gates.
