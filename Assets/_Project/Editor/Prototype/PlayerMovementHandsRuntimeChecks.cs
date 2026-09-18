using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The runtime rows of docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
    // section 9, driven through a virtual keyboard on the Input System so the
    // real input path runs (the editor gate is bypassed, the mouse stays out).
    // The editor is the host; one headless guest (Local build) proves the remote
    // stance and hands. Log: Temp/movement-hands-matrix.log.
    public static class PlayerMovementHandsRuntimeChecks
    {
        private const string Log = "Temp/movement-hands-matrix.log";
        private const string GuestDir = "Temp/movement-hands-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Keyboard keyboard;
        // The editor only hands keyboard input to the game while the Game view has
        // focus; the checks lift that for their virtual keyboard and put it back.
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static bool inputBehaviorChanged;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static Process guest;
        private static int guestId = 700;
        public static string Status { get; private set; } = "Not run";

        [MenuItem("Sunk Cost/Prototype/Run movement and hands matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Movement and hands matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Movement and hands matrix started " + DateTime.Now + "\n");
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            inputBehaviorChanged = true;
                // The editor loses focus whenever the tester types elsewhere; by default the
                // Input System then disables devices and drops their events, the virtual
                // keyboard included ("walked 0.00 m"). Ignore focus for the run.
                savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
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
            HQPlayerController.KeyboardForChecks = null;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            foreach (GameObject block in GameObject.FindObjectsByType<GameObject>().Where(g => g.name.StartsWith("CheckBlock")).ToArray()) UnityEngine.Object.Destroy(block);
        }

        // Runs every editor update so per-frame input sampling is real.
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
            if (Status == "MATRIX_PASS") Debug.Log("Movement and hands matrix: MATRIX_PASS"); else Debug.LogError("Movement and hands matrix: " + Status);
            Cleanup();
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        // ---- helpers ----------------------------------------------------------------

        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }

        private static IEnumerator Wait(float seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) { if (condition()) yield break; yield return null; }
            throw new Exception("timeout: " + label);
        }

        private static HQPlayerController Host() => SunkCost.World.WorldSceneFlow.LocalPlayer();

        // The virtual keyboard: Keyboard.current follows the last device that
        // produced input, so the first event makes it the player's keyboard.
        private static void Keys(params Key[] pressed)
        {
            var state = new KeyboardState(pressed);
            InputSystem.QueueStateEvent(keyboard, state);
        }

        private static IEnumerator Press(Key key, float holdSeconds = 0.05f)
        {
            Keys(key);
            yield return Wait(holdSeconds);
            Keys();
            yield return null;
        }

        // Feet apex above the start over a jump, sampled every frame.
        private static IEnumerator MeasureJump(Action<float> apex)
        {
            HQPlayerController host = Host();
            // A jump needs the ground: the rows teleport just before, and the probe
            // only confirms the floor on the next frame or two.
            double groundedBy = EditorApplication.timeSinceStartup + 1.0;
            while (!host.IsGrounded && EditorApplication.timeSinceStartup < groundedBy) yield return null;
            float startY = host.transform.position.y;
            float best = 0f;
            yield return Press(Key.Space);
            double until = EditorApplication.timeSinceStartup + 1.5;
            while (EditorApplication.timeSinceStartup < until)
            {
                best = Mathf.Max(best, host.transform.position.y - startY);
                yield return null;
            }
            apex(best);
        }

        private static GameObject Block(string name, Vector3 center, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "CheckBlock " + name;
            block.transform.position = center;
            block.transform.localScale = size;
            return block;
        }

        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            // The guest joins whatever port the editor's host is actually on.
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

        // One command, then its reply (the peer answers 0.4 s after executing).
        private static bool ColoursClose(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

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
                    if (reply.Contains("id=" + guestId + ";")) yield break;
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

        // ---- the rows --------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerMovementSettings settings = host.Movement;
            keyboard = InputSystem.AddDevice<Keyboard>("CheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu(); // the real keyboard/mouse stay out of the room
            Keys(); yield return null; yield return null;
            Vector3 home = new(0f, 0f, -3f);
            H.ClientMoveLocalPlayerTo(home); host.SetPitchForChecks(0f); yield return Wait(0.3f);
            Check(host.IsGrounded, "grounded at the start");

            // M1: empty jump apex.
            float apex = 0f;
            yield return MeasureJump(a => apex = a);
            Check(Mathf.Abs(apex - settings.BaseJumpHeight) < 0.08f, $"M1 empty jump apex {apex:0.000} m (target {settings.BaseJumpHeight})");
            Check(host.IsGrounded, "M1 landed");
            // One jump per press: holding Space for 3 s produces exactly one takeoff.
            float startY = host.transform.position.y;
            int takeoffs = 0;
            bool airborne = false;
            Keys(Key.Space);
            double holdUntil = EditorApplication.timeSinceStartup + 3.0;
            while (EditorApplication.timeSinceStartup < holdUntil)
            {
                bool up = host.transform.position.y - startY > 0.05f;
                if (up && !airborne) takeoffs++;
                airborne = up;
                yield return null;
            }
            Keys(); yield return null;
            Check(takeoffs == 1, "M1 holding Space does not auto-hop (takeoffs=" + takeoffs + ")");

            // M2: weight. Two blue balls (12 kg) halve the apex; the purple ball (two hands) forbids it.
            H.ClientMoveLocalPlayerToItem("HeavyBallBlue"); H.ClientLookAtItem("HeavyBallBlue"); yield return null;
            H.ClientRequestGrab("HeavyBallBlue"); yield return Wait(0.3f);
            H.ClientMoveLocalPlayerToItem("HeavyBallBlue (2)"); H.ClientLookAtItem("HeavyBallBlue (2)"); yield return null;
            H.ClientRequestGrab("HeavyBallBlue (2)"); yield return Wait(0.3f);
            yield return WaitUntil(() => host.Inventory.CarriedMassKg > 11.9f, 3f, "12 kg carried");
            H.ClientMoveLocalPlayerTo(home); yield return Wait(0.3f);
            float expected = PlayerMovementMath.JumpHeight(settings, host.Inventory.CarriedMassKg, host.Inventory.CapacityKg);
            yield return MeasureJump(a => apex = a);
            Check(Mathf.Abs(apex - expected) < 0.08f, $"M2 12 kg jump apex {apex:0.000} m (target {expected:0.000})");
            H.ClientRequestDrop(); yield return Wait(0.3f); H.ClientRequestEquip(0); yield return Wait(0.3f); H.ClientRequestDrop(); yield return Wait(0.3f);
            H.ClientRequestEquip(1); yield return Wait(0.3f); H.ClientRequestDrop(); yield return Wait(0.5f);
            yield return WaitUntil(() => host.Inventory.CarriedMassKg < 0.01f, 3f, "hands empty again");
            H.ClientMoveLocalPlayerToItem("HeavyBallPurple"); H.ClientLookAtItem("HeavyBallPurple"); yield return null;
            H.ClientRequestGrab("HeavyBallPurple"); yield return Wait(0.3f);
            Check(host.Inventory.HeldItem != null && host.Inventory.HeldItem.Grip == CarryGrip.TwoHands, "M2 purple ball held in two hands");
            H.ClientMoveLocalPlayerTo(home); yield return Wait(0.3f);
            yield return MeasureJump(a => apex = a);
            Check(apex < 0.03f, $"M2 no jump with a two-handed item ({apex:0.000} m)");
            H.ClientRequestDrop(); yield return Wait(0.5f);

            // M6 (hands, owner view): grab a basketball; both hands on its grips.
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return Wait(0.4f);
            PlayerHands hands = host.GetComponent<PlayerHands>();
            CarryableItem ball = H.Item("Basketball");
            Check(hands != null && hands.HeldForHands == ball, "M6 hands bound to the held basketball");
            ItemHandPose pose = ball.GetComponent<ItemHandPose>();
            Transform handR = host.transform.Find(PlayerHands.ArmRightName + "/" + PlayerHands.HandName);
            Transform handL = host.transform.Find(PlayerHands.ArmLeftName + "/" + PlayerHands.HandName);
            Check(handR != null && handL != null && Vector3.Distance(handR.position, pose.RightGrip.position) < 0.03f && Vector3.Distance(handL.position, pose.LeftGrip.position) < 0.03f, "M6 both palms on the grips (no reach clamp: " + !hands.ReachClamped + ")");
            Check(host.HoldPoint.localPosition.x == 0f && Mathf.Abs(ball.transform.position.x - host.EyePosition.x) < 0.3f, "M6 the ball is centred in front of the body");

            // M8: forward release looking down. Q places it ahead on the floor, not underfoot.
            host.SetPitchForChecks(80f); yield return null;
            Vector3 feet = host.transform.position;
            H.ClientRequestDrop(); yield return Wait(0.8f);
            Vector3 dropped = ball.transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(dropped - feet, Vector3.up);
            Check(ball.State != ItemState.Held && Vector3.Dot(flat.normalized, host.transform.forward) > 0.9f && flat.magnitude >= host.Controller.radius + ball.Radius + 0.05f && dropped.y > 0.05f,
                $"M8 Q looking down drops ahead at {flat.magnitude:0.00} m, y={dropped.y:0.00}");
            Check(Mathf.Abs(host.transform.position.y - feet.y) < 0.02f, "M8 the drop did not lift the player");
            // Throw looking down goes forward, level.
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return Wait(0.4f);
            host.SetPitchForChecks(80f); yield return null;
            H.ClientRequestUse(); yield return Wait(0.15f);
            Rigidbody body = ball.GetComponent<Rigidbody>();
            Vector3 launchVelocity = body.linearVelocity; // read now: the flight sampling below outlasts the fall
            // Flight is smooth: the rendered ball moves every frame, not only on the 50 Hz
            // physics steps (Rigidbody interpolation while the item simulates here).
            int flightFrames = 0, stillFrames = 0; Vector3 lastBall = ball.transform.position;
            for (double until = EditorApplication.timeSinceStartup + 0.4; EditorApplication.timeSinceStartup < until;)
            {
                yield return null;
                flightFrames++;
                if ((ball.transform.position - lastBall).sqrMagnitude < 1e-8f) stillFrames++;
                lastBall = ball.transform.position;
            }
            Check(flightFrames > 20 && stillFrames <= flightFrames / 10, $"M8 the thrown ball moved on {flightFrames - stillFrames} of {flightFrames} rendered frames in flight");
            // The throw follows the crosshair: at 80° down it heads down and forward, and
            // started in front of the player, so it lands ahead rather than underfoot.
            Vector3 flatVelocity = Vector3.ProjectOnPlane(launchVelocity, Vector3.up);
            Check(ball.State == ItemState.Released && launchVelocity.y < -1f && Vector3.Dot(flatVelocity.normalized, host.transform.forward) > 0.9f,
                $"M8 throw looking down goes down and forward (v={launchVelocity}, launch {ball.LastLaunchSpeed:0.0} m/s)");
            yield return Wait(0.6f); // 0.4 s of flight sampling already passed
            Vector3 landedFlat = Vector3.ProjectOnPlane(ball.transform.position - host.transform.position, Vector3.up);
            Check(Vector3.Dot(landedFlat.normalized, host.transform.forward) > 0.5f && landedFlat.magnitude > host.Controller.radius, $"M8 the downward throw ended in front, not under the player ({landedFlat.magnitude:0.00} m ahead)");
            host.SetPitchForChecks(0f);
            yield return Wait(0.5f);
            // M8b: a throw looking up leaves from where the ball is held, not from a
            // chest-height spot below it (Dan, 16 September 2026).
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return Wait(0.4f);
            host.SetPitchForChecks(-60f); yield return Wait(0.2f);
            Vector3 heldUp = ball.transform.position;
            H.ClientRequestUse();
            yield return WaitUntil(() => ball.State == ItemState.Released, 2f, "M8b throw looking up released");
            Vector3 startUp = ball.transform.position;
            Check(Vector3.Distance(startUp, heldUp) < 0.15f && startUp.y > host.EyePosition.y - 0.4f, $"M8b the upward throw starts where the ball was held (moved {Vector3.Distance(startUp, heldUp):0.00} m, y={startUp.y:0.00}, held y={heldUp.y:0.00})");
            yield return Wait(0.1f);
            Check(ball.GetComponent<Rigidbody>().linearVelocity.y > 2f, $"M8b the upward throw goes up (v={ball.GetComponent<Rigidbody>().linearVelocity})");
            host.SetPitchForChecks(0f);
            yield return Wait(1.5f);
            // Refusal against a wall: face the south wall from 0.35 m and try to drop.
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return Wait(0.4f);
            H.ClientMoveLocalPlayerTo(new Vector3(0f, 0f, -5.55f)); yield return null;
            host.transform.rotation = Quaternion.Euler(0f, 180f, 0f); yield return null;
            H.ClientRequestDrop(); yield return Wait(0.5f);
            Check(ball.State == ItemState.Held && H.PromptText().Contains("Not enough room"), "M8 no room against the wall: refused, still held (" + H.PromptText() + ")");
            host.transform.rotation = Quaternion.identity; H.ClientMoveLocalPlayerTo(home); yield return null;
            H.ClientRequestDrop(); yield return Wait(0.8f);

            // M8c: a throw whose release the owner cancels after a second grab was
            // granted does not come back into hands that are full (code check, 18
            // September 2026): it lies where it was placed; the second item stays held.
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return Wait(0.4f);
            host.SetPitchForChecks(-45f); yield return Wait(0.2f);
            H.ClientRequestUse();
            yield return WaitUntil(() => ball.State == ItemState.Released, 2f, "M8c the ball is thrown (Released)");
            host.SetPitchForChecks(0f);
            CarryableItem heavy = H.Item("HeavyBallBlue");
            H.ClientMoveLocalPlayerToItem("HeavyBallBlue"); H.ClientLookAtItem("HeavyBallBlue"); yield return null;
            H.ClientRequestGrab("HeavyBallBlue");
            yield return WaitUntil(() => heavy.State == ItemState.Held && heavy.HolderClientId == host.OwnerId, 2f, "M8c the heavy ball is in the hands while the throw is still in the air");
            if (ball.State == ItemState.Released)
            {
                ball.ServerCancelReleaseForChecks(host.Owner); yield return Wait(0.4f);
                Check(ball.State == ItemState.Free && ball.HolderClientId < 0, "M8c the cancelled throw lies loose, not back in full hands (" + ball.State + ")");
                Check(heavy.State == ItemState.Held && heavy.HolderClientId == host.OwnerId && host.Inventory.HeldItem == heavy, "M8c the heavy ball is still the one held");
            }
            else File.AppendAllText(Log, "  · M8c skipped: the ball came to rest before the cancel (" + ball.State + ")\n");
            H.ClientRequestDrop(); yield return Wait(0.8f);
            H.ClientMoveLocalPlayerTo(home); yield return Wait(0.3f);

            // M9: no standing on cargo. Stand where a ball lies: the feet end on the floor.
            Vector3 ballSpot = ball.transform.position;
            H.ClientMoveLocalPlayerTo(new Vector3(ballSpot.x, ballSpot.y + 0.6f, ballSpot.z)); yield return Wait(1.0f);
            Check(host.transform.position.y < 0.05f, $"M9 walked through the ball to the floor (y={host.transform.position.y:0.00})");
            H.ClientMoveLocalPlayerTo(home); yield return Wait(0.3f);

            // M3: crouch dimensions, eye and speed.
            Keys(Key.LeftCtrl); yield return Wait(0.4f);
            Check(host.IsCrouched && Mathf.Abs(host.Controller.height - settings.CrouchHeight) < 1e-3f && Mathf.Abs(host.Controller.center.y - settings.CrouchHeight * 0.5f) < 1e-3f && Mathf.Abs(host.Controller.radius - settings.CapsuleRadius) < 1e-4f,
                $"M3 crouched capsule {host.Controller.height:0.00} m, centre {host.Controller.center.y:0.00}, radius unchanged");
            Check(Mathf.Abs(host.EyeHeight - settings.CrouchEyeHeight) < 0.02f, $"M3 crouched eye {host.EyeHeight:0.00} m");
            Vector3 before = host.transform.position;
            Keys(Key.LeftCtrl, Key.W, Key.LeftShift); yield return Wait(1.0f); Keys(Key.LeftCtrl); yield return null;
            float walked = Vector3.ProjectOnPlane(host.transform.position - before, Vector3.up).magnitude;
            float crouchSpeed = 4f * settings.CrouchSpeedFactor * host.SpeedFactor;
            Check(Mathf.Abs(walked - crouchSpeed) < 0.5f, $"M3 crouch walk {walked:0.00} m in 1 s (sprint ignored; target {crouchSpeed:0.0})");
            yield return MeasureJump(a => apex = a);
            Check(apex < 0.03f, "M3 no jump while crouched");
            Keys(); yield return Wait(0.5f);
            Check(!host.IsCrouched && Mathf.Abs(host.Controller.height - settings.StandingHeight) < 1e-3f, "M3 stood up when Ctrl released");

            // M4: blocked standing under a shelf; stands when it is gone.
            H.ClientMoveLocalPlayerTo(home); yield return Wait(0.2f);
            Keys(Key.LeftCtrl); yield return Wait(0.4f);
            GameObject shelf = Block("Shelf", home + Vector3.up * 1.45f, new Vector3(2f, 0.2f, 2f));
            yield return Wait(0.2f);
            Keys(); yield return Wait(0.8f);
            Check(host.IsCrouched, "M4 stays crouched under the shelf after releasing Ctrl");
            UnityEngine.Object.Destroy(shelf); yield return Wait(0.8f);
            Check(!host.IsCrouched, "M4 stands once the shelf is gone");

            // M7: a guest crouches; the host sees its capsule, body and hands; late-join state.
            guest = LaunchGuest();
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().Length == 2, 40f, "guest player spawned");
            HQPlayerController remote = UnityEngine.Object.FindObjectsByType<HQPlayerController>().First(p => !p.IsOwner);
            yield return GuestEventually(r => r.Contains("local=True"), 15f, "M7 guest owns its player");
            yield return Send("{\"id\":{id},\"action\":\"crouch\",\"slot\":1}");
            yield return WaitUntil(() => remote.IsCrouched && Mathf.Abs(remote.Controller.height - settings.CrouchHeight) < 1e-3f, 5f, "host sees the guest crouched");
            File.AppendAllText(Log, "PASS M7 host sees the guest crouched: height " + remote.Controller.height + "\n");
            yield return GuestEventually(r => GuestPlayerLine(r, remote.OwnerId).Contains("crouched=True") && GuestPlayerLine(r, remote.OwnerId).Contains("height=1"), 5f, "M7 guest's own snapshot is crouched");
            yield return Send("{\"id\":{id},\"action\":\"crouch\",\"slot\":0}");
            yield return WaitUntil(() => !remote.IsCrouched, 5f, "host sees the guest standing");
            File.AppendAllText(Log, "PASS M7 host sees the guest stand again\n");
            // Hands on the remote view: the guest grabs a ball; the host's copy of its arms binds to it.
            // P: the colour panel on the HQ wall. Look at it, press E, pick a swatch:
            // the body wears it, the pick is saved, the guest sees it; the guest picks too.
            PlayerIdentity hostIdentity = host.GetComponent<PlayerIdentity>();
            Check(hostIdentity != null && hostIdentity.ColourIndex == 2, "P0 the host's saved colour (amber, 2) reached its player: " + (hostIdentity == null ? "no identity" : hostIdentity.ColourIndex.ToString()));
            GameObject panel = GameObject.Find(SunkCost.World.ColourPanel.PanelName);
            Check(panel != null, "P1 the colour panel is on the HQ wall");
            H.ClientMoveLocalPlayerTo(new Vector3(panel.transform.position.x, 0f, panel.transform.position.z + 1.3f)); yield return null;
            H.ClientLookAtNamed(SunkCost.World.ColourPanel.PanelName); yield return null; yield return null;
            Check(host.CurrentColourPanel != null && H.PromptText().Contains("Press E to pick your colour"), "P1 looking at the panel offers the pick: " + H.PromptText());
            Keys(Key.E); yield return null; yield return null; Keys(); yield return null;
            Check(SunkCost.Net.SessionInputGate.PickerOpen, "P2 E opens the colour picker");
            host.GetComponent<PlayerHudUI>().PickColourForChecks(9); // blue
            yield return WaitUntil(() => hostIdentity.ColourIndex == 9, 3f, "P3 the pick was written by the server");
            yield return null;
            Check(ColoursClose(host.BodyColour, PlayerPalette.Get(9)), $"P3 the body wears the pick ({host.BodyColour} vs {PlayerPalette.Get(9)})");
            Check(PlayerColourPrefs.Load() == 9, "P3 the pick is saved for next time");
            SunkCost.Net.SessionInputGate.ClosePicker(); yield return null;
            Check(!SunkCost.Net.SessionInputGate.PickerOpen, "P2 the picker closes");
            yield return GuestEventually(r => GuestPlayerLine(r, host.OwnerId).Contains("colour=9"), 5f, "P4 the guest sees the host's colour");
            yield return Send("{\"id\":{id},\"action\":\"colour\",\"slot\":13}"); // pink
            PlayerIdentity remoteIdentity = remote.GetComponent<PlayerIdentity>();
            yield return WaitUntil(() => remoteIdentity != null && remoteIdentity.ColourIndex == 13, 5f, "P4 the guest's pick reached the host");
            yield return null;
            Check(ColoursClose(remote.BodyColour, PlayerPalette.Get(13)), "P4 the host's copy of the guest wears the guest's colour");
            yield return Send("{\"id\":{id},\"action\":\"colour\",\"slot\":99}"); // out of the palette: refused, kept
            yield return Wait(0.5f);
            Check(remoteIdentity.ColourIndex == 13, "P5 an index outside the palette does not stick");

            // A ball the host's rows did not move: Basketball (2) at its fixture spot.
            CarryableItem target = H.Item("Basketball (2)");
            Vector3 stand = target.transform.position + new Vector3(0f, 0f, 0.7f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":{\"x\":" + stand.x + ",\"y\":0,\"z\":" + stand.z + "}}");
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":{\"x\":0,\"y\":-0.3,\"z\":-1}}");
            yield return Send("{\"id\":{id},\"action\":\"grab\",\"item\":\"#" + target.ObjectId + "\"}");
            PlayerHands remoteHands = remote.GetComponent<PlayerHands>();
            yield return WaitUntil(() => remoteHands.HeldForHands == target, 5f, "host's copy of the guest's hands binds to its held ball");
            File.AppendAllText(Log, "PASS M7 remote hands follow the guest's held item on the host\n");
            yield return GuestEventually(r => GuestPlayerLine(r, host.OwnerId).Contains("hands=rest") && GuestPlayerLine(r, remote.OwnerId).Contains("hands=Basketball"), 5f, "M7 guest sees its own hands on the ball and the host's at rest");
        }
    }
}
