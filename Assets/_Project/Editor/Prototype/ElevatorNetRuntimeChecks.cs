using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SunkCost.Diving;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using S = SunkCost.Net.ElevatorNetProbe.Sample;

namespace SunkCost.Editor.Prototype
{
    // The new elevator over the network (scratchpad elevator-build/BRIEF.md "TESTING
    // REQUIREMENTS — NETWORK", TESTPLAN.md §2.3 NT1-NT7; 28 September 2026). The ride's
    // rules are the cabin job's business; this job checks that every peer, a spectator and
    // the ship TV SHOW the same elevator at the same tick: the car's position, its doors,
    // the tube's gate, the deck cabin's doors and shutters, and the car's water (the level,
    // the flow, the streams, bubbles, foam, drain, the gauge and the screen).
    //
    // How peers are compared. Every peer runs an ElevatorNetProbe (the guests through
    // InventoryVerificationPeer, the host through a probe this job adds) that samples, each
    // LateUpdate, what that peer presents, stamped with the tick the car was driven at
    // (CarTick = the phase's StartTick + the car's own elapsed time) and the network tick.
    // The host keeps every frame's sample; each guest reply (about two a second per guest
    // during a ride) is compared with the host's two samples around the guest's own tick,
    // interpolated: a peer that agrees shows the same values at the same tick. A reply
    // from a phase the host has already left is "stale" (the phase change still on the
    // wire), measured and soft-failed past half a second. The two processes' ticks at one
    // moment of the machine's clock are compared too (the clock skew, soft).
    // Each probe also checks its own peer EVERY frame against the replicated phase (the
    // water against the level formula, the gauge, the gate against the door, the doors
    // shut while moving, the shutters against the deck doors) and the first frame after
    // the car or the ship appears (a late loader sees the state at once).
    //
    // Rows: E0 3 players at rest at sea; E1 3 players ride down; E2 guest B rides up alone
    // (the others below watch the drain); E3 the empty car goes back down (B on the ship:
    // the shutters shut, the TV goes live); E4 A dies below and spectates the host, B
    // watches the TV, the full car at the bottom captured, the host rides up (the drain on
    // the spectator's view and on the TV); E5 day 2: 3 players ride down while guest C is
    // launched at the press (refused: "Dive in progress — join between days"; X1: a real
    // mid-ride join is forbidden by the contract); E6 3 players ride up and B's process is
    // killed mid-drain; E7 day 3: B2 and C2 join between days (their first reply is the
    // host's state at once), 4 players ride down; E8 4 players ride up and A leaves
    // mid-ascent; E9 the sweep. Log: Temp/elevator-net-matrix.log.
    // Started by CameraClearanceMatrixDriver.Start("elevator-net").
    public static class ElevatorNetRuntimeChecks
    {
        private const string Log = "Temp/elevator-net-matrix.log";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Captures = "Temp/elevator-net/";

        // Tolerances at the same tick. Both peers compute the car from the same phase and
        // the same function, so at one tick they should agree to rounding; the host's
        // samples are one frame apart and interpolated.
        private const float TolY = 0.02f, TolWater = 0.02f, TolGauge = 0.01f, TolDoor = 0.03f, TolGate = 0.05f, TolFoam = 0.05f, TolDrain = 0.02f;
        private const float TolDeckDoors = 0.03f, TolYawDeg = 1.0f;
        private const float TolEye = 0.35f;        // a spectator's camera / the TV camera against the watched eye (eased)
        private const float SurfaceBand = 0.45f;   // the grade blends 0.5 m over the surface: judge views only away from it
        private const float LagLimit = 0.5f;       // s: a guest may show a phase the host has left for this long
        private const float SkewLimitTicks = 3f;   // the processes' ticks at one wall-clock moment
        private const double EarlyTicks = 2.0;     // a guest sample up to 2 ticks before the host's first of a phase

        private enum Role { Rider, Spectator, Tv }

        private sealed class Guest
        {
            public string Label; public string Dir; public Process Process; public int Id = -1; public string Name = string.Empty; public bool Headless; public Role Role;
            public override string ToString() => Label + "#" + Id;
        }

