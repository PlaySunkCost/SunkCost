# Remote smoothness — 17 September 2026 — MATRIX_PASS (`smooth`, 11 rows)

Dan and Idan, two machines over Steam on Build 78: "the picture steps / jumps"
while spectating and on the deck TV (their own hands smooth).

## Measuring it
A new job, `smooth` (`RemoteSmoothnessRuntimeChecks`, one guest build): the
guest turns at 90°/s with its head bobbing (peer command `turn`) while the host
samples its copy every frame — a frame where the copy did not advance while the
guest was turning is a **stall**, one that advanced several frames' worth at
once is a **jump** — once on the clean local link, once with the guest's
outgoing packets through FishNet's latency simulator (peer command `netsim`:
60 ms ±40 re-rolled per packet, so a slow packet holds the quick ones behind it
and they arrive in a burst; 3 % lost, 5 % reordered).

## What the numbers said (10 s samples, ~200 fps host)
| Link | Buffer | Raw transform: frozen frames / worst catch-up |
|---|---|---|
| clean | 2 ticks | 0 % / 1.3× |
| ±20 ms | 2 ticks | 2.5 % / 21× · 0.5 % / 2.8× (two runs) |
| ±20 ms | 6 ticks | 0.3 % / 2.7× · 2.3 % / 20× · 1.3 % / 18× (three runs) |
| ±40 ms | 2 ticks | 5.7 % / 23× |
| ±40 ms | 6 ticks | 3.7 % / 15× |

The stepping reproduces (a catch-up of 20 frames' worth is a ~8° yaw snap). The
first hypothesis — FishNet's 2-tick (33 ms) interpolation buffer is a LAN value
and 6 ticks (100 ms) would ride out the jitter — **did not hold**: run-to-run
noise is larger than the difference, and a burst still overflows FishNet's goal
queue (`Count > interpolation + 3` → it drops to the newest and snaps),
whatever the buffer. The buffer stays at 2.

## Fix (presentation only)
A remote copy's eyes — the one place a spectator camera and the TV camera are
placed from, `HQPlayerController.EyePose` — ease over 100 ms behind the
transform (position and yaw; the replicated pitch already did), snapping only
on a teleport (> 3 m in a frame). A late packet and its catch-up become a slow
and a quick turn of the head. Nothing replicated changes.

## Run (buffer 2 ticks, ±40 ms link) — [smooth-matrix.log](smooth-matrix.log)
| Link | Raw transform | Eased eyes |
|---|---|---|
| clean | 0.3 % / 2.4× | **0 % / 1.1×** |
| ±40 ms | 5.2 % / 22.7× | **0 % / 1.9×** |

The row passes on the eyes: frozen ≤ 1 %, jumps ≤ 1 %, worst catch-up ≤ 6.
`spectate` (death, spectating, TV; two guests) rerun afterwards as the
regression check: MATRIX_PASS, 206 rows ([spectate-matrix.log](spectate-matrix.log)).

## Still to do
Retest over Steam on the next build: the simulator is not a relay. If the head
still steps there, the next hypothesis is the guest's own frame pacing, not the
link.
