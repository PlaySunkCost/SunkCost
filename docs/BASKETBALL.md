# Basketball appearance, rebound and scoring

Implemented on `codex/basketball-physics-scoring`, from the completed HQ branch.

The existing basketball prefab/GUID remains. The ball and matching sphere collider
are now **26 cm** across (user's follow-up: make it bigger and easier to score).
This is a modest game-readability enlargement over the original 24 cm ball. A smooth
sphere mesh and original generated leather albedo/normal textures replace the
plain orange sphere. The slot icon is rendered from the same prefab. All players,
spectators and world cameras see the same mesh/material.

Physics tuning is on `Art/Prototype/Basketball/Basketball.physicMaterial` and the
basketball prefab: 0.62 kg, restitution 0.795 with Maximum combine, friction
0.55/0.65, linear/angular damping 0.02/0.18, continuous dynamic collision and
12 rad/s throw backspin. Other carryables default to zero backspin. No extra
force is added on impact; energy diminishes naturally through Unity physics.
The original 24 cm ball's measured first rebound on the actual court was **1.057 m** (ball
underside) from a **1.800 m** underside drop. The reference target is 1.035–1.085 m
in the [FIBA 2024 equipment specification](https://assets.fiba.basketball/image/upload/documents-corporate-fiba-official-rules-2024-official-basketball-rules-and-basketball-equipment.pdf),
used as a tuning reference rather than a certification claim.

`HoopScore` previously rejected `HolderClientId >= 0`, including thrown balls
in `Released`, and relied on server Rigidbody velocity even for client-simulated
throws. It now samples the existing server-visible trajectory through two planes:
the rim and the lower scoring plane. Downward crossing inside the rim clearance
adds one to the existing shared `CrewDayState.Baskets`. Both backboards display
that replicated value, including for late joiners. Held, stowed, transit and
non-basketball items cannot score. Upward passes, side misses and discontinuous
motion do not score. A pass latch/repeat delay prevents duplicate counts.

No new RPC/SyncVar, no change to who simulates a released item, no new personal
score or two/three-point rules. Human review of the authority-related correction
and contract update is required before merge; no sign-off is claimed.

## More forgiving court

The imported rims and their brackets are widened horizontally by **40%** using
a derived mesh; the original source model, posts and backboards stay unchanged.
Both renderers and MeshColliders use that mesh. The cosmetic net and scoring
radius match the wider opening (0.315 m configured rim radius). With the 0.13 m
ball radius and existing 0.015 m tolerance, the permitted centre offset grows
from 0.12 m to 0.20 m. This is intentionally more forgiving than a regulation
hoop. Shots still have to pass downward through the visible opening.

`HQBasketballCourtTuning.Apply` updates the authored HQ; the same tuning is called
by `HQGeneratedCourt.Apply`, so scene regeneration keeps it. The runtime matrix
also drops balls 0.16 m either side of centre at both hoops, testing physical
clearance and scoring together rather than only testing perfect-centre shots.

## Regenerate and test

- Out of Play Mode, run `BasketballSetup.Apply` (Sunk Cost / Prototype / Apply
  basketball look and physics) to regenerate the art/material/prefab/icon.
  Reapply after intentionally resetting prototype prefabs.
- Build the local Windows client with `HQPrototypeBuild.BuildWindowsLocalDevelopment`.
- Run `CameraClearanceMatrixDriver.Start("basketball")` or `matrix basketball`
  through the editor command bridge. Evidence: `Temp/basketball-matrix.log`.
- The matrix measures successive bounces, checks both hoops and false-positive
  cases, then shoots using the real host and separate client's inventory requests.
  Its test-only camera-pitch search is in `BasketballShotProbe`; it is excluded
  from release builds and is not production aim assistance.
- Steam transport across two computers and high-latency/packet-loss play remain
  separate acceptance checks. Local loopback success does not establish them.
