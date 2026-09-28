using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    // The ship's navigation console (the shared console, Dan, 27 September 2026;
    // scratchpad console/BRIEF.md "TESTING REQUIREMENTS — SHIP", TESTPLAN.md §3.1).
    // Host-only rows first (H): the console on the docked ship, choosing HQ / an open
    // site / a locked site with the real aim and E, the not-aboard refusal, the
    // unlock transaction for every site in a non-sequential order (short, exact,
    // already open, a same-frame double charge), the selection changed under the
    // pull, the sail and its screens, SITE 03's routing to Site 01's world with its
    // identity kept, the dive-in-progress and END DAY states, the locked-site
    // UNLOCK override after a dive, divers below, payday, the plank relocking the
    // sites. Then one guest (G): replication of the selection, the lock, the sign
    // and the screens, the guest changing the selection, WAITING FOR <guest> TO
    // BOARD, the guest's pull sailing the ship and its lever swinging on the guest,
    // the unlock and balance replicating, a same-frame host+guest pull charging
    // once, the guest's change refusing the host's stale pull, a late joiner's first
    // snapshot, the old server sail as the compat path. Every row asserts the
    // replicated state (CrewDayState through the hooks, the guest's snapshot) and
    // the visible text (the composer's screens and sign, the compat Text).
    // Log: Temp/console-ship-matrix.log. Started by CameraClearanceMatrixDriver.Start("console-ship").
    public static class ShipConsoleRuntimeChecks
    {
        private const string Log = "Temp/console-ship-matrix.log";
        private const string GuestDir = "Temp/console-ship-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Captures = "Temp/look/";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 2600;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run console-ship matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Console-ship matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Console-ship matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Console-ship matrix: MATRIX_PASS"); else Debug.LogError("Console-ship matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            WorldLoopSettings.QuotaOverrideForTests = null;
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
            if (!value) throw new Exception(label + "\n" + H.FlowStatus() + "\n" + H.ConsoleStatus() + "\n" + H.ShipScreenText() + "\nText=" + H.MonitorText() + "\nlever=" + H.LeverPullText());
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
        // The guest polls the file every frame; a write can collide with its read.
        private static void Command(string json)
        {
            string text = json.Replace("{id}", (++guestCommand).ToString());
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(Path.Combine(GuestDir, "command.json"), text); return; }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(15); }
            }
        }
        private static IEnumerator AwaitReply(float seconds = 10f)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                string reply = Reply();
                if (reply.StartsWith("id=" + guestCommand + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest did not answer command " + guestCommand + "\n" + Reply());
        }
        private static IEnumerator Send(string json)
        {
            Command(json);
            yield return AwaitReply();
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
            throw new Exception(label + "\n" + lastReply + "\n" + H.FlowStatus() + "\n" + H.ConsoleStatus());
        }
        private static string GuestLine(string reply, string prefix) => reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        private static string GuestConsole(string reply) => GuestLine(reply, "selected=");
        private static string GuestHeader(string reply) => GuestLine(reply, "server=");
        private static string GuestPlayerLine(string reply, int ownerId) => GuestLine(reply, "player=" + ownerId + ";");
        // The guest's ship top-screen dump alone ("topScreen=…" up to "; bottomScreen=").
        private static string GuestTop(string reply)
        {
            string line = GuestConsole(reply);
            int i = line.IndexOf("topScreen=", StringComparison.Ordinal), j = line.IndexOf("; bottomScreen=", StringComparison.Ordinal);
            return i < 0 || j < i ? string.Empty : line.Substring(i, j - i);
        }
        private static float GuestAngle(string reply)
        {
            Match m = Regex.Match(GuestConsole(reply), @"leverAngle=([0-9.\-]+);");
            return m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float a) ? a : float.NaN;
        }
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static IEnumerator GuestMove(Vector3 to) { yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }

        // ---- the console under test -----------------------------------------------------

        private static ShipNavigationConsole Composer() => ShipNavigationConsole.InWorld(Day != null ? Day.World : WorldId.HQ);
        private static string Top() => Composer() == null ? "(no console)" : ConsoleModels.Flatten(Composer().Top);
        private static string Bottom() => Composer() == null ? "(no console)" : ConsoleModels.Flatten(Composer().Bottom);
        private static string Sign() => Composer() == null ? "(no console)" : ConsoleModels.Flatten(Composer().Sign);
        private static string Text() => H.MonitorText();
        // The flags of a card on a top-screen dump: "[SITE 02*#]" → "*#".
        private static string CardFlags(string top, string name)
        {
            int i = top.IndexOf("[" + name, StringComparison.Ordinal);
            if (i < 0) return "(absent)";
            int end = top.IndexOf(']', i);
            return end < 0 ? "(absent)" : top.Substring(i + 1 + name.Length, end - i - 1 - name.Length);
        }
        private static bool CardIs(string name, bool selected, bool locked, bool here)
        {
            string f = CardFlags(Top(), name);
            return f != "(absent)" && f.Contains('*') == selected && f.Contains('#') == locked && f.Contains('@') == here;
        }
        private static bool GuestCardIs(string reply, string name, bool selected, bool locked, bool here)
        {
            string line = GuestConsole(reply);
            int i = line.IndexOf("topScreen=", StringComparison.Ordinal);
            if (i < 0) return false;
            string f = CardFlags(line.Substring(i), name);
            return f != "(absent)" && f.Contains('*') == selected && f.Contains('#') == locked && f.Contains('@') == here;
        }
        private static bool NoticeShown() => Composer() != null && Composer().Bottom != null && Composer().Bottom.Notice;

        private static IEnumerator AtConsole()
        {
            string moved = H.ClientMoveLocalPlayerToShipConsole();
            Check(moved.StartsWith("moved"), "stood at the ship console: " + moved);
            yield return null; yield return null; yield return null;
        }
        // Look at a control of THIS world's ship until the dot resolves it.
        private static IEnumerator Aim(string controlName, ConsoleControlKind kind, SiteId payload)
        {
            HQPlayerController host = Host();
            float deadline = Time.unscaledTime + 4f; int tries = 0;
            while (Time.unscaledTime < deadline)
            {
                ConsoleControl c = host.CurrentConsoleControl;
                if (c != null && c.Kind == kind && c.Payload == payload && c.Console == ConsoleKind.Ship) break;
                if (tries++ % 45 == 0) H.ClientMoveLocalPlayerToShipConsole();
                H.ClientLookAtShipControl(controlName);
                yield return null;
            }
            ConsoleControl aimed = host.CurrentConsoleControl;
            Check(aimed != null && aimed.Kind == kind && aimed.Payload == payload, "the dot is on " + controlName + " (aim " + (aimed == null ? "none" : aimed.Kind + "/" + aimed.Payload) + ")");
        }
        private static IEnumerator SelectReal(string controlName, SiteId site)
        {
            yield return Aim(controlName, ConsoleControlKind.Card, site);
            yield return Press(Key.E);
        }
        private static IEnumerator PullReal()
        {
            yield return Aim(ConsoleRig.ShipLeverName, ConsoleControlKind.Lever, SiteId.None);
            yield return Press(Key.E);
        }
        private static IEnumerator ExpectRefusal(int serialBefore, string text, string label)
        {
            yield return Expect(() => Day.LastRefusal.Serial > serialBefore, 3f, () => label + ": refused '" + Day.LastRefusal.Text + "'");
            Check(Day.LastRefusal.Text == text, label + ": the refusal reads '" + Day.LastRefusal.Text + "' (expected '" + text + "')");
        }
        // The swing this peer's rig plays for the pull: the played serial and the peak angle seen.
        private static IEnumerator ExpectSwing(int serial, string label)
        {
            float peak = 0f; float deadline = Time.unscaledTime + 1.2f;
            ShipNavigationConsole c = Composer();
            while (Time.unscaledTime < deadline) { if (c != null) peak = Mathf.Max(peak, c.LeverAngle); yield return null; }
            Check(c != null && c.LeverPlayedSerial == serial && peak > 0f, $"{label}: this rig swung its lever for pull {serial} (played {(c == null ? -1 : c.LeverPlayedSerial)}, peak {peak:0.#}°)");
        }
        private static IEnumerator Arrive(WorldId id, string label)
        {
            yield return Expect(() => Day.World == id && !Flow.Transitioning && Day.Phase != DayPhase.Sailing && Day.Phase != DayPhase.SailingHome && !Host().TravelLocked, 75f, () => label + ": arrived at " + id + " (phase " + Day.Phase + ")");
            yield return Wait(0.6f);
        }
        private static IEnumerator Shot(string name, Vector3 lookAt)
        {
            ShipNavigationConsole c = Composer();
            if (c == null) yield break;
            Vector3 front = c.transform.rotation * Vector3.forward; front.y = 0f; front.Normalize();
            Vector3 eye = c.transform.position + front * 1.5f + Vector3.up * 1.55f;
            Say("capture " + H.CaptureFrom(eye, lookAt == Vector3.zero ? c.transform.position + Vector3.up * 1.2f : lookAt, Captures + name + ".png"));
            yield return null;
        }
        // Dive done without diving (loop S5's recipe): the phase through DiveInProgress with nobody below.
        private static IEnumerator DiveDoneNow(string label)
        {
            string why = Day.Phase == DayPhase.AtSea ? string.Empty : "not at sea (" + Day.Phase + ")";
            Check(Day.Phase == DayPhase.AtSea && Day.ServerBeginDay(out why), label + ": the day began (" + why + ")");
            yield return null;
            Check(Day.ServerEndDayIfDone(Flow.Settings.DaysPerCycle) && Day.DiveDone && Day.Phase == DayPhase.AtSea, label + ": the dive is done");
            yield return null;
        }

        // A fake diver (ServerSetBelow) calls the car down (WorldSceneFlow.CarReturnWanted) and
        // nobody rides it up again; the ship's rule table then reads DIVERS BELOW for an empty
        // Below because the cabin is away (CrewDayState.CabinAway; bugs/SHIP-2). Put the car back
        // at the top as a fresh site does, once nobody is below.
        private static IEnumerator CarUp(string label)
        {
            Say(label + ": the car after the fake dive: " + Day.Elevator.State + (Day.CabinAway ? " (cabin away)" : string.Empty));
            float deadline = Time.unscaledTime + 3f;
            while (Time.unscaledTime < deadline && Day.Below.Count == 0 && Day.CabinAway)
            {
                Day.ServerSetElevator(new ElevatorPhase { Serial = Day.Elevator.Serial + 1, State = SunkCost.Diving.ElevatorState.AtTop, Upward = true, StartTick = FishNet.InstanceFinder.TimeManager.Tick, DurationTicks = 0 });
                yield return null; yield return null;
            }
            Check(!Day.CabinAway, label + ": the car is back at the top (" + Day.Elevator.State + ")");
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            WorldSceneFlow flow = Flow;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ConsoleShipCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            SiteCatalog sites = SiteCatalog.Resolve();
            string me = WorldSceneFlow.DisplayName(host.OwnerId);
            int days = flow.Settings.DaysPerCycle, quota = flow.Settings.QuotaPerCycle;
            float refusalSeconds = flow.Settings.RefusalDisplaySeconds;
            Say($"host '{me}', {days} days a cycle, quota ${quota}, refusals shown {refusalSeconds} s; prices Site02 ${sites.UnlockPrice(SiteId.Site02)} Site03 ${sites.UnlockPrice(SiteId.Site03)} Site04 ${sites.UnlockPrice(SiteId.Site04)}");

            Heading("H0 — the console stands on the docked ship: nothing selected, HERE on HQ, the locks, the dim sign");
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            Check(hqShip != null, "H0 HQ has a docked ship");
            ConsoleRig rig = ConsoleRig.OnShip(hqShip);
            Check(rig != null && rig.Kind == ConsoleKind.Ship && rig.gameObject.scene == WorldScenes.Scene(WorldId.HQ), "H0 the ship rig is on the docked ship, kind Ship");
            Check(rig.GetComponent<ShipNavigationConsole>() != null && rig.GetComponent<ConsoleLever>() != null, "H0 the rig carries the composer and the lever");
            Check(rig.LeverCollider != null && rig.LeverCollider.name == ConsoleRig.ShipLeverName && rig.CardColliders.Count == 5 && rig.CardColliders.All(c => c != null), "H0 the lever control and five card colliders");
            for (int i = 0; i < 5; i++)
            {
                ConsoleControl cc = rig.ControlOf(rig.CardColliders[i]);
                Check(cc != null && cc.Kind == ConsoleControlKind.Card && cc.Payload == Destinations.Cards[i] && rig.CardColliders[i].name == ConsoleRig.CardNamePrefix + Destinations.Cards[i], "H0 card " + i + " is " + rig.CardColliders[i].name + " → " + cc?.Payload);
            }
            Check(Day.Phase == DayPhase.AtHQ && Day.Day == 0 && Day.SelectedSite == SiteId.None && Day.UnlockedSites == 0 && Day.CurrentSite == SiteId.HQ && Day.LastLeverPull.Serial == 0, "H0 fresh state: " + H.ConsoleStatus());
            yield return AtConsole();
            yield return Expect(() => Top().StartsWith("NAVIGATION · NEW CYCLE") && CardIs("HQ", false, false, true) && CardIs("SITE 01", false, false, false) && CardIs("SITE 02", false, true, false) && CardIs("SITE 03", false, true, false) && CardIs("SITE 04", false, true, false), 2f, () => "H0 top screen: " + Top());
            Check(Top().Contains($"QUOTA $0 / ${quota}"), "H0 the quota at the foot: " + Top());
            Check(Bottom().StartsWith("SELECT A DESTINATION") && Bottom().Contains("NEW CYCLE") && Bottom().Contains($"QUOTA $0 / ${quota}"), "H0 bottom screen: " + Bottom());
            Check(Sign().EndsWith("/off"), "H0 the sign is dim: " + Sign());
            Check(H.ConsoleStatus().Contains("shipAction=None/None/off:" + ConsoleRules.SelectFirst), "H0 nothing to confirm: " + H.ConsoleStatus());
            Check(Text().StartsWith("Docked at HQ"), "H0 compat text: " + Text());
            Say("prompt at rest: " + H.PromptText());

            Heading("H1 — choose HQ with the real aim and E: selected, HERE stays, SAIL HOME, the lever dim with 'Already here'");
            yield return Aim("Nav Card HQ", ConsoleControlKind.Card, SiteId.HQ);
            yield return null;
            Check(hud.PromptText == "Press E to select HQ", "H1 the prompt on the HQ card: " + hud.PromptText);
            yield return Press(Key.E);
            yield return Expect(() => Day.SelectedSite == SiteId.HQ, 3f, () => "H1 the selection is HQ (" + Day.SelectedSite + ")");
            yield return Expect(() => CardIs("HQ", true, false, true), 2f, () => "H1 the HQ card is selected and HERE: " + Top());
            yield return Expect(() => Bottom().StartsWith("SELECTED DESTINATION · HQ · SAIL HOME") && Bottom().EndsWith("YOU ARE HERE"), 2f, () => "H1 bottom screen: " + Bottom());
            Check(Sign().EndsWith("/off") && H.ConsoleStatus().Contains("shipAction=None/None/off:" + ConsoleRules.AlreadyHere), "H1 the sign is dim, reason Already here: " + Sign());
            yield return null;
            Check(hud.PromptText == "HQ selected", "H1 the card's prompt now: " + hud.PromptText);
            yield return Aim(ConsoleRig.ShipLeverName, ConsoleControlKind.Lever, SiteId.None);
            yield return null;
            Check(hud.PromptText == ConsoleRules.AlreadyHere, "H1 the dim lever's prompt explains: " + hud.PromptText);
            int refusal = Day.LastRefusal.Serial, pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.AlreadyHere, "H1 a pull on the dim lever");
            yield return Expect(() => NoticeShown() && Bottom().Contains("ALREADY HERE") && Bottom().Contains("NOTICE"), 2f, () => "H1 the bottom screen shows the refusal prominently: " + Bottom());
            Check(Text() == ConsoleRules.AlreadyHere && Day.LastLeverPull.Serial == pulls && !Day.Travelling, "H1 compat text is the refusal, no pull, no trip: " + Text());
            yield return Expect(() => !NoticeShown() && Bottom().EndsWith("YOU ARE HERE"), refusalSeconds + 1.5f, () => "H1 the contextual screen is back after the notice: " + Bottom());

            Heading("H2 — choose the open site: CONFIRM lit, READY");
            yield return SelectReal("Nav Card Site01", SiteId.Site01);
            yield return Expect(() => Day.SelectedSite == SiteId.Site01, 3f, () => "H2 the selection is Site 01 (" + Day.SelectedSite + ")");
            yield return Expect(() => Sign() == "CONFIRM/on" && Bottom().StartsWith("SELECTED DESTINATION · SITE 01") && Bottom().EndsWith("READY"), 2f, () => "H2 CONFIRM lit, READY: " + Sign() + " | " + Bottom());
            Check(CardIs("SITE 01", true, false, false) && CardIs("HQ", false, false, true) && Bottom().Contains("NEW CYCLE") && Bottom().Contains($"QUOTA $0 / ${quota}"), "H2 the card selected, HERE still on HQ, the facts: " + Top());
            Check(H.ConsoleStatus().Contains("shipAction=Confirm/Site01/on"), "H2 the resolver: " + H.ConsoleStatus());
            yield return Aim(ConsoleRig.ShipLeverName, ConsoleControlKind.Lever, SiteId.None);
            yield return null;
            Check(hud.PromptText == "Press E to sail to SITE 01", "H2 the lever's prompt: " + hud.PromptText);
            Check(Text().StartsWith("Docked at HQ"), "H2 compat text: " + Text());
            yield return Shot("console-ship-H2-confirm", Vector3.zero);

            Heading("H3 — the presser ashore: 'Not aboard: <host>' on the screen and the compat text; nothing sails; a selection from ashore is refused too");
            H.ClientMoveLocalPlayerToBoard(); yield return null; yield return null; yield return null;
            Check(!hqShip.IsAboard(host.transform.position), "H3 the host stands on the HQ floor");
            refusal = Day.LastRefusal.Serial; pulls = Day.LastLeverPull.Serial; int trips = Day.Departure.Serial;
            Say("pull from ashore: " + H.ClientPullLever("Ship"));
            yield return ExpectRefusal(refusal, ConsoleRules.NotAboard(me), "H3 the pull from ashore");
            yield return Expect(() => Text() == ConsoleRules.NotAboard(me), 2f, () => "H3 compat text is the whole refusal: " + Text());
            yield return Expect(() => NoticeShown() && Bottom().Contains(ConsoleRules.NotAboard(me).ToUpperInvariant()), 2f, () => "H3 the bottom screen names the absentee: " + Bottom());
            Check(!Day.Travelling && Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls && Day.SelectedSite == SiteId.Site01, "H3 nothing sailed, no pull counted, the selection kept");
            refusal = Day.LastRefusal.Serial;
            Say("select from ashore: " + H.ClientSelect("HQ"));
            yield return ExpectRefusal(refusal, ConsoleRules.NotAboard(me), "H3 a selection from ashore");
            Check(Day.SelectedSite == SiteId.Site01, "H3 the selection is unchanged by the ashore press");
            yield return AtConsole();
            yield return Expect(() => !NoticeShown() && Sign() == "CONFIRM/on", refusalSeconds + 2f, () => "H3 back at the console: CONFIRM again after the notice: " + Sign());

            Heading("H4 — choose a locked site short of money: UNLOCK dim, BALANCE / COST / $40 SHORT, the pull refused and nothing charged");
            Day.ServerSetBalanceForChecks(60); yield return null;
            int price02 = sites.UnlockPrice(SiteId.Site02);
            yield return SelectReal("Nav Card Site02", SiteId.Site02);
            yield return Expect(() => Day.SelectedSite == SiteId.Site02, 3f, () => "H4 the locked site is selected (" + Day.SelectedSite + ")");
            yield return Expect(() => Sign() == $"UNLOCK ${price02}/off" && Bottom().EndsWith(ConsoleRules.ShortBy(price02 - 60).ToUpperInvariant()), 2f, () => "H4 UNLOCK dim, the shortfall: " + Sign() + " | " + Bottom());
            Check(Bottom().StartsWith("SELECTED DESTINATION · SITE 02") && Bottom().Contains("BALANCE $60") && Bottom().Contains($"COST ${price02}") && CardIs("SITE 02", true, true, false), "H4 the locked card's screen: " + Bottom() + " | " + Top());
            Check(H.ConsoleStatus().Contains($"shipAction=Unlock/Site02/off:{ConsoleRules.ShortBy(price02 - 60)}"), "H4 the resolver: " + H.ConsoleStatus());
            yield return Aim(ConsoleRig.ShipLeverName, ConsoleControlKind.Lever, SiteId.None);
            yield return null;
            Check(hud.PromptText == ConsoleRules.ShortBy(price02 - 60), "H4 the dim lever's prompt: " + hud.PromptText);
            refusal = Day.LastRefusal.Serial; pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.ShortBy(price02 - 60), "H4 the pull short of money");
            yield return Expect(() => NoticeShown() && Bottom().Contains(ConsoleRules.ShortBy(price02 - 60).ToUpperInvariant()), 2f, () => "H4 the screen explains: " + Bottom());
            Check(Day.Balance == 60 && Day.UnlockedSites == 0 && Day.LastLeverPull.Serial == pulls, $"H4 nothing charged (${Day.Balance}), nothing unlocked, no pull counted");
            yield return Shot("console-ship-H4-short", Vector3.zero);
            yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "H4 the notice cleared");

            Heading("H6 — the same-frame double charge: two server unlocks of one site in one frame charge once");
            Day.ServerSetBalanceForChecks(sites.UnlockPrice(SiteId.Site04)); yield return null;
            string first = H.ServerUnlock("Site04"), second = H.ServerUnlock("Site04");
            Check(first == "unlocked Site04" && second == "refused: " + ConsoleRules.AlreadyOpen, "H6 same frame: '" + first + "' then '" + second + "'");
            Check(Day.Balance == 0 && Day.IsOpen(SiteId.Site04) && Destinations.MaskText(Day.UnlockedSites) == "Site04", $"H6 charged exactly once: balance ${Day.Balance}, unlocked {Destinations.MaskText(Day.UnlockedSites)}");
            Check(H.ServerUnlock("Site01") == "refused: " + ConsoleRules.NoSuchDestination && H.ServerUnlock("HQ") == "refused: " + ConsoleRules.NoSuchDestination, "H6 HQ and Site 01 cannot be bought");
            Say("relock: " + H.ServerRelock());
            Check(Day.UnlockedSites == 0, "H6 every site locked again for the per-site rows");

            Heading("H5/H7 — unlock each site in the order 03, 04, 02: short (no charge), exact funds (charged once, the lever swings), already open (refused, no charge)");
            int pullsBefore = Day.LastLeverPull.Serial;
            int expectedMask = 0;
            foreach (SiteId site in new[] { SiteId.Site03, SiteId.Site04, SiteId.Site02 })
            {
                string card = ConsoleRig.CardNamePrefix + site, name = sites.NameOf(site);
                int price = sites.UnlockPrice(site);
                Check(price > 0, $"H7 {name} costs ${price} (the catalogue's serialized price; the default is ${SiteCatalog.DefaultPrice(site)})");
                Day.ServerSetBalanceForChecks(price - 40); yield return null;
                yield return SelectReal(card, site);
                yield return Expect(() => Day.SelectedSite == site && Sign() == $"UNLOCK ${price}/off" && Bottom().EndsWith("$40 SHORT"), 3f, () => $"H7 {name} selected short: " + Sign() + " | " + Bottom());
                refusal = Day.LastRefusal.Serial;
                Say("pull short: " + H.ClientPullLever("Ship"));
                yield return ExpectRefusal(refusal, ConsoleRules.ShortBy(40), $"H7 {name} pull $40 short");
                Check(Day.Balance == price - 40 && !Day.IsOpen(site), $"H7 {name}: no charge on the short pull (${Day.Balance})");
                yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "H7 the notice cleared");
                Day.ServerSetBalanceForChecks(price); yield return null;
                yield return Expect(() => Sign() == $"UNLOCK ${price}/on" && Bottom().EndsWith("PULL TO UNLOCK") && Bottom().Contains($"BALANCE ${price}"), 2f, () => $"H5 {name} exact funds: " + Sign() + " | " + Bottom());
                int serialBefore = Day.LastLeverPull.Serial;
                if (site == SiteId.Site03)
                {
                    yield return Aim(ConsoleRig.ShipLeverName, ConsoleControlKind.Lever, SiteId.None);
                    yield return null;
                    Check(hud.PromptText == $"Press E to unlock {name} (${price})", "H5 the lever's prompt: " + hud.PromptText);
                    yield return Shot("console-ship-H5-unlock-lit", Vector3.zero);
                    yield return Press(Key.E);
                }
                else Say("pull exact: " + H.ClientPullLever("Ship"));
                yield return Expect(() => Day.IsOpen(site) && Day.LastLeverPull.Serial == serialBefore + 1, 3f, () => $"H5 {name} unlocked by the pull (mask {Destinations.MaskText(Day.UnlockedSites)}, pull {Day.LastLeverPull.Serial})");
                expectedMask |= Destinations.Bit(site);
                Check(Day.Balance == 0 && Day.UnlockedSites == expectedMask && Day.LastLeverPull.Kind == ConsoleKind.Ship && Day.LastLeverPull.Action == LeverAction.Unlock, $"H5 {name}: charged exactly ${price}, balance $0, the pull is Ship/Unlock");
                yield return ExpectSwing(serialBefore + 1, $"H5 {name}");
                yield return Expect(() => CardIs(name, true, false, false) && Sign() == "CONFIRM/on" && Bottom().EndsWith("READY"), 2f, () => $"H5 {name} now open: no lock, CONFIRM lit, READY: " + Top() + " | " + Sign());
                Day.ServerSetBalanceForChecks(price); yield return null;
                refusal = Day.LastRefusal.Serial; serialBefore = Day.LastLeverPull.Serial;
                Say("stale UNLOCK pull: " + H.ClientPullLeverExpecting("Ship", "Unlock", site.ToString()));
                yield return ExpectRefusal(refusal, ConsoleRules.AlreadyOpen, $"H7 {name} a pull still expecting UNLOCK");
                Check(H.ServerUnlock(site.ToString()) == "refused: " + ConsoleRules.AlreadyOpen, $"H7 {name} the server unlock refuses an open site");
                Check(Day.Balance == price && Day.LastLeverPull.Serial == serialBefore && Day.UnlockedSites == expectedMask, $"H7 {name}: no second charge (${Day.Balance}), no pull counted");
                Day.ServerSetBalanceForChecks(0); yield return null;
                yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "H7 the notice cleared");
            }
            Check(Destinations.MaskText(Day.UnlockedSites) == "Site02+Site03+Site04" && Day.LastLeverPull.Serial == pullsBefore + 3, "H7 all three sites open after three pulls: " + Destinations.MaskText(Day.UnlockedSites));
            yield return Expect(() => CardIs("SITE 02", true, false, false) && CardIs("SITE 03", false, false, false) && CardIs("SITE 04", false, false, false), 2f, () => "H7 no lock on any card: " + Top());

            Heading("H8 — the selection changes in the frame before the pull: 'Selection changed', nothing sails; then the honest pull sails and clears the selection");
            Say("select: " + H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && Sign() == "CONFIRM/on", 3f, () => "H8 Site 01 selected, CONFIRM: " + Sign());
            refusal = Day.LastRefusal.Serial; pulls = Day.LastLeverPull.Serial; trips = Day.Departure.Serial;
            string sel = H.ClientSelect("Site03"), stale = H.ClientPullLeverExpecting("Ship", "Confirm", "Site01");
            Say("same frame: " + sel + " then " + stale);
            yield return ExpectRefusal(refusal, ConsoleRules.SelectionChanged, "H8 the stale CONFIRM");
            Check(Day.SelectedSite == SiteId.Site03 && !Day.Travelling && Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls && Day.Phase == DayPhase.AtHQ, "H8 the new selection stands, nothing sailed, no pull counted");
            yield return Expect(() => NoticeShown() && Bottom().Contains("SELECTION CHANGED"), 2f, () => "H8 the screen explains: " + Bottom());
            yield return Expect(() => !NoticeShown() && Sign() == "CONFIRM/on" && Bottom().StartsWith("SELECTED DESTINATION · SITE 03"), refusalSeconds + 1.5f, () => "H8 Site 03 ready after the notice: " + Bottom());
            Say("pull: " + H.ClientPullLever("Ship"));
            yield return Expect(() => Day.Departure.Serial == trips + 1 && Day.Travelling, 4f, () => "H8 one trip started (serial " + Day.Departure.Serial + ")");
            Check(Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Action == LeverAction.Confirm && Day.SelectedSite == SiteId.None, "H8 the pull counted once as CONFIRM and the selection cleared at once: " + H.ConsoleStatus());
            Check(Day.Destination == WorldId.Sea && Day.SiteDestination == SiteId.Site03, "H8 bound for Site 03 in the sea world: to=" + Day.Destination + " toSite=" + Day.SiteDestination);
            yield return ExpectSwing(pulls + 1, "H8");

            Heading("H10 — the sailing screens: SAILING TO SITE 03, the lever dim, HERE on no card, a selection refused during the trip");
            yield return Expect(() => Bottom().StartsWith("SAILING · SITE 03") && Bottom().EndsWith("SAILING TO SITE 03"), 3f, () => "H10 bottom screen: " + Bottom());
            Check(Sign().EndsWith("/off") && !Top().Contains("@"), "H10 the sign dim, HERE on no card while travelling: " + Sign() + " | " + Top());
            Check(Text().StartsWith("All aboard") || Text().StartsWith("Casting off") || Text().StartsWith("Sailing to Site 03"), "H10 compat text: " + Text());
            refusal = Day.LastRefusal.Serial;
            Say("select during the trip: " + H.ClientSelect("HQ"));
            yield return ExpectRefusal(refusal, ConsoleRules.Travelling, "H10 a selection during the trip");
            Check(Day.SelectedSite == SiteId.None, "H10 the selection stays cleared");
            yield return Arrive(WorldId.Sea, "H10");

            Heading("H9 — SITE 03 routed to Site 01's world with its identity kept: HERE on SITE 03, DAY 1/3");
            Check(Day.World == WorldId.Sea && WorldScenes.IsLoaded(WorldId.Sea) && Day.Phase == DayPhase.AtSea && Day.Day == 1, "H9 at sea on day 1 (phase " + Day.Phase + ")");
            Check(Day.CurrentSite == SiteId.Site03 && Day.SiteDestination == SiteId.Site03, "H9 the logical site is Site 03: site=" + Day.CurrentSite);
            ShipParts seaShip = ShipParts.InWorld(WorldId.Sea);
            Check(seaShip != null && ConsoleRig.OnShip(seaShip) != null && Composer() != null && Composer().gameObject.scene == WorldScenes.Scene(WorldId.Sea), "H9 the sea ship carries its own console");
            yield return AtConsole();
            yield return Expect(() => Top().StartsWith($"NAVIGATION · DAY 1/{days}") && CardIs("SITE 03", false, false, true) && CardIs("HQ", false, false, false), 2f, () => "H9/H16 HERE on SITE 03, DAY 1/3, nothing selected: " + Top());
            Check(Bottom().StartsWith("SELECT A DESTINATION") && Bottom().Contains($"DAY 1 OF {days}") && !Bottom().Contains("SAILING"), "H16 the screen re-entered after the trip: " + Bottom());
            Check(Text().StartsWith($"Day 1 of {days} — Site 03"), "H9 compat text names the site: " + Text());
            Say("the sea rig's played serial (a fresh rig plays no pull that preceded it): " + H.LeverPullText());

            Heading("H13 — a dive in progress: DIVE IN PROGRESS / 1 BELOW, the lever dim, a selection allowed, the pull refused");
            Check(Day.ServerBeginDay(out string beginWhy), "H13 the day began (" + beginWhy + ")");
            Day.ServerSetBelow(host.OwnerId, true); yield return null;
            yield return Expect(() => Bottom().StartsWith("DIVE IN PROGRESS") && Bottom().EndsWith("1 BELOW") && Sign().EndsWith("/off"), 2f, () => "H13 the dive screen: " + Bottom() + " | " + Sign());
            Check(Text().StartsWith($"Day 1 of {days} — dive in progress"), "H13 compat text: " + Text());
            Check(Sign() == "END DAY/off", "H13 the dim sign's word comes from the facts (the lever's next job, bugs/NET-2's fix): " + Sign());
            Say("select during the dive: " + H.ClientSelect("HQ"));
            yield return Expect(() => Day.SelectedSite == SiteId.HQ, 3f, () => "H13 a card may be selected during the dive (" + Day.SelectedSite + ")");
            refusal = Day.LastRefusal.Serial; pulls = Day.LastLeverPull.Serial;
            Say("pull during the dive: " + H.ClientPullLever("Ship"));
            yield return ExpectRefusal(refusal, ConsoleRules.DiveInProgress, "H13 the pull during the dive");
            Check(Day.LastLeverPull.Serial == pulls && Day.Phase == DayPhase.DiveInProgress, "H13 no pull counted, the dive goes on");
            yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "H13 the notice cleared");
            Day.ServerSetBelow(host.OwnerId, false); yield return null;
            Check(Day.ServerEndDayIfDone(days) && Day.DiveDone && Day.Phase == DayPhase.AtSea, "H13 everyone up: the dive is done");

            // Round 2 (28 September 2026): the retest of bugs/SHIP-2 (fixed in 9a2c11b). The fake
            // diver called the car down and nobody rode it up: Below is empty, the car is away.
            // END DAY must read and the real pull below (H11) must end the day with the car away.
            Heading("SHIP-2 retest — the car away with nobody below after the dive: END DAY lit (not 'Divers below')");
            Say("SHIP-2: the car after the fake dive: " + Day.Elevator.State + (Day.CabinAway ? " (cabin away)" : " (at the top)"));
            if (!Day.CabinAway)
            {
                Day.ServerSetElevator(new ElevatorPhase { Serial = Day.Elevator.Serial + 1, State = SunkCost.Diving.ElevatorState.AtBottom, Upward = false, StartTick = FishNet.InstanceFinder.TimeManager.Tick, DurationTicks = 0 });
                yield return null; yield return null;
                Say("SHIP-2: the car was up; sent to the bottom by hand: " + Day.Elevator.State);
            }
            Check(Day.CabinAway && Day.Below.Count == 0 && Day.DiveDone, "SHIP-2 the repro state: the cabin away (" + Day.Elevator.State + "), nobody below, the dive done");
            yield return Expect(() => Sign() == "END DAY/on" && Bottom().EndsWith("DIVE DONE — END THE DAY FIRST") && H.ConsoleStatus().Contains("shipAction=EndDay/None/on"), 2f, () => "SHIP-2 END DAY lit with the car away and nobody below: " + Sign() + " | " + Bottom());
            Check(!Bottom().Contains("DIVERS BELOW"), "SHIP-2 the screen names no divers: " + Bottom());

            Heading("H11 — after the dive: END DAY on the sign whatever is selected; the real pull ends the day once; a second pull is refused");
            yield return Expect(() => Sign() == "END DAY/on" && Bottom().EndsWith("DIVE DONE — END THE DAY FIRST"), 2f, () => "H11 END DAY with HQ selected: " + Sign() + " | " + Bottom());
            Say("select Site 01: " + H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && Sign() == "END DAY/on", 3f, () => "H11 still END DAY with Site 01 selected: " + Sign());
            Check(Text().StartsWith($"Day 1 of {days} — dive done"), "H11 compat text: " + Text());
            Check(H.ConsoleStatus().Contains("shipAction=EndDay/None/on"), "H11 the resolver: " + H.ConsoleStatus());
            yield return Aim(ConsoleRig.ShipLeverName, ConsoleControlKind.Lever, SiteId.None);
            yield return null;
            Check(hud.PromptText == "Press E to end the day", "H11 the lever's prompt: " + hud.PromptText);
            yield return Shot("console-ship-H11-endday", Vector3.zero);
            pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.Day == 2 && !Day.DiveDone && Day.LastLeverPull.Serial == pulls + 1, 3f, () => $"H11 the day ended once by the lever: day {Day.Day}, pull {Day.LastLeverPull.Serial}");
            Check(Day.LastLeverPull.Action == LeverAction.EndDay, "H11 the pull is END DAY");
            Say("SHIP-2: the car after END DAY: " + Day.Elevator.State + (Day.CabinAway ? " (cabin away)" : " (at the top)"));
            yield return ExpectSwing(pulls + 1, "H11");
            refusal = Day.LastRefusal.Serial;
            Say("second END DAY: " + H.ClientPullLeverExpecting("Ship", "EndDay", "None"));
            yield return ExpectRefusal(refusal, ConsoleRules.NobodyDived, "H11 a second END DAY pull");
            Check(Day.Day == 2 && Day.LastLeverPull.Serial == pulls + 1, "H11 the day did not end twice");
            yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "H11 the notice cleared");
            yield return Expect(() => Top().Contains($"DAY 2/{days}") && Bottom().EndsWith("SAIL HOME FIRST") && Sign().EndsWith("/off"), 2f, () => "H16 day 2, Site 01 selected at Site 03: sail home first: " + Top() + " | " + Bottom());
            Say("select the site the ship is at: " + H.ClientSelect("Site03"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site03 && Bottom().EndsWith("YOU ARE HERE") && CardIs("SITE 03", true, false, true), 3f, () => "H16 the current site selected: YOU ARE HERE: " + Bottom());
            Heading("SHIP-2 retest (the sail) — HQ selected with the car still away and nobody below: CONFIRM dim, 'Cabin below', the pull and the server sail refused; READY once the car is up");
            if (!Day.CabinAway)
            {
                Day.ServerSetElevator(new ElevatorPhase { Serial = Day.Elevator.Serial + 1, State = SunkCost.Diving.ElevatorState.AtBottom, Upward = false, StartTick = FishNet.InstanceFinder.TimeManager.Tick, DurationTicks = 0 });
                yield return null; yield return null;
                Say("SHIP-2: the car was up after END DAY; sent to the bottom by hand: " + Day.Elevator.State);
            }
            Say("select HQ: " + H.ClientSelect("HQ"));
            yield return Expect(() => Day.SelectedSite == SiteId.HQ && Sign() == "CONFIRM/off" && Bottom().StartsWith("SELECTED DESTINATION · HQ · SAIL HOME") && Bottom().EndsWith(ConsoleRules.CabinBelow.ToUpperInvariant()) && H.ConsoleStatus().Contains("shipAction=None/None/off:" + ConsoleRules.CabinBelow), 3f, () => "SHIP-2 HQ selected, the car away: CONFIRM dim, CABIN BELOW: " + Sign() + " | " + Bottom());
            refusal = Day.LastRefusal.Serial; pulls = Day.LastLeverPull.Serial; trips = Day.Departure.Serial;
            Say("pull with the car away: " + H.ClientPullLever("Ship"));
            yield return ExpectRefusal(refusal, ConsoleRules.CabinBelow, "SHIP-2 the pull with the car away");
            string carSail = H.ServerSail("HQ");
            Check(carSail == "refused: " + ConsoleRules.CabinBelow, "SHIP-2 the server sail agrees with the lever: " + carSail);
            Check(!Day.Travelling && Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls, "SHIP-2 nothing sailed, no pull counted");
            yield return CarUp("SHIP-2");
            yield return Expect(() => Day.SelectedSite == SiteId.HQ && Sign() == "CONFIRM/on" && Bottom().StartsWith("SELECTED DESTINATION · HQ · SAIL HOME") && Bottom().EndsWith("READY"), refusalSeconds + 3f, () => "H16 HQ selected at sea: SAIL HOME, CONFIRM: " + Bottom());
            Check(Text().StartsWith($"Day 2 of {days} — Site 03"), "H16 compat text at sea on day 2: " + Text());

            Heading("H12 — a locked site selected after the dive: UNLOCK overrides END DAY; the pull buys it; then END DAY reads for the open site");
            Say("relock: " + H.ServerRelock());
            Day.ServerSetBalanceForChecks(price02); yield return null;
            yield return DiveDoneNow("H12");
            yield return Expect(() => Sign() == "END DAY/on", 2f, () => "H12 END DAY first (HQ selected): " + Sign());
            Say("select the locked site: " + H.ClientSelect("Site02"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site02 && Sign() == $"UNLOCK ${price02}/on" && Bottom().EndsWith("PULL TO UNLOCK"), 3f, () => "H12 UNLOCK overrides END DAY: " + Sign() + " | " + Bottom());
            Check(CardIs("SITE 02", true, true, false) && H.ConsoleStatus().Contains("shipAction=Unlock/Site02/on"), "H12 the locked card selected, the resolver agrees: " + Top());
            pulls = Day.LastLeverPull.Serial;
            Say("pull: " + H.ClientPullLever("Ship"));
            yield return Expect(() => Day.IsOpen(SiteId.Site02) && Day.Balance == 0 && Day.LastLeverPull.Serial == pulls + 1, 3f, () => "H12 bought after the dive: " + Destinations.MaskText(Day.UnlockedSites) + " $" + Day.Balance);
            yield return Expect(() => Sign() == "END DAY/on" && Bottom().EndsWith("DIVE DONE — END THE DAY FIRST") && CardIs("SITE 02", true, false, false), 2f, () => "H12 END DAY again for the now-open site: " + Sign() + " | " + Bottom());
            Check(Day.DiveDone && Day.Day == 2, "H12 the day is still to be ended");
            pulls = Day.LastLeverPull.Serial;
            Say("END DAY: " + H.ClientPullLever("Ship"));
            yield return Expect(() => Day.Day == 3 && !Day.DiveDone && Day.LastLeverPull.Serial == pulls + 1, 3f, () => $"H12 the day ended: day {Day.Day}");

            Heading("H15 — someone still below: the sign dim, the reason names it, the pull refused");
            Say("select HQ: " + H.ClientSelect("HQ"));
            yield return Expect(() => Day.SelectedSite == SiteId.HQ && Sign() == "CONFIRM/on", 3f, () => "H15 HQ selected, CONFIRM: " + Sign());
            Day.ServerSetBelow(host.OwnerId, true); yield return null;
            yield return Expect(() => Sign().EndsWith("/off") && Bottom().EndsWith("DIVERS BELOW") && H.ConsoleStatus().Contains("shipAction=None/None/off:" + ConsoleRules.DiversBelow), 2f, () => "H15 dim, divers below: " + Sign() + " | " + Bottom());
            refusal = Day.LastRefusal.Serial; pulls = Day.LastLeverPull.Serial; trips = Day.Departure.Serial;
            Say("pull: " + H.ClientPullLever("Ship"));
            yield return ExpectRefusal(refusal, ConsoleRules.DiversBelow, "H15 the pull with a diver below");
            Check(!Day.Travelling && Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls, "H15 nothing sailed");
            Day.ServerSetBelow(host.OwnerId, false); yield return null;
            yield return CarUp("H15");
            yield return Expect(() => !NoticeShown() && Sign() == "CONFIRM/on", refusalSeconds + 1.5f, () => "H15 CONFIRM again once everyone is up: " + Sign());

            Heading("H14 — payday: PAYDAY in the corner, a site's CONFIRM refused 'Payday — only HQ', HQ sails home; at the dock 'Pay the quota first'");
            Day.ServerForceCycleForChecks(days, true); yield return null;
            Say("select Site 01: " + H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && Sign().EndsWith("/off") && Bottom().EndsWith("PAYDAY — ONLY HQ") && Top().StartsWith("NAVIGATION · PAYDAY"), 3f, () => "H14 a site on payday: " + Top() + " | " + Bottom() + " | " + Sign());
            Check(Bottom().Contains("PAYDAY") && Text().StartsWith("PAYDAY"), "H14 compat text: " + Text());
            refusal = Day.LastRefusal.Serial; trips = Day.Departure.Serial;
            Say("pull for a site on payday: " + H.ClientPullLever("Ship"));
            yield return ExpectRefusal(refusal, ConsoleRules.PaydayOnlyHQ, "H14 the site's pull on payday");
            Check(!Day.Travelling && Day.Departure.Serial == trips, "H14 nothing sailed");
            yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "H14 the notice cleared");
            Say("select HQ: " + H.ClientSelect("HQ"));
            yield return Expect(() => Day.SelectedSite == SiteId.HQ && Sign() == "CONFIRM/on" && Bottom().EndsWith("READY"), 3f, () => "H14 HQ on payday: CONFIRM: " + Sign() + " | " + Bottom());
            pulls = Day.LastLeverPull.Serial;
            Say("pull: " + H.ClientPullLever("Ship"));
            yield return Expect(() => Day.Departure.Serial == trips + 1 && Day.Travelling && Day.Phase == DayPhase.SailingHome, 4f, () => "H14 sailing home (phase " + Day.Phase + ")");
            Check(Day.LastLeverPull.Serial == pulls + 1 && Day.SelectedSite == SiteId.None, "H14 the pull counted, the selection cleared");
            yield return Expect(() => Bottom().StartsWith("SAILING · HQ · SAIL HOME") && Bottom().EndsWith("SAILING HOME"), 3f, () => "H14 SAILING HOME: " + Bottom());
            yield return Arrive(WorldId.HQ, "H14");
            Check(Day.Payday && Day.World == WorldId.HQ && Day.CurrentSite == SiteId.HQ && Day.SelectedSite == SiteId.None, "H14 docked on payday, nothing selected");
            yield return AtConsole();
            yield return Expect(() => Top().StartsWith("NAVIGATION · PAYDAY") && CardIs("HQ", false, false, true) && Bottom().StartsWith("SELECT A DESTINATION") && Bottom().Contains("PAYDAY"), 2f, () => "H16 re-entered at the dock on payday: " + Top() + " | " + Bottom());
            Check(Text().StartsWith("Docked at HQ — PAYDAY"), "H14 compat text: " + Text());
            Say("select Site 01 at the dock on payday: " + H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && Sign().EndsWith("/off") && Bottom().EndsWith(ConsoleRules.PayQuotaFirst.ToUpperInvariant()), 3f, () => "H14 pay the quota first: " + Bottom() + " | " + Sign());

            Heading("H17 — the run lost on the plank relocks the sites and clears the selection; the console reads THE RUN IS OVER meanwhile");
            Check(Day.UnlockedSites != 0, "H17 sites are open before the plank: " + Destinations.MaskText(Day.UnlockedSites));
            WorldLoopSettings.QuotaOverrideForTests = 99999;
            H.ClientMoveLocalPlayerToBoard(); yield return Wait(0.3f);
            int paySerial = Day.LastPay.Serial, plankSerial = Day.Plank.Serial;
            Check(flow.ServerPay(host.Owner, out string payWhy), "H17 the pay press is taken: " + payWhy);
            yield return Expect(() => Day.LastPay.Serial > paySerial && Day.LastPay.Lost && Day.Phase == DayPhase.Plank, 3f, () => "H17 short at payday: the plank");
            yield return Expect(() => H.ConsoleStatus().Contains("shipAction=None/None/off:" + ConsoleRules.RunOver) && Bottom().EndsWith("THE RUN IS OVER") && Sign().EndsWith("/off"), 2f, () => "H17 the console on the plank: " + Bottom() + " | " + Sign());
            Check(!flow.ServerUnlockSite(host.Owner, SiteId.Site04, out string plankWhy) && plankWhy == ConsoleRules.RunOver, "H17 an unlock on the plank is refused: " + plankWhy);
            Check(!flow.ServerConfirmSail(host.Owner, SiteId.Site01, out plankWhy) && plankWhy == ConsoleRules.RunOver, "H17 a sail on the plank is refused: " + plankWhy);
            float turn = flow.Settings.PlankTurnSeconds, cardSeconds = flow.Settings.RunOverCardSeconds;
            yield return Expect(() => Day.Phase == DayPhase.AtHQ && Day.Plank.Serial > plankSerial, turn + cardSeconds + 20f, () => "H17 the fresh run after the plank (phase " + Day.Phase + ")");
            WorldLoopSettings.QuotaOverrideForTests = null;
            Check(Day.UnlockedSites == 0 && Day.SelectedSite == SiteId.None && Day.Balance == 0 && Day.Day == 0 && !Day.Payday && Day.CurrentSite == SiteId.HQ && Day.SiteDestination == SiteId.HQ, "H17 the fresh run relocked every site, nothing selected, $0: " + H.ConsoleStatus());
            yield return Expect(() => Top().StartsWith("NAVIGATION · NEW CYCLE") && CardIs("SITE 02", false, true, false) && CardIs("SITE 03", false, true, false) && CardIs("SITE 04", false, true, false) && CardIs("HQ", false, false, true), 3f, () => "H17 the locks are back on the screen: " + Top());
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 6f, () => "H17 the screen clear again");

            Heading("G1 — a guest joins at HQ: the host's locked selection, the dim UNLOCK sign and the shortfall replicate to it");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "G1 guest player spawned");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && GuestHeader(r).Contains("world=HQ") && GuestHeader(r).Contains("phase=AtHQ"), 20f, "G1 guest joined at HQ");
            string them = WorldSceneFlow.DisplayName(guestId);
            Say("guest '" + them + "' is client " + guestId);
            yield return Expect(() => !Host().TravelLocked && !Host().IsDead, 5f, () => "G1 the host is free");
            yield return AtConsole();
            Day.ServerSetBalanceForChecks(0); yield return null;
            Say("host selects Site 02: " + H.ClientSelect("Site02"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site02, 3f, () => "G1 the host's selection is Site 02");
            yield return GuestEventually(r => GuestConsole(r).Contains("selected=Site02;") && GuestConsole(r).Contains("unlocked=none;") && GuestConsole(r).Contains("site=HQ;") && GuestConsole(r).Contains($"leverSign=UNLOCK ${price02}/off;") && GuestConsole(r).Contains($"${price02} SHORT") && GuestCardIs(r, "SITE 02", true, true, false) && GuestCardIs(r, "HQ", false, false, true), 6f, "G1 the guest's screens: the selection, the lock, the dim UNLOCK, the shortfall, HERE on HQ");
            Say("guest console line: " + GuestConsole(lastReply));
            Check(GuestHeader(lastReply).Contains("monitor=Docked at HQ"), "G1 the guest's compat text: " + GuestHeader(lastReply).Split(new[] { "monitor=" }, StringSplitOptions.None)[1].Split(';')[0]);

            Heading("G2 — the guest, aboard, changes the selection: both screens agree, CONFIRM lit on both");
            hqShip = ShipParts.InWorld(WorldId.HQ); // the docked ship is a fresh instance after the trips (the HQ scene was unloaded and reloaded)
            Check(hqShip != null && hqShip.gameObject.scene == WorldScenes.Scene(WorldId.HQ), "G2 the docked ship after the trips");
            Vector3 guestDeck = hqShip.FromShipLocal(new Vector3(2.5f, 0f, 4f));
            yield return GuestMove(guestDeck);
            yield return Expect(() => hqShip.IsSafelyAboard(remote.transform.position), 5f, () => "G2 the guest stands on the deck");
            yield return Send("{\"id\":{id},\"action\":\"select\",\"item\":\"Site01\"}");
            Check(lastReply.Contains("requested select Site01"), "G2 the guest's select went: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.SelectedSite == SiteId.Site01, 3f, () => "G2 the host sees the guest's selection (" + Day.SelectedSite + ")");
            yield return Expect(() => Sign() == "CONFIRM/on" && Bottom().EndsWith("READY") && CardIs("SITE 01", true, false, false), 3f, () => "G2 the host's console: CONFIRM, READY: " + Sign() + " | " + Bottom());
            yield return GuestEventually(r => GuestConsole(r).Contains("selected=Site01;") && GuestConsole(r).Contains("leverSign=CONFIRM/on;") && GuestConsole(r).Contains("READY") && GuestCardIs(r, "SITE 01", true, false, false), 6f, "G2 the guest's console agrees: CONFIRM, READY");

            Heading("G3 — the guest ashore: WAITING FOR <guest> TO BOARD on the host's screen, the host's pull refused naming the guest; aboard again → READY");
            List<Transform> pier = CrewSpawner.SpawnPointsIn(WorldScenes.Scene(WorldId.HQ));
            Check(pier.Count > 0, "G3 HQ has pier spawn points");
            yield return GuestMove(pier[0].position + Vector3.up * 0.05f);
            yield return Expect(() => !hqShip.IsAboard(remote.transform.position), 5f, () => "G3 the guest stands on the pier");
            yield return Expect(() => Sign() == "CONFIRM/off" && Bottom().EndsWith("WAITING FOR " + them.ToUpperInvariant() + " TO BOARD"), 4f, () => "G3 the host's screen waits for the guest: " + Bottom() + " | " + Sign());
            yield return Shot("console-ship-G3-waiting", Vector3.zero);
            refusal = Day.LastRefusal.Serial; trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial;
            Say("host pulls: " + H.ClientPullLever("Ship"));
            yield return ExpectRefusal(refusal, ConsoleRules.NotAboard(them), "G3 the host's pull");
            Check(!Day.Travelling && Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls, "G3 nothing sailed, no pull counted");
            yield return Expect(() => Text() == ConsoleRules.NotAboard(them) && NoticeShown() && Bottom().Contains(ConsoleRules.NotAboard(them).ToUpperInvariant()), 2f, () => "G3 the refusal on the host's screen and text: " + Bottom());
            yield return GuestEventually(r => GuestHeader(r).Contains("monitor=" + ConsoleRules.NotAboard(them) + ";") && GuestConsole(r).Contains(ConsoleRules.NotAboard(them).ToUpperInvariant()) && GuestConsole(r).Contains("NOTICE"), 4f, "G3 the guest's screen shows the same refusal");
            yield return GuestMove(guestDeck);
            yield return Expect(() => hqShip.IsSafelyAboard(remote.transform.position), 5f, () => "G3 the guest is aboard again");
            yield return Expect(() => !NoticeShown() && Sign() == "CONFIRM/on" && Bottom().EndsWith("READY"), refusalSeconds + 3f, () => "G3 READY once the guest is aboard: " + Bottom() + " | " + Sign());

            Heading("G5 — the guest buys a site: the unlock and the balance replicate; the lever swings on the guest");
            int price03 = sites.UnlockPrice(SiteId.Site03);
            Day.ServerSetBalanceForChecks(price03); yield return null;
            yield return Send("{\"id\":{id},\"action\":\"select\",\"item\":\"Site03\"}");
            yield return Expect(() => Day.SelectedSite == SiteId.Site03 && Sign() == $"UNLOCK ${price03}/on", 3f, () => "G5 the guest selected the locked Site 03; UNLOCK lit on the host: " + Sign());
            yield return GuestEventually(r => GuestConsole(r).Contains($"leverSign=UNLOCK ${price03}/on;") && GuestConsole(r).Contains("PULL TO UNLOCK"), 4f, "G5 UNLOCK lit on the guest too");
            pulls = Day.LastLeverPull.Serial;
            yield return Send("{\"id\":{id},\"action\":\"lever\",\"item\":\"Ship\"}");
            Check(lastReply.Contains("requested lever Ship/Unlock/Site03"), "G5 the guest's pull went with the right expectation: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.IsOpen(SiteId.Site03) && Day.Balance == 0 && Day.LastLeverPull.Serial == pulls + 1, 3f, () => "G5 the guest's pull bought Site 03 once: " + Destinations.MaskText(Day.UnlockedSites) + " $" + Day.Balance);
            float angleInReply = GuestAngle(lastReply);
            Say($"the guest's own reply (0.4 s after its pull): leverPlayed/leverAngle → {GuestConsole(lastReply).Split(new[] { "leverPulls=" }, StringSplitOptions.None).Last().Split(new[] { "leverSign=" }, StringSplitOptions.None)[0]}");
            yield return ExpectSwing(pulls + 1, "G6 host"); // measured at once: the swing lasts 0.6 s and a snapshot round trip would miss its peak
            yield return GuestEventually(r => GuestConsole(r).Contains($"leverPulls={pulls + 1}/Ship/Unlock;") && GuestConsole(r).Contains($"leverPlayed={pulls + 1};") && GuestConsole(r).Contains("unlocked=Site03;") && GuestHeader(r).Contains("balance=0;"), 6f, "G6 the guest saw the pull serial, played it, and reads the unlock and the balance");
            Check(!float.IsNaN(angleInReply) && angleInReply > 0f, $"G6 the guest's lever was mid-swing 0.4 s after its pull ({angleInReply:0.#}°)");
            yield return GuestEventually(r => GuestCardIs(r, "SITE 03", true, false, false) && GuestConsole(r).Contains("leverSign=CONFIRM/on;"), 4f, "G5 the guest's card lost its lock; CONFIRM lit");

            Heading("G5b — host and guest pull UNLOCK in the same editor frame for one locked site: charged once, one pull counted, the loser refused");
            Day.ServerSetBalanceForChecks(price02); yield return null;
            Say("host selects Site 02: " + H.ClientSelect("Site02"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site02 && Sign() == $"UNLOCK ${price02}/on", 3f, () => "G5b Site 02 selected, UNLOCK lit: " + Sign());
            yield return GuestEventually(r => GuestConsole(r).Contains($"leverSign=UNLOCK ${price02}/on;"), 4f, "G5b the guest's sign agrees");
            pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            Command("{\"id\":{id},\"action\":\"lever\",\"item\":\"Ship\"}");
            string hostPull = H.ClientPullLever("Ship");
            Say("same editor frame: guest lever written, host " + hostPull);
            yield return AwaitReply();
            yield return Expect(() => Day.IsOpen(SiteId.Site02), 3f, () => "G5b Site 02 unlocked");
            yield return Wait(1.0f);
            Check(Day.Balance == 0 && Day.LastLeverPull.Serial == pulls + 1, $"G5b charged once (${Day.Balance}), one pull counted ({Day.LastLeverPull.Serial - pulls})");
            yield return Expect(() => Day.LastRefusal.Serial > refusal, 3f, () => "G5b the loser was refused: '" + Day.LastRefusal.Text + "'");
            Check(Day.LastRefusal.Text == ConsoleRules.AlreadyOpen || Day.LastRefusal.Text == ConsoleRules.LeverInUse, "G5b the loser's reason: " + Day.LastRefusal.Text);
            yield return GuestEventually(r => GuestConsole(r).Contains("unlocked=Site02+Site03;") && GuestHeader(r).Contains("balance=0;") && GuestConsole(r).Contains($"leverPulls={pulls + 1}/"), 6f, "G5b the guest agrees: both sites open, $0, one pull");
            yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "G5b the notice cleared");

            Heading("G9 — the guest changes the selection right before the host's pull: 'Selection changed', nothing sails");
            Say("host selects Site 01: " + H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && Sign() == "CONFIRM/on", 3f, () => "G9 Site 01 selected, CONFIRM: " + Sign());
            yield return Send("{\"id\":{id},\"action\":\"select\",\"item\":\"Site03\"}");
            yield return Expect(() => Day.SelectedSite == SiteId.Site03, 3f, () => "G9 the guest moved the selection to Site 03");
            refusal = Day.LastRefusal.Serial; trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial;
            Say("host's stale pull: " + H.ClientPullLeverExpecting("Ship", "Confirm", "Site01"));
            yield return ExpectRefusal(refusal, ConsoleRules.SelectionChanged, "G9 the stale pull");
            Check(!Day.Travelling && Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls && Day.SelectedSite == SiteId.Site03, "G9 nothing sailed, the guest's selection stands");
            yield return GuestEventually(r => GuestConsole(r).Contains("SELECTION CHANGED") && GuestConsole(r).Contains("selected=Site03;"), 4f, "G9 the guest's screen shows the refusal and its own selection");
            yield return Expect(() => !NoticeShown(), refusalSeconds + 1.5f, () => "G9 the notice cleared");

            Heading("G4 — the guest pulls CONFIRM: one trip, SAILING TO SITE 01 on both peers, the pull serial once, the guest's lever swings; both clear on arrival");
            yield return Send("{\"id\":{id},\"action\":\"select\",\"item\":\"Site01\"}");
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && Sign() == "CONFIRM/on" && Bottom().EndsWith("READY"), 3f, () => "G4 Site 01 selected by the guest, READY: " + Bottom());
            trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial;
            yield return Send("{\"id\":{id},\"action\":\"lever\",\"item\":\"Ship\"}");
            Check(lastReply.Contains("requested lever Ship/Confirm/Site01"), "G4 the guest's CONFIRM went: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1, 3f, () => "G4 the guest's pull was accepted (serial " + Day.LastLeverPull.Serial + ")");
            yield return ExpectSwing(pulls + 1, "G4 host"); // measured at once (the swing lasts 0.6 s)
            yield return Expect(() => Day.Departure.Serial == trips + 1 && Day.Travelling, 4f, () => "G4 one trip started by the guest's pull (serial " + Day.Departure.Serial + ")");
            Check(Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Action == LeverAction.Confirm && Day.SelectedSite == SiteId.None, "G4 the pull counted once, the selection cleared");
            angleInReply = GuestAngle(lastReply);
            Check(GuestConsole(lastReply).Contains($"leverPulls={pulls + 1}/Ship/Confirm;") && GuestConsole(lastReply).Contains($"leverPlayed={pulls + 1};"), "G6 the guest's reply carries the pull and has played it: " + GuestConsole(lastReply).Split(new[] { "leverSign=" }, StringSplitOptions.None)[0]);
            Check(!float.IsNaN(angleInReply) && angleInReply > 0f, $"G6 the guest's lever mid-swing 0.4 s after the pull ({angleInReply:0.#}°)");
            yield return Expect(() => Bottom().EndsWith("SAILING TO SITE 01"), 3f, () => "G4 the host's screen: " + Bottom());
            yield return GuestEventually(r => GuestConsole(r).Contains("SAILING TO SITE 01") && GuestConsole(r).Contains("selected=None;") && GuestConsole(r).Contains("toSite=Site01;") && !GuestTop(r).Contains("@"), 6f, "G4 the guest's screen: SAILING TO SITE 01, nothing selected, HERE on no card");
            yield return Arrive(WorldId.Sea, "G4");
            seaShip = ShipParts.InWorld(WorldId.Sea);
            Check(Day.CurrentSite == SiteId.Site01 && Day.SelectedSite == SiteId.None && Day.Day == 1, "G4 at Site 01 on day 1, nothing selected");
            yield return GuestEventually(r => GuestHeader(r).Contains("world=Sea") && GuestHeader(r).Contains("phase=AtSea") && GuestConsole(r).Contains("selected=None;") && GuestConsole(r).Contains("site=Site01;") && GuestCardIs(r, "SITE 01", false, false, true) && GuestConsole(r).Contains("unlocked=Site02+Site03;") && GuestConsole(r).Contains("SELECT A DESTINATION"), 20f, "G4 the guest re-entered the console at sea: HERE on SITE 01, the unlocks kept, nothing selected");

            Heading("G7 — a late joiner sees the state at once: the guest leaves, the host selects the locked Site 04, the guest returns");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => GuestCopy() == null, 15f, () => "G7 the guest left");
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            yield return Wait(1.0f);
            yield return AtConsole();
            Day.ServerSetBalanceForChecks(0); yield return null;
            Say("host selects Site 04: " + H.ClientSelect("Site04"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site04 && Sign() == $"UNLOCK ${sites.UnlockPrice(SiteId.Site04)}/off", 3f, () => "G7 Site 04 selected, locked: " + Sign());
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "G7 the guest spawned again");
            remote = GuestCopy(); guestId = remote.OwnerId; them = WorldSceneFlow.DisplayName(guestId);
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
            string firstSnap = lastReply;
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && GuestHeader(r).Contains("world=Sea"), 20f, "G7 the joiner is at sea");
            Say("the joiner's first snapshot console line: " + GuestConsole(firstSnap));
            yield return GuestEventually(r => GuestConsole(r).Contains("selected=Site04;") && GuestConsole(r).Contains("unlocked=Site02+Site03;") && GuestConsole(r).Contains("site=Site01;") && GuestConsole(r).Contains($"leverSign=UNLOCK ${sites.UnlockPrice(SiteId.Site04)}/off;") && GuestCardIs(r, "SITE 04", true, true, false) && GuestCardIs(r, "SITE 01", false, false, true) && GuestConsole(r).Contains("topScreen=NAVIGATION"), 10f, "G7 the joiner's console: the selection, the locks, HERE, the dim UNLOCK, the screens composed");
            Check(GuestConsole(lastReply).Contains("leverPlayed=0;"), "G7 the joiner plays no pull that preceded it (leverPlayed=0)");

            Heading("H19 — the old server sail is still the checks' path: everyone aboard, ServerSail(HQ) sails home; both consoles read the dock on arrival");
            Vector3 seaDeck = seaShip.FromShipLocal(new Vector3(2.5f, 0f, 4f));
            yield return GuestMove(seaDeck);
            yield return Expect(() => seaShip.IsSafelyAboard(remote.transform.position), 5f, () => "H19 the guest stands on the sea ship's deck");
            yield return AtConsole();
            trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial;
            string sail = H.ServerSail("HQ");
            Check(sail == "sailing to HQ", "H19 the compat sail: " + sail);
            yield return Expect(() => Day.Departure.Serial == trips + 1 && Day.Travelling, 4f, () => "H19 sailing");
            Check(Day.SelectedSite == SiteId.None && Day.LastLeverPull.Serial == pulls, "H19 the compat sail cleared the selection and counted no lever pull");
            yield return Arrive(WorldId.HQ, "H19");
            Check(Day.World == WorldId.HQ && Day.CurrentSite == SiteId.HQ && Day.Day == 1, "H19 docked on day 1");
            yield return AtConsole();
            yield return Expect(() => CardIs("HQ", false, false, true) && Top().StartsWith($"NAVIGATION · DAY 1/{days}") && Text().StartsWith($"Docked at HQ — day 1 of {days}"), 3f, () => "H19 the docked console: " + Top() + " | " + Text());
            yield return GuestEventually(r => GuestHeader(r).Contains("world=HQ") && GuestHeader(r).Contains($"monitor=Docked at HQ — day 1 of {days}") && GuestConsole(r).Contains("site=HQ;") && GuestCardIs(r, "HQ", false, false, true), 20f, "H19 the guest reads the dock too");

            Heading("H18 — the save keeps the unlocks: the capture writes the mask, a restore brings it back");
            var captured = new RunSaveData();
            Day.ServerCapture(captured);
            Check(captured.unlockedSites == Day.UnlockedSites && (captured.unlockedSites & Destinations.Bit(SiteId.Site02)) != 0 && (captured.unlockedSites & Destinations.Bit(SiteId.Site03)) != 0 && !captured.IsFresh, "H18 the captured run lists the bought sites: " + Destinations.MaskText(captured.unlockedSites));
            string json = JsonUtility.ToJson(captured);
            Check(json.Contains("\"unlockedSites\":" + captured.unlockedSites), "H18 the slot's JSON carries unlockedSites");
            H.ServerRelock(); yield return null;
            Check(Day.UnlockedSites == 0, "H18 relocked for the restore");
            Day.ServerRestore(JsonUtility.FromJson<RunSaveData>(json)); yield return null;
            Check(Day.UnlockedSites == captured.unlockedSites && Day.SelectedSite == SiteId.None && Day.Phase == DayPhase.AtHQ && Day.Day == 1, "H18 the restore brought the unlocks back: " + Destinations.MaskText(Day.UnlockedSites));
            yield return GuestEventually(r => GuestConsole(r).Contains("unlocked=Site02+Site03;") && GuestCardIs(r, "SITE 02", false, false, false), 6f, "H18 the guest reads the restored unlocks");
            Say("a re-host from a slot (SaveRuntimeChecks.ReHost) is the save job's; marked for the final regression");

            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => GuestCopy() == null, 15f, () => "the guest left at the end");
            Say("end: " + H.ConsoleStatus());
        }
    }
}
