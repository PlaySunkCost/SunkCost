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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;
using M = SunkCost.Editor.Prototype.MonsterTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The Listener's polish checks (mon-listener, 24 September 2026), the host alone on
    // the seabed with a Listener it spawns: the model and its clips; deaf to a
    // crouch-walk; one dark beam judged frame by frame — where it leaves (the mouth,
    // in front of its planted body), where it points, where it ends, how thick it is
    // drawn against what hurts, the 1 s charge and the 3 s burn, one hit, the turn at
    // 160°/s, the body in Aiming then Shooting then Recovering; the dash on the flash
    // that escapes it, an early dash and a late one that do not; a wall between; three
    // shots running; its walks to a thing's sound and a diver's, and home after the
    // silence. Log: Temp/polish-Listener-matrix.log. Job "polish-Listener".
    public static class MonsterPolishListenerChecks
    {
        private const string Log = "Temp/polish-Listener-matrix.log";

        private static readonly Stack<IEnumerator> stack = new();
        private static bool running;
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
            if (running) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Listener polish checks started " + DateTime.Now + "\n");
            Status = "Running";
            running = true;
            stack.Clear();
            stack.Push(Run());
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
            if (Status == "MATRIX_PASS") Debug.Log("Listener polish checks: MATRIX_PASS"); else Debug.LogError("Listener polish checks: " + Status);
            running = false;
            stack.Clear();
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
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + M.MonstersText());
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

        private static Vector3 Seabed(ElevatorController car, float bearingDeg, float distance)
        {
            Vector3 dir = Quaternion.Euler(0f, bearingDeg, 0f) * Vector3.forward;
            return car.BottomPosition + dir * distance + Vector3.up * 0.15f;
        }
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        private static float FlatAngle(Vector3 a, Vector3 b) => Vector3.Angle(Flat(a), Flat(b));
        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            Vector3 to = Flat(facing - spot);
            host.TeleportLocal(spot, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : Vector3.forward, Vector3.up).eulerAngles.y);
            yield return null; yield return null;
            M.ClientLookAt(facing + Vector3.up * 1.2f);
            yield return null;
        }
        private static void Sound(HQPlayerController host) => NoiseSystem.Emit(host.transform.position, 15f, NoiseKind.Sprint, host.ObjectId);

        // A new beam: sound the host until the Listener charges (a shot waits out the last beam's cooldown).
        private static IEnumerator Provoke(Listener ears, CreatureBolts bolts, HQPlayerController host, string row, float within = 10f)
        {
            float deadline = Time.unscaledTime + within, next = 0f;
            while (Time.unscaledTime < deadline && !bolts.ServerCharging)
            {
                if (Time.unscaledTime >= next) { Sound(host); next = Time.unscaledTime + 0.5f; }
                yield return null;
            }
            Check(bolts.ServerCharging, row + " a sound drew a charge (" + ears.ServerStatus + ")");
        }
        // The last beam's cooldown and the recovery are over: the next sound draws a charge at once.
        private static IEnumerator Cooled(Listener ears, CreatureBolts bolts, string row)
        {
            MonsterSettings s = Settings;
            float ready = bolts.ServerEndedAt + s.ListenerShotCooldownSeconds + 0.2f;
            yield return Expect(() => !bolts.ServerAiming && Time.time >= ready && ears.Pose != CreaturePose.Recovering, s.BeamChargeSeconds + s.BeamSeconds + s.ListenerShotCooldownSeconds + 2f, () => row + " ready to shoot again (" + ears.ServerStatus + ")");
        }
        private static IEnumerator BeamOver(CreatureBolts bolts, string row)
        {
            MonsterSettings s = Settings;
            yield return Expect(() => bolts.ServerPhase == BeamPhase.Done, s.BeamChargeSeconds + s.BeamSeconds + 1f, () => row + " the beam ended (" + bolts.ServerPhase + ")");
        }
        // A beam's width on this spawned copy only (the captures' before and after).
        private static void SetHalfWidth(CreatureBolts bolts, float value)
        {
            var so = new SerializedObject(bolts);
            so.FindProperty("halfWidth").floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Film one beam from its charge to its fade, from the side, 15 frames a second,
        // with a temporary camera (HideAndDontSave, destroyed after).
        private static IEnumerator Film(CreatureBolts bolts, Vector3 monster, Vector3 diver, string name)
        {
            string dir = Path.Combine("Temp/polish-Listener-gifs", name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            Vector3 mid = (monster + diver) * 0.5f + Vector3.up * 1.1f;
            Vector3 along = Flat(diver - monster).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, along).normalized;
            var go = new GameObject("Listener checks film camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            go.transform.position = mid + across * 6.5f + Vector3.up * 0.6f - along * 1.0f;
            go.transform.LookAt(mid);
            var rt = new RenderTexture(640, 360, 24) { hideFlags = HideFlags.HideAndDontSave };
            var tex = new Texture2D(640, 360, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            int n = 0;
            float next = 0f, until = float.PositiveInfinity;
            try
            {
                while (Time.time < until)
                {
                    if (bolts.ServerPhase == BeamPhase.Done && float.IsPositiveInfinity(until)) until = Time.time + 0.45f;
                    if (Time.unscaledTime >= next)
                    {
                        next = Time.unscaledTime + 1f / 15f;
                        cam.targetTexture = rt;
                        cam.Render();
                        cam.targetTexture = null;
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(dir, n.ToString("000") + "-" + bolts.ServerPhase + ".png"), tex.EncodeToPNG());
                        n++;
                    }
                    yield return null;
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(tex);
            }
            Say($"filmed {name}: {n} frames in {dir}");
        }

        // One dash to the host's left on the virtual keyboard (Alt + A).
        private static IEnumerator DashLeft()
        {
            Keys(Key.A); yield return null;
            Keys(Key.A, Key.LeftAlt); yield return null; yield return null;
            Keys(Key.A); yield return Wait(Host().Movement.DashSeconds + 0.1f);
            Keys(); yield return null;
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerVitals vitals = host.Vitals;
            MonsterSettings s = Settings;
            Say($"beam: charge {s.BeamChargeSeconds} s, burn {s.BeamSeconds} s at {s.BeamSweepDegPerSec}°/s, {s.BeamRangeMeters} m; listener {s.ListenerDamage} HP, cooldown {s.ListenerShotCooldownSeconds} s, forget {s.ListenerForgetSeconds} s, approach ×{s.ListenerApproachSpeedFactor}; standoff {s.ShooterStandoffMeters} m");
            Check(Mathf.Approximately(s.BeamChargeSeconds, 1f) && Mathf.Approximately(s.BeamSeconds, 3f) && Mathf.Approximately(s.BeamSweepDegPerSec, 160f), "the beam carries Dan's numbers (1 s, 3 s, 160°/s)");
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ListenerCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;

            Heading("L0 — to sea and down; the Listener's model, anchor and clips");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            yield return Descend(1);
            ElevatorController car = WorldSceneFlow.FindCar();
            const float Bearing = 240f;
            Vector3 home = Seabed(car, Bearing, 34f);
            Creature creature = MonsterRoster.ServerSpawnForChecks(MonsterKind.Listener, home);
            Check(creature is Listener, "spawned the Listener at " + home.ToString("F1"));
            Listener ears = (Listener)creature;
            CreatureBolts bolts = ears.GetComponent<CreatureBolts>();
            CreatureRig rig = ears.GetComponent<CreatureRig>();
            Check(bolts != null && rig != null && rig.HasAnimator && rig.BeamOrigin != null, "L0 it carries CreatureBolts and a CreatureRig with an Animator and a BeamOrigin");
            foreach (CreaturePose pose in new[] { CreaturePose.Idle, CreaturePose.Drawn, CreaturePose.Hunting, CreaturePose.Aiming, CreaturePose.Shooting, CreaturePose.Recovering })
                Check(rig.Animated(pose), "L0 the model has a clip for " + pose);
            AnimationClip[] clips = rig.Animator.runtimeAnimatorController.animationClips;
            AnimationClip shooting = clips.FirstOrDefault(c => c.name == "Shooting");
            AnimationClip recovering = clips.FirstOrDefault(c => c.name == "Recovering");
            AnimationClip aiming = clips.FirstOrDefault(c => c.name == "Aiming");
            float shootingLength = shooting != null ? shooting.length : 0f, recoveringLength = recovering != null ? recovering.length : 0f, aimingLength = aiming != null ? aiming.length : 0f;
            Check(shooting != null && !shooting.isLooping && shootingLength >= s.BeamSeconds, $"L0 Shooting plays once and covers the burn ({shootingLength:0.00} s)");
            Check(recovering != null && !recovering.isLooping && recoveringLength <= ears.RecoverSeconds + 0.1f, $"L0 Recovering plays once within the recovery ({recoveringLength:0.00} s of {ears.RecoverSeconds})");
            Check(aiming != null && aimingLength >= s.BeamChargeSeconds, $"L0 Aiming covers the charge ({aimingLength:0.00} s)");
            Say("L0 clips: " + string.Join(", ", clips.Select(c => c.name + " " + c.length.ToString("0.00") + "s" + (c.isLooping ? " loop" : ""))));
            Check(bolts.HalfWidth >= 0.3f && bolts.HalfWidth <= 0.45f, $"L0 the beam is big (Dan): {bolts.HalfWidth * 2f:0.00} m across");

            Heading("L1 — idle, and deaf to a crouch-walk");
            Vector3 stand = Seabed(car, Bearing, 24f);
            yield return HostAt(stand, home);
            vitals.ServerHealForChecks();
            yield return Wait(1.0f);
            Check(ears.Pose == CreaturePose.Idle, "L1 it stands idle at home: " + ears.ServerStatus);
            int heardBefore = ears.ServerHeard;
            Keys(Key.LeftCtrl, Key.A);
            yield return Wait(3f);
            Keys(); yield return null;
            yield return Wait(0.5f);
            Check(ears.ServerHeard == heardBefore && bolts.ServerFired == 0 && !bolts.ServerAiming, $"L1 a 3 s crouch-walk 10 m from it made no sound it heard (heard {ears.ServerHeard - heardBefore}, fired {bolts.ServerFired})");
            Check(ears.Pose == CreaturePose.Idle, "L1 still idle: " + ears.ServerStatus);

            Heading("L2 — one dark beam at a diver standing still, 9 m off: emitter, path, width, timing, one hit, the turn, the body");
            Vector3 lAt = Seabed(car, Bearing, 30f);
            ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(Seabed(car, Bearing + 90f, 30f) - lAt)).eulerAngles.y); // facing away across: it must turn
            stand = Seabed(car, Bearing, 21f);
            yield return HostAt(stand, lAt);
            vitals.ServerHealForChecks();
            yield return Wait(0.3f);
            float health0 = vitals.Health;
            int hits0 = bolts.ServerHits, fired0 = bolts.ServerFired;
            Sound(host);
            yield return Expect(() => bolts.ServerCharging, 0.5f, () => "L2 the sound drew a charge at once: " + ears.ServerStatus);
            yield return null; yield return null;
            Check(ears.Pose == CreaturePose.Aiming, "L2 it charges in the Aiming pose: " + ears.Pose);
            Check(ears.TargetId == host.OwnerId && bolts.ServerTargetOwnerId == host.OwnerId, "L2 the beam follows the diver whose step it heard");
            MonsterBeamView view = ears.GetComponentInChildren<MonsterBeamView>(true);
            Check(view != null && view.Phase == BeamPhase.Charging && view.Dark, "L2 every screen draws the charge: the dark look");
            float maxFromGap = 0f, maxToGap = 0f, maxTurn = 0f, worstFacing = 0f, widthGap = 0f;
            bool poseRight = true, hitInCharge = false;
            Vector3 lastDir = bolts.ServerAimDirection;
            float chargeEnd = bolts.ServerChargedAt + s.BeamChargeSeconds;
            string poseSeen = string.Empty;
            bool moved = false;
            while (bolts.ServerAiming)
            {
                yield return null;
                if (!bolts.ServerAiming) break;
                float dt = Time.deltaTime;
                Vector3 dir = bolts.ServerAimDirection;
                if (dt > 0.0001f && dt < 0.1f) maxTurn = Mathf.Max(maxTurn, Vector3.Angle(lastDir, dir) / dt);
                lastDir = dir;
                maxFromGap = Mathf.Max(maxFromGap, Vector3.Distance(view.ShownFrom, bolts.ServerFrom));
                maxToGap = Mathf.Max(maxToGap, Vector3.Distance(view.ShownTo, bolts.ServerTo));
                CreaturePose want = bolts.ServerCharging ? CreaturePose.Aiming : CreaturePose.Shooting;
                if (ears.Pose != want && Time.time - bolts.ServerChargedAt > 0.05f && Time.time - bolts.ServerFiredAt > 0.05f) { poseRight = false; poseSeen = ears.Pose + " while " + bolts.ServerPhase; }
                if (bolts.ServerCharging && bolts.ServerHits > hits0) hitInCharge = true;
                if (bolts.ServerFiring) widthGap = Mathf.Max(widthGap, Mathf.Abs(view.ShownHalfWidth - bolts.HalfWidth));
                // Facing along the beam from the end of the charge (the body turns at 540°/s, the beam at 160°/s).
                if (Time.time > chargeEnd - 0.4f) worstFacing = Mathf.Max(worstFacing, FlatAngle(ears.transform.forward, dir));
                if (bolts.ServerFiring && Time.time > chargeEnd + 0.1f && Math.Abs(Time.time - bolts.ServerFiredAt - 0.4f) < 0.2f && !moved)
                {
                    // The mouth at the moment of the burn: in front of the planted body, at the head.
                    Vector3 local = ears.transform.InverseTransformPoint(bolts.ServerFrom);
                    Check(local.z > 0.2f && local.y > 0.8f && local.y < 1.7f && Mathf.Abs(local.x) < 0.15f, $"L2 the beam leaves the mouth in front of its body: {local:F2} (m, its own axes)");
                    Check(Vector3.Distance(bolts.ServerFrom, ears.EyePoint) > 0.05f && Vector3.Distance(bolts.ServerFrom, rig.BeamOrigin.position) < 0.08f, $"L2 the server's line starts at the model's BeamOrigin ({Vector3.Distance(bolts.ServerFrom, rig.BeamOrigin.position):0.000} m), not the eye point");
                    Vector3 toChest = CreatureBolts.AimPoint(host) - bolts.ServerFrom;
                    Check(Vector3.Angle(dir, toChest) < 3f, $"L2 it points at the diver's chest ({Vector3.Angle(dir, toChest):0.0}°)");
                    Check(Vector3.Distance(bolts.ServerTo, bolts.ServerFrom) > toChest.magnitude, "L2 the beam runs past the diver (nothing in the way)");
                    // The turn: the diver steps 2.5 m aside (a teleport, not a dash); the beam comes after at its rate.
                    Vector3 side = Vector3.Cross(Vector3.up, Flat(toChest).normalized);
                    host.TeleportLocal(host.transform.position + side * 2.5f, host.Yaw);
                    moved = true;
                }
            }
            Check(moved, "L2 the diver stepped aside while it burned");
            float charge = bolts.ServerFiredAt - bolts.ServerChargedAt, burn = bolts.ServerEndedAt - bolts.ServerFiredAt, hitAfter = bolts.ServerHitAt - bolts.ServerFiredAt;
            Say($"L2 charge {charge:0.000} s, burn {burn:0.000} s, hit {hitAfter:0.000} s after the flash, gap {bolts.ServerHitGap:0.000} m; drawn vs judged: from {maxFromGap:0.000} m, to {maxToGap:0.000} m, width {widthGap:0.000} m; turn ≤ {maxTurn:0} °/s; body off the beam ≤ {worstFacing:0.0}°");
            Check(Mathf.Abs(charge - s.BeamChargeSeconds) < 0.06f, $"L2 the charge lasted {charge:0.000} s");
            Check(Mathf.Abs(burn - s.BeamSeconds) < 0.06f, $"L2 the burn lasted {burn:0.000} s");
            Check(!hitInCharge && hitAfter >= 0f && hitAfter < 0.1f, $"L2 the hit landed as the beam lit, not during the charge ({hitAfter:0.000} s)");
            Check(bolts.ServerHits == hits0 + 1 && bolts.ServerFired == fired0 + 1 && bolts.ServerHitOwnerId == host.OwnerId, $"L2 one beam, one hit, on the host (hits {bolts.ServerHits - hits0})");
            Check(Mathf.Abs(health0 - s.ListenerDamage - vitals.Health) < 0.5f && vitals.Leaking, $"L2 35 HP and a leak, once: health {health0} → {vitals.Health}, leaking {vitals.Leaking}");
            CharacterController body = host.GetComponent<CharacterController>();
            Check(bolts.ServerHitGap <= body.radius + bolts.HalfWidth + 0.001f, $"L2 the beam touched the body it hurt ({bolts.ServerHitGap:0.000} m ≤ {body.radius + bolts.HalfWidth:0.000})");
            Check(maxFromGap < 0.08f && maxToGap < 0.05f && widthGap < 0.001f, $"L2 the drawn beam is the judged one on the host (from {maxFromGap:0.000} m, to {maxToGap:0.000} m, width {widthGap:0.000})");
            Check(maxTurn <= s.BeamSweepDegPerSec * 1.05f + 1f && maxTurn >= s.BeamSweepDegPerSec * 0.8f, $"L2 the beam turned after the diver at its rate ({maxTurn:0} °/s)");
            Check(worstFacing < 12f, $"L2 its body faced along the beam from the end of the charge ({worstFacing:0.0}°)");
            Check(poseRight, "L2 Aiming while it charged, Shooting while it burned " + poseSeen);
            yield return null; yield return null;
            Check(ears.Pose == CreaturePose.Recovering, "L2 then it recovers: " + ears.Pose);
            float recoverFrom = Time.time;
            yield return Expect(() => ears.Pose != CreaturePose.Recovering, ears.RecoverSeconds + 1f, () => "L2 the recovery ends");
            float recovered = Time.time - recoverFrom;
            Check(Mathf.Abs(recovered - ears.RecoverSeconds) < 0.12f, $"L2 the recovery lasted {recovered:0.00} s ({ears.RecoverSeconds})");
            yield return Wait(0.6f);
            Check(view.Phase == BeamPhase.None, "L2 the drawn beam has faded out");

            Heading("L2b — the edge of the drawn beam: just outside it misses, just inside it hits");
            foreach (float margin in new[] { 0.08f, -0.08f })
            {
                string row = margin > 0f ? "L2b outside" : "L2b inside";
                yield return Cooled(ears, bolts, row);
                Vector3 spot = Vector3.MoveTowards(lAt, stand, 8f);
                ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(spot - lAt)).eulerAngles.y);
                yield return HostAt(spot, lAt);
                vitals.ServerHealForChecks();
                yield return Wait(0.3f);
                CharacterController capsule = host.GetComponent<CharacterController>();
                float edge = capsule.radius + bolts.HalfWidth;
                Vector3 chest = CreatureBolts.AimPoint(host);
                Vector3 across = Vector3.Cross(Vector3.up, Flat(chest - lAt)).normalized;
                Vector3 aim = chest + across * (edge + margin);
                // Face the Listener down the line first, so its mouth does not swing across it as it turns.
                for (int k = 0; k < 2; k++)
                {
                    ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(aim - bolts.Origin)).eulerAngles.y);
                    yield return null; yield return null;
                }
                hits0 = bolts.ServerHits;
                bolts.ServerAim(bolts.Origin, aim, true, s.ListenerDamage, "the Listener (checks)", -1);
                Check(bolts.ServerCharging, row + " a beam aimed beside the diver charges");
                float minGap = float.PositiveInfinity;
                while (bolts.ServerAiming)
                {
                    yield return null;
                    if (!bolts.ServerFiring) continue;
                    bolts.Touches(host, bolts.ServerFrom, bolts.ServerTo, out _, out _, out float g);
                    minGap = Mathf.Min(minGap, g);
                }
                bool hit = bolts.ServerHits > hits0;
                Say($"{row}: designed {edge + margin:0.000} m from the body's axis, measured {minGap:0.000} m, the edge {edge:0.000} m (capsule {capsule.radius} + beam {bolts.HalfWidth}); hit {hit}");
                Check(hit == (minGap <= edge), $"{row} the hit agrees with what is drawn (gap {minGap:0.000} vs edge {edge:0.000}, hit {hit})");
                if (margin > 0f) Check(!hit && minGap > edge && minGap < edge + 0.16f, $"{row} a beam {minGap - edge:0.000} m clear of the body misses it");
                else Check(hit && minGap <= edge && minGap > edge - 0.16f, $"{row} a beam overlapping the body by {edge - minGap:0.000} m hits it");
            }
            vitals.ServerHealForChecks();

            Heading("L3 — the dash on the flash escapes; an early dash and a late one do not");
            // Each: the Listener re-placed 6 m off, the host still and facing it, a new beam.
            foreach ((string row, string when) in new[] { ("L3a", "flash"), ("L3b", "early"), ("L3c", "late") })
            {
                yield return Cooled(ears, bolts, row);
                ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(stand - lAt)).eulerAngles.y);
                Vector3 near = Vector3.MoveTowards(lAt, stand, 6f);
                yield return HostAt(near, lAt);
                vitals.ServerHealForChecks();
                yield return Expect(() => host.DashReady >= 1f, 4f, () => row + " the dash is ready");
                yield return Wait(0.3f);
                hits0 = bolts.ServerHits;
                yield return Provoke(ears, bolts, host, row, 1f);
                float t0 = bolts.ServerChargedAt;
                float at = when == "flash" ? t0 + s.BeamChargeSeconds - 0.16f : when == "early" ? t0 + 0.2f : t0 + s.BeamChargeSeconds + 0.3f;
                while (Time.time < at) yield return null;
                int dashes = host.Dashes;
                yield return DashLeft();
                Check(host.Dashes == dashes + 1, $"{row} the host dashed ({host.DashRefusal})");
                yield return BeamOver(bolts, row);
                Say($"{row} dash at {at - t0:0.00} s into the charge: hits {bolts.ServerHits - hits0}, held {bolts.ServerHeld}, health {vitals.Health}");
                if (when == "flash")
                {
                    Check(bolts.ServerHits == hits0 && bolts.ServerHeld && vitals.Health >= vitals.Settings.MaxHealth - 0.5f, $"{row} dashed across as it lit: missed, and it held its heading");
                }
                else if (when == "early")
                {
                    Check(bolts.ServerHits == hits0 + 1 && !bolts.ServerHeld, $"{row} dashed early: the charge turned after the diver and the beam hit");
                }
                else
                {
                    Check(bolts.ServerHits == hits0 + 1 && bolts.ServerHitAt - bolts.ServerFiredAt < 0.1f, $"{row} dashed with the beam already on the diver: hit as it lit");
                }
                yield return Wait(0.5f);
            }

            Heading("L4 — a wall between them stops the beam");
            yield return Cooled(ears, bolts, "L4");
            ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(stand - lAt)).eulerAngles.y);
            Vector3 behind = Vector3.MoveTowards(lAt, stand, 8f);
            yield return HostAt(behind, lAt);
            vitals.ServerHealForChecks();
            Vector3 mid = Vector3.MoveTowards(lAt, behind, 4f);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Listener checks wall";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, WorldScenes.Scene(WorldId.Dive));
            wall.transform.SetPositionAndRotation(new Vector3(mid.x, car.BottomPosition.y + 1.5f, mid.z), Quaternion.LookRotation(Flat(behind - lAt)));
            wall.transform.localScale = new Vector3(4f, 4f, 0.4f);
            Physics.SyncTransforms();
            Check(!CreatureSenses.ClearLine(ears.EyePoint, CreatureSenses.Chest(host)), "L4 the wall stands between them");
            hits0 = bolts.ServerHits;
            yield return Wait(0.3f);
            yield return Provoke(ears, bolts, host, "L4", 1f);
            yield return Expect(() => bolts.ServerFiring, s.BeamChargeSeconds + 0.5f, () => "L4 it fires at the wall");
            yield return Wait(0.5f);
            float wallDistance = Vector3.Distance(bolts.ServerFrom, bolts.ServerTo);
            float toWall = Vector3.Distance(Flat(bolts.ServerFrom), Flat(mid));
            Check(wallDistance < toWall + 0.5f, $"L4 the beam ends at the wall ({wallDistance:0.00} m, the wall {toWall:0.00} m)");
            Check(view.ShownImpact && Vector3.Distance(view.ShownTo, bolts.ServerTo) < 0.05f, "L4 the impact shows where it ends");
            yield return BeamOver(bolts, "L4");
            Check(bolts.ServerHits == hits0 && vitals.Health >= vitals.Settings.MaxHealth - 0.5f, $"L4 nothing reached the diver behind it (hits {bolts.ServerHits - hits0}, health {vitals.Health})");
            UnityEngine.Object.Destroy(wall); wall = null;
            yield return null;

            Heading("L5 — three shots running: each charges, burns, hits once and recovers; nothing sticks");
            for (int i = 1; i <= 3; i++)
            {
                yield return Cooled(ears, bolts, "L5." + i);
                ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(stand - lAt)).eulerAngles.y);
                yield return HostAt(Vector3.MoveTowards(lAt, stand, 7f), lAt);
                vitals.ServerHealForChecks();
                hits0 = bolts.ServerHits; fired0 = bolts.ServerFired;
                yield return Provoke(ears, bolts, host, "L5." + i, 1f);
                yield return Expect(() => ears.Pose == CreaturePose.Aiming, 0.3f, () => $"L5.{i} Aiming");
                yield return Expect(() => bolts.ServerFiring && ears.Pose == CreaturePose.Shooting, s.BeamChargeSeconds + 0.3f, () => $"L5.{i} Shooting");
                yield return BeamOver(bolts, "L5." + i);
                yield return Expect(() => ears.Pose == CreaturePose.Recovering, 0.3f, () => $"L5.{i} Recovering");
                Check(bolts.ServerFired == fired0 + 1 && bolts.ServerHits == hits0 + 1, $"L5.{i} one beam, one hit ({bolts.ServerFired - fired0}, {bolts.ServerHits - hits0})");
                yield return Expect(() => ears.Pose != CreaturePose.Recovering, ears.RecoverSeconds + 0.5f, () => $"L5.{i} out of the recovery: {ears.Pose}");
            }
            Check(!bolts.ServerAiming, "L5 no beam left up after the three");

            Heading("L6 — its walks: to a thing's sound (Drawn), to a diver's (Hunting), standing when there, home after the silence");
            // A thing's sound with no diver within the beam's reach: it charges at the spot, then walks there.
            yield return HostAt(Seabed(car, Bearing - 70f, 30f) + Flat(Seabed(car, Bearing - 70f, 30f) - car.BottomPosition).normalized * 25f, lAt);
            vitals.ServerHealForChecks();
            Check(CreatureSenses.Flat(host.transform.position, lAt) > s.BeamRangeMeters, $"L6 the host is out of the beam's reach ({CreatureSenses.Flat(host.transform.position, lAt):0} m)");
            yield return Cooled(ears, bolts, "L6");
            ears.ServerPlaceForChecks(lAt, 0f);
            Vector3 thing = Vector3.MoveTowards(lAt, Seabed(car, Bearing + 40f, 30f), 12f);
            NoiseSystem.Emit(thing, 15f, NoiseKind.Impact, 0);
            yield return Expect(() => bolts.ServerCharging, 0.5f, () => "L6 a coin's landing drew a charge at the spot");
            Check(bolts.ServerTargetOwnerId == -1, "L6 no diver near enough to follow: the beam stays on the spot");
            yield return BeamOver(bolts, "L6");
            yield return Expect(() => ears.Pose == CreaturePose.Drawn, ears.RecoverSeconds + 0.5f, () => "L6 it walks Drawn toward the sound: " + ears.ServerStatus);
            Vector3 p0 = ears.transform.position; float t0w = Time.time;
            yield return Wait(1.0f);
            float speed = CreatureSenses.Flat(p0, ears.transform.position) / (Time.time - t0w);
            float approach = host.WalkSpeed * s.ListenerApproachSpeedFactor;
            Say($"L6 Drawn at {speed:0.00} m/s (the brain's {approach:0.00}); the rig reads {rig.Speed:0.00}; facing the sound within {FlatAngle(ears.transform.forward, thing - ears.transform.position):0}°");
            Check(Mathf.Abs(speed - approach) < approach * 0.15f, $"L6 Drawn at the approach speed ({speed:0.00} m/s of {approach:0.00})");
            Check(FlatAngle(ears.transform.forward, thing - ears.transform.position) < 10f, "L6 it walks facing where it goes");
            yield return Expect(() => ears.Pose == CreaturePose.Idle, 10f, () => "L6 it stands when there: " + ears.ServerStatus);
            float off = CreatureSenses.Flat(ears.transform.position, thing);
            Check(Mathf.Abs(off - s.ShooterStandoffMeters) < 0.5f, $"L6 it stopped {off:0.00} m short of the sound (standoff {s.ShooterStandoffMeters})");
            // A diver's sound: after the shot it stalks there in Hunting.
            Vector3 hunter = ears.transform.position;
            Vector3 diverAt = Vector3.MoveTowards(hunter, Seabed(car, Bearing, 20f), 14f);
            yield return Cooled(ears, bolts, "L6");
            yield return HostAt(diverAt, hunter);
            vitals.ServerHealForChecks();
            yield return Provoke(ears, bolts, host, "L6", 1f);
            yield return BeamOver(bolts, "L6");
            vitals.ServerHealForChecks();
            yield return Expect(() => ears.Pose == CreaturePose.Hunting, ears.RecoverSeconds + 0.5f, () => "L6 it stalks Hunting toward a diver's sound: " + ears.ServerStatus);
            p0 = ears.transform.position; t0w = Time.time;
            yield return Wait(0.8f);
            speed = CreatureSenses.Flat(p0, ears.transform.position) / (Time.time - t0w);
            Say($"L6 Hunting at {speed:0.00} m/s; the rig reads {rig.Speed:0.00}");
            Check(Mathf.Abs(speed - approach) < approach * 0.15f, $"L6 Hunting at the approach speed ({speed:0.00} m/s)");
            yield return Expect(() => ears.Pose == CreaturePose.Idle, 10f, () => "L6 it stands at the standoff: " + ears.ServerStatus);
            // The silence: the host crouches still; after ListenerForgetSeconds it drifts home, Drawn, and stands there.
            yield return Expect(() => ears.Pose == CreaturePose.Drawn, s.ListenerForgetSeconds + 6f, () => "L6 after the silence it walks home: " + ears.ServerStatus);
            p0 = ears.transform.position; t0w = Time.time;
            yield return Wait(1.0f);
            speed = CreatureSenses.Flat(p0, ears.transform.position) / (Time.time - t0w);
            Say($"L6 home at {speed:0.00} m/s");
            Check(Mathf.Abs(speed - host.WalkSpeed * 0.35f) < host.WalkSpeed * 0.35f * 0.2f, $"L6 home at the drift speed ({speed:0.00} m/s)");
            yield return Expect(() => ears.Pose == CreaturePose.Idle && CreatureSenses.Flat(ears.transform.position, ears.Home) < 2f, 30f, () => "L6 home, idle: " + ears.ServerStatus);
            Check(!bolts.ServerAiming && bolts.ServerHits >= 1, "L6 back to rest");

            Heading("L7 — captures for Dan: both beams at the old width and the new (Temp/polish-Listener-gifs)");
            float bigWidth = bolts.HalfWidth;
            foreach (float w in new[] { 0.12f, bigWidth })
            {
                yield return Cooled(ears, bolts, "L7");
                SetHalfWidth(bolts, w);
                Vector3 spot = Vector3.MoveTowards(lAt, stand, 8f);
                ears.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(spot - lAt)).eulerAngles.y);
                yield return HostAt(spot, lAt);
                vitals.ServerHealForChecks();
                yield return Wait(0.4f);
                yield return Provoke(ears, bolts, host, "L7", 1f);
                yield return Film(bolts, lAt, spot, "Listener-" + (w < 0.2f ? "before" : "after") + "-" + (w * 2f).ToString("0.00") + "m");
                vitals.ServerHealForChecks();
            }
            SetHalfWidth(bolts, bigWidth);
            M.ServerDespawnMonsters();
            yield return Expect(() => Creature.All.Count == 0, 5f, () => "L7 the Listener despawned");
            Creature lure = MonsterRoster.ServerSpawnForChecks(MonsterKind.Lure, lAt);
            Check(lure != null, "L7 a Lure for its beam");
            CreatureBolts lureBolts = lure.GetComponent<CreatureBolts>();
            float lureWidth = lureBolts.HalfWidth;
            Say($"L7 the Lure's prefab halfWidth {lureWidth}");
            foreach (float w in new[] { 0.12f, Mathf.Max(lureWidth, bigWidth) })
            {
                SetHalfWidth(lureBolts, w);
                Vector3 spot = Vector3.MoveTowards(lAt, stand, 8f);
                lure.ServerPlaceForChecks(lAt, Quaternion.LookRotation(Flat(spot - lAt)).eulerAngles.y);
                yield return HostAt(spot, lAt);
                Check(host.LampOn, "L7 the host's lamp is on");
                vitals.ServerHealForChecks();
                yield return Expect(() => lureBolts.ServerCharging, 12f, () => "L7 the Lure saw the lamp and charges: " + lure.ServerStatus);
                yield return Film(lureBolts, lAt, spot, "Lure-" + (w < 0.2f ? "before" : "after") + "-" + (w * 2f).ToString("0.00") + "m");
                vitals.ServerHealForChecks();
                yield return Expect(() => !lureBolts.ServerAiming, 1f, () => "L7 the Lure's beam is over");
                yield return Wait(Settings.LureShotCooldownSeconds + 0.3f);
            }
            SetHalfWidth(lureBolts, lureWidth);

            M.ServerDespawnMonsters();
            yield return Expect(() => Creature.All.Count == 0, 5f, () => "despawned");
            vitals.ServerHealForChecks();
        }
    }
}
