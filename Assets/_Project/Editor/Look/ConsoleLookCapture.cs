using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using H = SunkCost.Editor.Prototype.HQPrototypeTestHooks;

namespace SunkCost.Editor.Look
{
    // The console polish pass's evidence (the shared console, 28 September 2026;
    // scratchpad console/BRIEF.md "FINAL VISUAL ACCEPTANCE TEST"): in Play Mode as the
    // host (`matrix host`), walks both consoles through every state the brief lists -
    // the ship's nothing selected, HQ / open / locked selected, short and unlock, a
    // refusal, sailing, HERE at sea, a dive in progress, END DAY, the locked card after
    // the dive, payday; HQ's new cycle, day, PAY, a refusal, GIVE UP 0/N, a vote in,
    // payday, PAID, SHORT BY, the run over - setting each through the real server paths
    // and the checks' hooks, and photographs each from where a player stands, with the
    // player's OWN camera (its FOV, post-processing and anti-aliasing) moved to the
    // spot: the full console from 1.9 m, the relevant screen close, and for the
    // blur study the top screen from 45 and 60 degrees to the side. Nothing here is a
    // check: it writes Temp/look/<prefix>-<name>.png and a log (Temp/console-look.log)
    // with each state's flattened screens, ending "LOOK_DONE" or "LOOK_FAIL: ...".
    // Start: ConsoleLookCapture.Run("final", "all") from the bridge ("ship", "hq", "blur").
    public static class ConsoleLookCapture
    {
        public const string Log = "Temp/console-look.log";
        private const int Width = 1920, HeightPx = 1080;

        private static readonly Stack<IEnumerator> stack = new();
        private static bool running;
        private static string prefix = "look";
        public static string Status { get; private set; } = "Not run";

        private static CrewDayState Day => CrewDayState.Instance;
        private static WorldSceneFlow Flow => WorldSceneFlow.Instance;
        private static HQPlayerController Host() => WorldSceneFlow.LocalPlayer();

