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
  crouched; a thud on landing — a takeoff is silent (Dan, later the same day) — (the owner from its
  motor, a friend's copy from its height; landings only after real flight,
  never from the ground probe blinking on a teleport). The owner's own play at
  0.6 of a friend's. 3D at the feet.
- `AudioLibrary`: slots `footstep`, `jump`, `land` with volumes and the own-
  step scale; generated placeholders (low-passed noise bursts of three
  lengths). Drop a .wav in the slot to replace.

## Tuned after Dan played (same day)
Steps "way lower": walk 0.07, sprint 0.11 (a fifth of the first cut), jump
0.22, landing 0.3. The winch only for the **5 seconds the car is near you** —
below: the last 5 going down, the first 5 going up; deck: the first 5 going
down, the last 5 going up (Dan's correction of a first cut that used "last 5"
everywhere) — faded over 0.5 s, read from the replicated phase's start tick
and duration; riders inside hear it low the whole ride; quiet (0.35) but carrying
250 m ("hear it from very far") for the divers left below; 0.06 inside the
car; low through the deck. The library asset was recreated with the new
defaults. The noise and air jobs now ignore editor focus like the hands job
(the run Dan's typing interrupted walked 0.0 m).

## Run
`noise` — MATRIX_PASS ([noise-matrix.log](noise-matrix.log)): the rider hears
the winch low (0.06) the whole descent, the bell at the bottom; walking 8 m = 10 footstep sounds for
10 noise events; sprinting 10 for 10; a jump and its landing heard once
each; 4 m crouch-walked = no footstep sound; the rider low the whole
way up; with a guest riding up alone, the host left below hears the car
leaving it at 0.35 carrying 250 m and silence after 5.0 s, nothing when it
arrives far above; the guest on the deck hears the returning car leave the
deck, low, then nothing; the host below hears it arrive in its last seconds;
the guest hears the host's car arrive at the deck and the bell.

Not exercised: the sound of the placeholders on a speaker.
