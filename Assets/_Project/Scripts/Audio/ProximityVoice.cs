using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Audio
{
    // Session-scoped voice transport. No gameplay RPCs, SyncVars or physics writers.
    public sealed class ProximityVoice : MonoBehaviour
    {
        private sealed class Sender
        {
            public uint Request; public ulong Generation; public int World;
            public bool Enabled;
            public readonly VoiceSequenceWindow Sequence = new();
            public float Tokens = 10, Updated, ControlAt;
        }
        private sealed class Receiver
        {
            public ulong Generation; public VoicePlayback Playback;
            public float Volume = 1; public bool Muted;
            public float LastFrame;
            public byte Route; // how the last frame is to be mixed (VoiceRoute)
            public float HeardUntil; // the indicator holds a speaker this long after their last loud frame
            public int Cushion = VoicePlayback.MinCushion; // the jitter buffer this player's link needed, kept across streams
        }
        public AudioDeviceService Devices { get; private set; }
        public bool MicrophoneEnabled { get; private set; }
        public bool TestingMicrophone { get; private set; }
        public float MicrophoneLevel => capture?.Level ?? 0;
        public string Status { get; private set; } = "Microphone OFF";
        public float VoiceVolume { get; private set; }
        public bool Connected => session != null && session.InRoom && manager != null && manager.ClientManager != null && manager.ClientManager.Started;
        public bool TextEntryActive { get; set; }
        public uint SentFrames { get; private set; }
        public uint ReceivedFrames { get; private set; }
        public uint RelayedFrames { get; private set; }
        public uint RejectedFrames { get; private set; }
        public VoiceSettings Settings { get; private set; }
        public IEnumerable<int> PeerIds => players.Keys;
        // Who this listener hears right now, and by which route — for the
        // indicator (Dan, 17 September 2026: "who sounds coming from, Player x /
        // TV, top right"). A speaker is listed while their mixed gain is above zero
        // and their decoded audio is louder than SpeechLevel, held HeardHoldSeconds
        // past the last loud frame so words do not flicker. Rebuilt by Mix.
        public struct HeardSpeaker { public int Id; public byte Route; public float Gain; }
        public const float SpeechLevel = 0.01f, HeardHoldSeconds = 0.4f;
        private readonly List<HeardSpeaker> heard = new();
        public IReadOnlyList<HeardSpeaker> Heard => heard;
        public bool IsSelf(int id) => Connected && id == manager.ClientManager.Connection.ClientId;
        public string PeerName(int id) => players.TryGetValue(id, out var player) ? player.GetComponent<PlayerIdentity>()?.DisplayName ?? PlayerIdentity.Fallback(id) : PlayerIdentity.Fallback(id);
        private PrototypeSessionController session;
        private NetworkManager manager;
        private VoiceCapture capture;
        private readonly Dictionary<int, Sender> senders = new();
        private readonly Dictionary<int, Receiver> receivers = new();
        private readonly Dictionary<int, HQPlayerController> players = new();
        private readonly List<int> removed = new();
        private uint request;
        private ulong generation, nextGeneration;
        private ushort sequence;
        private int world = -1;
        private bool wasConnected;
        private bool bound;
        private float retryAt;
        private bool syntheticCapture;
        public string Diagnostics
        {
            get
            {
                string text = $"mic={MicrophoneEnabled}; test={TestingMicrophone}; tone={syntheticCapture}; world={world}; epoch={generation}; sent={SentFrames}; received={ReceivedFrames}; relayed={RelayedFrames}; rejected={RejectedFrames}; mix={Devices.MixCallbacks}; output={Devices.OutputCallbacks}; status={Status}";
                foreach (var pair in receivers) text += $"\nvoicePeer={pair.Key}; epoch={pair.Value.Generation}; muted={pair.Value.Muted}; route={pair.Value.Route}; decoded={pair.Value.Playback?.Decoded ?? 0}; plc={pair.Value.Playback?.Concealed ?? 0}; level={pair.Value.Playback?.Level ?? 0:0.000}";
                text += "\nheard=";
                foreach (HeardSpeaker speaker in heard) text += $"{speaker.Id}:{speaker.Route},";
                return text;
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void StartLocalTestTone()
        {
            if (!Debug.isDebugBuild || !Connected || session.BoundMode != SessionMode.Local) return;
            SetMicrophone(false);
            syntheticCapture = true; MicrophoneEnabled = true; retryAt = 0; Status = "Generated test tone — microphone NOT captured";
        }
#endif

        private void Awake()
        {
            Application.runInBackground = true;
            Devices = gameObject.AddComponent<AudioDeviceService>();
            Devices.InputChanged += OnInputChanged;
            Devices.BeforeShutdown += ResetLocal;
            Devices.ConfigurationChangedEvent += OnAudioReset;
            Settings = Resources.Load<VoiceSettings>("VoiceSettings");
            if (Settings == null) Settings = ScriptableObject.CreateInstance<VoiceSettings>();
            VoiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Voice", 1));
            nextGeneration = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
        }
        private void Start()
        {
            session = GetComponent<PrototypeSessionController>(); manager = session.NetworkManager;
        }
        private bool Bind()
        {
            if (bound) return true;
            if (manager == null || manager.ServerManager == null || manager.ClientManager == null) return false;
            manager.ServerManager.RegisterBroadcast<VoiceRequest>(ServerRequest);
            manager.ServerManager.RegisterBroadcast<VoiceFrame>(ServerFrame);
            manager.ClientManager.RegisterBroadcast<VoiceState>(ClientState);
            manager.ClientManager.RegisterBroadcast<VoiceFrame>(ClientFrame);
            manager.ClientManager.OnClientConnectionState += ConnectionChanged;
            manager.ServerManager.OnRemoteConnectionState += RemoteChanged;
            bound = true; return true;
        }
        private void ConnectionChanged(ClientConnectionStateArgs args) { ResetLocal(); }
        private void RemoteChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (senders.TryGetValue(connection.ClientId, out var sender))
                Publish(new VoiceState { Speaker = connection.ClientId, Request = sender.Request, Generation = sender.Generation });
            senders.Remove(connection.ClientId);
        }
        private void Update()
        {
            if (!Bind()) return;
            bool connected = Connected;
            if (connected != wasConnected) { ResetLocal(); wasConnected = connected; }
            players.Clear();
            foreach (var player in FindObjectsByType<HQPlayerController>())
                if (player.IsSpawned) players[player.OwnerId] = player;
            if (!manager.ServerManager.Started) senders.Clear();
            else
            {
                foreach (var pair in senders)
                {
                    var sender = pair.Value;
                    if (!sender.Enabled || sender.World == WorldOf(pair.Key)) continue;
                    sender.Enabled = false; sender.Generation = ++nextGeneration;
                    Publish(new VoiceState { Speaker = pair.Key, Request = sender.Request, Generation = sender.Generation });
                }
            }
            int localId = connected ? manager.ClientManager.Connection.ClientId : -1;
            int nextWorld = WorldOf(localId);
            if (world != nextWorld)
            {
                StopTransmission(); world = nextWorld;
                foreach (var receiver in receivers.Values) { receiver.Playback?.Dispose(); receiver.Playback = null; }
                retryAt = 0;
            }
            if (connected && Application.isFocused && !SessionInputGate.OverlayOpen && !TextEntryActive && Keyboard.current?.pKey.wasPressedThisFrame == true)
                SetMicrophone(!MicrophoneEnabled);
            if (MicrophoneEnabled && connected && world >= 0 && generation == 0 && Time.unscaledTime >= retryAt)
            {
                retryAt = Time.unscaledTime + 1f;
                manager.ClientManager.Broadcast(new VoiceRequest { Request = ++request, Enabled = true }, Channel.Reliable);
            }
            if (generation != 0 && capture != null)
            {
                int limit = 5;
                while (limit-- > 0 && capture.Peek(out var packet))
                {
                    manager.ClientManager.Broadcast(new VoiceFrame { Generation = generation, Sequence = sequence++, Payload = packet }, Channel.Unreliable);
                    capture.Consume(); SentFrames++;
                }
            }
            if (MicrophoneEnabled && generation != 0 && (capture == null || !capture.Running)) SetMicrophone(false);
            Mix(localId);
        }
        // The dead's "world": they talk only to each other and hear by spectate
        // state (docs/SPECTATING_IMPLEMENTATION_PLAN.md §2), never by scene.
        public const int DeadWorld = 100;
        // Physical player identity/scene only: a spectator camera or a watched
        // scene never decides membership.
        private int WorldOf(int id)
        {
            if (!players.TryGetValue(id, out var player) || !player.gameObject.activeInHierarchy) return -1;
            var day = CrewDayState.Instance;
            if (day == null || day.Travelling || (day.Riding && day.IsRider(id))) return -1;
            if (day.IsDead(id)) return DeadWorld;
            if (!WorldScenes.TryParse(player.gameObject.scene.name, out var result)) return -1;
            if (day.IsBelow(id) != (result == WorldId.Dive)) return -1;
            if (result != WorldId.Dive && result != day.World) return -1;
            return (int)result;
        }
        // A living speaker's frame reaches a dead listener when the listener watches
        // the speaker, or watches someone in the speaker's proximity set.
        private bool DeadListenerHears(int listener, int speaker, int speakerWorld)
        {
            var day = CrewDayState.Instance;
            if (day == null) return false;
            return WatchedHears(day.SpectateTargetOf(listener), speaker, speakerWorld);
        }
        // The deck TV carries a diver's frame to the ship when the channel diver is
        // the speaker or in the speaker's proximity set (card 3).
        private bool TvHears(int speaker, int speakerWorld)
        {
            var day = CrewDayState.Instance;
            return day != null && speakerWorld == (int)WorldId.Dive && WatchedHears(day.TvChannel, speaker, speakerWorld);
        }
        private bool WatchedHears(int watched, int speaker, int speakerWorld)
        {
            if (watched < 0) return false;
            if (watched == speaker) return true;
            return WorldOf(watched) == speakerWorld && players.TryGetValue(watched, out var target) && players.TryGetValue(speaker, out var talker)
                && Vector3.Distance(talker.transform.position, target.transform.position) <= Settings.SilentMetres + Settings.RelayMarginMetres;
        }
        // The TV's speaker on this peer's ship, when it has one.
        private static ShipTV LocalTv()
        {
            var ship = ShipParts.InWorld(WorldId.Sea);
            return ship != null ? ship.GetComponent<ShipTV>() : null;
        }
        private void Mix(int localId)
        {
            players.TryGetValue(localId, out var local);
            var day = CrewDayState.Instance;
            heard.Clear();
            foreach (var pair in receivers)
            {
                var receiver = pair.Value;
                if (receiver.Playback == null) continue;
                float gain = 0, pan = 0;
                if (local != null && players.TryGetValue(pair.Key, out var remote) && world >= 0 && Time.unscaledTime - receiver.LastFrame < .25f)
                {
                    if (receiver.Route == VoiceRoute.Dead)
                    {
                        if (world == DeadWorld && WorldOf(pair.Key) == DeadWorld) gain = 1; // dead to dead: full, placeless
                    }
                    else if (receiver.Route == VoiceRoute.TV)
                    {
                        // At the TV's speaker, as loud as the channel diver hears the
                        // speaker, falling off with the listener's distance to the TV.
                        ShipTV tv = world == (int)WorldId.Sea ? LocalTv() : null;
                        int channel = day != null ? day.TvChannel : -1;
                        if (tv != null && tv.Live && channel >= 0 && players.TryGetValue(channel, out var diver))
                        {
                            float atDiver = channel == pair.Key ? 1 : Settings.Gain(Vector3.Distance(HeadPosition(remote), HeadPosition(diver)));
                            Vector3 delta = tv.SpeakerPosition - HeadPosition(local);
                            gain = atDiver * Settings.Gain(delta.magnitude);
                            var head = local.GetComponentInChildren<Camera>(true);
                            Vector3 right = head != null ? head.transform.right : local.transform.right;
                            pan = Vector3.Dot(right, delta.normalized);
                        }
                    }
                    else
                    {
                        // The ear: the listener's own head, or — dead, watching — the
                        // watched player's head, where the spectator camera is.
                        HQPlayerController ear = null;
                        if (receiver.Route == VoiceRoute.Spectate)
                        {
                            int target = world == DeadWorld && day != null ? day.SpectateTargetOf(localId) : -1;
                            if (target >= 0) players.TryGetValue(target, out ear);
                        }
                        else if (world != DeadWorld && WorldOf(pair.Key) == world) ear = local;
                        if (ear != null)
                        {
                            Vector3 delta = HeadPosition(remote) - HeadPosition(ear);
                            gain = Settings.Gain(delta.magnitude);
                            // Yaw from the local camera: the listener's own head, or the
                            // spectator camera sitting on the watched player's eyes.
                            var head = local.GetComponentInChildren<Camera>(true);
                            Vector3 right = head != null ? head.transform.right : ear.transform.right;
                            pan = Vector3.Dot(right, delta.normalized);
                        }
                    }
                }
                float mixed = receiver.Muted ? 0 : gain * VoiceVolume * receiver.Volume;
                receiver.Playback.SetMix(mixed, pan);
                if (mixed > 0.001f && receiver.Playback.Level >= SpeechLevel) receiver.HeardUntil = Time.unscaledTime + HeardHoldSeconds;
                if (mixed > 0.001f && Time.unscaledTime < receiver.HeardUntil) heard.Add(new HeardSpeaker { Id = pair.Key, Route = receiver.Route, Gain = mixed });
            }
            heard.Sort((a, b) => a.Id.CompareTo(b.Id));
            removed.Clear(); foreach (var pair in receivers) if (!players.ContainsKey(pair.Key) && Time.unscaledTime - pair.Value.LastFrame > 2) removed.Add(pair.Key);
            foreach (int id in removed) { receivers[id].Playback?.Dispose(); receivers.Remove(id); }
        }
        private static Vector3 HeadPosition(HQPlayerController player) => player.PlayerCamera != null ? player.PlayerCamera.transform.position : player.transform.position + Vector3.up * 1.6f;
        public void SetMicrophone(bool enabled)
        {
            StopMicrophoneTest();
            if (!enabled) { MicrophoneEnabled = false; syntheticCapture = false; StopTransmission(); Status = "Microphone OFF"; return; }
            if (!Connected || !Devices.Available || (!string.IsNullOrEmpty(Devices.InputId) && Devices.InputIndex < 0))
            { Status = "Microphone unavailable — join a session and select a connected mic."; return; }
            MicrophoneEnabled = true; retryAt = 0; Status = world < 0 ? "Microphone ON — waiting for world" : "Microphone ON — connecting";
        }
        public void TestMicrophone()
        {
            SetMicrophone(false);
            if (!Devices.Available || (!string.IsNullOrEmpty(Devices.InputId) && Devices.InputIndex < 0)) { Status = "Selected microphone unavailable"; return; }
            capture = new VoiceCapture(); TestingMicrophone = capture.Start(Devices.InputIndex, false);
            Status = TestingMicrophone ? "Testing microphone locally — transmission OFF" : "Cannot open microphone. Check OS permissions and device.";
        }
        public void StopMicrophoneTest()
        {
            if (!TestingMicrophone) return;
            capture?.Dispose(); capture = null; TestingMicrophone = false; Status = "Microphone OFF";
        }
        private void OnInputChanged() { SetMicrophone(false); Status = "Microphone changed or disconnected — OFF"; }
        private void OnAudioReset() { foreach (var receiver in receivers.Values) receiver.Playback?.RecreateClip(); }
        private void StopTransmission()
        {
            if (!TestingMicrophone) { capture?.Dispose(); capture = null; }
            generation = 0;
            if (bound && manager != null && manager.ClientManager != null && manager.ClientManager.Started)
                manager.ClientManager.Broadcast(new VoiceRequest { Request = ++request, Enabled = false }, Channel.Reliable);
        }
        private void ResetLocal()
        {
            MicrophoneEnabled = false; syntheticCapture = false; StopMicrophoneTest(); capture?.Dispose(); capture = null;
            generation = 0; world = -1; Status = "Microphone OFF";
            foreach (var receiver in receivers.Values) receiver.Playback?.Dispose(); receivers.Clear();
        }
        private void ServerRequest(NetworkConnection connection, VoiceRequest message, Channel channel)
        {
            if (channel != Channel.Reliable) return;
            int id = connection.ClientId;
            bool firstRequest = !senders.TryGetValue(id, out var sender);
            if (firstRequest) senders[id] = sender = new Sender { Updated = Time.unscaledTime };
            if ((int)(message.Request - sender.Request) <= 0) return;
            // Stops are never rate limited. Starts are bounded before allocating a generation.
            if (message.Enabled && Time.unscaledTime < sender.ControlAt) return;
            sender.Request = message.Request;
            if (!message.Enabled && !sender.Enabled && !firstRequest) return;
            if (message.Enabled) sender.ControlAt = Time.unscaledTime + .125f;
            sender.World = WorldOf(id); sender.Enabled = message.Enabled && sender.World >= 0;
            sender.Generation = ++nextGeneration; sender.Sequence.Reset();
            Publish(new VoiceState { Speaker = id, Request = message.Request, Generation = sender.Generation, Enabled = sender.Enabled });
            // A newly joined muted listener also learns current speakers' epochs.
            if (!message.Enabled)
                foreach (var pair in senders) if (pair.Key != id && pair.Value.Enabled)
                    manager.ServerManager.Broadcast(connection, new VoiceState { Speaker = pair.Key, Request = pair.Value.Request, Generation = pair.Value.Generation, Enabled = true }, true, Channel.Reliable);
        }
        private void Publish(VoiceState state)
        {
            foreach (var connection in manager.ServerManager.Clients.Values)
                if (connection.IsAuthenticated) manager.ServerManager.Broadcast(connection, state, true, Channel.Reliable);
        }
        private void ServerFrame(NetworkConnection connection, VoiceFrame frame, Channel channel)
        {
            int id = connection.ClientId;
            if (channel != Channel.Unreliable || frame.Payload.Count < 1 || frame.Payload.Count > 400 || !senders.TryGetValue(id, out var sender) || !sender.Enabled || sender.Generation != frame.Generation || sender.World < 0 || sender.World != WorldOf(id)) { RejectedFrames++; return; }
            float now = Time.unscaledTime; sender.Tokens = Mathf.Min(10, sender.Tokens + (now - sender.Updated) * 60); sender.Updated = now;
            if (sender.Tokens < 1 || !sender.Sequence.Accept(frame.Sequence)) { RejectedFrames++; return; }
            sender.Tokens--; frame.Speaker = id;
            bool speakerDead = sender.World == DeadWorld;
            foreach (var other in manager.ServerManager.Clients.Values)
            {
                if (other.ClientId == id || !other.IsAuthenticated) continue;
                int otherWorld = WorldOf(other.ClientId);
                if (speakerDead)
                {
                    // The dead talk only to the dead, anywhere; the living never get these frames.
                    if (otherWorld != DeadWorld) continue;
                    frame.Route = VoiceRoute.Dead;
                }
                else if (otherWorld == sender.World)
                {
                    if (Vector3.Distance(players[id].transform.position, players[other.ClientId].transform.position) > Settings.SilentMetres + Settings.RelayMarginMetres) continue;
                    frame.Route = VoiceRoute.Direct;
                }
                else if (otherWorld == DeadWorld)
                {
                    if (!DeadListenerHears(other.ClientId, id, sender.World)) continue;
                    frame.Route = VoiceRoute.Spectate;
                }
                else if (otherWorld == (int)WorldId.Sea)
                {
                    if (!TvHears(id, sender.World)) continue; // a diver, carried to the deck by the TV
                    frame.Route = VoiceRoute.TV;
                }
                else continue;
                manager.ServerManager.Broadcast(other, frame, true, Channel.Unreliable); RelayedFrames++;
            }
        }
        private void ClientState(VoiceState state, Channel channel)
        {
            if (channel != Channel.Reliable || !Connected) return;
            if (state.Speaker == manager.ClientManager.Connection.ClientId)
            {
                if (state.Request != request) return;
                capture?.Dispose(); capture = null; generation = 0;
                if (!state.Enabled || !MicrophoneEnabled || world < 0) return;
                capture = new VoiceCapture();
                if (!capture.Start(Devices.InputIndex, true, syntheticCapture)) { SetMicrophone(false); Status = "Cannot open microphone. Check OS permissions and device."; return; }
                generation = state.Generation; sequence = 0; Status = syntheticCapture ? "Generated test tone — microphone NOT captured" : "Microphone ON"; return;
            }
            if (!receivers.TryGetValue(state.Speaker, out var receiver))
            {
                if (receivers.Count >= 4) return;
                receivers[state.Speaker] = receiver = new Receiver();
            }
            if (state.Enabled && receiver.Generation == state.Generation) return;
            if (receiver.Playback != null) receiver.Cushion = receiver.Playback.Cushion;
            receiver.Playback?.Dispose(); receiver.Playback = null;
            receiver.Generation = state.Enabled ? state.Generation : 0;
        }
        private void ClientFrame(VoiceFrame frame, Channel channel)
        {
            if (channel != Channel.Unreliable || !Connected || frame.Speaker == manager.ClientManager.Connection.ClientId || world < 0 || !Devices.Available || !receivers.TryGetValue(frame.Speaker, out var receiver) || receiver.Generation == 0 || receiver.Generation != frame.Generation || receiver.Muted) return;
            // A living listener takes its own world's direct frames and, on the ship, the
            // TV's; a dead one only spectate and dead frames.
            bool accepted = frame.Route switch
            {
                VoiceRoute.Direct => world != DeadWorld && WorldOf(frame.Speaker) == world,
                VoiceRoute.TV => world == (int)WorldId.Sea,
                _ => world == DeadWorld
            };
            if (!accepted) return;
            if (receiver.Playback == null) { receiver.Playback = new VoicePlayback(gameObject, Devices); receiver.Playback.Cushion = receiver.Cushion; }
            receiver.Route = frame.Route;
            receiver.Playback.Enqueue(frame.Sequence, frame.Payload); receiver.LastFrame = Time.unscaledTime; ReceivedFrames++;
        }
        public void SetVoiceVolume(float value) { VoiceVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("Audio.Voice", VoiceVolume); }
        public bool PeerMuted(int id) => receivers.TryGetValue(id, out var receiver) && receiver.Muted;
        public int PeerDecoded(int id) => receivers.TryGetValue(id, out var receiver) ? receiver.Playback?.Decoded ?? 0 : 0;
        public string PeerReadStats(int id) => receivers.TryGetValue(id, out var receiver) && receiver.Playback != null ? $"reads={receiver.Playback.Reads} block={receiver.Playback.LastReadLength} underruns={receiver.Playback.Underruns} cushion={receiver.Playback.CushionMs}ms concealed={receiver.Playback.Concealed}" : "none";
        public float PeerPlaybackGain(int id) => receivers.TryGetValue(id, out var receiver) ? receiver.Playback?.Gain ?? 0 : 0;
        public float PeerPlaybackPan(int id) => receivers.TryGetValue(id, out var receiver) ? receiver.Playback?.Pan ?? 0 : 0;
        public float PeerVolume(int id) => receivers.TryGetValue(id, out var receiver) ? receiver.Volume : 1;
        public void SetPeer(int id, bool muted, float volume)
        {
            if (!receivers.TryGetValue(id, out var receiver)) { if (receivers.Count >= 4) return; receivers[id] = receiver = new Receiver(); }
            receiver.Muted = muted; receiver.Volume = Mathf.Clamp01(volume);
            if (muted) { receiver.Playback?.Dispose(); receiver.Playback = null; }
        }
        private void OnDestroy()
        {
            ResetLocal();
            if (bound && manager != null)
            {
                manager.ServerManager.UnregisterBroadcast<VoiceRequest>(ServerRequest); manager.ServerManager.UnregisterBroadcast<VoiceFrame>(ServerFrame);
                manager.ClientManager.UnregisterBroadcast<VoiceState>(ClientState); manager.ClientManager.UnregisterBroadcast<VoiceFrame>(ClientFrame);
                manager.ClientManager.OnClientConnectionState -= ConnectionChanged; manager.ServerManager.OnRemoteConnectionState -= RemoteChanged;
            }
            if (Devices != null) { Devices.InputChanged -= OnInputChanged; Devices.ConfigurationChangedEvent -= OnAudioReset; Devices.Shutdown(); }
        }
    }
}
