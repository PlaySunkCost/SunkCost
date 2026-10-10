# HQ consoles — the crew colour console and the HQ cleanup (dan/hq-consoles, 8–11 October 2026)

**Status: PASS**, with one known pre-existing failure (`dash` D8, red since 22 September 2026) and one flaky job
(`console-net`, green on its fourth run). Every behaviour Dan asked for is built and checked by a matrix row from
replicated state, host and guest(s), and the visuals were judged from captures.

## What was built

Mission A, the crew colour console (Dan, 8 October 2026; his picture `docs/reference/hq/colour-console-concept.webp`):
- The navigation console's rusty frame stands where the colour panel stood at the arrival pad, facing the pad.
- Top screen: `CHOOSE YOUR CREW COLOUR`, the sixteen palette swatches as glowing discs on a ring, the shown one
  ringed, a diver's helmet in the middle drawn in the shown colour. The swatch under the aiming dot gets a light ring
  of its own size (Dan, 11 October 2026: "a circle, not a rectangle"; the swatches carry no square touch quad).
- The shown colour is shared: one server-held swatch, anyone's press changes it, everyone sees it, last press wins.
- The lever is personal: whoever pulls it gets the shown colour (server-written, saved on the puller's machine), the
  lever swings for everyone, and the lower screen reads `<NAME> IS NOW <COLOUR>` for about 3 s.
- The old panel, its mast, sign and IMGUI picker are gone from the scene.

Mission B, the HQ cleanup (Dan's notes, 29 September–8 October 2026):
- The shop console is the shop. The whole catalogue is on its screens (three rows a page, `BUY` / `OWNED` /
  `NO MONEY`, the crew pot, `PAGE n/m` with PREV/NEXT). Aiming at a row and pressing E buys it through the same server
  path and refusals as before. The page is shared, and the lever is NEXT PAGE.
- The hanging sign, the four item stands and the HUD browser are out of the flow. The OFFICE / UPGRADES /
  GEAR & SUPPLIES boards are removed.
- The blank brown board (a half-size TV cabinet floating 1 m up, facing the window) is removed. The office sofa is
  turned to face the room.
- Props are settled on real surfaces: the two toolboxes that floated between shelves, the cargo toolbox 16 cm in the
  air, the tank cradles cutting into the shelves, the stock sunk 3 cm into the upper shelves, and the rack panels,
  hooks and office lockers standing off the wall (HQ-DEPOT-WALL-GAPS, found by the hq audit row and fixed).
- The silent spots. HQ had no sound of its own: the only ambience was the docked ship's, which hard-stopped 70 m
  from the ship, so the whole east side was silent. HQ now has its own bed (`HQAmbience`: a 2D wind plus sea washes
  along the platform's edges), and the ship's ambience yields to it at HQ. The probe grid finds 0 dead points of 510
  walkable (`hq` HQ6), and the bed is silent at sea (HQ8).

## How it was built

Planned by a planner + auditor + plan-fix (MAP / INTERFACES / TESTPLAN). The first two runs lost their build agents:
in run 1 every agent answered Dan's relayed "expected time?" instead of working, and run 2 hit the session limit
after drafting. Run 3 was serial, one agent at a time on the editor: props-impl, colour-impl, shop-impl, then the
colour, shop and hq test writers (each landed its job and shook it out), fixer-1, an independent retest, ui-polish,
and a polish retest. The final regression ran as two chains (9 October, then 11 October after Dan's hover change)
with the docs patch the final agent drafted.

## Matrices (logs in `regression/`)

The 9 Oct chain ran at 29220c7. The 11 Oct chain ran at 69531d8, after the hover ring (bf07c44, the colour console's
paint and the scene's swatch controls only) and the fullrun wait fix.

| job | verdict | PASS rows | ran at |
|---|---|---|---|
| console-colour | MATRIX_PASS | 135 | 69531d8 (and 29220c7) |
| console-shop | MATRIX_PASS | 173 | 29220c7 |
| shop (rewritten for the console) | MATRIX_PASS | 102 | 29220c7 |
| hq (with the HQ0–HQ8 cleanup rows) | MATRIX_PASS | 444 | 69531d8 |
| plank | MATRIX_PASS | 85 | 29220c7 |
| fullrun | MATRIX_PASS | 234 | 69531d8 (FAIL at 29220c7: "the pick is saved", a test race, fixed in 69531d8) |
| loop | MATRIX_PASS | 67 | 29220c7 |
| cabin | MATRIX_PASS | 288 | 29220c7 |
| save | MATRIX_PASS | 57 | 29220c7 |
| spectate | MATRIX_PASS | 239 | 29220c7 |
| monsters | MATRIX_PASS | 305 | 29220c7 |
| monsters-round | MATRIX_PASS | 134 | 29220c7 |
| air | MATRIX_PASS | 86 | 29220c7 |
| noise | MATRIX_PASS | 69 | 69531d8 (at 29220c7 the guest stopped answering after the last N6 row; not reproduced) |
| dash | FAIL: D8 (known, pre-existing since 22 Sep 2026) | 74 | 29220c7 |
| console-ship | MATRIX_PASS | 306 | 69531d8 |
| console-hq | MATRIX_PASS | 278 | 69531d8 |
| console-net | MATRIX_PASS on the 4th run | 296 | 69531d8 (runs 1 and 3: N11b "a legal order… rerun", the row's own same-frame race; run 2: one convergence at 1.28 s against 1.0 s) |
| elevator-deck | MATRIX_PASS | 302 | 69531d8 |
| elevator-dive | MATRIX_PASS | 369 | 69531d8 |
| elevator-net | MATRIX_PASS | 478 | 69531d8 |
| wipe | MATRIX_PASS | 156 | 69531d8 |
| figure | MATRIX_PASS | 17 | 69531d8 |
| smooth | MATRIX_PASS | 10 | 69531d8 |
| hands (P rows moved to the console, M8 re-placed) | MATRIX_PASS | 44 | 69531d8 |
| camera | MATRIX_PASS | 28 | 69531d8 |

The edit-mode suites were run through the command file at 69531d8: `HQColourConsoleTests` 9/9,
`ShopConsoleRulesTests` 14/14, `HQQuotaConsoleTests` 12/12, `ConsoleRulesTests` 21/21.

## Captures

`captures/` holds the colour console's states (idle, shown, taken, the suit, the refusal, side views), the shop
console's (page 1, page 2, aimed row, no money, owned, after a buy, refusal, side, the counter from the aisle), and
the cleanup (office, sofa, racks, the board's spot, the cargo toolbox, the depot front). The `hq-consoles-final-*`
set is the polish pass's. The hover ring was checked on 11 October from the player's camera aiming at yellow:
a ring on the disc, no rectangle.

## Deviations and decisions

- The shop lever is NEXT PAGE (it wraps, and is dim on a one-page catalogue), and the page holds three rows, so
  paging is live on today's four items.
- The `hq` job no longer asserts the colour panel's removal; console-colour's K0 row does.
- `hands` M8 stood at the old hall's south wall, which the platform HQ does not have (debt since 18 Sep). It was
  re-placed against the depot's east wall; the rule under test is unchanged.
- `ColourPanel`, `ShopDisplay` and `ShopBrowserUI` stay as dead classes: nothing places or opens them, but checks and
  the validator still name them (ColourPanel 5 files, ShopDisplay 9, ShopBrowserUI 4). Deleting them is a follow-up.

## Not covered

- No Steam two-machine session. Multiplayer was checked with local guest builds only.
- Network rows are PROPOSED in NETWORK_CONTRACT.md and need a teammate's review: the shown swatch and the shop page
  SyncVars, the press / take / page ServerRpcs.
- `console-net`'s N11b row races by design; it failed twice of four on this machine.
