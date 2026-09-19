# The HQ platform and ship look — 18–19 September 2026 — the matrices before the merge

Dan's look cards, one branch (`dan/hq-platform-look`, PR #78; the ship/tube
branch folded in): the HQ rebuilt as the offshore platform of his reference
picture, the look kit (procedural textures, materials, prop prefabs, signs),
the way aboard moved to the HQ (a bridge to the tower's roof, the ship's own
stair down), the ship after his second picture (44 × 16 m, the elevator dead
centre in an open well, the captain's tower), the tube and car truly round
with glass leaves and the tube's own doors at the top, every button a red
button with its word on it, every label a sign plate, no hover prompts (a
short notice on a refused press), borders everywhere, nothing to walk
through, static batching for the frame rate and an FPS counter, scene 3 in
the same kit.

What changed for the rules (see `docs/DESIGN.md` §1 Sailing and §2 HQ,
`docs/NETWORK_CONTRACT.md` the trip rows):

- The ship has **no gangway**; the HQ's bridge meets the tower's roof.
  "Not aboard" names anyone on the bridge or its landing.
- The **tube's own doors** at the top (`DeckCabinHousingDoorL/R`) follow the
  car's while it is up and stay shut while it is away, like the seafloor gate.
- The **court** counts baskets; bought gear **drops into the Pickup booth**.

## Matrices on this branch (host: the editor; guest: this branch's local build)

All at `e98316c` unless noted; the guest built from the same revision.

| Job | Result | Log |
|---|---|---|
| plank | MATRIX_PASS, 60 rows (at `e37bc12`) | `plank-matrix.log` |
| shop | MATRIX_PASS, 90 rows (at `e37bc12`) — the S7 failure of 18 September did not recur | `shop-matrix.log` |
| cabin | see below | `deck-cabin-matrix.log` |
| spectate | see below | `spectate-matrix.log` |
| loop | see below | `world-loop-matrix.log` |
| air | see below | `air-matrix.log` |
| fullrun | see below | `full-run.log` |

### What the cabin matrix found (fixed here)

**E1 — mid-ride the dot could not find the cargo on the car's floor.** A coin
thrown in the moving car came to rest about 2 cm *into* the car's floor (the
floor's collider and the body are stepped apart each frame while the car
moves), and the car's pin froze it at that depth. Carried to the deck cabin
at the top it was placed 2 cm into that floor too; a box whose centre is under
a mesh face gets no contact and fell through to the pedestal (world y 0.015
instead of 0.115). On the next ride down it rode inside the car's floor slab
and the dot's line of sight hit the floor first. Traced with a per-step log
of the coin's body (`CARGOTRACE`, removed). Fix: `CarryableItem` pins an item
that landed under way with its underside on the floor (a ray down from over
the item, `LiftedOntoFloor`). Not a regression of the look work as such —
the depth depends on frame timing, and the editor runs faster now.

**The guest refused with "Wrong build".** The editor's Play Mode identity is
`local-dev:` while the working tree is dirty, and the guest build carries a
clean SHA; committing the fix and rebuilding the guest cleared it. (A rule
worth remembering: commit before a matrix with a guest.)

## Remaining debt

- The `camera`, `hands` and `inventory` matrices still assert the old hall's
  coordinates (a Phase 02 close-out card on the board).
- A Steam host + guest session on the platform since the bridge moved to the
  tower's roof (a close-out card).
- Idan's read of the contract rows (the trip, the housing doors) on `main`.

Pictures: `iso.png` (the platform from the picture's angle), `ship.png`.
