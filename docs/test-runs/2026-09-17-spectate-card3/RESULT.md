# Spectating card 3 (the deck TV) — 17 September 2026 — MATRIX_PASS (176 rows)

The plan: [docs/SPECTATING_IMPLEMENTATION_PLAN.md](../../SPECTATING_IMPLEMENTATION_PLAN.md), card 3.
The job: `SpectateRuntimeChecks` (`CameraClearanceMatrixDriver.Start("spectate")`) —
cards 1 and 2's rows (D0–D3, S0–S4) followed by the TV rows (T1–T4), host in the
editor + two guest builds A and B (Local transport, port 7771), branch `dan/tv` on
top of `dan/spectating`. Full log: [spectate-matrix.log](spectate-matrix.log). The
host's TV picture saved by the S3 row: [tv-picture-b.png](tv-picture-b.png).

## What happened (the run's own numbers)

| Section | Seen |
|---|---|
| S3/T — the picture | the host on deck is a viewer: channel **B**, the host's TV live; the saved picture carries B's view **and B's visor** (brightness deviation 0.149 — mask, O2/HP, DEPTH 42.4 m, compass with HOME, slots, ON AIR) |
| T1 — early surfacer | three below on day 2; B rides up alone; the host and A stay below; channel = the first diver below, the **host**; B holds the dive world on the ship (`IsWatching(B) == Dive`); B's TV `LIVE · Skipper`; the host's visor `ON AIR · 1 watching`; E → channel **A** (`LIVE · <A>`, A on air, the host not); E again wraps back to the host |
| T2 — voice and the dead | the host's tone reaches B on the deck on the **TV route** (`route=3`); A dies below and watches the host → the host reads **ON AIR · 2 watching** (the TV and dead A); E keeps the host — the dead are no channel |
| T3 — the end of the signal | the car comes back down; the host surfaces: nobody below, dive done, the site unloads; channel −1; B's TV `NO SIGNAL`, the dive world gone from B, no watch tracked; the host is still watched by dead A only (1); at the dark TV the prompt reads `NO SIGNAL — nobody below` and E is refused with the same words |
| T4 — End day | A alive, no spectators, no channel; nobody on air; day 3 |

## Found on the way (three runs to a clean one)
1. The TV tuned itself to the first diver below as soon as anyone was below — with
   nobody on deck to watch, so the host's ON AIR read 2 in S1. The channel now
   exists only while a living player stands on the ship: a TV nobody sees has no
   channel, and ON AIR counts real watchers.
2. Dan (mid-run): the TV must show the diver's **visor**, not just their camera,
   and every such surface must follow the visor's look automatically. `PlayerHudUI`
   was refactored into `VisorFrame` + `Compute` + `DrawVisor` — one path for the
   owner's screen, the spectator view and the TV (the TV draws it into its screen
   texture with IMGUI on `RenderTexture.active`); the picture above is the proof.
   The rule went into docs/CONVENTIONS.md.
3. The last row read the host's ON AIR count the frame after End day cleared the
   spectate list (the HUD computes a frame later); it waits now.
