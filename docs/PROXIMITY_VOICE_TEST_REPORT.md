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
