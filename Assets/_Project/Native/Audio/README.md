# Native voice audio

First-party adapters: `voice_audio.c` (devices, bounded rings, codec ABI) and
`mixer_route.cpp` (Unity master mixer effect). Do not edit upstream dependencies.

Pinned dependencies, fetched by `build.py` into ignored `Library/VoiceBuild/deps`:

| Source | Revision | License notice |
|---|---|---|
| https://github.com/mackron/miniaudio | `4a5b74bef029b3592c54b6048650ee5f972c1a48` (0.11.21) | Licenses/miniaudio.txt |
| https://github.com/xiph/opus | `ddbe48383984d56acd9e1ab6a090c54ca6b735a6` (1.5.2) | Licenses/opus.txt |
| https://github.com/Unity-Technologies/NativeAudioPlugins | `bc7893edbba4c8a592777e590e34f21a11d762b4` | Licenses/unity-audio.txt |

Build with Python 3, Git, and Zig **0.13.0**, from the repository root, with Unity
and game processes closed (Windows locks loaded native libraries):

```text
python Assets/_Project/Native/Audio/build.py <path-to-zig> windows
python Assets/_Project/Native/Audio/build.py <path-to-zig> linux
```

Windows compiler archive from ziglang.org/download/0.13.0/:
`zig-windows-x86_64-0.13.0.zip`, SHA256
`d859994725ef9402381e557c60bb57497215682e355204d754ee3df75ee3c158`.
Do not store build dependencies in Unity's Temp folder: Unity deletes it on exit.
The script builds x86_64 Windows GNU and x86_64 Linux GNU with portable Opus C
sources. No separately installed Opus library is required. Commit runtime DLL/SO
and their Unity importer metadata, not linker PDB/LIB or compiler caches.
Windows Mono loading/building is tested; Linux runtime and IL2CPP remain unverified.

`Resources/SunkCostOutput.mixer` routes its Master through **Sunk Cost Output**.
Native DSP applies master gain, writes stereo to the selected miniaudio endpoint,
then silences the Unity route to prevent duplicate playback. When no native output
is open it passes audio to Unity instead. The first listener OnAudioFilterRead
prototype was rejected because it received no callbacks. The actual mixer was
verified with generated audio on Automatic and an explicit headset endpoint.

**Every new AudioSource must call `AudioDeviceService.RouteSource(source)` before
Play, or be authored into SunkCostOutput Master.** Current ship engine, voice
sources and output test follow this rule. Additional authored mixers must feed
that Master. Do not advertise a source as device-selectable if it bypasses the mix.
RunBackground is enabled by the voice service; never use a second local listener.

Capture is 48 kHz mono; output is stereo at Unity's DSP rate, converted by
miniaudio to hardware format/rate. A 50 ms output prebuffer absorbs callback
jitter. Playback discards excess backlog over 200 ms; occasional stereo-frame
draining bounds independent clock drift. This is a prototype correction, not an
audiophile adaptive resampler; long hardware listening tests remain required.
Audio configuration changes recreate procedural clips. Explicit input loss
mutes; output loss falls back to Automatic. No mixer tap means no claimed output
selection. Output meters prove sample activity, not which speaker a human hears.
