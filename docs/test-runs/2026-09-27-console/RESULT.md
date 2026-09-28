# The shared console — final regression, 27–28 September 2026

Branch `dan/hq-polish`, local only (not pushed). Unity **6000.6.0f1**. The build under test was HEAD
**3231de8** ("Console polish, pass 2"). The editor was the host, and separate Windows guest builds came
from the same revision (`test-build.json`: revision 3231de8e…, protocol 3, built 03:56Z). The transport
was local Tugboat over loopback on one machine with no injected latency, so **latency: n/a (local)**.
This is **not** a two-computer Steam test. No audio was opened, reviewed or changed. No human approval
is claimed.

## Roles

The work was done by AI agents in two workflow runs:
- Investigate: architect, model-inspector, render-scout, test-scout.
- Foundation: console-model, console-state, netcode review, code review, foundation fixer.
- Implement: ship-console, hq-console.
- Test, rounds 1–4: ship tester, HQ tester, network tester, with console-state, HQ and ship fixers
  between the rounds.
- UI polish, then a polish retest.
- This final regression.

The editor was shared under a lock. Commits run from b6cd0f2 to 3231de8. Contract rows stay
**PROPOSED** and need a teammate's review.

## Final regression (28 Sep, 07:14–07:58, HEAD 3231de8, guest build 3231de8, tree clean)

| Job | Verdict | Rows | Log |
|---|---|---|---|
| `fullrun` | **MATRIX_PASS** | 231 | `fullrun-matrix.txt` |
| `loop` | **MATRIX_PASS** | 67 | `loop-matrix.txt` |
| `cabin` | **MATRIX_PASS** (2nd attempt, see below) | 288 | `cabin-matrix.txt` |
| `save` | **MATRIX_PASS** | 57 | `save-matrix.txt` |
| `spectate` | **MATRIX_PASS** | 239 | `spectate-matrix.txt` |
| `shop` | **MATRIX_PASS** | 92 | `shop-matrix.txt` |
| `plank` | **MATRIX_PASS** | 85 | `plank-matrix.txt` |
| `hq` | **FAIL** at row 20, twice, because the editor did not have focus (not a regression, see below) | 19 before the failure | `hq-matrix-FAIL-unfocused.txt` |
| `console-ship` | **MATRIX_PASS** | 306 | `console-ship-matrix.txt` |
| `console-hq` | **MATRIX_PASS** | 278 | `console-hq-matrix.txt` |
| `console-net` (2/3/4 players + a refused 5th) | **MATRIX_PASS**, `PASS no soft failure (0: )` | 296 | `console-net-matrix.txt` |

`air` and `noise` were not run, because no console change touches them.

### The two failed attempts

- **`cabin`, attempt 1** (`cabin-matrix-attempt1-FAIL-dirty-tree.txt`): the job failed with
  `FAIL: timeout: guest player spawned`, and the cause was the tester. During the run I edited tracked
  docs. A dirty tree makes the editor's build identity `local-dev:<HEAD>`, so the guest was refused at
  admission (`WaitForAdmission` → `Fail`, player.log). I set the doc edits aside, reverted them and ran
  the job again from `cabin`. It passed with 288 rows. The playbook now warns about this.
- **`hq`** failed both times with `FAIL: System.Exception: E opens the catalogue and captures gameplay
  input`. While the job ran, the Claude desktop app held the OS foreground and the editor showed
  `Application.isFocused = False`. `ShopBrowserUI.Update` closes the browser whenever
  `!SessionInputGate.ApplicationFocused`. That rule dates from 1464477, before the console work.
  - The failing row is the shop catalogue's. The last passing `hq` run was at 06:17 on c39cdd9 (29 rows).
    Since then the only changes are the console polish files (`ConsoleBuilder`, `ConsolePaint`,
    `ConsoleScreen`, `ConsoleGlass.mat`, `ConsoleLookCapture`) and a test file. None touches the shop,
    the input gate or the player controller.
  - Live state at the failure: the player aimed at the counter, the prompt read "Equipment catalogue —
    Press E to browse and buy", and `ShopOpen = False`.
  - I could not bring the editor to the front from this session. **Needed: rerun `matrix hq` with the
    Unity editor as the focused window.**

## Sweeps

