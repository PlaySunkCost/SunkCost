# Code check, the small ones — 18 September 2026 — hands, cabin, loop all MATRIX_PASS

The lesser findings of the 18 September code check:

- **Late joiners replayed the last refusal / pay report** for a few seconds:
  FishNet raises OnChange for a joiner's initial values in the frame of
  OnStartClient; `CrewDayState` ignores those for the two timestamps.
- **The K-kill RPC** now checks `Debug.isDebugBuild` on the server, like L.
- **A forced rest handoff stopped a rolling ball dead:** the writer's velocity
  crosses with `ServerRequestRest` and is applied on the server, bounded by
  the throw speed (contract section 2, step 5 — already the rule).
- **An equip from a slot streaked in from the stow spot** on observers: the
  new owner's first write is a teleport when the item comes from far.
- **A remote rider copy sat at the server's captured spot** after a ride until
  its owner walked: the owner sends one fresh goal (a teleport) at Unlock.
- Contract: the tank counts from the top of the ride down (as built and as
  Dan decided — the suit is on), not "at the landing".

Two stale rows in the cabin matrix were brought up to date on the way (V2:
the tank counts inside the car since the oxygen card; V4: the two air tanks
among the placements are not coins). Skipped, on purpose: the door-seal
start fraction for a peer that loads mid-seal (the default of "fully open" is
the right guess in every case that occurs), and the per-frame
`FindObjectsByType` in the voice service and the HUD's IMGUI strings (GC
pressure, not bugs).

Host: the editor, guest builds, Local transport. Logs alongside.
