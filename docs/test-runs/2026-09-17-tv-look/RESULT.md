# The TV's look and cost, spectating smoothness — 17 September 2026 — MATRIX_PASS (206 rows)

Dan's report after watching a diver on the deck TV: the seafloor looked light
(daylight, not underwater) and the game got "very laggy" while spectating.

## Why
- Unity applies only the **active** scene's RenderSettings; the ship client's TV
  camera rendered the site with the ship's daylight fog and no post-processing.
  The same in reverse for a dead diver watching someone on deck.
- The TV was a second render of the whole site at full screen resolution every
  frame; and the watched player's look pitch arrived at 10 Hz in 2° steps with
  no smoothing.

## Fix
- `WorldLook` (a component every world scene now carries, written by the builders
  and `WorldLookSetup`): the scene's ambient and fog. `ShipTV` swaps the site's in
  around its render; `SpectatorView` does the same for the owner's camera when the
  watched player is in the other world (render pipeline begin/end callbacks).
- The TV camera is a copy of the diver's (culling, clip planes, URP
  post-processing and volume mask), renders at **half resolution, every other
  frame, only while someone stands within 30 m** of the screen; the visor is drawn
  into the smaller texture through a GUI matrix (`PlayerVisorMath.TryProject`
  now maps camera pixels to screen units, identity for the main camera).
- Look pitch: 20 Hz on a 1° change, eased on remote copies over ~80 ms.

## Run
`spectate` — MATRIX_PASS, 206 rows ([spectate-matrix.log](spectate-matrix.log)).
New rows: the host near the TV → it renders (99 frames in ~2 s = every other
frame); the picture's **mean brightness 0.170** (dark; [tv-picture-b.png](tv-picture-b.png));
both loaded worlds carry a `WorldLook`; 40 m off the ship the TV renders 0
frames; back on deck it renders again. The sky at the horizon is the same default
skybox in both scenes, so the TV matches the diver's own view there.
