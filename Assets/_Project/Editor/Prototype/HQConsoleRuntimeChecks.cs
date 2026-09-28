using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
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
    // The HQ quota console (the shared console, Dan, 27 September 2026; scratchpad
    // console/BRIEF.md "TESTING REQUIREMENTS — HQ", TESTPLAN.md §3.2). Host-only rows
    // first (Q): nothing payable yet (the dim PAY, the reason on the screen, the pull
    // refused with no pay serial change), PAY with enough money (a coin sold from the
    // docked ship's storage room through the real path, PAID, the lever swings once),
    // repeated PAY, an empty room before payday, SHORT BY $n with sailing allowed
    // again, the same-frame double pay (server path and the lever dispatcher), the
    // payday failure (THE RUN IS OVER, the plank), the lever and the card during the
    // plank and on the run-over card, GIVE UP with the host alone (unanimous at once).
    // Then two guests (G): the card's 0/2 on both peers, a vote 1/2 with YOU VOTED on
    // the voter's screen only, the take-back, one player refusing, a player joining
    // mid-vote (cleared, n grows), leaving mid-vote and a killed guest process mid-vote
    // (cleared, n shrinks), a same-frame PAY from host and guest (one sale), the vote
    // cleared by a sail, all vote (the plank for everyone), the vote cleared by a fresh
    // run after a lost payday, and three same-frame card presses (one run over).
    // Every row asserts replicated state (CrewDayState on the host, the guests'
    // snapshots) and the visible text (the composer's models, the compat Text).
    // Log: Temp/console-hq-matrix.log. Started by CameraClearanceMatrixDriver.Start("console-hq").
    public static class HQConsoleRuntimeChecks
    {
        private const string Log = "Temp/console-hq-matrix.log";
        private const string DirA = "Temp/console-hq-guest-a";
        private const string DirB = "Temp/console-hq-guest-b";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Captures = "Temp/look/";

        private sealed class Guest
        {
            public string Dir; public Process Process; public int Id = -1; public string Name = string.Empty;
            public override string ToString() => Dir + "#" + Id;
        }

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Guest guestA, guestB;
        private static int guestCommand = 3800;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static readonly List<string> logs = new();
        private static int exceptionsSeen, errorsSeen;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run console-hq matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Console-hq matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Console-hq matrix started " + DateTime.Now + "\n");
            Status = "Running";
            logs.Clear(); exceptionsSeen = 0; errorsSeen = 0;
            Application.logMessageReceived += OnLog;
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            EditorApplication.update += Tick;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception) exceptionsSeen++;
            if (type == LogType.Error || type == LogType.Assert) errorsSeen++;
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert || message.Contains("[GiveUp]") || message.Contains("[Plank]") || message.Contains("[Console]") || message.Contains("Pay: sold"))
                logs.Add("[" + type + "] " + message.Split('\n')[0]);
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
            try { foreach (string l in logs.Where(l => !l.StartsWith("[Log]"))) File.AppendAllText(Log, "  log " + l + "\n"); } catch (Exception) { }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Console-hq matrix: MATRIX_PASS"); else Debug.LogError("Console-hq matrix: " + Status);
            foreach (Guest g in new[] { guestA, guestB })
                try { if (g?.Process != null && !g.Process.HasExited) g.Process.Kill(); } catch (Exception) { }
            guestA = guestB = null;
            steps = null;
            stack.Clear();
            Application.logMessageReceived -= OnLog;
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
        private static string State() => H.FlowStatus() + "\n" + H.ConsoleStatus() + "\n" + H.HQScreenText() + "\nText=" + H.QuotaBoardText().Replace("\n", " | ") + "\nlever=" + H.LeverPullText() + "\n" + H.GiveUpStatus();
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + State());
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
        // Holds for the whole window (the "nothing happens" rows).
        private static IEnumerator Hold(Func<bool> condition, float seconds, Func<string> label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline) { if (!condition()) Check(false, label()); yield return null; }
            Check(condition(), label());
        }
        private static void Keys(params Key[] pressed) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
        private static IEnumerator Press(Key key, float holdSeconds = 0.05f)
        {
            Keys(key); yield return Wait(holdSeconds); Keys(); yield return null;
        }

        private static Guest LaunchGuest(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(dir, stale))) File.Delete(Path.Combine(dir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-screen-width 960 -screen-height 540 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(dir) + "\" -logFile \"" + Path.GetFullPath(dir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return new Guest { Dir = dir, Process = Process.Start(info) };
        }
        // Waits for the guest's player to spawn on the host (a copy nobody known owns) and its peer to answer.
        private static IEnumerator Joined(Guest g, string label)
        {
            HashSet<int> known = new(new[] { guestA, guestB }.Where(x => x != null && x != g && x.Id >= 0).Select(x => x.Id));
            HQPlayerController copy = null;
            float deadline = Time.unscaledTime + 45f;
            while (Time.unscaledTime < deadline)
            {
                copy = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner && !known.Contains(p.OwnerId));
                if (copy != null) break;
                yield return null;
            }
            Check(copy != null, label + ": the guest's player spawned on the host");
            g.Id = copy.OwnerId;
            yield return GuestEventually(g, r => GuestPlayerLine(r, g.Id).Contains("local=True") && GuestHeader(r).Contains("world=HQ") && GuestHeader(r).Contains("phase=AtHQ"), 25f, label + ": the guest's peer answers at HQ");
            g.Name = WorldSceneFlow.DisplayName(g.Id);
            Say(label + ": guest '" + g.Name + "' is client " + g.Id + " (" + g.Dir + ")");
        }
        private static HQPlayerController CopyOf(Guest g) => g == null ? null : UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner && p.OwnerId == g.Id);
        private static int SpawnedPlayers() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Count(p => p.IsSpawned);

        // The guest polls its file every frame; a write can collide with its read.
        private static void Command(Guest g, string json)
        {
            string text = json.Replace("{id}", (++guestCommand).ToString());
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(Path.Combine(g.Dir, "command.json"), text); return; }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(15); }
            }
        }
        private static IEnumerator AwaitReply(Guest g, int id, float seconds = 10f)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                string reply = Reply(g);
                if (reply.StartsWith("id=" + id + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest " + g + " did not answer command " + id + "\n" + Reply(g));
        }
        private static IEnumerator Send(Guest g, string json)
        {
            Command(g, json);
            yield return AwaitReply(g, guestCommand);
        }
        private static string Reply(Guest g)
        {
            try { string p = Path.Combine(g.Dir, "reply.txt"); return File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
            catch (IOException) { return string.Empty; }
        }
        private static IEnumerator GuestEventually(Guest g, Func<string, bool> predicate, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send(g, "{\"id\":{id},\"action\":\"snapshot\"}");
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.4f);
            }
            throw new Exception(label + "\n" + GuestHeader(lastReply) + "\n" + GuestLine(lastReply, "giveup=") + "\n" + GuestConsole(lastReply) + "\n" + State());
        }
        private static string GuestLine(string reply, string prefix) => reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        private static string GuestHeader(string reply) => GuestLine(reply, "server=");
        private static string GuestConsole(string reply) => GuestLine(reply, "selected=");
        private static string GuestVote(string reply) => GuestLine(reply, "giveup=");
        private static string GuestPlayerLine(string reply, int ownerId) => GuestLine(reply, "player=" + ownerId + ";");
        // "key=value; " on the console line (the values carry " · " but never "; ").
        private static string Field(string line, string key)
        {
            string k = key + "=";
            int start;
            if (line.StartsWith(k, StringComparison.Ordinal)) start = k.Length;
            else
            {
                int i = line.IndexOf(" " + k, StringComparison.Ordinal);
                if (i < 0) return "(absent)";
                start = i + 1 + k.Length;
            }
            int end = line.IndexOf("; ", start, StringComparison.Ordinal);
            if (end < 0) end = line.IndexOf(';', start);
            return end < 0 ? line.Substring(start) : line.Substring(start, end - start);
        }
        private static string GTop(string reply) => Field(GuestConsole(reply), "hqTop");
        private static string GBottom(string reply) => Field(GuestConsole(reply), "hqBottom");
        private static string GSign(string reply) => Field(GuestConsole(reply), "hqSign");
        private static string GCard(string reply) => Field(GuestConsole(reply), "hqCard");
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static IEnumerator GuestMove(Guest g, Vector3 to) { yield return Send(g, "{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }

        // ---- the console under test -----------------------------------------------------

        private static HQQuotaConsole Console() => HQQuotaConsole.InHQ();
        private static TopModel TopM() => Console()?.Top;
        private static VoteCardModel Card() => Console()?.Bottom?.VoteCard;
        private static string Top() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Top);
        private static string Bottom() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Bottom);
        private static string Sign() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Sign);
        private static string Text() => H.QuotaBoardText();
        private static string Big() => TopM()?.BigState ?? "(none)";
        private static string CardText() { VoteCardModel c = Card(); return c == null ? "(no card)" : $"{c.Word} {c.Count} foot='{c.Foot}' voted={c.LocalVoted} enabled={c.Enabled}"; }

        // In front of the HQ rig, standing where a reader stands; `side` metres to the rig's right.
        private static Vector3 StandSpot(float side)
        {
            Transform rig = Console().transform;
            Vector3 front = rig.rotation * Vector3.forward; front.y = 0f; front.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, front);
            Vector3 at = rig.position + front * 1.6f + right * side;
            at.y = 0.05f;
            return at;
        }
        private static IEnumerator AtBoard()
        {
            string moved = H.ClientMoveLocalPlayerToBoard();
            Check(moved.StartsWith("moved"), "stood at the quota console: " + moved);
            yield return null; yield return null; yield return null;
        }
        private static IEnumerator Aim(string objectName, ConsoleControlKind kind)
        {
            HQPlayerController host = Host();
            float deadline = Time.unscaledTime + 4f; int tries = 0;
            while (Time.unscaledTime < deadline)
            {
                ConsoleControl c = host.CurrentConsoleControl;
                if (c != null && c.Kind == kind && c.Console == ConsoleKind.HQ) break;
                if (tries++ % 45 == 0) H.ClientMoveLocalPlayerToBoard();
                H.ClientLookAtNamed(objectName);
                yield return null;
            }
            ConsoleControl aimed = host.CurrentConsoleControl;
            Check(aimed != null && aimed.Kind == kind && aimed.Console == ConsoleKind.HQ, "the dot is on " + objectName + " (aim " + (aimed == null ? "none" : aimed.Console + "/" + aimed.Kind) + ")");
        }
        private static IEnumerator AimLever() => Aim(WorldSceneFlow.HQLeverObjectName, ConsoleControlKind.Lever);
        private static IEnumerator AimCard() => Aim("Give Up Button", ConsoleControlKind.GiveUpCard);
        private static IEnumerator ExpectRefusal(int serialBefore, string text, string label)
        {
            yield return Expect(() => Day.LastRefusal.Serial > serialBefore, 3f, () => label + ": refused '" + Day.LastRefusal.Text + "'");
            Check(Day.LastRefusal.Text == text, label + ": the refusal reads '" + Day.LastRefusal.Text + "' (expected '" + text + "')");
        }
        // The HQ rig swings for the pull on this peer: the played serial and a peak angle.
        private static readonly List<string> sampled = new();
        private static IEnumerator ExpectSwing(int serial, string label, Func<string> sample = null)
        {
            float peak = 0f; float start = Time.unscaledTime, deadline = start + 1.2f;
            HQQuotaConsole c = Console();
            sampled.Clear();
            string lastSample = null;
            while (Time.unscaledTime < deadline)
            {
                if (c != null) peak = Mathf.Max(peak, c.LeverAngle);
                if (sample != null) { string s = sample(); if (s != lastSample) { sampled.Add($"{Time.unscaledTime - start:0.00}s {s}"); lastSample = s; } }
                yield return null;
            }
            Check(c != null && c.LeverPlayedSerial == serial && peak > 0f, $"{label}: the HQ rig swung its lever for pull {serial} (played {(c == null ? -1 : c.LeverPlayedSerial)}, peak {peak:0.#}°)");
        }
        private static IEnumerator Shot(string name)
        {
            HQQuotaConsole c = Console();
            if (c == null) yield break;
            Vector3 front = c.transform.rotation * Vector3.forward; front.y = 0f; front.Normalize();
            Vector3 eye = c.transform.position + front * 1.6f + Vector3.up * 1.6f;
            Say("capture " + H.CaptureFrom(eye, c.transform.position + Vector3.up * 1.0f, Captures + name + ".png"));
            yield return null;
        }
        // A coin (CoinMedium, rolled $40–90) spawned server side inside the docked ship's storage room.
        private static IEnumerator CoinInRoom(string name, Action<CarryableItem> got)
        {
            ShipParts ship = ShipParts.InWorld(WorldId.HQ);
            Check(ship != null, name + ": the docked ship");
            NetworkManager nm = InstanceFinder.NetworkManager;
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == "CoinMedium") { prefab = candidate; break; }
            }
            Check(prefab != null, name + ": the CoinMedium prefab is registered");
            Vector3 at = ship.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f));
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            instance.name = name;
            CarryableItem item = instance.GetComponent<CarryableItem>();
            item.SetResetPositionBeforeSpawn(at);
            nm.ServerManager.Spawn(instance, null, ship.gameObject.scene);
            yield return Wait(1.2f);
            Check(item.IsSpawned && item.Value > 0 && ship.IsInStorageRoom(item.transform.position) && item.gameObject.scene == WorldScenes.Scene(WorldId.HQ), $"{name}: a ${item.Value} coin lies in the docked ship's storage room");
            yield return Expect(() => Day.BoxValue == StorageReadout.SumInside(ship) && Day.BoxValue >= item.Value, 2f, () => $"{name}: the box reads ${Day.BoxValue}");
            got(item);
        }
        private static bool Gone(CarryableItem item) => item == null || !item.IsSpawned;
        private static int Count(string contains) => logs.Count(l => l.Contains(contains));

        // Everyone off the plank quickly: whoever is on the board steps off its end.
        private static IEnumerator RideOutPlank(string label)
        {
            HQPlank plank = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
            Check(plank != null, label + ": the plank");
            float turn = Flow.Settings.PlankTurnSeconds, card = Flow.Settings.RunOverCardSeconds;
            float deadline = Time.unscaledTime + 5f * (turn + 6f) + card + 20f;
            int movedFor = -2;
            while (Day.Phase == DayPhase.Plank && Time.unscaledTime < deadline)
            {
                PlankState p = Day.Plank;
                if (p.Active && p.Jumper >= 0 && p.Jumper != movedFor)
                {
                    int jumper = p.Jumper;
                    yield return Wait(0.8f); // placed at the base first
                    if (Day.Plank.Jumper != jumper) continue;
                    Vector3 off = plank.End.position + plank.End.forward * 0.8f + Vector3.up * 0.1f;
                    if (jumper == Host().OwnerId) Host().TeleportLocal(off, plank.WalkYaw);
                    else
                    {
                        Guest g = new[] { guestA, guestB }.FirstOrDefault(x => x != null && x.Id == jumper && x.Process != null && !x.Process.HasExited);
                        if (g != null) yield return GuestMove(g, off);
                    }
                    movedFor = jumper;
                    Say($"{label}: {WorldSceneFlow.DisplayName(jumper)} steps off the plank");
                }
                yield return null;
            }
            Check(Day.Phase == DayPhase.AtHQ, label + ": the fresh run after the plank (phase " + Day.Phase + ")");
            Check(Day.Day == 0 && Day.Balance == 0 && Day.CycleSales == 0 && !Day.Payday && Day.GiveUpVotes == 0 && Day.GiveUpCrew == 0, label + $": day 0, $0, no cycle, no votes ({Day.GiveUpVotes}/{Day.GiveUpCrew})");
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 8f, () => label + ": the screen clear again");
            yield return Wait(0.5f);
        }
        private static IEnumerator Arrive(WorldId id, string label)
        {
            yield return Expect(() => Day.World == id && !Flow.Transitioning && Day.Phase != DayPhase.Sailing && Day.Phase != DayPhase.SailingHome && !Host().TravelLocked, 80f, () => label + ": arrived at " + id + " (phase " + Day.Phase + ")");
            yield return Wait(0.8f);
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
            keyboard = InputSystem.AddDevice<Keyboard>("ConsoleHQCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            string me = WorldSceneFlow.DisplayName(host.OwnerId);
            int days = flow.Settings.DaysPerCycle, quota = flow.Settings.QuotaPerCycle;
            float refusalSeconds = flow.Settings.RefusalDisplaySeconds, reportSeconds = flow.Settings.PayReportSeconds;
            Say($"host '{me}', {days} days a cycle, quota ${quota}, refusals shown {refusalSeconds} s, pay reports {reportSeconds} s, plank turn {flow.Settings.PlankTurnSeconds} s, card {flow.Settings.RunOverCardSeconds} s");
            int refusal, paySerial, pulls, runOvers, balance;

            Heading("Q0 — nothing payable yet: the dim PAY, the reason on the prompt and the screen, the pull refused with no pay and no pull counted");
            HQQuotaConsole console = Console();
            Check(console != null && console.GetComponent<ConsoleRig>() != null && console.GetComponent<ConsoleLever>() != null && console.gameObject.scene == WorldScenes.Scene(WorldId.HQ), "Q0 the HQ console stands in the HQ scene with its rig and lever");
            Check(Day.Phase == DayPhase.AtHQ && Day.Day == 0 && !Day.Payday && Day.LastPay.Serial == 0 && Day.GiveUpVotes == 0, "Q0 fresh run: " + H.FlowStatus());
            yield return AtBoard();
            yield return Expect(() => TopM() != null && TopM().Title == "QUOTA BOARD" && Big() == "NEW CYCLE" && TopM().Hint == HQQuotaConsole.HintNewCycle, 2f, () => "Q0 top screen: " + Top());
            Check(TopM().FootLeft == $"QUOTA $0 / ${quota}" && TopM().FootLeftTone == ConsoleTone.Warn && TopM().FootRight == "BALANCE $0", "Q0 the quota amber, the balance: " + Top());
            Check(Sign() == "PAY/off", "Q0 the sign is a dim PAY: " + Sign());
            Check(H.ConsoleStatus().Contains("hqAction=None/off:" + ConsoleRules.NothingToPay), "Q0 the resolver: " + H.ConsoleStatus());
            Check(Text().StartsWith("NEW CYCLE"), "Q0 compat text: " + Text().Replace("\n", " | "));
            yield return AimLever();
            yield return null;
            Check(hud.PromptText == ConsoleRules.NothingToPay, "Q0 the dim lever's prompt explains: " + hud.PromptText);
            refusal = Day.LastRefusal.Serial; paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.NothingToPay, "Q0 E on the dim lever");
            yield return Expect(() => Big() == ConsoleRules.NothingToPay && TopM().BigTone == ConsoleTone.Warn && Text() == ConsoleRules.NothingToPay, 2f, () => "Q0 the top screen shows the reason large: " + Top());
            Check(Day.LastPay.Serial == paySerial && Day.LastLeverPull.Serial == pulls && Day.Balance == 0 && Console().LeverPlayedSerial == pulls, "Q0 no pay, no pull counted, the lever did not swing");
            yield return Shot("console-hq-Q0-refusal");
            yield return Expect(() => Big() == "NEW CYCLE", refusalSeconds + 1.5f, () => "Q0 the board returns to NEW CYCLE after the notice: " + Top());

            Heading("Q1 — PAY with enough money: a coin in the storage room sold through the real path, PAID, the lever swings once; then repeated PAY is refused");
            Day.ServerForceCycleForChecks(1, false);
            CarryableItem coin1 = null;
            yield return CoinInRoom("Q1 coin", c => coin1 = c);
            int v1 = coin1.Value;
            WorldLoopSettings.QuotaOverrideForTests = v1; // exactly the quota
            yield return Expect(() => Sign() == "PAY/on" && Big() == $"DAY 1 OF {days}" && TopM().Hint == HQQuotaConsole.HintDay && TopM().Corner == $"DAY 1/{days}" && TopM().FootLeft == $"QUOTA ${v1} / ${v1}", 2f, () => "Q1 PAY lit on day 1: " + Sign() + " | " + Top());
            Check(TopM().FootLeft == $"QUOTA ${v1} / ${v1}" && TopM().FootLeftTone == ConsoleTone.Good, "Q1 the quota counts the box and turns green when met: " + TopM().FootLeft + " " + TopM().FootLeftTone);
            yield return AimLever();
            yield return null;
            Check(hud.PromptText == $"Press E to pay the quota (${v1}) — sells the box (${v1})", "Q1 the lever's prompt: " + hud.PromptText);
            paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial; balance = Day.Balance;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastPay.Serial == paySerial + 1, 3f, () => "Q1 the pay went through (serial " + Day.LastPay.Serial + ")");
            PayReport r1 = Day.LastPay;
            Check(r1.Paid && !r1.Short && !r1.Lost && r1.Sales == v1 && r1.Had == v1 && r1.Quota == v1, $"Q1 PAID: sold ${r1.Sales}, had ${r1.Had} of ${r1.Quota}");
            Check(Day.Balance == balance + v1 && Day.Day == 0 && !Day.Payday && Day.CycleSales == 0, $"Q1 banked once: balance ${Day.Balance}, day {Day.Day}, the cycle closed");
            yield return Expect(() => Gone(coin1) && Day.BoxValue == 0, 2f, () => "Q1 the coin was sold (despawned), the box reads $0");
            Check(Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Kind == ConsoleKind.HQ && Day.LastLeverPull.Action == LeverAction.Pay, "Q1 one pull counted: " + H.LeverPullText());
            yield return ExpectSwing(pulls + 1, "Q1");
            yield return Expect(() => Big() == "PAID" && TopM().BigTone == ConsoleTone.Good && Text().StartsWith($"PAID ${v1}"), 2f, () => "Q1 the top screen: " + Top() + " | " + Text().Replace("\n", " | "));
            Check(Count("Pay: sold $" + v1 + ",") == 1, "Q1 the server logged one sale of $" + v1);
            yield return Shot("console-hq-Q1-paid");
            yield return Expect(() => Sign() == "PAY/off", 2f, () => "Q1 the lever dims once the cycle is paid: " + Sign());
            // Repeated PAY: E and the hook, right after.
            refusal = Day.LastRefusal.Serial; paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial; balance = Day.Balance;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.NothingToPay, "Q4 a second E right after PAID");
            // Retest HQ-2 (round 2): a refusal inside the pay report's window takes the top screen and the compat Text.
            Check(Time.unscaledTime - Day.LastPayAt < reportSeconds, $"HQ-2 retest: the refusal came {Time.unscaledTime - Day.LastPayAt:0.0} s into the {reportSeconds} s report");
            yield return Expect(() => Big() == ConsoleRules.NothingToPay && TopM().BigTone == ConsoleTone.Warn && Text() == ConsoleRules.NothingToPay, 1f, () => "HQ-2 retest: the top screen shows the refusal inside the PAID report's window: " + Top() + " | Text=" + Text().Replace("\n", " | "));
            refusal = Day.LastRefusal.Serial;
            Say("hook pull: " + H.ClientPullLever("HQ"));
            yield return ExpectRefusal(refusal, ConsoleRules.NothingToPay, "Q4 a third pull (hook)");
            refusal = Day.LastRefusal.Serial;
            Say("old pay path: " + H.ClientRequestPay());
            yield return ExpectRefusal(refusal, ConsoleRules.NothingToPay, "Q4 the old RequestPay path");
            Check(Day.LastPay.Serial == paySerial && Day.Balance == balance && Day.LastLeverPull.Serial == pulls, $"Q4 no second sale, no second bank (${Day.Balance}), no pull counted");
            Say($"the top screen during a refusal inside the pay report's window: {Big()} (the report has {reportSeconds} s)");
            {
                float back = Day.LastRefusalAt + refusalSeconds, reportEnd = Day.LastPayAt + reportSeconds;
                if (back < reportEnd - 0.6f)
                    yield return Expect(() => Big() == "PAID" && Text().StartsWith("PAID"), back - Time.unscaledTime + 0.5f, () => "HQ-2 retest: PAID returns after the refusal's window, inside the report's: " + Top());
                else Say($"HQ-2 retest: no time left in the report after the refusal (back {back:0.0}, report ends {reportEnd:0.0})");
            }
            yield return Expect(() => Big() == "NEW CYCLE", reportSeconds + refusalSeconds + 1f, () => "Q1 back to NEW CYCLE after the report: " + Top());

            Heading("Q2 — an empty storage room before payday: PAY dim with the reason, the pull refused, nothing sold");
            Day.ServerForceCycleForChecks(2, false);
            WorldLoopSettings.QuotaOverrideForTests = null;
            yield return Expect(() => Day.BoxValue == 0 && Sign() == "PAY/off" && Big() == $"DAY 2 OF {days}" && TopM().Hint == HQQuotaConsole.HintDayNothingToSell, 2f, () => "Q2 day 2, empty room: " + Sign() + " | " + Top());
            Check(H.ConsoleStatus().Contains("hqAction=None/off:" + ConsoleRules.NothingToSell), "Q2 the resolver: " + H.ConsoleStatus());
            yield return AimLever();
            yield return null;
            Check(hud.PromptText == ConsoleRules.NothingToSell, "Q2 the prompt: " + hud.PromptText);
            refusal = Day.LastRefusal.Serial; paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.NothingToSell, "Q2 E over an empty room");
            yield return Expect(() => Big() == ConsoleRules.NothingToSell, 2f, () => "Q2 the reason on the top screen: " + Top());
            Check(Day.LastPay.Serial == paySerial && Day.LastLeverPull.Serial == pulls && Day.Day == 2 && Day.CycleSales == 0, "Q2 nothing paid, the day kept");
            yield return Expect(() => Big() == $"DAY 2 OF {days}", refusalSeconds + 1.5f, () => "Q2 notice cleared: " + Top());

            Heading("Q3 — short but allowed to continue: SHORT BY $50 amber, the box banked, sailing allowed again, a second PAY refused (nothing to sell)");
            CarryableItem coin3 = null;
            yield return CoinInRoom("Q3 coin", c => coin3 = c);
            int v3 = coin3.Value;
            WorldLoopSettings.QuotaOverrideForTests = v3 + 50;
            yield return Expect(() => Sign() == "PAY/on" && TopM().FootLeft == $"QUOTA ${v3} / ${v3 + 50}" && TopM().FootLeftTone == ConsoleTone.Warn, 2f, () => "Q3 PAY lit, the quota amber: " + Sign() + " | " + Top());
            paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial; balance = Day.Balance;
            Say("pull: " + H.ClientPullLever("HQ"));
            // The swing, and the top screen sampled every frame from the pull on (the foot must never count the sold box twice).
            yield return ExpectSwing(pulls + 1, "Q3", () => (TopM().BigState ?? "") + " | " + TopM().FootLeft + "/" + TopM().FootLeftTone + " | box=" + Day.BoxValue + " handed=" + Day.CycleSales);
            List<string> q3Seen = new(sampled);
            Say("Q3 the top screen from the pull on: " + string.Join(" → ", q3Seen));
            Check(Day.LastPay.Serial == paySerial + 1, "Q3 the pay went through");
            PayReport r3 = Day.LastPay;
            Check(r3.Short && !r3.Paid && !r3.Lost && r3.Sales == v3, $"Q3 SHORT: sold ${r3.Sales}, had ${r3.Had} of ${r3.Quota}");
            Check(Day.Balance == balance + v3 && Day.CycleSales == v3 && Day.Day == 2 && Day.Phase == DayPhase.AtHQ, $"Q3 banked (${Day.Balance}), the cycle keeps ${Day.CycleSales}, still day 2");
            Check(Gone(coin3), "Q3 the coin was sold");
            Check(Big() == "SHORT BY $50" && TopM().BigTone == ConsoleTone.Warn && Text().StartsWith("SHORT BY $50"), "Q3 the top screen: " + Top() + " | " + Text().Replace("\n", " | "));
            Check(TopM().FootLeft == $"QUOTA ${v3} / ${v3 + 50}" && TopM().FootLeftTone == ConsoleTone.Warn, "Q3 the foot settles on the handed-over sale, amber: " + TopM().FootLeft);
            List<string> doubled = q3Seen.Where(s => s.Contains($"QUOTA ${2 * v3} /")).ToList();
            Check(doubled.Count == 0, "HQ-3 retest: the foot never counted the sold box twice from the pull on (every frame sampled)" + (doubled.Count == 0 ? string.Empty : ": " + string.Join(" → ", doubled)));
            Check(Day.ServerCanSail(WorldId.Sea, out string sailWhy), "Q3 sailing out is allowed again (" + sailWhy + ")");
            yield return Shot("console-hq-Q3-short");
            refusal = Day.LastRefusal.Serial; paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial; balance = Day.Balance;
            Say("second pull: " + H.ClientPullLever("HQ"));
            yield return ExpectRefusal(refusal, ConsoleRules.NothingToSell, "Q4 a second PAY after SHORT");
            Check(Time.unscaledTime - Day.LastPayAt < reportSeconds, $"HQ-2 retest: this refusal came {Time.unscaledTime - Day.LastPayAt:0.0} s into the SHORT report");
            yield return Expect(() => Big() == ConsoleRules.NothingToSell && TopM().BigTone == ConsoleTone.Warn && Text() == ConsoleRules.NothingToSell, 1f, () => "HQ-2 retest: the top screen shows the refusal inside the SHORT report's window: " + Top());
            {
                float back = Day.LastRefusalAt + refusalSeconds, reportEnd = Day.LastPayAt + reportSeconds;
                if (back < reportEnd - 0.6f)
                    yield return Expect(() => Big() == "SHORT BY $50", back - Time.unscaledTime + 0.5f, () => "HQ-2 retest: SHORT BY $50 returns after the refusal's window: " + Top());
                else Say($"HQ-2 retest: no time left in the SHORT report after the refusal (back {back:0.0}, report ends {reportEnd:0.0})");
            }
            Say("the old RequestPay path after SHORT: " + H.ClientRequestPay());
            yield return Wait(0.5f);
            Say("its answer: '" + Day.LastRefusal.Text + "', pay serial " + Day.LastPay.Serial + " (was " + paySerial + ")");
            Check(Day.LastLeverPull.Serial == pulls && Day.CycleSales == v3 && Day.Balance == balance, $"Q4 no second sale by the lever: the cycle keeps ${Day.CycleSales}, balance ${Day.Balance}");
            Check(Day.LastPay.Serial == paySerial || (Day.LastPay.Sales == 0 && Day.LastPay.Short), "Q4 the old path's second press sells nothing (serial " + Day.LastPay.Serial + ", sales $" + Day.LastPay.Sales + ")");
            yield return Wait(refusalSeconds + 0.5f);

            Heading("Q5 — the same-frame double PAY: two server pays and two lever pulls in one frame sell and bank once");
            Day.ServerForceCycleForChecks(1, false);
            CarryableItem coin5 = null;
            yield return CoinInRoom("Q5 coin", c => coin5 = c);
            int v5 = coin5.Value;
            WorldLoopSettings.QuotaOverrideForTests = Day.CycleSales + v5; // paid exactly
            paySerial = Day.LastPay.Serial; balance = Day.Balance;
            bool okA = flow.ServerPay(host.Owner, out string whyA), okB = flow.ServerPay(host.Owner, out string whyB);
            Check(okA && !okB && whyB == ConsoleRules.NothingToPay, $"Q5 same frame ServerPay ×2: {okA} '{whyA}', then {okB} '{whyB}'");
            Check(Day.LastPay.Serial == paySerial + 1 && Day.LastPay.Paid && Day.Balance == balance + v5 && Gone(coin5), $"Q5 one sale, one bank (+${Day.Balance - balance} of ${v5})");
            yield return Wait(0.3f);
            Day.ServerForceCycleForChecks(2, false);
            CarryableItem coin5b = null;
            yield return CoinInRoom("Q5b coin", c => coin5b = c);
            int v5b = coin5b.Value;
            WorldLoopSettings.QuotaOverrideForTests = 99999; // short: the day stays, so only the frame guard stops a second sale
            paySerial = Day.LastPay.Serial; balance = Day.Balance; pulls = Day.LastLeverPull.Serial; int sales = Day.CycleSales;
            yield return AtBoard();
            bool pA = flow.ServerPullLever(host.Owner, ConsoleKind.HQ, LeverAction.Pay, SiteId.None, out string pwA);
            bool pB = flow.ServerPullLever(host.Owner, ConsoleKind.HQ, LeverAction.Pay, SiteId.None, out string pwB);
            Check(pA && !pB && pwB == ConsoleRules.LeverInUse, $"Q5b same frame lever ×2: {pA} '{pwA}', then {pB} '{pwB}'");
            Check(Day.LastPay.Serial == paySerial + 1 && Day.LastPay.Short && Day.LastPay.Sales == v5b && Day.Balance == balance + v5b && Day.CycleSales == sales + v5b && Day.LastLeverPull.Serial == pulls + 1, $"Q5b one sale of ${v5b}, one short report, one pull ({Day.LastLeverPull.Serial - pulls})");
            // Retest HQ-3 / NET-1's root (round 2), still the pull's frame: the sold coin no longer counts in the room.
            {
                ShipParts q5Ship = ShipParts.InWorld(WorldId.HQ);
                int stale = CarryableItem.Spawned.Count(i => i != null && !i.IsSpawned);
                int sumNow = StorageReadout.SumInside(q5Ship);
                int balanceNow = Day.Balance, cycleNow = Day.CycleSales;
                bool payAgain = flow.ServerPay(host.Owner, out string payAgainWhy);
                Check(sumNow == 0 && (!payAgain || Day.LastPay.Sales == 0) && Day.Balance == balanceNow && Day.CycleSales == cycleNow, $"HQ-3 retest: in the sale's own frame the room sums ${sumNow} ({stale} despawned item(s) still listed); a second server pay sells nothing (ok={payAgain} '{payAgainWhy}', sales ${Day.LastPay.Sales}, balance ${Day.Balance}, cycle ${Day.CycleSales})");
                // The direct ServerPay (the RequestPay RPC) has no 'Nothing to sell' gate: a $0 SHORT report mid-cycle (bugs/HQ-4).
                if (payAgain) Say($"SOFT-FAIL HQ-4: the direct ServerPay over an empty room mid-cycle wrote a new report (serial {Day.LastPay.Serial}, sold ${Day.LastPay.Sales}, short={Day.LastPay.Short}) instead of refusing '{ConsoleRules.NothingToSell}'");
                else Check(payAgainWhy == ConsoleRules.NothingToSell, "HQ-4 retest: the direct ServerPay over an empty room is refused: " + payAgainWhy);
                paySerial = Day.LastPay.Serial - 1; // the rows below count from the lever's sale
            }
            yield return Wait(0.3f);
            bool pC = flow.ServerPullLever(host.Owner, ConsoleKind.HQ, LeverAction.Pay, SiteId.None, out string pwC);
            Check(!pC && pwC == ConsoleRules.NothingToSell && Day.LastPay.Serial == paySerial + 1, $"Q5b the next frame's pull finds nothing to sell: '{pwC}'");
            {
                int q5Double = sales + 2 * v5b; List<string> seen5 = new();
                float until5 = Time.unscaledTime + 1.5f;
                while (Time.unscaledTime < until5) { string foot = TopM().FootLeft; if (seen5.Count == 0 || seen5[seen5.Count - 1] != foot) seen5.Add(foot); yield return null; }
                Check(!seen5.Any(f => f.StartsWith($"QUOTA ${q5Double} /")) && seen5.Last().StartsWith($"QUOTA ${sales + v5b} /"), "HQ-3 retest: Q5b's foot after the sale: " + string.Join(" → ", seen5));
            }
            yield return Wait(reportSeconds + 0.5f);

            Heading("Q6 — payday failure: PAYDAY lit, the empty-room PAY loses the run: THE RUN IS OVER, the plank starts, sailing refused");
            Day.ServerForceCycleForChecks(days, true);
            WorldLoopSettings.QuotaOverrideForTests = 99999;
            yield return AtBoard();
            yield return Expect(() => Sign() == "PAY/on" && Big() == "PAYDAY" && TopM().Corner == "PAYDAY" && TopM().Hint == HQQuotaConsole.HintPayday, 2f, () => "Q6 PAYDAY, PAY lit over an empty room: " + Sign() + " | " + Top());
            Check(Text().StartsWith("PAYDAY"), "Q6 compat text: " + Text().Replace("\n", " | "));
            yield return AimLever();
            yield return null;
            Check(hud.PromptText.StartsWith("Press E to pay the quota ($99999)"), "Q6 the prompt: " + hud.PromptText);
            paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial; runOvers = Day.RunOver.Serial; int planks = Count("[Plank] The run is over");
            yield return Press(Key.E);
            yield return Expect(() => Day.LastPay.Serial == paySerial + 1 && Day.LastPay.Lost && Day.Phase == DayPhase.Plank, 3f, () => "Q6 short at payday: lost, the plank");
            Check(Day.LastPay.Sales == 0 && Day.Plank.Active && Day.Plank.Jumper == host.OwnerId, "Q6 sold $0, the host first on the plank");
            yield return Expect(() => Big() == "THE RUN IS OVER" && TopM().BigTone == ConsoleTone.Danger && Text().StartsWith("THE RUN IS OVER"), 2f, () => "Q6 the top screen: " + Top());
            yield return Expect(() => Sign() == "PAY/off" && Card() != null && !Card().Enabled, 2f, () => "Q6 the sign and the card dark: " + Sign() + " | " + CardText());
            Check(H.ConsoleStatus().Contains("hqAction=None/off:" + ConsoleRules.RunOver), "Q6 the resolver: " + H.ConsoleStatus());
            Check(!Day.ServerCanSail(WorldId.Sea, out sailWhy) && sailWhy == ConsoleRules.RunOver, "Q6 sailing is refused: " + sailWhy);
            Check(Count("[Plank] The run is over") == planks + 1, "Q6 one plank started");
            yield return Shot("console-hq-Q6-runover");

            Heading("Q7 — the plank already active: at the console the lever and the card are dim, their prompts say why, both presses refused, no vote");
            yield return Wait(1.0f);
            yield return AimLever();
            yield return null;
            Check(hud.PromptText.StartsWith("WALK THE PLANK"), "Q7 the jumper's prompt is the plank's (the non-jumper's console prompts are G11's): " + hud.PromptText);
            refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.RunOver, "Q7 E on the lever on the plank");
            yield return AimCard();
            yield return null;
            string cardPrompt = hud.PromptText;
            Say("Q7 the card's prompt on the plank: " + cardPrompt);
            Check(!cardPrompt.StartsWith("Press E"), "Q7 the card does not offer a vote on the plank: " + cardPrompt);
            refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastRefusal.Serial > refusal, 3f, () => "Q7 E on the card on the plank is refused");
            string cardWhy = Day.LastRefusal.Text;
            Check(cardWhy == ConsoleRules.RunOver, "HQ-1 retest: Q7 the card's refusal on the plank reads '" + cardWhy + "' (expected '" + ConsoleRules.RunOver + "')");
            refusal = Day.LastRefusal.Serial;
            Say("Q7 the hook vote on the plank: " + H.ClientRequestGiveUp());
            yield return ExpectRefusal(refusal, ConsoleRules.RunOver, "HQ-1 retest: Q7 ClientRequestGiveUp on the plank");
            Check(Day.GiveUpVotes == 0 && Day.Phase == DayPhase.Plank && Day.LastPay.Serial == paySerial + 1 && Day.RunOver.Serial == runOvers, "Q7 no vote, no second pay, no run-over yet");
            Check(Count("[Plank] The run is over") == planks + 1, "Q7 still one plank");
            H.ClientMoveLocalPlayerToBoard(); // leave the dot; the plank walk follows

            Heading("Q8 — the run already over (the card between the last jump and the fresh run): pay and vote refused, no second run-over");
            HQPlank plank = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
            host.TeleportLocal(plank.End.position + plank.End.forward * 0.8f + Vector3.up * 0.1f, plank.WalkYaw);
            yield return Expect(() => Day.RunOver.Serial == runOvers + 1, 8f, () => "Q8 the run-over card was sent");
            Check(Day.Phase == DayPhase.Plank, "Q8 still the plank phase under the card");
            Check(!flow.ServerPay(host.Owner, out string q8Pay), "Q8 ServerPay refused: " + q8Pay);
            Check(!flow.ServerToggleGiveUp(host.Owner, out string q8Vote) && Day.GiveUpVotes == 0, "Q8 a vote refused: " + q8Vote);
            Check(ConsoleRules.Expected(ConsoleKind.HQ, null, out _, out bool q8On, out string q8Why) == LeverAction.None && !q8On && q8Why == ConsoleRules.RunOver, "Q8 the lever reads dim: " + q8Why);
            Say($"Q8 refusals: pay '{q8Pay}', vote '{q8Vote}'");
            Check(q8Pay == ConsoleRules.RunOver && q8Vote == ConsoleRules.RunOver, $"HQ-1 retest: Q8 under the run-over card, pay '{q8Pay}' and vote '{q8Vote}' both read '{ConsoleRules.RunOver}'");
            yield return Expect(() => Day.Phase == DayPhase.AtHQ, flow.Settings.RunOverCardSeconds + 6f, () => "Q8 the fresh run");
            Check(Day.RunOver.Serial == runOvers + 1 && Count("[Plank] The run is over") == planks + 1, "Q8 exactly one run-over and one plank");
            WorldLoopSettings.QuotaOverrideForTests = null;
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 8f, () => "Q8 the screen clear again");
            yield return AtBoard();
            yield return Expect(() => Big() == "NEW CYCLE" && Sign() == "PAY/off" && Card() != null && Card().Enabled, 3f, () => "Q8 the fresh board: " + Top() + " | " + CardText());

            Heading("Q9 — GIVE UP with the host alone: the card reads 0 / 1, one E is unanimous: THE RUN IS OVER, the plank, the fresh run with no votes");
            yield return AimCard();
            yield return null;
            Check(Card().Count == "0 / 1" && Card().Enabled && !Card().LocalVoted && Card().Foot == "EVERYONE MUST AGREE" && Card().Word == "GIVE UP", "Q9 the card: " + CardText());
            Check(hud.PromptText == "Press E to vote to give up the run — everyone must agree", "Q9 the card's prompt: " + hud.PromptText);
            runOvers = Day.RunOver.Serial; planks = Count("[Plank] The run is over");
            yield return Press(Key.E);
            yield return Expect(() => Day.Phase == DayPhase.Plank && Day.Plank.Active, 3f, () => "Q9 the host's vote ends the run");
            Check(Day.GiveUpVotes == 0 && Count("[GiveUp] The whole crew agreed (1)") >= 1 && Count("[Plank] The run is over") == planks + 1, "Q9 unanimous with 1, the votes spent, one plank");
            yield return Expect(() => Big() == "THE RUN IS OVER" && Text().StartsWith("THE RUN IS OVER"), 2f, () => "Q9 the board: " + Top());
            yield return RideOutPlank("Q9");
            Check(Day.RunOver.Serial == runOvers + 1, "Q9 one run over");

            // ---- guests ---------------------------------------------------------------
            Heading("G1 — guest A joins: the card reads 0 / 2 on both peers");
            guestA = LaunchGuest(DirA);
            yield return Joined(guestA, "G1 A");
            yield return AtBoard();
            yield return GuestMove(guestA, StandSpot(1.0f));
            yield return Expect(() => Card() != null && Card().Count == "0 / 2" && Card().Enabled, 3f, () => "G1 the host's card: " + CardText());
            yield return GuestEventually(guestA, r => GBottom(r).Contains("vote=GIVE UP 0 / 2 voted=False") && GCard(r) == "on/EVERYONE MUST AGREE" && GVote(r).StartsWith("giveup=0/0;"), 5f, "G1 A's card reads 0 / 2");

            Heading("G2 — the host votes with the real E: 1/2 on both peers, YOU VOTED on the host's card only, the top hint on both");
            yield return AimCard();
            yield return Press(Key.E);
            yield return Expect(() => Day.GiveUpVotes == 1 && Day.GiveUpCrew == 2 && Day.HasVotedGiveUp(host.OwnerId), 3f, () => $"G2 the host's vote: {Day.GiveUpVotes}/{Day.GiveUpCrew}");
            yield return Expect(() => Card().Count == "1 / 2" && Card().LocalVoted && Card().Foot == "YOU VOTED · E TO TAKE BACK" && TopM().Hint == HQQuotaConsole.HintVote(1, 2) && TopM().HintTone == ConsoleTone.Danger, 2f, () => "G2 the host's screens: " + CardText() + " | " + Top());
            yield return null;
            Check(hud.PromptText == "Press E to take back your vote (give up 1/2)", "G2 the host's prompt: " + hud.PromptText);
            Check(Text().Contains("give up 1/2") && Day.Phase == DayPhase.AtHQ, "G2 compat text, the run goes on: " + Text().Replace("\n", " | "));
            yield return GuestEventually(guestA, r => GVote(r).StartsWith("giveup=1/2;") && GBottom(r).Contains("vote=GIVE UP 1 / 2 voted=False") && GCard(r) == "on/EVERYONE MUST AGREE" && GTop(r).Contains("GIVE UP 1/2 · EVERYONE MUST PRESS") && r.Contains("give up 1/2"), 5f, "G2 A sees 1 / 2 and the hint, and NOT 'YOU VOTED'");
            yield return Shot("console-hq-G2-voted");

            Heading("G3 — the host takes the vote back (E again): 0 / 2 on both, the hint gone");
            yield return AimCard();
            yield return Press(Key.E);
            yield return Expect(() => Day.GiveUpVotes == 0 && !Day.HasVotedGiveUp(host.OwnerId), 3f, () => "G3 the vote taken back: " + H.GiveUpStatus());
            yield return Expect(() => Card().Count == "0 / 2" && !Card().LocalVoted && Card().Foot == "EVERYONE MUST AGREE" && !TopM().Hint.Contains("GIVE UP"), 2f, () => "G3 the host's screens: " + CardText() + " | " + Top());
            yield return GuestEventually(guestA, r => GVote(r).StartsWith("giveup=0/") && GBottom(r).Contains("vote=GIVE UP 0 / 2 voted=False") && !GTop(r).Contains("GIVE UP"), 5f, "G3 A reads the vote taken back");

            Heading("G4 — one player refuses: A votes, the host does not; nothing happens");
            yield return Send(guestA, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.GiveUpVotes == 1 && Day.HasVotedGiveUp(guestA.Id) && !Day.HasVotedGiveUp(host.OwnerId), 3f, () => "G4 A's vote arrives: " + H.GiveUpStatus());
            yield return Expect(() => Card().Count == "1 / 2" && !Card().LocalVoted && Card().Foot == "EVERYONE MUST AGREE", 2f, () => "G4 the host's card (not voted): " + CardText());
            yield return GuestEventually(guestA, r => GBottom(r).Contains("vote=GIVE UP 1 / 2 voted=True") && GCard(r) == "on/YOU VOTED · E TO TAKE BACK", 5f, "G4 A's own card: YOU VOTED · E TO TAKE BACK");
            yield return Hold(() => Day.Phase == DayPhase.AtHQ && Day.GiveUpVotes == 1, 2.5f, () => "G4 one of two holds: no plank (" + Day.Phase + ")");

            Heading("G5 — a player joins mid-vote: B's arrival clears the vote; the count's n grows to 3 on every peer; the joiner never sees a stale count");
            int clearedBefore = Count("[GiveUp] Votes cleared");
            guestB = LaunchGuest(DirB);
            yield return Joined(guestB, "G5 B");
            Check(Day.GiveUpVotes == 0 && Day.GiveUpCrew == 0 && !Day.HasVotedGiveUp(guestA.Id), "G5 the vote cleared by the join: " + H.GiveUpStatus());
            Check(Count("[GiveUp] Votes cleared") > clearedBefore, "G5 the server logged the clear: " + string.Join(" / ", logs.Where(l => l.Contains("Votes cleared")).Skip(clearedBefore)));
            yield return Expect(() => Card().Count == "0 / 3" && !TopM().Hint.Contains("GIVE UP"), 3f, () => "G5 the host's card: " + CardText());
            yield return GuestEventually(guestA, r => GVote(r).StartsWith("giveup=0/0;") && GBottom(r).Contains("vote=GIVE UP 0 / 3 voted=False") && GCard(r) == "on/EVERYONE MUST AGREE", 5f, "G5 A's card: 0 / 3, its vote gone");
            yield return GuestEventually(guestB, r => GVote(r).StartsWith("giveup=0/0;") && GBottom(r).Contains("vote=GIVE UP 0 / 3") && !GTop(r).Contains("GIVE UP"), 5f, "G5 B's first board: 0 / 3, no vote hint");
            yield return GuestMove(guestB, StandSpot(-1.0f));

            Heading("G6 — a player leaves mid-vote: the host votes 1/3, B leaves; the vote clears and n shrinks to 2");
            Check(flow.ServerToggleGiveUp(host.Owner, out string g6Why) && Day.GiveUpVotes == 1 && Day.GiveUpCrew == 3, $"G6 the host votes: {Day.GiveUpVotes}/{Day.GiveUpCrew} {g6Why}");
            yield return GuestEventually(guestA, r => GVote(r).StartsWith("giveup=1/3;") && GTop(r).Contains("GIVE UP 1/3"), 5f, "G6 A sees 1/3");
            clearedBefore = Count("[GiveUp] Votes cleared");
            yield return Send(guestB, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => CopyOf(guestB) == null, 15f, () => "G6 B left");
            yield return Expect(() => Day.GiveUpVotes == 0 && Day.GiveUpCrew == 0, 3f, () => "G6 the vote cleared: " + H.GiveUpStatus());
            Check(Day.Phase == DayPhase.AtHQ && Count("[GiveUp] Votes cleared: a player left") >= 1, "G6 the run goes on, the server logged 'a player left'");
            yield return Expect(() => Card().Count == "0 / 2", 3f, () => "G6 the host's card: " + CardText());
            yield return GuestEventually(guestA, r => GVote(r).StartsWith("giveup=0/0;") && GBottom(r).Contains("vote=GIVE UP 0 / 2"), 6f, "G6 A's card: 0 / 2");
            try { if (!guestB.Process.HasExited) guestB.Process.Kill(); } catch (Exception) { }

            Heading("G7 — a disconnect during the vote: the host votes 1/2, A's process is killed; the vote clears when the connection drops, n = 1, no plank, no exception");
            Check(flow.ServerToggleGiveUp(host.Owner, out string g7Why) && Day.GiveUpVotes == 1 && Day.GiveUpCrew == 2, $"G7 the host votes: {Day.GiveUpVotes}/{Day.GiveUpCrew} {g7Why}");
            int exceptionsBefore = exceptionsSeen;
            float killedAt = Time.unscaledTime;
            guestA.Process.Kill();
            Say("G7 guest A killed");
            yield return Hold(() => Day.Phase == DayPhase.AtHQ, 1.0f, () => "G7 no plank right after the kill");
            yield return Expect(() => CopyOf(guestA) == null && Day.GiveUpVotes == 0, 70f, () => "G7 A's connection dropped and the vote cleared: " + H.GiveUpStatus() + " (A's copy " + (CopyOf(guestA) != null) + ")");
            Say($"G7 the drop took {Time.unscaledTime - killedAt:0.0} s");
            Check(Day.Phase == DayPhase.AtHQ && Day.GiveUpCrew == 0 && !Day.HasVotedGiveUp(host.OwnerId), "G7 the host alone, the run goes on: " + H.GiveUpStatus());
            yield return Expect(() => Card().Count == "0 / 1" && !Card().LocalVoted, 3f, () => "G7 the host's card: " + CardText());
            Check(exceptionsSeen == exceptionsBefore, "G7 no exception in the host's log during the drop: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]"))));

            Heading("G8 — two guests again (3 crew); PAY from the host and A in the same frame: one sale, one bank, one pull; everything replicates, the lever swings on A too");
            guestA = LaunchGuest(DirA + "2");
            yield return Joined(guestA, "G8 A");
            guestB = LaunchGuest(DirB + "2");
            yield return Joined(guestB, "G8 B");
            yield return AtBoard();
            yield return GuestMove(guestA, StandSpot(1.0f));
            yield return GuestMove(guestB, StandSpot(-1.0f));
            yield return Expect(() => Card().Count == "0 / 3", 3f, () => "G8 3 crew: " + CardText());
            Day.ServerForceCycleForChecks(1, false);
            CarryableItem coin8 = null;
            yield return CoinInRoom("G8 coin", c => coin8 = c);
            int v8 = coin8.Value;
            WorldLoopSettings.QuotaOverrideForTests = v8;
            yield return Expect(() => Sign() == "PAY/on", 2f, () => "G8 PAY lit on the host: " + Sign());
            yield return GuestEventually(guestA, r => GSign(r) == "PAY/on" && GuestHeader(r).Contains("box=" + v8 + ";"), 5f, "G8 PAY lit on A, the box $" + v8);
            yield return GuestEventually(guestB, r => GSign(r) == "PAY/on" && GTop(r).Contains($"QUOTA ${v8} / "), 5f, "G8 PAY lit on B, the box counted (the quota override is the editor's only)");
            paySerial = Day.LastPay.Serial; pulls = Day.LastLeverPull.Serial; balance = Day.Balance; refusal = Day.LastRefusal.Serial;
            Command(guestA, "{\"id\":{id},\"action\":\"lever\",\"item\":\"HQ\"}");
            int aCmd = guestCommand;
            string hostPull = H.ClientPullLever("HQ");
            Say("G8 same editor frame: A's lever written, host " + hostPull);
            yield return AwaitReply(guestA, aCmd);
            Check(lastReply.Contains("requested lever HQ/Pay/None"), "G8 A's pull went expecting PAY: " + lastReply.Split('\n')[0]);
            float aAngle = float.TryParse(Field(GuestConsole(lastReply), "hqLeverAngle"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ang) ? ang : float.NaN;
            yield return Expect(() => Day.LastPay.Serial > paySerial, 3f, () => "G8 a pay went through");
            yield return Wait(1.0f);
            Check(Day.LastPay.Serial == paySerial + 1 && Day.LastPay.Paid && Day.LastPay.Sales == v8 && Day.Balance == balance + v8 && Gone(coin8) && Day.LastLeverPull.Serial == pulls + 1, $"G8 one sale (${Day.LastPay.Sales}), one bank (+${Day.Balance - balance}), one pull ({Day.LastLeverPull.Serial - pulls})");
            Check(Day.LastRefusal.Serial > refusal && (Day.LastRefusal.Text == ConsoleRules.LeverInUse || Day.LastRefusal.Text == ConsoleRules.NothingToPay), "G8 the loser was refused: '" + Day.LastRefusal.Text + "'");
            Check(Count("Pay: sold $" + v8 + ",") == 1, "G8 the server logged exactly one sale of $" + v8);
            Check(Console().LeverPlayedSerial == pulls + 1, "G8 the host's rig played the pull once: " + H.LeverPullText());
            Say($"G8 A's lever angle in its reply 0.4 s after the pull: {aAngle}");
            yield return GuestEventually(guestA, r => GVote(r).Contains("quotaBoard=PAID $" + v8) && GuestHeader(r).Contains("balance=" + (balance + v8) + ";") && GTop(r).Contains("PAID") && Field(GuestConsole(r), "leverPulls").StartsWith((pulls + 1) + "/HQ/Pay") && Field(GuestConsole(r), "hqLeverPlayed") == (pulls + 1).ToString(), 6f, "G8 A reads PAID, the balance, the pull, and its rig played it");
            yield return GuestEventually(guestB, r => GVote(r).Contains("quotaBoard=PAID $" + v8) && GuestHeader(r).Contains("balance=" + (balance + v8) + ";") && GTop(r).Contains("PAID") && Field(GuestConsole(r), "hqLeverPlayed") == (pulls + 1).ToString() && GSign(r) == "PAY/off", 6f, "G8 B reads PAID, the balance, the dim PAY, and its rig played the pull");
            Check(!float.IsNaN(aAngle) && aAngle > 0f, $"G8 A's lever was mid-swing 0.4 s after the pull ({aAngle}°)");
            yield return Wait(reportSeconds);

            Heading("G9 — one player refuses with 3 crew: host and A vote 2/3, B does not; nothing happens");
            yield return AimCard();
            yield return Press(Key.E);
            yield return Send(guestA, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.GiveUpVotes == 2 && Day.GiveUpCrew == 3, 3f, () => "G9 2/3: " + H.GiveUpStatus());
            yield return Expect(() => Card().Count == "2 / 3" && Card().LocalVoted && TopM().Hint == HQQuotaConsole.HintVote(2, 3), 2f, () => "G9 the host's screens: " + CardText() + " | " + Top());
            yield return GuestEventually(guestB, r => GVote(r).StartsWith("giveup=2/3;") && GBottom(r).Contains("vote=GIVE UP 2 / 3 voted=False") && GTop(r).Contains("GIVE UP 2/3 · EVERYONE MUST PRESS"), 5f, "G9 B sees 2 / 3, not voted");
            yield return GuestEventually(guestA, r => GBottom(r).Contains("vote=GIVE UP 2 / 3 voted=True"), 5f, "G9 A sees its own vote");
            yield return Hold(() => Day.Phase == DayPhase.AtHQ && Day.GiveUpVotes == 2, 2.5f, () => "G9 two of three holds: no plank");

            Heading("G10 — the vote resets when the ship sails: 2/3 in, everyone aboard, CONFIRM on the ship's lever; cleared at once; no vote at sea; home again");
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            yield return GuestMove(guestA, hqShip.FromShipLocal(new Vector3(2.5f, 0f, 4f)));
            yield return GuestMove(guestB, hqShip.FromShipLocal(new Vector3(-2.5f, 0f, 4f)));
            yield return Expect(() => hqShip.IsSafelyAboard(CopyOf(guestA).transform.position) && hqShip.IsSafelyAboard(CopyOf(guestB).transform.position), 5f, () => "G10 both guests on the deck");
            Check(H.ClientMoveLocalPlayerToShipConsole().StartsWith("moved"), "G10 the host at the ship's console");
            yield return null; yield return null;
            Say("select: " + H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && H.ConsoleStatus().Contains("shipAction=Confirm/Site01/on"), 3f, () => "G10 Site 01 selected, CONFIRM lit");
            Check(Day.GiveUpVotes == 2, "G10 the vote still 2/3 before the sail");
            clearedBefore = Count("[GiveUp] Votes cleared: the ship sails");
            int trips = Day.Departure.Serial;
            Say("pull: " + H.ClientPullLever("Ship"));
            yield return Expect(() => Day.Departure.Serial == trips + 1 && Day.Travelling, 4f, () => "G10 the ship sails");
            Check(Day.GiveUpVotes == 0 && Day.GiveUpCrew == 0 && Count("[GiveUp] Votes cleared: the ship sails") == clearedBefore + 1, "G10 the vote cleared by the sail: " + H.GiveUpStatus());
            yield return GuestEventually(guestB, r => GVote(r).StartsWith("giveup=0/0;"), 6f, "G10 B reads no votes");
            yield return Arrive(WorldId.Sea, "G10");
            Check(!flow.ServerToggleGiveUp(host.Owner, out string seaWhy) && Day.GiveUpVotes == 0, "G10 a vote at sea is refused: " + seaWhy);
            refusal = Day.LastRefusal.Serial;
            yield return Send(guestA, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.LastRefusal.Serial > refusal, 3f, () => "G10 A's vote at sea is refused");
            Say("G10 A's refusal at sea: '" + Day.LastRefusal.Text + "'");
            Check(Day.GiveUpVotes == 0, "G10 no vote registered at sea");
            ShipParts seaShip = ShipParts.InWorld(WorldId.Sea);
            yield return GuestEventually(guestA, r => GuestHeader(r).Contains("world=Sea"), 20f, "G10 A at sea");
            yield return GuestEventually(guestB, r => GuestHeader(r).Contains("world=Sea"), 20f, "G10 B at sea");
            yield return GuestMove(guestA, seaShip.FromShipLocal(new Vector3(2.5f, 0f, 4f)));
            yield return GuestMove(guestB, seaShip.FromShipLocal(new Vector3(-2.5f, 0f, 4f)));
            Check(H.ClientMoveLocalPlayerToShipConsole().StartsWith("moved"), "G10 the host at the sea ship's console");
            yield return Expect(() => CopyOf(guestA) != null && CopyOf(guestB) != null && seaShip.IsSafelyAboard(CopyOf(guestA).transform.position) && seaShip.IsSafelyAboard(CopyOf(guestB).transform.position), 5f, () => "G10 everyone aboard at sea");
            string home = H.ServerSail("HQ");
            Check(home == "sailing to HQ", "G10 sail home: " + home);
            yield return Arrive(WorldId.HQ, "G10");
            Check(Day.GiveUpVotes == 0 && Day.Phase == DayPhase.AtHQ, "G10 docked, no votes: " + H.GiveUpStatus());
            yield return GuestEventually(guestA, r => GuestHeader(r).Contains("world=HQ") && GuestHeader(r).Contains("phase=AtHQ") && GVote(r).StartsWith("giveup=0/0;") && GBottom(r).Contains("vote=GIVE UP 0 / 3"), 25f, "G10 A docked, 0 / 3");
            yield return GuestEventually(guestB, r => GuestHeader(r).Contains("world=HQ") && GVote(r).StartsWith("giveup=0/0;"), 25f, "G10 B docked, no votes");

            Heading("G11 — all players vote (host, A, then B last): THE RUN IS OVER on every screen, the plank flow; on the plank the card and PAY are dark for the guests and their presses are refused");
            yield return AtBoard();
            yield return GuestMove(guestA, StandSpot(1.0f));
            yield return GuestMove(guestB, StandSpot(-1.0f));
            yield return Expect(() => Card() != null && Card().Count == "0 / 3", 4f, () => "G11 the card: " + CardText());
            runOvers = Day.RunOver.Serial; planks = Count("[Plank] The run is over");
            Check(flow.ServerToggleGiveUp(host.Owner, out _) && Day.GiveUpVotes == 1, "G11 the host votes");
            yield return Send(guestA, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.GiveUpVotes == 2 && Day.Phase == DayPhase.AtHQ, 3f, () => "G11 A votes: 2/3, not yet");
            yield return Send(guestB, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.Phase == DayPhase.Plank && Day.Plank.Active, 3f, () => "G11 B's vote completes it: the plank");
            Check(Day.GiveUpVotes == 0 && Count("[GiveUp] The whole crew agreed (3)") == 1 && Count("[Plank] The run is over") == planks + 1, "G11 unanimous 3/3, the votes spent, one plank");
            yield return Expect(() => Big() == "THE RUN IS OVER" && !Card().Enabled && Sign() == "PAY/off", 2f, () => "G11 the host's board: " + Top() + " | " + CardText() + " | " + Sign());
            yield return GuestEventually(guestA, r => GuestHeader(r).Contains("phase=Plank") && GVote(r).Contains("quotaBoard=THE RUN IS OVER") && GTop(r).Contains("THE RUN IS OVER"), 5f, "G11 A reads THE RUN IS OVER");
            yield return GuestEventually(guestB, r => GuestHeader(r).Contains("phase=Plank") && GTop(r).Contains("THE RUN IS OVER") && GSign(r) == "PAY/off" && GCard(r).StartsWith("off/"), 5f, "G11 B: THE RUN IS OVER, PAY dim, the card dark");
            // B stands at the console while the host is on the board: its presses are refused.
            refusal = Day.LastRefusal.Serial;
            yield return Send(guestB, "{\"id\":{id},\"action\":\"lever\",\"item\":\"HQ\"}");
            Say("G11 B's pull on the plank: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.LastRefusal.Serial > refusal, 3f, () => "G11 B's pull is refused");
            Check(Day.LastRefusal.Text == ConsoleRules.RunOver, "G11 B's pull is refused: '" + Day.LastRefusal.Text + "'");
            refusal = Day.LastRefusal.Serial;
            yield return Send(guestB, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.LastRefusal.Serial > refusal, 3f, () => "G11 B's vote on the plank is refused");
            Check(Day.LastRefusal.Text == ConsoleRules.RunOver, "HQ-1 retest: G11 B's vote refusal on the plank: '" + Day.LastRefusal.Text + "'");
            Check(Day.GiveUpVotes == 0 && Count("[Plank] The run is over") == planks + 1, "G11 no vote, still one plank");
            // The host steps off first; then, no longer the jumper, it reads the console's own prompts on the plank.
            {
                HQPlank plankG = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
                yield return Expect(() => Day.Plank.Jumper == host.OwnerId, 10f, () => "G11 the host is the first jumper");
                yield return Wait(0.8f);
                host.TeleportLocal(plankG.End.position + plankG.End.forward * 0.8f + Vector3.up * 0.1f, plankG.WalkYaw);
                yield return Expect(() => Day.HasJumped(host.OwnerId), 5f, () => "G11 the host jumped");
                yield return AimLever();
                yield return null;
                Check(hud.PromptText.StartsWith("THE RUN IS OVER"), "G11 the prompt at the lever for a non-jumper on the plank names the reason: " + hud.PromptText);
                yield return AimCard();
                yield return null;
                Check(hud.PromptText.StartsWith("THE RUN IS OVER") || hud.PromptText == ConsoleRules.RunOver, "HQ-1 retest: G11 the card's prompt for a non-jumper on the plank names the run over (the plank HUD's prompt covers every player): " + hud.PromptText);
                Check(!hud.PromptText.StartsWith("Press E"), "G11 the card offers no vote on the plank: " + hud.PromptText);
                Check(!Card().Enabled && Sign() == "PAY/off", "G11 the card and the sign dark: " + CardText() + " | " + Sign());
            }
            yield return RideOutPlank("G11");
            Check(Day.RunOver.Serial == runOvers + 1, "G11 one run over");
            yield return GuestEventually(guestA, r => GuestHeader(r).Contains("phase=AtHQ") && GuestHeader(r).Contains("day=0;") && GVote(r).StartsWith("giveup=0/0;") && GTop(r).Contains("NEW CYCLE"), 12f, "G11 A reads the fresh run, no votes");
            yield return GuestEventually(guestB, r => GuestHeader(r).Contains("phase=AtHQ") && GVote(r).StartsWith("giveup=0/0;") && GCard(r).StartsWith("on/"), 12f, "G11 B reads the fresh run, the card lit again");

            Heading("G12 — the vote resets with a fresh run: 2/3 in, a lost payday puts the crew on the plank; after it the vote is gone");
            yield return AtBoard();
            yield return GuestMove(guestA, StandSpot(1.0f));
            yield return GuestMove(guestB, StandSpot(-1.0f));
            Check(flow.ServerToggleGiveUp(host.Owner, out _), "G12 the host votes");
            yield return Send(guestA, "{\"id\":{id},\"action\":\"giveup\"}");
            yield return Expect(() => Day.GiveUpVotes == 2 && Day.GiveUpCrew == 3, 3f, () => "G12 2/3 in: " + H.GiveUpStatus());
            Day.ServerForceCycleForChecks(days, true);
            WorldLoopSettings.QuotaOverrideForTests = 99999;
            yield return Expect(() => Sign() == "PAY/on", 2f, () => "G12 PAYDAY: " + Sign());
            runOvers = Day.RunOver.Serial; planks = Count("[Plank] The run is over");
            Say("pay: " + H.ClientPullLever("HQ"));
            yield return Expect(() => Day.Phase == DayPhase.Plank && Day.LastPay.Lost, 3f, () => "G12 lost at payday: the plank");
            Say("G12 the vote during the plank: " + H.GiveUpStatus() + ", the card " + CardText());
            yield return RideOutPlank("G12");
            Check(Day.RunOver.Serial == runOvers + 1 && Count("[Plank] The run is over") == planks + 1, "G12 one run over");
            Check(Count("[GiveUp] Votes cleared: a fresh run") >= 1, "G12 the server cleared the vote for the fresh run");
            WorldLoopSettings.QuotaOverrideForTests = null;
            yield return GuestEventually(guestA, r => GuestHeader(r).Contains("phase=AtHQ") && GVote(r).StartsWith("giveup=0/0;") && GBottom(r).Contains("voted=False"), 12f, "G12 A: the fresh run has no votes, its own vote gone");

            Heading("G13 — the whole crew presses the card in the same frame: one plank, one run-over, no exception");
            yield return AtBoard();
            yield return GuestMove(guestA, StandSpot(1.0f));
            yield return GuestMove(guestB, StandSpot(-1.0f));
            yield return Expect(() => Card().Count == "0 / 3", 4f, () => "G13 the card: " + CardText());
            runOvers = Day.RunOver.Serial; planks = Count("[Plank] The run is over"); int agreed = Count("[GiveUp] The whole crew agreed"); exceptionsBefore = exceptionsSeen;
            Command(guestA, "{\"id\":{id},\"action\":\"giveup\"}"); int ca = guestCommand;
            Command(guestB, "{\"id\":{id},\"action\":\"giveup\"}"); int cb = guestCommand;
            Say("G13 same frame: " + H.ClientRequestGiveUp());
            yield return AwaitReply(guestA, ca);
            yield return AwaitReply(guestB, cb);
            yield return Expect(() => Day.Phase == DayPhase.Plank, 4f, () => "G13 the plank: " + H.GiveUpStatus());
            yield return Wait(1.0f);
            Check(Count("[GiveUp] The whole crew agreed") == agreed + 1 && Count("[Plank] The run is over") == planks + 1 && exceptionsSeen == exceptionsBefore, "G13 exactly one agreement, one plank, no exception");
            yield return GuestEventually(guestB, r => GuestHeader(r).Contains("phase=Plank") && GTop(r).Contains("THE RUN IS OVER"), 5f, "G13 B reads the run over");
            yield return RideOutPlank("G13");
            Check(Day.RunOver.Serial == runOvers + 1, "G13 one run over");

            yield return Send(guestA, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Send(guestB, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => CopyOf(guestA) == null && CopyOf(guestB) == null, 15f, () => "both guests left");
            Say($"host log: {errorsSeen} errors, {exceptionsSeen} exceptions during the run");
            Say("end: " + H.ConsoleStatus());
        }

        private static string GVote(string reply) => GuestVote(reply);
    }
}
