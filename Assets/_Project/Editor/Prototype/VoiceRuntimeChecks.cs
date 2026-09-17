using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Audio;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Generated tones only. Never opens a microphone or joins a Steam room.
    // Run after building this exact tree: CameraClearanceMatrixDriver.Start("voice").
    public static class VoiceRuntimeChecks
    {
        private const string DirectoryPath = "Library/VoiceVerification";
        private static string LogPath = DirectoryPath + "/matrix.log";
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int command;
        private static float previousMaster, previousVoice;
        private static ProximityVoice Voice => UnityEngine.Object.FindAnyObjectByType<ProximityVoice>();
        private static HQPlayerController Host => WorldSceneFlow.LocalPlayer();
        public static string Status { get; private set; } = "Not run";
        public static void RunAsHost(bool hostOnly = false)
        {
            if (!EditorApplication.isPlaying || Host == null || !Voice.Connected) throw new InvalidOperationException("Local host required");
            if (stack.Count != 0) throw new InvalidOperationException("Already running");
            LogPath = DirectoryPath + (hostOnly ? "/host.log" : "/matrix.log");
            Directory.CreateDirectory(DirectoryPath); File.WriteAllText(LogPath, DateTime.UtcNow.ToString("O") + " generated-tone voice matrix\n");
            previousMaster = Voice.Devices.Master; previousVoice = Voice.VoiceVolume;
            Voice.SetVoiceVolume(1); Voice.Devices.SetMaster(.1f);
            Status = "Running"; stack.Push(hostOnly ? RunHostOnly() : Run()); EditorApplication.update += Tick;
        }
        private static void Say(string text) => File.AppendAllText(LogPath, text + "\n");
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Say("PASS " + message); }
        private static IEnumerator Wait(double seconds) { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
        private static IEnumerator Until(Func<bool> ready, double seconds, string label)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (!ready()) { if (EditorApplication.timeSinceStartup >= until) throw new InvalidOperationException(label); yield return null; }
            Say("PASS " + label);
        }
        private static void Launch()
        {
            foreach (string name in new[] { "command.json", "reply.txt" }) { string p = DirectoryPath + "/" + name; if (File.Exists(p)) File.Delete(p); }
            var session = UnityEngine.Object.FindAnyObjectByType<PrototypeSessionController>();
            var port = session.NetworkManager.TransportManager.Transport.GetPort();
            var info = new ProcessStartInfo(Path.GetFullPath("Builds/HQPrototypeLocal/SunkCostHQ.exe"),
                $"-hq-auto-join-local 127.0.0.1 -hq-local-port {port} -hq-inventory-test-dir {Path.GetFullPath(DirectoryPath)} -logFile {Path.GetFullPath(DirectoryPath)}/guest.log -screen-fullscreen 0 -screen-width 960 -screen-height 600")
            { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            guest = Process.Start(info); Say("Guest PID " + guest.Id + "; port " + port);
        }
        private static void Send(string action, Vector3 position = default, int slot = 0)
        {
            var value = new InventoryVerificationPeer.Command { id = ++command, action = action, position = position, slot = slot };
            File.WriteAllText(DirectoryPath + "/command.json", JsonUtility.ToJson(value));
        }
        private static string Reply()
        {
            try { string p = DirectoryPath + "/reply.txt"; return File.Exists(p) ? File.ReadAllText(p) : ""; } catch (IOException) { return ""; }
        }
        private static IEnumerator Answer()
        {
            yield return Until(() => Reply().StartsWith("id=" + command + ";"), 10, "guest answered " + command);
            Say(Reply());
        }
        private static int GuestId => UnityEngine.Object.FindObjectsByType<HQPlayerController>().First(p => !p.IsOwner && p.IsSpawned).OwnerId;
        private static IEnumerator RunHostOnly()
        {
            Say(VoiceChecks.Run()); Check(!Voice.MicrophoneEnabled, "host starts muted");
            uint output = Voice.Devices.OutputCallbacks; Voice.Devices.PlayTest();
            yield return Until(() => Voice.Devices.OutputCallbacks > output, 5, "native output has test samples");
            Voice.StartLocalTestTone(); yield return Until(() => Voice.SentFrames > 20, 8, "generated tone sends after acknowledgement");
            Say(Voice.Diagnostics);
            foreach (var destination in new[] { WorldId.Sea, WorldId.HQ })
            {
                var ship = ShipParts.InWorld(CrewDayState.Instance.World);
                Host.TeleportLocal(ship.SpawnPoint(0).position, Host.Yaw); yield return Wait(.3);
                H.ClientRequestSail(destination.ToString());
                yield return Until(() => CrewDayState.Instance.Travelling, 5, "sail begins toward " + destination);
                yield return Until(() => Voice.Diagnostics.Contains("epoch=0;"), 3, "travel invalidates stream");
                Check(Voice.MicrophoneEnabled, "travel retains requested mic state");
                uint sent = Voice.SentFrames; yield return Wait(.25); Check(Voice.SentFrames == sent, "no capture frames sent during travel");
                Say(Voice.Diagnostics);
                yield return Until(() => CrewDayState.Instance.World == destination && !CrewDayState.Instance.Travelling, 50, "arrived " + destination);
                yield return Until(() => Voice.SentFrames > sent + 10, 8, "fresh stream resumes after arrival");
                Say(Voice.Diagnostics);
            }
            Voice.SetMicrophone(false); uint stopped = Voice.SentFrames; yield return Wait(.5);
            Check(Voice.SentFrames == stopped, "mute action stops outgoing stream");
            UnityEngine.Object.FindAnyObjectByType<VoiceSettingsUI>().ShowForChecks(); yield return Wait(.3);
            H.CaptureScreen(DirectoryPath + "/audio-settings.png"); yield return Wait(.5);
            UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>().LeaveSession(); yield return Wait(2);
            Check(!Voice.MicrophoneEnabled && !Voice.Connected, "leave resets microphone");
            UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>().StartLocalHost();
            yield return Until(() => Voice.Connected && Host != null, 20, "re-host succeeds");
            Check(!Voice.MicrophoneEnabled, "re-host muted");
            Say("HOST_MATRIX_PASS (separate-client rows not run)"); Status = "HOST_MATRIX_PASS";
        }
        private static IEnumerator Run()
        {
            Say(VoiceChecks.Run()); Check(!Voice.MicrophoneEnabled, "host starts muted");
            Launch(); yield return Until(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().Count(p => p.IsSpawned) == 2, 35, "separate authenticated client joined");
            yield return Wait(1); int guestId = GuestId;
            Send("snapshot"); yield return Answer(); Check(Reply().Contains("mic=False"), "guest starts muted");
            Voice.StartLocalTestTone(); Send("voice_tone"); yield return Answer();
            yield return Until(() => Voice.PeerDecoded(guestId) >= 20, 15, "client-to-host Opus decoding");
            Send("snapshot"); yield return Answer(); Check(!Reply().Contains("decoded=0;"), "host-to-client Opus decoding");
            Check(Voice.PeerDecoded(Host.OwnerId) == 0, "host has no self-playback");
            Say(Voice.Diagnostics);
            uint output = Voice.Devices.OutputCallbacks; yield return Wait(2);
            Check(Voice.Devices.OutputCallbacks > output, "native selected output consumes nonzero samples");
            Voice.SetPeer(guestId, true, 1); uint received = Voice.ReceivedFrames; yield return Wait(1);
            Check(Voice.ReceivedFrames == received && Voice.PeerPlaybackGain(guestId) == 0, "per-player mute discards received playback");
            Voice.SetPeer(guestId, false, 1); yield return Until(() => Voice.PeerDecoded(guestId) >= 10, 10, "unmute resumes live audio");
            Vector3 origin = Host.transform.position;
            foreach (float distance in new[] { 2f, 11f, 20f, 24f })
            {
                Send("move", origin + Vector3.right * distance); yield return Answer();
                yield return Wait(.3);
                float gain = Voice.PeerPlaybackGain(guestId);
                Say($"distance request={distance} gain={gain:0.000} pan={Voice.PeerPlaybackPan(guestId):0.000}");
                if (distance == 2) Check(gain > .95f, "full nearby gain");
                if (distance == 11) Check(gain > .35f && gain < .65f, "halfway smooth attenuation");
                if (distance >= 20) Check(gain == 0, "silence at/above 20m");
                // The indicator: the guest's tone is heard straight from them while in
                // range, and drops off the list (after the hold) once out of range.
                if (distance == 2) yield return Until(() => Voice.Heard.Any(h => h.Id == guestId && h.Route == VoiceRoute.Direct), 3, "indicator lists the guest, direct, while heard");
                if (distance == 24) yield return Until(() => Voice.Heard.All(h => h.Id != guestId), 3, "indicator drops the guest out of range");
            }
            yield return Wait(1); uint relay = Voice.RelayedFrames; yield return Wait(1);
            Check(Voice.RelayedFrames == relay, "no host relay outside margin");
            Send("move", origin + Vector3.right * 2); yield return Answer(); yield return Until(() => Voice.PeerPlaybackGain(guestId) > .95f, 10, "returning into range resumes");
            Voice.SetVoiceVolume(0); yield return Wait(.2); Check(Voice.PeerPlaybackGain(guestId) == 0, "voice volume zero"); Voice.SetVoiceVolume(1);
            Voice.SetMicrophone(false); uint sent = Voice.SentFrames; yield return Wait(.7); Check(Voice.SentFrames == sent, "settings mute action stops outgoing frames");
            Send("snapshot"); yield return Answer(); Check(System.Text.RegularExpressions.Regex.IsMatch(Reply(), @"epoch=0; muted=False; route=\d+; decoded=0;"), "reliable stop clears remote playback"); // the peer line gained route= (voice protocol 3) and level=
            Voice.StartLocalTestTone(); yield return Wait(1);
            // New main's day/payday rules stay authoritative; use the actual ship controls.
            var ship = ShipParts.InWorld(CrewDayState.Instance.World);
            Host.TeleportLocal(ship.SpawnPoint(0).position, Host.Yaw);
            Send("move", ship.SpawnPoint(1).position); yield return Answer();
            H.ClientRequestSail("Sea");
            yield return Until(() => CrewDayState.Instance.Travelling, 5, "sail begins");
            yield return Wait(.5); Say(Voice.Diagnostics);
            Check(Voice.MicrophoneEnabled && Voice.Diagnostics.Contains("epoch=0;"), "travel preserves preference but stops stream");
            yield return Until(() => CrewDayState.Instance.World == WorldId.Sea && !CrewDayState.Instance.Travelling, 50, "arrived at sea");
            yield return Until(() => Voice.PeerDecoded(guestId) > 10, 15, "voice resumes after fresh travel epoch");
            Say(Voice.Diagnostics);
            UnityEngine.Object.FindAnyObjectByType<VoiceSettingsUI>().ShowForChecks(); yield return Wait(.3);
            H.CaptureScreen(DirectoryPath + "/audio-settings.png"); yield return Wait(.5);
            Voice.SetMicrophone(false); Send("voice_off"); yield return Answer();
            Send("leave");
            yield return Until(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().Count(p => p.IsSpawned) == 1, 15, "disconnect cleanup");
            guest.Kill(); guest.Dispose(); guest = null;
            UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>().LeaveSession(); yield return Wait(2);
            Check(!Voice.MicrophoneEnabled && !Voice.Connected, "leave resets microphone");
            UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>().StartLocalHost();
            yield return Until(() => Voice.Connected && Host != null, 20, "re-host succeeds");
            Check(!Voice.MicrophoneEnabled, "re-host starts muted");
            Say("MATRIX_PASS"); Status = "MATRIX_PASS";
        }
        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode stopped during checks");
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); if (stack.Count == 0) Finish(); }
                else if (top.Current is IEnumerator child) stack.Push(child);
            }
            catch (Exception e) { Status = "FAIL: " + e.Message; Say(Status); Say(Voice != null ? Voice.Diagnostics : "No voice service"); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick; stack.Clear();
            if (Voice != null) { Voice.SetMicrophone(false); Voice.Devices.SetMaster(previousMaster); Voice.SetVoiceVolume(previousVoice); }
            if (guest != null) { if (!guest.HasExited) guest.Kill(); guest.Dispose(); guest = null; }
        }
    }
}
