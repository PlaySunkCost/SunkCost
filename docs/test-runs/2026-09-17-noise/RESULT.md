# Noise over the network, the elevator's sounds — 17 September 2026 — noise MATRIX_PASS (55 rows), air MATRIX_PASS (53 rows)

Dan's next system after oxygen, with his three notes: steps make sound but
crouch steps none at all; the elevator going up is a big sound and the
players on the ship hear it too, low; a movie-elevator "ding" when the car
arrives and opens.

## Built
- `NoiseSystem.IsServer` set with the server (`WorldSceneFlow`), cleared and
  the listeners dropped when it stops.
- `PlayerNoise` on the player prefab (server): a `Footstep` every 0.75 m of
  ground covered in the dive world (radius 6 m), a `Sprint` every 1.1 m while
  the server judges the copy sprinting (radius 15 m), **nothing crouched**,
  nothing on the ship. `SourceId` = the player's FishNet ObjectId.
- `ElevatorNoise` on the day state (server): an `Elevator` event at the car
  when it starts moving and every second while it moves, radius 60 m.
- `ElevatorSounds` on the day state (every client): the winch loops at the
  car (3D, loud) for a player whose object is below and low (2D, 0.15)
  through the deck for one on the ship; the bell at the bottom for those
  below, at the top for the deck and the riders. Read off the replicated
  elevator phase; nothing emitted.
- `AudioLibrary` (Resources) with a slot per sound and generated
  placeholders (`PlaceholderSounds`: a 42 Hz motor with a grind and filtered
  noise; a two-note bell); `NoiseSettings` (Resources) for the radii and
  strides. `NoiseSetup` creates both and patches the prefab.
- `NoiseGizmo`: the Scene-view discs, toggled from the menu.

## Runs
- `noise` — MATRIX_PASS ([noise-matrix.log](noise-matrix.log)): nothing on
  the deck; the ride down heard by the ocean (18 Elevator events at 60 m),
  the winch at the car and the bell at the bottom; 8.0 m walked = 10
  footsteps at 6 m; 12.0 m sprinted = 10 sprint steps at 15 m (one walking
  stride at the start); 4.0 m crouch-walked = 0 events; the ride up: winch at
  the car (not through the deck for the rider), 18 events, the bell at the
  top. With a guest on the deck: the returning car and the host's climb heard
  through the deck, low, not at the car; quiet when the car stops; the bell
  on the deck when the car opened at the top.
- `air` — MATRIX_PASS, as the regression (the same speed judgement).

Not exercised: impacts (a dropped statue) — `NoiseEmitter` is guarded but
not yet on the carryables; the sound of the placeholders on a real speaker.
