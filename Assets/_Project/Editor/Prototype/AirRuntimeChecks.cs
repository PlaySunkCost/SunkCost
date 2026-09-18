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
    // Air and health (PlayerVitals; docs/DESIGN.md §3). The host alone: the tank
    // does not count on the ship, counts from the landing below at 1×, at 1.5×
    // while sprinting, L takes 5 % off; an empty tank costs health at 8/s and
    // the ordinary death follows; End day revives with a full tank and full
    // health. Then a guest: its copy's values replicate to the host, a ride up
    // on an empty tank floors health at 1 and ends with a new tank on deck.
    // Log: Temp/air-matrix.log. Started by CameraClearanceMatrixDriver.Start("air").
    public static class AirRuntimeChecks
    {
        private const string Log = "Temp/air-matrix.log";
        private const string GuestDir = "Temp/air-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1100;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        // The editor loses focus whenever the tester types elsewhere; by default the
        // Input System then drops the virtual keyboard's events too ("walked 0.0 m").
        // Ignore focus for the run, as the hands matrix does.
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run air matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Air matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Air matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Air matrix: MATRIX_PASS"); else Debug.LogError("Air matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
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
            if (!value) throw new Exception(label + "\n" + H.FlowStatus());
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
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static float Field(string line, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, key + @"=(-?[0-9.]+)");
            return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
        }
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static CarryableItem SiteItem(string name)
        {
            foreach (CarryableItem c in CarryableItem.Spawned)
                if (c != null && (c.name == name || c.name.StartsWith(name + " (day ")) && c.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) return c;
            return null;
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
        private static IEnumerator Surface()
        {
            H.MoveLocalIntoCar(); yield return Wait(0.4f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the car took the press");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride up completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Host().gameObject.scene == WorldScenes.Scene(WorldId.Sea), "back on the deck: " + H.RideStatus());
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerVitals vitals = host.Vitals;
            Check(vitals != null, "the player prefab carries PlayerVitals (run the vitals setup)");
            PlayerVitalsSettings settings = vitals.Settings;
            Say($"tank {settings.TankSeconds}s, sprint ×{settings.SprintDrainMultiplier}, suffocation {settings.SuffocationDamagePerSecond}/s, max health {settings.MaxHealth}");
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("AirCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;

            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();

            Heading("A0 — on the ship the suit is off: a full tank that does not count");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            Check(vitals.AirFraction == 1f && vitals.Health == settings.MaxHealth, $"A0 full tank and full health on deck (air {vitals.AirFraction:0.###}, health {vitals.Health})");
            yield return Wait(2f);
            Check(vitals.AirFraction == 1f, "A0 two seconds on deck cost no air");
            yield return Press(Key.L); yield return Wait(0.3f);
            Check(vitals.AirFraction == 1f, "A0 L on deck does nothing (the suit is off)");

            Heading("A1 — below, the tank counts: one second of air a second while walking");
            yield return Descend(1);
            yield return Expect(() => vitals.AirFraction < 1f, 5f, () => $"A1 the tank counts once landed (air {vitals.AirFraction:0.###})");
            float before = vitals.ServerAirSeconds; float t0 = Time.unscaledTime;
            yield return Wait(3f);
            float rate = (before - vitals.ServerAirSeconds) / (Time.unscaledTime - t0);
            Check(Mathf.Abs(rate - 1f) < 0.15f, $"A1 standing still: {rate:0.00} s of air per second (target 1)");
            Check(!vitals.ServerSprinting, "A1 not sprinting while standing");
            Check(vitals.Health == settings.MaxHealth, "A1 health untouched with air in the tank");

            Heading("A2 — sprinting drains the tank faster");
            ElevatorController car = WorldSceneFlow.FindCar();
            float floorY = car.BottomPosition.y + 0.15f;
            host.TeleportLocal(new Vector3(25f, floorY, -25f), 90f); yield return Wait(0.5f);
            before = vitals.ServerAirSeconds; t0 = Time.unscaledTime;
            Keys(Key.W, Key.LeftShift);
            yield return Wait(1f);
            bool sawSprint = vitals.ServerSprinting;
            yield return Wait(1f);
            Keys(); yield return null;
            rate = (before - vitals.ServerAirSeconds) / (Time.unscaledTime - t0);
            Say($"A2 sprinted {Vector3.Distance(host.transform.position, new Vector3(25f, floorY, -25f)):0.0} m; server saw sprinting={sawSprint}; drain {rate:0.00}/s");
            Check(sawSprint, "A2 the server judged the run a sprint from the copy's speed");
            Check(rate > 1.25f && rate < settings.SprintDrainMultiplier + 0.2f, $"A2 sprint drain {rate:0.00} s of air per second (target {settings.SprintDrainMultiplier})");
            yield return Wait(0.5f);
            Check(!vitals.ServerSprinting, "A2 stopped: no longer sprinting");

            Heading("A3 — L takes a step off the tank");
            float airBefore = vitals.AirFraction;
            yield return Press(Key.L); yield return Wait(0.2f);
            yield return Press(Key.L); yield return Wait(0.3f);
            float dropped = airBefore - vitals.AirFraction;
            Check(Mathf.Abs(dropped - 2f * settings.DebugAirStepFraction) < 0.02f, $"A3 two presses of L took {100f * dropped:0.0}% (target {200f * settings.DebugAirStepFraction:0}%)");
            Check(vitals.AirLow == vitals.AirFraction < settings.LowAirFraction, "A3 the low-air flag follows the threshold");

            Heading("K1 — an air tank on the site: half a tank back, then it is an empty one");
            CarryableItem tank = SiteItem("Air tank 1");
            Check(tank != null, "K1 Air tank 1 lies on the seafloor");
            AirTankItem tankItem = tank.GetComponent<AirTankItem>();
            Check(tankItem != null && !tankItem.IsEmpty && tank.DisplayName == AirTankItem.FullName && tank.UseAction == ItemUseAction.Breathe && !tank.HasValue, $"K1 it is a {tank.DisplayName}: breathe on use, worth nothing");
            yield return GrabItem(tank);
            yield return null;
            Check(hud.PromptText.Contains("breathe"), "K1 in hand, the prompt says what left click does: " + hud.PromptText);
            while (vitals.AirFraction > 0.30f) yield return Press(Key.L, 0.03f);
            float airLow = vitals.AirFraction;
            Say($"K1 air down to {100f * airLow:0}% before breathing");
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Expect(() => tankItem.IsEmpty, 3f, () => "K1 the tank empties on left click");
            Check(Mathf.Abs(vitals.AirFraction - (airLow + 0.5f)) < 0.02f, $"K1 half a tank back: {100f * airLow:0}% → {100f * vitals.AirFraction:0}%");
            // An empty tank is not thrown (Dan, 18 September 2026): left click does
            // nothing, no prompt for it, Q drops it; its inventory icon is the grey one.
            Check(tank.DisplayName == AirTankItem.EmptyName && tank.UseAction == ItemUseAction.None, $"K1 now a {tank.DisplayName}: nothing on use");
            Check(tank.GetComponent<Renderer>().sharedMaterial.name.Contains("Empty"), "K1 it turned grey: " + tank.GetComponent<Renderer>().sharedMaterial.name);
            Check(tank.Icon != null && tank.Icon.name.Contains("Empty"), "K1 its inventory icon is the empty one: " + (tank.Icon == null ? "none" : tank.Icon.name));
            Check(tank.HolderClientId == host.OwnerId && tank.State == ItemState.Held, "K1 still in the hand");
            yield return null;
            Check(hud.PromptText == string.Empty, "K1 no prompt for an empty tank: '" + hud.PromptText + "'");
            float airAfterFirst = vitals.AirFraction;
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Wait(0.6f);
            Check(tank.HolderClientId == host.OwnerId && tank.State == ItemState.Held, "K1 left click does nothing with an empty tank (" + tank.State + ")");
            host.Inventory.RequestDrop();
            yield return Expect(() => tank.HolderClientId != host.OwnerId, 3f, () => "K1 Q drops it (" + tank.State + ")");
            yield return Wait(0.5f);
            Check(Mathf.Abs(vitals.AirFraction - airAfterFirst) < 0.02f, "K1 the empty tank gave no air");

            Heading("K2 — a second tank never fills past full");
            CarryableItem tank2 = SiteItem("Air tank 2");
            Check(tank2 != null && !tank2.GetComponent<AirTankItem>().IsEmpty, "K2 Air tank 2 lies full on the seafloor");
            yield return GrabItem(tank2);
            Say($"K2 air {100f * vitals.AirFraction:0}% before breathing");
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Expect(() => tank2.GetComponent<AirTankItem>().IsEmpty, 3f, () => "K2 the second tank empties");
            Check(vitals.AirFraction == 1f, $"K2 the tank is full and no fuller ({100f * vitals.AirFraction:0}%)");

            Heading("K3 — a breath with the air already full is refused: the tank is kept");
            tank2.GetComponent<AirTankItem>().ServerRefillForChecks();
            yield return Expect(() => !tank2.GetComponent<AirTankItem>().IsEmpty, 2f, () => "K3 the tank in hand is full again (checks only)");
            yield return null;
            Check(hud.PromptText.Contains("keep it"), "K3 the prompt says the air is full: " + hud.PromptText);
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Wait(0.8f);
            Check(!tank2.GetComponent<AirTankItem>().IsEmpty && vitals.AirFraction == 1f, $"K3 left click at full air: the tank stays full ({100f * vitals.AirFraction:0}%)");
            Check(host.Inventory.Refusal.Contains("air is full"), "K3 the refusal reads: " + host.Inventory.Refusal);
            host.Inventory.RequestDrop();
            yield return Expect(() => tank2.HolderClientId != host.OwnerId, 3f, () => "K3 dropped");

            Heading("A4 — an empty tank costs health, then the ordinary death");
            int presses = 0;
            while (vitals.AirFraction > 0f && presses < 40) { yield return Press(Key.L, 0.03f); presses++; }
            Check(vitals.AirFraction == 0f && vitals.AirEmpty, $"A4 the tank is empty after {presses} presses");
            yield return null;
            Check(hud.Visor.AirEmpty && hud.Visor.AirLow, "A4 the visor reads NO AIR");
            int healthBefore = vitals.Health; t0 = Time.unscaledTime;
            yield return Wait(2f);
            float damage = (healthBefore - vitals.Health) / (Time.unscaledTime - t0);
            Check(Mathf.Abs(damage - settings.SuffocationDamagePerSecond) < 1.5f, $"A4 suffocating: {damage:0.0} health a second (target {settings.SuffocationDamagePerSecond})");
            Check(hud.Visor.HealthFraction < 1f, $"A4 the HP bar shows it ({hud.Visor.HealthFraction:0.00})");
            Vector3 deathSpot = host.transform.position;
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), settings.MaxHealth / settings.SuffocationDamagePerSecond + 5f, () => "A4 dead of suffocation");
            Check(PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Dive)) != null, "A4 the ordinary death: a body where the host stood");
            yield return Expect(() => Day.Phase == DayPhase.AtSea && Day.DiveDone, 5f, () => "A4 nobody living below: the dive is done");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 60f, () => "A4 the dead host was carried to the ship");

            Heading("A5 — End day: alive again with a full tank and full health");
            // End day waits for the site to close with the dead (code check A, 18 September 2026).
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 30f, () => "A5 the site closed");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "A5 End day accepted: " + endWhy);
            yield return Expect(() => !host.IsDead, 5f, () => "A5 revived");
            Check(vitals.AirFraction == 1f && vitals.Health == settings.MaxHealth, $"A5 full tank and health after the revive (air {vitals.AirFraction:0.###}, health {vitals.Health})");

            Heading("G1 — a guest's tank and health replicate to the host");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "the guest's copy is here");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "G1 the guest joined at sea");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            float cabinFloor = DeckCabinBuilder.FloorThicknessMeters + 0.05f;
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}");
            yield return Wait(0.5f);
            yield return Descend(2);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "G1 the guest is in the site");
            PlayerVitals remoteVitals = remote.Vitals;
            yield return Expect(() => remoteVitals.AirFraction < 1f, 5f, () => $"G1 the guest's tank counts on the server ({remoteVitals.AirFraction:0.###})");
            yield return GuestEventually(r => { float a = Field(GuestPlayerLine(r, guestId), "air"); return a < 1f && Mathf.Abs(a - remoteVitals.AirFraction) < 0.02f; }, 6f, "G1 the guest reads its own air within 2 % of the server's");
            yield return GuestEventually(r => Mathf.Abs(Field(GuestPlayerLine(r, host.OwnerId), "air") - vitals.AirFraction) < 0.02f, 6f, "G1 the guest reads the host's air within 2 %");
            // A fresh site every day: the tanks lie full again (the emptied ones went
            // with yesterday's site). The host breathes from one; the guest reads the
            // tank's new name from the SyncVar.
            yield return GuestEventually(r => r.Contains("display=" + AirTankItem.FullName), 6f, "G1 the guest reads the day's fresh tanks as " + AirTankItem.FullName);
            CarryableItem dayTank = SiteItem("Air tank 1");
            Check(dayTank != null && !dayTank.GetComponent<AirTankItem>().IsEmpty, "G1 Air tank 1 lies full below again");
            yield return GrabItem(dayTank);
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Expect(() => dayTank.GetComponent<AirTankItem>().IsEmpty, 3f, () => "G1 the host breathed from it");
            yield return GuestEventually(r => r.Contains("display=" + AirTankItem.EmptyName), 6f, "G1 the guest reads the used tank as an " + AirTankItem.EmptyName);
            host.Inventory.RequestDrop();
            yield return Expect(() => dayTank.HolderClientId != host.OwnerId, 3f, () => "G1 dropped");

            Heading("G2 — a ride up on an empty tank: health floors at 1 in the car, a new tank on deck");
            car = WorldSceneFlow.FindCar();
            // The guest steps into the car with the host; L drains the guest's tank to nothing first.
            for (int i = 0; i < 25 && remoteVitals.AirFraction > 0f; i++) yield return Send(Action("air_down"));
            Check(remoteVitals.AirFraction == 0f, "G2 the guest's tank is empty");
            int guestHealth = remoteVitals.Health;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * 0.2f + car.transform.right * 0.6f) + "}");
            H.MoveLocalIntoCar(); yield return Wait(0.4f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "G2 the car took the press (refusal: " + Day.LastRefusal.Text + ")");
            yield return Expect(() => Day.CabinRide.Stage == CabinRideStage.Riding, 20f, () => "G2 riding up");
            yield return Wait(2f);
            Check(remoteVitals.Health < guestHealth && remoteVitals.Health >= 1, $"G2 in the car the guest still suffocates ({guestHealth} → {remoteVitals.Health}) but cannot die");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "G2 the ride up completed");
            Check(!remote.IsDead && remoteVitals.Health >= 1, $"G2 the guest reached the deck alive with {remoteVitals.Health} health");
            yield return Expect(() => remoteVitals.AirFraction == 1f, 3f, () => "G2 a new tank on deck");
            Check(vitals.AirFraction == 1f, "G2 the host's tank is new too");
            Check(remoteVitals.Health < settings.MaxHealth, "G2 health lost stays lost on deck");
            yield return Send(Action("leave"));
            yield return Wait(1f);
            Say("done");
        }
    }
}
