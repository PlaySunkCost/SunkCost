# Basket confetti and sound — 27 September 2026

Branch `codex/basketball-physics-scoring`, working changes on `2e83a37`.
Unity 6000.6.0f1, editor host + rendered Windows non-host client, local Tugboat
loopback without injected delay/loss. Build identity: `test-build.json`.

`matrix.log`: **MATRIX_PASS**.

- Both hoops create exactly one live confetti burst and one playing AudioSource
  chime on their first basket; short-effect cleanup verified after two seconds.
- Confetti screenshot visually reviewed (`confetti.png`). Score label pulses;
  pieces are unlit, two-sided world geometry without colliders.
- Existing bounce, both rims, off-centre scores, misses/upward rejection and
  real host/client throws still pass.
- A late joiner receives the accumulated score with zero old confetti/chimes.
- The guest's next score produces one burst/chime at the west hoop, none at
  the other hoop; both backboards receive the score (`guest-score.txt`).
- No replication exception signatures in the passing guest log.

Sound verification covers AudioSource playback, spatial configuration and
replicated cue counts; no human listening approval is claimed. Clip is original
synthesized three-note audio, routed through AudioDeviceService. Pieces/audio
are presentation only and emit no monster-hearing event.

Network review: only server-accepted scoring invokes the unbuffered observer
cue; no client request or score authority change. It is intentionally not
buffered or reconstructed from saved Baskets, so late loads do not replay it.
Human review is still required before merging the new RPC/contract addition.
Steam across two computers, packet loss and four-player play were not tested.
