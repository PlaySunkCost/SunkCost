# Small bugs batch — 17 September 2026

The eleven items listed after the 16 September full run
([docs/test-runs/2026-09-16-full-run/RESULT.md](../2026-09-16-full-run/RESULT.md)),
one by one. Six are code and are fixed here; five are process or open tests
and are listed with why.

## Fixed

| # | Item | Fix |
|---|---|---|
| 1 | `ServerRequestGrab` had no scene check — the worlds share one physics space, so reach alone let a script grab a coin on the ship from the seafloor | `PlayerInventory.ServerSameWorld`: the item must stand in the requester's world scene (or the session scene, where server spawns land); refused as `TooFar` otherwise. Contract row updated. |
| 2 | Coins rolled out of the storage room's doorway | `StorageSill`: a 12 cm sill across the doorway, built by the stub builder (the standing step offset is 25 cm, so players step over it). Design §1 notes it. |
| 3 | Refusals said `Player N` (`Waiting for: Player 2`, `Not aboard: Player 1`) | `WorldSceneFlow.DisplayName` looks up the player's `PlayerIdentity` name; `Player N` only for a connection without a player yet. The four matrices that expected the old text now expect the name. |
| 6 | Same coin names every dive (`Coin 6` on the ship and `Coin 6` on the seafloor) | `LootFixtureSpawner` stamps the day into a world scene's spawns on the server (`Coin 6 (day 2)`); clients still see the prefab name. The test hooks match either form. This run caught the exact confusion: on day 3 the look-at hook aimed at the deck's `Coin 7` instead of the seafloor's. |
| 8 | `Create or Update HQ` regenerated `Ship.prefab` without the cabin glass, the car glass and the doorway collider until someone ran the ride setup | `ShipStubBuilder.EnsurePrefab` runs `DeckCabinRideSetup.PatchShipPrefabGlass` itself; one call yields a complete ship. |
| 9 | A stale local guest build was refused in silence | `PrototypeAuthenticator` logs the refusal on the host (`[Admission] refused client N: WrongBuild — guest …/build … vs host …/build …`) and on the guest (`refused by the host: WrongBuild (…)`). |

## Not code (why they are not in this PR)

| # | Item | Why |
|---|---|---|
| 4 | GAME LOST has no consequence | By design until the plank card; a design decision, not a bug. |
| 5 | Two machines over Steam never verified | Needs the second machine (the Notion card). |
| 7 | Guest car-pin / E1 intermittency | Instrumented in #36/#38, not seen since; only a Steam run can close it. |
| 10 | Unity recompiles edits into a running Play Mode | Process: scripts are edited only while Play Mode is stopped (I stop it myself first). |
| 11 | Ride-time row fails on a 1 s editor stall | Tolerance kept tight on purpose; rerun, don't loosen. |

## Runs

- `fullrun` — MATRIX_PASS, 229 rows ([full-run.log](full-run.log)): the sill (four coins dropped in the room, none rolled out), day-stamped coins (`Coin 6 (day 3)`), refusals with names.
- `spectate` — MATRIX_PASS, 201 rows ([spectate-matrix.log](spectate-matrix.log)): two guest builds, the day-stamped lookups, the grab guard with both worlds loaded on a client (the dual-world rows still grab only in the physical world).
