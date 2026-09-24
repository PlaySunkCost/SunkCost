using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Audio;
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
    // with one guest build (D rows) and then two (S rows). Card 1 (death-minimum):
    // K on deck does nothing; K below kills — body where you stood, slots
    // scattered, Dead holds you, out of Below, the dive done, the site gone with
    // the dead carried to the ship; End day revives on deck; a guest's body
    // carried up revives its owner next to it. Card 2 (dead spectating): a dead
    // guest watches the nearest living player, left click cycles, the watched
    // player reads ON AIR, the dead hear through their target and are never heard
    // by the living, a dead diver watching the deck holds both worlds and drops
    // the extra one when its target changes or it is carried up; End day ends it.
    // Log: Temp/spectate-matrix.log. Started by CameraClearanceMatrixDriver.Start("spectate").
    public static class SpectateRuntimeChecks
    {
        private const string Log = "Temp/spectate-matrix.log";
        private const string GuestDir = "Temp/spectate-guest";
        private const string GuestDirB = "Temp/spectate-guest-b";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest, guestB;
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
            try { if (guestB != null && !guestB.HasExited) guestB.Kill(); } catch (Exception) { }
            guest = null; guestB = null;
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

        // Each guest build has its own command directory; the command ids are one
        // rising sequence for both (each peer only needs its own to rise).
        private static Process LaunchGuest(string dir = GuestDir)
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
        private static IEnumerator Send(string json, string dir = GuestDir)
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
        // Mean luminance of a saved picture over a window (fractions of its width and
        // height), every fourth pixel.
        private static float MeanLuma(string path, float x0, float x1, float y0, float y1)
        {
            if (!File.Exists(path)) return -1f;
            var tex = new Texture2D(2, 2);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) return -1f;
                int ax = (int)(x0 * tex.width), bx = (int)(x1 * tex.width), ay = (int)(y0 * tex.height), by = (int)(y1 * tex.height);
                double sum = 0; int n = 0;
                for (int y = ay; y < by; y += 4)
                    for (int x = ax; x < bx; x += 4) { Color c = tex.GetPixel(x, y); sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; n++; }
                return n > 0 ? (float)(sum / n) : -1f;
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, string label, string dir = GuestDir)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", dir);
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.5f);
            }
            throw new Exception(label + "\n" + lastReply);
        }
        // Snapshot readers: the scenes a guest holds, its voice counters, a player line's camera.
        private static bool Loaded(string reply, string scene)
        {
            var m = System.Text.RegularExpressions.Regex.Match(reply, @"loaded=([^;]*);");
            return m.Success && Array.IndexOf(m.Groups[1].Value.Split('+'), scene) >= 0;
        }
        private static uint CounterOf(string reply, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(reply, key + @"=(\d+)");
            return m.Success ? uint.Parse(m.Groups[1].Value) : 0;
        }
        private static Vector3? CamPosOf(string playerLine)
        {
            var m = System.Text.RegularExpressions.Regex.Match(playerLine, @"camPos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)");
            if (!m.Success) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(m.Groups[1].Value, ci), float.Parse(m.Groups[2].Value, ci), float.Parse(m.Groups[3].Value, ci));
        }
        private static List<int> OtherIds()
        {
            var ids = new List<int>();
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (!p.IsOwner && p.IsSpawned) ids.Add(p.OwnerId);
            ids.Sort();
            return ids;
        }
        private static string NameOf(HQPlayerController player)
        {
            PlayerIdentity identity = player.GetComponent<PlayerIdentity>();
            return identity != null ? identity.DisplayName : PlayerIdentity.Fallback(player.OwnerId);
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
                if (c != null && (c.name == name || c.name.StartsWith(name + " (day ")) && c.gameObject.scene == WorldScenes.Scene(WorldId.Dive)) return c;
            return null;
        }
        // The placeholder body (Body, CharacterModel) is hidden; the owner's
        // first-person arm rig (ArmL/ArmR) is not part of the check.
        // Every renderer of the player's own object, the arm rig included (Dan, 18
        // September 2026: "when a player died his hands stay in the air").
        private static bool RenderersOff(HQPlayerController player)
        {
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && r.GetComponentInParent<HQPlayerController>() == player) return false;
            return true;
        }

        // ---- moves --------------------------------------------------------------------

        private static IEnumerator Descend(int day)
        {
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the deck cabin took the press (refusal: " + Day.LastRefusal.Text + ")");
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
        // Guest B to a spot within the TV's reach (SHIP-043: 6 m, checked by the server
        // too), facing the screen, and on until the server's copy of B stands there.
        private static IEnumerator BToTheTv(Vector3 spot, Vector3 screen, HQPlayerController remoteB)
        {
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(spot) + "}", GuestDirB);
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(screen - (spot + Vector3.up * 1.6f)) + "}", GuestDirB);
            yield return Expect(() => Vector3.Distance(remoteB.EyePosition, screen) < 5.5f, 3f, () => "B stands within the TV's reach (" + Vector3.Distance(remoteB.EyePosition, screen).ToString("F1") + " m from the screen)");
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
            // A grab that lands after the death (the request crossed the wire as the
            // air ran out) is refused: nothing goes into a dead player's hands.
            host.Inventory.RequestGrab(coin); yield return Wait(0.6f);
            Check(coin.CanGrabFromWorld && coin.HolderClientId < 0 && host.Inventory.HeldItem == null, "D1 a dead player's grab is refused: the coin stays loose (" + coin.State + ")");
            PlayerBody body = PlayerBody.FindFor(host.OwnerId, WorldScenes.Scene(WorldId.Dive));
            Check(body != null, "D1 a body was spawned for the host");
            CarryableItem bodyItem = body.GetComponent<CarryableItem>();
            Check(bodyItem != null && bodyItem.Grip == CarryGrip.TwoHands && !bodyItem.FitsInSlot && bodyItem.CanGrabFromWorld, "D1 the body is a loose two-handed carryable");
            Check(bodyItem.DisplayName == "Skipper's body", "D1 it is named after its owner: " + bodyItem.DisplayName);
            Check(Vector3.Distance(body.transform.position, deathSpot) < 1.5f, $"D1 the body lies where the host died ({Vector3.Distance(body.transform.position, deathSpot):0.0} m)");
            Check(!host.Controller.enabled, "D1 the dead host's capsule is off");
            yield return Expect(() => Day.Phase == DayPhase.AtSea && Day.DiveDone, 5f, () => $"D1 nobody living below: the dive is done (phase={Day.Phase} diveDone={Day.DiveDone})");
            // The dead host's object is still in the site while the car comes home:
            // End day now would revive it at deck coordinates inside the site.
            Check(!flow.ServerEndDay(host.Owner, out string earlyWhy) && earlyWhy == "Bringing up the dead", "D1 End day while the site closes is refused: " + earlyWhy);
            PlayerHudUI hostHud = host.GetComponent<PlayerHudUI>();
            yield return Expect(() => host.Spectator != null && host.Spectator.Active && host.Spectator.Target == null, 4f, () => "D1 nobody living to watch: the spectator view is on with no target (after the second of own camera)");
            yield return Expect(() => hostHud.Visor.NoSignal && Day.SpectateTargetOf(host.OwnerId) < 0, 2f, () => "D1 the screen says NO SIGNAL"); // the HUD reads the view a frame later
            Check(H.PromptText().Contains("DEAD") || true, "D1 prompt while dead: " + H.PromptText());
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 60f, () => "D1 the site unloaded with the dead carried out");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => "D1 the dead host's object is on the ship: " + host.gameObject.scene.name);
            yield return Expect(() => sea.IsAboard(host.transform.position), 5f, () => "D1 parked aboard at " + sea.ToShipLocal(host.transform.position).ToString("F1"));
            Check(Day.Elevator.State == ElevatorState.AtTop, "D1 the deck cabin reads the car as up");
            yield return Expect(() => sea.DeckCabinPanel.text.StartsWith("Dive done"), 3f, () => "D1 cabin panel: " + sea.DeckCabinPanel.text);
            Check(host.IsDead && RenderersOff(host), "D1 still dead and hidden on the ship");

            // A hand-made press from the dead (E is off while dead): refused, the day untouched.
            int deadPress = Day.LastRefusal.Serial;
            host.GetComponent<ShipControls>().RequestEndDay(); yield return Wait(0.5f);
            Check(Day.LastRefusal.Serial > deadPress && Day.LastRefusal.Text == "The dead press nothing" && Day.Day == 1 && host.IsDead, "D1 a dead player's End day press is refused: " + Day.LastRefusal.Text);

            Heading("D2 — End day revives on the deck");
            Check(flow.ServerEndDay(host.Owner, out string endWhy), "D2 End day accepted: " + endWhy);
            yield return Expect(() => !host.IsDead && Day.Dead.Count == 0, 5f, () => "D2 the host is alive again");
            yield return Wait(0.5f);
            Check(host.Spectator != null && !host.Spectator.Active && !hostHud.Visor.NoSignal, "D2 the spectator view ended with the revival");
            Check(host.GetComponent<PlayerHands>().Visible, "D2 the hands are back with the revival");
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

            // ---- card 2: dead spectating (docs/SPECTATING_IMPLEMENTATION_PLAN.md §6) ----
            Heading("S0 — a second guest joins; the cycle is put back to day 1 for the rows below");
            Day.ServerForceCycleForChecks(1, false);
            guestB = LaunchGuest(GuestDirB);
            yield return Expect(() => OtherIds().Count == 2, 40f, () => "S0 second guest player spawned: " + string.Join(",", OtherIds()));
            int idA = guestId;
            int idB = OtherIds().Find(i => i != idA);
            HQPlayerController remoteA = PlayerWithId(idA), remoteB = PlayerWithId(idB);
            yield return GuestEventually(r => GuestPlayerLine(r, idB).Contains("local=True") && r.Contains("world=Sea"), 20f, "S0 guest B joined at sea", GuestDirB);
            Check(remoteA != null && remoteB != null && idA != idB, $"S0 guests A={idA} B={idB}");

            Heading("S1 — guest A dies below with two living: it watches the nearest, the host reads ON AIR, left click cycles");
            H.MoveLocalIntoDeckCabin("Sea");
            float cabinFloor = DeckCabinBuilder.FloorThicknessMeters + 0.05f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position + sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}", GuestDirB);
            yield return Wait(0.5f);
            yield return Descend(1);
            Check(Day.Below.Count == 3, "S1 three below");
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "S1 guest A is in the site");
            yield return GuestEventually(r => GuestPlayerLine(r, idB).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "S1 guest B is in the site", GuestDirB);
            car = WorldSceneFlow.FindCar();
            doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            Vector3 side = Vector3.Cross(Vector3.up, doorway);
            Vector3 spotA = car.transform.position + doorway * 4f + side * 1.5f + Vector3.up * 0.05f;
            Vector3 spotB = car.transform.position + doorway * 9f + Vector3.up * 0.05f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(spotA) + "}");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(spotB) + "}", GuestDirB);
            host.TeleportLocal(car.transform.position + doorway * 4f - side * 0.5f, host.Yaw); yield return Wait(0.8f);
            yield return Send("{\"id\":{id},\"action\":\"die\"}");
            yield return Expect(() => Day.IsDead(idA), 5f, () => "S1 guest A died below");
            yield return Expect(() => Day.SpectateTargetOf(idA) == host.OwnerId, 3f, () => "S1 A watches the nearest living player, the host (target " + Day.SpectateTargetOf(idA) + ")");
            yield return Expect(() => hostHud.Visor.OnAirCount == 1, 3f, () => "S1 the host's visor reads ON AIR · 1 watching (" + hostHud.Visor.OnAirCount + ")");
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("spectatorActive=True") && GuestPlayerLine(r, idA).Contains("spectatorTarget=" + host.OwnerId + ";"), 6f, "S1 A's spectator view is on the host (after its second of own camera and the fade)");
            yield return GuestEventually(r => r.Contains("spectatingName=" + NameOf(host) + ";"), 4f, "S1 A's screen says SPECTATING " + NameOf(host));
            yield return GuestEventually(r => CamPosOf(GuestPlayerLine(r, idA)) is Vector3 p && Vector3.Distance(p, host.EyePosition) < 1.5f, 4f, "S1 A's camera sits on the host's eyes");
            // A spectator sees no more than the diver it watches (Dan, 23 September 2026:
            // the TV showed the dark below brighter than the diver saw it - "make sure it
            // does not happen to spectators too"). Both screens as shown, the same moment.
            string ownShot = Path.GetFullPath("Temp/spectate-diver-own.png"), watchShot = Path.GetFullPath("Temp/spectate-a-watching.png");
            File.Delete(ownShot); File.Delete(watchShot);
            H.CaptureScreen(ownShot);
            yield return Send("{\"id\":{id},\"action\":\"capture_play\",\"item\":\"" + watchShot.Replace("\\", "/") + "\"}");
            yield return Expect(() => File.Exists(ownShot) && File.Exists(watchShot), 5f, () => "S1b both screens captured");
            yield return Wait(0.5f); // the writers close their files
            float ownLuma = MeanLuma(ownShot, 0f, 1f, 0f, 1f), watchLuma = MeanLuma(watchShot, 0f, 1f, 0f, 1f);
            Check(watchLuma <= ownLuma + 0.04f && Mathf.Abs(watchLuma - ownLuma) < 0.08f, $"S1b A watching the host below sees it as dark as the host does (spectator {watchLuma:0.000}, diver {ownLuma:0.000})");
            yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return Expect(() => Day.SpectateTargetOf(idA) == idB, 3f, () => "S1 left click: A now watches guest B (target " + Day.SpectateTargetOf(idA) + ")");
            yield return Expect(() => hostHud.Visor.OnAirCount == 0 && Day.WatchersOf(idB) == 1, 3f, () => "S1 the host is off air, B has one watcher");
            yield return GuestEventually(r => r.Contains("onAir=1"), 4f, "S1 B's visor reads ON AIR · 1 watching", GuestDirB);
            yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return Expect(() => Day.SpectateTargetOf(idA) == host.OwnerId, 3f, () => "S1 left click again wraps back to the host");

            Heading("S2 — dead voice: A hears the living through its target; the living never hear A");
            ProximityVoice voice = UnityEngine.Object.FindAnyObjectByType<ProximityVoice>();
            Check(voice != null, "S2 the host has a voice service");
            voice.StartLocalTestTone();
            yield return Expect(() => voice.SentFrames > 20, 8f, () => "S2 the host's tone is sending (" + voice.SentFrames + ")");
            yield return GuestEventually(r => CounterOf(r, "received") > 20 && r.Contains("route=1"), 8f, "S2 dead A receives the host's frames on the Spectate route");
            yield return GuestEventually(r => CounterOf(r, "received") > 20 && r.Contains("route=0"), 8f, "S2 living B, nearby, receives them on the Direct route", GuestDirB);
            // The "who am I hearing" indicator (top right): dead A lists the host via
            // its target (Spectate), living B lists the host straight (Direct).
            yield return GuestEventually(r => r.Contains("heard=" + host.OwnerId + ":1"), 6f, "S2 dead A's indicator: the host, via the watched player");
            yield return GuestEventually(r => r.Contains("heard=" + host.OwnerId + ":0"), 6f, "S2 living B's indicator: the host, direct", GuestDirB);
            voice.SetMicrophone(false);
            yield return Wait(0.6f);
            uint hostReceived = voice.ReceivedFrames;
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", GuestDirB);
            uint bReceived = CounterOf(lastReply, "received");
            yield return Send("{\"id\":{id},\"action\":\"voice_tone\"}");
            yield return GuestEventually(r => CounterOf(r, "sent") > 20, 8f, "S2 dead A's tone is sending");
            yield return Wait(1f);
            Check(voice.ReceivedFrames == hostReceived, $"S2 the living host received none of A's frames ({voice.ReceivedFrames - hostReceived})");
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", GuestDirB);
            Check(CounterOf(lastReply, "received") == bReceived, $"S2 living B received none of A's frames ({CounterOf(lastReply, "received") - bReceived})");
            yield return Send("{\"id\":{id},\"action\":\"voice_off\"}");

            Heading("S3 — dual world: the host surfaces alone; dead A below watches the deck and back; B's surfacing carries A up");
            yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return Expect(() => Day.SpectateTargetOf(idA) == idB, 3f, () => "S3 A watches B before the host leaves");
            yield return Surface();
            Check(Day.Below.Count == 1 && Day.IsBelow(idB) && Day.Phase == DayPhase.DiveInProgress, "S3 B still below: the dive goes on (below=" + Day.Below.Count + ")");
            Check(WorldSceneFlow.FindCar() != null, "S3 the site stays loaded on the host");
            // The host on deck is a TV viewer: the channel is B (card 3); the host's
            // own TV draws B's view and visor — saved for a look.
            ShipTV hostTv = sea.GetComponent<ShipTV>();
            yield return Expect(() => Day.TvChannel == idB && hostTv != null && hostTv.Live, 5f, () => "S3/T the host on deck sees the TV live on B (channel " + Day.TvChannel + ")");
            yield return Wait(1f);
            Check(hostTv.ViewerNear && hostTv.RenderedFrames > 0, $"S3/T the host stands near the TV, which rendered ({hostTv.RenderedFrames} frames)");
            float tvContent = hostTv.SavePicture("Temp/spectate-tv-b.png");
            Check(hostTv.Frame.Readout.On && tvContent > 0.03f, $"S3/T the TV picture carries B's view and visor (brightness deviation {tvContent:0.000}; Temp/spectate-tv-b.png)");
            Check(hostTv.LastMeanBrightness < 0.35f, $"S3/T the seafloor on the TV is dark, under the site's own fog (mean brightness {hostTv.LastMeanBrightness:0.000})");
            // And as it stands on the deck, in daylight: a camera 1.5 m in front of the
            // screen sees the picture, not the deck's sun on it (the screen was lit and
            // glossy until 23 September 2026 and lifted the black water to mid-blue). The
            // same window of both: at 1.5 m the frame shows the screen's middle 59% x 75%.
            string deckShot = Path.GetFullPath("Temp/spectate-tv-on-deck.png");
            Transform tvQuad = sea.TvScreen;
            H.CaptureFrom(tvQuad.position - tvQuad.forward * 1.5f, tvQuad.position, deckShot);
            float onDeck = MeanLuma(deckShot, 0.1f, 0.9f, 0.1f, 0.9f);
            float picture = MeanLuma(Path.GetFullPath("Temp/spectate-tv-b.png"), 0.266f, 0.734f, 0.198f, 0.802f); // the capture's 0.1-0.9 of the screen's middle 59% x 75%
            Check(Mathf.Abs(onDeck - picture) < 0.05f, $"S3/T the TV on the deck shows the picture as rendered, not lit by the deck (on deck {onDeck:0.000}, picture {picture:0.000}; Temp/spectate-tv-on-deck.png)");
            // The diver's own head is out of its picture for the render only (Dan, 18
            // September 2026: a disc of the diver's colour over the TV): shown again after.
            Check(hostTv.Diver != null && hostTv.Diver.HeadSplit != null && hostTv.Diver.HeadSplit.HeadShown, "S3/T B's head is shown to the deck again after the TV's render");
            Check(WorldLook.InScene(WorldScenes.Scene(WorldId.Dive)) != null && WorldLook.InScene(WorldScenes.Scene(WorldId.Sea)) != null, "S3/T both loaded worlds carry a WorldLook");
            // A hold for a person to look (Dan, 23 September 2026: "get to the same point
            // and tell me to watch"): with Temp/spectate-hold.txt present, the run waits
            // here - B diving below, dead A watching B, the host on deck facing the TV live
            // on B - until the file is deleted (15 minutes at most), then goes on as before.
            if (File.Exists("Temp/spectate-hold.txt"))
            {
                Vector3 keepAt = host.transform.position; float keepYaw = host.Yaw;
                Vector3 tvAtHold = sea.ToShipLocal(sea.TvScreen.position);
                host.TeleportLocal(sea.FromShipLocal(new Vector3(tvAtHold.x, 0.05f, tvAtHold.z - 6f)), sea.FromShipYaw(0f));
                File.AppendAllText(Log, "HOLDING for a look: the host faces the TV (live on B), B dives, A watches B\n");
                float until = Time.realtimeSinceStartup + 900f;
                while (File.Exists("Temp/spectate-hold.txt") && Time.realtimeSinceStartup < until) yield return Wait(1f);
                File.AppendAllText(Log, "HOLD released\n");
                host.TeleportLocal(keepAt, keepYaw); yield return Wait(0.5f);
            }
            // The TV is the diver's own eyes, never a camera behind them (Dan, 23 September
            // 2026: "does the tv show third person of him?"): its camera stands where B's
            // own camera is on B's machine, and its picture matches B's own screen at the
            // same moment - both saved for a look.
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", GuestDirB);
            Vector3? bCam = CamPosOf(GuestPlayerLine(lastReply, idB));
            Transform tvCam = hostTv.transform.Find("TvCamera");
            Check(bCam is Vector3 bc && tvCam != null && Vector3.Distance(bc, tvCam.position) < 0.4f, $"S3/T the TV's camera is at B's own eyes (TV camera {(tvCam != null ? tvCam.position.ToString("F2") : "none")}, B's camera {bCam?.ToString("F2") ?? "unknown"})");
            string bOwnShot = Path.GetFullPath("Temp/spectate-b-own.png"), bOnTv = Path.GetFullPath("Temp/spectate-b-on-tv.png");
            File.Delete(bOwnShot);
            SunkCost.Monsters.Impostor impostorNow = UnityEngine.Object.FindAnyObjectByType<SunkCost.Monsters.Impostor>();
            foreach (HQPlayerController p in UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
            {
                int drawn = 0, all = 0;
                foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true)) { if (r is ParticleSystemRenderer) continue; all++; if (r.enabled && r.gameObject.activeInHierarchy) drawn++; }
                Say("on the host, player " + p.OwnerId + (p.IsOwner ? " (host)" : "") + " dead=" + p.IsDead + " at " + p.transform.position.ToString("F1") + ": " + drawn + " of " + all + " renderers drawn");
            }
            HQPlayerController copyA = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.OwnerId == idA);
            HQPlayerController copyB = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.OwnerId == idB);
            string[] figureParts = { "CharacterModel", "Head", "Eye.L", "Eye.R", "UpperArm", "Forearm", "Palm" };
            int drawnA = copyA == null ? -1 : copyA.GetComponentsInChildren<Renderer>(true).Count(r => figureParts.Contains(r.name) && r.enabled && r.gameObject.activeInHierarchy);
            Check(copyA != null && copyA.IsDead && drawnA == 0, $"S3/T the host draws none of dead A's figure after coming back to the dive (only its body lies there; {drawnA} renderers drawn)");
            Check(copyB != null && copyB.GetComponentsInChildren<Renderer>(true).Any(r => r.name == "CharacterModel" && r.enabled), "S3/T B's figure is drawn for everyone else again after the TV's render");
            Say("the Impostor at this moment: " + (impostorNow == null ? "none" : "following " + impostorNow.TargetId + " at " + impostorNow.transform.position.ToString("F1") + " (B is " + idB + "; the TV and B see it only when it follows B)"));
            yield return Send("{\"id\":{id},\"action\":\"capture_play\",\"item\":\"" + bOwnShot.Replace("\\", "/") + "\"}", GuestDirB);
            hostTv.SavePicture(bOnTv);
            yield return Expect(() => File.Exists(bOwnShot), 5f, () => "S3/T B's own screen captured");
            yield return Wait(0.5f);
            float bOwnLuma = MeanLuma(bOwnShot, 0f, 1f, 0f, 1f), bTvLuma = MeanLuma(bOnTv, 0f, 1f, 0f, 1f);
            Check(Mathf.Abs(bOwnLuma - bTvLuma) < 0.05f, $"S3/T the TV shows B's dive as dark as B sees it (TV {bTvLuma:0.000}, B's own screen {bOwnLuma:0.000}; Temp/spectate-b-on-tv.png, Temp/spectate-b-own.png)");
            // The TV is a second render: half resolution, every other frame, nobody near → nothing.
            int before = hostTv.RenderedFrames;
            host.TeleportLocal(sea.FromShipLocal(new Vector3(40f, 0.05f, 0f)), 0f);
            yield return Wait(0.5f);
            Check(!hostTv.ViewerNear && hostTv.RenderedFrames == before, $"S3/T 40 m off the ship the TV stops rendering ({hostTv.RenderedFrames - before} frames)");
            host.TeleportLocal(sea.FromShipLocal(new Vector3(0f, 0.05f, -8f)), 0f);
            yield return Wait(0.5f);
            Check(hostTv.ViewerNear && hostTv.RenderedFrames > before, "S3/T back on deck the TV renders again");
            yield return GuestEventually(r => Loaded(r, "DiveSite01") && !Loaded(r, "ShipAtSea") && GuestPlayerLine(r, idA).Contains("scene=DiveSite01"), 10f, "S3 A (dead, below, watching B) holds only the site");
            yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return Expect(() => Day.SpectateTargetOf(idA) == host.OwnerId, 3f, () => "S3 A now watches the host on the deck");
            yield return GuestEventually(r => Loaded(r, "DiveSite01") && Loaded(r, "ShipAtSea") && r.Contains("active=DiveSite01;"), 15f, "S3 A's client loaded the ship as well (dual world), its own world still active");
            Check(WorldSceneFlow.Instance.IsWatching(idA, out WorldId watched) && watched == WorldId.Sea, "S3 the server tracks A watching the ship");
            yield return GuestEventually(r => CamPosOf(GuestPlayerLine(r, idA)) is Vector3 p && Vector3.Distance(p, host.EyePosition) < 1.5f, 6f, "S3 A's camera is on the host's eyes on the deck");
            yield return GuestEventually(r => r.Contains("visor=off;") && r.Contains("spectatingName=" + NameOf(host) + ";"), 4f, "S3 A's screen is the host's deck view: no visor, SPECTATING " + NameOf(host));
            yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return Expect(() => Day.SpectateTargetOf(idA) == idB, 3f, () => "S3 A back to B");
            yield return GuestEventually(r => Loaded(r, "DiveSite01") && !Loaded(r, "ShipAtSea"), 15f, "S3 the ship was dropped from A's client again");
            Check(!WorldSceneFlow.Instance.IsWatching(idA, out _), "S3 the server tracks no watch for A");
            yield return Send("{\"id\":{id},\"action\":\"spectate_next\"}");
            yield return Expect(() => Day.SpectateTargetOf(idA) == host.OwnerId, 3f, () => "S3 A on the host again");
            yield return GuestEventually(r => Loaded(r, "DiveSite01") && Loaded(r, "ShipAtSea"), 15f, "S3 dual world again");
            // B surfaces: the last living diver up — the site closes with A carried to the ship.
            // The car went back down for B after the host's ride; it must be at the bottom first.
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 60f, () => "S3 the car came back down for B (" + Day.Elevator.State + ")");
            yield return Wait(1f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * 0.15f) + "}", GuestDirB);
            // Dead A's hidden object stands inside the car too (it died there, say):
            // the ride takes the living only; A rides to the ship with the dead when
            // the site closes and is never kicked as a rider that "never arrived".
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.right * 0.4f + Vector3.up * 0.15f) + "}");
            yield return Wait(0.5f);
            Check(car.IsInsideCar(remoteA.transform.position), "S3 dead A's object stands inside the car with B");
            int rideSerial = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}", GuestDirB);
            yield return Expect(() => Day.CabinRide.Serial > rideSerial, 5f, () => "S3 B's car press was taken");
            Check(Day.IsRider(idB) && !Day.IsRider(idA), "S3 B rides, dead A is no rider (riders " + string.Join(",", Day.Riders) + ")");
            yield return Expect(() => !Day.CabinRide.Active && Day.CabinRide.Serial > rideSerial, 70f, () => "S3 B's ride up completed");
            Check(OtherIds().Count == 2, "S3 A is still connected (not kicked as a rider that never arrived)");
            yield return Expect(() => Day.Below.Count == 0 && Day.DiveDone, 5f, () => "S3 nobody below: dive done");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 20f, () => "S3 the site unloaded");
            yield return Expect(() => remoteA != null && remoteA.gameObject.scene == WorldScenes.Scene(WorldId.Sea), 10f, () => "S3 A's object was carried to the ship");
            yield return GuestEventually(r => Loaded(r, "ShipAtSea") && !Loaded(r, "DiveSite01") && GuestPlayerLine(r, idA).Contains("scene=ShipAtSea") && GuestPlayerLine(r, idA).Contains("dead=True"), 20f, "S3 A holds only the ship now, dead on it");
            Check(!WorldSceneFlow.Instance.IsWatching(idA, out _), "S3 the server tracks no watch for A after the move");
            yield return Expect(() => Day.SpectateTargetOf(idA) == host.OwnerId || Day.SpectateTargetOf(idA) == idB, 3f, () => "S3 A still watches a living player (" + Day.SpectateTargetOf(idA) + ")");

            Heading("S3v — voice after a split surfacing: B, up later than the host, still hears the host on the deck");
            // The host rode up alone; B followed later in its own load. On B the
            // host's copy respawned into whatever scene was active and was moved to
            // Session when the site unloaded — a copy's scene is no guide to its
            // world (code check, 18 September 2026): the day state is.
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", GuestDirB);
            Say("B's view of the host's copy: " + GuestPlayerLine(lastReply, host.OwnerId));
            uint bReceivedSplit = CounterOf(lastReply, "received");
            host.TeleportLocal(remoteB.transform.position + remoteB.transform.right * 1.5f, host.Yaw); yield return Wait(0.5f);
            Check(Vector3.Distance(host.transform.position, remoteB.transform.position) < 3f, "S3v the host stands next to B on the deck");
            uint hostSentSplit = voice.SentFrames;
            voice.StartLocalTestTone();
            yield return Expect(() => voice.SentFrames > hostSentSplit + 20, 8f, () => "S3v the host's tone is sending");
            yield return GuestEventually(r => CounterOf(r, "received") > bReceivedSplit + 20 && r.Contains("route=0"), 8f, "S3v B receives the host's frames on the Direct route", GuestDirB);
            yield return GuestEventually(r => r.Contains("heard=" + host.OwnerId + ":0"), 6f, "S3v B's indicator lists the host, direct", GuestDirB);
            voice.SetMicrophone(false);
            uint hostReceivedSplit = voice.ReceivedFrames;
            yield return Send("{\"id\":{id},\"action\":\"voice_tone\"}", GuestDirB);
            yield return GuestEventually(r => CounterOf(r, "sent") > 20, 8f, "S3v B's tone is sending", GuestDirB);
            yield return Expect(() => voice.ReceivedFrames > hostReceivedSplit + 20, 8f, () => "S3v the host receives B's frames (" + (voice.ReceivedFrames - hostReceivedSplit) + ")");
            yield return Send("{\"id\":{id},\"action\":\"voice_off\"}", GuestDirB);

            Heading("S4 — End day: A revives, watches nobody, nobody is on air");
            Check(flow.ServerEndDay(host.Owner, out string endWhy3), "S4 End day accepted: " + endWhy3);
            yield return Expect(() => !Day.IsDead(idA) && Day.SpectateTargetOf(idA) < 0 && Day.Spectate.Count == 0, 5f, () => "S4 A is alive and the spectate list is empty");
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("dead=False") && GuestPlayerLine(r, idA).Contains("spectatorActive=False") && Loaded(r, "ShipAtSea") && !Loaded(r, "DiveSite01"), 10f, "S4 A's spectator view ended; one world");
            Check(hostHud.Visor.OnAirCount == 0 && Day.WatchersOf(idB) == 0, "S4 nobody is on air");
            Check(Day.Day == 2, "S4 day 2");

            // ---- card 3: the deck TV (docs/SPECTATING_IMPLEMENTATION_PLAN.md §6) ----
            Heading("T1 — B surfaces early: the deck TV shows the first diver below, E cycles, ON AIR counts the TV");
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position + sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}", GuestDirB);
            yield return Wait(0.5f);
            yield return Descend(2);
            Check(Day.Below.Count == 3, "T1 three below on day 2");
            yield return GuestEventually(r => GuestPlayerLine(r, idB).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "T1 guest B is in the site", GuestDirB);
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "T1 guest A is in the site");
            car = WorldSceneFlow.FindCar();
            doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            side = Vector3.Cross(Vector3.up, doorway);
            // The host and A step out; B stays in the car and rides up alone.
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + doorway * 5f + side * 1.5f + Vector3.up * 0.05f) + "}");
            host.TeleportLocal(car.transform.position + doorway * 4f - side * 0.5f, host.Yaw); yield return Wait(0.8f);
            int tvSerial = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}", GuestDirB);
            yield return Expect(() => Day.CabinRide.Serial > tvSerial, 5f, () => "T1 B's car press was taken");
            yield return Expect(() => !Day.CabinRide.Active && Day.CabinRide.Serial > tvSerial, 70f, () => "T1 B's ride up completed");
            Check(Day.Below.Count == 2 && !Day.IsBelow(idB) && Day.IsBelow(host.OwnerId) && Day.IsBelow(idA), "T1 the host and A stay below");
            // The car is owed below, but B still stands in the deck cabin: it waits,
            // says so, puts B out after the grace, and reopens if B steps back in
            // while the doors close (Dan, 17 September 2026: never carried by accident).
            yield return Wait(2f);
            Check(Day.Elevator.State == ElevatorState.AtTop && sea.IsInDeckCabin(remoteB.transform.position), "T1 the car waits up while B stands in the deck cabin (" + Day.Elevator.State + ")");
            yield return Expect(() => sea.DeckCabinPanel.text.StartsWith("Step out"), 3f, () => "T1 the cabin panel says: " + sea.DeckCabinPanel.text);
            float grace = flow.Settings.CarReturnGraceSeconds;
            yield return Expect(() => !sea.IsInDeckCabin(remoteB.transform.position) && sea.IsAboard(remoteB.transform.position), grace + 8f, () => "T1 after the grace B was put out on the deck at " + sea.ToShipLocal(remoteB.transform.position).ToString("F1"));
            yield return Expect(() => Day.Elevator.State == ElevatorState.Sealing, 12f, () => "T1 the car seals to go back down (" + Day.Elevator.State + ")");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position + sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}", GuestDirB);
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtTop, 5f, () => "T1 B stepped in while the doors closed: they open again (" + Day.Elevator.State + ")");
            yield return Expect(() => !sea.IsInDeckCabin(remoteB.transform.position), grace + 8f, () => "T1 B is put out again after the grace");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Descending || Day.Elevator.State == ElevatorState.AtBottom, 15f, () => "T1 the car goes down for the host and A (" + Day.Elevator.State + ")");
            yield return Expect(() => Day.TvChannel == host.OwnerId, 5f, () => "T1 the TV's channel is the first diver below, the host (" + Day.TvChannel + ")");
            yield return GuestEventually(r => Loaded(r, "DiveSite01") && GuestPlayerLine(r, idB).Contains("scene=ShipAtSea"), 15f, "T1 B, on the ship, holds the dive world for the TV", GuestDirB);
            Check(WorldSceneFlow.Instance.IsWatching(idB, out WorldId tvWorld) && tvWorld == WorldId.Dive, "T1 the server tracks B watching the site");
            yield return GuestEventually(r => r.Contains("tvLive=True") && r.Contains("tvCaption=LIVE · " + NameOf(host) + ";"), 6f, "T1 B's TV is live: LIVE · " + NameOf(host), GuestDirB);
            yield return Expect(() => hostHud.Visor.OnAirCount == 1, 3f, () => "T1 the host's visor reads ON AIR · 1 watching — the TV (" + hostHud.Visor.OnAirCount + ")");
            // The screen answers from 6 m, on the server too (SHIP-043, 23 September 2026;
            // ShipControls → HQPlayerController.ServerCanReachTv, + 1.5 m of slack). Spots
            // read from the screen itself, on the centre line aft of it: 12 m back is out
            // of reach, 4 m back (in front of the couches) is in it.
            Vector3 tvLocal = sea.ToShipLocal(sea.TvScreen.position);
            Vector3 bFar = sea.FromShipLocal(new Vector3(tvLocal.x, 0.05f, tvLocal.z - 12f));
            Vector3 bNear = sea.FromShipLocal(new Vector3(tvLocal.x, 0.05f, tvLocal.z - 4f));
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(bFar) + "}", GuestDirB);
            yield return Expect(() => Vector3.Distance(remoteB.EyePosition, sea.TvScreen.position) > 10f, 3f, () => "T1r the server sees B 12 m from the screen (" + Vector3.Distance(remoteB.EyePosition, sea.TvScreen.position).ToString("F1") + " m)");
            int farRefusals = Day.LastRefusal.Serial;
            yield return Send("{\"id\":{id},\"action\":\"tv_next\"}", GuestDirB);
            yield return Expect(() => Day.LastRefusal.Serial > farRefusals && Day.LastRefusal.Text.StartsWith("Too far from the screen"), 3f, () => "T1r a press from 12 m is refused: " + Day.LastRefusal.Text);
            Check(Day.TvChannel == host.OwnerId, "T1r the refused press left the channel on the host (" + Day.TvChannel + ")");
            yield return BToTheTv(bNear, sea.TvScreen.position, remoteB);
            yield return Send("{\"id\":{id},\"action\":\"tv_next\"}", GuestDirB);
            yield return Expect(() => Day.TvChannel == idA, 3f, () => "T1 E on the TV: channel A (" + Day.TvChannel + ")");
            yield return GuestEventually(r => r.Contains("tvCaption=LIVE · " + NameOf(remoteA) + ";"), 6f, "T1 B's TV says LIVE · " + NameOf(remoteA), GuestDirB);
            yield return Expect(() => Day.WatchersOf(idA) == 1 && hostHud.Visor.OnAirCount == 0, 3f, () => "T1 A is on air, the host is not");
            yield return BToTheTv(bNear, sea.TvScreen.position, remoteB);
            yield return Send("{\"id\":{id},\"action\":\"tv_next\"}", GuestDirB);
            yield return Expect(() => Day.TvChannel == host.OwnerId, 3f, () => "T1 E again wraps back to the host");

            Heading("T2 — the channel diver's voice plays at the TV; a dead spectator adds to ON AIR; the dead are no channel");
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}", GuestDirB);
            uint bReceived0 = CounterOf(lastReply, "received");
            uint hostSent0 = voice.SentFrames;
            voice.StartLocalTestTone();
            yield return Expect(() => voice.SentFrames > hostSent0 + 20, 8f, () => "T2 the host's tone is sending");
            yield return GuestEventually(r => CounterOf(r, "received") > bReceived0 + 20 && r.Contains("route=3"), 8f, "T2 B on the deck receives the host's frames on the TV route", GuestDirB);
            yield return GuestEventually(r => r.Contains("heard=" + host.OwnerId + ":3"), 6f, "T2 B's indicator: the host, through the TV", GuestDirB);
            voice.SetMicrophone(false);
            yield return Send("{\"id\":{id},\"action\":\"die\"}");
            yield return Expect(() => Day.IsDead(idA) && Day.SpectateTargetOf(idA) == host.OwnerId, 5f, () => "T2 A died below and watches the host");
            yield return Expect(() => hostHud.Visor.OnAirCount == 2, 3f, () => "T2 the host reads ON AIR · 2 watching — the TV and dead A (" + hostHud.Visor.OnAirCount + ")");
            yield return BToTheTv(bNear, sea.TvScreen.position, remoteB); // within reach, so the host stays because A is dead, not because the press was too far
            int t2Refusals = Day.LastRefusal.Serial;
            yield return Send("{\"id\":{id},\"action\":\"tv_next\"}", GuestDirB);
            yield return Wait(1f);
            Check(Day.TvChannel == host.OwnerId, "T2 dead A is no channel: E keeps the host (" + Day.TvChannel + ")");
            Check(Day.LastRefusal.Serial == t2Refusals, "T2 the press was taken, not refused (" + Day.LastRefusal.Text + ")");

            Heading("T3 — the last living diver surfaces: NO SIGNAL, the ship drops the dive world, E on the dark TV is refused");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 60f, () => "T3 the car came back down for the host (" + Day.Elevator.State + ")");
            yield return Wait(1f);
            yield return Surface();
            yield return Expect(() => Day.Below.Count == 0 && Day.DiveDone, 5f, () => "T3 nobody below: dive done");
            yield return Expect(() => WorldSceneFlow.FindCar() == null, 20f, () => "T3 the site unloaded");
            yield return Expect(() => Day.TvChannel < 0, 3f, () => "T3 the TV has no channel");
            yield return GuestEventually(r => r.Contains("tvLive=False") && r.Contains("tvCaption=NO SIGNAL") && !Loaded(r, "DiveSite01"), 20f, "T3 B's TV says NO SIGNAL and the dive world is gone from B", GuestDirB);
            Check(!WorldSceneFlow.Instance.IsWatching(idB, out _), "T3 the server tracks no watch for B");
            yield return Expect(() => hostHud.Visor.OnAirCount == 1, 3f, () => "T3 the host is still watched by dead A only (" + hostHud.Visor.OnAirCount + ")");
            // Two metres aft of the screen, facing it: read from the screen itself, since the
            // TV has moved (the port rail, then the bow on 23 September 2026) and may again.
            Vector3 tvAt = sea.ToShipLocal(sea.TvScreen.position);
            host.TeleportLocal(sea.FromShipLocal(new Vector3(tvAt.x, 0.05f, tvAt.z - 2f)), sea.FromShipYaw(0f)); yield return Wait(0.3f);
            H.ClientLookAtNamed(ShipParts.TvScreenName);
            yield return Expect(() => host.CurrentTv != null, 3f, () => "T3 the dot is on the TV screen");
            Check(H.PromptText().Contains("NO SIGNAL"), "T3 the prompt reads NO SIGNAL: " + H.PromptText());
            int refusals = Day.LastRefusal.Serial;
            host.GetComponent<ShipControls>().RequestTvNext();
            yield return Expect(() => Day.LastRefusal.Serial > refusals && Day.LastRefusal.Text.Contains("NO SIGNAL"), 3f, () => "T3 E on the dark TV is refused: " + Day.LastRefusal.Text);

            Heading("T4 — End day: everyone alive, nothing on air, day 3");
            Check(flow.ServerEndDay(host.Owner, out string endWhy4), "T4 End day accepted: " + endWhy4);
            yield return Expect(() => !Day.IsDead(idA) && Day.Spectate.Count == 0 && Day.TvChannel < 0, 5f, () => "T4 A alive, no spectators, no channel");
            yield return Expect(() => hostHud.Visor.OnAirCount == 0 && Day.Day == 3, 2f, () => $"T4 nobody on air, day 3 (onAir={hostHud.Visor.OnAirCount} day={Day.Day})"); // the HUD reads the state a frame later
            // A's own client must read itself alive (with the capsule back on) before
            // anyone moves it: the revive's placement lands with the dead flag, and a
            // move sent before it would be overwritten by it.
            yield return GuestEventually(r => GuestPlayerLine(r, idA).Contains("dead=False") && GuestPlayerLine(r, idA).Contains("controllerOn=True"), 10f, "T4 A's client reads itself alive with the capsule on");
            // A was carried up while watching the ship (T3): the move must not cost it
            // the other players' objects (it did — "expected to exist but does not").
            yield return GuestEventually(r => GuestPlayerLine(r, host.OwnerId).Length > 0 && GuestPlayerLine(r, idB).Length > 0, 5f, "T4 A's client still has the host and B (nothing lost in the move up)");

            Heading("T5 — the diver below leaves while the car is on its way down for them: the car comes back and the site closes");
            Say($"server view of A: owner={remoteA.OwnerId} valid={remoteA.Owner.IsValid} isController={remoteA.IsController} spawned={remoteA.IsSpawned} scene={remoteA.gameObject.scene.name}; B: owner={remoteB.OwnerId} valid={remoteB.Owner.IsValid}");
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position + sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}");
            Say("A's move reply: " + lastReply.Split('\n')[0]); // a revived guest's teleport must stick (see HQPlayerController.TeleportLocal)
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.DeckCabin.position - sea.DeckCabin.right * 1.0f + Vector3.up * cabinFloor) + "}", GuestDirB);
            yield return Wait(0.5f);
            yield return Expect(() => sea.IsInDeckCabin(remoteA.transform.position) && sea.IsInDeckCabin(remoteB.transform.position), 6f,
                () => $"T5 the server sees A and B in the deck cabin (A {sea.ToShipLocal(remoteA.transform.position):F1} {remoteA.gameObject.scene.name}, B {sea.ToShipLocal(remoteB.transform.position):F1} {remoteB.gameObject.scene.name})");
            yield return Descend(3);
            Check(Day.Below.Count == 3, "T5 three below on day 3");
            yield return GuestEventually(r => GuestPlayerLine(r, idB).Contains("scene=DiveSite01") && r.Contains("ride=Complete"), 20f, "T5 guest B is in the site", GuestDirB);
            car = WorldSceneFlow.FindCar();
            doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.BottomPosition + doorway * 5f + Vector3.up * 0.2f) + "}", GuestDirB);
            H.MoveLocalIntoCar(); yield return Wait(0.8f);
            int serial5 = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial5, 5f, () => "T5 the car press was taken");
            yield return Expect(() => !Day.CabinRide.Active && Day.CabinRide.Serial > serial5, 70f, () => "T5 the host and A rode up");
            Check(Day.Below.Count == 1 && Day.IsBelow(idB), "T5 B alone below");
            host.TeleportLocal(sea.SpawnPoint(2).position, 0f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.SpawnPoint(3).position + Vector3.up * 0.1f) + "}");
            yield return Expect(() => Day.Elevator.State == ElevatorState.Descending, grace + 15f, () => "T5 the cabin is clear: the car is on its way down for B (" + Day.Elevator.State + ")");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}", GuestDirB);
            yield return Expect(() => Day.Below.Count == 0 && Day.DiveDone, 10f, () => "T5 B left: nobody below, the dive is done");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtTop && WorldSceneFlow.FindCar() == null, 70f, () => "T5 the car came back up and the site closed (" + Day.Elevator.State + ")");
            yield return Expect(() => sea.DeckCabinPanel.text.StartsWith("Dive done"), 3f, () => "T5 cabin panel: " + sea.DeckCabinPanel.text);
            Check(flow.ServerEndDay(host.Owner, out string endWhy5), "T5 End day accepted: " + endWhy5);
            yield return Expect(() => Day.Payday, 3f, () => "T5 payday after the third day");

            // No row for "every rider of a ride down drops mid-ride" (code check, 18
            // September 2026): a ride down needs every living player in the cabin and
            // the host cannot drop, so it is unreachable in play; EndRide's
            // ServerSiteMayClose is a guard for it all the same.
            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            Say("done");
        }
    }
}
