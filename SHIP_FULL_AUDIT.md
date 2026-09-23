# Ship Full Audit

**Branch:** `dan/ship-look` at `cc428d1`
**Date:** 23 Sep 2026
**Scope:** `Assets/_Project/Prefabs/World/Ship.prefab` as placed in `Scenes/Prototype/ShipAtSea.unity`. It also covers the code that generates it (`ShipStubBuilder`, `ShipDeckDressing`, `ShipModelSetup`, `DeckCabinBuilder`, `PropBuilder`), the art models under `Models/Ship/`, the runtime scripts the ship uses, and two places where the ship shows up outside its own scene: its mooring in `HQPrototype.unity`, and its lighting while `DiveSite01` is loaded with it.

This was an **audit only**. This file is the only change to the project.

- Eleven separate subagents did the investigating: placement, rotation, scale, physics, level design, visual consistency, hierarchy, rendering, lighting/audio, independent QA, and modern quality.
- The lead merged their findings, removed duplicates, checked conflicts against each other, and wrote this file.
- The agents used the Unity editor read-only: queries, raycasts, capsule and overlap probes, and captures from temporary hidden cameras. Both scenes were confirmed clean (not dirty) afterwards.

**Coordinates.** All coordinates are **ship-relative**:
- +z is the bow, −z is the stern.
- +x is starboard, −x is port.
- The deck is at y 0.
- The ship root sits at world (0, 0, 3000) in ShipAtSea.

**Player size.** Measured from the real settings (`PlayerMovementSettings.asset`, `PrototypePlayer.prefab`):
- capsule 1.8 m tall, radius 0.3 m
- eyes at 1.6 m
- jump 0.65 m
- step 0.25 m
- interact reach 3.5 m

