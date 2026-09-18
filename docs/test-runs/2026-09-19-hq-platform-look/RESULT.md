# The HQ platform look — 18–19 September 2026 — compile, validators, plank MATRIX_PASS; the rest deferred

Dan's look card, one branch (`dan/hq-platform-look`): the HQ rebuilt as the
offshore platform of his reference picture (six in the morning, 2300, "worn
future"), the look kit (procedural textures, materials, prop prefabs, signs),
the way aboard moved to the HQ, the ship in the same kit. Everything is a
prop prefab under `Assets/_Project/Prefabs/Props` or a line of `HQSigns`;
the markers the rules read are the same components in new places.

What changed for the rules (see `docs/DESIGN.md` §2 HQ and Sailing,
`docs/NETWORK_CONTRACT.md` the trip rows):

- The ship has **no gangway** any more; the HQ's bridge and stair reach its
  stern. "Clear the gangway" is gone; "Not aboard" still names anyone on the
  stairs. The `RaisingGangway` stage is a still "casting off" moment.
- The **court** counts baskets (`CrewDayState.Baskets`, reset with the run).
- Bought gear **drops into the Pickup booth** at deck level.

Checked (host: the editor; guest build of this branch where a guest was used):

| Check | Result |
|---|---|
| Compile | clean (warnings only, the usual obsolete-API ones) |
| `HQPrototypeValidator` on every rebuild | passed |
| `WorldLoopChecks` (pure) after the gangway's removal | passed |
| plank matrix, 18 September 23:15, on the platform | MATRIX_PASS (`plank-matrix.log`) |
| shop matrix, 18 September 23:16, on the platform | **failed at S7** (`shop-matrix.log`): after "dead below" the row hits a destroyed `HQPlayerController` — not yet diagnosed (row or regression) |
| cabin, spectate, loop, fullrun, air | **not run** — deferred at Dan's request (skip-matrices rule, 18 September) |
| Steam session | not done for this branch |

Pictures: `iso.png` (the platform from the picture's angle), `ship.png` (the
ship moored under the HQ's stairs).

## Matrix debt

- shop S7 on the platform (above).
- loop: D1/D2/D3 rows were rewritten for the stairs (no gangway) and have not
  run since.
- cabin, spectate, fullrun, air: not run on the platform. The old
  hall-coordinate rows in the camera clearance, hands and inventory checks are
  known debt.
- A Steam host + guest session on the platform.
