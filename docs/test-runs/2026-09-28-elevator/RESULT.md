# The round elevator: final regression, 28–29 September 2026

- **Branch:** `dan/elevator-look`, local only and not pushed.
- **Editor:** Unity **6000.6.0f1**.
- **Build under test:** HEAD **f650ce7**, "Ship layout, Dan's plan 4", which sits on the elevator work from `5e5fce6..79558aa`.
- **Host and guests:** the editor was the host. Separate Windows guest builds came from the same revision (`test-build.json`: revision f650ce7f…, protocol 3, `localOnly` false).
- **Transport:** local Tugboat over loopback on one machine, with no injected latency, so **latency: n/a (local)**. This is **not** a two-computer Steam test.
- **Scope:** no audio was authored. No human approval is claimed.
- **Contract:** the rows stay **PROPOSED** until a teammate reviews them.

## Final status: **FAIL**

Every BRIEF behaviour is built. Every elevator job and every job that rides the elevator passes at f650ce7, and this run found no regression. The status is still FAIL for two reasons:

1. **The visuals are not finished at the tube foot.**
   - Up close, the torn, faceted grey strips at the foot's left jamb are still there (`Logs/elevator-dive/dv10-gate-leaf-left.png`, this run).
   - The plain grey plinth blocks either side of the foot's doorway (`dv2-foot-bottom-stop.png`) still read as unfinished.
   - This is **DIVE-GATE-CLOSEUP-SHARDS / P2-5**. It needs a models re-prep of the Tube Foot in `tools/blender/elevator_parts.py`.
   - BRIEF: "If it looks placeholder/debug, the final status is FAIL."
2. **One matrix still fails: `dash` D8.**
   - It failed twice, with identical output, so it is deterministic.
   - It is **not** caused by this branch (see below), but the matrix is not clean.

## Agents (AI, two workflow runs on 28 September 2026)

- **Investigate:** architect (the map and the interfaces), test-scout.
- **Build:**
  - models: the Blender prep of all eight models, `ShipModelSetup`, the glass materials;
  - deck: the car in the ship's well, the housing and shutters, the panel;
  - dive: the moving car, the stacked tube, the collar, the foot and gate in DiveSite01;
  - water: the one water truth, jets, foam, bubbles, drain, gauge and per-camera sorting.
- **Reviews:** code-review and netcode-review (read-only), then review-fixer.
- **Test rounds 1–3:**
  - testers: deck-tester, dive-tester, net-tester;
  - fixers between the rounds: models-fix, deck-fix, water-fix, dive-fix.
