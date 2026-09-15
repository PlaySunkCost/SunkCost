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
        private static double waitUntil;

        static CameraClearanceMatrixDriver()
        {
            if (SessionState.GetInt(StageKey, -1) >= 0) EditorApplication.update += Tick;
        }

        public static void Start()
        {
            File.WriteAllText(Marker, "started\n");
            SessionState.SetInt(StageKey, 0);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
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
                    if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return; }
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
                    try { PlayerCameraClearanceRuntimeChecks.RunAsHost(); File.AppendAllText(Marker, "matrix started\n"); }
                    catch (System.Exception e) { File.AppendAllText(Marker, "start failed: " + e.Message + "\n"); }
                    SessionState.SetInt(StageKey, -1);
                    EditorApplication.update -= Tick;
                    return;
                default:
                    EditorApplication.update -= Tick;
                    return;
            }
        }
    }
}
