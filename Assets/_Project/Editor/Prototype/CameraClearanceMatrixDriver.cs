using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // One-shot driver for the camera clearance matrix from an editor command:
    // exits a stale Play Mode, waits for the compile, opens Session, enters Play
    // Mode, starts the Local host and runs the matrix. The stage survives the
    // domain reloads on the way in SessionState. Editor-only convenience for the
    // Unity bridge; the menu items remain the human path.
    [InitializeOnLoad]
    public static class CameraClearanceMatrixDriver
    {
        private const string Marker = "Temp/camera-clearance-driver.txt";
        private const string StageKey = "SunkCost.CameraClearanceMatrixDriver.stage";
        private const string JobKey = "SunkCost.CameraClearanceMatrixDriver.job";
        private static double waitUntil;

        static CameraClearanceMatrixDriver()
        {
            if (SessionState.GetInt(StageKey, -1) >= 0) EditorApplication.update += Tick;
        }

        // job: "camera" (the camera clearance matrix), "cabin" (the deck cabin ride
        // matrix) or "host" (host and stop; the caller drives the session).
        public static void Start(string job = "camera")
        {
            File.WriteAllText(Marker, "started " + job + "\n");
            SessionState.SetString(JobKey, job ?? "camera");
            SessionState.SetInt(StageKey, 0);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        // Leave the session and stop Play Mode without leaking the host socket.
        public static void StopCleanly()
        {
            SessionState.SetInt(StageKey, 3);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        // Leave the session once (the transport closes its socket on Leave; a Play
        // Mode stopped with the host up would keep it bound), then allow 1.5 s for
        // the shutdown before the stop. True once that time has passed.
        private const string LeftAtKey = "SunkCost.CameraClearanceMatrixDriver.leftAt";
        private static bool LeaveOnce()
        {
            string leftAt = SessionState.GetString(LeftAtKey, string.Empty);
            if (string.IsNullOrEmpty(leftAt))
            {
                var session = Object.FindAnyObjectByType<SunkCost.Net.PrototypeSessionUI>();
                if (session != null) session.LeaveSession();
                SessionState.SetString(LeftAtKey, EditorApplication.timeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return false;
            }
            double at = double.Parse(leftAt, System.Globalization.CultureInfo.InvariantCulture);
            if (EditorApplication.timeSinceStartup < at + 1.5) return false;
            SessionState.EraseString(LeftAtKey);
            return true;
        }

        private static ushort FreeUdpPort(ushort preferred)
        {
            for (ushort port = preferred; port < preferred + 20; port++)
            {
                try { using var probe = new System.Net.Sockets.UdpClient(port); return port; }
                catch (System.Net.Sockets.SocketException) { }
            }
            return preferred;
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            int stage = SessionState.GetInt(StageKey, -1);
            switch (stage)
            {
                case 0:
                    if (EditorApplication.isPlaying)
                    {
                        // Leave the session before stopping: a Play Mode stopped with the
                        // host up leaves its Tugboat socket bound in the editor process.
                        if (!LeaveOnce()) return;
                        EditorApplication.ExitPlaymode();
                        return;
                    }
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    EditorSceneManager.OpenScene("Assets/_Project/Scenes/Prototype/Session.unity", OpenSceneMode.Single);
                    SessionState.SetInt(StageKey, 1);
                    EditorApplication.EnterPlaymode();
                    return;
                case 1:
                    if (!EditorApplication.isPlaying) return;
                    var ui = Object.FindAnyObjectByType<SunkCost.Net.PrototypeSessionUI>();
                    if (ui == null) return;
                    // A stopped Play Mode can leave the previous Tugboat socket bound in
                    // the editor process until it restarts; host on the first free port.
                    var tugboat = Object.FindAnyObjectByType<FishNet.Transporting.Tugboat.Tugboat>(FindObjectsInactive.Include);
                    if (tugboat != null)
                    {
                        ushort port = FreeUdpPort(tugboat.GetPort());
                        if (port != tugboat.GetPort()) { tugboat.SetPort(port); File.AppendAllText(Marker, "port " + port + "\n"); }
                    }
                    ui.StartLocalHost();
                    waitUntil = EditorApplication.timeSinceStartup + 2.0;
                    SessionState.SetInt(StageKey, 2);
                    return;
                case 2:
                    if (EditorApplication.timeSinceStartup < waitUntil) return;
                    if (SunkCost.World.WorldSceneFlow.LocalPlayer() == null) return;
                    string job = SessionState.GetString(JobKey, "camera");
                    try
                    {
                        if (job == "camera") PlayerCameraClearanceRuntimeChecks.RunAsHost();
                        else if (job == "cabin") DeckCabinRideRuntimeChecks.RunAsHost();
                        File.AppendAllText(Marker, "matrix started (" + job + ")\n");
                    }
                    catch (System.Exception e) { File.AppendAllText(Marker, "start failed: " + e.Message + "\n"); }
                    SessionState.SetInt(StageKey, -1);
                    EditorApplication.update -= Tick;
                    return;
                case 3:
                    if (!EditorApplication.isPlaying) { SessionState.SetInt(StageKey, -1); EditorApplication.update -= Tick; return; }
                    if (!LeaveOnce()) return;
                    EditorApplication.ExitPlaymode();
                    return;
                default:
                    EditorApplication.update -= Tick;
                    return;
            }
        }
    }
}