- **Unity console and Editor.log** (from the start of `fullrun` on): 0 exceptions, 0 NullReference,
  0 MissingReference, 0 `error CS`.
  - The one error line is the cabin attempt 1 timeout dump.
  - Warnings:
    - the known `Box_600x1x600` sea-mesh triangle warning;
    - the intended admission refusals in the `loop` and `console-net` N8b rows;
    - the intended missing-acknowledgement kick in `console-net` N12;
    - two `[ConsoleScreen] the font atlas was rebuilt 6 times in 5 s` diagnostics. This is a known
      polish note from 30de3e4 and is not a failure.
- **Guest player.logs:** all 20 guest directories written by this run (console-hq ×4, console-net ×7,
  console-ship, cabin, plank, save, shop, spectate ×2, loop ×2) have **0** hits for
  `expected to exist|already found|Exception|MissingReference|NullReference`.
- **Validators** (edit mode):
  - `HQGeneratedSetup.Validate()`: "HQ validation passed: four clear spawns, level continuous bridge, two
    gates, shop, court and plank". It calls `HQPrototypeValidator.ValidateOrThrow()`.
  - `ShipAtSeaValidator`: ok.
  - `ShipShellAudit.Run()` in ShipAtSea: "1749 standing places, **0** surfaces the camera can enter".
  - `SessionSceneValidator`: ok.
- **Git:** apart from Dan's two root documents the tree was clean before and after every job, with no
  churn to revert. No `.meta` file is missing: every console asset and new folder has one. The console
  work added nothing to `Assets/` outside `Assets/_Project/`. Outside `Assets/` it changed only docs and
  `tools/blender/prepare_ship_part.py`.

## The runtime check as a player (host alone, Play Mode, 28 Sep ~08:02)

Every step below was the real aim and E. A check keyboard was added with the same input settings the
matrices use, and the hooks placed the view.

