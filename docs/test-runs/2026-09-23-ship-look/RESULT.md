# The ship rework and the deck TV — 23 September 2026 — testing owed

Branch `dan/ship-look`. Dan asked to stop testing for now and write down what is
left ("dont test it now after you finish. write it for later"). Nothing below
the first table has been run on the latest commit.

Host: the editor; guests: the local build of the same revision.

## Run so far

| Job | Revision | Result |
|---|---|---|
| spectate | `59c194a` | MATRIX_PASS, 227 rows: S1b (a spectator watching a diver below sees it as dark as the diver: 0.254 vs 0.256), S3/T (the TV on deck in daylight 0.161 vs its picture 0.156), T3 (the TV found where it now stands) |
| spectate | `aab9901`, `129699c` | FAIL at the new S3/T comparison with B's own screen — but both compared B's *session menu*: every guest command opens it, so a plain `capture` shows the menu and no visor. `capture_play` (closes it for the captured frame) was added in `33fd7e0`. |
| spectate | `33fd7e0` | S1b passed with menus closed (spectator 0.242, diver 0.256); then held for Dan to look, and stopped when Play Mode was stopped (editor scripts changed) — no verdict. |

## Owed before the PR

1. **spectate**, in full, on the latest commit. New since the last pass:
   - S3/T: the TV's camera stands at B's own camera (passed on `aab9901`: both at (-6.36, -43.35, -6.36)).
   - S3/T: the TV picture matches B's own screen (`capture_play`), mean luminance within 0.05 — not yet passed.
   - S3/T: the host draws none of dead A's figure after coming back to the dive
     (`HQPlayerController.OnStartClient` re-applies death; the host showed a dead diver standing).
   - S3/T: B's figure is drawn for everyone else after the TV's render (`BeginWatchedRender`/`EndWatchedRender`).
   - Optional hold for a person to look: create `Temp/spectate-hold.txt` before the run; delete it to go on.
2. **cabin** — the ride's dry stretch grew from 1 m to 4.5 m (sea level -4.5), the deck is now the hull's mesh collider, the storage room's walls moved and thickened.
3. **loop** and **fullrun** — the ship rebuilt around the models (boarding point, storage room, TV, console).
4. **The 1.2 m side against a jump**, including from a crate or barrel beside it, and the unseen guard over it (a by-hand check: 230 outward rays at 0.5-7.5 m found no way out).
5. `Sunk Cost/Look/Audit the ship's shell` must report 0 after any dressing change (it did on `41387a0`).

## Open question — the TV's floor is darker than the diver's

With the sky backdrop fixed (`aab9901`) the TV is no longer brighter than the
diver's view; on `129699c` it measured darker (0.061 against B's 0.123, though that
B capture had the menu over it). Dan's side-by-side shows the seafloor lit pale blue
around the diver on B's own screen and dark on the TV. Ruled out: the TV camera's
settings (post-processing, volume mask, fog colour, field of view, shadows all match
B's), the pose (B's own client reports the same camera position and facing), and B's
headlamp (on at the host, a 25 m spot). Left to check: the lights each machine has —
a guest loads the dive alone, the host loads the ship and the dive together and a
render has one main directional light (the query on the host found no directional
light at all, which itself needs explaining), and the ambient probe the host keeps
from the ship's scene while `WorldLook` swaps only the ambient colour and fog.

## Fixed on the way (to be verified by the runs above)

- The deck TV's screen is unlit (it was lit and glossy: the deck's daylight lifted the picture).
- The TV clears to the dive's fog colour under water, not the deck's sky; a spectator
  watching someone in the other world swaps the backdrop with the fog and ambient.
- The TV and spectators draw none of the watched diver's own figure (hands only when holding something):
  the eased eyes trail the body, and a running or jumping diver's body and hands came into the picture.
- The host keeps a dead player's figure hidden when it sets that player up again.