        public static string Run(string filePrefix, string parts)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode as the host first (matrix host).";
            if (running) return "Already running.";
            prefix = string.IsNullOrEmpty(filePrefix) ? "look" : filePrefix;
            Directory.CreateDirectory("Temp/look");
            File.WriteAllText(Log, $"Console look capture '{prefix}' ({parts}) started {DateTime.Now}\n");
            stack.Clear();
            stack.Push(Sequence(parts ?? "all"));
            running = true;
            Status = "Running";
            EditorApplication.update += Tick;
            return "running; log " + Log;
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
                Status = "LOOK_DONE";
            }
            catch (Exception e) { Status = "LOOK_FAIL: " + e.Message + "\n" + e.StackTrace; }
            File.AppendAllText(Log, Status + "\n");
            WorldLoopSettings.QuotaOverrideForTests = null;
            stack.Clear();
            running = false;
            EditorApplication.update -= Tick;
        }

        private static void Say(string text) => File.AppendAllText(Log, "  " + text + "\n");
        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }
        private static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.unscaledTime + seconds;
            while (Time.unscaledTime < deadline && !condition()) yield return null;
            if (!condition()) throw new Exception("timed out: " + what + "\n" + H.FlowStatus() + "\n" + H.ConsoleStatus());
        }

        // ---- the camera: the local player's own, moved to the spot ----------------------

        private static IEnumerator StandAndLook(Vector3 feet, Vector3 lookAt)
        {
            HQPlayerController local = Host();
            H.ClientMoveLocalPlayerTo(feet);
            yield return null; yield return null;
            for (int i = 0; i < 3; i++)
            {
                Vector3 to = lookAt - local.EyePosition;
                Vector3 flat = new(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.0001f) local.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
                local.SetPitchForChecks(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg);
                yield return null;
            }
        }

        private static string Capture(string name)
        {
            Camera camera = Host() != null ? Host().PlayerCamera : null;
            if (camera == null) return "no camera";
            string path = $"Temp/look/{prefix}-{name}.png";
            var rt = new RenderTexture(Width, HeightPx, 24);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            bool wasEnabled = camera.enabled;
            camera.enabled = true;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previousTarget;
            camera.enabled = wasEnabled;
            RenderTexture.active = rt;
            var texture = new Texture2D(Width, HeightPx, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, HeightPx), 0, 0);
            texture.Apply();
            RenderTexture.active = previousActive;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            float eye = Host().EyePosition.y - Host().transform.position.y;
            return $"{path} (fov {camera.fieldOfView:0.#}, post {(data != null && data.renderPostProcessing)}, aa {(data != null ? data.antialiasing.ToString() : "?")}, eye {eye:0.00} m)";
        }

        // A console's frame: its root on the floor, the flat front, the right hand of the reader.
        private static void Frame(ConsoleRig rig, out Vector3 root, out Vector3 front, out Vector3 right)
        {
            root = rig.transform.position;
            front = rig.transform.rotation * Vector3.forward; front.y = 0f; front.Normalize();
            right = Vector3.Cross(Vector3.up, front); // the reader's right is the rig's -X... measured from the reader facing -front
        }

        // The standing shot (1.9 m from the root, the whole console) and a close one.
        private static IEnumerator Shots(ConsoleRig rig, string name, bool closeTop, bool closeBottom)
        {
            Frame(rig, out Vector3 root, out Vector3 front, out _);
            Say($"[{name}] top={rig.TopText}");
            Say($"[{name}] bottom={rig.BottomText}");
            Say($"[{name}] sign={rig.SignText}");
            yield return StandAndLook(root + front * 1.9f, root + Vector3.up * 1.15f);
            Say($"[{name}] stand: " + Capture(name + "-stand") + " dist=" + Vector3.Distance(Host().EyePosition, rig.TopScreen.position).ToString("0.00") + " m to the top screen");
            if (closeTop)
            {
                yield return StandAndLook(root + front * 1.25f, rig.TopScreen.position);
                Say($"[{name}] close top: " + Capture(name + "-top"));
            }
            if (closeBottom)
            {
                Vector3 mid = Vector3.Lerp(rig.BottomScreen.position, rig.Sign.position, 0.35f);
                yield return StandAndLook(root + front * 1.25f, mid);
                Say($"[{name}] close bottom: " + Capture(name + "-bottom"));
            }
        }

        // The top screen from the side (Dan: blurry from the side, not from the front).
        private static IEnumerator SideShots(ConsoleRig rig, string name)
        {
            Frame(rig, out Vector3 root, out Vector3 front, out _);
            Vector3 top = rig.TopScreen.position;
            foreach (float angle in new[] { 0f, 45f, 60f })
            {
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * front;
                Vector3 feet = new Vector3(top.x, root.y, top.z) + dir * 1.8f;
                yield return StandAndLook(feet, top);
                Say($"[{name}] side {angle}: " + Capture($"{name}-side{angle:0}"));
            }
        }

        // ---- the sequence ----------------------------------------------------------------

        private static IEnumerator Sequence(string parts)
        {
            HQPlayerController host = Host();
            if (host == null || !host.IsServerStarted) throw new Exception("the editor is not the host");
            bool all = parts == "all";
            Say("host " + WorldSceneFlow.DisplayName(host.OwnerId) + "; " + H.ConsoleStatus());
            if (all || parts.Contains("blur")) yield return Blur();
            if (all || parts.Contains("ship")) yield return Ship();
            if (all || parts.Contains("hq")) yield return HQ();
        }

        private static ConsoleRig ShipRig() => ConsoleRig.OnShip(ShipParts.InWorld(Day.World));
        private static ConsoleRig HQRig() => ConsoleRig.InScene(WorldScenes.Scene(WorldId.HQ), ConsoleKind.HQ);

        private static IEnumerator Select(SiteId site, float settle = 0.6f)
        {
            yield return StandAndLook(StandSpot(ShipRig(), 1.6f), ShipRig().TopScreen.position);
            Say("select " + site + ": " + H.ClientSelect(site.ToString()));
            yield return Until(() => Day.SelectedSite == site, 3f, "selected " + site);
            yield return Wait(settle);
        }

        private static Vector3 StandSpot(ConsoleRig rig, float distance)
        {
            Frame(rig, out Vector3 root, out Vector3 front, out _);
            return root + front * distance;
        }

        private static IEnumerator Blur()
        {
            if (Day.World != WorldId.HQ) yield break;
            ConsoleRig ship = ShipRig();
            if (ship == null) throw new Exception("no ship rig");
            yield return Select(SiteId.Site01);
            yield return SideShots(ship, "blur-ship");
            ConsoleRig hq = HQRig();
            if (hq != null) yield return SideShots(hq, "blur-hq");
            Day.ServerClearSelection();
            yield return Wait(0.5f);
        }

        private static IEnumerator Ship()
        {
            if (Day.World != WorldId.HQ || Day.Phase != DayPhase.AtHQ) throw new Exception("start the ship pass docked at HQ (" + Day.World + "/" + Day.Phase + ")");
            ConsoleRig rig = ShipRig();
            if (rig == null) throw new Exception("no ship rig");
            int host = Host().OwnerId;

            // Docked, nothing selected: HERE on HQ, the three locks, SELECT A DESTINATION, CONFIRM dim.
            Day.ServerClearSelection();
            yield return StandAndLook(StandSpot(rig, 1.6f), rig.TopScreen.position);
            yield return Wait(0.6f);
            yield return Shots(rig, "ship-01-nothing", true, true);

            // HQ selected: selected + HERE on one card, YOU ARE HERE, the lever dim.
            yield return Select(SiteId.HQ);
            yield return Shots(rig, "ship-02-hq-selected", true, true);

            // An open site: CONFIRM lit, READY.
            yield return Select(SiteId.Site01);
            yield return Shots(rig, "ship-03-confirm", true, true);

            // A locked site short of money: UNLOCK $100 dim, $40 SHORT.
            Day.ServerSetBalanceForChecks(60);
            yield return Select(SiteId.Site02);
            yield return Shots(rig, "ship-04-locked-short", true, true);

            // Enough money: UNLOCK $100 lit, PULL TO UNLOCK.
            Day.ServerSetBalanceForChecks(500);
            yield return Wait(0.6f);
            yield return Shots(rig, "ship-05-unlock", false, true);

            // A refusal: HQ selected, the lever pulled while dim -> the reason, prominent.
            yield return Select(SiteId.HQ, 0.3f);
            int refusal = Day.LastRefusal.Serial;
            Say("pull on the dim lever: " + H.ClientPullLever("Ship"));
            yield return Until(() => Day.LastRefusal.Serial > refusal, 3f, "the refusal");
            yield return Wait(0.4f);
            Frame(rig, out Vector3 root, out Vector3 front, out _);
            Say("[ship-06-refusal] bottom=" + rig.BottomText);
            yield return StandAndLook(root + front * 1.25f, Vector3.Lerp(rig.BottomScreen.position, rig.Sign.position, 0.35f));
            Say("[ship-06-refusal] close bottom: " + Capture("ship-06-refusal-bottom"));

            // Sailing to SITE 01: the pull, then the trip screens.
            yield return Wait(3.2f);
            yield return Select(SiteId.Site01);
            int trips = Day.Departure.Serial;
            Say("pull CONFIRM: " + H.ClientPullLever("Ship"));
            yield return Until(() => Day.Departure.Serial == trips + 1 && Day.Travelling, 5f, "the trip began");
            yield return Wait(2.5f);
            rig = ShipRig();
            Say("[ship-07-sailing] bottom=" + rig.BottomText + " sign=" + rig.SignText + " world=" + Day.World);
            Say("[ship-07-sailing] view: " + Capture("ship-07-sailing-view"));
            yield return Until(() => Day.World == WorldId.Sea && !Flow.Transitioning && Day.Phase == DayPhase.AtSea && !Host().TravelLocked, 90f, "arrived at sea");
            yield return Wait(1.0f);
            rig = ShipRig();
            if (rig == null) throw new Exception("no sea ship rig");

            // At sea: HERE on SITE 01, DAY 1/3, nothing selected.
            yield return StandAndLook(StandSpot(rig, 1.6f), rig.TopScreen.position);
            yield return Wait(0.6f);
            yield return Shots(rig, "ship-08-at-sea", true, true);

            // A dive in progress: DIVE IN PROGRESS / 1 BELOW, END DAY dim.
            Say("begin the day: " + Day.ServerBeginDay(out string why) + " " + why);
            Day.ServerSetBelow(host, true);
            yield return Wait(0.8f);
            yield return Shots(rig, "ship-09-dive", false, true);

            // Dive done: END DAY lit.
            Day.ServerSetBelow(host, false);
            yield return null;
            Say("dive done: " + Day.ServerEndDayIfDone(Flow.Settings.DaysPerCycle) + " done=" + Day.DiveDone);
            yield return Wait(0.8f);
            yield return Shots(rig, "ship-10-endday", false, true);
            // The fake diver called the car down; put it back at the top as a real ascent would
            // (the checks' CarUp), or the lever reads CABIN BELOW for the rest of the pass.
            for (int i = 0; i < 90 && Day.CabinAway; i++)
            {
                Day.ServerSetElevator(new ElevatorPhase { Serial = Day.Elevator.Serial + 1, State = SunkCost.Diving.ElevatorState.AtTop, Upward = true, StartTick = InstanceFinder.TimeManager.Tick, DurationTicks = 0 });
                yield return null; yield return null;
            }
            Say("the car: " + Day.Elevator.State + " away=" + Day.CabinAway);

            // A locked card after the dive: UNLOCK $200 instead of END DAY.
            yield return Select(SiteId.Site03);
            yield return Shots(rig, "ship-11-unlock-after-dive", true, true);

            // Payday at sea: only HQ.
            Day.ServerForceCycleForChecks(Flow.Settings.DaysPerCycle, true);
            yield return Select(SiteId.Site01);
            yield return Shots(rig, "ship-12-payday-site", true, true);

            // Home: HQ selected on payday and CONFIRM.
            yield return Select(SiteId.HQ);
            yield return Shots(rig, "ship-13-payday-hq", false, true);
            trips = Day.Departure.Serial;
            Say("pull CONFIRM home: " + H.ClientPullLever("Ship"));
            yield return Until(() => Day.Departure.Serial == trips + 1, 5f, "the trip home began");
            yield return Until(() => Day.World == WorldId.HQ && !Flow.Transitioning && Day.Phase == DayPhase.AtHQ && !Host().TravelLocked, 90f, "docked at HQ");
            yield return Wait(1.0f);
        }

        private static IEnumerator HQ()
        {
            if (Day.World != WorldId.HQ) throw new Exception("the HQ pass needs the ship docked");
            ConsoleRig rig = HQRig();
            if (rig == null) throw new Exception("no HQ rig");
            int host = Host().OwnerId;
            int days = Flow.Settings.DaysPerCycle;
            Frame(rig, out Vector3 root, out Vector3 front, out _);
            Vector3 atBoard = root + front * 1.6f; atBoard.y = 0.05f;

            // What the board shows now (payday after the ship pass, or a fresh run).
            yield return StandAndLook(atBoard, rig.TopScreen.position);
            yield return Wait(0.6f);
            if (Day.Payday) yield return Shots(rig, "hq-01-payday", true, true);

            // A normal day with nothing to sell: DAY 1 OF 3, PAY dim.
            Day.ServerForceCycleForChecks(1, false);
            WorldLoopSettings.QuotaOverrideForTests = null;
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-02-day-dim", true, true);

            // The dim lever pulled: the reason on the top screen.
            int refusal = Day.LastRefusal.Serial;
            yield return StandAndLook(atBoard, rig.TopScreen.position);
            Say("pull the dim PAY: " + H.ClientPullLever("HQ"));
            yield return Until(() => Day.LastRefusal.Serial > refusal, 3f, "the refusal");
            yield return Wait(0.4f);
            Say("[hq-03-refusal] top=" + rig.TopText);
            yield return StandAndLook(root + front * 1.9f, root + Vector3.up * 1.15f);
            Say("[hq-03-refusal] stand: " + Capture("hq-03-refusal-stand"));
            yield return Wait(3.2f);

            // A coin in the room: PAY lit on day 1; quota met -> PAID.
            CarryableItem coin = null;
            yield return CoinInRoom("Look coin 1", c => coin = c);
            WorldLoopSettings.QuotaOverrideForTests = coin.Value;
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-04-pay-lit", true, true);
            int pay = Day.LastPay.Serial;
            yield return StandAndLook(atBoard, rig.TopScreen.position);
            Say("pull PAY: " + H.ClientPullLever("HQ"));
            yield return Until(() => Day.LastPay.Serial == pay + 1, 3f, "the pay");
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-05-paid", true, false);

            // SHORT BY $50.
            Day.ServerForceCycleForChecks(2, false);
            yield return CoinInRoom("Look coin 2", c => coin = c);
            WorldLoopSettings.QuotaOverrideForTests = coin.Value + 50;
            yield return Wait(0.6f);
            pay = Day.LastPay.Serial;
            yield return StandAndLook(atBoard, rig.TopScreen.position);
            Say("pull PAY short: " + H.ClientPullLever("HQ"));
            yield return Until(() => Day.LastPay.Serial == pay + 1, 3f, "the short pay");
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-06-short", true, false);
            yield return Wait(Flow.Settings.PayReportSeconds);

            // GIVE UP 0/1, then a vote in with a crew of two (the server's own count, as a
            // guest would make it): 1 / 2, YOU VOTED, the top hint.
            yield return Shots(rig, "hq-07-giveup-0", false, true);
            Day.ServerToggleGiveUp(host, 2);
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-08-voted", true, true);
            Day.ServerClearGiveUp();
            yield return Wait(0.5f);

            // Payday lost: THE RUN IS OVER (the same screen the unanimous vote gives).
            Day.ServerForceCycleForChecks(days, true);
            WorldLoopSettings.QuotaOverrideForTests = 99999;
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-09-payday", true, true);
            pay = Day.LastPay.Serial;
            yield return StandAndLook(atBoard, rig.TopScreen.position);
            Say("pull PAY on payday, empty room: " + H.ClientPullLever("HQ"));
            yield return Until(() => Day.LastPay.Serial == pay + 1 && Day.Phase == DayPhase.Plank, 3f, "the run lost");
            yield return Wait(0.8f);
            yield return Shots(rig, "hq-10-runover", true, true);
            WorldLoopSettings.QuotaOverrideForTests = null;
        }

        // A CoinMedium spawned server side in the docked ship's storage room (HQConsoleRuntimeChecks' recipe).
        private static IEnumerator CoinInRoom(string name, Action<CarryableItem> got)
        {
            ShipParts ship = ShipParts.InWorld(WorldId.HQ);
            NetworkManager nm = InstanceFinder.NetworkManager;
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == "CoinMedium") { prefab = candidate; break; }
            }
            if (ship == null || prefab == null) throw new Exception("no docked ship or no CoinMedium prefab");
            Vector3 at = ship.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f));
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
            instance.name = name;
            CarryableItem item = instance.GetComponent<CarryableItem>();
            item.SetResetPositionBeforeSpawn(at);
            nm.ServerManager.Spawn(instance, null, ship.gameObject.scene);
            yield return Wait(1.2f);
            yield return Until(() => Day.BoxValue >= item.Value && item.Value > 0, 3f, name + " counted in the box");
            Say($"{name}: ${item.Value}, box ${Day.BoxValue}");
            got(item);
        }
    }
}
