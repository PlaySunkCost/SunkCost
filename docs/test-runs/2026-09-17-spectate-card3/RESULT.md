# Spectating card 3 (the deck TV) — 17 September 2026 — MATRIX_PASS (201 rows)

Second pass the same night, after Dan's first three-player test: the car that goes
back for a diver left below (rules below) and a revived guest's teleport. The
table and notes of the first pass (176 rows) follow the new section.

## The car going back for a diver left below (Dan's test, "think what can happen and deny it")

| Section | Seen |
|---|---|
| T1 — the car waits | B rides up alone and stays in the deck cabin: the car **waits up** (`AtTop`), the panel says `Step out — the car is needed below`; after the 10 s grace B is **put out** on the deck at (-3, 0, 5) and the car seals; B steps back in while the doors close → **they open again** (`AtTop`); B is put out again after the grace; the car goes down for the host and A (`Descending`) |
| T5 — the diver below leaves | day 3, three below; B steps out, the host and A ride up; the cabin clears → the car is on its way down for B (`Descending`); B **disconnects** → nobody below, dive done; the car **comes back up and the site closes** (`AtTop`, no car); panel `Dive done — end the day at the monitor`; End day → **payday** |

Found on the way: a guest revived after a site close could not be teleported —
its CharacterController had stayed disabled (the dead→alive change callback was
missed on that client) and a teleport made with the capsule disabled snapped
back when the capsule came on over its stale physics pose. Fixed twice over:
the dead state's side effects are re-applied whenever the replicated value and
the applied one disagree, and `TeleportLocal` syncs the physics pose before it
re-enables the capsule (`Physics.SyncTransforms`). The last piece was the
harness racing the network: the script moved A right after the *server* revived
it, before A's client had received the revive, whose placement then landed on top
of the move. T4 now waits until A's client reads itself alive with the capsule on
(as D3 and S4 already did). Still open: A's client logs FishNet
"Spawned NetworkObject was expected to exist but does not for Id 8" (the host's
object) around the site close and the revive — a message to A references an
object A no longer holds; harmless in every row so far, to be understood.

---

# First pass — MATRIX_PASS (176 rows)

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
