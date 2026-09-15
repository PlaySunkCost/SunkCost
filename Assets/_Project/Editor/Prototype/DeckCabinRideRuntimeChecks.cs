using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
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
    // The Play Mode rows of the cabin ride (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // sections 5.3 and 5.4 without the day rules): the editor hosts and rides,
    // one headless guest (Local build) rides along, stays behind and goes alone.
    // Every row samples the authoritative state and the local rider; the fade,
    // the doors and the car position are read every frame during a ride.
    // Log: Temp/deck-cabin-matrix.log.
    public static class DeckCabinRideRuntimeChecks
    {
        private const string Log = "Temp/deck-cabin-matrix.log";
        private const string GuestDir = "Temp/deck-cabin-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestId = 900;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static bool inputBehaviorChanged;
        public static string Status { get; private set; } = "Not run";

        [MenuItem("Sunk Cost/Prototype/Run deck cabin ride matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Deck cabin ride matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Deck cabin ride matrix started " + DateTime.Now + "\n");
            Status = "Running";
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            EditorApplication.update += Tick;
        }

        public static void Abort()
        {
            Cleanup();
            if (steps == null) return;
            steps = null;
            EditorApplication.update -= Tick;
            Status = "Aborted";
        }

        private static void Cleanup()
        {
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; inputBehaviorChanged = false; }
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
        }

        // A virtual keyboard on the Input System, as the movement matrix uses: the
        // real input path, with the editor's focus gate lifted for the run.
        private static void Keys(params Key[] pressed)
        {
            if (keyboard == null)
            {
                savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                inputBehaviorChanged = true;
                keyboard = InputSystem.AddDevice<Keyboard>("CabinCheckKeyboard");
                HQPlayerController.BypassInputGateForChecks = true;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
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
            if (Status == "MATRIX_PASS") Debug.Log("Deck cabin ride matrix: MATRIX_PASS"); else Debug.LogError("Deck cabin ride matrix: " + Status);
            Cleanup();
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        // ---- helpers ------------------------------------------------------------------

        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.RideStatus() + "\n" + H.FlowStatus());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }

        private static void Note(string text) => File.AppendAllText(Log, "  " + text + "\n");

        private static IEnumerator Wait(float seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) { if (condition()) yield break; yield return null; }
            throw new Exception("timeout: " + label + "\n" + H.RideStatus() + "\n" + H.FlowStatus());
        }

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static ShipDepartureRider Rider() => WorldSceneFlow.LocalRider();

        private static bool CarDoorsOpen()
        {
            ElevatorController car = WorldSceneFlow.FindCar();
            if (car == null) return false;
            Transform leaf = null;
            foreach (Transform t in car.GetComponentsInChildren<Transform>(true)) if (t.name == "Leaf Right") { leaf = t; break; }
            return leaf != null && Mathf.Abs(Mathf.DeltaAngle(0f, leaf.localEulerAngles.y)) > 1f;
        }

        private static bool ShaftGateBlocks()
        {
            foreach (ShaftGate gate in UnityEngine.Object.FindObjectsByType<ShaftGate>(FindObjectsInactive.Include))
            {
                Collider c = gate.GetComponent<Collider>() ?? gate.GetComponentInChildren<Collider>(true);
                if (c != null) return c.enabled;
            }
            return false;
        }

        // Rides as the local player from the current cabin, sampling every frame:
        // the player stays inside the moving car, the fade behaves, and the ride
        // completes with the car at the expected landing.
        private static IEnumerator RideAndSample(RideDirection direction, string label)
        {
            int serialBefore = Day.CabinRide.Serial;
            string requested = direction == RideDirection.Down ? H.ClientRequestCabin() : H.ClientRequestCar();
            Note(label + ": " + requested);
            yield return WaitUntil(() => Day.CabinRide.Serial > serialBefore && Day.CabinRide.Active, 5f, label + " ride started");
            Check(Day.CabinRide.Direction == direction, label + " ride direction " + direction);
            bool everBlack = false, everMoved = false, everOutside = false, everUnlockedEarly = false, everFreeWhileMoving = false;
            float maxSink = 0f, walked = 0f;
            Vector3? walkStart = null;
            int frames = 0;
            double deadline = EditorApplication.timeSinceStartup + 60.0;
            double nextNote = 0;
            while (EditorApplication.timeSinceStartup < deadline && Day.CabinRide.Active)
            {
                frames++;
                CabinRideState state = Day.CabinRide;
                if (EditorApplication.timeSinceStartup >= nextNote)
                {
                    nextNote = EditorApplication.timeSinceStartup + 1.0;
                    ElevatorController c0 = WorldSceneFlow.FindCar();
                    HQPlayerController l0 = Host();
                    Note($"{label} t+{frames}: stage={state.Stage} car={Day.Elevator.State} carPos={(c0 == null ? "-" : c0.transform.position.ToString("F2"))} local={(l0 == null ? "-" : l0.transform.position.ToString("F2"))} carLocal={(c0 == null || l0 == null ? "-" : c0.transform.InverseTransformPoint(l0.transform.position).ToString("F2"))} locked={(Rider() != null && Rider().Locked)} travelLocked={(l0 != null && l0.TravelLocked)} grounded={(l0 != null && l0.IsGrounded)} inside={(c0 != null && l0 != null && c0.IsInsideCar(l0.transform.position + Vector3.up * 0.5f))}");
                }
                if (ScreenFade.Instance != null && ScreenFade.Instance.IsBlack) everBlack = true;
                ElevatorController car = WorldSceneFlow.FindCar();
                HQPlayerController local = Host();
                bool shouldBeLocked = WorldSceneFlow.RidersLockedDuring(state);
                if (state.Stage != CabinRideStage.Preparing && shouldBeLocked && Rider() != null && !Rider().Locked) everUnlockedEarly = true;
                if (car != null && local != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Dive))
                {
                    ElevatorState cs = Day.Elevator.State;
                    if (cs == ElevatorState.Descending || cs == ElevatorState.Ascending)
                    {
                        everMoved = true;
                        if (!car.IsInsideCar(local.transform.position + Vector3.up * 0.5f)) everOutside = true;
                        maxSink = Mathf.Max(maxSink, car.transform.position.y - local.transform.position.y);
                        // Free inside the moving car: walk forward for a while and measure it in the car frame.
                        if (!shouldBeLocked && !local.TravelLocked)
                        {
                            everFreeWhileMoving = true;
                            Vector3 carLocal = car.transform.InverseTransformPoint(local.transform.position);
                            if (walkStart == null) { walkStart = carLocal; Keys(Key.W); }
                            else
                            {
                                Vector3 d = carLocal - walkStart.Value; d.y = 0f;
                                walked = Mathf.Max(walked, d.magnitude);
                                if (walked > 0.6f) Keys();
                            }
                        }
                    }
                }
                yield return null;
            }
            Check(!Day.CabinRide.Active && Day.CabinRide.Stage == CabinRideStage.Complete, label + " ride completed in " + frames + " frames");
            if (direction == RideDirection.Down) Check(everBlack, label + " the suit fade went black");
            Check(everMoved, label + " the car moved with the rider inside");
            Check(!everOutside, label + " the rider never left the car while it moved");
            Keys();
            Check(maxSink < 0.3f, $"{label} the rider never sank into the car floor (max {maxSink:0.00} m)");
            Check(!everUnlockedEarly, label + " the rider was locked exactly when it should be");
            Check(everFreeWhileMoving, label + " the rider was free inside the moving car");
            Check(walked > 0.5f && !everOutside, $"{label} the rider walked {walked:0.00} m inside the moving car and stayed inside");
            yield return WaitUntil(() => Rider() == null || !Rider().Locked, 5f, label + " rider unlocked after the ride");
            yield return WaitUntil(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 5f, label + " screen clear after the ride");
        }

        // ---- guest ----------------------------------------------------------------------

        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-batchmode -nographics -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return Process.Start(info);
        }

        private static void Command(string json)
        {
            string text = json.Replace("{id}", (++guestId).ToString());
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(Path.Combine(GuestDir, "command.json"), text); return; }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(15); }
            }
        }

        private static string lastReply = string.Empty;

        private static IEnumerator Send(string json)
        {
            Command(json);
            double deadline = EditorApplication.timeSinceStartup + 8.0;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                string path = Path.Combine(GuestDir, "reply.txt");
                if (File.Exists(path))
                {
                    string reply;
                    try { reply = File.ReadAllText(path); } catch (IOException) { reply = string.Empty; }
                    if (reply.Contains("id=" + guestId + ";")) { lastReply = reply; yield break; }
                }
                yield return null;
            }
            throw new Exception("guest did not reply to command " + guestId);
        }

        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            string reply = string.Empty;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                Command("{\"id\":{id},\"action\":\"snapshot\"}");
                double replyDeadline = EditorApplication.timeSinceStartup + 8.0;
                while (EditorApplication.timeSinceStartup < replyDeadline)
                {
                    string path = Path.Combine(GuestDir, "reply.txt");
                    if (File.Exists(path))
                    {
                        try { reply = File.ReadAllText(path); } catch (IOException) { reply = string.Empty; }
                        if (reply.Contains("id=" + guestId + ";")) break;
                    }
                    yield return null;
                }
                if (predicate(reply)) { File.AppendAllText(Log, "PASS " + label + "\n" + reply + "\n"); yield break; }
            }
            throw new Exception(label + " (guest never agreed)\n" + reply);
        }

        private static string GuestPlayerLine(string reply, int ownerId) =>
            reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;

        private static int GuestId()
        {
            foreach (var conn in FishNet.InstanceFinder.ServerManager.Clients.Values)
                if (conn.IsActive && conn.ClientId != FishNet.InstanceFinder.ClientManager.Connection.ClientId) return conn.ClientId;
            return -1;
        }

        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###") + ",\"y\":" + v.y.ToString("0.###") + ",\"z\":" + v.z.ToString("0.###") + "}";

        // ---- the rows --------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            SunkCost.Net.SessionInputGate.OpenMenu(); // the real keyboard/mouse stay out of the room
            WorldSceneFlow flow = WorldSceneFlow.Instance;

            // R0: at HQ the cabin refuses; sail to sea with the host in the cabin.
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Not at sea", 3f, "R0 refusal at HQ");
            Check(!Day.Riding, "R0 no ride started at HQ");
            H.MoveLocalIntoDeckCabin("HQ");
            yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "R0 sailing to sea");
            yield return WaitUntil(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 30f, "R0 arrived at sea");
            yield return WaitUntil(() => Rider() != null && !Rider().Locked, 5f, "R0 unlocked at sea");
            Check(Mathf.Approximately(flow.DeckCabinOpenFraction(), 1f), "R0 deck cabin doors open at sea");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            yield return WaitUntil(() => sea != null && sea.DeckCabinPanel != null && sea.DeckCabinPanel.text.Contains("Step in"), 3f, "R0 deck cabin panel invites");
            Check(true, "R0 deck cabin panel invites: " + sea.DeckCabinPanel.text);

            // R1: refusals from outside the cabin and from the wrong scene.
            host.TeleportLocal(sea.BoardingPoint != null ? sea.BoardingPoint.position : sea.SpawnPoint(0).position, 0f);
            yield return Wait(0.2f);
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Step inside the cabin first", 3f, "R1 refusal outside the cabin");
            H.ClientRequestCar();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Cabin is up", 3f, "R1 car button refused from the deck");

            // R2: down. Sealing closes the deck doors, the suit fade, the car at the top, the descent, doors open at the bottom.
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.2f);
            yield return RideAndSample(RideDirection.Down, "R2");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Dive), "R2 the host's player is in DiveSite01");
            Check(Day.Elevator.State == ElevatorState.AtBottom, "R2 car at the bottom");
            ElevatorController car = WorldSceneFlow.FindCar();
            Check(car != null && car.Driven && Vector3.Distance(car.transform.position, car.BottomPosition) < 0.05f, "R2 the driven car sits on its bottom position");
            Check(car != null && car.IsInsideCar(host.transform.position + Vector3.up * 0.5f), "R2 the host stands inside the car");
            Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            yield return WaitUntil(CarDoorsOpen, 3f, "R2 car doors open at the bottom");
            Check(!ShaftGateBlocks(), "R2 shaft gate open at the bottom");
            Check(Day.IsBelow(host.OwnerId), "R2 host listed below");
            Check(flow.DeckCabinOpenFraction() < 0.01f, "R2 deck cabin doors closed while the car is below");
            yield return WaitUntil(() => sea.DeckCabinPanel.text == "Cabin below", 3f, "R2 deck cabin panel says 'Cabin below'");
            Check(true, "R2 deck cabin panel says 'Cabin below'");
            yield return Wait(1.6f); // doors fully open
            H.CaptureLocalCamera("Logs/deck-cabin-bottom.png");
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(doorway); yield return null;
            H.CaptureLocalCamera("Logs/deck-cabin-bottom-doorway.png");

            // R3: out onto the seafloor and back in; the car button from outside refuses.
            host.TeleportLocal(car.transform.position + doorway * 4f, host.Yaw);
            yield return Wait(0.3f);
            Check(!car.IsInsideCar(host.transform.position + Vector3.up * 0.5f), "R3 host outside the car");
            H.ClientRequestCar();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Step inside the cabin first", 3f, "R3 car button refused from outside");
            H.MoveLocalIntoCar();
            yield return Wait(0.3f);

            // R4: up. The car seals and climbs, the host is moved into the deck cabin, its doors open, the car stays up (nobody below).
            yield return RideAndSample(RideDirection.Up, "R4");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), "R4 the host's player is back in ShipAtSea");
            sea = ShipParts.InWorld(WorldId.Sea);
            Check(sea != null && sea.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "R4 the host stands in the deck cabin");
            Check(Day.Elevator.State == ElevatorState.AtTop, "R4 the car is up");
            Check(Day.Below.Count == 0, "R4 nobody below");
            yield return WaitUntil(() => flow.DeckCabinOpenFraction() > 0.99f, 3f, "R4 deck cabin doors open again");
            yield return WaitUntil(() => WorldSceneFlow.FindCar() == null, 10f, "R4 the site unloaded from the server once empty");
            H.CaptureLocalCamera("Logs/deck-cabin-back-on-deck.png");

            // R5: again, from a fresh site: down and up once more.
            yield return RideAndSample(RideDirection.Down, "R5a");
            Check(Day.Elevator.State == ElevatorState.AtBottom && host.gameObject.scene == WorldScenes.Scene(WorldId.Dive), "R5a down again on a fresh site");
            yield return Wait(0.5f);
            yield return RideAndSample(RideDirection.Up, "R5b");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Sea) && Day.Elevator.State == ElevatorState.AtTop, "R5b up again");

            // ---- guest rows ----
            guest = LaunchGuest();
            yield return WaitUntil(() => GuestId() >= 0 && UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Length >= 2, 40f, "guest player spawned");
            int guestId = GuestId();
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "G0 guest joined at sea");
            Check(!Day.Riding, "G0 no ride running");

            // G1: both in the cabin; the host presses; both ride down.
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3 guestSpot = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + Vector3.up * 0.05f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
            yield return Wait(0.5f);
            yield return RideAndSample(RideDirection.Down, "G1");
            Check(Day.IsBelow(guestId) && Day.IsBelow(host.OwnerId), "G1 both listed below");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("car=AtBottom") && r.Contains("ride=Complete"), 20f, "G1 guest rode down with the host");
            car = WorldSceneFlow.FindCar();
            HQPlayerController guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            Check(guestPlayer != null && car != null && car.IsInsideCar(guestPlayer.transform.position + Vector3.up * 0.5f), "G1 the guest's copy stands inside the car on the host");

            // G2: the host goes up alone; the guest stays below; the car comes back down empty for the guest.
            host.TeleportLocal(car.transform.position + doorway * 4f, host.Yaw); // the host steps out
            yield return Wait(0.3f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + doorway * 5f + Vector3.up * 0.05f) + "}"); // so does the guest
            yield return Wait(0.5f);
            H.MoveLocalIntoCar();
            yield return Wait(0.5f);
            yield return RideAndSample(RideDirection.Up, "G2");
            Check(Day.Below.Count == 1 && Day.IsBelow(guestId), "G2 only the guest is below");
            yield return WaitUntil(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, "G2 the car went back down empty for the guest");
            Check(WorldSceneFlow.FindCar() != null, "G2 the site stays loaded on the server while the guest is below");
            Check(flow.DeckCabinOpenFraction() < 0.01f, "G2 deck cabin doors closed on the ship");
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.3f);
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Cabin below", 3f, "G2 host refused: cabin below");

            // G3: the guest walks into the car and comes up alone; the host watches the doors open.
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * 0.05f) + "}");
            yield return Wait(0.5f);
            int serialBefore = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}");
            yield return WaitUntil(() => Day.CabinRide.Serial > serialBefore, 5f, "G3 guest's up ride started");
            yield return WaitUntil(() => !Day.CabinRide.Active, 60f, "G3 guest's ride completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Day.Below.Count == 0, "G3 guest surfaced, nobody below");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=ShipAtSea") && r.Contains("ride=Complete") && r.Contains("travelLocked=False"), 20f, "G3 guest is back on the ship, unlocked");
            yield return WaitUntil(() => flow.DeckCabinOpenFraction() > 0.99f, 3f, "G3 deck cabin doors open for the guest's arrival");
            guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            sea = ShipParts.InWorld(WorldId.Sea);
            Check(guestPlayer != null && sea.IsInDeckCabin(guestPlayer.transform.position + Vector3.up * 0.5f), "G3 the guest's copy stands in the deck cabin on the host");
            yield return WaitUntil(() => WorldSceneFlow.FindCar() == null, 10f, "G3 the site unloaded once empty");

            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            SunkCost.Net.SessionInputGate.Resume();
        }
    }
}
