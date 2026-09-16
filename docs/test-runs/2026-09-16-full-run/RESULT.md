# Full-run playthrough — 16 September 2026 — MATRIX_PASS (229 rows)

The script: [docs/FULL_RUN_PLAYTHROUGH.md](../../FULL_RUN_PLAYTHROUGH.md).
The job: `FullRunRuntimeChecks` (`CameraClearanceMatrixDriver.Start("fullrun")`),
played as the host in the editor, Local transport, one player, quota **$500**.
Branch `dan/payday` (PRs #43 + #44), commit after `f069760`. Full log:
[full-run.log](full-run.log); screenshots in [shots/](shots/).

## What happened (the run's own numbers)

| Step | Seen |
|---|---|
| HQ | board `NEW CYCLE · box $0 / quota $500 · balance $0 · dive first`; monitor `Docked at HQ — E on Site 01 to sail` |
| Colour | panel prompt `Press E to pick your colour`; sky (8) written by the server, worn by the body, saved |
| Sail out | `Day 1 of 3 — Site 01 …`; cabin panel `Day 1 of 3 — all in, press E to descend`; box `STORAGE $0 / $500 balance $0`; End day refused `Nobody has dived today` |
| Dive 1 | visor `DAY 1/3`, `BOX $0/$500 · ON ME $0`; Coins 7, 3, 5, 6 grabbed, `ON ME` climbing each time; haul **$454**; up: `dive done — E on END DAY`; second dive refused `Dive done — end the day at the monitor`; four coins dropped in the room → box **$454** (readout, server value); End day → `Day 2 of 3` |
| Early home | docked, still day 2, `Docked at HQ — day 2 of 3`; the four coins crossed to HQ inside the room; board `DAY 2 OF 3 …`; E → **`SHORT BY $46 · sold $454 · balance $454 · sail out and dive again`**; coins gone, box $0, the ship may sail |
| Dive 2 | day 2; haul **$473**; Coin 7 ($228) dropped on the open deck, three in the room → box counts three only; End day → `Day 3 of 3` |
| Dive 3 | haul **$435**; all four into the room → box **$635** (`STORAGE $635 / $500 balance $454`); End day → **`PAYDAY — E on HQ to sail home`**; cabin refused `Payday — sail home`; Site 01 refused `Payday — only HQ` |
| Home | `Docked at HQ — PAYDAY: pay the quota at the board`; Site 01 refused `Pay the quota first`; the deck coin came home on the deck, outside the room; board `PAYDAY …`; E → **`PAID $500 · sold $635 · balance $589 · next dive is day 1`**; box $0; the deck coin not sold; End day at the dock refused `Not at sea`; paying again refused `Nothing to pay yet — dive first` |
| Cycle 2 | `Day 1` again; the deck coin sailed out again on the deck; one dive, haul $410, all into the room; End day → `Day 2 of 3`; End day again refused; home; E → **`PAID $500 · sold $410 · balance $499`** (paid early from the balance); the deck coin survived two sails and two sales |

Screenshots: `01-hq-board` (NEW CYCLE), `02-colour-picked`, `03-dive1-seafloor`
(visor, `DAY 1/3`, `BOX $0/$500 · ON ME $0`), `04-dive1-loaded`, `05-box-after-dive1`,
`06/07-board` (SHORT), `08-box-full` ($635), `09-payday-monitor`, `10/11-board`
(PAID $500), `12/13-board` (cycle 2 PAID), `14-end`.

## Found on the way (six runs to a clean one)
1. **Coins roll.** A dropped coin is a cylinder; on the deck it can roll out of
   the room's doorway after it lands. The script waits for them to settle and
   puts a runaway back (none rolled in the passing run). A player will see it
   roll — it is real behaviour, not a bug; a low sill in the doorway would stop it.
2. **Same names every dive.** The site re-spawns `Coin 1..7` each day, so the
   ship's room can hold a `Coin 6` while a fresh `Coin 6` lies on the seafloor.
   The script once grabbed the ship's one from inside the dive (3 km away in the
   same physics world) — which shows the **server's grab has no scene or range
   check**. Harmless for an honest client (unreachable), but a guard belongs in
   `CarryableItem.ServerGrab` (same scene, within reach). Follow-up.
3. A grab with a coin already in hand goes straight into a slot (stowed), not
   the hands — correct, the script had assumed otherwise.
4. From inside the HQ room, End day answers `Not aboard: Player 0` (the press
   must come from the ship) — correct; the script now presses from the deck.
5. The board's text is slightly wider than its plate (cosmetic).

## Not covered here
- Two machines over Steam (the Notion card).
- GAME LOST at payday (the cabin matrix's Z5 row covers it; the balance carried
  over here, so a real loss needs a crew that sells nothing for three dives).
