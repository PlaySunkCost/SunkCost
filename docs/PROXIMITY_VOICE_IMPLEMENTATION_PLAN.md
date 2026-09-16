# Proximity voice and audio devices — implementation handoff

Status: prototype implementation on `codex/proximity-voice`, rebased onto the day
state/payday main at `57d20c7` on 16 September 2026. The original handoff below
was written 15 September on `codex/proximity-voice-plan` from main `0bb99c2`.
See section 14 and [test report](PROXIMITY_VOICE_TEST_REPORT.md) for actual
implementation and verification status; original acceptance rows are not claims
that their hardware/Steam tests have passed.
Read [DESIGN](DESIGN.md), [NETWORK_CONTRACT](NETWORK_CONTRACT.md),
[CONVENTIONS](CONVENTIONS.md) and [WORKFLOW](WORKFLOW.md) before implementation.

## 1. Outcome and confirmed decisions

Two to four connected players can talk while walking around HQ or the ship.
Voices originate at the speaker and get quieter with distance. Settings let each
player choose and test their microphone and headphones/speakers inside the game.

- P toggles microphone transmission ON/OFF; it is not hold-to-talk.
- Audio settings also has a **Mute microphone / Unmute microphone** button,
  using the same action (user addition, 16 September).
- Start OFF on every join, reconnect and newly hosted session. Device and volume
  preferences persist locally; microphone enabled state never persists.
- Keep transmitting and receiving while Alt-Tabbed if already enabled. P is a
  game shortcut, not a global Windows hotkey. Returning focus must not toggle it.
- Default microphone and sound output are Automatic (system default). Both have
  in-game device dropdowns. Output means all game sound, including voice.
- Include a microphone test with level bar, and an output test with level bar and
  audible test sound. A moving output bar proves samples exist, not audibility.
- Initial proximity: full distance gain within 2 m; fade smoothly to zero at
  20 m. These numbers are tuning, editable without code changes.
- This is the basic feature. Underwater filters, walls muffling voices, radios,
  monster reactions to speech, lip sync and voice recording are out of scope.

Implementation defaults below are engineering choices, not extra user decisions.
Networking and contract changes still need the human review in CONVENTIONS.

## 2. Verified baseline and architectural decision

The inspected project uses Unity 6000.6.0f1, FishNet 4.7.3,
Steamworks.NET 2025.164.1 and imported FishySteamworks. Session/admission code is
under `Assets/_Project/Scripts/Net`; authenticated FishNet broadcasts already
exist in `PrototypeAuthenticator`. Local/LAN and Steam modes both exist.
`PrototypePlayer` has a camera/AudioListener enabled for the local player;
`PrototypeSessionController` manages the menu preview listener. World flow moves
players between additive scenes. No voice capture/codec implementation was found.

DESIGN section 10 previously selected Steam's built-in voice API. That API
captures its own microphone stream; it exposes neither a device argument for
capture nor an encoder accepting our separately captured PCM. Therefore do not
combine a Unity microphone dropdown with Steam GetVoice and claim the selected
device is used. The newly requested device controls supersede that assumption.

Planned replacement: a small native audio-device adapter using **miniaudio** for
selectable capture/output, **libopus** for compression, and the **existing FishNet
connection** for transport. Steam P2P, authentication and gameplay authority stay
unchanged. No new hosted service, login or voice lobby. This is a justified audio
dependency, not a networking-stack replacement. Nothing is installed by this plan.

Use upstream sources with their licenses, pin exact immutable revisions during
step A and record hashes/build commands. Build Windows x64 and Linux x64 plugins
to match the existing build targets; no claim of other platforms. Retain license
notices. Do not commit downloaded opaque DLLs with unknown provenance.

## 3. Step A: prove device routing before voice networking

This is a mandatory implementation gate, not evidence already obtained.

1. Inspect the installed Unity 6000.6 API for supported output-device selection.
   If it supports the whole required route, prefer it and document the verified
   API. Do not invent `AudioSettings.outputDevice` or change the OS-wide default.
2. Planned fallback: capture Unity's final listener mix with a verified listener
   DSP tap, copy into a bounded native ring buffer, and silence that Unity output
   buffer. miniaudio plays the copied stereo mix on the chosen output device.
   Both Automatic and explicit outputs use one route; never play a duplicate mix.
3. Prove the tap includes every game/voice source, survives AudioListener handoff,
   works without microphone access, and keeps Unity's DSP running in background.
   A listener `OnAudioFilterRead` tap is a candidate to test, not an assumed
   verified final-mix hook. If it fails, use a verified Unity native mixer effect
   for the tap and document its insertion point before continuing.
