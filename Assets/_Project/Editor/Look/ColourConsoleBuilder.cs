using System.IO;
using System.Text;
using SunkCost.Look;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The crew colour console at the arrival pad (Dan, 8 October 2026): the shared
    // console model (ConsoleBuilder.Place, the navigation console's rig) on the quota
    // console's steel footing, its top screen carrying sixteen swatch controls laid
    // where ColourConsolePaint draws the discs, and the HQColourConsole composer on
    // its root. `at` is the footing's centre on the deck in `parent`'s frame, `yaw`
    // the console's facing (its front is +Z at yaw 0).
    public static class ColourConsoleBuilder
    {
        public static ConsoleRig Place(GameObject parent, Vector3 at, float yaw)
        {
            HQPlatformBuilder.ConsoleFooting(parent, "Colour console footing", at, yaw);
            ConsoleRig rig = ConsoleBuilder.Place(parent.transform, at + Vector3.up * HQPlatformBuilder.FootingHeight, yaw, ConsoleKind.Colour);
            Material touch = ConsoleBuilder.TouchMaterial();
            ConsoleScreen top = rig.TopSurface;
            Rect[] rects = ColourConsolePaint.SwatchRects(top.Width, top.Height);
            for (int i = 0; i < rects.Length; i++)
            {
                Collider swatch = ConsoleBuilder.Control(top, ConsoleRig.SwatchNamePrefix + i, rects[i], ConsoleKind.Colour, ConsoleControlKind.Swatch, SiteId.None, touch, i);
                // No touch quad on a swatch: InteractHighlight would frame it as a square.
                // The painter rings the aimed disc instead (Dan, 11 October 2026).
                Transform quad = swatch.transform.Find(ConsoleRig.TouchName);
                if (quad != null) Object.DestroyImmediate(quad.gameObject);
            }
            rig.gameObject.AddComponent<HQColourConsole>().ShowIdle();
            return rig;
        }

        // ---- the proof: the console through its states from a player's eye -------------

        // In Play Mode as the host (`matrix host`): walks the console through its states
        // over a few seconds (the shown swatch set on the server, a real take by the host
        // through the lever's own server path, the IS NOW line, a refusal) and shoots
        // Temp/look/hq-consoles-colour-<state>.png from where a player stands, in the
        // game's FOV, plus the suit after the take and a side view. Driven by
        // EditorApplication.update so each state is painted before its shot; writes
        // Temp/look/hq-consoles-colour-proof.txt when done (the models of every state).
        public const string ProofPath = LookCapture.Folder + "/hq-consoles-colour-proof.txt";
        private static System.Collections.Generic.IEnumerator<float> proof;
        private static double proofNext;

        public static string Prove()
        {
            if (!Application.isPlaying) return "Enter Play Mode as the host first (matrix host).";
            if (proof != null) return "A proof is already running.";
            Directory.CreateDirectory(LookCapture.Folder);
            File.Delete(ProofPath);
            proof = ProofSteps();
            proofNext = 0;
            EditorApplication.update += ProofTick;
            return "proof started; poll " + ProofPath;
        }

        private static void ProofTick()
        {
            if (proof == null) { EditorApplication.update -= ProofTick; return; }
            if (EditorApplication.timeSinceStartup < proofNext) return;
            try
            {
                if (!proof.MoveNext()) { proof = null; EditorApplication.update -= ProofTick; return; }
                proofNext = EditorApplication.timeSinceStartup + proof.Current;
            }
            catch (System.Exception e)
            {
                File.AppendAllText(ProofPath, "FAIL: " + e.Message + "\n" + e.StackTrace + "\n");
                proof = null; EditorApplication.update -= ProofTick;
            }
        }

        private static System.Collections.Generic.IEnumerator<float> ProofSteps()
        {
            var sb = new StringBuilder();
            HQColourConsole console = HQColourConsole.InHQ();
            ConsoleRig rig = console != null ? console.GetComponent<ConsoleRig>() : null;
            HQPlayerController host = WorldSceneFlow.LocalPlayer();
            CrewDayState day = CrewDayState.Instance;
            if (rig == null || host == null || day == null) { File.WriteAllText(ProofPath, "FAIL: no colour console / host / day state\n"); yield break; }
            Transform t = rig.transform;
            Vector3 front = t.forward; front.y = 0f; front.Normalize();
            Vector3 right = t.right; right.y = 0f; right.Normalize();
            Vector3 stand = t.position + front * 1.6f; stand.y = 0.05f;
            float face = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
            float fov = host.PlayerCamera != null ? host.PlayerCamera.fieldOfView : 75f;
            Vector3 eye = stand + Vector3.up * 1.65f;
            Vector3 topAt = t.TransformPoint(ConsoleBuilder.TopCentre);
            Vector3 bottomAt = t.TransformPoint(ConsoleBuilder.BottomCentre);
            Vector3 midAt = t.position + Vector3.up * 1.15f;
            host.TeleportLocal(stand, face);
            yield return 0.5f;

            // Idle: red shown (the server's default), from the player's eye.
            day.ServerSetShownSwatchForChecks(0);
            yield return 0.6f;
            sb.Append("idle: ").Append(Describe(console)).Append('\n');
            LookCapture.Shoot("hq-consoles-colour-idle", eye, midAt, fov);
            LookCapture.Shoot("hq-consoles-colour-idle-top", eye, topAt, 50f);
            LookCapture.Shoot("hq-consoles-colour-pad", t.position + front * 4.5f + right * 1.2f + Vector3.up * 1.65f, midAt, fov);

            // After a swatch press: BLUE shown.
            ShipControls ship = host.GetComponent<ShipControls>();
            ship.RequestPressSwatch(9);
            yield return 0.8f;
            sb.Append("shown 9: ").Append(Describe(console)).Append('\n');
            LookCapture.Shoot("hq-consoles-colour-shown", eye, midAt, fov);
            LookCapture.Shoot("hq-consoles-colour-shown-top", eye, topAt, 50f);
            LookCapture.Shoot("hq-consoles-colour-shown-bottom", eye, bottomAt, 50f);

            // The pull: the host wears BLUE, the IS NOW line.
            ship.RequestLever(ConsoleKind.Colour, ConsoleRules.Expected(ConsoleKind.Colour, null, out SiteId target), target);
            yield return 0.45f;
            sb.Append("taken: ").Append(Describe(console)).Append(" lever=").Append(console.LeverAngle.ToString("0.#")).Append('\n');
            LookCapture.Shoot("hq-consoles-colour-taken", eye, midAt, fov);
            LookCapture.Shoot("hq-consoles-colour-taken-bottom", eye, bottomAt, 50f);
            LookCapture.Shoot("hq-consoles-colour-suit", host.transform.position + front * 2.4f - right * 0.9f + Vector3.up * 1.7f, host.transform.position + Vector3.up * 1.0f, 60f);
            LookCapture.Shoot("hq-consoles-colour-side", t.position + front * 2.0f - right * 1.7f + Vector3.up * 1.6f, t.position + Vector3.up * 1.3f, 50f);
            LookCapture.Shoot("hq-consoles-colour-side-top", t.position + front * 1.3f - right * 1.5f + Vector3.up * 1.65f, topAt, 40f);

            // A refusal on the bottom screen: a swatch outside the palette.
            yield return HQColourConsole.TakenSeconds + 0.3f;
            ship.RequestPressSwatch(99);
            yield return 0.6f;
            sb.Append("refused: ").Append(Describe(console)).Append('\n');
            LookCapture.Shoot("hq-consoles-colour-refusal", eye, bottomAt, 50f);

            // Back to red for the next look.
            yield return 3f;
            day.ServerSetShownSwatchForChecks(0);
            yield return 0.6f;
            sb.Append("done: ").Append(Describe(console)).Append('\n');
            File.WriteAllText(ProofPath, sb.ToString());
        }

        private static string Describe(HQColourConsole c)
            => c == null ? "none" : $"shown={c.Shown} taken={c.TakenShowing} top={ConsoleModels.Flatten(c.Top)} bottom={ConsoleModels.Flatten(c.Bottom)} sign={ConsoleModels.Flatten(c.Sign)} played={c.LeverPlayedSerial}";
    }
}
