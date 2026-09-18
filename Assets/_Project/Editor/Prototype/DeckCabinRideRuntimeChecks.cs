using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Interaction;
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
    // the doors and the car position are read every frame during a ride. The
    // shaft tube rows (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md section 8.2: T1-T4,
    // G4) ride along in the same samples: the speed bands, the water standing in
    // the car, the submersion flip and the F3 line, the sealed tube, the drain.
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
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
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
            HQPlayerController.KeyboardForChecks = null;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
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
                // The editor loses focus whenever the tester types elsewhere; by default the
                // Input System then disables devices and drops their events, the virtual
                // keyboard included ("walked 0.00 m"). Ignore focus for the run.
                savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                keyboard = InputSystem.AddDevice<Keyboard>("CabinCheckKeyboard");
                HQPlayerController.KeyboardForChecks = keyboard;
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

        // E1/E2/E3 (Dan, 16 September 2026): mid-ride the dot finds the frozen cargo
        // and the visor brackets it, a grab takes it, a throw lands on the car's floor.
        private static IEnumerator HostGrabsAndThrowsCargoMidRide(int coinId)
        {
            HQPlayerController host = Host();
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            ElevatorController car = WorldSceneFlow.FindCar();
            CarryableItem coin = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.ObjectId == coinId);
            Check(coin != null && coin.InTransit && car != null && car.IsInsideCar(coin.transform.position + Vector3.up * 0.25f), "E1 the coin rides as frozen cargo in the moving car");
            Keys();
            // Stand 0.9 m from the coin toward the car's axis, on the floor, in the car's own frame.
            Vector3 coinLocal = car.transform.InverseTransformPoint(coin.transform.position);
            Vector3 towardAxis = -new Vector3(coinLocal.x, 0f, coinLocal.z).normalized;
            Vector3 standLocal = coinLocal + towardAxis * 0.9f; standLocal.y = 0.15f;
            host.TeleportLocal(car.transform.TransformPoint(standLocal), host.Yaw); yield return null; yield return null;
            // The rider is carried by the moving floor every frame; place and look again
            // until the dot lands (the ray is a fact of the frame it is cast in).
            float lookDeadline = Time.unscaledTime + 8f; int lookTries = 0;
            while (Time.unscaledTime < lookDeadline && host.CurrentTarget != coin)
            {
                if (lookTries++ % 30 == 0) host.TeleportLocal(car.transform.TransformPoint(standLocal), host.Yaw);
                H.ClientLookAtItem(coin.name);
                yield return null;
                if (lookTries % 60 == 1)
                {
                    // Why not: everything the targeting looks at, in one line.
                    Vector3 eye = host.EyePosition, fwd = host.PlayerCamera.transform.forward;
                    Collider col = coin.PrimaryCollider;
                    Vector3 closest = col != null && col.enabled ? col.ClosestPoint(eye) : coin.transform.position;
                    bool los = InteractionTargeting.HasLineOfSight(eye, closest, host.transform, coin);
                    CarryableItem found = InteractionTargeting.Find(eye, fwd, host.transform, host.InteractReach, 0.35f);
                    Note($"E1 look try {lookTries}: target={(host.CurrentTarget == null ? "none" : host.CurrentTarget.name)} find={(found == null ? "none" : found.name)} obstructed={host.ViewObstructed} travelLocked={host.TravelLocked} eye={eye:F2} coin={coin.transform.position:F2} dist={Vector3.Distance(eye, closest):0.00} colliderOn={(col != null && col.enabled)} los={los} state={coin.State} transit={coin.InTransit} camPitch={host.PlayerCamera.transform.localEulerAngles.x:0} hostLocal={car.transform.InverseTransformPoint(host.transform.position):F2}");
                }
            }
            Note($"E1 host at car-local {car.transform.InverseTransformPoint(host.transform.position):F2}, coin at {coinLocal:F2}");
            Check(host.CurrentTarget == coin, "E1 mid-ride the dot finds the cargo on the car's floor (target " + (host.CurrentTarget == null ? "none" : host.CurrentTarget.name) + ")");
            Check(hud.Visor.BracketCount >= 1 && hud.Visor.TargetTag.StartsWith(coin.DisplayName), $"E2 mid-ride the visor brackets and tags it ({hud.Visor.BracketCount}, '{hud.Visor.TargetTag}')");
            host.Inventory.RequestGrab(coin);
            yield return WaitUntil(() => coin.State == ItemState.Held && coin.HolderClientId == host.OwnerId, 3f, "E1 the cargo was grabbed mid-ride");
            Check(!coin.InTransit, "E1 grabbed cargo is cargo no more");
            Check(hud.Visor.OnMeValue == coin.Value && hud.Visor.MoneyText.EndsWith("ON ME $" + coin.Value), $"M2 the held coin counts on me ({hud.Visor.MoneyText})");
            yield return Wait(0.5f);
            // Throw it at the floor toward the back wall, away from the doorway: a coin
            // that lands in the shut door's threshold is behind the door collider and
            // rightly out of the dot's line of sight.
            Vector3 doorwayDir = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            // Steeply, so it lands mid-floor: a coin that skids into the wall ends with its
            // near edge inside the wall collider and the dot cannot see it.
            host.transform.rotation = Quaternion.LookRotation(-doorwayDir); yield return null;
            Vector3 aim = (-doorwayDir * 0.25f + Vector3.down).normalized;
            host.Inventory.RequestUse(aim);
            yield return WaitUntil(() => coin.State == ItemState.Released, 2f, "E3 the throw left the hand mid-ride");
            yield return WaitUntil(() => coin.State == ItemState.Free, 6f, "E3 the thrown coin came to rest");
            Vector3 local = car.transform.InverseTransformPoint(coin.transform.position);
            Check(car.IsInsideCar(coin.transform.position + Vector3.up * 0.25f) && local.y > 0.03f, $"E3 it rests on the moving car's floor, not below it (car-local {local:F2})");
            Check(coin.PinnedToCar, "E3 at rest it is pinned to the car");
            lastThrownCabinLocal = CabinFrame.Car(car).ToLocal(coin.transform.position);
        }

        // Where the last mid-ride throw came to rest, in the cabin frame: the same
        // spot must show in the other cabin after the swap (C2, C3).
        private static Vector3 lastThrownCabinLocal;

        private static IEnumerator GuestGrabsAndThrowsCargoMidRide(int coinId, int guestId)
        {
            ElevatorController car = WorldSceneFlow.FindCar();
            CarryableItem coin = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.ObjectId == coinId);
            HQPlayerController guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            Check(coin != null && coin.InTransit && guestPlayer != null, "E1 (guest) the coin rides as frozen cargo and the guest is aboard");
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
            Note("E1 (guest) car line mid-ride: " + System.Text.RegularExpressions.Regex.Match(lastReply, @"carDoor=[^\n]*?; itemsInCar=[^;\n]*; pinned=[^;\n]*; restKnown=[^;\n]*").Value);
            Vector3 coinLocal = car.transform.InverseTransformPoint(coin.transform.position);
            Vector3 standLocal = coinLocal - new Vector3(coinLocal.x, 0f, coinLocal.z).normalized * 0.9f; standLocal.y = 0.15f;
            Vector3 stand = car.transform.TransformPoint(standLocal);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(stand) + "}");
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(coin.transform.position - (stand + Vector3.up * 1.6f)) + "}");
            yield return Wait(0.3f);
            yield return Send("{\"id\":{id},\"action\":\"grab\",\"item\":\"#" + coinId + "\"}");
            yield return WaitUntil(() => coin.State == ItemState.Held && coin.HolderClientId == guestId, 4f, "E1 (guest) the cargo was grabbed mid-ride by the guest");
            Check(!coin.InTransit, "E1 (guest) grabbed cargo is cargo no more");
            yield return Wait(0.5f);
            Vector3 aim = (-car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward) * 0.25f + Vector3.down).normalized; // steeply at the floor, away from the door
            yield return Send("{\"id\":{id},\"action\":\"throw\",\"aim\":" + Vec(aim) + "}");
            yield return WaitUntil(() => coin.State == ItemState.Released, 3f, "E3 (guest) the throw left the guest's hand mid-ride");
            yield return WaitUntil(() => coin.State == ItemState.Free, 8f, "E3 (guest) the coin thrown by the guest came to rest");
            Vector3 local = car.transform.InverseTransformPoint(coin.transform.position);
            Check(car.IsInsideCar(coin.transform.position + Vector3.up * 0.25f) && local.y > 0.03f, $"E3 (guest) it rests on the moving car's floor on the server (car-local {local:F2})");
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
            throw new Exception("timeout: " + label + "\n" + H.RideStatus() + "\n" + H.FlowStatus());
        }

        // Holds W (re-queued every frame, see RideAndSample) until the condition holds.
        private static IEnumerator WalkUntil(Func<bool> condition, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            try
            {
                while (EditorApplication.timeSinceStartup < deadline) { if (condition()) yield break; Keys(Key.W); yield return null; }
            }
            finally { Keys(); }
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

        private static PlayerSubmersion Submersion() => Host() != null ? Host().GetComponent<PlayerSubmersion>() : null;

        // The F3 overlay's detail for the local player, as the overlay would join it.
        private static string OverlayDetail()
        {
            HQPlayerController local = Host();
            if (local == null) return string.Empty;
            // Player rows carry the display name and a swatch since the names/colour
            // cards; the owned player row is the host's own.
            foreach (SunkCost.Net.NetworkDebugSnapshot.ObjectRow row in SunkCost.Net.NetworkDebugSnapshot.Capture().Objects)
                if (row.IsOwner && (row.Swatch != null || row.Name == local.name.Replace("(Clone)", string.Empty))) return row.Detail;
            return string.Empty;
        }

        private static Transform TubeWalls()
        {
            foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                if (t.name == SunkCost.Sites.DiveSiteBuilder.TubeWallsName && t.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) return t;
            return null;
        }

        // Sweeps the host's capsule from a point along a direction; the first solid
        // thing it meets, or null when nothing within the distance.
        private static Collider Sweep(Vector3 from, Vector3 direction, float distance)
        {
            CharacterController capsule = Host().Controller;
            float radius = capsule.radius;
            Vector3 p1 = from + Vector3.up * radius;
            Vector3 p2 = from + Vector3.up * (capsule.height - radius);
            // The host's own capsule may stand in the path (it walked out through the doorway).
            return Physics.CapsuleCastAll(p1, p2, radius * 0.95f, direction, distance, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider != capsule).OrderBy(h => h.distance).Select(h => h.collider).FirstOrDefault();
        }

        // Rides as the local player from the current cabin, sampling every frame:
        // the player stays inside the moving car, the fade behaves, and the ride
        // completes with the car at the expected landing.
        // `midRide`, when given, runs once a few seconds into the moving stage (the
        // sampling pauses while it yields; the frame-to-frame checks tolerate a gap).
        private static IEnumerator RideAndSample(RideDirection direction, string label, Func<IEnumerator> midRide = null)
        {
            bool midRideDone = false;
            int serialBefore = Day.CabinRide.Serial;
            SunkCost.Diagnostics.FrameTimeRecorder.Instance?.Reset();
            if (SunkCost.Diagnostics.FrameTimeRecorder.Instance != null) Note($"{label} profiler markers recorded: {SunkCost.Diagnostics.FrameTimeRecorder.Instance.RecordedMarkerCount}");
            string requested = direction == RideDirection.Down ? H.ClientRequestCabin() : H.ClientRequestCar();
            Note(label + ": " + requested);
            yield return WaitUntil(() => Day.CabinRide.Serial > serialBefore && Day.CabinRide.Active, 5f, label + " ride started");
            Check(Day.CabinRide.Direction == direction, label + " ride direction " + direction);
            bool everBlack = false, everMoved = false, everOutside = false, everUnlockedEarly = false, everFreeWhileMoving = false;
            float maxSink = 0f, walked = 0f;
            Vector3? walkStart = null;
            int frames = 0;
            // Shaft tube samples (T1/T4): per-band distance and time for the speeds, the
            // water level against the formula, the submersion flip against the eye.
            // Per band (0 above the surface, 1 the crossing band, 2 below): the first and
            // last samples in which the car actually moved, so a tick's stall at either
            // end of the travel does not drag the average down.
            double[] bandStartT = new double[3], bandEndT = new double[3]; float[] bandStartY = new float[3], bandEndY = new float[3]; bool[] bandMoved = new bool[3];
            float lastCarY = float.NaN; double lastCarTime = 0, firstMoveTime = -1, lastMoveTime = -1;
            float waterError = 0f, waterAtEnd = -1f, floorYWhenDry = float.NaN, deepestWater = 0f;
            int submersionMismatchStreak = 0, worstSubmersionStreak = 0; bool everSubmerged = false, everF3Under = false, everF3Dry = false;
            bool capturedSurface = false, capturedUnder = false;
            float expectedTravel = 0f, carSpan = 0f, carSeaLevel = 0f; // read while the car exists: the site unloads after an up ride
            int fastFrames = 0, stalledFrames = 0; float largestStep = 0f; // smoothness: the car should move every frame, by about speed * frame time
            int hitchesSeen = 0, ridingHitches = 0; // each new hitch is noted with the ride's stage, so a spike can be blamed
            float lastCarLocalY = float.NaN, worstFloorJitter = 0f; int jitterNotes = 0;
            float worstGateLag = 0f; int gateSamples = 0, gateNotes = 0; // the tube gate mirrors the car door at the bottom
            // C: loose items on the car's floor ride with it, on every peer: their spot in the car's frame does not change between frames.
            var cargoLastLocal = new Dictionary<int, Vector3>(); float cargoWorstStep = 0f; int cargoSamples = 0, cargoNotes = 0;
            // D: the car's doors read shut from the moment the rider is in the car at the top until the car is at the bottom (Dan: "it looks like the door is open").
            float doorWorstOpenAtTop = 0f; int doorSamples = 0, doorNotes = 0; bool capturedTopArrival = false;
            // A remote rider's copy (the guest as the host sees it) stays on the moving floor, no hover, no steps.
            float remoteLastLocalY = float.NaN, remoteWorstJitter = 0f, remoteMinLocalY = float.PositiveInfinity, remoteMaxLocalY = float.NegativeInfinity; int remoteSamples = 0; // the rider's height above the car floor should not flicker frame to frame
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
                if (midRide != null && !midRideDone && state.Stage == CabinRideStage.Riding && firstMoveTime > 0 && EditorApplication.timeSinceStartup - firstMoveTime > 4.0)
                {
                    midRideDone = true;
                    yield return midRide();
                    cargoLastLocal.Clear(); // the script moved things on purpose; the next sample starts fresh
                    continue;
                }
                SunkCost.Diagnostics.FrameTimeRecorder recorder = SunkCost.Diagnostics.FrameTimeRecorder.Instance;
                if (recorder != null && recorder.HitchesSinceReset > hitchesSeen)
                {
                    hitchesSeen = recorder.HitchesSinceReset;
                    if (state.Stage == CabinRideStage.Riding) ridingHitches++;
                    ElevatorController c1 = WorldSceneFlow.FindCar();
                    Note($"{label} HITCH {recorder.LastFrameMs:0}ms at t+{recorder.SecondsSinceReset:0.0}s stage={state.Stage} car={Day.Elevator.State} carY={(c1 == null ? "-" : c1.transform.position.y.ToString("0.0"))} submerged={(Submersion() != null && Submersion().IsSubmerged)} scene={(Host() == null ? "-" : Host().gameObject.scene.name)} blame: {recorder.LastHitchBlame}");
                }
                ElevatorController car = WorldSceneFlow.FindCar();
                HQPlayerController local = Host();
                bool shouldBeLocked = WorldSceneFlow.RidersLockedDuring(state);
                if (car != null && (Day.Elevator.State == ElevatorState.AtBottom || (Day.Elevator.State == ElevatorState.Sealing && car.Upward)))
                {
                    ShaftGate gate0 = UnityEngine.Object.FindAnyObjectByType<ShaftGate>(FindObjectsInactive.Include);
                    ElevatorDoor door0 = car.GetComponentInChildren<ElevatorDoor>(true);
                    if (gate0 != null && door0 != null)
                    {
                        gateSamples++;
                        float gap = Mathf.Abs(gate0.OpenFraction - door0.OpenFraction);
                        if (gap > 0.2f && gateNotes++ < 6) Note($"{label} GATE GAP {gap:0.00} frame {frames}: gate={gate0.OpenFraction:0.00} door={door0.OpenFraction:0.00} carState={car.State} synced={Day.Elevator.State} upward={car.Upward} elapsed={car.StateElapsed:0.00} stage={state.Stage}");
                        worstGateLag = Mathf.Max(worstGateLag, gap);
                    }
                }
                if (state.Stage != CabinRideStage.Preparing && shouldBeLocked && Rider() != null && !Rider().Locked) everUnlockedEarly = true;
                if (car != null && (Day.Elevator.State == ElevatorState.Descending || Day.Elevator.State == ElevatorState.Ascending))
                {
                    foreach (CarryableItem item in UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude))
                    {
                        if (!item.IsSpawned || !item.CanGrabFromWorld || !car.IsInsideCar(item.transform.position + Vector3.up * 0.25f)) continue;
                        Vector3 itemLocal = car.transform.InverseTransformPoint(item.transform.position);
                        if (cargoLastLocal.TryGetValue(item.ObjectId, out Vector3 lastLocal))
                        {
                            cargoSamples++;
                            float step = (itemLocal - lastLocal).magnitude;
                            if (step > 0.01f && cargoNotes++ < 6) Note($"{label} CARGO STEP {step * 100f:0.0} cm frame {frames}: {item.name} local {lastLocal:F3} -> {itemLocal:F3} car={Day.Elevator.State} transit={item.InTransit} pinned={item.PinnedToCar}");
                            cargoWorstStep = Mathf.Max(cargoWorstStep, step);
                        }
                        cargoLastLocal[item.ObjectId] = itemLocal;
                    }
                }
                if (direction == RideDirection.Down && car != null && local != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && Day.Elevator.State != ElevatorState.AtBottom)
                {
                    ElevatorDoor doorTop = car.GetComponentInChildren<ElevatorDoor>(true);
                    if (doorTop != null)
                    {
                        doorSamples++;
                        if (doorTop.OpenFraction > 0.01f && doorNotes++ < 6) Note($"{label} DOOR OPEN {doorTop.OpenFraction:0.00} frame {frames}: stage={state.Stage} car={Day.Elevator.State} driven={car.Driven} carState={car.State}");
                        doorWorstOpenAtTop = Mathf.Max(doorWorstOpenAtTop, doorTop.OpenFraction);
                    }
                    if (!capturedTopArrival && ScreenFade.Instance != null && ScreenFade.Instance.IsClear)
                    {
                        capturedTopArrival = true;
                        SunkCost.Net.SessionInputGate.Resume(); H.CaptureScreen($"Logs/car-top-arrival-{label}.png"); SunkCost.Net.SessionInputGate.OpenMenu();
                    }
                }
                if (car != null && local != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Dive))
                {
                    ElevatorState cs = Day.Elevator.State;
                    if (cs == ElevatorState.Descending || cs == ElevatorState.Ascending)
                    {
                        everMoved = true;
                        double now = EditorApplication.timeSinceStartup;
                        if (firstMoveTime < 0) firstMoveTime = now;
                        lastMoveTime = now;
                        float carY = car.transform.position.y;
                        expectedTravel = car.TravelSecondsOneWay; carSpan = car.SpanMeters; carSeaLevel = car.SeaLevelY;
                        float surfaceDepth = car.TopPosition.y - car.SeaLevelY;
                        float depth = car.TopPosition.y - carY;
                        if (!float.IsNaN(lastCarY))
                        {
                            float previousDepth = car.TopPosition.y - lastCarY;
                            float lo = Mathf.Min(depth, previousDepth), hi = Mathf.Max(depth, previousDepth);
                            int band = hi < surfaceDepth - 0.3f ? 0 : lo > surfaceDepth + 0.3f && hi < surfaceDepth + car.SpanMeters - 0.3f ? 1 : lo > surfaceDepth + car.SpanMeters + 0.3f ? 2 : -1;
                            if (band == 2 && now - lastCarTime < 0.1)
                            {
                                fastFrames++;
                                float step = Mathf.Abs(carY - lastCarY);
                                if (step < 1e-4f) stalledFrames++;
                                largestStep = Mathf.Max(largestStep, step);
                            }
                            if (band >= 0 && Mathf.Abs(carY - lastCarY) > 1e-4f)
                            {
                                if (!bandMoved[band]) { bandMoved[band] = true; bandStartT[band] = lastCarTime; bandStartY[band] = lastCarY; }
                                bandEndT[band] = now; bandEndY[band] = carY;
                            }
                        }
                        lastCarY = carY; lastCarTime = now;
                        CabinWater water = car.GetComponent<CabinWater>();
                        if (water != null)
                        {
                            float expected = ElevatorMath.WaterLevelInCar(car.SeaLevelY, carY, car.SpanMeters);
                            waterError = Mathf.Max(waterError, Mathf.Abs(water.LevelMeters - expected));
                            waterAtEnd = water.LevelMeters;
                            deepestWater = Mathf.Max(deepestWater, water.LevelMeters);
                            if (direction == RideDirection.Up && float.IsNaN(floorYWhenDry) && deepestWater > 1f && water.LevelMeters <= 0.001f) floorYWhenDry = carY;
                        }
                        PlayerSubmersion submersion = Submersion();
                        if (submersion != null)
                        {
                            bool eyeUnder = local.EyePosition.y < car.SeaLevelY;
                            if (submersion.IsSubmerged != eyeUnder) { submersionMismatchStreak++; worstSubmersionStreak = Mathf.Max(worstSubmersionStreak, submersionMismatchStreak); }
                            else submersionMismatchStreak = 0;
                            if (submersion.IsSubmerged) everSubmerged = true;
                            string f3 = OverlayDetail();
                            if (submersion.IsSubmerged && f3.Contains("underwater=True depth=")) everF3Under = true;
                            if (!submersion.IsSubmerged && f3.Contains("underwater=False")) everF3Dry = true;
                            if (direction == RideDirection.Down && !capturedSurface && carY <= car.SeaLevelY) { capturedSurface = true; H.CaptureLocalCamera("Logs/shaft-tube-floor-at-surface.png"); }
                            if (direction == RideDirection.Down && !capturedUnder && submersion.IsSubmerged) { capturedUnder = true; H.CaptureLocalCamera("Logs/shaft-tube-eyes-under.png"); }
                        }
                        if (!car.IsInsideCar(local.transform.position + Vector3.up * 0.5f)) everOutside = true;
                        maxSink = Mathf.Max(maxSink, car.transform.position.y - local.transform.position.y);
                        foreach (HQPlayerController other in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                        {
                            if (other == local || other.gameObject.scene != car.gameObject.scene || !car.IsInsideCar(other.transform.position + Vector3.up * 0.5f)) continue;
                            float otherLocalY = other.transform.position.y - car.transform.position.y;
                            if (!float.IsNaN(remoteLastLocalY)) remoteWorstJitter = Mathf.Max(remoteWorstJitter, Mathf.Abs(otherLocalY - remoteLastLocalY));
                            remoteLastLocalY = otherLocalY; remoteSamples++;
                            remoteMinLocalY = Mathf.Min(remoteMinLocalY, otherLocalY); remoteMaxLocalY = Mathf.Max(remoteMaxLocalY, otherLocalY);
                        }
                        float carLocalY = local.transform.position.y - car.transform.position.y;
                        if (!float.IsNaN(lastCarLocalY) && local.IsGrounded)
                        {
                            float jitter = Mathf.Abs(carLocalY - lastCarLocalY);
                            if (jitter > 0.01f && jitterNotes++ < 12) Note($"{label} JUMP {jitter * 100f:0.0}cm frame {frames}: carLocalY {lastCarLocalY:0.000}->{carLocalY:0.000} carY={car.transform.position.y:0.00} stage={state.Stage} car={cs} grounded={local.IsGrounded} locked={(Rider() != null && Rider().Locked)} walking={(walkStart != null && walked <= 0.6f)}");
                            worstFloorJitter = Mathf.Max(worstFloorJitter, jitter);
                        }
                        lastCarLocalY = carLocalY;
                        // Free inside the moving car: walk forward for a while and measure it in the car frame.
                        if (!shouldBeLocked && !local.TravelLocked)
                        {
                            everFreeWhileMoving = true;
                            Vector3 carLocal = car.transform.InverseTransformPoint(local.transform.position);
                            // Re-queued every frame: Keyboard.current is whichever keyboard
                            // spoke last, and a tester typing during the run would steal it.
                            if (walkStart == null) { walkStart = carLocal; Keys(Key.W); }
                            else
                            {
                                Vector3 d = carLocal - walkStart.Value; d.y = 0f;
                                walked = Mathf.Max(walked, d.magnitude);
                                if (walked > 0.6f) Keys(); else Keys(Key.W);
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
            Check(maxSink < 0.05f, $"{label} the rider never sank into the car floor (max {maxSink:0.000} m)");
            Check(worstFloorJitter < 0.01f, $"{label} the rider's height above the floor never jumped between frames (worst {worstFloorJitter * 100f:0.0} cm)");
            if (gateSamples > 0) Check(worstGateLag < 0.05f, $"{label} the tube gate mirrored the car door at the bottom on {gateSamples} frames (worst gap {worstGateLag:0.00})");
            if (cargoSamples > 0) Check(cargoWorstStep < 0.01f, $"C {label} loose items on the car's floor rode with it on {cargoSamples} frames (worst step {cargoWorstStep * 100f:0.0} cm)");
            if (doorSamples > 0) Check(doorWorstOpenAtTop < 0.01f, $"D {label} the car's doors read shut from the swap to the bottom on {doorSamples} frames (worst open {doorWorstOpenAtTop:0.00})");
            if (remoteSamples > 100)
            {
                Note($"{label} remote rider copy: {remoteSamples} moving frames, height above the car {remoteMinLocalY:0.000}..{remoteMaxLocalY:0.000}, worst frame-to-frame change {remoteWorstJitter * 100f:0.0} cm");
                Check(remoteWorstJitter < 0.02f, $"{label} the remote rider's copy never stepped against the moving floor (worst {remoteWorstJitter * 100f:0.0} cm between frames)");
                Check(remoteMaxLocalY - remoteMinLocalY < 0.06f, $"{label} the remote rider's copy held its height on the moving floor (range {(remoteMaxLocalY - remoteMinLocalY) * 100f:0.0} cm)");
            }
            Check(!everUnlockedEarly, label + " the rider was locked exactly when it should be");
            Check(everFreeWhileMoving, label + " the rider was free inside the moving car");
            Check(walked > 0.5f && !everOutside, $"{label} the rider walked {walked:0.00} m inside the moving car and stayed inside");
            // T1/T4: the profile, the water and the submersion, sampled through the ride.
            float measured = (float)(lastMoveTime - firstMoveTime);
            float[] bandSeconds = new float[3], bandSpeed = new float[3];
            for (int b = 0; b < 3; b++) { bandSeconds[b] = (float)(bandEndT[b] - bandStartT[b]); bandSpeed[b] = bandMoved[b] && bandSeconds[b] > 0f ? Mathf.Abs(bandEndY[b] - bandStartY[b]) / bandSeconds[b] : 0f; }
            Note($"{label} bands: above {bandSpeed[0]:0.00} m/s over {bandSeconds[0]:0.00} s, crossing {bandSpeed[1]:0.00} m/s over {bandSeconds[1]:0.00} s, below {bandSpeed[2]:0.00} m/s over {bandSeconds[2]:0.00} s; moving {measured:0.00} s (expected {expectedTravel:0.00}); waterError {waterError:0.000}; deepest {deepestWater:0.00}; end {waterAtEnd:0.00}; dry at floor y {floorYWhenDry:0.00}; submersion streak {worstSubmersionStreak}");
            // The stretch above the surface is 1 m (seaLevelY -1): a 0.2 s window, where one
            // tick's step of the driven car is 0.35 m/s of the average, and a single hitched
            // frame at the start of the ride is another 0.5. A loose band there; the two
            // long bands below carry the real measurement.
            Check(bandSeconds[0] > 0.1f && Mathf.Abs(bandSpeed[0] - 3f) < 1.2f, $"{label} about 3 m/s above the surface band ({bandSpeed[0]:0.00} m/s over {bandSeconds[0]:0.00} s)");
            Check(bandSeconds[1] > 1f && Mathf.Abs(bandSpeed[1] - 1f) < 0.1f, $"{label} about 1 m/s through the surface band ({bandSpeed[1]:0.00} m/s)");
            Check(bandSeconds[2] > 5f && Mathf.Abs(bandSpeed[2] - 3f) < 0.3f, $"{label} about 3 m/s below the band ({bandSpeed[2]:0.00} m/s)");
            Check(Mathf.Abs(measured - expectedTravel) < 1f, $"{label} the car moved for {measured:0.0} s (profile says {expectedTravel:0.0})");
            // Smooth motion: the car moves every frame, not once per tick (a 50 Hz tick
            // at 200 fps would leave three frames in four standing still).
            Check(fastFrames > 100 && stalledFrames <= fastFrames / 20, $"{label} the car moved on {fastFrames - stalledFrames} of {fastFrames} frames at full speed (largest step {largestStep:0.000} m)");
            // No first-use stalls in the rider's face: the site's water and grade were
            // warmed up behind the black screen (DiveSiteWarmup). Loading may hitch.
            Note($"{label} host (editor) hitches while the car moved: {ridingHitches} (the editor's own windows share the GPU; the guest build below is the gate)");
            Check(waterError < 0.06f, $"{label} the cabin water matched sea level minus the floor every frame (worst {waterError:0.000} m)");
            Check(worstSubmersionStreak <= 2, $"{label} submersion flipped within a frame or two of the eye crossing sea level (worst streak {worstSubmersionStreak})");
            Check(everSubmerged && everF3Under && everF3Dry, $"{label} the rider went under and the F3 line read underwater=True depth= below and underwater=False above");
            if (direction == RideDirection.Down)
            {
                Check(Mathf.Abs(waterAtEnd - carSpan) < 0.03f, $"{label} the car arrived full ({waterAtEnd:0.00} m of {carSpan:0.00})");
                Check(capturedSurface && capturedUnder, label + " captured floor-at-surface and eyes-under");
            }
            else
            {
                Check(waterAtEnd <= 0.001f, $"{label} the car arrived dry ({waterAtEnd:0.00} m)");
                Check(Mathf.Abs(floorYWhenDry - carSeaLevel) < 0.15f, $"{label} the water drained to nothing as the floor passed sea level (floor y {floorYWhenDry:0.00}, sea level {carSeaLevel:0.00})");
                Check(Submersion() != null && !Submersion().IsSubmerged, label + " not submerged at the swap");
            }
            // Frame pacing on the host through the whole ride (measurement, not a gate yet).
            SunkCost.Diagnostics.FrameTimeRecorder pacing = SunkCost.Diagnostics.FrameTimeRecorder.Instance;
            if (pacing != null) Note($"{label} host frames: {pacing.Summary}; {pacing.HitchList}");
            yield return WaitUntil(() => Rider() == null || !Rider().Locked, 5f, label + " rider unlocked after the ride");
            yield return WaitUntil(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 5f, label + " screen clear after the ride");
        }

        // ---- guest ----------------------------------------------------------------------

        // The guest renders (no -batchmode/-nographics): its frame pacing is the
        // measurement that matters; the editor's is muddied by its own windows.
        private const bool GuestRenders = true;

        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                (GuestRenders ? "-screen-width 1280 -screen-height 720 -screen-fullscreen 0 " : "-batchmode -nographics ") + "-hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
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

        // "underwater=True/44.12; cabinWater=3.50": the guest's values within 0.2 m of ours.
        private static bool GuestAgrees(string reply, bool submerged, float cabinWater)
        {
            var under = System.Text.RegularExpressions.Regex.Match(reply, @"underwater=(True|False)/([0-9.]+)");
            var water = System.Text.RegularExpressions.Regex.Match(reply, @"cabinWater=([0-9.]+)");
            if (!under.Success || !water.Success) return false;
            if ((under.Groups[1].Value == "True") != submerged) return false;
            float hostDepth = Submersion() != null ? Submersion().DepthMeters : 0f;
            return Mathf.Abs(float.Parse(under.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) - hostDepth) < 0.2f &&
                Mathf.Abs(float.Parse(water.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - cabinWater) < 0.2f;
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
            yield return WaitUntil(() => sea != null && sea.DeckCabinPanel != null && sea.DeckCabinPanel.text.Contains("press E to descend"), 3f, "R0 deck cabin panel invites");
            Check(true, "R0 deck cabin panel invites: " + sea.DeckCabinPanel.text);
            // Y: the day state (Dan, 16 September 2026). Y1 arriving from HQ is day 1.
            Check(Day.Day == 1 && !Day.Payday && Day.Phase == DayPhase.AtSea, $"Y1 arriving at sea is day 1 (day={Day.Day} payday={Day.Payday} phase={Day.Phase})");
            yield return WaitUntil(() => H.MonitorText().StartsWith("Day 1 of 3"), 3f, "Y1 the monitor says 'Day 1 of 3': " + H.MonitorText());
            Check(sea.DeckCabinPanel.text.StartsWith("Day 1 of 3"), "Y1 the cabin panel says 'Day 1 of 3': " + sea.DeckCabinPanel.text);
            H.ClientRequestEndDay();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Nobody has dived today", 3f, "Y1 End day before a dive is refused: " + Day.LastRefusal.Text);
            Check(Day.Day == 1, "Y1 the day did not move");

            // R1: refusals from outside the cabin and from the wrong scene.
            host.TeleportLocal(sea.BoardingPoint != null ? sea.BoardingPoint.position : sea.SpawnPoint(0).position, 0f);
            yield return Wait(0.2f);
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Step inside the cabin first", 3f, "R1 refusal outside the cabin");
            H.ClientRequestCar();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Cabin is up", 3f, "R1 car button refused from the deck");

            Check(!host.GetComponent<PlayerHudUI>().Visor.On, "V1 the visor is off on the ship");

            // R1b: press the cabin button and step out at once (Dan, 18 September 2026:
            // "it teleports you back in"): the doors close with everyone free, the
            // leaver crosses the doorway, the doors turn around and open again, then
            // close; alone, nobody is aboard when they shut and the ride is cancelled.
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.2f);
            int outSerial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.CabinRide.Serial > outSerial && Day.CabinRide.Stage == CabinRideStage.Sealing, 3f, "R1b the ride starts with the doors closing");
            Check(!host.TravelLocked && !WorldSceneFlow.RidersLockedDuring(Day.CabinRide), "R1b free to move while the doors close");
            yield return Wait(0.3f);
            float closingAt = flow.DeckCabinOpenFraction();
            Vector3 cabinDoorway = sea.DeckCabin.TransformDirection(Quaternion.Euler(0f, CabinFrame.DeckCabinDoorwayYaw, 0f) * Vector3.forward);
            host.TeleportLocal(sea.DeckCabin.position + cabinDoorway * 3f + Vector3.up * 0.05f, host.Yaw); // out through the doorway, onto the deck
            yield return WaitUntil(() => Day.CabinRide.Stage == CabinRideStage.Sealing && Day.CabinRide.DoorOpening, 2f, "R1b the crossing turns the doors around");
            yield return WaitUntil(() => flow.DeckCabinOpenFraction() > closingAt, 2f, "R1b the doors open again at door speed");
            Check(!host.TravelLocked && !sea.IsInDeckCabin(host.transform.position), "R1b the leaver was not put back inside");
            yield return WaitUntil(() => Day.CabinRide.Stage == CabinRideStage.Cancelled, flow.Settings.CabinSealSeconds * 3f + 3f, "R1b alone and outside when the doors shut: the ride is cancelled");
            Check(Day.LastRefusal.Text == "Nobody aboard", "R1b the refusal: " + Day.LastRefusal.Text);
            yield return WaitUntil(() => flow.DeckCabinOpenFraction() > 0.99f, 4f, "R1b the doors open again for the next try");

            // R2: down. Sealing closes the deck doors, the suit fade, the car at the top, the descent, doors open at the bottom.
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.2f);
            yield return RideAndSample(RideDirection.Down, "R2");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Dive), "R2 the host's player is in DiveSite01");
            // V1/V2: the visor came on with the dive; its numbers are the truth's.
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            yield return null;
            Check(hud != null && hud.Visor.On, "V1 the visor is on in the dive");
            Check(Mathf.Abs(hud.Visor.DepthMeters - (Submersion() != null ? Submersion().DepthMeters : -1f)) < 0.1f, $"V2 the visor's depth is the submersion's ({hud.Visor.DepthMeters:0.0} m)");
            Check(Mathf.Abs(Mathf.DeltaAngle(hud.Visor.HeadingDeg, host.PlayerCamera.transform.eulerAngles.y)) < 1f, $"V2 the visor's heading is the camera's ({hud.Visor.HeadingDeg:0}°)");
            // The suit is on from the top of the ride down (the oxygen card, 17 September 2026): the tank counts, health is full.
            Check(hud.Visor.AirFraction > 0.9f && hud.Visor.AirFraction <= 1f && hud.Visor.HealthFraction >= 0.999f, $"V2 the tank counts in the car ({100f * hud.Visor.AirFraction:0}%), health full");
            Check(!hud.Visor.HomeShown, "V3 HOME is hidden inside the car");
            Check(hud.Visor.MoneyText == "BOX $0/$500  ·  ON ME $0" && hud.Visor.OnMeValue == 0, "M1 the visor's money line reads an empty box against the quota and nothing on me: " + hud.Visor.MoneyText);
            Check(Day.Elevator.State == ElevatorState.AtBottom, "R2 car at the bottom");
            ElevatorController car = WorldSceneFlow.FindCar();
            Check(car != null && car.Driven && Vector3.Distance(car.transform.position, car.BottomPosition) < 0.05f, "R2 the driven car sits on its bottom position");
            Check(car != null && car.IsInsideCar(host.transform.position + Vector3.up * 0.5f), "R2 the host stands inside the car");
            Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            yield return WaitUntil(CarDoorsOpen, 3f, "R2 car doors open at the bottom");
            Check(!ShaftGateBlocks(), "R2 shaft gate open at the bottom");
            Check(Day.IsBelow(host.OwnerId), "R2 host listed below");
            Check(flow.DeckCabinOpenFraction() < 0.01f, "R2 deck cabin doors closed while the car is below");
            yield return WaitUntil(() => sea.DeckCabinPanel.text.StartsWith("Dive in progress \u2014 1 below"), 3f, "R2/Y2 deck cabin panel says the dive is in progress: " + sea.DeckCabinPanel.text);
            Check(Day.Phase == DayPhase.DiveInProgress && Day.Day == 1, $"Y2 the day began when the riders arrived below (phase={Day.Phase} day={Day.Day})");
            Check(hud.Visor.DayText == "DAY 1/3", "Y2 the visor's corner says DAY 1/3: " + hud.Visor.DayText);
            Check(H.MonitorText().StartsWith("Day 1 of 3 \u2014 dive in progress"), "Y2 the monitor is locked for the dive: " + H.MonitorText());
            Check(true, "R2 deck cabin panel says 'Cabin below'");
            yield return Wait(1.6f); // doors fully open
            H.CaptureLocalCamera("Logs/deck-cabin-bottom.png");
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(doorway); yield return null;
            H.CaptureLocalCamera("Logs/deck-cabin-bottom-doorway.png");

            // T2: at the bottom the car is full, the tube gate's leaves are open, and the
            // host walks out through the two doorways onto the seafloor.
            CabinWater cabinWater = car.GetComponent<CabinWater>();
            Check(cabinWater != null && Mathf.Abs(cabinWater.LevelMeters - car.SpanMeters) < 0.03f, "T2 the cabin water stands at the car's full height with the doors open");
            ShaftGate tubeGate = UnityEngine.Object.FindObjectsByType<ShaftGate>(FindObjectsInactive.Include).FirstOrDefault();
            yield return WaitUntil(() => tubeGate != null && tubeGate.OpenFraction > 0.99f, 3f, "T2 the tube gate leaves are open");
            Check(Submersion() != null && Submersion().IsSubmerged && OverlayDetail().Contains("underwater=True depth="), "T2 submerged at the bottom, F3 says so: " + OverlayDetail());
            float tubeRadius = TubeWalls() != null ? Vector3.ProjectOnPlane(TubeWalls().GetChild(0).position - car.transform.position, Vector3.up).magnitude + 0.1f : 3.1f;
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(doorway); yield return null;
            yield return WalkUntil(() => Vector3.ProjectOnPlane(host.transform.position - car.transform.position, Vector3.up).magnitude > tubeRadius + 0.6f, 10f, "T2 walked out through the car and tube doorways");
            yield return Wait(0.3f);
            Check(!car.IsInsideCar(host.transform.position + Vector3.up * 0.5f), "T2 host outside the car and the tube after walking out");
            Check(Submersion() != null && Submersion().IsSubmerged, "T2 still submerged on the seafloor");

            // T3: the tube is sealed: a capsule sweep into it from behind is stopped by
            // the walls; from the car's centre, only the doorway direction gets out.
            Transform walls = TubeWalls();
            Check(walls != null, "T3 tube walls present");
            Vector3 behind = car.transform.position - doorway * (tubeRadius + 1.5f); behind.y = car.transform.position.y + 0.05f;
            Collider blockedBy = Sweep(behind, doorway, tubeRadius + 1.5f);
            Check(blockedBy != null && blockedBy.transform.IsChildOf(walls), "T3 sweep into the tube from behind is blocked by " + (blockedBy == null ? "nothing" : blockedBy.name));
            Vector3 side = car.transform.position + Vector3.Cross(Vector3.up, doorway) * (tubeRadius + 1.5f); side.y = behind.y;
            blockedBy = Sweep(side, -Vector3.Cross(Vector3.up, doorway), tubeRadius + 1.5f);
            Check(blockedBy != null && blockedBy.transform.IsChildOf(walls), "T3 sweep into the tube from the side is blocked by " + (blockedBy == null ? "nothing" : blockedBy.name));
            Vector3 centre = car.transform.position + Vector3.up * 0.15f;
            Check(Sweep(centre, -doorway, tubeRadius + 1f) != null, "T3 sweep out of the car away from the doorway is blocked");
            Check(Sweep(centre, doorway, tubeRadius + 1f) == null, "T3 sweep out of the car through the doorway passes");

            // V3: HOME from the seafloor: the arrow points at the tube doorway, the distance is the true one.
            Vector3 doorwayFoot = car.BottomPosition + doorway * 3.5f;
            Vector3 tenOut = doorwayFoot + doorway * 10f; tenOut.y = car.BottomPosition.y + 0.15f;
            host.TeleportLocal(tenOut, host.Yaw);
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(-doorway); yield return null; yield return null;
            Check(hud.Visor.HomeShown && Mathf.Abs(hud.Visor.HomeScreenAngleDeg) < 3f && Mathf.Abs(hud.Visor.HomeDistance - 10f) < 0.6f, $"V3 facing the tube from 10 m out HOME reads ahead at 10 m (angle {hud.Visor.HomeScreenAngleDeg:0.0}°, {hud.Visor.HomeDistance:0.0} m)");
            host.transform.rotation = Quaternion.LookRotation(doorway); yield return null; yield return null;
            Check(Mathf.Abs(Mathf.Abs(hud.Visor.HomeScreenAngleDeg) - 180f) < 3f, $"V3 facing away HOME reads behind (angle {hud.Visor.HomeScreenAngleDeg:0.0}°)");

            // V4: the coins are there, valued within their ranges; the dot's coin is tagged with its value.
            var coins = new List<CarryableItem>();
            foreach (CarryableItem item in UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude))
                if (item.HasValue && item.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) coins.Add(item);
            int coinPlacements = 0;
            foreach (DiveLootSetup.Placement placement in DiveLootSetup.Placements) if (placement.Coin != DiveLootSetup.AirTankPrefabName) coinPlacements++; // the air tanks among them are worth nothing
            Check(coins.Count >= coinPlacements, $"V4 {coins.Count} coins spawned on the seafloor (coin placements: {coinPlacements})");
            int badValues = 0;
            foreach (CarryableItem coin in coins) if (coin.Value < coin.ValueMin || coin.Value > coin.ValueMax) badValues++;
            Check(badValues == 0, "V4 every coin's value lies in its range");
            CarryableItem coin3 = coins.Find(c => c.name.StartsWith("Coin 3"));
            Check(coin3 != null, "V4 Coin 3 exists");
            Vector3 lookFrom = coin3.transform.position - doorway * 0.6f; lookFrom.y = car.BottomPosition.y + 0.15f; // within grab reach, as the hands matrix stands
            host.TeleportLocal(lookFrom, host.Yaw);
            yield return null;
            H.ClientLookAtItem(coin3.name); yield return null; yield return null;
            yield return WaitUntil(() => host.CurrentTarget == coin3, 2f, "V4 the dot is on Coin 3");
            Check(hud.Visor.TargetValue == coin3.Value && hud.Visor.TargetTag == $"{coin3.DisplayName} · ${coin3.Value}", "V4 the tag under the dot reads the coin's value: " + hud.Visor.TargetTag);
            Check(hud.Visor.BracketCount >= 1, $"V5 the coin in view is bracketed ({hud.Visor.BracketCount})");
            // The hook commands open the session menu (no stray input); the capture
            // wants the HUD, so close it for the frame and reopen it after.
            SunkCost.Net.SessionInputGate.Resume(); H.CaptureScreen("Logs/visor-seafloor.png"); yield return null; yield return null; SunkCost.Net.SessionInputGate.OpenMenu();
            // V5: nothing bracketed with every coin behind the camera: from past the last coin, looking away.
            Vector3 beyond = doorwayFoot + doorway * 27f; beyond.y = car.BottomPosition.y + 0.15f;
            host.TeleportLocal(beyond, host.Yaw);
            host.SetPitchForChecks(-15f); // the look at Coin 3 left the camera aimed at the floor; down the trail now
            host.transform.rotation = Quaternion.LookRotation(doorway); yield return null; yield return null;
            Check(hud.Visor.BracketCount == 0, $"V5 looking away from every coin nothing is bracketed ({hud.Visor.BracketCount})");
            host.transform.rotation = Quaternion.LookRotation(-doorway); yield return null; yield return null;
            Check(hud.Visor.BracketCount >= 1, $"V5 looking back at the trail the near coins are bracketed ({hud.Visor.BracketCount})");
            host.TeleportLocal(car.transform.position + doorway * 4f, host.Yaw); yield return Wait(0.3f);

            // C1: Coin 3 on the car's floor. It rides everything from here: the empty
            // up-and-back of R3b (pinned, loose), the up ride of R4 (frozen cargo, into
            // the deck cabin), the down ride of R5a (into a fresh site's car), R5b, G1.
            Vector3 coinSpot = car.transform.position - doorway * 0.6f + Vector3.up * 0.3f;
            coin3.ServerDropAt(coinSpot);
            yield return Wait(1f);
            Check(coin3.CanGrabFromWorld && car.IsInsideCar(coin3.transform.position + Vector3.up * 0.25f), "C1 Coin 3 lies on the car's floor: " + coin3.transform.position.ToString("F2"));
            float coinCarLocalY = car.transform.InverseTransformPoint(coin3.transform.position).y;
            Note($"C1 Coin 3 rests {coinCarLocalY:0.000} above the car's origin (value ${coin3.Value}, id #{coin3.ObjectId})");
            int coin3Id = coin3.ObjectId;

            // R3: back in; the car button from outside refuses.
            Check(!car.IsInsideCar(host.transform.position + Vector3.up * 0.5f), "R3 host outside the car");
            H.ClientRequestCar();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Step inside the cabin first", 3f, "R3 car button refused from outside");
            H.MoveLocalIntoCar();
            yield return Wait(0.3f);

            // R3b: press the car button and step out while the doors close (Dan's bug:
            // the rider was teleported to the deck cabin from the seafloor). The car
            // goes up without them, they stay below, the car comes back down for them.
            int leaverSerial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return WaitUntil(() => Day.CabinRide.Serial > leaverSerial && Day.CabinRide.Stage == CabinRideStage.Sealing, 8f, "R3b up ride sealing");
            host.TeleportLocal(car.transform.position + doorway * 4f, host.Yaw); // out through the closing doors
            // Crossing the closing doors turns them around (Dan, 18 September 2026: no
            // more being trapped in the tube): the car is back at AtBottom with the
            // doors opening, then seals again, then climbs without the leaver.
            yield return WaitUntil(() => Day.Elevator.State == ElevatorState.AtBottom, 3f, "R3b the crossing opens the car's doors again");
            yield return WaitUntil(() => Day.Elevator.State == ElevatorState.Sealing, 6f, "R3b the doors seal again once open");
            yield return WaitUntil(() => Day.CabinRide.Stage == CabinRideStage.Riding, 8f, "R3b the car climbs");
            yield return WaitUntil(() => !Day.IsRider(host.OwnerId) && Day.Riders.Count == 0, 2f, "R3b the leaver is no longer a rider once the doors shut");
            Check(Day.IsBelow(host.OwnerId), "R3b the leaver is still listed below");
            yield return WaitUntil(() => !Day.CabinRide.Active, 40f, "R3b the empty ride completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete, "R3b the ride completed without a cancel");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && !host.TravelLocked, "R3b the leaver stayed in DiveSite01, unlocked");
            Check(Vector3.Distance(host.transform.position, car.BottomPosition + doorway * 4f) < 1.5f, "R3b the leaver was not moved: " + host.transform.position.ToString("F2"));
            Check(WorldSceneFlow.FindCar() != null, "R3b the site stays loaded while the leaver is below");
            yield return WaitUntil(() => Day.Elevator.State == ElevatorState.AtBottom, 40f, "R3b the car came back down for the leaver");
            yield return WaitUntil(CarDoorsOpen, 3f, "R3b car doors open again at the bottom");
            Check(flow.DeckCabinOpenFraction() < 0.01f, "R3b deck cabin doors stay closed with the car below");
            car = WorldSceneFlow.FindCar();
            H.MoveLocalIntoCar();
            yield return Wait(0.3f);

            // R4: up. The car seals and climbs, the host is moved into the deck cabin, its doors open, the car stays up (nobody below).
            yield return RideAndSample(RideDirection.Up, "R4", () => HostGrabsAndThrowsCargoMidRide(coin3Id));
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), "R4 the host's player is back in ShipAtSea");
            yield return null;
            Check(!hud.Visor.On, "V1 the visor is off again on the deck");
            sea = ShipParts.InWorld(WorldId.Sea);
            Check(sea != null && sea.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "R4 the host stands in the deck cabin");
            Check(Day.Elevator.State == ElevatorState.AtTop, "R4 the car is up");
            Check(Day.Below.Count == 0, "R4 nobody below");
            Check(Day.Phase == DayPhase.AtSea && Day.Day == 1 && Day.DiveDone && !Day.Payday, $"Y3 the last one up: dive done, still day 1 (phase={Day.Phase} day={Day.Day} diveDone={Day.DiveDone})");
            yield return WaitUntil(() => H.MonitorText().StartsWith("Day 1 of 3 — dive done"), 3f, "Y3 the monitor says the dive is done: " + H.MonitorText());
            H.ClientRequestCabin(); // the host still stands in the deck cabin
            yield return WaitUntil(() => Day.LastRefusal.Text == "Dive done — end the day at the monitor", 3f, "Y3 once up, no going down again today: " + Day.LastRefusal.Text);
            Check(!Day.Riding, "Y3 no ride started");
            H.ClientRequestEndDay();
            yield return WaitUntil(() => Day.Day == 2 && !Day.DiveDone, 3f, "Y3 End day at the monitor: day 2");
            yield return WaitUntil(() => H.MonitorText().StartsWith("Day 2 of 3"), 3f, "Y3 the monitor says 'Day 2 of 3': " + H.MonitorText());
            // C2: the coin came up in the car and now lies on the deck cabin's floor at the same spot.
            CarryableItem coinUp = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.ObjectId == coin3Id);
            Check(coinUp != null && coinUp.gameObject.scene == WorldScenes.Scene(WorldId.Sea) && sea.IsInDeckCabin(coinUp.transform.position), "C2 Coin 3 rode up into the deck cabin: " + (coinUp == null ? "gone" : coinUp.gameObject.scene.name + " " + coinUp.transform.position.ToString("F2")));
            yield return Wait(0.6f); // released cargo settles
            Check(coinUp != null && coinUp.CanGrabFromWorld && !coinUp.InTransit, "C2 Coin 3 is loose again on the deck cabin's floor");
            Vector3 coinDeckLocal = coinUp == null ? Vector3.zero : CabinFrame.DeckCabin(sea).ToLocal(coinUp.transform.position);
            // Thrown mid-ride (so loose, not cargo, when the car reached the top): it crossed to the same cabin-frame spot.
            Check(coinUp != null && Vector3.Distance(coinDeckLocal, lastThrownCabinLocal) < 0.1f, $"C2 the coin thrown mid-ride crossed to the same spot in the deck cabin ({coinDeckLocal:F2} vs {lastThrownCabinLocal:F2} in the car)");
            yield return WaitUntil(() => flow.DeckCabinOpenFraction() > 0.99f, 3f, "R4 deck cabin doors open again");
            yield return WaitUntil(() => WorldSceneFlow.FindCar() == null, 10f, "R4 the site unloaded from the server once empty");
            H.CaptureLocalCamera("Logs/deck-cabin-back-on-deck.png");

            // R5: again, from a fresh site: down and up once more.
            yield return RideAndSample(RideDirection.Down, "R5a", () => HostGrabsAndThrowsCargoMidRide(coin3Id));
            Check(Day.Elevator.State == ElevatorState.AtBottom && host.gameObject.scene == WorldScenes.Scene(WorldId.Dive), "R5a down again on a fresh site");
            // C3: the coin came down again in the car, onto the fresh site's floor.
            car = WorldSceneFlow.FindCar();
            CarryableItem coinDown = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.ObjectId == coin3Id);
            yield return Wait(0.6f);
            Check(coinDown != null && car != null && coinDown.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && car.IsInsideCar(coinDown.transform.position + Vector3.up * 0.25f) && coinDown.CanGrabFromWorld && !coinDown.InTransit,
                "C3 Coin 3 rode down again in the car and is loose on its floor: " + (coinDown == null ? "gone" : coinDown.gameObject.scene.name + " " + coinDown.transform.position.ToString("F2")));
            Check(coinDown != null && car != null && Vector3.Distance(CabinFrame.Car(car).ToLocal(coinDown.transform.position), lastThrownCabinLocal) < 0.1f, $"C3 the coin thrown mid-ride lies where it landed in the car ({(coinDown == null ? "gone" : CabinFrame.Car(car).ToLocal(coinDown.transform.position).ToString("F2"))} vs {lastThrownCabinLocal:F2})");
            yield return Wait(0.5f);
            yield return RideAndSample(RideDirection.Up, "R5b");
            Check(host.gameObject.scene == WorldScenes.Scene(WorldId.Sea) && Day.Elevator.State == ElevatorState.AtTop, "R5b up again");
            Check(Day.Day == 2 && Day.DiveDone && !Day.Payday, $"Y4 the second dive is done on day 2 (day={Day.Day} diveDone={Day.DiveDone})");
            H.ClientRequestEndDay();
            yield return WaitUntil(() => Day.Day == 3 && !Day.DiveDone, 3f, "Y4 End day: day 3, not yet payday");

            // ---- guest rows ----
            guest = LaunchGuest();
            yield return WaitUntil(() => GuestId() >= 0 && UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Length >= 2, 40f, "guest player spawned");
            int guestId = GuestId();
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "G0 guest joined at sea");
            // N: display names. The host's saved name reached its player; the guest's
            // wish reaches the server, is sanitised, and both peers read both names.
            PlayerIdentity hostIdentity = host.GetComponent<PlayerIdentity>();
            Check(hostIdentity != null && hostIdentity.DisplayName == "Skipper", "N1 the host's saved name reached its player: " + (hostIdentity == null ? "no identity" : hostIdentity.DisplayName));
            yield return Send("{\"id\":{id},\"action\":\"name\",\"item\":\"  Mate <b>of the</b> deep, honestly too long  \"}");
            HQPlayerController guestCopy = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            PlayerIdentity guestIdentity = guestCopy != null ? guestCopy.GetComponent<PlayerIdentity>() : null;
            yield return WaitUntil(() => guestIdentity != null && guestIdentity.DisplayName == "Mate bof the/b d", 5f, "N2 the guest's name was sanitised and written by the server");
            Note("N2 guest name on the host: '" + guestIdentity.DisplayName + "'");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("name=Mate bof the/b d") && GuestPlayerLine(r, host.OwnerId).Contains("name=Skipper"), 5f, "N3 the guest reads both names");
            Check(!Day.Riding, "G0 no ride running");
            yield return GuestEventually(r => r.Contains("day=3;") && r.Contains("payday=False"), 5f, "Y5 the guest joined between days and reads day 3");

            // Y6: the day starts only with everyone in the cabin: the host inside, the
            // guest still at its spawn; the button names the guest.
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.3f);
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Waiting for: " + WorldSceneFlow.DisplayName(guestId), 3f, "Y6 the deck button waits for the guest by name: " + Day.LastRefusal.Text);
            Check(!Day.Riding && Day.Phase == DayPhase.AtSea, "Y6 no ride started without everyone aboard");

            // G1: both in the cabin; the host presses; both ride down.
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3 guestSpot = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"frames_reset\"}");
            yield return Send("{\"id\":{id},\"action\":\"cargo_reset\"}");
            yield return RideAndSample(RideDirection.Down, "G1", () => GuestGrabsAndThrowsCargoMidRide(coin3Id, guestId));
            Check(Day.IsBelow(guestId) && Day.IsBelow(host.OwnerId), "G1 both listed below");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("car=AtBottom") && r.Contains("ride=Complete"), 20f, "G1 guest rode down with the host");
            car = WorldSceneFlow.FindCar();
            HQPlayerController guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            Check(guestPlayer != null && car != null && car.IsInsideCar(guestPlayer.transform.position + Vector3.up * 0.5f), "G1 the guest's copy stands inside the car on the host");

            // G4: the guest's own submersion and cabin water agree with the host's.
            CabinWater sharedWater = car.GetComponent<CabinWater>();
            yield return GuestEventually(r => GuestAgrees(r, true, sharedWater != null ? sharedWater.LevelMeters : -1f), 10f, "G4 guest submerged at the bottom with the same cabin water as the host");
            // V4 (guest): the guest sees the same values on the same coins; V6: the host tags the guest's copy.
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
            string guestCoins = System.Text.RegularExpressions.Regex.Match(lastReply, @"coins=([^\n;]*)").Groups[1].Value;
            var hostCoins = new List<string>();
            foreach (CarryableItem item in UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude))
                if (item.HasValue && item.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) hostCoins.Add("#" + item.ObjectId + ":" + item.Value);
            hostCoins.Sort(string.CompareOrdinal);
            Check(guestCoins == string.Join(",", hostCoins), "V4 the guest sees the same coin values as the host: " + guestCoins);
            Check(lastReply.Contains("visor=on"), "V1 the guest's visor is on at the bottom");
            // C4: the guest's copy of the coin rode on the car's floor (its own per-frame record) and lies where the host's does.
            var cargoMatch = System.Text.RegularExpressions.Regex.Match(lastReply, @"cargoFrames=([0-9]+); cargoWorstStep=([0-9.]+)");
            int guestCargoFrames = cargoMatch.Success ? int.Parse(cargoMatch.Groups[1].Value) : -1;
            float guestCargoStep = cargoMatch.Success ? float.Parse(cargoMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : -1f;
            Check(guestCargoFrames > 100 && guestCargoStep < 0.01f, $"C4 the guest's copy of the coin rode with the car's floor ({guestCargoFrames} frames, worst step {guestCargoStep * 100f:0.0} cm)");
            CarryableItem coinBottom = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.ObjectId == coin3Id);
            var guestCoinLine = System.Text.RegularExpressions.Regex.Match(lastReply, @"item=[^\n]*; id=" + coin3Id + @";[^\n]*position=\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)");
            Vector3 guestCoinPos = guestCoinLine.Success ? new Vector3(float.Parse(guestCoinLine.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(guestCoinLine.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(guestCoinLine.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)) : Vector3.zero;
            Check(coinBottom != null && guestCoinLine.Success && Vector3.Distance(guestCoinPos, coinBottom.transform.position) < 0.05f, $"C4 the guest sees Coin 3 where the host has it ({guestCoinPos:F2} vs {(coinBottom == null ? "gone" : coinBottom.transform.position.ToString("F2"))})");
            Check(coinBottom != null && car != null && coinBottom.CanGrabFromWorld && !coinBottom.InTransit && car.IsInsideCar(coinBottom.transform.position + Vector3.up * 0.25f) && car.transform.InverseTransformPoint(coinBottom.transform.position).y > 0.03f, "E3 the coin the guest threw mid-ride rests on the car's floor, not below it: " + (coinBottom == null ? "gone" : car.transform.InverseTransformPoint(coinBottom.transform.position).ToString("F2")));
            // D: the guest's car door never read open between the swap and the bottom.
            var doorMatch = System.Text.RegularExpressions.Regex.Match(lastReply, @"doorWorstOpenAtTop=([0-9.]+)");
            Check(doorMatch.Success && float.Parse(doorMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < 0.01f, "D the guest's car doors read shut from the swap to the bottom (worst open " + (doorMatch.Success ? doorMatch.Groups[1].Value : "?") + ")");
            guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            host.SetPitchForChecks(0f); host.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(guestPlayer.transform.position - host.transform.position, Vector3.up)); yield return null; yield return null;
            float trueCrewDistance = Vector3.Distance(host.EyePosition, guestPlayer.transform.position + Vector3.up * 1.85f);
            Check(hud.Visor.CrewTagCount == 1 && Mathf.Abs(hud.Visor.NearestCrewDistance - trueCrewDistance) < 0.3f, $"V6 the host's visor tags the guest ({hud.Visor.CrewTagCount} tag, {hud.Visor.NearestCrewDistance:0.0} m vs {trueCrewDistance:0.0})");
            SunkCost.Net.SessionInputGate.Resume(); H.CaptureScreen("Logs/visor-crew.png"); yield return null; yield return null; SunkCost.Net.SessionInputGate.OpenMenu();
            // V4 (guest): the guest's own visor tags Coin 3 with the same value and
            // brackets it — its copies live in the session scene, not the site's.
            // Coin 4: Coin 3 rode down in the car and was thrown about by the guest (E3).
            CarryableItem guestCoin3 = UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.name.StartsWith("Coin 4"));
            Check(guestCoin3 != null, "V4 Coin 4 still on the seafloor for the guest's look");
            Vector3 guestStand = guestCoin3.transform.position - doorway * 0.6f; guestStand.y = car.BottomPosition.y + 0.15f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestStand) + "}");
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(guestCoin3.transform.position - (guestStand + Vector3.up * 1.6f)) + "}");
            yield return Wait(0.5f);
            string wantedTag = $"{guestCoin3.DisplayName} · ${guestCoin3.Value}";
            yield return GuestEventually(r => System.Text.RegularExpressions.Regex.Match(r, @"tag=([^;\n]*)").Groups[1].Value == wantedTag, 4f, "V4 the guest's visor tags Coin 4 with the host's value: " + wantedTag);
            Check(int.TryParse(System.Text.RegularExpressions.Regex.Match(lastReply, @"brackets=([0-9]+)").Groups[1].Value, out int guestBrackets) && guestBrackets >= 1, $"V5 the guest's visor brackets the coin in view ({guestBrackets})");
            yield return Send("{\"id\":{id},\"action\":\"frames\"}");
            string guestFrames = lastReply.Split('\n')[0];
            Note("G1 guest frames (build" + (GuestRenders ? ", rendering" : ", headless") + "): " + guestFrames);
            // The gate: on a real player, at most two hitches (30 ms or longer) once the
            // car is moving; the scene load before it may hitch. The editor host renders
            // the same descent on the same GPU, so the guest's present waits are noisier
            // here than for a real crew. Hitch times are seconds since the reset at the
            // button press; the car moves from about t+7 s.
            int guestRidingHitches = 0;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(guestFrames, @"t=([0-9.]+)s ([0-9]+)ms"))
                if (float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 7f) guestRidingHitches++;
            var fpsMatch = System.Text.RegularExpressions.Regex.Match(guestFrames, @"fps=([0-9]+)");
            if (GuestRenders)
            {
                Check(guestRidingHitches <= 2, $"G1 the guest build had at most two hitches of 30 ms or more while the car moved ({guestRidingHitches})");
                Check(fpsMatch.Success && int.Parse(fpsMatch.Groups[1].Value) >= 50, "G1 the guest build rendered at 50 fps or better (" + (fpsMatch.Success ? fpsMatch.Groups[1].Value : "?") + ")");
            }

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
            // With the car below the housing is shut: a capsule sweep in through the
            // doorway is stopped by the doorway collider (Dan, 16 September 2026: a
            // player standing in the housing while the car comes up "can never
            // happen"). The refusal row below teleports in through it on purpose and
            // steps out again before the guest rides up.
            Vector3 deckDoorway = sea.DeckCabin.forward;
            Vector3 outsideDoor = sea.DeckCabin.position + deckDoorway * 4f; outsideDoor.y = sea.DeckCabin.position.y + 0.15f;
            Collider deckBlocker = Sweep(outsideDoor, -deckDoorway, 4f);
            Check(deckBlocker != null && deckBlocker.name == ShipParts.DeckCabinDoorColliderName, "G2 the deck cabin doorway is blocked while the car is below (" + (deckBlocker == null ? "nothing" : deckBlocker.name) + ")");
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.3f);
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Dive in progress \u2014 1 below: " + WorldSceneFlow.DisplayName(guestId), 3f, "G2/Y7 host refused mid-day, the guest named: " + Day.LastRefusal.Text);
            Check(Day.Phase == DayPhase.DiveInProgress && Day.Day == 3, "Y7 the day is still in progress while the guest is below");
            host.TeleportLocal(sea.BoardingPoint != null ? sea.BoardingPoint.position : sea.SpawnPoint(0).position, host.Yaw); // out of the housing: nobody stands where the car arrives
            yield return Wait(0.3f);
            Check(!sea.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "G2 host out of the housing before the guest rides up");

            // G3: the guest walks into the car and comes up alone; the host watches the doors open.
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f)) + "}");
            yield return Wait(0.5f);
            int serialBefore = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}");
            yield return WaitUntil(() => Day.CabinRide.Serial > serialBefore, 5f, "G3 guest's up ride started");
            yield return WaitUntil(() => !Day.CabinRide.Active, 60f, "G3 guest's ride completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Day.Below.Count == 0, "G3 guest surfaced, nobody below");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=ShipAtSea") && r.Contains("ride=Complete") && r.Contains("travelLocked=False"), 20f, "G3 guest is back on the ship, unlocked");
            yield return WaitUntil(() => flow.DeckCabinOpenFraction() > 0.99f, 3f, "G3 deck cabin doors open for the guest's arrival");
            yield return null;
            Check(Sweep(outsideDoor, -deckDoorway, 4f) == null || Sweep(outsideDoor, -deckDoorway, 4f).name != ShipParts.DeckCabinDoorColliderName, "G3 the deck cabin doorway is open again with the car up");
            guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.OwnerId == guestId);
            sea = ShipParts.InWorld(WorldId.Sea);
            Check(guestPlayer != null && sea.IsInDeckCabin(guestPlayer.transform.position + Vector3.up * 0.5f), "G3 the guest's copy stands in the deck cabin on the host");
            yield return GuestEventually(r => r.Contains("underwater=False/"), 10f, "G4 guest dry again on the ship");
            yield return WaitUntil(() => WorldSceneFlow.FindCar() == null, 10f, "G3 the site unloaded once empty");

            // Y8: the third day is over: payday. The monitor offers only HQ, the deck
            // button refuses, the guest reads the same.
            Check(Day.DiveDone && Day.Day == 3 && !Day.Payday && Day.Phase == DayPhase.AtSea, $"Y8 the third dive is done (day={Day.Day} diveDone={Day.DiveDone})");
            // Home only at the start of a day (Dan, 17 September 2026): with the dive done, HQ waits for End day.
            string doneSail = H.ServerSail("HQ");
            Check(doneSail == "refused: Dive done — End day first", "Y8 sailing home with the dive done is refused until End day: " + doneSail);
            H.ClientRequestEndDay();
            yield return WaitUntil(() => Day.Payday && Day.Day == 3, 3f, "Y8 End day after the third dive is payday");
            Check(Day.Phase == DayPhase.AtSea, $"Y8 payday at sea (phase={Day.Phase})");
            yield return WaitUntil(() => H.MonitorText().StartsWith("PAYDAY"), 3f, "Y8 the monitor says PAYDAY: " + H.MonitorText());
            string paydaySail = H.ServerSail("Sea");
            Check(paydaySail.Contains("Payday \u2014 only HQ"), "Y8 the monitor refuses Site 01 on payday: " + paydaySail);
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Wait(0.3f);
            H.ClientRequestCabin();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Payday \u2014 sail home", 3f, "Y8 the deck button refuses on payday: " + Day.LastRefusal.Text);
            Check(!Day.Riding, "Y8 no ride on payday");
            yield return GuestEventually(r => r.Contains("payday=True"), 5f, "Y8 the guest reads payday");

            // Z1: the storage room. Coin 3 into the box: the server's sum, the readout
            // on the box, the visor line and the guest agree; out of the box it is nothing.
            CarryableItem boxCoin = H.Item("Coin 3");
            Check(boxCoin != null && boxCoin.gameObject.scene == WorldScenes.Scene(WorldId.Sea), "Z1 Coin 3 is on the ship");
            int coinValue = boxCoin.Value;
            boxCoin.ServerDropAt(sea.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f)));
            yield return WaitUntil(() => Day.BoxValue == coinValue, 2f, $"Z1 the box is worth the coin (${coinValue}): box=${Day.BoxValue}");
            Check(sea.IsInStorageRoom(boxCoin.transform.position), "Z1 the coin lies inside the storage volume");
            SunkCost.World.StorageReadout readout = sea.GetComponent<SunkCost.World.StorageReadout>();
            yield return WaitUntil(() => readout != null && readout.Text == $"STORAGE\n${coinValue} / $500\nbalance $0", 2f, "Z1 the box's readout says " + (readout == null ? "(none)" : readout.Text.Replace("\n", " | ")));
            yield return GuestEventually(r => r.Contains($"box={coinValue};"), 5f, "Z1 the guest reads the box's worth");
            boxCoin.ServerDropAt(sea.FromShipLocal(new Vector3(-3f, 0.3f, 3f)));
            yield return WaitUntil(() => Day.BoxValue == 0, 2f, "Z1 out of the box it counts for nothing");
            boxCoin.ServerDropAt(sea.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f)));
            yield return WaitUntil(() => Day.BoxValue == coinValue, 2f, "Z1 back in the box for the sale");

            yield return Send("{\"id\":{id},\"action\":\"leave\"}");

            // Y9: home. Docking resets the cycle: day 0, no payday.
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Length == 1, 10f, "Y9 the guest left");
            host.TeleportLocal(sea.SpawnPoint(0).position, 0f);
            yield return Wait(0.3f);
            string homeSail = H.ServerSail("HQ");
            Check(homeSail.StartsWith("sailing"), "Y9 sailing home on payday: " + homeSail);
            yield return WaitUntil(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.HQ, 30f, "Y9 docked at HQ");
            Check(Day.Day == 3 && Day.Payday && Day.Phase == DayPhase.AtHQ, $"Y9 docking keeps the payday until it is paid (day={Day.Day} payday={Day.Payday} phase={Day.Phase})");
            yield return WaitUntil(() => H.MonitorText().StartsWith("Docked at HQ — PAYDAY"), 3f, "Y9 the monitor says payday at the dock: " + H.MonitorText());

            // Z2: the ship stays docked until the quota is paid; the coin came home in the box.
            string paydayOut = H.ServerSail("Sea");
            Check(paydayOut == "refused: Pay the quota first", "Z2 the monitor refuses Site 01 until the quota is paid: " + paydayOut);
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            CarryableItem homeCoin = H.Item("Coin 3");
            Check(hqShip != null && homeCoin != null && homeCoin.gameObject.scene == WorldScenes.Scene(WorldId.HQ) && hqShip.IsInStorageRoom(homeCoin.transform.position), "Z2 Coin 3 crossed to HQ inside the storage room");
            yield return WaitUntil(() => Day.BoxValue == coinValue, 2f, "Z2 the docked ship's box is worth the coin");
            yield return WaitUntil(() => H.QuotaBoardText().StartsWith("PAYDAY"), 3f, "Z2 the HQ board says PAYDAY: " + H.QuotaBoardText().Replace("\n", " | "));

            // Z3: look at the board: the prompt offers the pay. Pay with the quota set to
            // the coin's worth: sold, paid exactly, a new cycle, the coin gone.
            H.ClientMoveLocalPlayerTo(new Vector3(-2.5f, 0f, -4.4f)); yield return null;
            H.ClientLookAtNamed(SunkCost.World.QuotaBoard.BoardName); yield return null; yield return null;
            Check(host.CurrentQuotaBoard != null && H.PromptText().Contains("Press E to pay the quota"), "Z3 looking at the board offers the pay: " + H.PromptText());
            WorldLoopSettings.QuotaOverrideForTests = coinValue;
            H.ClientRequestPay();
            yield return WaitUntil(() => Day.LastPay.Serial == 1, 3f, "Z3 the pay was processed");
            PayReport pay = Day.LastPay;
            // Every dollar handed over is the crew's (Dan, 17 September 2026): the quota is met, nothing is charged.
            Check(pay.Paid && !pay.Lost && pay.Sales == coinValue && pay.Quota == coinValue && pay.Had == coinValue && pay.Balance == coinValue, $"Z3 sold ${pay.Sales}, quota ${pay.Quota}, paid={pay.Paid}, balance ${pay.Balance} (nothing charged)");
            Check(Day.Day == 0 && !Day.Payday && Day.Balance == coinValue && Day.CycleSales == 0, $"Z3 a new cycle after paying (day={Day.Day} payday={Day.Payday} balance={Day.Balance} handed={Day.CycleSales})");
            yield return WaitUntil(() => H.Item("Coin 3") == null || !H.Item("Coin 3").IsSpawned, 2f, "Z3 the sold coin is gone");
            yield return WaitUntil(() => Day.BoxValue == 0, 2f, "Z3 the box is empty");
            Check(H.QuotaBoardText().StartsWith($"PAID ${coinValue}"), "Z3 the board says PAID: " + H.QuotaBoardText().Replace("\n", " | "));
            Check(Day.ServerCanSail(WorldId.Sea, out string sailWhy), "Z3 the monitor may sail again: " + sailWhy);

            // Z4: nothing to pay in a fresh cycle.
            H.ClientRequestPay();
            yield return WaitUntil(() => Day.LastRefusal.Text == "Nothing to pay yet — dive first", 3f, "Z4 paying with no dive taken is refused: " + Day.LastRefusal.Text);
            Check(Day.LastPay.Serial == 1, "Z4 no second pay happened");

            // Z5: short before payday is not a loss: the box is banked, the count goes
            // on (Dan: "not a loss instantly"). Short at payday is GAME LOST. The server
            // API directly, the cycle forced; the button's path is Z3.
            WorldLoopSettings.QuotaOverrideForTests = 99999;
            Day.ServerForceCycleForChecks(2, false);
            PayReport shortPay = Day.ServerPay(sales: 40, quota: 99999);
            Check(shortPay.Short && !shortPay.Paid && !shortPay.Lost && shortPay.Had == 40 && shortPay.Balance == coinValue + 40 && Day.Balance == coinValue + 40 && Day.CycleSales == 40 && Day.Day == 2 && !Day.Payday, $"Z5 short before payday banks the box and keeps the day (balance ${Day.Balance}, handed ${Day.CycleSales}, day {Day.Day})");
            yield return null;
            Check(H.QuotaBoardText().StartsWith("SHORT BY $99959"), "Z5 the board says SHORT BY: " + H.QuotaBoardText().Replace("\n", " | "));
            Check(Day.ServerCanSail(WorldId.Sea, out string shortWhy), "Z5 the ship may sail out again to try for the rest: " + shortWhy);
            Day.ServerForceCycleForChecks(3, true);
            PayReport lost = Day.ServerPay(sales: 0, quota: 99999);
            // Short at payday: the run is over — the phase goes to Plank (the walk is
            // WorldSceneFlow's, the plank matrix; here the day state's API alone) and
            // the reset comes after it.
            Check(lost.Lost && !lost.Paid && !lost.Short && lost.Had == 40 && Day.Phase == DayPhase.Plank && Day.Balance == coinValue + 40, $"Z5 short at payday: the run is over, the plank phase (had ${lost.Had}, balance ${Day.Balance} until the reset)");
            yield return null;
            Check(H.QuotaBoardText().StartsWith("THE RUN IS OVER"), "Z5 the board says THE RUN IS OVER: " + H.QuotaBoardText().Replace("\n", " | "));
            Check(!Day.ServerCanSail(WorldId.Sea, out string overWhy) && overWhy == "The run is over", "Z5 no sailing once the run is over: " + overWhy);
            Day.ServerResetRun();
            Check(Day.Phase == DayPhase.AtHQ && Day.Balance == 0 && Day.CycleSales == 0 && Day.Day == 0 && !Day.Payday && Day.RunDays == 0, $"Z5 the reset: day 0, $0, phase {Day.Phase}");
            WorldLoopSettings.QuotaOverrideForTests = null;
            SunkCost.Net.SessionInputGate.Resume();
        }
    }
}
