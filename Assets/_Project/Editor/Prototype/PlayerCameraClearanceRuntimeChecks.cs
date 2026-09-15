using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Prototype
{
    // The Play Mode rows of docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md
    // section 7 that only need the host: the corner with the small near plane,
    // an overhang correction and its return, the crouch blend under a low slab,
    // the enclosure cover, and the teleport reset. Camera captures land in Logs/
    // as evidence; the assertions are on the solver state and the physics
    // envelope. Log: Temp/camera-clearance-matrix.log.
    public static class PlayerCameraClearanceRuntimeChecks
    {
        private const string Log = "Temp/camera-clearance-matrix.log";
        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        public static string Status { get; private set; } = "Not run";

        [MenuItem("Sunk Cost/Prototype/Run camera clearance matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Camera clearance matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            File.WriteAllText(Log, "Camera clearance matrix started " + DateTime.Now + "\n");
            Status = "Running";
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            EditorApplication.update += Tick;
        }

        private static void Cleanup()
        {
            foreach (GameObject block in GameObject.FindObjectsByType<GameObject>().Where(g => g.name.StartsWith("CheckBlock")).ToArray()) UnityEngine.Object.Destroy(block);
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
            if (Status == "MATRIX_PASS") Debug.Log("Camera clearance matrix: MATRIX_PASS"); else Debug.LogError("Camera clearance matrix: " + Status);
            Cleanup();
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
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until) yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string label)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) { if (condition()) yield break; yield return null; }
            throw new Exception("timeout: " + label);
        }

        private static HQPlayerController Host() => SunkCost.World.WorldSceneFlow.LocalPlayer();

        private static GameObject Block(string name, Vector3 center, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "CheckBlock " + name;
            block.transform.position = center;
            block.transform.localScale = size;
            Physics.SyncTransforms(); // queries see it this frame, not after the next physics step
            return block;
        }

        private static bool EnvelopeClear(HQPlayerController host)
        {
            PlayerCameraClearance c = host.CameraClearance;
            return PlayerCameraClearance.IsClear(host.PlayerCamera.transform.position, c.LastEnvelopeRadius, host.transform);
        }

        private static void Face(HQPlayerController host, float yaw) => host.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            PlayerCameraClearance clearance = host.CameraClearance;
            Check(clearance != null, "player has PlayerCameraClearance");
            PlayerCameraSettings settings = clearance.Settings;
            Camera camera = host.PlayerCamera;
            Check(Mathf.Approximately(camera.nearClipPlane, settings.NearClip), $"near clip is {settings.NearClip} at runtime");
            SunkCost.Net.SessionInputGate.OpenMenu(); // the real keyboard/mouse stay out of the room
            Vector3 home = new(0f, 0.02f, -3f);
            H.ClientMoveLocalPlayerTo(home); host.SetPitchForChecks(0f); Face(host, 0f);
            yield return Wait(0.3f);
            Check(host.IsGrounded && !host.IsCrouched, "grounded and standing at the start");
            float radius = clearance.LastEnvelopeRadius;
            Check(radius > 0.05f && radius < host.Movement.CapsuleRadius, $"envelope radius {radius:0.000} m fits the capsule (aspect {camera.aspect:0.00})");

            // C1: the corner. Nose in the south-east corner looking into it: no
            // correction needed and nothing outside the room in view.
            Face(host, 135f); H.ClientMoveLocalPlayerTo(new Vector3(5.54f, 0.02f, -5.54f)); yield return Wait(0.2f);
            Check(!clearance.Obstructed && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C1 corner: eye clear without correction");
            H.CaptureLocalCamera("Logs/camera-wall-after-corner.png");
            host.SetPitchForChecks(-70f); yield return null;
            Check(!clearance.Obstructed && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C1 corner looking up: still clear");
            host.SetPitchForChecks(70f); yield return null;
            Check(!clearance.Obstructed && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C1 corner looking down: still clear");
            H.CaptureLocalCamera("Logs/camera-wall-after-corner-down.png");
            // Flat wall head-on, then sideways.
            host.SetPitchForChecks(0f); Face(host, 180f); H.ClientMoveLocalPlayerTo(new Vector3(0f, 0.02f, -5.54f)); yield return Wait(0.2f);
            Check(!clearance.Obstructed && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C1 flat wall head-on: clear without correction");
            Face(host, 90f); yield return null;
            Check(!clearance.Obstructed && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C1 flat wall sideways: clear without correction");

            // C2: an overhang cutting the standing eye's envelope pushes the eye
            // down along the safe line; the rendered envelope is clear.
            Face(host, 0f); H.ClientMoveLocalPlayerTo(home); yield return Wait(0.2f);
            float eyeY = host.EyePosition.y;
            float slabBottom = eyeY + radius * 0.5f;
            GameObject slab = Block("slab", new Vector3(home.x, slabBottom + 0.15f, home.z), new Vector3(2f, 0.3f, 2f));
            yield return null; yield return null;
            Vector3 offset = clearance.Offset;
            Check(!clearance.Obstructed && offset.y < -0.01f && Mathf.Abs(offset.x) < 1e-3f && Mathf.Abs(offset.z) < 1e-3f, $"C2 overhang: eye corrected down by {-offset.y:0.000} m");
            Check(camera.transform.position.y + radius <= slabBottom + 1e-3f && EnvelopeClear(host), "C2 overhang: rendered envelope clear of the slab");
            Check(host.transform.position.y < 0.1f, "C2 overhang: the root did not move");
            H.CaptureLocalCamera("Logs/camera-wall-after-overhang.png");

            // C3: slab gone, the view returns over the blend and settles at zero.
            UnityEngine.Object.Destroy(slab); yield return null; yield return null;
            float previous = clearance.Offset.magnitude;
            Check(previous < Mathf.Abs(offset.y) + 1e-4f, "C3 return: started back toward the desired eye");
            double deadline = EditorApplication.timeSinceStartup + 1.0;
            bool monotonic = true;
            while (clearance.Offset.magnitude > 1e-4f && EditorApplication.timeSinceStartup < deadline)
            {
                if (clearance.Offset.magnitude > previous + 1e-4f) monotonic = false;
                previous = clearance.Offset.magnitude;
                yield return null;
            }
            Check(clearance.Offset.magnitude < 1e-4f && monotonic, "C3 return: back on the desired eye without overshoot");

            // C4: crouching under a low slab. The capsule drops at once, the eye
            // blends down over 0.2 s; every frame of the blend renders clear.
            float lowBottom = 1.3f;
            H.ClientCrouch(true);
            yield return WaitUntil(() => host.IsCrouched, 1f, "crouch accepted");
            Check(host.EyePosition.y + radius > lowBottom, $"C4 the eye is still blending down (eye {host.EyePosition.y:0.00})");
            GameObject low = Block("low", new Vector3(home.x, lowBottom + 0.15f, home.z), new Vector3(2f, 0.3f, 2f));
            int frames = 0; bool everBlocked = false; bool everCorrected = false; bool everObstructed = false;
            deadline = EditorApplication.timeSinceStartup + 0.6;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;
                frames++;
                if (!EnvelopeClear(host) || camera.transform.position.y + radius > lowBottom + 1e-3f) everBlocked = true;
                if (clearance.Offset.magnitude > 1e-4f) everCorrected = true;
                if (clearance.Obstructed) everObstructed = true;
            }
            Check(host.IsCrouched, "C4 crouched under the slab");
            Check(!everBlocked && !everObstructed, $"C4 crouch blend under the slab: {frames} frames, envelope always clear, no cover");
            Check(everCorrected && clearance.Offset.magnitude < 1e-4f, "C4 the blend was corrected on the way down and is free once crouched");
            H.CaptureLocalCamera("Logs/camera-wall-after-crouch-low.png");
            // Standing is refused under the slab (headroom); the eye stays put.
            H.ClientCrouch(false); yield return Wait(0.5f);
            Check(host.IsCrouched && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C4 stand refused under the slab, view unchanged");
            UnityEngine.Object.Destroy(low); yield return Wait(0.6f);
            Check(!host.IsCrouched && clearance.Offset.magnitude < 1e-4f, "C4 stood up once the slab was gone");

            // C5: enclosure. A box around the head leaves no clear pose: the view is
            // obstructed (cover), no target is offered; clearing the box restores it.
            GameObject box = Block("box", host.EyePosition, Vector3.one * 0.8f);
            yield return null; yield return null;
            Check(clearance.Obstructed && host.ViewObstructed && host.CurrentTarget == null && host.CurrentButton == null, "C5 enclosure: obstructed, no target");
            Check(host.transform.position.y < 0.1f, "C5 enclosure: the root did not move");
            UnityEngine.Object.Destroy(box); yield return null; yield return null;
            Check(!clearance.Obstructed && !host.ViewObstructed, "C5 enclosure gone: cover down at once");
            yield return WaitUntil(() => clearance.Offset.magnitude < 1e-4f, 0.5f, "C5 view back on the desired eye");
            Check(EnvelopeClear(host), "C5 enclosure gone: view restored");

            // C6: teleport discards the correction at once (no blend from the old spot).
            GameObject slab2 = Block("slab2", new Vector3(home.x, slabBottom + 0.15f, home.z), new Vector3(2f, 0.3f, 2f));
            yield return null; yield return null;
            Check(clearance.Offset.magnitude > 0.01f, "C6 corrected before the teleport");
            host.TeleportLocal(new Vector3(3f, 0.02f, 3f), 0f);
            Check(clearance.Offset.magnitude < 1e-4f, "C6 teleport: offset reset immediately");
            yield return null; yield return null;
            Check(!clearance.Obstructed && clearance.Offset.magnitude < 1e-4f && EnvelopeClear(host), "C6 after the teleport: clear at the new spot");
            UnityEngine.Object.Destroy(slab2);
            H.ClientMoveLocalPlayerTo(home); Face(host, 0f); host.SetPitchForChecks(0f);
            SunkCost.Net.SessionInputGate.Resume();
            yield return null;
        }
    }
}
