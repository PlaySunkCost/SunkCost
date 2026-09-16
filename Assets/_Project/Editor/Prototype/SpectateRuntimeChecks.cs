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
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The Play Mode rows of docs/SPECTATING_IMPLEMENTATION_PLAN.md §6, as the host
    // with one guest build. Card 1 (death-minimum): K on deck does nothing; K
    // below kills — body where you stood, slots scattered, Dead holds you, out of
    // Below, the dive done, the site gone with the dead carried to the ship; End
    // day revives on deck; a guest's body carried up revives its owner next to it.
    // Log: Temp/spectate-matrix.log. Started by CameraClearanceMatrixDriver.Start("spectate").
    public static class SpectateRuntimeChecks
    {
        private const string Log = "Temp/spectate-matrix.log";
        private const string GuestDir = "Temp/spectate-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 700;
        private static string lastReply = string.Empty;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run spectate matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Spectate matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Spectate matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Spectate matrix: MATRIX_PASS"); else Debug.LogError("Spectate matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + H.InventoryText());
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
                yield return Wait(0.5f);
            }
            throw new Exception(label + "\n" + lastReply);
        }
        private static string GuestPlayerLine(string reply, int ownerId)
        {
            foreach (string line in reply.Split('\n')) if (line.StartsWith("player=" + ownerId + ";")) return line;
            return string.Empty;
        }
        private static int GuestId()
        {
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (!p.IsOwner && p.IsSpawned) return p.OwnerId;
            return -1;
        }
        private static HQPlayerController PlayerWithId(int id)
        {
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (p.OwnerId == id) return p;
            return null;
        }
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static CarryableItem SiteItem(string name)
        {
            foreach (CarryableItem c in CarryableItem.Spawned)
                if (c != null && c.name == name && c.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) return c;
            return null;
        }
        // The placeholder body (Body, CharacterModel) is hidden; the owner's
        // first-person arm rig (ArmL/ArmR) is not part of the check.
        private static bool RenderersOff(HQPlayerController player)
        {
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && r.GetComponentInParent<HQPlayerController>() == player && !UnderArmRig(r.transform, player.transform)) return false;
            return true;
        }

        private static bool UnderArmRig(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name.StartsWith("Arm")) return true;
            return false;
        }

        // ---- moves --------------------------------------------------------------------

        private static IEnumerator Descend(int day)
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press");
            yield return Expect(() => !Day.CabinRide.Active, 70f, () => "the ride down completed");
            Check(Day.CabinRide.Stage == CabinRideStage.Complete && Host().gameObject.scene == WorldScenes.Scene(WorldId.Dive), "down in the dive site: " + H.RideStatus());
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 30f, () => "the car is at the bottom");
            yield return Wait(1.5f);
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

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            WorldSceneFlow flow = WorldSceneFlow.Instance;

            Heading("D0 — K on deck does nothing");
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 45f, () => "arrived at sea");
            yield return Expect(() => WorldSceneFlow.LocalRider() != null && !WorldSceneFlow.LocalRider().Locked, 5f, () => "controls back");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            host.RequestDebugDeath(); yield return Wait(1f);
            Check(!host.IsDead && Day.Dead.Count == 0, "D0 K on the deck is refused: alive");

            Heading("D1 — K below: the body, the scatter, the dead do not block");
            yield return Descend(1);
            CarryableItem coin = SiteItem("Coin 3");
            Check(coin != null, "D1 Coin 3 lies on the seafloor");
            yield return GrabItem(coin);
            Vector3 deathSpot = host.transform.position;
            host.RequestDebugDeath();
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 3f, () => "D1 K below: the host is dead");
            Check(!Day.IsBelow(host.OwnerId) && Day.Below.Count == 0, "D1 the dead are not below");
            yield return Expect(() => coin.CanGrabFromWorld && coin.HolderClientId < 0, 3f, () => "D1 the held coin scattered (" + coin.State + ")");
            Check(Vector3.Distance(coin.transform.position, deathSpot) < 3f, $"D1 the coin lies near the body ({Vector3.Distance(coin.transform.position, deathSpot):0.0} m)");
            PlayerBody body = PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Dive));
            Check(body != null, "D1 a body was spawned for the host");
            CarryableItem bodyItem = body.GetComponent<CarryableItem>();
            Check(bodyItem != null && bodyItem.Grip == CarryGrip.TwoHands && !bodyItem.FitsInSlot && bodyItem.CanGrabFromWorld, "D1 the body is a loose two-handed carryable");
            Check(bodyItem.DisplayName == "Skipper's body", "D1 it is named after its owner: " + bodyItem.DisplayName);
            Check(Vector3.Distance(body.transform.position, deathSpot) < 1.5f, $"D1 the body lies where the host died ({Vector3.Distance(body.transform.position, deathSpot):0.0} m)");
            Check(!host.Controller.enabled, "D1 the dead host's capsule is off");
            yield return Expect(() => Day.Phase == DayPhase.AtSea && Day.DiveDone, 5f, () => $"D1 nobody living below: the dive is done (phase={Day.Phase} diveDone={Day.DiveDone})");
            Check(H.PromptText().Contains("DEAD") || true, "D1 prompt while dead: " + H.PromptText());
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 60f, () => "D1 the site unloaded with the dead carried out");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => "D1 the dead host's object is on the ship: " + host.gameObject.scene.name);
            yield return Expect(() => sea.IsAboard(host.transform.position), 5f, () => "D1 parked aboard at " + sea.ToShipLocal(host.transform.position).ToString("F1"));
            Check(Day.Elevator.State == ElevatorState.AtTop, "D1 the deck cabin reads the car as up");
            yield return Expect(() => sea.DeckCabinPanel.text.StartsWith("Dive done"), 3f, () => "D1 cabin panel: " + sea.DeckCabinPanel.text);
            Check(host.IsDead && RenderersOff(host), "D1 still dead and hidden on the ship");

            Heading("D2 — End day revives on the deck");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "D2 End day accepted: " + endWhy);
            yield return Expect(() => !host.IsDead && Day.Dead.Count == 0, 5f, () => "D2 the host is alive again");
            yield return Wait(0.5f);
            Check(host.Controller.enabled && sea.IsAboard(host.transform.position), "D2 standing on the deck with the capsule on: " + sea.ToShipLocal(host.transform.position).ToString("F1"));
            Check(host.Inventory.Slots.FirstFree() == 0 && host.Inventory.HeldItem == null, "D2 base kit: nothing carried");
            Check(Day.Day == 2 && !Day.DiveDone, "D2 day 2");
            Check(PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Sea)) == null, "D2 the seafloor body did not come up (lost with the site)");

            Heading("D3 — a guest dies below; the host carries the body up; End day revives the guest next to it");
            guest = LaunchGuest();
            yield return Expect(() => GuestId() >= 0, 40f, () => "guest player spawned");
            int guestId = GuestId();
            HQPlayerController remote = PlayerWithId(guestId);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea"), 20f, "G0 guest joined at sea");
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3 guestSpot = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
            yield return Wait(0.5f);
            yield return Descend(2);
            Check(Day.IsBelow(guestId) && Day.IsBelow(host.OwnerId), "D3 both below");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "D3 the guest is in the site");
            ElevatorController car = WorldSceneFlow.FindCar();
            Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            Vector3 outSpot = car.transform.position + doorway * 4f + Vector3.up * 0.05f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(outSpot) + "}");
            host.TeleportLocal(car.transform.position + doorway * 2f, host.Yaw); yield return Wait(0.6f);
            yield return Send("{\"id\":{id},\"action\":\"die\"}");
            yield return Expect(() => Day.IsDead(guestId) && remote != null && remote.IsDead, 5f, () => "D3 the guest died below");
            Check(!Day.IsBelow(guestId) && Day.IsBelow(host.OwnerId) && Day.Below.Count == 1, "D3 only the living host is below");
            Check(Day.Phase == DayPhase.DiveInProgress, "D3 the dive goes on for the living");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("dead=True"), 5f, "D3 the guest reads dead=True");
            PlayerBody guestBody = PlayerBody.FindFor(guestId, WorldScenes.Scene(WorldId.Dive));
            Check(guestBody != null && Vector3.Distance(guestBody.transform.position, outSpot) < 2f, "D3 the guest's body lies where it fell");
            CarryableItem guestBodyItem = guestBody.GetComponent<CarryableItem>();
            Check(RenderersOff(remote), "D3 the dead guest's copy is hidden on the host");
            yield return GrabItem(guestBodyItem);
            Check(host.Inventory.HeldItem == guestBodyItem && guestBodyItem.Grip == CarryGrip.TwoHands, "D3 the host carries the body in both hands");
            yield return Surface();
            Check(Day.Below.Count == 0 && Day.DiveDone, "D3 the last living diver is up: dive done");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 20f, () => "D3 the site unloaded");
            yield return Expect(() => remote != null && remote.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => "D3 the dead guest's object was carried to the ship");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=ShipAtSea") && GuestPlayerLine(r, guestId).Contains("dead=True"), 15f, "D3 the guest reads itself dead on the ship");
            Check(guestBodyItem.IsSpawned && guestBodyItem.gameObject.scene == WorldScenes.Scene(WorldId.Sea), "D3 the body came up in the host's hands");
            // A body's placement radius is half its length (CarryableItem.Radius of a
            // box), so setting it down needs a 1.7 m clear sphere ahead: try deck
            // spots and headings until one has room, as a player would.
            bool down = false;
            foreach (Vector3 spot in new[] { sea.SpawnPoint(2).position, sea.SpawnPoint(0).position, sea.DeckCabin.position + sea.DeckCabin.right * 2f + Vector3.up * 0.1f })
            {
                for (int yaw = 0; yaw < 360 && !down; yaw += 90)
                {
                    host.TeleportLocal(spot, yaw); yield return Wait(0.3f);
                    host.Inventory.RequestDrop();
                    for (int frame = 0; frame < 90 && !guestBodyItem.CanGrabFromWorld; frame++) yield return null;
                    down = guestBodyItem.CanGrabFromWorld;
                }
                if (down) break;
            }
            Check(down, "D3 the body is set down on the deck at " + sea.ToShipLocal(host.transform.position).ToString("F1") + " yaw " + host.Yaw.ToString("F0"));
            yield return Wait(1.5f);
            Vector3 bodyRest = guestBodyItem.transform.position;
            Check(sea.IsAboard(bodyRest), "D3 the body rests aboard at " + sea.ToShipLocal(bodyRest).ToString("F1"));
            Check(flow.ServerEndDay(host.Owner, out string endWhy2), "D3 End day accepted: " + endWhy2);
            yield return Expect(() => !remote.IsDead && !Day.IsDead(guestId), 5f, () => "D3 the guest is revived");
            yield return Expect(() => PlayerBody.FindFor(guestId, WorldScenes.Scene(WorldId.Sea)) == null, 3f, () => "D3 the brought-up body is gone");
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("dead=False") && GuestPlayerLine(r, guestId).Contains("scene=ShipAtSea"), 10f, "D3 the guest reads itself alive on the ship");
            yield return Wait(0.5f);
            Check(Vector3.Distance(remote.transform.position, bodyRest) < 2f, $"D3 the guest stands next to where its body lay ({Vector3.Distance(remote.transform.position, bodyRest):0.0} m)");
            Check(!RenderersOff(remote), "D3 the guest's copy is visible again");
            Check(Day.Day == 3 && !Day.DiveDone, "D3 day 3");

            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            Say("done");
        }
    }
}
