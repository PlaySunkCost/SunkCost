using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Monsters;
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
    // The Long Walker's polish checks (mon-walker, 24 September 2026), the host alone
    // below, an empty roster (the rows spawn their own Walker): the idle, the chase
    // (speed, facing, the clip and its stride), a wall (no grab through it, around
    // it), and the grab — Dan's decision: it grabs you, lifts you to its face for
    // about two seconds, then you die. The nine grab cases of the checklist:
    //   1 a stationary diver caught (G1)            2 a moving diver caught (G2)
    //   3 caught at the right distance (G1, G2)     4 visual contact and the hold (G1)
    //   5 the diver's state (G1)                    6 the chase stops (G1)
    //   7 repeated triggers (G3)                    8 the end state (G1)
    //   9 controls and physics back (E1 after the revive, G2/G3 after a refused kill)
    // and the Impostor's touch unchanged (I1). Log Temp/polish-LongWalker-matrix.log;
    // captures in Temp/polish-LongWalker/. Job "polish-LongWalker". With
    // Temp/polish-LongWalker-guest.flag present the same job runs the guest's rows
    // instead (R1, R2): a guest build (Builds/HQPrototypeLocal) joins; held, its own
    // screen shows the hold (R1), and dead, its spectator view shows the host's (R2).
    public static class MonsterPolishLongWalkerChecks
    {
        private const string Log = "Temp/polish-LongWalker-matrix.log";
        private const string Shots = "Temp/polish-LongWalker";
        private const string GuestFlag = "Temp/polish-LongWalker-guest.flag";
        private const string GuestDir = "Temp/polish-LongWalker-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private static bool guestMode;
        private static Process guest;
        private static int guestCommand = 3100;
        private static string lastReply = string.Empty;

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static GameObject wall;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static MonsterSettings Settings => MonsterSettings.Get();

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            Directory.CreateDirectory(Shots);
            guestMode = File.Exists(GuestFlag);
            if (guestMode && !File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first (the guest rows).");
            File.WriteAllText(Log, "Long Walker polish checks started " + DateTime.Now + (guestMode ? " (the guest rows)" : string.Empty) + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Long Walker polish: MATRIX_PASS"); else Debug.LogError("Long Walker polish: " + Status);
            steps = null;
            stack.Clear();
            Creature.RefuseGrabKillForChecks = false;
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            if (wall != null) UnityEngine.Object.Destroy(wall);
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
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + M.MonstersText() + "\n" + (Host() != null ? Host().GrabStatus : "no host"));
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
        private static IEnumerator Press(Key key, float holdSeconds = 0.05f)
        {
            Keys(key); yield return Wait(holdSeconds); Keys(); yield return null;
        }
        private static float Flat(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);
        private static Vector3 Seabed(ElevatorController car, float bearingDeg, float distance) =>
            car.BottomPosition + Quaternion.Euler(0f, bearingDeg, 0f) * Vector3.forward * distance + Vector3.up * 0.15f;
        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            Vector3 to = facing - spot; to.y = 0f;
            host.TeleportLocal(spot, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : Vector3.forward, Vector3.up).eulerAngles.y);
            yield return null; yield return null;
            M.ClientLookAt(facing);
            yield return null;
        }
        private static IEnumerator Despawn()
        {
            M.ServerDespawnMonsters();
            yield return Expect(() => Creature.All.Count == 0, 5f, () => "every monster despawned (" + Creature.All.Count + " left)");
        }
        private static Creature Spawn(MonsterKind kind, Vector3 at, float yaw)
        {
            Creature c = MonsterRoster.ServerSpawnForChecks(kind, at);
            Check(c != null, "spawned " + kind + " at " + at.ToString("F1"));
            c.ServerPlaceForChecks(at, yaw);
            return c;
        }
        private static float YawTo(Vector3 from, Vector3 to) { Vector3 d = to - from; d.y = 0f; return Quaternion.LookRotation(d, Vector3.up).eulerAngles.y; }
        private static Transform Named(Component root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        private static string AnimState(Creature c)
        {
            CreatureRig rig = c.GetComponent<CreatureRig>();
            if (rig == null || !rig.HasAnimator) return "no animator";
            Animator a = rig.Animator;
            AnimatorClipInfo[] clips = a.GetCurrentAnimatorClipInfo(0);
            string now = clips.Length > 0 ? clips[0].clip.name : "?";
            if (a.IsInTransition(0)) { AnimatorClipInfo[] next = a.GetNextAnimatorClipInfo(0); now += "->" + (next.Length > 0 ? next[0].clip.name : "?"); }
            return now;
        }
        private static bool InState(Creature c, string clip)
        {
            string s = AnimState(c);
            return s == clip || s.EndsWith("->" + clip);
        }
        private static float FacingError(Creature c, Vector3 target)
        {
            Vector3 to = target - c.transform.position; to.y = 0f;
            Vector3 fwd = c.transform.forward; fwd.y = 0f;
            return Vector3.Angle(fwd, to);
        }

        private static IEnumerator Descend(int day)
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            Check(Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), "down in the dive site: " + H.RideStatus());
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(1.0f);
            Check(Day.Phase == DayPhase.DiveInProgress && Day.Day == day, $"dive in progress on day {day}");
        }

        // The held diver as the grab should show them, sampled every frame of a hold:
        // the gap to the holder, the eye to the face, the palms to the chest, the
        // camera on the face, the Walker still, W pressed doing nothing.
        private sealed class HoldProbe
        {
            public float MaxEyeCameraError, MaxFaceAngle, MinFaceDistance = 99f, MaxFaceDistance, MaxPalmGap, MaxWalkerDrift, MaxDriftFromHold, EyeLevelAtEnd = float.NaN, FaceGapAtEnd = float.NaN;
            public int Frames, CapsuleOnFrames, UnlockedFrames, NotGrabbingPose;
            public string Worst = string.Empty;
        }

        private static IEnumerator ProbeHold(HQPlayerController host, Creature walker, HoldProbe p, float untilSeconds, bool pressKeys)
        {
            Transform face = Named(walker, "Face"), gripL = Named(walker, "GripL"), gripR = Named(walker, "GripR");
            CreatureGrab core = walker.GrabCore;
            Vector3 walkerAt = walker.transform.position;
            if (pressKeys) Keys(Key.W, Key.LeftShift);
            while (host.IsGrabbed && walker.ServerGrabSeconds < untilSeconds)
            {
                yield return null;
                if (!host.IsGrabbed) break;
                float t = walker.ServerGrabSeconds;
                p.Frames++;
                if (host.Controller.enabled) p.CapsuleOnFrames++;
                if (!host.GrabbedLocal) p.UnlockedFrames++;
                if (walker.Pose != CreaturePose.Grabbing) p.NotGrabbingPose++;
                p.MaxWalkerDrift = Mathf.Max(p.MaxWalkerDrift, Vector3.Distance(walker.transform.position, walkerAt));
                host.EyePose(out Vector3 eye, out Quaternion look);
                Transform cam = host.PlayerCamera.transform;
                p.MaxEyeCameraError = Mathf.Max(p.MaxEyeCameraError, Vector3.Distance(cam.position, eye));
                GrabPose pose = core.HoldPose(host.Grab.CaughtAt, host.GrabSeconds);
                p.MaxDriftFromHold = Mathf.Max(p.MaxDriftFromHold, t > core.GripSeconds ? Vector3.Distance(host.transform.position, pose.Feet) : 0f);
                if (face != null)
                {
                    Vector3 toFace = face.position - cam.position;
                    float angle = Vector3.Angle(cam.forward, toFace);
                    if (t > core.LiftSeconds) { p.MaxFaceAngle = Mathf.Max(p.MaxFaceAngle, angle); p.MinFaceDistance = Mathf.Min(p.MinFaceDistance, toFace.magnitude); p.MaxFaceDistance = Mathf.Max(p.MaxFaceDistance, toFace.magnitude); }
                    if (t > core.HoldSeconds - 0.25f) { p.EyeLevelAtEnd = cam.position.y - face.position.y; p.FaceGapAtEnd = Flat(cam.position, face.position); }
                }
                if (gripL != null && gripR != null && t > core.LiftSeconds)
                {
                    // The palms on the diver's chest: each within its reach of the capsule's surface.
                    Vector3 chest = host.transform.position + Vector3.up * 1.2f;
                    float gap = Mathf.Max(Flat(gripL.position, chest), Flat(gripR.position, chest)) - host.Controller.radius;
                    float dy = Mathf.Max(Mathf.Abs(gripL.position.y - chest.y), Mathf.Abs(gripR.position.y - chest.y));
                    p.MaxPalmGap = Mathf.Max(p.MaxPalmGap, Mathf.Max(gap, dy - 0.35f));
                }
            }
            if (pressKeys) Keys();
        }

        // ---- the guest (the replication rows) ---------------------------------------------

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
        private static IEnumerator Snapshot() { yield return Send("{\"id\":{id},\"action\":\"snapshot\"}"); }
        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Snapshot();
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.2f);
            }
            throw new Exception(label + "\n" + lastReply);
        }
        private static string Vec(Vector3 v) => "{\"x\":" + F(v.x) + ",\"y\":" + F(v.y) + ",\"z\":" + F(v.z) + "}";
        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static string Text(string line, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, "(?:^|; )" + key + "=([^;]*)");
            return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
        }
        private static Vector3 VecField(string line, string key)
        {
            string[] parts = Text(line, key).Trim('(', ')').Split(',');
            if (parts.Length != 3) return new Vector3(float.NaN, float.NaN, float.NaN);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(parts[0], inv), float.Parse(parts[1], inv), float.Parse(parts[2], inv));
        }
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static IEnumerator GuestMove(Vector3 to) { yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static IEnumerator GuestLook(Vector3 aim) { yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(aim) + "}"); }
        private static IEnumerator GuestLamp(bool on) { yield return Send("{\"id\":{id},\"action\":\"lamp\",\"slot\":" + (on ? 1 : 0) + "}"); }

        // What another screen shows of a hold: the camera the guest's snapshot reports (its
        // own eyes when alive, its spectator view when dead) against the held diver's eye
        // pose as the host computes it, and against the Walker's Face anchor on the host.
        private static void CompareView(string row, string line, HQPlayerController held, Creature walker, float maxMetres, float maxDegrees)
        {
            Vector3 cam = VecField(line, "camPos"), fwd = VecField(line, "camFwd");
            held.EyePose(out Vector3 eye, out Quaternion _);
            Transform face = Named(walker, "Face");
            Vector3 toFace = (face != null ? face.position : walker.EyePoint) - cam;
            float gap = Vector3.Distance(cam, eye), angle = Vector3.Angle(fwd, toFace);
            Say($"{row} the guest's camera at {cam:F2} looking {fwd:F2}; the held eyes on the host {eye:F2}: {gap:0.00} m apart; the face {toFace.magnitude:0.00} m off, {angle:0.0} deg from the view");
            Check(gap < maxMetres, $"{row} the guest's camera is at the held diver's eyes ({gap:0.00} m)");
            Check(angle < maxDegrees && toFace.magnitude < 0.9f, $"{row} and it looks into the Walker's face ({angle:0.0} deg, {toFace.magnitude:0.00} m)");
        }

        private static IEnumerator GuestRows(HQPlayerController host, int guestId, ElevatorController car)
        {
            HQPlayerController guestCopy = GuestCopy();
            Heading("R1 — the guest is caught: its own screen is lifted to the Walker's face, the host sees it held, then it dies");
            if (host.LampOn) yield return Press(Key.F);
            yield return Expect(() => !host.LampOn, 2f, () => "R1 the host's lamp is off");
            yield return HostAt(Seabed(car, 300f, 34f), Seabed(car, 300f, 36f));
            Vector3 guestSpot = Seabed(car, 60f, 24f), walkerAt = Seabed(car, 60f, 30f);
            yield return GuestLamp(true);
            yield return GuestMove(guestSpot);
            yield return GuestLook(walkerAt + Vector3.up * 2.5f);
            yield return GuestEventually(r => Flat(VecField(GuestPlayerLine(r, guestId), "position"), guestSpot) < 1.5f && Text(GuestPlayerLine(r, guestId), "lamp") == "True", 8f, "R1 the guest stands lit on the seabed");
            int felt0 = int.TryParse(Text(GuestPlayerLine(lastReply, guestId), "grabsFelt"), out int f0) ? f0 : 0;
            Creature walker = Spawn(MonsterKind.LongWalker, walkerAt, YawTo(walkerAt, guestSpot));
            yield return Expect(() => walker.Pose == CreaturePose.Hunting && walker.TargetId == guestId, 4f, () => "R1 it hunts the lit guest: " + walker.ServerStatus);
            yield return Expect(() => walker.ServerGrabbing && walker.ServerGrabVictim == guestCopy, 12f, () => "R1 it caught the guest: " + walker.ServerStatus);
            int holderId = walker.NetworkObject.ObjectId;
            yield return GuestEventually(r => Text(GuestPlayerLine(r, guestId), "grabbed") == "True" && Text(GuestPlayerLine(r, guestId), "grabHolder") == holderId.ToString(), 1.5f, $"R1 the guest reads itself held by the Walker (object {holderId})");
            CreatureGrab core = walker.GrabCore;
            yield return Expect(() => walker.ServerGrabSeconds > core.LiftSeconds + 0.1f, core.LiftSeconds + 1f, () => "R1 past the lift");
            yield return Snapshot();
            string line = GuestPlayerLine(lastReply, guestId);
            Check(int.TryParse(Text(line, "grabsFelt"), out int felt) && felt == felt0 + 1, $"R1 the guest's owner applied the hold once ({felt0} → {Text(line, "grabsFelt")})");
            Check(Text(line, "controllerOn") == "False", "R1 the guest's capsule is off while held");
            CompareView("R1", line, guestCopy, walker, 0.15f, 16f);
            GrabPose pose = core.HoldPose(guestCopy.Grab.CaughtAt, walker.ServerGrabSeconds);
            Check(Vector3.Distance(guestCopy.transform.position, pose.Feet) < 0.3f, $"R1 the host sees the guest's copy lifted on the hold point ({Vector3.Distance(guestCopy.transform.position, pose.Feet):0.00} m off, {guestCopy.transform.position.y - walker.transform.position.y:0.00} m up)");
            H.CaptureFrom(walker.transform.position + walker.transform.right * 4.5f + walker.transform.forward * 1.5f + Vector3.up * 2.2f, walker.transform.position + walker.transform.forward * 0.6f + Vector3.up * 2.4f, Shots + "/R1-guest-held-seen-by-host.png");
            yield return Expect(() => guestCopy.IsDead, core.HoldSeconds + 1f, () => "R1 the hold ended in the guest's death: " + walker.ServerStatus);
            yield return GuestEventually(r => Text(GuestPlayerLine(r, guestId), "dead") == "True" && Text(GuestPlayerLine(r, guestId), "grabbed") == "False", 3f, "R1 the guest reads itself dead and let go of");
            yield return Expect(() => !walker.ServerGrabbing, core.ReleaseSeconds + 1f, () => "R1 the Walker let go: " + walker.ServerStatus);
            yield return Despawn();

            Heading("R2 — the dead guest spectates the host; the host is caught (the kill refused): the spectator's view is the host's held eyes");
            yield return GuestEventually(r => Text(GuestPlayerLine(r, guestId), "spectatorActive") == "True", 15f, "R2 the dead guest spectates");
            if (Text(GuestPlayerLine(lastReply, guestId), "spectatorTarget") != host.OwnerId.ToString())
                yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return GuestEventually(r => Text(GuestPlayerLine(r, guestId), "spectatorTarget") == host.OwnerId.ToString(), 5f, "R2 it watches the host");
            Creature.RefuseGrabKillForChecks = true;
            Vector3 stand = Seabed(car, 110f, 22f);
            walkerAt = Seabed(car, 110f, 28f);
            yield return HostAt(stand, walkerAt);
            yield return Press(Key.F);
            yield return Expect(() => host.LampOn, 2f, () => "R2 the host's lamp is on");
            walker = Spawn(MonsterKind.LongWalker, walkerAt, YawTo(walkerAt, stand));
            yield return Expect(() => walker.ServerGrabbing && walker.ServerGrabVictim == host, 12f, () => "R2 it caught the host: " + walker.ServerStatus);
            core = walker.GrabCore;
            yield return Expect(() => walker.ServerGrabSeconds > core.LiftSeconds + 0.1f, core.LiftSeconds + 1f, () => "R2 past the lift");
            yield return Snapshot();
            line = GuestPlayerLine(lastReply, guestId);
            Check(Text(GuestPlayerLine(lastReply, host.OwnerId), "grabbed") == "True", "R2 the guest reads the host held");
            CompareView("R2", line, host, walker, 0.15f, 16f);
            yield return Expect(() => !host.IsGrabbed && !walker.ServerGrabbing, core.HoldSeconds + core.ReleaseSeconds + 1f, () => "R2 the kill refused: let go: " + walker.ServerStatus);
            yield return GuestEventually(r => Text(GuestPlayerLine(r, host.OwnerId), "grabbed") == "False" && Text(GuestPlayerLine(r, host.OwnerId), "dead") == "False", 3f, "R2 the guest reads the host let go of, alive");
            Creature.RefuseGrabKillForChecks = false;
            yield return Despawn();
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerVitals vitals = host.Vitals;
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            MonsterSettings s = Settings;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("WalkerCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            Check(MonsterSettings.RosterOverrideForTests != null && MonsterSettings.RosterOverrideForTests.Length == 0, "the polish job runs with an empty roster");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterCatalog.PrefabPath(MonsterKind.LongWalker));
            CreatureGrab prefabGrab = prefab.GetComponent<CreatureGrab>();
            Check(prefabGrab != null && prefabGrab.enabled, "the Walker's prefab carries a CreatureGrab");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(MonsterCatalog.PrefabPath(MonsterKind.Impostor)).GetComponent<CreatureGrab>() == null, "the Impostor's prefab carries none (it touches, it does not grab)");
            Say($"grab: grip {prefabGrab.GripSeconds} s, lift {prefabGrab.LiftSeconds} s, kill at {prefabGrab.HoldSeconds} s, release {prefabGrab.ReleaseSeconds} s; grip {prefabGrab.GripPoint}, lift {prefabGrab.LiftPoint}, face {prefabGrab.FaceTarget}; reach {s.ReachMeters} m, standoff {s.TouchStandoffMeters:0.00} m, walker ×{s.WalkerSpeedFactor}");

            Heading("M0 — to sea and down, alone, on day 1");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            int guestId = -1;
            if (guestMode)
            {
                guest = LaunchGuest();
                yield return Expect(() => GuestCopy() != null, 40f, () => "the guest's copy is here");
                guestId = GuestCopy().OwnerId;
                yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "the guest joined at sea");
                ShipParts sea = ShipParts.InWorld(WorldId.Sea);
                yield return GuestMove(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f));
                yield return Wait(0.5f);
            }
            yield return Descend(1);
            if (guestMode) yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "the guest is in the site");
            ElevatorController car = WorldSceneFlow.FindCar();
            yield return Wait(s.WakeDelaySeconds + 0.5f);
            Check(Creature.All.Count == 0, "no roster monster on the seabed");
            if (guestMode)
            {
                yield return GuestRows(host, guestId, car);
                Say("done (the guest rows): " + M.MonstersText());
                yield break;
            }

            Heading("I0 — idle: a diver in the dark and far away is not seen; it stands and breathes");
            Vector3 stand = Seabed(car, 20f, 26f);
            Vector3 walkerAt = Seabed(car, 20f, 40f);
            yield return Press(Key.F);
            yield return Expect(() => !host.LampOn, 2f, () => "lamp off");
            yield return HostAt(stand, walkerAt);
            Creature walker = Spawn(MonsterKind.LongWalker, walkerAt, YawTo(walkerAt, stand) + 90f);
            yield return Wait(1.5f);
            Vector3 idleAt = walker.transform.position;
            yield return Wait(2f);
            Check(walker.Pose == CreaturePose.Idle && walker.TargetId == -1, "I0 it has not seen the dark diver 14 m away: " + walker.ServerStatus);
            Check(Vector3.Distance(walker.transform.position, idleAt) < 0.05f, "I0 idle, it stays where it stands");
            Check(InState(walker, "Idle"), "I0 the Idle clip plays: " + AnimState(walker));
            CreatureRig rig = walker.GetComponent<CreatureRig>();
            Check(rig != null && rig.Speed < 0.1f, $"I0 the rig reads no speed ({rig?.Speed:0.00})");
            H.CaptureFrom(walkerAt + (stand - walkerAt).normalized * 5f + Vector3.up * 2f, walkerAt + Vector3.up * 1.8f, Shots + "/I0-idle.png");

            Heading("C1 — the chase: lit, it sees you, turns, and walks after you at 0.6 of a walk, its clip in step");
            yield return Press(Key.F);
            yield return Expect(() => host.LampOn, 2f, () => "lamp on");
            yield return Expect(() => walker.Pose == CreaturePose.Hunting && walker.TargetId == host.OwnerId, 3f, () => "C1 it saw the lit diver and hunts: " + walker.ServerStatus);
            yield return Wait(1.2f);
            Check(FacingError(walker, host.transform.position) < 15f, $"C1 it turned to face the diver ({FacingError(walker, host.transform.position):0} deg off)");
            float d0 = Flat(walker.transform.position, host.transform.position); float t0 = Time.unscaledTime;
            float worstFacing = 0f;
            while (Time.unscaledTime - t0 < 2.5f) { worstFacing = Mathf.Max(worstFacing, FacingError(walker, host.transform.position)); yield return null; }
            float closed = (d0 - Flat(walker.transform.position, host.transform.position)) / (Time.unscaledTime - t0);
            float expected = host.WalkSpeed * s.WalkerSpeedFactor;
            Check(Mathf.Abs(closed - expected) < expected * 0.12f, $"C1 it closes at {closed:0.00} m/s (walking {host.WalkSpeed} × {s.WalkerSpeedFactor} = {expected:0.00})");
            Check(worstFacing < 8f, $"C1 it walks facing the diver (worst {worstFacing:0.0} deg)");
            Check(InState(walker, "Hunting"), "C1 the Hunting clip plays: " + AnimState(walker));
            Check(Mathf.Abs(rig.Speed - expected) < 0.35f, $"C1 the rig reads its speed ({rig.Speed:0.00} m/s)");
            float playback = rig.Animator.speed;
            Say($"C1 animator speed {playback:0.00} (the stride match), state {AnimState(walker)}");
            H.CaptureFrom(walker.transform.position + walker.transform.right * 6f + Vector3.up * 1.8f, walker.transform.position + Vector3.up * 1.6f, Shots + "/C1-chase-side.png");

            yield return Despawn();

            Heading("G1 — cases 1, 3, 4, 5, 6, 8: a diver standing still is caught at reach, held at its face, and dies at the end of the hold");
            stand = Seabed(car, 60f, 24f);
            walkerAt = Seabed(car, 60f, 30f);
            yield return HostAt(stand, walkerAt);
            walker = Spawn(MonsterKind.LongWalker, walkerAt, YawTo(walkerAt, stand));
            yield return Expect(() => walker.Pose == CreaturePose.Hunting, 3f, () => "G1 hunting: " + walker.ServerStatus);
            Vector3 hostBefore = host.transform.position;
            float caughtDistance = -1f, caughtAt = -1f;
            float catchDeadline = Time.unscaledTime + 10f;
            while (!walker.ServerGrabbing && Time.unscaledTime < catchDeadline)
            {
                yield return null;
                if (Flat(walker.transform.position, host.transform.position) > 8f) throw new Exception("G1 the Walker walked away: " + walker.ServerStatus);
            }
            Check(walker.ServerGrabbing, "G1 case 1: it reached the diver and caught them: " + walker.ServerStatus);
            caughtDistance = Flat(walker.transform.position, host.transform.position); caughtAt = Time.unscaledTime;
            Check(caughtDistance <= s.ReachMeters + 0.05f && caughtDistance >= s.TouchStandoffMeters - 0.15f, $"G1 case 3: caught at {caughtDistance:0.00} m, centre to centre (reach {s.ReachMeters}, standoff {s.TouchStandoffMeters:0.00}; the capsules {walker.GetComponent<CharacterController>().radius + host.Controller.radius:0.00})");
            Check(Flat(hostBefore, host.transform.position) < 0.3f, "G1 case 1: the diver had not moved");
            Check(walker.Pose == CreaturePose.Grabbing && walker.ServerGrabVictim == host, "G1 case 6: its pose is Grabbing, holding the host: " + walker.ServerStatus);
            yield return null; yield return null;
            Check(host.IsGrabbed && host.GrabbedLocal, "G1 case 5: the diver is held (replicated) and the owner applied it: " + host.GrabStatus);
            Check(!host.Controller.enabled, "G1 case 5: the capsule is off while held (the hold carries the root)");
            Check(!host.TryDash(Vector2.up) && host.DashRefusal == "Not now", "G1 case 5: no dash while held");
            Check(!host.CanJump(), "G1 case 5: no jump while held");
            var probe = new HoldProbe();
            // Half the hold with W and Shift down: the keys must do nothing.
            yield return ProbeHold(host, walker, probe, walker.GrabCore.LiftSeconds + 0.1f, pressKeys: true);
            yield return Expect(() => InState(walker, "Grabbing"), 1f, () => "G1 case 4: the Grabbing clip plays: " + AnimState(walker));
            H.CaptureLocalCamera(Shots + "/G1-held-own-view.png");
            H.CaptureFrom(walker.transform.position + walker.transform.right * 4.5f + walker.transform.forward * 1.5f + Vector3.up * 2.2f, walker.transform.position + walker.transform.forward * 0.6f + Vector3.up * 2.4f, Shots + "/G1-held-side.png");
            yield return ProbeHold(host, walker, probe, 99f, pressKeys: false);
            float heldFor = Time.unscaledTime - caughtAt;
            Say($"G1 hold: {probe.Frames} frames; eye vs camera ≤ {probe.MaxEyeCameraError:0.000} m; face within {probe.MaxFaceAngle:0.0} deg of the view, {probe.MinFaceDistance:0.00}–{probe.MaxFaceDistance:0.00} m; eye {probe.EyeLevelAtEnd:+0.00;-0.00} m over the face and {probe.FaceGapAtEnd:0.00} m in front of it at the end; palms ≤ {probe.MaxPalmGap:0.00} m off the chest; the diver ≤ {probe.MaxDriftFromHold:0.000} m off the hold; the Walker moved ≤ {probe.MaxWalkerDrift:0.000} m");
            Check(probe.Frames > 30 && probe.UnlockedFrames == 0 && probe.CapsuleOnFrames == 0, $"G1 case 5: locked all the hold ({probe.Frames} frames, {probe.UnlockedFrames} unlocked, {probe.CapsuleOnFrames} with the capsule on)");
            Check(probe.NotGrabbingPose == 0 && probe.MaxWalkerDrift < 0.05f, $"G1 case 6: it stood in its Grabbing pose the whole hold (moved {probe.MaxWalkerDrift:0.000} m)");
            Check(probe.MaxDriftFromHold < 0.05f, $"G1 case 4: the diver stayed on the hold point, W and Shift held down ({probe.MaxDriftFromHold:0.000} m)");
            Check(probe.MaxEyeCameraError < 0.05f, $"G1 case 5: the owner's camera is the replicated eye pose spectators and the TV render ({probe.MaxEyeCameraError:0.000} m)");
            Check(probe.MaxFaceAngle < 16f, $"G1 case 4: the view is turned to its face, the shake included ({probe.MaxFaceAngle:0.0} deg)");
            Check(probe.MinFaceDistance > 0.25f && probe.MaxFaceDistance < 0.8f, $"G1 case 4: face to face, not inside it ({probe.MinFaceDistance:0.00}–{probe.MaxFaceDistance:0.00} m)");
            Check(Mathf.Abs(probe.EyeLevelAtEnd) < 0.25f && probe.FaceGapAtEnd > 0.25f && probe.FaceGapAtEnd < 0.65f, $"G1 case 4: at the end the diver's eye is level with its face ({probe.EyeLevelAtEnd:+0.00;-0.00} m) and {probe.FaceGapAtEnd:0.00} m in front of it");
            Check(probe.MaxPalmGap < 0.2f, $"G1 case 4: its palms are on the diver's chest ({probe.MaxPalmGap:0.00} m at worst)");
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 1f, () => "G1 case 8: the hold ended in death: " + walker.ServerStatus);
            Check(heldFor > walker.GrabCore.HoldSeconds - 0.2f && heldFor < walker.GrabCore.HoldSeconds + 0.4f, $"G1 case 8: after {heldFor:0.00} s held (the hold is {walker.GrabCore.HoldSeconds} s)");
            Check(walker.ServerGrabKills == 1 && walker.ServerGrabOutcome == "killed", "G1 case 8: one kill: " + walker.ServerStatus);
            yield return null; yield return null;
            Check(!host.IsGrabbed && !host.GrabbedLocal, "G1 case 8: the hold is let go of at the death: " + host.GrabStatus);
            Check(PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Dive)) != null, "G1 case 8: the ordinary death: a body where it held the diver");
            Check(host.transform.parent == null || host.transform.parent.GetComponentInParent<Creature>() == null, "G1 case 8: nothing parented to the monster");
            yield return Expect(() => !walker.ServerGrabbing, walker.GrabCore.ReleaseSeconds + 0.5f, () => "G1 case 8: the Walker let go after its release pose: " + walker.ServerStatus);
            yield return Wait(0.5f);
            Check(walker.Pose == CreaturePose.Idle && walker.ServerGrabsStarted == 1, "G1 case 8: not frozen, no second grab: idle with nobody to see (" + walker.ServerStatus + ")");
            yield return Expect(() => InState(walker, "Idle"), 1f, () => "G1 case 8: back to its Idle clip: " + AnimState(walker));

            Heading("E1 — case 9: End day revives the diver with controls and physics back");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 90f, () => "E1 the dead host was carried to the ship");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 30f, () => "E1 the site closed");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "E1 End day accepted: " + endWhy);
            yield return Expect(() => !host.IsDead, 5f, () => "E1 revived");
            yield return Wait(1f);
            Check(!host.IsGrabbed && !host.GrabbedLocal && host.Controller.enabled, "E1 case 9: not held, the capsule on: " + host.GrabStatus);
            Check(host.PlayerCamera.transform.localPosition.magnitude < 0.3f, $"E1 case 9: the camera is back on its pivot ({host.PlayerCamera.transform.localPosition})");
            Vector3 before = host.transform.position;
            Keys(Key.W); yield return Wait(0.8f); Keys(); yield return null;
            Check(Flat(before, host.transform.position) > 1.5f, $"E1 case 9: W walks again ({Flat(before, host.transform.position):0.00} m)");
            Check(host.IsGrounded, "E1 case 9: on the deck, grounded");
            yield return Descend(2);
            car = WorldSceneFlow.FindCar();

            Heading("G2 — cases 2, 3, 9: a diver crouch-walking away (slower than it) is caught; the kill refused: let go, controls and physics back");
            Creature.RefuseGrabKillForChecks = true;
            stand = Seabed(car, 110f, 22f);
            walkerAt = Seabed(car, 110f, 26f);
            yield return HostAt(stand, stand + (stand - walkerAt)); // the back to it
            M.ClientLookAt(stand + (stand - walkerAt) * 3f + Vector3.up * 1.4f);
            walker = Spawn(MonsterKind.LongWalker, walkerAt, YawTo(walkerAt, stand));
            yield return Expect(() => walker.Pose == CreaturePose.Hunting, 3f, () => "G2 hunting: " + walker.ServerStatus);
            Keys(Key.LeftCtrl, Key.W);
            float moving = 0f; Vector3 last = host.transform.position; float lastAt = Time.unscaledTime;
            float deadline = Time.unscaledTime + 20f;
            while (!walker.ServerGrabbing && Time.unscaledTime < deadline)
            {
                yield return null;
                if (Time.unscaledTime - lastAt > 0.25f) { moving = Flat(last, host.transform.position) / (Time.unscaledTime - lastAt); last = host.transform.position; lastAt = Time.unscaledTime; }
            }
            caughtDistance = Flat(walker.transform.position, host.transform.position);
            Check(walker.ServerGrabbing, "G2 case 2: it caught the crouch-walking diver: " + walker.ServerStatus);
            Check(moving > 1f, $"G2 case 2: the diver was moving at {moving:0.00} m/s when caught (crouch walk {host.WalkSpeed * host.Movement.CrouchSpeedFactor:0.0}, the Walker {host.WalkSpeed * s.WalkerSpeedFactor:0.0})");
            Check(caughtDistance <= s.ReachMeters + 0.1f, $"G2 case 3: caught at {caughtDistance:0.00} m");
            yield return Wait(0.3f);
            Vector3 heldAt = host.transform.position;
            yield return Wait(0.5f);
            Check(host.GrabbedLocal && !host.IsCrouched, "G2 the held diver stands (the crouch let go): " + host.GrabStatus);
            Keys(); yield return null;
            yield return Expect(() => !host.IsGrabbed, walker.GrabCore.HoldSeconds + 1f, () => "G2 case 9: the kill refused, the hold let go of: " + walker.ServerStatus);
            Check(!host.IsDead && walker.ServerGrabsLetGo >= 1 && walker.ServerGrabOutcome.StartsWith("kill refused"), "G2 case 9: alive, let go: " + walker.ServerStatus);
            yield return null; yield return null;
            Check(!host.GrabbedLocal && host.Controller.enabled, "G2 case 9: the owner let go, the capsule on: " + host.GrabStatus);
            yield return Expect(() => host.IsGrounded, 2f, () => $"G2 case 9: it fell back to the seabed and stands ({host.transform.position.y - car.BottomPosition.y:0.00} m over the floor)");
            // Out of its reach at a run (the cooldown and the release give the time): the keys work.
            Vector3 away = host.transform.position + (host.transform.position - walker.transform.position).normalized * 20f;
            yield return HostAt(host.transform.position, away);
            before = host.transform.position;
            Keys(Key.W, Key.LeftShift); yield return Wait(1f); Keys(); yield return null;
            Check(Flat(before, host.transform.position) > 3f, $"G2 case 9: sprinting works again ({Flat(before, host.transform.position):0.00} m in 1 s)");
            Check(host.PlayerCamera.transform.localPosition.magnitude < 0.3f, "G2 case 9: the camera is back on its pivot");

            Heading("G3 — case 7: repeated catches, kills refused: one hold at a time, each let go of cleanly");
            // Out on open seabed first: G2's sprint away from the Walker ran toward the shaft's safe ground.
            stand = Seabed(car, 140f, 22f);
            yield return HostAt(stand, Seabed(car, 140f, 30f));
            walker.ServerPlaceForChecks(host.transform.position + host.transform.forward * 1.2f, YawTo(host.transform.position + host.transform.forward * 1.2f, host.transform.position));
            int startGrabs = walker.ServerGrabsStarted;
            float lastEnd = -1f; int overlaps = 0;
            for (int i = 0; i < 3; i++)
            {
                yield return Expect(() => walker.ServerGrabbing, s.StrikeCooldownSeconds + walker.GrabCore.ReleaseSeconds + 4f, () => $"G3 catch {i + 1}: " + walker.ServerStatus);
                float began = Time.unscaledTime;
                if (lastEnd > 0f && began - lastEnd < s.StrikeCooldownSeconds - 0.1f) overlaps++;
                yield return Expect(() => host.GrabbedLocal, 1f, () => $"G3 catch {i + 1}: the owner is held");
                yield return Expect(() => !walker.ServerGrabbing, walker.GrabCore.HoldSeconds + walker.GrabCore.ReleaseSeconds + 1f, () => $"G3 catch {i + 1} ended: " + walker.ServerStatus);
                lastEnd = Time.unscaledTime;
                Check(!host.IsGrabbed && !host.GrabbedLocal && host.Controller.enabled && !host.IsDead, $"G3 catch {i + 1}: let go cleanly: " + host.GrabStatus);
            }
            Check(walker.ServerGrabsStarted == startGrabs + 3 && overlaps == 0, $"G3 case 7: three holds, none sooner than the cooldown ({walker.ServerGrabsStarted - startGrabs} holds, {overlaps} early)");
            Check(walker.ServerGrabKills == 0, "G3 no kill while refused");
            yield return Despawn();
            Check(!host.IsGrabbed && host.Controller.enabled, "G3 the Walker gone, nothing held");
            Creature.RefuseGrabKillForChecks = false;
            vitals.ServerHealForChecks();

            Heading("D1 — a Walker despawned mid-hold lets the diver go");
            stand = Seabed(car, 160f, 22f);
            yield return HostAt(stand, stand + Vector3.forward);
            walker = Spawn(MonsterKind.LongWalker, stand + Vector3.forward * 1.1f, 180f);
            yield return Expect(() => host.GrabbedLocal, 3f, () => "D1 held: " + walker.ServerStatus);
            yield return Wait(0.8f);
            yield return Despawn();
            yield return Expect(() => !host.IsGrabbed && !host.GrabbedLocal && host.Controller.enabled, 2f, () => "D1 let go when the holder went: " + host.GrabStatus);
            Check(!host.IsDead, "D1 alive");
            yield return Expect(() => host.IsGrounded, 2f, () => "D1 back on the seabed");

            Heading("O1 — a wall between: no grab through it; it works round it (day 2, after the grab rows)");
            vitals.ServerHealForChecks();
            stand = Seabed(car, 200f, 22f);
            walkerAt = Seabed(car, 200f, 28f);
            yield return HostAt(stand, walkerAt);
            if (!host.LampOn) { yield return Press(Key.F); yield return Expect(() => host.LampOn, 2f, () => "O1 lamp on"); }
            walker = Spawn(MonsterKind.LongWalker, walkerAt, YawTo(walkerAt, stand));
            yield return Expect(() => walker.Pose == CreaturePose.Hunting && walker.TargetId == host.OwnerId, 3f, () => "O1 hunting: " + walker.ServerStatus);
            // A wall 4 m high and 5 m wide, 1.1 m in front of the diver, across the Walker's line.
            Vector3 toHost = host.transform.position - walker.transform.position; toHost.y = 0f; toHost.Normalize();
            Vector3 wallAt = host.transform.position - toHost * 0.75f + Vector3.up * 2f;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Walker check wall";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, WorldScenes.Scene(WorldId.Dive));
            wall.transform.SetPositionAndRotation(wallAt, Quaternion.LookRotation(toHost, Vector3.up));
            wall.transform.localScale = new Vector3(5f, 4f, 0.25f);
            Physics.SyncTransforms();
            walker.ServerPlaceForChecks(host.transform.position - toHost * 1.35f, YawTo(host.transform.position - toHost * 1.35f, host.transform.position));
            yield return null;
            Say($"O1 placed {Flat(walker.transform.position, host.transform.position):0.00} m from the diver, {wall.transform.InverseTransformPoint(walker.transform.position).z * 0.25f:+0.00;-0.00} m off the wall's middle");
            yield return Wait(2.5f);
            Check(!host.IsGrabbed && walker.ServerGrabsStarted == 0, $"O1 {Flat(walker.transform.position, host.transform.position):0.00} m away behind the wall: no grab ({walker.ServerStatus})");
            Check(walker.Pose == CreaturePose.Hunting, "O1 it still hunts the diver it saw");
            Creature.RefuseGrabKillForChecks = true; // the diver lives through this row's grab
            float roundFrom = Time.unscaledTime, nextTrace = 0f;
            while (walker.ServerGrabsStarted < 1 && Time.unscaledTime - roundFrom < 25f)
            {
                yield return null;
                float since = Time.unscaledTime - roundFrom;
                if (since < nextTrace) continue;
                nextTrace = since + 0.5f;
                Vector3 local = wall.transform.InverseTransformPoint(walker.transform.position);
                Say($"O1 t={since:0.0} walker along the wall {local.x * wall.transform.localScale.x:+0.00;-0.00} m, off it {local.z * wall.transform.localScale.z:+0.00;-0.00} m, {Flat(walker.transform.position, host.transform.position):0.00} m from the diver, sidestepping={walker.ServerSidestepping}, facing {walker.transform.eulerAngles.y:0}");
            }
            Check(walker.ServerGrabsStarted >= 1, $"O1 it worked round the wall and caught the diver in {Time.unscaledTime - roundFrom:0.0} s ({walker.ServerStatus})");
            Check(CreatureSenses.ClearLine(walker.EyePoint, CreatureSenses.Chest(host)), "O1 caught with a clear line from its eyes to the chest");
            yield return Expect(() => !host.IsGrabbed && !walker.ServerGrabbing, 5f, () => "O1 refused kill: let go");
            UnityEngine.Object.Destroy(wall); wall = null;
            yield return Despawn();
            vitals.ServerHealForChecks();
            Creature.RefuseGrabKillForChecks = false;

            Heading("I1 — the Impostor is unchanged: a touch is 30 HP and a leak, then it runs; no hold");
            vitals.ServerHealForChecks();
            stand = Seabed(car, 250f, 22f);
            Vector3 impostorAt = Seabed(car, 250f, 28f);
            yield return HostAt(stand, impostorAt);
            Creature impostor = Spawn(MonsterKind.Impostor, impostorAt, YawTo(impostorAt, stand));
            ((Impostor)impostor).ServerChooseForChecks(host.OwnerId);
            yield return Expect(() => ((Impostor)impostor).ServerTouches >= 1, 15f, () => "I1 it touched the host: " + impostor.ServerStatus);
            Check(!host.IsGrabbed && !host.GrabbedLocal && !impostor.ServerGrabbing, "I1 no hold");
            Check(vitals.Leaking && Mathf.Abs(vitals.Health - (vitals.Settings.MaxHealth - s.ImpostorDamage)) < 1.01f, $"I1 30 HP and a leak (health {vitals.Health}, leaking {vitals.Leaking})");
            yield return Expect(() => impostor.Pose == CreaturePose.Fleeing, 2f, () => "I1 then it runs away: " + impostor.ServerStatus);
            yield return Despawn();
            vitals.ServerHealForChecks();

            Heading("done");
            Say("the host lives below; stop with CameraClearanceMatrixDriver.StopCleanly()");
        }
    }
}
