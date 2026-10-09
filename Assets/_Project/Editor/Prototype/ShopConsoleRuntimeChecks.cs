using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Look;
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
    // The shop console at the equipment depot (Dan, 8 October 2026: "the shop console IS
    // the shop"; scratchpad hq-consoles/BRIEF.md B.1-B.3, TESTPLAN.md §2). Host-only rows
    // first (P): the console stands at the counter with its rows, zones and lever, the
    // hanging sign, the three fascia signs and the four display stands are gone, the first
    // page reads the catalogue with an empty pot (P0); a row with no money is refused and
    // the refusal shows in the prompt and on the lower screen (P1); a buy from a row with
    // the real aim and E delivers the air tank through the chute onto the pickup pad and
    // charges the pot (P2); an upgrade row turns OWNED and a second buy is refused (P3);
    // the NEXT / PREV zones and the lever page the shared page (P4); the refusals far,
    // hand-made and dead (P5); E at the console never opens the HUD browser (P6); a
    // same-frame double buy is charged once (P7); the catalogue grows in memory and the
    // console pages it without a rebuild (P8). Then one guest (G): the page is shared both
    // ways (G1), a guest buys from the console and each screen shows its own OWNED (G2),
    // the guest's refusals far and hand-made (G3), the dead guest (G4), the host's lever
    // seen on the guest (G5). Every row asserts replicated state (CrewDayState and the
    // buyer's PlayerUpgrades on the host, the guest's `hqconsoles=` and `player=`
    // snapshot lines) and the composer's models (what the screens draw).
    // Log: Temp/console-shop-matrix.log. Started by CameraClearanceMatrixDriver.Start("console-shop")
    // or, before the driver knows the job, `matrix host` then
    // `run SunkCost.Editor.Prototype.ShopConsoleRuntimeChecks.RunAsHost`.
    public static class ShopConsoleRuntimeChecks
    {
        private const string Log = "Temp/console-shop-matrix.log";
        private const string GuestDir = "Temp/console-shop-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Captures = "Temp/look/";
        // Where the console stands (INTERFACES §6.5: depot-local (-6, 0, 14) yaw 180 under "Shop Room" at the origin).
        private static readonly Vector3 ExpectedRoot = new(-6f, 0.05f, 14f);
        private const string ProofId = "console-shop-proof", ProofName = "Catalogue expansion proof";
        private const int ProofPrice = 11;

        private sealed class Guest
        {
            public string Dir; public Process Process; public int Id = -1; public string Name = string.Empty;
            public override string ToString() => Dir + "#" + Id;
        }

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Guest guest;
        private static int guestCommand = 6900;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static ShopItem extraRow;
        private static readonly List<string> logs = new();
        private static int exceptionsSeen, errorsSeen;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run console-shop matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Console-shop matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Console-shop matrix started " + DateTime.Now + "\n");
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
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert || type == LogType.Warning || message.Contains("[Console]") || message.Contains("[Shop]"))
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
            if (Status == "MATRIX_PASS") Debug.Log("Console-shop matrix: MATRIX_PASS"); else Debug.LogError("Console-shop matrix: " + Status);
            RemoveProofRow();
            try { if (guest?.Process != null && !guest.Process.HasExited) guest.Process.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            Application.logMessageReceived -= OnLog;
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            EditorApplication.update -= Tick;
        }

        private static void RemoveProofRow()
        {
            if (extraRow == null) return;
            try { ((List<ShopItem>)ShopCatalog.Resolve().Items).Remove(extraRow); } catch (Exception) { }
            extraRow = null;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static string State()
        {
            HQShopConsole c = Console();
            string console = c == null ? "(no shop console)" : $"page={c.Page}/{c.Pages} rows='{c.RowsText}' aimedRow={c.AimedRow} aimedPage={c.AimedPage} played={c.LeverPlayedSerial} angle={c.LeverAngle:0.#}\ntop={Top()}\nbottom={Bottom()}\nsign={Sign()}";
            HQPlayerController host = Host();
            string buyer = host == null || host.Upgrades == null ? "(no upgrades)" : $"owned={host.Upgrades.Owned} refusal='{host.Upgrades.Refusal}'";
            string day = Day == null ? "(no day state)" : $"ShopPage={Day.ShopPage} balance={Day.Balance} pull={H.LeverPullText()} refusal={Day.LastRefusal.Serial}:'{Day.LastRefusal.Text}' shopOpen={SunkCost.Net.SessionInputGate.ShopOpen}";
            return H.FlowStatus() + "\n" + day + "\n" + buyer + "\n" + console;
        }
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

        private static Guest LaunchGuest(string dir, bool headless = false)
        {
            Directory.CreateDirectory(dir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(dir, stale))) File.Delete(Path.Combine(dir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            string screen = headless ? "-batchmode -nographics" : "-screen-width 960 -screen-height 540 -screen-fullscreen 0";
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                screen + " -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(dir) + "\" -logFile \"" + Path.GetFullPath(dir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            return new Guest { Dir = dir, Process = Process.Start(info) };
        }
        // Waits for the guest's player to spawn on the host and its peer to answer at HQ.
        private static IEnumerator Joined(Guest g, string label)
        {
            HQPlayerController copy = null;
            float deadline = Time.unscaledTime + 45f;
            while (Time.unscaledTime < deadline)
            {
                copy = UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
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
            throw new Exception(label + "\n" + GuestHeader(lastReply) + "\n" + GuestConsoles(lastReply) + "\n" + GuestPlayerLine(lastReply, g.Id) + "\n" + State());
        }
        private static string GuestLine(string reply, string prefix) => reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        private static string GuestHeader(string reply) => GuestLine(reply, "server=");
        // The `hqconsoles=1; … shopPage=…` line (INTERFACES §9).
        private static string GuestConsoles(string reply) => GuestLine(reply, "hqconsoles=");
        private static string GuestPlayerLine(string reply, int ownerId) => GuestLine(reply, "player=" + ownerId + ";");
        // The guest's item lines by display name and scene (`item=…; display=Air tank; … scene=HQPrototype; …`).
        private static int GuestItems(string reply, string displayName, string scene) => reply.Split('\n').Count(l => l.StartsWith("item=") && l.Contains("; display=" + displayName + "; ") && l.Contains("; scene=" + scene + "; "));
        // "key=value; " on a snapshot line (the values carry " · " and "|" but never "; ").
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
        private static string GPage(string reply) => Field(GuestConsoles(reply), "shopPage");
        private static string GPages(string reply) => Field(GuestConsoles(reply), "shopPages");
        private static string GRows(string reply) => Field(GuestConsoles(reply), "shopRows");
        private static string GTop(string reply) => Field(GuestConsoles(reply), "shopTop");
        private static string GPlayed(string reply) => Field(GuestConsoles(reply), "shopLeverPlayed");
        private static string GOpen(string reply) => Field(GuestConsoles(reply), "shopOpen");
        // The player line's shopRefusal='…' (quoted; the quote keeps the "; " rule).
        private static string GRefusal(string reply, int ownerId)
        {
            string line = GuestPlayerLine(reply, ownerId);
            int i = line.IndexOf("shopRefusal='", StringComparison.Ordinal);
            if (i < 0) return "(absent)";
            int start = i + "shopRefusal='".Length;
            int end = line.IndexOf('\'', start);
            return end < 0 ? line.Substring(start) : line.Substring(start, end - start);
        }
        private static string GUpgrades(string reply, int ownerId) => Field(GuestPlayerLine(reply, ownerId), "upgrades");
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static IEnumerator GuestMove(Guest g, Vector3 to) { yield return Send(g, "{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static IEnumerator GuestPage(Guest g, int page) { yield return Send(g, "{\"id\":{id},\"action\":\"shop_page\",\"slot\":" + page + "}"); }
        private static IEnumerator GuestBuy(Guest g, string itemId) { yield return Send(g, "{\"id\":{id},\"action\":\"buy\",\"item\":\"" + itemId + "\"}"); }
        private static IEnumerator GuestLook(Guest g, string control) { yield return Send(g, "{\"id\":{id},\"action\":\"look_control\",\"item\":\"" + control + "\"}"); }

        // ---- the console under test -----------------------------------------------------

        private static HQShopConsole Console() => HQShopConsole.InHQ();
        private static ConsoleRig Rig() => Console()?.GetComponent<ConsoleRig>();
        private static string Top() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Top);
        private static string Bottom() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Bottom);
        private static string Sign() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Sign);
        private static string Row(int row) { string[] rows = (Console()?.RowsText ?? string.Empty).Split('|'); return row < rows.Length ? rows[row] : "(no row)"; }
        private static Vector3 Front() { Vector3 f = Rig().transform.rotation * Vector3.forward; f.y = 0f; return f.normalized; }
        private static Vector3 RightOf() => Vector3.Cross(Vector3.up, Front());
        // In front of the Shop rig, standing where a reader stands; `side` metres to the rig's right.
        private static Vector3 StandSpot(float side)
        {
            Vector3 at = Rig().transform.position + Front() * 1.6f + RightOf() * side;
            at.y = 0.05f;
            return at;
        }
        private static float FacingYaw() { Vector3 back = -Front(); return Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg; }
        private static IEnumerator AtConsole(float side = 0f)
        {
            Host().TeleportLocal(StandSpot(side), FacingYaw());
            yield return null; yield return null; yield return null;
        }
        // Looks at the named control until the dot is on it (re-placed at the console every 45 frames).
        private static IEnumerator Aim(string objectName, ConsoleControlKind kind, int index = int.MinValue)
        {
            HQPlayerController host = Host();
            float deadline = Time.unscaledTime + 4f; int tries = 0;
            while (Time.unscaledTime < deadline)
            {
                ConsoleControl c = host.CurrentConsoleControl;
                if (c != null && c.Kind == kind && c.Console == ConsoleKind.Shop && (index == int.MinValue || c.Index == index)) break;
                if (tries++ % 45 == 0) host.TeleportLocal(StandSpot(0f), FacingYaw());
                H.ClientLookAtNamed(objectName);
                yield return null;
            }
            ConsoleControl aimed = host.CurrentConsoleControl;
            Check(aimed != null && aimed.Kind == kind && aimed.Console == ConsoleKind.Shop && (index == int.MinValue || aimed.Index == index), "the dot is on " + objectName + " (aim " + (aimed == null ? "none" : aimed.Console + "/" + aimed.Kind + "/" + aimed.Index) + ")");
        }
        private static IEnumerator AimRow(int row) => Aim(ConsoleRig.RowNamePrefix + row, ConsoleControlKind.ShopRow, row);
        private static IEnumerator AimNext() => Aim(ConsoleRig.NextName, ConsoleControlKind.ShopPage, +1);
        private static IEnumerator AimPrev() => Aim(ConsoleRig.PrevName, ConsoleControlKind.ShopPage, -1);
        private static IEnumerator AimLever() => Aim(ConsoleRig.CatalogueLeverName, ConsoleControlKind.Lever);
        // A console refusal (CrewDayState.LastRefusal, every peer): the serial grew and the text is the rule's.
        private static IEnumerator ExpectRefusal(int serialBefore, string text, string label)
        {
            yield return Expect(() => Day.LastRefusal.Serial > serialBefore, 3f, () => label + ": refused '" + Day.LastRefusal.Text + "'");
            Check(Day.LastRefusal.Text == text, label + ": the refusal reads '" + Day.LastRefusal.Text + "' (expected '" + text + "')");
        }
        // A buy refusal (PlayerUpgrades.Refusal, the buyer only): RequestBuy clears the old answer first, so a fresh one is what arrives.
        // A buy refusal stays on the buyer (and in the HUD prompt, which prefers it) for
        // 2.5 s (PlayerUpgrades.RefusalSeconds): a prompt check after a refused buy waits it out.
        private static IEnumerator RefusalLapsed(PlayerUpgrades upgrades, string label)
            => Expect(() => string.IsNullOrEmpty(upgrades.Refusal), 3f, () => label + " the last buy refusal lapsed: '" + upgrades.Refusal + "'");

        private static IEnumerator ExpectBuyRefusal(PlayerUpgrades upgrades, string contains, string label)
        {
            yield return Expect(() => upgrades.Refusal.Contains(contains), 3f, () => label + ": the buyer's refusal reads '" + upgrades.Refusal + "' (expected to contain '" + contains + "')");
        }
        // The Shop rig swings for the pull on this peer: the played serial and a peak angle.
        private static IEnumerator ExpectSwing(int serial, string label)
        {
            float peak = 0f; float deadline = Time.unscaledTime + 1.2f;
            HQShopConsole c = Console();
            while (Time.unscaledTime < deadline)
            {
                if (c != null) peak = Mathf.Max(peak, c.LeverAngle);
                yield return null;
            }
            Check(c != null && c.LeverPlayedSerial == serial && peak > 0f, $"{label}: the Shop rig swung its lever for pull {serial} (played {(c == null ? -1 : c.LeverPlayedSerial)}, peak {peak:0.#}°)");
        }
        // From a reader's eye 1.6 m in front, at the screens.
        private static IEnumerator Shot(string name)
        {
            ConsoleRig rig = Rig();
            if (rig == null) yield break;
            Vector3 eye = rig.transform.position + Front() * 1.6f + Vector3.up * 1.6f;
            Say("capture " + H.CaptureFrom(eye, rig.transform.position + Vector3.up * 1.3f, Captures + name + ".png"));
            yield return null;
        }
        // The class names are compared as strings so this file compiles whether or not the
        // dead classes (ShopDisplay, ShopBrowserUI) are still in the project.
        private static int CountOfType(string typeName)
        {
            int n = 0;
            foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == typeName) n++;
            return n;
        }
        private static bool NamedInHQ(string name, UnityEngine.SceneManagement.Scene hq)
        {
            foreach (GameObject root in hq.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return true;
            return false;
        }
        private static bool NameStartsInHQ(string prefix, UnityEngine.SceneManagement.Scene hq)
        {
            foreach (GameObject root in hq.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
        private static int BoughtCount(string displayNameStartsWith, UnityEngine.SceneManagement.Scene hq)
        {
            int n = 0;
            foreach (CarryableItem c in CarryableItem.Spawned)
                if (c != null && c.name.StartsWith(displayNameStartsWith + " (bought)") && c.gameObject.scene == hq) n++;
            return n;
        }
        private static CarryableItem Bought(string displayNameStartsWith, UnityEngine.SceneManagement.Scene hq)
        {
            foreach (CarryableItem c in CarryableItem.Spawned)
                if (c != null && c.name.StartsWith(displayNameStartsWith + " (bought)") && c.gameObject.scene == hq) return c;
            return null;
        }
        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            Check(host.Upgrades != null, "the player prefab carries PlayerUpgrades (run the shop setup)");
            ShopCatalog catalog = ShopCatalog.Resolve();
            Check(catalog.Items.Count == 4 && catalog.Find(ShopCatalog.AirTankId) != null && catalog.Find(ShopCatalog.LargeTankId) != null && catalog.Find(ShopCatalog.BrightHeadlampId) != null && catalog.Find(ShopCatalog.PatchKitId) != null, $"the catalogue has the four baseline items ({catalog.Items.Count} rows)");
            ShopItem airTank = catalog.Find(ShopCatalog.AirTankId), largeTank = catalog.Find(ShopCatalog.LargeTankId), lamp = catalog.Find(ShopCatalog.BrightHeadlampId), patchKit = catalog.Find(ShopCatalog.PatchKitId);
            int pages = ShopConsoleRules.PageCount(catalog.Items.Count);
            Say($"prices: {airTank.Name} ${airTank.Price}, {largeTank.Name} ${largeTank.Price}, {lamp.Name} ${lamp.Price}, {patchKit.Name} ${patchKit.Price}; {ShopConsoleRules.RowsPerPage} rows a page, {pages} pages");
            Check(pages == 2, "two pages for four items (paging is live on the real catalogue)");
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ConsoleShopCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerHudUI hud = host.GetComponent<PlayerHudUI>();
            PlayerUpgrades upgrades = host.Upgrades;
            ShipControls ship = host.GetComponent<ShipControls>();
            Check(hud != null && ship != null, "the host carries PlayerHudUI and ShipControls");
            UnityEngine.SceneManagement.Scene hq = WorldScenes.Scene(WorldId.HQ);
            HQSigns signs = HQSigns.Resolve();
            string title = signs.Get("counter"), subtitle = signs.Get("counter.sub");
            Say($"title '{title}', lower heading '{subtitle}', prev '{signs.Get("shop.prev")}', next '{signs.Get("shop.next")}', lever '{signs.Get("lever.page")}'");
            int refusal, pulls;
            string buyRowAir = ShopConsoleRules.RowLine(airTank, ShopConsoleRules.StateBuy), buyRowLarge = ShopConsoleRules.RowLine(largeTank, ShopConsoleRules.StateBuy), buyRowLamp = ShopConsoleRules.RowLine(lamp, ShopConsoleRules.StateBuy);

            Heading("P0 — the console is the shop: the rig at the counter with its rows, zones and lever; the sign, the three fascia signs and the stands gone; page 1 of 2 with an empty pot");
            Day.ServerSetBalanceForChecks(0);
            Day.ServerSetShopPageForChecks(0);
            yield return Expect(() => Console() != null, 3f, () => "P0 the shop console composer is in the HQ scene");
            HQShopConsole console = Console();
            ConsoleRig rig = Rig();
            Check(rig != null && rig.Kind == ConsoleKind.Shop && rig.name == ConsoleRig.CatalogueRootName && rig.gameObject.scene == hq, $"P0 the rig '{rig?.name}' of kind {rig?.Kind} in the HQ scene");
            Check(rig.GetComponent<ConsoleLever>() != null && rig.LeverCollider != null && rig.TopSurface != null && rig.BottomSurface != null && rig.SignSurface != null, "P0 the rig has its lever, its collider and the three surfaces");
            Check(Vector3.Distance(rig.transform.position, ExpectedRoot) < 0.6f, $"P0 the root at {rig.transform.position} (expected {ExpectedRoot} ± 0.6 m)");
            Check(Vector3.Dot(Front(), Vector3.back) > 0.9f, $"P0 the console faces south into the depot aisle (front {Front()})");
            {
                ConsoleControl[] controls = rig.GetComponentsInChildren<ConsoleControl>(true);
                int rows = 0;
                for (int r = 0; r < ShopConsoleRules.RowsPerPage; r++)
                {
                    ConsoleControl c = controls.FirstOrDefault(x => x.name == ConsoleRig.RowNamePrefix + r);
                    if (c != null && c.Console == ConsoleKind.Shop && c.Kind == ConsoleControlKind.ShopRow && c.Index == r && c.transform.IsChildOf(rig.TopSurface.transform) && c.GetComponent<Collider>() != null && !c.GetComponent<Collider>().isTrigger) rows++;
                }
                Check(rows == ShopConsoleRules.RowsPerPage, $"P0 {rows}/{ShopConsoleRules.RowsPerPage} row zones 'Shop Row 0..{ShopConsoleRules.RowsPerPage - 1}' under the top screen (Shop/ShopRow, Index r, solid colliders)");
                ConsoleControl prev = controls.FirstOrDefault(x => x.name == ConsoleRig.PrevName), next = controls.FirstOrDefault(x => x.name == ConsoleRig.NextName);
                Check(prev != null && prev.Console == ConsoleKind.Shop && prev.Kind == ConsoleControlKind.ShopPage && prev.Index == -1 && prev.transform.IsChildOf(rig.TopSurface.transform), "P0 the PREV zone '" + ConsoleRig.PrevName + "' (Shop/ShopPage, Index -1)");
                Check(next != null && next.Console == ConsoleKind.Shop && next.Kind == ConsoleControlKind.ShopPage && next.Index == 1 && next.transform.IsChildOf(rig.TopSurface.transform), "P0 the NEXT zone '" + ConsoleRig.NextName + "' (Shop/ShopPage, Index +1)");
                ConsoleControl lever = controls.FirstOrDefault(x => x.name == ConsoleRig.CatalogueLeverName);
                Check(lever != null && lever.Console == ConsoleKind.Shop && lever.Kind == ConsoleControlKind.Lever, "P0 the lever control '" + ConsoleRig.CatalogueLeverName + "'");
                Check(GameObject.Find(ConsoleRig.CatalogueLeverName) == lever.gameObject && GameObject.Find(ConsoleRig.RowNamePrefix + "0") != null && GameObject.Find(ConsoleRig.NextName) != null, "P0 the control names resolve by GameObject.Find (what the aim hook uses)");
            }
            {
                var gone = new List<string>();
                foreach (string name in new[] { "Equipment catalogue counter", "Catalogue sign mast", "counter Sign", "counter.sub Sign", "office Sign", "upgrades Sign", "gear Sign", "Shop Stand " + ShopCatalog.AirTankId, "Shop Stand " + ShopCatalog.LargeTankId, "Shop Stand " + ShopCatalog.BrightHeadlampId, "Shop Stand " + ShopCatalog.PatchKitId })
                    if (NamedInHQ(name, hq)) gone.Add(name);
                Check(gone.Count == 0 && !NameStartsInHQ("Shop Stand", hq), "P0 the hanging sign, the three fascia signs, the counter and the four stands are gone from the HQ scene" + (gone.Count == 0 ? string.Empty : " (still there: " + string.Join(", ", gone) + ")"));
                int displays = CountOfType("ShopDisplay");
                Check(displays == 0, $"P0 no ShopDisplay component anywhere ({displays})");
                Check(GameObject.Find(HQPrototypeBuilder.ShopRoomName) != null, "P0 the depot group '" + HQPrototypeBuilder.ShopRoomName + "' still stands");
                Check(ShopDeliveryPoint.Nearest(rig.transform.position, hq) != null, "P0 the chute is in the HQ scene for the console's deliveries");
            }
            Check(Day.ShopPage == 0 && Day.Balance == 0, $"P0 the server's page {Day.ShopPage} and pot ${Day.Balance}");
            yield return Expect(() => console.Page == 0 && console.Pages == pages && Top().Contains(title) && Top().Contains(string.Format(signs.Get("shop.pot"), 0)) && Top().Contains(ShopConsoleRules.PageText(0, pages)), 2f, () => "P0 the top screen: " + Top());
            string noMoneyRows = ShopConsoleRules.RowLine(airTank, ShopConsoleRules.StateNoMoney) + "|" + ShopConsoleRules.RowLine(largeTank, ShopConsoleRules.StateNoMoney) + "|" + ShopConsoleRules.RowLine(lamp, ShopConsoleRules.StateNoMoney);
            Check(console.RowsText == noMoneyRows && Top().Contains(noMoneyRows), $"P0 the first page's rows read NO MONEY with an empty pot: '{console.RowsText}'");
            Check(Bottom().Contains(subtitle), "P0 the lower screen carries Dan's second phrase: " + Bottom());
            Check(Sign() == signs.Get("lever.page") + "/on", "P0 the sign (two pages: lit): " + Sign());
            Check(console.Text.StartsWith("CATALOGUE " + ShopConsoleRules.PageText(0, pages)), "P0 the compat text: " + console.Text.Replace("\n", " | "));
            Check(CountOfType("ShopBrowserUI") == 0 && !SunkCost.Net.SessionInputGate.ShopOpen, $"P0 no HUD browser component on anyone ({CountOfType("ShopBrowserUI")}) and no shop window open");
            yield return AtConsole();
            yield return Shot("console-shop-P0-page1-nomoney");

            Heading("P1 — a row with no money: the prompt offers the buy, E is refused 'Not enough money', the refusal shows in the prompt and on the lower screen, nothing sold");
            yield return AtConsole();
            yield return AimRow(0);
            yield return null;
            Check(hud.PromptText.Contains($"{airTank.Name} · ${airTank.Price}") && hud.PromptText.Contains("Press E to buy") && hud.PromptText.Contains("pot $0"), "P1 the prompt: " + hud.PromptText);
            Check(console.AimedRow == 0, $"P1 the composer sees the aim on row {console.AimedRow}");
            refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return ExpectBuyRefusal(upgrades, "Not enough money", "P1 E with an empty pot");
            Check(upgrades.Refusal.Contains($"${airTank.Price} needed") && upgrades.Refusal.Contains("$0 in the pot"), "P1 the refusal names the price and the pot: " + upgrades.Refusal);
            yield return null;
            Check(hud.PromptText == upgrades.Refusal, "P1 the refusal shows in the prompt: " + hud.PromptText);
            yield return Expect(() => console.Bottom != null && console.Bottom.Notice && console.Bottom.Status.Text.Contains("Not enough money"), 1f, () => "P1 the lower screen shows the buyer's refusal as a notice: " + Bottom());
            Check(Bought(airTank.Name, hq) == null && Day.Balance == 0, "P1 nothing was sold");
            Check(Day.LastRefusal.Serial == refusal, "P1 a buy refusal is the buyer's alone (the console's shared refusal serial did not move)");
            Check(!SunkCost.Net.SessionInputGate.ShopOpen, "P1 no shop window opened on E");
            yield return Shot("console-shop-P1-refused");

            Heading("P2 — a buy from a row: $500 in the pot, every row reads BUY, E on the air tank's row delivers it through the chute onto the pickup pad and charges $40");
            Day.ServerSetBalanceForChecks(500);
            yield return Expect(() => console.RowsText == buyRowAir + "|" + buyRowLarge + "|" + buyRowLamp, 2f, () => $"P2 the rows read BUY with $500: '{console.RowsText}'");
            yield return Expect(() => Top().Contains(string.Format(signs.Get("shop.pot"), 500)), 2f, () => "P2 the pot on the screen: " + Top());
            yield return AtConsole();
            yield return AimRow(0);
            yield return RefusalLapsed(upgrades, "P2");
            yield return null;
            Check(hud.PromptText.Contains("Press E to buy") && hud.PromptText.Contains("pot $500"), "P2 the prompt with money: " + hud.PromptText);
            yield return Shot("console-shop-P2-page1-buy-aimed");
            yield return Press(Key.E);
            yield return Expect(() => Bought(airTank.Name, hq) != null, 3f, () => "P2 an air tank was spawned");
            CarryableItem bought = Bought(airTank.Name, hq);
            Check(logs.Any(l => l.Contains("[Shop]") && l.Contains("bought " + airTank.Name)), "P2 the server logged the sale: " + string.Join(" / ", logs.Where(l => l.Contains("[Shop]")).TakeLast(1)));
            yield return Wait(1.2f);
            yield return Expect(() => bought.transform.position.y < .6f, 4f, () => "P2 the purchase reaches the pickup floor");
            Vector3 landed = bought.transform.position;
            ShopDeliveryPoint chute = ShopDeliveryPoint.Nearest(rig.transform.position, hq);
            Check(landed.x > 8f && landed.x < 12f && landed.z > 10f && landed.z < 14f && landed.y > -.1f, $"P2 the purchase landed inside the marked pickup pad: {landed}");
            Check(Flat(landed, chute.transform.position) < 3f, $"P2 it came down the chute at {chute.transform.position} ({Flat(landed, chute.transform.position):0.00} m off)");
            Check(bought.CanGrabFromWorld && bought.HolderClientId < 0 && bought.gameObject.scene == hq, "P2 loose in the HQ scene, grabbable (" + bought.State + ")");
            Check(Day.Balance == 500 - airTank.Price, $"P2 the pot paid ${airTank.Price}: ${Day.Balance} left");
            yield return Expect(() => Top().Contains(string.Format(signs.Get("shop.pot"), 500 - airTank.Price)), 2f, () => "P2 the screen's pot after the buy: " + Top());
            Check(!SunkCost.Net.SessionInputGate.ShopOpen && upgrades.Refusal == string.Empty, "P2 no window, no refusal");
            yield return AtConsole();
            yield return Shot("console-shop-P2-bought");

            Heading("P3 — an upgrade row: E buys the large tank, the row turns OWNED, the prompt says owned, a second E is refused 'already have'");
            yield return AimRow(1);
            yield return null;
            Check(hud.PromptText.Contains($"{largeTank.Name} · ${largeTank.Price}") && hud.PromptText.Contains("Press E to buy"), "P3 the prompt: " + hud.PromptText);
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Has(PlayerUpgrade.LargeTank), 3f, () => "P3 the large tank is owned");
            int afterLarge = 500 - airTank.Price - largeTank.Price;
            Check(Day.Balance == afterLarge, $"P3 the pot paid ${largeTank.Price}: ${Day.Balance} left");
            yield return Expect(() => Row(1) == ShopConsoleRules.RowLine(largeTank, ShopConsoleRules.StateOwned), 2f, () => $"P3 the row reads OWNED: '{Row(1)}' (rows '{console.RowsText}')");
            yield return Expect(() => hud.PromptText.Contains("owned"), 1.5f, () => "P3 the prompt on the owned row: " + hud.PromptText);
            Check(hud.PromptText == $"{largeTank.Name} · owned", "P3 the prompt's words: " + hud.PromptText);
            yield return Shot("console-shop-P3-owned");
            yield return Press(Key.E);
            yield return ExpectBuyRefusal(upgrades, "already have", "P3 buying it again");
            Check(Day.Balance == afterLarge, "P3 nothing charged for the refused buy");
            yield return Expect(() => console.Bottom.Notice && console.Bottom.Status.Text.Contains("already have"), 1f, () => "P3 the lower screen shows it: " + Bottom());

            Heading("P4 — paging: NEXT turns the shared page, NEXT at the end is refused 'No such page', PREV turns it back, the lever is NEXT PAGE and wraps");
            yield return AtConsole();
            yield return AimNext();
            yield return RefusalLapsed(upgrades, "P4");
            yield return null;
            Check(hud.PromptText == "Press E — next page", "P4 the NEXT zone's prompt: " + hud.PromptText);
            Check(console.AimedPage == 1, $"P4 the composer sees NEXT aimed ({console.AimedPage})");
            refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.ShopPage == 1, 2f, () => $"P4 the server turned to page {Day.ShopPage}");
            yield return Expect(() => console.Page == 1 && Top().Contains(ShopConsoleRules.PageText(1, pages)), 2f, () => "P4 the top screen on page 2: " + Top());
            string page2Rows = ShopConsoleRules.RowLine(patchKit, ShopConsoleRules.StateBuy) + "|-|-";
            Check(console.RowsText == page2Rows, $"P4 page 2's rows: '{console.RowsText}' (expected '{page2Rows}')");
            Check(Day.LastRefusal.Serial == refusal, "P4 no refusal for the turn");
            yield return Shot("console-shop-P4-page2");
            yield return AimNext();
            yield return null;
            Check(hud.PromptText == "Last page", "P4 NEXT on the last page: " + hud.PromptText);
            refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return ExpectRefusal(refusal, ConsoleRules.NoSuchPage, "P4 E on NEXT at the end");
            Check(Day.ShopPage == 1, "P4 the page stays 2 after the refused turn");
            yield return AimPrev();
            yield return null;
            Check(hud.PromptText == "Press E — previous page", "P4 the PREV zone's prompt: " + hud.PromptText);
            yield return Press(Key.E);
            yield return Expect(() => Day.ShopPage == 0 && console.Page == 0, 2f, () => $"P4 PREV turned back to page {Day.ShopPage + 1}");
            yield return AimPrev();
            yield return null;
            Check(hud.PromptText == "First page", "P4 PREV on the first page: " + hud.PromptText);
            yield return AimLever();
            yield return null;
            Check(hud.PromptText == "Press E — next page", "P4 the lever's prompt: " + hud.PromptText);
            Check(console.Sign.Aimed && Sign() == signs.Get("lever.page") + "/on", "P4 the sign lights as aimed: " + Sign());
            pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1, 2f, () => "P4 one pull counted: " + H.LeverPullText());
            Check(Day.LastLeverPull.Kind == ConsoleKind.Shop && Day.LastLeverPull.Action == LeverAction.NextPage, "P4 the pull is Shop/NextPage: " + H.LeverPullText());
            yield return Expect(() => Day.ShopPage == 1, 2f, () => $"P4 the lever turned to page {Day.ShopPage + 1}");
            yield return ExpectSwing(pulls + 1, "P4");
            yield return AimLever();
            pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1 && Day.ShopPage == 0, 2f, () => $"P4 the lever wraps round to page {Day.ShopPage + 1} ({H.LeverPullText()})");
            Check(Day.LastRefusal.Serial == refusal, "P4 no refusal for the lever's two turns");
            Check(logs.Count(l => l.Contains("[Console]")) >= 2, "P4 the server logged the pulls: " + string.Join(" / ", logs.Where(l => l.Contains("[Console]")).TakeLast(2)));

            Heading("P5 — refusals: a buy and a turn from across the platform, pages outside the catalogue, a wrong expectation on the lever, the dead buy and press nothing");
            host.TeleportLocal(new Vector3(0f, 0.05f, 0f), 0f);
            yield return null; yield return null;
            int balance5 = Day.Balance; pulls = Day.LastLeverPull.Serial;
            upgrades.RequestBuy(ShopCatalog.AirTankId);
            yield return ExpectBuyRefusal(upgrades, ConsoleRules.NotAtConsole, "P5 a buy from (0,0)");
            refusal = Day.LastRefusal.Serial;
            ship.RequestShopPage(1);
            yield return ExpectRefusal(refusal, ConsoleRules.NotAtConsole, "P5 a page turn from (0,0)");
            yield return AtConsole();
            refusal = Day.LastRefusal.Serial;
            ship.RequestShopPage(5);
            yield return ExpectRefusal(refusal, ConsoleRules.NoSuchPage, "P5 page 6 of 2");
            refusal = Day.LastRefusal.Serial;
            ship.RequestShopPage(-1);
            yield return ExpectRefusal(refusal, ConsoleRules.NoSuchPage, "P5 page 0 of 2");
            refusal = Day.LastRefusal.Serial;
            ship.RequestLever(ConsoleKind.Shop, LeverAction.Pay, SiteId.None);
            yield return ExpectRefusal(refusal, ConsoleRules.SelectionChanged, "P5 a pull expecting PAY");
            upgrades.RequestBuy("no-such-item");
            yield return ExpectBuyRefusal(upgrades, "No such item", "P5 an item outside the catalogue");
            // The server's own death state (a debug death is refused at HQ: "You can only die below").
            Day.ServerPlayerDied(host.OwnerId); host.ServerSetDead(true);
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 5f, () => "P5 the host is dead");
            upgrades.RequestBuy(ShopCatalog.PatchKitId);
            yield return ExpectBuyRefusal(upgrades, "The dead buy nothing", "P5 a dead buy");
            refusal = Day.LastRefusal.Serial;
            ship.RequestShopPage(1);
            yield return ExpectRefusal(refusal, "The dead press nothing", "P5 a dead page turn");
            refusal = Day.LastRefusal.Serial;
            ship.RequestLever(ConsoleKind.Shop, LeverAction.NextPage, SiteId.None);
            yield return ExpectRefusal(refusal, "The dead press nothing", "P5 a dead pull");
            Check(Day.Balance == balance5 && Day.ShopPage == 0 && Day.LastLeverPull.Serial == pulls && BoughtCount(airTank.Name, hq) == 1 && Bought(patchKit.Name, hq) == null, $"P5 nothing changed: pot ${Day.Balance}, page {Day.ShopPage}, pull {Day.LastLeverPull.Serial}, one tank on the floor, no kit");
            Day.ServerRevive(host.OwnerId); host.ServerSetDead(false);
            yield return Expect(() => !host.IsDead && !Day.IsDead(host.OwnerId), 5f, () => "P5 the host revived");
            yield return Wait(0.5f);

            Heading("P6 — the HUD browser never opens: E on the console's body and E on a row leave the shop window shut; no ShopBrowserUI exists");
            yield return AtConsole();
            {
                // The body: look at the rig's footing (the root, on the deck), below every control.
                float deadline = Time.unscaledTime + 3f;
                while (Time.unscaledTime < deadline && host.CurrentConsoleControl != null) { H.ClientLookAtNamed(ConsoleRig.CatalogueRootName); yield return null; }
                Check(host.CurrentConsoleControl == null, "P6 the dot is off every control (looking at the footing)");
            }
            yield return Press(Key.E);
            yield return Hold(() => !SunkCost.Net.SessionInputGate.ShopOpen, 1f, () => "P6 E on the body opens nothing");
            yield return AimRow(2);
            yield return Press(Key.E);
            yield return Expect(() => upgrades.Has(PlayerUpgrade.BrightHeadlamp), 3f, () => "P6 E on the lamp's row buys the lamp (the row is the only thing E does here)");
            yield return Hold(() => !SunkCost.Net.SessionInputGate.ShopOpen, 1f, () => "P6 the shop window stays shut through a buy");
            Check(CountOfType("ShopBrowserUI") == 0 && host.GetComponents<MonoBehaviour>().All(m => m == null || m.GetType().Name != "ShopBrowserUI"), "P6 no ShopBrowserUI on the host or anywhere");
            Check(SunkCost.Net.SessionInputGate.CanPlay || SunkCost.Net.SessionInputGate.MenuOpen, "P6 gameplay input is not blocked by a shop window (CanPlay " + SunkCost.Net.SessionInputGate.CanPlay + ")");
            int afterLamp = Day.Balance;
            Say($"P6 pot after the lamp: ${afterLamp}");

            Heading("P7 — a same-frame double buy of an upgrade is charged once");
            upgrades.ServerClearAll();
            yield return Expect(() => upgrades.Owned == PlayerUpgrade.None, 2f, () => "P7 the host's upgrades cleared for the row (" + upgrades.Owned + ")");
            Day.ServerSetBalanceForChecks(400);
            yield return Wait(0.2f);
            upgrades.RequestBuy(ShopCatalog.BrightHeadlampId); upgrades.RequestBuy(ShopCatalog.BrightHeadlampId);
            yield return Expect(() => upgrades.Has(PlayerUpgrade.BrightHeadlamp), 3f, () => "P7 the lamp is owned");
            yield return ExpectBuyRefusal(upgrades, "already have", "P7 the second buy of the pair");
            yield return Hold(() => Day.Balance == 400 - lamp.Price, 0.8f, () => $"P7 charged once: ${Day.Balance} (expected ${400 - lamp.Price})");
            Check(Row(2) == ShopConsoleRules.RowLine(lamp, ShopConsoleRules.StateOwned), $"P7 the lamp's row reads OWNED: '{Row(2)}'");

            Heading("P8 — the catalogue grows without a rebuild: a fifth row in memory lands on page 2, row 2; the console sells it from there; removed again, the page clamps");
            extraRow = new ShopItem { Id = ProofId, Name = ProofName, Price = ProofPrice, Kind = ShopItemKind.Consumable, Prefab = airTank.Prefab };
            ((List<ShopItem>)catalog.Items).Add(extraRow);
            int proofPage = 4 / ShopConsoleRules.RowsPerPage, proofRow = 4 % ShopConsoleRules.RowsPerPage;
            yield return Expect(() => console.Pages == ShopConsoleRules.PageCount(5), 2f, () => $"P8 five items make {console.Pages} pages");
            Day.ServerSetShopPageForChecks(proofPage);
            yield return Expect(() => Day.ShopPage == proofPage && console.Page == proofPage && Row(proofRow) == ShopConsoleRules.RowLine(extraRow, ShopConsoleRules.StateBuy), 2f, () => $"P8 page {proofPage + 1} row {proofRow + 1} reads the new item: '{console.RowsText}'");
            yield return AtConsole();
            yield return AimRow(proofRow);
            yield return RefusalLapsed(upgrades, "P8");
            yield return null;
            Check(hud.PromptText.Contains($"{ProofName} · ${ProofPrice}") && hud.PromptText.Contains("Press E to buy"), "P8 the prompt names the new item: " + hud.PromptText);
            int before8 = Day.Balance;
            yield return Press(Key.E);
            yield return Expect(() => Day.Balance == before8 - ProofPrice, 3f, () => $"P8 sold for ${ProofPrice}: ${Day.Balance} (refusal '{upgrades.Refusal}')");
            yield return Expect(() => Bought(ProofName, hq) != null, 3f, () => "P8 the new item was delivered");
            CarryableItem proof = Bought(ProofName, hq);
            yield return Expect(() => proof.transform.position.y < .6f, 4f, () => "P8 it reached the pickup floor");
            Check(Flat(proof.transform.position, chute.transform.position) < 3f, $"P8 through the chute: {proof.transform.position}");
            RemoveProofRow();
            yield return Expect(() => console.Pages == pages, 2f, () => $"P8 four items again: {console.Pages} pages");
            Say($"P8 the server's page after the removal: {Day.ShopPage}, the console shows page {console.Page + 1}");
            Check(console.Page < console.Pages, "P8 the shown page is inside the catalogue");
            Day.ServerSetShopPageForChecks(0);
            yield return Expect(() => Day.ShopPage == 0 && console.Page == 0, 2f, () => "P8 back on page 1");

            // ---- the guest ---------------------------------------------------------------
            Heading("G1 — the page is shared: the guest joins on page 1 of 2, its NEXT turns the host's page, the host's PREV turns the guest's");
            upgrades.ServerClearAll();
            Day.ServerSetBalanceForChecks(0);
            yield return Wait(0.3f);
            guest = LaunchGuest(GuestDir);
            yield return Joined(guest, "G1");
            yield return GuestMove(guest, StandSpot(1.0f));
            yield return Wait(0.3f);
            yield return GuestEventually(guest, r => GPage(r) == "0" && GPages(r) == pages.ToString() && GTop(r).Contains(ShopConsoleRules.PageText(0, pages)) && GOpen(r) == "False", 10f, "G1 the guest's console: page 1 of 2, no shop window");
            Say("G1 the guest's console line: " + GuestConsoles(lastReply));
            yield return GuestLook(guest, ConsoleRig.NextName);
            Say("G1 the guest looks: " + lastReply.Split('\n')[0]);
            refusal = Day.LastRefusal.Serial;
            yield return GuestPage(guest, 1);
            Check(lastReply.Contains("requested page 1"), "G1 the guest's turn went: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.ShopPage == 1, 3f, () => $"G1 the server turned to page {Day.ShopPage + 1} on the guest's press");
            yield return Expect(() => console.Page == 1 && Top().Contains(ShopConsoleRules.PageText(1, pages)), 2f, () => "G1 the host's screen on page 2: " + Top());
            yield return GuestEventually(guest, r => GPage(r) == "1" && GTop(r).Contains(ShopConsoleRules.PageText(1, pages)), 2f, "G1 the guest's screen on page 2");
            Check(Day.LastRefusal.Serial == refusal, "G1 no refusal");
            yield return AtConsole();
            yield return AimPrev();
            yield return Press(Key.E);
            yield return Expect(() => Day.ShopPage == 0, 2f, () => "G1 the host's PREV turned back");
            yield return GuestEventually(guest, r => GPage(r) == "0", 2f, "G1 the guest's screen followed to page 1");

            Heading("G2 — a guest buys from the console: refused on money, then the large tank is its own; each screen shows its own player's state; an air tank comes down the chute");
            Day.ServerSetBalanceForChecks(50);
            yield return Wait(0.2f);
            yield return GuestBuy(guest, ShopCatalog.LargeTankId);
            yield return GuestEventually(guest, r => GRefusal(r, guest.Id).StartsWith("Not enough money"), 5f, "G2 $50 in the pot: the guest's large tank is refused, and the guest sees why");
            Say("G2 the guest's refusal: '" + GRefusal(lastReply, guest.Id) + "'");
            Day.ServerSetBalanceForChecks(400);
            yield return Wait(0.2f);
            yield return GuestBuy(guest, ShopCatalog.LargeTankId);
            yield return Expect(() => CopyOf(guest)?.Upgrades != null && CopyOf(guest).Upgrades.Has(PlayerUpgrade.LargeTank), 5f, () => "G2 the guest bought the large tank; the host's copy shows it");
            Check(!upgrades.Has(PlayerUpgrade.LargeTank), "G2 the host has none of it (upgrades are per player)");
            Check(Day.Balance == 400 - largeTank.Price, $"G2 the pot paid ${largeTank.Price}: ${Day.Balance} left");
            yield return GuestEventually(guest, r => GUpgrades(r, guest.Id) == "LargeTank", 5f, "G2 the guest reads its own upgrade");
            yield return GuestEventually(guest, r => GRows(r).Split('|').ElementAtOrDefault(1) == ShopConsoleRules.RowLine(largeTank, ShopConsoleRules.StateOwned), 3f, "G2 the guest's screen reads OWNED on the large tank's row");
            yield return Expect(() => Row(1) == ShopConsoleRules.RowLine(largeTank, ShopConsoleRules.StateNoMoney), 2f, () => $"G2 the host's screen reads NO MONEY on the same row (${Day.Balance} < ${largeTank.Price}): '{Row(1)}'");
            Say($"G2 host rows '{console.RowsText}' | guest rows '{GRows(lastReply)}'");
            int tanks = BoughtCount(airTank.Name, hq);
            // The guest names a spawned item by its prefab, not by the server's "(bought)" name: count its tank items by display name.
            yield return Send(guest, "{\"id\":{id},\"action\":\"snapshot\"}");
            int guestTanks = GuestItems(lastReply, AirTankItem.FullName, "HQPrototype");
            yield return GuestBuy(guest, ShopCatalog.AirTankId);
            yield return Expect(() => BoughtCount(airTank.Name, hq) == tanks + 1, 5f, () => $"G2 the guest's air tank was spawned ({BoughtCount(airTank.Name, hq)} on the floor)");
            CarryableItem guestTank = CarryableItem.Spawned.Where(c => c != null && c.name.StartsWith(airTank.Name + " (bought)") && c.gameObject.scene == hq).OrderByDescending(c => c.ObjectId).First();
            yield return Expect(() => guestTank.transform.position.y < .6f, 4f, () => "G2 it reached the pickup floor");
            Check(Flat(guestTank.transform.position, chute.transform.position) < 3f, $"G2 delivered through the chute where the host's was: {guestTank.transform.position}");
            Check(Day.Balance == 400 - largeTank.Price - airTank.Price, $"G2 the pot paid ${airTank.Price}: ${Day.Balance} left");
            yield return GuestEventually(guest, r => GuestHeader(r).Contains($"balance={Day.Balance}") && GuestItems(r, AirTankItem.FullName, "HQPrototype") == guestTanks + 1, 5f, $"G2 the guest sees the pot and one more tank on the floor ({guestTanks} → {guestTanks + 1})");

            Heading("G3 — the guest's refusals: a page outside the catalogue, then a turn and a buy from across the platform");
            refusal = Day.LastRefusal.Serial;
            yield return GuestPage(guest, 7);
            yield return ExpectRefusal(refusal, ConsoleRules.NoSuchPage, "G3 the guest's page 8 of 2");
            yield return GuestMove(guest, new Vector3(0f, 0.05f, 0f));
            yield return Wait(0.3f);
            refusal = Day.LastRefusal.Serial;
            yield return GuestPage(guest, 1);
            yield return ExpectRefusal(refusal, ConsoleRules.NotAtConsole, "G3 the guest's far turn");
            int balance3 = Day.Balance;
            yield return GuestBuy(guest, ShopCatalog.PatchKitId);
            yield return GuestEventually(guest, r => GRefusal(r, guest.Id) == ConsoleRules.NotAtConsole, 5f, "G3 the guest's far buy is refused '" + ConsoleRules.NotAtConsole + "'");
            Check(Day.ShopPage == 0 && Day.Balance == balance3 && Bought(patchKit.Name, hq) == null, $"G3 nothing changed: page {Day.ShopPage}, pot ${Day.Balance}, no kit");

            Heading("G4 — the dead guest buys and presses nothing; revived after");
            yield return GuestMove(guest, StandSpot(1.0f));
            yield return Wait(0.3f);
            Day.ServerPlayerDied(guest.Id); CopyOf(guest)?.ServerSetDead(true);
            yield return Expect(() => Day.IsDead(guest.Id), 5f, () => "G4 the guest is dead on the server");
            yield return GuestBuy(guest, ShopCatalog.PatchKitId);
            yield return GuestEventually(guest, r => GRefusal(r, guest.Id) == "The dead buy nothing", 5f, "G4 the dead guest's buy is refused");
            refusal = Day.LastRefusal.Serial;
            yield return GuestPage(guest, 1);
            yield return ExpectRefusal(refusal, "The dead press nothing", "G4 the dead guest's turn");
            Check(Day.ShopPage == 0 && Day.Balance == balance3, $"G4 nothing changed: page {Day.ShopPage}, pot ${Day.Balance}");
            Day.ServerRevive(guest.Id); CopyOf(guest)?.ServerSetDead(false);
            yield return Expect(() => !Day.IsDead(guest.Id), 5f, () => "G4 the guest revived");
            yield return GuestEventually(guest, r => GuestPlayerLine(r, guest.Id).Contains("dead=False"), 8f, "G4 the guest reads itself alive");

            Heading("G5 — the guest sees the host's lever: the pull's serial and the page follow on the guest's screen; the guest leaves");
            yield return AtConsole();
            yield return AimLever();
            yield return null;
            pulls = Day.LastLeverPull.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1 && Day.ShopPage == 1, 2f, () => $"G5 the host's pull turned to page {Day.ShopPage + 1} ({H.LeverPullText()})");
            yield return GuestEventually(guest, r => GPlayed(r) == (pulls + 1).ToString() && GPage(r) == "1", 3f, $"G5 the guest's rig played pull {pulls + 1} and its screen is on page 2");
            Say("G5 the guest's lever angle in its last reply: " + Field(GuestConsoles(lastReply), "shopLeverAngle"));
            yield return Send(guest, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => CopyOf(guest) == null, 15f, () => "G5 the guest left");
            Day.ServerSetShopPageForChecks(0);

            Heading("end — the side view for the sharpness judgement; no exception in the host's log");
            yield return AtConsole();
            {
                Vector3 eye = rig.transform.position + Front() * 2.2f + RightOf() * 1.6f + Vector3.up * 1.5f;
                string saved = H.CaptureFrom(eye, rig.transform.position + Vector3.up * 1.4f, Captures + "console-shop-side.png");
                Say("capture " + saved);
                Check(saved.StartsWith("saved") && File.Exists(Captures + "console-shop-side.png"), "the side capture exists: " + saved);
            }
            Say($"host log: {errorsSeen} errors, {exceptionsSeen} exceptions during the run");
            Check(exceptionsSeen == 0, "no exception in the host's log during the run: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]")).Take(3)));
            Say("end: " + State().Replace("\n", " | "));
        }
    }
}
