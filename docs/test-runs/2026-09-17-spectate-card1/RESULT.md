# Spectating card 1 (death minimum) — 17 September 2026 — MATRIX_PASS (71 rows)

The plan: [docs/SPECTATING_IMPLEMENTATION_PLAN.md](../../SPECTATING_IMPLEMENTATION_PLAN.md), card 1.
The job: `SpectateRuntimeChecks` (`CameraClearanceMatrixDriver.Start("spectate")`),
host in the editor plus one guest build (Local transport, port 7771), branch
`dan/death`. Full log: [spectate-matrix.log](spectate-matrix.log).

## What happened (the run's own numbers)

| Section | Seen |
|---|---|
| D0 — K on deck | `RequestDebugDeath` on the deck is refused: `You can only die below`; nobody dead |
| D1 — K below | the host dies holding Coin 3: the coin scatters (`Free`, 0.6 m from the body); a body `Skipper's body` spawns 0.3 m from where it fell — a loose two-handed carryable; the host is `Dead`, not `Below`, its capsule off; nobody living below → `phase=AtSea diveDone=True`; the site unloads with the dead carried out; the host's object lands on `ShipAtSea`, parked aboard; cabin panel `Dive done`; still dead and hidden |
| D2 — End day | `ServerEndDay` accepted; the host is alive with the capsule on, standing on the deck, nothing carried; `Day 2`; the seafloor body did not come up (lost with the site) |
| D3 — guest dies, body carried up | guest joins at sea, both descend (`below=[0,1]`); the guest `die`s 4 m out of the car: `Dead`, the host alone below, the dive goes on; the guest reads `dead=True`; its body lies where it fell; its copy is hidden on the host; the host grabs the body in both hands and rides up; `Below=0 diveDone`; site unloads; the dead guest's object is carried to the ship and the guest reads itself dead on `ShipAtSea`; the body came up in the host's hands and is set down on the deck at ship-local (-3.0, 0.2, 1.7); End day → the guest is revived **0.8 m** from where its body lay, the body gone, its copy visible again, `Day 3` |

## Found on the way (four runs to a clean one)
1. The dead player's parking on the deck happens the frame after its object
   arrives (`ParkDeadWhenHome`): the row now waits up to 5 s instead of checking
   at once.
2. `RenderersOff` only filtered renderers named `Hand`; the owner's first-person
   arm rig (`ArmL`/`ArmR`, palms, fingers) stays on for the dead owner and is
   not part of the hidden-body check. The game state was right.
3. **Setting the body down needs room.** A box item's placement radius is half
   its longest side (`CarryableItem.Radius`), so the 1.7 m body needs a 1.7 m
   clear sphere ahead of the player; at spawn point 3 with the ride's heading
   the drop was refused (`NoRoom`). The script now tries deck spots and headings
   like a player would (the first spot facing yaw 0 had room). Left as is for
   card 1; a body-shaped placement is a follow-up if it annoys in play.
