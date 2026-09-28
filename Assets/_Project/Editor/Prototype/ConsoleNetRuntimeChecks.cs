using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The shared console over the network (Dan, 27 September 2026; scratchpad
    // console/BRIEF.md "TESTING REQUIREMENTS — NETWORK", TESTPLAN.md §3.3). The
    // functional words are the console-ship and console-hq jobs' business; this job
    // checks that every peer shows the SAME console, at once, and that the server
    // alone decides, with 2, 3 and 4 players:
    //   2 players (host + windowed guest A): the selection, the lock, the sign, the
    //   screens, the balance, an unlock and the lever's swing on both peers within a
    //   second of each change; same-frame selections and unlocks; a dead A spectating
    //   the host at the console (its screens are the host's) who can press nothing; A
    //   off the ship and hand-made requests refused; the vote sent as the ship casts
    //   off and a stale pull and a selection sent during the trip's Preparing stage;
    //   the screens during the trip and after it; a same-frame END DAY.
    //   3 players (+ headless B): B joins at sea after selections, unlocks and pulls
    //   (the state at once, no swing replayed); three same-frame selections; three
    //   same-frame CONFIRMs (one trip) with B's process killed while the ship pulls
    //   away; a late joiner at HQ after a selection, an unlock and a vote; three
    //   same-frame PAYs (one sale); an UNLOCK in the frame a lost payday starts the
    //   plank, presses refused on the plank, the fresh run relocked everywhere.
    //   4 players (+ headless C): four identical peers; a fifth guest refused; a
    //   4-player vote with a take-back and a leave mid-vote; four same-frame GIVE UPs.
    // "Identical" compares, per guest snapshot and the host's own state: the
    // selection, the unlock mask, HERE/destination, the lever pull serial, the
    // balance, the vote, the day and phase, both signs, both consoles' flattened
    // screens (minus the viewer-local aim marks and "voted=" flag) and both compat
    // status lines. A peer that still differs more than a second after the change
    // fails the row. Log: Temp/console-net-matrix.log.
    // Started by CameraClearanceMatrixDriver.Start("console-net").
    public static class ConsoleNetRuntimeChecks
    {
        private const string Log = "Temp/console-net-matrix.log";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const string Captures = "Temp/look/";
        private const float SyncLimit = 1.0f; // BRIEF: identical on every peer within a second of every change

        private sealed class Guest
        {
            public string Label; public string Dir; public Process Process; public int Id = -1; public string Name = string.Empty; public bool Headless;
            public override string ToString() => Label + "#" + Id;
        }

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static readonly List<Guest> crew = new();      // guests connected now
        private static readonly List<Guest> launched = new();  // every process started (killed at the end, logs swept)
        private static int guestCommand = 5100;
        private static string lastReply = string.Empty;
        private static readonly List<string> logs = new();
        private static readonly List<string> syncs = new();
        private static int exceptionsSeen, errorsSeen;
        private static float worstSync;
        // Findings that should not stop the run (a late peer, a log hit): logged, then failed at the end.
        private static readonly List<string> softFails = new();
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run console-net matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Console-net matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Console-net matrix started " + DateTime.Now + "\n");
            Status = "Running";
            logs.Clear(); syncs.Clear(); crew.Clear(); launched.Clear(); softFails.Clear();
            exceptionsSeen = 0; errorsSeen = 0; worstSync = 0f;
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
            if (type != LogType.Log || message.Contains("[GiveUp]") || message.Contains("[Plank]") || message.Contains("[Console]") || message.Contains("Pay: sold"))
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
            try { File.AppendAllText(Log, "  sync " + string.Join(" / ", syncs) + "\n"); } catch (Exception) { }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Console-net matrix: MATRIX_PASS"); else Debug.LogError("Console-net matrix: " + Status);
            foreach (Guest g in launched)
                try { if (g?.Process != null && !g.Process.HasExited) g.Process.Kill(); } catch (Exception) { }
            crew.Clear(); launched.Clear();
            steps = null;
            stack.Clear();
            Application.logMessageReceived -= OnLog;
            WorldLoopSettings.QuotaOverrideForTests = null;
            EditorApplication.update -= Tick;
        }

        // ---- harness ------------------------------------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static string State() => H.FlowStatus() + "\n" + H.ConsoleStatus() + "\nship: " + H.ShipScreenText() + "\nhq: " + H.HQScreenText() + "\nlever: " + H.LeverPullText() + "\n" + H.GiveUpStatus() + "\ncrew: " + string.Join(", ", crew);
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + State());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static void Soft(bool value, string label)
        {
            if (value) { File.AppendAllText(Log, "PASS " + label + "\n"); return; }
            softFails.Add(label.Split('\n')[0]);
            File.AppendAllText(Log, "SOFT-FAIL " + label + "\n    " + State().Replace("\n", "\n    ") + "\n");
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
        private static int Count(string contains) => logs.Count(l => l.Contains(contains));
        private static int IndexOf(string contains) => logs.FindIndex(l => l.Contains(contains));
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", CultureInfo.InvariantCulture) + ",\"y\":" + v.y.ToString("0.###", CultureInfo.InvariantCulture) + ",\"z\":" + v.z.ToString("0.###", CultureInfo.InvariantCulture) + "}";

        // ---- guests ------------------------------------------------------------------------

        private static Guest Launch(string label, string dir, bool headless)
        {
            Directory.CreateDirectory(dir);
            foreach (string stale in new[] { "command.json", "reply.txt", "player.log" })
                if (File.Exists(Path.Combine(dir, stale))) File.Delete(Path.Combine(dir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            string video = headless ? "-batchmode -nographics" : "-screen-width 960 -screen-height 540 -screen-fullscreen 0";
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                video + " -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(dir) + "\" -logFile \"" + Path.GetFullPath(dir + "/player.log") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            var g = new Guest { Label = label, Dir = dir, Process = Process.Start(info), Headless = headless };
            launched.Add(g);
            Say($"launched guest {label} ({(headless ? "headless" : "windowed")}) in {dir}");
            return g;
        }
        // Waits for the guest's player to spawn on the host (a copy no connected guest owns) and its peer to answer in `world`.
        private static IEnumerator Joined(Guest g, string label, string world, string phase)
        {
            HashSet<int> known = new(crew.Where(x => x != g && x.Id >= 0).Select(x => x.Id));
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
            yield return GuestEventually(g, r => PlayerLine(r, g.Id).Contains("local=True") && Header(r).Contains("world=" + world + ";") && Header(r).Contains("phase=" + phase + ";") && ConsoleL(r).Contains("topScreen=NAVIGATION"), 30f, label + ": the guest's peer answers in " + world);
            g.Name = WorldSceneFlow.DisplayName(g.Id);
            crew.Add(g);
            Say($"{label}: guest '{g.Name}' is client {g.Id} ({g.Dir}); {SpawnedPlayers()} players; working set {WorkingSetMb(g)} MB");
        }
        private static long WorkingSetMb(Guest g) { try { g.Process.Refresh(); return g.Process.WorkingSet64 / (1024 * 1024); } catch (Exception) { return -1; } }
        private static HQPlayerController CopyOf(Guest g) => g == null || g.Id < 0 ? null : UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner && p.OwnerId == g.Id);
        private static int SpawnedPlayers() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).Count(p => p.IsSpawned);

        // The guest polls its file every frame; a write can collide with its read.
        private static int Command(Guest g, string json)
        {
            int id = ++guestCommand;
            string text = json.Replace("{id}", id.ToString());
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(Path.Combine(g.Dir, "command.json"), text); return id; }
                catch (IOException) when (attempt < 20) { System.Threading.Thread.Sleep(15); }
            }
        }
        private static string Reply(Guest g)
        {
            try { string p = Path.Combine(g.Dir, "reply.txt"); return File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
            catch (IOException) { return string.Empty; }
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
            throw new Exception("guest " + g + " did not answer command " + id + "\n" + Reply(g) + "\n" + State());
        }
        private static IEnumerator Send(Guest g, string json) { int id = Command(g, json); yield return AwaitReply(g, id); }
        private static IEnumerator GuestEventually(Guest g, Func<string, bool> predicate, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send(g, "{\"id\":{id},\"action\":\"snapshot\"}");
                if (predicate(lastReply)) { Check(true, label); yield break; }
                yield return Wait(0.3f);
            }
            throw new Exception(label + "\n" + g + " " + Header(lastReply) + "\n" + Line(lastReply, "giveup=") + "\n" + ConsoleL(lastReply) + "\n" + State());
        }
        // One snapshot from each guest, requested in the same frame.
        private static IEnumerator Snap(IList<Guest> gs, Dictionary<Guest, string> into)
        {
            var ids = new Dictionary<Guest, int>();
            foreach (Guest g in gs) ids[g] = Command(g, "{\"id\":{id},\"action\":\"snapshot\"}");
            var pending = new List<Guest>(gs);
            float deadline = Time.unscaledTime + 10f;
            while (pending.Count > 0 && Time.unscaledTime < deadline)
            {
                foreach (Guest g in pending.ToList())
                {
                    string r = Reply(g);
                    if (r.StartsWith("id=" + ids[g] + ";")) { into[g] = r; pending.Remove(g); }
                }
                if (pending.Count > 0) yield return null;
            }
            if (pending.Count > 0) throw new Exception("no snapshot from " + string.Join(", ", pending) + "\n" + State());
        }
        private static IEnumerator GuestMove(Guest g, Vector3 to) { yield return Send(g, "{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(to) + "}"); }
        private static string SelectJson(string site) => "{\"id\":{id},\"action\":\"select\",\"item\":\"" + site + "\"}";
        private static string LeverJson(string kind) => "{\"id\":{id},\"action\":\"lever\",\"item\":\"" + kind + "\"}";
        private static string ExpectJson(string kind, LeverAction action, SiteId site) => "{\"id\":{id},\"action\":\"lever_expect\",\"item\":\"" + kind + "\",\"slot\":" + (int)action + ",\"position\":{\"x\":" + (int)site + ",\"y\":0,\"z\":0}}";
        private const string GiveUpJson = "{\"id\":{id},\"action\":\"giveup\"}";

        private static string Line(string reply, string prefix) => reply.Split('\n').FirstOrDefault(l => l.StartsWith(prefix)) ?? string.Empty;
        private static string Header(string reply) => Line(reply, "server=");
        private static string ConsoleL(string reply) => Line(reply, "selected=");
        private static string PlayerLine(string reply, int ownerId) => Line(reply, "player=" + ownerId + ";");
        // "key=value; " on the console line (the values carry " · " and " | " but never "; ").
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
        private static string Rx(string text, string pattern) { Match m = Regex.Match(text, pattern); return m.Success ? m.Groups[1].Value : "(absent)"; }
        private static float Num(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN;
        private static string G(string reply, string key) => Field(ConsoleL(reply), key);

        // ---- the consoles, on the host ---------------------------------------------------

        private static IConsoleComposer ShipC() => Day == null ? null : ShipNavigationConsole.InWorld(Day.World);
        private static IConsoleComposer HQC() => HQQuotaConsole.InHQ();
        private static string ShipSign() => ShipC() == null ? "none" : ConsoleModels.Flatten(ShipC().Sign);
        private static string ShipTop() => ShipC() == null ? "none" : ConsoleModels.Flatten(ShipC().Top);
        private static string ShipBottom() => ShipC() == null ? "none" : ConsoleModels.Flatten(ShipC().Bottom);
        private static string HQSign() => HQC() == null ? "none" : ConsoleModels.Flatten(HQC().Sign);
        private static string HQTop() => HQC() == null ? "none" : ConsoleModels.Flatten(HQC().Top);
        // Viewer-local presentation out: the aim marks (~) and the voter's own flag.
        private static string Norm(string s) => (s ?? string.Empty).Replace("~", string.Empty).Replace(" voted=True", string.Empty).Replace(" voted=False", string.Empty);
        private static bool Here(string top, string card) { int i = top.IndexOf("[" + card, StringComparison.Ordinal); int e = i < 0 ? -1 : top.IndexOf(']', i); return e > i && top.Substring(i, e - i).Contains('@'); }
        private static int HereCount(string top) => top.Count(c => c == '@');

        private static readonly string[] HQKeys = { "hqSign", "hqTop", "hqBottom", "quotaBoard" };

        private static Dictionary<string, string> HostTuple()
        {
            IConsoleComposer ship = ShipC(), hq = HQC();
            LeverPull p = Day.LastLeverPull;
            return new Dictionary<string, string>
            {
                ["selected"] = Day.SelectedSite.ToString(),
                ["unlocked"] = Destinations.MaskText(Day.UnlockedSites),
                ["site"] = Day.CurrentSite.ToString(),
                ["toSite"] = Day.SiteDestination.ToString(),
                ["leverPulls"] = $"{p.Serial}/{p.Kind}/{p.Action}",
                ["balance"] = Day.Balance.ToString(),
                ["giveup"] = $"{Day.GiveUpVotes}/{Day.GiveUpCrew}",
                ["phase"] = Day.Phase.ToString(),
                ["day"] = Day.Day.ToString(),
                ["leverSign"] = ship == null ? "none" : ConsoleModels.Flatten(ship.Sign),
                ["topScreen"] = ship == null ? "none" : Norm(ConsoleModels.Flatten(ship.Top)),
                ["bottomScreen"] = ship == null ? "none" : Norm(ConsoleModels.Flatten(ship.Bottom)),
                ["monitor"] = ship == null ? string.Empty : ship.Text,
                ["hqSign"] = hq == null ? "none" : ConsoleModels.Flatten(hq.Sign),
                ["hqTop"] = hq == null ? "none" : Norm(ConsoleModels.Flatten(hq.Top)),
                ["hqBottom"] = hq == null ? "none" : Norm(ConsoleModels.Flatten(hq.Bottom)),
                ["quotaBoard"] = hq == null ? "none" : hq.Text.Replace("\n", " | "),
            };
        }
        private static Dictionary<string, string> GuestTuple(string r)
        {
            string h = Header(r), c = ConsoleL(r), v = Line(r, "giveup=");
            int q = v.IndexOf("quotaBoard=", StringComparison.Ordinal), qe = v.LastIndexOf(';');
            int m = h.IndexOf("; monitor=", StringComparison.Ordinal), me = h.IndexOf("; trip=", StringComparison.Ordinal);
            return new Dictionary<string, string>
            {
                ["selected"] = Field(c, "selected"),
                ["unlocked"] = Field(c, "unlocked"),
                ["site"] = Field(c, "site"),
                ["toSite"] = Field(c, "toSite"),
                ["leverPulls"] = Field(c, "leverPulls"),
                ["balance"] = Rx(h, @"; balance=(-?\d+);"),
                ["giveup"] = Rx(v, @"^giveup=([^;]*);"),
                ["phase"] = Rx(h, @"; phase=([^;]*);"),
                ["day"] = Rx(h, @"; day=(-?\d+);"),
                ["leverSign"] = Field(c, "leverSign"),
                ["topScreen"] = Norm(Field(c, "topScreen")),
                ["bottomScreen"] = Norm(Field(c, "bottomScreen")),
                ["monitor"] = m < 0 || me < m ? "(absent)" : h.Substring(m + "; monitor=".Length, me - m - "; monitor=".Length),
                ["hqSign"] = Field(c, "hqSign"),
                ["hqTop"] = Norm(Field(c, "hqTop")),
                ["hqBottom"] = Norm(Field(c, "hqBottom")),
                ["quotaBoard"] = q < 0 || qe < q ? "(absent)" : v.Substring(q + "quotaBoard=".Length, qe - q - "quotaBoard=".Length),
            };
        }
        // "" when equal; else the differing keys with both values. The HQ console is not in the
        // crew's world at sea (a guest unloads HQ; the host keeps it), so it is compared at HQ only.
        private static string Diff(Dictionary<string, string> host, Dictionary<string, string> guest, ICollection<string> skip)
        {
            var parts = new List<string>();
            bool atHQ = Day.World == WorldId.HQ;
            foreach (var kv in host)
            {
                if (skip != null && skip.Contains(kv.Key)) continue;
                if (!atHQ && HQKeys.Contains(kv.Key)) continue;
                string g = guest.TryGetValue(kv.Key, out string gv) ? gv : "(absent)";
                if (g != kv.Value) parts.Add($"{kv.Key}: host '{kv.Value}' guest '{g}'");
            }
            return string.Join("; ", parts);
        }

        // Every connected guest's snapshot equals the host's state. `since` is the moment of the
        // change; a guest whose snapshot still differed later than SyncLimit after it fails.
        private static IEnumerator Converge(string label, float since = -1f, ICollection<string> skip = null, float patience = 6f)
        {
            float t0 = since >= 0f ? since : Time.unscaledTime;
            var pending = new List<Guest>(crew);
            var lastMiss = crew.ToDictionary(g => g, g => -1f);
            var matchedAt = new Dictionary<Guest, float>();
            var diffs = new Dictionary<Guest, string>();
            var replies = new Dictionary<Guest, string>();
            while (pending.Count > 0 && Time.unscaledTime < t0 + patience)
            {
                replies.Clear();
                yield return Snap(pending, replies);
                Dictionary<string, string> host = HostTuple();
                float now = Time.unscaledTime - t0;
                foreach (Guest g in pending.ToList())
                {
                    string d = Diff(host, GuestTuple(replies[g]), skip);
                    if (d.Length == 0) { matchedAt[g] = now; pending.Remove(g); }
                    else { lastMiss[g] = now; diffs[g] = d; }
                }
            }
            Check(pending.Count == 0, $"{label}: every peer converged ({string.Join(" / ", pending.Select(g => g + " " + (diffs.TryGetValue(g, out string d) ? d : "")))})");
            float worst = crew.Select(g => lastMiss[g]).DefaultIfEmpty(-1f).Max();
            float upper = crew.Select(g => matchedAt[g]).DefaultIfEmpty(0f).Max();
            worstSync = Mathf.Max(worstSync, worst);
            syncs.Add($"{label}: ≤{upper:0.00}s");
            string detail = string.Join(", ", crew.Select(g => $"{g.Label} {(lastMiss[g] < 0f ? "first look" : $"differed at {lastMiss[g]:0.00}s ({diffs[g]})")}, equal at {matchedAt[g]:0.00}s"));
            Soft(worst <= SyncLimit, $"{label}: host + {crew.Count} guests identical within {SyncLimit:0.0} s ({detail})");
        }

        // The accepted pull `serial` swung this console's lever on the host and on every guest.
        private static IEnumerator ExpectPlayed(int serial, ConsoleKind kind, string label)
        {
            IConsoleComposer mine = kind == ConsoleKind.Ship ? ShipC() : HQC();
            yield return Expect(() => mine != null && mine.LeverPlayedSerial == serial, 1.5f, () => $"{label}: the host's {kind} rig played pull {serial} (played {(mine == null ? -1 : mine.LeverPlayedSerial)})");
            string key = kind == ConsoleKind.Ship ? "leverPlayed" : "hqLeverPlayed";
            foreach (Guest g in crew.ToList())
                yield return GuestEventually(g, r => G(r, key) == serial.ToString(), 4f, $"{label}: {g.Label}'s {kind} rig played pull {serial}");
        }

        private static IEnumerator ExpectRefused(int serialBefore, int howMany, Func<string, bool> text, string label)
        {
            yield return Expect(() => Day.LastRefusal.Serial >= serialBefore + howMany, 4f, () => $"{label}: {howMany} refusal(s) (serial {Day.LastRefusal.Serial - serialBefore}, last '{Day.LastRefusal.Text}')");
            Check(Day.LastRefusal.Serial == serialBefore + howMany && text(Day.LastRefusal.Text), $"{label}: exactly {howMany} refusal(s), the last '{Day.LastRefusal.Text}'");
        }

        private static Vector3 Deck(ShipParts ship, int i)
        {
            Transform t = ship.SpawnPoint(i);
            return (t != null ? t.position : ship.FromShipLocal(new Vector3(2.5f * (i - 1.5f), 0f, 4f))) + Vector3.up * 0.1f;
        }
        private static IEnumerator Aboard(Guest g, ShipParts ship, int spot, string label)
        {
            yield return GuestMove(g, Deck(ship, spot));
            yield return Expect(() => CopyOf(g) != null && ship.IsSafelyAboard(CopyOf(g).transform.position), 5f, () => $"{label}: {g.Label} on the deck");
        }
        private static IEnumerator AtShipConsole()
        {
            string moved = H.ClientMoveLocalPlayerToShipConsole();
            Check(moved.StartsWith("moved"), "the host at the ship console: " + moved);
            yield return null; yield return null;
        }
        private static IEnumerator AtBoard()
        {
            string moved = H.ClientMoveLocalPlayerToBoard();
            Check(moved.StartsWith("moved"), "the host at the quota console: " + moved);
            yield return null; yield return null;
        }
        private static Vector3 StandSpot(float side)
        {
            Transform rig = ((MonoBehaviour)HQC()).transform;
            Vector3 front = rig.rotation * Vector3.forward; front.y = 0f; front.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, front);
            Vector3 at = rig.position + front * 1.6f + right * side;
            at.y = 0.05f;
            return at;
        }
        private static IEnumerator Arrive(WorldId id, string label, float seconds = 90f)
        {
            yield return Expect(() => Day.World == id && !Flow.Transitioning && Day.Phase != DayPhase.Sailing && Day.Phase != DayPhase.SailingHome && !Host().TravelLocked, seconds, () => label + ": arrived at " + id + " (phase " + Day.Phase + ")");
            yield return Wait(0.8f);
        }
        private static IEnumerator DiveDoneNow(string label)
        {
            string why = Day.Phase == DayPhase.AtSea ? string.Empty : "not at sea (" + Day.Phase + ")";
            Check(Day.Phase == DayPhase.AtSea && Day.ServerBeginDay(out why), label + ": the day began (" + why + ")");
            yield return null;
            Check(Day.ServerEndDayIfDone(Flow.Settings.DaysPerCycle) && Day.DiveDone && Day.Phase == DayPhase.AtSea, label + ": the dive is done");
            yield return null;
        }
        // A coin (CoinMedium) spawned server side inside the docked ship's storage room.
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
            Check(item.IsSpawned && item.Value > 0 && ship.IsInStorageRoom(item.transform.position), $"{name}: a ${item.Value} coin lies in the docked ship's storage room");
            yield return Expect(() => Day.BoxValue >= item.Value, 2f, () => $"{name}: the box reads ${Day.BoxValue}");
            got(item);
        }
        // Everyone off the plank quickly: whoever is on the board steps off its end.
        private static IEnumerator RideOutPlank(string label)
        {
            HQPlank plank = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
            Check(plank != null, label + ": the plank");
            float deadline = Time.unscaledTime + 6f * (Flow.Settings.PlankTurnSeconds + 6f) + Flow.Settings.RunOverCardSeconds + 20f;
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
                        Guest g = crew.FirstOrDefault(x => x.Id == jumper && x.Process != null && !x.Process.HasExited);
                        if (g != null) yield return GuestMove(g, off);
                    }
                    movedFor = jumper;
                    Say($"{label}: {WorldSceneFlow.DisplayName(jumper)} steps off the plank");
                }
                yield return null;
            }
            Check(Day.Phase == DayPhase.AtHQ, label + ": the fresh run after the plank (phase " + Day.Phase + ")");
            Check(Day.Day == 0 && Day.Balance == 0 && Day.GiveUpVotes == 0 && Day.UnlockedSites == 0 && Day.SelectedSite == SiteId.None, label + $": day 0, $0, no votes, every site locked, nothing selected ({H.ConsoleStatus()})");
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, 8f, () => label + ": the screen clear again");
            yield return Wait(0.5f);
        }
        private static IEnumerator Leave(Guest g, string label)
        {
            yield return Send(g, "{\"id\":{id},\"action\":\"leave\"}");
            yield return Expect(() => CopyOf(g) == null, 15f, () => label + ": " + g.Label + " left");
            crew.Remove(g);
            try { if (!g.Process.HasExited) g.Process.Kill(); } catch (Exception) { }
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            WorldSceneFlow flow = Flow;
            SiteCatalog sites = SiteCatalog.Resolve();
            string me = WorldSceneFlow.DisplayName(host.OwnerId);
            int days = flow.Settings.DaysPerCycle, quota = flow.Settings.QuotaPerCycle;
            float refusalSeconds = flow.Settings.RefusalDisplaySeconds;
            int p2 = sites.UnlockPrice(SiteId.Site02), p3 = sites.UnlockPrice(SiteId.Site03), p4 = sites.UnlockPrice(SiteId.Site04);
            Say($"host '{me}' (client {host.OwnerId}), {days} days, quota ${quota}, prices ${p2}/${p3}/${p4}, refusals {refusalSeconds} s; sync limit {SyncLimit} s");
            Check(Day.Phase == DayPhase.AtHQ && Day.Day == 0 && Day.SelectedSite == SiteId.None && Day.UnlockedSites == 0 && Day.LastLeverPull.Serial == 0 && WorldLoopSettings.QuotaOverrideForTests == null, "fresh run: " + H.ConsoleStatus());
            ShipParts hqShip = ShipParts.InWorld(WorldId.HQ);
            Check(hqShip != null && ShipC() != null && HQC() != null, "both consoles stand at HQ (the docked ship's and the quota console)");
            int refusal, pulls, trips, balance;
            float t;

            // ================================ 2 players ================================
            Heading("N0 — 2 players: guest A (windowed) joins at HQ; its consoles read what the host's read");
            Guest A = Launch("A", "Temp/console-net-guest-a", headless: false);
            yield return Joined(A, "N0 A", "HQ", "AtHQ");
            yield return AtShipConsole();
            yield return Aboard(A, hqShip, 1, "N0");
            yield return Converge("N0 A joined");

            Heading("N1 — the host selects SITE 02 (locked): the selection, the lock, the dim UNLOCK sign and both screens on A within a second");
            Say(H.ClientSelect("Site02"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site02, 3f, () => "N1 selected Site02");
            t = Time.unscaledTime;
            yield return Expect(() => ShipSign() == $"UNLOCK ${p2}/off" && ShipTop().Contains("[SITE 02*#]"), 2f, () => "N1 the host's sign and card: " + ShipSign() + " | " + ShipTop());
            yield return Converge("N1 SITE 02 selected", t);

            Heading("N2 — A changes the selection (SITE 01), the host follows; the host picks HQ, A follows; no flip-flop");
            int cmd = Command(A, SelectJson("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01, 3f, () => "N2 the server took A's selection: " + Day.SelectedSite);
            t = Time.unscaledTime;
            yield return AwaitReply(A, cmd);
            Check(lastReply.Contains("requested select Site01"), "N2 A's press went out: " + lastReply.Split('\n')[0]);
            yield return Converge("N2 A's selection", t);
            Check(Count("[Console] " + A.Name + " selects Site01") == 1, "N2 the server processed A's press once");
            Say(H.ClientSelect("HQ"));
            yield return Expect(() => Day.SelectedSite == SiteId.HQ, 3f, () => "N2 the host's HQ");
            yield return Converge("N2 the host's selection", Time.unscaledTime);
            var seen = new List<string>();
            for (int i = 0; i < 4; i++) { yield return Send(A, "{\"id\":{id},\"action\":\"snapshot\"}"); seen.Add(G(lastReply, "selected")); yield return Wait(0.2f); }
            Check(seen.All(s => s == "HQ"), "N2 A's selection holds on HQ: " + string.Join(",", seen));

            Heading("N3 — A unlocks SITE 02 with its own pull: the charge, the mask, the balance and the swing on both peers");
            Day.ServerSetBalanceForChecks(p2 + 50);
            yield return Converge("N3 the balance $" + (p2 + 50), Time.unscaledTime);
            cmd = Command(A, SelectJson("Site02"));
            yield return AwaitReply(A, cmd);
            yield return Expect(() => Day.SelectedSite == SiteId.Site02 && ShipSign() == $"UNLOCK ${p2}/on", 3f, () => "N3 UNLOCK lit on the host: " + ShipSign());
            yield return GuestEventually(A, r => G(r, "leverSign") == $"UNLOCK ${p2}/on", 3f, "N3 UNLOCK lit on A");
            pulls = Day.LastLeverPull.Serial;
            cmd = Command(A, LeverJson("Ship"));
            yield return Expect(() => Day.IsOpen(SiteId.Site02), 3f, () => "N3 A's pull unlocked SITE 02");
            t = Time.unscaledTime;
            Check(Day.Balance == 50 && Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Kind == ConsoleKind.Ship && Day.LastLeverPull.Action == LeverAction.Unlock, $"N3 charged ${p2} once (balance ${Day.Balance}), one Ship/Unlock pull ({H.LeverPullText()})");
            yield return AwaitReply(A, cmd);
            float aAngle = Num(G(lastReply, "leverAngle"));
            Check(lastReply.Contains("requested lever Ship/Unlock/Site02"), "N3 A's pull expected UNLOCK SITE 02: " + lastReply.Split('\n')[0]);
            yield return Converge("N3 the unlock", t);
            Check(Count("[Console] " + A.Name + " unlocked Site02") == 1 && Count("[Console] " + A.Name + " pulled Ship/Unlock/Site02") == 1, "N3 the server processed A's pull once");
            yield return ExpectPlayed(pulls + 1, ConsoleKind.Ship, "N3");
            Say($"N3 A's lever angle in its reply 0.4 s after its pull: {aAngle}°");

            Heading("N4 — two card presses in one frame (A: SITE 03, host: SITE 04): one selection, the same on both peers");
            int selectsBefore = Count(" selects ");
            cmd = Command(A, SelectJson("Site03"));
            Say("N4 same editor frame: " + H.ClientSelect("Site04"));
            yield return AwaitReply(A, cmd);
            yield return Wait(0.8f);
            Check(Day.SelectedSite == SiteId.Site03 || Day.SelectedSite == SiteId.Site04, "N4 the selection is one of the two: " + Day.SelectedSite);
            int selectsNow = Count(" selects ") - selectsBefore;
            Check(selectsNow >= 1 && selectsNow <= 2, $"N4 each press processed at most once ({selectsNow} selections logged)");
            yield return Converge("N4 one selection everywhere (" + Day.SelectedSite + ")", Time.unscaledTime);

            Heading("N5 — two UNLOCK pulls in one frame (A and host, SITE 03, exactly $" + p3 + "): one charge, one pull, one refusal");
            Say(H.ClientSelect("Site03"));
            Day.ServerSetBalanceForChecks(p3);
            yield return Expect(() => Day.SelectedSite == SiteId.Site03 && ShipSign() == $"UNLOCK ${p3}/on", 3f, () => "N5 UNLOCK lit on the host: " + ShipSign());
            yield return GuestEventually(A, r => G(r, "leverSign") == $"UNLOCK ${p3}/on", 3f, "N5 UNLOCK lit on A");
            pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            cmd = Command(A, LeverJson("Ship"));
            Say("N5 same editor frame: " + H.ClientPullLever("Ship"));
            yield return AwaitReply(A, cmd);
            yield return Expect(() => Day.IsOpen(SiteId.Site03), 3f, () => "N5 SITE 03 unlocked");
            t = Time.unscaledTime;
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.LeverInUse || s == ConsoleRules.AlreadyOpen, "N5 the loser");
            Check(Day.Balance == 0 && Day.LastLeverPull.Serial == pulls + 1 && Count("unlocked Site03") == 1, $"N5 one charge (balance ${Day.Balance}), one pull ({Day.LastLeverPull.Serial - pulls}), one unlock logged");
            yield return Converge("N5 the unlock", t);
            yield return ExpectPlayed(pulls + 1, ConsoleKind.Ship, "N5");

            Heading("N6 — A dead (the server's death state) spectates the host at the console: A's screens are the host's; the dead press nothing");
            yield return AtShipConsole();
            Say(H.ClientLookAtShipControl("Nav Card Site01"));
            HQPlayerController aCopy = CopyOf(A);
            Day.ServerPlayerDied(A.Id);
            aCopy.ServerSetDead(true);
            yield return Expect(() => Day.SpectateTargetOf(A.Id) == host.OwnerId, 3f, () => "N6 the server gives dead A the host to watch (" + Day.SpectateTargetOf(A.Id) + ")");
            yield return GuestEventually(A, r => PlayerLine(r, A.Id).Contains("dead=True") && PlayerLine(r, A.Id).Contains("spectatorActive=True") && PlayerLine(r, A.Id).Contains("spectatorTarget=" + host.OwnerId + ";"), 10f, "N6 A's spectator view is on the host");
            yield return Converge("N6 the spectator's consoles", Time.unscaledTime);
            H.ClientLookAtShipControl("Nav Card Site01");
            yield return Wait(0.5f);
            string spectatorShot = Path.GetFullPath(Captures + "console-net-N6-spectator.png");
            if (File.Exists(spectatorShot)) File.Delete(spectatorShot);
            yield return Send(A, "{\"id\":{id},\"action\":\"capture_play\",\"item\":\"" + spectatorShot.Replace("\\", "/") + "\"}");
            yield return Expect(() => File.Exists(spectatorShot), 5f, () => "N6 A's own screen captured while spectating: " + spectatorShot);
            Say("capture " + H.CaptureLocalCamera(Captures + "console-net-N6-host.png"));
            SiteId selBefore = Day.SelectedSite; pulls = Day.LastLeverPull.Serial; trips = Day.Departure.Serial; refusal = Day.LastRefusal.Serial;
            yield return Send(A, SelectJson("Site01"));
            yield return ExpectRefused(refusal, 1, s => s == "The dead press nothing", "N6 dead A's card press");
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, LeverJson("Ship"));
            yield return ExpectRefused(refusal, 1, s => s == "The dead press nothing", "N6 dead A's pull");
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, ExpectJson("Ship", LeverAction.Confirm, SiteId.Site03));
            yield return ExpectRefused(refusal, 1, s => s == "The dead press nothing", "N6 dead A's hand-made CONFIRM");
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, GiveUpJson);
            yield return ExpectRefused(refusal, 1, s => s == "The dead press nothing", "N6 dead A's vote");
            Check(Day.SelectedSite == selBefore && Day.LastLeverPull.Serial == pulls && Day.Departure.Serial == trips && Day.GiveUpVotes == 0 && Day.Balance == 0, "N6 nothing changed: " + H.ConsoleStatus());
            Day.ServerRevive(A.Id);
            aCopy.ServerSetDead(false);
            yield return GuestEventually(A, r => PlayerLine(r, A.Id).Contains("dead=False") && PlayerLine(r, A.Id).Contains("spectatorActive=False"), 8f, "N6 A alive again, its own view back");
            yield return Wait(refusalSeconds);

            Heading("N7 — A off the ship: its presses are refused 'Not aboard: A' and the host's screen waits for A; hand-made requests change nothing");
            List<Transform> pier = CrewSpawner.SpawnPointsIn(WorldScenes.Scene(WorldId.HQ));
            Check(pier.Count > 0, "N7 HQ has pier spawn points");
            yield return GuestMove(A, pier[0].position + Vector3.up * 0.05f);
            yield return Expect(() => CopyOf(A) != null && !hqShip.IsAboard(CopyOf(A).transform.position), 5f, () => "N7 A stands on the pier");
            Say(H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01, 3f, () => "N7 Site01 selected");
            string waiting = "WAITING FOR " + A.Name.ToUpperInvariant() + " TO BOARD";
            t = Time.unscaledTime;
            yield return Expect(() => ShipBottom().Contains(waiting) && ShipSign() == "CONFIRM/off", 3f, () => "N7 the host's console waits for A: " + ShipBottom() + " | " + ShipSign());
            yield return Converge("N7 the crew waits for A", t);
            refusal = Day.LastRefusal.Serial; trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial;
            yield return Send(A, SelectJson("Site04"));
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.NotAboard(A.Name), "N7 A's card press from the pier");
            Check(Day.SelectedSite == SiteId.Site01, "N7 the selection unchanged");
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, LeverJson("Ship"));
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.NotAboard(A.Name), "N7 A's pull from the pier");
            refusal = Day.LastRefusal.Serial;
            Say("N7 the host pulls: " + H.ClientPullLever("Ship"));
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.NotAboard(A.Name), "N7 the host's pull names A");
            yield return GuestEventually(A, r => G(r, "bottomScreen").Contains("NOTICE") && Header(r).Contains("monitor=Not aboard: " + A.Name + ";"), 3f, "N7 A's screen shows the refusal too");
            // The HQ lever from the pier: the server's reach rule (the pier is not the quota console).
            float pierToLever = Vector3.Distance(CopyOf(A).EyePosition, HQC() is MonoBehaviour hqRig ? hqRig.GetComponent<ConsoleRig>().LeverCollider.ClosestPoint(CopyOf(A).EyePosition) : Vector3.zero);
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, ExpectJson("HQ", LeverAction.Pay, SiteId.None));
            yield return ExpectRefused(refusal, 1, s => pierToLever > CopyOf(A).InteractReach + WorldSceneFlow.ConsoleReachMargin ? s == ConsoleRules.NotAtConsole : s.Length > 0, $"N7 A's PAY from the pier ({pierToLever:0.0} m from the lever)");
            // Hand-made requests from a guest aboard.
            yield return Aboard(A, hqShip, 1, "N7");
            yield return Wait(0.5f);
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, "{\"id\":{id},\"action\":\"lever_expect\",\"item\":\"7\",\"slot\":1,\"position\":{\"x\":2,\"y\":0,\"z\":0}}");
            yield return ExpectRefused(refusal, 1, s => s == "No such console", "N7 a pull of an unknown console (kind 7)");
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, ExpectJson("Ship", LeverAction.Confirm, SiteId.Site04));
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.SelectionChanged, "N7 a CONFIRM for a card that is not selected");
            Say(H.ClientSelect("Site04"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site04, 3f, () => "N7 Site04 selected");
            refusal = Day.LastRefusal.Serial;
            yield return Send(A, ExpectJson("Ship", LeverAction.Unlock, SiteId.Site04));
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.ShortBy(p4), "N7 an UNLOCK the crew cannot afford");
            yield return Send(A, "{\"id\":{id},\"action\":\"sail\",\"item\":\"Sea\"}");
            string guestSail = lastReply.Split('\n')[0];
            Check(guestSail.StartsWith("id=" + guestCommand + "; refused:"), "N7 the server's sail called on A's peer does nothing: " + guestSail);
            Check(Day.Departure.Serial == trips && Day.LastLeverPull.Serial == pulls && Day.UnlockedSites == (Destinations.Bit(SiteId.Site02) | Destinations.Bit(SiteId.Site03)) && Day.Balance == 0 && Day.Phase == DayPhase.AtHQ, "N7 nothing sailed, nothing charged, no pull: " + H.ConsoleStatus());
            yield return Wait(refusalSeconds);

            Heading("N8 — transition races: A's vote sent in the frame the ship casts off; a stale CONFIRM and a card press sent while the trip reads Preparing; one trip, one pull, no vote left");
            Say(H.ClientSelect("Site01"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site01 && ShipSign() == "CONFIRM/on", 3f, () => "N8 CONFIRM lit on the host: " + ShipSign() + " | " + ShipBottom());
            yield return GuestEventually(A, r => G(r, "leverSign") == "CONFIRM/on", 3f, "N8 CONFIRM lit on A");
            trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            int aVoted = Count("[GiveUp] " + A.Name + " votes"), sailClears = Count("[GiveUp] Votes cleared: the ship sails");
            int voteCmd = Command(A, GiveUpJson);
            Say("N8 same editor frame: " + H.ClientPullLever("Ship"));
            yield return Expect(() => Day.Departure.Serial == trips + 1, 3f, () => "N8 the ship casts off");
            float castOff = Time.unscaledTime;
            string stageAtRace = Day.Departure.Stage.ToString();
            int staleCmd = Command(A, ExpectJson("Ship", LeverAction.Confirm, SiteId.Site01));
            Say($"N8 while the trip reads {stageAtRace}: A's stale CONFIRM written; host: {H.ClientPullLeverExpecting("Ship", "Confirm", "Site01")}; host: {H.ClientSelect("HQ")}");
            yield return AwaitReply(A, voteCmd);
            yield return AwaitReply(A, staleCmd);
            int selCmd2 = Command(A, SelectJson("Site02"));
            yield return AwaitReply(A, selCmd2);
            yield return Wait(0.8f);
            Soft(stageAtRace == DepartureStage.Preparing.ToString(), "N8 the race presses went out while the trip read Preparing (" + stageAtRace + ")");
            Check(Day.Departure.Serial == trips + 1 && Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Action == LeverAction.Confirm, $"N8 one trip ({Day.Departure.Serial - trips}), one pull ({Day.LastLeverPull.Serial - pulls})");
            Check(Day.SelectedSite == SiteId.None, "N8 the selection cleared by the sail and no press during the trip changed it: " + Day.SelectedSite);
            Check(Day.GiveUpVotes == 0 && Day.Phase != DayPhase.Plank, "N8 no vote left, no plank: " + H.GiveUpStatus());
            bool voteTook = Count("[GiveUp] " + A.Name + " votes") > aVoted;
            Check(!voteTook || Count("[GiveUp] Votes cleared: the ship sails") > sailClears, "N8 A's vote " + (voteTook ? "came first and the sail cleared it" : "was refused (the trip had begun)"));
            Check(Day.LastRefusal.Serial >= refusal + 4 && Day.LastRefusal.Text == ConsoleRules.Travelling, $"N8 the presses during the trip refused ({Day.LastRefusal.Serial - refusal}), the last '{Day.LastRefusal.Text}'");
            yield return Expect(() => ShipBottom().Contains("SAILING TO SITE 01") && ShipSign().EndsWith("/off") && HereCount(ShipTop()) == 0, 3f, () => "N8 the host's screens during the trip: " + ShipBottom() + " | " + ShipTop());
            yield return GuestEventually(A, r => G(r, "bottomScreen").Contains("SAILING TO SITE 01") && G(r, "leverSign").EndsWith("/off") && G(r, "selected") == "None" && !G(r, "topScreen").Contains("@"), 5f, "N8 A's screens during the trip: SAILING TO SITE 01, the sign dim, HERE on no card");
            Say($"N8 trip stage {Day.Departure.Stage} {Time.unscaledTime - castOff:0.0} s after casting off");
            yield return Arrive(WorldId.Sea, "N8");
            yield return GuestEventually(A, r => Header(r).Contains("world=Sea;") && Header(r).Contains("phase=AtSea;"), 20f, "N8 A arrived at sea");
            t = Time.unscaledTime;
            yield return Expect(() => ShipTop().StartsWith($"NAVIGATION · DAY 1/{days}") && Here(ShipTop(), "SITE 01") && HereCount(ShipTop()) == 1 && !ShipBottom().Contains("SAILING") && Day.SelectedSite == SiteId.None, 3f, () => "N8 the host's console at sea is fresh: " + ShipTop() + " | " + ShipBottom());
            yield return Converge("N8 arrived at sea", t);

            Heading("N9 — END DAY pulled by A and the host in one frame: the day ends once, one pull, one refusal");
            ShipParts seaShip = ShipParts.InWorld(WorldId.Sea);
            yield return AtShipConsole();
            yield return Aboard(A, seaShip, 1, "N9");
            yield return DiveDoneNow("N9");
            yield return Expect(() => ShipSign() == "END DAY/on", 3f, () => "N9 END DAY lit on the host: " + ShipSign());
            yield return GuestEventually(A, r => G(r, "leverSign") == "END DAY/on", 3f, "N9 END DAY lit on A");
            int dayBefore = Day.Day; pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            cmd = Command(A, LeverJson("Ship"));
            Say("N9 same editor frame: " + H.ClientPullLever("Ship"));
            yield return AwaitReply(A, cmd);
            yield return Expect(() => Day.Day == dayBefore + 1, 3f, () => "N9 the day ended");
            t = Time.unscaledTime;
            yield return ExpectRefused(refusal, 1, s => s == ConsoleRules.LeverInUse || s == ConsoleRules.NobodyDived, "N9 the loser");
            Check(Day.Day == dayBefore + 1 && !Day.DiveDone && Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Action == LeverAction.EndDay, $"N9 one day ended (day {Day.Day}), one pull");
            yield return Converge("N9 the new day", t);
            yield return ExpectPlayed(pulls + 1, ConsoleKind.Ship, "N9");

            // ================================ 3 players ================================
            Heading("N10 — 3 players: B (headless) joins at sea after selections, unlocks and pulls: it reads the state at once, its lever never swings for a pull it did not see");
            Say(H.ClientSelect("Site04"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site04, 3f, () => "N10 Site04 selected");
            int pullsBeforeJoin = Day.LastLeverPull.Serial;
            Guest B = Launch("B", "Temp/console-net-guest-b", headless: true);
            yield return Joined(B, "N10 B", "Sea", "AtSea");
            string first = lastReply;
            Check(G(first, "selected") == "Site04" && G(first, "unlocked") == "Site02+Site03" && G(first, "site") == "Site01" && G(first, "leverPulls").StartsWith(pullsBeforeJoin + "/Ship/EndDay"), "N10 B's first console line has the state: " + ConsoleL(first));
            Check(G(first, "leverPlayed") == "0" && Num(G(first, "leverAngle")) == 0f, $"N10 B's lever never swung for pull {pullsBeforeJoin} (played {G(first, "leverPlayed")}, angle {G(first, "leverAngle")})");
            Check(Here(G(first, "topScreen"), "SITE 01") && G(first, "leverSign") == $"UNLOCK ${p4}/off", "N10 B's first screens: HERE on SITE 01, a dim UNLOCK $" + p4 + ": " + G(first, "leverSign"));
            for (int i = 0; i < 3; i++)
            {
                yield return Wait(0.4f);
                yield return Send(B, "{\"id\":{id},\"action\":\"snapshot\"}");
                Check(G(lastReply, "leverPlayed") == "0" && Num(G(lastReply, "leverAngle")) == 0f, $"N10 B's lever still at rest ({i + 1}/3)");
            }
            yield return Converge("N10 B joined at sea");

            Heading("N11 — three card presses in one frame (host SITE 02, A HQ, B SITE 01): one selection, the same on every peer");
            yield return Aboard(B, seaShip, 2, "N11");
            selectsBefore = Count(" selects ");
            int ca = Command(A, SelectJson("HQ")), cb = Command(B, SelectJson("Site01"));
            Say("N11 same editor frame: " + H.ClientSelect("Site02"));
            yield return AwaitReply(A, ca);
            yield return AwaitReply(B, cb);
            yield return Wait(0.8f);
            Check(Day.SelectedSite == SiteId.HQ || Day.SelectedSite == SiteId.Site01 || Day.SelectedSite == SiteId.Site02, "N11 one of the three: " + Day.SelectedSite);
            selectsNow = Count(" selects ") - selectsBefore;
            Check(selectsNow >= 1 && selectsNow <= 3, $"N11 each press processed at most once ({selectsNow} logged)");
            yield return Converge("N11 one selection everywhere (" + Day.SelectedSite + ")", Time.unscaledTime);

            Heading("N12 — three CONFIRM pulls in one frame (HQ selected at sea): one trip home, two refusals; B's process killed while the ship pulls away: the trip goes on, B is dropped, the survivors agree");
            Say(H.ClientSelect("HQ"));
            yield return Expect(() => Day.SelectedSite == SiteId.HQ && ShipSign() == "CONFIRM/on", 4f, () => "N12 CONFIRM lit on the host: " + ShipSign() + " | " + ShipBottom());
            yield return GuestEventually(A, r => G(r, "leverSign") == "CONFIRM/on", 4f, "N12 CONFIRM lit on A");
            yield return GuestEventually(B, r => G(r, "leverSign") == "CONFIRM/on", 4f, "N12 CONFIRM lit on B");
            trips = Day.Departure.Serial; pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            ca = Command(A, LeverJson("Ship")); cb = Command(B, LeverJson("Ship"));
            Say("N12 same editor frame: " + H.ClientPullLever("Ship"));
            yield return AwaitReply(A, ca);
            yield return AwaitReply(B, cb);
            yield return Expect(() => Day.Departure.Serial == trips + 1, 3f, () => "N12 the ship sails home");
            yield return ExpectRefused(refusal, 2, s => s == ConsoleRules.LeverInUse || s == ConsoleRules.Travelling, "N12 the two losers");
            Check(Day.Departure.Serial == trips + 1 && Day.LastLeverPull.Serial == pulls + 1 && Day.LastLeverPull.Action == LeverAction.Confirm && Day.SiteDestination == SiteId.HQ, $"N12 one trip, one pull ({H.LeverPullText()})");
            yield return Expect(() => Day.Departure.Stage == DepartureStage.PullingAway, 15f, () => "N12 the ship pulls away (" + Day.Departure.Stage + ")");
            int exceptionsBefore = exceptionsSeen;
            float killedAt = Time.unscaledTime;
            B.Process.Kill();
            crew.Remove(B);
            Say("N12 B's process killed during " + Day.Departure.Stage);
            yield return Arrive(WorldId.HQ, "N12", 120f);
            Say($"N12 docked {Time.unscaledTime - killedAt:0.0} s after the kill");
            yield return Expect(() => CopyOf(B) == null, 70f, () => "N12 B's player is gone");
            Say($"N12 B dropped {Time.unscaledTime - killedAt:0.0} s after the kill; kick logged: {Count("disconnecting Player " + B.Id)}");
            Check(exceptionsSeen == exceptionsBefore, "N12 no exception on the host through the kill: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]"))));
            yield return GuestEventually(A, r => Header(r).Contains("world=HQ;") && Header(r).Contains("phase=AtHQ;"), 25f, "N12 A docked at HQ");
            t = Time.unscaledTime;
            yield return Expect(() => ShipTop().StartsWith($"NAVIGATION · DAY 2/{days}") && Here(ShipTop(), "HQ") && HereCount(ShipTop()) == 1 && !ShipBottom().Contains("SAILING") && Day.SelectedSite == SiteId.None && HQTop().Contains($"DAY 2 OF {days}"), 3f, () => "N12 the host's consoles at the dock are fresh: " + ShipTop() + " | " + ShipBottom() + " | " + HQTop());
            yield return Converge("N12 docked, B gone", t);
            hqShip = ShipParts.InWorld(WorldId.HQ);

            Heading("N13 — a late joiner at HQ after a selection, an unlock and a vote: it reads the state at once, its join clears the vote for everyone, its levers never swung");
            Say(H.ClientSelect("Site04"));
            yield return Expect(() => Day.SelectedSite == SiteId.Site04, 3f, () => "N13 Site04 selected");
            Check(flow.ServerToggleGiveUp(host.Owner, out string n13Why) && Day.GiveUpVotes == 1 && Day.GiveUpCrew == 2, $"N13 the host votes: {H.GiveUpStatus()} {n13Why}");
            yield return GuestEventually(A, r => Line(r, "giveup=").StartsWith("giveup=1/2;"), 3f, "N13 A sees the vote 1/2");
            int clears = Count("[GiveUp] Votes cleared");
            pullsBeforeJoin = Day.LastLeverPull.Serial;
            Guest B2 = Launch("B2", "Temp/console-net-guest-b2", headless: true);
            yield return Joined(B2, "N13 B2", "HQ", "AtHQ");
            first = lastReply;
            Check(Day.GiveUpVotes == 0 && Day.GiveUpCrew == 0 && Count("[GiveUp] Votes cleared") > clears, "N13 the join cleared the vote: " + H.GiveUpStatus());
            Check(G(first, "selected") == "Site04" && G(first, "unlocked") == "Site02+Site03" && G(first, "site") == "HQ" && G(first, "leverPulls").StartsWith(pullsBeforeJoin + "/Ship/Confirm"), "N13 B2's first console line has the state: " + ConsoleL(first));
            Check(G(first, "leverPlayed") == "0" && G(first, "hqLeverPlayed") == "0" && Num(G(first, "leverAngle")) == 0f && Num(G(first, "hqLeverAngle")) == 0f, $"N13 B2's levers never swung (ship {G(first, "leverPlayed")}/{G(first, "leverAngle")}°, HQ {G(first, "hqLeverPlayed")}/{G(first, "hqLeverAngle")}°)");
            Check(Line(first, "giveup=").StartsWith("giveup=0/"), "N13 B2 never saw the old vote: " + Line(first, "giveup="));
            yield return Converge("N13 B2 joined at HQ");

            Heading("N14 — PAY pulled by the host, A and B2 in one frame (day 2, a coin in the room): one sale, one bank, one pull, two refusals; every peer reads the report and swung its lever");
            yield return AtBoard();
            yield return GuestMove(A, StandSpot(1.0f));
            yield return GuestMove(B2, StandSpot(-1.0f));
            CarryableItem coin = null;
            yield return CoinInRoom("N14 coin", c => coin = c);
            int v = coin.Value;
            yield return Expect(() => HQSign() == "PAY/on", 3f, () => "N14 PAY lit on the host: " + HQSign());
            yield return GuestEventually(A, r => G(r, "hqSign") == "PAY/on", 4f, "N14 PAY lit on A");
            yield return GuestEventually(B2, r => G(r, "hqSign") == "PAY/on", 4f, "N14 PAY lit on B2");
            int paySerial = Day.LastPay.Serial; balance = Day.Balance; pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            ca = Command(A, LeverJson("HQ")); cb = Command(B2, LeverJson("HQ"));
            Say("N14 same editor frame: " + H.ClientPullLever("HQ"));
            yield return AwaitReply(A, ca);
            yield return AwaitReply(B2, cb);
            yield return Expect(() => Day.LastPay.Serial > paySerial, 3f, () => "N14 a pay went through");
            t = Time.unscaledTime;
            yield return ExpectRefused(refusal, 2, s => s == ConsoleRules.LeverInUse || s == ConsoleRules.NothingToSell, "N14 the two losers");
            Check(Day.LastPay.Serial == paySerial + 1 && Day.LastPay.Sales == v && Day.Balance == balance + v && Day.LastLeverPull.Serial == pulls + 1 && (coin == null || !coin.IsSpawned) && Count("Pay: sold $" + v + ",") == 1, $"N14 one sale (${Day.LastPay.Sales}), one bank (+${Day.Balance - balance}), one pull ({Day.LastLeverPull.Serial - pulls}), one sale logged");
            yield return Converge("N14 the report", t);
            yield return ExpectPlayed(pulls + 1, ConsoleKind.HQ, "N14");

            Heading("N15 — an UNLOCK sent in the frame a lost payday starts the plank: bought before the loss or refused, never after; on the plank a guest cannot select, pull or vote; the fresh run relocks every site on every peer");
            yield return Wait(flow.Settings.PayReportSeconds);
            Day.ServerForceCycleForChecks(days, true);
            Day.ServerSetBalanceForChecks(p4);
            yield return Aboard(A, hqShip, 1, "N15");
            yield return Aboard(B2, hqShip, 2, "N15");
            yield return AtBoard();
            Check(Day.SelectedSite == SiteId.Site04, "N15 SITE 04 still selected");
            yield return Expect(() => HQSign() == "PAY/on" && ShipSign() == $"UNLOCK ${p4}/on", 3f, () => "N15 PAY and UNLOCK $" + p4 + " lit on the host: " + HQSign() + " | " + ShipSign());
            yield return GuestEventually(A, r => G(r, "leverSign") == $"UNLOCK ${p4}/on", 4f, "N15 UNLOCK lit on A");
            pulls = Day.LastLeverPull.Serial; int planks = Count("[Plank] The run is over");
            ca = Command(A, LeverJson("Ship"));
            Say("N15 same editor frame: " + H.ClientPullLever("HQ"));
            yield return AwaitReply(A, ca);
            yield return Expect(() => Day.Phase == DayPhase.Plank && Day.LastPay.Lost, 3f, () => "N15 the payday is lost: the plank");
            yield return Wait(0.8f);
            bool bought = Day.IsOpen(SiteId.Site04);
            int boughtAt = IndexOf("unlocked Site04"), plankAt = IndexOf("[Plank] The run is over");
            Say($"N15 A's unlock {(bought ? "came first (log " + boughtAt + " before the plank " + plankAt + ")" : "was refused: '" + Day.LastRefusal.Text + "'")}; pulls +{Day.LastLeverPull.Serial - pulls}");
            Check(Count("[Plank] The run is over") == planks + 1, "N15 one plank");
            if (bought) Check(boughtAt >= 0 && boughtAt < plankAt && Day.Balance == 0 && Day.LastLeverPull.Serial == pulls + 2, "N15 bought before the loss: charged once, two pulls");
            else Check(Day.Balance == p4 && Day.LastLeverPull.Serial == pulls + 1 && Count("unlocked Site04") == 0, "N15 refused: nothing charged, one pull (PAY)");
            t = Time.unscaledTime;
            yield return Expect(() => HQTop().Contains("THE RUN IS OVER"), 3f, () => "N15 THE RUN IS OVER on the host: " + HQTop());
            yield return Converge("N15 the run over", t, new[] { "hqTop", "quotaBoard", "hqBottom" }); // the plank walker's name changes turn by turn
            foreach (Guest g in crew.ToList())
                yield return GuestEventually(g, r => G(r, "hqTop").Contains("THE RUN IS OVER") && G(r, "hqSign") == "PAY/off" && G(r, "hqCard").StartsWith("off/"), 4f, "N15 " + g.Label + " reads THE RUN IS OVER, PAY dim, the card dark");
            bool b2Aboard = CopyOf(B2) != null && hqShip.IsAboard(CopyOf(B2).transform.position);
            string b2Expect = b2Aboard ? ConsoleRules.RunOver : ConsoleRules.NotAboard(B2.Name);
            SiteId selOnPlank = Day.SelectedSite; int maskOnPlank = Day.UnlockedSites; pulls = Day.LastLeverPull.Serial; refusal = Day.LastRefusal.Serial;
            yield return Send(B2, SelectJson("Site02"));
            yield return ExpectRefused(refusal, 1, s => s == b2Expect, "N15 B2's card press on the plank" + (b2Aboard ? "" : " (B2 was moved off the deck)"));
            refusal = Day.LastRefusal.Serial;
            yield return Send(B2, LeverJson("Ship"));
            yield return ExpectRefused(refusal, 1, s => s == b2Expect, "N15 B2's pull on the plank");
            refusal = Day.LastRefusal.Serial;
            yield return Send(B2, GiveUpJson);
            yield return ExpectRefused(refusal, 1, s => s.Length > 0, "N15 B2's vote on the plank");
            Say("N15 B2's vote refusal on the plank: '" + Day.LastRefusal.Text + "' (bugs/HQ-1 for the wording)");
            Check(Day.SelectedSite == selOnPlank && Day.UnlockedSites == maskOnPlank && Day.LastLeverPull.Serial == pulls && Day.GiveUpVotes == 0 && Count("[Plank] The run is over") == planks + 1, "N15 nothing changed on the plank: " + H.ConsoleStatus());
            yield return RideOutPlank("N15");
            t = Time.unscaledTime;
            yield return Expect(() => ShipTop().Contains("[SITE 02#]") && ShipTop().Contains("[SITE 03#]") && ShipTop().Contains("[SITE 04#]") && HQTop().Contains("NEW CYCLE"), 3f, () => "N15 the host's consoles after the fresh run: " + ShipTop() + " | " + HQTop());
            yield return Converge("N15 the fresh run relocked everywhere", t);
            hqShip = ShipParts.InWorld(WorldId.HQ);

            // ================================ 4 players ================================
            Heading("N16 — 4 players: C (headless) joins; four identical peers");
            Guest C = Launch("C", "Temp/console-net-guest-c", headless: true);
            yield return Joined(C, "N16 C", "HQ", "AtHQ");
            Check(SpawnedPlayers() == 4, "N16 four players spawned: " + SpawnedPlayers());
            yield return AtBoard();
            yield return GuestMove(A, StandSpot(1.0f));
            yield return GuestMove(B2, StandSpot(-1.0f));
            yield return GuestMove(C, StandSpot(1.8f));
            yield return Converge("N16 four peers");
            yield return Expect(() => HQC().Bottom.VoteCard.Count == "0 / 4", 3f, () => "N16 the card counts four: " + HQC().Bottom.VoteCard.Count);

            Heading("N17 — a fifth guest is refused (the room holds four) while a vote is pending: nobody else is affected, the vote stays");
            Check(flow.ServerToggleGiveUp(host.Owner, out string n17Why) && Day.GiveUpVotes == 1 && Day.GiveUpCrew == 4, "N17 the host votes 1/4: " + H.GiveUpStatus() + " " + n17Why);
            int clearsBeforeD = Count("[GiveUp] Votes cleared");
            Guest D = Launch("D", "Temp/console-net-guest-d", headless: true);
            string dHeader = string.Empty, dMessage = string.Empty;
            float dDeadline = Time.unscaledTime + 40f;
            int most = 4;
            while (Time.unscaledTime < dDeadline)
            {
                most = Math.Max(most, SpawnedPlayers());
                int dc = Command(D, "{\"id\":{id},\"action\":\"snapshot\"}");
                float until = Time.unscaledTime + 3f;
                while (Time.unscaledTime < until && !Reply(D).StartsWith("id=" + dc + ";")) yield return null;
                string r = Reply(D);
                if (r.StartsWith("id=" + dc + ";"))
                {
                    dHeader = Header(r);
                    dMessage = Rx(dHeader, @"; message=([^;]*);");
                    if (dHeader.Contains("client=False") && dMessage.Length > 0 && dMessage != "(absent)") break;
                }
                yield return Wait(1f);
            }
            Say("N17 D's header: " + dHeader.Substring(0, Math.Min(260, dHeader.Length)));
            Check(most == 4 && SpawnedPlayers() == 4, "N17 never a fifth player on the host (most " + most + ")");
            Check(dHeader.Contains("client=False") && dMessage.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0, "N17 D refused: message '" + dMessage + "'");
            try { if (!D.Process.HasExited) D.Process.Kill(); } catch (Exception) { }
            yield return Wait(1f);
            Soft(Day.GiveUpVotes == 1 && Day.HasVotedGiveUp(host.OwnerId) && Count("[GiveUp] Votes cleared") == clearsBeforeD, "N17 the refused connection left the vote alone: " + H.GiveUpStatus() + " / " + string.Join(" / ", logs.Where(l => l.Contains("Votes cleared")).Skip(clearsBeforeD)));
            Check(crew.Count == 3 && Day.Phase == DayPhase.AtHQ, "N17 the four are still in the run");
            yield return Converge("N17 the four after the refusal");

            Heading("N18 — a 4-player vote: three in (3/4 on every peer, YOU VOTED only on the voters' cards), one takes it back (2/4), C leaves mid-vote: cleared for everyone");
            if (!Day.HasVotedGiveUp(host.OwnerId)) Check(flow.ServerToggleGiveUp(host.Owner, out _), "N18 the host votes (again)");
            yield return Send(A, GiveUpJson);
            yield return Send(B2, GiveUpJson);
            yield return Expect(() => Day.GiveUpVotes == 3 && Day.GiveUpCrew == 4 && Day.Phase == DayPhase.AtHQ, 3f, () => "N18 3/4: " + H.GiveUpStatus());
            t = Time.unscaledTime;
            yield return Converge("N18 3/4", t);
            yield return GuestEventually(A, r => G(r, "hqCard") == "on/YOU VOTED · E TO TAKE BACK" && G(r, "hqTop").Contains("GIVE UP 3/4"), 3f, "N18 A's card: YOU VOTED");
            yield return GuestEventually(C, r => G(r, "hqCard") == "on/EVERYONE MUST AGREE" && G(r, "hqTop").Contains("GIVE UP 3/4"), 3f, "N18 C's card: EVERYONE MUST AGREE (C has not voted)");
            yield return Send(B2, GiveUpJson);
            yield return Expect(() => Day.GiveUpVotes == 2 && !Day.HasVotedGiveUp(B2.Id), 3f, () => "N18 B2 took its vote back: " + H.GiveUpStatus());
            yield return Converge("N18 2/4", Time.unscaledTime);
            clears = Count("[GiveUp] Votes cleared: a player left");
            yield return Leave(C, "N18");
            yield return Expect(() => Day.GiveUpVotes == 0 && Day.GiveUpCrew == 0 && Count("[GiveUp] Votes cleared: a player left") > clears, 3f, () => "N18 C's leave cleared the vote: " + H.GiveUpStatus());
            t = Time.unscaledTime;
            yield return Expect(() => HQC().Bottom.VoteCard.Count == "0 / 3", 3f, () => "N18 the card counts three: " + HQC().Bottom.VoteCard.Count);
            yield return Converge("N18 C left", t);
            Check(Day.Phase == DayPhase.AtHQ, "N18 the run goes on");

            Heading("N19 — C comes back (4 players); all four press GIVE UP in the same frame: one agreement, one plank, no exception; the fresh run on every peer");
            Guest C2 = Launch("C2", "Temp/console-net-guest-c2", headless: true);
            yield return Joined(C2, "N19 C2", "HQ", "AtHQ");
            yield return GuestMove(C2, StandSpot(1.8f));
            yield return Expect(() => HQC().Bottom.VoteCard.Count == "0 / 4", 3f, () => "N19 the card counts four: " + HQC().Bottom.VoteCard.Count);
            int agreed = Count("[GiveUp] The whole crew agreed"); planks = Count("[Plank] The run is over"); exceptionsBefore = exceptionsSeen; int runOvers = Day.RunOver.Serial;
            ca = Command(A, GiveUpJson); cb = Command(B2, GiveUpJson); int cc = Command(C2, GiveUpJson);
            Say("N19 same editor frame: " + H.ClientRequestGiveUp());
            yield return AwaitReply(A, ca);
            yield return AwaitReply(B2, cb);
            yield return AwaitReply(C2, cc);
            yield return Expect(() => Day.Phase == DayPhase.Plank, 4f, () => "N19 the plank: " + H.GiveUpStatus());
            yield return Wait(1.0f);
            Check(Count("[GiveUp] The whole crew agreed (4)") == 1 && Count("[GiveUp] The whole crew agreed") == agreed + 1 && Count("[Plank] The run is over") == planks + 1 && exceptionsSeen == exceptionsBefore, "N19 exactly one agreement of four, one plank, no exception");
            foreach (Guest g in crew.ToList())
                yield return GuestEventually(g, r => Header(r).Contains("phase=Plank;") && G(r, "hqTop").Contains("THE RUN IS OVER"), 5f, "N19 " + g.Label + " reads the run over");
            yield return RideOutPlank("N19");
            Check(Day.RunOver.Serial == runOvers + 1, "N19 one run over");
            t = Time.unscaledTime;
            yield return Expect(() => HQTop().Contains("NEW CYCLE") && HQC().Bottom.VoteCard.Count == "0 / 4", 3f, () => "N19 the host's board after the fresh run: " + HQTop());
            yield return Converge("N19 the fresh run, four peers", t);

            // ================================ sweep ================================
            Heading("N20 — the sweep: every guest leaves; no exception on the host; each guest's log is clean");
            foreach (Guest g in crew.ToList()) yield return Leave(g, "N20");
            Say($"host log: {errorsSeen} errors, {exceptionsSeen} exceptions, {logs.Count(l => l.StartsWith("[Warning]"))} warnings during the run");
            Check(exceptionsSeen == 0, "N20 no exception on the host: " + string.Join(" / ", logs.Where(l => l.StartsWith("[Exception]"))));
            yield return Wait(2f);
            var bad = new Regex("expected to exist|already found|Exception|MissingReference|NullReference", RegexOptions.IgnoreCase);
            foreach (Guest g in launched)
            {
                string path = Path.Combine(g.Dir, "player.log");
                string[] lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
                string[] hits = lines.Where(l => bad.IsMatch(l)).ToArray();
                Say($"{g.Label} player.log: {lines.Length} lines, {hits.Length} hits" + (hits.Length > 0 ? ": " + string.Join(" / ", hits.Take(6)) : string.Empty));
                Soft(hits.Length == 0, $"N20 {g.Label}'s log has no exception, MissingReference or 'expected to exist'");
            }
            Say($"worst late sample {worstSync:0.00} s; end: {H.ConsoleStatus()}");
            Check(softFails.Count == 0, $"no soft failure ({softFails.Count}: {string.Join(" | ", softFails)})");
        }
    }
}
