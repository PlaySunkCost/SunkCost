using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SunkCost.Diving;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // Two divers on the seafloor, each photographed from the other's eyes (Dan,
    // 19 September 2026: "if there are two players under the water, how does one
    // see the other?"). The host is player 1, the guest build player 2. Pictures:
    // Logs/two-divers-player1-sees-player2.png and Logs/two-divers-player2-sees-player1.png.
    // Log: Temp/photo.log. Started by CameraClearanceMatrixDriver.Start("photo").
    public static class TwoDiversPhoto
    {
        private const string Log = "Temp/photo.log";
        private const string GuestDir = "Temp/photo-guest";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 1500;
        private static string lastReply = string.Empty;
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            File.WriteAllText(Log, "Photo started " + DateTime.Now + "\n");
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
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            EditorApplication.update -= Tick;
        }

        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.FlowStatus());
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
        private static Process LaunchGuest()
        {
            Directory.CreateDirectory(GuestDir);
            foreach (string stale in new[] { "command.json", "reply.txt" })
                if (File.Exists(Path.Combine(GuestDir, stale))) File.Delete(Path.Combine(GuestDir, stale));
            var tugboat = UnityEngine.Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
            string port = tugboat != null ? " -hq-local-port " + tugboat.GetPort() : string.Empty;
            var info = new ProcessStartInfo(Path.GetFullPath(BuildExe),
                "-screen-width 1600 -screen-height 900 -screen-fullscreen 0 -hq-auto-join-local 127.0.0.1" + port + " -hq-inventory-test-dir \"" + Path.GetFullPath(GuestDir) + "\" -logFile \"" + Path.GetFullPath(GuestDir + "/player.log") + "\"")
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
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###") + ",\"y\":" + v.y.ToString("0.###") + ",\"z\":" + v.z.ToString("0.###") + "}";
        private static HQPlayerController GuestPlayer() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => !p.IsOwner && p.IsSpawned);

        private static IEnumerator Run()
        {
            string sail = H.ServerSail("Sea");
            Check(sail.StartsWith("sailing"), "sailing to sea: " + sail);
            yield return Expect(() => Day != null && Day.Departure.Stage == DepartureStage.Complete && Day.World == WorldId.Sea, 40f, () => "at sea");
            guest = LaunchGuest();
            yield return Expect(() => GuestPlayer() != null, 40f, () => "player 2 (the guest) spawned");
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            H.MoveLocalIntoDeckCabin("Sea");
            Vector3 guestSpot = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + Vector3.up * (DeckCabinBuilder.FloorThicknessMeters + 0.05f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
            yield return Wait(0.5f);
            H.ClientRequestCabin();
            yield return Expect(() => Day.Below.Count == 2 && Day.Elevator.State == ElevatorState.AtBottom && !Day.Riding, 60f, () => "both at the bottom");
            yield return Wait(1f);
            ElevatorController car = WorldSceneFlow.FindCar();
            HQPlayerController host = Host(), other = GuestPlayer();
            // Out of the car through the doorway onto the seafloor: player 2 seven metres out, player 1 four, facing each other.
            Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            Vector3 p2 = car.transform.position + doorway * 7f + Vector3.up * 0.1f;
            Vector3 p1 = car.transform.position + doorway * 4f + Vector3.up * 0.1f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(p2) + "}");
            yield return Wait(0.5f);
            host.TeleportLocal(p1, host.Yaw); yield return Wait(0.5f);
            Vector3 toOther = (other.transform.position + Vector3.up * 1.2f) - host.EyePosition;
            host.transform.rotation = Quaternion.LookRotation(new Vector3(toOther.x, 0f, toOther.z), Vector3.up);
            host.SetPitchForChecks(-Mathf.Atan2(toOther.y, new Vector3(toOther.x, 0f, toOther.z).magnitude) * Mathf.Rad2Deg);
            Vector3 toHost = (host.transform.position + Vector3.up * 1.2f) - (other.transform.position + Vector3.up * 1.6f);
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(toHost) + "}");
            yield return Wait(1.5f);
            H.CaptureScreen("Logs/two-divers-player1-sees-player2.png");
            yield return Send("{\"id\":{id},\"action\":\"capture\",\"item\":\"" + Path.GetFullPath("Logs/two-divers-player2-sees-player1.png").Replace("\\", "/") + "\"}");
            yield return Wait(2f);
            Check(File.Exists("Logs/two-divers-player1-sees-player2.png"), "player 1's picture written");
            Check(File.Exists("Logs/two-divers-player2-sees-player1.png"), "player 2's picture written");
        }
    }
}
