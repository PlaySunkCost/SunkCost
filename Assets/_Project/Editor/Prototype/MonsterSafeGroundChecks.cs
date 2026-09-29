using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Monsters;
using SunkCost.Noise;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using M = SunkCost.Editor.Prototype.MonsterTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Round the safe ground (Dan, 29 September 2026: an Impostor stood against the tube's
    // foot for good; "make sure none of the monsters gets stuck there"). Each of the six
    // walkers is placed by the foot (5.3 m from the shaft) on five sides — the doorway,
    // the plinth corner where Dan saw it (-40° from the doorway), both flanks and the back
    // — with a goal on the far side of the tube (9 m out, opposite): the host for the
    // Impostor, the Weeping Angel (the host looking away), the Long Walker (it saw the host
    // first) and the Charger (its sight through the tube for the row:
    // Charger.SeesThroughWorldForChecks); a lamp glimpsed there for the Lure
    // (Lure.ServerGlimpseForChecks); a sound there for the Listener. Its position is read
    // every frame for the safe ground and every second for the stillness: it never steps
    // within SafeZoneMeters of the shaft, never stands within 1 m of one spot for more than
    // 3 s while it walks for a goal elsewhere (the Charger's tell, rush and daze, a beam's
    // charge, burn and recovery and the Angel's freeze are not walking), and it reaches the
    // far side — a straight way to the goal that no longer crosses the safe ground, within
    // 60° of the goal's bearing — within 45 s. The Elevator Ghost has no body.
    //
    // The rows run inside the monsters job (MonsterRuntimeChecks, before G0, the guest in
    // the car) and alone as the job "monsters-round" (the host alone, day 1, log
    // Temp/monsters-round-matrix.log).
    public static class MonsterSafeGroundChecks
    {
        private const string Log = "Temp/monsters-round-matrix.log";
        private const float StartRadius = 5.3f;
        private const float GoalRadius = 9f;
        private const float TrialSeconds = 45f;
        private const float StillLimitSeconds = 3f;

        private static readonly Stack<IEnumerator> stack = new();
        private static bool running;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static MonsterSettings Settings => MonsterSettings.Get();

        private static readonly (string name, float bearing)[] Sides =
        {
            ("the doorway", 0f),
            ("the plinth corner (Dan's spot)", -40f),
            ("the right flank", 90f),
            ("the left flank", -90f),
            ("the back", 180f),
        };

        private static readonly MonsterKind[] Kinds =
        {
            MonsterKind.Impostor, MonsterKind.WeepingAngel, MonsterKind.LongWalker,
            MonsterKind.Charger, MonsterKind.Lure, MonsterKind.Listener,
        };

        // ---- the job alone ------------------------------------------------------------

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (running) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Monsters round the safe ground started " + DateTime.Now + "\n");
            Status = "Running";
            running = true;
            stack.Clear();
            stack.Push(Alone());
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
            if (Status == "MATRIX_PASS") Debug.Log("Monsters round matrix: MATRIX_PASS"); else Debug.LogError("Monsters round matrix: " + Status);
            running = false;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + M.MonstersText());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
        }

        private static IEnumerator Alone()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            File.AppendAllText(Log, "\n== SG0 — to sea and down on day 1, the host alone\n");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "SG0 sailing to sea");
            yield return Until(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f);
            Check(Day.World == WorldId.Sea, "SG0 arrived at sea");
            yield return Until(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f);
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Until(() => Day.CabinRide.Serial > serial, 3f);
            Check(Day.CabinRide.Serial > serial, "SG0 the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Until(() => !Day.CabinRide.Active, 70f);
            yield return Until(() => Day.Elevator.State == ElevatorState.AtBottom, 30f);
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && Day.Elevator.State == ElevatorState.AtBottom, "SG0 down in the dive site, the car at the bottom: " + H.RideStatus());
            yield return Wait(Settings.WakeDelaySeconds + 1f);
            M.ServerDespawnMonsters();
            yield return Rows(Say, Check);
            Say("done");
        }

        // ---- the rows (shared with the monsters job) ------------------------------------

        public static IEnumerator Rows(Action<string> say, Action<bool, string> check)
        {
            HQPlayerController host = Host();
            ElevatorController car = WorldSceneFlow.FindCar();
            check(host != null && car != null, "SG the host and the car are here");
            check(CreatureSenses.ShaftCentre(out Vector3 axis), "SG the shaft's centre is known");
            Transform foot = FindFoot();
            check(foot != null, "SG the tube's foot is in the dive scene (Shaft Tube/Tube Foot)");
            Vector3 door = foot.forward; door.y = 0f;
            if (door.sqrMagnitude < 0.01f) door = car.transform.forward;
            door.y = 0f; door.Normalize();
            float safe = Settings.SafeZoneMeters;
            say($"SG shaft at {axis:F2}, doorway bearing 0 along {door:F2}; safe ground {safe} m; start {StartRadius} m, goal {GoalRadius} m opposite; {TrialSeconds} s per trial");
            check(!MonsterCatalog.Walkers.Contains(MonsterKind.ElevatorGhost), "SG the Elevator Ghost is not a walker: it has no body (the car's green light), nothing to walk round or get stuck");
            PlayerVitals vitals = host.Vitals;
            M.ServerDespawnMonsters();
            yield return Until(() => Creature.All.Count(c => c != null) == 0, 5f);

            foreach (MonsterKind kind in Kinds)
                foreach ((string side, float bearing) in Sides)
                {
                    vitals.ServerBeginDive(); // a full tank and full health for every trial (the rows take minutes)
                    yield return Trial(kind, side, bearing, axis, door, safe, say, check);
                    M.ServerDespawnMonsters();
                    yield return Until(() => Creature.All.Count(c => c != null) == 0, 5f);
                    check(Creature.All.Count(c => c != null) == 0, $"SG {kind} from {side}: despawned");
                    check(!host.IsDead, $"SG {kind} from {side}: the host lives");
                }
            vitals.ServerBeginDive();
        }

        private static Transform FindFoot()
        {
            UnityEngine.SceneManagement.Scene dive = WorldScenes.Scene(WorldId.Dive);
            if (!dive.isLoaded) return null;
            foreach (GameObject root in dive.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == SunkCost.Sites.ShaftTubeLook.FootName && t.parent != null && t.parent.name == "Shaft Tube") return t;
            return null;
        }

        private static Vector3 At(Vector3 axis, Vector3 door, float bearing, float radius) =>
            axis + Quaternion.Euler(0f, bearing, 0f) * door * radius + Vector3.up * 0.15f;

        private static float Flat(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);

        private static float BearingOf(Vector3 axis, Vector3 door, Vector3 p)
        {
            Vector3 v = p - axis; v.y = 0f;
            return Vector3.SignedAngle(door, v, Vector3.up);
        }

        // The closest the straight flat way from a to b comes to the shaft.
        private static float ClosestToShaft(Vector3 axis, Vector3 a, Vector3 b)
        {
            Vector3 h = a - axis; h.y = 0f;
            Vector3 seg = b - a; seg.y = 0f;
            float len2 = seg.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(-Vector3.Dot(h, seg) / len2) : 0f;
            return (h + seg * t).magnitude;
        }

        private static bool Acting(Creature c)
        {
            CreaturePose pose = c.Pose;
            return pose == CreaturePose.Frozen || pose == CreaturePose.Windup || pose == CreaturePose.Rushing
                || pose == CreaturePose.Recovering || pose == CreaturePose.Aiming || pose == CreaturePose.Shooting
                || pose == CreaturePose.Grabbing;
        }

        private static IEnumerator Trial(MonsterKind kind, string side, float bearing, Vector3 axis, Vector3 door, float safe, Action<string> say, Action<bool, string> check)
        {
            HQPlayerController host = Host();
            string label = $"SG {kind} from {side} ({bearing:0}°)";
            Vector3 start = At(axis, door, bearing, StartRadius);
            Vector3 goal = At(axis, door, bearing + 180f, GoalRadius);
            Vector3 outward = goal + (goal - axis).normalized * 6f + Vector3.up * 1.4f; // the host looks away from the shaft
            bool hostIsGoal = kind == MonsterKind.Impostor || kind == MonsterKind.WeepingAngel || kind == MonsterKind.LongWalker || kind == MonsterKind.Charger;

            if (hostIsGoal) { host.TeleportLocal(goal, Quaternion.LookRotation(goal - axis).eulerAngles.y); }
            else H.MoveLocalIntoCar(); // the Lure and the Listener: the host is safe in the car, the goal is a spot
            yield return null; yield return null;
            if (hostIsGoal) M.ClientLookAt(outward);

            Creature c;
            if (kind == MonsterKind.LongWalker)
            {
                // It walks after a diver it has seen: it sees the host first on its own side, then the host goes across.
                Vector3 seen = At(axis, door, bearing, 12f);
                host.TeleportLocal(seen, Quaternion.LookRotation(start - seen).eulerAngles.y);
                yield return null; yield return null;
                M.ClientLookAt(start + Vector3.up * 1.5f);
                c = MonsterRoster.ServerSpawnForChecks(kind, start);
                check(c != null, label + ": spawned");
                yield return Until(() => c.TargetId == host.OwnerId, 5f);
                check(c.TargetId == host.OwnerId, label + ": it saw the host and walks after them (" + c.ServerStatus + ")");
                host.TeleportLocal(goal, Quaternion.LookRotation(goal - axis).eulerAngles.y);
                c.ServerPlaceForChecks(start, Quaternion.LookRotation(axis - start).eulerAngles.y);
                yield return null; yield return null;
                M.ClientLookAt(outward);
            }
            else
            {
                c = MonsterRoster.ServerSpawnForChecks(kind, start);
                check(c != null, label + ": spawned");
                c.ServerPlaceForChecks(start, Quaternion.LookRotation(axis - start).eulerAngles.y);
                if (c is Charger charger) charger.SeesThroughWorldForChecks = true;
                if (c is Impostor impostor) impostor.ServerChooseForChecks(host.OwnerId);
            }
            if (c is Lure lure) lure.ServerGlimpseForChecks(goal);
            if (c is Listener) NoiseSystem.Emit(goal, 30f, NoiseKind.Sprint);

            float t0 = Time.unscaledTime, lastSample = t0, lastCue = t0;
            float minR = float.PositiveInfinity, longestStill = 0f, travelled = 0f;
            Vector3 anchor = c.transform.position, last = anchor;
            float anchorSince = t0;
            bool reached = false;
            int samples = 0;
            var trace = new System.Text.StringBuilder();
            while (Time.unscaledTime - t0 < TrialSeconds)
            {
                yield return null;
                if (c == null || !c.IsSpawned) { check(false, label + ": the creature vanished"); yield break; }
                float now = Time.unscaledTime;
                Vector3 p = c.transform.position;
                float r = Flat(p, axis);
                minR = Mathf.Min(minR, r);
                if (r < safe - 0.02f) check(false, $"{label}: it stepped onto the safe ground, {r:0.00} m from the shaft (< {safe}) at {now - t0:0.0} s ({c.ServerStatus})");
                // Keep its goal alive: the Lure's glimpse each second, the Listener's sound before it forgets.
                if (c is Lure l && now - lastCue >= 1f) { l.ServerGlimpseForChecks(goal); lastCue = now; }
                if (c is Listener && now - lastCue >= Settings.ListenerForgetSeconds - 2f) { NoiseSystem.Emit(goal, 30f, NoiseKind.Sprint); lastCue = now; }
                Vector3 goalNow = hostIsGoal ? host.transform.position : goal;
                float gap = Mathf.Abs(Mathf.DeltaAngle(BearingOf(axis, door, p), BearingOf(axis, door, goalNow)));
                if (gap <= 60f && ClosestToShaft(axis, p, goalNow) >= safe - 0.05f) { reached = true; break; }
                if (now - lastSample < 1f) continue;
                // The one-second sample: the stillness clock.
                lastSample = now;
                samples++;
                travelled += Flat(p, last);
                last = p;
                bool walking = !Acting(c) && Flat(p, goalNow) > 3.5f;
                if (!walking || Flat(p, anchor) > 1f) { anchor = p; anchorSince = now; }
                float still = now - anchorSince;
                longestStill = Mathf.Max(longestStill, still);
                trace.Append($" {now - t0:0}s r{r:0.00} b{BearingOf(axis, door, p):0} {c.Pose};");
                if (still > StillLimitSeconds)
                    check(false, $"{label}: it stood within 1 m of one spot for {still:0.0} s with a goal {Flat(p, goalNow):0.0} m away (r {r:0.00}, bearing {BearingOf(axis, door, p):0}); trace{trace} ({c.ServerStatus})");
            }
            travelled += Flat(c.transform.position, last);
            float took = Time.unscaledTime - t0;
            if (!reached) say(label + " trace" + trace);
            check(reached, $"{label}: it reached the far side in {took:0.0} s (limit {TrialSeconds}) — never nearer the shaft than {minR:0.00} m (safe {safe}), longest still {longestStill:0.0} s, travelled {travelled:0.0} m, now at bearing {BearingOf(axis, door, c.transform.position):0} r {Flat(c.transform.position, axis):0.00} ({c.ServerStatus})");
        }
    }
}
