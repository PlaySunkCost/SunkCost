# Who is talking — 17 September 2026 — voice MATRIX_PASS, spectate MATRIX_PASS (209 rows)

Dan's mission: "an indicator to who sounds coming from (Player x… / TV…),
something nice top right."

## Built
- `VoicePlayback.Level`: the RMS of each decoded frame. Frames arrive whenever
  a speaker's microphone is on, so the level, not the arrival, says whether
  they are saying anything.
- `ProximityVoice.Heard`: rebuilt by the mixer every frame — each receiver with
  a gain above zero whose decoded audio is louder than `SpeechLevel` (0.01),
  held 0.4 s past the last loud frame, with the frame's route byte. Nothing sent.
- `PlayerHudUI.DrawHeard`: top right under the ON AIR mark, one line per
  speaker — `Idan`, `Idan · TV`, `Idan · via Dan`, `Idan · dead` — green dot,
  the visor's right-aligned style; on deck, in the corner. The listener's own
  list, drawn beside the visor (not through `DrawVisor`, which is the watched
  player's screen).

## Runs
- `voice` (one guest, generated tone) — MATRIX_PASS ([voice-matrix.log](voice-matrix.log)).
  New rows: at 2 m the guest is listed, direct; at 24 m it drops off. Also fixed
  a row that had been stale since the route byte was added to the peer line
  ("reliable stop clears remote playback").
- `spectate` (two guests) — MATRIX_PASS, 209 rows ([spectate-matrix.log](spectate-matrix.log)).
  New rows: dead A's list shows the host `via` the watched player (route 1),
  living B's shows the host direct (route 0), B on deck shows the host through
  the TV (route 3).

Not exercised: the dead-to-dead line (no row where two dead players talk), and
the look of it on a real screen — the matrices read the list, not the pixels.