4. Open a chosen capture endpoint independently from playback; separate device
   clocks/rates require resampling and bounded drift correction. Request a
   canonical mono 48 kHz voice stream and stereo playback at the negotiated rate.
5. Test two actual output endpoints, two microphones if available, hot unplug,
   default change, device busy, zero devices, 44.1/48 kHz, background execution
   and editor Play/Stop. A missing hardware test remains explicitly outstanding.
6. Verify Windows and Linux standalone loading plus the project's configured
   scripting backend. Missing plugin must show a clear voice error and preserve
   ordinary Unity game audio, rather than crash or silently mute the game.

If routing cannot meet these checks, stop dependent networking work and report
the concrete failing case. Do not quietly reduce Output to voice-only or replace
the dropdown with instructions to use OS settings.

## 4. User interface and lifecycle

Add an Audio panel accessible before joining and from the in-session menu.

| Control | Behaviour |
|---|---|
| Microphone | Automatic plus available input names; remember backend device ID, not list index |
| Sound output | Automatic plus available output names; applies to game and voice |
| Test microphone | Explicit start/stop, RMS bar and clipping indication; no network transmission during test |
| Play test sound | Short comfortable stereo left/right cue, output meter, Stop control |
| Master volume | Affects all playback; initial 100%, adjustable 0–100% |
| Voice volume | Affects received voices only; initial 100%, adjustable 0–100% |
| Crew controls | Local per-player mute and volume; do not mute that player's game sounds |
| Mic status | OFF / ON / testing / unavailable text and icon; do not depend on colour alone |

Microphone ON means capture is enabled, not that another player can hear you.
A separate activity indication derives from sample level. Show remote speaking
activity only for currently audible eligible speakers; no across-map tracking.
Do not play the local voice back in normal use. The microphone test is a bar-only
test initially; output is tested with a generated cue, avoiding feedback loops.

Opening settings or the ordinary pause menu does not mute live voice. P must not
fire while editing a text field or binding a key; a clickable mute button remains
available. Settings test temporarily stops transmission and finishes muted with
an explicit 'Press P to enable microphone' hint. Switching a microphone also
finishes muted. Closing/leaving/disconnecting always closes capture and clears
queued speech. Alt-Tab and an ordinary scene fade do not count as leaving.

Automatic follows system-default device changes. An explicit unplugged microphone
stops capture and shows 'Selected microphone unavailable'; never secretly switch
to a laptop microphone. An explicit missing output falls back to Automatic with a
visible notice. Refresh device lists without using display names as unique IDs.
When duplicate names exist, disambiguate them. Recover audio configuration changes
without recreating the network session or interrupting player movement.

## 5. Audio pipeline and ownership

Capture -> bounded PCM ring -> Opus encode worker -> bounded outgoing queue ->
FishNet on main thread -> host relay -> client jitter queue -> decode worker ->
per-speaker PCM ring -> Unity positional source -> master output route.

- One local capture device/encoder; one decoder and bounded queue per remote
  talker. No host self-playback and no duplicate host/client receive path.
- Start with mono 48 kHz, 20 ms (960 sample) frames, Opus VOIP mode, 24 kbit/s
  target and constrained bitrate. Do not send uncompressed PCM in production.
- Start jitter target at 60 ms; cap buffered speech at 200 ms and discard stale
  frames rather than replaying a long backlog. Sequence gaps use bounded Opus
  packet-loss concealment; prolonged silence resets playback state.
- Use one consistent frame duration and sample rate for protocol v1. Never trust
  packet-provided allocation sizes. Set Opus packet cap to 400 bytes; configure
  encoder max output accordingly. Benchmark intelligibility rather than claiming
  codec settings alone guarantee acceptable voice.
- No Unity/FishNet calls, allocations, logging, blocking locks or device
  reinitialization in audio callbacks. Use preallocated single-producer/consumer
  rings with explicit ownership, shutdown flags and counters. Workers use native
  handles safely; dispose after callbacks/workers stop, including domain reload.
- Unity and playback endpoint clocks will drift. Resample/adjust consumption to
  maintain ring occupancy without indefinite accumulation; log counters only.
- Do not capture system loopback audio. Do not save or log voice payloads.
  Acoustic echo cancellation/noise suppression are not delivered by this basic
  plan; test with headphones and document speaker echo as a known limitation.

## 6. Position, range and eligibility

