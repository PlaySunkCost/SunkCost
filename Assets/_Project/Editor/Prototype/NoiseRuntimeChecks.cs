using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunkCost.Audio;
using SunkCost.Diving;
using SunkCost.Noise;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Noise and the elevator's sounds (docs/DESIGN.md §1, §6; decided with Dan,
    // 17 September 2026), the host alone with a listener on the NoiseSystem:
    // nothing on the deck; below, walking emits Footsteps at the walk radius,
    // sprinting Sprints at the sprint radius, crouching nothing at all; the
    // moving car emits Elevator events at the big radius; the winch plays at
    // the car for a diver and through the deck on the ship, the bell rings at
    // the bottom and at the top. Log: Temp/noise-matrix.log.
    // Started by CameraClearanceMatrixDriver.Start("noise").
    public static class NoiseRuntimeChecks
    {
        private const string Log = "Temp/noise-matrix.log";
        private const string GuestDir = "Temp/noise-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private static System.Diagnostics.Process guest;
        private static int guestCommand = 1300;
        private static string lastReply = string.Empty;

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Keyboard keyboard;
        // The editor loses focus whenever the tester types elsewhere; by default the
        // Input System then drops the virtual keyboard's events too ("walked 0.0 m").
        // Ignore focus for the run, as the hands matrix does.
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static readonly Ears ears = new();
        public static string Status { get; private set; } = "Not run";

        private sealed class Ears : INoiseListener
        {
            public readonly List<NoiseEvent> Heard = new();
            public void OnNoise(in NoiseEvent noise) => Heard.Add(noise);
            public int Count(NoiseKind kind) => Heard.Count(e => e.Kind == kind);
        }

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run noise matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Noise matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Noise matrix started " + DateTime.Now + "\n");
            Status = "Running";
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode stopped");
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    if (!current.MoveNext()) { stack.Pop(); continue; }
                    if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    return;
                }
                Status = "MATRIX_PASS";
            }
            catch (Exception e) { Status = "FAIL: " + e.Message + "\n" + e.StackTrace; }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Noise matrix: MATRIX_PASS"); else Debug.LogError("Noise matrix: " + Status);
            NoiseSystem.Unregister(ears);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }
        private static IEnumerator Expect(Func<bool> condition, float seconds, Func<string> label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
            Check(condition(), label());
        }
        private static void Keys(params Key[] pressed) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static float Field(string text, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, key + "=(-?[0-9.]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
        }
        private static System.Diagnostics.Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-screen-width 960 -screen-height 540 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return System.Diagnostics.Process.Start(info);
        }
        private static IEnumerator Send(string json)
        {
            string text = json.Replace("{id}", (++guestCommand).ToString());
            for (int attempt = 0; ; attempt++)
            {
                bool written = false;
                try { File.WriteAllText(Path.Combine(GuestDir, "command.json"), text); written = true; }
                catch (IOException) when (attempt < 20) { }
                if (written) break;
                yield return null;
            }
            float deadline = Time.unscaledTime + 10f;
            while (Time.unscaledTime < deadline)
            {
                string reply;
                try { string p = Path.Combine(GuestDir, "reply.txt"); reply = File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
                catch (IOException) { reply = string.Empty; }
                if (reply.StartsWith("id=" + guestCommand + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest did not answer command " + guestCommand + ": " + json);
        }
        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.4f);
            }
            throw new Exception(label + "\n" + lastReply);
        }

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            Check(host.GetComponent<PlayerNoise>() != null, "the player prefab carries PlayerNoise (run the noise setup)");
            Check(NoiseSystem.IsServer, "the NoiseSystem knows it is on the server");
            NoiseSettings settings = NoiseSettings.Get();
            AudioLibrary library = AudioLibrary.Get();
            Say($"walk {settings.WalkStepMetres} m / {settings.WalkRadius} m, sprint {settings.SprintStepMetres} m / {settings.SprintRadius} m, elevator {settings.ElevatorRadius} m every {settings.ElevatorEmitInterval} s; winch {(library.WinchIsPlaceholder ? "placeholder" : library.ElevatorWinch.name)}, ding {(library.DingIsPlaceholder ? "placeholder" : library.ElevatorDing.name)}");
            NoiseSystem.Register(ears);
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("NoiseCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            Check(ElevatorSounds.Instance != null, "the elevator's sounds are attached on the client");
            PlayerFootstepSounds feet = host.GetComponent<PlayerFootstepSounds>();
            Check(feet != null, "the player prefab carries PlayerFootstepSounds (run the noise setup)");

            Heading("N0 — on the deck nothing is heard");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            host.TeleportLocal(sea.SpawnPoint(0).position, 0f); yield return Wait(0.3f);
            ears.Heard.Clear();
            Keys(Key.W); yield return Wait(1f); Keys(); yield return null;
            Check(ears.Heard.Count == 0, "N0 walking on the deck emits nothing (the suit is off)");

            Heading("N1 — the ride down: the winch through the deck, then at the car; the bell at the bottom");
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            ears.Heard.Clear();
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Descending, 40f, () => "the car is descending");
            yield return Wait(0.5f);
            Say($"N1 descending: {ElevatorSounds.Instance.SecondsToArrival:0.0} s to the bottom");
            Check(ElevatorSounds.Instance.SecondsToArrival > library.WinchSecondsBeforeArrival + 1f && !ElevatorSounds.Instance.WinchPlayingAtCar, "N1 silent at the start of the ride (the winch is for the arrival)");
            yield return Expect(() => ElevatorSounds.Instance.WinchPlayingAtCar, 40f, () => "N1 the winch starts before the car arrives");
            Say($"N1 the winch began with {ElevatorSounds.Instance.SecondsToArrival:0.0} s to go");
            Check(ElevatorSounds.Instance.SecondsToArrival <= library.WinchSecondsBeforeArrival + 0.3f, $"N1 within the last {library.WinchSecondsBeforeArrival:0} s");
            yield return Wait(1f);
            Check(Mathf.Abs(ElevatorSounds.Instance.CarWinchVolume - library.WinchVolumeInCar) < 0.01f, $"N1 lower still for the rider inside the car ({ElevatorSounds.Instance.CarWinchVolume:0.00} = {library.WinchVolumeInCar:0.00})");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(0.5f);
            Check(!ElevatorSounds.Instance.WinchPlayingAtCar && !ElevatorSounds.Instance.WinchPlayingOnShip, "N1 the winch stops when the car stops");
            Check(ElevatorSounds.Instance.DingsAtCar >= 1, $"N1 the bell rang at the car when the doors opened below ({ElevatorSounds.Instance.DingsAtCar})");
            int descentEvents = ears.Count(NoiseKind.Elevator);
            Check(descentEvents >= 3, $"N1 the descending car was heard by the ocean ({descentEvents} Elevator events)");
            Check(ears.Heard.Where(e => e.Kind == NoiseKind.Elevator).All(e => Mathf.Approximately(e.Radius, settings.ElevatorRadius)), $"N1 at the elevator radius ({settings.ElevatorRadius} m)");
            yield return Wait(1f);

            Heading("N2 — walking below: a footstep every stride at the walk radius");
            ElevatorController car = WorldSceneFlow.FindCar();
            float floorY = car.BottomPosition.y + 0.15f;
            host.TeleportLocal(new Vector3(25f, floorY, -25f), 90f); yield return Wait(0.5f);
            ears.Heard.Clear();
            int soundsBefore = feet.StepsPlayed;
            Vector3 start = host.transform.position;
            Keys(Key.W); yield return Wait(2f); Keys(); yield return Wait(0.2f);
            float walked = Vector3.ProjectOnPlane(host.transform.position - start, Vector3.up).magnitude;
            int expected = Mathf.FloorToInt(walked / settings.WalkStepMetres);
            int steps = ears.Count(NoiseKind.Footstep);
            Say($"N2 walked {walked:0.0} m: {steps} footsteps (expected about {expected}), {ears.Count(NoiseKind.Sprint)} sprints");
            Check(steps >= expected - 2 && steps <= expected + 1 && steps > 3, $"N2 {steps} footsteps over {walked:0.0} m");
            Check(ears.Count(NoiseKind.Sprint) == 0, "N2 none of them a sprint");
            Check(ears.Heard.Where(e => e.Kind == NoiseKind.Footstep).All(e => Mathf.Approximately(e.Radius, settings.WalkRadius)), $"N2 at the walk radius ({settings.WalkRadius} m)");
            Check(ears.Heard.All(e => Vector3.Distance(e.Position, host.transform.position) < walked + 1f), "N2 at the diver's feet");
            int heard = feet.StepsPlayed - soundsBefore;
            Check(heard >= steps - 2 && heard <= steps + 2, $"N2 and the steps were heard: {heard} footstep sounds for {steps} noise events");

            Heading("N3 — sprinting: fewer, louder steps");
            host.TeleportLocal(new Vector3(25f, floorY, -25f), 90f); yield return Wait(0.5f);
            ears.Heard.Clear(); start = host.transform.position; soundsBefore = feet.StepsPlayed;
            Keys(Key.W, Key.LeftShift); yield return Wait(2f); Keys(); yield return Wait(0.2f);
            walked = Vector3.ProjectOnPlane(host.transform.position - start, Vector3.up).magnitude;
            int sprints = ears.Count(NoiseKind.Sprint);
            Say($"N3 sprinted {walked:0.0} m: {sprints} sprint steps, {ears.Count(NoiseKind.Footstep)} footsteps");
            Check(sprints >= Mathf.FloorToInt(walked / settings.SprintStepMetres) - 2 && sprints > 3, $"N3 {sprints} sprint steps over {walked:0.0} m");
            Check(ears.Heard.Where(e => e.Kind == NoiseKind.Sprint).All(e => Mathf.Approximately(e.Radius, settings.SprintRadius)), $"N3 at the sprint radius ({settings.SprintRadius} m)");
            Check(ears.Count(NoiseKind.Footstep) <= 2, "N3 the run's first strides at most read as walking");
            Check(feet.StepsPlayed - soundsBefore >= sprints - 2, $"N3 sprint steps were heard ({feet.StepsPlayed - soundsBefore} sounds for {sprints} events)");
            int soundsBeforeJump = feet.JumpsPlayed, landingsBefore = feet.LandingsPlayed;
            Keys(Key.Space); yield return Wait(0.06f); Keys(); yield return Wait(1.2f);
            Check(feet.JumpsPlayed == soundsBeforeJump + 1, $"N3 a jump was heard ({feet.JumpsPlayed})");
            Check(feet.LandingsPlayed == landingsBefore + 1, $"N3 and the landing ({feet.LandingsPlayed})");

            Heading("N4 — crouching is silent");
            host.TeleportLocal(new Vector3(25f, floorY, -25f), 90f); yield return Wait(0.5f);
            Keys(Key.LeftCtrl); yield return Wait(0.5f);
            Check(host.IsCrouched, "N4 crouched");
            int stepsBeforeCrouch = feet.StepsPlayed;
            ears.Heard.Clear(); start = host.transform.position;
            Keys(Key.LeftCtrl, Key.W); yield return Wait(2f); Keys(Key.LeftCtrl); yield return Wait(0.2f);
            walked = Vector3.ProjectOnPlane(host.transform.position - start, Vector3.up).magnitude;
            Check(walked > 1f, $"N4 crouch-walked {walked:0.0} m");
            Check(ears.Heard.Count == 0, $"N4 not a sound ({ears.Heard.Count} events)");
            Check(feet.StepsPlayed == stepsBeforeCrouch, $"N4 and no footstep sound either ({feet.StepsPlayed - stepsBeforeCrouch})");
            Keys(); yield return Wait(0.5f);
            Check(!host.IsCrouched, "N4 standing again");

            Heading("N5 — the ride up: the winch at the car, Elevator events, the bell on the deck");
            H.MoveLocalIntoCar(); yield return Wait(0.4f);
            ears.Heard.Clear();
            int dingsBefore = ElevatorSounds.Instance.DingsOnShip + ElevatorSounds.Instance.DingsAtCar;
            serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the car took the press");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Ascending, 20f, () => "the car is ascending");
            yield return Wait(0.5f);
            Check(!ElevatorSounds.Instance.WinchPlayingAtCar, "N5 silent as the car leaves the bottom");
            yield return Expect(() => ElevatorSounds.Instance.WinchPlayingAtCar, 40f, () => "N5 the winch starts before the top");
            Check(ElevatorSounds.Instance.SecondsToArrival <= library.WinchSecondsBeforeArrival + 0.3f, $"N5 within the last {library.WinchSecondsBeforeArrival:0} s ({ElevatorSounds.Instance.SecondsToArrival:0.0} to go)");
            yield return Wait(1f);
            Check(Mathf.Abs(ElevatorSounds.Instance.CarWinchVolume - library.WinchVolumeInCar) < 0.01f, $"N5 lower still inside the car ({ElevatorSounds.Instance.CarWinchVolume:0.00})");
            Check(!ElevatorSounds.Instance.WinchPlayingOnShip, "N5 not through the deck: the rider is below");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride up completed");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), "back on the deck");
            yield return Wait(0.5f);
            int ascentEvents = ears.Count(NoiseKind.Elevator);
            Check(ascentEvents >= 3, $"N5 the ascending car was heard by the ocean ({ascentEvents} Elevator events)");
            Check(!ElevatorSounds.Instance.WinchPlayingAtCar && !ElevatorSounds.Instance.WinchPlayingOnShip, "N5 the winch stopped at the top");
            Check(ElevatorSounds.Instance.DingsOnShip + ElevatorSounds.Instance.DingsAtCar > dingsBefore, $"N5 the bell rang when the car opened at the top (deck {ElevatorSounds.Instance.DingsOnShip}, car {ElevatorSounds.Instance.DingsAtCar})");

            Heading("N6 — a guest on the deck hears the winch low while the host rides down, and the bell when the car returns");
            guest = LaunchGuest();
            yield return Expect(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Any(p => p.IsSpawned && !p.IsOwner), 40f, () => "the guest's copy is here");
            yield return GuestEventually(r => r.Contains("world=Sea") && r.Contains("winchOnShip=False"), 20f, "N6 the guest joined at sea; the winch is quiet");
            // Everyone rides down (the deck cabin waits for everyone), the guest rides up
            // alone: from the deck it hears the car come back down for the host, low,
            // and the bell when the host's car opens at the top.
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "N6 End day for a second dive: " + endWhy);
            HQPlayerController remote = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).First(p => p.IsSpawned && !p.IsOwner);
            int guestId = remote.OwnerId;
            float cabinFloor = DeckCabinBuilder.FloorThicknessMeters + 0.05f;
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}");
            yield return Wait(0.5f);
            serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(1f);
            car = WorldSceneFlow.FindCar();
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * 0.2f + car.transform.right * 0.6f) + "}");
            host.TeleportLocal(car.transform.position + car.transform.forward * 4f + Vector3.up * 0.15f, host.Yaw); yield return Wait(0.5f);
            serial = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}");
            yield return Expect(() => Day.CabinRide.Serial > serial, 4f, () => "N6 the guest's car press was taken (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Ascending, 20f, () => "N6 the guest's car is climbing");
            yield return Expect(() => ElevatorSounds.Instance.WinchPlayingAtCar, 40f, () => "N6 the host, left below, hears the car in its last seconds before the top");
            yield return Wait(1f);
            Check(Mathf.Abs(ElevatorSounds.Instance.CarWinchVolume - library.WinchVolumeAtCar) < 0.01f && Mathf.Approximately(ElevatorSounds.Instance.CarWinchReach, library.WinchReachMetres), $"N6 quiet ({ElevatorSounds.Instance.CarWinchVolume:0.00}) but carrying {ElevatorSounds.Instance.CarWinchReach:0} m — heard from very far");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "N6 the guest's ride up completed");
            yield return GuestEventually(r => r.Contains("scene=ShipAtSea") && r.Contains("winchOnShip=False"), 15f, "N6 the guest is on the deck; the winch is quiet");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Descending, 40f, () => "N6 the car comes back down for the host");
            yield return GuestEventually(r => r.Contains("winchOnShip=True") && r.Contains("winchAtCar=False"), 40f, "N6 the guest on the deck hears the returning car through the deck in its last seconds, low, not at the car");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 40f, () => "N6 the car is back at the bottom");
            yield return GuestEventually(r => r.Contains("winchOnShip=False"), 6f, "N6 the winch stops for the guest when the car stops");
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
            int guestDings = (int)Field(lastReply, "dingsOnShip");
            H.MoveLocalIntoCar(); yield return Wait(0.4f);
            serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the car took the press");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Ascending, 20f, () => "the host's car is ascending");
            yield return GuestEventually(r => r.Contains("winchOnShip=True"), 40f, "N6 the guest hears the host's car in its last seconds before the top, low");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the host's ride up completed");
            yield return GuestEventually(r => (int)Field(r, "dingsOnShip") > guestDings, 6f, "N6 the bell rang on the deck for the guest when the car opened at the top");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            Say("done");
        }
    }
}