        // One guest's comparisons over one stage.
        private sealed class Stats
        {
            public string Label;
            public int Replies, NoLine, Car, Deck, DeckInFlight, Eye, TvRows, Stale, Unmatched, Torn, FailCount, ScreenSoft, GateEdge, SkewHostStall;
            public float WorstY, WorstWater, WorstDoor, WorstGate, WorstGauge, WorstDeckDoors, WorstYaw, WorstEye, WorstTvEye, MaxLag;
            public readonly List<double> Skews = new();
            public readonly List<string> Fails = new();
            public readonly HashSet<string> Moments = new();
            public string LastReply = string.Empty;
            public string WorstSkewAt = "none";
            public S Last;
            public bool HaveLast;
            public void Fail(string what) { FailCount++; if (Fails.Count < 10) Fails.Add(what); }
        }

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static readonly List<Guest> crew = new();      // guests connected now
        private static readonly List<Guest> launched = new();  // every process started (killed at the end, logs swept)
        private static int guestCommand = 5600;
        private static string lastReply = string.Empty;
        private static readonly List<string> logs = new();
        private static readonly List<string> softFails = new();
        private static readonly List<bool> admissionRefusalDuringRide = new();
        private static int exceptionsSeen, errorsSeen;
        private static GameObject hostProbe;
        private static Dictionary<Guest, Stats> lastStats = new();
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run elevator-net matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Elevator-net matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            Directory.CreateDirectory(Captures);
            File.WriteAllText(Log, "Elevator-net matrix started " + DateTime.Now + "\n");
            Status = "Running";
            logs.Clear(); crew.Clear(); launched.Clear(); softFails.Clear(); admissionRefusalDuringRide.Clear(); lastStats = new Dictionary<Guest, Stats>();
            exceptionsSeen = 0; errorsSeen = 0;
            profileSeconds = float.NaN;
            Application.logMessageReceived += OnLog;
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            EditorApplication.update += Tick;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception) exceptionsSeen++;
            if (type == LogType.Error || type == LogType.Assert) errorsSeen++;
            if (message.Contains("[Admission]") && message.Contains("DiveInProgress")) admissionRefusalDuringRide.Add(Day != null && Day.CabinRide.Active);
            if (type != LogType.Log || message.Contains("[WorldSceneFlow]") || message.Contains("[Admission]"))
                logs.Add("[" + type + "] " + message.Split('\n')[0]);
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
            try { foreach (string l in logs.Where(l => !l.StartsWith("[Log]"))) File.AppendAllText(Log, "  log " + l + "\n"); } catch (Exception) { }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Elevator-net matrix: MATRIX_PASS"); else Debug.LogError("Elevator-net matrix: " + Status);
            foreach (Guest g in launched)
                try { if (g?.Process != null && !g.Process.HasExited) g.Process.Kill(); } catch (Exception) { }
            crew.Clear(); launched.Clear();
            ElevatorNetProbe.StopRecording();
            if (hostProbe != null) UnityEngine.Object.Destroy(hostProbe);
            hostProbe = null;
            steps = null;
            stack.Clear();
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Tick;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static string State() => H.RideStatus() + "\n" + H.FlowStatus() + "\nhost probe: " + ElevatorNetProbe.Line(ElevatorNetProbe.Last) + "\n" + ElevatorNetProbe.RecorderLine() + "\ncrew: " + string.Join(", ", crew);
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + State());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static void Soft(bool value, string label)
        {
            if (value) { File.AppendAllText(Log, "PASS " + label + "\n"); return; }
            softFails.Add(label.Split('\n')[0]);
            File.AppendAllText(Log, "SOFT-FAIL " + label + "\n");
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
        private static int Count(string contains) => logs.Count(l => l.Contains(contains));
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", CultureInfo.InvariantCulture) + "}";
        private static string Json(string action, string item = null) => "{\"id\":{id},\"action\":\"" + action + "\"" + (item == null ? string.Empty : ",\"item\":\"" + item.Replace("\\", "/") + "\"") + "}";

        // ---- guests (the console-net pattern: a directory each, one rising command id) -----

        private static Guest Launch(string label, string dir, bool headless)
        {
            Directory.CreateDirectory(dir);
            foreach (string stale in new[] { "command.json", "reply.txt", "player.log" })
                if (File.Exists(Path.Combine(dir, stale))) File.Delete(Path.Combine(dir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            string video = headless ? "-batchmode -nographics" : "-screen-width 960 -screen-height 540 -screen-fullscreen 0";
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                video + " -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(dir) + "\" -logFile \"" + Path.GetFullPath(dir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            var g = new Guest { Label = label, Dir = dir, Process = Process.Start(info), Headless = headless };
            launched.Add(g);
            Say($"launched guest {label} ({(headless ? "headless" : "windowed")}) in {dir}");
            return g;
        }
        // Waits for the guest's player to spawn on the host and its peer to answer at sea.
        private static IEnumerator Joined(Guest g, string label)
        {
            HashSet<int> known = new(crew.Where(x => x != g && x.Id >= 0).Select(x => x.Id));
            HQPlayerController copy = null;
            float deadline = Time.unscaledTime + 45f;
            while (Time.unscaledTime < deadline)
            {
                copy = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner && !known.Contains(p.OwnerId));
                if (copy != null) break;
                yield return null;
            }
            Check(copy != null, label + ": the guest's player spawned on the host");
            g.Id = copy.OwnerId;
            yield return GuestEventually(g, r => PlayerLine(r, g.Id).Contains("local=True") && Header(r).Contains("world=Sea;") && Header(r).Contains("phase=AtSea;") && r.Contains("\nelevnet: "), 30f, label + ": the guest's peer answers at sea with its elevator probe");
            g.Name = WorldSceneFlow.DisplayName(g.Id);
            crew.Add(g);
            Say($"{label}: guest '{g.Name}' is client {g.Id} ({g.Dir}); {SpawnedPlayers()} players");
        }
        private static HQPlayerController CopyOf(Guest g) => g == null || g.Id < 0 ? null : UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner && p.OwnerId == g.Id);
        private static int SpawnedPlayers() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Count(p => p.IsSpawned);
        private static bool Alive(Guest g) { try { return g.Process != null && !g.Process.HasExited; } catch (Exception) { return false; } }

        // The guest polls its file every frame; a write can collide with its read.
        private static int Command(Guest g, string json)
        {
            int id = ++guestCommand;
            string text = json.Replace("{id}", id.ToString());
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(Path.Combine(g.Dir, "command.json"), text); return id; }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(15); }
            }
        }
        private static string Reply(Guest g)
        {
            try { string p = Path.Combine(g.Dir, "reply.txt"); return File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
            catch (IOException) { return string.Empty; }
        }
        private static IEnumerator AwaitReply(Guest g, int id, float seconds = 10f)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                string reply = Reply(g);
                if (reply.StartsWith("id=" + id + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest " + g + " did not answer command " + id + "\n" + Reply(g) + "\n" + State());
        }
        private static IEnumerator Send(Guest g, string json) { int id = Command(g, json); yield return AwaitReply(g, id); }
        private static IEnumerator GuestEventually(Guest g, Func<string, bool> predicate, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send(g, Json("snapshot"));
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.3f);
            }
            throw new Exception(label + "\n" + g + " " + Header(lastReply) + "\n" + Line(lastReply, "elevnet: ") + "\n" + State());
        }
        // One snapshot from each guest, requested in the same frame; a guest that does not
        // answer within the time (killed, leaving) is left out of this round, not failed.
        private static IEnumerator Snap(IList<Guest> gs, Dictionary<Guest, string> into, float seconds = 6f)
        {
            var ids = new Dictionary<Guest, int>();
            foreach (Guest g in gs) ids[g] = Command(g, Json("snapshot"));
            var pending = new List<Guest>(gs);
            float deadline = Time.unscaledTime + seconds;
            while (pending.Count > 0 && Time.unscaledTime < deadline)
            {
                foreach (Guest g in pending.ToList())
                {
                    string r = Reply(g);
                    if (r.StartsWith("id=" + ids[g] + ";")) { into[g] = r; pending.Remove(g); }
                }
                if (pending.Count > 0) yield return null;
            }
            foreach (Guest g in pending) Say($"no snapshot from {g} this round (alive={Alive(g)})");
        }
        private static IEnumerator GuestMove(Guest g, Vector3 to) { yield return Send(g, "{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static IEnumerator GuestLook(Guest g, Vector3 aim) { yield return Send(g, "{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(aim) + "}"); }
        private static IEnumerator Leave(Guest g, string label)
        {
            yield return Send(g, Json("leave"));
            yield return Expect(() => CopyOf(g) == null, 15f, () => label + ": " + g.Label + " left");
            crew.Remove(g);
            try { if (!g.Process.HasExited) g.Process.Kill(); } catch (Exception) { }
        }

        private static string Line(string reply, string prefix) => reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        private static string Header(string reply) => Line(reply, "server=");
        private static string PlayerLine(string reply, int ownerId) => Line(reply, "player=" + ownerId + ";");
        private static string Rx(string text, string pattern) { Match m = Regex.Match(text, pattern); return m.Success ? m.Groups[1].Value : "(absent)"; }
        private static float Rec(string reply, string key) => ElevatorNetProbe.Fields(reply, "elevnetrec: ").TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;
        private static string RecText(string reply, string key) => ElevatorNetProbe.Fields(reply, "elevnetrec: ").TryGetValue(key, out string v) ? v : "(absent)";

        // ---- the host's record at a guest's tick -------------------------------------------

        // The host's two samples of phase `serial` around carTick x: 0 bracketed (a, b, f);
        // 1 x lies past the host's last sample of that phase (the guest still shows a phase
        // the host has left; lastTick is that last sample); 2 x lies before the host's first
        // (a = b = that first); 3 the host never showed that phase.
        private static int HostCar(int serial, double x, out S a, out S b, out float f, out double lastTick)
        {
            a = default; b = default; f = 0f; lastTick = double.NaN;
            bool haveA = false, haveB = false, any = false;
            IReadOnlyList<S> rec = ElevatorNetProbe.Recorded;
            for (int i = 0; i < rec.Count; i++)
            {
                S h = rec[i];
                if (!h.Car || !h.Driven || h.Torn || h.Serial != serial) continue;
                any = true; lastTick = h.CarTick;
                if (h.CarTick <= x) { a = h; haveA = true; }
                else if (!haveB) { b = h; haveB = true; }
            }
            if (!any) return 3;
            if (haveA && haveB)
            {
                double span = b.CarTick - a.CarTick;
                f = span > 1e-9 ? (float)((x - a.CarTick) / span) : 0f;
                return 0;
            }
            if (haveA) { b = a; return 1; }
            a = b; return 2;
        }

        // The host's two samples around network tick x (any frame with `want`).
        private static bool HostAt(double x, Func<S, bool> want, out S a, out S b, out float f)
        {
            a = default; b = default; f = 0f;
            bool haveA = false, haveB = false;
            IReadOnlyList<S> rec = ElevatorNetProbe.Recorded;
            for (int i = 0; i < rec.Count; i++)
            {
                S h = rec[i];
                if (!want(h)) continue;
                if (h.Tick <= x) { a = h; haveA = true; }
                else if (!haveB) { b = h; haveB = true; }
            }
            if (!haveA || !haveB) return false;
            double span = b.Tick - a.Tick;
            f = span > 1e-9 ? (float)((x - a.Tick) / span) : 0f;
            return true;
        }

        // The host's network tick at a moment of the machine's clock.
        private static bool HostTickAtWall(long wall, out double tick) => HostTickAtWall(wall, out tick, out _);
        private static bool HostTickAtWall(long wall, out double tick, out double gapSeconds)
        {
            tick = double.NaN; gapSeconds = double.NaN;
            IReadOnlyList<S> rec = ElevatorNetProbe.Recorded;
            S a = default, b = default; bool haveA = false, haveB = false;
            for (int i = 0; i < rec.Count; i++)
            {
                S h = rec[i];
                if (h.Wall <= wall) { a = h; haveA = true; }
                else if (!haveB) { b = h; haveB = true; }
            }
            if (!haveA || !haveB || b.Wall == a.Wall) return false;
            gapSeconds = (b.Wall - a.Wall) / (double)TimeSpan.TicksPerSecond;
            tick = a.Tick + (b.Tick - a.Tick) * (wall - a.Wall) / (double)(b.Wall - a.Wall);
            return true;
        }

        private static bool Near(float v, float a, float b, float f, float tol) =>
            !float.IsNaN(v) && (Mathf.Abs(v - Mathf.Lerp(a, b, f)) <= tol || Mathf.Abs(v - a) <= tol || Mathf.Abs(v - b) <= tol);
        private static float Off(float v, float a, float b, float f) => float.IsNaN(v) ? float.PositiveInfinity : Mathf.Min(Mathf.Abs(v - Mathf.Lerp(a, b, f)), Mathf.Min(Mathf.Abs(v - a), Mathf.Abs(v - b)));
        private static string At(S s) => $"{s.State}{(s.Upward ? "↑" : "↓")} serial {s.Serial} carTick {s.CarTick:0.0} tick {s.Tick:0.0}";

        // What a guest's sample shows, by moment of the ride (the coverage the rows need).
        private static void NoteMoments(S s, Stats st)
        {
            if (s.Ship && s.RideStage == CabinRideStage.Sealing && s.DeckDoors > 0.02f && s.DeckDoors < 0.98f) st.Moments.Add("deck-sealing");
            if (s.Ship && s.RideStage == CabinRideStage.Arriving && s.DeckDoors > 0.02f && s.DeckDoors < 0.98f) st.Moments.Add("deck-opening");
            if (s.Ship && !s.CarShown && s.ShutterBlocks) st.Moments.Add("shutters-shut");
            if (s.Ship && s.CarShown && Mathf.Abs(s.ShutterYaw) > 1f && !s.ShutterBlocks) st.Moments.Add("shutters-open");
            if (!s.Car || !s.Driven) return;
            bool moving = s.State == ElevatorState.Descending || s.State == ElevatorState.Ascending;
            if (s.State == ElevatorState.AtTop || (s.State == ElevatorState.Sealing && !s.Upward)) st.Moments.Add("top-shut");
            if (moving && s.Water <= 0.001f) st.Moments.Add("moving-dry");
            if (s.Flow == CarWaterFlow.Filling) st.Moments.Add("flooding");
            if (s.Flow == CarWaterFlow.Draining) st.Moments.Add("draining");
            if (moving && s.Water >= s.Span - 0.02f) st.Moments.Add("moving-full");
            if (s.State == ElevatorState.AtBottom && s.Door > 0.02f && s.Door < 0.98f) st.Moments.Add("bottom-opening");
            if (s.State == ElevatorState.Sealing && s.Upward && s.Door > 0.02f && s.Door < 0.98f) st.Moments.Add("bottom-closing");
            if (s.State == ElevatorState.AtBottom && s.Door >= 0.99f && s.Gate >= 0.95f) st.Moments.Add("bottom-open");
        }

        private static void CompareCar(S s, S a, S b, float f, Stats st)
        {
            string at = At(s);
            void Num(string key, float v, float av, float bv, float tol, ref float worst)
            {
                float off = Off(v, av, bv, f);
                if (!float.IsInfinity(off)) worst = Mathf.Max(worst, off);
                if (!Near(v, av, bv, f, tol)) st.Fail($"{key} {v:0.0000} vs host {Mathf.Lerp(av, bv, f):0.0000} (±{tol}) at {at}");
            }
            void Same<T>(string key, T v, T av, T bv)
            {
                if (!EqualityComparer<T>.Default.Equals(v, av) && !EqualityComparer<T>.Default.Equals(v, bv)) st.Fail($"{key} {v} vs host {av}/{bv} at {at}");
            }
            float unused = 0f;
            Num("carY", s.CarY, a.CarY, b.CarY, TolY, ref st.WorstY);
            Num("water", s.Water, a.Water, b.Water, TolWater, ref st.WorstWater);
            Num("gauge", s.Gauge, a.Gauge, b.Gauge, TolGauge, ref st.WorstGauge);
            Num("door", s.Door, a.Door, b.Door, TolDoor, ref st.WorstDoor);
            Num("gate", s.Gate, a.Gate, b.Gate, TolGate, ref st.WorstGate);
            Num("foam", s.Foam, a.Foam, b.Foam, TolFoam, ref unused);
            Num("drain", s.Drain, a.Drain, b.Drain, TolDrain, ref unused);
            Same("flow", s.Flow, a.Flow, b.Flow);
            Same("pour", s.Pour, a.Pour, b.Pour);
            Same("visibleStreams", s.Visible, a.Visible, b.Visible);
            Same("bubbles", s.Bubbles, a.Bubbles, b.Bubbles);
            Same("surfaceShown", s.Shown, a.Shown, b.Shown);
            Same("tubeRing", s.Ring, a.Ring, b.Ring);
            // The gate's box follows its fraction (blocks at 0.01 or less), and ShaftGate reads the
            // door's fraction from the door's own Update with no set order: on the first frame
            // of an opening or the last of a closing the gate may be one frame behind the door
            // (both within TolGate). A box that differs while either peer's gate stands clearly
            // open is a real difference (round 1 fix to the test).
            if (s.GateBlocks != a.GateBlocks && s.GateBlocks != b.GateBlocks)
            {
                float hostGate = Mathf.Lerp(a.Gate, b.Gate, f);
                if (s.Gate > 0.01f + TolGate || hostGate > 0.01f + TolGate) st.Fail($"gateBlocks {s.GateBlocks} (gate {s.Gate:0.000}, door {s.Door:0.000}) vs host {a.GateBlocks}/{b.GateBlocks} (gate {hostGate:0.000}, door {Mathf.Lerp(a.Door, b.Door, f):0.000}) at {at}");
                else st.GateEdge++;
            }
            string sa = ElevatorNetProbe.Normalised(a.CarScreen), sb = ElevatorNetProbe.Normalised(b.CarScreen);
            if (!ScreenBetween(s.CarScreen, sa, sb)) st.Fail($"carScreen {s.CarScreen} vs host {sa}/{sb} at {at}");
        }

        // The screen's words equal one host frame's, and each number lies between the two
        // host frames' (±1): depth and water round at different moments, so a peer between
        // two host frames may show one number of each (round 1 fix to the test).
        private static readonly Regex Digits = new(@"\d+");
        private static bool ScreenBetween(string v, string a, string b)
        {
            if (v == a || v == b) return true;
            string sk = Digits.Replace(v, "#");
            if (sk != Digits.Replace(a, "#") || sk != Digits.Replace(b, "#")) return false;
            MatchCollection mv = Digits.Matches(v), ma = Digits.Matches(a), mb = Digits.Matches(b);
            for (int i = 0; i < mv.Count; i++)
            {
                int x = int.Parse(mv[i].Value), xa = int.Parse(ma[i].Value), xb = int.Parse(mb[i].Value);
                if (x < Math.Min(xa, xb) - 1 || x > Math.Max(xa, xb) + 1) return false;
            }
            return true;
        }

        private static void CompareDeck(S s, Stats st)
        {
            bool Same(S h) => h.Ship && h.RideSerial == s.RideSerial && h.RideStage == s.RideStage && h.Serial == s.Serial;
            if (!HostAt(s.Tick, h => h.Ship, out S a, out S b, out float f)) return;
            if (!Same(a) || !Same(b)) { st.DeckInFlight++; return; }
            st.Deck++;
            string at = $"ride {s.RideSerial}/{s.RideStage} phase {s.Serial} tick {s.Tick:0.0}";
            float off = Off(s.DeckDoors, a.DeckDoors, b.DeckDoors, f);
            st.WorstDeckDoors = Mathf.Max(st.WorstDeckDoors, float.IsInfinity(off) ? 0f : off);
            if (!Near(s.DeckDoors, a.DeckDoors, b.DeckDoors, f, TolDeckDoors)) st.Fail($"deckDoors {s.DeckDoors:0.000} vs host {Mathf.Lerp(a.DeckDoors, b.DeckDoors, f):0.000} at {at}");
            float yawOff = Mathf.Max(Off(s.DoorYaw, a.DoorYaw, b.DoorYaw, f), Off(s.ShutterYaw, a.ShutterYaw, b.ShutterYaw, f));
            st.WorstYaw = Mathf.Max(st.WorstYaw, float.IsInfinity(yawOff) ? 0f : yawOff);
            if (!Near(s.DoorYaw, a.DoorYaw, b.DoorYaw, f, TolYawDeg)) st.Fail($"deck door yaw {s.DoorYaw:0.00} vs host {Mathf.Lerp(a.DoorYaw, b.DoorYaw, f):0.00} at {at}");
            if (!Near(s.ShutterYaw, a.ShutterYaw, b.ShutterYaw, f, TolYawDeg)) st.Fail($"shutter yaw {s.ShutterYaw:0.00} vs host {Mathf.Lerp(a.ShutterYaw, b.ShutterYaw, f):0.00} at {at}");
            if (s.CarShown != a.CarShown && s.CarShown != b.CarShown) st.Fail($"deck car shown {s.CarShown} vs host {a.CarShown}/{b.CarShown} at {at}");
            if (s.ShutterBlocks != a.ShutterBlocks && s.ShutterBlocks != b.ShutterBlocks) st.Fail($"shutter collider {s.ShutterBlocks} vs host {a.ShutterBlocks}/{b.ShutterBlocks} at {at}");
            if (s.DoorwayBlocks != a.DoorwayBlocks && s.DoorwayBlocks != b.DoorwayBlocks) st.Fail($"doorway collider {s.DoorwayBlocks} vs host {a.DoorwayBlocks}/{b.DoorwayBlocks} at {at}");
            // The plate's words include a refusal shown for a few seconds by each peer's own clock: soft.
            if (s.DeckScreen != ElevatorNetProbe.Normalised(a.DeckScreen) && s.DeckScreen != ElevatorNetProbe.Normalised(b.DeckScreen)) st.ScreenSoft++;
        }

        // A spectator's own view, and the TV's picture, against the watched host's eye at the tick.
        private static void CompareEyes(Guest g, S s, Stats st, int hostId)
        {
            // Only while the host stands in the car at the site (not through its scene move at the top).
            if (!HostAt(s.Tick, h => h.Local && h.Car && h.Driven, out S a, out S b, out float f) || !a.InCar || !b.InCar || a.Dead) return;
            float hostEye = Mathf.Lerp(a.EyeY, b.EyeY, f);
            float sea = a.Sea;
            bool hostUnder = hostEye < sea;
            bool clear = Mathf.Abs(hostEye - sea) > SurfaceBand;
            string at = $"host eye {hostEye:0.00} (sea {sea:0.00}) tick {s.Tick:0.0}";
            if (g.Role == Role.Spectator && s.Local)
            {
                st.Eye++;
                if (s.Spectating != hostId) st.Fail($"{g.Label} watches {s.Spectating}, not the host {hostId} at {at}");
                st.WorstEye = Mathf.Max(st.WorstEye, Mathf.Abs(s.EyeY - hostEye));
                if (Mathf.Abs(s.EyeY - hostEye) > TolEye) st.Fail($"{g.Label}'s camera at y {s.EyeY:0.00}, the host's eye at {at}");
                if (clear && (s.View > 0.5f) != hostUnder) st.Fail($"{g.Label}'s view underwater={s.View:0.00} but the host's eye {(hostUnder ? "is" : "is not")} under at {at}");
            }
            if (g.Role == Role.Tv && s.Ship && s.TvLive && s.Tv == hostId && !float.IsNaN(s.TvEyeY) && s.TvFrames > 0)
            {
                st.TvRows++;
                st.WorstTvEye = Mathf.Max(st.WorstTvEye, Mathf.Abs(s.TvEyeY - hostEye));
                if (Mathf.Abs(s.TvEyeY - hostEye) > TolEye) st.Fail($"{g.Label}'s TV camera at y {s.TvEyeY:0.00}, the host's eye at {at}");
                if (clear && (s.TvGrade > 0.5f) != hostUnder) st.Fail($"{g.Label}'s TV grade {s.TvGrade:0.00} but the host's eye {(hostUnder ? "is" : "is not")} under at {at}");
            }
        }

        private static void Compare(Guest g, S s, Stats st, int hostId)
        {
            st.Replies++;
            NoteMoments(s, st);
            if (s.Car)
            {
                if (!s.Driven || s.Torn) st.Torn++;
                else
                {
                    int code = HostCar(s.Serial, s.CarTick, out S a, out S b, out float f, out double lastTick);
                    if (code == 1 && Day.Elevator.Serial != s.Serial)
                    {
                        // The host has left this phase: the guest still shows it (the change on the wire).
                        float lag = (float)((s.CarTick - lastTick) / Math.Max(1, s.Rate));
                        st.Stale++;
                        st.MaxLag = Mathf.Max(st.MaxLag, lag);
                    }
                    else if (code == 1)
                    {
                        // Same phase, the guest's tick a little past the host's last recorded frame.
                        if (s.CarTick - lastTick <= EarlyTicks) { st.Car++; CompareCar(s, a, a, 0f, st); }
                        else st.Unmatched++;
                    }
                    else if (code == 3 || (code == 2 && b.CarTick - s.CarTick > EarlyTicks)) st.Unmatched++;
                    else { st.Car++; CompareCar(s, a, b, code == 2 ? 1f : f, st); }
                }
            }
            if (s.Ship) CompareDeck(s, st);
            CompareEyes(g, s, st, hostId);
            double hostTick = double.NaN, hostGap = 0;
            bool haveHostTick = s.Tick > 0 && HostTickAtWall(s.Wall, out hostTick, out hostGap);
            if (haveHostTick && hostGap > 0.1)
            {
                // The host's two frames around the guest's moment are more than 0.1 s apart (an
                // editor stall): the host's tick is not linear across a stall (it catches up in
                // bursts), so the skew is not measurable there (round 1 fix to the test).
                st.SkewHostStall++;
            }
            else if (haveHostTick)
            {
                double skewNow = s.Tick - hostTick;
                if (st.Skews.Count == 0 || Math.Abs(skewNow) > st.Skews.Max(x => Math.Abs(x))) st.WorstSkewAt = $"{skewNow:0.00} ticks at {At(s)} (reply {st.Replies})";
                st.Skews.Add(skewNow);
            }
        }

        // ---- sampling a stage ---------------------------------------------------------------

        private static void ResetRecorders() => ElevatorNetProbe.ResetRecorder();
        private static IEnumerator ResetGuests() { foreach (Guest g in crew.ToList()) yield return Send(g, Json("elev_reset")); }

        // Snapshots from every connected guest, compared with the host at each guest's own
        // tick, until `done` has held for `tail` seconds (or `seconds` run out: a FAIL).
        private static IEnumerator SampleStage(string label, Func<bool> done, float seconds, Func<IEnumerator> between = null, float tail = 1.5f)
        {
            int hostId = Host().OwnerId;
            var stats = new Dictionary<Guest, Stats>();
            float deadline = Time.unscaledTime + seconds, doneAt = -1f;
            var replies = new Dictionary<Guest, string>();
            // A guest's sample is compared only once the host has recorded frames past the
            // guest's tick (the guest's tick often runs a tick or two ahead of the host's
            // last LateUpdate: comparing it with the host's last frame would compare two
            // different moments). Round 1 fix to the test (net tester, 28 Sep 2026).
            var pending = new List<(Guest g, S s, Stats st)>();
            void ComparePending(bool final)
            {
                IReadOnlyList<S> rec = ElevatorNetProbe.Recorded;
                double hostLast = rec.Count > 0 ? rec[rec.Count - 1].Tick : double.NegativeInfinity;
                for (int i = 0; i < pending.Count; i++)
                {
                    (Guest g, S s, Stats st) p = pending[i];
                    double need = Math.Max(p.s.Tick, p.s.CarTick) + SkewLimitTicks;
                    if (!final && hostLast < need) continue;
                    Compare(p.g, p.s, p.st, hostId);
                    pending.RemoveAt(i--);
                }
            }
            while (Time.unscaledTime < deadline)
            {
                if (doneAt < 0f && done()) doneAt = Time.unscaledTime;
                if (doneAt >= 0f && Time.unscaledTime >= doneAt + tail) break;
                List<Guest> live = crew.Where(Alive).ToList();
                replies.Clear();
                if (live.Count > 0) yield return Snap(live, replies);
                else yield return null;
                foreach (KeyValuePair<Guest, string> kv in replies)
                {
                    if (!stats.TryGetValue(kv.Key, out Stats st)) stats[kv.Key] = st = new Stats { Label = label + " " + kv.Key.Label };
                    st.LastReply = kv.Value;
                    if (!ElevatorNetProbe.TryParse(kv.Value, out S s)) { st.NoLine++; continue; }
                    st.Last = s; st.HaveLast = true;
                    pending.Add((kv.Key, s, st));
                }
                ComparePending(false);
                if (between != null) yield return between();
            }
            yield return Wait(0.25f); // let the host record past the last replies
            ComparePending(true);
            Check(doneAt >= 0f, $"{label}: the stage completed within {seconds:0} s");
            lastStats = stats;
        }

        // The stage's verdict per guest: every comparison at the tick within tolerance, enough
        // of them, the ride's moments covered, the guest's own per-frame self-check clean.
        private static void Judge(string label, IEnumerable<Guest> gs, string[] required, string[] wanted, int minCar = 8)
        {
            foreach (Guest g in gs)
            {
                if (!lastStats.TryGetValue(g, out Stats st)) { Check(false, $"{label}: {g.Label} answered no snapshot"); continue; }
                double skew = st.Skews.Count == 0 ? 0 : st.Skews.Max(x => Math.Abs(x));
                double median = st.Skews.Count == 0 ? 0 : st.Skews.OrderBy(x => x).ElementAt(st.Skews.Count / 2);
                Say($"{st.Label}: {st.Replies} replies, {st.Car} car + {st.Deck} deck comparisons at the tick ({st.DeckInFlight} deck in flight), {st.Eye} spectator, {st.TvRows} TV; " +
                    $"worst carY {st.WorstY * 100f:0.0} cm, water {st.WorstWater * 100f:0.0} cm, gauge {st.WorstGauge:0.000}, door {st.WorstDoor:0.000}, gate {st.WorstGate:0.000}, deck doors {st.WorstDeckDoors:0.000}, yaw {st.WorstYaw:0.00}°, spectator eye {st.WorstEye:0.00} m, TV eye {st.WorstTvEye:0.00} m; " +
                    $"stale {st.Stale} (worst {st.MaxLag:0.00} s), unmatched {st.Unmatched}, torn {st.Torn}, no line {st.NoLine}, gate box one frame behind at the edge {st.GateEdge}; tick skew median {median:0.00}, worst {skew:0.00} ticks; moments {string.Join(",", st.Moments.OrderBy(m => m))}");
                Say($"{st.Label} last recorder: {Line(st.LastReply, "elevnetrec: ")}");
                Check(st.NoLine == 0, $"{label}: {g.Label}'s every reply carries the elevnet line ({st.NoLine} without)");
                Check(st.FailCount == 0, $"{label}: {g.Label} shows what the host shows at the same tick ({st.FailCount} differences: {string.Join(" | ", st.Fails)})");
                if (minCar > 0) Check(st.Car >= minCar, $"{label}: {g.Label} was compared at {st.Car} car ticks (at least {minCar})");
                foreach (string m in required) Check(st.Moments.Contains(m), $"{label}: {g.Label} was sampled at '{m}' (saw {string.Join(",", st.Moments.OrderBy(x => x))})");
                foreach (string m in wanted) Soft(st.Moments.Contains(m), $"{label}: {g.Label} was sampled at '{m}' (saw {string.Join(",", st.Moments.OrderBy(x => x))})");
                Soft(st.MaxLag <= LagLimit, $"{label}: {g.Label} showed a phase the host had left for at most {LagLimit} s (worst {st.MaxLag:0.00} s over {st.Stale} replies)");
                Soft(skew <= SkewLimitTicks, $"{label}: {g.Label}'s tick within {SkewLimitTicks} ticks of the host's at the same moment (worst {skew:0.00}, median {median:0.00}; worst {st.WorstSkewAt}; {st.SkewHostStall} replies skipped across a host stall over 0.1 s)");
                Soft(st.Unmatched <= Math.Max(2, st.Replies / 10), $"{label}: {g.Label}'s car replies found the host's frames at their tick ({st.Unmatched} of {st.Replies} did not)");
                Soft(st.ScreenSoft == 0, $"{label}: {g.Label}'s deck screen read the host's words ({st.ScreenSoft} replies differed; a refusal shows by each peer's own clock)");
                JudgeRecorder(label + ": " + g.Label, st.LastReply);
            }
            JudgeHost(label);
        }

        // A guest's own per-frame self-check since its last elev_reset (its elevnetrec line).
        private static void JudgeRecorder(string label, string reply)
        {
            float frames = Rec(reply, "frames"), worstWater = Rec(reply, "worstWater"), worstGauge = Rec(reply, "worstGauge"), worstGate = Rec(reply, "worstGate"),
                  worstShut = Rec(reply, "worstDoorShut"), worstShutter = Rec(reply, "worstShutter"), mismatch = Rec(reply, "shutterBlockMismatch"),
                  firstLoads = Rec(reply, "firstLoads"), firstErr = Rec(reply, "firstErr"), firstShips = Rec(reply, "firstShips"), firstShipErr = Rec(reply, "firstShipErr"),
                  worstY = Rec(reply, "worstY"), undriven = Rec(reply, "undriven"), torn = Rec(reply, "torn");
            Say($"{label} per-frame: {frames} frames, water {worstWater}, gauge {worstGauge}, gate {worstGate}, door while moving/at the top {worstShut}, shutters {worstShutter}°, shutter box mismatches {mismatch}, " +
                $"first car frames {firstLoads} ({RecText(reply, "firstStates")}) worst {firstErr}, first ship frames {firstShips} worst {firstShipErr}°; carY vs phase {worstY}, undriven {undriven}, torn {torn}");
            Check(frames > 0, $"{label}: the per-frame self-check ran ({frames} frames)");
            Check(worstWater <= 0.002f && worstGauge <= 0.01f && worstGate <= TolGate && worstShut <= 0.001f, $"{label}: every frame, the water is the level formula ({worstWater}), the gauge the level ({worstGauge}), the gate the door ({worstGate}), the doors shut while moving ({worstShut})");
            Check(worstShutter <= 0.5f && mismatch == 0, $"{label}: every frame, the shutters follow the deck doors or stay shut ({worstShutter}°), their box with them ({mismatch} mismatches)");
            Check(firstErr <= 0.1f && firstShipErr <= 0.5f, $"{label}: the first frame after a load shows the state at once, nothing replayed (car {firstErr}, ship {firstShipErr}°)");
            Soft(worstY <= 0.1f, $"{label}: every frame the car stands where the phase puts it ({worstY} m)");
        }

        // The host's own probe: its self-check, and the one-truth rule on its own eye
        // (MAP §4: the eye under the car's visible surface exactly when under sea level,
        // and the grade on the host's camera agreeing away from the blend band).
        private static void JudgeHost(string label)
        {
            int under = 0, grade = 0, frames = 0;
            foreach (S h in ElevatorNetProbe.Recorded)
            {
                if (!h.Local || !h.Car || !h.Driven || h.Torn || h.Dead || !h.InCar) continue;
                frames++;
                if (Mathf.Abs(h.EyeY - h.SurfaceY) > 0.03f && h.EyeUnder != (h.EyeY < h.SurfaceY) && h.Water > 0.02f && h.Water < h.Span - 0.02f) under++;
                if (Mathf.Abs(h.EyeY - h.Sea) > SurfaceBand && (h.View > 0.5f) != h.EyeUnder) grade++;
            }
            Say($"{label}: host per-frame: {ElevatorNetProbe.RecorderLine()}; eye-in-car frames {frames}, submersion vs visible surface {under}, grade vs submersion {grade}");
            Check(ElevatorNetProbe.WorstWater <= 0.002f && ElevatorNetProbe.WorstGauge <= 0.01f && ElevatorNetProbe.WorstGate <= TolGate && ElevatorNetProbe.WorstDoorShut <= 0.001f && ElevatorNetProbe.WorstShutter <= 0.5f && ElevatorNetProbe.ShutterBlockMismatch == 0,
                $"{label}: the host's own frames: water, gauge, gate, doors and shutters follow the phase");
            Check(under == 0 && grade == 0, $"{label}: on the host, the eye's submersion is the visible surface ({under}) and the grade agrees ({grade}) in every frame with the eye in the car");
        }

        // ---- the ride's clock (the host's phase log) ----------------------------------------

        private static float profileSeconds = float.NaN;
        private static void CheckTravel(string label, ElevatorState moving, ElevatorState arrive)
        {
            IReadOnlyList<ElevatorPhase> log = ElevatorNetProbe.PhaseLog;
            int i = -1;
            for (int k = log.Count - 1; k > 0; k--) if (log[k].State == arrive && log[k - 1].State == moving) { i = k; break; }
            Check(i > 0, $"{label}: the host logged {moving} then {arrive} ({string.Join(",", log.Select(p => p.Serial + ":" + p.State))})");
            // The site may already have unloaded (the last diver up): use the profile's time
            // read while the car stood in a scene (round 1 fix to the test).
            ElevatorController car = WorldSceneFlow.FindCar();
            if (car != null) profileSeconds = car.TravelSecondsOneWay;
            int rate = FishNet.InstanceFinder.TimeManager.TickRate;
            double seconds = (log[i].StartTick - log[i - 1].StartTick) / (double)rate;
            float expected = profileSeconds;
            Check(!float.IsNaN(expected) && Math.Abs(seconds - expected) <= 0.5, $"{label}: the car travelled {seconds:0.00} s by the server's ticks (the profile's {expected:0.00} s ± 0.5)");
        }

        // ---- places -----------------------------------------------------------------------

        private static ShipParts Sea() => ShipParts.InWorld(WorldId.Sea);
        private static Vector3 CabinFloorUp => Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f);
        private static Vector3 CarFloorUp => Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f);
        private static Vector3 Doorway(ElevatorController car) => car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
        private static float CarDoor() { ElevatorController c = WorldSceneFlow.FindCar(); ElevatorDoor d = c != null ? c.GetComponentInChildren<ElevatorDoor>(true) : null; return d != null ? d.OpenFraction : -1f; }
        private static CabinWater HostWater() { ElevatorController c = WorldSceneFlow.FindCar(); return c != null ? c.GetComponent<CabinWater>() : null; }

        // A standing spot on whatever is solid under `at` (the sill, the ramp, the sand), not a player.
        private static Vector3 Stand(Vector3 at)
        {
            Vector3 from = new(at.x, at.y + 1.5f, at.z);
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (hit.collider.GetComponentInParent<HQPlayerController>() == null) return hit.point + Vector3.up * 0.05f;
            return at;
        }

        private static IEnumerator IntoDeckCabin(IList<Guest> gs, string label)
        {
            ShipParts sea = Sea();
            Check(sea != null && sea.DeckCabin != null, label + ": the ship at sea and its deck cabin");
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3[] offsets = { sea.DeckCabin.right * 1.0f, -sea.DeckCabin.right * 1.0f, -sea.DeckCabin.forward * 1.0f };
            for (int i = 0; i < gs.Count; i++) yield return GuestMove(gs[i], sea.DeckCabin.position + offsets[i] + CabinFloorUp);
            yield return Wait(0.5f);
            yield return Expect(() => sea.IsInDeckCabin(Host().transform.position) && gs.All(g => CopyOf(g) != null && sea.IsInDeckCabin(CopyOf(g).transform.position)), 6f,
                () => $"{label}: the host and {string.Join(", ", gs.Select(g => g.Label))} stand in the deck cabin ({string.Join(", ", gs.Select(g => g.Label + " " + (CopyOf(g) == null ? "gone" : sea.ToShipLocal(CopyOf(g).transform.position).ToString("F1"))))})");
        }

        private static IEnumerator IntoCar(IList<Guest> gs, string label)
        {
            ElevatorController car = WorldSceneFlow.FindCar();
            Check(car != null && Day.Elevator.State == ElevatorState.AtBottom, label + ": the car waits at the bottom");
            Vector3 doorway = Doorway(car), side = Vector3.Cross(Vector3.up, doorway);
            H.MoveLocalIntoCar();
            Vector3[] offsets = { side * 1.0f, -side * 1.0f, -doorway * 1.0f };
            for (int i = 0; i < gs.Count; i++) yield return GuestMove(gs[i], car.transform.position + offsets[i] + CarFloorUp);
            yield return Wait(0.6f);
            yield return Expect(() => car.IsInsideCar(Host().transform.position + Vector3.up * 0.5f) && gs.All(g => CopyOf(g) != null && car.IsInsideCar(CopyOf(g).transform.position + Vector3.up * 0.5f)), 6f,
                () => $"{label}: the host and {string.Join(", ", gs.Select(g => g.Label))} stand in the car");
        }

        private static Vector3 TvSpot(ShipParts sea)
        {
            Vector3 tvLocal = sea.ToShipLocal(sea.TvScreen.position);
            return sea.FromShipLocal(new Vector3(tvLocal.x, 0.05f, tvLocal.z - 4f)); // in front of the couches, within the TV's reach
        }

        // Every living player in the car and below, no two too close (NT6).
        private static void CheckAllInCar(string label, IList<Guest> gs)
        {
            ElevatorController car = WorldSceneFlow.FindCar();
            var bodies = new List<HQPlayerController> { Host() };
            bodies.AddRange(gs.Select(CopyOf));
            Check(car != null && bodies.All(p => p != null && car.IsInsideCar(p.transform.position + Vector3.up * 0.5f)), $"{label}: every player stands inside the car on the host");
            Check(bodies.All(p => Day.IsBelow(p.OwnerId)) && Day.Below.Count == bodies.Count, $"{label}: all {bodies.Count} listed below ({string.Join(",", Day.Below)})");
            float closest = float.PositiveInfinity;
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    Vector3 d = bodies[i].transform.position - bodies[j].transform.position; d.y = 0f;
                    closest = Mathf.Min(closest, d.magnitude);
                }
            Check(closest >= 0.55f, $"{label}: no two riders overlap (closest {closest:0.00} m)");
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            WorldSceneFlow flow = Flow;
            float grace = flow.Settings.CarReturnGraceSeconds;
            string me = WorldSceneFlow.DisplayName(host.OwnerId);

            // ================================ E0 ================================
            Heading("E0 — setup: the host sails to sea; guests A and B (windowed) join between days; the deck cabin at rest reads the same on every peer");
            hostProbe = new GameObject("Elevator Net Probe (host)");
            UnityEngine.Object.DontDestroyOnLoad(hostProbe);
            hostProbe.AddComponent<ElevatorNetProbe>();
            ElevatorNetProbe.StartRecording();
            if (Day.World != WorldId.Sea)
            {
                H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
                string sail = H.ServerSail("Sea");
                Check(sail.StartsWith("sailing"), "E0 sailing to sea: " + sail);
                yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 60f, () => "E0 arrived at sea (" + Day.Departure.Stage + ")");
            }
            yield return Expect(() => WorldSceneFlow.LocalRider() == null || !WorldSceneFlow.LocalRider().Locked, 8f, () => "E0 controls back");
            Check(Day.Phase == DayPhase.AtSea && Day.Elevator.State == ElevatorState.AtTop && !Day.CabinRide.Active, $"E0 at sea between days, the car up (phase {Day.Phase}, car {Day.Elevator.State})");
            Say($"host '{me}' (client {host.OwnerId}); tick rate {FishNet.InstanceFinder.TimeManager.TickRate}; car return grace {grace} s");
            Guest A = Launch("A", "Temp/elevator-net-guest-a", headless: false);
            yield return Joined(A, "E0 A");
            Guest B = Launch("B", "Temp/elevator-net-guest-b", headless: false);
            yield return Joined(B, "E0 B");
            ResetRecorders(); yield return ResetGuests();
            yield return SampleStage("E0 at rest", () => true, 20f, tail: 3f);
            Judge("E0 at rest", crew, new[] { "shutters-open" }, Array.Empty<string>(), minCar: 0);
            foreach (Guest g in crew) Check(lastStats[g].HaveLast && lastStats[g].Last.Ship && lastStats[g].Last.CarShown && !lastStats[g].Last.ShutterBlocks && lastStats[g].Last.DeckDoors > 0.99f, $"E0 {g.Label}: the car on the deck, doors and shutters open");

            // ================================ E1 ================================
            Heading("E1 — 3 players ride down together (NT1, NT6 3p): car, doors, gate, deck doors and shutters, the flood and the gauge the same at the tick on every peer");
            yield return IntoDeckCabin(new[] { A, B }, "E1");
            ResetRecorders(); yield return ResetGuests();
            int serial = Day.CabinRide.Serial;
            Say("E1 " + H.ClientRequestCabin());
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "E1 the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return SampleStage("E1 down", () => !Day.CabinRide.Active && Day.Elevator.State == ElevatorState.AtBottom && CarDoor() >= 0.999f, 100f);
            Check(Day.CabinRide.Stage == CabinRideStage.Complete, "E1 the ride down completed: " + H.RideStatus());
            CheckTravel("E1", ElevatorState.Descending, ElevatorState.AtBottom);
            Judge("E1 down", crew, new[] { "moving-dry", "flooding", "moving-full", "bottom-open" }, new[] { "deck-sealing", "top-shut", "bottom-opening" });
            CheckAllInCar("E1", crew);

            // ================================ E2 ================================
            Heading("E2 — B rides up alone; the host and A stay below and watch the car drain away (NT1 on the way up)");
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 doorway = Doorway(car), side = Vector3.Cross(Vector3.up, doorway);
            host.TeleportLocal(Stand(car.transform.position + doorway * 4.5f - side * 1.5f), host.Yaw);
            yield return GuestMove(A, Stand(car.transform.position + doorway * 4.5f + side * 1.5f));
            yield return GuestMove(B, car.transform.position + CarFloorUp);
            yield return Wait(0.8f);
            Check(!car.IsInsideCar(host.transform.position + Vector3.up * 0.5f) && CopyOf(A) != null && !car.IsInsideCar(CopyOf(A).transform.position + Vector3.up * 0.5f) && CopyOf(B) != null && car.IsInsideCar(CopyOf(B).transform.position + Vector3.up * 0.5f), "E2 the host and A outside the car, B inside");
            ResetRecorders(); yield return ResetGuests();
            serial = Day.CabinRide.Serial;
            yield return Send(B, Json("car"));
            yield return Expect(() => Day.CabinRide.Serial > serial, 5f, () => "E2 B's car press was taken (refusal: " + Day.LastRefusal.Text + ")");
            yield return SampleStage("E2 B up", () => !Day.CabinRide.Active && Day.CabinRide.Serial > serial, 110f);
            Check(Day.Below.Count == 2 && Day.IsBelow(host.OwnerId) && Day.IsBelow(A.Id) && !Day.IsBelow(B.Id), "E2 the host and A below, B up: " + string.Join(",", Day.Below));
            CheckTravel("E2", ElevatorState.Ascending, ElevatorState.AtTop);
            Judge("E2 B up", new[] { A }, new[] { "moving-full", "draining", "moving-dry" }, new[] { "bottom-closing" });
            Judge("E2 B up", new[] { B }, new[] { "moving-full", "draining" }, new[] { "bottom-closing", "deck-opening" }, minCar: 4);

            // ================================ E3 ================================
            Heading("E3 — the empty car goes back down for the host and A; B on the ship sees the shutters shut and the TV go live on the host");
            ShipParts sea = Sea();
            Vector3 tvSpot = TvSpot(sea);
            yield return GuestMove(B, tvSpot);
            yield return GuestLook(B, sea.TvScreen.position - (tvSpot + Vector3.up * 1.6f));
            B.Role = Role.Tv;
            ResetRecorders(); yield return ResetGuests();
            yield return Expect(() => Day.Elevator.State == ElevatorState.Sealing || Day.Elevator.State == ElevatorState.Descending, grace + 15f, () => "E3 the cabin is clear: the car seals to go back down (" + Day.Elevator.State + ")");
            yield return SampleStage("E3 empty car down", () => Day.Elevator.State == ElevatorState.AtBottom && CarDoor() >= 0.999f, 60f);
            CheckTravel("E3", ElevatorState.Descending, ElevatorState.AtBottom);
            Judge("E3 empty car down", new[] { A }, new[] { "moving-dry", "flooding", "moving-full", "bottom-open" }, Array.Empty<string>());
            Judge("E3 empty car down", new[] { B }, new[] { "shutters-shut" }, new[] { "flooding", "moving-full" }, minCar: 0);
            Say($"E3 B's car frames: {lastStats[B].Car} compared (B holds the dive world for the TV); B's first car frames: {RecText(lastStats[B].LastReply, "firstLoads")} ({RecText(lastStats[B].LastReply, "firstStates")})");
            yield return Expect(() => Day.TvChannel == host.OwnerId, 10f, () => "E3 the TV's channel is the host, the first diver below (" + Day.TvChannel + ")");
            yield return GuestEventually(B, r => r.Contains("tvLive=True") && r.Contains("tvCaption=LIVE · ") && r.Contains("tv=" + host.OwnerId + ";"), 10f, "E3 B's TV is live on the host (" + me + ")");
            Say("E3 B's TV: " + Rx(Header(lastReply), @"(tv=[^;]*; tvLive=[^;]*; tvCaption=[^;]*)"));

            // ================================ E4 ================================
            Heading("E4 — A dies below and spectates the host; B watches the TV; the full car at the bottom (X3: no TV during a ride down); the host rides up alone: the drain on A's view and on the TV (NT2, NT3)");
            yield return Send(A, Json("die"));
            yield return Expect(() => Day.IsDead(A.Id), 5f, () => "E4 A died below");
            for (int i = 0; i < 3 && Day.SpectateTargetOf(A.Id) != host.OwnerId; i++) { yield return Wait(1.5f); if (Day.SpectateTargetOf(A.Id) != host.OwnerId) yield return Send(A, Json("spectate_next")); }
            yield return Expect(() => Day.SpectateTargetOf(A.Id) == host.OwnerId, 4f, () => "E4 dead A watches the host (" + Day.SpectateTargetOf(A.Id) + ")");
            yield return GuestEventually(A, r => PlayerLine(r, A.Id).Contains("spectatorActive=True") && PlayerLine(r, A.Id).Contains("spectatorTarget=" + host.OwnerId + ";"), 8f, "E4 A's spectator view is on the host");
            A.Role = Role.Spectator;
            H.MoveLocalIntoCar();
            yield return Wait(1.2f);
            Check(HostWater() != null && HostWater().LevelMeters >= HostWater().SpanMeters - 0.02f && host.GetComponent<PlayerSubmersion>().IsSubmerged, "E4 the host stands in the full car at the bottom, under water");
            string tvFull = Path.GetFullPath(Captures + "E4-tv-full-car.png"), aFull = Path.GetFullPath(Captures + "E4-spectator-full-car.png"), hostFull = Path.GetFullPath(Captures + "E4-host-full-car.png");
            foreach (string p in new[] { tvFull, aFull, hostFull }) if (File.Exists(p)) File.Delete(p);
            Say("E4 " + H.CaptureScreen(hostFull));
            yield return Send(B, Json("capture_tv", tvFull));
            string tvReply = lastReply.Split('\n')[0];
            yield return Send(A, Json("capture_play", aFull));
            yield return Expect(() => File.Exists(tvFull) && File.Exists(aFull), 5f, () => "E4 the TV picture and A's screen at the bottom saved (" + tvReply + ")");
            Check(tvReply.Contains("live=True"), "E4 B's TV is live on the host in the full car: " + tvReply);
            ResetRecorders(); yield return ResetGuests();
            yield return SampleStage("E4 at the bottom", () => true, 20f, tail: 3f);
            Judge("E4 at the bottom", crew, Array.Empty<string>(), Array.Empty<string>(), minCar: 3);
            Check(lastStats[A].Eye >= 3 && lastStats[B].TvRows >= 2, $"E4 A's view ({lastStats[A].Eye}) and B's TV ({lastStats[B].TvRows}) were compared with the host's eye in the full car");

            ResetRecorders(); yield return ResetGuests();
            serial = Day.CabinRide.Serial;
            bool captured = false;
            string tvDrain = Path.GetFullPath(Captures + "E4-tv-draining.png"), aDrain = Path.GetFullPath(Captures + "E4-spectator-draining.png"), hostDrain = Path.GetFullPath(Captures + "E4-host-draining.png");
            IEnumerator MidDrain()
            {
                CabinWater w = HostWater();
                if (captured || w == null || w.Flow != CarWaterFlow.Draining || w.LevelMeters > 2.8f || w.LevelMeters < 0.8f) yield break;
                captured = true;
                Say($"E4 mid-drain captures at level {w.LevelMeters:0.00}: " + H.CaptureScreen(hostDrain));
                int cb = Command(B, Json("capture_tv", tvDrain)), ca = Command(A, Json("capture_play", aDrain));
                yield return AwaitReply(B, cb);
                Say("E4 B: " + lastReply.Split('\n')[0]);
                yield return AwaitReply(A, ca);
            }
            Say("E4 " + H.ClientRequestCar());
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "E4 the car took the host's press (refusal: " + Day.LastRefusal.Text + ")");
            yield return SampleStage("E4 host up", () => !Day.CabinRide.Active && Day.CabinRide.Serial > serial, 110f, MidDrain);
            CheckTravel("E4", ElevatorState.Ascending, ElevatorState.AtTop);
            Judge("E4 host up", new[] { A }, new[] { "moving-full", "draining" }, new[] { "moving-dry", "bottom-closing" });
            Judge("E4 host up", new[] { B }, new[] { "draining" }, new[] { "moving-full" }, minCar: 4);
            Check(lastStats[A].Eye >= 8, $"E4 A's spectator view compared with the host's eye through the ride ({lastStats[A].Eye} replies)");
            Check(lastStats[B].TvRows >= 6, $"E4 B's TV compared with the host's eye through the ride ({lastStats[B].TvRows} replies)");
            Check(captured, "E4 the mid-drain captures were taken");
            yield return Expect(() => File.Exists(tvDrain) && File.Exists(aDrain) && File.Exists(hostDrain), 5f, () => "E4 the mid-drain pictures saved: " + Captures);
            Say($"E4 captures to Read: {hostFull}, {aFull}, {tvFull}, {hostDrain}, {aDrain}, {tvDrain}");
            yield return Expect(() => Day.Below.Count == 0 && Day.DiveDone, 10f, () => "E4 the last living diver is up: the dive is done");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 40f, () => "E4 the site unloaded");
            yield return Expect(() => CopyOf(A) != null && CopyOf(A).gameObject.scene == WorldScenes.Scene(WorldId.Sea), 15f, () => "E4 dead A was carried to the ship");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "E4 End day accepted: " + endWhy);
            yield return Expect(() => !Day.IsDead(A.Id), 5f, () => "E4 A revived");
            yield return GuestEventually(A, r => PlayerLine(r, A.Id).Contains("dead=False") && PlayerLine(r, A.Id).Contains("controllerOn=True") && PlayerLine(r, A.Id).Contains("spectatorActive=False"), 12f, "E4 A's client reads itself alive with the capsule on");
            A.Role = Role.Rider; B.Role = Role.Rider;

            // ================================ E5 ================================
            Heading("E5 — day 2: 3 players ride down; guest C is launched at the press and refused (X1: joins are refused during a ride and a dive); the ride is unaffected (NT4a)");
            yield return IntoDeckCabin(new[] { A, B }, "E5");
            ResetRecorders(); yield return ResetGuests();
            serial = Day.CabinRide.Serial;
            int refusalsBefore = Count("refused: DiveInProgress");
            int duringBefore = admissionRefusalDuringRide.Count;
            Say("E5 " + H.ClientRequestCabin());
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "E5 the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            Guest C = Launch("C", "Temp/elevator-net-guest-c", headless: true); // not crew: it must never join
            int most = SpawnedPlayers();
            IEnumerator Watch() { most = Math.Max(most, SpawnedPlayers()); yield break; }
            yield return SampleStage("E5 down", () => !Day.CabinRide.Active && Day.Elevator.State == ElevatorState.AtBottom && CarDoor() >= 0.999f, 100f, Watch);
            CheckTravel("E5", ElevatorState.Descending, ElevatorState.AtBottom);
            Judge("E5 down", crew, new[] { "moving-dry", "flooding", "moving-full", "bottom-open" }, new[] { "deck-sealing" });
            string cHeader = string.Empty, cMessage = string.Empty;
            float cDeadline = Time.unscaledTime + 40f;
            while (Time.unscaledTime < cDeadline)
            {
                most = Math.Max(most, SpawnedPlayers());
                int cc = Command(C, Json("snapshot"));
                float until = Time.unscaledTime + 3f;
                while (Time.unscaledTime < until && !Reply(C).StartsWith("id=" + cc + ";")) yield return null;
                string r = Reply(C);
                if (r.StartsWith("id=" + cc + ";"))
                {
                    cHeader = Header(r);
                    cMessage = Rx(cHeader, @"; message=(.*?); monitor=");
                    if (cHeader.Contains("client=False") && cMessage.Length > 0 && cMessage != "(absent)") break;
                }
                yield return Wait(1f);
            }
            int refusedNow = Count("refused: DiveInProgress") - refusalsBefore;
            bool duringRide = admissionRefusalDuringRide.Skip(duringBefore).Any(x => x);
            Say($"E5 C: message '{cMessage}'; host refusals (DiveInProgress) +{refusedNow}, {(duringRide ? "one landed while the ride ran" : "none landed while the ride ran (the dive was in progress)")}; most players on the host {most}");
            Check(cHeader.Contains("client=False") && cMessage.StartsWith("Dive in progress") && refusedNow >= 1, "E5 C refused: '" + cMessage + "'");
            Check(most == 3 && SpawnedPlayers() == 3, "E5 C never spawned (most " + most + ")");
            Soft(duringRide, "E5 C's refusal landed while the ride itself ran (else only while the dive was in progress)");
            try { if (!C.Process.HasExited) C.Process.Kill(); } catch (Exception) { }
            CheckAllInCar("E5", crew);

            // ================================ E6 ================================
            Heading("E6 — 3 players ride up; B's process is killed mid-drain: the ride completes, A stays consistent with the host, no exception (NT5)");
            yield return IntoCar(new[] { A, B }, "E6");
            ResetRecorders(); yield return ResetGuests();
            serial = Day.CabinRide.Serial;
            int exceptionsBefore = exceptionsSeen;
            bool killed = false; float killedAt = -1f;
            IEnumerator KillMidDrain()
            {
                CabinWater w = HostWater();
                if (killed || w == null || w.Flow != CarWaterFlow.Draining || w.LevelMeters > 3.0f) yield break;
                killed = true; killedAt = Time.unscaledTime;
                try { B.Process.Kill(); } catch (Exception e) { Say("E6 kill: " + e.Message); }
                crew.Remove(B);
                Say($"E6 B's process killed at level {w.LevelMeters:0.00} ({Day.Elevator.State}, ride {Day.CabinRide.Stage})");
            }
            Say("E6 " + H.ClientRequestCar());
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "E6 the car took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return SampleStage("E6 up, B killed", () => !Day.CabinRide.Active && Day.CabinRide.Serial > serial, 160f, KillMidDrain);
            Check(killed, "E6 B was killed mid-drain");
            Say($"E6 the ride ended {Time.unscaledTime - killedAt:0.0} s after the kill; stage {Day.CabinRide.Stage}");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete, "E6 the ride up completed for the others: " + H.RideStatus());
            CheckTravel("E6", ElevatorState.Ascending, ElevatorState.AtTop);
            Judge("E6 up, B killed", new[] { A }, new[] { "moving-full", "draining" }, new[] { "moving-dry", "deck-opening" });
            yield return Expect(() => CopyOf(B) == null, 70f, () => "E6 B's player is gone");
            Say($"E6 B dropped {Time.unscaledTime - killedAt:0.0} s after the kill");
            Check(!Day.IsBelow(B.Id) && !Day.IsRider(B.Id), "E6 B left Below and the riders");
            Check(exceptionsSeen == exceptionsBefore, "E6 no exception on the host through the kill: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]"))));
            ResetRecorders(); yield return ResetGuests();
            yield return SampleStage("E6 arrived", () => true, 20f, tail: 3f);
            Judge("E6 arrived", crew, new[] { "shutters-open" }, Array.Empty<string>(), minCar: 0);
            yield return Expect(() => Day.Below.Count == 0 && Day.DiveDone, 10f, () => "E6 nobody below: the dive is done");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 40f, () => "E6 the site unloaded");
            Check(flow.ServerEndDay(host.Owner, out string endWhy2), "E6 End day accepted: " + endWhy2);
            yield return Wait(1f);

            // ================================ E7 ================================
            Heading("E7 — day 3: B2 and C2 (headless) join between days: their first reply is the host's deck state at once (NT4b); 4 players ride down (NT6 4p)");
            Guest B2 = Launch("B2", "Temp/elevator-net-guest-b2", headless: true);
            yield return Joined(B2, "E7 B2");
            string firstB2 = lastReply;
            yield return Wait(0.3f); // the host records past the reply's tick before the comparison
            JudgeFirstReply(B2, firstB2, "E7 B2");
            Guest C2 = Launch("C2", "Temp/elevator-net-guest-c2", headless: true);
            yield return Joined(C2, "E7 C2");
            string firstC2 = lastReply;
            yield return Wait(0.3f); // the host records past the reply's tick before the comparison
            JudgeFirstReply(C2, firstC2, "E7 C2");
            Check(SpawnedPlayers() == 4 && crew.Count == 3, "E7 four players: " + SpawnedPlayers());
            yield return IntoDeckCabin(new[] { A, B2, C2 }, "E7");
            ResetRecorders(); yield return ResetGuests();
            yield return Send(A, Json("frames_reset"));
            serial = Day.CabinRide.Serial;
            Say("E7 " + H.ClientRequestCabin());
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "E7 the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return SampleStage("E7 down 4p", () => !Day.CabinRide.Active && Day.Elevator.State == ElevatorState.AtBottom && CarDoor() >= 0.999f, 100f);
            CheckTravel("E7", ElevatorState.Descending, ElevatorState.AtBottom);
            Judge("E7 down 4p", crew, new[] { "moving-dry", "flooding", "moving-full", "bottom-open" }, new[] { "deck-sealing" });
            CheckAllInCar("E7", crew);
            yield return Send(A, Json("frames"));
            string aFrames = lastReply.Split('\n')[0];
            Say("E7 A's frames (windowed, 4 bodies and the water): " + aFrames);
            Match fps = Regex.Match(aFrames, @"fps=([0-9]+)");
            int aHitches = Regex.Matches(aFrames, @"t=([0-9.]+)s ([0-9]+)ms").Count;
            Soft(fps.Success && int.Parse(fps.Groups[1].Value) >= 50 && aHitches <= 2, $"E7 A rendered at 50 fps or better with at most two hitches over the ride (fps {(fps.Success ? fps.Groups[1].Value : "?")}, hitches {aHitches}; the snapshot polling itself costs frames)");

            // ================================ E8 ================================
            Heading("E8 — 4 players ride up; A leaves mid-ascent (a clean disconnect): the others stay consistent, no exception (NT5)");
            ResetRecorders(); yield return ResetGuests();
            serial = Day.CabinRide.Serial;
            exceptionsBefore = exceptionsSeen;
            bool left = false;
            IEnumerator LeaveMidAscent()
            {
                if (left || Day.Elevator.State != ElevatorState.Ascending) yield break;
                ElevatorController c = WorldSceneFlow.FindCar();
                if (c == null || c.transform.position.y < c.BottomPosition.y + 15f) yield break;
                left = true;
                Command(A, Json("leave"));
                crew.Remove(A);
                Say($"E8 A leaves at car y {c.transform.position.y:0.0} ({Day.CabinRide.Stage})");
            }
            Say("E8 " + H.ClientRequestCar());
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "E8 the car took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return SampleStage("E8 up 4p, A leaves", () => !Day.CabinRide.Active && Day.CabinRide.Serial > serial, 160f, LeaveMidAscent);
            Check(left, "E8 A left mid-ascent");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete, "E8 the ride up completed: " + H.RideStatus());
            CheckTravel("E8", ElevatorState.Ascending, ElevatorState.AtTop);
            Judge("E8 up 4p, A leaves", crew, new[] { "moving-full", "draining", "moving-dry" }, new[] { "deck-opening" });
            yield return Expect(() => CopyOf(A) == null, 20f, () => "E8 A's player is gone");
            Check(!Day.IsBelow(A.Id) && !Day.IsRider(A.Id), "E8 A left Below and the riders");
            Check(exceptionsSeen == exceptionsBefore, "E8 no exception on the host through the leave: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]"))));
            try { if (!A.Process.HasExited) A.Process.Kill(); } catch (Exception) { }

            // ================================ E9 ================================
            Heading("E9 — the sweep: every guest leaves; no exception on the host; each guest's log is clean");
            foreach (Guest g in crew.ToList()) yield return Leave(g, "E9");
            Say($"host log: {errorsSeen} errors, {exceptionsSeen} exceptions, {logs.Count(l => l.StartsWith("[Warning]"))} warnings during the run");
            Check(exceptionsSeen == 0, "E9 no exception on the host: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]"))));
            yield return Wait(2f);
            var bad = new Regex("expected to exist|already found|Exception|MissingReference|NullReference", RegexOptions.IgnoreCase);
            foreach (Guest g in launched)
            {
                string path = Path.Combine(g.Dir, "player.log");
                string[] lines;
                try { lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); } catch (IOException) { lines = Array.Empty<string>(); }
                // A -nographics guest has no GPU: DiveSiteWarmup's Camera.Render (on main since
                // c9604d0) fails inside URP there ("RenderTexture.Create failed", a
                // NullReferenceException in a render pass). Those blocks are counted and named,
                // not failed; every other hit fails (round 1 fix to the test).
                var blocks = new List<string>(); var cur = new List<string>();
                foreach (string l in lines) { if (l.Trim().Length == 0) { if (cur.Count > 0) blocks.Add(string.Join("\n", cur)); cur.Clear(); } else cur.Add(l); }
                if (cur.Count > 0) blocks.Add(string.Join("\n", cur));
                string[] badBlocks = blocks.Where(b => bad.IsMatch(b)).ToArray();
                bool NoGpuRender(string b) => g.Headless && b.Contains("DiveSiteWarmup:RenderOnce") && (b.Contains("RenderGraph") || b.Contains("RenderTexture") || b.Contains("Rendering.Universal"));
                string[] hits = badBlocks.Where(b => !NoGpuRender(b)).ToArray();
                int noGpu = badBlocks.Length - hits.Length;
                Say($"{g.Label} player.log: {lines.Length} lines, {hits.Length} bad blocks, {noGpu} headless no-GPU warm-up render blocks" + (hits.Length > 0 ? ": " + string.Join(" / ", hits.Take(4).Select(b => b.Split('\n')[0])) : string.Empty));
                Soft(hits.Length == 0, $"E9 {g.Label}'s log has no exception, MissingReference or 'expected to exist' (besides the headless no-GPU warm-up render)");
            }
            Check(softFails.Count == 0, $"no soft failure ({softFails.Count}: {string.Join(" | ", softFails)})");
        }

        // A guest's first reply after joining between days: the deck cabin as the host shows
        // it at that tick, with no animation replayed (its first ship frame's shutters).
        private static void JudgeFirstReply(Guest g, string reply, string label)
        {
            Check(ElevatorNetProbe.TryParse(reply, out S s), label + ": the first reply carries the elevnet line");
            var st = new Stats { Label = label + " first reply" };
            Compare(g, s, st, Host().OwnerId);
            Say($"{label}: first reply: deck doors {s.DeckDoors:0.00}, shutter yaw {s.ShutterYaw:0.0}°, car shown {s.CarShown}, shutter box {s.ShutterBlocks}; {st.Deck} deck comparison(s), {st.DeckInFlight} in flight");
            Check(s.Ship && !s.Car && s.CarShown && !s.ShutterBlocks && s.DeckDoors > 0.99f, $"{label}: its first reply shows the car up and the doors and shutters open, as on the host");
            Check(st.FailCount == 0 && st.Deck + st.DeckInFlight >= 1, $"{label}: its first reply equals the host at its tick ({string.Join(" | ", st.Fails)})");
            Check(Rec(reply, "firstShips") >= 1 && Rec(reply, "firstShipErr") <= 0.5f && Rec(reply, "worstShutter") <= 0.5f, $"{label}: its first ship frame had the shutters where the state puts them, no swing replayed ({RecText(reply, "firstShipErr")}°)");
        }
    }
}
