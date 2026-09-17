# Footstep, jump and landing sounds; the winch low for the riders — 18 September 2026 — noise MATRIX_PASS (65 rows)

Dan's two problems after playing Build 90: the winch is too loud inside the
car (it is meant for those left below); there are no footstep sounds at all,
and a jump should sound.

## Built
- `ElevatorSounds`: a rider inside the car (in `CrewDayState.Riders` while a
  ride is on) hears the car winch at `winchVolumeInCar` (0.12); a diver left
  below hears it at the car at full; the ship through the deck low, as before.
- `PlayerFootstepSounds` (on the player prefab, every peer): a footstep
  every stride of ground covered — the strides `NoiseSettings` gives the
  ocean's ears — pitched left/right, quicker and louder sprinting, none
  crouched; a scuff on takeoff and a thud on landing (the owner from its
  motor, a friend's copy from its height; landings only after real flight,
  never from the ground probe blinking on a teleport). The owner's own play at
  0.6 of a friend's. 3D at the feet.
- `AudioLibrary`: slots `footstep`, `jump`, `land` with volumes and the own-
  step scale; generated placeholders (low-passed noise bursts of three
  lengths). Drop a .wav in the slot to replace.

## Run
`noise` — MATRIX_PASS ([noise-matrix.log](noise-matrix.log)): the rider in
the descending and ascending car hears the winch at 0.12; walking 8 m = 10
footstep sounds for 10 noise events; sprinting 10 for 10; a jump and its
landing heard once each; 4 m crouch-walked = no footstep sound; with a
guest riding up alone, the host left below hears the departing car at 1.00,
the guest on the deck low.

Not exercised: the sound of the placeholders on a speaker.
