#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FishNet;
using FishNet.Managing.Timing;
using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Net
{
    // The elevator as THIS peer presents it, every frame, for the elevator-net matrix
    // (the new elevator, 28 September 2026; ElevatorNetRuntimeChecks): the car, its doors,
    // the tube's gate, the car's water and everything drawn from it, the deck cabin's
    // doors and shutters on the ship at sea, what the local camera sees (a spectator's
    // camera sits on its target's eyes) and what the ship TV renders. Two tick stamps:
    // CarTick is the tick the car was driven at (the phase's start tick plus the car's own
    // elapsed time), Tick the network tick at this LateUpdate. Two peers that agree show
    // the same values at the same tick; Wall (the machine's clock) lets the matrix measure
    // how far apart two processes' ticks are at one moment.
    // A self-check recorder compares, every frame, what is presented with what the
    // replicated phase says (the per-frame guest check without 60 Hz snapshot traffic),
    // and the first driven frame after the car or the ship appears (a late loader sees the
    // state at once, no replayed animation). On the host the matrix keeps every frame's
    // sample (StartRecording) to compare a guest's reply at the guest's own tick.
    // Development builds and the editor only, like InventoryVerificationPeer, which adds
    // one to every guest and prints Line/RecorderLine in its snapshot.
    [DefaultExecutionOrder(1000)] // after CabinWater (0), the water visuals and the panel (100) and the TV (500)
    public sealed class ElevatorNetProbe : MonoBehaviour
    {
        public struct Sample
        {
            public long Wall;           // DateTime.UtcNow.Ticks at the start of this frame: one clock for every process on the machine
            public float FrameLag;      // seconds from the frame's start to this LateUpdate (subtracted from Wall)
            public float SinceStall;     // seconds since this peer's last frame over 0.1 s (the network tick catches up after one)
            public double Tick;         // network tick + the fraction into it, at this LateUpdate
            public int Rate;            // ticks per second
            public int Serial;          // CrewDayState.Elevator.Serial (also without a car)
            // The car (in the dive scene this peer holds, if any).
            public bool Car, Driven, Torn;
            public ElevatorState State;
            public bool Upward;
            public uint StartTick;
            public double CarTick;
            public float CarY, Sea, Span, Water, SurfaceY;
            public bool Shown;
            public CarWaterFlow Flow;
            public int Pour, Visible;
            public bool Bubbles;
            public float Foam, Drain, Gauge, Door, Gate;
            public bool GateBlocks, Ring;
            public string CarScreen;
            // The deck cabin on the ship at sea.
            public bool Ship;
            public int RideSerial;
            public CabinRideStage RideStage;
            public float DeckDoors, DoorYaw, ShutterYaw;
            public bool CarShown, ShutterBlocks, DoorwayBlocks;
            public string DeckScreen;
            // The local player's own camera (a spectator's sits on its target's eyes).
            public bool Local;
            public int Id;
            public float EyeY, View;
            public bool EyeUnder, InCar, Dead;
            public int Spectating;
            // The ship TV at sea.
            public int Tv;
            public bool TvLive;
            public float TvGrade, TvEyeY;
            public int TvFrames;
        }

        public static Sample Last { get; private set; }

        // ---- the host's record (the matrix compares a guest's reply against it) ----------

        private static readonly List<Sample> recorded = new();
        private static readonly List<ElevatorPhase> phases = new();
        private static bool recording;
        private static int lastPhaseSerial = int.MinValue;
        private const long KeepWallTicks = 180L * TimeSpan.TicksPerSecond;
        public static IReadOnlyList<Sample> Recorded => recorded;
        public static IReadOnlyList<ElevatorPhase> PhaseLog => phases;
        public static void StartRecording() { recorded.Clear(); phases.Clear(); lastPhaseSerial = int.MinValue; recording = true; }
        public static void StopRecording() { recording = false; recorded.Clear(); phases.Clear(); }

        // ---- the per-frame self-check -------------------------------------------------------

        public static int Frames, Torn, Undriven, FirstLoads, FirstShips, ShutterBlockMismatch;
        public static float WorstY, WorstWater, WorstGauge, WorstGate, WorstDoorShut, WorstShutter, FirstErr, FirstShipErr;
        public static string FirstStates = string.Empty;
        private static bool hadDrivenCar, hadShip;

        public static void ResetRecorder()
        {
            Frames = Torn = Undriven = FirstLoads = FirstShips = ShutterBlockMismatch = 0;
            WorstY = WorstWater = WorstGauge = WorstGate = WorstDoorShut = WorstShutter = FirstErr = FirstShipErr = 0f;
            FirstStates = string.Empty;
            // hadDrivenCar/hadShip are kept: a reset is not a load.
        }

        private void LateUpdate()
        {
            Sample s = Capture();
            Last = s;
            SelfCheck(s);
            if (!recording) return;
            recorded.Add(s);
            CrewDayState day = CrewDayState.Instance;
            if (day != null && day.Elevator.Serial != lastPhaseSerial) { lastPhaseSerial = day.Elevator.Serial; phases.Add(day.Elevator); }
            if (recorded.Count % 600 == 0)
            {
                long keepFrom = s.Wall - KeepWallTicks;
                int cut = recorded.FindIndex(x => x.Wall >= keepFrom);
                if (cut > 0) recorded.RemoveRange(0, cut);
            }
        }

        // ---- what this peer presents ------------------------------------------------------

        private static ElevatorController car;
        private static CabinWater water;
        private static CabinWaterVisuals visuals;
        private static CabinPanelDisplay carPanel;
        private static ElevatorDoor door;
        private static ShaftGate gate;
        private static Collider gateCollider;
        private static TubeWaterSurface tubeWater;
        private static ShipParts ship;
        private static Transform doorR, housingR, carGlass, tvCamera;
        private static Renderer[] carGlassRenderers = new Renderer[0];
        private static Collider shutterCollider, doorwayCollider;
        private static CabinPanelDisplay deckPanel;
        private static ShipTV tv;
        private static HQPlayerController local;
        private static PlayerSubmersion submersion;
        private static SunkCost.Sites.UnderwaterGrade[] grades = new SunkCost.Sites.UnderwaterGrade[0];
        private static int gradesFrame = -1000;

        private static double lastStall = -1000.0;

        public static Sample Capture()
        {
            // The network tick is advanced at the frame's start; a long frame (a scene load, a
            // warm-up render) would otherwise pair a frame-start tick with a frame-end clock.
            double inFrame = Math.Max(0.0, Time.realtimeSinceStartupAsDouble - Time.unscaledTimeAsDouble);
            if (Time.unscaledDeltaTime > 0.1f) lastStall = Time.unscaledTimeAsDouble;
            var s = new Sample { Wall = DateTime.UtcNow.Ticks - (long)(inFrame * TimeSpan.TicksPerSecond), FrameLag = (float)inFrame, SinceStall = (float)Math.Min(999.0, Time.unscaledTimeAsDouble - lastStall), Serial = -1, RideSerial = -1, Id = -1, Spectating = -1, Tv = -1, CarScreen = string.Empty, DeckScreen = string.Empty };
            TimeManager time = InstanceFinder.TimeManager;
            if (time != null)
            {
                s.Rate = time.TickRate;
                s.Tick = time.Tick + time.GetTickElapsedAsDouble() * time.TickRate;
            }
            CrewDayState day = CrewDayState.Instance;
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            if (day != null) { s.Serial = day.Elevator.Serial; s.RideSerial = day.CabinRide.Serial; s.RideStage = day.CabinRide.Stage; s.State = day.Elevator.State; s.Upward = day.Elevator.Upward; s.StartTick = day.Elevator.StartTick; }

            ElevatorController found = WorldSceneFlow.FindCarCached();
            if (found != car) BindCar(found);
            if (car != null)
            {
                s.Car = true;
                s.Driven = car.Driven;
                s.Torn = day == null || day.Elevator.State != car.State || day.Elevator.Upward != car.Upward;
                s.State = car.State;
                s.Upward = car.Upward;
                s.CarTick = s.StartTick + (double)car.StateElapsed * s.Rate;
                s.CarY = car.transform.position.y;
                s.Sea = car.SeaLevelY;
                s.Span = car.SpanMeters;
                if (water != null) { s.Water = water.LevelMeters; s.SurfaceY = water.SurfaceWorldY; s.Shown = water.SurfaceShown; s.Flow = water.Flow; }
                if (visuals != null) { s.Pour = visuals.ActiveStreams; s.Visible = visuals.VisibleStreams; s.Bubbles = visuals.Bubbles; s.Foam = visuals.Foam01; s.Drain = visuals.Drain01; }
                if (carPanel != null) { s.Gauge = carPanel.GaugeFraction; s.CarScreen = carPanel.ScreenText ?? string.Empty; }
                s.Door = door != null ? door.OpenFraction : -1f;
                s.Gate = gate != null ? gate.OpenFraction : -1f;
                s.GateBlocks = gateCollider != null && gateCollider.enabled;
                s.Ring = tubeWater != null && tubeWater.RingShown;
            }

            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            if (sea != ship) BindShip(sea);
            if (ship != null && doorR != null)
            {
                s.Ship = true;
                s.DeckDoors = flow != null ? flow.DeckCabinOpenFraction() : -1f;
                s.DoorYaw = Mathf.DeltaAngle(0f, doorR.localEulerAngles.y);
                s.ShutterYaw = housingR != null ? Mathf.DeltaAngle(0f, housingR.localEulerAngles.y) : 0f;
                s.CarShown = false;
                foreach (Renderer r in carGlassRenderers) if (r != null && r.enabled) { s.CarShown = true; break; }
                s.ShutterBlocks = shutterCollider != null && shutterCollider.enabled;
                s.DoorwayBlocks = doorwayCollider != null && doorwayCollider.enabled;
                s.DeckScreen = deckPanel != null ? deckPanel.ScreenText ?? string.Empty : string.Empty;
                if (tv != null)
                {
                    s.Tv = tv.Channel;
                    s.TvLive = tv.Live;
                    s.TvGrade = tv.PictureGradeWeight;
                    s.TvFrames = tv.RenderedFrames;
                    if (tvCamera == null) tvCamera = tv.transform.Find("TvCamera");
                    s.TvEyeY = tvCamera != null ? tvCamera.position.y : float.NaN;
                }
            }

            HQPlayerController me = WorldSceneFlow.LocalPlayer();
            if (me != local) { local = me; submersion = me != null ? me.GetComponent<PlayerSubmersion>() : null; }
            if (local != null && local.PlayerCamera != null)
            {
                s.Local = true;
                s.Id = local.OwnerId;
                Vector3 eye = local.PlayerCamera.transform.position;
                s.EyeY = eye.y;
                s.EyeUnder = submersion != null && submersion.IsSubmerged;
                s.Dead = local.IsDead;
                s.InCar = car != null && car.IsInsideCar(local.transform.position + Vector3.up * 0.5f);
                s.Spectating = local.Spectator != null && local.Spectator.Active && local.Spectator.Target != null ? local.Spectator.Target.OwnerId : -1;
                if (Time.frameCount - gradesFrame > 30) { grades = FindObjectsByType<SunkCost.Sites.UnderwaterGrade>(FindObjectsInactive.Exclude); gradesFrame = Time.frameCount; }
                float view = 0f;
                foreach (SunkCost.Sites.UnderwaterGrade g in grades) if (g != null && g.isActiveAndEnabled) view = Mathf.Max(view, g.WeightAt(eye));
                s.View = view;
            }
            return s;
        }

        private static void BindCar(ElevatorController found)
        {
            car = found; water = null; visuals = null; carPanel = null; door = null; gate = null; gateCollider = null; tubeWater = null;
            gradesFrame = -1000;
            if (car == null) return;
            water = car.GetComponent<CabinWater>();
            visuals = car.GetComponentInChildren<CabinWaterVisuals>(true);
            foreach (CabinPanelDisplay p in car.GetComponentsInChildren<CabinPanelDisplay>(true))
                if (p.DisplayMode == CabinPanelDisplay.Mode.Car) { carPanel = p; break; }
            door = car.GetComponentInChildren<ElevatorDoor>(true); // the car's own door is its first (the matrices' rule)
            foreach (ShaftGate g in FindObjectsByType<ShaftGate>(FindObjectsInactive.Include))
                if (g.gameObject.scene == car.gameObject.scene) { gate = g; break; }
            if (gate != null)
                foreach (Collider c in gate.GetComponentsInChildren<Collider>(true))
                    if (c.name == "Gate Collider") { gateCollider = c; break; }
            foreach (TubeWaterSurface t in FindObjectsByType<TubeWaterSurface>(FindObjectsInactive.Include))
                if (t.gameObject.scene == car.gameObject.scene) { tubeWater = t; break; }
        }

        private static void BindShip(ShipParts found)
        {
            ship = found; doorR = housingR = carGlass = tvCamera = null; carGlassRenderers = new Renderer[0];
            shutterCollider = doorwayCollider = null; deckPanel = null; tv = null;
            if (ship == null) return;
            doorR = ship.DeckCabinDoorR;
            housingR = ship.DeckCabinHousingDoorR;
            carGlass = ship.DeckCabinCarGlass;
            if (carGlass != null)
            {
                carGlassRenderers = carGlass.GetComponentsInChildren<Renderer>(true);
                deckPanel = carGlass.GetComponentInChildren<CabinPanelDisplay>(true);
            }
            shutterCollider = ship.DeckCabinShutterCollider;
            doorwayCollider = ship.DeckCabinDoorCollider;
            tv = ship.GetComponent<ShipTV>();
        }

        // ---- the self-check ---------------------------------------------------------------

        private static float ExpectedCarY(ElevatorController c, ElevatorState state, bool upward, float elapsed)
        {
            float p = state switch
            {
                ElevatorState.AtTop => 0f,
                ElevatorState.AtBottom => 1f,
                ElevatorState.Sealing => upward ? 1f : 0f,
                ElevatorState.Descending => ElevatorMath.ProgressAt(c.Profile, elapsed, false),
                ElevatorState.Ascending => ElevatorMath.ProgressAt(c.Profile, elapsed, true),
                _ => c.Progress
            };
            return Mathf.Lerp(c.TopPosition.y, c.BottomPosition.y, p);
        }

        // The car door as a peer that has just loaded the car shows it (ElevatorDoor with
        // no history: openFrom 0, sealFrom 1), driven by the network.
        private static float FreshDoor(ElevatorController c, ElevatorState state, bool upward, float elapsed)
        {
            float seal = Mathf.Max(c.DoorSealSeconds, 0.0001f);
            if (state == ElevatorState.AtTop || (state == ElevatorState.Sealing && !upward)) return 0f;
            return state switch
            {
                ElevatorState.AtBottom => Mathf.Clamp01(elapsed / seal),
                ElevatorState.Sealing => Mathf.Clamp01(1f - elapsed / seal),
                _ => 0f
            };
        }

        private static float prevGateTarget;

        private static void SelfCheck(Sample s)
        {
            Frames++;
            CrewDayState day = CrewDayState.Instance;
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            if (s.Car && car != null && day != null)
            {
                if (!s.Driven) Undriven++;
                else if (s.Torn) Torn++;
                else
                {
                    ElevatorPhase phase = day.Elevator;
                    float elapsed = flow != null ? flow.ElapsedSince(phase.StartTick) : car.StateElapsed;
                    float errY = Mathf.Abs(s.CarY - ExpectedCarY(car, phase.State, phase.Upward, elapsed));
                    float errWater = water == null ? 0f : Mathf.Abs(s.Water - ElevatorMath.WaterLevelInCar(car.SeaLevelY, s.CarY, car.SpanMeters));
                    float errGauge = carPanel == null || water == null ? 0f : Mathf.Abs(s.Gauge - water.Level01);
                    bool atBottom = s.State == ElevatorState.AtBottom || (s.State == ElevatorState.Sealing && s.Upward);
                    // ShaftGate reads the door's fraction from the door's own Update with no set script
                    // order, so the gate may show this frame's or the previous frame's target (a one-frame
                    // lag, largest on a slow headless frame): either counts (round 1 fix to the test).
                    float gateTarget = atBottom ? s.Door : 0f;
                    float errGate = gate == null || door == null ? 0f : Mathf.Min(Mathf.Abs(s.Gate - gateTarget), Mathf.Abs(s.Gate - prevGateTarget));
                    prevGateTarget = gateTarget;
                    bool mustBeShut = s.State == ElevatorState.Descending || s.State == ElevatorState.Ascending || s.State == ElevatorState.AtTop || (s.State == ElevatorState.Sealing && !s.Upward);
                    float errShut = mustBeShut && door != null ? Mathf.Max(0f, s.Door) : 0f;
                    WorstY = Mathf.Max(WorstY, errY);
                    WorstWater = Mathf.Max(WorstWater, errWater);
                    WorstGauge = Mathf.Max(WorstGauge, errGauge);
                    WorstGate = Mathf.Max(WorstGate, errGate);
                    WorstDoorShut = Mathf.Max(WorstDoorShut, errShut);
                    if (!hadDrivenCar)
                    {
                        // The first driven frame after the car appeared on this peer.
                        float fresh = FreshDoor(car, s.State, s.Upward, car.StateElapsed);
                        float errDoor = door == null ? 0f : Mathf.Abs(s.Door - fresh);
                        float errFreshGate = gate == null ? 0f : Mathf.Abs(s.Gate - (atBottom ? fresh : 0f));
                        FirstLoads++;
                        FirstErr = Mathf.Max(FirstErr, Mathf.Max(Mathf.Max(errY, errWater), Mathf.Max(errDoor, errFreshGate)));
                        if (FirstStates.Length < 120) FirstStates += (FirstStates.Length > 0 ? "+" : string.Empty) + s.State + (s.Upward ? "Up" : "Down");
                    }
                    hadDrivenCar = true;
                }
            }
            else hadDrivenCar = false;

            if (s.Ship)
            {
                // Present: the shutters mirror the car's deck doors; away: shut.
                float errShutter = s.CarShown ? Mathf.Abs(Mathf.DeltaAngle(s.ShutterYaw, s.DoorYaw)) : Mathf.Abs(s.ShutterYaw);
                WorstShutter = Mathf.Max(WorstShutter, errShutter);
                if (shutterCollider != null && ((s.ShutterBlocks && Mathf.Abs(s.ShutterYaw) > 0.1f) || (!s.ShutterBlocks && Mathf.Abs(s.ShutterYaw) < 0.01f))) ShutterBlockMismatch++;
                if (!hadShip) { FirstShips++; FirstShipErr = Mathf.Max(FirstShipErr, errShutter); }
                hadShip = true;
            }
            else hadShip = false;
        }

        // ---- the snapshot lines -----------------------------------------------------------

        private static string B(bool v) => v ? "1" : "0";
        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        private static string Text(string v) => (v ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " | ").Replace("'", "’");

        public static string Line(Sample s)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var b = new StringBuilder("elevnet: ", 900);
            b.Append("wall=").Append(s.Wall.ToString(c)).Append("; frameLag=").Append(F(s.FrameLag, "0.000")).Append("; sinceStall=").Append(F(s.SinceStall, "0.00")).Append("; tick=").Append(s.Tick.ToString("0.000", c)).Append("; rate=").Append(s.Rate.ToString(c));
            b.Append("; serial=").Append(s.Serial.ToString(c)).Append("; car=").Append(B(s.Car)).Append("; driven=").Append(B(s.Driven)).Append("; torn=").Append(B(s.Torn));
            b.Append("; state=").Append(s.State).Append("; up=").Append(B(s.Upward)).Append("; start=").Append(s.StartTick.ToString(c)).Append("; carTick=").Append(s.CarTick.ToString("0.000", c));
            b.Append("; carY=").Append(F(s.CarY, "0.0000")).Append("; sea=").Append(F(s.Sea, "0.000")).Append("; span=").Append(F(s.Span, "0.000"));
            b.Append("; water=").Append(F(s.Water, "0.0000")).Append("; surfY=").Append(F(s.SurfaceY, "0.0000")).Append("; shown=").Append(B(s.Shown)).Append("; flow=").Append(s.Flow);
            b.Append("; pour=").Append(s.Pour.ToString(c)).Append("; vis=").Append(s.Visible.ToString(c)).Append("; bubbles=").Append(B(s.Bubbles));
            b.Append("; foam=").Append(F(s.Foam, "0.000")).Append("; drain=").Append(F(s.Drain, "0.000")).Append("; gauge=").Append(F(s.Gauge, "0.0000"));
            b.Append("; door=").Append(F(s.Door, "0.0000")).Append("; gate=").Append(F(s.Gate, "0.0000")).Append("; gateBlocks=").Append(B(s.GateBlocks)).Append("; ring=").Append(B(s.Ring));
            b.Append("; ship=").Append(B(s.Ship)).Append("; rideSerial=").Append(s.RideSerial.ToString(c)).Append("; rideStage=").Append(s.RideStage);
            b.Append("; deckDoors=").Append(F(s.DeckDoors, "0.0000")).Append("; doorYaw=").Append(F(s.DoorYaw, "0.00")).Append("; shutterYaw=").Append(F(s.ShutterYaw, "0.00"));
            b.Append("; carShown=").Append(B(s.CarShown)).Append("; shutterBlocks=").Append(B(s.ShutterBlocks)).Append("; doorwayBlocks=").Append(B(s.DoorwayBlocks));
            b.Append("; local=").Append(B(s.Local)).Append("; id=").Append(s.Id.ToString(c)).Append("; eyeY=").Append(F(s.EyeY, "0.000")).Append("; eyeUnder=").Append(B(s.EyeUnder));
            b.Append("; view=").Append(F(s.View, "0.00")).Append("; inCar=").Append(B(s.InCar)).Append("; dead=").Append(B(s.Dead)).Append("; spectating=").Append(s.Spectating.ToString(c));
            b.Append("; tv=").Append(s.Tv.ToString(c)).Append("; tvLive=").Append(B(s.TvLive)).Append("; tvGrade=").Append(F(s.TvGrade, "0.00"));
            b.Append("; tvEyeY=").Append(float.IsNaN(s.TvEyeY) ? "nan" : F(s.TvEyeY, "0.000")).Append("; tvFrames=").Append(s.TvFrames.ToString(c));
            b.Append("; carScreen='").Append(Text(s.CarScreen)).Append("'; deckScreen='").Append(Text(s.DeckScreen)).Append("'");
            return b.ToString();
        }

        public static string RecorderLine()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "elevnetrec: frames={0}; torn={1}; undriven={2}; worstY={3:0.0000}; worstWater={4:0.0000}; worstGauge={5:0.0000}; worstGate={6:0.0000}; worstDoorShut={7:0.0000}; worstShutter={8:0.00}; shutterBlockMismatch={9}; firstLoads={10}; firstErr={11:0.0000}; firstStates={12}; firstShips={13}; firstShipErr={14:0.00}",
                Frames, Torn, Undriven, WorstY, WorstWater, WorstGauge, WorstGate, WorstDoorShut, WorstShutter, ShutterBlockMismatch, FirstLoads, FirstErr, FirstStates.Length > 0 ? FirstStates : "none", FirstShips, FirstShipErr);
        }

        // ---- reading a line back (the matrix, on the host) -----------------------------------

        private static readonly Regex Quoted = new(@"(\w+)='([^']*)'");
        private static readonly Regex Pair = new(@"(\w+)=([^;']*)");

        public static Dictionary<string, string> Fields(string reply, string prefix)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(reply)) return d;
            string line = null;
            foreach (string l in reply.Split('\n')) if (l.StartsWith(prefix, StringComparison.Ordinal)) { line = l.Substring(prefix.Length); break; }
            if (line == null) return d;
            foreach (Match m in Quoted.Matches(line)) d[m.Groups[1].Value] = m.Groups[2].Value;
            string rest = Quoted.Replace(line, string.Empty);
            foreach (Match m in Pair.Matches(rest)) if (!d.ContainsKey(m.Groups[1].Value)) d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            return d;
        }

        public static bool TryParse(string reply, out Sample s)
        {
            s = default;
            Dictionary<string, string> d = Fields(reply, "elevnet: ");
            if (!d.ContainsKey("wall")) return false;
            CultureInfo c = CultureInfo.InvariantCulture;
            string Get(string k) => d.TryGetValue(k, out string v) ? v : string.Empty;
            float Fl(string k) => Get(k) == "nan" ? float.NaN : float.TryParse(Get(k), NumberStyles.Float, c, out float v) ? v : float.NaN;
            double Db(string k) => double.TryParse(Get(k), NumberStyles.Float, c, out double v) ? v : double.NaN;
            int In(string k) => int.TryParse(Get(k), NumberStyles.Integer, c, out int v) ? v : -1;
            bool Bo(string k) => Get(k) == "1";
            long.TryParse(Get("wall"), NumberStyles.Integer, c, out s.Wall);
            s.FrameLag = Fl("frameLag"); s.SinceStall = Fl("sinceStall"); s.Tick = Db("tick"); s.Rate = In("rate"); s.Serial = In("serial");
            s.Car = Bo("car"); s.Driven = Bo("driven"); s.Torn = Bo("torn");
            Enum.TryParse(Get("state"), out s.State); s.Upward = Bo("up");
            uint.TryParse(Get("start"), NumberStyles.Integer, c, out s.StartTick); s.CarTick = Db("carTick");
            s.CarY = Fl("carY"); s.Sea = Fl("sea"); s.Span = Fl("span"); s.Water = Fl("water"); s.SurfaceY = Fl("surfY"); s.Shown = Bo("shown");
            Enum.TryParse(Get("flow"), out s.Flow); s.Pour = In("pour"); s.Visible = In("vis"); s.Bubbles = Bo("bubbles");
            s.Foam = Fl("foam"); s.Drain = Fl("drain"); s.Gauge = Fl("gauge"); s.Door = Fl("door"); s.Gate = Fl("gate");
            s.GateBlocks = Bo("gateBlocks"); s.Ring = Bo("ring"); s.CarScreen = Get("carScreen");
            s.Ship = Bo("ship"); s.RideSerial = In("rideSerial"); Enum.TryParse(Get("rideStage"), out s.RideStage);
            s.DeckDoors = Fl("deckDoors"); s.DoorYaw = Fl("doorYaw"); s.ShutterYaw = Fl("shutterYaw");
            s.CarShown = Bo("carShown"); s.ShutterBlocks = Bo("shutterBlocks"); s.DoorwayBlocks = Bo("doorwayBlocks"); s.DeckScreen = Get("deckScreen");
            s.Local = Bo("local"); s.Id = In("id"); s.EyeY = Fl("eyeY"); s.EyeUnder = Bo("eyeUnder"); s.View = Fl("view");
            s.InCar = Bo("inCar"); s.Dead = Bo("dead"); s.Spectating = In("spectating");
            s.Tv = In("tv"); s.TvLive = Bo("tvLive"); s.TvGrade = Fl("tvGrade"); s.TvEyeY = Fl("tvEyeY"); s.TvFrames = In("tvFrames");
            return true;
        }

        // The host's own screens go through the same text normaliser as a guest's line.
        public static string Normalised(string screen) => Text(screen);
    }
}
#endif
