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

Lead from an edit-mode look (ShipAtSea with DiveSite01 added, no Play Mode): the
dive has its own directional light ('Surface Light', intensity 2) and three
'Seafloor Light' points on the DiveSiteDeep layer; the ship has its 'Sun' (0.9),
'Well Light', 'Tube Light' and a zero-intensity 'Light'. Both suns cull layer 8.
The dive's ambient is Flat (0.015, 0.030, 0.028), the ship's (0.25, 0.28, 0.32).
Next step: render one camera at the seafloor twice - the dive scene active, then the
ship active with `WorldLook.Begin(dive)` as the TV does - and compare; then check
which directional light URP takes as the main light in each case and whether the
ambient probe follows the swapped ambient colour.

## Fixed on the way (to be verified by the runs above)

- The deck TV's screen is unlit (it was lit and glossy: the deck's daylight lifted the picture).
- The TV clears to the dive's fog colour under water, not the deck's sky; a spectator
  watching someone in the other world swaps the backdrop with the fog and ambient.
- The TV and spectators draw none of the watched diver's own figure (hands only when holding something):
  the eased eyes trail the body, and a running or jumping diver's body and hands came into the picture.
- The host keeps a dead player's figure hidden when it sets that player up again.

## Ship audit fix — 23 September 2026 — testing owed

The fixes for `SHIP_FULL_AUDIT.md` (SHIP-001 … SHIP-087), in three rounds on
24 September 2026: f3c4a85, aeadab0, e7dccc7, 7511263, 6586123. They were run
by the QA agent `qa-technical` in the editor matrices. Host: the editor. Guest:
the local build `Builds/HQPrototypeLocal` on the same machine, stamped with the
revision under test. **This is not a Steam run across two machines**; that
transport check is still owed.

Matrices: **loop** 62 rows, **cabin** 288 and **save** 57 on aeadab0; **noise**
69 on aeadab0; **fullrun** 231 on e7dccc7; **spectate** 239 on 6586123. Every
job ended MATRIX_PASS.

