using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SunkCost.Diving;
using SunkCost.Editor.Look;
using SunkCost.Net;
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
    // The deck cabin of Dan's round elevator (28 September 2026), BRIEF "TESTING
    // REQUIREMENTS -> DECK CABIN": the car flush in the well (DK1), walking in and out
    // with no step (DK2), the housing's entrance lined up (DK3), the shutters shut while
    // the car is away and open when it is back, on every frame and every peer (DK4),
    // nobody reaching the well from any side, and nobody shut in the entrance (DK5),
    // the panel naming who is missing (DK6), the button starting only with everyone in
    // (DK7), the swap to the dive car in the same place with the same look (DK8), the
    // camera never cutting the glass (DK9), the docked ship at HQ (DK10), and captures
    // at the game's FOV (VA). The host and one windowed guest (Temp/elevator-deck-guest).
    // Log: Temp/elevator-deck-matrix.log; captures: Temp/elevator-deck/*.png.
    // Started by CameraClearanceMatrixDriver.Start("elevator-deck").
    public static class ElevatorDeckRuntimeChecks
    {
        private const string Log = "Temp/elevator-deck-matrix.log";
        private const string GuestDir = "Temp/elevator-deck-guest";
        private const string Shots = "Temp/elevator-deck/";
        private const string BuildExe = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        private const float GlassInnerRadius = 2.4737f; // the car's glass band's inner face (docs/ELEVATOR_LOOK.md)
        private const float WellRadius = 3.70f;          // the well's edge: nobody's capsule reaches inside it off the lane
        private const float LaneHalfWidth = 1.15f;       // the grate rails' unseen walls

        private static IEnumerator steps;
        private static readonly Stack<IEnumerator> stack = new();
        private static Process guest;
        private static int guestCommand = 2800;
        private static string lastReply = string.Empty;
        private static Keyboard keyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode savedInputBehavior;
        private static InputSettings.BackgroundBehavior savedBackgroundBehavior;
        private static bool inputBehaviorChanged;
        private static readonly List<string> softFails = new();
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        [MenuItem("Sunk Cost/Prototype/Run elevator deck matrix (Local host, Play Mode)")]
        public static void RunFromMenu()
        {
            try { RunAsHost(); Debug.Log("Elevator deck matrix running; result in " + Log); }
            catch (InvalidOperationException e) { Debug.LogError(e.Message); }
        }

        public static void RunAsHost()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode and start the Local host first.");
            if (steps != null) throw new InvalidOperationException("Already running");
            if (!File.Exists(BuildExe)) throw new InvalidOperationException("Build " + BuildExe + " first.");
            Directory.CreateDirectory(Shots);
            File.WriteAllText(Log, "Elevator deck matrix started " + DateTime.Now + "\n");
            Status = "Running";
            softFails.Clear();
            steps = Run();
            stack.Clear();
            stack.Push(steps);
            InventoryVerificationPeer.ResetElevatorDeck();
            EditorApplication.update += HostRecord;
            EditorApplication.update += Tick;
        }

        // The host's own per-frame record of its deck cabin (the peer's recorder, shared code).
        private static void HostRecord()
        {
            if (EditorApplication.isPlaying) InventoryVerificationPeer.RecordElevatorDeck();
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
                Status = softFails.Count == 0 ? "MATRIX_PASS" : "FAIL: " + softFails.Count + " soft failure(s): " + string.Join(" | ", softFails);
            }
            catch (Exception e) { Status = "FAIL: " + e.Message + "\n" + e.StackTrace; }
            File.AppendAllText(Log, Status + "\n");
            if (Status == "MATRIX_PASS") Debug.Log("Elevator deck matrix: MATRIX_PASS"); else Debug.LogError("Elevator deck matrix: " + Status);
            try { if (guest != null && !guest.HasExited) guest.Kill(); } catch (Exception) { }
            guest = null;
            steps = null;
            stack.Clear();
            HQPlayerController.KeyboardForChecks = null;
            HQPlayerController.BypassInputGateForChecks = false;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            if (inputBehaviorChanged) { InputSystem.settings.editorInputBehaviorInPlayMode = savedInputBehavior; InputSystem.settings.backgroundBehavior = savedBackgroundBehavior; inputBehaviorChanged = false; }
            EditorApplication.update -= HostRecord;
            EditorApplication.update -= Tick;
        }

        // ---- harness (PlankRuntimeChecks' pattern) ---------------------------------------

        private static void Say(string text) => File.AppendAllText(Log, "  · " + text + "\n");
        private static void Heading(string text) => File.AppendAllText(Log, "\n== " + text + "\n");
        private static void Check(bool value, string label)
        {
            if (!value) throw new Exception(label + "\n" + H.RideStatus() + "\n" + H.FlowStatus());
            File.AppendAllText(Log, "PASS " + label + "\n");
        }
        private static void Soft(bool value, string label)
        {
            if (value) { File.AppendAllText(Log, "PASS " + label + "\n"); return; }
            softFails.Add(label);
            File.AppendAllText(Log, "SOFT-FAIL " + label + "\n");
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
        private static void Keys(params Key[] pressed) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed));
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
            string text = json.Replace("{id}", (++guestCommand).ToString(Ci));
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
        private static IEnumerator GuestEventually(Func<string, bool> predicate, float seconds, Func<string> label)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline)
            {
                yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
                if (predicate(lastReply)) { Check(true, label() + "\n    guest " + DeckLine(lastReply)); yield break; }
                yield return Wait(0.3f);
            }
            throw new Exception(label() + " (the guest never agreed)\n    guest " + DeckLine(lastReply) + "\n" + lastReply);
        }
        private static string Vec(Vector3 v) => "{\"x\":" + v.x.ToString("0.###", Ci) + ",\"y\":" + v.y.ToString("0.###", Ci) + ",\"z\":" + v.z.ToString("0.###", Ci) + "}";
        private static string GuestPlayerLine(string reply, int ownerId) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("player=" + ownerId + ";")) ?? string.Empty;
        private static string DeckLine(string reply) => reply.Split('\n').FirstOrDefault(l => l.StartsWith("elevatorDeck:")) ?? "(no elevatorDeck line)";
        private static string Field(string reply, string name)
        {
            Match m = Regex.Match(DeckLine(reply), @"(?:^|; |: )" + name + @"=('[^']*'|[^;\n]*)");
            return m.Success ? m.Groups[1].Value.Trim('\'') : string.Empty;
        }
        private static float FieldF(string reply, string name) => float.TryParse(Field(reply, name), NumberStyles.Float, Ci, out float v) ? v : float.NaN;
        private static int FieldI(string reply, string name) => int.TryParse(Field(reply, name), NumberStyles.Integer, Ci, out int v) ? v : -1;
        private static Vector3 ParseVec(string text)
        {
            Match m = Regex.Match(text ?? string.Empty, @"\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)");
            return m.Success ? new Vector3(float.Parse(m.Groups[1].Value, Ci), float.Parse(m.Groups[2].Value, Ci), float.Parse(m.Groups[3].Value, Ci)) : new Vector3(float.NaN, float.NaN, float.NaN);
        }
        private static Vector3 GuestPosition(string reply, int guestId) => ParseVec(Regex.Match(GuestPlayerLine(reply, guestId), @"position=(\([^)]*\))").Groups[1].Value);
        private static HQPlayerController GuestCopy() => UnityEngine.Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.IsSpawned && !p.IsOwner);

        // ---- the deck cabin's geometry --------------------------------------------------

        // A point in the deck cabin's own frame: `bearing` degrees from the doorway (+Z,
        // counter-clockwise seen from above is positive toward -X), `r` from the axis, `y` above the root.
        private static Vector3 CabinPoint(ShipParts ship, float r, float bearing, float y)
        {
            float a = bearing * Mathf.Deg2Rad;
            return ship.DeckCabin.TransformPoint(new Vector3(-Mathf.Sin(a) * r, y, Mathf.Cos(a) * r));
        }
        private static Vector3 CabinLocal(ShipParts ship, Vector3 world) => ship.DeckCabin.InverseTransformPoint(world);
        private static float RadiusOf(ShipParts ship, Vector3 world) { Vector3 l = CabinLocal(ship, world); return new Vector2(l.x, l.z).magnitude; }
        private static float ShipY(ShipParts ship, Vector3 world) => ship.ToShipLocal(world).y;
        private static float StandY => DeckCabinBuilder.FloorThicknessMeters + 0.05f; // cabin-local: 5 cm over the floor top

        private static bool IsPlayer(Collider c) => c != null && c.GetComponentInParent<HQPlayerController>() != null;

        // The first solid thing straight down from above a point (players ignored).
        private static bool Ground(Vector3 at, float fromAbove, out RaycastHit hit)
        {
            Vector3 origin = at + Vector3.up * fromAbove;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, fromAbove + 3f, ~0, QueryTriggerInteraction.Ignore);
            hit = default;
            float best = float.MaxValue; bool found = false;
            foreach (RaycastHit h in hits)
                if (!IsPlayer(h.collider) && h.distance < best) { best = h.distance; hit = h; found = true; }
            return found;
        }

        // A player-sized capsule swept from `from` (feet) along `direction`; the first solid thing it meets.
        private static bool Probe(Vector3 from, Vector3 direction, float distance, out RaycastHit hit)
        {
            CharacterController capsule = Host().Controller;
            float radius = capsule.radius * 0.95f;
            Vector3 p1 = from + Vector3.up * (capsule.radius + 0.05f);
            Vector3 p2 = from + Vector3.up * (capsule.height - capsule.radius);
            hit = default;
            float best = float.MaxValue; bool found = false;
            foreach (RaycastHit h in Physics.CapsuleCastAll(p1, p2, radius, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!IsPlayer(h.collider) && h.distance < best && h.distance > 0f) { best = h.distance; hit = h; found = true; }
            return found;
        }

        private static Transform Named(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        private static bool AnyRendererOn(Transform part) => part != null && part.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled);
        private static float YawFraction(Transform pivot, float half) => pivot == null ? -1f : Mathf.Abs(Mathf.DeltaAngle(0f, pivot.localEulerAngles.y)) / half;
        private static CabinPanelDisplay DeckDisplay(ShipParts ship) => ship.DeckCabinCarGlass != null ? ship.DeckCabinCarGlass.GetComponentInChildren<CabinPanelDisplay>(true) : null;

        // A free camera at the game's FOV (the player prefab's) for views no player stands in.
        private static void CaptureAt(Vector3 eye, Vector3 lookAt, float fov, string name)
        {
            GameObject go = new("DeckCheckCamera", typeof(Camera));
            try
            {
                go.transform.position = eye;
                go.transform.LookAt(lookAt);
                Camera camera = go.GetComponent<Camera>();
                camera.fieldOfView = fov;
                camera.nearClipPlane = 0.05f;
                var rt = new RenderTexture(1280, 720, 24);
                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = null;
                RenderTexture.active = rt;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Shots + name, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                rt.Release();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Stand the host at a cabin point looking at another, and capture its own camera (the game's FOV).
        private static IEnumerator HostShot(ShipParts ship, Vector3 feet, Vector3 lookAt, string name)
        {
            HQPlayerController host = Host();
            Vector3 flat = lookAt - feet; flat.y = 0f;
            host.TeleportLocal(feet, Quaternion.LookRotation(flat).eulerAngles.y);
            yield return null; yield return null;
            Vector3 to = lookAt - host.EyePosition;
            host.SetPitchForChecks(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            yield return Wait(0.3f);
            H.CaptureLocalCamera(Shots + name);
            Say($"capture {Shots + name} (FOV {host.PlayerCamera.fieldOfView:0}, eye r {RadiusOf(ship, host.EyePosition):0.00})");
        }

        // ---- round 2 retests (28 September 2026) -----------------------------------------

        // DECK-LEAF-GLASS: the open shutters park right behind the car's parked door leaves;
        // their inside face now wears a bare-steel "Lining". Structure rows plus captures from
        // inside the car toward each parked leaf and from beside one (the tester reads them).
        private static IEnumerator RetestLeafGlass(ShipParts sea)
        {
            Heading("RT-LG — DECK-LEAF-GLASS retest: the parked door leaves read as glass (captures), the shutters' backs are lined");
            foreach (Transform pivot in new[] { sea.DeckCabinHousingDoorL, sea.DeckCabinHousingDoorR })
            {
                Transform lining = pivot != null ? Named(pivot, ElevatorLook.ShutterLiningName) : null;
                Renderer r = lining != null ? lining.GetComponent<Renderer>() : null;
                Check(r != null && r.enabled && r.sharedMaterial != null, $"RT-LG {(pivot != null ? pivot.name : "?")} has its lining shown ({(r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "none")})");
            }
            yield return HostShot(sea, CabinPoint(sea, 1.2f, 180f, StandY), CabinPoint(sea, 3f, 0f, 1.5f), "rt-leaf-inside-to-doorway.png");
            yield return HostShot(sea, CabinPoint(sea, 1.0f, -150f, StandY), CabinPoint(sea, 2.44f, 40f, 1.5f), "rt-leaf-left.png");
            yield return HostShot(sea, CabinPoint(sea, 1.0f, 150f, StandY), CabinPoint(sea, 2.44f, -40f, 1.5f), "rt-leaf-right.png");
            yield return HostShot(sea, CabinPoint(sea, 1.9f, 8f, StandY), CabinPoint(sea, 2.44f, 45f, 1.4f), "rt-leaf-beside.png");
        }

        // DECK-PANEL-TEXT: the deck panel's drawn screen text is wrapped to short lines, big
        // enough to read, and stays inside the ~0.42 x 0.24 m screen (measured in the
        // Screen Anchor's frame from the text mesh's bounds).
        private static IEnumerator PanelTextLayout(ShipParts sea, CabinPanelDisplay display, string state, string captureName)
        {
            yield return Wait(0.4f); // the display refits on the frame after a write
            Transform anchor = Named(sea.DeckCabinCarGlass, CabinPanelDisplay.ScreenAnchorName);
            Transform textT = Named(sea.DeckCabinCarGlass, CabinPanelDisplay.ScreenTextName);
            TextMesh text = textT != null ? textT.GetComponent<TextMesh>() : null;
            MeshRenderer mr = textT != null ? textT.GetComponent<MeshRenderer>() : null;
            Check(anchor != null && text != null && mr != null && mr.enabled, $"RT-PT ({state}) the panel has its Screen Anchor and a shown Screen Text");
            Bounds lb = mr.localBounds;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 a = anchor.InverseTransformPoint(textT.TransformPoint(c));
                minX = Mathf.Min(minX, a.x); maxX = Mathf.Max(maxX, a.x); minY = Mathf.Min(minY, a.y); maxY = Mathf.Max(maxY, a.y);
            }
            string[] lines = text.text.Split('\n');
            float lineH = (maxY - minY) / Mathf.Max(1, lines.Length);
            string drawn = text.text.Replace("\n", " | ");
            string Words(string s) => Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();
            Check(Words(text.text) == Words(display.ScreenText), $"RT-PT ({state}) the drawn words are the screen's words: '{drawn}'");
            int longest = lines.Max(l => l.Length);
            Check(longest <= 16, $"RT-PT ({state}) every drawn line is 16 characters or fewer (longest {longest}, {lines.Length} lines)");
            Check(minX >= -0.21f && maxX <= 0.21f && minY >= -0.12f && maxY <= 0.12f,
                $"RT-PT ({state}) the text stays on the screen: x {minX:+0.000;-0.000} .. {maxX:+0.000;-0.000}, y {minY:+0.000;-0.000} .. {maxY:+0.000;-0.000} (screen ±0.21 x ±0.12 m)");
            Check(lineH >= 0.03f, $"RT-PT ({state}) each line is at least 3 cm tall ({lineH * 100f:0.0} cm per line, characterSize {text.characterSize:0.0000})");
            if (captureName != null)
            {
                Vector3 face = anchor.position + anchor.forward * 1.0f; face.y = sea.DeckCabin.position.y + StandY;
                yield return HostShot(sea, face, anchor.position, captureName);
                yield return HostShot(sea, CabinPoint(sea, 0.2f, 0f, StandY), CabinPoint(sea, 2.3f, 75f, 1.3f), captureName.Replace(".png", "-far.png"));
            }
        }

        private static IEnumerator RetestPanelText(ShipParts sea, CabinPanelDisplay display, string waiting)
        {
            Heading("RT-PT — DECK-PANEL-TEXT retest: the panel's screen is readable and on the screen");
            Check(display.ScreenText.Contains(waiting), "RT-PT the screen names the missing guest: '" + display.ScreenText.Replace("\n", " | ") + "'");
            yield return PanelTextLayout(sea, display, "waiting", "rt-panel-waiting.png");
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.3f);
        }

        // DECK-N1-CANCEL: both press in, both step out onto the grate in the entrance band
        // (r 3.0) while the doors seal; the ride is cancelled "Nobody aboard" and must leave
        // everyone where they stand (no entrance put-out), the car up, the shutters open again.
        private static IEnumerator RetestCancelInEntrance(ShipParts sea, HQPlayerController remote, int guestId, Vector3 guestInCabin)
        {
            Heading("RT-N1 — DECK-N1-CANCEL retest: a cancelled ride leaves the leavers in the entrance where they stand");
            HQPlayerController host = Host();
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            float half = sea.DeckCabinDoorR != null ? Mathf.Abs(Mathf.DeltaAngle(0f, sea.DeckCabinDoorR.localEulerAngles.y)) : 23.578f;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestInCabin) + "}");
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.6f);
            Check(sea.IsInDeckCabin(remote.transform.position + Vector3.up * 0.5f) && sea.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "RT-N1 both stand in the deck cabin");
            CabinPanelDisplay display = DeckDisplay(sea);
            yield return Expect(() => !display.ScreenText.Contains("Waiting for"), flow.Settings.RefusalDisplaySeconds + 3f, () => "RT-PT the screen stops naming the guest once all are in: '" + display.ScreenText.Replace("\n", " | ") + "'");
            yield return PanelTextLayout(sea, display, "all in", "rt-panel-all-in.png");
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.4f);

            bool putOut = false; string putOutLine = null;
            Application.LogCallback listen = (message, stack, type) => { if (message.Contains("put out of the deck cabin")) { putOut = true; putOutLine = message; } };
            Application.logMessageReceived += listen;
            try
            {
                int serial = Day.CabinRide.Serial;
                H.ClientRequestCabin();
                yield return Expect(() => Day.CabinRide.Serial > serial && Day.CabinRide.Stage == CabinRideStage.Sealing, 3f, () => "RT-N1 the press starts the seal with both in (refusal '" + Day.LastRefusal.Text + "')");
                yield return Wait(0.3f);
                Vector3 hostSpot = CabinPoint(sea, 3.0f, 0f, StandY);
                Vector3 guestSpot = CabinPoint(sea, 3.0f, 12f, StandY);
                host.TeleportLocal(hostSpot, host.Yaw);
                yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestSpot) + "}");
                yield return Expect(() => RadiusOf(sea, remote.transform.position) > 2.6f, 3f, () => $"RT-N1 the guest's copy stepped out to r {RadiusOf(sea, remote.transform.position):0.00}");
                float cr = remote.Controller != null ? remote.Controller.radius : 0.35f;
                Check(sea.InDeckCabinEntrance(host.transform.position, host.Controller.radius) && sea.InDeckCabinEntrance(remote.transform.position, cr), $"RT-N1 both stand in the entrance band (host r {RadiusOf(sea, host.transform.position):0.00}, guest r {RadiusOf(sea, remote.transform.position):0.00})");
                yield return Expect(() => Day.CabinRide.Stage == CabinRideStage.Cancelled, flow.Settings.CabinSealSeconds * 4f + 6f, () => $"RT-N1 nobody aboard when the doors shut: the ride is cancelled (stage {Day.CabinRide.Stage})");
                Check(Day.LastRefusal.Text == "Nobody aboard", "RT-N1 the refusal: " + Day.LastRefusal.Text);
                yield return Wait(1.5f); // the old bug's TargetPlace landed within a frame or two of the seal
                float hostMoved = Vector3.Distance(host.transform.position, hostSpot);
                float guestMoved = Vector3.Distance(remote.transform.position, guestSpot);
                Check(!putOut, "RT-N1 no entrance put-out was logged (" + (putOutLine ?? "none") + ")");
                Check(hostMoved < 0.5f && !host.TravelLocked, $"RT-N1 the host was left where it stood (moved {hostMoved:0.00} m, now r {RadiusOf(sea, host.transform.position):0.00}, ship y {ShipY(sea, host.transform.position):0.00})");
                Check(guestMoved < 0.5f, $"RT-N1 the guest's copy was left where it stood (moved {guestMoved:0.00} m, now r {RadiusOf(sea, remote.transform.position):0.00})");
                yield return GuestEventually(r => { Vector3 p = GuestPosition(r, guestId); return !float.IsNaN(p.x) && Vector3.Distance(p, guestSpot) < 0.5f; }, 3f,
                    () => "RT-N1 the guest's own view: still in the entrance where it stood");
                yield return Expect(() => flow.DeckCabinOpenFraction() > 0.99f && YawFraction(sea.DeckCabinHousingDoorR, half) > 0.99f && YawFraction(sea.DeckCabinHousingDoorL, half) > 0.99f && !sea.DeckCabinShutterCollider.enabled && !sea.DeckCabinDoorCollider.enabled, 5f,
                    () => $"RT-N1 the doors and shutters open again, the entrance passable (doors {flow.DeckCabinOpenFraction():0.00}, shutters {YawFraction(sea.DeckCabinHousingDoorR, half):0.00}/{YawFraction(sea.DeckCabinHousingDoorL, half):0.00})");
                Check(Day.Elevator.State == ElevatorState.AtTop && AnyRendererOn(sea.DeckCabinCarGlass) && !Day.Riding, $"RT-N1 the car stayed up ({Day.Elevator.State}), shown, nobody riding");
                yield return GuestEventually(r => Field(r, "deckCarShown") == "True" && FieldF(r, "shutters") > 0.99f && Field(r, "shutterBox") == "False", 4f, () => "RT-N1 the guest sees the car up and the shutters open");
            }
            finally { Application.logMessageReceived -= listen; }
        }

        // Holds W (re-queued every frame) while `sample` reads each frame; stops when `done` holds or the time is up.
        private static IEnumerator WalkWhile(Func<bool> done, float seconds, Action sample)
        {
            float deadline = Time.unscaledTime + seconds;
            try
            {
                while (Time.unscaledTime < deadline && !done()) { Keys(Key.W); yield return null; sample?.Invoke(); }
            }
            finally { Keys(); }
        }

        // ---- the run ------------------------------------------------------------------

        private static IEnumerator Run()
        {
            HQPlayerController host = Host();
            Check(host != null && host.IsServerStarted, "editor is the host with a spawned player");
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            savedInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputBehaviorChanged = true;
            keyboard = InputSystem.AddDevice<Keyboard>("ElevatorDeckCheckKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            SessionInputGate.OpenMenu();
            Keys(); yield return null;
            float seal = flow.Settings.CabinSealSeconds;
            Say($"seal {seal} s, car return grace {flow.Settings.CarReturnGraceSeconds} s, refusal shown {flow.Settings.RefusalDisplaySeconds} s");

            // ---- DK10: the docked ship at HQ ----
            Heading("DK10 — docked at HQ: the car stands in the housing, doors and shutters open, the floor flush");
            ShipParts hq = ShipParts.InWorld(WorldId.HQ);
            Check(hq != null && hq.DeckCabin != null && hq.DeckCabinCarGlass != null && hq.DeckCabinHousingDoorR != null, "DK10 the docked ship has its deck cabin, car and shutters");
            yield return Wait(0.3f);
            float half = Mathf.Abs(Mathf.DeltaAngle(0f, hq.DeckCabinDoorR.localEulerAngles.y));
            Check(half > 20f && half < 27f, $"DK10 the car's doors stand open at their half-angle ({half:0.000}°)");
            Check(AnyRendererOn(hq.DeckCabinCarGlass) && AnyRendererOn(Named(hq.DeckCabinCarGlass, ElevatorLook.CarLookName)), "DK10 the car and its model are shown at HQ");
            Check(Mathf.Abs(YawFraction(hq.DeckCabinHousingDoorR, half) - 1f) < 0.01f && Mathf.Abs(YawFraction(hq.DeckCabinHousingDoorL, half) - 1f) < 0.01f, $"DK10 both shutters open ({YawFraction(hq.DeckCabinHousingDoorR, half):0.000}/{YawFraction(hq.DeckCabinHousingDoorL, half):0.000})");
            Check(hq.DeckCabinShutterCollider != null && !hq.DeckCabinShutterCollider.enabled && hq.DeckCabinDoorCollider != null && !hq.DeckCabinDoorCollider.enabled, "DK10 the entrance is passable: shutter and doorway boxes off");
            yield return FloorFlush(hq, "DK10");

            // ---- to sea, the host in the deck cabin ----
            H.MoveLocalIntoDeckCabin("HQ"); yield return Wait(0.3f);
            Check(H.ServerSail("Sea").StartsWith("sailing"), "sailing to sea with the host in the deck cabin");
            yield return Expect(() => Day.World == WorldId.Sea && Day.Departure.Stage == DepartureStage.Complete && !flow.Transitioning && !host.TravelLocked, 60f, () => "arrived at sea");
            yield return Wait(1f);
            ShipParts sea = ShipParts.InWorld(WorldId.Sea);
            Check(sea != null && sea.DeckCabin != null, "the ship at sea has its deck cabin");
            yield return Expect(() => flow.DeckCabinOpenFraction() > 0.999f, 5f, () => "the car is up and its doors are open at sea");

            // ---- DK1: flush ----
            Heading("DK1 — the car stands flush in the well");
            Check(Mathf.Abs(ShipY(sea, sea.DeckCabin.position) + 0.10f) < 0.002f, $"DK1 the cabin root sits 0.10 m under the deck (ship y {ShipY(sea, sea.DeckCabin.position):0.000})");
            yield return FloorFlush(sea, "DK1");
            Transform pedestal = Named(sea.transform, "Pedestal");
            Renderer pedestalRenderer = pedestal != null ? pedestal.GetComponent<Renderer>() : null;
            if (pedestalRenderer != null && pedestalRenderer.enabled)
            {
                float top = float.MinValue;
                Bounds b = pedestalRenderer.bounds;
                foreach (Vector3 corner in new[] { b.min, b.max, new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.max.x, b.max.y, b.min.z), new Vector3(b.min.x, b.max.y, b.max.z) })
                    top = Mathf.Max(top, ShipY(sea, corner));
                Check(top <= -0.005f, $"DK1 the pedestal's top is under the car's floor, no z-fight (ship y {top:0.000})");
            }
            else Check(true, "DK1 the pedestal is hidden (no z-fight)");
            yield return HostShot(sea, CabinPoint(sea, 4.2f, 0f, StandY), CabinPoint(sea, 1.0f, 0f, 0.1f), "dk1-floor-flush.png");

            // ---- DK3: the entrance lines up ----
            Heading("DK3 — the housing's entrance lines up with the car's doorway");
            Transform housing = Named(sea.DeckCabin, "Cabin Housing");
            MeshCollider housingCollider = housing != null ? housing.GetComponentInChildren<MeshCollider>(true) : null;
            Check(housingCollider != null && housingCollider.enabled, "DK3 the housing has its own mesh collider");
            float lo = float.NaN, hi = float.NaN;
            for (float b = -70f; b <= 70.001f; b += 0.25f)
            {
                Vector3 from = CabinPoint(sea, 0f, 0f, 1.2f), to = CabinPoint(sea, 6f, b, 1.2f);
                bool blocked = housingCollider.Raycast(new Ray(from, (to - from).normalized), out RaycastHit _, 6f);
                if (!blocked) { if (float.IsNaN(lo)) lo = b; hi = b; }
            }
            Check(!float.IsNaN(lo), "DK3 the housing is open somewhere in front of the doorway");
            float centre = (lo + hi) * 0.5f;
            Check(Mathf.Abs(centre) <= 1f, $"DK3 the housing's opening is centred on the car's doorway ({lo:0.00}° .. {hi:0.00}°, centre {centre:0.00}°)");
            // Widths as chords where they stand: the housing's opening at the shutters' radius, the car's doorway at its door leaves (r 2.35).
            float housingChord = 2f * DeckCabinBuilder.ShutterRadius * Mathf.Sin(Mathf.Min(-lo, hi) * Mathf.Deg2Rad);
            float doorwayChord = 2f * DeckCabinBuilder.InteriorRadiusMeters * Mathf.Sin(half * Mathf.Deg2Rad);
            Check(housingChord >= doorwayChord, $"DK3 the housing's opening ({hi - lo:0.0}°, {housingChord:0.00} m at r {DeckCabinBuilder.ShutterRadius}) is wider than the car's doorway ({2f * half:0.0}°, {doorwayChord:0.00} m at r {DeckCabinBuilder.InteriorRadiusMeters})");
            foreach (float x in new[] { -0.7f, 0f, 0.7f })
            {
                Vector3 start = sea.DeckCabin.TransformPoint(new Vector3(x, StandY, 5.5f));
                bool hitLane = Probe(start, -sea.DeckCabin.forward, 2.8f, out RaycastHit laneHit);
                Check(!hitLane, $"DK3 a walker's capsule along the lane at x {x:+0.0;-0.0;0} reaches the car's doorway with nothing in the way ({(hitLane ? laneHit.collider.name + " at r " + RadiusOf(sea, laneHit.point).ToString("0.00") : "clear")})");
            }
            {
                Vector3 underHeader = CabinPoint(sea, 3.075f, 0f, 0.5f);
                RaycastHit[] ups = Physics.RaycastAll(underHeader, Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore).Where(h => !IsPlayer(h.collider)).OrderBy(h => h.distance).ToArray();
                float headroom = ups.Length == 0 ? float.PositiveInfinity : ShipY(sea, ups[0].point);
                Check(headroom >= 3.05f, $"DK3 headroom under the housing's entrance: first solid at ship y {headroom:0.00} ({(ups.Length == 0 ? "none" : ups[0].collider.name)})");
            }

            // ---- DK5 (car up): the well from every side ----
            Heading("DK5 — nobody can fall into the well from any side (car up)");
            yield return WellProbes(sea, carAway: false);
            yield return WellWalks(sea);

            // ---- DK2: walk in and out with no step ----
            Heading("DK2 — walk in and out of the car on the ship with no step");
            yield return WalkInOut(sea);

            // ---- DK9: the camera does not clip the glass ----
            Heading("DK9 — the camera does not cut the glass from inside the car on the ship");
            yield return CameraAgainstGlass(sea);

            // ---- VA: the deck cabin, car up, at the game's FOV ----
            Heading("VA — captures: the deck cabin from the deck (open) and inside the car on the ship");
            yield return HostShot(sea, CabinPoint(sea, 9.5f, 0f, StandY), CabinPoint(sea, 0f, 0f, 1.6f), "va-deck-open.png");
            yield return HostShot(sea, CabinPoint(sea, 8f, 40f, StandY), CabinPoint(sea, 0f, 0f, 1.6f), "va-deck-open-3q.png");
            yield return HostShot(sea, CabinPoint(sea, 3.4f, 0f, StandY), CabinPoint(sea, 0f, 0f, 1.4f), "va-deck-entrance-open.png");
            yield return HostShot(sea, CabinPoint(sea, 1.2f, 180f, StandY), CabinPoint(sea, 3f, 0f, 1.5f), "va-inside-to-doorway.png");
            yield return HostShot(sea, CabinPoint(sea, 0.2f, 0f, StandY), CabinPoint(sea, 2.3f, 75f, 1.3f), "va-inside-to-panel.png");
            yield return HostShot(sea, CabinPoint(sea, 0.8f, 0f, StandY), CabinPoint(sea, 2.4f, 180f, 1.6f), "va-inside-back.png");
            yield return RetestLeafGlass(sea);

            // ---- the guest ----
            Heading("G0 — a guest joins at sea between days");
            guest = LaunchGuest();
            yield return Expect(() => GuestCopy() != null, 45f, () => "G0 the guest's player spawned");
            HQPlayerController remote = GuestCopy();
            int guestId = remote.OwnerId;
            string guestName = WorldSceneFlow.DisplayName(guestId);
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("local=True") && r.Contains("world=Sea") && DeckLine(r).Contains("deckPresent=True"), 25f, () => "G0 the guest is at sea and sees the car up");
            yield return Send("{\"id\":{id},\"action\":\"elev_reset\"}");
            yield return GuestEventually(r => Field(r, "deckCarShown") == "True" && FieldF(r, "shutters") > 0.99f && Field(r, "shutterBox") == "False" && Field(r, "doorBox") == "False", 5f,
                () => "G0/DK4 the guest shows the car up: the car, the shutters open, the entrance passable");

            // ---- DK6: the panel names who is missing ----
            Heading("DK6 — the panel writes who is missing");
            Vector3 guestOnDeck = sea.BoardingPoint != null ? sea.BoardingPoint.position : sea.SpawnPoint(1).position;
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestOnDeck) + "}");
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.5f);
            int serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.LastRefusal.Text == "Waiting for: " + WorldSceneFlow.DisplayName(guestId), 3f, () => "DK6 the deck button refuses without the guest: '" + Day.LastRefusal.Text + "'");
            Check(Day.CabinRide.Serial == serial && !Day.Riding, "DK6/DK7 no ride started with the guest on the deck");
            guestName = WorldSceneFlow.DisplayName(guestId); // the guest's saved name arrives after its spawn
            string waiting = "Waiting for: " + guestName;
            yield return Expect(() => sea.DeckCabinPanel.text.Contains(waiting), 2f, () => "DK6 the status plate names the guest: '" + sea.DeckCabinPanel.text.Replace("\n", " | ") + "'");
            CabinPanelDisplay deckDisplay = DeckDisplay(sea);
            Check(deckDisplay != null, "DK6 the car's panel on the deck has its screen (CabinPanelDisplay)");
            yield return Expect(() => deckDisplay.ScreenText.Contains(waiting), 2f, () => "DK6 the panel's screen names the guest: '" + deckDisplay.ScreenText.Replace("\n", " | ") + "'");
            Check(deckDisplay.GaugeFraction == 0f, $"DK6 the deck panel's gauge reads empty ({deckDisplay.GaugeFraction:0.000})");
            yield return GuestEventually(r => Field(r, "deckPanel").Contains(waiting) && Field(r, "deckScreen").Contains(waiting), 3f, () => "DK6 the guest's plate and panel screen name the guest too");

            // ---- round 2 retests: DECK-PANEL-TEXT and DECK-N1-CANCEL ----
            yield return RetestPanelText(sea, deckDisplay, waiting);
            Vector3 guestInCabin = sea.DeckCabin.position + sea.DeckCabin.right * 1.2f + sea.DeckCabin.up * StandY;
            yield return RetestCancelInEntrance(sea, remote, guestId, guestInCabin);

            // ---- DK7 + DK8: everyone in, the press, the swap ----
            Heading("DK7 — the deck button starts the dive only with everyone in; DK8 — the swap is seamless");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestInCabin) + "}");
            H.MoveLocalIntoDeckCabin("Sea"); yield return Wait(0.6f);
            host.SetPitchForChecks(0f);
            Check(sea.IsInDeckCabin(remote.transform.position + Vector3.up * 0.5f), "DK7 the guest's copy stands in the deck cabin on the host");
            Vector3 hostDeckLocal = CabinFrame.DeckCabin(sea).ToLocal(host.transform.position);
            float hostDeckYaw = CabinFrame.DeckCabin(sea).ToYaw(host.Yaw);
            H.CaptureLocalCamera(Shots + "dk8-deck-inside.png");
            yield return DeckVsCarLook(sea, "before the ride", null);
            serial = Day.CabinRide.Serial;
            H.ClientRequestCabin();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "DK7 the host's press starts the ride with everyone in (refusal: '" + Day.LastRefusal.Text + "')");
            yield return Expect(() => host.gameObject.scene == WorldScenes.Scene(WorldId.Dive) && WorldSceneFlow.FindCar() != null && WorldSceneFlow.FindCar().IsInsideCar(host.transform.position + Vector3.up * 0.5f), 60f, () => "DK8 the host crossed into the dive car");
            yield return null; yield return null;
            ElevatorController car = WorldSceneFlow.FindCar();
            Say($"DK8 the swap read at ride {Day.CabinRide.Stage}, car {Day.Elevator.State}");
            Vector3 hostCarLocal = CabinFrame.Car(car).ToLocal(host.transform.position);
            float hostCarYaw = CabinFrame.Car(car).ToYaw(host.Yaw);
            Check(Vector3.Distance(hostCarLocal, hostDeckLocal) < 0.05f, $"DK8 the rider stands at the same spot in the car ({hostCarLocal:F3}) as in the deck cabin ({hostDeckLocal:F3})");
            Check(Mathf.Abs(Mathf.DeltaAngle(hostCarYaw, hostDeckYaw)) < 1f, $"DK8 facing the same way ({hostCarYaw:0.0}° vs {hostDeckYaw:0.0}°)");
            H.CaptureLocalCamera(Shots + "dk8-car-top-arrival.png");
            yield return DeckVsCarLook(sea, "at the swap", car);
            yield return Expect(() => !Day.CabinRide.Active && Day.Elevator.State == ElevatorState.AtBottom, 60f, () => "DK7 both rode down");
            Check(Day.IsBelow(host.OwnerId) && Day.IsBelow(guestId), "DK7 both are listed below");
            // A diver below holds only the dive world (no ship loaded): its ship rows come once it is back on deck.
            yield return GuestEventually(r => GuestPlayerLine(r, guestId).Contains("scene=DiveSite01") && DeckLine(r).Contains("elevatorDeck: none"), 20f,
                () => "DK4 below, the guest holds the dive world only (its ship is not loaded; its shutter rows follow on deck)");
            Check(!AnyRendererOn(sea.DeckCabinCarGlass) && YawFraction(sea.DeckCabinHousingDoorR, half) < 0.001f && sea.DeckCabinShutterCollider.enabled, "DK4 the host's ship shows the same: no car, shutters shut, the entrance boxed");

            // ---- DK5 (car away): the entrance and the well while the car is below ----
            Heading("DK5 — nobody can fall into the well from any side (car below)");
            yield return WellProbes(sea, carAway: true);

            // ---- the guest rides up alone; the car goes back down empty for the host ----
            Heading("DK4/DK5 — the guest comes up alone; the empty car goes back down for the host; the guest waits in the entrance");
            car = WorldSceneFlow.FindCar();
            Vector3 carDoorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
            yield return GuestEventually(r => r.Contains("travelLocked=False") && GuestPlayerLine(r, guestId).Contains("controllerOn=True"), 8f, () => "the guest is unlocked at the bottom");
            host.TeleportLocal(car.transform.position + carDoorway * 4.2f + Vector3.up * 0.1f, host.Yaw); // the host steps out onto the sand
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f)) + "}");
            yield return Wait(0.6f);
            serial = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"car\"}");
            yield return Expect(() => Day.CabinRide.Serial > serial, 5f, () => "the guest's press starts its ride up (refusal: '" + Day.LastRefusal.Text + "')");
            yield return ArrivalOpens(sea, half, "the guest's ride up");
            Check(Day.IsBelow(host.OwnerId) && !Day.IsBelow(guestId), "the guest is up, the host still below");
            // N1 (the review fix, 28 September 2026): the guest waits on the grate between the
            // car's doorway and the shutters while the empty car is owed below.
            yield return GuestEventually(r => r.Contains("travelLocked=False") && GuestPlayerLine(r, guestId).Contains("controllerOn=True"), 8f, () => "the guest is unlocked on the deck after its ride");
            Vector3 entrance = CabinPoint(sea, 2.8f, 0f, StandY);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(entrance) + "}");
            yield return Wait(0.3f);
            Check(sea.InDeckCabinEntrance(remote.transform.position, remote.Controller != null ? remote.Controller.radius : 0.35f) && !sea.IsInDeckCabin(remote.transform.position + Vector3.up * 0.5f), $"DK5/N1 the guest's copy stands in the entrance (r {RadiusOf(sea, remote.transform.position):0.00}), not in the car");
            bool trapped = false; string trappedAt = string.Empty; float waitedFrom = Time.unscaledTime;
            float returnBy = flow.Settings.CarReturnGraceSeconds + flow.Settings.ArrivalTimeoutSeconds + seal + 20f;
            while (Time.unscaledTime - waitedFrom < returnBy && Day.Elevator.State != ElevatorState.Descending && Day.Elevator.State != ElevatorState.AtBottom)
            {
                if (sea.DeckCabinShutterCollider.enabled && (sea.InDeckCabinEntrance(remote.transform.position, 0.35f) || sea.IsInDeckCabin(remote.transform.position + Vector3.up * 0.5f)))
                { trapped = true; trappedAt = $"r {RadiusOf(sea, remote.transform.position):0.00} car {Day.Elevator.State}"; }
                yield return null;
            }
            Check(Day.Elevator.State == ElevatorState.Descending || Day.Elevator.State == ElevatorState.AtBottom, $"DK5/N1 the empty car went back down for the host after {Time.unscaledTime - waitedFrom:0.0} s");
            Check(!trapped, "DK5/N1 the guest was never inside the shut shutters (" + (trapped ? trappedAt : "never") + ")");
            Check(RadiusOf(sea, remote.transform.position) > 3.2f, $"DK5/N1 the guest was put out on the deck (its copy at r {RadiusOf(sea, remote.transform.position):0.00})");
            yield return GuestEventually(r => RadiusOf(sea, GuestPosition(r, guestId)) > 3.2f && Field(r, "shutterBox") == "True" && Field(r, "deckCarShown") == "False", 10f,
                () => "DK5/N1 the guest's own view: outside the shutters, which are shut and boxed, the car gone");
            Say("guest at r " + RadiusOf(sea, GuestPosition(lastReply, guestId)).ToString("0.00", Ci));

            // X4: with the car away the guest walks down the lane into the shut shutters.
            Vector3 laneStart = CabinPoint(sea, 5.5f, 0f, StandY);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(laneStart) + "}");
            yield return Wait(0.4f);
            yield return Send("{\"id\":{id},\"action\":\"walk\",\"position\":{\"x\":2.5,\"y\":0,\"z\":0},\"aim\":" + Vec(-sea.DeckCabin.forward * 3f) + "}");
            yield return Wait(2.7f);
            yield return GuestEventually(r => Field(r, "walkDone") == "True", 5f, () => "DK5 the guest's walk down the lane into the shut shutters ended");
            {
                Vector3 end = ParseVec(Field(lastReply, "walkEnd"));
                float endR = RadiusOf(sea, end);
                Check(endR > 3.2f && endR < 4.2f, $"DK5 the shut shutters stop the guest at r {endR:0.00} (walked {FieldF(lastReply, "walked"):0.00} m)");
                Check(FieldF(lastReply, "walkMaxDrop") < 0.05f && ShipY(sea, end) > -0.05f, $"DK5 the guest stays on the deck (drop {FieldF(lastReply, "walkMaxDrop"):0.000}, ship y {ShipY(sea, end):0.00})");
            }
            // VA: the deck cabin with the shutters shut, at the game's FOV: the guest's own screen, and a free camera.
            Vector3 guestView = CabinPoint(sea, 8f, 0f, StandY);
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestView) + "}");
            yield return Send("{\"id\":{id},\"action\":\"look\",\"aim\":" + Vec(CabinPoint(sea, 0f, 0f, 1.6f) - (guestView + Vector3.up * 1.6f)) + "}");
            yield return Wait(0.8f);
            string guestShot = Path.GetFullPath(Shots + "va-guest-deck-shut.png").Replace("\\", "/");
            yield return Send("{\"id\":{id},\"action\":\"capture_play\",\"item\":\"" + guestShot + "\"}");
            float fov = host.PlayerCamera != null ? host.PlayerCamera.fieldOfView : 75f;
            CaptureAt(CabinPoint(sea, 9.5f, 0f, 1.7f), CabinPoint(sea, 0f, 0f, 1.6f), fov, "va-deck-shut.png");
            CaptureAt(CabinPoint(sea, 8f, 40f, 1.7f), CabinPoint(sea, 0f, 0f, 1.6f), fov, "va-deck-shut-3q.png");
            CaptureAt(CabinPoint(sea, 4.0f, 0f, 1.7f), CabinPoint(sea, 0f, 0f, 1.4f), fov, "va-deck-entrance-shut.png");
            Say("captures " + guestShot + ", va-deck-shut.png, va-deck-shut-3q.png, va-deck-entrance-shut.png (free camera at FOV " + fov.ToString("0", Ci) + ")");

            // ---- the host rides up; the shutters open with the car ----
            Heading("DK4 — the host rides up: the shutters open with the car's doors when it is back");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(sea.SpawnPoint(1).position) + "}");
            yield return Expect(() => Day.Elevator.State == ElevatorState.AtBottom, 40f, () => "the empty car is at the bottom for the host");
            yield return Wait(1f);
            H.MoveLocalIntoCar(); yield return Wait(0.5f);
            serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "the host's car press starts the ride up");
            yield return ArrivalOpens(sea, half, "the host's ride up");
            Check(Day.Below.Count == 0 && sea.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "the host is back in the deck cabin, nobody below");
            yield return GuestEventually(r => Field(r, "deckCarShown") == "True" && FieldF(r, "shutters") > 0.999f && Field(r, "shutterBox") == "False" && Field(r, "doorBox") == "False", 6f,
                () => "DK4 the guest sees the car back, the shutters open, the entrance passable");

            // ---- DK7b: the guest's own press, on day 2 ----
            Heading("DK7 — day 2: the guest's own press starts the dive with everyone in");
            H.ClientRequestEndDay();
            yield return Expect(() => Day.Day == 2 && !Day.DiveDone, 4f, () => "day 2 after End day");
            yield return Expect(() => ScreenFade.Instance == null || ScreenFade.Instance.IsClear, flow.Settings.DayCardSeconds + 3f, () => "the day card is over");
            H.MoveLocalIntoDeckCabin("Sea");
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(guestInCabin) + "}");
            yield return Wait(0.6f);
            serial = Day.CabinRide.Serial;
            yield return Send("{\"id\":{id},\"action\":\"cabin\"}");
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "DK7 the guest's press starts the ride with everyone in (refusal: '" + Day.LastRefusal.Text + "')");
            yield return Expect(() => !Day.CabinRide.Active && Day.Elevator.State == ElevatorState.AtBottom, 70f, () => "DK7 both rode down on day 2");
            yield return GuestEventually(r => r.Contains("travelLocked=False") && GuestPlayerLine(r, guestId).Contains("controllerOn=True"), 8f, () => "the guest is unlocked at the bottom");
            car = WorldSceneFlow.FindCar();
            H.MoveLocalIntoCar();
            yield return Send("{\"id\":{id},\"action\":\"move\",\"position\":" + Vec(car.transform.position + car.transform.forward * 1.1f + Vector3.up * (SunkCost.Sites.ElevatorCabinBuilder.CarFloorThickness + 0.05f)) + "}");
            yield return Wait(0.8f);
            serial = Day.CabinRide.Serial;
            H.ClientRequestCar();
            yield return Expect(() => Day.CabinRide.Serial > serial, 3f, () => "both ride up");
            yield return ArrivalOpens(sea, half, "the ride up together");

            // ---- DK4: every frame on both peers ----
            Heading("DK4 — the shutters, the boxes and the car's presence on every frame of the day, on both peers");
            yield return Wait(0.5f);
            yield return Send("{\"id\":{id},\"action\":\"snapshot\"}");
            string hostLine = InventoryVerificationPeer.ElevatorDeckLine().Trim();
            Say("host  " + hostLine);
            Say("guest " + DeckLine(lastReply));
            foreach ((string who, string line) in new[] { ("host", "x\n" + hostLine), ("guest", lastReply) })
            {
                int frames = FieldI(line, "deckFrames"), shut = FieldI(line, "deckShutFrames");
                Check(frames > 2000 && shut > 300, $"DK4 {who}: recorded {frames} frames, {shut} with the shutters shut");
                Check(FieldF(line, "deckWorstShutter") < 0.02f, $"DK4 {who}: the shutters followed the replicated state on every frame (worst {Field(line, "deckWorstShutter")} of their sweep)");
                Check(FieldI(line, "deckBadBox") == 0, $"DK4 {who}: the shutter box was on exactly while the shutters were shut ({Field(line, "deckBadBox")} bad frames; {Field(line, "deckFirstBad")})");
                Check(FieldI(line, "deckBadDoorway") == 0, $"DK4 {who}: the doorway box was on exactly while the doors were shut ({Field(line, "deckBadDoorway")} bad frames)");
                Check(FieldI(line, "deckBadShown") == 0, $"DK4 {who}: the car showed exactly while the replicated state has it up ({Field(line, "deckBadShown")} frames off by 3 frames or more; {Field(line, "deckFirstBad")})");
            }

            // ---- guest logs ----
            string guestLog = Path.Combine(GuestDir, "player.log");
            string logText = File.Exists(guestLog) ? ReadShared(guestLog) : string.Empty;
            int missing = Regex.Matches(logText, "expected to exist|already found|NullReferenceException|MissingReferenceException").Count;
            Soft(missing == 0, $"guest log: no 'expected to exist', 'already found' or null/missing reference ({missing})");
            yield return Send("{\"id\":{id},\"action\":\"leave\"}");
        }

        private static string ReadShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // The floor inside the car and the grate in front of it, read by rays: the floor top at the
        // deck's plane (ship y 0.000), the grate within a centimetre of it, no step at the doorway.
        private static IEnumerator FloorFlush(ShipParts ship, string row)
        {
            float worst = 0f; int n = 0; string where = string.Empty;
            foreach (float r in new[] { 0.3f, 0.9f, 1.5f, 2.1f })
                foreach (float b in new[] { 45f, 135f, 225f, 315f })
                {
                    Vector3 at = CabinPoint(ship, r, b, 0.1f);
                    Check(Ground(at, 1.5f, out RaycastHit hit), $"{row} a ray finds the car's floor at r {r} bearing {b}");
                    float y = ShipY(ship, hit.point);
                    n++;
                    if (Mathf.Abs(y) > Mathf.Abs(worst)) { worst = y; where = $"r {r} b {b} on {hit.collider.name}"; }
                }
            Check(Mathf.Abs(worst) <= 0.005f, $"{row} the car's floor is flush with the deck at {n} points (worst ship y {worst:+0.000;-0.000;0.000} at {where})");
            yield return LaneGround(ship, row, 0f, 1.8f, 4.8f, true);
        }

        // Rays down the lane every 5 cm: the ground stays at deck height (a slit narrower than a
        // body is only noted), and no step between neighbouring readings.
        private static IEnumerator LaneGround(ShipParts ship, string row, float x, float zFrom, float zTo, bool steps)
        {
            float prev = float.NaN, worstStep = 0f, gapFrom = float.NaN, widestGap = 0f;
            string stepAt = string.Empty, gaps = string.Empty;
            for (float z = zFrom; z <= zTo + 0.001f; z += 0.05f)
            {
                bool found = Ground(ship.DeckCabin.TransformPoint(new Vector3(x, 0.1f, z)), 1.5f, out RaycastHit hit);
                float y = found ? ShipY(ship, hit.point) : float.NegativeInfinity;
                bool level = y >= -0.02f && y <= 0.03f;
                if (!level)
                {
                    if (float.IsNaN(gapFrom)) { gapFrom = z; gaps += $" z {z:0.00} ship y {y:0.00} ({(found ? hit.collider.name : "nothing")});"; }
                    widestGap = Mathf.Max(widestGap, z - gapFrom + 0.05f);
                    prev = float.NaN;
                    continue;
                }
                gapFrom = float.NaN;
                if (steps && !float.IsNaN(prev) && Mathf.Abs(y - prev) > worstStep) { worstStep = Mathf.Abs(y - prev); stepAt = $"z {z:0.00} ({hit.collider.name})"; }
                prev = y;
            }
            Check(widestGap < 0.30f, $"{row} the lane at x {x:+0.0;-0.0;0} from z {zFrom:0.00} to {zTo:0.00} has no hole a body could drop into (widest dip {widestGap:0.00} m)");
            Soft(widestGap == 0f, $"{row} the lane at x {x:+0.0;-0.0;0} is at deck height all along (dips:{(gaps.Length == 0 ? " none" : gaps)})");
            if (steps) Check(worstStep < 0.02f, $"{row} no step from the car's floor over the doorway and the grate to the deck (largest {worstStep * 100f:0.0} cm at {stepAt})");
            yield break;
        }

        // Capsule sweeps at 36 bearings from outside the housing toward the axis, and sideways from the entrance.
        private static IEnumerator WellProbes(ShipParts ship, bool carAway)
        {
            string tag = carAway ? "car below" : "car up";
            int blockedOutside = 0;
            for (float b = 0f; b < 360f; b += 10f)
            {
                float signed = Mathf.DeltaAngle(0f, b);
                Vector3 start = CabinPoint(ship, 5.6f, b, StandY);
                bool hitSomething = Probe(start, CabinPoint(ship, 0f, 0f, StandY) - start, 5.6f - 2.0f, out RaycastHit hit);
                float hitR = hitSomething ? RadiusOf(ship, hit.point) : 0f;
                string what = hitSomething ? $"{hit.collider.name} at r {hitR:0.00}" : "nothing";
                bool lane = Mathf.Abs(signed) < 12f; // the capsule's width fits the grate lane only near the doorway's bearing
                if (!lane)
                {
                    Check(hitSomething && hitR >= WellRadius - 0.01f, $"DK5 ({tag}) bearing {signed:+0;-0;0}°: stopped outside the well by {what}");
                    blockedOutside++;
                }
                else if (carAway)
                    Check(hitSomething && (hit.collider.name == ShipParts.DeckCabinShutterColliderName || hit.collider.name == ShipParts.DeckCabinDoorColliderName || hitR >= WellRadius - 0.01f), $"DK5 ({tag}) bearing {signed:+0;-0;0}° on the lane: the shut entrance stops it ({what})");
                else
                    Check(!hitSomething || hitR <= 2.5f || hitR >= WellRadius - 0.01f, $"DK5 ({tag}) bearing {signed:+0;-0;0}° on the lane: into the car or stopped outside the well ({what})");
            }
            Say($"{blockedOutside} bearings off the lane checked ({tag})");
            // Sideways off the grate, from the entrance band: the grate rails' walls.
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 from = ship.DeckCabin.TransformPoint(new Vector3(0f, StandY, 2.9f));
                bool hit = Probe(from, ship.DeckCabin.right * side, 2.5f, out RaycastHit h);
                float x = hit ? Mathf.Abs(CabinLocal(ship, h.point).x) : float.PositiveInfinity;
                Check(hit && x <= LaneHalfWidth + 0.06f, $"DK5 ({tag}) sideways off the grate in the entrance ({(side < 0 ? "-X" : "+X")}): the rail stops the capsule at |x| {x:0.00} ({(hit ? h.collider.name : "nothing")})");
            }
            // Along the lane, the ground is continuous (no hole to fall through) from the deck to the car's doorway.
            foreach (float x in new[] { -0.8f, 0.8f })
                yield return LaneGround(ship, $"DK5 ({tag})", x, 2.45f, 5.5f, false);
            if (!carAway)
            {
                // From inside the car outward: the car's walls and posts hold everyone in except at the doorway.
                for (float b = 30f; b < 360f; b += 30f)
                {
                    Vector3 from = CabinPoint(ship, 0f, 0f, StandY);
                    bool hit = Probe(from, CabinPoint(ship, 3f, b, StandY) - from, 3.2f, out RaycastHit h);
                    float r = hit ? RadiusOf(ship, h.point) : float.PositiveInfinity;
                    Check(hit && r <= 2.5f, $"DK5 (car up) from inside the car toward bearing {b:0}°: stopped by {(hit ? h.collider.name : "nothing")} at r {r:0.00}");
                }
            }
            yield break;
        }

        // The virtual W from outside the housing toward the axis at 8 bearings and along both lane edges.
        private static IEnumerator WellWalks(ShipParts ship)
        {
            HQPlayerController host = Host();
            foreach (float b in new[] { 30f, 60f, 90f, 135f, 180f, 225f, 270f, 315f })
            {
                Vector3 start = CabinPoint(ship, 5.8f, b, StandY);
                Vector3 toAxis = CabinPoint(ship, 0f, 0f, StandY) - start; toAxis.y = 0f;
                host.TeleportLocal(start, Quaternion.LookRotation(toAxis).eulerAngles.y);
                host.SetPitchForChecks(0f);
                yield return Wait(0.3f);
                float minR = float.MaxValue, minY = float.MaxValue, offLane = 0f;
                yield return WalkWhile(() => false, 3f, () =>
                {
                    Vector3 l = CabinLocal(ship, host.transform.position);
                    float r = new Vector2(l.x, l.z).magnitude;
                    minR = Mathf.Min(minR, r); minY = Mathf.Min(minY, ShipY(ship, host.transform.position));
                    if (r < WellRadius && (Mathf.Abs(l.x) > LaneHalfWidth || l.z < 0f)) offLane = Mathf.Max(offLane, WellRadius - r); // over the well, not on the grate lane
                });
                Check(offLane == 0f && minY > -0.2f, $"DK5 walking at the well from bearing {b:0}° for 3 s: never over the well off the lane (closest r {minR:0.00}, the well's edge {WellRadius}), lowest ship y {minY:0.00}");
            }
            foreach (float x in new[] { -1.0f, 1.0f })
            {
                Vector3 start = ship.DeckCabin.TransformPoint(new Vector3(x, StandY, 6.0f));
                host.TeleportLocal(start, Quaternion.LookRotation(-ship.DeckCabin.forward).eulerAngles.y);
                host.SetPitchForChecks(0f);
                yield return Wait(0.3f);
                float minY = float.MaxValue, worstX = 0f;
                yield return WalkWhile(() => false, 3f, () =>
                {
                    Vector3 l = CabinLocal(ship, host.transform.position);
                    minY = Mathf.Min(minY, ShipY(ship, host.transform.position));
                    float r = new Vector2(l.x, l.z).magnitude;
                    if (r < WellRadius && r > 2.3f) worstX = Mathf.Max(worstX, Mathf.Abs(l.x));
                });
                Check(minY > -0.2f && worstX <= LaneHalfWidth, $"DK5 walking along the lane's edge at x {x:+0.0;-0.0}: never lower than ship y {minY:0.00}, never off the grate over the well (|x| {worstX:0.00})");
            }
        }

        // In from the deck to the car's centre and back out, the virtual W, every frame read.
        private static IEnumerator WalkInOut(ShipParts ship)
        {
            HQPlayerController host = Host();
            foreach (bool inward in new[] { true, false })
            {
                Vector3 start = inward ? CabinPoint(ship, 6f, 0f, StandY) : CabinPoint(ship, 0f, 0f, StandY);
                Vector3 dir = inward ? -ship.DeckCabin.forward : ship.DeckCabin.forward;
                host.TeleportLocal(start, Quaternion.LookRotation(dir).eulerAngles.y);
                host.SetPitchForChecks(0f);
                yield return Wait(0.5f);
                int frames = 0, airborne = 0, stalls = 0;
                float worstDy = 0f, lastY = ShipY(ship, host.transform.position), stallFrom = Time.unscaledTime;
                Vector3 stallAt = host.transform.position;
                string worstAt = string.Empty;
                Func<bool> arrived = inward ? () => RadiusOf(ship, host.transform.position) < 0.4f : () => RadiusOf(ship, host.transform.position) > 5.8f;
                yield return WalkWhile(arrived, 6f, () =>
                {
                    frames++;
                    float y = ShipY(ship, host.transform.position);
                    if (!host.IsGrounded) airborne++;
                    if (Mathf.Abs(y - lastY) > worstDy) { worstDy = Mathf.Abs(y - lastY); worstAt = $"r {RadiusOf(ship, host.transform.position):0.00}"; }
                    lastY = y;
                    if (Time.unscaledTime - stallFrom >= 0.5f)
                    {
                        if (Vector3.Distance(host.transform.position, stallAt) < 0.2f) stalls++;
                        stallFrom = Time.unscaledTime; stallAt = host.transform.position;
                    }
                });
                string way = inward ? "in from the deck to the car's centre" : "out from the car's centre to the deck";
                Check(arrived(), $"DK2 walked {way} ({frames} frames, r now {RadiusOf(ship, host.transform.position):0.00})");
                Check(airborne <= 2, $"DK2 {way}: on the ground the whole way ({airborne} airborne frames)");
                Check(worstDy < 0.02f, $"DK2 {way}: no step (largest height change in a frame {worstDy * 100f:0.0} cm at {worstAt})");
                Check(stalls == 0, $"DK2 {way}: never held up ({stalls} half-seconds under 0.2 m)");
                if (inward) Check(ship.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "DK2 in the deck cabin at the centre");
                else Check(!ship.IsInDeckCabin(host.transform.position + Vector3.up * 0.5f), "DK2 out of the deck cabin on the deck");
            }
        }

        // Face each bearing from the centre, walk into the wall, and read the camera's clearance against the glass.
        private static IEnumerator CameraAgainstGlass(ShipParts ship)
        {
            HQPlayerController host = Host();
            PlayerCameraClearance clearance = host.CameraClearance;
            Check(clearance != null, "DK9 the player has its camera clearance");
            float worstGap = float.MaxValue; string worstAt = string.Empty;
            for (float b = 30f; b < 360f; b += 30f)
                foreach (float pitch in new[] { 0f, -30f })
                {
                    host.TeleportLocal(CabinPoint(ship, 0f, 0f, StandY), Quaternion.LookRotation(CabinPoint(ship, 3f, b, StandY) - CabinPoint(ship, 0f, 0f, StandY)).eulerAngles.y);
                    host.SetPitchForChecks(pitch);
                    yield return Wait(0.25f);
                    yield return WalkWhile(() => false, 1.5f, null);
                    yield return Wait(0.2f);
                    Camera cam = host.PlayerCamera;
                    float env = clearance.LastEnvelopeRadius;
                    float r = RadiusOf(ship, cam.transform.position);
                    float gap = GlassInnerRadius - (r + env);
                    bool clear = PlayerCameraClearance.IsClear(cam.transform.position, env, host.transform);
                    if (gap < worstGap) { worstGap = gap; worstAt = $"bearing {b:0} pitch {pitch:0}"; }
                    Check(gap > 0f && clear, $"DK9 pressed into the wall toward bearing {b:0}° (pitch {pitch:0}): the near plane stays {gap * 100f:0.0} cm inside the glass (eye r {r:0.000} + envelope {env:0.000}), clear={clear}");
                    if (pitch == 0f && (b == 90f || b == 180f)) { H.CaptureLocalCamera(Shots + $"dk9-glass-press-{b:0}.png"); Say($"capture {Shots}dk9-glass-press-{b:0}.png"); }
                }
            Say($"DK9 the smallest gap to the glass: {worstGap * 100f:0.0} cm ({worstAt})");
            // Into a post beside the doorway.
            Transform post = ship.DeckCabinCarGlass != null ? Named(ship.DeckCabinCarGlass, ElevatorLook.PostColliderPrefix + "1") : null;
            if (post != null)
            {
                Vector3 postPoint = post.GetComponent<Collider>() != null ? post.GetComponent<Collider>().bounds.center : post.position;
                Vector3 from = CabinPoint(ship, 0.8f, 0f, StandY);
                Vector3 flat = postPoint - from; flat.y = 0f;
                host.TeleportLocal(from, Quaternion.LookRotation(flat).eulerAngles.y);
                host.SetPitchForChecks(0f);
                yield return Wait(0.25f);
                yield return WalkWhile(() => false, 1.5f, null);
                yield return Wait(0.2f);
                Camera cam = host.PlayerCamera;
                bool clear = PlayerCameraClearance.IsClear(cam.transform.position, clearance.LastEnvelopeRadius, host.transform);
                Check(clear && !clearance.Obstructed, $"DK9 pressed into {post.name}: the camera stays clear (eye r {RadiusOf(ship, cam.transform.position):0.00}, obstructed={clearance.Obstructed})");
                H.CaptureLocalCamera(Shots + "dk9-glass-press-post.png");
            }
            else Check(false, "DK9 the car on the ship has its post colliders under DeckCabinCarGlass");
        }

        // The car's look on the deck and in the dive: the model, the panel and the button in the same cabin-frame pose.
        private static IEnumerator DeckVsCarLook(ShipParts ship, string when, ElevatorController car)
        {
            CabinFrame deck = CabinFrame.DeckCabin(ship);
            Transform deckLook = ship.DeckCabinCarGlass != null ? Named(ship.DeckCabinCarGlass, ElevatorLook.CarLookName) : null;
            Transform deckPanel = ship.DeckCabinCarGlass != null ? Named(ship.DeckCabinCarGlass, ElevatorLook.PanelLookName) : null;
            Transform deckButton = ship.DeckCabinButton;
            Check(deckLook != null && deckPanel != null && deckButton != null, $"DK8 ({when}) the deck cabin has the car's model, panel and button");
            int deckRenderers = deckLook.GetComponentsInChildren<MeshFilter>(true).Length;
            if (car == null) { Say($"DK8 deck car look: {deckRenderers} meshes; the panel at {deck.ToLocal(deckPanel.position):F3}"); yield break; }
            CabinFrame carFrame = CabinFrame.Car(car);
            Transform carLook = Named(car.transform, ElevatorLook.CarLookName), carPanel = Named(car.transform, ElevatorLook.PanelLookName), carButton = Named(car.transform, "Control Panel");
            Check(carLook != null && carPanel != null && carButton != null, "DK8 the dive car has the car's model, panel and button");
            void Same(string what, Transform a, Transform b)
            {
                Vector3 pa = deck.ToLocal(a.position), pb = carFrame.ToLocal(b.position);
                float ya = deck.ToYaw(a.eulerAngles.y), yb = carFrame.ToYaw(b.eulerAngles.y);
                Check(Vector3.Distance(pa, pb) < 0.02f && Mathf.Abs(Mathf.DeltaAngle(ya, yb)) < 1f, $"DK8 {what} in the same place in both cabins (deck {pa:F3} yaw {ya:0.0}, car {pb:F3} yaw {yb:0.0})");
            }
            Same("the car's model", deckLook, carLook);
            Same("the panel", deckPanel, carPanel);
            Same("the panel's button", deckButton, carButton);
            string Meshes(Transform t) => string.Join(",", t.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Select(f => f.sharedMesh.name).OrderBy(n => n));
            Soft(Meshes(deckLook) == Meshes(carLook),$"DK8 the same model meshes on the deck and in the dive ({Meshes(carLook)})");
            Check(Ground(deck.FromLocal(new Vector3(0.6f, 0.15f, 0.6f)), 1f, out RaycastHit dh) && Ground(carFrame.FromLocal(new Vector3(0.6f, 0.15f, 0.6f)), 1f, out RaycastHit ch)
                  && Mathf.Abs(deck.ToLocal(dh.point).y - carFrame.ToLocal(ch.point).y) < 0.005f, "DK8 the floor top at the same height in both cabin frames");
        }

        // Waits out a ride up while watching the ship's shutters: shut until the car is back, then
        // opening with the car's doors, never closing again, fully open within the seal time.
        private static IEnumerator ArrivalOpens(ShipParts ship, float half, string which)
        {
            float arrivingAt = -1f, last = 0f, worstBack = 0f, openAt = -1f;
            bool openedEarly = false;
            float deadline = Time.unscaledTime + 80f;
            while (Time.unscaledTime < deadline)
            {
                float f = YawFraction(ship.DeckCabinHousingDoorR, half);
                CabinRideState ride = Day.CabinRide;
                if (ride.Direction == RideDirection.Up && ride.Stage == CabinRideStage.Arriving && arrivingAt < 0f) arrivingAt = Time.unscaledTime;
                if (arrivingAt < 0f && f > 0.001f && ride.Direction == RideDirection.Up && ride.Stage < CabinRideStage.Loading) openedEarly = true;
                if (arrivingAt >= 0f)
                {
                    worstBack = Mathf.Max(worstBack, last - f);
                    if (openAt < 0f && f >= 0.999f) openAt = Time.unscaledTime - arrivingAt;
                }
                last = f;
                if (!ride.Active && ride.Stage == CabinRideStage.Complete && openAt >= 0f) break;
                yield return null;
            }
            Check(arrivingAt >= 0f && Day.CabinRide.Stage == CabinRideStage.Complete, $"DK4 {which} arrived");
            Check(!openedEarly, $"DK4 {which}: the shutters stayed shut until the car was back");
            Check(worstBack <= 0.001f, $"DK4 {which}: the shutters only opened, never closed again (worst step back {worstBack:0.0000})");
            Check(openAt >= 0f && openAt <= WorldSceneFlow.Instance.Settings.CabinSealSeconds + 0.3f, $"DK4 {which}: fully open {openAt:0.00} s after the arrival began (the seal time is {WorldSceneFlow.Instance.Settings.CabinSealSeconds:0.00} s)");
        }
    }
}
