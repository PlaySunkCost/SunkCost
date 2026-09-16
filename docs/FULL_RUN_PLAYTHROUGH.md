# Full-run playthrough — what to do and what must happen

A scripted playthrough of the whole loop as of 16 September 2026 (PRs #43 and
#44): colour, sail, three dives with coins, the box, End day, payday, the pay
button, and a second cycle. The editor job `fullrun`
(`CameraClearanceMatrixDriver.Start("fullrun")` → `FullRunRuntimeChecks`) plays
it as the host with hooks for the walking; a person can play the same script by
hand from a CI build. Log: `Temp/full-run.log`; screenshots: `Logs/full-run/`.

Numbers this script relies on: quota **$500** (`WorldLoopSettings`); each dive
site holds **7 coins** — 3 small ($10–30), 3 medium ($40–90), 1 large
($150–300) — re-rolled every dive; a diver carries **4** things (four slots;
the held one is one of them); every dive is one trip down and one up.

## Setup
| Do | Must happen |
|---|---|
| Host a Local room from `Session.unity`. | HQ, the ship docked at the pier. Board on the south wall: `NEW CYCLE · box $0 / quota $500 · balance $0 · dive first`. Monitor: `Docked at HQ — E on Site 01 to sail`. |
| Walk to the colour panel, E, click **sky** (index 8). | The picker opens and closes; the body wears sky blue; F3 shows a sky square before the name. Saved for next time. |

## Cycle 1

### Sail out
| Do | Must happen |
|---|---|
| Walk across the bridge onto the deck, E on **Site 01**. | Gangway up, fade, the ship at sea. Monitor `Day 1 of 3 — Site 01 — E on HQ to sail home`. Cabin panel `Day 1 of 3 — all in, press E to descend`. Box readout `STORAGE $0 / $500 balance $0`. F3 `day=1`. |
| E on **End day** now. | Refused: `Nobody has dived today`. |

### Dive 1 — everything into the box
| Do | Must happen |
|---|---|
| Step into the deck cabin, E. | Doors close, suit fade, you stand in the car at the top, the car descends ~17 s, doors open at the bottom, water over the head. Visor on: `DAY 1/3`, `BOX $0/$500 · ON ME $0`. Monitor (if someone watched) `Day 1 of 3 — dive in progress — monitor locked`. Joins refused. |
| Walk the trail: grab Coin 7 (large), then Coins 3, 5, 6 (medium) — the first goes to the hands, the rest straight into slots. | Each grab adds its value: `ON ME` climbs to the sum of the four (about $270–$570). |
| Back into the car, E. | Car climbs, dry stop, you stand in the deck cabin, doors open, visor off. Nobody below → **dive done**: monitor `Day 1 of 3 — dive done — E on END DAY`; cabin panel `Dive done — end the day at the monitor`. |
| E on the cabin button again. | Refused: `Dive done — end the day at the monitor` (once a day). |
| Walk into the storage room (stern, starboard, doorway toward the middle), drop all four inside. | Box readout `STORAGE $<sum> / $500`; F3 `box=<sum>`. Every coin counted. |
| E on **End day**. | Monitor `Day 2 of 3 — Site 01 — …`. The site is unloaded (fresh next dive). |

### Early trip home — SHORT
| Do | Must happen |
|---|---|
| E on **HQ**. | Sail home; docked. Day stays **2** (sailing spends no day). Monitor `Docked at HQ — day 2 of 3 — E on Site 01 to sail`. Board `DAY 2 OF 3 · box $<sum> / quota $500 · balance $0 · E to pay early`. The coins are still in the room on the docked ship. |
| Look at the board, E. | If the box is under $500 (usual after one dive): `SHORT BY $<500−sum> · sold $<sum> · balance $<sum> · sail out and dive again`. The coins are gone (sold), box $0, balance = sum. Day still 2. If the haul happened to reach $500: `PAID $500 …`, day 0 — then this cycle ends here and the script notes it. |
| E on **Site 01**. | Sails out again. Monitor `Day 2 of 3 — Site 01 …`. |

### Dive 2 — one coin left outside the box
| Do | Must happen |
|---|---|
| Cabin, E; grab four coins; car, E. | As dive 1, visor `DAY 2/3`, `ON ME` the new sum. Dive done. |
| Drop three in the room, one on the open deck. | Box readout counts **three** — the deck coin is not in it. |
| E on **End day**. | `Day 3 of 3`. |

### Dive 3 — payday
| Do | Must happen |
|---|---|
| Cabin, E; grab four; car, E; all into the room. | Visor `DAY 3/3`. Box = the sum of seven coins. |
| E on **End day**. | Monitor `PAYDAY — E on HQ to sail home`. Visor (if diving) `PAYDAY`. |
| E on the cabin button. | Refused: `Payday — sail home`. |
| E on **Site 01**. | Refused: `Payday — only HQ`. |
| E on **HQ**. | Sails home. Monitor `Docked at HQ — PAYDAY: pay the quota at the board`. |
| E on **Site 01** at the dock. | Refused: `Pay the quota first`. |
| Board, E. | balance (from the SHORT sale) + box ≥ $500 is expected: `PAID $500 · sold $<box> · balance $<rest> · next dive is day 1`. Coins in the room gone; the deck coin still on the deck. Day 0. If it were short: `GAME LOST …`, balance $0, day 0 — the script records which. |
| E on **End day** from the docked ship's deck; board E again. | Refused: `Not at sea`; then `Nothing to pay yet — dive first`. (From inside the HQ room End day answers `Not aboard` — the press must come from the ship.) |

## Cycle 2 — pays from the balance
| Do | Must happen |
|---|---|
| E on **Site 01**. | `Day 1 of 3` again. The deck coin from cycle 1 rides along, still on the deck. |
| Dive once, all four coins into the room; End day; End day again. | `Day 2 of 3`, then refused: `Nobody has dived today`. |
| E on **HQ**, board, E. | Pay early on day 2: balance + box ≥ $500 → `PAID $500 …`, day 0. The deck coin is still on the deck (never sold). |

## Runs
- [2026-09-16 — MATRIX_PASS, 229 rows](test-runs/2026-09-16-full-run/RESULT.md) — the log, the numbers and the screenshots.

## Always
- F3 line matches the board and the monitor at every step (`day=`, `PAYDAY`, `balance=`, `box=`).
- No item ever disappears except by selling; the deck coin survives two sails and a sale.
- Nothing is charged by docking; only the board's E charges.
- After every End day the next dive finds seven fresh coins.
