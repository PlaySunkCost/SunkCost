# Mission brief: the world loop — HQ, the ship, the dive, and the days between

**Status: brief for planning, not a plan.** Written 14 September 2026 against
`main` at `fa8b60d` (`github.com/PlaySunkCost/SunkCost`). Whoever plans this
should produce an implementation document in the style of
`docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md` (verified FishNet API names, file list,
verification rows with expected output) before code is written.

Owner: Dan (networking, scene flow) with Idan (scenes, ship and site layouts,
elevator). Epic: E7 The first dive site / E4 The elevator. Phase: 02 Prototype.
Branch: `dan/world-loop`. Touches the contract: **yes** (scene membership,
elevator and day state are new server-owned state). Needs two-client test: **yes**.

`docs/DESIGN.md` section 1 was rewritten in the same PR as this brief; it is the
source of the rules below. Idan's cards "Decide how a dive site gets played over
the network" and "Two clients standing in DiveSite01" are answered and absorbed by
this mission.

---

## 1. The goal in one paragraph

Today the whole game is one grey room. After this mission a crew walks out of HQ
across a bridge onto the docked ship, picks a site on the ship's screen, sails
(the scene swaps to open ocean around the same ship), climbs into the glass
elevator on deck, suits up, and rides down together into `DiveSite01`. Divers
ride up whenever they want and land back on the deck; the cabin brings cargo up
too. When everyone is up the day ends; the crew dives again or changes site; after
three days the ship sails home to HQ. All grey boxes, no art, four players over
FishNet, every rule server-decided.

## 2. Decisions (fixed — Dan, 14 September 2026)

**Three scenes, one ship**
1. **HQ** with the ship docked beside it, one Unity scene, a walkable bridge
   between base and pier. No loading between them.
2. **The ship at sea**: the *same ship prefab* placed in an open-ocean scene.
   Pulling the wheel/lever loads that scene for everyone and puts each player at
   the same spot on the new deck.
