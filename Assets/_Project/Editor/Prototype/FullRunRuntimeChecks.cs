using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The full-run playthrough of docs/FULL_RUN_PLAYTHROUGH.md, played as the
    // host: colour, sail, three dives with coins, the box, End day, an early
    // trip home and a short pay, payday, the pay button, a second cycle. The
    // walking is hooks (teleports); every press is the real client request;
    // every expectation is the text a player would read. Log: Temp/full-run.log;
    // screenshots: Logs/full-run/. Started by CameraClearanceMatrixDriver.Start("fullrun").
    public static class FullRunRuntimeChecks
    {
        private const string Log = "Temp/full-run.log";
        private const string Shots = "Logs/full-run/";
        private const int Quota = 500;

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static int shot;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run full-run playthrough (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Full run playing; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Full run started " + DateTime.Now + "\n");
            Directory.CreateDirectory(Shots);
            Status = "Running";
            shot = 0;
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
            if (Status == "MATRIX_PASS") Debug.Log("Full run: MATRIX_PASS"); else Debug.LogError("Full run: " + Status);
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        // ---- the log ----------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");

        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + H.InventoryText());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }

        private static IEnumerator Expect(Func<bool> condition, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
            Check(condition(), label);
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        // The hooks open the session menu (no stray input); a screenshot wants the
        // HUD, so close it for the frames and reopen it after.
        private static IEnumerator Shot(string name)
        {
            string path = Shots + (++shot).ToString("00") + "-" + name + ".png";
            SunkCost.Net.SessionInputGate.Resume();
            yield return null; yield return null;
            H.CaptureScreen(path);
            yield return null; yield return null;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Say("screenshot " + path);
        }

        private static void ShotFrom(string name, Vector3 position, Vector3 lookAt)
        {
            string path = Shots + (++shot).ToString("00") + "-" + name + ".png";
            H.CaptureFrom(position, lookAt, path);
            Say("screenshot " + path);
        }

        private static string Board() => H.QuotaBoardText().Replace("\n", " | ");
        private static string Readout(ShipParts ship) { StorageReadout r = ship != null ? ship.GetComponent<StorageReadout>() : null; return r == null ? "(no readout)" : r.Text.Replace("\n", " | "); }

        // ---- moves ------------------------------------------------------------------

        private static IEnumerator SailTo(string world, WorldId expected)
        {
            ShipParts ship = ShipParts.InWorld(Day.World);
            H.ClientMoveLocalPlayerTo(ship.SpawnPoint(0).position); yield return Wait(0.3f);
            H.ClientRequestSail(world);
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == expected, 45f, "the ship arrived: " + expected);
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, "controls back after the sail");
        }

        private static IEnumerator Descend(int day)
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, "the deck cabin took the press");
            yield return Expect(() => !Day.CabinRide.Active, 70f, "the ride down completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete, "the ride down was not cancelled: " + H.RideStatus());
            Check(Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), "standing in the dive site");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, "the car is at the bottom");
            yield return Wait(1.5f);
            Check(Day.Phase == DayPhase.DiveInProgress && Day.Day == day, $"dive in progress on day {day} (phase={Day.Phase} day={Day.Day})");
            PlayerHudUI hud = Host().GetComponent<PlayerHudUI>();
            Check(hud.Visor.On && hud.Visor.DayText == $"DAY {day}/3", "visor on, corner says DAY " + day + "/3: " + hud.Visor.DayText);
            Say("visor money line: " + hud.Visor.MoneyText);
        }

        private static IEnumerator Surface()
        {
            H.MoveLocalIntoCar(); yield return Wait(0.4f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, "the car took the press");
            yield return Expect(() => !Day.CabinRide.Active, 70f, "the ride up completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete, "the ride up was not cancelled: " + H.RideStatus());
            Check(Host().gameObject.scene == WorldScenes.Scene(WorldId.Sea), "back on the deck");
            yield return Expect(() => Day.DiveDone && Day.Phase == DayPhase.AtSea, 5f, "nobody below: the dive is done");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 15f, "the site unloaded once empty");
        }

        // Stand 0.9 m from the coin on the seafloor, look at it until the dot lands, grab.
        private static IEnumerator Grab(string coinName)
        {
            // By name in the dive scene only: the ship's room may hold a coin of the
            // same name from an earlier dive (they are re-spawned with the site).
            CarryableItem coin = null;
            foreach (CarryableItem candidate in CarryableItem.Spawned)
                if (candidate != null && (candidate.name == coinName || candidate.name.StartsWith(coinName + " (day ")) && candidate.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) { coin = candidate; break; }
            Check(coin != null && coin.CanGrabFromWorld, coinName + " lies on the seafloor");
            HQPlayerController host = Host();
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 fromCar = coin.transform.position - car.transform.position; fromCar.y = 0f;
            Vector3 toward = fromCar.normalized, side = Vector3.Cross(Vector3.up, toward);
            // Four places to stand, in turn: toward the car, away, either side.
            Vector3[] stands = { -toward * 0.9f, toward * 0.9f, side * 0.9f, -side * 0.9f };
            foreach (Vector3 offset in stands)
            {
                Vector3 stand = coin.transform.position + offset; stand.y = coin.transform.position.y + 0.1f;
                host.TeleportLocal(stand, host.Yaw); yield return null; yield return null;
                float deadline = Time.unscaledTime + 3f; int tries = 0;
                while (Time.unscaledTime < deadline && host.CurrentTarget != coin)
                {
                    if (tries++ % 30 == 0) host.TeleportLocal(stand, host.Yaw);
                    H.ClientLookAtItem(coin.name);
                    yield return null;
                }
                if (host.CurrentTarget == coin) break;
                Vector3 eye = host.EyePosition;
                Collider col = coin.PrimaryCollider;
                Vector3 closest = col != null && col.enabled ? col.ClosestPoint(eye) : coin.transform.position;
                Say($"could not aim at {coinName} from {offset:F1}: target={(host.CurrentTarget == null ? "none" : host.CurrentTarget.name)} los={InteractionTargeting.HasLineOfSight(eye, closest, host.transform, coin)} coinAt={coin.transform.position:F2} eyeAt={eye:F2}");
            }
            Check(host.CurrentTarget == coin, "the dot is on " + coinName + " ($" + coin.Value + ")");
            int before = host.GetComponent<PlayerHudUI>().Visor.OnMeValue;
            host.Inventory.RequestGrab(coin);
            // The first coin lands in the hands; with hands full the next ones go straight into a slot.
            yield return Expect(() => (coin.State == ItemState.Held || coin.State == ItemState.Stowed) && coin.HolderClientId == host.OwnerId, 3f, coinName + " grabbed (" + coin.State + ")");
            yield return null; yield return null;
            int after = host.GetComponent<PlayerHudUI>().Visor.OnMeValue;
            Check(after == before + coin.Value, $"ON ME went from ${before} to ${after} (+${coin.Value})");
        }

        // Drop everything carried where the player stands, one slot at a time.
        private static IEnumerator DropAll(List<CarryableItem> dropped)
        {
            PlayerInventory inventory = Host().Inventory;
            for (int slot = 0; slot < InventorySlots.Count; slot++)
            {
                CarryableItem item = inventory.ItemInSlot(slot);
                if (item == null) continue;
                if (inventory.HeldItem != item)
                {
                    inventory.RequestEquip(slot);
                    yield return Expect(() => inventory.HeldItem == item, 3f, "equipped " + item.name);
                }
                inventory.RequestDrop();
                yield return Expect(() => item.CanGrabFromWorld && inventory.HeldItem != item, 3f, "dropped " + item.name);
                dropped.Add(item);
                yield return Wait(0.4f);
            }
            if (inventory.HeldItem != null)
            {
                CarryableItem held = inventory.HeldItem;
                inventory.RequestDrop();
                yield return Expect(() => held.CanGrabFromWorld, 3f, "dropped the held " + held.name);
                dropped.Add(held);
            }
            yield return Wait(1f); // let them settle
        }

        // Coins are cylinders: a dropped one can roll out through the doorway. Let
        // them settle, put any runaway back inside (noted), settle again.
        private static IEnumerator SettleInRoom(ShipParts sea, List<CarryableItem> coins)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                yield return Wait(2.5f);
                foreach (CarryableItem c in coins)
                {
                    if (c == null || sea.IsInStorageRoom(c.transform.position)) continue;
                    Say("NOTE: " + c.name + " rolled out of the room to ship-local " + sea.ToShipLocal(c.transform.position).ToString("F2") + "; placed back inside");
                    c.ServerDropAt(sea.FromShipLocal(new Vector3(3.6f, 0.3f, -12.5f)));
                }
            }
            foreach (CarryableItem c in coins) Say(c.name + " rests at ship-local " + sea.ToShipLocal(c.transform.position).ToString("F2") + (sea.IsInStorageRoom(c.transform.position) ? " (in the room)" : " (OUTSIDE)"));
        }

        private static int Sum(List<CarryableItem> items) { int s = 0; foreach (CarryableItem i in items) if (i != null && i.IsSpawned) s += i.Value; return s; }

        private static IEnumerator EndDay(string expectMonitorStart)
        {
            H.ClientRequestEndDay();
            yield return Expect(() => H.MonitorText().StartsWith(expectMonitorStart), 4f, "after End day the monitor says '" + expectMonitorStart + "…': " + H.MonitorText());
        }

        private static IEnumerator ExpectRefusal(string text, string label)
        {
            float deadline = Time.unscaledTime + 4f;
            while (Time.unscaledTime < deadline && Day.LastRefusal.Text != text) yield return null;
            Check(Day.LastRefusal.Text == text, label + ": '" + Day.LastRefusal.Text + "'");
        }

        private static IEnumerator Pay(int serialBefore)
        {
            HQPlayerController host = Host();
            H.ClientMoveLocalPlayerTo(new Vector3(-2.5f, 0f, -4.4f)); yield return null;
            H.ClientLookAtNamed(QuotaBoard.BoardName); yield return null; yield return null;
            Check(host.CurrentQuotaBoard != null, "looking at the board: " + H.PromptText());
            Say("prompt: " + H.PromptText());
            ShotFrom("board-before-pay", new Vector3(-2.5f, 1.6f, -3.6f), new Vector3(-2.5f, 1.6f, -5.85f));
            H.ClientRequestPay();
            yield return Expect(() => Day.LastPay.Serial > serialBefore, 4f, "the pay was processed");
            yield return null;
            Say("pay report: sold $" + Day.LastPay.Sales + " quota $" + Day.LastPay.Quota + " had $" + Day.LastPay.Had + " → " + (Day.LastPay.Paid ? "PAID" : Day.LastPay.Short ? "SHORT" : "LOST") + ", balance $" + Day.LastPay.Balance);
            Say("board: " + Board());
            ShotFrom("board-after-pay", new Vector3(-2.5f, 1.6f, -3.6f), new Vector3(-2.5f, 1.6f, -5.85f));
        }

        // ---- the run ----------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "hosting with a spawned player");
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            Check(WorldLoopSettings.Resolve(null).QuotaPerCycle == Quota, "the quota is $" + Quota);

            Heading("Setup — at HQ");
            Check(Day.Phase == DayPhase.AtHQ && Day.Day == 0 && Day.Balance == 0, "docked at HQ, day 0, balance $0");
            yield return Expect(() => H.QuotaBoardText().StartsWith("NEW CYCLE"), 3f, "board: " + Board());
            Check(H.MonitorText().StartsWith("Docked at HQ"), "monitor: " + H.MonitorText());
            ShotFrom("hq-board", new Vector3(-2.5f, 1.6f, -3.6f), new Vector3(-2.5f, 1.6f, -5.85f));

            Heading("Colour — sky");
            PlayerIdentity identity = host.GetComponent<PlayerIdentity>();
            GameObject panel = GameObject.Find(ColourPanel.PanelName);
            H.ClientMoveLocalPlayerTo(new Vector3(panel.transform.position.x, 0f, panel.transform.position.z + 1.3f)); yield return null;
            H.ClientLookAtNamed(ColourPanel.PanelName); yield return null; yield return null;
            Check(host.CurrentColourPanel != null && H.PromptText().Contains("Press E to pick your colour"), "the panel offers the pick: " + H.PromptText());
            SunkCost.Net.SessionInputGate.OpenPicker(); yield return null;
            hud.PickColourForChecks(8);
            yield return Expect(() => identity.ColourIndex == 8, 3f, "sky (8) written by the server");
            SunkCost.Net.SessionInputGate.ClosePicker(); yield return null; yield return null;
            Check(Mathf.Abs(host.BodyColour.r - PlayerPalette.Get(8).r) < 0.02f && Mathf.Abs(host.BodyColour.b - PlayerPalette.Get(8).b) < 0.02f, "the body wears sky: " + host.BodyColour);
            Check(PlayerColourPrefs.Load() == 8, "the pick is saved");
            yield return Shot("colour-picked");

            Heading("Cycle 1 — sail out");
            yield return SailTo("Sea", WorldId.Sea);
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            Check(Day.Day == 1 && !Day.Payday && Day.Phase == DayPhase.AtSea, $"day 1 at sea (day={Day.Day})");
            yield return Expect(() => H.MonitorText().StartsWith("Day 1 of 3 — Site 01"), 3f, "monitor: " + H.MonitorText());
            yield return Expect(() => sea.DeckCabinPanel.text.StartsWith("Day 1 of 3 — all in"), 3f, "cabin panel: " + sea.DeckCabinPanel.text);
            yield return Expect(() => Readout(sea) == "STORAGE | $0 / $500 | balance $0", 3f, "box readout: " + Readout(sea));
            H.ClientRequestEndDay();
            yield return ExpectRefusal("Nobody has dived today", "End day before a dive is refused");

            Heading("Cycle 1 — dive 1, everything into the box");
            yield return Descend(1);
            Check(hud.Visor.MoneyText == "BOX $0/$500  ·  ON ME $0", "visor money line: " + hud.Visor.MoneyText);
            yield return Shot("dive1-seafloor");
            foreach (string name in new[] { "Coin 7", "Coin 3", "Coin 5", "Coin 6" }) yield return Grab(name);
            int haul1 = hud.Visor.OnMeValue;
            Say($"dive 1 haul on me: ${haul1}");
            yield return Shot("dive1-loaded");
            yield return Surface();
            yield return Expect(() => H.MonitorText().StartsWith("Day 1 of 3 — dive done"), 3f, "monitor: " + H.MonitorText());
            yield return Expect(() => sea.DeckCabinPanel.text == "Dive done — end the day at the monitor", 3f, "cabin panel: " + sea.DeckCabinPanel.text);
            H.ClientRequestCabin(); // still standing in the deck cabin
            yield return ExpectRefusal("Dive done — end the day at the monitor", "a second dive today is refused");
            Check(!Day.Riding, "no ride started");
            H.ClientMoveLocalPlayerTo(sea.FromShipLocal(new Vector3(3.2f, 0f, -12.5f))); yield return null;
            H.ClientLookAtNamed("StorageWallStarboard"); yield return null;
            var inBox1 = new List<CarryableItem>();
            yield return DropAll(inBox1);
            yield return SettleInRoom(sea, inBox1);
            int box1 = Sum(inBox1);
            Check(box1 == haul1, $"the four coins in the room are worth the haul (${box1})");
            yield return Expect(() => Day.BoxValue == box1, 3f, $"box value ${Day.BoxValue} = ${box1}");
            yield return Expect(() => Readout(sea) == $"STORAGE | ${box1} / $500 | balance $0", 3f, "box readout: " + Readout(sea));
            ShotFrom("box-after-dive1", sea.FromShipLocal(new Vector3(0.2f, 1.7f, -12.5f)), sea.FromShipLocal(new Vector3(1.8f, 1.5f, -12.5f)));
            yield return EndDay("Day 2 of 3");
            Check(Day.Day == 2 && !Day.DiveDone, "day 2, no dive yet");

            Heading("Cycle 1 — early trip home, SHORT");
            yield return SailTo("HQ", WorldId.HQ);
            Check(Day.Day == 2 && !Day.Payday && Day.Phase == DayPhase.AtHQ, $"docked, still day 2 (day={Day.Day} payday={Day.Payday})");
            yield return Expect(() => H.MonitorText().StartsWith("Docked at HQ — day 2 of 3"), 3f, "monitor: " + H.MonitorText());
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            int crossed = 0; foreach (CarryableItem c in inBox1) if (c != null && c.IsSpawned && c.gameObject.scene == WorldScenes.Scene(WorldId.HQ) && hqShip.IsInStorageRoom(c.transform.position)) crossed++;
            Check(crossed == inBox1.Count, $"all {inBox1.Count} coins crossed to HQ inside the room ({crossed})");
            yield return Expect(() => Day.BoxValue == box1, 3f, "the docked ship's box is worth the same");
            yield return Expect(() => H.QuotaBoardText().StartsWith("DAY 2 OF 3"), 3f, "board: " + Board());
            int paySerial = Day.LastPay.Serial;
            yield return Pay(paySerial);
            bool paidEarly = Day.LastPay.Paid;
            if (paidEarly)
            {
                Say("the first haul covered the quota by itself: PAID early, cycle over");
                Check(Day.Day == 0 && Day.Balance == box1 - Quota, $"paid: day 0, balance ${Day.Balance}");
            }
            else
            {
                Check(Day.LastPay.Short && !Day.LastPay.Lost, "short before payday is SHORT, not a loss");
                Check(Day.LastPay.Sales == box1 && Day.Balance == box1 && Day.Day == 2 && !Day.Payday, $"the box was banked (balance ${Day.Balance}), day still 2");
                Check(H.QuotaBoardText().StartsWith($"SHORT BY ${Quota - box1}"), "board: " + Board());
            }
            foreach (CarryableItem c in inBox1) Check(c == null || !c.IsSpawned, "sold: " + (c == null ? "(gone)" : c.name));
            yield return Expect(() => Day.BoxValue == 0, 3f, "box empty after the sale");
            bool canSailOut = Day.ServerCanSail(WorldId.Sea, out string outWhy);
            Check(!Day.Payday && canSailOut, "the ship may sail out again: " + outWhy);

            Heading("Cycle 1 — dive 2, one coin left on the deck");
            yield return SailTo("Sea", WorldId.Sea);
            sea = ShipParts.InWorld(WorldId.Sea);
            int dayNow = paidEarly ? 1 : 2;
            Check(Day.Day == dayNow, $"at sea on day {dayNow} (day={Day.Day})");
            yield return Descend(dayNow);
            foreach (string name in new[] { "Coin 7", "Coin 3", "Coin 5", "Coin 6" }) yield return Grab(name);
            int haul2 = hud.Visor.OnMeValue;
            Say($"dive 2 haul on me: ${haul2}");
            yield return Surface();
            // one coin on the open deck first (the equipped one), the rest in the room
            H.ClientMoveLocalPlayerTo(sea.SpawnPoint(1).position); yield return null;
            PlayerInventory inventory = host.Inventory;
            CarryableItem deckCoin = inventory.HeldItem ?? inventory.ItemInSlot(0);
            if (inventory.HeldItem != deckCoin) { inventory.RequestEquip(0); yield return Expect(() => inventory.HeldItem == deckCoin, 3f, "equipped the deck coin"); }
            inventory.RequestDrop();
            yield return Expect(() => deckCoin.CanGrabFromWorld, 3f, "dropped " + deckCoin.name + " on the open deck");
            yield return Wait(1f);
            Check(sea.IsAboard(deckCoin.transform.position) && !sea.IsInStorageRoom(deckCoin.transform.position), deckCoin.name + " lies on the deck, outside the room");
            H.ClientMoveLocalPlayerTo(sea.FromShipLocal(new Vector3(3.2f, 0f, -12.5f))); yield return null;
            H.ClientLookAtNamed("StorageWallStarboard"); yield return null;
            var inBox2 = new List<CarryableItem>();
            yield return DropAll(inBox2);
            yield return SettleInRoom(sea, inBox2);
            int box2 = Sum(inBox2);
            Check(box2 == haul2 - deckCoin.Value, $"three coins in the room (${box2}), the deck coin (${deckCoin.Value}) not counted");
            yield return Expect(() => Day.BoxValue == box2, 3f, $"box value ${Day.BoxValue}");
            yield return EndDay(paidEarly ? "Day 2 of 3" : "Day 3 of 3");

            Heading("Cycle 1 — dive 3, payday");
            int lastDay = paidEarly ? 2 : 3;
            yield return Descend(lastDay);
            foreach (string name in new[] { "Coin 7", "Coin 3", "Coin 5", "Coin 6" }) yield return Grab(name);
            int haul3 = hud.Visor.OnMeValue;
            Say($"dive 3 haul on me: ${haul3}");
            yield return Surface();
            H.ClientMoveLocalPlayerTo(sea.FromShipLocal(new Vector3(3.2f, 0f, -12.5f))); yield return null;
            H.ClientLookAtNamed("StorageWallStarboard"); yield return null;
            var inBox3 = new List<CarryableItem>();
            yield return DropAll(inBox3);
            yield return SettleInRoom(sea, inBox3);
            int boxTotal = box2 + Sum(inBox3);
            yield return Expect(() => Day.BoxValue == boxTotal, 3f, $"box value ${Day.BoxValue} = ${boxTotal}");
            yield return Expect(() => Readout(sea) == $"STORAGE | ${boxTotal} / $500 | balance ${Day.Balance}", 3f, "box readout: " + Readout(sea));
            ShotFrom("box-full", sea.FromShipLocal(new Vector3(0.2f, 1.7f, -12.5f)), sea.FromShipLocal(new Vector3(1.8f, 1.5f, -12.5f)));
            if (paidEarly)
            {
                yield return EndDay("Day 3 of 3");
                yield return Descend(3);
                yield return Surface();
            }
            yield return EndDay("PAYDAY");
            Check(Day.Payday && Day.Day == 3, "payday");
            Check(hud.Visor.DayText == "PAYDAY" || !hud.Visor.On, "visor corner would say PAYDAY: " + hud.Visor.DayText);
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            H.ClientRequestCabin();
            yield return ExpectRefusal("Payday — sail home", "the cabin refuses on payday");
            H.ClientMoveLocalPlayerTo(sea.SpawnPoint(0).position); yield return Wait(0.3f);
            H.ClientRequestSail("Sea");
            yield return ExpectRefusal("Payday — only HQ", "Site 01 refused on payday");
            yield return Shot("payday-monitor");

            Heading("Cycle 1 — home and pay");
            yield return SailTo("HQ", WorldId.HQ);
            hqShip = ShipParts.InWorld(WorldId.HQ);
            yield return Expect(() => H.MonitorText().StartsWith("Docked at HQ — PAYDAY"), 3f, "monitor: " + H.MonitorText());
            H.ClientRequestSail("Sea");
            yield return ExpectRefusal("Pay the quota first", "the docked ship will not sail out unpaid");
            Check(deckCoin != null && deckCoin.IsSpawned && deckCoin.gameObject.scene == WorldScenes.Scene(WorldId.HQ) && hqShip.IsAboard(deckCoin.transform.position) && !hqShip.IsInStorageRoom(deckCoin.transform.position), "the deck coin came home on the deck, outside the room");
            yield return Expect(() => H.QuotaBoardText().StartsWith("PAYDAY"), 3f, "board: " + Board());
            int balanceBefore = Day.Balance;
            yield return Pay(Day.LastPay.Serial);
            int had = balanceBefore + boxTotal;
            if (had >= Quota)
            {
                Check(Day.LastPay.Paid && Day.LastPay.Sales == boxTotal && Day.Balance == had - Quota && Day.Day == 0 && !Day.Payday, $"PAID: sold ${boxTotal}, balance ${Day.Balance}, day 0");
                Check(H.QuotaBoardText().StartsWith("PAID $500"), "board: " + Board());
            }
            else
            {
                Check(Day.LastPay.Lost && Day.Balance == 0 && Day.Day == 0 && !Day.Payday, $"GAME LOST: had ${had} of ${Quota}");
                Check(H.QuotaBoardText().StartsWith("GAME LOST"), "board: " + Board());
            }
            yield return Expect(() => Day.BoxValue == 0, 3f, "box empty");
            Check(deckCoin != null && deckCoin.IsSpawned, "the deck coin was not sold");
            H.ClientMoveLocalPlayerTo(hqShip.SpawnPoint(0).position); yield return Wait(0.3f);
            H.ClientRequestEndDay();
            yield return ExpectRefusal("Not at sea", "End day at the dock is refused");
            H.ClientMoveLocalPlayerTo(new Vector3(-2.5f, 0f, -4.4f)); yield return Wait(0.3f);
            H.ClientRequestPay();
            yield return ExpectRefusal("Nothing to pay yet — dive first", "paying twice is refused");

            Heading("Cycle 2 — one dive, pay early from the balance");
            int balance2 = Day.Balance;
            yield return SailTo("Sea", WorldId.Sea);
            sea = ShipParts.InWorld(WorldId.Sea);
            Check(Day.Day == 1, "day 1 again");
            Check(deckCoin != null && deckCoin.IsSpawned && deckCoin.gameObject.scene == WorldScenes.Scene(WorldId.Sea) && sea.IsAboard(deckCoin.transform.position), "the deck coin sailed out again on the deck");
            yield return Descend(1);
            foreach (string name in new[] { "Coin 7", "Coin 3", "Coin 5", "Coin 6" }) yield return Grab(name);
            int haul4 = hud.Visor.OnMeValue;
            yield return Surface();
            H.ClientMoveLocalPlayerTo(sea.FromShipLocal(new Vector3(3.2f, 0f, -12.5f))); yield return null;
            H.ClientLookAtNamed("StorageWallStarboard"); yield return null;
            var inBox4 = new List<CarryableItem>();
            yield return DropAll(inBox4);
            yield return SettleInRoom(sea, inBox4);
            int box4 = Sum(inBox4);
            yield return Expect(() => Day.BoxValue == box4, 3f, $"box ${Day.BoxValue}");
            yield return EndDay("Day 2 of 3");
            H.ClientRequestEndDay();
            yield return ExpectRefusal("Nobody has dived today", "End day twice is refused");
            yield return SailTo("HQ", WorldId.HQ);
            yield return Pay(Day.LastPay.Serial);
            int had2 = balance2 + box4;
            if (had2 >= Quota) Check(Day.LastPay.Paid && Day.Balance == had2 - Quota && Day.Day == 0, $"cycle 2 PAID early from the balance: ${had2} → ${Day.Balance}");
            else Check(Day.LastPay.Short && Day.Balance == had2 && Day.Day == 2, $"cycle 2 SHORT by ${Quota - had2}, banked");
            Check(deckCoin != null && deckCoin.IsSpawned, "the deck coin survived two sails and two sales");
            yield return Shot("end");
            Say("done");
        }
    }
}
