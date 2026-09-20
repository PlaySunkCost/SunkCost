# The name over the head — 20 September 2026 — spectate, cabin, fullrun MATRIX_PASS; the two-diver photos

Dan's card: "a way to know who is who — a name tag on him, it should look
good", after his reference picture (plain floating text, no plate). The
display name over every player's head in soft white with a dark drop shadow,
no colour; facing the viewer, the same size on screen to 14 m, faded out over
the next 4 m; hidden for its owner, on the dead and when the viewer's eyes are
at that head. `PlayerNamePlate` (runtime) + `PlayerNamePlateSetup` (builds it
onto the player prefab). The visor's crew readout keeps only the distance.

Host: the editor; guest: the local build of the same revision.

| Job | Result | Log |
|---|---|---|
| photo (new, `TwoDiversPhoto`) | MATRIX_PASS, 8 rows — both divers on the seafloor, each captured from the other's eyes | `photo.log`, `two-divers-player1-sees-player2.png`, `two-divers-player2-sees-player1.png` |
| spectate | MATRIX_PASS, 224 rows (the crew-tag rows) | `spectate-matrix.log` |
| cabin | MATRIX_PASS, 288 rows | `deck-cabin-matrix.log` |
| fullrun | MATRIX_PASS, 231 rows | `full-run.log` |

Found on the way: the full-run's second pay computed the cycle's hand-over as
balance + box, which is only right when cycle 1 was SHORT and banked; the
coins' rolled values reached $505 this time, cycle 1 paid outright, and the
row failed on its own arithmetic. The row now reads the cycle's hand-over.
The guest build takes a `capture` command (its own screen to a file).