3. **The dive**: `DiveSite01` (Idan's scene) with the elevator shaft; entered only
   through the elevator on the ship's deck.

**The screen and sailing**
4. The site screen is on the ship (docked or at sea, same screen). Any player
   picks the site. For the prototype there is one site, so the pick is one button.
5. Sailing needs a chosen site. Everyone must be aboard.

**The elevator and the day**
6. The glass elevator on the deck is the only way down and up. Going down: walk
   in, press the button. **It does not depart until every living player is in the
   cabin**; the cabin shows who is missing.
7. Suiting up: once the button is pressed with everyone inside, each player gets a
   timed "Putting on suit..." (about 3 s, no art yet), then the cabin descends.
   Suit on = air starts (Idan's oxygen card hooks here).
8. **A day = one dive.** It starts when the cabin departs with everyone; it ends
   when everyone is up or dead. Three days per cycle.
9. Anyone can ride up any time. **Once up, you cannot go down again until the next
   day.** The cabin returns for the others.
10. Ship and dive run **at the same time**: players on deck and players on the
    seafloor coexist; each sees only their own scene.
11. Between days the ship stays at sea. The crew may go straight back down to the
    same site, or change site on the screen first. After day 3, or an early
    "go home" on the screen, the ship sails to HQ and the quota is judged (quota
    itself is a later card).

**What travels, what stays**
12. Carried items (hands + four slots) travel with the player through every
    transition. Dropped items stay in their scene: seafloor drops are lost when
    the day ends; deck drops stay on the ship (and survive sailing).
13. Cargo sent up unattended arrives in the cabin on deck; players carry it into
    the storage room by hand. The storage room is a marked area for now.

**People**
14. Dead players: body and loot drop where they died; they get a free spectator
    camera that follows a diver of their choice (click to switch); they reappear
    on deck when the day ends. Hearing what the watched diver hears, and talking
    only to other dead players, arrive with the proximity voice card. A body
    carried up in the elevator keeps its upgrades (design section 4; no upgrades
    exist yet).
15. Surfaced players on deck see the same chosen diver on the ship's screen.
16. Joining: at HQ, and on the ship at sea between days (spawn on deck). Never
    mid-dive.

**Feel**
17. As few loading screens as possible: the elevator ride *is* the load (stand
    in the cabin, ~15 s, door opens when the scene is ready). Sailing may fade
    through the ship's screen; no black "Loading..." if it can be avoided.

**First delivery: the whole loop in grey boxes** — HQ + docked ship + bridge, ship
at sea, dive site, one-site screen, elevator with the all-aboard rule and the
suit timer, down/up with cargo, day counter 1–3, sail home. Idan builds the ship
and scene layouts and the elevator object; Dan builds the networking, scene flow
and day state.

## 3. What exists today (verified)

- `Assets/_Project/Scenes/Prototype/HQPrototype.unity`: the grey HQ room, the
  session UI, the network root, four spawn points, seven carryable items. This is
  the only scene the network ever loads; `PrototypeSessionController` hosts/joins
  in it and `PlayerSpawner` spawns players at its spawn points.
- `Assets/_Project/Scenes/Prototype/DiveSite01.unity` (Idan, PR #6/#10): 150 m
  seafloor, wreck, placeholder glass car, guide cable through an open shaft,
  deep-water lighting layer; `DiveSiteBuilder` / `DiveSiteValidator` generate and
  check it; `DiveSiteDevPlayer` is a local-only harness, not networked;
  `DiveSiteSettings` holds its tuning.
- Inventory and weight (PR #11, #13): `CarryableItem` state machine, `PlayerInventory`
  with server-written slots and carried mass, moving catches, `WeightSettings`.
  Items are scene objects in the HQ scene; nothing moves between scenes yet.
- FishNet 4.7.3 scene management is installed and unused: `SceneManager.LoadGlobalScenes`
  / `LoadConnectionScenes(NetworkConnection, SceneLoadData)` / `UnloadConnectionScenes`,
  `SceneLoadData` with `SceneLookupDatas`, `MovedNetworkObjects`, `ReplaceScenes`
  (`ReplaceOption`), `PreferredActiveScene`, events `OnLoadEnd` and
  `OnClientPresenceChangeEnd`; an `ObserverManager` `SceneCondition` limits
  observers to the scenes they are in. The player prefab is spawned by
  `PlayerSpawner` at host/join; there is no per-scene spawn point logic.
- The contract (`NETWORK_CONTRACT.md`) says the server decides site seed and dive
  lifecycle and the elevator state, calls moving-elevator handoff undefined, and
  lists no rule for scene membership or day state.
- The elevator does not exist as an object yet (Idan's E4 cards: prefab + timing
  ScriptableObject, lever, ride, noise).

## 4. What has to be done

- **Scenes**: the HQ scene gains a pier, a bridge and the ship prefab; a new
  `ShipAtSea` scene with the same prefab on open water; `DiveSite01` gains the
  elevator landing and four spawn points at the shaft. All grey boxes, generated
  by editor scripts like the existing builders (Idan).
- **The ship prefab**: deck, screen (one button: the site), wheel/lever, elevator
  cabin position, storage area, spawn points on deck.
- **Scene flow over the network** (Dan): the host loads/unloads scenes for
  connections; players are moved between scenes with their inventory; per-scene
  observers so deck and seafloor coexist; spawn points per scene; joiners land
  in the right scene. Local/LAN and Steam both.
- **Day state** (server-owned): current site, day number 1–3, who is on the
  seafloor, who has surfaced today (and so cannot go down), dive in progress or
  not, sail-home condition.
- **Elevator flow** (server-owned): all-aboard check, suit timer, descend, ride
  up for whoever is inside, cargo handling, arrival on deck. The 15 s / 15 s
  timing lives in Idan's elevator ScriptableObject; the noise event is Idan's
  card and can be stubbed.
- **Spectator camera** for dead players and the deck screen showing a chosen
  diver (camera switching only).
- **Docs**: `NETWORK_CONTRACT.md` gains scene membership, day state and elevator
  rules (server-only rows, disconnect during a dive); `README.md` gets the
  walkthrough; the test report gets the rows.

## 5. Done means

- Four players (host + three, Local; then two machines on Steam) walk from HQ
  across the bridge onto the ship, one picks the site, one pulls the wheel, all
  four arrive on the deck at sea.
- The elevator refuses to leave with someone missing and says who; with all four
  inside and suited it descends; all four stand in `DiveSite01` with their
  inventories, F3 rows correct.
- One player rides up alone: lands on deck, the others keep playing below; that
  player cannot go down again this day; cargo sent up unattended appears in the
  cabin on deck.
- When the last diver is up the day counter advances; the crew dives again
  without touching the screen; after day 3 the ship returns to HQ.
- A friend joins while the ship is at sea between days and spawns on deck.
- A dead player spectates a diver; the deck screen shows the same diver.
- Leave/re-host and disconnect during a dive behave under the contract rules
  written for this mission.
- Recorded in `docs/HQ_PROTOTYPE_TEST_REPORT.md` with revision, roles, RTT.

## 6. Explicitly not this card

Art, water, the suit animation itself, oxygen, damage, the quota and shop
(economy cards), the site vote (one site for now), the elevator noise event and
monsters (Idan's noise/monster cards), proximity voice, saving.

## 7. Contract and review notes

- New server-owned state: scene membership, day state, elevator state. Section
  3 of the contract needs rows; section 6 needs a rule for a disconnect while
  diving (body stays where it fell — the design's rule — versus the harness's
  "drop everything at the last position"); section 9 (elevator) needs the
  all-aboard, suit and once-per-day rules.
- Moving players between scenes with `MovedNetworkObjects` changes who observes
  what; the one-writer rule is unchanged, but held items must move with their
  holder and stowed items with their carrier.
- Everything here crosses the network: a human reads it before merge, and the
  scenes are Idan's — coordinate before touching `DiveSite01.unity`.

## 8. Open questions for the planner

1. Which scenes are loaded on the host at once (HQ never together with the sea;
   ship + dive together during a day)? `ReplaceOption` and connection-scoped
   loads decide this.
2. Are carried items re-parented to the player object for the move, or moved as
   separate `MovedNetworkObjects`?
3. How the elevator cabin "moves" without moving-platform physics: the player is
   held in place (input off) during the ride and teleported at arrival.
4. What the guest sees during the 15 s ride while the scene loads on their
   machine.
5. Spawn-point assignment per scene when the ship prefab is instanced twice.
6. Where the day state lives (a scene-independent `NetworkObject` owned by the
   server).
