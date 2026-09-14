# Mission brief: the world loop — HQ, the ship, the dive, and the days between

**Status: brief for planning, not a plan.** Written 14 September 2026 against
`main` at `fa8b60d` and revised the same day against `ab91de5` (after Idan's
local elevator car, PR #15) with Dan's decisions on the three scenes, the
transitions between them and the flooding cabin. Whoever plans this should
produce an implementation document in the style of
`docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md` (verified FishNet API names, file list,
verification rows with expected output) before code is written.

Owner: Dan (networking, scene flow, day state, elevator rules) with Idan (ship
prefab, scene layouts, the elevator object, the water). Epic: E7 The first dive
site / E4 The elevator. Phase: 02 Prototype. Touches the contract: **yes** (scene
membership, elevator and day state are new server-owned state). Needs two-client
test: **yes**.

`docs/DESIGN.md` section 1 is the source of the rules below; the contract's
section 9 records the networking consequences as decided-not-implemented. Idan's
cards "Decide how a dive site gets played over the network" and "Two clients
standing in DiveSite01" are answered and absorbed by this mission.

---

## 1. The goal in one paragraph

Today the whole game is one grey room. After this mission a crew walks out of HQ
across a bridge onto the docked ship, picks a site on the monitor and the ship
sails (the scene swaps to open ocean around the same ship). They climb into the
glass cabin on deck and press the button: the doors seal, the screen fades to
"Putting on suit…", and they fade back in already descending the shaft of
`DiveSite01`. The cabin floods over their heads and fifteen seconds after leaving
the deck the door opens on the seafloor. Divers ride up whenever they want:
fifteen seconds up, the water drains, they step out onto the deck. When everyone
is up the day ends; the crew dives again or changes site; after three days the
ship sails home to HQ. All grey boxes, no art, four players over FishNet, every
rule server-decided.

## 2. Decisions (fixed — Dan, 14 September 2026)

**Three scenes, one ship**
1. **HQ** (`HQPrototype`): the base and the ship docked at a pier in one Unity
   scene, a walkable bridge between them, no loading. The docked ship's cabin
   button is dead ("not at sea"): there is no HQ → dive and no dive → HQ.
2. **The ship at sea** (`ShipAtSea`, new): the *same ship prefab* alone on open
   water. Same monitor, same deck cabin.
3. **The dive** (`DiveSite01`): the shaft, the moving cabin and the seafloor.
   Entered only from the deck cabin, left only into the deck cabin.

**The monitor sails**
4. The monitor on the ship is the only sailing control; there is no wheel or
   lever. It lists every site and HQ (one site for the prototype, so two
   buttons). Choosing a destination sails at once **if every player is aboard**;
   otherwise it names who is missing.
5. HQ → sea: choose a site (scene swap to `ShipAtSea`). Sea → HQ: choose HQ
   (scene swap to `HQPrototype`). Changing site between days: choose another
   site; the ship stays in the sea scene, a short "sailing" fade is enough.
   After day 3 the monitor offers only HQ; choosing HQ before day 3 is allowed
   (early go home). The monitor is locked while a dive is in progress.

**The way down (2 → 3)**
6. Everyone walks into the deck cabin; any player presses the button. It does
   nothing unless **every living player is inside**; the cabin panel lists who
   is missing.
7. Button → doors seal (Idan's 1.5 s) → the screen fades to "Putting on suit…"
   (~3 s) → **the riders move to the dive scene during that fade** → fade in
   inside the shaft with the cabin already descending. Suit on = air starts.
   The fade is the only loading moment in the loop.
8. When the suit fade ends the cabin floods: water rises over everyone's head in
   ~4 s and the rest of the 15 s descent is spent submerged. The bottom door
   opens only when the ride is done *and* the cabin is full (with these numbers
   the ride is always the later one).
9. Players walk freely inside the cabin during the ride, as in Idan's harness
   (the cabin pushes its movement into each rider).

**The way up (3 → 2)**
10. The cabin at the bottom is always flooded. Walk in, press the button: doors
    seal, 15 s up. Over the last ~4 s the water drains, still in the dive scene.
    Once the cabin is dry, just before the top, **the riders move to the deck
    cabin** and its doors open. The deck cabin never moves and never holds
    water; every metre of travel and every drop of water lives in the dive scene.
11. Anyone can ride up any time. **Once up, you cannot go down again until the
    next day**; mid-day the deck button is dead ("cabin below"). The deck button
    only ever starts a day. The cabin returns down empty as soon as it has
    unloaded (a diver below waits 30 s, as the contract already says).
12. Unattended cargo (and bodies): loaded at the bottom and sent up with the
    lever outside the cabin (design section 1, "The elevator"); it rides the same
    way and is moved to the deck cabin at the top.

**The day**
13. **A day = one dive.** It starts when the cabin departs with everyone; it ends
    when everyone is up or dead. Three days per cycle. Ship and dive run **at
    the same time**: deck and seafloor coexist, each player sees only their own
    scene.
14. Between days the ship stays at sea; the crew may go straight back down or
    change site first. **A site reloads fresh every day**: all its loot is there
    again, anything dropped on the seafloor is gone. Dan accepted that a crew can
    farm one site three times; revisit if playtests show it.

**What travels, what stays**
15. Carried items (hands + four slots) travel with the player through every
    transition. Dropped items stay in their scene: seafloor drops are lost when
    the day ends; deck drops stay on the ship and survive sailing.

**People**
16. Dead players: body and loot drop where they died; a free spectator camera
    follows a diver of their choice (click to switch); they reappear on deck
    when the day ends. Hearing what the watched diver hears, and talking only to
    other dead players, arrive with the proximity voice card. A body carried up
    keeps its upgrades (design section 4; no upgrades exist yet).
17. Surfaced players on deck see the same chosen diver on the monitor.
18. Joining: at HQ, and on the ship at sea **between days only** (spawn on deck).
    While a dive is in progress the lobby refuses with "dive in progress".

**Feel and the water**
19. No black "Loading…" screens: the suit fade is the only load on the way down;
    the way up needs none (the swap happens behind sealed doors in a dry cabin).
    Sailing may fade through the monitor.
20. Water, first version: a flat water plane rising and falling inside the
    dive-scene cabin; when a camera is below it the screen gets the tint and fog
    of Idan's deep-water layer. No splash, no sound, no buoyancy — walking only.
    Fill and drain durations are provisional numbers in the elevator settings.

**First delivery: the whole loop in grey boxes** — HQ + docked ship + bridge, the
ship at sea, the dive site with its moving cabin, the monitor (one site + HQ),
the deck cabin with the all-aboard rule and the suit fade, down with flooding,
up with draining, cargo, day counter 1–3, sail home. Idan builds the ship prefab,
the scene layouts, the elevator object and the water; Dan builds the networking,
scene flow, day state and the elevator rules.

## 3. What exists today (verified 14 September 2026, `main` at `ab91de5`)

- `Assets/_Project/Scenes/Prototype/HQPrototype.unity`: the grey HQ room, the
  session UI, the network root, four spawn points, seven carryable items. This is
  the only scene the network ever loads; `PrototypeSessionController` hosts/joins
  in it and `PlayerSpawner` spawns players at its spawn points.
- `Assets/_Project/Scenes/Prototype/DiveSite01.unity` (Idan, PR #6/#10/#15):
  150 m seafloor, wreck, guide cable through an open shaft, deep-water lighting
  layer; `DiveSiteBuilder` / `DiveSiteValidator` generate and check it;
  `DiveSiteSettings` holds its tuning. **Since PR #15 it has a real elevator
  car**: `SunkCost.Diving.ElevatorController` (a local `MonoBehaviour`; states
  `AtTop → Sealing → Descending → AtBottom → Sealing → Ascending → AtTop`,
  `travelSecondsOneWay` 15, `doorSealSeconds` 1.5; riders registered by
  `ElevatorRiderTrigger` and pushed along by the car's per-frame delta),
  `ElevatorDoor` (sliding doors), `ElevatorControlPanel` + `ElevatorInteractor`
  (the button inside), `ElevatorState`, and an `ElevatorSelfCheck` editor check.
  It only moves `DiveSiteDevPlayer`, the local-only harness. Its header comment
  says the networked version wraps it as a `NetworkBehaviour` with SyncVar state
  and a `ServerRequestElevatorMove` ServerRpc, and that none of that is built.
  There is no water and no outside lever yet.
- Inventory and weight (PR #11, #13): `CarryableItem` state machine,
  `PlayerInventory` with server-written slots and carried mass, moving catches,
  `WeightSettings`. Items are scene objects in the HQ scene; nothing moves
  between scenes yet.
- FishNet 4.7.3 scene management is installed and unused:
  `SceneManager.LoadGlobalScenes` / `LoadConnectionScenes(NetworkConnection,
  SceneLoadData)` / `UnloadConnectionScenes`, `SceneLoadData` with
  `SceneLookupDatas`, `MovedNetworkObjects`, `ReplaceScenes` (`ReplaceOption`),
  `PreferredActiveScene`, events `OnLoadEnd` and `OnClientPresenceChangeEnd`; an
  `ObserverManager` `SceneCondition` limits observers to the scenes they are
  in. The player prefab is spawned by `PlayerSpawner` at host/join; there is no
  per-scene spawn point logic.
- The contract (`NETWORK_CONTRACT.md`) says the server decides site seed, dive
  lifecycle and elevator state, and (section 9) now records the static deck
  cabin, the scene changes at the button / just before the top, and the derived
  water level as decided-not-implemented. It lists no rule yet for scene
  membership or day state and still calls the moving-elevator handoff open.
- The ship prefab, the sea scene, the monitor and the deck cabin do not exist.

## 4. What has to be done

- **Scenes** (Idan): the HQ scene gains a pier, a bridge and the ship prefab; a
  new `ShipAtSea` scene with the same prefab on open water; `DiveSite01` gains
  the water inside the car and the outside lever. Nobody ever spawns in the dive
  scene — players arrive inside the cabin and keep their cabin-relative position
  across each swap — so it needs no spawn points beyond the harness's. All grey
  boxes, generated by editor scripts like the existing builders.
- **The ship prefab** (Idan): deck, the monitor (site list + HQ; later the TV),
  the static deck cabin (doors, button, a panel listing who is missing), the
  storage area, four spawn points on deck.
- **The elevator object** (Idan, then Dan wraps it): the outside lever at the
  bottom landing; the water plane and the underwater tint; fill/drain durations
  next to the travel time in the elevator settings.
- **Elevator networking** (Dan): wrap `ElevatorController` as its own comment
  proposes (SyncVar state + elapsed, `ServerRequestElevatorMove`); the deck
  cabin as a second, static, server-owned object with the all-aboard rule and
  the once-per-day rule; the suit fade; the scene move of riders and cargo at
  the button and just before the top; the empty return; water level derived
  from state + elapsed on every client.
- **Scene flow over the network** (Dan): the host loads/unloads scenes for
  connections; players move between scenes with hands + slots; per-scene
  observers so deck and seafloor coexist; spawn points per scene; joiners land
  in the right scene between days and are refused mid-day. Local/LAN and Steam.
- **Day state** (Dan, server-owned): current site, day number 1–3, who is on the
  seafloor, who has surfaced today (and so cannot go down), dive in progress or
  not, the monitor lock, sail-home condition, the fresh reload of the site each
  day.
- **Spectator camera** for dead players and the monitor showing a chosen diver
  (camera switching only).
- **Docs**: `NETWORK_CONTRACT.md` gains scene membership, day state and the
  elevator rows proper (server-only rows, disconnect during a dive); `README.md`
  gets the walkthrough; the test report gets the rows.

## 5. Done means

- Four players (host + three, Local; then two machines on Steam) walk from HQ
  across the bridge onto the ship, one picks the site on the monitor, all four
  arrive on the deck at sea. With one player left on the pier the monitor
  refuses and names them.
- The deck cabin refuses to leave with someone missing and says who; with all
  four inside the doors seal, every screen shows the suit fade, all four fade in
  descending inside the shaft of `DiveSite01` with their inventories, the water
  rises over their heads within ~4 s, the door opens on the seafloor 15 s after
  departure. F3 rows correct on every client.
- One player rides up alone: 15 s, the water drains before the top, they step
  out of the deck cabin dry; the others keep playing below; that player cannot
  go down again this day (the deck button says so); the cabin is back at the
  bottom 30 s after it left; cargo sent up with the lever appears in the deck
  cabin.
- When the last diver is up the day counter advances; the crew dives again
  without touching the monitor and the site is fresh; after day 3 the monitor
  offers only HQ and the ship returns to it.
- A friend joins while the ship is at sea between days and spawns on deck; a
  friend trying to join mid-day is refused with "dive in progress".
- A dead player spectates a diver; the monitor shows the same diver.
- Leave/re-host and disconnect during a dive behave under the contract rules
  written for this mission.
- Recorded in `docs/HQ_PROTOTYPE_TEST_REPORT.md` with revision, roles, RTT.

## 6. Explicitly not this card

Art, real water and splash or bubble sounds, the suit animation itself, oxygen
and air drain, damage, the quota and shop (economy cards), the site vote (one
site for now), the elevator noise event and monsters (Idan's noise/monster
cards), proximity voice, saving.

## 7. Contract and review notes

- New server-owned state: scene membership, day state, elevator state (both
  cabins). Section 3 of the contract needs rows; section 6 needs a rule for a
  disconnect while diving (body stays where it fell — the design's rule — versus
  the harness's "drop everything at the last position"); section 9 already
  carries the decided flow and needs the all-aboard, suit, once-per-day and
  water rules written as rules once implemented.
- Moving players between scenes with `MovedNetworkObjects` changes who observes
  what; the one-writer rule is unchanged, but held items must move with their
  holder and stowed items with their carrier.
- Riders walk inside the moving cabin. The contract lists the moving-platform
  handoff as open; the plan must define it (client-simulated movement plus the
  cabin's delta, remote riders through `NetworkTransform`) and the two-machine
  test must look at remote riders inside the cabin.
- Everything here crosses the network: a human reads it before merge, and the
  scenes and the elevator scripts are Idan's — coordinate before touching
  `DiveSite01.unity` or `Assets/_Project/Scripts/Diving/`.

## 8. Open questions for the planner

1. Which scenes are loaded on the host at once: HQ never together with the sea;
   ship + dive together during a day; the dive scene unloaded at day end and
   loaded fresh at the next descent (decision 14). `ReplaceOption` and
   connection-scoped loads decide this.
2. Are carried items re-parented to the player object for the move, or moved as
   separate `MovedNetworkObjects`? Cargo riding without a passenger is the same
   question for unowned items.
3. What a guest sees if its scene load takes longer than the suit fade (~3 s):
   the fade holds until the scene is ready, and the server starts the descent
   only when every rider has arrived — or the cabin starts on time and a slow
   guest fades in mid-shaft.
4. How rider movement inside the moving cabin works over the network (section
   7), and whether the cabin's transform is driven on every client from the
   synced state + elapsed (as Idan's comment intends) rather than replicated.
5. Spawn-point assignment per scene when the ship prefab is instanced twice, and
   where the four deck spawn points versus the deck cabin sit.
6. Where the day state lives (a scene-independent `NetworkObject` owned by the
   server) and how the monitor and both cabins read it.
7. How "the site reloads fresh" is done: unload and reload `DiveSite01` on the
   host each day, or reset its objects in place.
