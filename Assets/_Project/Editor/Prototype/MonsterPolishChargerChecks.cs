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
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The Charger's polish checks (mon-charger, 24 September 2026; docs/DESIGN.md §6;
    // Dan's checklist: the eight Charger cases plus idle, the prowl, the chase,
    // obstacles and repeated cycles). The host alone, below, with the roster empty:
    // each row spawns its own Charger on open seabed, places the host with
    // TeleportLocal and reads the server's brain, the host's own vitals and
    // knock-back, and the Animator and bones of the host's copy. Log:
    // Temp/polish-Charger-matrix.log. Started by CameraClearanceMatrixDriver
    // ("polish-Charger"). With Temp/polish-Charger-guest.flag present the same job
    // runs the guest's rows instead (G1): a guest build (Builds/HQPrototypeLocal)
    // joins, and its snapshot must show the Charger's poses and the struck host's
    // knock-back cue and shove.
    public static class MonsterPolishChargerChecks
    {
        private const string Log = "Temp/polish-Charger-matrix.log";
        private const string GuestFlag = "Temp/polish-Charger-guest.flag";
        private const string GuestDir = "Temp/polish-Charger-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private static bool guestMode;
        private static Process guest;
        private static int guestCommand = 2400;
        private static string lastReply = string.Empty;

        private static readonly Stack<IEnumerator> stack = new();
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged, running;
        private static readonly List<GameObject> props = new();
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static MonsterSettings Settings => MonsterSettings.Get();

        [MenuItem("Sunk Cost/Prototype/Run Charger polish checks (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Charger polish checks running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (running) throw new InvalidOperationException("Already running");
            Directory.CreateDirectory("Temp");
            guestMode = File.Exists(GuestFlag);
            if (guestMode && !File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first (the guest rows).");
            File.WriteAllText(Log, "Charger polish checks started " + DateTime.Now + (guestMode ? " (the guest rows)" : string.Empty) + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Charger polish checks: MATRIX_PASS"); else Debug.LogError("Charger polish checks: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            foreach (GameObject p in props) if (p != null) Object.Destroy(p);
            props.Clear();
            try { M.ServerDespawnMonsters(); } catch (Exception) { }
            stack.Clear();
            running = false;
            MonsterSettings.RosterOverrideForTests = null;
            MonsterSettings.GhostChanceOverrideForTests = null;
            PlayerVitalsSettings.TankSecondsOverrideForTests = null;
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

        private static IEnumerator Descend()
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), "down in the dive site: " + H.RideStatus());
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(1.0f);
        }

        private static float Flat(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);
        private static float Yaw(Transform t) => t.eulerAngles.y;
        private static float YawDelta(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));
        private static Vector3 FlatDir(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero; }

        // The seabed under a point (the world's colliders), a little over it.
        private static Vector3 Ground(Vector3 p)
        {
            Vector3 from = new(p.x, p.y + 6f, p.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 20f, CreatureSenses.SightMask, QueryTriggerInteraction.Ignore)) return hit.point + Vector3.up * 0.05f;
            return p;
        }

        private static Charger SpawnCharger(Vector3 at, Vector3 facing)
        {
            M.ServerDespawnMonsters();
            Creature c = MonsterRoster.ServerSpawnForChecks(MonsterKind.Charger, at);
            Check(c is Charger, "spawned the Charger at " + at.ToString("F1"));
            Vector3 to = FlatDir(facing - at);
            c.ServerPlaceForChecks(at, Quaternion.LookRotation(to == Vector3.zero ? Vector3.forward : to, Vector3.up).eulerAngles.y);
            return (Charger)c;
        }

        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            Vector3 to = FlatDir(facing - spot);
            host.TeleportLocal(spot, Quaternion.LookRotation(to == Vector3.zero ? Vector3.forward : to, Vector3.up).eulerAngles.y);
            yield return null; yield return null;
            M.ClientLookAt(facing + Vector3.up * 0.6f);
            yield return null;
        }

        private static void Heal(HQPlayerController host) => host.Vitals.ServerHealForChecks();

        private static bool InState(Charger c, string state)
        {
            CreatureRig rig = c.GetComponent<CreatureRig>();
            if (rig == null || !rig.HasAnimator) return false;
            Animator a = rig.Animator;
            int hash = Animator.StringToHash(state);
            AnimatorStateInfo now = a.GetCurrentAnimatorStateInfo(0);
            if (now.shortNameHash == hash) return true;
            return a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).shortNameHash == hash;
        }

        private static Transform Bone(Charger c, string name)
        {
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // One rush sampled a frame at a time, from the launch to the stop.
        private struct Sample { public float T; public Vector3 Pos; public float Yaw; public CreaturePose Pose; public Charger.ChargePhase Phase; public float Speed; public Vector3 Host; }

        private static float launchedAt;
        private static IEnumerator SampleRush(Charger c, List<Sample> into, Action<float> perFrame = null, float maxSeconds = 4f)
        {
            yield return Expect(() => c.Phase == Charger.ChargePhase.Rushing, Settings.ChargerWindupSeconds + 1.5f, () => "the Charger launches (" + c.ServerStatus + ")");
            float t0 = Time.time;
            launchedAt = t0;
            Vector3 last = c.transform.position;
            float lastT = t0;
            while (Time.time - t0 < maxSeconds)
            {
                float now = Time.time, dt = now - lastT;
                Vector3 pos = c.transform.position;
                into.Add(new Sample { T = now - t0, Pos = pos, Yaw = Yaw(c.transform), Pose = c.Pose, Phase = c.Phase, Speed = dt > 0f ? Flat(pos, last) / dt : 0f, Host = Host().transform.position });
                last = pos; lastT = now;
                perFrame?.Invoke(now - t0);
                if (c.Phase == Charger.ChargePhase.Recovering) break;
                yield return null;
            }
        }

        // The lowest flat speed a foot bone reaches while the body keeps its pace:
        // near zero when the clip's feet are planted for that speed.
        private static IEnumerator FootSpeeds(Charger c, float seconds, Func<bool> keep, float[] minOut, float[] bodyOut)
        {
            string[] names = { "FootL", "FootR", "HandL", "HandR" };
            Transform[] feet = names.Select(n => Bone(c, n)).ToArray();
            Vector3[] last = feet.Select(f => f != null ? f.position : Vector3.zero).ToArray();
            Vector3 bodyLast = c.transform.position;
            float t0 = Time.time, lastT = t0, bodySum = 0f; int bodyN = 0;
            // The host keeps its eyes on it: an Animator off every screen stops moving its bones.
            SkinnedMeshRenderer skin = c.GetComponentInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < minOut.Length; i++) minOut[i] = float.PositiveInfinity;
            M.ClientLookAt(c.EyePoint);
            yield return null;
            while (Time.time - t0 < seconds && keep())
            {
                M.ClientLookAt(c.EyePoint);
                float dt = Time.time - lastT; lastT = Time.time;
                bool seen = skin == null || skin.isVisible;
                if (!seen) { for (int i = 0; i < feet.Length; i++) if (feet[i] != null) last[i] = feet[i].position; bodyLast = c.transform.position; }
                if (dt > 0f && seen)
                {
                    for (int i = 0; i < feet.Length; i++)
                    {
                        if (feet[i] == null) continue;
                        float v = Flat(feet[i].position, last[i]) / dt;
                        minOut[i] = Mathf.Min(minOut[i], v);
                        last[i] = feet[i].position;
                    }
                    bodySum += Flat(c.transform.position, bodyLast) / dt; bodyN++;
                    bodyLast = c.transform.position;
                }
                yield return null;
            }
            bodyOut[0] = bodyN > 0 ? bodySum / bodyN : 0f;
            Check(bodyN >= 8, $"the feet were sampled on screen ({bodyN} frames)");
        }

        // How far the skinned body reaches ahead of the middle along a flat direction, this
        // frame (the mesh as posed now, not the bind pose): where the snout really is.
        private static Mesh snoutMesh;
        private static float SnoutAhead(Charger c, Vector3 dir)
        {
            SkinnedMeshRenderer smr = c.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) return float.NaN;
            if (snoutMesh == null) snoutMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            smr.BakeMesh(snoutMesh, true);
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            Vector3 mid = c.transform.position;
            float best = float.NegativeInfinity;
            foreach (Vector3 v in snoutMesh.vertices)
            {
                Vector3 w = m.MultiplyPoint3x4(v) - mid; w.y = 0f;
                best = Mathf.Max(best, Vector3.Dot(w, dir));
            }
            return best;
        }

        private static GameObject Wall(Vector3 centre, Vector3 across, Vector3 size)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Charger check wall";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, WorldScenes.Scene(WorldId.Dive));
            wall.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(across, Vector3.up));
            wall.transform.localScale = size;
            Physics.SyncTransforms();
            props.Add(wall);
            return wall;
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
        private static string GuestMonsterLine(string reply) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("monster=" + MonsterKind.Charger + ";")) ?? string.Empty;
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
        private static IEnumerator GuestLook(Vector3 aim) { yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(aim) + "}"); }
        private static IEnumerator GuestLamp(bool on) { yield return Send("{\"id\":{id},\"action\":\"lamp\",\"slot\":" + (on ? 1 : 0) + "}"); }

        // G1: what a guest sees of a charge that strikes the host. The server's word
        // (the pose, the knock-back cue) and the shove carried by the host's
        // NetworkTransform must reach the guest's copies.
        private static IEnumerator GuestRows(HQPlayerController host, int guestId, Func<float, float, Vector3> P, Vector3 outward)
        {
            Heading("G1 — the guest's view of a hit on the host: the Charger's poses, the knock-back cue +1, the host's copy shoved about 3 m");
            yield return GuestLamp(false);
            Vector3 guestSpot = P(0f, 12f);
            yield return GuestMove(guestSpot);
            yield return GuestLook(P(0f, 0f) + Vector3.up * 0.8f);
            yield return GuestEventually(r => Flat(VecField(GuestPlayerLine(r, guestId), "position"), guestSpot) < 1.5f && Text(GuestPlayerLine(r, guestId), "lamp") == "False", 8f, "G1 the guest stands 12 m off the lane, its lamp off");
            host.RequestLamp(true);
            yield return Expect(() => host.LampOn, 2f, () => "G1 the host's lamp is on");
            Heal(host);
            yield return HostAt(P(8f, 0f), P(-8f, 0f));
            Charger c = SpawnCharger(P(-8f, 0f), P(8f, 0f));
            yield return GuestEventually(r => GuestMonsterLine(r) != string.Empty, 6f, "G1 the guest holds a copy of the Charger");
            int serial0 = host.LastKnockback.Serial;
            yield return Snapshot();
            int guestSerial0 = int.Parse(Text(GuestPlayerLine(lastReply, host.OwnerId), "knockback"));
            string guestHealth0 = Text(GuestPlayerLine(lastReply, guestId), "health");
            Check(guestSerial0 == serial0, $"G1 before the hit the guest reads the host's knock-back serial {guestSerial0} (server {serial0})");
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 6f, () => "G1 it winds up at the host, not the dark guest (" + c.ServerStatus + ")");
            Check(c.TargetId == host.OwnerId, "G1 its target is the host");
            yield return GuestEventually(r => Text(GuestMonsterLine(r), "pose") == "Windup", 1.4f, "G1 the guest sees the wind-up (pose=Windup)");
            Vector3 hostBefore = host.transform.position;
            yield return Expect(() => c.ServerRushHits > 0, 4f, () => "G1 the rush hits the host (" + c.ServerStatus + ")");
            Vector3 hitAt = host.transform.position;
            Vector3 dir = c.RushDirection;
            Check(host.LastKnockback.Serial == serial0 + 1, "G1 the server wrote one knock-back cue");
            yield return GuestEventually(r => int.TryParse(Text(GuestPlayerLine(r, host.OwnerId), "knockback"), out int k) && k == serial0 + 1, 3f, $"G1 the guest reads the host's knock-back serial go up by 1 ({serial0} → {serial0 + 1})");
            yield return GuestEventually(r => Text(GuestMonsterLine(r), "pose") == "Recovering", 3f, "G1 the guest sees it skid and recover (pose=Recovering)");
            yield return Wait(0.8f);
            yield return Snapshot();
            Vector3 seen = VecField(GuestPlayerLine(lastReply, host.OwnerId), "position");
            float along = Vector3.Dot(Vector3.ProjectOnPlane(seen - hitAt, Vector3.up), dir);
            float serverAlong = Vector3.Dot(Vector3.ProjectOnPlane(host.transform.position - hitAt, Vector3.up), dir);
            Say($"G1 the host was shoved {serverAlong:0.00} m along the path; the guest's copy of it stands {along:0.00} m along from the hit point ({Flat(seen, host.transform.position):0.00} m from the host's own)");
            Check(along > 2.0f && Flat(seen, host.transform.position) < 0.5f, "G1 the guest sees the host shoved along the path, where the host is");
            string beforeHit = GuestPlayerLine(lastReply, host.OwnerId);
            Check(Text(beforeHit, "leak") == "True", "G1 the guest reads the host's leak");
            Check(Text(GuestPlayerLine(lastReply, guestId), "health") == guestHealth0, $"G1 the guest itself was untouched (health {guestHealth0} → {Text(GuestPlayerLine(lastReply, guestId), "health")})");
            M.ServerDespawnMonsters();
            Heal(host);
            yield return GuestEventually(r => GuestMonsterLine(r) == string.Empty, 6f, "G1 the guest's copy went with it");
        }

        // One dash to the host's left on the virtual keyboard (Alt + A), as the Listener's checks do.
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
            MonsterSettings s = Settings;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ChargerCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            MonsterSettings.RosterOverrideForTests = Array.Empty<MonsterKind>();
            MonsterSettings.GhostChanceOverrideForTests = 0f;
            PlayerVitalsSettings.TankSecondsOverrideForTests = 3600f;

            Heading("S0 — to sea and down, the host alone, no roster");
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
            yield return Descend();
            if (guestMode) yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "the guest is in the site");
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 shaft = car.BottomPosition;
            M.ServerDespawnMonsters();
            yield return Wait(0.5f);
            Check(Creature.All.Count == 0, "no roster monsters (" + Creature.All.Count + ")");
            Say($"charger: tell {s.ChargerWindupSeconds} s, rush {s.ChargerRushMeters} m ×{s.ChargerRushSpeedFactor} sprint, turn {s.ChargerTurnSeconds} s, {s.ChargerDamage} HP, hit half-width {s.ChargerHitRadius + 0.3f} m, winds up within {s.ChargerRushFromMeters} m");

            // The lane: open seabed 35 m out from the shaft at bearing 300°, running across the bearing.
            Vector3 outward = Quaternion.Euler(0f, 300f, 0f) * Vector3.forward;
            Vector3 lane = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 middle = Ground(shaft + outward * 35f);
            Vector3 P(float along, float side = 0f) => Ground(middle + lane * along + outward * side);
            // Anything standing in the lane would spoil the rows: say so.
            Vector3 laneFrom = P(-26f) + Vector3.up * 0.8f, laneTo = P(26f) + Vector3.up * 0.8f;
            bool laneClear = !Physics.SphereCast(laneFrom, 0.6f, (laneTo - laneFrom).normalized, out RaycastHit laneHit, Vector3.Distance(laneFrom, laneTo), CreatureSenses.SightMask, QueryTriggerInteraction.Ignore);
            Say("the lane " + (laneClear ? "is clear" : "meets " + laneHit.collider.name + " at " + laneHit.point.ToString("F1")) + $"; ground from {P(-26f).y:0.00} to {P(26f).y:0.00}");
            Check(laneClear, "the test lane on the seabed is open");
            if (guestMode)
            {
                yield return GuestRows(host, guestId, (a, b) => P(a, b), outward);
                Say("done (the guest rows): " + M.MonstersText());
                yield break;
            }

            // ---------------------------------------------------------------------------
            Heading("I1 — idle: no diver in sight, it stands and breathes");
            host.RequestLamp(false);
            yield return Expect(() => !host.LampOn, 2f, () => "the host's lamp is off");
            yield return HostAt(P(-8f, 16f), P(-8f));
            Charger c = SpawnCharger(P(-8f), P(0f));
            yield return Wait(1.5f);
            Check(c.Pose == CreaturePose.Idle && c.TargetId == -1, "I1 idle with no target (" + c.ServerStatus + ")");
            Check(InState(c, "Idle"), "I1 the Animator plays Idle");
            Transform head = Bone(c, "Head");
            Vector3 start = c.transform.position, h0 = head.position;
            float headTravel = 0f; Vector3 hl = h0;
            for (float t = 0f; t < 1.6f; t += Time.deltaTime) { headTravel += Vector3.Distance(head.position, hl); hl = head.position; yield return null; }
            Check(Flat(start, c.transform.position) < 0.05f, $"I1 it keeps its spot ({Flat(start, c.transform.position):0.000} m)");
            Check(headTravel > 0.01f && headTravel < 1.0f, $"I1 alive: the head moved {headTravel * 100f:0.0} cm over 1.6 s (breathing, looking)");
            M.ClientLookAt(c.EyePoint);

            // ---------------------------------------------------------------------------
            Heading("P1 — the prowl and the chase: it sees a lit diver, walks where it faces at its prowl speed, feet planted");
            host.RequestLamp(true);
            yield return Expect(() => host.LampOn, 2f, () => "the host's lamp is on");
            yield return HostAt(P(16f), P(-8f));
            c.ServerPlaceForChecks(P(-8f), Quaternion.LookRotation(-lane, Vector3.up).eulerAngles.y); // facing away: it must turn first
            yield return Expect(() => c.Pose == CreaturePose.Hunting && c.TargetId == host.OwnerId, 2f, () => "P1 it hunts the host (" + c.ServerStatus + ")");
            float turnFrom = Yaw(c.transform);
            yield return Wait(0.4f);
            Say($"P1 turned {YawDelta(turnFrom, Yaw(c.transform)):0} degrees in 0.4 s (prowl turn)");
            yield return Expect(() => Vector3.Angle(c.transform.forward, FlatDir(host.transform.position - c.transform.position)) < 10f, 2f, () => "P1 it turns to face the host");
            // Walking: heading against the velocity, the speed, the Animator, the feet.
            float[] footMin = new float[4], body = new float[1];
            float worstCrab = 0f; Vector3 lastPos = c.transform.position;
            float d0 = Flat(c.transform.position, host.transform.position);
            yield return FootSpeeds(c, 1.2f, () => { Vector3 v = FlatDir(c.transform.position - lastPos); if (v != Vector3.zero) worstCrab = Mathf.Max(worstCrab, Vector3.Angle(v, FlatDir(c.transform.forward))); lastPos = c.transform.position; return c.Pose == CreaturePose.Hunting; }, footMin, body);
            float d1 = Flat(c.transform.position, host.transform.position);
            Say($"P1 prowl {body[0]:0.00} m/s (brain {c.ProwlSpeed:0.00}); feet lowest {string.Join(", ", footMin.Select(f => f.ToString("0.00")))} m/s; worst heading-vs-path {worstCrab:0.0}°; rig rate {c.GetComponent<CreatureRig>().PlaybackRate:0.00}");
            Check(d1 < d0 - 1.5f, $"P1 it closes in ({d0:0.0} → {d1:0.0} m)");
            Check(Mathf.Abs(body[0] - c.ProwlSpeed) < c.ProwlSpeed * 0.2f, $"P1 prowl speed {body[0]:0.00} m/s ≈ {c.ProwlSpeed:0.00}");
            Check(worstCrab < 12f, $"P1 it walks where it faces (worst {worstCrab:0.0}°): no crab, no moonwalk");
            Check(InState(c, "Hunting"), "P1 the Animator plays the prowl (Hunting)");
            Check(footMin.Min() < body[0] * 0.35f, $"P1 a foot is planted while it walks (lowest foot {footMin.Min():0.00} m/s against {body[0]:0.00})");

            // ---------------------------------------------------------------------------
            Heading("C1 — the player directly ahead: the tell, the launch, the hit (35 HP, a leak, ~3 m knock-back, the jolt), one hit");
            Heal(host);
            int health0 = host.Vitals.Health;
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 6f, () => "C1 within range and facing it winds up (" + c.ServerStatus + ")");
            float windupAt = Time.time;
            Check(Flat(c.transform.position, host.transform.position) <= s.ChargerRushFromMeters + 0.3f, $"C1 the tell starts within {s.ChargerRushFromMeters} m ({Flat(c.transform.position, host.transform.position):0.0} m)");
            Check(Vector3.Angle(c.transform.forward, FlatDir(host.transform.position - c.transform.position)) < 26f, "C1 facing the host at the tell");
            yield return Expect(() => InState(c, "Windup"), 0.4f, () => "C1 the Animator plays the wind-up");
            var rush = new List<Sample>();
            int strikes0 = c.StrikeSerial, hits0 = c.ServerRushHits, felt0 = host.KnockbacksFelt, cue0 = host.LastKnockback.Serial;
            float snoutAtHit = float.NaN, hostAlongAtHit = float.NaN;
            float jolt = 0f, hitAt = -1f, joltPeakAt = -1f, joltUp = 0f, joltBack = 0f, settledAt = -1f;
            Vector3 hostAtHit = Vector3.zero;
            int healthDrops = 0, lastHealth = host.Vitals.Health;
            Transform cam = host.EyeAnchor;
            yield return SampleRush(c, rush, t =>
            {
                if (host.Vitals.Health < lastHealth) { healthDrops++; lastHealth = host.Vitals.Health; if (hitAt < 0f) { hitAt = t; hostAtHit = host.transform.position; snoutAtHit = SnoutAhead(c, c.RushDirection); hostAlongAtHit = Vector3.Dot(FlatDir(Vector3.zero) + Vector3.ProjectOnPlane(host.transform.position - c.transform.position, Vector3.up), c.RushDirection); } }
                if (hitAt < 0f) return;
                Quaternion rel = Quaternion.Inverse(Quaternion.Euler(host.LookPitch, 0f, 0f)) * cam.localRotation;
                float a = Quaternion.Angle(Quaternion.identity, rel);
                if (a > jolt) { jolt = a; joltPeakAt = t - hitAt; }
                float up = (rel * Vector3.forward).y;                       // + the view kicked up
                joltUp = Mathf.Max(joltUp, up); joltBack = Mathf.Min(joltBack, up);
            });
            // The jolt outlasts the rush's stop: follow it to the end.
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                Quaternion rel = Quaternion.Inverse(Quaternion.Euler(host.LookPitch, 0f, 0f)) * cam.localRotation;
                float up = (rel * Vector3.forward).y;
                joltUp = Mathf.Max(joltUp, up); joltBack = Mathf.Min(joltBack, up);
                if (settledAt < 0f && Quaternion.Angle(Quaternion.identity, rel) < 0.3f && joltPeakAt >= 0f) settledAt = Time.time - launchedAt - hitAt;
                if (settledAt >= 0f && Quaternion.Angle(Quaternion.identity, rel) >= 0.3f) settledAt = -1f;
                yield return null;
            }
            float windup = launchedAt - windupAt;
            Say($"C1 the tell lasted {windup:0.00} s (target {s.ChargerWindupSeconds})");
            Check(Mathf.Abs(windup - s.ChargerWindupSeconds) < 0.12f, $"C1 the tell lasts {s.ChargerWindupSeconds} s");
            Check(InState(c, "Recovering") || c.Pose == CreaturePose.Recovering, "C1 recovering after the hit");
            // The line: straight, the body pointing along it, never turning.
            Vector3 dir = c.RushDirection;
            float maxSide = rush.Max(r => Vector3.Cross(dir, r.Pos - rush[0].Pos).magnitude);
            float maxTurn = rush.Max(r => YawDelta(r.Yaw, rush[0].Yaw));
            float top = rush.Max(r => r.Speed);
            Check(Vector3.Angle(dir, FlatDir(rush[0].Host - rush[0].Pos)) < 6f, "C1 the line points at the host (standing still)");
            Check(maxSide < 0.08f && maxTurn < 0.5f, $"C1 a straight line (strays {maxSide:0.000} m, turns {maxTurn:0.00}°)");
            Check(Mathf.Abs(top - c.TopSpeed) < c.TopSpeed * 0.12f, $"C1 top speed {top:0.0} m/s ≈ {c.TopSpeed:0.0} (3 × sprint)");
            float reach = rush.FirstOrDefault(r => r.Speed > c.TopSpeed * 0.9f).T;
            Check(reach > 0f && reach < 0.4f, $"C1 a burst: 90 % of top speed after {reach:0.00} s");
            yield return Wait(0.8f);
            Check(c.ServerRushHits == hits0 + 1 && c.StrikeSerial == strikes0 + 1 && healthDrops == 1, $"C1 one hit, one strike, one health drop (hits {c.ServerRushHits - hits0}, strikes {c.StrikeSerial - strikes0}, drops {healthDrops})");
            Check(host.Vitals.Health == health0 - (int)s.ChargerDamage && host.Vitals.Leaking, $"C1 35 HP and a leak (health {health0} → {host.Vitals.Health}, leaking {host.Vitals.Leaking})");
            Say($"C1 struck {c.LastHitFaceMeters:0.00} m ahead of its middle (face at {c.FrontReach:0.00}); at the hit's frame the skinned snout reached {snoutAtHit:0.00} m and the host's middle stood {hostAlongAtHit:0.00} m ahead, so the snout was {hostAlongAtHit - 0.35f - snoutAtHit:0.00} m short of the capsule's front (negative: inside it)");
            Check(!float.IsNaN(snoutAtHit) && Mathf.Abs(hostAlongAtHit - 0.35f - snoutAtHit) < 0.3f, "C1 the snout meets the diver when the hit lands: not from afar, not deep inside");
            Check(c.LastHitFaceMeters > c.FrontReach - 0.7f && c.LastHitFaceMeters <= c.FrontReach + 0.4f, "C1 the hit lands at the ram's face, not before it and not after the head passed through");
            Check(host.KnockbacksFelt == felt0 + 1 && host.LastKnockback.Serial == cue0 + 1, "C1 one knock-back, told to the owner and in the replicated cue");
            Vector3 shove = host.transform.position - hostAtHit; shove.y = 0f;
            float along = Vector3.Dot(shove, dir);
            float kickUp = Mathf.Asin(Mathf.Clamp(joltUp, -1f, 1f)) * Mathf.Rad2Deg, swingDown = -Mathf.Asin(Mathf.Clamp(joltBack, -1f, 1f)) * Mathf.Rad2Deg;
            Say($"C1 knocked {along:0.00} m along the path ({Vector3.Cross(dir, shove).magnitude:0.00} m aside), the capsule moved {host.LastKnockbackMoved:0.00} m; the view jolted {jolt:0.0}° at its peak, {joltPeakAt * 1000f:0} ms after the blow (up {kickUp:0.0}°, back down past level {swingDown:0.0}°), settled {settledAt:0.00} s after it");
            Check(along > 2.3f && along < 3.8f, "C1 a knock-back of about 3 m along its path");
            Check(jolt > 10f && jolt < 25f, $"C1 the host's view jolts hard ({jolt:0.0}°, target about 14°)");
            Check(joltPeakAt >= 0f && joltPeakAt < 0.12f, $"C1 the jolt snaps at once ({joltPeakAt * 1000f:0} ms after the health drop)");
            Check(kickUp > 6f, $"C1 struck in the face, the view kicks up ({kickUp:0.0}°)");
            Check(swingDown > 1f, $"C1 the view swings back past level before it settles ({swingDown:0.0}°): a spring, not a fade");
            Check(settledAt > 0f && settledAt < 0.8f, $"C1 the jolt settles within 0.8 s ({settledAt:0.00} s)");
            Check(Quaternion.Angle(cam.localRotation, Quaternion.Euler(host.LookPitch, 0f, 0f)) < 0.5f, "C1 the view settles after the jolt");
            Sample hitSample = rush.LastOrDefault(r => r.T <= hitAt + 0.001f);
            float stopAfterHit = Flat(rush[rush.Count - 1].Pos, hitSample.Pos);
            Check(stopAfterHit < 2.2f, $"C1 the blow stops it: {stopAfterHit:0.00} m on after the hit");
            Check(Flat(c.transform.position, host.transform.position) > 0.8f, $"C1 it does not end up standing in the diver ({Flat(c.transform.position, host.transform.position):0.0} m)");

            // ---------------------------------------------------------------------------
            Heading("K1 — a diver held by a monster is never knocked back (ServerKnockback skips IsGrabbed)");
            int k1Serial = host.LastKnockback.Serial, k1Server = host.ServerKnockbacks, k1Felt = host.KnockbacksFelt;
            Vector3 k1At = host.transform.position;
            host.ServerGrab(c.NetworkObject, k1At);
            Check(host.IsGrabbed, "K1 the host is marked held (the server's hold, set directly)");
            host.ServerKnockback(c.transform.forward * 3f);
            Check(host.LastKnockback.Serial == k1Serial && host.ServerKnockbacks == k1Server, "K1 no knock-back cue for a held diver");
            host.ServerReleaseGrab(); // the same frame: the owner never applies this test hold
            yield return Wait(0.3f);
            Check(!host.IsGrabbed && host.KnockbacksFelt == k1Felt && Flat(host.transform.position, k1At) < 0.3f, $"K1 released, no shove felt, not moved ({Flat(host.transform.position, k1At):0.00} m)");

            // ---------------------------------------------------------------------------
            Heading("C8 — the charge animation matches the movement: the rush clip on, feet planted at 18 m/s, the tell's and the recovery's clips on time");
            Heal(host);
            yield return HostAt(P(14f, 1.5f), P(-8f));
            c = SpawnCharger(P(-8f), P(14f));
            float[] rushFoot = new float[4], rushBody = new float[1];
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 4f, () => "C8 winds up");
            yield return Expect(() => c.Phase == Charger.ChargePhase.Rushing, 2.5f, () => "C8 launches");
            // The host steps aside so the whole rush runs at speed.
            yield return HostAt(P(14f, 6f), c.transform.position);
            yield return Expect(() => c.RushSpeed > c.TopSpeed * 0.95f, 0.6f, () => "C8 at top speed");
            yield return Expect(() => InState(c, "Rushing"), 0.2f, () => "C8 the Animator plays the rush");
            yield return FootSpeeds(c, 0.45f, () => c.Phase == Charger.ChargePhase.Rushing, rushFoot, rushBody);
            Say($"C8 rush {rushBody[0]:0.0} m/s; feet lowest {string.Join(", ", rushFoot.Select(f => f.ToString("0.0")))} m/s; rig rate {c.GetComponent<CreatureRig>().PlaybackRate:0.00}");
            Check(rushFoot.Where(f => !float.IsInfinity(f)).Min() < rushBody[0] * 0.3f, $"C8 a foot is planted mid-rush (lowest {rushFoot.Min():0.0} m/s against {rushBody[0]:0.0}): the gallop is authored for this speed");
            yield return Expect(() => c.Pose == CreaturePose.Recovering, 2f, () => "C8 it brakes");
            yield return Expect(() => InState(c, "Recovering"), 0.3f, () => "C8 the Animator plays the skid and recovery");

            // ---------------------------------------------------------------------------
            Heading("C4 — a miss: the diver steps 2.5 m aside at the launch; it runs on to 20 m and skids, no spin, and recovers");
            Heal(host);
            yield return HostAt(P(4f), P(-8f));
            c = SpawnCharger(P(-8f), P(4f));
            health0 = host.Vitals.Health;
            hits0 = c.ServerRushHits;
            var miss = new List<Sample>();
            bool stepped = false;
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 4f, () => "C4 winds up");
            yield return SampleRush(c, miss, t => { if (!stepped) { stepped = true; Host().TeleportLocal(P(4f, 2.5f), Host().Yaw); } }, 5f);
            Vector3 missDir = c.RushDirection;
            float total = Flat(miss[0].Pos, miss[miss.Count - 1].Pos);
            Check(host.Vitals.Health == health0 && c.ServerRushHits == hits0, "C4 no damage on a visible miss");
            Check(Mathf.Abs(total - s.ChargerRushMeters) < 1.5f, $"C4 it runs {total:0.0} m (the rush is {s.ChargerRushMeters} m)");
            // The skid: the speed falls over several frames, not at once.
            int brakeFrames = miss.Count(r => r.Phase == Charger.ChargePhase.Braking);
            float brakeTime = miss.Where(r => r.Phase == Charger.ChargePhase.Braking).Select(r => r.T).DefaultIfEmpty(0f).Max() - miss.Where(r => r.Phase == Charger.ChargePhase.Braking).Select(r => r.T).DefaultIfEmpty(0f).Min();
            Check(brakeFrames >= 5 && brakeTime > 0.15f, $"C4 it skids to a stop ({brakeFrames} frames over {brakeTime:0.00} s), not a dead stop");
            Check(miss.Where(r => r.Phase == Charger.ChargePhase.Braking).All(r => r.Pose == CreaturePose.Recovering), "C4 the skid shows the Recovering clip");
            float missTurn = miss.Max(r => YawDelta(r.Yaw, miss[0].Yaw));
            Check(missTurn < 0.5f, $"C4 no spin through the rush and the skid ({missTurn:0.00}°)");
            float stopYaw = Yaw(c.transform), stoppedAt = Time.time;
            yield return Expect(() => c.Phase == Charger.ChargePhase.Hunting, c.RecoverSeconds + 0.3f, () => "C4 it recovers");
            float recovered = Time.time - stoppedAt;
            Check(recovered > c.RecoverSeconds - 0.15f, $"C4 winded for {recovered:0.00} s (target {c.RecoverSeconds})");
            Check(YawDelta(stopYaw, Yaw(c.transform)) < 5f, "C4 it stands still while winded (no turn on the spot)");

            // ---------------------------------------------------------------------------
            Heading("C7 — back to the chase: it turns while it walks, then waits out the 3 s before the next tell");
            Vector3 behind = FlatDir(host.transform.position - c.transform.position);
            Check(Vector3.Angle(c.transform.forward, behind) > 90f, "C7 the host is behind it after the miss");
            yield return Expect(() => c.Pose == CreaturePose.Hunting || c.Phase == Charger.ChargePhase.Windup, 1f, () => "C7 it hunts again");
            float crab = 0f; lastPos = c.transform.position;
            float until = Time.time + 1.2f;
            while (Time.time < until && c.Phase == Charger.ChargePhase.Hunting)
            {
                Vector3 v = FlatDir(c.transform.position - lastPos);
                if (v != Vector3.zero && Flat(c.transform.position, lastPos) > 0.005f) crab = Mathf.Max(crab, Vector3.Angle(v, FlatDir(c.transform.forward)));
                lastPos = c.transform.position;
                yield return null;
            }
            Check(crab < 15f, $"C7 walking out of the turn where it faces (worst {crab:0.0}°)");
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 5f, () => "C7 it winds up again (" + c.ServerStatus + ")");
            float gap = Time.time - c.LastStopAt;
            Check(gap >= s.ChargerTurnSeconds - 0.05f, $"C7 the next tell {gap:0.00} s after the stop (≥ {s.ChargerTurnSeconds})");
            Check(Vector3.Angle(c.transform.forward, FlatDir(host.transform.position - c.transform.position)) < 26f, "C7 facing the host when the tell starts");

            // ---------------------------------------------------------------------------
            Heading("C2 — the player moving sideways: the aim follows through the tell, locks for the last beat, and the rush keeps its line");
            Heal(host);
            yield return HostAt(P(4f, -3f), P(-8f));
            c = SpawnCharger(P(-8f), P(4f, -3f));
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 4f, () => "C2 winds up");
            float tellStart = Time.time, lockAt = tellStart + s.ChargerWindupSeconds - c.AimLockSeconds;
            float strafe = 4.5f, side = -3f;
            float yawAtLock = float.NaN, aimErrAtLock = 0f, yaw0 = Yaw(c.transform);
            while (c.Phase == Charger.ChargePhase.Windup)
            {
                side += strafe * Time.deltaTime;
                Host().TeleportLocal(P(4f, side), Host().Yaw);
                if (float.IsNaN(yawAtLock) && Time.time >= lockAt + 0.02f)
                {
                    yawAtLock = Yaw(c.transform);
                    aimErrAtLock = Vector3.Angle(c.transform.forward, FlatDir(Host().transform.position - c.transform.position));
                }
                yield return null;
            }
            float yawAtLaunch = Yaw(c.transform);
            Say($"C2 tracked {YawDelta(yaw0, yawAtLaunch):0.0}° while the host strafed {strafe} m/s; aim error at the lock {aimErrAtLock:0.0}°");
            Check(YawDelta(yaw0, yawAtLaunch) > 8f, "C2 the aim followed the moving diver through the tell");
            Check(aimErrAtLock < 10f, "C2 on target when the aim locks");
            Check(!float.IsNaN(yawAtLock) && YawDelta(yawAtLock, yawAtLaunch) < 0.3f, $"C2 the aim held still over the last {c.AimLockSeconds} s ({YawDelta(yawAtLock, yawAtLaunch):0.00}°)");
            Check(Vector3.Angle(c.RushDirection, FlatDir(c.transform.forward)) < 0.5f, "C2 it rushes the way its body points");
            var side2 = new List<Sample>();
            yield return SampleRush(c, side2, t => { side += strafe * Time.deltaTime; Host().TeleportLocal(P(4f, side), Host().Yaw); });
            float sideTurn = side2.Max(r => YawDelta(r.Yaw, side2[0].Yaw));
            float stray = side2.Max(r => Vector3.Cross(c.RushDirection, r.Pos - side2[0].Pos).magnitude);
            Check(sideTurn < 0.5f && stray < 0.08f, $"C2 no homing: the rush kept its line while the diver moved ({sideTurn:0.00}°, {stray:0.000} m)");
            Say("C2 " + (c.ServerRushHits > 0 ? "hit" : "missed") + " the strafing diver");

            // ---------------------------------------------------------------------------
            Heading("C5 — a charge into a wall: it stops with its face at the wall, dazed; nothing behind the wall is hit");
            Heal(host);
            health0 = host.Vitals.Health;
            // A low wall (it sees over it; its body cannot pass), across the lane, 6 m ahead.
            Vector3 wallAt = P(-2f) + Vector3.up * 0.4f;
            GameObject wall = Wall(wallAt, lane, new Vector3(8f, 0.8f, 0.6f));
            yield return HostAt(P(2.5f), P(-8f));
            c = SpawnCharger(P(-8f), P(2.5f));
            int walls0 = c.ServerWallStops;
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 4f, () => "C5 it sees the host over the low wall and winds up (" + c.ServerStatus + ")");
            var wallRush = new List<Sample>();
            yield return SampleRush(c, wallRush);
            float faceGap = Vector3.Dot(wallAt - c.transform.position, lane) - 0.3f; // from its middle to the wall's near face
            float snoutAtWall = SnoutAhead(c, lane);
            Say($"C5 stopped {faceGap:0.00} m from the wall (its face reaches {c.FrontReach:0.00}); the skinned snout reaches {snoutAtWall:0.00} m, {snoutAtWall - faceGap:0.00} m into the wall (positive: through its face)");
            Check(Mathf.Abs(snoutAtWall - faceGap) < 0.1f, "C5 the snout stops at the wall: not in it, not short of it");
            Check(c.ServerWallStops == walls0 + 1, "C5 the wall stopped it");
            Check(faceGap > c.FrontReach - 0.25f && faceGap < c.FrontReach + 0.8f, "C5 its face at the wall: no head through the wall, no stop short of it");
            Check(host.Vitals.Health == health0, "C5 the host behind the wall is untouched");
            float wallStop = Time.time;
            yield return Expect(() => c.Phase == Charger.ChargePhase.Hunting, c.RecoverSeconds + 1.5f, () => "C5 it recovers from the wall");
            Say($"C5 dazed {Time.time - wallStop:0.00} s after the wall");
            Object.Destroy(wall); props.Remove(wall);
            yield return null;

            // ---------------------------------------------------------------------------
            Heading("O1 — obstacles in the chase: a diver behind a tall wall is lost; a low wall in the prowl's way is walked round, head out of it");
            GameObject tall = Wall(P(-2f) + Vector3.up * 1.5f, lane, new Vector3(10f, 3f, 0.6f));
            yield return HostAt(P(3f), P(-8f));
            c = SpawnCharger(P(-9f), P(3f));
            yield return Wait(1.0f);
            Check(c.TargetId == -1 && c.Pose == CreaturePose.Idle, "O1 no sight through the wall: idle (" + c.ServerStatus + ")");
            Object.Destroy(tall); props.Remove(tall);
            yield return null;
            // A low wall 6 m ahead and the diver 20 m off beyond it: it prowls (too far for a tell), meets the wall, follows it round.
            GameObject low = Wall(P(-2f) + Vector3.up * 0.4f, lane, new Vector3(8f, 0.8f, 0.6f));
            yield return HostAt(P(12f), P(-8f));
            c = SpawnCharger(P(-8f), P(12f));
            yield return Expect(() => c.Pose == CreaturePose.Hunting, 2f, () => "O1 it prowls toward the far diver");
            float worstNose = 99f, deepest = 0f;
            float deadline = Time.time + 9f;
            bool past = false;
            while (Time.time < deadline && c.Phase == Charger.ChargePhase.Hunting)
            {
                Vector3 face = c.transform.position + c.transform.forward * c.FrontReach;
                float toPlane = Vector3.Dot(P(-2f) - face, lane) - 0.3f;               // + before the wall's near face
                bool inSpan = Mathf.Abs(Vector3.Dot(face - P(-2f), outward)) < 4f;
                if (inSpan) { worstNose = Mathf.Min(worstNose, toPlane); deepest = Mathf.Min(deepest, toPlane); }
                if (Vector3.Dot(c.transform.position - P(-2f), lane) > 0.5f) past = true;
                yield return null;
            }
            Say($"O1 its face came within {worstNose:0.00} m of the wall; {(past ? "it got past the wall" : "it did not get past")}; ended {c.ServerStatus}");
            Check(deepest > -0.25f, $"O1 its head stays out of the wall ({deepest:0.00} m into it at worst)");
            Check(past || c.Phase == Charger.ChargePhase.Windup, "O1 it gets round the wall (or winds up at the diver): no sticking");
            Object.Destroy(low); props.Remove(low);
            yield return null;

            // ---------------------------------------------------------------------------
            Heading("C6 — repeated cycles: three tells, rushes and recoveries in a row, dodged each time; nothing sticks");
            Heal(host);
            yield return HostAt(P(4f), P(-8f));
            c = SpawnCharger(P(-8f), P(4f));
            float dodge = 2.6f;
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                int rushes = c.ServerRushes;
                yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 9f, () => $"C6 cycle {cycle}: the tell ({c.ServerStatus})");
                yield return Expect(() => c.Phase == Charger.ChargePhase.Rushing, 2.5f, () => $"C6 cycle {cycle}: the launch");
                Vector3 hostNow = Host().transform.position;
                Vector3 aside = Vector3.Cross(Vector3.up, c.RushDirection).normalized * dodge;
                Host().TeleportLocal(Ground(hostNow + aside), Host().Yaw);
                dodge = -dodge;
                yield return Expect(() => c.Phase == Charger.ChargePhase.Recovering, 3f, () => $"C6 cycle {cycle}: the stop");
                Check(c.ServerRushes == rushes + 1, $"C6 cycle {cycle}: one rush");
                yield return Expect(() => c.Phase == Charger.ChargePhase.Hunting, c.RecoverSeconds + 0.3f, () => $"C6 cycle {cycle}: back to the hunt");
                M.ClientLookAt(c.EyePoint);
            }
            Check(c.ServerRushHits == 0, "C6 dodged three times: no hit");
            Check(Mathf.Abs(Host().transform.position.y - c.transform.position.y) < 1.5f && Flat(c.transform.position, shaft) > s.SafeZoneMeters, "C6 both still on the seabed, off the safe ground");

            // ---------------------------------------------------------------------------
            Heading("D1 — a real dash (Alt + A) at the launch dodges the rush: no hit, no homing after the diver");
            Heal(host);
            yield return Wait(3.2f); // the dash's cooldown, whatever came before
            yield return HostAt(P(8f), P(-8f));
            c = SpawnCharger(P(-8f), P(8f));
            int d1Hits = c.ServerRushHits, d1Health = host.Vitals.Health, d1Dashes = host.Dashes;
            yield return Expect(() => c.Phase == Charger.ChargePhase.Windup, 4f, () => "D1 winds up (" + c.ServerStatus + ")");
            M.ClientLookAt(c.EyePoint);
            yield return Expect(() => c.Phase == Charger.ChargePhase.Rushing, 2.5f, () => "D1 launches");
            Vector3 d1From = host.transform.position;
            float d1Yaw = Yaw(c.transform);
            yield return DashLeft();
            yield return Expect(() => c.Phase == Charger.ChargePhase.Recovering, 3f, () => "D1 the rush ends (" + c.ServerStatus + ")");
            float d1Aside = Vector3.Cross(c.RushDirection, host.transform.position - d1From).magnitude;
            Say($"D1 dashed {Flat(host.transform.position, d1From):0.0} m ({d1Aside:0.0} m off the line); dashes {d1Dashes} → {host.Dashes}");
            Check(host.Dashes == d1Dashes + 1, "D1 the host dashed");
            Check(c.ServerRushHits == d1Hits && host.Vitals.Health == d1Health, "D1 the dash dodged it: no hit, no damage");
            Check(YawDelta(d1Yaw, Yaw(c.transform)) < 0.5f, "D1 it never turned after the dashing diver");


            M.ServerDespawnMonsters();
            Heal(host);
            Say("done: " + M.MonstersText());
        }
    }
}
