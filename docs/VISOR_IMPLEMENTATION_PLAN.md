# The diving visor — implementation plan

Status: plan, 16 September 2026. Owner Dan; touches the contract (one item
value rule) and `PlayerHudUI`, `CarryableItem`, the loot manifest. Idan reads
the contract paragraph before the implementation PR merges.

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

### 3.2 Layout (1920 × 1080 reference, scaled by screen height)

```
 AIR  ████████████░░  86%          ▲ HOME 23 m            N · · E · · S
 HP   ██████████████ 100%                                 (compass strip)
 DEPTH 42.3 m                                             [crew tags follow
                                                           the divers]
                       [item outlines follow the items]
                            ·  ← aiming dot, gold on a takeable item
                       "Basketball · $48"  (tag under the dot's item)

 [slots] [weight meter] — unchanged, on the ship too
```

- Top-left cluster: AIR and HP bars, DEPTH. Bars are thin (8 px), pale cyan,
  a red tint below 25 % (both stay full for now).
- Top-centre: HOME — an arrow that points toward the tube doorway *relative
  to the view* (up = ahead, rotates as you turn) and the distance in metres.
  Hidden while inside the car.
- Top-right: a compass strip, 90° of heading visible, the cardinal letters
  sliding as you turn.
- Crew tags: `Name · 12 m` above each other diver's head, projected from the
  head position, drawn only when the diver is in front of the camera and
  within 40 m; brighter and slightly larger when the dot is on them.
- Item outlines: four bracket corners around the screen bounds of every
  `CarryableItem` in front of the camera within **12 m** (fog is 15–20 m, so
  the visor confirms what you can nearly see — it does not see through the
  murk). Pale; the targeted item's brackets turn gold and its tag appears:
  `Name · $value`, or `Name · $value · too heavy` when the weight rule refuses
  it, or `Name · $value · needs two hands`.
- Vignette: a radial darkening from 78 % of the half-diagonal outward, 35 %
  black at the corners. One texture, one `GUI.DrawTexture`.
- Everything is `OnGUI` in `PlayerHudUI`, like the rest of the HUD, so one
  file draws the whole surface and the editor test hooks keep reading it.

### 3.3 Item values

- `ItemValueRange` (`minValue`, `maxValue`, integers, dollars) serialized on
  `CarryableItem`; the loot manifest (`HQPrototypeLootSetup`) sets it per type:
  proposal, to confirm with Dan — Basketball 20–60, HeavyBallBlue 120–200,
  HeavyBallPurple 250–400, HeavyBallBlack 500–800 (the two-handed ones are
  the "alien artifacts" stand-ins: heavy, valuable, awkward).
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
| `Player/PlayerHudUI.cs` | The visor layer: bars, depth, compass, home arrow, crew tags, item brackets and tag, vignette; on only in the dive. |
| `Player/PlayerVisorMath.cs` (new, pure) | Screen projection helpers, bracket bounds, compass strip offsets, home-arrow angle, bar fill; testable without a scene. |
| `Interaction/CarryableItem.cs` | `value` SyncVar rolled at spawn; `Value`; `ItemValueRange` fields. |
| `Editor/Prototype/HQPrototypeLootSetup.cs` | Value ranges per manifest entry; validator requires `max ≥ min > 0`. |
| `Player/HQPlayerController.cs` | Display name, if needed (3.4). |
| `Net/InventoryVerificationPeer.cs` | Snapshot gains `visor=on/off`, `home=<m>`, the targeted item's `value`, so the guest's view can be compared. |
| `Editor/Prototype/HQPrototypeTestHooks.cs` | `VisorStatus()` for the matrices. |
| `docs/NETWORK_CONTRACT.md`, `docs/DESIGN.md` (section 5 row), `README.md` | The rules above. |

## 6. Verification

Pure (`Run visor checks`): bracket bounds for a sphere at several depths and
edges of the screen; compass offsets at 0/90/359°; home-arrow angle for a
target behind the camera; bar fill clamping; value ranges valid on every
manifest entry; layout fits at 16:9 and 21:9.

Play Mode, in the deck cabin matrix (host + rendering guest):

| Row | Check |
|---|---|
| V1 | Visor off on the ship, on the first frame in DiveSite01 (host snapshot and guest snapshot both say so), off again on the deck after the up ride. |
| V2 | DEPTH readout equals `PlayerSubmersion.DepthMeters` ± 0.1; compass heading equals the camera yaw ± 1°. |
| V3 | HOME: from 10 m out on the seafloor, the arrow's angle equals the true bearing to the tube doorway ± 3° and the distance ± 0.3 m; hidden inside the car. |
| V4 | A basketball carried down: the host's tag shows `Basketball · $value`, the guest's snapshot shows the same value for the same item id; the value lies in the manifest range. |
| V5 | Two items in view: both bracketed; only the one under the dot tagged; an item behind the camera or beyond 12 m: no bracket. |
| V6 | The guest's copy in view: a crew tag with its name and a distance within 0.3 m of the truth. |
| Captures | `Logs/visor-arrival.png` (first frame in the car), `Logs/visor-seafloor.png` (home arrow + a bracketed item), `Logs/visor-crew.png`. |

## 7. Build sequence

1. `ItemValueRange` + `value` SyncVar + manifest ranges + pure check; contract row.
2. `PlayerVisorMath` + pure checks.
3. `PlayerHudUI` visor layer: on/off, bars, depth, compass, home; rows V1–V3.
4. Item brackets + tag; crew tags; rows V4–V6; captures.
5. Docs; PR (contract → Idan reviews, someone other than Dan merges).

## 8. Open questions for Dan

- The value ranges in 3.3 — keep, or different numbers?
- Should the visor stay on inside the deck cabin at the top, until you step
  out onto the deck (the suit is still on), or switch off at the swap as
  planned? Planned: off at the swap, so the deck is always "dry".
- Vignette strength: 35 % at the corners as planned, or fainter?
- Crew tags always, or only when looked at? Planned: always within 40 m, brighter when looked at.

## 9. Risks

- `OnGUI` draws per item per frame; brackets for a dozen items are cheap, a
  hundred would not be — cap the bracketed set at the 12 nearest in view.
- Projection: an item behind the camera projects to a mirrored point; the
  math helper rejects `z < 0` before drawing (pure check).
- Values on items that already exist when a client joins arrive with the
  SyncVar's initial state (FishNet sends them on spawn); the tag must tolerate
  a frame of `0` before the first value lands — draw `$…` until `value > 0`.
