# The HQ platform and ship look — 18–19 September 2026 — all seven matrices MATRIX_PASS before the merge

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

Revisions as noted; the guest built from the same revision each time.

| Job | Result | Log |
|---|---|---|
| plank | MATRIX_PASS, 60 rows (at `e37bc12`) | `plank-matrix.log` |
| shop | MATRIX_PASS, 90 rows (at `e37bc12`) — the S7 failure of 18 September did not recur | `shop-matrix.log` |
| cabin | MATRIX_PASS, 288 rows (at `829677c`, after the two fixes below) | `deck-cabin-matrix.log` |
| spectate | MATRIX_PASS, 224 rows (at `3957985`, the T3 spot moved) | `spectate-matrix.log` |
| loop | MATRIX_PASS, 62 rows (at `196f1b8`, the deck spots moved off the well) | `world-loop-matrix.log` |
| air | MATRIX_PASS, 86 rows (at `196f1b8`) | `air-matrix.log` |
| fullrun | MATRIX_PASS, 231 rows (at `196f1b8`) | `full-run.log` |

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

**Z2 — the coin in the storage room reset to the seafloor on docking.** The
"lost under y = −2" void line was written for a ship whose deck is at 0; the
ship moored at the rig lies 6 m under the platform, so every loose item on it
was under the line the moment it was placed. Two fixes in `CarryableItem`:
the line is 2 m under the deck of the item's world's ship (cached per scene),
and cargo in transit is never judged at all — it is placed at its destination
spot a frame before the scene move carries it there, and at that moment its
scene is still the one it left. An item that does fall under the world now
says so in the log.

**The guest refused with "Wrong build".** The editor's Play Mode identity is
`local-dev:` while the working tree is dirty, and the guest build carries a
clean SHA; committing the fix and rebuilding the guest cleared it. (A rule
worth remembering: commit — docs included — before a matrix with a guest, and
rebuild the guest after every commit.)

### What the other matrices found (test rows, not rules)

- **spectate T3**: the row stood 5 m from the TV; the deck is 16 m wide now
  and the screen is on the port rail. It stands 2 m off the screen.
- **loop D7**: the row dropped its deck ball at ship-local (2, 0.5, −3) —
  inside the open well round the car, which is a hole to the sea since this
  branch; the ball fell, reset to its court spot, and the row saw no cargo.
  The ball and the host's deck spot moved starboard/port of the ring rail.
- The runner (`Temp/matrix-run.sh`) reads the verdict line anywhere in the
  log: the loop and air jobs append a status dump after `MATRIX_PASS`.

## Remaining debt

- The `camera`, `hands` and `inventory` matrices still assert the old hall's
  coordinates (a Phase 02 close-out card on the board).
- A Steam host + guest session on the platform since the bridge moved to the
  tower's roof (a close-out card).
- Idan's read of the contract rows (the trip, the housing doors) on `main`.

Pictures: `iso.png` (the platform from the picture's angle), `ship.png`.
