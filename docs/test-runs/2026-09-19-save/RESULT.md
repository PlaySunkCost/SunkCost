# The save — 19 September 2026 — save MATRIX_PASS, loop/shop/plank/fullrun MATRIX_PASS

Dan's card: three named save slots per player (Save 1, 2, 3), picked on the
Host screen; written automatically whenever the crew is at HQ; a used slot
continues, renames or is deleted; guests get their upgrades and hands back by
identity. `RunSave.cs`, `WorldSceneFlow.Save.cs`, the day state's capture and
restore, the inventory's carried/restore, the identity hook, the menu.

Host: the editor at `e518109`; guest: the local build of the same revision.

| Job | Result | Log |
|---|---|---|
| save (new) | MATRIX_PASS, 57 rows: no slot keeps nothing; a new named run; a buy writes the balance and the host's upgrade under `name:Skipper`; a tank in the box and one in the hands cross the cast-off; the docking writes; leave + re-host brings back the run (day 1), the box tank in the room, the upgrade and the hand tank, exactly two tanks; rename/delete; a guest's headlamp back by name after a re-host | `save-matrix.log` |
| loop | MATRIX_PASS, 62 rows (regression: the sail and cast-off paths) | `world-loop-matrix.log` |
| shop | MATRIX_PASS, 90 rows (regression: the buy path) | `shop-matrix.log` |
| plank | MATRIX_PASS, 60 rows (regression: the run's end) | `plank-matrix.log` |
| fullrun | MATRIX_PASS, 231 rows | `full-run.log` |

Found on the way: the first S5 row expected day 0 after a sail out and home;
the day is 1 (a cycle begun, nothing dived) — the row was wrong, not the game.
One wasted shop run: docs committed after the guest build ("Wrong build" again;
the rule holds — commit, then build the guest, then run).

Not done: a Steam host + guest session (identity by Steam id is the same code
path with a different key; the LAN path is what the matrix exercises).