**Evidence files** live outside the repo, in the session scratchpad: `C:\Users\Owner\AppData\Local\Temp\claude\C--Dev-SunkCost\c2843964-3fae-44a2-8c9f-6f90cd7e1ce4\scratchpad\audit\`. Below, it is written as `AUDIT\`.
- `captures\<agent>\*.png`: the captures
- `findings\<agent>.md`: each agent's raw notes
- `ship-scene-dump.txt`: the full hierarchy dump
- `layout\walkmap.txt`: the walkability map
- `captures\ship-physics-auditor\grid5.txt`: the capsule sweep
- `captures\ship-placement-auditor\penetration.txt`: prop-into-prop overlap depths

**Agent tags:** PLC placement · ROT rotation · SCL scale · PHY physics · LVL level design · VIS visual consistency · HIE hierarchy · RND rendering · LGT lighting/audio · QA independent QA · MOD modern quality.

---

## Executive Summary

| | Count |
|---|---|
| Objects inspected | 366 scene objects in the Ship/ShipAtSea hierarchy (68 art renderers and ~107 stub renderers), 30 part materials and their textures, ~15 related scripts, plus the HQ mooring and the dive-site lighting |
| **Critical** | **3** |
| **Likely errors** | **18** |
| **Suspicious / needs human review** | **18** |
| **Design / modernization** | **26** |
| **Minor** | **22** |
| **Total** | **87** |

**The short version:**
- **Recovery is broken.** Two problems combine into one trap:
  - The ship's Unstuck point (`BoardingPoint`) is *inside* the cargo container.
  - The open ring round the elevator well is a 4.5 m drop onto the sea with no way out, and a player stuck there blocks departure. The only way out is Unstuck, which puts them inside a collider.
- **The two worlds' lighting leaks into each other** whenever the dive site is loaded next to the ship. This is tested and confirmed, and it bears on the open "TV floor is darker" question.
- **One gap in the dressing code causes most of the placement errors:** nothing checks one prop against another. Lamps, bollards and lifebuoys land inside barrels, toolboxes, pipes and the container, and a stacked crate floats.
- **The console is the weakest station:**
  - its placeholder boxes were never hidden;
  - its screen and buttons stand on the desk lip, in front of the model's own screen;
  - its text overflows;
  - its labels are three different sizes.
- **Perceived quality suffers from mixed art and missing depth.** Good Meshy props sit next to flat-coloured stub boxes and debug-style `TextMesh` text. The lamps give no light, and no art model casts a shadow, so the deck looks flat and pasted together.

---

## Critical Problems

### SHIP-001: Unstuck on the ship teleports players inside the cargo container
- **Severity:** CRITICAL
- **Object:** `Ship/BoardingPoint` at (−6, 0, −11)
- **Hierarchy / source:**
  - `ShipStubBuilder.cs:42` (`StairFoot`) and `:135-138`
  - the container placed at `ShipDeckDressing.cs:216`
- **Area:** port side, midship to stern
- **Problem:** At sea, `WorldSceneFlow.Unstuck.cs:57` sends a stuck player to `ship.BoardingPoint` first. The 9 m container (box collider x −7.55..−3.65, y 0..3.9, z −14.5..−5.5) now stands on that point, 1.55 m deep inside it.
- **Evidence:**
  - A capsule overlap at the point returns only `Look/Container/Mesh`, and the walkmap cell there is solid.
  - The container arrived in 98c7224, after `StairFoot` already existed.
  - `WorldSceneChecks.cs:44` and `WorldLoopChecks:72` only test that the point is inside the Aboard volume, never that a capsule fits there.
  - The matrices also teleport the host there: `DeckCabinRideRuntimeChecks.cs:703/715/1102` and `SaveRuntimeChecks.cs:252`.
- **Why it matters:** The recovery path itself traps the player. Combined with SHIP-002 there is no clean way out.
- **Detected by:** PLC, HIE, LVL, PHY, QA, VIS, ROT (7 agents)
- **Fix direction:** Move the point to open deck (for example (−1.5, 0, −11) in the aft corridor), or fall back to `SpawnPoint_1`. Add a capsule-clearance check for BoardingPoint and every spawn point to `ShipAtSeaValidator` / `WorldSceneChecks`. The BoardingPoint is also a leftover of the removed stair plan; decide whether it should become "the Unstuck point".

### SHIP-002: The ring round the elevator well is an unguarded 4.5 m drop with no way out, and it blocks departure
- **Severity:** CRITICAL
- **Objects:**
  - `Ship/Well Rim` (inner radius 3.7)
  - `Ship/Pedestal` (radius 2.55)
  - the 24 `Ship/Well Wall Collider`s
  - the `Sea` mesh collider at y −4.5
  - `Ship/Well Grate` with its two short `Grate Rail`s, the only guard
- **Area:** midship well
- **Problem:**
  - A 1.15 m open ring runs between the cabin pedestal and the deck edge, and about 89 % of its edge has no guard.
  - A player who falls lands on the solid sea plane 4.5 m down, between walls they cannot climb.
  - Down there they are below the Aboard and SafeDeck volumes (their lowest y is −1), so `ServerEveryoneAboard` (`WorldSceneFlow.cs:333`) reports "Not aboard" and **the ship can't sail**.
  - Their only way out is Unstuck, which puts them in the container (SHIP-001).
  - Dropped or thrown loot that falls in is lost.
- **Evidence:**
  - The physics grid (`grid5.txt`) finds 158 edges where a player walking with 0.65 m jumps falls into the sea, for example from (±2.0, 2.75).
  - A capsule fits standing at (0, −4.5, −3.1). See `LVL 02_well_gap_unrailed.png` and `layout\moat.txt`.
  - The round rail was deleted along with `BuildLook` in 98c7224. `RingRadius` (`ShipStubBuilder.cs:41`) is now unused.
  - The comment at `ShipStubBuilder.cs:460-465` still promises "a round gap 0.6 m deep … the rail round it … a jump out".
- **Cross-check / human review:** Dan asked this session to "remove the elevator ring", and separately said "no fences" about the ship's sides. The removal may therefore have been intended. The *trap* (no exit, departure blocked) is not intended either way.
- **Detected by:** LVL, PHY, QA (3 agents)
- **Fix direction:** Dan picks one of:
  - (a) a rail or lip round the ring, open at the grate;
  - (b) an invisible guard like the bulwark's;
  - (c) a shallow catch floor about 0.6 m down, as the comment describes;
  - (d) an automatic put-back for players below deck −2, the way items already get one.

  Then update the stale comment.

### SHIP-003: The ship's and the dive's lights leak into each other when both scenes are loaded
- **Severity:** CRITICAL
- **Objects:**
  - the ShipAtSea `Sun` (intensity 0.9, cullingMask −257)
  - the DiveSite01 `Surface Light` (intensity 2.0, cullingMask −257)
  - `Assets/Settings/PC_Renderer.asset` (`m_RenderingMode: 2`, which is Forward+)
  - both scenes' `m_Sun: {fileID: 0}`
- **Area:** the whole ship and the whole dive, on the host and on any guest watching the TV (`WorldSceneFlow.Watch` loads the dive for them)
- **Problem:** Two faults combine.
  1. **Forward+ ignores `Light.cullingMask` on every light except the main one.**
     - Tested: at the seafloor (layer 8, DiveSiteDeep), a hidden directional light that masks out layer 8 raised brightness from 0.1525 to 0.2310, exactly as much as the same light with no mask. A masked point light raised it too (0.1678).
     - Captures: `LGT seafloor_base.png`, `seafloor_plus_masked_dir.png` and `seafloor_plus_unmasked_dir.png`.
  2. **Neither scene names its sun.** With both loaded, Unity picks the brightest directional light, and a live query returned `sun = Surface Light (DiveSite01)`. As a result:
     - The ship is lit mainly by the dive's steep 2.0 light, plus its own Sun as an additional light.
     - It loses the Sun's soft shadows.
     - The sky's sun disk moves.
- **Knock-on effects:**
  - The ship's Sun lights the seafloor in TV and spectator renders.
  - Hands, carried items and monsters at 45 m depth (layers Player 9, Carryable 10, Monster 11) are lit as if in sunlight, while the floor around them is not.
  - The deck looks different whenever anyone is diving.
- **Detected by:** LGT. The editor query from the ship-look work earlier this session also saw "RenderSettings.sun reported Surface Light whichever scene was active".
- **Fix direction:** Separate the two worlds' lights with **Rendering Layers**; `m_SupportsLightLayers: 1` is already on in the URP asset. Alternatively, switch off the other world's directional light and set the main light for each camera (`beginCameraRendering` / `endCameraRendering`). Setting `RenderSettings.sun` alone is not enough. See also SHIP-037.

---

## Likely Errors

### SHIP-004: The rail-row props are placed on top of hand-placed props all over the deck
- **Severity:** LIKELY ERROR
- **Source:** the lamp, bollard and lifebuoy rows at `ShipDeckDressing.cs:159-168` never test against the fixed-coordinate props at `:196-228` and `:259-261`
- **Area:** the whole ship, along both sides
- **Instances** (overlap depths from `penetration.txt`):

  | Where | Props | Overlap |
  |---|---|---|
  | port (−6.9, 5) | DeckLamp inside Barrel (−7, 5.2) | 0.57 m |
  | stbd (7.4, −13) | DeckLamp inside Barrel (7.4, −13.2) | 0.61 m |
  | port (−7.2, 9) | Lifebuoy stand inside Toolbox | 0.56 m |
  | stbd (6.8, 5) | Bollard swallowed by Pipes (6.42, 4) | 0.50 m |
  | stbd (7.1, −7) | Bollard exactly inside CableCoil | 0.53 m. Reads as rope round a bollard, but it only happened because two unrelated formulas landed 2 cm apart. |
  | stbd (7.2, 9) | Lifebuoy into Crate (6.4, 9.6) | 0.17 m |
  | port stern (−7, −20) | DeckLamp, Pipes, Barrel, CableCoil and a second Barrel piled together | 0.16–0.59 m; the lamp and barrels can't be seen (`PLC 29_port_stern_cluster.png`) |
- **Why it looks wrong:** The props merge into each other's meshes and colliders (`VIS eye_port_barrel_lamp.png`, `eye_port_buoy_toolbox.png`, `eye_stbd_pipes_bollard.png`). This breaks Dan's "nothing floating weird" rule in spirit.
- **Detected by:** PLC, VIS, HIE, PHY (4)
- **Fix direction:** One clearance pass in `ShipDeckDressing`: record each placed footprint, and skip or nudge any later prop that overlaps. It works like `OverTheDeck`, but prop against prop. This one change fixes SHIP-004, -005 and -006, and most of SHIP-013.

### SHIP-005: The container hides a lamp, a bollard and two barrels
- **Severity:** LIKELY ERROR
- **Object:** `Ship/Look/Container` at (−5.6, 0, −10), 9 × 3.9 × 3.9 m. Inside it:
  - DeckLamp (−7.22, −7): 0.67 m deep
  - Bollard (−7.27, −13): 0.83 m
  - Barrel (−6.8, −12.4): 1.10 m
  - Barrel (−5.6, −13.2): fully inside
- **Area:** port stern cargo
- **Evidence:**
  - None of them shows in `VIS top_stern.png`, `ov_port_beam.png` or `PLC 00_topdown.png`.
  - The comment at `ShipDeckDressing.cs:214` still says "a stack of two containers against the rail". The barrels at `:218-219` belong to that older layout.
  - The port lifebuoy (−8.1, −9) is squeezed into a 0.29 m gap beside the container.
- **Detected by:** PLC, VIS, HIE, PHY (4)
- **Fix direction:** The clearance pass (SHIP-004), plus moving the two barrels beside the container, plus fixing the comment. See SHIP-040 for the dead-end slot the container creates.

### SHIP-006: Two lamps side by side at the port bow; both lounge lamps poke into the bulwark
- **Severity:** LIKELY ERROR
- **Objects:** `Look/DeckLamp` at (−5.28, 0, 17) from the rail row, and (−6.2, 0, 17) from the lounge pair; the starboard side has only (6.2, 0, 17)
- **Source:** `ShipDeckDressing.cs:162-163` against `:194-195` (the lounge pair at `tv − 2`, which is 17)
- **Area:** bow lounge
- **Problem:**
  - The port rail row lands exactly on the lounge pair's z, so port ends up with 5 lamps and starboard with 4.
  - Both lounge lamps at x ±6.2 also sit about 0.1 m inside the bulwark, where the bow narrows.
- **Evidence:** `VIS eye_bow_port_lamps.png` vs `eye_bow_stbd_lamps.png`; `LGT lounge_to_tv.png`.
- **Detected by:** VIS, PLC, HIE, LGT (4)
- **Fix direction:** Stop the rail row before the lounge (for example `z < tv − 4`). Place the lounge lamps from `W(z)` (the hull's half-width at that z) minus a margin.

### SHIP-007: The stacked stern crate floats 0.525 m above the crate under it
- **Severity:** LIKELY ERROR
- **Object:** `Look/Crate` at (6.8, 1.2, −16.6), on top of `Look/Crate` at (6.8, 0, −16.4)
- **Source:** the hard-coded y at `ShipDeckDressing.cs:208`
- **Area:** starboard stern cargo
- **Evidence:**
  - The lower crate at gear scale 1.5 is 0.675 m tall. A raycast hits the upper crate's bottom at 1.20.
  - The gap is visible in `SCL 11_crate_stack.png` and `PLC 28_stacked_crate_out.png`.
  - Even at the old 2× scale the height (0.9 m) never matched.
- **Detected by:** SCL, VIS, PLC (3)
- **Fix direction:** Stack the upper crate on the lower crate's `renderer.bounds.max.y`.

### SHIP-008: The console's screen and buttons stand on the desk's front lip, in front of the model's own screen
- **Severity:** LIKELY ERROR
- **Objects:** `Ship/Monitor` (z −16.636), `Ship/MonitorButton_Site01`, `_HQ`, `_EndDay`, `Ship/MonitorStatus`; the model `Ship/Look/Console` (bounds z −17.805..−16.696)
- **Source:** `DressConsole`, `ShipDeckDressing.cs:419-428`
- **Area:** console, at the tower face
- **Problem:**
  - "Front" is taken as the bounding box's max z, which is the lower desk lip. The model's upright display is 0.6–0.9 m further back.
  - The flat 1.6 × 1.0 m cyan screen stands upright on the lip, in mid-air. It hides the model's own control panel and leaves the model's screen frame empty.
- **Evidence:** `VIS eye_console_side_port.png` (the gap from the side), `PLC 23_console_side.png`, `ROT scene_console_side.png`, `rot2_tower_face_beside_console.png`.
- **Comparison:** the TV was solved properly. `DressTv` / `ScreenPanel` measure the real panel from the mesh.
- **Detected by:** VIS, PLC, ROT, MOD (4)
- **Fix direction:** Measure the console's display panel the way `ScreenPanel` does (make the Console mesh readable). Put the screen on that panel and set the buttons into the sloped desk. This goes with SHIP-009, -028, -039, -044, -048 and -050.

### SHIP-009: The console's old stub boxes are never hidden, because of a prefix-match bug
- **Severity:** LIKELY ERROR
- **Objects:**
  - `Ship/Monitor Frame` (Ink cube)
  - `Ship/Monitor Console` (PanelDark cube)
  - `Ship/Monitor Console Stripe` (yellow Trim cube)

  All three renderers are enabled and casting shadows.
- **Source:** `ShipDeckDressing.cs`:
  - `:62`: `HiddenPrefixes` names "Monitor Frame" and "Monitor Console"
  - `:79-81`: `KeptPrefixes` contains "Monitor"
  - `:97`: the kept list is tested first, by `StartsWith`
- **Area:** console
- **Evidence:**
  - The desk box pokes 4.5 cm out of the model's back (z −17.85 vs −17.805). It shows as a navy slab between the console and the tower (`RND console_side.png`).
  - The boxes cast the only hard rectangular shadows on the deck (`RND console_front2.png`).
  - The code and comment both say they should be hidden.
- **Detected by:** HIE, VIS, RND, PLC, QA, PHY, ROT (7)
- **Fix direction:** Test the hidden list before the kept list, or make the kept entries exact names (`Monitor`, `MonitorButton_*`, `MonitorStatus`).

### SHIP-010: The cabin's hazard "Foot Band" is a solid disc that covers the cabin floor
- **Severity:** LIKELY ERROR
- **Objects:** `Ship/DeckCabin/Foot Band` (`ShipStubBuilder.cs:546`), a Unity built-in cylinder of radius 2.58 spanning y 0–0.2 m; `Ship/DeckCabin/Cabin Floor` (`DeckCabinBuilder.cs:57`), radius 2.5, y 0–0.1, carrying the floor collider
- **Area:** elevator cabin
- **Problem:**
  - The cylinder has caps, so its striped top at 0.2 m is the floor players see.
  - Players stand on the collider at 0.1 m, so their feet sink 10 cm into the stripes.
  - The real floor is never visible.
- **Evidence:** `RND cabin_floor_inside.png`, `grate_edge.png`; a downward ray hits `Cabin Floor` at 0.100.
- **Detected by:** RND
- **Fix direction:** Build the band as an open ring (`MeshKit.Band`, as the well rim band already does), or raise the floor to meet it.

### SHIP-011: The elevator's cap pieces float with visible gaps
- **Severity:** LIKELY ERROR
- **Objects:** `Ship/DeckCabin/Cap Ring`, `Cap Glow`, `Cap Top`, `Tube Beacon`
- **Source:** `DressCabin`, `ShipStubBuilder.cs:542-545`, together with `Visual()` at `:578`
- **Area:** elevator cabin top
- **Problem:** `DressCabin` stacks the pieces as if each position were a base, but `Visual()` centres each cylinder on its position. So:
  - the ring spans 3.32–3.68, and the glow and top start at 3.81, leaving a **13 cm** gap;
  - the top ends at 4.11, and the beacon starts at 4.26, leaving a **15 cm** gap.
- **Evidence:** `PLC 21_cabin_cap_side.png`.
- **Detected by:** PLC
- **Fix direction:** Place each piece at its base plus half its height. This also fixes SHIP-031, the sign that hangs from the ring.

### SHIP-012: The crane stands 3.3 m further inboard than coded, on top of a well bollard and the winch
- **Severity:** LIKELY ERROR
- **Objects:** `Look/Crane` placed at (6.5, 0, −6.5); `Look/Bollard` (4.17, 0, −4.17); `Look/Winch` (6.1, −3.5)
- **Source:** `ShipDeckDressing.cs:206`, with the crane special case in `OverTheDeck` at `:442-446`. `prepare_ship_part.py` centres the pivot on the whole mesh, boom included.
- **Area:** starboard midship, beside the well
- **Problem:**
  - Raycasts put the solid base at x 0.26–6.11, z −9.17..−3.79, which is centred near (3.2, −6.5).
  - The base overlaps the well bollard by 0.23 m and the winch by 0.38 m, and ends about 1 m from the well rim.
  - `OverTheDeck` checks the wrong point.
- **Evidence:** `PLC 31_crane_base_well.png`, `VIS eye_stbd_crane_base.png`.
- **Detected by:** PLC, VIS, PHY (3)
- **Fix direction:** Place the crane by its base offset, not its pivot. Drop or move the well bollard at (4.17, −4.17) and the winch.

### SHIP-013: A toolbox and a cable coil are sunk into the tower's base
- **Severity:** LIKELY ERROR
- **Objects:** `Look/Toolbox` (−3.4, 0, −18.6) (`:221`); `Look/CableCoil` (3.4, 0, −18.2) (`:211`)
- **Area:** the tower's bow face
- **Problem:** At knee height the tower's surface is at z −18.35..−18.9, not the −18.0 its bounding box suggests. About 0.6 m of the toolbox and 0.42 m of the coil are inside the tower.
- **Evidence:** `PLC 26_toolbox_tower.png`, `25_coil_tower.png`; `VIS eye_stern_port_cluster.png`, `eye_tower_stbd_flank.png`.
- **Detected by:** PLC, VIS, PHY (3)
- **Fix direction:** Place props against the tower by raycasting to its face, or move them about 1 m forward (z ≈ −17.3).

### SHIP-014: The well light is under the sea, and the well's depth never followed the sea level
- **Severity:** LIKELY ERROR
- **Objects:** `Ship/Well Light` at (0, −6.3, −3.3), intensity 2.5, range 6 (`ShipStubBuilder.cs:526-532`); `Well Wall` and `Pedestal`, which reach down to −7.5
- **Area:** midship well
- **Problem:**
  - The sea moved up to −4.5, but `WellDepth` (7.5) and the lamp's height stayed put.
  - The light is 1.8 m under the opaque sea plane and faces its underside, so the gap renders black (`LGT well_rim_down_port.png`). The comment "the water lit at its foot" no longer holds.
  - The walls and pedestal end 3 m below the sea and about 1 m below the hull's keel (−6.51).
- **Detected by:** LGT, QA (2)
- **Fix direction:** Derive `WellDepth` and the lamp height from `SeaLevelY`, with the lamp just above the water (about −3.5).

### SHIP-015: The deck lamps give no light
- **Severity:** LIKELY ERROR
- **Objects:** 9 `Ship/Look/DeckLamp` (`:163`, `:195`); `Models/Ship/DeckLamp/DeckLamp.mat` (keywords `[_NORMALMAP]` only, no emission)
- **Area:** the deck edge and the bow lounge
- **Problem:**
  - No lamp has a Light, and the heads don't glow.
  - The only lights on the ship are Sun, Well Light, Tube Light and the beacon.
  - `ShipModelSetup.cs:95` switches art shadows off because "the ship is lit by its own lamps", but it has none.
- **Detected by:** LGT, MOD, VIS (3)
- **Fix direction:** Give the prefab an emissive warm head and a small point light (range about 6 m, intensity 3–5, no shadows), following `PropBuilder.cs:72-75`. Aim the heads first (SHIP-027).

### SHIP-016: No art model casts a shadow; only the leftover stub boxes do
- **Severity:** LIKELY ERROR
- **Source:** `ShipModelSetup.cs:97` and `ShipDeckDressing.cs:553` (bulwark). In the dump, all 68 `Look` renderers have `shadows=Off`, while about 107 stub renderers still cast. So does the 2 km sea plane.
- **Area:** the whole ship, worst in the storage room and the lounge
- **Problem:**
  - The Sun has soft shadows (4096 map, 40 m distance), but the hull, tower, storage room, crane, container and furniture cast none.
  - Props look pasted onto a flat-lit deck.
  - The storage room floor is as bright as the open deck, because its roof casts nothing (`LGT storage_in_to_back.png`).
  - The only shadows on deck are rectangles from placeholder boxes (`RND console_front2.png`).
- **Detected by:** RND, LGT, VIS (3)
- **Fix direction:** Turn shadows on for the big parts: Hull, Tower, StorageRoom, Crane, Container, TvCabinet, Console and furniture. Turn them off for the sea, the hidden stubs and tiny props.

### SHIP-017: The HQ scene still moors the old 44 m ship, so the 48 m ship sits 2 m into the landing and its piles
- **Severity:** LIKELY ERROR (the HQ world, same `Ship.prefab`)
- **Objects:** the Ship instance in `HQPrototype.unity`, with its position override (−44, −6, −36.05), yaw 180; `Bridge Landing`, `Landing Girder`, two `Pile`s
- **Area:** the tower and the stern at the HQ mooring
- **Problem:**
  - The HQ scene was last saved on 20 Sep (7fbf4fc). The ship grew to 48 m on 23 Sep (98c7224).
  - `HQPlatformBuilder.cs:713-715` would now put it at z −38.05.
  - As saved, the landing slab and girder sit 2 m inside the top of the tower, and the piles (x −45.6 and −42.4) pass through the stern.
  - The scene also keeps an orphaned override on `Roof Gate` (fileID 7573397337574386929), which no longer exists.
- **Detected by:** QA
- **Fix direction:** Rebuild or reposition the HQ scene, coordinated with the scene owner. It overlaps the later "way aboard from the HQ" card.

### SHIP-018: Status text overflows the console screen and the cabin sign
- **Severity:** LIKELY ERROR
- **Objects:** `Ship/MonitorStatus` (strings from `ShipMonitor.Compose`); `Ship/DeckCabin/Cabin Status Sign`
- **Area:** console; elevator cabin
- **Problem:**
  - The status line has no fit box. Strings of 43–50 characters, such as "Day 1 of 3 — Site 01 — E on HQ to sail home", render about 3 m wide on a 1.6 m screen and run across the crew screen (`MOD 25`).
  - The cabin sign overflows its 2 m plate with just three short names ("Dive in progress — 3 below: Alice, Bob, Carol"). Real Steam names are longer.
  - The cabin sign is blank when idle (`MOD 07/09/26`).
- **Detected by:** MOD
- **Fix direction:** Fit or wrap the text to its screen. Split it into a state line and a hint line, and fall back to a count when names are too long. Give every display an idle message (see SHIP-064).

### SHIP-019: The ladder is half its intended height, stands off the wall, and reaches nothing
- **Severity:** LIKELY ERROR
- **Object:** `Look/Ladder` (−4.4, 0, −21) (`ShipDeckDressing.cs:257`); the fit rule in `prepare_ship_part.py:52` (target 0.5 × 0.15 × 3.0, "uniform")
- **Area:** the tower's port side
- **Problem:**
  - The uniform fit hit the 0.5 m width limit first, so the model is 1.53 m instead of 3.0 m, and 2.3 m at 1.5×. The tower is 6 m tall and its floor line is at 3 m.
  - The ladder stands 0.3–0.6 m off the tower wall. Its inner face is at x −4.32; the wall is at −3.7..−4.0 depending on height.
  - The game has no climbing, and the tower model already has its own ladder.
- **Evidence:** `SCL 07_tower_ladder.png`, `PLC 22_ladder_gap.png`, `LVL 09_tower_ladder.png`.
- **Cross-check:** ROT called it "flat against the tower wall". PLC's raycasts measure a gap, so the gap is taken as correct.
- **Detected by:** SCL, PLC, VIS, LVL, PHY (5)
- **Fix direction:** Remove it, since the tower has its own ladder. Or refit it by height and mount it flush.

### SHIP-020: The ship's name can't be read anywhere
- **Severity:** LIKELY ERROR
- **Objects:** the two `Look/NamePlate`s (±8.40, −2.6, 12); the disabled `Ship/Hull Name` ×2 and `Hull Year` ("BLACK TIDE", "- SALVAGE -", "2300")
- **Area:** hull, bow
- **Problem:**
  - The text names were switched off when the NamePlate model replaced them.
  - The model's baked lettering is garbled (it reads roughly "BLASK THE SALVAGE"), dark on dark, and small: 2.42 × 0.43 m, 40 % of its 3.0 × 0.6 target, where the old text was 6.6 × 1.2 m.
- **Evidence:** `MOD 19`; `RND nameplate_stbd.png`.
- **Detected by:** MOD, SCL, HIE (3)
- **Fix direction:** Put real text on the plate, or paint the name on the hull. Size it to read from the sea (SHIP-068 covers the scale table).

### SHIP-021: The dive site's underwater grade profile is never saved
- **Severity:** LIKELY ERROR (dive side; it affects what the TV and spectators see)
- **Object:** DiveSite01 `Underwater Volume` (`sharedProfile = null`, no profile)
- **Source:** `DiveSiteBuilder.cs:519` assigns `volume.profile`, a runtime instance that isn't saved, instead of `volume.sharedProfile`
- **Problem:** Every underwater view gets only the default `SampleSceneProfile` (Neutral tonemapping, vignette 0.2). The designed underwater grade doesn't exist in the saved scene.
- **Detected by:** LGT
- **Fix direction:** Assign `sharedProfile` to a saved profile asset. This also rules the grade out as the cause of the TV/diver brightness gap (SHIP-037).

---

## Suspicious / Needs Human Review

### SHIP-022: The lounge table stands behind the couches
- **Object:** `Look/Table` (0, 0, 9.5) (`ShipDeckDressing.cs:189-193`); Couches at z 13.5 facing the TV (z 19.45); Benches at (±5.2, 12)
- **Area:** bow lounge
- **Problem:** The comment says "a table between the benches", but the table is 2.5 m aft of the benches and 4 m behind the couch backs, on the side away from the TV.
- **Evidence:** `VIS eye_lounge_to_tv.png`, `ROT scene_lounge_from_tv.png`, `PLC 06_lounge.png`.
- **Detected by:** VIS, PLC, ROT, MOD (4)
- **Fix direction:** Confirm the layout with Dan. Move the table to z ≈ 12, between the benches, or to z ≈ 15.5–16, between the couches and the TV.

### SHIP-023: A full-height hole in the cabin wall behind the descend button
- **Objects:** `Ship/DeckCabin/Interior Walls` (Wall Segment 12 at 165° is skipped); the `DeckCabinButton` collider covers only y 1.1–1.5 (`RoundCabinGeometry.cs:98-99`)
- **Area:** elevator cabin; the dive-site car too (`ElevatorCabinBuilder.cs:62`)
- **Problem:**
  - A gap about 0.55–0.6 m wide runs the full 3.5 m height, and the glass over it has no collider.
  - A standing capsule is only just held. A crouched player is held by about 2 cm plus skin width.
  - Carried items can slide out into the well.
- **Detected by:** PHY, QA (2)
- **Fix direction:** Skip only the button's height band, or add a full-height collider behind the button.

### SHIP-024: The couch's collider is solid up to its backrest
- **Object:** `Look/Couch/Mesh` ×2 (`Solidify` box mode, `ShipDeckDressing.cs:505-518`)
- **Area:** bow lounge
- **Problem:**
  - The seat is at 0.53 m, but the box is filled to 0.92 m (z 12.97..14.03).
  - The seat can't be stood in or sat on, and 0.92 m is about the limit of jump plus step.
  - Dan's complaint "cant go on the couch" was answered with scale, which doesn't change this.
- **Detected by:** LVL
- **Fix direction:** A mesh collider for seating, or two boxes (seat and back).

### SHIP-025: The furniture and gear scales assume a bigger player than the real 1.8 m one
- **Objects:** Couch, Table, Bench (HumanScale 1.25); Barrel, Bollard, DeckLamp, Container (GearScale 1.5) (`ShipDeckDressing.cs:40-56`)
- **Problem:** The furniture models were already human-sized at 1×. With the extra 1.25 on a 1.8 m / 1.6 m-eye player:
  - the table top is 0.88 m (typical 0.75);
  - the couch and bench seats are 0.52 m (typical 0.45).
- **Human review:** Dan asked this session "GIVE PLAYER MORE HEIGHT 150%?". The player's capsule and eye height were **not** changed (SCL measured 1.8 m / 1.6 m), so the scale tiers answer a question the player settings never followed. Dan should decide: raise the player, or return furniture to 1×.
- **Detected by:** SCL (with LVL's measurements)
- **Fix direction:** Decide the player's size first, then set the tiers from it. See SHIP-063 for the gear.

### SHIP-026: The TV cabinet gets the 2× machinery scale by default, and the screen's bottom edge is above head height
- **Objects:** `Look/TvCabinet` (6.14 × 4.66 m); `Ship/TvScreen` (y 2.155–4.455)
- **Source:** TvCabinet is missing from the `Scales` table, so `ScaleOf` falls back to 2
- **Area:** bow lounge
- **Problem:**
  - The screen's bottom edge is 0.35 m above the top of the player capsule.
  - From the couch, the look-up is about 19° to the centre and 28° to the top. SCL cites about 15° as a comfort limit; LVL measured about 22° and found the sightline clear.
  - Commit 5107627 asked for a big (about 5.2 m) screen, so the size is intended; the height is a side effect of the missing entry.
- **Detected by:** SCL, LVL (2)
- **Fix direction:** Add a deliberate `TvCabinet` entry, or keep the size and lower the screen.

### SHIP-027: Every deck lamp points its lamp head toward the bow
- **Objects:** all `Look/DeckLamp` (yaw 0; `Place` is called with no rotation at `:163`, `:195`)
- **Problem:**
  - The model is a one-sided floodlight: a caged lens on one face, fins on the back.
  - The rail lamps shine along the rail, not onto the deck.
  - The lounge pair shine at the empty bow tip, away from the couches.
- **Evidence:** `ROT rot2_lamp_from_bow.png`, `rot2_lamp_from_stern.png`.
- **Detected by:** ROT
- **Fix direction:** Rail lamps get yaw `side > 0 ? −90 : 90` so they face inboard, as the lifebuoys already do at `:167`. The lounge lamps face the couches.

### SHIP-028: The console buttons read "END DAY · HQ · SITE 01" from where the player stands
- **Objects:** `MonitorButton_Site01` (x −0.55), `_HQ` (0), `_EndDay` (+0.55) (`ShipStubBuilder.cs:400-405`)
- **Problem:**
  - The code order suggests Site01 → HQ → EndDay from left to right.
  - The panel faces the bow, so the player reading it looks aft and has −x on their right. They read End Day first, and it is the irreversible action.
- **Detected by:** ROT, VIS (2)
- **Fix direction:** Negate the x offsets, or confirm the order with Dan.

### SHIP-029: The end of the well grate flickers against the deck
- **Object:** `Ship/Well Grate` (`ShipStubBuilder.cs:517-519`)
- **Problem:**
  - The grate's top is at exactly y 0.000, like the hull deck and `Well Rim Band`.
  - The grate is 0.3 m longer than the gap, so at z 3.70–3.85 three surfaces overlap at the same height.
- **Evidence:** `RND grate_deck_overlap.png` shows yellow specks and a curved cut.
- **Detected by:** RND
- **Fix direction:** End the grate at radius 3.7, or lift it a few millimetres.

### SHIP-030: The winch floats 11 cm above the deck
- **Object:** `Look/Winch/Mesh`; `Models/Ship/Winch/Winch.fbx`
- **Problem:**
  - The lowest point of the mesh is at y 0.111; every other model is at 0–0.007.
  - Its size (1.56 × 1.17 × 1.06) doesn't match the prep target (2.0 × 1.2 × 1.6), which suggests it was reprocessed after being grounded.
- **Detected by:** SCL, PLC, ROT (3)
- **Fix direction:** Re-run it through `prepare_ship_part.py`, or offset it by its mesh minimum.

### SHIP-031: The cabin status sign hangs in mid-air
- **Object:** `Ship/DeckCabin/Cabin Status Sign` (`DeckCabinBuilder.cs:125`)
- **Problem:** The plate (z 2.5–2.6, y 2.75–3.25) is 0.25 m in front of the door housing and 7 cm below the cap ring, so it touches nothing.
- **Evidence:** `PLC 21_cabin_cap_side.png`.
- **Detected by:** PLC
- **Fix direction:** Hang it from the cap ring once SHIP-011 is fixed, or set it flush with the housing.

### SHIP-032: Holes in the stern deck on each side of the tower
- **Object:** the `Look/Hull/Mesh` collider near the transom, at x ±3.6..5.2, z −23.9..−23.1
- **Problem:** The ground drops to y ≈ −3.1. The holes are too narrow for a player, but dropped items fall in and are lost. The collider is the rendered mesh, so the holes are probably visible too.
- **Detected by:** PHY, LVL (2)
- **Fix direction:** Close the holes in the hull prep (`hull_flatten` / the deck slot), or add a filler collider.

### SHIP-033: The tower's collider is open above about 4 m
- **Object:** `Look/Tower/Mesh` (MeshCollider)
- **Problem:** Capsules at y 4–5 pass through the tower and out over the transom, where the guard is open on purpose. Nobody can reach that height today.
- **Detected by:** PHY (low present risk)
- **Fix direction:** Close the collider, or extend the guard over the tower span. This matters once the HQ bridge lands on the tower roof.

### SHIP-034: The ship moves about 90 static colliders with no kinematic Rigidbody
- **Objects:** the `Ship` root; `Look/Hull/Mesh` (60,956 vertices) and `Look/Tower/Mesh` (54,755), both non-convex MeshColliders
- **Problem:** `ShipDepartureVisual` moves the transform every frame while sailing. The pattern is older, but the colliders are now far heavier. This ties to the roadmap's "jitter on moving floors" card.
- **Detected by:** QA
- **Fix direction:** Consider a kinematic Rigidbody on the ship root, and simplified collision meshes.

### SHIP-035: The same glass elevator car is lit differently in the two worlds
- **Objects:** ship `DeckCabin/Tube Light`: cyan (0.75, 0.95, 1), intensity 8, range 8 (`ShipStubBuilder.cs:547-550`). Dive `Elevator/Cabin Light`: warm (1, 0.95, 0.85), intensity 6, range 7 (`DeckCabinRideSetup.cs:81-84`).
- **Problem:** Dan's rule, recorded in the builder, is that the two cars "should look the same". Riders will see the light change at the scene swap, and the cyan light spills about 8 m onto the deck.
- **Detected by:** LGT
- **Fix direction:** One shared car-light definition, used by both builders.

### SHIP-036: The winch and bell sounds are 2D across the whole ship
- **Source:** `ElevatorSounds.cs:46-48`: `spatialBlend = 0` for "Winch through the deck" and "Bell on the deck", yet the sources are moved to the cabin every frame (`:83`)
- **Problem:** Both play at the same volume inside the storage room and at the bow. Moving the sources suggests they were meant to be 3D.
- **Detected by:** LGT
- **Fix direction:** A partial spatial blend (about 0.7), or 3D with min distance about 6 m and max about 40 m.

### SHIP-037: WorldLook swaps only part of the lighting; an expert view on the open "TV floor is darker" issue
- **Objects:** `Scripts/World/WorldLook.cs`, `ShipTV.cs:157-168`, `docs/test-runs/2026-09-23-ship-look/RESULT.md`
- **Problem:** `WorldLook` swaps the ambient colour and the fog. It does not swap the ambient probe, the reflection source, the skybox or `RenderSettings.sun`.
  - The host's ambient probe is the ship's (L0 ≈ 0.051, 0.064, 0.084 linear), 20–40× brighter than the dive's flat ambient.
  - That, and SHIP-003, would make the TV picture **brighter**, not darker.
  - Also ruled out as causes: headlamp shadows (none), Forward+ per-object light limits (they don't apply), and ship lights reaching the site (it is 3 km away).
- **Assessment:** The measured gap (TV 0.060 vs B's own screen 0.124) is more likely a capture-path effect than a lighting one:
  - a backbuffer capture with HUD, compared with a ReadPixels of an sRGB render texture;
  - or a small pose or headlamp-aim difference.
- **Decisive test:** Log the following inside the TV render (between `Begin` and `Restore`) and in B's own frame at the same moment:
  - `ambientProbe[0..2,0]`
  - `RenderSettings.sun`
  - the visible lights and their intensities
  - the headlamp's enabled state, intensity and rotation
  - the fog
- **Detected by:** LGT (matches the lead left in RESULT.md)
- **Fix direction:** Whatever the test shows, extend `WorldLook` to carry the sun, the reflection source and the ambient probe, alongside SHIP-003.

### SHIP-038: The TV's box collider is an invisible wall under and in front of the screen
- **Object:** `Look/TvCabinet/Mesh` BoxCollider (x ±3.07, z 19.05..19.85, y 0..4.66); `TvSpeaker` sits inside it
- **Problem:**
  - The model is a screen on two legs, and you can see under it, but the gap between the legs is solid.
  - The box's front face stands 23 cm in front of the screen, so thrown items bounce off nothing visible.
  - About 35 m² of bow tip behind it can be reached only round the sides, and holds nothing.
- **Evidence:** `LVL 08_bow_behind_tv.png`.
- **Detected by:** LVL, QA (2)
- **Fix direction:** A mesh collider, or separate boxes for the legs and the panel. Give the bow tip a use (see SHIP-041).

### SHIP-039: The tower model's door is right behind the console
- **Objects:** `Look/Tower` (its hatch door is about 1 m to starboard of centre); `Look/Console` at (0, 0, −17.25), x ±1.35
- **Problem:** The decorative door looks blocked by the console.
- **Evidence:** `ROT rot2_tower_face_beside_console.png`.
- **Detected by:** ROT
- **Fix direction:** Shift the console to port of the door as part of the console rework (SHIP-008).

---

## Modern Game Quality / UI / UX / Design Issues

### Layout / Gameplay

#### SHIP-040: The slot between the container and the port side is a dead end
- **Objects:** `Look/Container` (−5.6, 0, −10); `Look/Lifebuoy` (−8.1, −9) and `Look/DeckLamp` (−7.22, −7) close its end
- **Problem:** It looks like a rail walkway, but only about 0.75 m is free. A player gets about 3 m in before the lifebuoy stand blocks it, and the port rail beyond is walled off.
- **Evidence:** walkmap rows z −6.5..−9.5 have one free cell at x −8.0; `LVL 04_port_slot_container_rail.png`.
- **Detected by:** LVL, VIS
- **Direction:** Move the container about 0.6 m inboard (the aft corridor is 3.9 m wide and can spare it), or close the slot openly at its bow end.

#### SHIP-041: Four walled-off pockets, and one sign nobody can read
- **Pockets:**
  - starboard x 6–9, z −14..−7, holding a DeckLamp, a Lifebuoy and `Look/Signs` at (9.49, 0.6, −11), which no reachable spot can read;
  - port-aft of the tower, x −8..−4, z −23..−20, whose only gap in is 0.78 m;
  - round the winch, starboard x 6.25..8.25, z −6.5..−2.5;
  - along the port rail, x −9.25..−8, z −12.75..−9.75.
- **Why it matters:** Items dropped there can't be recovered, and the space reads as filler.
- **Detected by:** LVL, PHY
- **Direction:** Open a 1 m way into each, or make them deliberately solid. Move the starboard sign to where people can see it.

#### SHIP-042: Wayfinding: the container hatch reads as the door, the storage door is side-on, and every haul is a U-turn
- **Objects:** the `Look/Container` end face (z −5.5, a big round hatch); `Ship/Storage Sign` (faces port); the `DeckCabin` doorway (faces the bow); storage at (3.9, −12.5); console at z −17
- **Problems:**
  - The first door-like thing a spawned player sees aft of the elevator is the container's hatch, which can't be opened.
  - From the well, the real storage doorway and its sign are side-on.
  - The cabin opens toward the bow, but storage and the console are aft. Every haul walks about 27 m round the unguarded ring (SHIP-002) to cover 13 m in a straight line.
- **Evidence:** `LVL 01_spawn3_first_view.png`, `06_well_aft_what_next.png`, `10_cabin_inside_from_door.png`.
- **Detected by:** LVL
- **Direction:** Turn the container so the hatch faces a side. Add floor arrows or painted walkways (see SHIP-055). Consider an aft-facing door in the coming elevator redesign.

#### SHIP-043: The TV can't be switched from the couch
- **Objects:** `Ship/TvScreen` (E changes the watched diver); `interactReach` 3.5 m (`HQPlayerController.cs:60`)
- **Problem:** The screen's nearest point is about 4.7 m from the couch. A player must walk to about z > 15.5 to change channel.
- **Detected by:** LVL
- **Direction:** Accept it, or add a remote or panel by the couch that drives the same request.

### UI / Buttons / Screens

#### SHIP-044: The console monitor is a blank, over-bright cyan slab, with the buttons covering its bottom 40 %
- **Objects:** `Ship/Monitor` (y 1.10–2.10); buttons at y 1.12–1.52; `ScreenTeal` emission (0.35, 1.29, 1.15)
- **Why it lowers quality:**
  - A flat, glowing, empty rectangle is the brightest thing on deck and has no content, so it reads as a placeholder.
  - Putting the buttons on top of the screen confuses touchscreen and hardware panel.
- **Evidence:** `MOD 01, 02, 03, 16`.
- **Detected by:** MOD, RND (placeholder material)
- **Direction:** A darker screen with a small layout (current site, day, quota, and the destination list, with the highlighted choice). Physical buttons go on the desk (SHIP-008).

#### SHIP-045: The crew screen shows a joke placeholder
- **Object:** `Ship/Crew Screen Text/Text`: "GOOD HAULS TODAY :)" in dark slate on over-bright cyan
- **Why it lowers quality:** It competes with the monitor for attention and carries no information, so it reads as developer filler.
- **Detected by:** MOD
- **Direction:** Give it a job (quota progress, crew list and states, a day clock), or remove it.

#### SHIP-046: The TV's idle state is a small "NO SIGNAL" on a 12 m² dead field
- **Objects:** `Ship/TvScreen`; `Tv Caption Sign/TvCaption`. The saved caption colour is red, but the game recolours it grey, so the editor and the game disagree.
- **Why it lowers quality:** The lounge's centrepiece is a dead rectangle most of the time.
- **Evidence:** `MOD 05, 06, 14`.
- **Detected by:** MOD
- **Direction:** A designed idle channel (noise or the ship's logo), plus a proper "● LIVE — NAME" chip while broadcasting. One colour source for the caption.

#### SHIP-047: The storage readout has no hierarchy and no quota state
- **Object:** `Ship/Storage Sign/StorageReadout`: "STORAGE / $1250 / $500 / balance $300" in mixed case and one size, filling under 30 % of its plate
- **Why it lowers quality:** 1250/500 looks the same as 0/500, so the most important number in the game has no emphasis.
- **Evidence:** `MOD 10, 11, 27`.
- **Detected by:** MOD
- **Direction:** A big value, a quota bar with an under/over colour, and a small balance line. Add an inward-facing copy inside the room.

#### SHIP-048: The buttons are primitive boxes with no states, and all of them are red
- **Objects:** `MonitorButton_*` and `DeckCabinButton` (Bezel/Cap/Cap Edge cubes, `PropBuilder.PushButton`); the `ButtonRed` material looks maroon at the console and bright red in the cabin
- **Why it lowers quality:**
  - No aimed, pressed or locked state, so the controls don't answer the player.
  - Red on everything means red carries no meaning.
- **Detected by:** MOD
- **Direction:** One button prefab with hover, pressed and locked states, and a colour code (red only for danger or irreversible actions, such as End Day).

#### SHIP-049: The cabin's descend button floats against the glass
- **Object:** `Ship/DeckCabin/DeckCabinButton` (the inner walls are collider-only)
- **Why it lowers quality:** Nothing visible holds the most important control on the ship.
- **Evidence:** `MOD 08, 20`.
- **Detected by:** MOD
- **Direction:** Mount it on a pillar or panel with a "DESCEND" plate. This goes with the elevator redesign and SHIP-023.

### Typography / Readability

#### SHIP-050: The console's labels are three different sizes
- **Objects:** the `Text` child of each `MonitorButton_*`. `SignText` shrinks each label on its own, so "HQ" (0.022) is 2.3× the size of "SITE 01" and "END DAY" (0.0096).
- **Why it lowers quality:** HQ looks like the main action, and the panel looks assembled by hand rather than designed.
- **Evidence:** `SCL 02_console_eye.png`, `MOD 02`.
- **Detected by:** MOD, SCL
- **Direction:** Fit the group to its longest label and use one size for all.

#### SHIP-051: All world text is legacy `TextMesh` in built-in Arial, with mixed styles
- **Objects:** every `TextMesh` on the ship. Buttons are bold caps, the status is regular, the cabin panel italic, and the storage readout mixed case.
- **Why it lowers quality:** The default engine font with no type system is the single strongest "prototype" signal.
- **Detected by:** MOD
- **Direction:** Choose one or two fonts, move world text to TextMeshPro (available through `com.unity.ugui` 2.6.0), and define title, value and hint styles.

#### SHIP-052: The in-world screens print control hints with the E key built in
- **Examples:** "E on HQ to sail home", "monitor locked"
- **Why it lowers quality:** Diegetic screens should show ship state. Key prompts belong to the HUD, which already shows them.
- **Detected by:** MOD
- **Direction:** Screens show state; the HUD carries the key.

#### SHIP-053: The HUD's interaction prompt is a stock grey IMGUI box at a fixed 15 px
- **Source:** `PlayerHudUI.cs:1055`, `:913`
- **Why it lowers quality:** The rest of the visor HUD scales with the screen and has its own style; this box is a debug widget. (Found in code; there was no Play Mode capture.)
- **Detected by:** MOD
- **Direction:** Restyle it to match the visor HUD, and scale it with resolution.

### Interaction Design

#### SHIP-054: Nothing in the world marks what can be used
- **Objects:** all interactables. Only the HUD aim dot changes colour. The Console model's moulded knobs look usable but aren't, and the TV looks the same whether or not it can be used.
- **Why it lowers quality:** A new player can't tell controls from decoration.
- **Detected by:** MOD
- **Direction:** One interactable signature (an edge glow or aim outline, plus a label plate), used only on things that can be used.

### Environmental Composition

#### SHIP-055: The deck floor is louder than the props, and the deck has no zones
- **Object:** the hull's `DeckPlate` material, one high-contrast tile with orange chevrons repeating about every 2 m over 48 × 20 m
- **Why it lowers quality:** The floor out-shouts everything, and the deck reads as a flat plaza with no walkways, zones or focal points.
- **Evidence:** `MOD 13–17`.
- **Detected by:** MOD
- **Direction:** A quieter base plate, plus painted walkways to the stations and marked zones (lounge, crane, well). This also answers SHIP-042.

#### SHIP-056: Props stand in mirrored rows; mooring bollards ring the lift well
- **Objects:** 9 lamps and 9 bollards in rows; 4 bollards at (±4.17, ±4.17) round the well (`:174-175`, commented as intended)
- **Why it lowers quality:** The regular rows read as procedurally placed, and mooring bollards belong at the ship's edge.
- **Evidence:** `MOD 13, 15, 17`; `VIS eye_well_from_bow.png`.
- **Detected by:** MOD, VIS
- **Direction:** Group props into purposeful clusters (a mooring station, a dive-prep bench, a cargo corner). Use guard posts or deck lights round the well.

#### SHIP-057: The same three-sign set is used four times, and one sign says "no diving" on a diving ship
- **Objects:** `Look/Signs` on the port bow, starboard side, tower and storage room. The bow copy is clipped by a bulwark corner.
- **Why it lowers quality:** Repeated identical signage reads as asset reuse, and a "no diving" sign contradicts the game.
- **Evidence:** `MOD 21, 22, 24`.
- **Detected by:** MOD
- **Direction:** Signs that carry information for each place (STORAGE, CREW ONLY, DIVE CAGE, MIND THE GAP). Remove the "no diving" pictogram.

#### SHIP-058: The storage room is an empty, unlit box
- **Object:** `Look/StorageRoom` interior
- **Why it lowers quality:** The room where loot goes has no shelving, drop-zone marking, light or inward readout, so it feels unfinished.
- **Evidence:** `VIS eye_storage_inside_back.png`, `eye_storage_inside_to_door.png`; `MOD 12`.
- **Detected by:** MOD, VIS
- **Direction:** Shelving or racks, a floor drop zone, a small interior light (it needs shadows first, SHIP-016), and an inward readout.

### Visual Consistency

#### SHIP-059: Three screen styles, three unrelated yellows, two hazard-stripe styles
- **Objects:**
  - screens: the TV is unlit navy; the monitor and crew screen glow cyan; the signs are black with yellow
  - hazard stripes: clean and saturated on the cabin; worn orange on the Meshy art
- **Why it lowers quality:** There is no shared palette or UI language across the stations.
- **Evidence:** `MOD 07, 13, 15`.
- **Detected by:** MOD
- **Direction:** Define one screen style (background, accent, text colours) and one hazard or accent palette, and apply them everywhere.

#### SHIP-060: Flat-colour stub geometry sits next to textured Meshy art
- **Objects and materials:**
  - Crew Screen and its frame, Monitor, button bezels and caps (`Ink`, `ScreenTeal`, `ButtonRed`)
  - the Storage Sign plate and glow bars (`SignBoard`), which cover the top of the storage model's door frame
  - the Well Grate slats and rails, the Pedestal and the Well Wall (`Ink`)
  - the console stripe (`Trim`)
- **Why it lowers quality:** Mixed detail levels are the main reason the deck reads as an asset pile rather than a designed space.
- **Evidence:** `VIS eye_tower_console_front.png`, `eye_storage_outside.png`, `eye_well_from_bow.png`.
- **Detected by:** VIS, RND, MOD (3)
- **Direction:** Replace these with art models, or give them a worn textured material from the Meshy set. Keep their colliders and interactions.

### Art Direction / Cohesion

#### SHIP-061: The elevator cabin, the ship's centrepiece, is its flattest object
- **Objects:** `Ship/DeckCabin/*` (plain cylinders and boxes; plain cube sign plates and frames)
- **Why it lowers quality:** The most important object on deck is the least detailed.
- **Human note:** a new elevator look is already planned ("after the ship is fixed").
- **Detected by:** MOD
- **Direction:** Until the redesign lands, reuse a worn Meshy material on the ring, gates and plates, and add bevels and grime.

#### SHIP-062: The Meshy models look blurry or faceted, and carry about 3× the vertices they need
- **Objects:**
  - all Meshy parts: flat shading gives every triangle its own vertices, about 960k vertices in total (Hull 61k, Tower 55k, Crane 43k, DeckLamp 10.6k × 9, and even flat signs about 8.2k)
  - the hull and tower: one 2048 texture over 48 × 20 × 6.5 m (about 20–40 texels per metre)
  - the crane base and winch look rough up close
- **Why it lowers quality:** Round props (barrels, lifebuoys, coils, bollards) look faceted, and the hull sides look smeared at close range.
- **Evidence:** `RND nameplate_stbd.png`; `VIS eye_crane_base_outside.png`; `MOD 12, 16, 19`.
- **Detected by:** RND, VIS, MOD (3)
- **Direction:**
  - Import with smoothing normals (Calculate, with a smoothing angle) instead of baked flat faces.
  - Enable GPU instancing on repeated props.
  - Add a tiling detail or trim texture to the hull and tower, as the deck and bulwark already have.
  - Regenerate or rebake the crane and winch.

#### SHIP-063: The deck gear at 1.5× reads large next to a 1.8 m player
- **Objects:**
  - Barrels 1.35 m tall (a real drum is 0.88 m)
  - Bollards 1.05 m, almost the bulwark's height
  - Lamp heads at 1.64–2.10 m, at eye level
  - Container 3.9 × 3.9 × 9 m, taller than the storage room. The code comment calls it "a 12 m one", but it is really a 6 m model at 1.5×.
- **Evidence:** `SCL 04_container_port.png`, `scale\layers.txt`.
- **Detected by:** SCL
- **Direction:** Dan's call, together with SHIP-025. If large props stay on purpose, consider 1–1.25× for the container and barrels.

### Areas That Feel Placeholder or Unfinished

#### SHIP-064: Blank, zero or joke text reaches the screens
- **Examples:** the crew screen joke (SHIP-045); "$0 / $0"; a blank monitor or cabin sign whenever the day state is empty
- **Why it lowers quality:** Every empty state shows the scaffolding.
- **Detected by:** MOD
- **Direction:** Give every display a designed idle state.

#### SHIP-065: No ambient audio, and no effects, on a ship at sea
- **Objects:** `ShipDepartureVisual.engine = null`; `AudioLibrary` has no sea, wind or engine slot; there are no audio files in `Assets` (every clip is a procedural placeholder); there is no ParticleSystem in either scene
- **Why it lowers quality:** The ship sails silently, with no wake, spray or exhaust, and the deck is dead quiet between events.
- **Detected by:** LGT, QA (2)
- **Direction:**
  - a 3D engine loop at the stern
  - a sea-wash bed
  - wind that rises on the upper deck
  - an optional TV hiss while it shows NO SIGNAL
  - a wake and bow spray while sailing

  Route all of it through `AudioDeviceService`.

---

## Minor Polish Issues

| ID | Object / area | Problem | Evidence / source | Agents | Direction |
|---|---|---|---|---|---|
| SHIP-066 | `SpawnPoint_1/2` (±3.5, 0, 7) | A player who walks straight ahead from spawn hits the well bollards at (±4.17, 4.17); the capsule overlaps by about 0.18 m | walkmap | LVL | Move the spawns about 0.5 m inboard, or turn them toward the cabin door |
| SHIP-067 | `Look/Bench` ×2 (±5.2, 12), yaw ±90 | The benches face the centre line, 60–90° off the TV | dump | LVL | Confirm with Dan (low confidence) |
| SHIP-068 | Scale table vs placement | NamePlate is 1.5 in `Scales` but placed at a literal 2 (`:151`); the storage sign is placed at 1, the other signs at 1.5 (`:263`) | dump | SCL, QA, ROT | Use one value and delete the dead entry; comment on the storage sign's scale if it is intentional |
| SHIP-069 | `Look/StorageSill` vs the `Ship/StorageSill` collider; the storage door collider | The visible sill is 87 % of the doorway (1.615 of 1.81 m), 16 cm off-centre (x 1.58–2.02 vs 1.77–2.15) and 0.158 vs 0.12 m tall. The door collider is 1.85 m wide to 2.7 m, while the model's opening is about 1.7 m with its lintel at 2.3 m. | `:239-241` | SCL, PLC, HIE, PHY | Scale from the measured mesh bounds, centre on x 1.96, and match the collider to the opening |
| SHIP-070 | `Ship/Well Rim`, `Well Rim Under` | They duplicate the hull deck, which is already cut round the well: the rim sits 1 cm above it (a 1 cm step, and a faint seam in `RND rim_corner_far.png`), and `Under` is buried in the hull | `ShipStubBuilder.cs:474-485` | RND, QA | Remove both |
| SHIP-071 | `DeckCabin/Cabin Roof` | The glass roof disc is entirely inside the opaque `Cap Ring`, so it can never be seen | dump | RND | Disable its renderer and keep the collider |
| SHIP-072 | `*/Cap Edge` on every button | Completely buried inside the Bezel box, so it is never seen but still drawn (the HQ's buttons too) | `PropBuilder.cs:506` | RND | Remove it, or make it proud of the bezel |
| SHIP-073 | 25–29 empty `Hull*` and `Tower*` stub objects; `Hull Name` ×2 and `Hull Year` | The hide pass leaves empty, oddly scaled transforms (up to 6.3, 7, 35.4). The hidden text objects still run `SignText` `[ExecuteAlways]` four times a second. The comment at `:105` says "the sign goes as a whole". | `ShipDeckDressing.cs:102-111` | HIE, RND, QA | Stop building them, or destroy the emptied branches |
| SHIP-074 | `ShipStubBuilder` create-then-delete; `HQPlatformBuilder.cs:716` | About 60 objects are built only to be destroyed (fenders, tyres, TV post and frame, bridge parts, roof rails, lamps, gate, radar, antenna, funnels, beacons), and colliders are added to Tower and Monitor Console only to be stripped. `HQPlatformBuilder` still looks up "Roof Gate", which no longer exists (`RoofGateName` is dead). | code | HIE, QA | Remove the dead build code, the lookup and the constant |
| SHIP-075 | Stale comments and docs | `ShipStubBuilder.cs:12-16` ("TEMPORARY … Idan's ShipBuilder"), `:92-95` (66 cm bulwark), `:194-197` (TV at the port rail), `:341-346` (radar, stair), `:451-458`, `:460-465` (well 0.6 m, rail). `ShipModelSetup.cs:200` (hull material "used by the tower too"). `DESIGN.md:66-74` (tower stair), `:291` (44 × 16 barge, bow crane, helipad, fenders), `:394` (TV at port midships). | code, docs | HIE, QA, LVL | Refresh them, in DESIGN.md with Dan's confirmation |
| SHIP-076 | `Ship` root, 106 unsorted children | Interactive parts are separate from the models they sit on (DressConsole and DressTv move stubs onto `Look/Console` and `Look/TvCabinet`), so moving a model leaves its buttons or screen behind. Two names are duplicated: `Ship/Tower` and `Look/Tower`, and `Ship/StorageSill` and `Look/StorageSill`. | dump | HIE | Group by area (Well, Tower, Console, Tv, Storage). `ShipParts.Find` searches at any depth, so regrouping is safe. |
| SHIP-077 | `ShipDeckDressing.Build` / `DressTv:326` | The TV caption's character size is multiplied by 1.4 on every run, and the class keeps static state. It is safe only because `EnsurePrefab` always starts from a fresh root. | code | HIE (QA confirmed fresh-root use) | Set absolute values |
| SHIP-078 | Every `Look/*/Mesh` at local rot (270.02, 0, 0) | The FBX `bakeAxisConversion` is off (`ShipModelSetup.cs:61-73`), leaving a 0.02° tilt (the deck is about 8 mm off level over 48 m; QA measured the collider flat to 0.2 mm) and Z-up children | `.fbx.meta` | ROT | Set `bakeAxisConversion = true`, then recheck `ScreenPanel`, `SampleOutline` and `MountSigns` |
| SHIP-079 | `Look/Signs` on the bulwark and tower; `Crew Screen Frame`; `Look/NamePlate` | The bulwark signs are 5–13 cm into the wall, with tops 7 cm above the rail. Where the tower face is uneven, the tower sign and crew screen stand off it by 6–38 cm in places and cut 8 cm into it in others. The storage sign is 4 cm proud of the visible wall. The nameplate may have up to 1 m of air behind its lower half. | `rays.txt`, `penetration.txt` | PLC | Use several rays against the mesh in `MountSigns`; cap signs at the 1.2 m bulwark; raycast the hull at the plate's own height |
| SHIP-080 | `Look/Toolbox` (6.6, 0, −23.4) | 10 cm into the stern wall and its guard | `:261` | PLC | Move it to z ≤ −23.2 |
| SHIP-081 | `Models/Ship/*/Generated/`; `Textures/Ship/Grime.png`; 7 part prefabs | 2.1 GB of raw Meshy downloads (git-ignored, but inside `Assets`, so Unity imports them: 120 PNGs with normals imported as colour, and 30 FBX files including a 218 MB one). `Grime.png` (2.6 MB) is unreferenced. Railing, CabinHousing, CabinDoor, ElevatorCar, CarPanel, TubeSection and TubeFoot are built but never placed; they are probably kept for the elevator redesign. | disk | RND, QA | Rename to `Generated~` or move outside `Assets`; delete Grime; keep the prefabs until the elevator redesign |
| SHIP-082 | `DiveSiteGlass.mat`; `Hull.mat`, `Tower.mat`, `StorageRoom.mat` | An invalid legacy `_ALPHABLEND_ON` keyword; a leftover detail scale of 24 (harmless: the texture is empty and the keyword is off) | .mat files | RND | Clean them in `BuildMaterial` |
| SHIP-083 | `Assets/_Recovery/0.unity` | An untracked, un-ignored crash-backup scene that could be committed by accident | git status | QA | Delete it, or ignore `_Recovery` |
| SHIP-084 | `Scripts/Look/Beacon.cs` (`[ExecuteAlways]`); `Tube Beacon/Light` (range 40, pulsing to intensity 4) | Writes `Light.intensity` in edit mode, so the saved value depends on when the scene was saved (0 in the dump) | code | LGT | Only animate in Play Mode |
| SHIP-085 | `BroadcastSound.cs:43-47`; `TvSpeaker` | The echo source it creates is never routed through `AudioDeviceService.Route`, unlike every other source. Its 20 m earshot is shorter than the TV's 30 m viewer radius. | code | LGT | Route it; align the radii |
| SHIP-086 | `DeckCabin/DeckCabinVolume` | A square 5 × 5 m trigger on a round cabin: its corners reach over the well gap, and it takes in 0.1 m of the grate | dump | PHY | A cylinder or capsule volume, or trimmed corners |
| SHIP-087 | ShipAtSea fog (linear 60–350 m, colour 0.45, 0.5, 0.55); the default procedural skybox | The sea goes flat grey well before the horizon, which shows as a band against the sky | `LGT` captures | LGT | Match the fog to the sky's horizon colour, or author a ship sky material |

---

## Category Index

To avoid repetition, each issue is written up once, above. The sections below list the IDs by technical category, so each category can be reviewed on its own.

### Collision / Physics Problems
- **Recovery and falling:** SHIP-001, 002, 023, 032, 033
- **Colliders not matching the visible shape:** 024 (couch), 038 (TV box), 069 (storage door and sill)
- **Colliders buried inside other props:** 004, 005, 012, 013
- **Engine and physics setup:** 034 (moving static colliders), 086 (square cabin volume)

### Layout / Gameplay Problems
SHIP-001, 002, 022, 024, 040, 041, 042, 043, 066, 067

### Transform Problems
- **Position:** SHIP-004, 005, 006, 007, 008, 011, 012, 013, 019, 022, 029, 030, 031, 069, 079, 080
- **Rotation:** SHIP-027 (lamps face the bow), 028 (button order), 039 (door behind the console), 067 (benches), 078 (0.02° import tilt)
- **Scale:** SHIP-019 (ladder), 020 (nameplate), 025 (tiers vs the real player), 026 (TV cabinet default), 050 (label sizes), 063 (gear), 068 (dead scale entries), 069 (sill)

### Rendering / Materials / Mesh Problems
SHIP-009, 010, 016, 029, 060, 062, 070, 071, 072, 081, 082

### Lighting / Effects / Audio Problems
SHIP-003, 014, 015, 016, 021, 035, 036, 037, 065, 084, 085, 087

### Hierarchy / Organization Problems
SHIP-009 (the prefix bug), 073, 074, 075, 076, 077, 083; also 017 (orphaned HQ override)

---

## Issues Detected by Multiple Agents

These were found independently by several agents. Treat them as the most certain findings.

| ID | Issue | Agents |
|---|---|---|
| SHIP-001 | BoardingPoint (Unstuck) inside the container | PLC, HIE, LVL, PHY, QA, VIS, ROT (7) |
| SHIP-009 | Monitor stub boxes never hidden | HIE, VIS, RND, PLC, QA, PHY, ROT (7) |
| SHIP-019 | The ladder: half height, off the wall, leads nowhere | SCL, PLC, VIS, LVL, PHY (5) |
| SHIP-004 | Rail-row props buried in other props | PLC, VIS, HIE, PHY (4) |
| SHIP-005 | Container hides a lamp, a bollard and 2 barrels | PLC, VIS, HIE, PHY (4) |
| SHIP-006 | Double lamp at the port bow | VIS, PLC, HIE, LGT (4) |
| SHIP-008 | Console screen and buttons float on the desk lip | VIS, PLC, ROT, MOD (4) |
| SHIP-022 | Lounge table behind the couches | VIS, PLC, ROT, MOD (4) |
| SHIP-069 | Storage sill and door don't match their colliders | SCL, PLC, HIE, PHY (4) |
| SHIP-002 | Well ring trap | LVL, PHY, QA (3) |
| SHIP-007 | Floating stacked crate | SCL, VIS, PLC (3) |
| SHIP-012 | Crane base inboard, overlapping the bollard and winch | PLC, VIS, PHY (3) |
| SHIP-013 | Toolbox and coil inside the tower | PLC, VIS, PHY (3) |
| SHIP-015 | Lamps give no light | LGT, MOD, VIS (3) |
| SHIP-016 | No shadows from the art models | RND, LGT, VIS (3) |
| SHIP-020 | Ship name unreadable | MOD, SCL, HIE (3) |
| SHIP-030 | Winch floats 11 cm | SCL, PLC, ROT (3) |
| SHIP-060 | Stub geometry beside Meshy art | VIS, RND, MOD (3) |
| SHIP-062 | Faceted, blurry, vertex-heavy art | RND, VIS, MOD (3) |
| SHIP-068 | Scale-table contradictions | SCL, QA, ROT (3) |
| SHIP-073 | Empty stub leftovers | HIE, RND, QA (3) |
| SHIP-014 | Well light under the sea | LGT, QA (2) |
| SHIP-023 | Cabin wall gap behind the button | PHY, QA (2) |
| SHIP-026 | TV cabinet scale and screen height | SCL, LVL (2) |
| SHIP-028 | Button reading order | ROT, VIS (2) |
| SHIP-032 | Stern deck holes | PHY, LVL (2) |
| SHIP-038 | TV invisible collider face | LVL, QA (2) |
| SHIP-050 | Console label sizes | MOD, SCL (2) |
| SHIP-065 | No ambient audio | LGT, QA (2) |

**Conflicts resolved during cross-checking:**
- **Ladder position (SHIP-019).** ROT saw the ladder "flat against the tower wall", but PLC's raycasts measured a 0.3–0.6 m gap. The measurement is kept.
- **TV viewing angle (SHIP-026).** SCL estimated about 19° up from the couch to the screen's centre; LVL measured about 22° with a clear sightline. It is kept as suspicious, not as an error.
- **Couch collider (SHIP-024).** PHY found that every prop's box collider matches its visible bounds. That is true, and still consistent with LVL's point: the bounds of a couch include its backrest, so a box around them fills the seat.
- **The well ring (SHIP-002).** It may have been removed on Dan's own "remove the elevator ring" instruction, so the decision is left to him. The trap itself is a defect either way.
- **The brief called BoardingPoint "a leftover" (SHIP-001).** It is not: Unstuck and three matrices use it.
- **Is the builder idempotent? (SHIP-077).** HIE found `Build` has side effects on a second run; QA found the prefab rebuild idempotent. Both are right: it is safe only because the root is always rebuilt fresh.

---

## Areas That Appear Healthy

Every part of the ship was inspected. These areas showed no meaningful problems:

- **Scripts and references:**
  - No missing scripts; all 8 script references and 25 nested prefab references resolve.
  - No null meshes or materials; material slot counts match sub-mesh counts everywhere.
  - All 25 `ShipParts.RequiredChildren` exist exactly once, and the runtime name lookups (ShipTV, ShipMonitor, StorageReadout, the TV press, Unstuck) all match.
- **Scene vs prefab:** one Ship instance in ShipAtSea, overriding only its name, position and rotation. No NetworkObjects on the ship, as the contract requires.
- **Constants agree:**
  - Sea level is −4.5 everywhere: ShipStubBuilder, DiveSiteSettings and its asset, ElevatorController, PlayerSubmersion.
  - The hull (19.97 × 48.0) matches the deck constants, and its well cut (3.74) matches the well wall.
- **Bulwark and guard:**
  - The 1.2 m bulwark and 8 m invisible guard are continuous along both sides and the bow, and the stern boxes close x ±4..±7.39.
  - 1,217 outward capsule casts found no way over the side. The crate → barrel → bulwark climb fails: the highest reachable surface is 2.22 m.
  - The bulwark mesh is visible from both sides, with correct winding.
- **Walkability:**
  - Every step is below the 0.25 m step offset, and no walked surface is steeper than 45°.
  - The storage room, cabin, console, lounge and all four spawns are reachable and connected.
  - The spawns are clear of every collider and face the cabin door.
- **Volumes:** all are triggers. `StorageVolume` matches the room's inner walls exactly, and Aboard/SafeDeck cover the hull and the tower roof.
- **Storage room:**
  - The doorway is 1.81–1.85 m wide with 2.28 m clear, and the ceiling underside is at 2.72 m. The 0.12 m sill is under the step height.
  - The model fits the stub colliders to within 3 cm.
  - The readout sits on the door header and reads from the doorway.
- **Console reach:** the buttons are at 1.12–1.52 m, easily reachable, and the console is in plain sight from the well and the aft corridor.
- **Elevator cabin:**
  - The doorway is 1.93 × 3.5 m, the button 1.1–1.5 m high and 2.3 m from the centre.
  - The door blocks only when shut, and the server reopens it for anyone in the doorway.
- **The TV:**
  - The TvCabinet model and its measured screen panel line up, and the screen sits 5 mm proud of the panel, facing the couches.
  - The caption reads correctly, and TvSpeaker is centred on the screen.
  - `ShipScreen` is Unlit.
- **Lounge symmetry:** the couches face the TV, and the benches mirror each other.
- **Materials and imports:**
  - All 30 part materials, plus DeckPlate and HullSteel, are URP Lit with no pink or error shaders, and the stale detail keywords are cleared.
  - Normal maps are imported as normal maps with sRGB off; colour maps have sRGB on; mipmaps are on; the size is 2048.
  - Read/write is on only for the Hull and TvCabinet.
- **Scale hygiene:**
  - No negative scale.
  - Every model prefab root is scale 1, and every FBX imports at scale 1.
  - All instances of a model share one scale.
  - No distorted children under non-uniform parents.
- **Rotation:**
  - Nothing is upside down, sideways or mirrored.
  - The side signs face inboard and the nameplates outboard.
  - The lifebuoys face inboard, the crane boom is over the side, and the winch drum leads to the well.
- **Nothing overboard:** apart from the crane boom and nameplates (both intended), nothing sits outside the hull. Nothing is accidentally at the origin.
- **Meshy prop art:** the crane, winch, bollards, lifebuoys, crates and console body share one art style. The sign icons themselves are good. The TV cabinet works as the bow's focal point.
- **Light and render settings:**
  - World Look snapshots match each scene's saved settings. Nothing is baked, so there are no stale lightmaps.
  - The render pipeline asset (HDR, 4096 shadow map at 40 m) is sound, and point lights have their shadows off.
  - The storage room and the tower face are not too dark (brightness 0.21 and 0.37). Their problem is flatness (SHIP-016), not exposure.
- **Audio code:** the player and car audio (footsteps, leak hiss, the winch at the car) have sensible rolloff and are routed properly.
- **Text foundation:** `SignText`, `DepthText` and the TV's "LIVE · name" caption are a solid base for a restyle.

---

## Areas Requiring the Most Attention

Ranked by how many documented issues each area has, and how severe they are:

1. **The elevator well and cabin**, the densest area and the most severe:
   - the ring trap (002, critical)
   - the cabin wall gap (023)
   - the floor covered by the foot band (010)
   - floating cap pieces (011)
   - the hanging sign (031)
   - the well light under the sea (014)
   - grate z-fighting (029)
   - the square volume (086)
   - buried or duplicate geometry (070–072)
   - the cabin light mismatch (035)
   - the floating button (049)
   - the flattest art on the ship (061)

   About 14 issues, many of them generated by the same two builders (`ShipStubBuilder.DressCabin`/`BuildDeck` and `DeckCabinBuilder`).
2. **The console station at the tower face.** About 12 issues:
   - stub boxes never hidden (009)
   - screen and buttons floating on the lip (008)
   - text overflow (018)
   - label sizes (050)
   - button order (028)
   - a blocked tower door (039)
   - the blank cyan screen (044)
   - button styling (048)
   - key hints in the text (052)
   - the crew-screen joke (045)
   - the toolbox and coil inside the tower beside it (013)

   It is the station players use every day, and it looks the most placeholder.
3. **Port midship to stern cargo, round the container.** The Unstuck point (001, critical), the hidden props (005), the dead-end slot (040), the unreachable pockets (041), the port-stern pile (004) and the wayfinding confusion (042).
4. **Lighting across both worlds.** Leaking lights (003, critical), the WorldLook gaps and the TV question (037), the unsaved underwater grade (021), no shadows (016), and lamps that give no light (015). This also holds the open TV issue from the last session.
5. **The dressing code as a whole.** Most placement errors (004–007, 012, 013, 019, 022, 027, 030, 080) trace back to one missing feature in `ShipDeckDressing`: a prop-against-prop clearance check, and props mounted by raycast instead of fixed coordinates.

---

## Suggested Work Order

Related issues are grouped so each batch touches the same code once. Each batch needs Dan's look and the owed matrices (see `docs/test-runs/2026-09-23-ship-look/RESULT.md`) before a PR.

1. **Safety and recovery** (critical, small changes):
   - Move BoardingPoint (001).
   - Decide the well-ring design with Dan and add the exit or guard (002).
   - Close the cabin wall gap (023) and the stern deck holes (032).
   - Add a capsule-clearance validator for BoardingPoint and spawns (001, 066).
2. **Dressing clearance pass** (one change in `ShipDeckDressing`):
   - A prop-footprint registry that skips or nudges overlaps: 004, 005, 006.
   - Stack from measured bounds: 007.
   - Mount to structure by raycast: 013, 019, 079, 080.
   - Place the crane by its base: 012.
   - Ground the winch: 030.
   - Aim the lamps: 027.
   - The lounge table and benches, after asking Dan: 022, 067.
   - The sill and door: 069.
   - The scale-table cleanup: 068.
   - Re-run the shell audit afterwards.
3. **Console rework:**
   - Fix the hide/keep prefix bug (009).
   - Mount the screen on the measured panel and the buttons on the desk (008, 039).
   - Button order (028), one label size (050), text fit and state layout (018, 044, 052).
   - A button prefab with states (048).
   - The crew screen's job (045).
4. **Elevator cabin and well fixes** (before or alongside the planned elevator redesign):
   - 010, 011, 031, 014, 029, 070, 071, 072, 086.
   - One shared car light (035).
   - Button mounting (049).
   - An interim material pass (061).
5. **Lighting isolation and depth:**
   - Rendering-layer separation and a main light per world (003).
   - A complete WorldLook (037), then run the TV-floor test in RESULT.md.
   - Save the underwater profile (021).
   - Shadows on the large art (016).
   - Lamp lights and emission (015), after 027.
   - The fog horizon (087) and the edit-mode beacon (084).
6. **UI and typography system:**
   - One font and style set on TextMeshPro (051).
   - Screen style and palette (059).
   - Displays: the storage readout (047), the TV idle and live states (046), idle states everywhere (064).
   - Restyle the HUD prompt (053).
   - An interactable signature (054).
7. **Scale decision with Dan:**
   - The player's height vs the furniture and gear tiers (025, 063).
   - The TV cabinet entry and screen height (026).
   - Couch seating collision (024).
   - The nameplate and a readable ship name (020).
8. **Environment and art direction:**
   - A quieter deck and walkways (055, which also helps 042).
   - Prop clustering and the well bollards (056).
   - Per-place signage and dropping the "no diving" sign (057).
   - Storage interior dressing (058).
   - Replacing stub geometry and materials (060).
   - Smoothing normals, instancing and hull detail textures (062).
   - Opening or closing the pockets and the dead-end slot (040, 041); the TV collider and the bow tip (038, 043).
9. **Audio and effects:**
   - An engine, sea and wind bed, plus the wake (065).
   - 3D winch and bell (036).
   - Route the TV echo (085).
10. **Cleanup** (low risk, whenever):
    - Remove create-then-delete stubs and the stale Roof Gate lookup (073, 074).
    - Group the hierarchy (076).
    - Absolute values in `DressTv` (077).
    - `bakeAxisConversion` (078).
    - Move the Generated folders out of `Assets` (081); material keywords (082); `_Recovery` (083).
    - Stale comments and DESIGN.md (075, with Dan).
    - The HQ mooring rebuild (017), coordinated with the HQ scene owner and the later "way aboard from the HQ" card.
    - The kinematic Rigidbody for sailing (034), together with the "jitter on moving floors" card.
