using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.Shop;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The crew wipe (Dan, 29 September 2026; docs/DESIGN.md §4 "Nobody came back"):
    // when every connected player is dead the day ends by itself after the NOBODY
    // CAME BACK card. Host + guest builds: W1 one dead and one alive is no wipe (END
    // DAY still needed); W2 both die below — the card on both peers for ~8 s, the day
    // advanced exactly once, both alive on the deck with a full tank, upgrades gone,
    // the storage room's coin kept, joins refused while the card is up; W3 a wipe on
    // the last day is payday; W4 the guest leaves during the card; W5 a new guest
    // joins (no replay of old cards), rides up and leaves, and the host dies below
    // while the car is on its way down for him. Log: Temp/wipe-matrix.log. Started by
    // CameraClearanceMatrixDriver.Start("wipe") — the one job that runs with the crew
    // wipe on (WorldLoopSettings.CrewWipeDisabledForTests false).
    public static class WipeRuntimeChecks
    {
        private const string Log = "Temp/wipe-matrix.log";
        private const string GuestDir = "Temp/wipe-guest";
        private const string GuestDirB = "Temp/wipe-guest-b";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Card = WorldSceneFlow.CrewWipeCardText;

        private static Stack<IEnumerator> stack = new();
        private static Process guest, guestB;
        private static int guestCommand = 900;
        private static string lastReply = string.Empty;
        private static int dayChanges, paydayChanges;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;

        [MenuItem("Sunk Cost/Prototype/Run crew wipe matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Crew wipe matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (stack.Count > 0) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Crew wipe matrix started " + DateTime.Now + "\n");
            Status = "Running";
            stack = new Stack<IEnumerator>();
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
            if (Status == "MATRIX_PASS") Debug.Log("Crew wipe matrix: MATRIX_PASS"); else Debug.LogError("Crew wipe matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            try { if (guestB != null && !guestB.HasExited) guestB.Kill(); } catch (Exception) { }
            guest = null; guestB = null;
            if (Day != null) { Day.DayChanged -= CountDay; Day.PaydayChanged -= CountPayday; }
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        // ---- harness (SpectateRuntimeChecks' pattern) ------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + (Day != null ? Day.DebugStatus : "no day state"));
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static IEnumerator Expect(Func<bool> condition, float seconds, Func<string> label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
            Check(condition(), label());
        }
        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }
        private static Process LaunchGuest(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(dir, stale))) File.Delete(Path.Combine(dir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-screen-width 960 -screen-height 540 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(dir) + "\" -logFile \"" + Path.GetFullPath(dir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return Process.Start(info);
        }
        private static IEnumerator Send(string json, string dir)
        {
            string text = json.Replace("{id}", (++guestCommand).ToString());
            for (int attempt = 0; ; attempt++)
            {
                bool written = false;
                try { File.WriteAllText(Path.Combine(dir, "command.json"), text); written = true; }
                catch (IOException) when (attempt < 20) { }
                if (written) break;
                yield return null;
            }
            float deadline = Time.unscaledTime + 10f;
            while (Time.unscaledTime < deadline)
            {
                string reply = Reply(dir);
                if (reply.StartsWith("id=" + guestCommand + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest (" + dir + ") did not answer command " + guestCommand + ": " + json);
        }
        private static string Reply(string dir)
        {
            try { string p = Path.Combine(dir, "reply.txt"); return File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
            catch (IOException) { return string.Empty; }
        }
        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, string label, string dir)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", dir);
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.4f);
            }
            throw new Exception(label + "\n" + lastReply);
        }
        private static string GuestPlayerLine(string reply, int ownerId)
        {
            foreach (string line in reply.Split('\n')) if (line.StartsWith("player=" + ownerId + ";")) return line;
            return string.Empty;
        }
        private static List<int> OtherIds()
        {
            var ids = new List<int>();
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (!p.IsOwner && p.IsSpawned) ids.Add(p.OwnerId);
            ids.Sort();
            return ids;
        }
        private static HQPlayerController PlayerWithId(int id)
        {
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (p.OwnerId == id && p.IsSpawned) return p;
            return null;
        }
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static void CountDay(int previous, int next) => dayChanges++;
        private static void CountPayday(bool payday) { if (payday) paydayChanges++; }
        private static string FadeText() => ScreenFade.Instance == null ? "(no fade)" : ScreenFade.Instance.Text.Replace("\n", " | ");

        // ---- moves --------------------------------------------------------------------

        // The host and the given guests into the deck cabin, down, in the site on `day`.
        private static IEnumerator Descend(int day, ShipParts sea, params (HQPlayerController remote, string dir, float side)[] guests)
        {
            H.MoveLocalIntoDeckCabin("Sea");
            float floor = DeckCabinBuilder.FloorThicknessMeters + 0.05f;
            foreach (var g in guests)
                yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position + sea.DeckCabin.right * g.side + Vector3.up * floor) + "}", g.dir);
            yield return Wait(0.5f);
            foreach (var g in guests)
            {
                HQPlayerController r = g.remote;
                yield return Expect(() => sea.IsInDeckCabin(r.transform.position), 6f, () => $"day {day}: the server sees guest {r.OwnerId} in the deck cabin ({sea.ToShipLocal(r.transform.position):F1})");
            }
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => $"day {day}: the deck cabin took the press (refusal: {Day.LastRefusal.Text})");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => $"day {day}: the ride down completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), $"day {day}: down in the dive site: {H.RideStatus()}");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => $"day {day}: the car is at the bottom");
            yield return Wait(1.5f);
            Check(Day.Phase == DayPhase.DiveInProgress && Day.Day == day && Day.Below.Count == 1 + guests.Length, $"day {day}: dive in progress with {1 + guests.Length} below");
            foreach (var g in guests)
            {
                int id = g.remote.OwnerId;
                yield return GuestEventually(r => GuestPlayerLine(r, id).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, $"day {day}: guest {id} is in the site", g.dir);
            }
        }
        private static Vector3 Doorway(ElevatorController car) => car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);

        // A coin of known worth spawned in the storage room of the ship at sea.
        private static IEnumerator CoinInRoom(ShipParts sea, Action<CarryableItem> got)
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == "CoinMedium") { prefab = candidate; break; }
            }
            Check(prefab != null, "the CoinMedium prefab is registered");
            Vector3 at = sea.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f));
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            instance.name = "Wipe box coin";
            CarryableItem item = instance.GetComponent<CarryableItem>();
            item.SetResetPositionBeforeSpawn(at);
            nm.ServerManager.Spawn(instance, null, sea.gameObject.scene);
            yield return Wait(1.2f);
            Check(item.IsSpawned && item.Value > 0 && sea.IsInStorageRoom(item.transform.position), $"a ${item.Value} coin lies in the storage room");
            yield return Expect(() => Day.BoxValue >= item.Value, 2f, () => $"the box reads ${Day.BoxValue}");
            got(item);
        }

        // Everyone listed dies below, in order, each standing outside the car.
        private static IEnumerator KillBelow(HQPlayerController host, params (int id, string dir)[] guests)
        {
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 doorway = Doorway(car), side = Vector3.Cross(Vector3.up, doorway);
            int k = 0;
            foreach (var g in guests)
            {
                yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + doorway * 4f + side * (1.5f * (k++ - 0.5f)) + Vector3.up * 0.05f) + "}", g.dir);
                yield return Wait(0.6f);
                yield return Send("{\"id\":{id},\"action\":\"die\"}", g.dir);
                int id = g.id;
                yield return Expect(() => Day.IsDead(id), 5f, () => $"guest {id} died below");
            }
            if (host != null)
            {
                host.TeleportLocal(car.transform.position + doorway * 2.5f + Vector3.up * 0.05f, host.Yaw); yield return Wait(0.6f);
                host.RequestDebugDeath();
                yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 3f, () => "the host died below");
            }
        }

        // The wipe from the last death: the card after the settle, on every peer, for the
        // card's seconds, then the day once. Returns the card's measured hold.
        private static IEnumerator WipePlays(string row, int wipeSerialBefore, float deathAt, int dayBefore, bool payday, string[] guestDirs, Action<float> held)
        {
            WorldLoopSettings s = Flow.Settings;
            dayChanges = 0; paydayChanges = 0;
            yield return Expect(() => Day.CrewWipe.Active && Day.CrewWipe.Serial == wipeSerialBefore + 1, 90f, () => $"{row} the card went up (wipe {Day.CrewWipe.Serial}/{Day.CrewWipe.Active})");
            float up = Time.unscaledTime;
            Check(up - deathAt >= s.CrewWipeSettleSeconds - 0.05f, $"{row} not before the death settled: {up - deathAt:0.0} s after the last death (settle {s.CrewWipeSettleSeconds:0.#} s)");
            Check(WorldSceneFlow.FindCar() == null && Day.Elevator.State == ElevatorState.AtTop, $"{row} the card waited for the site to close (car {Day.Elevator.State})");
            Check(Host().gameObject.scene == WorldScenes.Scene(WorldId.Sea) && Host().IsDead, $"{row} the dead host was carried to the ship before the card");
            Check(Day.Day == dayBefore && !Day.Payday && Day.DiveDone, $"{row} the day has not ended yet under the card (day {Day.Day}, diveDone {Day.DiveDone})");
            Check(Day.RefusesJoins, $"{row} joins are refused while the card is up");
            yield return Expect(() => ScreenFade.Instance != null && ScreenFade.Instance.IsBlack && ScreenFade.Instance.Text == Card, 2f, () => $"{row} the host's screen: black with '{FadeText()}'");
            foreach (string dir in guestDirs)
                yield return GuestEventually(r => r.Contains("fadeText='" + Card + "'") && r.Contains("wipe=" + Day.CrewWipe.Serial + "/True"), 5f, $"{row} the guest ({dir}) shows the card", dir);
            int wantDay = payday ? dayBefore : dayBefore + 1;
            yield return Expect(() => payday ? Day.Payday : Day.Day == wantDay, s.CrewWipeCardSeconds + 6f, () => $"{row} the day ended by itself (day {Day.Day}, payday {Day.Payday})");
            float hold = Time.unscaledTime - up;
            held(hold);
            // The day card takes the black over on this peer (DAY n OF N, PAYDAY), then fades in itself.
            string follow = FadeText();
            string wantCard = s.DayCardSeconds <= 0f ? Card : payday ? "PAYDAY" : $"DAY {wantDay} OF {s.DaysPerCycle}";
            Check(follow == wantCard, $"{row} the host's card after the wipe's: '{follow}' (want '{wantCard}')");
            Check(hold >= s.CrewWipeCardSeconds - 0.3f && hold <= s.CrewWipeCardSeconds + 2.5f, $"{row} the card held {hold:0.0} s (setting {s.CrewWipeCardSeconds:0.#} s)");
            yield return Expect(() => !Day.CrewWipe.Active, 3f, () => $"{row} the card came down on the server");
            yield return Wait(3f); // a second end would show here
            Check(Day.Day == wantDay && Day.Payday == payday && (payday ? paydayChanges == 1 && dayChanges == 0 : dayChanges == 1 && paydayChanges == 0),
                $"{row} the day advanced exactly once (day {Day.Day}, payday {Day.Payday}; day changes {dayChanges}, payday changes {paydayChanges})");
            Check(!Day.DiveDone && Day.Dead.Count == 0, $"{row} nobody is dead and today's dive is cleared");
        }

        // After the wipe: the host alive on the deck with a full tank and no upgrades.
        private static IEnumerator HostBackOnDeck(string row, ShipParts sea)
        {
            HQPlayerController host = Host();
            yield return Expect(() => !host.IsDead && host.Controller.enabled && sea.IsAboard(host.transform.position), 5f, () => $"{row} the host stands alive on the deck ({sea.ToShipLocal(host.transform.position):F1}, dead {host.IsDead})");
            Check(host.Vitals.AirFraction >= 0.999f && host.Vitals.HealthFraction >= 0.999f, $"{row} the host has a full tank and full health (air {host.Vitals.AirFraction:0.###}, health {host.Vitals.Health})");
            Check(host.Upgrades.Owned == PlayerUpgrade.None, $"{row} the host's upgrades are gone: the body was not brought up ({host.Upgrades.Owned})");
            Check(host.Inventory.HeldItem == null && host.Inventory.Slots.FirstFree() == 0, $"{row} the host carries nothing (what the dead carried stayed below)");
            yield return Expect(() => ScreenFade.Instance.IsClear, 6f, () => $"{row} the host's screen is clear again ('{FadeText()}', alpha {ScreenFade.Instance.Alpha:0.00})");
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            Check(!WorldLoopSettings.CrewWipeDisabledForTests, "the crew wipe is on for this job");
            WorldSceneFlow flow = Flow;
            Say($"settings: card {flow.Settings.CrewWipeCardSeconds:0.#} s, settle {flow.Settings.CrewWipeSettleSeconds:0.#} s, day card {flow.Settings.DayCardSeconds:0.#} s, {flow.Settings.DaysPerCycle} days per cycle");
            Day.DayChanged += CountDay;
            Day.PaydayChanged += CountPayday;

            Heading("W0 — to sea; guest A joins");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "W0 sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "W0 arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "W0 controls back");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            guest = LaunchGuest(GuestDir);
            yield return Expect(() => OtherIds().Count == 1, 40f, () => "W0 guest A spawned");
            int idA = OtherIds()[0];
            HQPlayerController remoteA = PlayerWithId(idA);
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("local=True") && r.Contains("world=Sea"), 20f, "W0 guest A joined at sea", GuestDir);
            yield return GuestEventually(r => r.Contains("wipeCards=0;"), 3f, "W0 guest A has shown no card", GuestDir);

            Heading("W1 — one dead, one alive: no wipe; END DAY is still needed");
            yield return Descend(1, sea, (remoteA, GuestDir, 1.2f));
            int wipeSerial = Day.CrewWipe.Serial;
            yield return KillBelow(null, (idA, GuestDir));
            Check(Day.Phase == DayPhase.DiveInProgress && !host.IsDead, "W1 the host lives on below");
            H.MoveLocalIntoCar(); yield return Wait(0.5f);
            int rideSerial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > rideSerial, 3f, () => "W1 the car took the host's press");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "W1 the host rode up alive");
            yield return Expect(() => WorldSceneFlow.FindCar() == null && remoteA.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 30f, () => "W1 the site closed with dead A carried to the ship");
            host.TeleportLocal(sea.SpawnPoint(0).position, 0f);
            float wait = flow.Settings.CrewWipeSettleSeconds + flow.Settings.CrewWipeCardSeconds + 4f;
            yield return Wait(wait);
            Check(Day.CrewWipe.Serial == wipeSerial && !Day.CrewWipe.Active && !flow.CrewWipePending, $"W1 no card with a living player aboard after {wait:0} s (wipe {Day.CrewWipe.Serial})");
            Check(Day.Day == 1 && Day.DiveDone && Day.IsDead(idA) && remoteA.IsDead, "W1 the day waits: day 1, dive done, A still dead");
            Check(ScreenFade.Instance.Text != Card, "W1 the host never saw the card ('" + FadeText() + "')");
            dayChanges = 0;
            Check(flow.ServerEndDay(host.Owner, out string w1Why), "W1 END DAY by the living host: " + w1Why);
            yield return Expect(() => !remoteA.IsDead && Day.Day == 2, 5f, () => "W1 A revived, day 2");
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("dead=False") && GuestPlayerLine(r, idA).Contains("scene=ShipAtSea") && r.Contains("wipeCards=0;"), 10f, "W1 A reads itself alive on the ship and has shown no card", GuestDir);

            Heading("W2 — both die below: NOBODY CAME BACK on both peers, the day once, both alive on the deck");
            host.Upgrades.ServerGrant(PlayerUpgrade.LargeTank);
            host.Upgrades.ServerGrant(PlayerUpgrade.BrightHeadlamp);
            remoteA.Upgrades.ServerGrant(PlayerUpgrade.LargeTank);
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("upgrades=LargeTank"), 5f, "W2 A owns the large tank", GuestDir);
            CarryableItem boxCoin = null;
            yield return CoinInRoom(sea, c => boxCoin = c);
            int boxWorth = Day.BoxValue;
            yield return Descend(2, sea, (remoteA, GuestDir, 1.2f));
            Say($"W2 the host's tank below: {host.Vitals.AirFraction:0.###}");
            wipeSerial = Day.CrewWipe.Serial;
            yield return KillBelow(host, (idA, GuestDir));
            float deathAt = Time.unscaledTime;
            Check(Day.Dead.Count == 2 && Day.Below.Count == 0, "W2 both dead, nobody below");
            yield return Expect(() => flow.CrewWipePending, 2f, () => "W2 the server judged a crew wipe");
            Check(!Day.CrewWipe.Active, "W2 no card yet while the site closes (the death settles first)");
            float w2Hold = 0f;
            yield return WipePlays("W2", wipeSerial, deathAt, 2, false, new[] { GuestDir }, h => w2Hold = h);
            yield return HostBackOnDeck("W2", sea);
            yield return Expect(() => !remoteA.IsDead && sea.IsAboard(remoteA.transform.position), 6f, () => $"W2 A stands alive on the deck ({sea.ToShipLocal(remoteA.transform.position):F1})");
            Check(remoteA.Vitals.AirFraction >= 0.999f && remoteA.Upgrades.Owned == PlayerUpgrade.None, $"W2 A: a full tank and no upgrades (air {remoteA.Vitals.AirFraction:0.###}, {remoteA.Upgrades.Owned})");
            yield return GuestEventually(r => { string me = GuestPlayerLine(r, idA); return me.Contains("dead=False") && me.Contains("scene=ShipAtSea") && me.Contains("air=1;") && me.Contains("upgrades=None") && r.Contains("day=3;") && r.Contains("wipeCards=1;"); }, 10f, "W2 A's own view: alive on the ship, air 1, no upgrades, day 3, one card shown", GuestDir);
            yield return GuestEventually(r => r.Contains("fade=0;"), 6f, "W2 A's screen is clear again", GuestDir);
            Check(boxCoin != null && boxCoin.IsSpawned && sea.IsInStorageRoom(boxCoin.transform.position) && Day.BoxValue == boxWorth, $"W2 the storage room kept its coin (box ${Day.BoxValue}, was ${boxWorth})");
            Check(PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Sea)) == null && PlayerBody.FindFor(idA, WorldScenes.Scene(WorldId.Sea)) == null, "W2 no body came up");
            Say($"W2 card held {w2Hold:0.00} s");

            Heading("W3 — a wipe on the last day: payday");
            Check(Day.Day == flow.Settings.DaysPerCycle && !Day.Payday, $"W3 day {Day.Day} is the last of {flow.Settings.DaysPerCycle}");
            yield return Descend(Day.Day, sea, (remoteA, GuestDir, 1.2f));
            wipeSerial = Day.CrewWipe.Serial;
            int lastDay = Day.Day;
            yield return KillBelow(host, (idA, GuestDir));
            deathAt = Time.unscaledTime;
            yield return WipePlays("W3", wipeSerial, deathAt, lastDay, true, new[] { GuestDir }, h => { });
            yield return HostBackOnDeck("W3", sea);
            yield return GuestEventually(r => r.Contains("payday=True") && GuestPlayerLine(r, idA).Contains("dead=False") && r.Contains("wipeCards=2;"), 10f, "W3 A reads payday, alive, two cards shown", GuestDir);

            Heading("W4 — the guest leaves during the card: the day still ends once, the host stands up");
            Day.ServerForceCycleForChecks(1, false);
            yield return Descend(1, sea, (remoteA, GuestDir, 1.2f));
            wipeSerial = Day.CrewWipe.Serial;
            yield return KillBelow(host, (idA, GuestDir));
            dayChanges = 0; paydayChanges = 0;
            yield return Expect(() => Day.CrewWipe.Active && Day.CrewWipe.Serial == wipeSerial + 1, 90f, () => "W4 the card went up");
            yield return Wait(1.5f);
            yield return Send("{\"id\":{id},\"action\":\"leave\"}", GuestDir);
            yield return Expect(() => PlayerWithId(idA) == null && !Day.IsDead(idA), 8f, () => "W4 A left during the card");
            Check(Day.CrewWipe.Active && Day.Day == 1, "W4 the card is still up after the leave");
            yield return Expect(() => Day.Day == 2, flow.Settings.CrewWipeCardSeconds + 6f, () => $"W4 the day ended by itself (day {Day.Day})");
            yield return Expect(() => !Day.CrewWipe.Active, 3f, () => "W4 the card came down");
            yield return Wait(3f);
            Check(Day.Day == 2 && dayChanges == 1, $"W4 the day advanced exactly once (day {Day.Day}, changes {dayChanges})");
            yield return HostBackOnDeck("W4", sea);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;

            Heading("W5 — a new guest joins (no old card replayed), rides up and leaves; the host dies while the car comes down for him");
            guestB = LaunchGuest(GuestDirB);
            yield return Expect(() => OtherIds().Count == 1, 40f, () => "W5 guest B spawned");
            int idB = OtherIds()[0];
            HQPlayerController remoteB = PlayerWithId(idB);
            yield return GuestEventually(r => GuestPlayerLine(r, idB).Contains("local=True") && r.Contains("world=Sea"), 20f, "W5 guest B joined at sea", GuestDirB);
            yield return GuestEventually(r => r.Contains("wipeCards=0;") && r.Contains("wipe=" + Day.CrewWipe.Serial + "/False") && !r.Contains("fadeText='" + Card + "'"), 3f, $"W5 B holds wipe {Day.CrewWipe.Serial} and has not replayed it", GuestDirB);
            yield return Descend(2, sea, (remoteB, GuestDirB, 1.2f));
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 doorway = Doorway(car);
            host.TeleportLocal(car.transform.position + doorway * 5f + Vector3.up * 0.05f, host.Yaw);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f)) + "}", GuestDirB);
            yield return Wait(0.8f);
            rideSerial = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}", GuestDirB);
            yield return Expect(() => Day.CabinRide.Serial > rideSerial, 5f, () => "W5 the car took B's press");
            yield return Expect(() => !Day.CabinRide.Active && Day.CabinRide.Serial > rideSerial, 70f, () => "W5 B rode up alone");
            Check(Day.Below.Count == 1 && Day.IsBelow(host.OwnerId), "W5 the host alone below");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}", GuestDirB);
            yield return Expect(() => PlayerWithId(idB) == null, 8f, () => "W5 B left");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Descending, flow.Settings.CarReturnGraceSeconds + 15f, () => $"W5 the car is on its way down for the host ({Day.Elevator.State})");
            yield return Wait(2f);
            Check(Day.Elevator.State == ElevatorState.Descending, "W5 still descending at the death");
            wipeSerial = Day.CrewWipe.Serial;
            dayChanges = 0;
            yield return KillBelow(host);
            deathAt = Time.unscaledTime;
            Check(Day.Elevator.State == ElevatorState.Descending || Day.Elevator.State == ElevatorState.AtBottom, $"W5 the last death came with the car moving ({Day.Elevator.State})");
            yield return WipePlays("W5", wipeSerial, deathAt, 2, false, new string[0], h => { });
            yield return HostBackOnDeck("W5", sea);
            try { if (guestB != null && !guestB.HasExited) guestB.Kill(); } catch (Exception) { }
            guestB = null;
            Say("done");
        }
    }
}
