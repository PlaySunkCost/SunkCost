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
using Object = UnityEngine.Object;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using M = SunkCost.Editor.Prototype.MonsterTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The Weeping Angel's polish checks (24 September 2026, mon-angel), the host alone
    // below, the Angel spawned by the rows: the prefab and its clips (S0); it freezes
    // on screen and nothing of it moves under a look (F1); it moves only unseen, at
    // its sprint (F2); the statues it freezes in (F3); a touch never lands while it is
    // frozen (F4); then the embrace, Dan's nine cases — the catch (G1, G2), its
    // movement stopped (G3), aligned with the diver (G4), the embrace's beats and
    // evidence captures (G5), the diver's state (G6), no second catch (G7), a refused
    // kill let go and valid behaviour after (G9), a moving diver caught and the kill
    // with its clean end state (G8). Log: Temp/polish-WeepingAngel-matrix.log;
    // captures in Temp/polish-angel/. Started by the matrix driver's polish-WeepingAngel.
    public static class MonsterPolishWeepingAngelChecks
    {
        private const string Log = "Temp/polish-WeepingAngel-matrix.log";
        private const string Shots = "Temp/polish-angel/";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        // The guest's rows (R1-R3): with Temp/polish-WeepingAngel-guest.flag present the same
        // job runs them instead of the host's, with a guest build joined (Builds/HQPrototypeLocal).
        private const string GuestFlag = "Temp/polish-WeepingAngel-guest.flag";
        private const string GuestDir = "Temp/polish-WeepingAngel-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private static bool guestMode;
        private static Process guest;
        private static int guestCommand = 2600;
        private static string lastReply = string.Empty;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static MonsterSettings Settings => MonsterSettings.Get();

        [MenuItem("Sunk Cost/Prototype/Run Weeping Angel polish checks (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Weeping Angel checks running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            guestMode = File.Exists(GuestFlag);
            if (guestMode && !File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first (the guest rows).");
            File.WriteAllText(Log, "Weeping Angel polish checks started " + DateTime.Now + (guestMode ? " (the guest rows)" : string.Empty) + "\n");
            Directory.CreateDirectory(Shots);
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
            if (Status == "MATRIX_PASS") Debug.Log("Weeping Angel checks: MATRIX_PASS"); else Debug.LogError("Weeping Angel checks: " + Status);
            steps = null;
            stack.Clear();
            Creature.RefuseGrabKillForChecks = false;
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            MonsterSettings.RosterOverrideForTests = null;
            MonsterSettings.GhostChanceOverrideForTests = null;
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
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + M.MonstersText() + "\n" + Host()?.GrabStatus);
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

        private static WeepingAngel Spawn(Vector3 at)
        {
            Creature c = MonsterRoster.ServerSpawnForChecks(MonsterKind.WeepingAngel, at);
            Check(c is WeepingAngel, "spawned the Weeping Angel at " + at.ToString("F1"));
            return (WeepingAngel)c;
        }
        private static IEnumerator Despawn()
        {
            M.ServerDespawnMonsters();
            yield return Expect(() => Creature.All.Count == 0, 5f, () => "the Angel despawned (" + Creature.All.Count + " left)");
        }
        private static float Flat(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);
        private static Vector3 Seabed(ElevatorController car, float bearingDeg, float distance)
        {
            Vector3 dir = Quaternion.Euler(0f, bearingDeg, 0f) * Vector3.forward;
            return car.BottomPosition + dir * distance + Vector3.up * 0.15f;
        }
        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            Vector3 to = facing - spot; to.y = 0f;
            host.TeleportLocal(spot, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : Vector3.forward, Vector3.up).eulerAngles.y);
            yield return null; yield return null;
            M.ClientLookAt(facing);
            yield return null;
        }
        private static void LookAtAngel(WeepingAngel angel) => M.ClientLookAt(angel.transform.position + Vector3.up * angel.EyeHeight);
        private static void LookAway(WeepingAngel angel)
        {
            HQPlayerController host = Host();
            Vector3 away = host.transform.position - (angel.transform.position - host.transform.position);
            M.ClientLookAt(away + Vector3.up * 1.6f);
        }

        private static Transform Bone(Creature c, string name) => c.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        private static readonly string[] WatchedBones = { "Head", "HandL", "HandR", "Spine", "FootL", "FootR" };
        private static Dictionary<string, Vector3> BonePositions(Creature c) =>
            WatchedBones.Select(n => (n, t: Bone(c, n))).Where(p => p.t != null).ToDictionary(p => p.n, p => p.t.position);
        // Nothing of the model moves (bones, metres) and the body stays put over `seconds`, the look held on it.
        private static IEnumerator StillUnderLook(WeepingAngel angel, float seconds, string label)
        {
            LookAtAngel(angel);
            yield return Wait(0.3f); // the blend into the statue
            Dictionary<string, Vector3> before = BonePositions(angel);
            Vector3 at = angel.transform.position;
            float worst = 0f; string worstBone = "";
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                LookAtAngel(angel);
                Check(angel.Pose == CreaturePose.Frozen, label + ": frozen the whole look (" + angel.ServerStatus + ")");
                foreach (KeyValuePair<string, Vector3> b in BonePositions(angel))
                {
                    float d = Vector3.Distance(b.Value, before[b.Key]);
                    if (d > worst) { worst = d; worstBone = b.Key; }
                }
                yield return null;
            }
            float moved = Vector3.Distance(angel.transform.position, at);
            Check(before.Count == WatchedBones.Length, $"{label}: all {WatchedBones.Length} watched bones found ({before.Count})");
            Check(moved < 0.01f && worst < 0.004f, $"{label}: nothing moves under the look for {seconds:0.0} s (body {moved * 1000f:0.0} mm, worst of {before.Count} bones {worstBone} {worst * 1000f:0.0} mm)");
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

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            MonsterSettings s = Settings;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("AngelCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            MonsterSettings.RosterOverrideForTests = Array.Empty<MonsterKind>();
            MonsterSettings.GhostChanceOverrideForTests = 0f;
            Creature.RefuseGrabKillForChecks = false;

            Heading("S0 — the prefab: the embrace, the statue, the clips");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterCatalog.PrefabPath(MonsterKind.WeepingAngel));
            CreatureGrab grabDef = prefab.GetComponent<CreatureGrab>();
            Check(grabDef != null && grabDef.enabled, "the Angel's prefab carries an enabled CreatureGrab (it embraces, it does not kill on the touch)");
            Say($"embrace: grip {grabDef.GripSeconds} s, kill {grabDef.HoldSeconds} s, release {grabDef.ReleaseSeconds} s, feet {grabDef.GripPoint}, face {grabDef.FaceTarget}");
            Check(Mathf.Approximately(grabDef.GripSeconds, grabDef.LiftSeconds) && grabDef.GripPoint == grabDef.LiftPoint, "no lift: the Angel holds, it does not raise (the Walker's style is not the Angel's)");
            Check(prefab.GetComponent<WeepingAngelStatue>() != null, "the prefab carries the statue holder");
            CreatureRig rigDef = prefab.GetComponent<CreatureRig>();
            var ac = rigDef.Animator.runtimeAnimatorController;
            string[] clips = ac.animationClips.Select(c => c.name + " " + c.length.ToString("0.00") + "s" + (c.isLooping ? " loop" : "")).ToArray();
            Say("clips: " + string.Join(", ", clips));
            foreach (string need in new[] { "Idle", "Hunting", "Frozen", "Grabbing" })
                Check(ac.animationClips.Any(c => c.name == need), "the controller has the " + need + " clip");
            AnimationClip grabbing = ac.animationClips.First(c => c.name == "Grabbing");
            Check(grabbing.length >= grabDef.HoldSeconds + grabDef.ReleaseSeconds + 0.05f, $"the embrace clip ({grabbing.length:0.00} s) outlasts the hold and the release ({grabDef.HoldSeconds + grabDef.ReleaseSeconds:0.00} s): its loop never shows");
            Check(rigDef.AuthoredSpeed(CreaturePose.Hunting) > 5f && rigDef.AuthoredSpeed(CreaturePose.Frozen) <= 0f, $"the sprint is speed-matched ({rigDef.AuthoredSpeed(CreaturePose.Hunting)} m/s), the statue is not");

            Heading("M0 — to sea, down on day 1 (the roster empty: the rows spawn the Angel)");
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
                H.MoveLocalIntoDeckCabin("Sea");
                yield return GuestMove(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f));
                yield return Wait(0.5f);
            }
            yield return Descend(1);
            if (guestMode)
            {
                yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "the guest is in the site");
                yield return GuestRows(WorldSceneFlow.FindCar(), guestId);
                yield break;
            }
            ElevatorController car = WorldSceneFlow.FindCar();
            host = Host();

            // ---------------------------------------------------------------------------
            Heading("F1 — watched, it freezes; a statue does not move under a look");
            Vector3 stand = Seabed(car, 60f, 22f);
            Vector3 angelAt = Seabed(car, 60f, 36f);
            yield return HostAt(stand, angelAt + Vector3.up * 1.8f);
            WeepingAngel angel = Spawn(angelAt);
            WeepingAngelStatue statue = angel.GetComponent<WeepingAngelStatue>();
            CreatureRig rig = angel.GetComponent<CreatureRig>();
            Check(statue != null && rig != null && rig.HasAnimator, "the spawned Angel has its statue holder and its animated model");
            yield return Expect(() => angel.Pose == CreaturePose.Frozen, 3f, () => "F1 watched at 14 m: frozen (" + angel.ServerStatus + ")");
            Check(angel.FrozenStatue == WeepingAngel.Statue.Weep, "F1 caught standing, not hunting: it weeps (" + angel.FrozenStatue + ")");
            yield return StillUnderLook(angel, 2f, "F1");
            Check(statue.ShownStatue == (int)angel.FrozenStatue, $"F1 the model shows the server's statue ({statue.ShownStatue} = {angel.FrozenStatue})");
            H.CaptureLocalCamera(Shots + "F1-weep.png");
            Vector3 toAngel = angelAt - stand; toAngel.y = 0f;
            M.ClientLookAt(stand + Quaternion.Euler(0f, 45f, 0f) * toAngel + Vector3.up * 1.6f); yield return Wait(0.5f);
            Check(angel.Pose == CreaturePose.Frozen, "F1 at the edge of the screen (45° off the look) it is still frozen (" + angel.ServerStatus + ")");

            Heading("F2 — unseen, it moves: at 2.5× sprint straight at the diver; seen again, it stops dead");
            LookAway(angel);
            yield return Expect(() => angel.Pose == CreaturePose.Hunting, 2f, () => "F2 unwatched: it hunts (" + angel.ServerStatus + ")");
            float d0 = Flat(angel.transform.position, host.transform.position); float t0 = Time.unscaledTime;
            yield return Wait(0.35f);
            float closed = (d0 - Flat(angel.transform.position, host.transform.position)) / (Time.unscaledTime - t0);
            Check(closed > host.SprintSpeed * 1.5f, $"F2 it comes at {closed:0.0} m/s (sprint {host.SprintSpeed} × {s.AngelSpeedFactor})");
            Say($"F2 the sprint clip plays at {rig.PlaybackRate:0.00}× for {rig.Speed:0.0} m/s (authored {rig.AuthoredSpeed(CreaturePose.Hunting)} m/s); shown pose {rig.ShownPose}");
            Check(rig.ShownPose == CreaturePose.Hunting && rig.PlaybackRate > 1.05f, "F2 the model runs its sprint, sped to the ground speed");
            LookAtAngel(angel);
            yield return Expect(() => angel.Pose == CreaturePose.Frozen, 1f, () => "F2 looked at again: frozen (" + angel.ServerStatus + ")");
            float dFrozen = Flat(angel.transform.position, host.transform.position);
            Check(dFrozen > s.ReachMeters, $"F2 it stopped short of the diver ({dFrozen:0.0} m)");
            Check(angel.FrozenStatue != WeepingAngel.Statue.Weep || dFrozen > 16f, "F2 out of a hunt it freezes mid-motion, not weeping (" + angel.FrozenStatue + $" at {dFrozen:0.0} m)");
            yield return StillUnderLook(angel, 1.5f, "F2 " + angel.FrozenStatue);
            H.CaptureLocalCamera(Shots + "F2-" + angel.FrozenStatue + ".png");
            Check(!host.IsDead && !host.IsGrabbed, "F2 the host lives, unheld");
            yield return Despawn();

            Heading("F3 — the statues: varied, and the closer it is caught the more it reaches");
            var seen = new HashSet<WeepingAngel.Statue>();
            for (int round = 0; round < 6; round++)
            {
                float start = round % 3 == 0 ? 4.4f : 13f;
                stand = Seabed(car, 120f + round * 25f, 24f);
                Vector3 look = Seabed(car, 120f + round * 25f, 24f + start);
                yield return HostAt(stand, look + Vector3.up * 1.6f);
                WeepingAngel a = Spawn(look);
                yield return Expect(() => a.Pose == CreaturePose.Frozen, 2f, () => $"F3.{round} frozen at first sight ({a.ServerStatus})");
                LookAway(a);
                yield return Expect(() => a.Pose == CreaturePose.Hunting, 2f, () => $"F3.{round} it hunts unseen ({a.ServerStatus})");
                LookAtAngel(a);
                yield return Expect(() => a.Pose == CreaturePose.Frozen, 1f, () => $"F3.{round} frozen again ({a.ServerStatus})");
                float d = Flat(a.transform.position, host.transform.position);
                yield return Wait(0.3f);
                WeepingAngelStatue st = a.GetComponent<WeepingAngelStatue>();
                Check(st.ShownStatue == (int)a.FrozenStatue, $"F3.{round} caught at {d:0.0} m: statue {a.FrozenStatue}, shown {st.ShownStatue}");
                if (d < 4.5f) Check(a.FrozenStatue == WeepingAngel.Statue.Reach, $"F3.{round} caught close ({d:0.0} m): it reaches for the diver ({a.FrozenStatue})");
                seen.Add(a.FrozenStatue);
                H.CaptureLocalCamera(Shots + $"F3-{round}-{a.FrozenStatue}.png");
                Check(!host.IsDead && !host.IsGrabbed, $"F3.{round} the host lives");
                yield return Despawn();
            }
            Check(seen.Count >= 2, "F3 more than one statue across six freezes: " + string.Join(", ", seen));

            Heading("F4 — frozen within reach, it never touches");
            stand = Seabed(car, 250f, 22f);
            Vector3 close = Seabed(car, 250f, 22f + 1.15f);
            yield return HostAt(stand, close + Vector3.up * 1.8f);
            WeepingAngel near = Spawn(close);
            yield return Expect(() => near.Pose == CreaturePose.Frozen, 2f, () => "F4 watched at 1.15 m: frozen (" + near.ServerStatus + ")");
            float until = Time.unscaledTime + 3f;
            while (Time.unscaledTime < until)
            {
                LookAtAngel(near);
                Check(near.Pose == CreaturePose.Frozen && !near.ServerGrabbing && !host.IsGrabbed, "F4 inside its reach and watched: no embrace (" + near.ServerStatus + ")");
                yield return null;
            }
            Check(near.ServerGrabsStarted == 0 && !host.IsDead, "F4 3 s within reach, watched: no catch, the host lives");
            yield return Despawn();

            // ---------------------------------------------------------------------------
            Heading("G1/G2 — it reaches a diver whose back is turned and catches them, from within its reach");
            Creature.RefuseGrabKillForChecks = true; // this hold ends refused: G9 checks the diver let go of
            stand = Seabed(car, 300f, 24f);
            Vector3 behind = Seabed(car, 300f, 24f + 10f);
            Vector3 ahead = Seabed(car, 300f, 24f - 8f);
            yield return HostAt(stand, ahead + Vector3.up * 1.6f);
            angel = Spawn(behind);
            yield return Expect(() => angel.ServerGrabbing, 4f, () => "G1 it came and caught the host (" + angel.ServerStatus + ")");
            int dashesBeforeCatchPress = host.Dashes;
            bool dashAtCatch = host.TryDash(Vector2.up); // the diver's Alt on the frame the hold lands (G4 then checks the hold was not moved)
            Say($"G1 a dash on the frame of the catch: {(dashAtCatch ? "started, before the hold reached the owner" : "refused ('" + host.DashRefusal + "')")}; dashes {dashesBeforeCatchPress} → {host.Dashes}");
            CreatureGrab grab = angel.GetComponent<CreatureGrab>();
            rig = angel.GetComponent<CreatureRig>(); // this Angel's rig (F1's went with its Angel)
            Vector3 angelHold = angel.transform.position;
            float catchDistance = Flat(angelHold, host.Grab.CaughtAt);
            Check(catchDistance <= s.ReachMeters + 0.05f && catchDistance >= 0.5f, $"G2 caught at {catchDistance:0.00} m (reach {s.ReachMeters} m): not from afar, not from inside the diver");
            Check(angel.ServerGrabsStarted == 1 && angel.ServerGrabVictim == host, "G2 one catch, of the host");

            Heading("G3/G6 — the embrace: the chase stops, the diver's state changes");
            yield return Expect(() => host.IsGrabbed && host.GrabbedLocal, 1f, () => "G6 the host is held (" + host.GrabStatus + ")");
            Check(host.GrabStatus.Contains("capsule=False"), "G6 the capsule is off while held: " + host.GrabStatus);
            Keys(Key.W, Key.LeftShift);
            float sampleUntil = Time.unscaledTime + grab.HoldSeconds - 0.25f;
            float worstMove = 0f, worstLookOff = 0f; bool alwaysGrabbing = true; int grabsDuring = angel.ServerGrabsStarted;
            bool shot03 = false, shot09 = false, shot14 = false, shot20 = false, sideShot = false;
            float faceAt = -1f; float handsLeftFaceBy = -1f; float handsOnHeadBy = -1f; float joltMax = 0f, stillShake = 0f;
            Transform head = Bone(angel, "Head"), handL = Bone(angel, "HandL"), handR = Bone(angel, "HandR");
            while (Time.unscaledTime < sampleUntil && angel.ServerGrabbing)
            {
                float t = angel.ServerGrabSeconds;
                worstMove = Mathf.Max(worstMove, Flat(angel.transform.position, angelHold));
                if (angel.Pose != CreaturePose.Grabbing) alwaysGrabbing = false;
                // the diver's eyes are on the Angel: it is watched the whole embrace
                host.EyePose(out Vector3 eye, out Quaternion look);
                Vector3 toFace = angel.transform.TransformPoint(grab.FaceTarget) - eye;
                float off = Vector3.Angle(look * Vector3.forward, toFace);
                worstLookOff = t > grab.GripSeconds ? Mathf.Max(worstLookOff, off) : worstLookOff;
                float roll = Mathf.Abs(Mathf.DeltaAngle(0f, (Quaternion.Inverse(Quaternion.LookRotation(toFace, Vector3.up)) * look).eulerAngles.z));
                if (Mathf.Abs(t - grab.GripSeconds) < 0.2f) joltMax = Mathf.Max(joltMax, Mathf.Max(off, roll));
                if (t > grab.GripSeconds + 0.3f) stillShake = Mathf.Max(stillShake, Mathf.Max(off, roll));
                if (head != null && handL != null && handR != null)
                {
                    float handsFromFace = Mathf.Min(Vector3.Distance(handL.position, head.position), Vector3.Distance(handR.position, head.position));
                    if (handsLeftFaceBy < 0f && t > 0.2f && handsFromFace > 0.28f) handsLeftFaceBy = t;
                    float onHead = Mathf.Max(Vector3.Distance(handL.position, eye), Vector3.Distance(handR.position, eye));
                    if (handsOnHeadBy < 0f && onHead < 0.3f) handsOnHeadBy = t;
                }
                if (!shot03 && t > 0.3f) { shot03 = true; H.CaptureLocalCamera(Shots + "G5-victim-0.3s.png"); }
                if (!shot09 && t > 0.85f) { shot09 = true; H.CaptureLocalCamera(Shots + "G5-victim-0.85s.png"); }
                if (!shot14 && t > 1.4f) { shot14 = true; H.CaptureLocalCamera(Shots + "G5-victim-1.4s.png"); faceAt = Vector3.Distance(eye, angel.transform.TransformPoint(grab.FaceTarget)); }
                if (!sideShot && t > 1.6f)
                {
                    sideShot = true;
                    Vector3 mid = (angel.transform.position + host.transform.position) * 0.5f + Vector3.up * 1.4f;
                    H.CaptureFrom(mid + angel.transform.right * 2.6f + Vector3.up * 0.2f, mid, Shots + "G5-side-1.6s.png");
                    H.CaptureFrom(mid + (angel.transform.right - angel.transform.forward).normalized * 2.4f, mid, Shots + "G5-three-quarter-1.6s.png");
                }
                if (!shot20 && t > 2.0f) { shot20 = true; H.CaptureLocalCamera(Shots + "G5-victim-2.0s.png"); }
                yield return null;
            }
            Keys();
            Check(alwaysGrabbing, "G3 the pose stayed Grabbing the whole hold: no freeze, no hunt, though the diver's eyes are on it");
            Check(worstMove < 0.05f, $"G3 its sprint stopped: it stood still in the hold (moved {worstMove * 100f:0.0} cm)");
            Check(worstLookOff < 12f, $"G3 the diver looks at its face through the hold (at most {worstLookOff:0.0}° off): watched, and the embrace goes on");
            Check(angel.ServerGrabsStarted == grabsDuring, "G7 no second catch during the hold");
            Say($"G3 the held view: the jolt as the hands close peaks at {joltMax:0.0}° (pitch/yaw off the face, or roll), the stillness trembles up to {stillShake:0.0}°");
            Check(joltMax > 1f && stillShake > 0.1f && stillShake < 6f, "G3 the view shakes a little: a jolt at the grip, a tremor in the stillness");

            Heading("G4/G5 — aligned with the diver; it looks like the embrace");
            float feetGap = Flat(host.transform.position, angel.transform.TransformPoint(grab.GripPoint));
            float bodyGap = Flat(host.transform.position, angel.transform.position);
            Vector3 hostFwd = host.transform.forward; hostFwd.y = 0f;
            Vector3 toAngelFlat = angel.transform.position - host.transform.position; toAngelFlat.y = 0f;
            Vector3 angelFwd = angel.transform.forward; angelFwd.y = 0f;
            Say($"G4 feet {feetGap * 100f:0.0} cm from the hold point, {bodyGap:0.00} m from the Angel; hands left the face at {handsLeftFaceBy:0.00} s, on the diver's head at {handsOnHeadBy:0.00} s; the face {faceAt:0.00} m from the diver's eyes");
            Check(feetGap < 0.08f, "G4 the diver stands at the embrace's hold point");
            Check(host.Dashes == dashesBeforeCatchPress + (dashAtCatch ? 1 : 0) && feetGap < 0.08f && host.IsGrabbed, $"D0 a dash on the frame of the catch ({(dashAtCatch ? "started, then cancelled by the hold" : "refused '" + host.DashRefusal + "'")}) did not move the held diver: at the hold point, still held");
            Check(bodyGap > 0.55f && bodyGap < 0.85f, $"G4 close, not inside it ({bodyGap:0.00} m apart; the diver's capsule is 0.3 m)");
            Check(Vector3.Angle(hostFwd, toAngelFlat) < 10f && Vector3.Angle(angelFwd, -toAngelFlat) < 12f, "G4 face to face: the diver faces the Angel and the Angel the diver");
            Check(handsLeftFaceBy > 0.2f && handsLeftFaceBy < grab.GripSeconds + 0.1f, $"G5 its hands leave its face before the grip ({handsLeftFaceBy:0.00} s)");
            Check(handsOnHeadBy > 0f && handsOnHeadBy < grab.GripSeconds + 0.35f, $"G5 its hands are on the diver's head from the grip ({handsOnHeadBy:0.00} s, grip {grab.GripSeconds} s)");
            Check(faceAt > 0.25f && faceAt < 0.7f, $"G5 the diver's eyes are turned to its face, {faceAt:0.00} m away");
            AnimatorStateInfo state = rig.Animator.GetCurrentAnimatorStateInfo(0);
            Check(state.IsName("Grabbing") || rig.Animator.GetNextAnimatorStateInfo(0).IsName("Grabbing"), "G5 the model plays the embrace clip");

            Heading("G9 — the kill refused: let go, restored, and valid again");
            yield return Expect(() => !host.IsGrabbed, grab.HoldSeconds + 1f, () => "G9 released at the hold's end (" + host.GrabStatus + " | " + angel.ServerStatus + ")");
            Check(!host.IsDead && angel.ServerGrabOutcome.StartsWith("kill refused"), "G9 alive, the Angel's outcome: " + angel.ServerGrabOutcome);
            yield return Expect(() => !host.GrabbedLocal && host.GrabStatus.Contains("capsule=True"), 1f, () => "G9 the lock let go, the capsule back: " + host.GrabStatus);
            Check(host.transform.parent == null || host.transform.parent.GetComponentInParent<Creature>() == null, "G9 nothing left parented to the Angel");
            Vector3 before = host.transform.position;
            Keys(Key.S); yield return Wait(0.5f); Keys(); yield return Wait(0.1f); // back away from it
            Check(Flat(before, host.transform.position) > 0.8f, $"G9 the controls work again: S walked {Flat(before, host.transform.position):0.00} m");
            yield return Expect(() => !angel.ServerGrabbing, grab.ReleaseSeconds + 1f, () => "G9 the hold ended (" + angel.ServerStatus + ")");
            LookAtAngel(angel);
            yield return Expect(() => angel.Pose == CreaturePose.Frozen, 1.5f, () => "G9 looked at after the hold, it is a statue again (" + angel.ServerStatus + ")");
            Check(angel.ServerGrabsStarted == 1, "G7 still one catch: no re-grab spam");
            yield return StillUnderLook(angel, 1f, "G9 after the hold");

            Heading("G8 — a diver walking away is caught and dashes at the catch and in the hold; the kill and a clean end");
            Creature.RefuseGrabKillForChecks = false;
            Vector3 away = host.transform.position + (host.transform.position - angel.transform.position).normalized * 20f;
            M.ClientLookAt(away + Vector3.up * 1.6f);
            Keys(Key.W); // walking away, back turned
            yield return Expect(() => angel.ServerGrabbing, 4f, () => "G8 the walking host was caught (" + angel.ServerStatus + ")");
            int dashesBefore8 = host.Dashes;
            bool dashAtCatch8 = host.TryDash(Vector2.up); // Alt on the frame of the catch, the kill on
            Say($"G8 a dash on the frame of the catch: {(dashAtCatch8 ? "started (the owner had not yet seen the hold)" : "refused '" + host.DashRefusal + "'")}; dashes {dashesBefore8} → {host.Dashes}");
            Keys();
            Check(angel.ServerGrabsStarted == 2, "G8 a second catch after the first let go: it returned to valid behaviour");
            float caughtWalking = Flat(angel.transform.position, host.Grab.CaughtAt);
            Check(caughtWalking <= s.ReachMeters + 0.3f, $"G8 caught walking at {caughtWalking:0.00} m");
            {
                // Alt (the key and the call it makes) at 0.1, 0.6 and 1.5 s into the embrace: refused, and the kill lands
                int dashesAtCatch = host.Dashes, pressed = 0, notNow = 0, started = 0;
                float[] pressAt = { 0.1f, 0.6f, 1.5f };
                float worstFeet = 0f;
                while (!host.IsDead && angel.ServerGrabbing && angel.ServerGrabSeconds < grab.HoldSeconds + 1f)
                {
                    float t = angel.ServerGrabSeconds;
                    if (pressed < pressAt.Length && t >= pressAt[pressed])
                    {
                        Keys(Key.W, Key.LeftAlt);
                        if (host.TryDash(Vector2.up)) started++; else if (host.DashRefusal == "Not now") notNow++;
                        pressed++;
                        yield return null;
                        Keys();
                    }
                    if (host.IsGrabbed && t > grab.GripSeconds + 0.1f) worstFeet = Mathf.Max(worstFeet, Flat(host.transform.position, angel.transform.TransformPoint(grab.GripPoint)));
                    yield return null;
                }
                Check(pressed == 3 && started == 0 && notNow == 3 && host.Dashes == dashesAtCatch, $"G8 three dashes in the embrace, all refused 'Not now' (pressed {pressed}, started {started}, 'Not now' {notNow}, dashes {dashesAtCatch} → {host.Dashes})");
                Check(worstFeet < 0.08f, $"G8 the diver stayed at the hold point through the dashes ({worstFeet * 100f:0.0} cm at worst)");
            }
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), grab.HoldSeconds + 1.5f, () => "G8 death after the hold (" + angel.ServerStatus + ")");
            Check(Mathf.Abs(angel.ServerGrabSeconds - grab.HoldSeconds) < 0.4f || !angel.ServerGrabbing, $"G8 the kill came at the hold's end ({angel.ServerGrabSeconds:0.00} s ≈ {grab.HoldSeconds} s)");
            Check(angel.ServerGrabKills == 1 && angel.ServerGrabOutcome == "killed", "G8 one kill: " + angel.ServerGrabOutcome);
            Check(!host.IsGrabbed, "G8 the hold let go of the dead diver");
            Check(PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Dive)) != null, "G8 the ordinary death: a body where the diver was held");
            yield return Expect(() => !angel.ServerGrabbing && angel.Pose != CreaturePose.Grabbing, grab.ReleaseSeconds + 1f, () => "G8 the release ran out and the Angel's brain is back (" + angel.ServerStatus + ")");
            Check(angel.ServerGrabsStarted == 2, "G7 no catch of the dead");
            Say("G8 after the kill: " + angel.ServerStatus);
            MonsterSettings.RosterOverrideForTests = null;
            MonsterSettings.GhostChanceOverrideForTests = null;
        }

        // ---- the guest ------------------------------------------------------------------

        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
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
        private static string GuestMonsterLine(string reply) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("monster=" + MonsterKind.WeepingAngel + ";")) ?? string.Empty;
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
        private static HQPlayerController GuestCopy() => Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static IEnumerator GuestMove(Vector3 to) { yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        // The peer's "look" takes a direction (its aim vector), not a point: turn the guest standing at
        // `feet` so its eyes look at `target`.
        private static IEnumerator GuestLook(Vector3 target, Vector3 feet)
        {
            Vector3 aim = target - (feet + Vector3.up * 1.6f);
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(aim) + "}");
        }

        // R1-R3: a remote diver's eyes freeze it; a guest embraced, seen from the guest's own
        // screen and from the host (a spectator's and the TV's source: the guest's EyePose);
        // the release and the kill on the guest.
        private static IEnumerator GuestRows(ElevatorController car, int guestId)
        {
            HQPlayerController host = Host();
            HQPlayerController copy = GuestCopy();
            Check(copy != null && copy.OwnerId == guestId, "the host holds the guest's copy");

            Heading("R1 — the guest's eyes alone freeze it (the host's back is turned)");
            Vector3 angelAt = Seabed(car, 60f, 38f);
            Vector3 hostSpot = Seabed(car, 60f, 26f);
            Vector3 guestSpot = Seabed(car, 78f, 29f);
            yield return HostAt(hostSpot, Seabed(car, 60f, 10f) + Vector3.up * 1.6f);
            yield return GuestMove(guestSpot);
            yield return GuestLook(angelAt + Vector3.up * 1.6f, guestSpot);
            yield return GuestEventually(r => Flat(VecField(GuestPlayerLine(r, guestId), "position"), guestSpot) < 1.5f, 8f, "R1 the guest stands about 11 m from the Angel's spot, looking at it");
            WeepingAngel angel = Spawn(angelAt);
            yield return Expect(() => angel.Pose == CreaturePose.Frozen && angel.ServerWatcherId == guestId, 3f, () => "R1 frozen, watched by the guest (" + angel.ServerStatus + ")");
            Vector3 frozenAt = angel.transform.position;
            float until = Time.unscaledTime + 1.5f;
            while (Time.unscaledTime < until)
            {
                Check(angel.Pose == CreaturePose.Frozen, "R1 the guest's look holds it (" + angel.ServerStatus + ")");
                yield return null;
            }
            Check(Flat(angel.transform.position, frozenAt) < 0.01f && !host.IsGrabbed, $"R1 1.5 s under the guest's look alone, it did not move ({Flat(angel.transform.position, frozenAt) * 1000f:0.0} mm)");
            yield return GuestEventually(r => Text(GuestMonsterLine(r), "pose") == "Frozen", 3f, "R1 the guest's copy shows it Frozen");
            yield return GuestLook(guestSpot + (guestSpot - angelAt) + Vector3.up * 1.6f, guestSpot);
            yield return Expect(() => angel.Pose == CreaturePose.Hunting, 3f, () => "R1 the guest looks away: it hunts (" + angel.ServerStatus + ")");
            LookAtAngel(angel);
            yield return Expect(() => angel.Pose == CreaturePose.Frozen, 1.5f, () => "R1 the host turns and looks: frozen again (" + angel.ServerStatus + ")");
            Check(!host.IsGrabbed && !copy.IsGrabbed, "R1 nobody caught");
            yield return Despawn();

            Heading("R2 — the guest embraced: its own screen, the host's copy of its eyes, the release");
            Creature.RefuseGrabKillForChecks = true;
            hostSpot = Seabed(car, 140f, 20f);
            guestSpot = Seabed(car, 165f, 26f);
            angelAt = Seabed(car, 165f, 33f);
            yield return HostAt(hostSpot, Seabed(car, 140f, 5f) + Vector3.up * 1.6f);
            yield return GuestMove(guestSpot);
            yield return GuestLook(Seabed(car, 165f, 8f) + Vector3.up * 1.6f, guestSpot);
            yield return GuestEventually(r => Flat(VecField(GuestPlayerLine(r, guestId), "position"), guestSpot) < 1.5f, 8f, "R2 the guest stands with its back to the Angel's spot");
            angel = Spawn(angelAt);
            yield return Expect(() => angel.ServerGrabbing, 5f, () => "R2 it came and caught (" + angel.ServerStatus + ")");
            Check(angel.ServerGrabVictim == copy, "R2 it caught the guest, the nearer diver (" + angel.ServerStatus + ")");
            CreatureGrab grab = angel.GetComponent<CreatureGrab>();
            yield return GuestEventually(r => Text(GuestPlayerLine(r, guestId), "grabbed") == "True" && Text(GuestPlayerLine(r, guestId), "grabHolder") == angel.ObjectId.ToString(), 2f, $"R2 the guest reads itself held by the Angel (holder {angel.ObjectId})");
            yield return Expect(() => angel.ServerGrabSeconds > grab.GripSeconds + 0.35f, 3f, () => "R2 past the grip");
            // the host's copy of the guest: where a spectator's and the TV's view comes from
            copy.EyePose(out Vector3 eye, out Quaternion look);
            Vector3 face = angel.transform.TransformPoint(grab.FaceTarget);
            float hostOff = Vector3.Angle(look * Vector3.forward, face - eye);
            float feetOff = Flat(copy.transform.position, angel.transform.TransformPoint(grab.GripPoint));
            Say($"R2 on the host the guest's eyes are {hostOff:0.0}° off the Angel's face, {Vector3.Distance(eye, face):0.00} m from it; the guest's copy stands {feetOff * 100f:0.0} cm from the hold point");
            Check(hostOff < 12f && feetOff < 0.15f, "R2 on the host (spectators, the TV) the held guest looks into the Angel's face from the hold point");
            yield return Snapshot();
            string me = GuestPlayerLine(lastReply, guestId);
            Vector3 camPos = VecField(me, "camPos"), camFwd = VecField(me, "camFwd"), guestFeet = VecField(me, "position");
            float guestOff = Vector3.Angle(camFwd, face - camPos);
            float guestFeetOff = Flat(guestFeet, angel.transform.TransformPoint(grab.GripPoint));
            Say($"R2 on the guest's own screen: {guestOff:0.0}° off the face, the camera {Vector3.Distance(camPos, face):0.00} m from it, the feet {guestFeetOff * 100f:0.0} cm from the hold point; grabT={Text(me, "grabT")} (server {angel.ServerGrabSeconds:0.00})");
            Check(guestOff < 15f && guestFeetOff < 0.3f, "R2 the guest's own first-person view is turned into the Angel's face, held at the hold point");
            Check(Text(me, "controllerOn") == "False", "R2 the guest's capsule is off while held");
            yield return GuestEventually(r => Text(GuestMonsterLine(r), "pose") == "Grabbing", 2f, "R2 the guest's copy of the Angel plays the embrace (pose=Grabbing)");
            yield return Expect(() => !copy.IsGrabbed, grab.HoldSeconds + 1f, () => "R2 let go at the hold's end, the kill refused (" + angel.ServerStatus + ")");
            yield return GuestEventually(r => { string l = GuestPlayerLine(r, guestId); return Text(l, "grabbed") == "False" && Text(l, "dead") == "False" && Text(l, "controllerOn") == "True" && Text(l, "grabsFelt") == "1"; }, 3f, "R2 the guest is let go: alive, its capsule back, one hold felt");
            yield return Expect(() => !angel.ServerGrabbing, grab.ReleaseSeconds + 1f, () => "R2 the hold ended (" + angel.ServerStatus + ")");
            yield return GuestLook(angel.transform.position + Vector3.up * angel.EyeHeight, copy.transform.position);
            yield return Expect(() => angel.Pose == CreaturePose.Frozen && angel.ServerWatcherId == guestId, 2f, () => "R2 the guest looks at it after the hold: frozen by the guest (" + angel.ServerStatus + ")");
            Check(angel.ServerGrabsStarted == 1, "R2 one catch");

            Heading("R3 — the kill on the guest");
            Creature.RefuseGrabKillForChecks = false;
            // Step back out of the statue first: at 0.7 m the reaching statue's bounds reach past the
            // guest's eyes, so a diver standing in them is "watching" whichever way it faces
            // (CreatureSenses.Watched reads the renderer bounds' corners).
            Vector3 backOff = copy.transform.position - angel.transform.position; backOff.y = 0f;
            Vector3 r3Spot = copy.transform.position + backOff.normalized * 4f;
            yield return GuestMove(r3Spot);
            yield return GuestEventually(r => Flat(VecField(GuestPlayerLine(r, guestId), "position"), r3Spot) < 1.0f, 6f, "R3 the guest steps back 4 m, still watching it");
            Check(angel.Pose == CreaturePose.Frozen, "R3 still frozen under the guest's look (" + angel.ServerStatus + ")");
            yield return GuestLook(r3Spot + backOff.normalized * 10f + Vector3.up * 1.6f, r3Spot);
            // Wait on the server's catch, then the guest's Alt at once (the guest has not yet seen the
            // hold, or just has): whichever, the dash must not carry it out of the embrace.
            float lookedAwayAt = Time.unscaledTime;
            while (!angel.ServerGrabbing && Time.unscaledTime - lookedAwayAt < 6f) yield return null;
            Check(angel.ServerGrabbing && angel.ServerGrabsStarted == 2 && angel.ServerGrabVictim == copy, "R3 the guest looked away: caught again (" + angel.ServerStatus + ")");
            Vector3 away = copy.transform.position - angel.transform.position; away.y = 0f;
            yield return Send("{\"id\":{id},\"action\":\"dash\",\"aim\":" + Vec(away) + "}");
            Say($"R3 the guest dashes away as it is caught (server hold t={angel.ServerGrabSeconds:0.00} s): " + lastReply.Split('\n')[0]);
            yield return Expect(() => angel.ServerGrabSeconds > grab.GripSeconds + 0.2f, 3f, () => "R3 past the grip");
            yield return Send("{\"id\":{id},\"action\":\"dash\",\"aim\":" + Vec(away) + "}");
            string inHold = lastReply.Split('\n')[0];
            Check(inHold.Contains("dash=False") && inHold.Contains("Not now"), "R3 the guest's dash in the hold is refused 'Not now': " + inHold);
            yield return Snapshot();
            string held = GuestPlayerLine(lastReply, guestId);
            float heldGap = Flat(VecField(held, "position"), angel.transform.TransformPoint(grab.GripPoint));
            Check(Text(held, "grabbed") == "True" && heldGap < 0.3f, $"R3 still held at the hold point on the guest's own screen ({heldGap * 100f:0.0} cm)");
            yield return Expect(() => angel.ServerGrabKills == 1, grab.HoldSeconds + 1.5f, () => "R3 the kill at the hold's end (" + angel.ServerStatus + ")");
            yield return GuestEventually(r => { string l = GuestPlayerLine(r, guestId); return Text(l, "dead") == "True" && Text(l, "grabbed") == "False"; }, 4f, "R3 the guest reads itself dead and let go");
            Check(!host.IsDead && !host.IsGrabbed, "R3 the host untouched");
            yield return Despawn();
            yield return GuestEventually(r => GuestMonsterLine(r) == string.Empty, 6f, "R3 the guest's copy went with it");
        }
    }
}
