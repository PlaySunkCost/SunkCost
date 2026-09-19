using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.Shop;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The save (RunSave, WorldSceneFlow.Save; Dan, 19 September 2026): three named
    // slots on the host's machine, written whenever the crew is at HQ, read at the
    // next host. S: a host without a slot keeps nothing; a slot's run — day,
    // balance, the box, the host's upgrades and hands — survives a leave and a
    // re-host; renaming and deleting. G: a guest's upgrade comes back by its
    // name. The slots live in a scratch folder for the run (SaveSlots.DirectoryOverride).
    // Log: Temp/save-matrix.log. Started by CameraClearanceMatrixDriver.Start("save").
    public static class SaveRuntimeChecks
    {
        private const string Log = "Temp/save-matrix.log";
        private const string SavesDir = "Temp/save-matrix-saves";
        private const string GuestDir = "Temp/save-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1300;
        private static string lastReply = string.Empty;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static PrototypeSessionUI Ui() => UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>();

        [MenuItem("Sunk Cost/Prototype/Run save matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Save matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Save matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Save matrix: MATRIX_PASS"); else Debug.LogError("Save matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            SaveSlots.DirectoryOverride = null;
            steps = null;
            stack.Clear();
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
        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-batchmode -nographics -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
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
        private static int GuestId() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Where(p => !p.IsOwner && p.IsSpawned).Select(p => p.OwnerId).DefaultIfEmpty(-1).First();
        private static HQPlayerController GuestPlayer() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => !p.IsOwner && p.IsSpawned);

        private static IEnumerator SailAndWait(string world, WorldId expected, string label)
        {
            string sail = H.ServerSail(world);
            Check(sail.StartsWith("sailing"), label + ": " + sail);
            yield return Expect(() => Day != null && Day.Departure.Stage == DepartureStage.Complete && Day.World == expected && !Flow.Transitioning, 40f, () => label + ": arrived at " + expected);
            yield return Wait(0.5f);
        }

        // Leave the room and host again with a slot (the loop matrix's S15 pattern).
        private static IEnumerator ReHost(int slot, string label)
        {
            Ui().LeaveSession();
            yield return Expect(() => !FishNet.InstanceFinder.IsServerStarted && !FishNet.InstanceFinder.IsClientStarted, 15f, () => label + " host left");
            yield return Expect(() => CrewDayState.Instance == null, 10f, () => label + " day state gone");
            bool hosted = false;
            for (int attempt = 0; attempt < 4 && !hosted; attempt++)
            {
                yield return Wait(2f);
                Ui().StartLocalHost(slot);
                float deadline = Time.unscaledTime + 12f;
                while (Time.unscaledTime < deadline)
                {
                    if (Host() != null && Host().IsServerStarted && Day != null && Day.Phase == DayPhase.AtHQ && ShipParts.InWorld(WorldId.HQ) != null) { hosted = true; break; }
                    yield return null;
                }
            }
            Check(hosted, label + " re-hosted" + (SaveSlots.IsValid(slot) ? " on slot " + (slot + 1) : " without a slot"));
            yield return Wait(1.5f); // the name lands, the restore runs, the box respawns
        }

        private static IEnumerator BuyAtStand(string itemId, string label)
        {
            HQPlayerController host = Host();
            ShopDisplay stand = null;
            foreach (ShopDisplay d in UnityEngine.Object.FindObjectsByType<ShopDisplay>(FindObjectsInactive.Exclude))
                if (d.ItemId == itemId && d.gameObject.scene == WorldScenes.Scene(WorldId.HQ)) stand = d;
            Check(stand != null, label + ": a stand sells " + itemId);
            Vector3 at = stand.transform.position + stand.transform.forward * 1.6f; at.y = 0.05f;
            host.TeleportLocal(at, host.Yaw); yield return null; yield return null;
            Check(Flow.ServerBuy(host.Owner, itemId, out string why), label + ": bought " + itemId + " (" + why + ")");
        }

        private static CarryableItem TankNamed(string startsWith, UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).FirstOrDefault(c => c.IsSpawned && c.gameObject.scene == scene && c.GetComponent<AirTankItem>() != null && c.name.StartsWith(startsWith));
        private static int TanksIn(UnityEngine.SceneManagement.Scene scene) =>
            UnityEngine.Object.FindObjectsByType<CarryableItem>(FindObjectsInactive.Exclude).Count(c => c.IsSpawned && c.gameObject.scene == scene && c.GetComponent<AirTankItem>() != null);

        // ---- the rows -------------------------------------------------------------------

        private static IEnumerator Run()
        {
            if (Directory.Exists(SavesDir)) Directory.Delete(SavesDir, true);
            SaveSlots.DirectoryOverride = SavesDir;
            ShopCatalog catalog = ShopCatalog.Resolve();
            int largePrice = catalog.Find(ShopCatalog.LargeTankId).Price, tankPrice = catalog.Find(ShopCatalog.AirTankId).Price;

            Heading("S0 — hosted by the driver without a slot: nothing is kept");
            Check(Flow != null && !Flow.HostsASave, "S0 this run hosts no save");
            Day.ServerSetBalanceForChecks(500); yield return Wait(0.2f);
            yield return BuyAtStand(ShopCatalog.LargeTankId, "S0");
            yield return Wait(0.3f);
            Check(!SaveSlots.Exists(0) && !SaveSlots.Exists(1) && !SaveSlots.Exists(2), "S0 a buy wrote no slot");
            Check(SaveSlots.Summary(0) == "Save 1 — empty" && SaveSlots.DefaultName(2) == "Save 3", "S0 the empty slots read 'Save 1 — empty': " + SaveSlots.Summary(0));

            Heading("S1 — a new run in slot 1, named; the host keeps nothing from the unsaved run");
            SaveSlots.Write(0, new RunSaveData { name = SaveSlots.CleanName("  Matrix run  ", 0) }); // what the Host button does for an empty slot
            Check(SaveSlots.Exists(0) && SaveSlots.Load(0).name == "Matrix run" && SaveSlots.Load(0).IsFresh, "S1 slot 1 written fresh as 'Matrix run'");
            yield return ReHost(0, "S1");
            Check(Flow.HostsASave && Flow.HostedSaveName == "Matrix run", "S1 the host runs slot 1 'Matrix run'");
            Check(Day.Day == 0 && Day.Balance == 0 && !Day.Payday, "S1 a fresh run: day 0, $0");
            Check(Host().Upgrades.Owned == PlayerUpgrade.None, "S1 the unsaved run's large tank is gone");

            Heading("S2 — a buy at HQ writes the slot: the balance, the host's upgrade under its name");
            Day.ServerSetBalanceForChecks(500); yield return Wait(0.2f);
            yield return BuyAtStand(ShopCatalog.LargeTankId, "S2");
            yield return Wait(0.3f);
            RunSaveData saved = SaveSlots.Load(0);
            Check(saved != null && saved.balance == 500 - largePrice, "S2 the slot's balance is $" + (saved == null ? -1 : saved.balance));
            SavedPlayer me = saved.players.FirstOrDefault(p => p.identity == "name:Skipper");
            Check(me != null && (me.upgrades & (int)PlayerUpgrade.LargeTank) != 0, "S2 the host is kept as 'name:Skipper' with the large tank");
            Check(SaveSlots.Summary(0).StartsWith("Matrix run · at HQ, no cycle · $" + (500 - largePrice)), "S2 the summary reads: " + SaveSlots.Summary(0));

            Heading("S3 — a tank in the storage room and one in the hands cross the cast-off into the slot");
            yield return BuyAtStand(ShopCatalog.AirTankId, "S3 first tank");
            yield return Expect(() => TankNamed("Air tank (bought)", WorldScenes.Scene(WorldId.HQ)) != null, 3f, () => "S3 the first tank landed");
            CarryableItem boxTank = TankNamed("Air tank (bought)", WorldScenes.Scene(WorldId.HQ));
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            boxTank.ServerDropAt(hqShip.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f)));
            boxTank.name = "Box tank";
            yield return Wait(1.5f);
            Check(hqShip.IsInStorageRoom(boxTank.transform.position), "S3 the first tank lies in the docked ship's storage room");
            yield return BuyAtStand(ShopCatalog.AirTankId, "S3 second tank");
            yield return Expect(() => TankNamed("Air tank (bought)", WorldScenes.Scene(WorldId.HQ)) != null, 3f, () => "S3 the second tank landed");
            CarryableItem handTank = TankNamed("Air tank (bought)", WorldScenes.Scene(WorldId.HQ));
            handTank.name = "Hand tank";
            HQPlayerController host = Host();
            host.TeleportLocal(handTank.transform.position + Vector3.right * 0.8f, host.Yaw); yield return Wait(0.3f);
            H.ClientLookAtNamed(handTank.name); yield return null;
            yield return Expect(() => host.CurrentTarget == handTank, 3f, () => "S3 the dot is on the second tank");
            host.Inventory.RequestGrab(handTank);
            yield return Expect(() => handTank.HolderClientId == host.OwnerId, 3f, () => "S3 the second tank is in the hands");
            // Aboard, then cast off: the save at the dock has the box and the hands.
            host.TeleportLocal(hqShip.BoardingPoint != null ? hqShip.BoardingPoint.position : hqShip.FromShipLocal(new Vector3(-5f, 0f, 2f)), host.Yaw); yield return Wait(0.5f);
            yield return SailAndWait("Sea", WorldId.Sea, "S3 cast off");
            saved = SaveSlots.Load(0);
            Check(saved != null && saved.box.Count == 1 && saved.box[0].instanceName == "Box tank" && !saved.box[0].empty, "S3 the slot's box holds the first tank (" + (saved == null ? "?" : saved.box.Count.ToString()) + ")");
            me = saved.players.FirstOrDefault(p => p.identity == "name:Skipper");
            Check(me != null && me.hasHeld && me.held.instanceName == "Hand tank", "S3 the host's hands hold the second tank in the slot");
            Check(saved.day == 0 && saved.balance == 500 - largePrice - 2 * tankPrice, "S3 the cast-off state: day 0, $" + saved.balance);

            Heading("S4 — home again: the docking writes the slot; nothing new at sea leaked in");
            yield return SailAndWait("HQ", WorldId.HQ, "S4 home");
            saved = SaveSlots.Load(0);
            Check(saved != null && saved.box.Count == 1 && saved.players.Count == 1, "S4 written at the dock: 1 in the box, 1 known");

            Heading("S5 — the host leaves and hosts the slot again: the run, the box, the upgrade and the hands are back");
            string savedAtLeave = SaveSlots.Load(0).savedUtc;
            yield return ReHost(0, "S5");
            Check(Day.Balance == 500 - largePrice - 2 * tankPrice && Day.Day == 0, "S5 the balance is back: $" + Day.Balance);
            hqShip = ShipParts.InWorld(WorldId.HQ);
            yield return Expect(() => TankNamed("Box tank", WorldScenes.Scene(WorldId.HQ)) != null, 5f, () => "S5 the box tank respawned");
            CarryableItem boxBack = TankNamed("Box tank", WorldScenes.Scene(WorldId.HQ));
            Check(hqShip.IsInStorageRoom(boxBack.transform.position) && boxBack.CanGrabFromWorld, "S5 it lies in the storage room, loose (" + hqShip.ToShipLocal(boxBack.transform.position).ToString("F1") + ")");
            host = Host();
            yield return Expect(() => host.Upgrades.Has(PlayerUpgrade.LargeTank), 5f, () => "S5 the host's large tank is back");
            yield return Expect(() => TankNamed("Hand tank", WorldScenes.Scene(WorldId.HQ)) != null && TankNamed("Hand tank", WorldScenes.Scene(WorldId.HQ)).HolderClientId == host.OwnerId && TankNamed("Hand tank", WorldScenes.Scene(WorldId.HQ)).State == ItemState.Held, 5f, () => "S5 the hand tank is in the host's hands again");
            Check(TanksIn(WorldScenes.Scene(WorldId.HQ)) == 2, "S5 exactly two tanks exist (" + TanksIn(WorldScenes.Scene(WorldId.HQ)) + ")");
            Check(SaveSlots.Load(0).savedUtc != savedAtLeave, "S5 the leave wrote the slot once more");

            Heading("S6 — rename and delete");
            SaveSlots.Rename(0, "Renamed run");
            Check(SaveSlots.Load(0).name == "Renamed run" && SaveSlots.Load(0).balance == Day.Balance, "S6 renamed, the run untouched");
            SaveSlots.Write(1, new RunSaveData { name = SaveSlots.CleanName(string.Empty, 1) });
            Check(SaveSlots.Load(1).name == "Save 2", "S6 an empty name falls back to 'Save 2'");
            SaveSlots.Delete(1);
            Check(!SaveSlots.Exists(1) && SaveSlots.Exists(0), "S6 slot 2 deleted, slot 1 kept");

            Heading("G1 — a guest's upgrade comes back by its name after a re-host");
            guest = LaunchGuest();
            yield return Expect(() => GuestId() >= 0 && GuestPlayer() != null && GuestPlayer().GetComponent<PlayerIdentity>().DisplayName != PlayerIdentity.Fallback(GuestId()), 40f, () => "G1 the guest spawned with its name");
            HQPlayerController guestPlayer = GuestPlayer();
            string guestName = guestPlayer.GetComponent<PlayerIdentity>().DisplayName;
            guestPlayer.Upgrades.ServerGrant(PlayerUpgrade.BrightHeadlamp);
            yield return Wait(0.3f);
            yield return BuyAtStand(ShopCatalog.AirTankId, "G1 a buy to write the slot"); // any HQ event writes everyone present
            yield return Wait(0.3f);
            saved = SaveSlots.Load(0);
            SavedPlayer them = saved.players.FirstOrDefault(p => p.identity == "name:" + guestName);
            Check(them != null && (them.upgrades & (int)PlayerUpgrade.BrightHeadlamp) != 0, "G1 the guest '" + guestName + "' is kept with the headlamp");
            guest.Kill(); guest = null;
            yield return Wait(1f);
            yield return ReHost(0, "G1");
            guest = LaunchGuest();
            yield return Expect(() => GuestPlayer() != null && GuestPlayer().Upgrades.Has(PlayerUpgrade.BrightHeadlamp), 40f, () => "G1 the guest rejoined and has its headlamp back");
            Check(!GuestPlayer().Upgrades.Has(PlayerUpgrade.LargeTank), "G1 and not the host's large tank");
            guest.Kill(); guest = null;

            Heading("Z — the scratch slots are the matrix's alone");
            Check(SaveSlots.Directory.EndsWith("save-matrix-saves"), "Z the slots lived in " + SaveSlots.Directory);
        }
    }
}
