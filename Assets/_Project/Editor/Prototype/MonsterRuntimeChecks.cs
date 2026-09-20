using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Interaction;
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
    // The monsters (docs/DESIGN.md §6, 20 September 2026), host and guest below:
    // the day's roster; the lamp switch; the Long Walker's chase and the safe
    // car; the Weeping Angel frozen under a look; the Charger's wind-up, rush and
    // hit (35 HP and a leak); a friend's patch, once a day, and the kit; the hold
    // on E; the Lure's bolt at a lit lamp and its loss of interest in the dark;
    // the Listener deaf to a crouch and shooting at a sprint; the Impostor seen
    // by its victim only; the Elevator Ghost on a return trip and its slam; then
    // the two killers on day 2. Log: Temp/monsters-matrix.log. Started by
    // CameraClearanceMatrixDriver.Start("monsters").
    public static class MonsterRuntimeChecks
    {
        private const string Log = "Temp/monsters-matrix.log";
        private const string GuestDir = "Temp/monsters-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1700;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static MonsterSettings Settings => MonsterSettings.Get();

        [MenuItem("Sunk Cost/Prototype/Run monsters matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Monsters matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Monsters matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Monsters matrix: MATRIX_PASS"); else Debug.LogError("Monsters matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
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
        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.4f);
            }
            throw new Exception(label + "\n" + lastReply);
        }
        private static string Action(string action) => "{\"id\":{id},\"action\":\"" + action + "\"}";
        private static string Vec(Vector3 v) => "{\"x\":" + F(v.x) + ",\"y\":" + F(v.y) + ",\"z\":" + F(v.z) + "}";
        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static string GuestMonsterLine(string reply, MonsterKind kind) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("monster=" + kind + ";")) ?? string.Empty;
        private static float Field(string line, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, key + @"=(-?[0-9.]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
        }
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static IEnumerator GuestMove(Vector3 to) { yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static IEnumerator GuestLook(Vector3 aim) { yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(aim) + "}"); }
        private static IEnumerator GuestLamp(bool on) { yield return Send("{\"id\":{id},\"action\":\"lamp\",\"slot\":" + (on ? 1 : 0) + "}"); }

        private static Creature Spawn(MonsterKind kind, Vector3 at)
        {
            Creature c = MonsterRoster.ServerSpawnForChecks(kind, at);
            Check(c != null, "spawned " + kind + " at " + at.ToString("F1"));
            return c;
        }
        private static IEnumerator Despawn()
        {
            M.ServerDespawnMonsters();
            yield return Expect(() => Creature.All.Count == 0, 5f, () => "every monster despawned (" + Creature.All.Count + " left)");
        }
        private static float FlatDistance(Vector3 a, Vector3 b) => CreatureSenses.Flat(a, b);
        // A spot on the seabed at a bearing and distance from the shaft (metres, flat).
        private static Vector3 Seabed(ElevatorController car, float bearingDeg, float distance)
        {
            Vector3 dir = Quaternion.Euler(0f, bearingDeg, 0f) * Vector3.forward;
            return car.BottomPosition + dir * distance + Vector3.up * 0.15f;
        }
        // Stand the host at a spot facing a point.
        private static IEnumerator HostAt(Vector3 spot, Vector3 facing)
        {
            HQPlayerController host = Host();
            Vector3 to = facing - spot; to.y = 0f;
            host.TeleportLocal(spot, Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : Vector3.forward, Vector3.up).eulerAngles.y);
            yield return null; yield return null;
            M.ClientLookAt(facing);
            yield return null;
        }
        // Stand near an item on the seafloor, aim at it until the dot lands, grab it.
        private static IEnumerator GrabItem(CarryableItem item)
        {
            HQPlayerController host = Host();
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 fromCar = item.transform.position - car.transform.position; fromCar.y = 0f;
            Vector3 toward = fromCar.normalized, side = Vector3.Cross(Vector3.up, toward);
            foreach (Vector3 offset in new[] { -toward * 1.1f, toward * 1.1f, side * 1.1f, -side * 1.1f })
            {
                Vector3 stand = item.transform.position + offset; stand.y = car.BottomPosition.y + 0.15f;
                host.TeleportLocal(stand, host.Yaw); yield return null; yield return null;
                float deadline = Time.unscaledTime + 3f; int tries = 0;
                while (Time.unscaledTime < deadline && host.CurrentTarget != item)
                {
                    if (tries++ % 30 == 0) host.TeleportLocal(stand, host.Yaw);
                    H.ClientLookAtItem(item.name);
                    yield return null;
                }
                if (host.CurrentTarget == item) break;
            }
            Check(host.CurrentTarget == item, "the dot is on " + item.DisplayName);
            host.Inventory.RequestGrab(item);
            yield return Expect(() => item.HolderClientId == host.OwnerId && item.State == ItemState.Held, 3f, () => item.DisplayName + " grabbed (" + item.State + ")");
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
            PlayerVitals vitals = host.Vitals;
            Check(vitals != null, "the player prefab carries PlayerVitals (run the vitals setup)");
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            Check(Resources.Load<MonsterSettings>(MonsterSettings.ResourceName) != null, "MonsterSettings lives in Resources (run the monster setup)");
            MonsterSettings s = Settings;
            Say($"roster {s.MonstersPerDive} per dive, spawn ≥ {s.SpawnMinMeters} m, safe zone {s.SafeZoneMeters} m, ghost chance {s.GhostChance}, green {s.GhostSeconds} s; walker ×{s.WalkerSpeedFactor}; charger {s.ChargerDamage} HP; lure {s.LureDamage}; listener {s.ListenerDamage}; impostor {s.ImpostorDamage}");
            foreach (MonsterKind kind in MonsterCatalog.Walkers)
                Check(Enumerable.Range(0, host.NetworkManager.SpawnablePrefabs.GetObjectCount()).Any(i => host.NetworkManager.SpawnablePrefabs.GetObject(true, i)?.name == MonsterCatalog.PrefabName(kind)), MonsterCatalog.PrefabName(kind) + " is a registered spawnable");
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("MonsterCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            MonsterSettings.RosterOverrideForTests = null; // day 1: the real draw
            MonsterSettings.GhostChanceOverrideForTests = 1f;

            Heading("M0 — to sea; a guest joins; both ride down on day 1");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "the guest's copy is here");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            PlayerVitals remoteVitals = remote.Vitals;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "M0 the guest joined at sea");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            float cabinFloor = DeckCabinBuilder.FloorThicknessMeters + 0.05f;
            H.MoveLocalIntoDeckCabin("Sea");
            yield return GuestMove(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor);
            yield return Wait(0.5f);
            yield return Descend(1);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "M0 the guest is in the site");
            Check(flow.GhostRolls == 0, "M0 the ride down rolled no Ghost (never the first arrival)");
            ElevatorController car = WorldSceneFlow.FindCar();
            float floorY = car.BottomPosition.y + 0.15f;
            Vector3 shaft = car.BottomPosition;

            Heading("R1 — the day's roster: three distinct walkers on the seabed, away from the shaft and the divers");
            yield return Expect(() => Creature.All.Count >= s.MonstersPerDive, s.WakeDelaySeconds + 8f, () => $"R1 {Creature.All.Count} monsters spawned (target {s.MonstersPerDive})");
            Check(Creature.All.Count == s.MonstersPerDive, $"R1 exactly {s.MonstersPerDive} monsters");
            Check(Creature.All.Select(c => c.Kind).Distinct().Count() == Creature.All.Count, "R1 distinct kinds: " + string.Join(", ", Creature.All.Select(c => c.Kind)));
            MonsterRoster roster = MonsterRoster.Instance;
            Check(roster != null && roster.Rolled && roster.LastSpawns.Count == Creature.All.Count, "R1 the roster on the day state made the draw");
            // They walk from their spots at once (an Angel is at the car's edge in seconds): judge the spots.
            foreach ((MonsterKind kind, Vector3 at) in roster.LastSpawns)
            {
                Check(FlatDistance(at, shaft) >= s.SpawnMinMeters - 1f, $"R1 {kind} appeared {FlatDistance(at, shaft):0} m from the shaft (≥ {s.SpawnMinMeters})");
                Check(FlatDistance(at, host.transform.position) >= s.SpawnClearOfDiversMeters - 2f && FlatDistance(at, remote.transform.position) >= s.SpawnClearOfDiversMeters - 2f, $"R1 {kind} appeared clear of the divers (they are in the car)");
            }
            foreach (Creature c in Creature.All)
            {
                Check(c.gameObject.scene == WorldScenes.Scene(WorldId.Dive), "R1 " + c.Kind + " stands in the dive scene");
                Check(Mathf.Abs(c.transform.position.y - car.BottomPosition.y) < 2.5f, $"R1 {c.Kind} stands on the seabed (y {c.transform.position.y:0.0})");
                Check(FlatDistance(c.transform.position, shaft) >= s.SafeZoneMeters - 0.5f, $"R1 {c.Kind} keeps off the safe ground ({FlatDistance(c.transform.position, shaft):0.0} m from the shaft)");
            }
            Check(!host.IsDead && !remote.IsDead && vitals.Health == vitals.Settings.MaxHealth, "R1 the divers in the car are untouched");
            Say("R1 " + M.MonstersText());
            yield return GuestEventually(r => r.Split('\n').Count(l => l.StartsWith("monster=")) == s.MonstersPerDive, 10f, "R1 the guest holds copies of the three");
            yield return Despawn();
            yield return GuestEventually(r => !r.Contains("monster="), 10f, "R1 the guest's copies went with them");
            MonsterSettings.RosterOverrideForTests = Array.Empty<MonsterKind>(); // day 2's site rolls nothing: the rows spawn their own

            Heading("L1 — F: the headlamp's switch, replicated both ways");
            Check(host.LampOn, "L1 the host's lamp is on after landing");
            yield return Press(Key.F);
            yield return Expect(() => !host.LampOn, 2f, () => "L1 F turned the host's lamp off");
            yield return GuestEventually(r => GuestPlayerLine(r, host.OwnerId).Contains("lamp=False"), 6f, "L1 the guest reads the host's lamp off");
            yield return Press(Key.F);
            yield return Expect(() => host.LampOn, 2f, () => "L1 F turned it on again");
            yield return GuestLamp(false);
            yield return Expect(() => !remote.LampOn, 4f, () => "L1 the guest's F reaches the server");
            yield return GuestLamp(true);
            yield return Expect(() => remote.LampOn, 4f, () => "L1 and on again");

            // The guest parks far from the rows, looking at the wall (its eyes must not
            // freeze the Angel), lamp on.
            Vector3 guestPark = Seabed(car, 200f, 35f);
            yield return GuestMove(guestPark);
            yield return GuestLook(Quaternion.Euler(0f, 200f, 0f) * Vector3.forward);

            Heading("W1 — the Long Walker sees a lit diver and walks after them at 85 % of walking speed; the car is safe");
            Vector3 stand = Seabed(car, 20f, 20f);
            Vector3 walkerAt = Seabed(car, 20f, 38f);
            yield return HostAt(stand, walkerAt);
            Creature walker = Spawn(MonsterKind.LongWalker, walkerAt);
            yield return Expect(() => walker.Pose == CreaturePose.Hunting && walker.TargetId == host.OwnerId, 4f, () => "W1 it saw the host and hunts (" + walker.ServerStatus + ")");
            float d0 = FlatDistance(walker.transform.position, host.transform.position); float t0 = Time.unscaledTime;
            yield return Wait(2f);
            float closed = (d0 - FlatDistance(walker.transform.position, host.transform.position)) / (Time.unscaledTime - t0);
            float expectedSpeed = host.WalkSpeed * s.WalkerSpeedFactor;
            Check(closed > expectedSpeed * 0.6f && closed < expectedSpeed * 1.3f, $"W1 it closes at {closed:0.0} m/s (walking {host.WalkSpeed} × {s.WalkerSpeedFactor} = {expectedSpeed:0.0})");
            Vector3 far = Seabed(car, 110f, 40f);
            yield return HostAt(far, walkerAt);
            yield return Wait(2f);
            Check(walker.Pose == CreaturePose.Hunting && walker.TargetId == host.OwnerId, "W1 out of sight, it still walks after the host: " + walker.ServerStatus);
            yield return GuestEventually(r => GuestMonsterLine(r, MonsterKind.LongWalker).Contains("pose=Hunting"), 6f, "W1 the guest reads its pose");
            H.MoveLocalIntoCar(); yield return Wait(0.5f);
            walker.ServerPlaceForChecks(Seabed(car, 20f, 9f), 200f);
            yield return Wait(4f);
            float toShaft = FlatDistance(walker.transform.position, shaft);
            Check(toShaft >= s.SafeZoneMeters - 0.4f, $"W1 it stops at the car's doorway: {toShaft:0.0} m from the shaft (safe ground {s.SafeZoneMeters} m)");
            Check(!host.IsDead && vitals.Health == vitals.Settings.MaxHealth, "W1 inside the car the host is untouched");
            yield return Despawn();

            Heading("A1 — the Weeping Angel: frozen under a look, sprinting when unwatched");
            stand = Seabed(car, 60f, 22f);
            Vector3 angelAt = Seabed(car, 60f, 40f);
            yield return HostAt(stand, angelAt);
            Creature angel = Spawn(MonsterKind.WeepingAngel, angelAt);
            yield return Expect(() => angel.Pose == CreaturePose.Frozen, 3f, () => "A1 watched: frozen (" + angel.ServerStatus + ")");
            Vector3 frozenAt = angel.transform.position;
            yield return Wait(2f);
            Check(Vector3.Distance(angel.transform.position, frozenAt) < 0.2f, "A1 a frozen Angel does not move");
            M.ClientLookAt(stand + (stand - angelAt)); yield return null; // the host turns its back
            yield return Expect(() => angel.Pose == CreaturePose.Hunting, 3f, () => "A1 unwatched: it hunts (" + angel.ServerStatus + ")");
            d0 = FlatDistance(angel.transform.position, host.transform.position); t0 = Time.unscaledTime;
            yield return Wait(1.5f);
            closed = (d0 - FlatDistance(angel.transform.position, host.transform.position)) / (Time.unscaledTime - t0);
            Check(closed > host.SprintSpeed * 0.5f, $"A1 it comes at {closed:0.0} m/s (sprint {host.SprintSpeed})");
            M.ClientLookAt(angel.transform.position + Vector3.up * angel.EyeHeight); yield return null;
            yield return Expect(() => angel.Pose == CreaturePose.Frozen, 2f, () => "A1 looked at again: frozen (" + angel.ServerStatus + ")");
            Check(!host.IsDead, "A1 the host lives");
            yield return Despawn();

            Heading("C1 — the Charger: the shake, the rush, 35 HP and a leak; LEAK on the visor");
            stand = Seabed(car, 300f, 24f);
            Vector3 chargerAt = Seabed(car, 300f, 34f);
            yield return HostAt(stand, chargerAt);
            Creature charger = Spawn(MonsterKind.Charger, chargerAt);
            yield return Expect(() => charger.Pose == CreaturePose.Windup, 4f, () => "C1 facing the host it winds up (" + charger.ServerStatus + ")");
            float windupAt = Time.unscaledTime;
            yield return Expect(() => charger.Pose == CreaturePose.Rushing, s.ChargerWindupSeconds + 1f, () => "C1 then it rushes");
            Say($"C1 wind-up lasted {Time.unscaledTime - windupAt:0.0} s (target {s.ChargerWindupSeconds})");
            yield return Expect(() => vitals.Leaking && vitals.Health <= vitals.Settings.MaxHealth - s.ChargerDamage + 1, 4f, () => $"C1 the rush hit: health {vitals.Health}, leaking {vitals.Leaking}");
            yield return null;
            Check(hud.Visor.Leaking, "C1 the visor says LEAK");
            yield return GuestEventually(r => GuestPlayerLine(r, host.OwnerId).Contains("leak=True"), 6f, "C1 the guest reads the host's leak");
            float airBefore = vitals.ServerAirSeconds; t0 = Time.unscaledTime;
            yield return Wait(2f);
            float drain = (airBefore - vitals.ServerAirSeconds) / (Time.unscaledTime - t0);
            Check(drain > vitals.Settings.LeakDrainMultiplier * 0.7f, $"C1 a leaking tank drains at {drain:0.0} s/s (×{vitals.Settings.LeakDrainMultiplier})");
            yield return Despawn();

            Heading("P1 — a friend's hands patch the leak, once a day; P2 the second one needs a kit");
            yield return HostAt(stand, chargerAt);
            yield return GuestMove(stand + (chargerAt - stand).normalized * 1.5f);
            yield return Wait(0.5f);
            yield return Send(Action("patch"));
            Check(lastReply.Contains("patch requested"), "P1 the guest asked to patch: " + lastReply.Split('\n')[0]);
            yield return Expect(() => !vitals.Leaking, 4f, () => "P1 the host's leak is closed");
            yield return Expect(() => vitals.PatchNotice.Contains("Patched by"), 3f, () => "P1 the host was told: " + vitals.PatchNotice);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("patchNotice='Patched "), 6f, "P1 the guest was told it patched");
            vitals.ServerSetLeak(true); yield return Wait(0.3f);
            Check(vitals.FriendPatchedToday, "P2 the host reads that a friend patched it today");
            yield return Send(Action("patch"));
            yield return Wait(1.5f);
            Check(vitals.Leaking, "P2 a second patch by a friend the same day is refused: still leaking");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("once today"), 6f, "P2 the guest was told why");

            Heading("P3 — the patch kit: left click closes your own leak, once");
            CarryableItem kit = M.ServerSpawnPatchKit(stand + Vector3.right * 1.2f);
            Check(kit != null, "P3 a patch kit spawned by the host (run the monster setup for the prefab)");
            yield return Expect(() => kit.IsSpawned, 3f, () => "P3 spawned");
            yield return GrabItem(kit);
            yield return null;
            Check(hud.PromptText.Contains("patch your leak"), "P3 in hand the prompt says what left click does: " + hud.PromptText);
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Expect(() => !vitals.Leaking, 3f, () => "P3 the kit closed the leak");
            PatchKitItem kitItem = kit.GetComponent<PatchKitItem>();
            Check(kitItem.IsUsed && kit.DisplayName == PatchKitItem.UsedName && kit.UseAction == ItemUseAction.None, $"P3 now a {kit.DisplayName}: nothing on use");
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Wait(0.6f);
            Check(kit.HolderClientId == host.OwnerId, "P3 left click does nothing with a used kit");
            host.Inventory.RequestDrop();
            yield return Expect(() => kit.HolderClientId != host.OwnerId, 3f, () => "P3 Q drops it");
            yield return GuestEventually(r => r.Contains("display=" + PatchKitItem.UsedName), 6f, "P3 the guest reads the used kit");

            Heading("P4 — the hold: E on a leaking friend for three seconds");
            remoteVitals.ServerSetLeak(true);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("leak=True"), 6f, "P4 the guest reads its own leak");
            M.ClientLookAtPlayer(remote); yield return null; yield return null;
            Check(host.CurrentPatient == remote && hud.PromptText.StartsWith("Hold E to patch"), "P4 the prompt offers the hold: " + hud.PromptText);
            Keys(Key.E);
            yield return Wait(1.2f);
            Check(host.PatchProgress > 0.2f && host.PatchProgress < 0.7f, $"P4 the hold is under way ({host.PatchProgress:0.00})");
            Check(hud.PromptText.Contains(" s"), "P4 the prompt counts down: " + hud.PromptText);
            yield return Wait(vitals.Settings.TeammatePatchSeconds);
            Keys(); yield return null;
            yield return Expect(() => !remoteVitals.Leaking, 4f, () => "P4 the guest's leak is closed by the host's hands");
            yield return Expect(() => vitals.PatchNotice.StartsWith("Patched "), 3f, () => "P4 the host was told: " + vitals.PatchNotice);
            // The same friend a second time today: the prompt says so and the dot is not offered.
            remoteVitals.ServerSetLeak(true); yield return Wait(0.3f);
            Check(remoteVitals.FriendPatchedToday, "P4 the guest reads that a friend patched it today");
            M.ClientLookAtPlayer(remote); yield return null; yield return null;
            Check(host.CurrentPatient == remote, "P4 the dot is on the guest again");
            Check(hud.PromptText.Contains("patched by a friend today"), "P4 the prompt says a kit now: " + hud.PromptText);
            vitals.ServerHealForChecks(); remoteVitals.ServerHealForChecks();

            Heading("LU1 — the Lure: a bolt of light at a lit lamp; lamps off and it forgets");
            stand = Seabed(car, 150f, 22f);
            Vector3 lureAt = Seabed(car, 150f, 36f);
            yield return HostAt(stand, lureAt);
            Check(host.LampOn, "LU1 the host's lamp is on");
            Creature lure = Spawn(MonsterKind.Lure, lureAt);
            CreatureBolts lureBolts = lure.GetComponent<CreatureBolts>();
            yield return Expect(() => lureBolts.ServerFired >= 1 && lure.TargetId == host.OwnerId, 4f, () => "LU1 it saw the lamp and shot (" + lure.ServerStatus + ")");
            yield return Expect(() => vitals.Leaking && vitals.Health <= vitals.Settings.MaxHealth - s.LureDamage + 1, 4f, () => $"LU1 the bolt landed on a diver standing still: health {vitals.Health}, leaking {vitals.Leaking}");
            yield return GuestEventually(r => GuestMonsterLine(r, MonsterKind.Lure).Length > 0, 6f, "LU1 the guest holds the Lure");
            yield return Press(Key.F);
            yield return Expect(() => !host.LampOn, 2f, () => "LU1 lamp off");
            int firedAtDark = lureBolts.ServerFired;
            yield return Wait(1f);
            firedAtDark = Mathf.Max(firedAtDark, lureBolts.ServerFired); // a bolt already in the air is not a new one
            yield return Expect(() => lure.Pose == CreaturePose.Idle, s.LureForgetSeconds + 3f, () => "LU1 in the dark it lost interest (" + lure.ServerStatus + ")");
            Check(lureBolts.ServerFired <= firedAtDark + 1, $"LU1 no more bolts in the dark ({lureBolts.ServerFired - firedAtDark} after the lamp went off)");
            yield return Press(Key.F);
            yield return Expect(() => host.LampOn, 2f, () => "LU1 lamp on again");
            yield return Despawn();
            vitals.ServerHealForChecks();

            Heading("LI1 — the Listener: deaf to a crouch, shoots at a sprint");
            stand = Seabed(car, 240f, 24f);
            Vector3 listenerAt = Seabed(car, 240f, 36f);
            yield return HostAt(stand, listenerAt);
            Creature listener = Spawn(MonsterKind.Listener, listenerAt);
            Listener ears = (Listener)listener;
            CreatureBolts listenerBolts = listener.GetComponent<CreatureBolts>();
            yield return Wait(0.5f);
            int heardBefore = ears.ServerHeard;
            Keys(Key.LeftCtrl, Key.W);
            yield return Wait(3f);
            Keys(); yield return null;
            Check(ears.ServerHeard == heardBefore && listenerBolts.ServerFired == 0, $"LI1 a crouch-walk of 3 s made no sound it could hear (heard {ears.ServerHeard - heardBefore})");
            Check(listener.Pose == CreaturePose.Idle, "LI1 it stands idle: " + listener.ServerStatus);
            // A sprint across its front: the bolt flies at where a step was and the runner is gone.
            yield return HostAt(stand, stand + Vector3.Cross(Vector3.up, listenerAt - stand));
            Keys(Key.W, Key.LeftShift);
            yield return Expect(() => ears.ServerHeard > heardBefore, 3f, () => "LI1 a sprint is heard (" + ears.ServerStatus + ")");
            yield return Expect(() => listenerBolts.ServerFired >= 1, 3f, () => "LI1 it shot a dark bolt at the sound");
            yield return Wait(2f);
            Keys(); yield return null;
            Check(ears.ServerLastKind == SunkCost.Noise.NoiseKind.Sprint || ears.ServerLastKind == SunkCost.Noise.NoiseKind.Footstep, "LI1 the last thing it heard was a step: " + ears.ServerLastKind);
            Check(!host.IsDead, "LI1 the host lives");
            yield return Despawn();
            vitals.ServerHealForChecks();

            Heading("I1 — the Impostor: seen by its chosen diver only, wearing a crewmate's name; a touch, then it runs");
            yield return GuestMove(guestPark);
            yield return GuestLook(Quaternion.Euler(0f, 200f, 0f) * Vector3.forward);
            Vector3 impostorAt = Seabed(car, 200f, 55f);
            yield return HostAt(Seabed(car, 20f, 22f), impostorAt);
            Creature impostor = Spawn(MonsterKind.Impostor, impostorAt);
            Impostor doppel = (Impostor)impostor;
            doppel.ServerChooseForChecks(guestId);
            PlayerIdentity hostIdentity = host.GetComponent<PlayerIdentity>();
            yield return Expect(() => impostor.TargetId == guestId, 2f, () => "I1 it chose the guest");
            Check(doppel.WornName == hostIdentity.DisplayName && doppel.WornColourIndex == hostIdentity.ColourIndex, $"I1 it wears the host's name and colour: {doppel.WornName}/{doppel.WornColourIndex}");
            yield return Wait(1f);
            ImpostorLook look = impostor.GetComponent<ImpostorLook>();
            Check(look != null && !look.ShownToLocal, "I1 the host, not the victim, does not see it");
            yield return GuestEventually(r => GuestMonsterLine(r, MonsterKind.Impostor).Contains("shown=True") && GuestMonsterLine(r, MonsterKind.Impostor).Contains("wears=" + hostIdentity.DisplayName), 8f, "I1 the guest sees it, wearing the host's name");
            yield return Expect(() => impostor.Pose == CreaturePose.Hunting, 20f, () => "I1 it came close and chases (" + impostor.ServerStatus + ")");
            yield return Expect(() => doppel.ServerTouches >= 1 && remoteVitals.Leaking, 12f, () => $"I1 a touch: the guest at {remoteVitals.Health} health, leaking {remoteVitals.Leaking}");
            Check(remoteVitals.Health <= remoteVitals.Settings.MaxHealth - s.ImpostorDamage + 1, $"I1 30 HP gone ({remoteVitals.Health})");
            yield return Expect(() => impostor.Pose == CreaturePose.Fleeing, 3f, () => "I1 then it runs away");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("leak=True"), 6f, "I1 the guest reads its leak");
            yield return Despawn();
            remoteVitals.ServerHealForChecks();

            Heading("G0 — the Elevator Ghost rides the car back down for a diver still below; wait it out");
            // The guest rides up alone; the car returns for the host, green.
            yield return HostAt(Seabed(car, 20f, 14f), shaft);
            yield return GuestMove(car.transform.position + Vector3.up * 0.2f + car.transform.right * 0.6f);
            yield return Wait(0.5f);
            int rideSerial = Day.CabinRide.Serial;
            yield return Send(Action("car"));
            yield return Expect(() => Day.CabinRide.Serial > rideSerial, 4f, () => "G0 the car took the guest's press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "G0 the guest's ride up completed");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=ShipAtSea"), 10f, "G0 the guest is on deck");
            int rollsBefore = flow.GhostRolls;
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom && flow.GhostRolls > rollsBefore, 80f, () => "G0 the car came back down empty and was rolled");
            Check(Day.Ghost.Active, "G0 with the chance forced to 1 the Ghost is in the car");
            yield return Expect(() => ElevatorGhostLight.Instance != null && ElevatorGhostLight.Instance.IsGreen, 3f, () => "G0 the car's light is green on the host");
            yield return GuestEventually(r => r.Contains("ghostGreen=True") || r.Contains("ghostActive=True"), 6f, "G0 the guest reads the green");
            Transform cabinLight = car.transform.Find("Cabin Light");
            Check(cabinLight != null && cabinLight.GetComponent<Light>().color.g > cabinLight.GetComponent<Light>().color.r, "G0 the cabin light is green");
            yield return Expect(() => !Day.Ghost.Active, s.GhostSeconds + 5f, () => "G0 the green ended by itself");
            Check(!host.IsDead && !ElevatorGhostLight.Instance.IsGreen, "G0 the host who waited outside lives; the light is white");

            Heading("G1 — walking into the green: the doors slam");
            Check(flow.ServerSummonGhostForChecks(out string ghostWhy), "G1 the Ghost summoned for the checks: " + ghostWhy);
            yield return Expect(() => ElevatorGhostLight.Instance.IsGreen, 3f, () => "G1 green again");
            int slamsBefore = Day.Ghost.SlamSerial;
            H.MoveLocalIntoCar();
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 5f, () => "G1 the host stepped in and died");
            Check(Day.Ghost.SlamSerial == slamsBefore + 1 && !Day.Ghost.Active, "G1 the doors slammed and the car is clean");
            yield return GuestEventually(r => Field(r.Split('\n')[0], "slamsHeard") >= 1f, 8f, "G1 the guest heard the slam");
            Check(PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Dive)) != null, "G1 the ordinary death: a body in the car");
            yield return Expect(() => Day.Phase == DayPhase.AtSea && Day.DiveDone, 5f, () => "G1 nobody living below: the dive is done");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 60f, () => "G1 the dead host was carried to the ship");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 30f, () => "G1 the site closed");
            Check(Creature.All.Count == 0, "G1 no monster outlived the site");

            Heading("D2 — End day; day 2 with no roster: the two killers");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "D2 End day accepted: " + endWhy);
            yield return Expect(() => !host.IsDead, 5f, () => "D2 revived");
            H.MoveLocalIntoDeckCabin("Sea");
            yield return GuestMove(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor);
            yield return Wait(0.5f);
            yield return Descend(2);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "D2 the guest is below again");
            yield return Wait(s.WakeDelaySeconds + 1f);
            Check(Creature.All.Count == 0, "D2 the forced empty roster spawned nothing");
            car = WorldSceneFlow.FindCar(); shaft = car.BottomPosition;

            Heading("W2 — the Long Walker reaches the guest: death");
            Vector3 victim = Seabed(car, 20f, 25f);
            yield return GuestMove(victim);
            yield return GuestLamp(true);
            yield return GuestLook(Quaternion.Euler(0f, 20f, 0f) * Vector3.forward);
            yield return HostAt(Seabed(car, 200f, 20f), shaft);
            yield return Wait(0.5f);
            Creature walker2 = Spawn(MonsterKind.LongWalker, victim + (Quaternion.Euler(0f, 20f, 0f) * Vector3.forward) * 4f);
            yield return Expect(() => remote.IsDead && Day.IsDead(guestId), 8f, () => "W2 taken by the Long Walker (" + walker2.ServerStatus + ")");
            Check(PlayerBody.FindFor(guestId, WorldScenes.Scene(WorldId.Dive)) != null, "W2 a body where the guest stood");
            yield return Despawn();

            Heading("A2 — the Weeping Angel reaches a diver whose back is turned: death");
            Vector3 hostSpot = Seabed(car, 200f, 20f);
            yield return HostAt(hostSpot, shaft); // looking at the shaft: the Angel comes from behind
            Creature angel2 = Spawn(MonsterKind.WeepingAngel, Seabed(car, 200f, 26f));
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 8f, () => "A2 taken by the Weeping Angel (" + angel2.ServerStatus + ")");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 90f, () => "A2 the site closed with the dead");
            Check(Creature.All.Count == 0, "A2 the Angel went with the site");
            yield return Send(Action("leave"));
            yield return Wait(1f);
            Say("done");
        }
    }
}