Use the real player mouth/head anchor, with a root-height fallback. Set voice
sources to 3D, Doppler off, no reverb/occlusion in v1. Distance gain is 1 at d<=2,
0 at d>=20, otherwise `1 - smoothstep(0, 1, (d-2)/18)`. Apply distance once,
then per-player, Voice and Master gain. Do not accidentally apply Unity default
rolloff on top of the custom curve. Stereo direction follows the listener view.

For normal living players the listener origin is their actual player, not a
preview camera or unrelated spectator camera. Only players in the same logical
world and allowed voice group receive proximity packets. Across-scene coordinates
must not accidentally establish hearing. Through-wall hearing is intentional v1.

Current HQ/ship harness has no complete dead/surfaced roster. Use admitted living
players in the same world for this slice. Expose a server-owned eligibility
adapter so future day/death code supplies membership; unknown/loading membership
denies delivery. Do not build the entire death feature to ship basic voice.

Preserve NETWORK_CONTRACT section 9: dead players speak only to dead players;
surfaced players only to surfaced players; divers use proximity. Camera switches
never grant transmit rights. DESIGN's future 'dead hear what the selected diver
hears' needs an explicit server-approved listen-only spectating route, separate
from speaking membership. That route and spectator UI are deferred here; do not
silently enable it based on AudioListener position or erase the design decision.

## 7. Wire protocol and host routing

Use authenticated FishNet broadcasts, not ObserversRpc on a moving player root.
Global session lifetime avoids voice service destruction at scene changes.
Check exact FishNet signatures and transport MTU in the installed package.

Client frame: protocol version, server-issued connection/session generation,
transmit epoch, uint sequence, exactly one bounded encoded frame. Host-to-client
frame adds speaker identity derived by the host from the authenticated connection.
Never accept client-asserted speaker IDs, positions, recipient lists or voice group.

Use reliable start/stop control to request a transmit epoch; host acknowledges it.
Do not send frames before its acknowledgement. Host tracks active epoch and
rejects frames after Stop or from old epochs. Relay stop reliably to recipients
so they clear queued speech; epoch checks must tolerate cross-channel ordering.
On rejoin use a fresh generation even if FishNet reuses a client ID.

Voice frames use Channel.Unreliable; no retransmission backlog. Custom bounded
reader rejects oversize lengths before allocating. Rate-limit each connection
to 60 frames/s with a small bounded burst, and <=32 KiB/s total voice bytes.
Validate admitted membership before processing. Duplicate, invalid, truncated,
stale and out-of-window sequences are discarded using wrap-safe comparisons.

Host sends only to eligible nearby recipients, including its own local playback
once. Use up to 22 m relay cutoff to tolerate replicated-position delay; clients
still apply exact 20 m silence. Cap recipients at admitted crew (maximum four).
Clients re-check local mute/world/epoch before playback and clear buffers when
membership changes. No all-lobby plaintext voice broadcast filtered only by gain.

Average encoded payload estimate: 24 kbit/s = 3 kB/s per talker before headers.
Worst four-talker relay is 4*3*3 = 36 kB/s payload across recipient deliveries;
host's local deliveries are not network egress. Measure real overhead/CPU/loss.
No claim that this estimate is a measured bandwidth result.

## 8. Scene changes, startup and integration

- Initialize service once per network session; audio settings may exist at menu.
  Binding player emitters is driven by spawn/despawn and owner changes.
- Keep requested mic state during travel, but suspend packet delivery while
  routing membership is unknown; discard those samples, never replay them later.
  Resume with a fresh epoch once eligible. New session always starts muted.
- Follow player listener handoff during sail/load without two output taps or two
  AudioListeners. Preserve existing WorldSceneFlow observer/load workarounds.
- Enforce background running and verify actual capture/playback while minimized;
  do not reuse the movement controller's focus-loss input reset as a voice mute.
- Upgrade the admission protocol/build identity as appropriate for the new
  wire format. Older incompatible builds must fail admission clearly.
- Local/LAN uses the same codec/relay without Steam capture requirements. It is
  development coverage; Steam acceptance still needs separate machines/accounts.

## 9. Files and folders to create or integrate

All names below are proposed new files unless identified as existing.

