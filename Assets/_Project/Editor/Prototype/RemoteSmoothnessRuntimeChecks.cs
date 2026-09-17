using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SunkCost.Editor.Prototype
{
    // How smoothly another player's head arrives on this machine — what a
    // spectator's eyes and the deck TV are made of (Dan and Idan, Build 78 over
    // Steam, 17 September 2026: "the picture steps"). The guest build turns at a
    // steady rate with its pitch swinging while this host samples its copy every
    // frame: a frame where the copy did not move while the guest was turning is a
    // stall, a frame where it moved several frames' worth at once is a jump; the
    // two together are the stepping. Once on a clean local link, once with the
    // guest's outgoing packets delayed, dropped and reordered by FishNet's latency
    // simulator (an internet link, roughly; Steam relay jitter is the real thing).
    // Log: Temp/smooth-matrix.log. Started by CameraClearanceMatrixDriver.Start("smooth").
    public static class RemoteSmoothnessRuntimeChecks
    {
        private const string Log = "Temp/smooth-matrix.log";
        private const string GuestDir = "Temp/smooth-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const float YawSpeed = 90f, PitchSwing = 30f, SampleSeconds = 10f;
        // A poor internet link: 60 ms one way swinging ±40 (a relay's jitter — the
        // part that starves the transform's buffer), 3 % lost, 5 % reordered.
        private const int SimLatencyMs = 60, SimJitterMs = 40;
        private const float SimLoss = 0.03f, SimOutOfOrder = 0.05f;
        // Allowed for the eyes on the simulated link. Stalls are the visible fault:
        // the head freezes, then catches up in one jump. Measured 17 September 2026
        // on this link, raw transform: 0.5–2.5 % of frames stalled, worst catch-up
        // 18–21 frames' worth in one frame, whatever the interpolation buffer (2 or
        // 6 ticks — the buffer moved nothing the noise did not); the eased eyes
        // turn both into a slow and a quick turn of the head.
        private const float MaxStallFraction = 0.01f, MaxJumpFraction = 0.01f, MaxStep = 6f;

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 900;
        private static string lastReply = string.Empty;
        public static string Status { get; private set; } = "Not run";

        [MenuItem("Sunk Cost/Prototype/Run remote smoothness matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Remote smoothness matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Remote smoothness matrix started " + DateTime.Now + "\n");
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
            if (Status == "MATRIX_PASS") Debug.Log("Remote smoothness matrix: MATRIX_PASS"); else Debug.LogError("Remote smoothness matrix: " + Status);
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
            if (!value) throw new Exception(label);
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }
        private static IEnumerator Expect(Func<bool> condition, float seconds, string label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
            Check(condition(), label);
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
        private static string V(Vector3 v) => "{\"x\":" + v.x + ",\"y\":" + v.y + ",\"z\":" + v.z + "}";
        private static string Turn(float yawSpeed, float pitchSwing, float seconds) =>
            "{\"id\":{id},\"action\":\"turn\",\"aim\":" + V(new Vector3(yawSpeed, pitchSwing, 0f)) + ",\"position\":" + V(new Vector3(seconds, 0f, 0f)) + "}";
        private static string NetSim(bool on) =>
            "{\"id\":{id},\"action\":\"netsim\",\"slot\":" + (on ? 1 : 0) + ",\"position\":" + V(on ? new Vector3(SimLatencyMs, SimLoss, SimOutOfOrder) : Vector3.zero) + ",\"aim\":" + V(on ? new Vector3(SimJitterMs, 0f, 0f) : Vector3.zero) + "}";
        private static HQPlayerController GuestCopy() =>
            UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);

        // The per-frame sampler (execution order after the watchers', so the
        // copy's eased eyes are this frame's): the raw transform the
        // NetworkTransform moves, and the eyes a spectator's camera and the TV are
        // placed from (HQPlayerController.EyePose). The eyes are the verdict.
        [DefaultExecutionOrder(600)]
        private sealed class Sampler : MonoBehaviour
        {
            public HQPlayerController Target;
            public float YawSpeed;
            public bool Counting;
            public readonly Track Raw = new(), Eyes = new();
            public int Frames, PitchStill;
            private float previousPitch;
            private bool primed;
            public sealed class Track
            {
                public int Stalls, Jumps;
                public float WorstRatio, previous;
                public void Sample(float yaw, float expected, bool count)
                {
                    float ratio = Mathf.DeltaAngle(previous, yaw) / expected;
                    previous = yaw;
                    if (!count) return;
                    if (Mathf.Abs(ratio) < 0.2f) Stalls++;
                    if (ratio > 2.5f) Jumps++;
                    WorstRatio = Mathf.Max(WorstRatio, ratio);
                }
                public void Reset() { Stalls = Jumps = 0; WorstRatio = 0f; }
            }
            private void LateUpdate()
            {
                if (Target == null) return;
                float raw = Target.Yaw, eyes = Target.EyeYaw, pitch = Target.LookPitch;
                if (!primed) { Raw.previous = raw; Eyes.previous = eyes; previousPitch = pitch; primed = true; return; }
                float expected = Mathf.Max(YawSpeed * Time.deltaTime, 0.0001f);
                Raw.Sample(raw, expected, Counting);
                Eyes.Sample(eyes, expected, Counting);
                bool pitchStill = Mathf.Abs(pitch - previousPitch) < 0.01f;
                previousPitch = pitch;
                if (!Counting) return;
                Frames++;
                if (pitchStill) PitchStill++;
            }
            public void Reset() { Frames = PitchStill = 0; Raw.Reset(); Eyes.Reset(); }
            private string Line(Track t) => $"stalls={t.Stalls} ({100f * Fraction(t.Stalls):0.0}%) jumps={t.Jumps} ({100f * Fraction(t.Jumps):0.0}%) worstStep={t.WorstRatio:0.0}x";
            public string Summary => $"frames={Frames}; transform: {Line(Raw)}; eyes: {Line(Eyes)}; pitchStill={PitchStill} ({100f * Fraction(PitchStill):0.0}%)";
            public float Fraction(int n) => (float)n / Mathf.Max(Frames, 1);
            public float StallFraction => Fraction(Eyes.Stalls);
            public float JumpFraction => Fraction(Eyes.Jumps);
            public float WorstRatio => Eyes.WorstRatio;
        }

        // The turn starts, the link settles for half a second, then the sample window.
        private static IEnumerator Measure(string label, Sampler sampler)
        {
            sampler.Target = GuestCopy(); sampler.YawSpeed = YawSpeed;
            yield return Send(Turn(YawSpeed, PitchSwing, SampleSeconds + 2f));
            yield return Wait(0.5f);
            sampler.Counting = true;
            yield return Wait(SampleSeconds);
            sampler.Counting = false;
            sampler.Target = null;
            Say(label + ": " + sampler.Summary);
            yield return Send(Turn(0f, 0f, 0f));
            yield return Wait(0.5f);
        }
        private static void Judge(string link, Sampler s)
        {
            Check(s.Frames > SampleSeconds * 20, link + ": enough frames sampled (" + s.Frames + ")");
            Check(s.StallFraction <= MaxStallFraction, $"{link}: stalls {100f * s.StallFraction:0.0}% ≤ {100f * MaxStallFraction:0}%");
            Check(s.JumpFraction <= MaxJumpFraction, $"{link}: jumps {100f * s.JumpFraction:0.0}% ≤ {100f * MaxJumpFraction:0}%");
            Check(s.WorstRatio <= MaxStep, $"{link}: worst catch-up {s.WorstRatio:0.0} frames' worth in one frame ≤ {MaxStep:0}");
        }

        private static IEnumerator Run()
        {
            Heading("Guest joins");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 40f, "the guest's copy is here");
            yield return Wait(1.5f);
            var so = new SerializedObject(GuestCopy().GetComponent<FishNet.Component.Transforming.NetworkTransform>());
            Say("player NetworkTransform interpolation=" + so.FindProperty("_interpolation").intValue + " ticks at " + FishNet.InstanceFinder.TimeManager.TickRate + " Hz; editor " + (1f / Time.smoothDeltaTime).ToString("0") + " fps");
            var go = new GameObject("Remote motion sampler");
            var sampler = go.AddComponent<Sampler>();

            Heading("Clean local link");
            yield return Measure("clean", sampler);
            var clean = new { sampler.Frames, sampler.StallFraction, sampler.JumpFraction, sampler.WorstRatio };

            Heading("Simulated internet link (guest outgoing: " + SimLatencyMs + " ±" + SimJitterMs + " ms, " + 100f * SimLoss + "% lost, " + 100f * SimOutOfOrder + "% reordered)");
            yield return Send(NetSim(true));
            Say(lastReply.Split('\n')[0]);
            Check(lastReply.Contains("netsim enabled=True"), "the guest's latency simulator is on");
            yield return Wait(1f);
            sampler.Reset();
            yield return Measure("simulated", sampler);
            yield return Send(NetSim(false));
            UnityEngine.Object.Destroy(go);

            Heading("Verdict");
            Check(clean.Frames > SampleSeconds * 20, "clean link: enough frames sampled (" + clean.Frames + ")");
            Check(clean.StallFraction <= MaxStallFraction, $"clean link: stalls {100f * clean.StallFraction:0.0}% ≤ {100f * MaxStallFraction:0}%");
            Check(clean.JumpFraction <= MaxJumpFraction, $"clean link: jumps {100f * clean.JumpFraction:0.0}% ≤ {100f * MaxJumpFraction:0}%");
            Check(clean.WorstRatio <= MaxStep, $"clean link: worst catch-up {clean.WorstRatio:0.0} frames' worth in one frame ≤ {MaxStep:0}");
            Judge("simulated link", sampler);
        }
    }
}
