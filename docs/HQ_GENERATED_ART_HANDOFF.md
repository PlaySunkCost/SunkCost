# Generated HQ — assembly and verification

Branch: `codex/hq-generated-art`, started from `origin/main` at `90e61b7`.
User request: build the HQ from the 24 finished Meshy FBX archives, reusing the
ship kit and the approved placement handoff. The original downloads are unchanged.

## Assets and reproducibility

- Game-ready models: `Assets/_Project/Models/HQ/<part>/`. Each has its FBX,
  rebaked colour/normal maps, URP material and `preparation.json` identifying
  the original archive, source hash, dimensions and triangle counts.
- Reusable visual prefabs: `Assets/_Project/Prefabs/HQ/`. Collision is authored
  in the assembly, not automatically added across door/window openings.
- `tools/blender/prepare_hq.py` prepares the extracted archives listed in
  `Library/HQBuild/archives.json`. This is an offline authoring operation;
  players do not need Blender, Meshy or the original archives.
- Source ZIPs: the user's `Desktop/SunkCost3D/HQ` directory. Raw extractions
  and audit renders are ignored under `Library/HQBuild`, not runtime assets.
- The basketball hoop is calibrated to a circular rim at 3.05 m. Its supplied
  proportions needed correction; the post and upper board are adjusted separately.
- Existing ship assets supply railings, lamps, crane, winch, container, console,
  TV cabinet, bollards, pipes, ladder, barrels, crates, coil, lifebuoys, toolbox,
  couches, benches, table and crew panel housing. Deck/structural materials
  are reused from the ship kit. Labels remain editable Unity text.

## Layout

Coordinates are metres; deck top is Y=0, north is +Z. Sea is Y=-4.5. The platform
is 60 × 36 m and the ship remains the same 48 × 20 m gameplay prefab.

| Area | Placement / purpose |
|---|---|
| Depot | X -24 to 8, Z 10 to 18; one 32 × 8 m building, 4 m interior height |
| Office | West end of the connected depot; no separate save station |
| Shop stands | Large tank (-13,14), bright lamp (-9,14), air tank (-3,14), patch kit (1,14), X/Z |
| Catalogue counter | (-6,14): E opens all catalogue items; search/filter/scroll, shared-pot purchases, no stand required for new merchandise |
| Pickup | Chute at (10,15), discharge just ahead of its lip; clear 4 × 4 m pad centred at (10,12) |
| Intake / quota | Former arrival area: console (-9,-14), hopper (-13,-14), facing inward; sells ship storage as before |
| Court | X -18 to 2, Z -8 to 4, 20 × 12 m; west/east hoop and backstop |
| Arrival | Centre (18,14), 6 × 4 m pad; four clear spawn positions facing south into the HQ |
| Colour panel | Directly ahead of arrivals at (18,11.35); existing colour-selection behaviour |
| Cargo apron | East side; crane around (24,4), container around (20,-10) |
| Crew rest | South side, clear of the arrival pad and plank |
| Bridge | Fixed X -30 to -41; hinged leaf to -44.2, centred Z -12, 3 m wide; leaf top 4 cm above the deck avoids coplanar overlap |
| Ship boarding | Port opening at ship-local Z -6; ship aligned using its measured hull outline |
| Plank | Existing south-east board, gate, jumper markers and reset rules; water now 4.5 m below |

The entire lower exterior catwalk, lamps, rails, supports and access ladders
are removed. The ship tower is preserved: the earlier tower-ladder cover was
an incorrect interpretation of the screenshot and has been undone.
Both hoop meshes face inward and their real rims align with the score triggers.
Shop names/prices use the existing aim-based HUD prompt; permanent item signs
are removed. Merchandise detailing, arrival wayfinding and seating light reuse
the existing kit. The crane and winch use mesh surfaces instead of oversized,
misplaced collision boxes on empty floor.

The catalogue counter makes the shop expandable independently of these four
featured displays. [Shop authoring](SHOP_CATALOGUE_AUTHORING.md) explains how to
add merchandise and distinguishes data additions from new gameplay effects.

The bridge remains HQ-owned. Both gates close during departure; the ship gate
stays closed at sea. The shared ship prefab keeps the same boarding opening at
HQ and sea. Its fixed HQ bridge and hinged tip are outside the safe-deck volume.
`DockGangway` raises during `RaisingGangway`, stays raised during departure,
and lowers during HQ `Arriving`. It uses the existing synchronized trip clock;
there is no new replicated state. The old overlapping ship threshold is removed.

## Build and run

With Play Mode stopped, use **Sunk Cost → Look → Build generated HQ**.
`HQGeneratedSetup.Apply()` imports the kit, rebuilds the HQ, reapplies cabin
glass, saves and validates. `Rebuild()` skips model import. These commands
regenerate the authored HQ scene; change the builder for persistent layout edits.

Open `Assets/_Project/Scenes/Prototype/Session.unity`, press Play, then Host.
The HQ world scene itself contains no session networking root or player camera.

The editor file bridge also accepts these lines in `Temp/editor-command.txt`:

```text
run SunkCost.Editor.Look.HQGeneratedSetup.Rebuild
run SunkCost.Editor.Look.HQGeneratedSetup.Capture
build-guest
matrix hq
matrix shop
matrix plank
matrix loop
stop
```

Read `Temp/editor-command.reply.txt` before issuing another command. A matrix
continues after its command reply; inspect its log for `MATRIX_PASS` or `FAIL`.
Stop Play Mode before modifying scripts or rebuilding the scene.

## Verification record

The 26 September corrections and expandable catalogue are recorded separately
in the [HQ polish test report](test-runs/2026-09-26-hq-polish/RESULT.md).
The earlier scoring test below checked score increments, but did not catch the
backward art. The corrected version also checks the visible rim orientation.

Verified on 25 September 2026 with Unity 6000.6.0f1. Full evidence:
[test report](test-runs/2026-09-25-hq-generated/RESULT.md).

- Unity compilation and Windows development build succeeded.
- Static layout validation passed: four clear spawns, a continuous level bridge,
  no blocked boarding capsule, fixed bridge excluded from aboard tests, and the
  existing shop/court/plank setup valid.
- `matrix hq`: PASS. Actual keyboard-driven controller movement across the bridge
  both ways and through the depot; real balls scored through both generated rims.
- `matrix shop`: PASS with editor host and separate executable client. Purchases,
  delivery, upgrades, refusals, diving, abandoned-body loss and recovered-body
  upgrade retention all passed.
- `matrix plank`: PASS with host and separate client. Jump, timeout push, gates,
  the lower water surface and fresh-run reset passed.
- `matrix loop`: PASS with host, guest and a second late joiner. Boarding refusal,
  fixed-bridge cargo exclusion, replicated gates, sailing, return, cargo,
  disconnect during departure and re-host passed.

These were local Tugboat/loopback runs, without injected latency or packet loss.
They do not prove Steam transport on two computers or four-player performance.
Teammate review of `ShipParts`, `DockBoardingGate` and the contract change remains
required before merging. No human approval is claimed.