| Job / check | What is new since the last run | Result |
|---|---|---|
| **HQ mooring** | `HQPrototype.unity` regenerated for the 48 m ship: the tower's aft face 0.2 m off the landing, no pile or girder in the hull or tower. Regenerate once more after the final ship rebuild. Walk the bridge to the landing and look over the edge. | Regenerated after the final ship; the HQ validator passes; 0 collider overlaps between the ship and HQ (245 × 474). The 2.3 m drop from the landing onto the tower roof is Dan's (the way-aboard card). Nobody has walked it. |
| **cabin** | The ring round the well (low visible rail, 1.2 m collider, open at the grate); the Unstuck point clear of the container; the warm car light on both cars. | MATRIX_PASS, 288 rows (aeadab0). Two real sprint jumps from the crane base end against the rail's wall, now 5 m. |
| **loop** and **fullrun** | Props at Dan's sizes; the console buttons SITE 01 · HQ · END DAY; the ladder gone; the lounge table between the benches. | loop MATRIX_PASS, 62 rows (aeadab0). D7's ball now drops on `ShipDeckDressing.OpenDeck`, since the crane base had moved under the old spot. fullrun MATRIX_PASS, 231 rows (e7dccc7). |
| **spectate** | The TV lowered (same size); the rows below on the TV and the couch. | MATRIX_PASS, 239 rows (6586123). S1b: spectator 0.206 vs diver 0.215. S3/T: the TV 0.207 vs B's screen 0.205, with the visor on the TV picture. **The TV floor question is closed:** the screen copy in `ShipTV.OnGUI` had lost its gamma (sRGB writes off in IMGUI). |
| TV reach (SHIP-043) | At the couch front, about 5 m from the screen: the dot turns gold, E changes the channel and the server accepts it. At 6.7 m nothing happens. A console button at 4.5 m: no target; at 3.0 m: pressable. The cabin button, loot and the car panel still at 3.5 m. Needs the `ShipControls` server check wired first. | Pass. At 4.95 m it is targeted and the server accepts; at 7.74 m it is refused ('Too far from the screen'); a console button is not targeted at 4.58 m. The matrix also covers it now (spectate T1r / T1 / T2). |
| Couch: sit and stand | E on each couch: host and guest both see `SeatNumber` = N; the owner's root within 5 cm of `CouchSeat_N`, capsule off; the view turns to the TV; the FOV settles with the screen's four corners inside viewport [0.02, 0.98] and one axis at 0.95 ± 0.02. Space, E and W each stand you up on the deck (y about 0, capsule on, seat 0 on both peers). A second player asking for the same seat gets "Seat taken". Stepping onto the seat (seat and back collide, SHIP-024). | Pass: the root is on the seat (0.000 m), the capsule off, the eyes at 0.75; standing puts you 0.85 m in front. The zoom fills 0.908 of the tall axis in a 2.19:1 editor window (0.95 at 16:9). 'Seat taken' was not tested: the guest has no sit command. |
| Couch: others see the sitter | The guest's copy of the seated host is at the seat, not floating: `SeatedPose` true, body scale about 0.95/1.8, eyes at hip + 0.75. Kill a diver below (K), spectate the seated host: the spectator's FOV equals the host's within 1°. Look at the squashed body and the arms on it. | The guest sees the host on the seat. A spectator's view of a seated player was not tested. |
| Couch: departure while seated | Sit, sail HQ to sea: seated through the fades; on arrival the root within 5 cm of the destination ship's `CouchSeat_N`; the capsule stays off after the unlock until the player stands; standing puts them on the destination deck. The server logs no "[Seat] ... stood up". | Pass: arrives on the sea ship's seat (0.000 m), the capsule still off, no '[Seat] … stood up'. |
| Couch: edge cases | Unstuck stands you up (seat 0); a disconnect frees the seat; the plank at HQ stands you up. | Not run in Play Mode (code review only). |
| Riders while sailing (SHIP-034) | The ship root has a kinematic Rigidbody in both worlds; during PullingAway the local rider's ship-local offset stays constant (under 1 cm) and remote copies behave as before; at rest `transform.hasChanged` stays false and the physics sync cost is near zero; frozen cargo sails and lands as before (matrix D1 on). | Pass at rest: the kinematic Rigidbody is on the root, and no root writes over 621 frames at the pier or after arriving. Riders are carried in fullrun. |
| Use highlight (SHIP-054) | `InteractHighlight.Highlighted` is the target root with PartCount > 0 for a console button, the cabin button, the TV screen, a couch and a coin; null aimed at the sky, at a teammate, with the menu open, while seated, and for an item "Hands full" blocks. Look at it on the deck and below in fog. | Pass for the TV, a console button and the couch; clear on the sky. Not looked at under water or on loot. |
| Winch and bell | Mostly 3D: loud near the cabin, faint across the deck. | The state checks pass (blend 0.7, 6–40 m; the noise matrix N1/N5/N6 pass). **Nobody has listened yet.** |
| The 1.2 m side and the well's rail against a jump | Including from a crate or barrel beside them. | The side: 1,217 outward casts, no way over. The rail: a simulated sweep of 28,656 jumps, 0 in; two real sprint jumps, 0 in. |
| `Sunk Cost/Look/Audit the ship's shell` | Must report 0 problems after the final ship rebuild. | 0 problems after the final rebuild (1,731 standing places). |
| Lighting between worlds | The dive site loaded next to the ship: neither world's lights leak into the other (ties to the TV floor question above). | Pass: each world has its own rendering layer and main light. In edit mode the deck is pixel-identical with the dive loaded; spectate S1b and S3/T pass. |

Test seams for the couch rows (editor only, `HQPlayerController.Couch.cs`): `RequestSitForChecks(int number)` makes the same request E does, `StandForChecks()` stands up. Matrix rows fix-player suggested (not written): T3b/T3c in `SpectateRuntimeChecks` after T3 (the TV from 5 m, the console from 4.5 m); a couch block (the four couch rows above); next to D1 in `WorldLoopRuntimeChecks`, the ship's kinematic Rigidbody and no root writes at rest.