| Location under Assets/_Project | Responsibility |
|---|---|
| Scripts/Audio/AudioDeviceService.cs | Enumeration, defaults, persistence, device changes |
| Scripts/Audio/NativeAudioBridge.cs | Small C ABI wrapper; native handle lifetime |
| Scripts/Audio/GameOutputRouter.cs | Single final mix route and listener handoff |
| Scripts/Audio/AudioSettingsUI.cs | Dropdowns, meters, test controls and volume |
| Scripts/Voice/VoiceSession.cs | Local lifecycle, toggle, startup/shutdown |
| Scripts/Voice/VoiceCapture.cs | Capture/encode pipeline and microphone meter |
| Scripts/Voice/VoiceMessages.cs | Versioned bounded serializers and messages |
| Scripts/Voice/VoiceRelay.cs | Authentication, limits, identity, epoch and routing |
| Scripts/Voice/VoiceEligibility.cs | World/group adapter, future day-state seam |
| Scripts/Voice/VoicePlayback.cs | Speaker binding, jitter/decode and positional source |
| Scripts/Voice/VoiceSettings.cs | Serialized tuning asset definition |
| Scripts/Voice/VoiceStatusUI.cs | Local mic state and audible speaker indication |
| Settings/Audio/VoiceSettings.asset | Proximity/codec/queue defaults |
| Audio/AudioMixer.mixer | Master/game/voice separation if needed for routing |
| Plugins/Audio/ | Built first-party native bridge binaries and importer metadata |
| Native/Audio/ | Bridge source, CMake/build scripts, pinned dependency manifest |
| Tests/Editor/Voice/ | Wire, routing, queue and attenuation unit tests |
| Editor/Voice/ | Repeatable setup, validation and runtime test hooks |

Vendored upstream sources/licenses belong in `Assets/ThirdParty/Audio/` with
plugin import exclusions; preserve upstream content and wrap it. Prefer build-time
source dependencies outside Unity import if practical and document exact paths.
Namespaces: SunkCost.Audio and SunkCost.Voice. Match the actual assembly layout.

Integrate narrowly with existing `PrototypeSessionController`, `PlayerHudUI`,
player spawn lifecycle and `CrewDayState`/`WorldSceneFlow` membership. Inspect exact
APIs before editing. Coordinate Session/player prefab and mixer edits with owners;
provide idempotent editor setup and validator instead of undocumented manual steps.
Preserve GUIDs. No edits to FishNet, FishySteamworks or Steamworks.NET source.

## 10. Implementation sequence

A. Device routing proof in section 3; document actual native revisions, output tap,
   platform results and reproducible build instructions.
B. Local audio settings, tests, default/hotplug behaviour and cleanup.
C. Local capture/codec roundtrip through bounded queues; synthetic audio only in
   automated checks. Verify no accidental normal-use self-monitoring.
D. Authenticated host relay and two-client audible voice; both directions and
   two non-host clients talking to each other.
E. Proximity, status, mute/volume, scene transition and reconnect integration.
F. Four-player/load tests, docs, reviewer handoff and PR. Do not mark complete
   merely because it compiles or an MCP hook reports a nonzero amplitude.

## 11. Acceptance matrix

Record revision, platform/backend, endpoints, host/client roles, actual latency
and result for each row. Never invent an audio listening result.

| ID | Scenario | Required result |
|---|---|---|
| V1 | Join/rejoin/re-host | Muted each time; no queued previous-session audio |
| V2 | P ON/OFF, menu/text entry | One toggle per press; no accidental text-field toggle; OFF stops delivery |
| V3 | Input selection and bar | Chosen mic alone drives meter; preference survives restart |
| V4 | Output selection and test | Cue, game sound and voice go only to chosen endpoint; bar tracks test |
| V5 | Automatic/hotplug/permission failure | Correct default follow/fallback; missing explicit mic stays muted |
| V6 | Host/client and client/client | Clear intelligible speech both ways, no self-echo or host duplication |
| V7 | 1, 2, 11, 20, 22 m; rotate view | Full nearby gain, monotonic fade, zero at 20+, correct stereo direction |
| V8 | Player mute/voice/master volume | Local controls work independently; muted speaker's footsteps remain |
| V9 | Alt-Tab/minimize and return | Enabled voice continues both ways; P typed elsewhere does nothing |
| V10 | Sail HQ->sea->HQ, late join | No duplicate services, leaking cross-world speech or stale audio |
| V11 | Disconnect while speaking, reused IDs | Immediate cleanup; stale generation/epoch cannot speak |
| V12 | Four simultaneous talkers | Usable levels, bounded memory/CPU and queues; no severe clipping |
| V13 | Reorder/loss/latency | No growing speech delay; recovery after loss; measured conditions recorded |
| V14 | Malformed/flooded/unauthenticated frames | Rejected before unbounded allocation or unauthorized relay |
| V15 | Unknown/dead/surfaced routing fixtures | Membership gates obey adapter policy independently of camera |
| V16 | Play/Stop cycles, device switch, DSP rates | No callbacks into disposed memory, crashes, pitch errors or duplicate output |
| V17 | Two Steam machines and Windows/Linux builds | Real transport plus audible device validation; unsupported rows clearly outstanding |

