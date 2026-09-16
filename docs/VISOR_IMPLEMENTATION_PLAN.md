# The diving visor — implementation plan

Status: built, 16 September 2026 (`dan/visor`). Owner Dan; touches the
contract (one item value row) and `PlayerHudUI`, `CarryableItem`, the loot
setup. Idan reads the contract row before the PR merges.

Decisions taken on the open questions (Dan: "check what other games do and
figure it yourself"): vitals bottom-left (where DRG and most co-op games keep
them; the centre stays for the world), the compass strip top-centre with HOME
as a marker on it (the waypoint-on-compass pattern of Subnautica and open
world games) plus an on-world marker when the doorway is in view; crew tags
always within 40 m and brighter when looked at (DRG's always-visible team,
the co-op safety tool in the dark); the visor off at the deck swap. Air and
health read full, always, for now (Dan). The balls carry no value: they are
the HQ game's; the loot is coins of three sizes (section 3.3 as built). The
planned vignette became the **diving mask itself** (Dan, later the same day:
"not all the vision is open on the screen — some of it blocked because of the
binoculars"; section 3.5), and the coins carry their own slot icons.

## 1. Decision (Dan, 16 September 2026)

Every diver wears a **visor**: an AR overlay that is on from the moment the
suit is on (the fade in the elevator car) until they step back onto the deck.
No item, no key, no per-player choice — it is the suit. `DESIGN.md` section 5
already says "shows the basics as an AR overlay; loot values appear floating
over objects"; this plan is that row built.

Dan's answers on the card:

| Question | Answer |
|---|---|
| What it shows | "A lot of info": an **air bar**, a **health bar**, **item info** (items highlighted gold when pointed at, with their **value**), the **direction to the elevator**. Futuristic. |
| Item tagging | **Outline every item in view**, so you notice them; **name and value only on the one under the aiming dot**. |
| Values | **Randomised per dive** within a fixed range per item type, rolled by the server, replicated — the doc's rule, made real now. |
| Look | **Minimal readouts plus a faint vignette**: thin futuristic text and bars in the corners, a barely-there darkening of the screen edge that says "you are behind glass". Never blocks the view. |
| Carrying | Automatic, on with the suit, everyone has one. |
| The mask (later) | "The screen is the black rectangle — the player sees through the binoculars": the mask's frame edges the view. Picks: **one wide lens with a nose bridge** (his picture: a swim mask), a **thin rim** (light coverage), the **readouts on the glass**, **dark matte with a faint cyan edge glow**. |

Not a zoom, not a see-through-the-fog device: the visor tells you about what
you can already nearly see. The handheld device (detailed readouts, scans) of
`DESIGN.md` section 5 stays a later card.

## 2. What exists, what is missing

| Piece | Today | The visor needs |
|---|---|---|
| HUD | `PlayerHudUI` (owner-only `OnGUI`): aiming dot (gold on a takeable target), prompt, four slot boxes, weight meter | The same surface, extended. These stay on the ship too; the visor's own elements appear only in the dive. |
| Air | Nothing (the Air card) | A bar that reads full until the Air card wires it. The bar's place and look are decided here so the Air card only supplies the number. |
| Health | Nothing (no damage in the game) | A bar that reads full. Same reason. |
| Depth | `PlayerSubmersion.DepthMeters` (eye below sea level) | Shown directly. |
| Heading | Camera yaw | A compass strip (N E S W, degrees). `DESIGN.md`: "a compass in the suit, no map". |
| Way home | The car (`WorldSceneFlow.FindCar()`), the tube gate (`ShaftGate`) | An arrow and a distance to the tube doorway from anywhere on the seafloor. |
| Crew | Other `HQPlayerController`s replicate position and yaw | A name and distance over each diver in view, brighter when looked at. Names: the session's display name for that client (the lobby settings / `PrototypeSessionController`). |
| Items | `CarryableItem` replicates state, holder, motion; `InteractionTargeting.Find` picks the one under the dot; **no value** | A per-item value replicated from the server, an outline on every item in view, a tag on the targeted one. |
| Dive-site loot | None yet: the only items are the HQ fixture balls (carried down if a player brings them) | Out of scope: the wreck-loot card spawns items in `DiveSite01`. The visor works on any `CarryableItem`, wherever it spawned. |

## 3. Design

### 3.1 On and off

The visor is on **exactly while the local player's object is in `DiveSite01`**:
that is where the suit is on. The car's fade-in at the top of the shaft is the
first frame you see through it; the deck cabin's doors opening at the top of
the up ride is the first frame without it (the swap to `ShipAtSea` happens
while the screen is dry and the doors are shut). No extra state — the world
the player stands in is already replicated and already drives the headlamp
and the sky (`WorldSceneFlow.PresentSky`).

### 3.2 Layout as built (1920 × 1080 reference, scaled by screen height)

```
 ╭──────────────────────────────────────────────────────────────╮
 │              N · · E · · S   ▲ 047°   ● HOME 23 m             │  ← compass strip on the glass
 │                                          [crew tags follow    │     under the top rim
 │          ⌐ ¬                              the divers]         │
 │          ∟ ┘ ← brackets on every coin in view                 │
 │                          ·  ← aiming dot, gold on a takeable  │
 │                     "Coin · $48"  (tag under the dot's coin)  │
 │  AIR ████████ 100%                                            │
 │  HP  ████████ 100%       [slots] [weight meter]               │  ← above the nose bridge
 │  DEPTH 42.3 m        ╭────╮                                   │
 ╰──────────────────────╯    ╰───────────────────────────────────╯
```

- Bottom-left, inside the rounded corner: AIR and HP bars, DEPTH. Bars are
  thin (8 px), pale cyan, a red tint below 25 % (both stay full for now).
- Top-centre, under the rim: a compass strip, 90° of heading visible, the
  cardinal letters sliding as you turn, the heading in degrees under it.
  HOME is a gold marker on the strip with `HOME 23 m` beneath (an arrow-head
  at the strip's edge when the doorway is outside the arc), plus a gold mark
  on the doorway itself when it is in view. Hidden while inside the car.
- Crew tags: `Diver N · 12 m` above each other diver's head, projected from
  the head position, drawn only when the diver is in front of the camera and
  within 40 m; brighter and slightly larger when the dot is on them.
- Item outlines: four bracket corners around the screen bounds of every
  `CarryableItem` in front of the camera within **12 m** (fog is 15–20 m, so
  the visor confirms what you can nearly see — it does not see through the
  murk), the 12 nearest at most. Pale; the targeted item's brackets turn gold
  and its tag appears: `Name · $value` (`$…` until the roll has landed).
- The slot row and weight meter move up onto the glass, just above the nose
  bridge, while the visor is on; on the ship they sit along the bottom edge.
- Everything is `OnGUI` in `PlayerHudUI`, like the rest of the HUD, so one
  file draws the whole surface and the editor test hooks keep reading it.
  Order: world-anchored marks (brackets, tags, the HOME mark on the doorway),
  then the mask, then the readouts, dot, prompt and slots — so the frame hides
  what a frame would hide and the glass shows everything else.

### 3.5 The mask (Dan, 16 September 2026, after the first build)

The view is seen through the suit's diving mask: `PlayerVisorMask` is one
wide lens with rounded corners and a nose bridge rising from the bottom
centre, described as a signed distance in screen pixels and baked once (at
half the screen's size, re-baked when the window changes) into a texture
`PlayerHudUI` stretches over the screen.

- Coverage, "light": the side rim is 5 % of the width, the top and bottom rim
  6 % of the height, the corner radius 20 % of the height; the bridge rises
  10 % of the height above the bottom rim and is 22 % of the height wide at
  its base, with a 3.5 %-of-height fillet where it meets the rim. Fractions of
  the height keep the lens' look at 16:9 and 21:9; the screen centre is always
  deep on the glass.
- Look: a near-black matte rim (94 % opaque), a thin cyan line just onto the
  frame (1.6 px wide at 1080p) with a soft cyan bloom either side, and the
  rim's shadow on the glass (30 % black at the edge fading over 60 px). No
  tint on the glass.
- The mask is on exactly when the visor is on, the car ride included (the
  readouts wait for the travel lock to lift, as the dot and prompt do); it
  draws under the readouts and above the world-anchored marks.
- On the ship there is no mask and the HUD is as before.

### 3.3 Item values — as built

- `valueMin`/`valueMax` (integers, dollars) serialized on `CarryableItem`;
  both zero means "not loot" — the HQ balls (Dan: "they should not have a
  value, they are HQ game only").
- The loot is **coins in three sizes** (`DiveLootSetup`): small ⌀ 0.16 m,
  0.4 kg, $10–30; medium ⌀ 0.26 m, 1.5 kg, $40–90; large ⌀ 0.42 m, 5 kg,
  $150–300. Gold, flat, one-hand, slot-able, throwable. Seven of them lie on
  the seafloor in a trail from the tube doorway toward the wreck (5–24 m out),
  spawned by a `LootFixtureSpawner` in `DiveSite01` when the server loads the
  site — fresh, and re-rolled, every dive.
- `SyncVar<int> value`, written **once by the server at spawn** from the
  range (`UnityEngine.Random.Range(min, max + 1)`), never re-rolled while the
  item exists. Items the dive site spawns fresh each load roll fresh each dive
  (the doc's "randomised each run"); the HQ fixture rolls once per session.
- Clients read `CarryableItem.Value`; nothing else changes on the item.
- Contract, section 3 table: `Item value | Server (rolled at spawn) | Clients display it; SyncVar, written once.`

### 3.4 Names

`PrototypeSessionController` already knows a display name per connection
(the lobby); if it is not on the player object yet, a `SyncVar<string>` on
`HQPlayerController` set by the server at spawn is the one addition — also
needed by every later feature that says who did what.

## 4. Network

- New replicated state: `CarryableItem.value` (one int per item, once) and,
  if not already there, the player's display name. Both server-written, both
  read-only on clients. Nothing else: the visor is presentation from positions
  and states that already replicate.
- Contract text: one row in the section 3 table (item value), one line under
  the SyncVar rule of section 4 naming the two fields. Idan reviews.

## 5. Files

| File | Change |
|---|---|
| `Player/PlayerHudUI.cs` | The visor layer: the mask, bars, depth, compass, HOME, crew tags, item brackets and tag; on only in the dive. |
| `Player/PlayerVisorMath.cs` (new, pure) | Screen projection helpers, bracket bounds, compass strip offsets, home-arrow angle, bar fill; testable without a scene. |
| `Player/PlayerVisorMask.cs` (new, pure) | The mask's shape (signed distance), where the readouts sit on the glass, the frame baked to a texture. |
| `Editor/Prototype/DiveLootSetup.cs` (new) | The coin prefabs (three sizes, gold, box collider, value range, their own slot icons), their placements, the `LootFixtureSpawner` in `DiveSite01`. |
| `Editor/Prototype/ItemIconGenerator.cs` | Per-prefab framing: the coins are seen from higher up and all framed to the largest, so a small coin looks small in the slot. |
| `Interaction/CarryableItem.cs` | `value` SyncVar rolled at spawn; `Value`; `ItemValueRange` fields. |
| `Editor/Prototype/HQPrototypeLootSetup.cs` | Value ranges per manifest entry; validator requires `max ≥ min > 0`. |
| `Player/HQPlayerController.cs` | Display name, if needed (3.4). |
| `Net/InventoryVerificationPeer.cs` | Snapshot gains `visor=on/off`, `home=<m>`, the targeted item's `value`, so the guest's view can be compared. |
| `Editor/Prototype/HQPrototypeTestHooks.cs` | `VisorStatus()` for the matrices. |
| `docs/NETWORK_CONTRACT.md`, `docs/DESIGN.md` (section 5 row), `README.md` | The rules above. |

## 6. Verification

Pure (`Run visor checks`): bearings and screen angles; compass offsets at
0/45/350° and outside the arc; bar fill clamping; projection refuses points
behind the camera, a box ahead brackets around its centre; the mask is glass
at the centre and frame at the corners, the rims and the nose bridge, and the
vitals block, the compass strip and the slot row lie on the glass at 16:9,
21:9 and 720p; a baked mask is clear at the centre and dark at the corner;
each coin prefab carries its range, a box collider, its mass and its own
icon; the basketball carries no value; placements distinct, off the doorway.

Play Mode, in the deck cabin matrix (host + rendering guest):

| Row | Check |
|---|---|
| V1 | Visor off on the ship, on at the bottom of the down ride (host readout and guest snapshot both say so), off again on the deck after the up ride. |
| V2 | DEPTH readout equals `PlayerSubmersion.DepthMeters` ± 0.1; compass heading equals the camera yaw ± 1°; air and health read full. |
| V3 | HOME: hidden inside the car; from 10 m out on the seafloor facing the doorway, the screen angle is under 3° and the distance 10 ± 0.6 m; facing away, ± 180°. |
| V4 | Seven coins on the seafloor with values in their ranges; standing at Coin 3 with the dot on it, the tag reads `Coin · $value` and it is bracketed; the guest's snapshot lists the same coins with the same values. |
| V5 | From 27 m out facing away: no brackets; facing back: at least one. |
| V6 | The guest's copy in view: one crew tag with a distance within 0.3 m of the truth. |
| Captures | `Logs/visor-seafloor.png` (the mask, the compass with HOME, a gold-bracketed coin with its tag), `Logs/visor-crew.png` (a crew tag). |

## 7. Build sequence

1. `ItemValueRange` + `value` SyncVar + manifest ranges + pure check; contract row.
2. `PlayerVisorMath` + pure checks.
3. `PlayerHudUI` visor layer: on/off, bars, depth, compass, home; rows V1–V3.
4. Item brackets + tag; crew tags; rows V4–V6; captures.
5. Docs; PR (contract → Idan reviews, someone other than Dan merges).

## 8. Open questions — answered

- Value ranges: the coins' (3.3), Dan's to retune in `DiveLootSetup`.
- The visor switches off at the deck swap, so the deck is always "dry".
- Vignette: replaced by the mask (3.5); no extra darkening.
- Crew tags: always within 40 m, brighter when looked at.
- Still open: players' display names on the crew tags (now `Diver N`).

## 9. Test report (16 September 2026, `dan/visor`)

Host in the editor (Unity 6000.6.0f1, 60 Hz tick), guest the Windows Local
Development build over Tugboat on one machine (UDP 7771), 240 Hz monitor.

- `Run visor checks`: passed — bearings, compass strip, bars, projection and
  brackets, the mask and its layout at 1920×1080, 2560×1080 and 1280×720, the
  baked mask's alpha, the coin prefabs (ranges, box collider wrapping the disc,
  mass, own icon), the basketball valueless, the placements.
- Dive site validator: passed (one `Dive Loot` spawner, seven entries, coins
  registered in the spawnable collection, resting on the seafloor).
- Deck cabin ride matrix: **MATRIX_PASS**, 200 rows. Visor rows: V1 off on the
  ship / on at the bottom / off again on the deck (host readout and guest
  snapshot); V2 depth 42.3 m = submersion, heading = camera yaw, air and
  health 100 %; V3 HOME hidden in the car, 0.0° and 10.0 m from 10 m out
  facing the doorway, 180.0° facing away; V4 seven coins, every value in its
  range, the host's tag on Coin 3 `Coin · $72`, the guest's snapshot lists the
  same seven network ids with the same values, and the guest's own visor tags
  Coin 3 `Coin · $67` (a later dive's roll) and brackets it; V5 one bracket at
  Coin 3, none from 27 m out looking away, one looking back down the trail;
  V6 the host tags the guest's copy at 1.4 m (truth 1.4 m). Guest build during
  the descent: 240 fps, 0 hitches ≥ 30 ms while the car moved.
- Player movement and hands matrix (the item and controller changed): **MATRIX_PASS**, 33 rows (M8 flight and the M8b upward throw included).
- Captures: `Logs/visor-seafloor.png` (the mask, the compass with HOME, the
  vitals, the slot row above the nose bridge, Coin 3 gold-bracketed under the
  dot with the grab prompt), `Logs/visor-crew.png` (inside the car, the crew
  tag row passing).

Found and fixed on the way: the coins were built on Unity's legacy cylinder
mesh, which is 2 units wide — every coin was twice its designed diameter with
a box collider over half the disc (now the primitive mesh, scaled from its
real bounds, with a pure check on the size); a client instantiates the
server's spawns into the session scene, so the visor's "same scene" filter
hid every coin from guests (now "not another world's scene"); the dot's
target was only found while the input gate was open, so a guest with the
session menu up (as the hook tests keep it) had no tag (the target is a view
fact now; only presses need the gate); the hook "look" command set the camera
directly and lost its pitch (it goes through the controller now).

## 10. Risks

- `OnGUI` draws per item per frame; brackets for a dozen items are cheap, a
  hundred would not be — cap the bracketed set at the 12 nearest in view.
- Projection: an item behind the camera projects to a mirrored point; the
  math helper rejects `z < 0` before drawing (pure check).
- Values on items that already exist when a client joins arrive with the
  SyncVar's initial state (FishNet sends them on spawn); the tag must tolerate
  a frame of `0` before the first value lands — draw `$…` until `value > 0`.
