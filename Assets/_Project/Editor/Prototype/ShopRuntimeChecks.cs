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
    // The shop at HQ (docs/DESIGN.md §8; 18 September 2026). The host alone: the
    // room and its stands, the prompt, a refusal with no money, an air tank that
    // falls at the delivery spot, the two upgrades (one each, refused twice),
    // a press from too far, the large tank counting below, the marks on the
    // visor, and the upgrades lost with an unrescued body. Then a guest: buys
    // from the pot, its upgrade replicates, and a body brought up keeps them.
    // Log: Temp/shop-matrix.log. Started by CameraClearanceMatrixDriver.Start("shop").
    public static class ShopRuntimeChecks
    {
        private const string Log = "Temp/shop-matrix.log";
        private const string GuestDir = "Temp/shop-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1300;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run shop matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Shop matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Shop matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Shop matrix: MATRIX_PASS"); else Debug.LogError("Shop matrix: " + Status);
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
            Check(host.Upgrades != null, "the player prefab carries PlayerUpgrades (run the shop setup)");
            ShopCatalog catalog = ShopCatalog.Resolve();
            Check(catalog.Items.Count == 4 && catalog.Find(ShopCatalog.AirTankId) != null && catalog.Find(ShopCatalog.LargeTankId) != null && catalog.Find(ShopCatalog.BrightHeadlampId) != null && catalog.Find(ShopCatalog.PatchKitId) != null, "the catalogue lists the four items (the patch kit since the monsters)");
            ShopItem airTank = catalog.Find(ShopCatalog.AirTankId), largeTank = catalog.Find(ShopCatalog.LargeTankId), lamp = catalog.Find(ShopCatalog.BrightHeadlampId);
            Say($"prices: {airTank.Name} ${airTank.Price}, {largeTank.Name} ${largeTank.Price}, {lamp.Name} ${lamp.Price}; large tank ×{catalog.LargeTankMultiplier}; lamp range ×{catalog.BrightHeadlampRange}");
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ShopCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            PlayerUpgrades upgrades = host.Upgrades;
            float plainRange = host.HeadlampRange;

            Heading("S0 — the shop room at HQ: three stands with a name and a price, a delivery spot");
            Check(GameObject.Find(HQPrototypeBuilder.ShopRoomName) != null, "S0 the shop room stands at HQ");
            ShopDisplay tankStand = Stand(ShopCatalog.AirTankId), largeStand = Stand(ShopCatalog.LargeTankId), lampStand = Stand(ShopCatalog.BrightHeadlampId);
            Check(tankStand != null && largeStand != null && lampStand != null && Stand(ShopCatalog.PatchKitId) != null, "S0 a stand for each item");
            TextMesh tankLabel = tankStand.GetComponentInChildren<TextMesh>();
            Check(tankLabel != null && tankLabel.text == $"{airTank.Name}\n${airTank.Price}", "S0 the air tank's label reads its name and price: " + (tankLabel == null ? "none" : tankLabel.text.Replace("\n", " / ")));
            Check(tankStand.DeliveryPoint != null, "S0 the stand knows its delivery spot");

            Heading("S1 — look at a stand: the prompt; E with an empty pot is refused");
            yield return LookAtStand(tankStand);
            yield return null;
            Check(hud.PromptText.Contains($"{airTank.Name} · ${airTank.Price}") && hud.PromptText.Contains("Press E to buy") && hud.PromptText.Contains("pot $0"), "S1 the prompt: " + hud.PromptText);
            Check(Day.Balance == 0, "S1 the pot is empty");
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Refusal.Contains("Not enough money"), 3f, () => "S1 E with no money is refused: " + upgrades.Refusal);
            Check(hud.PromptText == upgrades.Refusal, "S1 the refusal shows in the prompt: " + hud.PromptText);
            Check(BoughtTank() == null && Day.Balance == 0, "S1 nothing was sold");

            Heading("S2 — with money: the air tank falls at the delivery spot, anyone's to take");
            Day.ServerSetBalanceForChecks(500);
            yield return Wait(0.2f);
            yield return LookAtStand(tankStand);
            yield return Press(Key.E);
            yield return Expect(() => BoughtTank() != null, 3f, () => "S2 an air tank was spawned");
            CarryableItem bought = BoughtTank();
            yield return Wait(1.2f);
            // The generated HQ chute discharges onto its clear deck-level pickup pad.
            yield return Expect(() => bought.transform.position.y < .6f, 4f, () => "S2 purchase reaches the pickup floor");
            Vector3 landed = bought.transform.position;
            Check(landed.x > 8f && landed.x < 12f && landed.z > 10f && landed.z < 14f && landed.y > -.1f,
                $"S2 purchase landed inside the marked pickup pad: {landed}");
            Check(bought.CanGrabFromWorld && bought.HolderClientId < 0 && bought.gameObject.scene == WorldScenes.Scene(WorldId.HQ), "S2 loose in the HQ scene, grabbable (" + bought.State + ")");
            Check(bought.DisplayName == AirTankItem.FullName && bought.UseAction == ItemUseAction.Breathe, "S2 it is a " + bought.DisplayName);
            // Breathing is for underwater (Dan, 18 September 2026): in hand at HQ the
            // prompt says so and a left click is refused.
            host.TeleportLocal(bought.transform.position + Vector3.right * 1.0f, host.Yaw); yield return Wait(0.3f);
            H.ClientLookAtNamed(bought.name); yield return null;
            yield return Expect(() => host.CurrentTarget == bought, 3f, () => "S2 the dot is on the bought tank");
            host.Inventory.RequestGrab(bought);
            yield return Expect(() => bought.HolderClientId == host.OwnerId, 3f, () => "S2 picked up");
            yield return null;
            Check(hud.PromptText.Contains("underwater"), "S2 in hand on land the prompt says: " + hud.PromptText);
            host.Inventory.RequestUse(host.PlayerCamera.transform.forward);
            yield return Wait(0.5f);
            Check(!bought.GetComponent<AirTankItem>().IsEmpty && host.Inventory.Refusal.Contains("underwater"), "S2 a breath on land is refused: " + host.Inventory.Refusal);
            host.Inventory.RequestDrop();
            yield return Expect(() => bought.HolderClientId < 0, 3f, () => "S2 dropped again");
            Check(Day.Balance == 500 - airTank.Price, $"S2 the pot paid ${airTank.Price}: ${Day.Balance} left");

            Heading("S3 — the large tank: an upgrade on the buyer, one only");
            yield return LookAtStand(largeStand);
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Has(PlayerUpgrade.LargeTank), 3f, () => "S3 the large tank is owned");
            Check(Day.Balance == 500 - airTank.Price - largeTank.Price, $"S3 the pot paid ${largeTank.Price}: ${Day.Balance} left");
            yield return null;
            Check(hud.PromptText.Contains("owned"), "S3 the stand now says owned: " + hud.PromptText);
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Refusal.Contains("already have"), 3f, () => "S3 buying it again is refused: " + upgrades.Refusal);
            Check(Day.Balance == 500 - airTank.Price - largeTank.Price, "S3 nothing charged for the refused buy");

            Heading("S4 — the bright headlamp: a longer beam; then the pot runs dry");
            yield return LookAtStand(lampStand);
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Has(PlayerUpgrade.BrightHeadlamp), 3f, () => "S4 the bright headlamp is owned");
            yield return null;
            Check(Mathf.Abs(host.HeadlampRange - plainRange * catalog.BrightHeadlampRange) < 0.05f, $"S4 the beam reaches ×{catalog.BrightHeadlampRange}: {plainRange:0.0} → {host.HeadlampRange:0.0} m");
            int left = 500 - airTank.Price - largeTank.Price - lamp.Price;
            Check(Day.Balance == left, $"S4 ${left} left in the pot");
            yield return LookAtStand(tankStand);
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Refusal.Contains("Not enough money") && upgrades.Refusal.Contains($"${left} in the pot"), 3f, () => "S4 a second air tank is refused on money: " + upgrades.Refusal);

            Heading("S5 — a request from across the room is refused");
            Day.ServerSetBalanceForChecks(100);
            host.TeleportLocal(new Vector3(0f, 0.05f, 0f), 0f); yield return Wait(0.3f);
            upgrades.RequestBuy(ShopCatalog.AirTankId);
            yield return Expect(() => upgrades.Refusal.Contains("Step up to the shelf"), 3f, () => "S5 refused from 12 m: " + upgrades.Refusal);
            Check(Day.Balance == 100 && BoughtTank() != null && CarryableItem.Spawned.Count(c => c != null && c.name.StartsWith("Air tank (bought)")) == 1, "S5 nothing sold");

            Heading("S6 — below, the large tank counts and the visor marks what was bought");
            yield return SailTo("Sea", WorldId.Sea);
            yield return Descend(1);
            PlayerVitals vitals = host.Vitals;
            float expected = vitals.Settings.EffectiveTankSeconds * catalog.LargeTankMultiplier;
            Check(Mathf.Abs(vitals.TankSeconds - expected) < 0.01f && vitals.ServerAirSeconds > expected - 30f && vitals.ServerAirSeconds <= expected, $"S6 the tank holds {vitals.TankSeconds:0} s (plain {vitals.Settings.EffectiveTankSeconds:0}); {vitals.ServerAirSeconds:0} s in it");
            Check(hud.Visor.AirFraction > 0.9f, $"S6 the visor's O2 reads the fraction of the large tank ({100f * hud.Visor.AirFraction:0}%)");
            Check(hud.Visor.UpgradeMarks == "L-TANK  LAMP", "S6 the visor marks: " + hud.Visor.UpgradeMarks);

            Heading("S7 — dying with the body left below costs the upgrades");
            host.RequestDebugDeath();
            yield return Expect(() => host.IsDead, 3f, () => "S7 dead below");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 60f, () => "S7 the site closed with the dead (the body stayed below)");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => "S7 the dead host is on the ship");
            Check(upgrades.Has(PlayerUpgrade.LargeTank) && upgrades.Has(PlayerUpgrade.BrightHeadlamp), "S7 still owned while dead (nothing decided yet)");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "S7 End day accepted: " + endWhy);
            yield return Expect(() => !host.IsDead && upgrades.Owned == PlayerUpgrade.None, 5f, () => "S7 revived with nothing: the body was not brought up (" + upgrades.Owned + ")");
            yield return null;
            Check(Mathf.Abs(host.HeadlampRange - plainRange) < 0.05f, $"S7 the plain beam again ({host.HeadlampRange:0.0} m)");
            Check(Day.Balance == 100, "S7 the pot is untouched by the death");

            Heading("G1 — a guest buys from the pot at HQ; its upgrade replicates");
            yield return SailTo("HQ", WorldId.HQ);
            largeStand = Stand(ShopCatalog.LargeTankId); // HQ is a fresh scene after the sail home
            Check(largeStand != null, "G1 the shop is there again at HQ");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "guest player spawned");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=HQ"), 20f, "G1 guest joined at HQ");
            Vector3 guestSpot = largeStand.transform.position + largeStand.transform.forward * 1.6f; guestSpot.y = 0.05f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"buy\",\"item\":\"" + ShopCatalog.LargeTankId + "\"}");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("shopRefusal='Not enough money"), 5f, "G1 $100 in the pot: the guest's large tank is refused, and the guest sees why");
            Day.ServerSetBalanceForChecks(400);
            yield return Wait(0.2f);
            yield return Send("{\"id\":{id},\"action\":\"buy\",\"item\":\"" + ShopCatalog.LargeTankId + "\"}");
            yield return Expect(() => remote.Upgrades != null && remote.Upgrades.Has(PlayerUpgrade.LargeTank), 5f, () => "G1 the guest bought the large tank; the host's copy shows it");
            Check(Day.Balance == 400 - largeTank.Price, $"G1 the pot paid ${largeTank.Price}: ${Day.Balance} left");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("upgrades=LargeTank"), 5f, "G1 the guest reads its own upgrade");
            Check(!upgrades.Has(PlayerUpgrade.LargeTank), "G1 the host has none of it (upgrades are per player)");

            Heading("G2 — the guest dies below; the host brings the body up; End day: the guest keeps its upgrade");
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(hqShip.SpawnPoint(1).position + Vector3.up * 0.1f) + "}");
            yield return Wait(0.5f);
            yield return SailTo("Sea", WorldId.Sea);
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3 cabinSpot = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(cabinSpot) + "}");
            yield return Wait(0.5f);
            yield return Descend(2); // day 2: S7's End day counted, the sails did not
            Check(Day.IsBelow(guestId) && Day.IsBelow(host.OwnerId), "G2 both below");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "G2 the guest is in the site");
            float guestExpected = vitals.Settings.EffectiveTankSeconds * catalog.LargeTankMultiplier;
            Check(remote.Vitals != null && Mathf.Abs(remote.Vitals.TankSeconds - guestExpected) < 0.01f, $"G2 the guest's tank holds {remote.Vitals.TankSeconds:0} s on the server");
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            Vector3 outSpot = car.transform.position + doorway * 4f + Vector3.up * 0.05f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(outSpot) + "}");
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"die\"}");
            yield return Expect(() => Day.IsDead(guestId) && remote.IsDead, 5f, () => "G2 the guest died below");
            PlayerBody guestBody = PlayerBody.FindFor(guestId, WorldScenes.Scene(WorldId.Dive));
            Check(guestBody != null, "G2 the guest's body lies below");
            yield return GrabBody(guestBody.GetComponent<CarryableItem>());
            yield return Surface();
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 30f, () => "G2 the site closed");
            yield return Expect(() => remote.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => "G2 the dead guest is on the ship");
            bool down = false;
            foreach (Vector3 place in new[] { sea.SpawnPoint(2).position, sea.SpawnPoint(0).position })
            {
                for (int yaw = 0; yaw < 360 && !down; yaw += 90)
                {
                    host.TeleportLocal(place, yaw); yield return Wait(0.3f);
                    host.Inventory.RequestDrop();
                    for (int frame = 0; frame < 90 && !guestBody.GetComponent<CarryableItem>().CanGrabFromWorld; frame++) yield return null;
                    down = guestBody.GetComponent<CarryableItem>().CanGrabFromWorld;
                }
                if (down) break;
            }
            Check(down, "G2 the body is set down on the deck");
            yield return Wait(1f);
            Check(flow.ServerEndDay(host.Owner, out string endWhy2), "G2 End day accepted: " + endWhy2);
            yield return Expect(() => !remote.IsDead && !Day.IsDead(guestId), 5f, () => "G2 the guest is revived next to its body");
            Check(remote.Upgrades.Has(PlayerUpgrade.LargeTank), "G2 the body came up: the guest keeps the large tank (" + remote.Upgrades.Owned + ")");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("dead=False") && GuestPlayerLine(r, guestId).Contains("upgrades=LargeTank"), 10f, "G2 the guest reads itself alive with the upgrade");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
        }
    }
}