- **Pause:** STOP was raised at 95 % weekly usage. Dan paused the build at 18:24, and it resumed at about 20:00.
- **After the resume:**
  - dive-tester-finish;
  - ui-polish-plan and ui-polish;
  - polish-retest;
  - polish-fix;
  - polish-retest-2 (two copies ran);
  - the ship-layout agent (Dan's plan 4);
  - this final regression.
- **Independence:** testers never tested their own code, and the original tester role retested each fix.
- **Shared editor:** the editor was shared under a lock, and every job ran on a clean tree.

## Final regression (28 Sep 23:12 to 29 Sep 00:26, HEAD f650ce7, guest f650ce7, tree clean)

| Job | Verdict | Rows | Log |
|---|---|---|---|
| `fullrun` | **MATRIX_PASS** on the 2nd run. The 1st run failed on a loot-dependent row that predates this branch (see below). | 231 | `fullrun-matrix.txt`, `fullrun-run1-FAIL.txt` |
| `loop` | **MATRIX_PASS** | 67 | `loop-matrix.txt` |
| `cabin` | **MATRIX_PASS** | 288 | `cabin-matrix.txt` |
| `save` | **MATRIX_PASS** | 57 | `save-matrix.txt` |
| `spectate` | **MATRIX_PASS** | 239 | `spectate-matrix.txt` |
| `monsters` | **MATRIX_PASS** | 175 | `monsters-matrix.txt` |
| `air` | **MATRIX_PASS** | 86 | `air-matrix.txt` |
| `noise` | **MATRIX_PASS** | 69 | `noise-matrix.txt` |
| `dash` | **FAIL** at D8, twice with identical output. It predates this branch (see below). | 74 before the failure | `dash-run1-FAIL.txt` |
| `shop` | **MATRIX_PASS** | 92 | `shop-matrix.txt` |
| `plank` | **MATRIX_PASS** | 85 | `plank-matrix.txt` |
| `hq` | **MATRIX_PASS**. The editor had focus this time, so the shop-catalogue focus rule did not trip. | 29 | `hq-matrix.txt` |
| `console-ship` (the console at its new bow place) | **MATRIX_PASS** | 306 | `console-ship-matrix.txt` |
| `console-hq` | **MATRIX_PASS** | 278 | `console-hq-matrix.txt` |
| `console-net` | **MATRIX_PASS** | 296 | `console-net-matrix.txt` |
| `elevator-deck` | **MATRIX_PASS** | 302 | `elevator-deck-matrix.txt` |
| `elevator-dive` | **MATRIX_PASS** | 369 | `elevator-dive-matrix.txt` |
| `elevator-net` | **MATRIX_PASS** on the 2nd run. The 1st run failed on one soft tick-skew row (see below). | 478 | `elevator-net-matrix.txt`, `elevator-net-run1-SOFTFAIL.txt` |

### The failures, and why they are not regressions

**`dash` D8 predates this branch.** The failing line was:

`FAIL: D8 the Listener heard the coin land: Listener pose=Hunting ... at=(15.5, -45.0, 14.8) heard=4 last=Dash ... fired=1 hits=1 phase=Done`

- **What happened:**
  - In D7 the Listener's beam hit the host (`hits=1`), and the Listener went Hunting.
  - It stood about 11 m from the coin, which was inspected live at (8.3, -45.0, 22.9).
  - The landing carried 6.6 m, so the Listener could not hear it.
- **The last passing run** was `docs/test-runs/2026-09-21-dash`:
  - there, D7 had `hits=0`;
  - the Listener was Drawn at (10.1, 19.5), within reach, when the coin landed.
- **What changed since then:**
  - The Listener's beam changed on 22 September (91ae10c: charge, then a turning beam with one hit per beam).
  - The monster polish landed on 24 September.
  - The `dash` job was not run again until now.
- **This branch changes nothing** in `Scripts/Monsters`, `Scripts/Noise` or `DashRuntimeChecks.cs` (`git diff 5e5fce6..HEAD`).
- **Needed:** the dash/monsters owner should re-place the D8 Listener, or reset it after D7.

**`fullrun`, 1st run: a loot-dependent row that predates this branch.** The failing line was:

`FAIL: PAID: sold $636, handed over $1145 this cycle`

- **What happened:**
  - Dive 1's box happened to reach $509, so the early trip home PAID and started a fresh cycle.
  - The row computes "had" as the balance before plus the box, which assumes the early trip was SHORT.
  - The game reported `had $636`, the new cycle alone, which is the rule from d32a921.
- **The 2nd run** had a $486 early box, so the early trip was SHORT, and the job passed 231/231.
- **This branch touches no pay code.**

**`elevator-net`, 1st run: the soft tick-skew row.** The failing line was:

`SOFT-FAIL ... E7 down 4p: A's tick within 3 ticks ... worst 3.44 ... at AtTop↑ ... dropped-tick allowance 0.00`

- Every state comparison had 0 differences: the car, the water, the gauge, the doors, the gate, the deck doors, the spectator and the TV.
- The 2nd run passed 478/478, with a worst skew of 2.88.
- This is the residue of NET-TICK-SKEW-SOFT, a clock measure on one machine running five processes.
- The TV eye read 0.35 m again, right at `TolEye` (NET-TV-EYE-EDGE, still open).

## Sweeps

- **Unity console:** 0 errors (bridge).
- **Editor.log, from the start of the run:**
  - 0 `error CS`, 0 game exceptions;
  - the only `SetException` frames are the Unity AI assistant's model-list call;
  - the 4 LogErrors are the four FAIL dumps above.
- **Warnings:**
  - the known `Box_600x1x600` sea-mesh triangle warning;
  - the intended admission refusals ("Dive in progress", "Ship travelling");
  - the intended kicks ("never arrived in the deck cabin", "no black acknowledgement");
  - three `[ConsoleScreen] font atlas rebuilt` diagnostics, a known note from the console work.
- **Guest `player.log`s:**
  - 29 of the 31 guest directories written by this run have **0** hits for `expected to exist|already found|Exception|MissingReference|NullReference`.
  - elevator-net's headless B2 and C2 have 15 and 18 `NullReferenceException`s from URP's `ColorGradingLutPass`. These are the headless no-GPU renders that E9 excludes by design (NET-HEADLESS-WARMUP's ShipTV part, which predates this branch).
  - E9 passed for all five guests.
- **Validators (edit mode):**
  - `HQGeneratedSetup.Validate()`: "HQ validation passed: four clear spawns, level continuous bridge, two gates, shop, court and plank";
  - `ShipAtSeaValidator`: ok;
  - `DiveSiteValidator`: ok;
  - `SessionSceneValidator`: ok;
  - **`ShipShellAudit.Run()`** in ShipAtSea: "1806 standing places, **0** surfaces the camera can enter".
- **Git and meta files:**
  - The tree was clean apart from Dan's two root documents before and after every job, with no churn to revert.
  - Every file under `Assets/` added on this branch has its `.meta`, and so does every new folder.
  - Outside `Assets/_Project/` and `docs/`, the branch changed only `tools/blender/elevator_parts.py` and `tools/blender/prepare_ship_part.py`.

## Captures read in this run

- **`w-jets-overview.png`:** thick aerated jets from the nozzle mouths, with mist where they land; the screen reads FLOODING, 17 %. The roof lens still shows a bright disc with a ragged rim.
- **`va-bottom-out-through-gate.png`:** the sill, the ramp and the lit sand through the open gate.
- **`dv10-gate-leaf-left.png`:** the torn jamb shards (open).
- **`dv2-foot-bottom-stop.png`:** the foot, the ramp, and the grey plinth blocks.
- **`Temp/elevator-deck/va-deck-open.png`:** the housing with the car flush and the panel lit.
- **`Temp/elevator-net/E4-tv-full-car.png`:** the TV on the diver in the full car, with the panel.

The polish pass's full set of 47 captures, with before/after pixel counts, is in the ui-polish report (`docs/ELEVATOR_LOOK.md`, "Polish pass").

## Bugs found, fixed and retested

| Bug | Severity | Fix | Independent retest |
|---|---|---|---|
| DIVE-BUBBLES (sticker strips that never stop; Dan) | High | 9354e75: particle bubbles only where air enters | dive-tester round 2: FIXED. This run: `W-B6 ... nothing bubbles in a still, full car` |
| DIVE-STREAMS (thin flat cards; Dan) | High | 9354e75, f95de2a: powerful 3D jets, aerated, with mist | round 2: FIXED. This run: W-J rows pass |
| DIVE-GLARE (blown roof ball, slab streak) | Medium | 3708d59, 4fee69d, 59022f1, f95de2a | glass: FIXED (round 3). Roof lens: improved (2.0 % of pixels ≥ 200), but a ragged rim remains (a look call; see below) |
| DIVE-GATE-VIEW (flat dark panel through the gate) | Medium | 04fd16c | round 2: FIXED |
| DIVE-SLAB-EDGE (sawtooth post edge) | Low | a2c0b37 | round 3: FIXED |
| DECK-N1-CANCEL (the put-out fired on a cancelled ride) | Medium | e8968ee | deck-tester round 2: RT-N1 PASS |
| DECK-LEAF-GLASS (milky parked leaves) | Low | ba866e5 | RT-LG PASS |
| DECK-PANEL-TEXT (the screen too small) | Low | 9354e75 | RT-PT PASS |
| DECK-CANCEL-SNAP (the doors snap open on a cancel) | Low | 5bc2f98 | deck-tester round 3: RT-CS PASS (largest step 0.007 against the 0.040 allowed) |
| NET-CHURN-ADMISSION (a guest build dirtied the tree, and guests refused the host) | Low | b2ed37a, 59022f1, f413aac | net-tester round 2 and later: no churn after any guest build |
| NET-HEADLESS-WARMUP | Low | 9354e75 (DiveSiteWarmup) | E9: 0 DiveSiteWarmup lines. The ShipTV part is open and predates this branch |
| NET-CARSCREEN-WORD-EDGE, NET-TICK-SKEW-SOFT, NET-E5-HOST-LEFT-CAR (test tolerance and environment) | Low | c4b9a40, 79558aa | polish-retest-2: 3 of 4 net runs pass. This run: 1 of 2 |
| DIVE-DV10-GATE-ROWS (the gate rows never reached the leaves) | test | f704e3b | DV10 reaches the leaves (33.6 cm) |
| DIVE-G1-GUEST-HITCHES | Low | none: not reproduced (GPU sharing) | cabin G1 passed in every run since, including this one |
| **DIVE-GATE-CLOSEUP-SHARDS** | Low (look) | **open**: a Tube Foot re-prep | still visible in this run |
| **NET-TV-EYE-EDGE** | Low (test margin) | **open**: widen `TolEye` or compare with the eased target | 0.35 m again in this run, still passing |

## Deviations and why

- **No mid-ride join.** The contract refuses joins during a dive ("Dive in progress — join between days"). elevator-net E5 checks that refusal, and E7 checks joiners between days, whose first reply already shows the host's state.
- **The TV cannot show the car flooding.**
  - Nobody is on the ship during a ride down, so there is no TV viewer then (TESTPLAN X3).
  - E4 captures the TV on a diver in the full car at the bottom and while it drains instead.
- **The jets fall straight.** The car model's nozzle mouths point down, so each jet leaves along its outlet's axis (W-J3: 1-2° off the outflow). There is no visible arc out of the bend.
- **Fill timing.** The car is full 3.48 s after the roof goes under (DV5), within the brief's "about 4 s", which is today's timing.

## Remaining

1. **P2-5 / DIVE-GATE-CLOSEUP-SHARDS:** the Tube Foot's jamb shards and the plain plinth blocks. Re-prep the foot's model.
2. **`dash` D8:** re-place or reset the Listener after D7 (dash/monsters owner).
3. **NET-TV-EYE-EDGE:** the TV-eye tolerance has no headroom.
4. **Optional:**
   - the roof-lens rim, which is still ragged in `w-jets-overview.png` and at HQ at night (a roof-lens disc would hide it);
   - P3-7/8/9: glass lines, post smears, the threshold lamp bar.
5. **For Dan:**
   - a two-computer Steam test;
   - a teammate's review of the PROPOSED contract rows;
   - the Meshy plan and its terms for the eight models (docs/ELEVATOR_LOOK.md §6).
