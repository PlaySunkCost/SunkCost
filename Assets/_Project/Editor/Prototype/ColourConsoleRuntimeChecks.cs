using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Look;
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
    // The crew colour console at the arrival pad (Dan, 8 October 2026; scratchpad
    // hq-consoles/BRIEF.md A, TESTPLAN.md §1). Host-only rows first (K): the console
    // stands where the old colour panel stood and the panel is gone (K0), a swatch
    // press moves the shared helmet without colouring anyone (K1), the lever takes the
    // shown colour for the puller, saves it and swings, the lower screen names who took
    // what for three seconds (K2), the same colour again is allowed (K3), far and
    // hand-made presses are refused through LastRefusal (K4), two presses in one frame:
    // the last wins (K5), the dead press nothing (K6), the pure composers (K7). Then
    // guests (G): a late joiner sees the shown swatch with no replayed swing (G1), a
    // guest's press moves the host's helmet (G2), a host pull colours the host and the
    // guest sees the suit and the IS NOW line (G3), a guest pull colours the guest only
    // (G4), same-frame presses and pulls from two peers (G5/G6), the dead and the far
    // guest refused (G7/G8), a second late joiner (G9), a side-view capture for the
    // sharpness judgement (G10). Every row asserts replicated state (CrewDayState on the
    // host, the guests' `hqconsoles=` and `player=` snapshot lines) and the composer's
    // models (what the screens draw).
    // Log: Temp/console-colour-matrix.log. Started by CameraClearanceMatrixDriver.Start("console-colour").
    public static class ColourConsoleRuntimeChecks
    {
        private const string Log = "Temp/console-colour-matrix.log";
        private const string DirA = "Temp/console-colour-guest-a";
        private const string DirB = "Temp/console-colour-guest-b";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Captures = "Temp/look/";
        // Where the console stands (INTERFACES §6.5: pad-local (0, 0, 3.2) under "Crew Arrival" at (18, 0, 14) yaw 180;
        // the A7 contingency (0, 0, 3.4) → (18, 0.05, 10.6) is inside the 0.6 m tolerance).
        private static readonly Vector3 ExpectedRoot = new(18f, 0.05f, 10.8f);

        private sealed class Guest
        {
            public string Dir; public Process Process; public int Id = -1; public string Name = string.Empty;
            public override string ToString() => Dir + "#" + Id;
        }

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Guest guestA, guestB;
        private static int guestCommand = 6400;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static readonly List<string> logs = new();
        private static int exceptionsSeen, errorsSeen;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run console-colour matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Console-colour matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Console-colour matrix started " + DateTime.Now + "\n");
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
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert || type == LogType.Warning || message.Contains("[Console]"))
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
            if (Status == "MATRIX_PASS") Debug.Log("Console-colour matrix: MATRIX_PASS"); else Debug.LogError("Console-colour matrix: " + Status);
            foreach (Guest g in new[] { guestA, guestB })
                try { if (g?.Process != null && !g.Process.HasExited) g.Process.Kill(); } catch (Exception) { }
            guestA = guestB = null;
            steps = null;
            stack.Clear();
            Application.logMessageReceived -= OnLog;
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            EditorApplication.update -= Tick;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static string State()
        {
            HQColourConsole c = Console();
            string console = c == null ? "(no colour console)" : $"shown={c.Shown} aimed={c.AimedSwatch} taken={c.TakenShowing} played={c.LeverPlayedSerial} angle={c.LeverAngle:0.#}\ntop={Top()}\nbottom={Bottom()}\nsign={Sign()}";
            string day = Day == null ? "(no day state)" : $"ShownSwatch={Day.ShownSwatch} take={TakeText()} pull={H.LeverPullText()} refusal={Day.LastRefusal.Serial}:'{Day.LastRefusal.Text}'";
            return H.FlowStatus() + "\n" + day + "\n" + console;
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
        private static int ColourOf(Guest g) { PlayerIdentity id = CopyOf(g)?.GetComponent<PlayerIdentity>(); return id == null ? -1 : id.ColourIndex; }

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
        // The `hqconsoles=1; shownSwatch=…` line (INTERFACES §9).
        private static string GuestConsoles(string reply) => GuestLine(reply, "hqconsoles=");
        private static string GuestPlayerLine(string reply, int ownerId) => GuestLine(reply, "player=" + ownerId + ";");
        // "key=value; " on a snapshot line (the values carry " · " but never "; ").
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
        private static string GShown(string reply) => Field(GuestConsoles(reply), "shownSwatch");
        private static string GShownName(string reply) => Field(GuestConsoles(reply), "shownName");
        private static string GTop(string reply) => Field(GuestConsoles(reply), "colourTop");
        private static string GBottom(string reply) => Field(GuestConsoles(reply), "colourBottom");
        private static string GTake(string reply) => Field(GuestConsoles(reply), "colourTake");
        private static string GPlayed(string reply) => Field(GuestConsoles(reply), "colourLeverPlayed");
        private static string GColour(string reply, int ownerId) => Field(GuestPlayerLine(reply, ownerId), "colour");
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static IEnumerator GuestMove(Guest g, Vector3 to) { yield return Send(g, "{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static IEnumerator GuestSwatch(Guest g, int index) { yield return Send(g, "{\"id\":{id},\"action\":\"swatch\",\"slot\":" + index + "}"); }
        private static IEnumerator GuestLever(Guest g) { yield return Send(g, "{\"id\":{id},\"action\":\"lever\",\"item\":\"Colour\"}"); }
        private static IEnumerator GuestLook(Guest g, string control) { yield return Send(g, "{\"id\":{id},\"action\":\"look_control\",\"item\":\"" + control + "\"}"); }

        // ---- the console under test -----------------------------------------------------

        private static HQColourConsole Console() => HQColourConsole.InHQ();
        private static ConsoleRig Rig() => Console()?.GetComponent<ConsoleRig>();
        private static string Top() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Top);
        private static string Bottom() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Bottom);
        private static string Sign() => Console() == null ? "(no console)" : ConsoleModels.Flatten(Console().Sign);
        private static string BottomStatus() => Console()?.Bottom?.Status.Text ?? "(none)";
        private static string TakeText() { ColourTake t = Day.LastColourTake; return $"{t.Serial}/{t.Taker}/{t.Swatch}"; }
        private static string Name(int index) => PlayerPalette.Name(index);
        private static bool Wears(HQPlayerController p, int index) => p != null && Mathf.Abs(p.BodyColour.r - PlayerPalette.Get(index).r) < 0.02f && Mathf.Abs(p.BodyColour.b - PlayerPalette.Get(index).b) < 0.02f;
        private static Vector3 Front() { Vector3 f = Rig().transform.rotation * Vector3.forward; f.y = 0f; return f.normalized; }
        private static Vector3 RightOf() => Vector3.Cross(Vector3.up, Front());

        // In front of the Colour rig, standing where a reader stands; `side` metres to the rig's right.
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
        private static IEnumerator Aim(string objectName, ConsoleControlKind kind, int index = -1)
        {
            HQPlayerController host = Host();
            float deadline = Time.unscaledTime + 4f; int tries = 0;
            while (Time.unscaledTime < deadline)
            {
                ConsoleControl c = host.CurrentConsoleControl;
                if (c != null && c.Kind == kind && c.Console == ConsoleKind.Colour && (index < 0 || c.Index == index)) break;
                if (tries++ % 45 == 0) host.TeleportLocal(StandSpot(0f), FacingYaw());
                H.ClientLookAtNamed(objectName);
                yield return null;
            }
            ConsoleControl aimed = host.CurrentConsoleControl;
            Check(aimed != null && aimed.Kind == kind && aimed.Console == ConsoleKind.Colour && (index < 0 || aimed.Index == index), "the dot is on " + objectName + " (aim " + (aimed == null ? "none" : aimed.Console + "/" + aimed.Kind + "/" + aimed.Index) + ")");
        }
        private static IEnumerator AimSwatch(int index) => Aim(ConsoleRig.SwatchNamePrefix + index, ConsoleControlKind.Swatch, index);
        private static IEnumerator AimLever() => Aim(ConsoleRig.ColourLeverName, ConsoleControlKind.Lever);
        private static IEnumerator ExpectRefusal(int serialBefore, string text, string label)
        {
            yield return Expect(() => Day.LastRefusal.Serial > serialBefore, 3f, () => label + ": refused '" + Day.LastRefusal.Text + "'");
            Check(Day.LastRefusal.Text == text, label + ": the refusal reads '" + Day.LastRefusal.Text + "' (expected '" + text + "')");
        }
        // The Colour rig swings for the pull on this peer: the played serial and a peak angle.
        private static IEnumerator ExpectSwing(int serial, string label)
        {
            float peak = 0f; float deadline = Time.unscaledTime + 1.2f;
            HQColourConsole c = Console();
            while (Time.unscaledTime < deadline)
            {
                if (c != null) peak = Mathf.Max(peak, c.LeverAngle);
                yield return null;
            }
            Check(c != null && c.LeverPlayedSerial == serial && peak > 0f, $"{label}: the Colour rig swung its lever for pull {serial} (played {(c == null ? -1 : c.LeverPlayedSerial)}, peak {peak:0.#}°)");
        }
        // From a reader's eye 1.6 m in front, at the top screen.
        private static IEnumerator Shot(string name)
        {
            ConsoleRig rig = Rig();
            if (rig == null) yield break;
            Vector3 eye = rig.transform.position + Front() * 1.6f + Vector3.up * 1.6f;
            Say("capture " + H.CaptureFrom(eye, rig.transform.position + Vector3.up * 1.3f, Captures + name + ".png"));
            yield return null;
        }
        private static int Count(string contains) => logs.Count(l => l.Contains(contains));

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ConsoleColourCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SunkCost.Net.SessionInputGate.OpenMenu();
            Keys(); yield return null;
            PlayerIdentity identity = host.GetComponent<PlayerIdentity>();
            ShipControls ship = host.GetComponent<ShipControls>();
            Check(identity != null && ship != null, "the host carries PlayerIdentity and ShipControls");
            string me = WorldSceneFlow.DisplayName(host.OwnerId);
            string ME = me.ToUpperInvariant();
            UnityEngine.SceneManagement.Scene hq = WorldScenes.Scene(WorldId.HQ);
            HQSigns signs = HQSigns.Resolve();
            string pullLine = signs.Get("colour.pull"), takenFormat = signs.Get("colour.taken");
            Say($"host '{me}' (colour {identity.ColourIndex}), {PlayerPalette.Count} swatches: {string.Join(" ", PlayerPalette.Names)}; pull line '{pullLine}', taken '{takenFormat}'");
            int refusal, pulls, takes;

            Heading("K0 — the console stands where the colour panel stood, facing the pad; the panel is gone; sixteen swatch zones and the lever; red shown, nothing taken");
            yield return Expect(() => Console() != null, 3f, () => "K0 the colour console composer is in the HQ scene");
            HQColourConsole console = Console();
            ConsoleRig rig = Rig();
            Check(rig != null && rig.Kind == ConsoleKind.Colour && rig.name == ConsoleRig.ColourRootName && rig.gameObject.scene == hq, $"K0 the rig '{rig?.name}' of kind {rig?.Kind} in the HQ scene");
            Check(rig.GetComponent<ConsoleLever>() != null && rig.LeverCollider != null && rig.TopSurface != null && rig.BottomSurface != null && rig.SignSurface != null, "K0 the rig has its lever, its collider and the three surfaces");
            Check(Vector3.Distance(rig.transform.position, ExpectedRoot) < 0.6f, $"K0 the root at {rig.transform.position} (expected {ExpectedRoot} ± 0.6 m)");
            {
                GameObject spawns = GameObject.Find("Spawn Points");
                Check(spawns != null && spawns.transform.childCount > 0, "K0 the spawn points");
                int facing = 0;
                foreach (Transform s in spawns.transform) if (Vector3.Dot(Front(), s.position - rig.transform.position) > 0f) facing++;
                Check(facing == spawns.transform.childCount, $"K0 the console faces every spawn ({facing}/{spawns.transform.childCount} in front of it; front {Front()})");
                float nearest = float.MaxValue;
                foreach (Transform s in spawns.transform) nearest = Mathf.Min(nearest, Vector3.Distance(new Vector3(s.position.x, 0f, s.position.z), new Vector3(rig.transform.position.x, 0f, rig.transform.position.z)));
                Say($"K0 the nearest spawn is {nearest:0.00} m from the root");
            }
            {
                ConsoleControl[] controls = rig.GetComponentsInChildren<ConsoleControl>(true);
                int swatches = 0;
                for (int i = 0; i < PlayerPalette.Count; i++)
                {
                    ConsoleControl c = controls.FirstOrDefault(x => x.name == ConsoleRig.SwatchNamePrefix + i);
                    if (c != null && c.Console == ConsoleKind.Colour && c.Kind == ConsoleControlKind.Swatch && c.Index == i && c.transform.IsChildOf(rig.TopSurface.transform) && c.GetComponent<Collider>() != null && !c.GetComponent<Collider>().isTrigger) swatches++;
                }
                Check(swatches == PlayerPalette.Count, $"K0 {swatches}/{PlayerPalette.Count} swatch zones 'Swatch 0..15' under the top screen (Colour/Swatch, Index i, solid colliders)");
                ConsoleControl lever = controls.FirstOrDefault(x => x.name == ConsoleRig.ColourLeverName);
                Check(lever != null && lever.Console == ConsoleKind.Colour && lever.Kind == ConsoleControlKind.Lever, "K0 the lever control '" + ConsoleRig.ColourLeverName + "'");
                Check(GameObject.Find(ConsoleRig.ColourLeverName) == lever.gameObject && GameObject.Find(ConsoleRig.SwatchNamePrefix + "9") != null, "K0 the control names resolve by GameObject.Find (what the aim hook uses)");
            }
            {
                // By name and, while the dead class still exists, by type through reflection (the
                // final agent deletes ColourPanel.cs; this row must not need it to compile).
                Type panelType = typeof(ConsoleRig).Assembly.GetType("SunkCost.World.ColourPanel");
                int panels = panelType == null ? 0 : UnityEngine.Object.FindObjectsByType(panelType, FindObjectsInactive.Include).Length;
                Check(GameObject.Find("Colour Panel") == null && panels == 0, $"K0 the old colour panel is gone (no 'Colour Panel' object; {panels} ColourPanel component(s); class {(panelType == null ? "deleted" : "still compiled")})");
            }
            Check(Day.ShownSwatch == 0 && Day.LastColourTake.Serial == 0, $"K0 the server shows swatch {Day.ShownSwatch} (red) and nobody has taken (take {TakeText()})");
            yield return Expect(() => console.Shown == 0 && Top().Contains("CHOOSE YOUR CREW COLOUR") && Top().Contains("RED"), 2f, () => "K0 the top screen: " + Top());
            Check(Bottom().Contains(pullLine), "K0 the lower screen: " + Bottom());
            Check(Sign() == "TAKE/on", "K0 the sign: " + Sign());
            Check(console.LeverPlayedSerial == 0 && !console.TakenShowing, $"K0 no swing played, no IS NOW line (played {console.LeverPlayedSerial})");
            Check(Day.LastLeverPull.Serial == 0 || Day.LastLeverPull.Kind != ConsoleKind.Colour, "K0 no colour pull yet: " + H.LeverPullText());
            yield return AtConsole();
            yield return Shot("console-colour-K0-idle");

            Heading("K1 — a swatch press: the helmet shows BLUE for everyone, nobody is coloured, the prompt turns to 'is shown'");
            yield return AtConsole();
            yield return AimSwatch(9);
            yield return null;
            Check(H.PromptText().Contains("Press E to show BLUE"), "K1 the prompt offers the press: " + H.PromptText());
            refusal = Day.LastRefusal.Serial; int hostColour = identity.ColourIndex; takes = Day.LastColourTake.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.ShownSwatch == 9, 2f, () => $"K1 the server shows swatch {Day.ShownSwatch}");
            yield return Expect(() => console.Shown == 9 && Top().Contains("BLUE") && Top().Contains("SWATCH 9"), 2f, () => "K1 the top screen: " + Top());
            Check(console.Bottom != null && console.Bottom.Heading == "BLUE" && BottomStatus() == pullLine, "K1 the lower screen names it and still invites the pull: " + Bottom());
            Check(identity.ColourIndex == hostColour && Day.LastColourTake.Serial == takes, $"K1 a press is not a take: the host is still colour {identity.ColourIndex}, take {TakeText()}");
            Check(Day.LastRefusal.Serial == refusal, "K1 no refusal");
            yield return Expect(() => H.PromptText().Contains("BLUE is shown"), 1.5f, () => "K1 the prompt on the shown swatch: " + H.PromptText());
            Check(Count("[Console]") >= 1 && logs.Any(l => l.Contains("showed BLUE")), "K1 the server logged the press: " + string.Join(" / ", logs.Where(l => l.Contains("[Console]")).TakeLast(2)));
            yield return Shot("console-colour-K1-shown");

            Heading("K2 — the pull takes it: the host wears BLUE, the pick is saved, the lever swings, SKIPPER IS NOW BLUE for ~3 s");
            yield return AimLever();
            yield return null;
            Check(H.PromptText().Contains("Press E to take BLUE"), "K2 the lever's prompt: " + H.PromptText());
            Check(console.Sign.Aimed && Sign() == "TAKE/on", "K2 the sign lights as aimed: " + Sign());
            pulls = Day.LastLeverPull.Serial; takes = Day.LastColourTake.Serial; refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1, 2f, () => "K2 one pull counted: " + H.LeverPullText());
            Check(Day.LastLeverPull.Kind == ConsoleKind.Colour && Day.LastLeverPull.Action == LeverAction.TakeColour, "K2 the pull is Colour/TakeColour: " + H.LeverPullText());
            yield return Expect(() => identity.ColourIndex == 9, 2f, () => $"K2 the server wrote the host's colour ({identity.ColourIndex})");
            yield return Expect(() => Day.LastColourTake.Serial == takes + 1, 2f, () => "K2 the take announced: " + TakeText());
            Check(Day.LastColourTake.Taker == host.OwnerId && Day.LastColourTake.Swatch == 9, $"K2 the take names the host and BLUE: {TakeText()} (host {host.OwnerId})");
            yield return Expect(() => console.TakenShowing && BottomStatus() == string.Format(takenFormat, ME, "BLUE"), 0.8f, () => "K2 the lower screen: " + Bottom());
            yield return ExpectSwing(pulls + 1, "K2");
            yield return null;
            Check(Wears(host, 9), $"K2 the body wears BLUE ({host.BodyColour} vs {PlayerPalette.Get(9)})");
            Check(PlayerColourPrefs.Load() == 9, "K2 the pick is saved for next time: " + PlayerColourPrefs.Load());
            Check(Day.LastRefusal.Serial == refusal && Day.ShownSwatch == 9, "K2 no refusal; the shown swatch stays BLUE");
            yield return Shot("console-colour-K2-taken");
            // Behind the host (it faces the rig, so "behind" is further along the rig's front), the suit and the console in one frame.
            Say("capture " + H.CaptureFrom(host.transform.position + Front() * 2.2f + Vector3.up * 1.7f, host.transform.position + Vector3.up * 1.0f, Captures + "console-colour-K2-suit.png"));
            yield return Expect(() => !console.TakenShowing && BottomStatus() == pullLine, HQColourConsole.TakenSeconds + 1.5f, () => "K2 the IS NOW line gives way to the pull line: " + Bottom());
            Check(Day.ShownSwatch == 9 && console.Shown == 9, "K2 the helmet still BLUE after the line clears");

            Heading("K3 — the same colour again: the pull is accepted, the lever swings, nothing changes");
            yield return AimLever();
            yield return null;
            pulls = Day.LastLeverPull.Serial; takes = Day.LastColourTake.Serial; refusal = Day.LastRefusal.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1 && Day.LastColourTake.Serial == takes + 1, 2f, () => $"K3 accepted: pull {Day.LastLeverPull.Serial}, take {TakeText()}");
            yield return ExpectSwing(pulls + 1, "K3");
            Check(identity.ColourIndex == 9 && Day.LastRefusal.Serial == refusal && Day.ShownSwatch == 9, "K3 still BLUE, no refusal");
            yield return Wait(HQColourConsole.TakenSeconds + 0.5f);

            Heading("K4 — refused far away and hand-made: a press and a pull from across the platform, a swatch outside the palette, a wrong expectation, a console that does not exist");
            host.TeleportLocal(new Vector3(0f, 0.05f, 0f), 0f);
            yield return null; yield return null;
            pulls = Day.LastLeverPull.Serial; takes = Day.LastColourTake.Serial;
            refusal = Day.LastRefusal.Serial;
            ship.RequestPressSwatch(3);
            yield return ExpectRefusal(refusal, ConsoleRules.NotAtConsole, "K4 a swatch press from (0,0)");
            refusal = Day.LastRefusal.Serial;
            ship.RequestLever(ConsoleKind.Colour, LeverAction.TakeColour, SiteId.None);
            yield return ExpectRefusal(refusal, ConsoleRules.NotAtConsole, "K4 a pull from (0,0)");
            yield return AtConsole();
            refusal = Day.LastRefusal.Serial;
            ship.RequestPressSwatch(99);
            yield return ExpectRefusal(refusal, ConsoleRules.NoSuchColour, "K4 swatch 99");
            refusal = Day.LastRefusal.Serial;
            ship.RequestLever(ConsoleKind.Colour, LeverAction.Pay, SiteId.None);
            yield return ExpectRefusal(refusal, ConsoleRules.SelectionChanged, "K4 a pull expecting PAY");
            refusal = Day.LastRefusal.Serial; int exceptionsBefore = exceptionsSeen;
            ship.RequestLever((ConsoleKind)7, LeverAction.TakeColour, SiteId.None);
            yield return ExpectRefusal(refusal, "No such console", "K4 a pull on console kind 7");
            Check(exceptionsSeen == exceptionsBefore, "K4 no exception on the server for kind 7 (the kind check runs before the frame table): " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]")).TakeLast(2)));
            Check(Day.ShownSwatch == 9 && identity.ColourIndex == 9 && Day.LastLeverPull.Serial == pulls && Day.LastColourTake.Serial == takes, $"K4 nothing changed: shown {Day.ShownSwatch}, host {identity.ColourIndex}, pull {Day.LastLeverPull.Serial}, take {TakeText()}");

            Heading("K5 — two presses in one frame: the last wins, no refusal");
            yield return AtConsole();
            refusal = Day.LastRefusal.Serial;
            ship.RequestPressSwatch(1); ship.RequestPressSwatch(7);
            yield return Expect(() => Day.ShownSwatch == 7, 2f, () => $"K5 the last press wins: shown {Day.ShownSwatch}");
            yield return Hold(() => Day.LastRefusal.Serial == refusal, 0.8f, () => "K5 no refusal for either press: '" + Day.LastRefusal.Text + "'");
            yield return Expect(() => console.Shown == 7 && Top().Contains("CYAN"), 2f, () => "K5 the screen: " + Top());

            Heading("K6 — the dead press nothing: a dead host's press and pull are refused; revived after");
            // The server's own death state, as console-net N6 does it: nobody can die at HQ
            // (ServerCanKill: 'You can only die below'), so the debug death cannot reach it.
            Day.ServerPlayerDied(host.OwnerId); host.ServerSetDead(true);
            yield return Expect(() => host.IsDead && Day.IsDead(host.OwnerId), 5f, () => "K6 the host is dead");
            refusal = Day.LastRefusal.Serial;
            ship.RequestPressSwatch(4);
            yield return ExpectRefusal(refusal, "The dead press nothing", "K6 a dead press");
            refusal = Day.LastRefusal.Serial;
            ship.RequestLever(ConsoleKind.Colour, LeverAction.TakeColour, SiteId.None);
            yield return ExpectRefusal(refusal, "The dead press nothing", "K6 a dead pull");
            Check(Day.ShownSwatch == 7 && identity.ColourIndex == 9, $"K6 nothing changed: shown {Day.ShownSwatch}, host {identity.ColourIndex}");
            Day.ServerRevive(host.OwnerId); host.ServerSetDead(false);
            yield return Expect(() => !host.IsDead && !Day.IsDead(host.OwnerId), 5f, () => "K6 the host revived");
            yield return Wait(0.5f);

            Heading("K7 — the composers are pure: ComposeTop / ComposeBottom / ComposeSign from numbers alone");
            {
                var top = new TopModel(); var bottom = new BottomModel();
                HQColourConsole.ComposeTop(3, top, signs);
                string flat = ConsoleModels.Flatten(top);
                Check(flat == "CHOOSE YOUR CREW COLOUR ·  ·  · YELLOW · SWATCH 3 ·  · BLACK TIDE", "K7 ComposeTop(3): " + flat);
                var take = new ColourTake { Serial = 1, Taker = 0, Swatch = 3, Tick = 0 };
                HQColourConsole.ComposeBottom(3, in take, true, "dan", string.Empty, bottom, signs);
                Check(bottom.Heading == "YELLOW" && bottom.Status.Text == "DAN IS NOW YELLOW" && !bottom.Notice, "K7 ComposeBottom taken: " + ConsoleModels.Flatten(bottom));
                HQColourConsole.ComposeBottom(3, in take, false, "dan", string.Empty, bottom, signs);
                Check(bottom.Status.Text == pullLine && !bottom.Notice, "K7 ComposeBottom idle: " + ConsoleModels.Flatten(bottom));
                HQColourConsole.ComposeBottom(3, in take, false, "dan", ConsoleRules.NotAtConsole, bottom, signs);
                Check(bottom.Status.Text == ConsoleRules.NotAtConsole && bottom.Notice, "K7 ComposeBottom refusal: " + ConsoleModels.Flatten(bottom));
                SignModel sign = HQColourConsole.ComposeSign(false, signs);
                Check(ConsoleModels.Flatten(sign) == "TAKE/on" && !sign.Aimed, "K7 ComposeSign: " + ConsoleModels.Flatten(sign));
                Check(ConsoleRules.Expected(ConsoleKind.Colour, null, out SiteId t, out bool on, out string why) == LeverAction.TakeColour && on && t == SiteId.None && why == string.Empty, $"K7 the rule table: Colour → TakeColour/{on}/{t} '{why}'");
            }

            // ---- guests ---------------------------------------------------------------
            Heading("G1 — a late joiner sees the shown swatch (VIOLET) with no replayed swing");
            Day.ServerSetShownSwatchForChecks(11);
            yield return Expect(() => Day.ShownSwatch == 11 && console.Shown == 11, 2f, () => $"G1 the server shows VIOLET ({Day.ShownSwatch})");
            guestA = LaunchGuest(DirA);
            yield return Joined(guestA, "G1 A");
            yield return GuestEventually(guestA, r => GShown(r) == "11" && GTop(r).Contains("VIOLET") && GPlayed(r) == "0", 10f, "G1 A's first screens show VIOLET and its lever has not swung");
            Say("G1 A's console line: " + GuestConsoles(lastReply));

            Heading("G2 — a guest's press moves the host's helmet: A shows PINK, the host's screen and A's agree");
            yield return AtConsole();
            yield return GuestMove(guestA, StandSpot(1.0f));
            yield return GuestLook(guestA, ConsoleRig.SwatchNamePrefix + "13");
            Say("G2 A looks: " + lastReply.Split('\n')[0]);
            refusal = Day.LastRefusal.Serial;
            yield return GuestSwatch(guestA, 13);
            Check(lastReply.Contains("requested swatch 13"), "G2 A's press went: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.ShownSwatch == 13, 3f, () => $"G2 the server shows PINK ({Day.ShownSwatch})");
            yield return Expect(() => console.Shown == 13 && Top().Contains("PINK"), 2f, () => "G2 the host's screen: " + Top());
            yield return GuestEventually(guestA, r => GShown(r) == "13" && GShownName(r) == "PINK", 3f, "G2 A's screen agrees: PINK");
            Check(Day.LastRefusal.Serial == refusal, "G2 no refusal");

            Heading("G3 — a host pull: the host wears PINK; A sees the suit's colour, the IS NOW line, the swing and the take");
            yield return AtConsole();
            yield return AimLever();
            yield return null;
            pulls = Day.LastLeverPull.Serial; takes = Day.LastColourTake.Serial;
            yield return Press(Key.E);
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1 && identity.ColourIndex == 13, 3f, () => $"G3 the host took PINK (pull {Day.LastLeverPull.Serial}, colour {identity.ColourIndex})");
            int g3Pull = Day.LastLeverPull.Serial;
            yield return GuestEventually(guestA, r => GColour(r, host.OwnerId) == "13", 2f, "G3 A's copy of the host wears colour 13");
            yield return GuestEventually(guestA, r => GBottom(r).Contains(string.Format(takenFormat, ME, "PINK")), 2f, "G3 A's lower screen reads " + string.Format(takenFormat, ME, "PINK"));
            yield return GuestEventually(guestA, r => GPlayed(r) == g3Pull.ToString(), 2f, "G3 A's rig played pull " + g3Pull);
            yield return GuestEventually(guestA, r => GTake(r) == $"{takes + 1}/{host.OwnerId}/13", 2f, $"G3 A's take line {takes + 1}/{host.OwnerId}/13");
            Say("G3 A's lever angle in its last reply: " + Field(GuestConsoles(lastReply), "colourLeverAngle"));
            yield return Wait(HQColourConsole.TakenSeconds + 0.5f);

            Heading("G4 — a guest pull colours the guest only: A shows GREEN and pulls; A wears it, the host keeps PINK, both rigs swing");
            yield return GuestSwatch(guestA, 5);
            yield return Expect(() => Day.ShownSwatch == 5, 3f, () => $"G4 the server shows GREEN ({Day.ShownSwatch})");
            pulls = Day.LastLeverPull.Serial; takes = Day.LastColourTake.Serial; int played = console.LeverPlayedSerial;
            yield return GuestLever(guestA);
            Check(lastReply.Contains("requested lever Colour/TakeColour/None"), "G4 A's pull went expecting TAKE: " + lastReply.Split('\n')[0]);
            yield return Expect(() => ColourOf(guestA) == 5, 3f, () => $"G4 A's colour on the host: {ColourOf(guestA)}");
            yield return Expect(() => Day.LastLeverPull.Serial == pulls + 1 && Day.LastColourTake.Serial == takes + 1, 2f, () => $"G4 one pull, one take: {H.LeverPullText()} / {TakeText()}");
            Check(Day.LastColourTake.Taker == guestA.Id && Day.LastColourTake.Swatch == 5, $"G4 the take names A and GREEN: {TakeText()} (A {guestA.Id})");
            Check(identity.ColourIndex == 13, $"G4 the host keeps PINK ({identity.ColourIndex})");
            Check(Wears(CopyOf(guestA), 5), $"G4 the host's copy of A wears GREEN ({CopyOf(guestA)?.BodyColour})");
            yield return Expect(() => BottomStatus() == string.Format(takenFormat, guestA.Name.ToUpperInvariant(), "GREEN"), 1.5f, () => "G4 the host's lower screen: " + Bottom());
            yield return ExpectSwing(pulls + 1, "G4");
            Check(console.LeverPlayedSerial > played, $"G4 the host's rig swung for A's pull ({played} → {console.LeverPlayedSerial})");
            yield return GuestEventually(guestA, r => GColour(r, guestA.Id) == "5" && GColour(r, host.OwnerId) == "13", 3f, "G4 A's own line colour=5, the host's colour=13");
            yield return GuestEventually(guestA, r => GPlayed(r) == (pulls + 1).ToString(), 2f, "G4 A's rig played its own pull");
            yield return Wait(HQColourConsole.TakenSeconds + 0.5f);

            Heading("G5 — same-frame presses from two peers: A asks AMBER, the host asks INDIGO in the frame A's command is in flight; one wins, every screen agrees, no refusal");
            yield return AtConsole();
            refusal = Day.LastRefusal.Serial;
            Command(guestA, "{\"id\":{id},\"action\":\"swatch\",\"slot\":2}");
            int aCmd = guestCommand;
            ship.RequestPressSwatch(10);
            yield return AwaitReply(guestA, aCmd);
            Check(lastReply.Contains("requested swatch 2"), "G5 A's press went: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.ShownSwatch == 2 || Day.ShownSwatch == 10, 2f, () => $"G5 one of the two is shown: {Day.ShownSwatch}");
            yield return Wait(0.5f);
            int g5Shown = Day.ShownSwatch;
            Say($"G5 the winner: swatch {g5Shown} ({Name(g5Shown)})");
            Check(console.Shown == g5Shown, $"G5 the host's screen shows {console.Shown}");
            yield return GuestEventually(guestA, r => GShown(r) == g5Shown.ToString(), 2f, "G5 A's screen shows the same " + g5Shown);
            Check(Day.LastRefusal.Serial == refusal, "G5 no refusal: '" + Day.LastRefusal.Text + "'");

            Heading("G6 — same-frame pulls from two peers: A pulls and the host pulls in the frame A's command is in flight; one swing a frame, every accepted puller wears the shown colour");
            yield return AtConsole();
            yield return AimLever();
            yield return null;
            pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial; takes = Day.LastColourTake.Serial;
            int hostBefore = identity.ColourIndex, aBefore = ColourOf(guestA);
            int shown6 = Day.ShownSwatch;
            Command(guestA, "{\"id\":{id},\"action\":\"lever\",\"item\":\"Colour\"}");
            aCmd = guestCommand;
            ship.RequestLever(ConsoleKind.Colour, LeverAction.TakeColour, SiteId.None); // the host's E in the same editor frame (H.ClientPullLever's call)
            yield return AwaitReply(guestA, aCmd);
            Check(lastReply.Contains("requested lever Colour/TakeColour/None"), "G6 A's pull went: " + lastReply.Split('\n')[0]);
            yield return Expect(() => Day.LastLeverPull.Serial > pulls, 3f, () => "G6 at least one pull accepted: " + H.LeverPullText());
            yield return Wait(1.0f);
            int serialDelta = Day.LastLeverPull.Serial - pulls, refusalDelta = Day.LastRefusal.Serial - refusal;
            string g6Case = serialDelta == 2 ? "two frames, both accepted" : refusalDelta == 1 ? "same frame (Lever in use)" : "one pull only";
            Say($"G6 serial delta={serialDelta}, refusal delta={refusalDelta} '{(refusalDelta > 0 ? Day.LastRefusal.Text : string.Empty)}' → {g6Case}");
            Check(serialDelta == 1 || serialDelta == 2, $"G6 the pull serial grew by {serialDelta}");
            Check(refusalDelta <= 1 && (refusalDelta == 0 || Day.LastRefusal.Text == ConsoleRules.LeverInUse), $"G6 at most one refusal and it reads 'Lever in use': {refusalDelta} '{Day.LastRefusal.Text}'");
            Check(serialDelta + refusalDelta == 2, $"G6 every pull was either accepted or refused ({serialDelta} + {refusalDelta})");
            Check(Day.LastColourTake.Serial - takes == serialDelta, $"G6 one take per accepted pull ({Day.LastColourTake.Serial - takes})");
            bool hostTook = identity.ColourIndex == shown6, aTook = ColourOf(guestA) == shown6;
            Say($"G6 host {hostBefore} → {identity.ColourIndex} ({(hostTook ? "took" : "kept")}), A {aBefore} → {ColourOf(guestA)} ({(aTook ? "took" : "kept")})");
            Check((hostTook ? 1 : 0) + (aTook ? 1 : 0) == serialDelta || (hostBefore == shown6 || aBefore == shown6), $"G6 the accepted pullers wear {Name(shown6)}");
            Check(Day.ShownSwatch == shown6 && console.Shown == shown6, "G6 the shown swatch did not move");
            yield return GuestEventually(guestA, r => GShown(r) == shown6.ToString() && GPlayed(r) == Day.LastLeverPull.Serial.ToString(), 3f, "G6 A's screen agrees and its rig played the last pull");
            yield return Wait(HQColourConsole.TakenSeconds + 0.5f);

            Heading("G7 — the dead guest: A dies; its press and pull are refused; revived after");
            // The server's own death state (console-net N6): the debug death is refused at HQ
            // ("You can only die below"), so the guest's die command cannot reach it.
            Day.ServerPlayerDied(guestA.Id); CopyOf(guestA)?.ServerSetDead(true);
            yield return Expect(() => Day.IsDead(guestA.Id), 5f, () => "G7 A is dead on the server");
            int aColour = ColourOf(guestA); int shown7 = Day.ShownSwatch;
            refusal = Day.LastRefusal.Serial;
            yield return GuestSwatch(guestA, 6);
            yield return ExpectRefusal(refusal, "The dead press nothing", "G7 A's dead press");
            refusal = Day.LastRefusal.Serial;
            yield return GuestLever(guestA);
            yield return ExpectRefusal(refusal, "The dead press nothing", "G7 A's dead pull");
            Check(Day.ShownSwatch == shown7 && ColourOf(guestA) == aColour, $"G7 nothing changed: shown {Day.ShownSwatch}, A {ColourOf(guestA)}");
            Day.ServerRevive(guestA.Id); CopyOf(guestA)?.ServerSetDead(false);
            yield return Expect(() => !Day.IsDead(guestA.Id), 5f, () => "G7 A revived");
            yield return GuestEventually(guestA, r => GuestPlayerLine(r, guestA.Id).Contains("dead=False"), 8f, "G7 A reads itself alive");

            Heading("G8 — a guest far away: A at (0,0) presses and pulls; both refused 'Step up to the console'");
            yield return GuestMove(guestA, new Vector3(0f, 0.05f, 0f));
            yield return Wait(0.3f);
            shown7 = Day.ShownSwatch; aColour = ColourOf(guestA); pulls = Day.LastLeverPull.Serial;
            refusal = Day.LastRefusal.Serial;
            yield return GuestSwatch(guestA, 1);
            yield return ExpectRefusal(refusal, ConsoleRules.NotAtConsole, "G8 A's far press");
            refusal = Day.LastRefusal.Serial;
            yield return GuestLever(guestA);
            yield return ExpectRefusal(refusal, ConsoleRules.NotAtConsole, "G8 A's far pull");
            Check(Day.ShownSwatch == shown7 && ColourOf(guestA) == aColour && Day.LastLeverPull.Serial == pulls, $"G8 nothing changed: shown {Day.ShownSwatch}, A {ColourOf(guestA)}, pull {Day.LastLeverPull.Serial}");

            Heading("G9 — a second late joiner (headless B) sees the shown swatch and no replayed swing; both guests leave");
            guestB = LaunchGuest(DirB, headless: true);
            yield return Joined(guestB, "G9 B");
            int shown9 = Day.ShownSwatch;
            yield return GuestEventually(guestB, r => GShown(r) == shown9.ToString() && GPlayed(r) == "0" && GTop(r).Contains(Name(shown9)), 10f, $"G9 B's first screens show {Name(shown9)} ({shown9}) and its lever has not swung");
            Say("G9 B's console line: " + GuestConsoles(lastReply));
            yield return Send(guestB, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Send(guestA, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => CopyOf(guestA) == null && CopyOf(guestB) == null, 15f, () => "G9 both guests left");

            Heading("G10 — the side view for the sharpness judgement (the tester Reads the capture)");
            yield return AtConsole();
            {
                Vector3 eye = rig.transform.position + Front() * 2.2f + RightOf() * 1.6f + Vector3.up * 1.5f;
                string saved = H.CaptureFrom(eye, rig.transform.position + Vector3.up * 1.4f, Captures + "console-colour-G10-side.png");
                Say("capture " + saved);
                Check(saved.StartsWith("saved") && File.Exists(Captures + "console-colour-G10-side.png"), "G10 the side capture exists: " + saved);
            }
            Say($"host log: {errorsSeen} errors, {exceptionsSeen} exceptions during the run");
            Check(exceptionsSeen == 0, "no exception in the host's log during the run: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]")).Take(3)));
            Say("end: " + State().Replace("\n", " | "));
        }
    }
}