1. **At the HQ console, aiming at the dim PAY lever** (`hqSign=PAY/off`, prompt "Nothing to pay yet —
   dive first"), I pressed E. The server refused it with `refusal='Nothing to pay yet — dive first'`.
   `lever=0` stayed unchanged, with no pull and no swing.
2. **On the docked ship, aiming at the SITE 01 card,** I pressed E, with the prompt "Press E to select
   SITE 01".
   - Result: `selected=Site01`, and the sign changed to `CONFIRM/on`.
   - Top screen: `[HQ@][SITE 01*~][SITE 02#][SITE 03#][SITE 04#]`.
   - Bottom screen: `SELECTED DESTINATION · SITE 01 … READY`.
3. **Aiming at the lever,** I pressed E, with the prompt "Press E to sail to SITE 01".
   - Result: `lever=1/Ship/Confirm`, the rig played serial 1, `phase=Sailing`, and the selection was
     cleared.
   - Bottom screen: `SAILING · SITE 01 … SAILING TO SITE 01`.
   - On arrival: `phase=AtSea day=1`, HERE on SITE 01, `DAY 1/3`, `SELECT A DESTINATION`.
4. **The dive.** I took the cabin down and the car up.
   - During the dive the bottom screen read `DIVE IN PROGRESS … 1 BELOW` with the sign `END DAY/off`.
   - Back up: `DIVE DONE — END THE DAY FIRST`, sign `END DAY/on`.
   - E on the lever (prompt "Press E to end the day") gave `day=2`, `lever=2/Ship/EndDay`, and the
     lever was seen mid-swing at 11.8°.
5. **E on the HQ card, then E on the lever** gave `lever=3/Ship/Confirm`, `phase=SailingHome`,
   `SAILING · HQ · SAIL HOME … SAILING HOME`.
   - Docked: `phase=AtHQ day=2`, with the HQ top screen at `DAY 2 OF 3`.
6. **E on PAY.** Nothing was hauled up, so the sign was `PAY/off` with "Nothing to sell — dive again".
   The server refused it with that reason, and there was no pay report (`pay=0`) and no pull.
7. **E on GIVE UP.** The prompt read "Press E to vote to give up the run — everyone must agree", and the
   card read `GIVE UP 0 / 1`. With one player the vote is unanimous, so the run ended:
   `phase=Plank`, top screen `THE RUN IS OVER · Skipper walks the plank`.
   - Take-back needs a second player, because one vote ends the run at once. It is covered with guests
     by `console-hq` (G rows) and `console-net`.
   - A PAY that actually sells was not played by hand, because it needs cargo carried into the storage
     room. It is covered by `console-hq` Q1/Q3 (PAID and SHORT with a real sale) and by `console-net` N14.

Afterwards I removed the check keyboard, restored the input settings and stopped Play Mode.

## Bugs found and fixed (with retest evidence from this run)

| Bug | Severity | Fix | Evidence in this run |
|---|---|---|---|
| SHIP-1: two HQ meshes left uncommitted | low | a61bc46 | the tree is clean and the guest builds from HEAD |
| SHIP-2: the ship lever read "Divers below" with nobody below while the car was away | low | 9a2c11b | console-ship: `PASS SHIP-2 END DAY lit with the car away and nobody below: END DAY/on` |
| HQ-1: GIVE UP on the plank was refused "Not docked at HQ" | low | 9a2c11b | console-hq: `PASS HQ-1 retest: Q7 the card's refusal on the plank reads 'The run is over'`; console-net: `PASS N15 HQ-1 retest: B2's vote on the plank is refused 'The run is over'` |
| HQ-2: a refusal inside the pay report's window did not reach the top screen | low | ef07fed | console-hq: `PASS HQ-2 retest: the top screen shows the refusal inside the PAID report's window` |
| HQ-3: the quota foot counted a sold box twice | low | ef07fed | console-hq: `PASS HQ-3 retest: the foot never counted the sold box twice from the pull on (every frame sampled)` |
| HQ-4: a direct ServerPay / RequestPay RPC over an empty room wrote a $0 report | low | b5e5ea3 | console-hq: `PASS HQ-4 retest (round 3): Q4 the RequestPay RPC writes no new pay report`; console-net: `PASS N14 HQ-4 retest: no new pay report (pay +0 …)` |
| NET-1: same-frame PAYs ran twice | medium | ef07fed (root: `StorageReadout.SumInside`) | console-net: `PASS N14 NET-1 retest: one pay report (1, sold $47), one pull (1), two refusals` |
| NET-2: the dim ship sign word differed per peer, and a late joiner disagreed | low | bd93189 | console-net: `PASS N16 NET-2 retest: late joiner C's first ship sign equals the host's ('CONFIRM/off' / 'CONFIRM/off')` |

The bug files and the testers' round reports live in the session scratchpad (`console/bugs`, `console/reports`).

## Visual acceptance

The UI polish pass is in commits 400ff2d and 3231de8, with its report in `console/reports/ui-polish.md`.

- **Dan's "blurry from the side":** the cause was the glass reflecting the sky. The same pixel at 60°
  went from (140,115,109) to (33,43,54) after the fix. The glass now has no environment reflections,
  the RenderTextures use aniso 16 with a mip bias of −0.5, and long reasons split onto two lines.
- **Captures:** every state the brief lists was photographed with the player's camera at 1920×1080,
  FOV 75, from the standing spot, in `Temp/look/console-final-*`.
- **Re-read in this run:** I opened `-ship-03-confirm-stand`, `-hq-08-voted-stand` and
  `-blur-ship-side60`.
  - The five cards are equal, SITE 01 is selected with a bright frame, and HERE is a chip on HQ.
  - Each lock shows an amber price.
  - The bottom screen reads SELECTED DESTINATION with the picture, the facts and READY.
  - CONFIRM is a lit plate.
  - The GIVE UP card is red and reads "1 / 2 · YOU VOTED · E TO TAKE BACK".
  - At 60° the screens stay dark and legible.
- **Verdict:** close to the reference and not default Unity UI. The gaps are listed below.

## Remaining issues

- `matrix hq` has to be rerun with the editor focused (above).
- There is no Steam run on two machines yet (see AGENTS.md "Multiplayer validation"). All guest rows
  here ran on one machine over loopback.
- The contract rows (the console, the amended sailing request, the give-up vote) are PROPOSED and
  await a teammate's review.
- Look gaps are left to Dan's call:
  - The cyan is ScreenStyle's paler accent.
  - The font is the built-in bold, because no licensed techno font is in the project.
  - Every card shares one placeholder picture.
  - The sea world's hazier grade greys the screens at sea.
- The `[ConsoleScreen]` font-atlas thrash diagnostic still fires about once per HQ-heavy job.
- Nothing yet photographs "unanimous vote" separately. It shows the same THE RUN IS OVER board as the
  run-over capture.

## Deviations

- The HQ console test used two guests (three players) instead of one, to test a join during a vote.
- The glass has no reflections, where RENDER.md proposed a faint reflection. Dan asked for readability
  at an angle.
- During this run: the cabin job was run twice (my doc edit dirtied the tree), and the `hq` job was
  left red because of focus (explained above).
