# Code check, the voice world — 18 September 2026 — `spectate` MATRIX_PASS (with the new S3v row)

Finding of the 18 September code check (desync hunt): `ProximityVoice.WorldOf`
judged a remote player's world from its copy's Unity scene. On a client that
scene is not the world — a client instantiates spawns into its active scene
and only a load it is part of moves them. After a split surfacing (the host
rode up alone, B followed later in its own load) the host's copy on B sat in
`Session`, so B dropped every direct frame from the host until the next shared
load.

## Fix
- `WorldOf`: the server and a peer's own object keep the scene rule; a remote
  copy on a client is judged from the replicated day state (`IsBelow` → the
  dive world, else the ship's world). The server's relay decisions were never
  affected; the client-side filter now agrees with them.
- `ShipDepartureRider` had the same scene test for a rider copy's frame and
  the moving-floor pin: clients now use `IsBelow`; the server keeps its scene
  (its scene flips at the load, before `Below` does — a first cut that used
  `IsBelow` on the server too left a rider at dive coordinates after the ride
  up, caught by row T1).

## Rows
- **S3v** (new): after the split surfacing, the host stands next to B on the
  deck; B receives the host's frames on the Direct route and lists the host
  in its indicator; the host receives B's.
  - `spectate-matrix-before-fix.log`: the same row on the old code —
    `B's view of the host's copy: … scene=Session …` then
    `FAIL: S3v B receives the host's frames on the Direct route`.
  - `spectate-matrix.log`: with the fix, MATRIX_PASS.

Host: the editor, two guest builds, Local transport. The contract's voice
section records the rule; a human networking review is still required for a
voice change, and the two-machine Steam check is Dan and Idan's.
