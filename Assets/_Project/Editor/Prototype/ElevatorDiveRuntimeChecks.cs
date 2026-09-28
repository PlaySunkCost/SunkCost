using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    // The new elevator in the dive (BRIEF "TESTING REQUIREMENTS -> DIVE", 28 September 2026;
    // TESTPLAN §2.2 rows DV1-DV12 and VA-V1..V5), plus the three water fixes Dan asked for
    // the same day (DIVE-STREAMS, DIVE-BUBBLES, DIVE-GLARE: the "W" rows).
    //
    // The host alone first: to sea, down with a per-frame recorder (timing and speed bands,
    // the doors and the gate, the flood, the gauge, the underwater view against the visible
    // surface, the winch and the Elevator noise, the jets, the bubbles, the glare), then at
    // the bottom: the clearance sweep, the doors and gate together, the walk down the ramp
    // and back, the camera against every drawn mesh, the transparency order, the rider left
    // below (the empty round trip drains and floods again), the real E on the car's panel,
    // the ride up (the drain, dry before the top), End day. Then one windowed guest: down
    // together (the guest's own per-frame recorder), the guest's walk, the Elevator Ghost's
    // green car and ring on both peers, a dead rider in the car on the way up.
    //
    // Every host sample is taken once a frame by Sample() (EditorApplication.update) into
    // `samples`; the rows analyse windows of it. The guest records the same per-frame
    // numbers in its InventoryVerificationPeer (patches/01-peer-elevator-line.md) and reports
    // them on its "elevator:" snapshot line.
    //
    // Rows that need the water rework (the W rows) never throw: they are SOFT rows,
    // collected and failed at the end ("W-SOFT-FAIL"). Job "elevator-dive" fails the run on
    // them; job "elevator-dive-prefix" logs them as XFAIL (expected until the rework lands)
    // and passes on every other row. Every other finding that should not stop the run is a
    // SOFT-FAIL line and fails the run at the end (console-net pattern).
    //
    // Log: Temp/elevator-dive-matrix.log. Captures: Logs/elevator-dive/*.png (Read every one:
    // VISUAL ACCEPTANCE is a human/agent judgement, not a pixel test). Started by
    // CameraClearanceMatrixDriver.Start("elevator-dive").
    public static class ElevatorDiveRuntimeChecks
    {
        private const string Log = "Temp/elevator-dive-matrix.log";
        private const string GuestDir = "Temp/elevator-dive-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Shots = "Logs/elevator-dive/";

        // The ride's numbers that must not change (docs/DESIGN.md "The way down"; INTERFACES A1):
        // 4.5 m at 3 m/s above the surface, the 3.5 m span at 1 m/s, 37 m at 3 m/s.
        private const float ExpectedTravelSeconds = 17.333f;
        private const float DoorHalfAngleDeg = 23.578f;   // ElevatorLook / INTERFACES §1: the car leaves' sweep
        private const float SlabBearingA = 54.70f, SlabBearingB = -57.07f; // the car model's two dark glass slabs, from the doorway (ElevatorLook.Posts)

        // ---- the water rework's contract (DIVE-STREAMS / DIVE-BUBBLES / DIVE-GLARE) -----------
        // The W rows find the rework's pieces by these names under the car's "Cabin Water FX".
        // If the water fixer names them differently, change them here (one place).
        private static class WaterFix
        {
            public static readonly string[] JetPrefixes = { "Jet ", "Stream " };          // "Jet 1".."Jet 6" (or today's "Stream n")
            public static readonly string[] BubbleWords = { "Bubble" };                  // any object whose name contains it
            public static readonly string[] SprayWords = { "Spray", "Mist" };            // outlet / impact spray systems
            public static readonly string[] NotJetWords = { "Spray", "Mist", "Splash", "Foam" }; // excluded from a jet's own points
            public const float JetStartTolerance = 0.06f;   // the jet's first point within this of its outlet (3D)
            public const float OutletOnModel = 0.09f;       // the outlet (a mouth's centre, r ≈ 0.06 m) lies within this of the car model's drawn mesh
            public const float JetMinRadius = 0.08f;        // "thick, high-pressure": the jet's widest section at least 16 cm across
            public const float JetMinMouthRadius = 0.04f;   // and it fills the pipe's bore at the nozzle (bore r ≈ 0.05)
            public const float JetMaxDirectionDeg = 20f;    // it leaves along the nozzle's own outflow (OutletDirectionLocal)
            public const float JetMaxDrift = 0.8f;          // and lands within this of the nozzle, horizontally (inside the car)
            // The rework's outflow direction per nozzle (drafts/water: CabinWaterVisuals.OutletDirectionLocal(int),
            // car-root local). Read by reflection so this file compiles before the rework lands.
            public const string OutletDirectionMethod = "OutletDirectionLocal";
            public const int JetMinAzimuthBins = 8;         // of 12: a round jet, not two crossed cards (4 bins)
            public const float BubbleNearJet = 0.8f;        // a filling car's bubbles lie within this (horizontally) of a jet's landing
            public const float FizzSeconds = 4.0f;          // after full: none left (a ~2 s fizz plus the last bubbles' rise)
            public const float StillBelowFullMeters = 12f;  // descending: 12 m (4 s at 3 m/s) past full the car counts as still
            public const float MaxBlownFraction = 0.005f;   // of the frame, fully white
            public const int MaxBallArea = 1500;            // px: a compact white blob (the "blown-out ball")
        }

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 2600;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged, sampling, waterFixRequired = true;
        private static readonly List<string> softFails = new();
        private static readonly List<string> waterSoftFails = new();
        private static readonly Ears ears = new();
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run elevator dive matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Elevator dive matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        // waterFixRequired false: the W rows are logged as XFAIL and do not fail the run
        // (job "elevator-dive-prefix", before the water rework lands).
        public static void RunAsHost() => RunAsHost(true);

        public static void RunAsHost(bool requireWaterFix)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            Directory.CreateDirectory(Shots);
            File.WriteAllText(Log, "Elevator dive matrix started " + DateTime.Now + (requireWaterFix ? "" : " (water rework rows XFAIL)") + "\n");
            waterFixRequired = requireWaterFix;
            softFails.Clear(); waterSoftFails.Clear(); samples.Clear(); parts = null; meshCache.Clear();
            Status = "Running";
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            EditorApplication.update += Tick;
            EditorApplication.update += Sample;
            sampling = true;
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
                Status = Verdict();
            }
            catch (Exception e) { Status = "FAIL: " + e.Message + "\n" + e.StackTrace; }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Elevator dive matrix: MATRIX_PASS"); else Debug.LogError("Elevator dive matrix: " + Status);
            Cleanup();
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        private static string Verdict()
        {
            var failing = new List<string>(softFails);
            if (waterFixRequired) failing.AddRange(waterSoftFails);
            else if (waterSoftFails.Count > 0) File.AppendAllText(Log, "\nXFAIL (the water rework has not landed; job elevator-dive-prefix): " + waterSoftFails.Count + " W rows\n  " + string.Join("\n  ", waterSoftFails) + "\n");
            if (failing.Count == 0) return "MATRIX_PASS";
            return "FAIL: " + failing.Count + " SOFT-FAIL rows\n  " + string.Join("\n  ", failing);
        }

        private static void Cleanup()
        {
            if (sampling) { EditorApplication.update -= Sample; sampling = false; }
            try { SunkCost.Noise.NoiseSystem.Unregister(ears); } catch (Exception) { }
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            SunkCost.Net.SessionInputGate.Resume();
        }

        // ---- harness (PlankRuntimeChecks / DeckCabinRideRuntimeChecks) ---------------------

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.RideStatus() + "\n" + H.FlowStatus());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        // A finding that should not stop the run; the run fails at the end.
        private static void Soft(bool value, string label)
        {
            if (value) { File.AppendAllText(Log, "PASS " + label + "\n"); return; }
            File.AppendAllText(Log, "SOFT-FAIL " + label + "\n");
            softFails.Add(label);
        }
        // A water-rework row (DIVE-STREAMS/BUBBLES/GLARE): never stops the run.
        private static void W(bool value, string label)
        {
            if (value) { File.AppendAllText(Log, "PASS " + label + "\n"); return; }
            File.AppendAllText(Log, (waterFixRequired ? "W-SOFT-FAIL " : "XFAIL ") + label + "\n");
            waterSoftFails.Add(label);
        }
        private static IEnumerator Wait(float seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }
        private static IEnumerator Expect(Func<bool> condition, float seconds, Func<string> label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline && !condition()) yield return null;
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
                "-screen-width 1280 -screen-height 720 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return Process.Start(info);
        }
        private static IEnumerator Send(string json)
        {
            string text = json.Replace("{id}", (++guestCommand).ToString(CultureInfo.InvariantCulture));
            for (int attempt = 0; ; attempt++)
            {
                bool written = false;
                try { File.WriteAllText(Path.Combine(GuestDir, "command.json"), text); written = true; }
                catch (IOException) when (attempt < 20) { }
                if (written) break;
                yield return null;
            }
            double deadline = EditorApplication.timeSinceStartup + 10.0;
            while (EditorApplication.timeSinceStartup < deadline)
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
        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, Func<string> label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
                if (predicate(lastReply)) { Check(true, label()); yield break; }
                yield return Wait(0.4f);
            }
            throw new Exception(label() + " (guest never agreed)\n" + ElevatorLineOf(lastReply) + "\n" + lastReply);
        }
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", CultureInfo.InvariantCulture) + "}";
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static int GuestId()
        {
            foreach (var conn in FishNet.InstanceFinder.ServerManager.Clients.Values)
                if (conn.IsActive && conn.ClientId != FishNet.InstanceFinder.ClientManager.Connection.ClientId) return conn.ClientId;
            return -1;
        }

        // The guest's "elevator:" line (patches/01-peer-elevator-line.md) and its fields; the
        // walk probe's fields ride on the elevator-deck tester's "elevatorDeck:" line (74526e6).
        // ("elevatorDeck:", "elevnet:" and "elevnetrec:" do not start with "elevator:".)
        private static string LineOf(string reply, string prefix) => reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        private static string ElevatorLineOf(string reply) => LineOf(reply, "elevator:");
        private static string ElevField(string reply, string name) => FieldOf(ElevatorLineOf(reply), name);
        private static string DeckField(string reply, string name) => FieldOf(LineOf(reply, "elevatorDeck:"), name);
        // The walk probe's fields: on the "elevator:" line (a guest below has no ship, so no "elevatorDeck:" fields).
        private static string WalkField(string reply, string name) => ElevField(reply, name) ?? DeckField(reply, name);
        private static string FieldOf(string line, string name)
        {
            int colon = line.IndexOf(':');
            foreach (string part in line.Substring(colon < 0 ? 0 : colon + 1).Split(';'))
            {
                string p = part.Trim();
                if (p.StartsWith(name + "=")) return p.Substring(name.Length + 1);
            }
            return null;
        }
        private static float ElevFloat(string reply, string name, float missing = float.NaN)
        {
            string v = ElevField(reply, name);
            return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : missing;
        }
        private static bool HasElevatorLine(string reply) => ElevatorLineOf(reply).Length > 0;

        // ---- the per-frame recorder (host) ----------------------------------------------

        private struct S
        {
            public double T;
            public float Dt;
            public int PhaseSerial;
            public ElevatorState Phase, CarState;
            public bool PhaseUp, CarUp, Driven;
            public float PhaseElapsed, StateElapsed, CarY, YErr, LagExcess, Travel;
            public float Level, LevelErr, Level01, SurfaceY, Sea, Span;
            public bool DiscWrong, SurfaceShown, TubeSurfaceWrong;
            public CarWaterFlow Flow;
            public int Pour, Visible, BubbleCount, BubbleAbove;   // BubbleCount -1: no particle bubbles under the FX
            public bool BubblesFlag;
            public float Foam, Drain;
            public float Gauge, GaugeErr, MarkerErr;               // MarkerErr -1: no gauge marker
            public float Door, Gate;
            public bool GateBlocks, HasDoor, HasGate;
            public bool LocalInDive, LocalDead, EyeInCar, Submerged;
            public float EyeY, Weight;
            public bool Winch;
        }

        private static readonly List<S> samples = new();
        private static string sampleError;

        // What a car carries, found once per car instance.
        private sealed class CarParts
        {
            public ElevatorController Car;
            public CabinWater Water;
            public Transform Disc, Fx;
            public CabinWaterVisuals Visuals;
            public CabinPanelDisplay Display;
            public Transform GaugeMarker, GaugeBottom, GaugeTop;
            public ElevatorDoor Door;
            public ShaftGate Gate;
            public Collider GateCollider;
            public Transform Tube;
            public Renderer TubeDisc, TubeRing;
            public ParticleSystem[] Bubbles = Array.Empty<ParticleSystem>();
            public CarWaterSorting Sorting;
            public CarRingLight Ring;
        }
        private static CarParts parts;
        private static readonly ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[8192];

        private static CarParts Parts(ElevatorController car)
        {
            if (car == null) return null;
            if (parts != null && parts.Car == car) return parts;
            var p = new CarParts { Car = car };
            p.Water = car.GetComponent<CabinWater>();
            p.Disc = car.transform.Find(ShaftTubeSetup.CabinWaterSurfaceName);
            p.Fx = car.transform.Find(ShaftTubeSetup.CabinWaterFxName);
            p.Visuals = p.Fx != null ? p.Fx.GetComponent<CabinWaterVisuals>() : null;
            p.Display = car.GetComponentsInChildren<CabinPanelDisplay>(true).FirstOrDefault(d => d.DisplayMode == CabinPanelDisplay.Mode.Car);
            if (p.Display != null)
            {
                p.GaugeMarker = FindDeep(p.Display.transform, CabinPanelDisplay.GaugeMarkerName);
                p.GaugeBottom = FindDeep(p.Display.transform, CabinPanelDisplay.GaugeBottomName);
                p.GaugeTop = FindDeep(p.Display.transform, CabinPanelDisplay.GaugeTopName);
            }
            p.Door = car.GetComponentInChildren<ElevatorDoor>(true);
            foreach (GameObject root in car.gameObject.scene.GetRootGameObjects())
            {
                if (root.name == SunkCost.Sites.DiveSiteBuilder.ShaftTubeName) p.Tube = root.transform;
                if (p.Gate == null) p.Gate = root.GetComponentInChildren<ShaftGate>(true);
            }
            if (p.Gate != null) p.GateCollider = p.Gate.GetComponentInChildren<Collider>(true); // the gate's own object or its "Gate Collider" child (no ?? on Unity objects: GetComponent returns a fake null in the editor)
            if (p.Tube != null)
            {
                p.TubeDisc = p.Tube.Find(SunkCost.Sites.DiveSiteBuilder.WaterSurfaceName)?.GetComponent<Renderer>();
                p.TubeRing = p.Tube.Find(ShaftTubeSetup.WaterSurfaceRingName)?.GetComponent<Renderer>();
            }
            if (p.Fx != null)
                p.Bubbles = p.Fx.GetComponentsInChildren<ParticleSystem>(true).Where(ps => NamedUnder(ps.transform, p.Fx, WaterFix.BubbleWords)).ToArray();
            p.Sorting = car.GetComponent<CarWaterSorting>();
            p.Ring = car.GetComponentInChildren<CarRingLight>(true);
            parts = p;
            return p;
        }

        // True when the object or a parent below `stop` has a name containing one of the words.
        private static bool NamedUnder(Transform t, Transform stop, string[] words)
        {
            for (Transform x = t; x != null && x != stop; x = x.parent)
                foreach (string w in words)
                    if (x.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void Sample()
        {
            if (!EditorApplication.isPlaying || samples.Count > 400000) return;
            try { SampleNow(); }
            catch (Exception e) { sampleError ??= e.GetType().Name + ": " + e.Message; }
        }

        private static void SampleNow()
        {
            ElevatorController car = WorldSceneFlow.FindCarCached();
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = Day;
            if (car == null || flow == null || day == null) return;
            CarParts p = Parts(car);
            ElevatorPhase phase = day.Elevator;
            var s = new S
            {
                T = Time.realtimeSinceStartupAsDouble,
                Dt = Time.unscaledDeltaTime,
                PhaseSerial = phase.Serial, Phase = phase.State, PhaseUp = phase.Upward,
                PhaseElapsed = flow.ElapsedSince(phase.StartTick),
                CarState = car.State, CarUp = car.Upward, Driven = car.Driven,
                CarY = car.transform.position.y, Travel = car.TravelSecondsOneWay,
                Sea = car.SeaLevelY, Span = car.SpanMeters,
                BubbleCount = -1, MarkerErr = -1f
            };
            // The car stands exactly where its own elapsed time puts it (the profile), and that
            // elapsed time is the tick-anchored phase clock's, to within this frame (this sample
            // may run before or after the frame's drive, so one frame of lag is allowed).
            s.StateElapsed = car.StateElapsed;
            float progress = car.State switch
            {
                ElevatorState.AtTop => 0f,
                ElevatorState.AtBottom => 1f,
                ElevatorState.Sealing => car.Upward ? 1f : 0f,
                ElevatorState.Descending => ElevatorMath.ProgressAt(car.Profile, car.StateElapsed, false),
                ElevatorState.Ascending => ElevatorMath.ProgressAt(car.Profile, car.StateElapsed, true),
                _ => 0f
            };
            s.YErr = Mathf.Abs(s.CarY - Vector3.Lerp(car.TopPosition, car.BottomPosition, progress).y);
            s.LagExcess = Mathf.Max(0f, Mathf.Abs(s.PhaseElapsed - s.StateElapsed) - s.Dt - 0.005f);

            if (p.Water != null)
            {
                s.Level = p.Water.LevelMeters;
                s.LevelErr = Mathf.Abs(s.Level - ElevatorMath.WaterLevelInCar(s.Sea, s.CarY, s.Span));
                s.Level01 = p.Water.Level01;
                s.SurfaceY = p.Water.SurfaceWorldY;
                s.SurfaceShown = p.Water.SurfaceShown;
                s.Flow = p.Water.Flow;
                if (p.Disc != null)
                {
                    bool active = p.Disc.gameObject.activeInHierarchy;
                    s.DiscWrong = active != s.SurfaceShown || (active && Mathf.Abs(p.Disc.position.y - (s.CarY + s.Level)) > 0.005f);
                }
            }
            if (p.Visuals != null)
            {
                s.Pour = p.Visuals.ActiveStreams; s.Visible = p.Visuals.VisibleStreams;
                s.BubblesFlag = p.Visuals.Bubbles; s.Foam = p.Visuals.Foam01; s.Drain = p.Visuals.Drain01;
            }
            if (p.Bubbles.Length > 0)
            {
                s.BubbleCount = 0;
                foreach (ParticleSystem ps in p.Bubbles)
                {
                    if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                    int n = ps.GetParticles(particleBuffer);
                    s.BubbleCount += n;
                    for (int i = 0; i < n; i++)
                        if (ParticleWorld(ps, particleBuffer[i]).y > s.SurfaceY + 0.03f) s.BubbleAbove++;
                }
            }
            if (p.Display != null)
            {
                s.Gauge = p.Display.GaugeFraction;
                s.GaugeErr = Mathf.Abs(s.Gauge - s.Level01);
                if (p.GaugeMarker != null && p.GaugeBottom != null && p.GaugeTop != null)
                    s.MarkerErr = Vector3.Distance(p.GaugeMarker.position, Vector3.Lerp(p.GaugeBottom.position, p.GaugeTop.position, s.Level01));
            }
            else s.GaugeErr = -1f;
            if (p.Door != null) { s.HasDoor = true; s.Door = p.Door.OpenFraction; }
            if (p.Gate != null) { s.HasGate = true; s.Gate = p.Gate.OpenFraction; s.GateBlocks = p.GateCollider != null && p.GateCollider.enabled; }
            if (p.TubeDisc != null && p.TubeRing != null) s.TubeSurfaceWrong = p.TubeDisc.enabled == p.TubeRing.enabled; // exactly one of them

            HQPlayerController local = Host();
            if (local != null && local.PlayerCamera != null && local.gameObject.scene == car.gameObject.scene)
            {
                s.LocalInDive = true;
                s.LocalDead = local.IsDead;
                Camera cam = local.PlayerCamera;
                s.EyeY = cam.transform.position.y;
                s.EyeInCar = car.IsInsideCar(cam.transform.position);
                s.Weight = SunkCost.Sites.UnderwaterGrade.PrepareAll(cam);
                PlayerSubmersion sub = local.GetComponent<PlayerSubmersion>();
                s.Submerged = sub != null && sub.IsSubmerged;
            }
            SunkCost.Audio.ElevatorSounds sounds = SunkCost.Audio.ElevatorSounds.Instance;
            s.Winch = sounds != null && sounds.WinchPlayingAtCar;
            samples.Add(s);
        }

        private static Vector3 ParticleWorld(ParticleSystem ps, ParticleSystem.Particle particle)
        {
            ParticleSystem.MainModule main = ps.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World: return particle.position;
                case ParticleSystemSimulationSpace.Custom: return main.customSimulationSpace != null ? main.customSimulationSpace.TransformPoint(particle.position) : particle.position;
                default: return ps.transform.TransformPoint(particle.position);
            }
        }

        private static List<S> Since(int from) => samples.Skip(from).ToList();

        // The samples of the last move in that direction since `from` (one phase serial).
        private static List<S> LastMove(int from, bool upward)
        {
            ElevatorState moving = upward ? ElevatorState.Ascending : ElevatorState.Descending;
            List<S> all = Since(from).Where(s => s.Phase == moving && s.CarState == moving).ToList();
            if (all.Count == 0) return all;
            int serial = all[all.Count - 1].PhaseSerial;
            return all.Where(s => s.PhaseSerial == serial).ToList();
        }

        // DV1: the timing, the speed bands, the car where the phase says.
        private static void AnalyseMotion(string label, List<S> move)
        {
            Check(move.Count > 100, $"{label} the recorder saw the move ({move.Count} frames){(sampleError == null ? "" : "; sampler error: " + sampleError)}");
            float travel = move[0].Travel;
            Check(Mathf.Abs(travel - ExpectedTravelSeconds) < 0.02f, $"DV1 {label} the profile's one-way time is unchanged ({travel:0.000} s, expected {ExpectedTravelSeconds:0.000})");
            float real = (float)(move[move.Count - 1].T - move[0].T);
            Check(Mathf.Abs(real - ExpectedTravelSeconds) < 0.5f, $"DV1 {label} the car moved for {real:0.00} s of real time (expected {ExpectedTravelSeconds:0.00} ± 0.5)");
            float[] dy = new float[3], dt = new float[3];
            for (int i = 1; i < move.Count; i++)
            {
                S a = move[i - 1], b = move[i];
                int ba = Band(a), bb = Band(b);
                if (ba < 0 || ba != bb) continue;
                float t = Mathf.Abs(b.PhaseElapsed - a.PhaseElapsed);
                if (t <= 0f || t > 0.5f) continue;
                dy[ba] += Mathf.Abs(b.CarY - a.CarY); dt[ba] += t;
            }
            float above = dt[0] > 0f ? dy[0] / dt[0] : 0f, crossing = dt[1] > 0f ? dy[1] / dt[1] : 0f, below = dt[2] > 0f ? dy[2] / dt[2] : 0f;
            Say($"{label} bands (phase clock): above {above:0.000} m/s over {dt[0]:0.00} s, crossing {crossing:0.000} over {dt[1]:0.00} s, below {below:0.000} over {dt[2]:0.00} s");
            Check(dt[0] > 0.3f && Mathf.Abs(above - 3f) < 0.3f, $"DV1 {label} 3 m/s above the surface ({above:0.00})");
            Check(dt[1] > 1.5f && Mathf.Abs(crossing - 1f) < 0.05f, $"DV1 {label} 1 m/s while the span crosses the surface ({crossing:0.00})");
            Check(dt[2] > 5f && Mathf.Abs(below - 3f) < 0.1f, $"DV1 {label} 3 m/s below the band ({below:0.00})");
            float worstY = move.Max(s => s.YErr), worstLag = move.Max(s => s.LagExcess);
            Check(worstY < 0.005f, $"DV1 {label} the car stood where its profile puts it on every frame (worst {worstY:0.0000} m)");
            Check(worstLag < 0.01f, $"DV1 {label} the car's clock is the tick-anchored phase's, within a frame, on every frame (worst excess {worstLag:0.000} s)");
            Check(move.All(s => s.Driven && s.CarState == s.Phase), $"DV1 {label} the car was driven by the replicated phase (state {move[0].Phase}) on every frame");
        }

        private static int Band(S s)
        {
            if (s.CarY > s.Sea + 0.3f) return 0;
            if (s.CarY < s.Sea - 0.3f && s.CarY > s.Sea - s.Span + 0.3f) return 1;
            if (s.CarY < s.Sea - s.Span - 0.3f) return 2;
            return -1;
        }

        // DV5 (+ the W bubble fizz rows): the flood on a descent.
        private static void AnalyseFlood(string label, List<S> move, bool waterRows)
        {
            int i0 = move.FindIndex(s => s.CarY < s.Sea);
            int iWet = move.FindIndex(s => s.Level > 0.02f);
            int iFull = move.FindIndex(s => s.Level >= s.Span - 0.02f);
            Check(i0 >= 0 && iWet >= 0 && iFull >= 0, $"DV5 {label} the car went under, got wet and filled (under {i0}, wet {iWet}, full {iFull})");
            float t0 = move[i0].PhaseElapsed, tWet = move[iWet].PhaseElapsed, tFull = move[iFull].PhaseElapsed;
            Say($"{label} flood: under at +{t0:0.000} s, wet at +{tWet:0.000}, full at +{tFull:0.000} (phase clock)");
            Check(Mathf.Abs(tWet - t0) <= 0.1f, $"DV5 {label} the flood starts as the car goes under ({tWet - t0:+0.000;-0.000} s)");
            Check(tFull - t0 >= 3.0f && tFull - t0 <= 4.5f, $"DV5 {label} full {tFull - t0:0.00} s after going under (about 4 s; today's 3.5)");
            List<S> pouring = move.Where(s => s.PhaseElapsed >= t0 + 0.1f && s.PhaseElapsed <= tFull - 0.1f).ToList();
            int notSix = pouring.Count(s => s.Pour != 6 || s.Flow != CarWaterFlow.Filling);
            Check(pouring.Count > 10 && notSix == 0, $"DV5 {label} six nozzles pour (flow Filling) on every frame from going under to full ({pouring.Count} frames, {notSix} not)");
            List<S> after = move.Where(s => s.PhaseElapsed >= tFull + 0.2f).ToList();
            int stillPouring = after.Count(s => s.Pour != 0 || s.Visible != 0);
            Check(after.Count > 10 && stillPouring == 0, $"DV5 {label} the nozzles stop within 0.2 s of full ({stillPouring} frames still pouring)");
            int maxVisible = pouring.Count == 0 ? 0 : pouring.Max(s => s.Visible);
            Say($"{label} visible jets while pouring: up to {maxVisible}; foam up to {(pouring.Count == 0 ? 0f : pouring.Max(s => s.Foam)):0.00}");
            if (!waterRows) return;
            // DIVE-BUBBLES: a short fizz after full, then nothing in a still, full car.
            if (move.All(s => s.BubbleCount < 0)) { W(false, $"W-B2 {label} bubbles are real particles (no ParticleSystem under a '{WaterFix.BubbleWords[0]}' object in Cabin Water FX)"); return; }
            bool fizz = move.Any(s => s.PhaseElapsed >= tFull + 0.1f && s.PhaseElapsed <= tFull + 1.0f && s.BubbleCount > 0);
            W(fizz, $"W-B5 {label} a short fizz right after full (bubbles in the first second after full)");
            List<S> settled = move.Where(s => s.PhaseElapsed >= tFull + WaterFix.FizzSeconds).ToList();
            int bubbling = settled.Count(s => s.BubbleCount > 0);
            W(settled.Count > 10 && bubbling == 0, $"W-B5 {label} the fizz is over {WaterFix.FizzSeconds:0} s after full: no bubbles on {settled.Count - bubbling} of {settled.Count} frames");
            int above = move.Count(s => s.BubbleAbove > 0);
            W(above == 0, $"W-B3 {label} no bubble above the water on any frame ({above} frames with one)");
        }

        // DV6: the drain on an ascent.
        private static void AnalyseDrain(string label, List<S> move, bool waterRows)
        {
            int iRoof = move.FindIndex(s => s.CarY + s.Span > s.Sea);
            int iStart = move.FindIndex(s => s.Level < s.Span - 0.02f);
            int iDry = move.FindIndex(s => s.Level <= 0.001f && !s.SurfaceShown);
            Check(iRoof >= 0 && iStart >= 0 && iDry >= 0, $"DV6 {label} the roof surfaced, the drain started and the car went dry (roof {iRoof}, start {iStart}, dry {iDry})");
            float tRoof = move[iRoof].PhaseElapsed, tStart = move[iStart].PhaseElapsed, tDry = move[iDry].PhaseElapsed, travel = move[0].Travel;
            Say($"{label} drain: roof up at +{tRoof:0.000} s, draining at +{tStart:0.000}, dry at +{tDry:0.000}, top at +{travel:0.000} (phase clock)");
            Check(Mathf.Abs(tStart - tRoof) <= 0.1f, $"DV6 {label} the drain starts as the car comes up past the surface ({tStart - tRoof:+0.000;-0.000} s)");
            List<S> draining = move.Where(s => s.Level > 0.02f && s.Level < s.Span - 0.02f).ToList();
            int wrong = draining.Count(s => s.Flow != CarWaterFlow.Draining || s.Drain <= 0f || s.Foam < 0.3f || s.Pour != 0);
            Check(draining.Count > 10 && wrong == 0, $"DV6 {label} flow Draining, the drain and the foam gathering on every draining frame, no nozzle pouring ({draining.Count} frames, {wrong} not)");
            Check(travel - tDry >= 1.0f, $"DV6 {label} dry {travel - tDry:0.00} s before the top (at least 1.0; today's 1.5)");
            Check(Mathf.Abs(move[iDry].CarY - move[iDry].Sea) <= 0.15f, $"DV6 {label} dry as the floor passes sea level (root {move[iDry].CarY:0.00}, sea {move[iDry].Sea:0.00})");
            if (!waterRows || move.All(s => s.BubbleCount < 0)) return;
            int above = move.Count(s => s.BubbleAbove > 0);
            W(above == 0, $"W-B3 {label} no bubble above the water while draining ({above} frames with one)");
        }

        // DV3, DV5/6 water, DV7, DV8 and the tube's one surface, over every frame since `from`.
        private static void AnalyseInvariants(string label, int from, bool waterRows)
        {
            List<S> all = Since(from);
            Check(all.Count > 50, $"{label} recorder frames: {all.Count}{(sampleError == null ? "" : "; sampler error: " + sampleError)}");
            float levelErr = all.Max(s => s.LevelErr);
            Check(levelErr < 0.005f, $"DV5 {label} the car's water is the one truth, WaterLevelInCar(sea, root, span), on every frame (worst {levelErr:0.0000} m)");
            int disc = all.Count(s => s.DiscWrong);
            Check(disc == 0, $"DV5 {label} the surface disc is shown exactly between dry and full, at root + level ({disc} frames wrong)");
            int tube = all.Count(s => s.TubeSurfaceWrong);
            Check(tube == 0, $"DV8 {label} the tube shows exactly one water surface (disc or ring) on every frame ({tube} frames wrong)");
            Check(all.All(s => s.GaugeErr >= 0f), $"DV7 {label} the car has its panel display (Car mode)");
            float gauge = all.Max(s => s.GaugeErr);
            Check(gauge < 0.02f, $"DV7 {label} the gauge follows the level on every frame (worst {gauge:0.000})");
            Check(all.All(s => s.MarkerErr >= 0f), $"DV7 {label} the gauge has its marker between Gauge Bottom and Gauge Top");
            float marker = all.Max(s => s.MarkerErr);
            Check(marker < 0.01f, $"DV7 {label} the gauge's marker stands at the level on every frame (worst {marker * 1000f:0.0} mm)");

            int doorWrong = 0, gateWrong = 0; float worstGap = 0f; int bottomFrames = 0;
            foreach (S s in all)
            {
                if (!s.HasDoor || !s.HasGate) continue;
                bool doorShut = s.CarState == ElevatorState.Descending || s.CarState == ElevatorState.Ascending || (s.Driven && (s.CarState == ElevatorState.AtTop || (s.CarState == ElevatorState.Sealing && !s.CarUp)));
                if (doorShut && s.Door > 0.001f) doorWrong++;
                bool atBottom = s.CarState == ElevatorState.AtBottom || (s.CarState == ElevatorState.Sealing && s.CarUp);
                if (!atBottom && (s.Gate > 0.001f || !s.GateBlocks)) gateWrong++;
                if (atBottom) { bottomFrames++; worstGap = Mathf.Max(worstGap, Mathf.Abs(s.Gate - s.Door)); }
            }
            Check(all.Any(s => s.HasDoor && s.HasGate), $"DV3 {label} the car door and the shaft gate were found");
            Check(doorWrong == 0, $"DV3 {label} the car's doors never open while it moves or at the top ({doorWrong} frames)");
            Check(gateWrong == 0, $"DV3 {label} the gate is shut and blocking whenever the car is not at the bottom ({gateWrong} frames)");
            if (bottomFrames > 0) Check(worstGap < 0.05f, $"DV3 {label} the gate moves with the car's doors at the bottom on {bottomFrames} frames (worst gap {worstGap:0.000})");

            int viewStreak = 0, viewWorst = 0, subStreak = 0, subWorst = 0, truth = 0, viewFrames = 0;
            foreach (S s in all)
            {
                if (!s.LocalInDive || s.LocalDead) { viewStreak = subStreak = 0; continue; }
                viewFrames++;
                bool under = s.EyeY < s.Sea;
                bool viewWrong = (s.EyeY < s.Sea - 0.03f && s.Weight < 0.999f) || (s.EyeY > s.Sea + 0.55f && s.Weight > 0.001f);
                viewStreak = viewWrong ? viewStreak + 1 : 0; viewWorst = Mathf.Max(viewWorst, viewStreak);
                bool subWrong = Mathf.Abs(s.EyeY - s.Sea) > 0.03f && s.Submerged != under;
                subStreak = subWrong ? subStreak + 1 : 0; subWorst = Mathf.Max(subWorst, subStreak);
                if (s.EyeInCar && Mathf.Abs(s.EyeY - s.Sea) > 0.03f && (s.EyeY < s.SurfaceY) != under) truth++;
            }
            if (viewFrames > 0)
            {
                Check(viewWorst <= 2, $"DV8 {label} the underwater view agrees with the eye against the visible surface (worst disagreeing streak {viewWorst} frames of {viewFrames})");
                Check(subWorst <= 2, $"DV8 {label} PlayerSubmersion agrees with the eye (worst streak {subWorst})");
                Check(truth == 0, $"DV8 {label} inside the car, below the car's visible water ⇔ below sea level ({truth} frames not)");
            }
            if (waterRows && all.Any(s => s.BubbleCount >= 0))
            {
                int still = all.Count(s => StillFull(s) && (s.BubbleCount > 0 || s.BubblesFlag));
                int stillFrames = all.Count(StillFull);
                W(stillFrames == 0 || still == 0, $"W-B6 {label} nothing bubbles in a still, full car ({still} of {stillFrames} still frames bubbled)");
            }
        }

        private static bool StillFull(S s) =>
            s.Level >= s.Span - 0.02f &&
            (s.CarState == ElevatorState.AtBottom || (s.CarState == ElevatorState.Sealing && s.CarUp) ||
             (s.CarState == ElevatorState.Descending && s.CarY < s.Sea - s.Span - WaterFix.StillBelowFullMeters));

        // ---- the rides ---------------------------------------------------------------------

        // Down from the deck cabin with the host aboard; one-off captures and the W probes on
        // the way. Returns once the car is at the bottom with its doors open.
        private static IEnumerator RideDown(string label, bool firstRide)
        {
            int serial = Day.CabinRide.Serial;
            string requested = H.ClientRequestCabin();
            Say(label + ": " + requested);
            yield return Expect(() => Day.CabinRide.Serial > serial && Day.CabinRide.Active, 5f, () => label + " the ride down started (refusal: " + Day.LastRefusal.Text + ")");
            bool collar = !firstRide, above = !firstRide, glare = !firstRide, jets = !firstRide, eyeAbove = false, eyeBelow = false, bubbles = !firstRide, deep = !firstRide;
            double deadline = EditorApplication.timeSinceStartup + 90.0;
            while (EditorApplication.timeSinceStartup < deadline && (Day.CabinRide.Active || Day.Elevator.State != ElevatorState.AtBottom))
            {
                ElevatorController car = WorldSceneFlow.FindCar();
                HQPlayerController host = Host();
                if (car != null && host != null && host.gameObject.scene == car.gameObject.scene && ScreenFade.Instance != null && ScreenFade.Instance.IsClear)
                {
                    CabinWater water = car.GetComponent<CabinWater>();
                    Vector3 doorway = Doorway(car);
                    float root = car.transform.position.y;
                    if (!collar && root > -0.5f)
                    {
                        collar = true; // DV2 / VA-V5: the collar over the car at the top stop, from the platform
                        Capture(car.TopPosition + doorway * 7f + Vector3.up * 1.7f, car.TopPosition + Vector3.up * 3.3f, "dv2-collar-top-stop.png");
                    }
                    if (Day.Elevator.State == ElevatorState.Descending && water != null)
                    {
                        float eye = host.PlayerCamera.transform.position.y;
                        if (!above && root < car.SeaLevelY + 2f && root > car.SeaLevelY + 0.3f) { above = true; CaptureEye("va-descent-above-surface.png"); }
                        if (!glare && root <= car.SeaLevelY - 0.5f) { glare = true; GlareFromAbove(car); }
                        if (!jets && water.LevelMeters >= 0.8f && water.LevelMeters <= 1.4f) { jets = true; MeasureJets(label, car, water); } // numbers only: the close-ups are taken on DV9e's empty flood
                        if (!eyeAbove && water.SurfaceShown && eye - water.SurfaceWorldY <= 0.15f)
                        {
                            eyeAbove = true; float gap = eye - water.SurfaceWorldY;
                            CaptureEye("dv8-eye-above-surface.png");
                            Soft(gap > 0.02f, $"DV8 {label} capture with the eye {gap * 100f:0} cm above the car's water (dv8-eye-above-surface.png)");
                        }
                        if (!eyeBelow && water.SurfaceShown && water.SurfaceWorldY - eye >= 0.08f)
                        {
                            eyeBelow = true; float gap = water.SurfaceWorldY - eye;
                            CaptureEye("dv8-eye-below-surface.png");
                            Soft(gap < 0.35f, $"DV8 {label} capture with the eye {gap * 100f:0} cm below the car's water (dv8-eye-below-surface.png)");
                        }
                        if (!bubbles && water.LevelMeters >= 2.2f && water.LevelMeters <= 3.1f) { bubbles = true; MeasureBubblesFilling(label, car, water); }
                        if (!deep && root < -15f) { deep = true; CaptureEye("va-descent-deep.png"); }
                    }
                }
                yield return null;
            }
            Check(!Day.CabinRide.Active && Day.CabinRide.Stage == CabinRideStage.Complete, label + " the ride down completed");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => label + " the car is at the bottom");
            if (firstRide)
            {
                Soft(collar && above && glare && jets && bubbles && deep, $"{label} every one-off capture and probe ran (collar {collar}, above {above}, glare {glare}, jets {jets}, bubbles {bubbles}, deep {deep})");
                Soft(eyeAbove && eyeBelow, $"DV8 {label} both eye-at-the-surface captures were taken (above {eyeAbove}, below {eyeBelow})");
            }
            ElevatorController carAtBottom = WorldSceneFlow.FindCar();
            Check(carAtBottom != null && carAtBottom.GetComponentInChildren<ElevatorDoor>(true) != null, label + " the car and its door are loaded at the bottom");
            yield return Expect(() => carAtBottom.GetComponentInChildren<ElevatorDoor>(true).OpenFraction >= 0.99f, carAtBottom.DoorSealSeconds + 2f, () => label + " the car's doors opened at the bottom");
        }

        // Up from the car with the host aboard (the press is the caller's); returns on the deck.
        private static IEnumerator RideUp(string label, bool captures)
        {
            bool drainShot = !captures, slabs = !captures, swirlShot = !captures;
            double deadline = EditorApplication.timeSinceStartup + 90.0;
            while (EditorApplication.timeSinceStartup < deadline && Day.CabinRide.Active)
            {
                ElevatorController car = WorldSceneFlow.FindCar();
                HQPlayerController host = Host();
                if (car != null && host != null && host.gameObject.scene == car.gameObject.scene && Day.Elevator.State == ElevatorState.Ascending)
                {
                    CabinWater water = car.GetComponent<CabinWater>();
                    float root = car.transform.position.y;
                    if (!drainShot && water != null && water.LevelMeters <= 1.8f && water.LevelMeters >= 1.2f) { drainShot = true; CaptureEye("va-ascent-draining-inside.png"); }
                    if (!swirlShot && water != null && water.LevelMeters <= 1.1f && water.LevelMeters >= 0.6f)
                    {
                        // The polish pass: the drain seen from a rider's eye, looking down at the far grilles.
                        swirlShot = true;
                        Vector3 eyeAt = host.PlayerCamera.transform.position;
                        Capture(eyeAt, car.transform.position + Doorway(car) * -2.4f + Vector3.up * 0.3f, "va-drain-swirl.png");
                    }
                    if (!slabs && root > car.SeaLevelY + 1.0f) { slabs = true; GlareOnSlabs(car); }
                }
                yield return null;
            }
            Check(!Day.CabinRide.Active && Day.CabinRide.Stage == CabinRideStage.Complete, label + " the ride up completed");
            if (captures) Soft(drainShot && slabs, $"{label} the draining capture and the slab glare probe ran (drain {drainShot}, slabs {slabs})");
            yield return Expect(() => Host().gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => label + " the host is back on the ship");
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 5f, () => label + " the screen is clear on the deck");
        }

        // ---- geometry and captures ------------------------------------------------------------

        private static Vector3 Doorway(ElevatorController car) =>
            car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);

        // A bearing measured counter-clockwise (from above) from the doorway, as ElevatorLook's.
        private static Vector3 FromDoorway(ElevatorController car, float bearingDeg) =>
            Quaternion.Euler(0f, -bearingDeg, 0f) * Doorway(car);

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<Transform> AllNamed(Transform root, string name) =>
            root == null ? Enumerable.Empty<Transform>() : root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name);

        // The local player's own camera (its FOV, post-processing, the underwater grade and the
        // per-camera sorting all apply) moved to a pose for one render, then put back.
        private static string Capture(Vector3 position, Vector3 lookAt, string file)
        {
            Camera cam = Host() != null ? Host().PlayerCamera : null;
            if (cam == null) return "no camera";
            Transform t = cam.transform;
            Vector3 p = t.position; Quaternion r = t.rotation;
            try
            {
                t.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
                string result = H.CaptureLocalCamera(Shots + file);
                Say("capture " + Shots + file + " from " + position.ToString("F2") + " (" + result + ")");
                return Shots + file;
            }
            finally { t.SetPositionAndRotation(p, r); }
        }

        private static string CaptureEye(string file)
        {
            string result = H.CaptureLocalCamera(Shots + file);
            Say("capture " + Shots + file + " from the eye (" + result + ")");
            return Shots + file;
        }

        // Every drawn triangle under the roots, nearest to a point (the camera's near-plane test).
        private static readonly Dictionary<Mesh, (Vector3[] v, int[] t)> meshCache = new();
        private static float NearestDrawn(IEnumerable<Transform> roots, Vector3 point, out string nearest) =>
            NearestDrawn(roots, point, out nearest, out _);

        private static float NearestDrawn(IEnumerable<Transform> roots, Vector3 point, out string nearest, out Vector3 at)
        {
            float best = float.PositiveInfinity; nearest = "nothing"; at = point;
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
                {
                    Renderer r = filter.GetComponent<Renderer>();
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || r == null || !r.enabled) continue;
                    if (r.bounds.SqrDistance(point) >= best * best) continue;
                    if (!meshCache.TryGetValue(mesh, out var data)) { data = (mesh.vertices, mesh.triangles); meshCache[mesh] = data; }
                    Matrix4x4 m = filter.transform.localToWorldMatrix;
                    var world = new Vector3[data.v.Length];
                    for (int i = 0; i < world.Length; i++) world[i] = m.MultiplyPoint3x4(data.v[i]);
                    for (int i = 0; i + 2 < data.t.Length; i += 3)
                    {
                        Vector3 c = ClosestOnTriangle(point, world[data.t[i]], world[data.t[i + 1]], world[data.t[i + 2]]);
                        float d = (c - point).magnitude;
                        if (!float.IsNaN(d) && d < best) { best = d; nearest = filter.name; at = c; }
                    }
                }
            }
            return best;
        }

        // Ericson, Real-Time Collision Detection §5.1.5.
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        // The white in a capture: the blown fraction, the largest compact white blob (a "ball")
        // and the thin long white components (a "streak").
        private static string Glare(string file, out float blownFraction, out int ballArea, out int streaks)
        {
            blownFraction = 1f; ballArea = int.MaxValue; streaks = int.MaxValue;
            if (!File.Exists(file)) return "no file " + file;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(file))) return "unreadable " + file;
                int w = tex.width, h = tex.height;
                Color32[] px = tex.GetPixels32();
                var hot = new bool[px.Length];
                int count = 0;
                for (int i = 0; i < px.Length; i++) if (px[i].r >= 245 && px[i].g >= 245 && px[i].b >= 245) { hot[i] = true; count++; }
                blownFraction = count / (float)px.Length;
                ballArea = 0; streaks = 0;
                var seen = new bool[px.Length];
                var todo = new Stack<int>();
                for (int start = 0; start < px.Length; start++)
                {
                    if (!hot[start] || seen[start]) continue;
                    int area = 0, minX = w, maxX = 0, minY = h, maxY = 0;
                    seen[start] = true; todo.Push(start);
                    while (todo.Count > 0)
                    {
                        int i = todo.Pop(); area++;
                        int x = i % w, y = i / w;
                        if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                        for (int oy = -1; oy <= 1; oy++)
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int nx = x + ox, ny = y + oy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                                int n = ny * w + nx;
                                if (hot[n] && !seen[n]) { seen[n] = true; todo.Push(n); }
                            }
                    }
                    int bw = maxX - minX + 1, bh = maxY - minY + 1;
                    float fill = area / (float)(bw * bh);
                    if (fill >= 0.4f) ballArea = Mathf.Max(ballArea, area);
                    if (Mathf.Max(bw, bh) >= 80 && fill <= 0.2f) streaks++;
                }
                return $"blown {blownFraction * 100f:0.000}% of the frame, largest compact blob {ballArea} px, thin streaks {streaks}";
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        // ---- the W probes (the water rework) ------------------------------------------------

        // DIVE-GLARE (1): from above the car in the tube, looking down at its roof.
        private static void GlareFromAbove(ElevatorController car)
        {
            Vector3 axis = car.transform.position;
            string file = Capture(new Vector3(axis.x, axis.y + 5.2f, axis.z) + Doorway(car) * 0.4f, axis + Vector3.up * 1.2f, "w-glare-above-car.png");
            string text = Glare(file, out float blown, out int ball, out _);
            Say("W-G1 " + text);
            W(blown < WaterFix.MaxBlownFraction && ball < WaterFix.MaxBallArea, $"W-G1 from above the car no blown-out white ball ({text}); Read w-glare-above-car.png");
        }

        // DIVE-GLARE (2): inside, dry, facing each dark glass slab: no thin white streak.
        private static void GlareOnSlabs(ElevatorController car)
        {
            Vector3 eye = car.transform.position + Vector3.up * 1.8f;
            int n = 0;
            foreach (float bearing in new[] { SlabBearingA, SlabBearingB })
            {
                string file = Capture(eye, eye + FromDoorway(car, bearing) * 2f, "w-glare-slab-" + (++n) + ".png");
                string text = Glare(file, out float blown, out _, out int streaks);
                Say("W-G2 slab " + n + ": " + text);
                W(streaks == 0 && blown < WaterFix.MaxBlownFraction, $"W-G2 no white streak on the car's glass slab at {bearing:0.0}° ({text}); Read w-glare-slab-{n}.png");
            }
        }

        // DIVE-STREAMS: six powerful 3D jets from the exact nozzle tips (level about 1 m).
        private static void MeasureJets(string label, ElevatorController car, CabinWater water)
        {
            CarParts p = Parts(car);
            if (p.Fx == null || p.Visuals == null) { W(false, $"W-J {label} no Cabin Water FX / CabinWaterVisuals on the car"); return; }
            float landing = Mathf.Max(water.LevelMeters, SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness);
            int count = Mathf.Min(p.Visuals.OutletCount, CabinWaterVisuals.NozzleCount);
            W(count == 6, $"W-J0 {label} six outlets on the car ({count})");
            Transform root = car.transform;
            for (int i = 0; i < count; i++)
            {
                Vector3 outlet = p.Visuals.OutletLocal(i);
                Transform jet = null;
                foreach (string prefix in WaterFix.JetPrefixes) { jet = FindDeep(p.Fx, prefix + (i + 1)); if (jet != null) break; }
                if (jet == null || !jet.gameObject.activeInHierarchy) { W(false, $"W-J {label} jet {i + 1}: no active object named {string.Join("/", WaterFix.JetPrefixes)}{i + 1} under Cabin Water FX"); continue; }
                bool strip = jet.GetComponentsInChildren<LineRenderer>(false).Length > 0 || jet.GetComponentsInChildren<TrailRenderer>(false).Length > 0;
                List<Vector3> pts = JetPoints(jet, p.Fx, root, out bool meshes, out int particles);
                if (pts.Count < 8) { W(false, $"W-J {label} jet {i + 1}: only {pts.Count} drawn points"); continue; }
                float yTop = pts.Max(v => v.y), yBottom = pts.Min(v => v.y);
                List<Vector3> head = pts.Where(v => v.y >= yTop - 0.15f).ToList(), tail = pts.Where(v => v.y <= yBottom + 0.15f).ToList();
                Vector3 hc = Centroid(head), tc = Centroid(tail);
                float drift = new Vector2(tc.x - outlet.x, tc.z - outlet.z).magnitude;
                float mouthRadius = head.Max(v => new Vector2(v.x - hc.x, v.z - hc.z).magnitude);
                float widest = WidestSection(pts);
                // It starts at its outlet: the outlet lies within the jet's first section (its
                // nearest drawn point no further than the jet's own radius, plus a tolerance), and
                // nothing of the jet rises above the nozzle by more than its radius.
                float startGap = pts.Min(v => (v - outlet).magnitude);
                float rise = yTop - outlet.y;
                W(startGap <= mouthRadius + WaterFix.JetStartTolerance && rise <= mouthRadius + 0.05f, $"W-J1 {label} jet {i + 1} starts at its outlet (nearest point {startGap * 100f:0.0} cm, top {rise * 100f:+0.0;-0.0} cm above it, mouth radius {mouthRadius * 100f:0.0} cm)");
                int bins = meshes ? AzimuthBins(pts) : 12;
                W(!strip && (bins >= WaterFix.JetMinAzimuthBins || (!meshes && particles >= 12)), $"W-J2 {label} jet {i + 1} is a 3D jet readable from any side ({(strip ? "a line/trail strip, " : "")}{bins} of 12 azimuth bins, {particles} particles)");
                // It leaves along the nozzle's own outflow (Dan: "a short arc out of the bent nozzle";
                // the rework measured every mouth facing straight down), then falls and lands in the car.
                Vector3? outflow = OutletDirection(p.Visuals, i);
                List<Vector3> early = pts.Where(v => (v - outlet).magnitude >= 0.15f && (v - outlet).magnitude <= 0.35f).ToList();
                float angle = outflow.HasValue && early.Count > 0 ? Vector3.Angle(outflow.Value, Centroid(early) - outlet) : float.NaN;
                W(outflow.HasValue && angle <= WaterFix.JetMaxDirectionDeg && drift <= WaterFix.JetMaxDrift,
                    $"W-J3 {label} jet {i + 1} leaves along its nozzle's outflow ({(outflow.HasValue ? $"{angle:0} degrees off {outflow.Value:F2}" : $"no CabinWaterVisuals.{WaterFix.OutletDirectionMethod}(int): the rework has not landed")}) and lands {drift * 100f:0} cm from the nozzle, horizontally");
                W(widest >= WaterFix.JetMinRadius && mouthRadius >= WaterFix.JetMinMouthRadius, $"W-J4 {label} jet {i + 1} is thick (widest section radius {widest * 100f:0.0} cm, at least {WaterFix.JetMinRadius * 100f:0}; at the mouth {mouthRadius * 100f:0.0} cm, at least {WaterFix.JetMinMouthRadius * 100f:0})");
                W(Mathf.Abs(yBottom - landing) <= 0.15f, $"W-J5 {label} jet {i + 1} reaches the water ({(yBottom - landing) * 100f:+0;-0} cm from the surface)");
            }
            int sprays = p.Fx.GetComponentsInChildren<ParticleSystem>(false).Count(ps => NamedUnder(ps.transform, p.Fx, WaterFix.SprayWords) && ps.particleCount > 0);
            W(sprays > 0, $"W-J6 {label} spray/mist at the outlets or where the jets hit ({sprays} live spray systems)");
            W(p.Visuals.Foam01 >= 0.6f, $"W-J7 {label} white churning foam on the rising surface (Foam01 {p.Visuals.Foam01:0.00})");
        }

        // DIVE-STREAMS: every outlet the water visuals pour from lies on the car model's drawn
        // mesh (not in the air beside it). Static in the car's frame: checked with the car at rest.
        private static void OutletsOnModel(ElevatorController car)
        {
            CarParts p = Parts(car);
            Transform carLook = car.transform.Find(ShaftTubeSetup.CarLookName);
            if (p.Visuals == null || carLook == null) { W(false, "W-J1 the car has its water visuals and its Car Look"); return; }
            for (int i = 0; i < Mathf.Min(p.Visuals.OutletCount, CabinWaterVisuals.NozzleCount); i++)
            {
                float onModel = NearestDrawn(new[] { carLook }, car.transform.TransformPoint(p.Visuals.OutletLocal(i)), out string what);
                W(onModel <= WaterFix.OutletOnModel, $"W-J1 outlet {i + 1} sits on the car model ({onModel * 100f:0.0} cm from {what}); Read w-nozzle-{i + 1}.png for the exact tip");
            }
        }

        // DIVE-STREAMS evidence: a close-up of each nozzle pouring, from inside the car toward
        // the axis and a little below, and an overview of the six. Taken on the empty car's flood
        // (the host stands on the seafloor, so the stalls cost no eye-at-the-surface row).
        private static void NozzleShots(ElevatorController car)
        {
            CarParts p = Parts(car);
            if (p.Visuals == null) return;
            Transform root = car.transform;
            for (int i = 0; i < Mathf.Min(p.Visuals.OutletCount, CabinWaterVisuals.NozzleCount); i++)
            {
                Vector3 outletWorld = root.TransformPoint(p.Visuals.OutletLocal(i));
                Vector3 inward = Vector3.ProjectOnPlane(root.position - outletWorld, Vector3.up).normalized;
                Capture(outletWorld + inward * 0.9f - Vector3.up * 0.35f, outletWorld - Vector3.up * 0.25f, "w-nozzle-" + (i + 1) + ".png");
            }
            Vector3 eye = root.position + Vector3.up * 1.2f - Doorway(car) * 1.5f;
            Capture(eye, root.position + Vector3.up * 2.4f + Doorway(car) * 0.5f, "w-jets-overview.png");
            Capture(root.position + Vector3.up * 2.0f + Doorway(car) * 3.6f, root.position + Vector3.up * 1.6f, "w-jets-from-outside.png");
        }

        // A jet's drawn points in the car root's frame: its meshes' vertices and its live
        // particles (spray/mist/splash/foam systems excluded).
        private static List<Vector3> JetPoints(Transform jet, Transform fx, Transform carRoot, out bool meshes, out int particles)
        {
            var pts = new List<Vector3>();
            meshes = false; particles = 0;
            foreach (MeshFilter filter in jet.GetComponentsInChildren<MeshFilter>(false))
            {
                Renderer r = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || r == null || !r.enabled || NamedUnder(filter.transform, fx, WaterFix.NotJetWords)) continue;
                meshes = true;
                foreach (Vector3 v in filter.sharedMesh.vertices) pts.Add(carRoot.InverseTransformPoint(filter.transform.TransformPoint(v)));
            }
            foreach (ParticleSystem ps in jet.GetComponentsInChildren<ParticleSystem>(false))
            {
                if (NamedUnder(ps.transform, fx, WaterFix.NotJetWords)) continue;
                int n = ps.GetParticles(particleBuffer);
                particles += n;
                for (int i = 0; i < n; i++) pts.Add(carRoot.InverseTransformPoint(ParticleWorld(ps, particleBuffer[i])));
            }
            return pts;
        }

        // The widest horizontal radius of any 10 cm slice of the jet, about that slice's centre.
        private static float WidestSection(List<Vector3> pts)
        {
            float widest = 0f;
            foreach (var slice in pts.GroupBy(v => Mathf.FloorToInt(v.y / 0.1f)))
            {
                List<Vector3> s = slice.ToList();
                Vector3 c = Centroid(s);
                foreach (Vector3 v in s) widest = Mathf.Max(widest, new Vector2(v.x - c.x, v.z - c.z).magnitude);
            }
            return widest;
        }

        // CabinWaterVisuals.OutletDirectionLocal(i) when the water rework has it (car-root local), else null.
        private static Vector3? OutletDirection(CabinWaterVisuals visuals, int i)
        {
            System.Reflection.MethodInfo method = typeof(CabinWaterVisuals).GetMethod(WaterFix.OutletDirectionMethod, new[] { typeof(int) });
            if (method == null || method.ReturnType != typeof(Vector3)) return null;
            return (Vector3)method.Invoke(visuals, new object[] { i });
        }

        private static Vector3 Centroid(List<Vector3> pts)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 v in pts) sum += v;
            return pts.Count > 0 ? sum / pts.Count : Vector3.zero;
        }

        // How many of 12 azimuth bins the points fill round each 10 cm slice's own centre:
        // two crossed cards fill 4, a round jet 12.
        private static int AzimuthBins(List<Vector3> pts)
        {
            var bins = new bool[12];
            foreach (var slice in pts.GroupBy(v => Mathf.FloorToInt(v.y / 0.1f)))
            {
                List<Vector3> s = slice.ToList();
                Vector3 c = Centroid(s);
                foreach (Vector3 v in s)
                {
                    Vector2 d = new(v.x - c.x, v.z - c.z);
                    if (d.magnitude < 0.01f) continue;
                    float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    bins[Mathf.Clamp(Mathf.FloorToInt((a + 180f) / 30f), 0, 11)] = true;
                }
            }
            return bins.Count(b => b);
        }

        // DIVE-BUBBLES while filling: particles, where the air goes in, none above the water,
        // random sizes and speeds.
        private static void MeasureBubblesFilling(string label, ElevatorController car, CabinWater water)
        {
            CarParts p = Parts(car);
            if (p.Fx == null) { W(false, $"W-B {label} no Cabin Water FX"); return; }
            var bubbleObjects = p.Fx.GetComponentsInChildren<Transform>(true).Where(t => t != p.Fx && NamedUnder(t, p.Fx, WaterFix.BubbleWords)).ToList();
            int quads = bubbleObjects.Sum(t => t.GetComponents<MeshRenderer>().Length);
            W(p.Bubbles.Length > 0 && quads == 0, $"W-B1 {label} the bubbles are particles, not textured quads ({p.Bubbles.Length} particle systems, {quads} mesh renderers under bubble objects)");
            if (p.Bubbles.Length == 0) return;
            var positions = new List<Vector3>(); var sizes = new List<float>(); var speeds = new List<float>();
            foreach (ParticleSystem ps in p.Bubbles)
            {
                if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                int n = ps.GetParticles(particleBuffer);
                for (int i = 0; i < n; i++)
                {
                    positions.Add(car.transform.InverseTransformPoint(ParticleWorld(ps, particleBuffer[i])));
                    sizes.Add(particleBuffer[i].GetCurrentSize(ps));
                    speeds.Add(particleBuffer[i].velocity.magnitude);
                }
            }
            W(positions.Count >= 20, $"W-B2 {label} the pouring jets drive bubbles into the water ({positions.Count} live bubbles at level {water.LevelMeters:0.00})");
            if (positions.Count == 0) return;
            // Where the air enters: under each jet's landing (the jet's lowest points), else its outlet.
            var landings = new List<Vector2>();
            for (int i = 0; i < Mathf.Min(p.Visuals != null ? p.Visuals.OutletCount : 0, CabinWaterVisuals.NozzleCount); i++)
            {
                Transform jet = null;
                foreach (string prefix in WaterFix.JetPrefixes) { jet = FindDeep(p.Fx, prefix + (i + 1)); if (jet != null) break; }
                List<Vector3> pts = jet != null && jet.gameObject.activeInHierarchy ? JetPoints(jet, p.Fx, car.transform, out _, out _) : new List<Vector3>();
                Vector3 o = p.Visuals.OutletLocal(i);
                if (pts.Count > 0) { float yb = pts.Min(v => v.y); Vector3 c = Centroid(pts.Where(v => v.y <= yb + 0.15f).ToList()); landings.Add(new Vector2(c.x, c.z)); }
                else landings.Add(new Vector2(o.x, o.z));
            }
            int near = positions.Count(v => landings.Any(l => (new Vector2(v.x, v.z) - l).magnitude <= WaterFix.BubbleNearJet));
            W(near >= 0.9f * positions.Count, $"W-B2 {label} bubbles only where the air goes in: {near} of {positions.Count} within {WaterFix.BubbleNearJet:0.0} m of a jet's landing");
            float surfaceLocal = water.LevelMeters;
            int aboveNow = positions.Count(v => v.y > surfaceLocal + 0.03f);
            W(aboveNow == 0, $"W-B3 {label} no bubble above the water ({aboveNow} above)");
            // The rework draws its bubbles with SetParticles and leaves their velocity at zero (it
            // derives every position each frame), so speed variety is judged only when velocities are
            // set; otherwise by the captures.
            bool speedsSet = speeds.Count > 0 && speeds.Average() > 1e-3f;
            W(Cov(sizes) >= 0.15f && (!speedsSet || Cov(speeds) >= 0.15f), $"W-B4 {label} random sizes{(speedsSet ? " and speeds" : "")} (size CoV {Cov(sizes):0.00}{(speedsSet ? $", speed CoV {Cov(speeds):0.00}" : "; velocities not set: speed and wobble by w-bubbles-filling-under.png")})");
            CaptureEye("w-bubbles-filling-under.png");
        }

        private static float Cov(List<float> values)
        {
            if (values.Count < 2) return 0f;
            float mean = values.Average();
            if (mean <= 1e-5f) return 0f;
            float variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
            return Mathf.Sqrt(variance) / mean;
        }

        // ---- noise ----------------------------------------------------------------------------

        private sealed class Ears : SunkCost.Noise.INoiseListener
        {
            public readonly List<SunkCost.Noise.NoiseEvent> Heard = new();
            public void OnNoise(in SunkCost.Noise.NoiseEvent noise) => Heard.Add(noise);
        }

        // ---- the run ------------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ElevatorDiveCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();

            Heading("S0 — to sea, standing in the deck cabin");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "S0 sailing to sea");
            yield return Expect(() => Day.World == WorldId.Sea && Day.Departure.Stage == DepartureStage.Complete && !flow.Transitioning, 60f, () => "S0 arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() == null || !WorldSceneFlow.LocalRider().Locked, 5f, () => "S0 unlocked at sea");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            CabinPanelDisplay deckDisplay = sea != null ? sea.GetComponentsInChildren<CabinPanelDisplay>(true).FirstOrDefault(d => d.DisplayMode == CabinPanelDisplay.Mode.Deck) : null;
            Check(deckDisplay != null && deckDisplay.GaugeFraction == 0f, "DV7 the deck cabin's panel gauge reads 0 (the deck never floods): " + (deckDisplay == null ? "no deck display" : deckDisplay.GaugeFraction.ToString("0.000", CultureInfo.InvariantCulture)));

            Heading("DV1/DV3/DV5/DV7/DV8/DV9b + W — the host rides down alone, every frame recorded");
            SunkCost.Noise.NoiseSystem.Register(ears); ears.Heard.Clear();
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int from = samples.Count;
            int dingsBefore = SunkCost.Audio.ElevatorSounds.Instance != null ? SunkCost.Audio.ElevatorSounds.Instance.DingsAtCar : 0;
            yield return RideDown("D1", firstRide: true);
            ElevatorController car = WorldSceneFlow.FindCar();
            Check(car != null && host.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && car.IsInsideCar(host.transform.position + Vector3.up * 0.5f), "D1 the host stands in the car at the bottom");
            List<S> down = LastMove(from, upward: false);
            AnalyseMotion("D1", down);
            AnalyseFlood("D1", down, waterRows: true);
            AnalyseInvariants("D1", from, waterRows: true);
            int winchFrames = down.Count(s => s.Winch);
            Check(winchFrames >= 0.9f * down.Count, $"DV9b D1 the winch played at the car on {winchFrames} of {down.Count} moving frames");
            int elevatorEvents = ears.Heard.Count(e => e.Kind == SunkCost.Noise.NoiseKind.Elevator);
            float radius = SunkCost.Noise.NoiseSettings.Get().ElevatorRadius;
            Check(elevatorEvents >= 3 && ears.Heard.Where(e => e.Kind == SunkCost.Noise.NoiseKind.Elevator).All(e => Mathf.Approximately(e.Radius, radius)), $"DV9b D1 the moving car was heard by the ocean ({elevatorEvents} Elevator events at {radius} m)");
            SunkCost.Noise.NoiseSystem.Unregister(ears);
            Check(SunkCost.Audio.ElevatorSounds.Instance != null && SunkCost.Audio.ElevatorSounds.Instance.DingsAtCar > dingsBefore, "DV9b D1 the bell rang at the car on arrival");
            yield return null;
            Check(hud.Visor.On && hud.Visor.AirFraction > 0.9f && hud.Visor.AirFraction <= 1f && hud.Visor.HealthFraction >= 0.999f, $"DV9a D1 the suit is on from the top: the tank counts ({100f * hud.Visor.AirFraction:0}%), health full");
            float airAtBottom = host.Vitals.AirFraction;
            int restFrom = samples.Count;

            Heading("DV0 — the car's runtime parts");
            CarParts cp = Parts(car);
            Check(cp.Water != null && cp.Visuals != null && cp.Visuals.OutletCount == CabinWaterVisuals.NozzleCount && cp.Display != null && cp.Sorting != null && cp.Ring != null,
                $"DV0 the car carries its water ({cp.Water != null}), its FX with six outlets ({(cp.Visuals == null ? 0 : cp.Visuals.OutletCount)}), its panel display ({cp.Display != null}), its sorting ({cp.Sorting != null}) and its ring light ({cp.Ring != null})");
            Check(car.transform.Find("Cabin Light") != null, "DV0 \"Cabin Light\" is a direct child of the car (the Elevator Ghost finds it)");
            Check(car.transform.Find(ShaftTubeSetup.CarLookName) != null && car.transform.Find(ShaftTubeSetup.PanelLookName) != null, "DV0 the car wears Car Look and Panel Look");
            Check(cp.Tube != null && cp.Gate != null && cp.TubeDisc != null && cp.TubeRing != null, "DV0 the shaft tube has its gate, its water disc and its ring");
            Check(FindDeep(cp.Tube, SunkCost.Sites.ShaftTubeLook.SillName) != null && FindDeep(cp.Tube, SunkCost.Sites.ShaftTubeLook.RampName) != null, "DV0 the foot has its sill and ramp colliders");

            Heading("DV3 — the doors and the gate open together at the bottom");
            ElevatorDoor door = cp.Door;
            Check(cp.GateCollider != null, "DV3 the shaft gate has its collider");
            yield return Expect(() => cp.Gate.OpenFraction >= 0.99f && !cp.GateCollider.enabled, 2f, () => $"DV3 the gate is open with the car's doors (gate {cp.Gate.OpenFraction:0.00}, door {door.OpenFraction:0.00})");
            Transform leafR = FindDeep(car.transform, "Leaf Right"), leafL = FindDeep(car.transform, "Leaf Left");
            float leafRYaw = leafR == null ? 0f : Mathf.DeltaAngle(0f, leafR.localEulerAngles.y), leafLYaw = leafL == null ? 0f : Mathf.DeltaAngle(0f, leafL.localEulerAngles.y);
            Check(leafR != null && leafL != null && Mathf.Abs(Mathf.Abs(leafRYaw) - DoorHalfAngleDeg) < 0.3f && Mathf.Abs(leafRYaw + leafLYaw) < 0.3f, $"DV3 the car's leaves are open by ±{DoorHalfAngleDeg}° about the axis (right {leafRYaw:0.00}°, left {leafLYaw:0.00}°)");
            Transform gateR = FindDeep(cp.Tube, "Gate Leaf Right"), gateL = FindDeep(cp.Tube, "Gate Leaf Left");
            float gateRYaw = gateR == null ? 0f : Mathf.DeltaAngle(0f, gateR.localEulerAngles.y), gateLYaw = gateL == null ? 0f : Mathf.DeltaAngle(0f, gateL.localEulerAngles.y);
            Check(gateR != null && gateL != null && Mathf.Abs(gateRYaw) > 10f && Mathf.Abs(gateRYaw + gateLYaw) < 0.5f, $"DV3 the gate's leaves are open, mirrored (right {gateRYaw:0.00}°, left {gateLYaw:0.00}°)");
            Check(AllNamed(cp.Tube, SunkCost.Editor.Look.ElevatorLook.GateLeafLookName).Count() == 2 && AllNamed(car.transform, SunkCost.Editor.Look.ElevatorLook.LeafLookName).Count() == 2, "DV3 both car leaves and both gate leaves wear the new glass leaves");

            Heading("DV2 — no clipping with the collar, the tube, the foot");
            var watch = Stopwatch.StartNew();
            SunkCost.Sites.DiveElevatorClearance.Report sweep = SunkCost.Sites.DiveElevatorClearance.Sweep();
            Say(sweep.Text + $" ({watch.ElapsedMilliseconds} ms)");
            Check(!float.IsNaN(sweep.Worst) && sweep.Worst > 0.005f, $"DV2 the car clears the collar, the sections, the foot and the gate over the whole ride (worst {sweep.Worst:+0.000;-0.000} m)");
            // TESTPLAN DV2's per-part margins: the collar ≥ 0, the foot's pit ≥ 0.02, the tube sections ≥ 0.2.
            float PartGap(string part)
            {
                var m = System.Text.RegularExpressions.Regex.Match(sweep.Text, System.Text.RegularExpressions.Regex.Escape(part) + @": ([+-][0-9.]+) m");
                return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : float.NaN;
            }
            float collarGap = PartGap(SunkCost.Sites.ShaftTubeLook.CollarName), footGap = PartGap(SunkCost.Sites.ShaftTubeLook.FootName), tubeGap = PartGap(SunkCost.Sites.ShaftTubeLook.SectionsName);
            Check(collarGap >= 0f && footGap >= 0.02f && tubeGap >= 0.2f, $"DV2 per part: collar {collarGap:+0.000;-0.000} m (≥ 0), foot {footGap:+0.000;-0.000} m (≥ 0.02), tube sections {tubeGap:+0.000;-0.000} m (≥ 0.2)");
            SunkCost.Sites.DiveElevatorClearance.Report bottom = SunkCost.Sites.DiveElevatorClearance.At(car.transform.position.y);
            Check(!float.IsNaN(bottom.Worst) && bottom.Worst > 0.005f, $"DV2 at the bottom stop, on the live car: {bottom.Text}");
            Vector3 doorway = Doorway(car);
            Capture(car.BottomPosition + doorway * 8f + Vector3.up * 1.6f, car.BottomPosition + Vector3.up * 1.6f, "dv2-foot-bottom-stop.png");
            Check(cp.Sorting.LastCameraInside == false && car.transform.Find(ShaftTubeSetup.CabinWaterSurfaceName).GetComponent<Renderer>().sortingOrder == CarWaterSorting.OrderOf(CarWaterSorting.Group.CarWaterSurface, false), "DV11 the outside capture drew with the outside order");

            Heading("DV11 — the transparency order");
            var inside = new[] { CarWaterSorting.Group.TubeGlass, CarWaterSorting.Group.TubeWater, CarWaterSorting.Group.CarGlass, CarWaterSorting.Group.CarWaterSurface, CarWaterSorting.Group.CarWaterFX };
            var outside = new[] { CarWaterSorting.Group.CarWaterSurface, CarWaterSorting.Group.CarWaterFX, CarWaterSorting.Group.CarGlass, CarWaterSorting.Group.TubeWater, CarWaterSorting.Group.TubeGlass };
            bool insideOrdered = true, outsideOrdered = true;
            for (int i = 1; i < inside.Length; i++) insideOrdered &= CarWaterSorting.OrderOf(inside[i - 1], true) < CarWaterSorting.OrderOf(inside[i], true);
            for (int i = 1; i < outside.Length; i++) outsideOrdered &= CarWaterSorting.OrderOf(outside[i - 1], false) < CarWaterSorting.OrderOf(outside[i], false);
            Check(insideOrdered && outsideOrdered, "DV11 far to near from inside: tube glass → tube water → car glass → car water → its FX; from outside: car water → FX → car glass → tube water → tube glass");
            Check(inside.All(grp => cp.Sorting.CountOf(grp) > 0), "DV11 every group has renderers: " + string.Join(", ", inside.Select(grp => grp + " " + cp.Sorting.CountOf(grp))));
            var transparent = new List<Renderer>();
            foreach (string name in new[] { ShaftTubeSetup.CabinWaterSurfaceName, ShaftTubeSetup.CabinWaterFxName, "Glass Shell" })
            { Transform t = car.transform.Find(name); if (t != null) transparent.AddRange(t.GetComponentsInChildren<Renderer>(true)); }
            if (cp.TubeDisc != null) transparent.Add(cp.TubeDisc);
            if (cp.TubeRing != null) transparent.Add(cp.TubeRing);
            var badQueues = new List<string>();
            foreach (Renderer r in transparent)
                foreach (Material m in r.sharedMaterials)
                    if (m != null && (m.renderQueue < 3000 || (m.HasProperty("_ZWrite") && m.GetFloat("_ZWrite") > 0.5f))) badQueues.Add(r.name + "/" + m.name + " q" + m.renderQueue);
            Check(badQueues.Count == 0, "DV11 the car's water, its FX, its glass and the tube's water are transparent (queue ≥ 3000, no depth write): " + (badQueues.Count == 0 ? "all" : string.Join(", ", badQueues.Distinct())));

            Heading("VA-V3 — at the bottom, looking out through the open doors and gate");
            MoveIntoCar(car, Vector3.zero); yield return Wait(0.3f);
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(doorway); yield return null; yield return null;
            CaptureEye("va-bottom-out-through-gate.png");
            Check(cp.Sorting.LastCameraInside && car.transform.Find(ShaftTubeSetup.CabinWaterSurfaceName).GetComponent<Renderer>().sortingOrder == CarWaterSorting.OrderOf(CarWaterSorting.Group.CarWaterSurface, true), "DV11 the inside capture drew with the inside order");
            CaptureEye("w-bubbles-full-bottom.png"); // DIVE-BUBBLES: the still, full car — nothing should rise (Read it)

            Heading("DV4 — the ramp: out of the car onto the sand and back");
            yield return WalkRow("DV4 out", car, doorway, until: () => Flat(Host().transform.position - car.transform.position).magnitude > 7f);
            host.transform.rotation = Quaternion.LookRotation(-doorway); yield return null;
            yield return WalkRow("DV4 back", car, -doorway, until: () => car.IsInsideCar(Host().transform.position + Vector3.up * 0.5f) && Flat(Host().transform.position - car.transform.position).magnitude < 0.8f);
            Check(host.Vitals.AirFraction < airAtBottom, $"DV9a the tank drains below ({100f * airAtBottom:0.0}% → {100f * host.Vitals.AirFraction:0.0}%)");

            Heading("DV10 — the camera does not clip the car's glass, posts, leaves or panel, nor the gate");
            yield return CameraRows(car, cp);
            OutletsOnModel(car);
            // Everything since the arrival: the car stood full and still at the bottom while the
            // host walked, pushed and captured — the doors, the gate, the view, and (W-B6) no bubbles.
            AnalyseInvariants("bottom rest", restFrom, waterRows: true);

            Heading("DV9e — a rider left below: the car climbs empty, drains, comes back and floods");
            MoveIntoCar(car, Vector3.zero); yield return Wait(0.3f);
            from = samples.Count;
            int leaverSerial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > leaverSerial && Day.Elevator.State == ElevatorState.Sealing, 8f, () => "DV9e the car's seal started");
            host.TeleportLocal(car.transform.position + doorway * 4.5f + Vector3.up * 0.05f, host.Yaw); // out through the doorway onto the sand
            yield return Expect(() => Day.CabinRide.Stage == CabinRideStage.Riding || Day.Elevator.State == ElevatorState.Ascending, 12f, () => "DV9e the car climbs without the leaver");
            Check(Day.IsBelow(host.OwnerId) && !Day.IsRider(host.OwnerId), "DV9e the leaver is below, not a rider");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtTop, 30f, () => "DV9e the empty car reached the top");
            bool shots = false;
            double returnDeadline = EditorApplication.timeSinceStartup + 60.0;
            while (EditorApplication.timeSinceStartup < returnDeadline && Day.Elevator.State != ElevatorState.AtBottom)
            {
                if (!shots && Day.Elevator.State == ElevatorState.Descending && cp.Water.LevelMeters >= 0.6f && cp.Water.LevelMeters <= 1.6f) { shots = true; NozzleShots(car); }
                yield return null;
            }
            Check(Day.Elevator.State == ElevatorState.AtBottom, "DV9e the car came back down for the leaver");
            Soft(shots, "W DV9e the nozzle close-ups were taken on the empty car's flood (w-nozzle-1..6.png, w-jets-overview.png, w-jets-from-outside.png)");
            yield return Expect(() => door.OpenFraction >= 0.99f && cp.Gate.OpenFraction >= 0.99f, car.DoorSealSeconds + 2f, () => "DV9e doors and gate open again for the leaver");
            AnalyseDrain("DV9e (empty, up)", LastMove(from, upward: true), waterRows: true);
            AnalyseFlood("DV9e (empty, down)", LastMove(from, upward: false), waterRows: true);
            AnalyseInvariants("DV9e", from, waterRows: true);
            Check(Mathf.Abs(cp.Water.LevelMeters - car.SpanMeters) < 0.03f, $"DV9e the returned car stands full ({cp.Water.LevelMeters:0.00} m)");

            Heading("DV12 + DV6 — the real E on the car's panel; the ride up drains before the top");
            yield return PressCarPanel(car, hud);
            from = samples.Count;
            yield return RideUp("U1", captures: true);
            List<S> up = LastMove(from, upward: true);
            AnalyseMotion("U1", up);
            AnalyseDrain("U1", up, waterRows: true);
            AnalyseInvariants("U1", from, waterRows: true);
            sea = ShipParts.InWorld(WorldId.Sea);
            Check(sea != null && sea.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f) && Day.Below.Count == 0, "U1 the host stands in the deck cabin; nobody below");
            yield return Expect(() => Day.DiveDone, 3f, () => "U1 the dive is done");
            H.ClientRequestEndDay();
            yield return Expect(() => Day.Day == 2 && !Day.DiveDone, 3f, () => "U1 End day: day 2 (day " + Day.Day + ")");
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, flow.Settings.DayCardSeconds + 3f, () => "U1 the day card faded");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 15f, () => "U1 the site unloaded");

            // ---- guest rows ----
            Heading("G0 — a guest joins at sea between days");
            guest = LaunchGuest();
            yield return Expect(() => GuestId() >= 0 && UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Length >= 2, 40f, () => "G0 guest player spawned");
            int guestId = GuestId();
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, () => "G0 the guest joined at sea");
            yield return GuestEventually(HasElevatorLine, 3f, () => "G0 the guest build reports its elevator line (patches/01 applied, guest rebuilt)");
            yield return Send("{\"id\":{id},\"action\":\"elev_reset\"}");

            Heading("G1 — down together; the guest's own per-frame record");
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3 guestSpot = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
            yield return Wait(0.5f);
            from = samples.Count;
            yield return RideDown("G1", firstRide: false);
            car = WorldSceneFlow.FindCar(); cp = Parts(car); door = cp.Door; doorway = Doorway(car);
            Check(Day.IsBelow(guestId) && Day.IsBelow(host.OwnerId), "G1 both below");
            down = LastMove(from, upward: false);
            AnalyseMotion("G1", down);
            AnalyseFlood("G1", down, waterRows: true);
            AnalyseInvariants("G1", from, waterRows: true);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && ElevField(r, "carState") == "AtBottom" && ElevFloat(r, "gate") >= 0.99f, 20f, () => "G1 the guest is below with the car at the bottom, its gate open");
            string g = lastReply;
            Say("G1 guest: " + ElevatorLineOf(g));
            Check(ElevFloat(g, "elevFrames") > 500f, "G1 the guest recorded the ride frame by frame (" + ElevField(g, "elevFrames") + " frames)");
            Check(ElevFloat(g, "elevWorstY") < 0.005f && ElevFloat(g, "elevWorstLag") < 0.01f, "DV1 G1 the guest's car stood where its profile puts it (worst " + ElevField(g, "elevWorstY") + " m) on the tick-anchored clock (worst excess lag " + ElevField(g, "elevWorstLag") + " s)");
            Check(ElevFloat(g, "elevWorstWater") < 0.005f, "DV5 G1 the guest's water is the one truth (worst " + ElevField(g, "elevWorstWater") + " m)");
            Check(ElevFloat(g, "elevWorstGauge") < 0.02f, "DV7 G1 the guest's gauge follows (worst " + ElevField(g, "elevWorstGauge") + ")");
            Check(ElevFloat(g, "elevDoorWrong") == 0f && ElevFloat(g, "elevGateWrong") == 0f && ElevFloat(g, "elevWorstGate") < 0.05f, $"DV3 G1 the guest's doors and gate: never open at the wrong time ({ElevField(g, "elevDoorWrong")}/{ElevField(g, "elevGateWrong")} frames), together at the bottom (worst gap {ElevField(g, "elevWorstGate")})");
            Check(ElevFloat(g, "elevViewWorst") <= 2f && ElevFloat(g, "elevSubWorst") <= 2f, $"DV8 G1 the guest's view and submersion agree with its eye (worst streaks {ElevField(g, "elevViewWorst")}/{ElevField(g, "elevSubWorst")})");
            Check(Mathf.Abs(ElevFloat(g, "carWater") - cp.Water.LevelMeters) < 0.02f && Mathf.Abs(ElevFloat(g, "gauge") - cp.Display.GaugeFraction) < 0.02f, $"G1 the guest's water and gauge equal the host's ({ElevField(g, "carWater")} / {cp.Water.LevelMeters:0.000}; {ElevField(g, "gauge")} / {cp.Display.GaugeFraction:0.000})");
            float guestBubbles = ElevFloat(g, "bubbleParticles", -1f);
            W(guestBubbles == 0f && ElevFloat(g, "elevBubbleStill") == 0f && ElevFloat(g, "elevBubbleAbove") == 0f && ElevField(g, "bubbles") == "False",
                $"W-B6 G1 the guest sees nothing bubbling in the still, full car and never a bubble above the water (particles {ElevField(g, "bubbleParticles")}, still frames {ElevField(g, "elevBubbleStill")}, above frames {ElevField(g, "elevBubbleAbove")}, bubbles={ElevField(g, "bubbles")})");

            Heading("DV4 (guest) — the guest walks down the ramp and back");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f)) + "}");
            yield return Wait(0.4f);
            // The peer's walk probe (the elevator-deck tester's, 74526e6): aim = the velocity in m/s,
            // position.x = seconds; its result rides on the "elevatorDeck:" line.
            yield return Send("{\"id\":{id},\"action\":\"walk\",\"aim\":" + Vec(doorway * 2.5f) + ",\"position\":{\"x\":3.2,\"y\":0,\"z\":0}}");
            yield return Wait(3.4f);
            yield return GuestEventually(r => WalkField(r, "walkDone") == "True", 6f, () => "DV4 G the guest's walk out finished");
            GuestWalkChecks("out", lastReply, 6f);
            yield return Send("{\"id\":{id},\"action\":\"walk\",\"aim\":" + Vec(-doorway * 2.5f) + ",\"position\":{\"x\":3.2,\"y\":0,\"z\":0}}");
            yield return Wait(3.4f);
            yield return GuestEventually(r => WalkField(r, "walkDone") == "True", 6f, () => "DV4 G the guest's walk back finished");
            GuestWalkChecks("back", lastReply, 6f);
            HQPlayerController remote = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(pc => pc.OwnerId == guestId);
            yield return Expect(() => remote != null && car.IsInsideCar(remote.transform.position + Vector3.up * 0.5f), 3f, () => "DV4 G the guest's copy is back inside the car on the host");

            Heading("DV9c — the Elevator Ghost: the green car and its ring on both peers");
            host.TeleportLocal(car.transform.position + doorway * 5f + Vector3.up * 0.05f, host.Yaw);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + doorway * 5f + Vector3.Cross(Vector3.up, doorway) * 1.5f + Vector3.up * 0.05f) + "}");
            yield return Wait(0.6f);
            Check(flow.ServerSummonGhostForChecks(out string ghostWhy), "DV9c the Ghost summoned for the checks: " + ghostWhy);
            yield return Expect(() => SunkCost.Monsters.ElevatorGhostLight.Instance != null && SunkCost.Monsters.ElevatorGhostLight.Instance.IsGreen, 3f, () => "DV9c the car's light is green on the host");
            yield return null;
            Light cabinLight = car.transform.Find("Cabin Light")?.GetComponent<Light>();
            Check(cabinLight != null && cabinLight.color.g > cabinLight.color.r * 1.5f, "DV9c the Cabin Light is green: " + (cabinLight == null ? "none" : cabinLight.color.ToString()));
            Color ring = cp.Ring.CurrentEmission;
            Check(ring.g > ring.r * 1.5f && ring.g > ring.b * 1.5f, "DV9c the car's ring light is green too (emission " + ring + ")");
            Capture(car.transform.position + doorway * 4.5f + Vector3.up * 1.7f, car.transform.position + Vector3.up * 2.4f, "dv9c-ghost-green.png");
            yield return GuestEventually(r => r.Contains("ghostGreen=True") && RingGreen(ElevField(r, "ringEmission")), 6f, () => "DV9c the guest sees the green light and the green ring");
            float ghostSeconds = SunkCost.Monsters.MonsterSettings.Get().GhostSeconds;
            yield return Expect(() => !Day.Ghost.Active, ghostSeconds + 5f, () => "DV9c the green ended by itself");
            yield return null; yield return null;
            ring = cp.Ring.CurrentEmission;
            Check(!SunkCost.Monsters.ElevatorGhostLight.Instance.IsGreen && ring.r >= ring.g * 0.9f, "DV9c the light and the ring are warm again (emission " + ring + ")");
            yield return GuestEventually(r => r.Contains("ghostGreen=False") && !RingGreen(ElevField(r, "ringEmission")), 6f, () => "DV9c the guest's ring is warm again");

            Heading("DV9d — a dead rider: the guest dies in the car; the host rides up with the body");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.Cross(Vector3.up, doorway) * 1.2f + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f)) + "}");
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"elev_reset\"}");
            yield return Send("{\"id\":{id},\"action\":\"die\"}");
            yield return Expect(() => Day.IsDead(guestId), 5f, () => "DV9d the guest died in the car");
            SunkCost.Player.PlayerBody body = null;
            yield return Expect(() => (body = SunkCost.Player.PlayerBody.FindFor(guestId, WorldScenes.Scene(WorldId.Dive))) != null && car.IsInsideCar(body.transform.position + Vector3.up * 0.25f), 4f, () => "DV9d the guest's body lies in the car");
            host.TeleportLocal(car.transform.position - Vector3.Cross(Vector3.up, doorway) * 1.2f + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f), host.Yaw);
            yield return Wait(0.5f);
            from = samples.Count;
            int deadSerial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > deadSerial, 4f, () => "DV9d the host's ride up with the body started (refusal: " + Day.LastRefusal.Text + ")");
            yield return RideUp("DV9d", captures: false);
            AnalyseMotion("DV9d", LastMove(from, upward: true));
            AnalyseDrain("DV9d", LastMove(from, upward: true), waterRows: false);
            AnalyseInvariants("DV9d", from, waterRows: false);
            Check(float.IsFinite(samples.Skip(from).Select(s => s.Level + s.CarY + s.Door + s.Gate).DefaultIfEmpty(0f).Sum()), "DV9d no NaN in the recorder with a body aboard");
            SunkCost.Player.PlayerBody bodyUp = SunkCost.Player.PlayerBody.FindFor(guestId, WorldScenes.Scene(WorldId.Sea));
            Say("DV9d the body after the ride: " + (bodyUp != null ? "on the ship at ship-local " + sea.ToShipLocal(bodyUp.transform.position).ToString("F2") + (sea.IsInDeckCabin(bodyUp.transform.position + Vector3.up * 0.25f) ? " (in the deck cabin: rode up as cargo)" : "") : "not on the ship (compare with main's rule: spectate D1/D3)"));
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("dead=True") && GuestPlayerLine(r, guestId).Contains("scene=ShipAtSea"), 20f, () => "DV9d the dead guest's object was carried to the ship, as today");
            string gd = lastReply;
            Check(!ElevatorLineOf(gd).Contains("NaN"), "DV9d the dead guest's elevator line holds no NaN: " + ElevatorLineOf(gd));

            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => GuestId() < 0, 15f, () => "the guest left");
            if (sampleError != null) Soft(false, "the host's recorder threw once: " + sampleError);
        }

        private static bool RingGreen(string field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            string[] c = field.Split('/');
            if (c.Length < 3) return false;
            float r = float.Parse(c[0], CultureInfo.InvariantCulture), gr = float.Parse(c[1], CultureInfo.InvariantCulture), b = float.Parse(c[2], CultureInfo.InvariantCulture);
            return gr > r * 1.5f && gr > b * 1.5f;
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        private static void MoveIntoCar(ElevatorController car, Vector3 flatOffset)
        {
            HQPlayerController host = Host();
            host.TeleportLocal(car.transform.position + flatOffset + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f), host.Yaw);
        }

        // The real W on the virtual keyboard until `until`; every frame's footing recorded.
        private static IEnumerator WalkRow(string label, ElevatorController car, Vector3 direction, Func<bool> until)
        {
            HQPlayerController host = Host();
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(Flat(direction)); yield return null;
            int frames = 0, grounded = 0, bigDrops = 0, airStreak = 0, longestAir = 0; var airLog = new List<string>(); float maxRise = 0f, maxDrop = 0f, worstProgress = float.PositiveInfinity;
            float lastY = host.transform.position.y; Vector3 windowStart = host.transform.position; double windowAt = EditorApplication.timeSinceStartup;
            double deadline = EditorApplication.timeSinceStartup + 14.0;
            try
            {
                while (EditorApplication.timeSinceStartup < deadline && !until())
                {
                    host.transform.rotation = Quaternion.LookRotation(Flat(direction));
                    Keys(Key.W);
                    yield return null;
                    frames++;
                    float y = host.transform.position.y, dy = y - lastY; lastY = y;
                    if (host.IsGrounded) { grounded++; airStreak = 0; }
                    else
                    {
                        airStreak++; longestAir = Mathf.Max(longestAir, airStreak);
                        if (airLog.Count < 40) airLog.Add($"r{Flat(host.transform.position - car.transform.position).magnitude:0.00}/y{y - car.transform.position.y:+0.00;-0.00}/dy{dy * 100f:+0.0;-0.0}cm");
                    }
                    if (frames > 3)
                    {
                        maxRise = Mathf.Max(maxRise, dy); maxDrop = Mathf.Max(maxDrop, -dy);
                        if (-dy > 0.05f) bigDrops++;
                    }
                    if (EditorApplication.timeSinceStartup - windowAt >= 0.5)
                    {
                        worstProgress = Mathf.Min(worstProgress, Flat(host.transform.position - windowStart).magnitude);
                        windowStart = host.transform.position; windowAt = EditorApplication.timeSinceStartup;
                    }
                }
            }
            finally { Keys(); }
            Check(until(), $"{label} reached its end ({frames} frames, at {Flat(host.transform.position - car.transform.position).magnitude:0.00} m from the axis, y {host.transform.position.y:0.00})");
            if (airLog.Count > 0) Say($"{label} frames not grounded (radius from the axis / height over the car's root / step): " + string.Join(" ", airLog));
            // Underwater the player's gravity is weak, so walking off the sill onto the 13° ramp is a
            // short glide (first run, 28 Sep: 25 frames from r 3.69 to 4.15, at most 0.6 cm a frame; the
            // sill, ramp and sand are continuous colliders). A step or a gap shows as a big drop (below)
            // or a long fall; the glide is allowed, a longer float is not.
            Check(frames > 10 && grounded >= 0.90f * frames && longestAir <= 40, $"{label} grounded on {grounded} of {frames} frames, longest airborne run {longestAir} frames (≤ 40: the underwater glide off the sill)");
            Check(maxRise <= 0.26f, $"{label} never stepped up more than the step offset in a frame ({maxRise:0.000} m)");
            Check(bigDrops <= 1 && maxDrop <= 0.2f, $"{label} never dropped more than 5 cm in a frame, bar one sill edge ≤ 20 cm ({bigDrops} drops, worst {maxDrop:0.000} m)");
            Check(worstProgress >= 0.2f || float.IsPositiveInfinity(worstProgress), $"{label} never stalled (least progress in half a second {worstProgress:0.00} m)");
        }

        private static void GuestWalkChecks(string label, string reply, float minWalk)
        {
            float Num(string name) => float.TryParse(WalkField(reply, name), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;
            float walked = Num("walked"), maxDrop = Num("walkMaxDrop"), maxRise = Num("walkMaxRise"), gFrames = Num("walkGrounded"), all = Mathf.Max(1f, Num("walkFrames"));
            Check(walked >= minWalk && gFrames >= 0.95f * all && maxRise <= 0.26f && maxDrop <= 0.2f, $"DV4 G the guest walked {label} {walked:0.00} m, grounded {gFrames}/{all} frames, worst rise {maxRise:0.000}, worst drop {maxDrop:0.000} (the peer's CharacterController walk probe)");
        }

        // DV10: face the glass at many bearings, walk into it, and at every stop the near plane
        // touches no drawn mesh of the car, its leaves, its panel or the tube's gate and glass.
        private static IEnumerator CameraRows(ElevatorController car, CarParts cp)
        {
            HQPlayerController host = Host();
            Camera cam = host.PlayerCamera;
            PlayerCameraClearance clearance = host.GetComponentInChildren<PlayerCameraClearance>(true);
            float near = PlayerCameraClearance.EnvelopeRadius(cam.nearClipPlane, cam.fieldOfView, cam.aspect, 0f);
            var roots = new List<Transform>
            {
                car.transform.Find(ShaftTubeSetup.CarLookName), car.transform.Find("Glass Shell"), car.transform.Find(ShaftTubeSetup.PanelLookName)
            };
            roots.AddRange(AllNamed(car.transform, SunkCost.Editor.Look.ElevatorLook.LeafLookName));
            roots.AddRange(AllNamed(cp.Tube, SunkCost.Editor.Look.ElevatorLook.GateLeafLookName));
            roots.Add(FindDeep(cp.Tube, SunkCost.Sites.DiveSiteBuilder.TubeGlassName));
            roots.Add(FindDeep(cp.Tube, SunkCost.Sites.ShaftTubeLook.FootName));
            Say($"DV10 near-plane envelope {near * 100f:0.0} cm; clearance envelope {(clearance == null ? -1f : clearance.LastEnvelopeRadius) * 100f:0.0} cm");
            int shot = 0;
            foreach (float bearing in new[] { 35f, 60f, 90f, 120f, 150f, 180f, -150f, -120f, -90f, -60f, -35f })
            {
                MoveIntoCar(car, Vector3.zero); yield return Wait(0.2f);
                Vector3 dir = FromDoorway(car, bearing);
                yield return PushInto(dir, 1.5f);
                foreach (float pitch in new[] { 0f, 30f, -30f })
                {
                    host.SetPitchForChecks(pitch); yield return null; yield return null;
                    float d = NearestDrawn(roots, cam.transform.position, out string what);
                    bool clear = clearance == null || PlayerCameraClearance.IsClear(cam.transform.position, clearance.LastEnvelopeRadius, host.transform);
                    Check(d >= near && clear, $"DV10 facing {bearing:+0;-0}° from the doorway, pitch {pitch:0}: the near plane is {d * 100f:0.0} cm from the nearest drawn mesh ({what}), envelope {near * 100f:0.0} cm; clearance clear={clear}");
                }
                if (bearing == 90f || bearing == 35f) { host.SetPitchForChecks(0f); yield return null; CaptureEye("dv10-glass-press-" + (++shot) + ".png"); }
            }
            // From the tube's doorway (on the sill, between the car's doorway and the open gate),
            // walk straight at each open gate leaf until the colliders stop the player, face it,
            // and judge the near plane there. DIVE-DV10-GATE-ROWS: the first version started at
            // r 2.95 (the capsule already in the tube wall's band) and pushed along a straight
            // tangent, so the player slid out through the doorway onto the ramp and ended about
            // 2 m from every mesh: the rows passed without ever meeting a leaf. The coverage row
            // below fails if the camera does not end up at the leaf, in view.
            Vector3 doorway = Doorway(car);
            float standY = SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f;
            foreach (Transform leaf in AllNamed(cp.Tube, SunkCost.Editor.Look.ElevatorLook.GateLeafLookName).ToList())
            {
                string side = leaf.parent != null ? leaf.parent.name : leaf.name;
                string file = "dv10-" + side.ToLowerInvariant().Replace(' ', '-') + ".png";
                var leafRoots = new[] { leaf };
                host.TeleportLocal(car.transform.position + doorway * GateRowStartRadius + Vector3.up * standY, host.Yaw);
                yield return Wait(0.2f);
                NearestDrawn(leafRoots, cam.transform.position, out _, out Vector3 aim);
                yield return PushInto(aim - cam.transform.position, 1.5f);
                float dLeaf = NearestDrawn(leafRoots, cam.transform.position, out _, out Vector3 onLeaf);
                host.transform.rotation = Quaternion.LookRotation(Flat(onLeaf - cam.transform.position)); host.SetPitchForChecks(0f);
                yield return null; yield return null;
                dLeaf = NearestDrawn(leafRoots, cam.transform.position, out _, out onLeaf);
                Vector3 vp = cam.WorldToViewportPoint(onLeaf);
                bool inView = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                float r = Flat(cam.transform.position - car.transform.position).magnitude;
                Check(cp.Gate.OpenFraction >= 0.99f && dLeaf <= GateRowReach && inView,
                    $"DV10 the push from the tube's doorway reached the open {side} (gate {cp.Gate.OpenFraction:0.00}; camera {dLeaf * 100f:0.0} cm from the leaf, ≤ {GateRowReach * 100f:0} cm; r {r:0.00} m from the axis; the leaf in view {inView})");
                float d = NearestDrawn(roots, cam.transform.position, out string what);
                bool clear = clearance == null || PlayerCameraClearance.IsClear(cam.transform.position, clearance.LastEnvelopeRadius, host.transform);
                Check(d >= near && clear, $"DV10 pressed against the open {side}: the near plane is {d * 100f:0.0} cm from the nearest drawn mesh ({what}), envelope {near * 100f:0.0} cm; clearance clear={clear}");
                CaptureEye(file);
            }
        }

        // DV10's gate rows: the start on the sill, midway between the car's doorway (r 2.4-2.5)
        // and the gate leaves' inner face (r 2.69), so the 0.3 m capsule stands clear of both; and
        // how near the camera must come to a leaf for the row to count as a test of that leaf.
        private const float GateRowStartRadius = 2.72f;
        private const float GateRowReach = 0.6f;

        private static IEnumerator PushInto(Vector3 direction, float seconds)
        {
            HQPlayerController host = Host();
            host.SetPitchForChecks(0f);
            double until = EditorApplication.timeSinceStartup + seconds;
            try
            {
                while (EditorApplication.timeSinceStartup < until)
                {
                    host.transform.rotation = Quaternion.LookRotation(Flat(direction));
                    Keys(Key.W);
                    yield return null;
                }
            }
            finally { Keys(); }
            yield return null;
        }

        // DV12: stand before the car's panel, aim at its button through the real targeting, see
        // the prompt, press a virtual E; the ride up starts.
        private static IEnumerator PressCarPanel(ElevatorController car, PlayerHudUI hud)
        {
            HQPlayerController host = Host();
            ElevatorControlPanel panel = car.GetComponentInChildren<ElevatorControlPanel>(true);
            Check(panel != null, "DV12 the car has its control panel");
            Vector3 toPanel = Flat(panel.transform.position - car.transform.position);
            MoveIntoCar(car, toPanel.normalized * Mathf.Max(0f, toPanel.magnitude - 1.1f));
            yield return Wait(0.4f);
            double deadline = EditorApplication.timeSinceStartup + 3.0;
            while (EditorApplication.timeSinceStartup < deadline && host.CurrentCabinControl != CabinControl.Car)
            {
                Vector3 to = panel.transform.position - host.EyePosition;
                Vector3 flat = Flat(to);
                host.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
                host.SetPitchForChecks(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
                host.RefreshTarget();
                yield return null;
            }
            Check(host.CurrentCabinControl == CabinControl.Car, "DV12 the dot is on the car's panel button (target " + host.CurrentCabinControl + ")");
            yield return null;
            Check(hud.PromptText.Contains("Press E to surface"), "DV12 the prompt offers the ride up: " + hud.PromptText);
            int serial = Day.CabinRide.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.CabinRide.Serial > serial && Day.CabinRide.Direction == RideDirection.Up, 3f, () => "DV12 a virtual E on the panel started the ride up (refusal: " + Day.LastRefusal.Text + ")");
            TextMesh screen = car.GetComponentsInChildren<TextMesh>(true).FirstOrDefault(t => t.name == CabinPanelDisplay.ScreenTextName);
            yield return Expect(() => screen != null && screen.text.StartsWith("SURFACE"), 2f, () => "DV12 the panel's screen says SURFACE: " + (screen == null ? "no screen" : screen.text.Replace("\n", " | ")));
        }
    }
}
