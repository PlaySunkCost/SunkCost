using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The first-person body (PlayerHeadSplit on Dor's GenericCharacter; Dan,
    // 17 September 2026). The host: the model is cut at the neck, the owner sees
    // its body and not its head, the model stands capsule-tall; screenshots
    // looking down at itself, standing and crouched. Then a guest: the host
    // sees the guest's head and body and its colour. Log: Temp/figure-matrix.log.
    // Started by CameraClearanceMatrixDriver.Start("figure").
    public static class FigureRuntimeChecks
    {
        private const string Log = "Temp/figure-matrix.log";
        private const string GuestDir = "Temp/figure-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static System.Diagnostics.Process guest;
        private static int guestCommand = 1500;
        private static string lastReply = string.Empty;
        public static string Status { get; private set; } = "Not run";

        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        [MenuItem("Sunk Cost/Prototype/Run figure matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Figure matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Figure matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Figure matrix: MATRIX_PASS"); else Debug.LogError("Figure matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
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
        // A screenshot without the menu over it: the gate resumes for the frame and opens again.
        private static IEnumerator Shot(string path)
        {
            SunkCost.Net.SessionInputGate.Resume(); yield return null; yield return null;
            H.CaptureScreen(path); yield return Wait(0.5f);
            SunkCost.Net.SessionInputGate.OpenMenu(); yield return null;
            Say("screenshot " + path);
        }
        private static System.Diagnostics.Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            char q = '"';
            var info = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-screen-width 960 -screen-height 540 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir " + q + Path.GetFullPath(GuestDir) + q + " -logFile " + q + Path.GetFullPath(GuestDir + "/player.log") + q)
            { UseShellExecute = false, CreateNoWindow = true };
            return System.Diagnostics.Process.Start(info);
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
                string reply;
                try { string p = Path.Combine(GuestDir, "reply.txt"); reply = File.Exists(p) ? File.ReadAllText(p) : string.Empty; }
                catch (IOException) { reply = string.Empty; }
                if (reply.StartsWith("id=" + guestCommand + ";")) { lastReply = reply; yield break; }
                yield return null;
            }
            throw new Exception("guest did not answer command " + guestCommand + ": " + json);
        }
        private static string Json(string action, int slot = 0, Vector3? position = null)
        {
            char q = '"';
            string text = "{" + q + "id" + q + ":{id}," + q + "action" + q + ":" + q + action + q + "," + q + "slot" + q + ":" + slot;
            if (position.HasValue)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                text += "," + q + "position" + q + ":{" + q + "x" + q + ":" + position.Value.x.ToString("0.###", ci) + "," + q + "y" + q + ":" + position.Value.y.ToString("0.###", ci) + "," + q + "z" + q + ":" + position.Value.z.ToString("0.###", ci) + "}";
            }
            return text + "}";
        }
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);
        private static Renderer[] BodyRenderers(HQPlayerController player, PlayerHeadSplit split)
        {
            Transform model = player.transform.Find("CharacterModel");
            return model == null ? new Renderer[0] : model.GetComponentsInChildren<Renderer>(true).Where(r => !split.HeadRenderers.Contains(r)).ToArray();
        }

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerHeadSplit split = host.HeadSplit;
            Check(split != null && split.Split, "the player prefab carries PlayerHeadSplit and the model was cut (run the head split setup)");
            SunkCost.Net.SessionInputGate.OpenMenu();
            yield return null;

            Heading("F1 — the cut, and what the owner sees of itself");
            Transform model = host.transform.Find("CharacterModel");
            Check(model != null, "F1 the body is Dor's CharacterModel");
            Transform head = model.Find(PlayerHeadSplit.HeadName);
            Check(head != null && head.GetComponent<MeshFilter>().sharedMesh.triangles.Length > 300, $"F1 the head is its own mesh ({(head == null ? 0 : head.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3)} triangles)");
            MeshFilter body = model.GetComponent<MeshFilter>();
            Check(body.sharedMesh.triangles.Length > 300, $"F1 the body kept the rest ({body.sharedMesh.triangles.Length / 3} triangles)");
            Check(split.HeadRenderers.Count == 3, $"F1 the head is the cut mesh and both eyes ({split.HeadRenderers.Count} renderers)");
            Check(!split.HeadShown, "F1 the owner does not see its own head");
            Check(BodyRenderers(host, split).All(r => r.enabled), "F1 the owner sees its own body");
            Bounds bounds = body.GetComponent<Renderer>().bounds;
            Say($"F1 model {model.localScale.y:0.00}× scale; body bounds top {bounds.max.y - host.transform.position.y:0.00} m, head top {(head.GetComponent<Renderer>().bounds.max.y - host.transform.position.y):0.00} m over the feet");
            Check(Mathf.Abs(head.GetComponent<Renderer>().bounds.max.y - host.transform.position.y - 1.8f) < 0.08f, "F1 the model stands capsule-tall (1.8 m)");

            Heading("F2 — looking down at yourself");
            host.TeleportLocal(new Vector3(0f, 0f, -3f), 180f); yield return Wait(0.3f); // facing the empty side of the hall
            host.SetPitchForChecks(70f); yield return Wait(0.3f);
            Check(body.GetComponent<Renderer>().isVisible, "F2 the body is in the owner's view when looking down");
            yield return Shot("Temp/figure-own-body.png");
            host.SetPitchForChecks(0f); yield return Wait(0.2f);

            Heading("F3 — crouched");
            PlayerStance stance = host.GetComponent<PlayerStance>();
            stance.SetDesiredCrouch(true);
            yield return Expect(() => host.IsCrouched, 3f, () => "F3 crouched");
            yield return Wait(0.4f);
            Check(model.localScale.y < 0.7f * PlayerMovementHandsSetup.CharacterModelScale, $"F3 the model folds with the crouch (scale y {model.localScale.y:0.00})");
            host.SetPitchForChecks(70f); yield return Wait(0.3f);
            yield return Shot("Temp/figure-own-crouch.png");
            stance.SetDesiredCrouch(false); host.SetPitchForChecks(0f);
            yield return Expect(() => !host.IsCrouched, 3f, () => "F3 standing again");

            Heading("F4 — a friend: head, body and colour, as the host sees them");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, () => "the guest's copy is here");
            HQPlayerController remote = GuestCopy();
            yield return Wait(1f);
            PlayerHeadSplit friend = remote.HeadSplit;
            Check(friend != null && friend.Split && friend.HeadShown, "F4 the host sees the guest's head");
            Check(BodyRenderers(remote, friend).All(r => r.enabled), "F4 and its body");
            H.ClientMoveLocalPlayerTo(new Vector3(0f, 0f, -3f)); host.SetPitchForChecks(0f); yield return Wait(0.3f);
            Vector3 spot = host.transform.position + host.transform.forward * 2.5f;
            yield return Send(Json("move", 0, spot));
            yield return Wait(0.8f);
            host.SetPitchForChecks(8f); yield return Wait(0.3f);
            Check(remote.BodyColour.a > 0f, "F4 the guest's badge wears a colour: " + remote.BodyColour);
            yield return Shot("Temp/figure-friend.png");
            yield return Send(Json("leave"));
            Say("done");
        }
    }
}
