# Voice prototype check — 16 September 2026

Unity 6000.6.0f1, Windows editor hosting Local, branch `codex/proximity-voice`
after rebase onto day/payday main `57d20c7`. Protocol 2.

`voice` finished **MATRIX_PASS** with a separate Windows Local client. The
[two-process log](matrix.log) covers codec delivery in both directions, distance
gain/cutoff, mute/volume, sailing, graceful disconnect and re-host.

`voice-host` also finished **HOST_MATRIX_PASS**. [Log](host.log) contains the exact
epoch/counter changes and assertions; [settings](audio-settings.png) was
visually inspected. Tests generated tones and did not transmit a microphone.

Initially the `voice` job could not start its separate player: Windows Application
Control blocked `Builds/HQPrototypeLocal/SunkCostHQ.exe` from both Unity and an
approved shell. No bypass/security-policy changes were attempted. The user was
asked to approve the locally built game through their usual Windows process, and
confirmed the executable worked. The subsequent final run passed. The first
running attempt used a hard process kill with an incorrect 15 s disconnect
expectation; the final test uses the normal Leave action. Abrupt loss remains unrun.
Rebuild from the final checkout before rerunning so admission revisions match.

Earlier successful two-process checks and outstanding Steam/hardware checks are
listed in the [test report](../../PROXIMITY_VOICE_TEST_REPORT.md). This result does
not claim a two-machine Steam pass, audible microphone quality or teammate review.
