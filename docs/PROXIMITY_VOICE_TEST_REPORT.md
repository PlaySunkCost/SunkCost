# Proximity voice — verification

Branch: `codex/proximity-voice`. Unity 6000.6.0f1, Windows x64/Mono.
Implementation started at `ae07286`, then the user's rebase brought in main
`57d20c7` (day state/payday) with WIP commit `d34590f`. No merge approval implied.

## Evidence so far

- Unity compilation and Windows Local development build passed before rebase.
  Main's updated build/CI workflow is retained; the Windows build passed after
  the rebase. Final editor checks passed after the buffer/lifecycle fixes.
  Existing shader/API deprecation warnings are not voice compilation failures.
- `VoiceChecks.Run()` passed before and after rebase: sequence rollover,
  reordered/duplicate/stale sequences, distance endpoints and midpoint, bounded
  frame serialization/invalid lengths, 100 encoded/decoded Opus frames and PLC.
  Largest test packet 93 bytes (limit 400); decoded peak 0.105.
- Native enumeration: 2 inputs, 7 outputs. Local microphone test opened the
  system-default SteelSeries Sonar microphone and measured RMS 0.0003195.
  No microphone audio was saved or transmitted by these checks.
- Unity listener OnAudioFilterRead produced no callbacks and was removed.
  The native Master mixer produced callbacks and nonzero samples on Automatic
  (SteelSeries Sonar Gaming) and explicit Arctis Nova 7 Gen 2 headphones.
  Example explicit route: 11,164 mix callbacks, 3,525 nonzero output callbacks,
  peak 0.04. Those numbers establish samples, not human-confirmed sound.
- Separate Windows Local client and editor host exchanged generated 440 Hz
  tones over authenticated FishNet. Host source had no self-playback. Examples:
  6,777 host frames sent; 4,268 client frames received at host; 10,199 forwarded
  frames. Remote decoder and native output counts increased in both directions.
- Per-player mute discarded playback; sender mute stopped sending and cleared
  the receiver epoch. At a test separation beyond 22 m, relay count stayed
  exactly 7,744 while host sent count increased 5,489 → 6,002. The distant player
  fell outside the HQ floor during that fixture and was restored by the hook;
  this was a range gate test, not a walking-path test.
- Leaving reset mic=false, epoch=0, world=-1; no pending microphone state carried
  into a new session. Final repeatable matrix adds re-host and sailing assertions.

## Repeatable final matrix

Run `CameraClearanceMatrixDriver.Start("voice")` after building the exact tree.
It records `Library/VoiceVerification/matrix.log`, guest logs and a settings
screenshot. The final repeated two-process run finished **MATRIX_PASS** after
the user resolved Windows Application Control's initial launch block. No Windows
security policy was changed by this task. See the
[final two-process log](test-runs/2026-09-16-voice/matrix.log).

The final run verified both Opus directions, no host self-playback, native output,
per-player mute/unmute, voice volume, gain at 2/11/20/24 m, host relay cutoff,
returning into range, sender mute with remote epoch cleanup, sailing with fresh
stream recovery, graceful client disconnect, leaving and muted re-host. The first
attempt incorrectly killed the client and expected transport disconnect detection
within 15 s; the corrected row uses the actual Leave action. Abrupt network-loss
timing remains a separate unverified case. A host RTT sample during local testing
was 20 ms; this does not measure end-to-end speech latency or Steam RTT.

The fallback editor job `CameraClearanceMatrixDriver.Start("voice-host")` passed:
native output samples, muted initial state, acknowledged generated-tone sending,
HQ → Sea → HQ, zero frames sent during travel, fresh streams after arrival,
mute action, leaving, re-hosting and reset to muted. No Unity errors were logged.
See [recorded host log](test-runs/2026-09-16-voice/host.log) and the
[visually inspected settings screenshot](test-runs/2026-09-16-voice/audio-settings.png).
This separate **HOST_MATRIX_PASS** additionally verifies the return voyage.

Generated tones never access the mic; normal settings/P are the user path for
actual speech. The automatic approval review rejected automatically enabling
real microphone transmission without destination-specific user authorization.

The final review also added host-driven epoch invalidation on membership change,
preserved rate limits across repeated start/stop, suppressed redundant stopped
announcements, and discarded stale PCM when a silent/virtualized source resumes.
The user rebased/committed the WIP; the follow-up commit records these fixes.

## First human listening — 17 September 2026 (Dan, one machine, Local)

Editor host + the local client build on the same PC, Dan on a headset, driven
by Claude through the peer commands. What was heard and measured:

- **Test sound**: the left/right cue on the default output — heard on both ears.
- **Client tone → host**: the client's generated 440 Hz "voice" relayed by the
  host — heard, "a bit low" (the test tone is deliberately at ~10 % amplitude;
  distance 4.8 m, gain 0.94). At that point the host decoded only **79 of 527**
  received frames; over a measured 20 s window **117 of 835** (14 %).
- **Dan's mic → client** (P in the editor, the client plays it back on the same
  headset): 616 frames captured and sent in ~12 s, all relayed, but the client
  decoded ~6 frames/s — Dan: "I heard it bad, maybe 50 % of the word".
- **Cause** (`VoicePlayback.Run`): the decoder was allowed to run only two
  frames (40 ms) ahead of the audio consumer (`write - read <= 1920`), while
  Unity pulls a streaming clip in blocks it decides (1408–2816 samples seen) at
  its own cadence; most of each pull found nothing to play. **Fix**: decode at
  the pace frames arrive, bounded by a 65536-sample ring, and wait for a missing
  frame until three later ones have arrived before concealing it. After the
  fix: **892 of 892** and **1419 of 1419** frames decoded, zero concealed; Dan's
  speech decoded at ~52 frames/s — Dan: **"I hear it very good."**
- **Remaining click** on the *test tone* once per second: the tone is paced by a
  software timer at 48.3 frames/s against the output's 50/s, so the ring drains
  one frame per second (30 underruns in 29 s). A test-signal artefact — the
  microphone is clocked by the audio device and showed none — but the same
  mechanism would surface with a real clock drift between two machines' devices
  (44.1 vs 48 kHz, cheap USB mics): drift compensation (re-prime on underrun or
  adaptive resampling) is still to do.
- **Second round** (after both fixes, fresh host and client): Dan talked for a
  minute — decoded at 52 frames/s throughout, zero concealed, "I hear myself
  good". The tone, now stopwatch-paced (49.8 frames/s measured), had **2
  misses in 21 s** instead of ~20: most likely the editor host's own main-thread
  hitches (incoming packets are handed over on the main thread; the editor
  stalls 50–100 ms now and then, longer than the 60 ms cushion). A built game
  hitches far less; an adaptive jitter buffer is the proper follow-up.
- **Rounds three to six — the jitter buffer.** With the decoder fixed the tone
  still stopped twice per stream, always near the start. The client build was
  smooth (237 fps, worst frame 39 ms), so the sender was not it. Found and
  fixed in turn: (1) the receiver held a fixed 60 ms and forgot it at every
  unmute — now an adaptive jitter buffer the way voice apps do it: starts at
  100 ms, follows the measured arrival gaps (1.5 x the worst gap, up to
  300 ms), grows a frame on every underrun, relaxes a frame per 10 s of calm,
  and is remembered per player across streams; (2) the cushion was only
  enforced after an underrun — a stream now pre-buffers before its first
  sample plays; (3) Unity's streaming reader tops itself up in bursts of two
  2816-sample blocks, so the cushion is floored at two blocks plus a frame
  (about 137 ms) and the first play of a stream waits for cushion + three
  blocks. Round six: 2049 frames, **Dan counted zero stops in 41 s**.
- What that does and does not promise: any arrival gap shorter than the
  cushion is silent; the buffer grows to what a link shows, to 300 ms; a lost
  packet is concealed by Opus (a 20 ms blur, not a stop). A stall over 300 ms
  still gaps, and slow clock drift between two machines' sound cards is not
  compensated yet (time-stretching later).
- Not covered: two machines, Steam, a second person's ears, latency, echo.

## Still requires people/hardware

- Audible, intelligible speech and correct physical endpoints on two Steam
  machines; host/client/client-client, measured latency, no echo, keyboard P,
  settings clicks and background focus behaviour.
- Physical device unplug/replug, default change, permission denied, busy/no
  devices, 44.1/48 kHz and long clock-drift listening.
- Linux native runtime/player and the Linux-editor-to-Windows CI build. Linux
  native compilation succeeds; there is no Linux runtime validation claim.
- Four simultaneous speakers, actual loss/jitter/latency injection and prolonged
  memory/CPU/GC observation. Pure sequence tests do not prove impairment quality.
- Future dead/spectator cohorts when their authoritative gameplay state exists.
  Current scene/day/Below membership is implemented independently of cameras.
- Human teammate networking review before merge.

## Native artifact hashes (SHA256)

- Windows `AudioPluginSunkCostAudio.dll`:
  `8ccd2b1eac0a345cf0bfb16e47f3a30682a055f1cb4510426bdd08c11e7ff4b8`
- Linux `libAudioPluginSunkCostAudio.so`:
  `418f51ea258a9d6cc99ae3a3aee9f675ce56e54367de6617ec200081e7eb31f4`

Sources, immutable dependency revisions, compiler hash and licenses:
[native audio README](../Assets/_Project/Native/Audio/README.md).
