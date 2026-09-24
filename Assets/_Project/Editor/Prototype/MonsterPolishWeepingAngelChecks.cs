using System;
using System.Collections;
using System.Collections.Generic;
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
            File.WriteAllText(Log, "Weeping Angel polish checks started " + DateTime.Now + "\n");
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
            Check(moved < 0.01f && worst < 0.004f, $"{label}: nothing moves under the look for {seconds:0.0} s (body {moved * 1000f:0.0} mm, worst bone {worstBone} {worst * 1000f:0.0} mm)");
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
            Check(grabbing.length >= grabDef.HoldSeconds + grabDef.ReleaseSeconds - 0.05f, $"the embrace clip ({grabbing.length:0.00} s) outlasts the hold and the release ({grabDef.HoldSeconds + grabDef.ReleaseSeconds:0.00} s): its loop never shows");
            Check(rigDef.AuthoredSpeed(CreaturePose.Hunting) > 5f && rigDef.AuthoredSpeed(CreaturePose.Frozen) <= 0f, $"the sprint is speed-matched ({rigDef.AuthoredSpeed(CreaturePose.Hunting)} m/s), the statue is not");

            Heading("M0 — to sea, down on day 1 (the roster empty: the rows spawn the Angel)");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            yield return Descend(1);
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
            CreatureGrab grab = angel.GetComponent<CreatureGrab>();
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
            float faceAt = -1f; float handsLeftFaceBy = -1f; float handsOnHeadBy = -1f;
            Transform head = Bone(angel, "Head"), handL = Bone(angel, "HandL"), handR = Bone(angel, "HandR");
            while (Time.unscaledTime < sampleUntil && angel.ServerGrabbing)
            {
                float t = angel.ServerGrabSeconds;
                worstMove = Mathf.Max(worstMove, Flat(angel.transform.position, angelHold));
                if (angel.Pose != CreaturePose.Grabbing) alwaysGrabbing = false;
                // the diver's eyes are on the Angel: it is watched the whole embrace
                host.EyePose(out Vector3 eye, out Quaternion look);
                Vector3 toFace = angel.transform.TransformPoint(grab.FaceTarget) - eye;
                worstLookOff = t > grab.GripSeconds ? Mathf.Max(worstLookOff, Vector3.Angle(look * Vector3.forward, toFace)) : worstLookOff;
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

            Heading("G4/G5 — aligned with the diver; it looks like the embrace");
            float feetGap = Flat(host.transform.position, angel.transform.TransformPoint(grab.GripPoint));
            float bodyGap = Flat(host.transform.position, angel.transform.position);
            Vector3 hostFwd = host.transform.forward; hostFwd.y = 0f;
            Vector3 toAngelFlat = angel.transform.position - host.transform.position; toAngelFlat.y = 0f;
            Vector3 angelFwd = angel.transform.forward; angelFwd.y = 0f;
            Say($"G4 feet {feetGap * 100f:0.0} cm from the hold point, {bodyGap:0.00} m from the Angel; hands left the face at {handsLeftFaceBy:0.00} s, on the diver's head at {handsOnHeadBy:0.00} s; the face {faceAt:0.00} m from the diver's eyes");
            Check(feetGap < 0.08f, "G4 the diver stands at the embrace's hold point");
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

            Heading("G8 — a diver walking away is caught; the kill and a clean end");
            Creature.RefuseGrabKillForChecks = false;
            Vector3 away = host.transform.position + (host.transform.position - angel.transform.position).normalized * 20f;
            M.ClientLookAt(away + Vector3.up * 1.6f);
            Keys(Key.W); // walking away, back turned
            yield return Expect(() => angel.ServerGrabbing, 4f, () => "G8 the walking host was caught (" + angel.ServerStatus + ")");
            Keys();
            Check(angel.ServerGrabsStarted == 2, "G8 a second catch after the first let go: it returned to valid behaviour");
            float caughtWalking = Flat(angel.transform.position, host.Grab.CaughtAt);
            Check(caughtWalking <= s.ReachMeters + 0.3f, $"G8 caught walking at {caughtWalking:0.00} m");
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
    }
}
