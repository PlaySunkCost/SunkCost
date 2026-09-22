using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Monsters;
using SunkCost.Noise;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using M = SunkCost.Editor.Prototype.MonsterTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The dash (docs/DESIGN.md §3, 21 September 2026) and a landing's noise, host
    // and guest below: refused at sea (D0); Alt on the seabed — the burst, the
    // server's judgement, the 4 s of air, the Dash noise, the rings and the whoosh
    // on the host and on the guest's copy of it (D1); the cooldown (D2); the
    // steering direction (D3); an air dash (D4); refused crouched and with both
    // hands full (D5); the guest's dash seen and judged by the host (D6); the
    // Listener turning to a dash (D7); a thrown coin's landing heard by the host
    // and by the Listener, thrown by the host and by the guest (D8). Log:
    // Temp/dash-matrix.log. Started by CameraClearanceMatrixDriver.Start("dash").
    public static class DashRuntimeChecks
    {
        private const string Log = "Temp/dash-matrix.log";
        private const string GuestDir = "Temp/dash-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1800;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static Ears ears;
        private static bool liftSeen;   // HostDash: the vertical speed was upward right after the press
        private static float peakY;     // HostDash: the highest the feet got during the burst
        public static string Status { get; private set; } = "Not run";

        private sealed class Ears : INoiseListener
        {
            public readonly List<NoiseEvent> Heard = new();
            public void OnNoise(in NoiseEvent noise) => Heard.Add(noise);
            public int Count(NoiseKind kind) => Heard.Count(e => e.Kind == kind);
            public int Count(NoiseKind kind, int source) => Heard.Count(e => e.Kind == kind && e.SourceId == source);
        }

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run dash matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Dash matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Dash matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Dash matrix: MATRIX_PASS"); else Debug.LogError("Dash matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            if (ears != null) { NoiseSystem.Unregister(ears); ears = null; }
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            EditorApplication.update -= Tick;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + DashText());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static string DashText()
        {
            HQPlayerController host = Host();
            if (host == null) return "no host";
            return $"host dashes={host.Dashes} serverDashes={host.ServerDashes} serial={host.LastDashCue.Serial} ready={host.DashReady:0.00} refusal='{host.DashRefusal}' grounded={host.IsGrounded} heard={(ears == null ? "-" : string.Join(",", ears.Heard.Select(e => e.Kind + "/" + e.Radius + "/" + e.SourceId)))}";
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
        private static IEnumerator Press(Key key, float holdSeconds = 0.05f)
        {
            Keys(key); yield return Wait(holdSeconds); Keys(); yield return null;
        }
        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-screen-width 960 -screen-height 540 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return Process.Start(info);
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
                string reply = Reply();
                if (reply.StartsWith("id=" + guestCommand + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest did not answer command " + guestCommand + ": " + json);
        }
        private static string Reply()
        {
            try { string p = Path.Combine(GuestDir, "reply.txt"); return File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
            catch (IOException) { return string.Empty; }
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
        private static string Action(string action) => "{\"id\":{id},\"action\":\"" + action + "\"}";
        private static string Vec(Vector3 v) => "{\"x\":" + F(v.x) + ",\"y\":" + F(v.y) + ",\"z\":" + F(v.z) + "}";
        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static string FirstLine(string reply) => reply.Split('\n').FirstOrDefault() ?? string.Empty;
        private static float Field(string line, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, key + @"=(-?[0-9.]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
        }
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static IEnumerator GuestMove(Vector3 to) { yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static IEnumerator GuestLook(Vector3 aim) { yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(aim) + "}"); }
        private static IEnumerator GuestDash(Vector3 direction) { yield return Send("{\"id\":{id},\"action\":\"dash\",\"aim\":" + Vec(direction) + "}"); }
        private static float FlatDistance(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        // A spot on the seabed at a bearing and distance from the shaft (metres, flat).
        private static Vector3 Seabed(ElevatorController car, float bearingDeg, float distance)
        {
            Vector3 dir = Quaternion.Euler(0f, bearingDeg, 0f) * Vector3.forward;
            return car.BottomPosition + dir * distance + Vector3.up * 0.15f;
        }
        // Stand the host at a spot facing a point, settled on the floor.
        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            Vector3 to = facing - spot; to.y = 0f;
            host.TeleportLocal(spot, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : Vector3.forward, Vector3.up).eulerAngles.y);
            yield return null; yield return null;
            M.ClientLookAt(facing);
            yield return Wait(0.4f);
        }
        private static IEnumerator GrabItem(CarryableItem item)
        {
            HQPlayerController host = Host();
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 fromCar = item.transform.position - car.transform.position; fromCar.y = 0f;
            Vector3 toward = fromCar.normalized, side = Vector3.Cross(Vector3.up, toward);
            foreach (Vector3 offset in new[] { -toward * 1.1f, toward * 1.1f, side * 1.1f, -side * 1.1f })
            {
                Vector3 stand = item.transform.position + offset; stand.y = car.BottomPosition.y + 0.15f;
                host.TeleportLocal(stand, host.Yaw); yield return null; yield return null;
                float deadline = Time.unscaledTime + 3f; int tries = 0;
                while (Time.unscaledTime < deadline && host.CurrentTarget != item)
                {
                    if (tries++ % 30 == 0) host.TeleportLocal(stand, host.Yaw);
                    H.ClientLookAtItem(item.name);
                    yield return null;
                }
                if (host.CurrentTarget == item) break;
            }
            Check(host.CurrentTarget == item, "the dot is on " + item.DisplayName);
            host.Inventory.RequestGrab(item);
            yield return Expect(() => item.HolderClientId == host.OwnerId && item.State == ItemState.Held, 3f, () => item.DisplayName + " grabbed (" + item.State + ")");
        }
        private static IEnumerator Descend(int day)
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), "down in the dive site: " + H.RideStatus());
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(1.0f);
            Check(Day.Phase == DayPhase.DiveInProgress && Day.Day == day, $"dive in progress on day {day}");
        }
        // One dash on the virtual keyboard from where the host stands; returns once the burst is over.
        private static IEnumerator HostDash(params Key[] steering)
        {
            var pressed = steering.ToList();
            if (pressed.Count > 0) { Keys(pressed.ToArray()); yield return null; yield return null; }
            pressed.Add(Key.LeftAlt);
            Keys(pressed.ToArray()); yield return null; yield return null;
            liftSeen = Host().VerticalSpeed > 0f; // the little lift, right after the press
            peakY = Host().transform.position.y;
            yield return Wait(0.04f);
            if (steering.Length > 0) Keys(steering); else Keys();
            float until = Time.unscaledTime + Host().Movement.DashSeconds + 0.15f;
            while (Time.unscaledTime < until) { peakY = Mathf.Max(peakY, Host().transform.position.y); yield return null; }
            Keys(); yield return null;
        }
        private static IEnumerator CooldownOver()
        {
            yield return Expect(() => Host().DashReady >= 1f, Host().Movement.DashCooldownSeconds + 1f, () => "the cooldown ran down");
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerVitals vitals = host.Vitals;
            Check(vitals != null, "the player prefab carries PlayerVitals (run the vitals setup)");
            PlayerMovementSettings move = host.Movement;
            NoiseSettings noise = NoiseSettings.Get();
            Say($"dash {move.DashMeters} m in {move.DashSeconds} s with a {move.DashLiftSpeed} m/s lift, {move.DashCooldownSeconds} s apart, {vitals.Settings.DashAirSeconds} s of air; noise {noise.DashRadius} m; a landing {noise.LandingRadius} m over {noise.LandingSpeed} m/s (full at {noise.LandingFullSpeed})");
            Check(move.IsValid && move.DashMeters >= 6f && Mathf.Approximately(move.DashSeconds, 0.25f) && move.DashLiftSpeed > 0f && move.DashCooldownSeconds >= 3f && vitals.Settings.DashAirSeconds >= 4f, "the settings carry Dan's numbers (6 m, 0.25 s, a lift, 3 s, 4 s of air)");
            Check(noise.DashRadius > noise.SprintRadius, $"a dash carries farther than a sprinting step ({noise.DashRadius} > {noise.SprintRadius})");
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("DashCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            PlayerDashEffects hostEffects = host.GetComponent<PlayerDashEffects>();
            Check(hostEffects != null, "the host's player carries PlayerDashEffects (added at OnStartClient)");
            ears = new Ears();
            NoiseSystem.Register(ears);

            Heading("D0 — to sea; a guest joins; Alt on the deck is refused (only below)");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "the guest's copy is here");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            PlayerVitals remoteVitals = remote.Vitals;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "D0 the guest joined at sea");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            float cabinFloor = DeckCabinBuilder.FloorThicknessMeters + 0.05f;
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.5f);
            Vector3 before = host.transform.position;
            yield return HostDash();
            Check(host.Dashes == 0 && host.DashRefusal == "Only below" && FlatDistance(host.transform.position, before) < 0.5f, $"D0 Alt on the deck is refused: '{host.DashRefusal}', moved {FlatDistance(host.transform.position, before):0.00} m");
            Check(host.DashReady >= 1f, "D0 the dash reads ready (the visor is off on the deck; its bar is read below)");
            yield return GuestMove(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor);
            yield return Wait(0.5f);
            yield return Descend(1);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "D0 the guest is in the site");
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 shaft = car.BottomPosition;
            // The guest parks off to the side, out of every row's way.
            Vector3 guestPark = Seabed(car, 200f, 30f);
            yield return GuestMove(guestPark);
            yield return GuestLook(Quaternion.Euler(0f, 200f, 0f) * Vector3.forward);

            Heading("D1 — Alt on the seabed: the burst, the server's judgement, 4 s of air, the noise, the rings on both screens");
            Vector3 stand = Seabed(car, 20f, 18f);
            Vector3 facing = Seabed(car, 20f, 40f);
            yield return HostAt(stand, facing);
            yield return Wait(0.5f);
            Vector3 forward = Flat(host.transform.forward).normalized;
            before = host.transform.position;
            float airBefore = vitals.ServerAirSeconds;
            ears.Heard.Clear();
            int ringsBefore = hostEffects.RingsShown, whooshBefore = hostEffects.WhooshesPlayed;
            yield return HostDash();
            Vector3 moved = Flat(host.transform.position - before);
            Check(host.Dashes == 1 && host.DashRefusal == string.Empty, "D1 Alt started a dash");
            Check(moved.magnitude >= move.DashMeters * 0.75f && moved.magnitude <= move.DashMeters * 1.3f, $"D1 the burst covered {moved.magnitude:0.0} m (target {move.DashMeters})");
            Check(Vector3.Dot(moved.normalized, forward) > 0.9f, "D1 forward, with no steering");
            Check(liftSeen && peakY - before.y >= 0.12f, $"D1 the little lift: going up right after the press, feet {peakY - before.y:0.00} m over the floor at the peak (a dash through water)");
            yield return Expect(() => host.ServerDashes == 1, 1f, () => "D1 the server judged one dash from the copy's speed");
            Check(host.LastDashCue.Serial == 1, "D1 the cue's serial is 1");
            float spent = airBefore - vitals.ServerAirSeconds;
            Check(spent >= vitals.Settings.DashAirSeconds - 0.2f && spent <= vitals.Settings.DashAirSeconds + 1.5f, $"D1 the tank lost {spent:0.0} s (the dash's {vitals.Settings.DashAirSeconds} plus the moment's breathing)");
            Check(ears.Count(NoiseKind.Dash, host.ObjectId) == 1, $"D1 one Dash noise from the host ({ears.Count(NoiseKind.Dash)} heard)");
            Check(ears.Heard.Where(e => e.Kind == NoiseKind.Dash).All(e => Mathf.Approximately(e.Radius, noise.DashRadius)), $"D1 at the dash radius ({noise.DashRadius} m)");
            Check(hostEffects.RingsShown - ringsBefore == 2 && hostEffects.WhooshesPlayed - whooshBefore == 1, $"D1 two rings out of the boots and a whoosh on the host ({hostEffects.RingsShown - ringsBefore}, {hostEffects.WhooshesPlayed - whooshBefore})");
            Check(host.DashReady < 1f && hud.Visor.DashReady < 1f, $"D1 the visor's DASH bar is running down ({hud.Visor.DashReady:0.00})");
            yield return GuestEventually(r => Field(GuestPlayerLine(r, host.OwnerId), "dashSerial") == 1 && Field(GuestPlayerLine(r, host.OwnerId), "dashRings") == 2, 8f, "D1 the guest's copy of the host played the two rings from the cue");
            Check(Field(GuestPlayerLine(lastReply, host.OwnerId), "dashReady") < 1f, "D1 the guest reads the host's dash bar running down (a spectator's and the TV's visor read the same)");

            Heading("D2 — the cooldown: a second Alt at once does nothing; after it, the next dash");
            before = host.transform.position;
            yield return HostDash();
            Check(host.Dashes == 1 && host.DashRefusal.StartsWith("Dash in") && FlatDistance(host.transform.position, before) < 0.5f, $"D2 refused within the cooldown: '{host.DashRefusal}'");
            yield return CooldownOver();
            before = host.transform.position;
            yield return HostDash();
            Check(host.Dashes == 2 && FlatDistance(host.transform.position, before) >= move.DashMeters * 0.75f, $"D2 the next dash went ({FlatDistance(host.transform.position, before):0.0} m)");
            yield return Expect(() => host.ServerDashes == 2, 1f, () => "D2 judged as the second");
            yield return CooldownOver();

            Heading("D3 — the steering direction: A held with Alt dashes left");
            yield return HostAt(stand, facing);
            forward = Flat(host.transform.forward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            before = host.transform.position;
            yield return HostDash(Key.A);
            moved = Flat(host.transform.position - before);
            Check(moved.magnitude >= move.DashMeters * 0.75f && Vector3.Dot(moved.normalized, right) < -0.9f, $"D3 dashed {moved.magnitude:0.0} m to the left (dot right {Vector3.Dot(moved.normalized, right):0.00})");
            yield return Expect(() => host.ServerDashes == 3, 1f, () => "D3 judged");
            Check(Vector3.Dot(Flat(host.LastDashCue.Direction).normalized, right) < -0.8f, "D3 the cue carries the direction (the rings trail the right way on every screen)");
            yield return CooldownOver();

            Heading("D4 — an air dash: Space, then Alt in the air (Dan wants to try it)");
            yield return HostAt(stand, facing);
            before = host.transform.position;
            Keys(Key.Space); yield return Wait(0.05f); Keys(); yield return null;
            yield return Expect(() => !host.IsGrounded && host.VerticalSpeed > 0f, 0.5f, () => "D4 the host took off");
            yield return HostDash();
            moved = Flat(host.transform.position - before);
            Check(host.Dashes == 4 && moved.magnitude >= move.DashMeters * 0.7f, $"D4 the air dash covered {moved.magnitude:0.0} m");
            yield return Expect(() => host.ServerDashes == 4 && host.IsGrounded, 2f, () => "D4 judged, and the host landed");
            yield return CooldownOver();

            Heading("D5 — refused crouched, and with both hands full");
            yield return HostAt(stand, facing);
            Keys(Key.LeftCtrl); yield return Wait(0.3f);
            before = host.transform.position;
            yield return HostDash(Key.LeftCtrl);
            Check(host.Dashes == 4 && host.DashRefusal == "Not crouched" && FlatDistance(host.transform.position, before) < 0.5f, $"D5 crouched: '{host.DashRefusal}'");
            Keys(); yield return Wait(0.5f);
            CarryableItem ball = M.ServerSpawnItem("HeavyBallBlack", stand + forward * 1.2f + Vector3.up * 0.4f);
            Check(ball != null, "D5 a heavy ball spawned (registered prefab)");
            yield return Expect(() => ball.IsSpawned, 3f, () => "D5 spawned");
            yield return GrabItem(ball);
            Check(ball.Grip == CarryGrip.TwoHands, "D5 it takes both hands");
            before = host.transform.position;
            yield return HostDash();
            Check(host.Dashes == 4 && host.DashRefusal == "Not with both hands full" && FlatDistance(host.transform.position, before) < 0.5f, $"D5 both hands full: '{host.DashRefusal}'");
            host.Inventory.RequestDrop();
            yield return Expect(() => ball.HolderClientId != host.OwnerId, 3f, () => "D5 dropped it");
            yield return Wait(0.5f);
            ears.Heard.Clear();

            Heading("D6 — the guest dashes: the host's copy moves, the server judges it, the host sees the rings");
            Vector3 guestStand = Seabed(car, 200f, 20f);
            Vector3 guestFacing = Seabed(car, 200f, 40f);
            yield return GuestMove(guestStand);
            yield return GuestLook(Flat(guestFacing - guestStand).normalized);
            yield return Wait(0.6f);
            Vector3 remoteBefore = remote.transform.position;
            float remoteAirBefore = remoteVitals.ServerAirSeconds;
            PlayerDashEffects remoteEffects = remote.GetComponent<PlayerDashEffects>();
            Check(remoteEffects != null, "D6 the host's copy of the guest carries PlayerDashEffects");
            int remoteRingsBefore = remoteEffects.RingsShown;
            ears.Heard.Clear();
            yield return GuestDash(Flat(guestFacing - guestStand).normalized);
            Check(FirstLine(lastReply).Contains("dash=True"), "D6 the guest's dash started: " + FirstLine(lastReply));
            yield return Expect(() => FlatDistance(remote.transform.position, remoteBefore) >= move.DashMeters * 0.7f, 1.5f, () => $"D6 the host's copy of the guest moved {FlatDistance(remote.transform.position, remoteBefore):0.0} m");
            yield return Expect(() => remote.ServerDashes == 1, 1.5f, () => "D6 the server judged the guest's dash");
            yield return Wait(0.3f);
            float remoteSpent = remoteAirBefore - remoteVitals.ServerAirSeconds;
            Check(remoteSpent >= vitals.Settings.DashAirSeconds - 0.2f && remoteSpent <= vitals.Settings.DashAirSeconds + 2f, $"D6 the guest's tank lost {remoteSpent:0.0} s");
            Check(ears.Count(NoiseKind.Dash, remote.ObjectId) == 1, $"D6 one Dash noise from the guest ({ears.Count(NoiseKind.Dash)} heard)");
            Check(remoteEffects.RingsShown - remoteRingsBefore == 2, $"D6 the host sees the guest's two rings ({remoteEffects.RingsShown - remoteRingsBefore})");
            yield return GuestEventually(r => Field(GuestPlayerLine(r, guestId), "dashes") == 1 && Field(GuestPlayerLine(r, guestId), "dashRings") == 2, 6f, "D6 the guest played its own rings at the press");
            yield return GuestEventually(r => Field(GuestPlayerLine(r, guestId), "dashSerial") == 1, 6f, "D6 the guest reads its own cue");
            yield return Wait(move.DashCooldownSeconds + 0.5f);

            Heading("D7 — the Listener turns to a dash");
            yield return HostAt(stand, facing);
            Vector3 listenerAt = stand + right * 14f;
            Creature listener = MonsterRoster.ServerSpawnForChecks(MonsterKind.Listener, listenerAt);
            Check(listener != null, "D7 a Listener spawned 14 m to the side");
            yield return Wait(1f);
            Listener listenerBrain = listener.GetComponent<Listener>();
            int heardBefore = listenerBrain.ServerHeard;
            yield return HostDash();
            yield return Expect(() => listenerBrain.ServerHeard > heardBefore && listenerBrain.ServerLastKind == NoiseKind.Dash, 1.5f, () => "D7 it heard the dash: " + listener.ServerStatus);
            yield return Expect(() => listener.Pose == CreaturePose.Drawn || listener.Pose == CreaturePose.Shooting, 2f, () => "D7 and turned to it (" + listener.ServerStatus + ")");
            yield return CooldownOver();

            Heading("D8 — a thrown coin lands loud: the host's throw, the guest's throw, the Listener");
            yield return HostAt(stand, facing);
            CarryableItem coin = M.ServerSpawnItem("CoinSmall", stand + forward * 1.2f + Vector3.up * 0.3f);
            Check(coin != null, "D8 a small coin spawned");
            yield return Expect(() => coin.IsSpawned, 3f, () => "D8 spawned");
            yield return GrabItem(coin);
            M.ClientLookAt(stand + forward * 6f); yield return null;
            ears.Heard.Clear();
            heardBefore = listenerBrain.ServerHeard;
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Expect(() => coin.HolderClientId != host.OwnerId, 8f, () => "D8 the coin left the hands and came to rest (" + coin.State + ")");
            yield return Expect(() => ears.Count(NoiseKind.Impact, coin.ObjectId) >= 1, 5f, () => $"D8 the landing was heard, from the coin ({ears.Count(NoiseKind.Impact)} impacts)");
            NoiseEvent landing = ears.Heard.First(e => e.Kind == NoiseKind.Impact && e.SourceId == coin.ObjectId);
            Check(landing.Radius >= noise.LandingRadius * 0.5f - 0.01f && landing.Radius <= noise.LandingRadius + 0.01f, $"D8 at {landing.Radius:0.0} m (the landing's {noise.LandingRadius}, scaled by the fall)");
            Check(FlatDistance(landing.Position, coin.transform.position) < 2.5f, $"D8 where the coin lies ({FlatDistance(landing.Position, coin.transform.position):0.0} m off)");
            Check(coin.ServerLandings == 1, "D8 one landing on the server's count");
            yield return Expect(() => listenerBrain.ServerHeard > heardBefore && listenerBrain.ServerLastKind == NoiseKind.Impact, 2f, () => "D8 the Listener heard the coin land: " + listener.ServerStatus);
            yield return Wait(1.5f);
            // The guest's throw: the server never simulates it, so the landing is judged from the copy.
            yield return GuestMove(coin.transform.position - forward * 1.0f + Vector3.up * 0.15f);
            yield return GuestLook(forward - Vector3.up * 0.3f);
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"grab\",\"item\":\"#" + coin.ObjectId + "\"}");
            yield return Expect(() => coin.HolderClientId == guestId, 4f, () => "D8 the guest holds the coin (" + coin.State + ")");
            yield return Wait(0.3f);
            ears.Heard.Clear();
            int landingsBefore = coin.ServerLandings;
            yield return Send("{\"id\":{id},\"action\":\"throw\",\"aim\":" + Vec(forward + Vector3.up * 0.2f) + "}");
            yield return Expect(() => coin.HolderClientId != guestId, 8f, () => "D8 the coin left the guest's hands and came to rest (" + coin.State + ")");
            yield return Expect(() => ears.Count(NoiseKind.Impact, coin.ObjectId) >= 1 && coin.ServerLandings == landingsBefore + 1, 6f, () => $"D8 the guest's throw landed loud on the server ({ears.Count(NoiseKind.Impact)} impacts, landings {coin.ServerLandings})");
            yield return Expect(() => !ears.Heard.Any(e => e.Kind == NoiseKind.Impact && e.SourceId == coin.ObjectId && FlatDistance(e.Position, coin.transform.position) > 4f), 2f, () => "D8 near where it lies");
            M.ServerDespawnMonsters();
            yield return Expect(() => Creature.All.Count == 0, 5f, () => "the Listener despawned");
            Check(!host.IsDead && !remote.IsDead, "both divers live");
            Say("done: " + DashText());
        }
    }
}