Unit tests: attenuation boundaries; eligible recipient sets; input limits and
sequence wrap; stale epochs; queue overflow/underflow; defaults and missing IDs.
Use Unity compilation, meaningful tests, runtime logs and MCP if connected.
MCP state/meter inspection cannot establish microphone fidelity or what a person
hears. Human listening on separate Steam machines is required for that claim.

## 12. Documentation and delivery

Update README with P, Audio panel, both tests, defaults, Alt-Tab behaviour and
troubleshooting. Update DESIGN's voice implementation note to the verified audio
backend and retain all user-facing decisions above. Update NETWORK_CONTRACT with
authenticated voice routing/identity, epochs and membership isolation; it must not
grant speech permission based on a spectating camera. Add a test report listing
all unrun rows and dependencies/licenses/build instructions. Obtain teammate
network review before merge. Commit/push implementation and open its PR only when
requested by that implementation task; this document itself authorizes no build.

This plan is ready to hand to a builder, with step A an explicit engineering gate.
It is not proof that native device routing or multiplayer audio already works.

## 13. Primary references checked 15 September 2026

- [Steam Voice integration](https://partner.steamgames.com/doc/features/voice)
  and [ISteamUser](https://partner.steamgames.com/doc/api/ISteamUser): capture,
  compression/decompression and separately supplied transport.
- [miniaudio manual](https://miniaud.io/docs/manual/index.html) and
  [device enumeration example](https://miniaud.io/docs/examples/simple_enumeration.html):
  selectable capture/playback endpoints and callback constraints.
- [miniaudio upstream](https://github.com/mackron/miniaudio) and
  [Opus upstream](https://github.com/xiph/opus): source/build/license starting points;
  no particular revision has been installed or compiled by this planning task.
- [Unity AudioSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.html)
  and [audio filter callback](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html):
  baseline documentation; inspect the installed 6000.6 API and verify routing.

## 14. Implementation handoff — 16 September 2026

Runtime code is in `Assets/_Project/Scripts/Audio/`; native source, pinned
dependency build and licenses are in `Assets/_Project/Native/Audio/`; platform
binaries are in `Assets/_Project/Plugins/Audio/`. Resources holds the voice tuning
asset and master mixer. Session UI creates one voice/device/settings service;
there are no shared scene/prefab changes for this feature.

The listener OnAudioFilterRead experiment failed. The implemented fallback is a
native **Sunk Cost Output** effect on `SunkCostOutput.mixer` Master. Existing
ship engine, generated cue and remote voice sources route there before playing.
Future game audio sources must do the same. Automatic and explicit headset
routes carried nonzero samples. This verifies routing code, not human audibility.
Output playback now prebuffers 50 ms; capture/device and codec workers are shut
down before the native context. Configuration changes recreate procedural clips.

`ProximityVoice` handles authenticated routing, epochs, current day/scene/Below
membership, volume and lifecycle. `VoiceCapture` and `VoicePlayback` own bounded
worker queues. `VoiceMessages` bounds payloads before allocation; protocol is 2.
`VoiceSettingsUI` includes the requested mute button, device dropdowns, local
tests/meters and player controls in both Local and Steam sessions. An input
device/default change mutes deliberately; the user unmutes the new input.

New main's day/payday rules are retained. Current players have no death state;
the future death/spectator cohort must be added before dead-player voice is enabled.
The host independently invalidates an enabled stream when world eligibility
changes, so it does not depend on a cooperative client's stop request.

Use `VoiceChecks.Run()` for codec/serializer/sequence/gain checks and the editor
job `CameraClearanceMatrixDriver.Start("voice")` after building the exact current
tree with `HQPrototypeBuild.BuildWindowsLocalDevelopment()`. It uses generated
tones only and a separate Local client; it never captures a microphone or joins
Steam. Evidence lives in `Library/VoiceVerification`, which survives editor exit.
Stop the editor host with `CameraClearanceMatrixDriver.StopCleanly()`.

Real two-machine Steam listening, hotplug/permission fixtures, Linux runtime,
four simultaneous speakers and network impairment remain release acceptance
work. Human network review remains required; no review is claimed here.
