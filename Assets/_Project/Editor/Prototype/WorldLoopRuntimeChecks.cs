using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The Local rows of docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 9.2 that the
    // scene-flow card owns (S1-S5, S13 with the debug sail, S15). The editor is
    // the host; one headless guest is a Local development build launched with
    // -hq-auto-join-local and driven through InventoryVerificationPeer's command
    // directory. Every assertion reads both peers: the host through the hooks,
    // the guest through its snapshot reply.
    public static class WorldLoopRuntimeChecks
    {
        private const string Log = "Temp/world-loop-matrix.log";
        private const string GuestDir = "Temp/world-loop-guest";
        private const string SecondGuestDir = "Temp/world-loop-guest2";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        // Nested waits (yield return WaitUntil(...)) are run by this stack; there is
        // no Unity coroutine scheduler behind EditorApplication.update.
        private static readonly System.Collections.Generic.Stack<IEnumerator> stack = new();
        private static double next;
        private static int guestId = 500;
        private static Process guest;
        private static Process secondGuest;
        public static string Status { get; private set; } = "Not run";

        [MenuItem("Sunk Cost/Prototype/Run world loop matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("World loop matrix running; result in " + Log + " and WorldLoopRuntimeChecks.Status."); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        // Editor hosting (Play Mode, Local host started). The guest build is launched
        // by the run itself.
        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first (Sunk Cost/Prototype/Build Windows Local Development).");
            File.WriteAllText(Log, "World loop matrix started " + DateTime.Now + "\n");
            Status = "Running";
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            next = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        public static void Abort()
        {
            KillGuests();
            if (steps == null) return;
            steps = null;
            EditorApplication.update -= Tick;
            Status = "Aborted";
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.5;
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode stopped");
                if (Step()) return;
                Status = "MATRIX_PASS";
            }
            catch (Exception e) { Status = "FAIL: " + e.Message + "\n" + e.StackTrace; }
            File.AppendAllText(Log, Status + "\n" + H.FlowStatus() + "\n" + H.ScenesText() + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("World loop matrix: MATRIX_PASS (" + Log + ")"); else Debug.LogError("World loop matrix: " + Status);
            KillGuests();
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        // One step of the innermost enumerator; a yielded enumerator is entered, a
        // finished one is left. Returns false when the whole run is done.
        private static bool Step()
        {
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            return false;
        }

        // ---- helpers ----------------------------------------------------------------

        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + H.ScenesText());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }

        private static Process LaunchGuest(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(dir, stale))) File.Delete(Path.Combine(dir, stale));
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-batchmode -nographics -hq-auto-join-local 127.0.0.1 -hq-inventory-test-dir \"" + Path.GetFullPath(dir) + "\" -logFile \"" + Path.GetFullPath(dir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return Process.Start(info);
        }

        private static void KillGuests()
        {
            foreach (Process p in new[] { guest, secondGuest })
            {
                try { if (p != null && !p.HasExited) p.Kill(); } catch (Exception) { }
            }
            guest = null;
            secondGuest = null;
        }

        // The guest polls the file every frame; a write can collide with its read.
        private static void Command(string dir, string json)
        {
            string text = json.Replace("{id}", (++guestId).ToString());
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(Path.Combine(dir, "command.json"), text); return; }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(15); }
            }
        }

        // Waits until the peer's reply carries the last command id, or throws.
        private static IEnumerator AwaitReply(string dir, float seconds = 8f)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            string reply = string.Empty;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                string path = Path.Combine(dir, "reply.txt");
                if (File.Exists(path))
                {
                    try { reply = File.ReadAllText(path); } catch (IOException) { reply = string.Empty; }
                    if (reply.Contains("id=" + guestId + ";")) { File.AppendAllText(Log, reply + "\n"); yield break; }
                }
                yield return null;
            }
            throw new Exception("guest in " + dir + " did not reply to command " + guestId + ": " + reply);
        }

        private static string Reply(string dir) => File.ReadAllText(Path.Combine(dir, "reply.txt"));

        private static IEnumerator Snapshot(string dir)
        {
            Command(dir, "{\"id\":{id},\"action\":\"snapshot\"}");
            yield return AwaitReply(dir);
        }

        // Observer changes reach a guest a tick or two after the server's move (an
        // object it stops observing is despawned and respawned when it enters the
        // new scene), so guest-side facts are polled: fresh snapshots until the
        // predicate holds, then a PASS, or a failure with the last snapshot.
        private static IEnumerator GuestEventually(string dir, Func<string, bool> predicate, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            string reply = string.Empty;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                yield return Snapshot(dir);
                reply = Reply(dir);
                if (predicate(reply)) { File.AppendAllText(Log, "PASS " + label + "\n"); yield break; }
            }
            throw new Exception(label + " (guest never agreed)\n" + reply + "\n" + H.FlowStatus() + "\n" + H.ScenesText());
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (condition()) yield break;
                yield return null;
            }
            throw new Exception("timeout: " + label + "\n" + H.FlowStatus());
        }

        private static string GuestLine(string reply, string prefix)
        {
            return reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        }

        // Spawned copies keep the prefab's "(Clone)" name on a client, so items
        // are matched by object id, which is the same on every peer.
        private static string GuestItem(string reply, int objectId)
        {
            return reply.Split('\n').FirstOrDefault(l => l.StartsWith("item=") && l.Contains("; id=" + objectId + ";")) ?? string.Empty;
        }

        private static Vector3 ParseVector(string text, string key)
        {
            int i = text.IndexOf(key + "=(", StringComparison.Ordinal);
            if (i < 0) return new Vector3(float.NaN, float.NaN, float.NaN);
            int end = text.IndexOf(')', i);
            string[] parts = text.Substring(i + key.Length + 2, end - i - key.Length - 2).Split(',');
            return new Vector3(float.Parse(parts[0].Trim()), float.Parse(parts[1].Trim()), float.Parse(parts[2].Trim()));
        }

        private static bool LoadedOnHost(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        private static HQPlayerController HostPlayer() => WorldSceneFlow.LocalPlayer();
        private static string Phase() => CrewDayState.Instance == null ? "none" : CrewDayState.Instance.Phase.ToString();
        private static DepartureStage Stage() => CrewDayState.Instance == null ? DepartureStage.Idle : CrewDayState.Instance.Departure.Stage;
        private static int GuestClientId(string reply)
        {
            string header = GuestLine(reply, "server=");
            int i = header.IndexOf("clientId=", StringComparison.Ordinal);
            return int.Parse(header.Substring(i + 9).Split(';')[0]);
        }
        private static int ItemsInScene(string sceneName) =>
            UnityEngine.Object.FindObjectsByType<CarryableItem>().Count(i => i.gameObject.scene.name == sceneName);

        // ---- the rows --------------------------------------------------------------

        private static IEnumerator Run()
        {
            SessionInputGate.OpenMenu();
            Check(HostPlayer() != null && HostPlayer().IsServerStarted, "editor is the host with a spawned player");

            // S1: guest joins at HQ.
            guest = LaunchGuest(GuestDir);
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().Length == 2, 40f, "guest player spawned");
            yield return Snapshot(GuestDir);
            string reply = Reply(GuestDir);
            int guestClient = GuestClientId(reply);
            Check(UnityEngine.Object.FindObjectsByType<HQPlayerController>().All(p => p.gameObject.scene.name == WorldScenes.HQName), "S1 host: both players in HQPrototype");
            Check(reply.Split('\n').Count(l => l.StartsWith("player=")) == 2 && reply.Split('\n').Where(l => l.StartsWith("player=")).All(l => l.Contains("scene=HQPrototype")), "S1 guest: both players in HQPrototype");
            Check(GuestLine(reply, "server=").Contains("loaded=HQPrototype+Session"), "S1 guest: loaded Session + HQPrototype only");
            Check(GuestLine(reply, "server=").Contains("phase=AtHQ"), "S1 guest: phase AtHQ");
            Check(UnityEngine.Object.FindObjectsByType<FishNet.Managing.NetworkManager>().Length == 1, "S1 host: one NetworkManager");
            Check(ItemsInScene(WorldScenes.HQName) == HQPrototypeLootSetup.SceneItemCount, "S1 host: fixture spawned in HQ (" + HQPrototypeLootSetup.SceneItemCount + ")");
            Check(reply.Split('\n').Count(l => l.StartsWith("item=")) == HQPrototypeLootSetup.SceneItemCount, "S1 guest: sees the fixture");

            // S2: refused while someone is off the ship, naming them, on every monitor.
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            Check(hqShip != null, "S2 HQ has a docked ship");
            Check(H.MonitorText().StartsWith("Docked at HQ"), "S2 monitor idle text: " + H.MonitorText());
            // The guest presses from ashore: only its own absence is reported.
            Command(GuestDir, "{\"id\":{id},\"action\":\"monitor\",\"item\":\"Sea\"}");
            yield return AwaitReply(GuestDir);
            yield return WaitUntil(() => H.MonitorText().StartsWith("Not aboard: " + WorldSceneFlow.DisplayName(guestClient)), 5f, "host monitor shows the guest's refusal");
            yield return GuestEventually(GuestDir, r => GuestLine(r, "server=").Contains("monitor=Not aboard: " + WorldSceneFlow.DisplayName(guestClient)), 5f, "S2 guest's monitor shows the same refusal");
            Vector3 deckSpot = hqShip.FromShipLocal(new Vector3(-2f, 0f, 2f));
            H.ClientMoveLocalPlayerTo(deckSpot); yield return null; yield return null; yield return null;
            // The host presses from the deck while the guest is ashore.
            H.ClientRequestSail("Sea");
            yield return WaitUntil(() => H.MonitorText() == "Not aboard: " + WorldSceneFlow.DisplayName(guestClient), 5f, "host monitor names only the guest");
            File.AppendAllText(Log, "PASS S2 refused, names only the guest: " + H.MonitorText() + "\n");
            yield return GuestEventually(GuestDir, r => GuestLine(r, "server=").Contains("monitor=Not aboard: " + WorldSceneFlow.DisplayName(guestClient)), 5f, "S2 guest's monitor names only itself");
            Check(Phase() == "AtHQ" && !LoadedOnHost(WorldScenes.SeaName), "S2 nothing moved");
            yield return WaitUntil(() => H.MonitorText().StartsWith("Docked at HQ"), 6f, "refusal cleared after refusalDisplaySeconds");
            string refusal;

            // D3: the gangway is not the deck. Host on the ramp, guest on the deck.
            Vector3 guestDeckEarly = hqShip.FromShipLocal(new Vector3(2.5f, 0f, 4f));
            Command(GuestDir, "{\"id\":{id},\"action\":\"move\",\"position\":{\"x\":" + guestDeckEarly.x + ",\"y\":" + guestDeckEarly.y + ",\"z\":" + guestDeckEarly.z + "}}");
            yield return AwaitReply(GuestDir);
            Vector3 rampSpot = hqShip.FromShipLocal(new Vector3(0f, 0f, -ShipStubBuilder.DeckLength / 2f - 2f));
            Check(hqShip.IsOnGangway(rampSpot) && !hqShip.IsSafelyAboard(rampSpot), "D3 the ramp spot is on the gangway and not safely aboard");
            H.ClientMoveLocalPlayerTo(rampSpot); yield return null; yield return null; yield return null;
            refusal = H.ServerSail("Sea");
            Check(refusal == "refused: Not aboard: " + WorldSceneFlow.DisplayName(HostPlayer().OwnerId), "D3 a passenger on the gangway is named: " + refusal);
            H.ClientMoveLocalPlayerTo(deckSpot); yield return null; yield return null; yield return null;
            CarryableItem rampBall = H.Item("Basketball (3)");
            rampBall.ServerDropAt(hqShip.FromShipLocal(new Vector3(0f, 0.3f, -ShipStubBuilder.DeckLength / 2f - 2f)));
            yield return null; yield return null;
            refusal = H.ServerSail("Sea");
            Check(refusal == "refused: Clear the gangway", "D3 cargo on the gangway refuses: " + refusal);
            rampBall.ServerDropAt(new Vector3(4f, 0.3f, -4f)); // back into the room, away from the other balls
            yield return null; yield return null;
            Check(Phase() == "AtHQ" && !LoadedOnHost(WorldScenes.SeaName) && H.ShipStatus("HQ").Contains("lowered=True"), "D3 nothing moved, gangway still down: " + H.ShipStatus("HQ"));

            // S3: a ball on the deck, a ball in the host's hand, everyone aboard, sail.
            CarryableItem deckBall = H.Item("Basketball (2)");
            deckBall.ServerDropAt(hqShip.FromShipLocal(new Vector3(2f, 0.5f, -3f)));
            yield return null; yield return null;
            Vector3 deckBallLocalBefore = hqShip.ToShipLocal(deckBall.transform.position);
            H.ClientMoveLocalPlayerToItem("Basketball"); H.ClientLookAtItem("Basketball"); yield return null;
            H.ClientRequestGrab("Basketball"); yield return null; yield return null;
            CarryableItem heldBall = H.Item("Basketball");
            Check(heldBall.State == ItemState.Held && heldBall.HolderClientId == HostPlayer().OwnerId, "S3 host holds Basketball");
            H.ClientMoveLocalPlayerTo(deckSpot); yield return null; yield return null;
            Vector3 guestDeck = hqShip.FromShipLocal(new Vector3(2.5f, 0f, 4f));
            Command(GuestDir, "{\"id\":{id},\"action\":\"move\",\"position\":{\"x\":" + guestDeck.x + ",\"y\":" + guestDeck.y + ",\"z\":" + guestDeck.z + "}}");
            yield return AwaitReply(GuestDir);
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().All(p => hqShip.IsAboard(p.transform.position)), 5f, "both aboard on the host");
            Vector3 hostLocalBefore = hqShip.ToShipLocal(HostPlayer().transform.position);
            HQPlayerController guestPlayer = UnityEngine.Object.FindObjectsByType<HQPlayerController>().First(p => !p.IsOwner);
            Vector3 guestLocalBefore = hqShip.ToShipLocal(guestPlayer.transform.position);
            int fadesBefore = ScreenFade.Instance.FadeOutCount;
            Quaternion deckBallRotationBefore = Quaternion.Inverse(hqShip.transform.rotation) * deckBall.transform.rotation;
            Vector3 hqShipRest = hqShip.transform.position;
            string sail = H.ClientRequestSail("Sea"); // the host presses the Site 01 button
            Check(sail == "requested Sea", "S3 monitor press: " + sail);
            yield return WaitUntil(() => Phase() == "Sailing" || Phase() == "AtSea", 5f, "sail accepted from the monitor"); // the matrix ticks every 0.5 s
            File.AppendAllText(Log, "PASS S3 sail accepted from the monitor, monitor reads: " + H.MonitorText() + "\n");
            // D1/D4/D7 during the trip: everyone locked, the ship visibly moving, the
            // fade not yet started, items refused, the deck ball riding along.
            yield return WaitUntil(() => Stage() == DepartureStage.PullingAway, 8f, "the ship pulls away");
            Check(HostPlayer().TravelLocked && !ScreenFade.Instance.IsBlack, "D1 host locked in place, screen still visible while the ship moves");
            Check(hqShip.GetComponent<ShipDepartureVisual>() != null && !hqShip.GetComponent<ShipDepartureVisual>().GangwayLowered, "D1 gangway raised before moving: " + H.ShipStatus("HQ"));
            yield return GuestEventually(GuestDir, r => GuestLine(r, "server=").Contains("travelLocked=True") && GuestLine(r, "server=").Contains("trip=PullingAway/"), 4f, "D1 guest locked and sees PullingAway");
            double untilMoved = EditorApplication.timeSinceStartup + 3.0;
            while (EditorApplication.timeSinceStartup < untilMoved && Stage() == DepartureStage.PullingAway) yield return null;
            float travelled = Vector3.Distance(hqShip.transform.position, hqShipRest);
            Check(travelled > 1f, "D1 the ship has moved away from the dock (" + travelled.ToString("0.0") + " m)");
            Check(Vector3.Distance(hqShip.ToShipLocal(HostPlayer().transform.position), hostLocalBefore) < 0.15f, "D1 host rides at the same deck spot");
            Check(Vector3.Distance(hqShip.ToShipLocal(deckBall.transform.position), deckBallLocalBefore) < 0.15f && deckBall.InTransit, "D7 deck ball rides frozen at its spot");
            H.ClientRequestGrab("HeavyBallBlue"); H.ClientRequestDrop(); yield return null; yield return null;
            Check(heldBall.State == ItemState.Held && H.Item("HeavyBallBlue").State == ItemState.Free, "D4 item requests during the trip change nothing");
            yield return WaitUntil(() => Phase() == "AtSea" && !WorldSceneFlow.Instance.Transitioning, 25f, "sail completes");
            Check(ScreenFade.Instance.FadeOutCount > fadesBefore, "S3 host faded for the sail");
            yield return WaitUntil(() => !HostPlayer().TravelLocked, 5f, "host unlocked after the fade-in");
            yield return WaitUntil(() => !LoadedOnHost(WorldScenes.HQName), 10f, "HQ unloaded on host");
            ShipParts seaShip = ShipParts.InWorld(WorldId.Sea);
            Check(seaShip != null, "S3 sea ship present");
            Check(HostPlayer().gameObject.scene.name == WorldScenes.SeaName, "S3 host player in ShipAtSea");
            Check(Vector3.Distance(seaShip.ToShipLocal(HostPlayer().transform.position), hostLocalBefore) < 0.15f, "S3 host at the same spot on the new deck");
            Check(heldBall.gameObject.scene.name == WorldScenes.SeaName && heldBall.State == ItemState.Held, "S3 held ball travelled with the host");
            Check(deckBall.gameObject.scene.name == WorldScenes.SeaName && Vector3.Distance(seaShip.ToShipLocal(deckBall.transform.position), deckBallLocalBefore) < 0.15f, "S3 deck ball on the sea deck at the same spot");
            Quaternion deckBallRotationAfter = Quaternion.Inverse(seaShip.transform.rotation) * deckBall.transform.rotation;
            Check(Quaternion.Angle(deckBallRotationBefore, deckBallRotationAfter) < 2f && !deckBall.InTransit, "D7 deck ball keeps its rotation and is released");
            Check(Vector3.Distance(seaShip.transform.position, seaShip.GetComponent<ShipDepartureVisual>().RestPosition) < 0.01f && !seaShip.GetComponent<ShipDepartureVisual>().GangwayLowered, "D1 sea ship at rest, gangway stowed: " + H.ShipStatus("Sea"));
            yield return WaitUntil(() => ScreenFade.Instance.IsClear, 5f, "host fade clears");
            int heldId = heldBall.ObjectId;
            yield return GuestEventually(GuestDir, r =>
                GuestLine(r, "server=").Contains("loaded=Session+ShipAtSea") &&
                GuestLine(r, "server=").Contains("phase=AtSea") && GuestLine(r, "server=").Contains("world=Sea") &&
                GuestLine(r, "server=").Contains("active=ShipAtSea") &&
                r.Split('\n').Count(l => l.StartsWith("player=")) == 2 &&
                r.Split('\n').Where(l => l.StartsWith("player=")).All(l => l.Contains("scene=ShipAtSea")) &&
                GuestItem(r, deckBall.ObjectId).Contains("scene=ShipAtSea") &&
                GuestItem(r, heldId).Contains("state=Held") && GuestItem(r, heldId).Contains("scene=ShipAtSea") &&
                !r.Contains("scene=HQPrototype"),
                8f, "S3 guest: Session + ShipAtSea only, phase AtSea, active ShipAtSea, both players and both balls in ShipAtSea, nothing left in HQ");
            reply = Reply(GuestDir);
            string guestSelf = reply.Split('\n').First(l => l.StartsWith("player=") && l.Contains("local=True"));
            Vector3 guestPos = ParseVector(guestSelf, "position");
            Check(Vector3.Distance(seaShip.ToShipLocal(guestPos), guestLocalBefore) < 0.15f, "S3 guest at the same spot on the new deck (" + seaShip.ToShipLocal(guestPos) + " vs " + guestLocalBefore + ")");

            // S4: a second guest joins at sea and spawns on deck.
            secondGuest = LaunchGuest(SecondGuestDir);
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().Length == 3, 40f, "second guest spawned");
            yield return GuestEventually(SecondGuestDir, r =>
                r.Split('\n').Count(l => l.StartsWith("player=")) == 3 &&
                GuestLine(r, "server=").Contains("loaded=Session+ShipAtSea") &&
                r.Split('\n').Any(l => l.StartsWith("player=") && l.Contains("local=True") && l.Contains("scene=ShipAtSea") && seaShip.IsAboard(ParseVector(l, "position") + Vector3.up * 0.5f)),
                8f, "S4 joiner spawned on the sea deck, loaded Session + ShipAtSea only, sees three players");
            // A clean leave: an abruptly killed client is only noticed after the
            // transport timeout, which is longer than this row waits.
            Command(SecondGuestDir, "{\"id\":{id},\"action\":\"leave\"}");
            yield return WaitUntil(() => UnityEngine.Object.FindObjectsByType<HQPlayerController>().Length == 2, 20f, "second guest gone");
            secondGuest.Kill(); secondGuest = null;

            // S5: a join during a day is refused at admission; the crew is untouched.
            Check(CrewDayState.Instance.ServerBeginDay(out string dayWhy), "S5 day begun: " + dayWhy);
            yield return null; yield return null;
            refusal = H.ServerSail("HQ");
            Check(refusal == "refused: Dive in progress.", "S5 sail refused during the day: " + refusal);
            secondGuest = LaunchGuest(SecondGuestDir);
            Command(SecondGuestDir, "{\"id\":{id},\"action\":\"snapshot\"}");
            yield return AwaitReply(SecondGuestDir, 40f); // the build boots and binds its transport
            yield return GuestEventually(SecondGuestDir, r =>
                GuestLine(r, "server=").Contains("client=False") && r.Contains("message=Dive in progress") &&
                !r.Contains("player=") && !GuestLine(r, "server=").Contains("ShipAtSea"),
                20f, "S5 joiner refused with the dive-in-progress message, never in the world");
            Check(UnityEngine.Object.FindObjectsByType<HQPlayerController>().Length == 2 && Phase() == "DiveInProgress" &&
                HostPlayer().gameObject.scene.name == WorldScenes.SeaName, "S5 host unaffected: two players, still at sea, day in progress");
            yield return GuestEventually(GuestDir, r =>
                GuestLine(r, "server=").Contains("phase=DiveInProgress") && r.Split('\n').Count(l => l.StartsWith("player=")) == 2,
                8f, "S5 first guest unaffected: sees the day in progress and two players");
            secondGuest.Kill(); secondGuest = null;
            Check(CrewDayState.Instance.ServerEndDayIfDone(3), "S5 the dive is done with nobody below");
            yield return WaitUntil(() => Phase() == "AtSea", 5f, "dive done");
            Check(CrewDayState.Instance.Day == 1 && CrewDayState.Instance.DiveDone, "S12 the day waits for the crew: still day 1, dive done");
            Check(CrewDayState.Instance.ServerEndDay(3, out string endWhy), "S12 End day: " + endWhy);
            Check(CrewDayState.Instance.Day == 2 && !CrewDayState.Instance.DiveDone, "S12 the day counter advanced to 2: " + CrewDayState.Instance.Day);

            // S13 (scene part): the guest presses HQ; everything comes back, HQ is fresh.
            yield return WaitUntil(() => H.MonitorText().StartsWith("Day 2 of 3"), 5f, "monitor back to the at-sea line after the day ended: " + H.MonitorText());
            File.AppendAllText(Log, "PASS S13 monitor at sea: " + H.MonitorText() + "\n");
            Command(GuestDir, "{\"id\":{id},\"action\":\"monitor\",\"item\":\"HQ\"}");
            yield return AwaitReply(GuestDir);
            yield return WaitUntil(() => Phase() == "SailingHome" || Phase() == "AtHQ", 5f, "sail home accepted from the guest's press");
            File.AppendAllText(Log, "PASS S13 sail home accepted from the guest's monitor press\n");
            yield return WaitUntil(() => Stage() == DepartureStage.Arriving, 25f, "arriving at HQ");
            Check(HostPlayer().gameObject.scene.name == WorldScenes.HQName && HostPlayer().TravelLocked, "D2 still locked while the gangway lowers");
            yield return WaitUntil(() => !HostPlayer().TravelLocked, 8f, "unlocked at HQ");
            Check(H.ShipStatus("HQ").Contains("lowered=True") && H.ShipStatus("HQ").Contains("rampCollider=True"), "D2 gangway down and walkable before the unlock: " + H.ShipStatus("HQ"));
            yield return WaitUntil(() => Phase() == "AtHQ" && !WorldSceneFlow.Instance.Transitioning, 20f, "sail home completes");
            yield return WaitUntil(() => !LoadedOnHost(WorldScenes.SeaName), 10f, "sea unloaded on host");
            hqShip = ShipParts.InWorld(WorldId.HQ);
            Check(hqShip != null && HostPlayer().gameObject.scene.name == WorldScenes.HQName, "S13 host back in HQ");
            Check(Vector3.Distance(hqShip.ToShipLocal(HostPlayer().transform.position), hostLocalBefore) < 0.15f, "S13 host at the same deck spot at HQ");
            // The fresh HQ fixture reuses the names, so the travelled balls are held by reference.
            Check(heldBall.State == ItemState.Held && heldBall.gameObject.scene.name == WorldScenes.HQName, "S13 held ball came home");
            Check(deckBall.gameObject.scene.name == WorldScenes.HQName, "S13 deck ball came home");
            Check(ItemsInScene(WorldScenes.HQName) == HQPrototypeLootSetup.SceneItemCount + 2, "S13 HQ fixture respawned fresh plus the two balls that travelled");
            yield return GuestEventually(GuestDir, r =>
                GuestLine(r, "server=").Contains("loaded=HQPrototype+Session") && GuestLine(r, "server=").Contains("phase=AtHQ") &&
                r.Split('\n').Where(l => l.StartsWith("player=")).All(l => l.Contains("scene=HQPrototype")) &&
                r.Split('\n').Count(l => l.StartsWith("item=")) == HQPrototypeLootSetup.SceneItemCount + 2,
                8f, "S13 guest back at HQ only, both players in HQ, sees the fresh fixture plus the two balls that travelled");

            // S15: host leaves; every world scene is gone; re-host; fresh HQ.
            H.ClientRequestDrop(); yield return null;
            UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>().LeaveSession();
            yield return WaitUntil(() => !FishNet.InstanceFinder.IsServerStarted && !FishNet.InstanceFinder.IsClientStarted, 15f, "host left");
            yield return WaitUntil(() => !LoadedOnHost(WorldScenes.HQName) && !LoadedOnHost(WorldScenes.SeaName), 10f, "world scenes unloaded after leave");
            Check(SceneManager.GetActiveScene().name == WorldScenes.SessionName, "S15 back on Session");
            Check(CrewDayState.Instance == null, "S15 day state gone");
            guest.Kill(); guest = null; // its connection is already gone with the host
            // The UDP port is released a moment after the server stops; Tugboat fails
            // to bind if the host is restarted immediately, so retry a few times.
            bool hosted = false;
            for (int attempt = 0; attempt < 4 && !hosted; attempt++)
            {
                double until = EditorApplication.timeSinceStartup + 2.0;
                while (EditorApplication.timeSinceStartup < until) yield return null;
                UnityEngine.Object.FindAnyObjectByType<PrototypeSessionUI>().StartLocalHost();
                double deadline = EditorApplication.timeSinceStartup + 12.0;
                while (EditorApplication.timeSinceStartup < deadline)
                {
                    if (HostPlayer() != null && HostPlayer().IsServerStarted) { hosted = true; break; }
                    yield return null;
                }
                if (!hosted) File.AppendAllText(Log, "re-host attempt " + (attempt + 1) + " did not come up: " + H.SessionUiState() + "\n");
            }
            Check(hosted, "S15 re-hosted");
            yield return WaitUntil(() => ItemsInScene(WorldScenes.HQName) == HQPrototypeLootSetup.SceneItemCount, 10f, "fixture fresh after re-host");
            Check(UnityEngine.Object.FindObjectsByType<CrewDayState>().Length == 1 && Phase() == "AtHQ", "S15 one day state, AtHQ");
            Check(HostPlayer().gameObject.scene.name == WorldScenes.HQName, "S15 host spawned in HQ");
        }
    }
}
