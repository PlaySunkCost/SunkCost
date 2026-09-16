# Spectating card 2 (dead spectating) — 17 September 2026 — MATRIX_PASS (133 rows)

The plan: [docs/SPECTATING_IMPLEMENTATION_PLAN.md](../../SPECTATING_IMPLEMENTATION_PLAN.md), card 2.
The job: `SpectateRuntimeChecks` (`CameraClearanceMatrixDriver.Start("spectate")`) —
the card-1 rows (D0–D3, host + one guest build) followed by the card-2 rows
(S0–S4, host + **two** guest builds A and B, Local transport, port 7771), branch
`dan/spectating` on top of `dan/death`. Full log: [spectate-matrix.log](spectate-matrix.log).

## What happened (the run's own numbers)

| Section | Seen |
|---|---|
| D1/D2 (card 1, extended) | the host dies alone below: after its second of own camera the spectator view is on with no target — the screen says **NO SIGNAL**; End day ends the view with the revival |
| S0 | a second guest joins at sea (ids A=1, B=2); the cycle is put back to day 1 |
| S1 — nearest, ON AIR, left click | three below; A dies 1 m from the host and 5 m from B → `spectate=[1>0]`: A watches the **host** (nearest living); the host's visor reads **ON AIR · 1 watching**; A's spectator view is on the host after the second of own camera and the fade, its screen says `SPECTATING Skipper`, its camera sits on the host's eyes (< 1.5 m); left click → A watches **B** (host off air, B `ON AIR · 1 watching` on B's own visor); left click again wraps back to the host |
| S2 — dead voice | the host's test tone: dead A receives it on the **Spectate** route (`route=1`), living B nearby on the **Direct** route (`route=0`); A's own tone: the living host received **0** of A's frames, living B received **0** |
| S3 — dual world | A watches B, the host surfaces alone: B still below, the dive goes on, the site stays; A (dead, below) holds only the site; left click → A watches the host on the deck → A's client **loaded the ship as well** (`DiveSite01+Session+ShipAtSea`, active scene still its own), the server tracks `A watching Sea`, A's camera is on the host's eyes on the deck, A's screen is the deck view (no visor, `SPECTATING Skipper`); left click → B: the ship is **dropped** from A's client, no watch tracked; left click → host: dual world again; the car comes back down for B, B rides up: nobody below, dive done, the site unloads, A's object is carried to the ship — A holds only the ship, dead on it, no watch tracked, still watching a living player |
| S4 — End day | A alive, the spectate list empty, A's spectator view ended, one world; nobody on air; day 2 |

## Found on the way (four runs to a clean one)
1. `SpectatorView` runs after the HUD in the frame (execution order 500, after
   the targets' NetworkTransforms), so a `Check` right after the view became
   active read the previous frame's `NoSignal`; the row waits a second now.
2. **A remote copy's Unity scene on a client is not its world.** A client
   instantiates spawns into its active scene, so on A's client the host's copy
   (re-spawned when A loaded the ship) sat in `DiveSite01` although the host was
   on the deck — the HUD drew the visor for a deck view. The spectating HUD now
   takes the watched player's world from the replicated day state (`IsBelow`),
   the item/crew filters likewise, and the depth from the spectator camera's
   height under the site's sea level.
3. After the host's ride up the car goes back down for the diver left below; B's
   panel press came while it was still descending (`Cabin moving`). The script
   waits for `AtBottom` before B boards.
