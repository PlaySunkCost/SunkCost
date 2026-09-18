# Dan's playtest fixes, round 2 — 18 September 2026 — cabin, plank, spectate MATRIX_PASS; regression deferred

Nine items from Dan's second play of the plank/doors build, one branch
(`dan/playtest-fixes-2`).

1. Doors are passable while they move: the deck cabin's doorway blocks only
   when shut (like the car's); the server turns the doors around for anyone
   who crosses or stands in the doorway box (cabin R1b, R1c, R3b).
2. The day card at End day — `DAY 2 OF 3`, `PAYDAY` after day 3 — 0.5 s out,
   1.5 s held from the frame the screen is black, 0.5 s in (cabin Y3, Y8).
3. The visor and the board count the cycle: `QUOTA $handed+box/$500`
   (cabin M1, fullrun's visor line).
4. Doors never close on a body in the doorway: they hold open until it moves;
   the 20 s cap counts only while the doorway is clear (cabin R1c: the deck
   doors hold past two seal lengths; R3b: the car's doors hold at AtBottom).
5. The plank gate: a rail across the board's base, up while the crew walks the
   plank (plank P0 down, P1 up behind the jumper, P2 down again).
6. Dead hands: the arm rig is hidden with the body and back at revival
   (spectate D1 stricter — every renderer off, arms included — and D2).
7. The TV picture: the watched diver's own head is hidden for the TV's and the
   spectator camera's render only, shown again after (spectate S3/T).
8. Items reset with the run: every loose item in the loaded world scenes is
   despawned and each scene's loot fixture spawned again (plank P0 buys a tank,
   P2 finds no tank and the fixture's ball back).
9. Unstuck in the Escape menu: a pier spawn point at HQ, the boarding point on
   the ship, the car's floor below (plank U1: taken, then "Just did"; U2:
   "Walk the plank" to the jumper; cabin U3 on the deck, U4 below).

Host: the editor, guest build revision of this branch, Local transport. Logs alongside.

| Job | Rows | Result |
|---|---|---|
| cabin | 288 | MATRIX_PASS (`deck-cabin-matrix.log`) |
| plank | 55 | MATRIX_PASS (`plank-matrix.log`) |
| spectate | 224 | MATRIX_PASS (`spectate-matrix.log`, `spectate-tv-b.png`) |
| fullrun | 209 passed, then stopped | **not finished** — stopped at Dan's request (`full-run-partial.log`, no failure up to the stop) |
| loop, shop, air | — | **not run** — regression deferred at Dan's request |

## Matrix debt

Regression to run when the editor is free for ~30 minutes: `fullrun`, `loop`,
`shop`, `air` (nothing in them changed; they share the door, day-state and
item code touched here).

## Notes from the runs

- The first cabin runs failed Y3 on the day card because the row waited for
  the monitor's "Day 2 of 3" line first — the monitor keeps its End day
  report for a while and the two-second card was over by then. The row now
  reads the card right after the day count changes; the card itself (logged
  frame by frame once) went black at 0.5 s, held 1.5 s, faded in.
- One cabin run failed E1 (the down ride's mid-ride grab): the coin thrown in
  C2 landed against the car wall (2.12 m from the axis) and the dot's line of
  sight hit the wall segment. Pre-existing flakiness of where a thrown coin
  lands; the rerun passed. `deck-cabin-matrix-e1flake.log` kept in Temp only.
- P2's gate check ran in the frame of the phase change; `HQPlank.Update`
  drops the gate a frame later — the row waits up to a second now.
