using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.Shop;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The plank (docs/DESIGN.md §8 "Failure"; 18 September 2026). The host alone:
    // a lost payday puts the run on the plank, the board and the prompt say so,
    // sailing and the shop are refused, the host is placed at the board, is
    // pushed after its turn, lands in the water, the card comes, then the fresh
    // run — day 0, $0, no upgrades, empty hands, on the pier, the screen clear.
    // Then a guest: the host jumps by itself, the guest is next, pushed, and both
    // start over. Log: Temp/plank-matrix.log. Started by
    // CameraClearanceMatrixDriver.Start("plank").
    public static class PlankRuntimeChecks
    {
        private const string Log = "Temp/plank-matrix.log";
        private const string GuestDir = "Temp/plank-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1400;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run plank matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Plank matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Plank matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Plank matrix: MATRIX_PASS"); else Debug.LogError("Plank matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
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
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);

        private static ShopDisplay Stand(string itemId)
        {
            foreach (ShopDisplay d in UnityEngine.Object.FindObjectsByType<ShopDisplay>(FindObjectsInactive.Exclude))
                if (d.ItemId == itemId && d.gameObject.scene == WorldScenes.Scene(WorldId.HQ)) return d;
            return null;
        }
        // Stand in front of a shelf and look at the thing on it until the dot is on it.
        private static IEnumerator LookAtStand(ShopDisplay stand)
        {
            HQPlayerController host = Host();
            Transform thing = stand.transform.Find("Display") ?? stand.transform;
            Vector3 at = stand.transform.position + stand.transform.forward * 1.6f; at.y = 0.05f;
            host.TeleportLocal(at, host.Yaw); yield return null; yield return null;
            float deadline = Time.unscaledTime + 3f;
            while (Time.unscaledTime < deadline && host.CurrentShopDisplay != stand)
            {
                Vector3 to = thing.position - host.EyePosition;
                Vector3 flat = new(to.x, 0f, to.z);
                host.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
                host.SetPitchForChecks(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
                yield return null;
            }
            Check(host.CurrentShopDisplay == stand, "the dot is on the " + stand.ItemId + " stand");
        }
        private static CarryableItem BoughtTank()
        {
            foreach (CarryableItem c in CarryableItem.Spawned)
                if (c != null && c.name.StartsWith("Air tank (bought)") && c.gameObject.scene == WorldScenes.Scene(WorldId.HQ)) return c;
            return null;
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
        private static IEnumerator SailTo(string world, WorldId id)
        {
            H.MoveLocalIntoDeckCabin(world == "Sea" ? "HQ" : "Sea"); yield return Wait(0.4f); // aboard first: the ship sails with everyone on it
            Check(H.ServerSail(world).StartsWith("sailing"), "sailing to " + world);
            yield return Expect(() => Day.World == id && !WorldSceneFlow.Instance.Transitioning && !Host().TravelLocked, 60f, () => "arrived at " + world);
            yield return Wait(0.5f);
        }
        private static IEnumerator GrabBody(CarryableItem item)
        {
            HQPlayerController host = Host();
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 fromCar = item.transform.position - car.transform.position; fromCar.y = 0f;
            Vector3 toward = fromCar.normalized, side = Vector3.Cross(Vector3.up, toward);
            foreach (Vector3 offset in new[] { -toward * 1.3f, toward * 1.3f, side * 1.3f, -side * 1.3f })
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

        // ---- the run ------------------------------------------------------------------

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
            keyboard = InputSystem.AddDevice<Keyboard>("PlankCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            float turn = flow.Settings.PlankTurnSeconds, card = flow.Settings.RunOverCardSeconds;
            Say($"turn {turn} s, card {card} s");

            Heading("P0 — the plank stands off the pier; a lost payday puts the run on it");
            HQPlank plank = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
            Check(plank != null && plank.Base != null && plank.End != null, "P0 HQ has the plank with its base and end");
            Check(plank.Gate != null && !plank.GateUp, "P0 the gate at the board's base is down while the run is on");
            // U1 — Unstuck at HQ (Dan, 18 September 2026): from anywhere back to a pier spawn point; not twice in a row.
            host.TeleportLocal(new Vector3(40f, 0.05f, 40f), 0f); yield return Wait(0.3f);
            Check(flow.ServerUnstuck(host.Owner, out string unstuckWhy), "U1 unstuck at HQ is taken: " + unstuckWhy);
            List<Transform> pierPoints = CrewSpawner.SpawnPointsIn(WorldScenes.Scene(WorldId.HQ));
            yield return Expect(() => pierPoints.Exists(p => Vector3.Distance(p.position, host.transform.position) < 1f), 3f, () => "U1 back on a pier spawn point: " + host.transform.position.ToString("F1"));
            Check(!flow.ServerUnstuck(host.Owner, out unstuckWhy) && unstuckWhy == "Just did", "U1 a second press right after is refused: " + unstuckWhy);
            // Something to lose: an upgrade, a ball in hand, and a bought tank on the shop's floor.
            host.Upgrades.ServerGrant(PlayerUpgrade.LargeTank);
            ShopDisplay tankStand = null;
            foreach (ShopDisplay d in UnityEngine.Object.FindObjectsByType<ShopDisplay>(FindObjectsInactive.Exclude))
                if (d.ItemId == ShopCatalog.AirTankId && d.gameObject.scene == WorldScenes.Scene(WorldId.HQ)) tankStand = d;
            Check(tankStand != null, "P0 the shop has the air tank's stand");
            Day.ServerSetBalanceForChecks(40);
            Vector3 atStand = tankStand.transform.position + tankStand.transform.forward * 1.6f; atStand.y = 0.05f;
            host.TeleportLocal(atStand, host.Yaw); yield return Wait(0.3f);
            Check(flow.ServerBuy(host.Owner, ShopCatalog.AirTankId, out string tankWhy), "P0 an air tank is bought for $40: " + tankWhy);
            yield return Expect(() => UnityEngine.Object.FindObjectsByType<AirTankItem>(FindObjectsInactive.Exclude).Length == 1, 3f, () => "P0 the tank fell into the shop");
            yield return Wait(1f);
            CarryableItem ball = H.Item("Basketball");
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return Wait(0.4f);
            Check(ball.State == ItemState.Held && ball.HolderClientId == host.OwnerId && host.Upgrades.Has(PlayerUpgrade.LargeTank), "P0 the host holds a ball and owns the large tank");
            Day.ServerForceCycleForChecks(3, true); // payday, nothing in the box, $0
            H.ClientMoveLocalPlayerToBoard(); yield return Wait(0.3f);
            int paySerial = Day.LastPay.Serial;
            Check(flow.ServerPay(host.Owner, out string payWhy), "P0 the pay press is taken: " + payWhy);
            yield return Expect(() => Day.LastPay.Serial > paySerial && Day.LastPay.Lost, 3f, () => "P0 short at payday: the run is lost");
            Check(Day.Phase == DayPhase.Plank && Day.Plank.Active && Day.Plank.Jumper == host.OwnerId, $"P0 the plank phase; the host is first on the board (phase {Day.Phase}, jumper {Day.Plank.Jumper})");
            yield return null;
            Check(H.QuotaBoardText().StartsWith("THE RUN IS OVER"), "P0 the board: " + H.QuotaBoardText().Replace("\n", " | "));
            Check(!Day.ServerCanSail(WorldId.Sea, out string sailWhy) && sailWhy == "The run is over", "P0 sailing is refused: " + sailWhy);
            Check(!flow.ServerBuy(host.Owner, ShopCatalog.AirTankId, out string buyWhy) && buyWhy == "The run is over", "P0 the shop is shut: " + buyWhy);

            Heading("P1 — the host stands on the board, free to move, told to jump; pushed when its time runs out");
            yield return Expect(() => Vector3.Distance(host.transform.position, plank.Base.position) < 1f, 3f, () => $"P1 placed at the base of the board ({Vector3.Distance(host.transform.position, plank.Base.position):0.0} m)");
            yield return null;
            Check(hud.PromptText.StartsWith("WALK THE PLANK"), "P1 the prompt: " + hud.PromptText);
            Check(host.Controller.enabled && !host.TravelLocked, "P1 free to walk");
            // The gate (Dan, 18 September 2026: "will not be able to leave the plank"):
            // up behind the jumper, a collider across the base, the jumper clear of it.
            Check(plank.GateUp && plank.Gate.GetComponent<Collider>().enabled, "P1 the gate is up while the crew walks the plank");
            Check(Vector3.Dot(plank.Gate.transform.position - host.transform.position, plank.Base.forward) < 0f, "P1 the gate stands behind the jumper, between the board and the pier");
            Check(!plank.Gate.GetComponent<Collider>().bounds.Contains(host.transform.position + Vector3.up * 0.9f), "P1 the jumper is not inside the gate");
            Check(!flow.ServerUnstuck(host.Owner, out string jumperWhy) && jumperWhy == "Walk the plank", "U2 unstuck is refused to the jumper: " + jumperWhy);
            Vector3 onBoard = plank.Base.position + plank.Base.forward * 1.5f;
            host.TeleportLocal(onBoard, plank.WalkYaw); yield return Wait(0.5f);
            Check(!plank.IsInWater(host.transform.position) && Day.Plank.Jumper == host.OwnerId && !Day.HasJumped(host.OwnerId), "P1 out on the board is allowed: still the jumper, not in the water");
            float pushedBy = turn + 4f;
            yield return Expect(() => plank.IsInWater(host.transform.position), pushedBy, () => $"P1 pushed after {turn:0} s: in the water (y={host.transform.position.y:0.0})");
            yield return Expect(() => Day.HasJumped(host.OwnerId), 3f, () => "P1 counted as jumped");

            Heading("P2 — the last one in: the card, then everything from nothing");
            yield return Expect(() => Day.RunOver.Serial >= 1, 3f, () => "P2 the run-over card was sent");
            RunOverReport report = Day.RunOver;
            Check(report.Days == 0 && report.Minutes >= 0, $"P2 the card counts the dive days and the minutes of the run ({report.Days} days, {report.Minutes} minutes; no dive this run)");
            yield return Expect(() => ScreenFade.Instance != null && ScreenFade.Instance.IsBlack, 3f, () => "P2 the screen is black with the card");
            yield return Expect(() => Day.Phase == DayPhase.AtHQ, card + 4f, () => "P2 the fresh run after the card (phase " + Day.Phase + ")");
            Check(Day.Day == 0 && Day.Balance == 0 && Day.CycleSales == 0 && !Day.Payday && Day.RunDays == 0 && !Day.Plank.Active, "P2 day 0, $0, no cycle, the plank off");
            Check(host.Upgrades.Owned == PlayerUpgrade.None, "P2 the upgrades are gone");
            yield return Expect(() => !plank.GateUp, 1f, () => "P2 the gate is down again for the fresh run"); // a frame behind the phase (HQPlank.Update)
            // The world as it was found (Dan, 18 September 2026: "oxygen tanks are still
            // on the ship after game over"): the bought tank is gone, the fixture's ball is back.
            yield return Wait(0.5f);
            Check(UnityEngine.Object.FindObjectsByType<AirTankItem>(FindObjectsInactive.Exclude).Length == 0, "P2 no air tank left anywhere");
            Check(ball == null || !ball.IsSpawned, "P2 the old ball is gone");
            CarryableItem freshBall = H.Item("Basketball");
            Check(freshBall != null && freshBall.IsSpawned && freshBall.HolderClientId < 0 && freshBall.gameObject.scene == WorldScenes.Scene(WorldId.HQ), "P2 the fixture's ball is back on the pier, loose");
            yield return Expect(() => host.Inventory.HeldItem == null && ball.HolderClientId < 0, 3f, () => "P2 empty hands: the ball was dropped");
            yield return Expect(() => !plank.IsInWater(host.transform.position) && host.transform.position.y > -0.5f, 5f, () => $"P2 back on land at a spawn point (y={host.transform.position.y:0.0})");
            yield return Expect(() => ScreenFade.Instance.IsClear, 4f, () => "P2 the screen is clear again");
            Check(!host.IsDead && host.Vitals.AirFraction == 1f, "P2 alive with a full tank");
            yield return null;
            Check(!hud.PromptText.StartsWith("WALK") && !hud.PromptText.StartsWith("THE RUN"), "P2 no plank prompt any more: " + hud.PromptText);

            Heading("G1 — with a guest: the host jumps by itself, the guest is next and is pushed; both start over");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "guest player spawned");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=HQ"), 20f, "G1 guest joined at HQ");
            Day.ServerForceCycleForChecks(3, true);
            H.ClientMoveLocalPlayerToBoard(); yield return Wait(0.3f);
            paySerial = Day.LastPay.Serial;
            Check(flow.ServerPay(host.Owner, out string payWhy2), "G1 the pay press is taken: " + payWhy2);
            yield return Expect(() => Day.LastPay.Serial > paySerial && Day.LastPay.Lost && Day.Phase == DayPhase.Plank, 3f, () => "G1 lost again: the plank");
            Check(Day.Plank.Jumper == host.OwnerId, "G1 the host (id 0) goes first");
            yield return GuestEventually(r => r.Contains("phase=Plank"), 5f, "G1 the guest reads the plank phase");
            yield return Expect(() => Vector3.Distance(host.transform.position, plank.Base.position) < 1f, 3f, () => "G1 the host is at the base");
            // The host jumps: off the end, into the water.
            host.TeleportLocal(plank.End.position + plank.End.forward * 0.8f + Vector3.up * 0.1f, plank.WalkYaw);
            yield return Expect(() => Day.HasJumped(host.OwnerId), 5f, () => "G1 the host jumped");
            yield return Expect(() => Day.Plank.Active && Day.Plank.Jumper == guestId, 3f, () => "G1 the guest is next (jumper " + Day.Plank.Jumper + ")");
            yield return Expect(() => Vector3.Distance(remote.transform.position, plank.Base.position) < 1.5f, 5f, () => $"G1 the guest was placed at the base ({Vector3.Distance(remote.transform.position, plank.Base.position):0.0} m)");
            yield return Expect(() => plank.IsInWater(remote.transform.position), turn + 6f, () => $"G1 the guest, not jumping, was pushed into the water (y={remote.transform.position.y:0.0})");
            yield return Expect(() => Day.Phase == DayPhase.AtHQ, card + 6f, () => "G1 the fresh run (phase " + Day.Phase + ")");
            yield return GuestEventually(r => r.Contains("phase=AtHQ") && r.Contains("day=0") && GuestPlayerLine(r, guestId).Contains("dead=False") && GuestPlayerLine(r, guestId).Contains("upgrades=None"), 10f, "G1 the guest reads the fresh run, alive, no upgrades");
            yield return Expect(() => !plank.IsInWater(remote.transform.position) && !plank.IsInWater(host.transform.position), 6f, () => "G1 both back on land");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
        }
    }
}
