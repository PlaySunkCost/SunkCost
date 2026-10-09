using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SunkCost.Look;
using SunkCost.Player;
using SunkCost.Shop;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Look
{
    // The shop console at the depot's counter (Dan, 8 October 2026, BRIEF B.1): the
    // shared console model (ConsoleBuilder.Place, the navigation console's rig) on the
    // same steel footing the quota console stands on, with the catalogue's controls
    // on its top screen — three row cards "Shop Row 0..2" (ConsoleControl Shop/ShopRow,
    // Index = the row), the paging zones "Shop Prev" (Shop/ShopPage, Index -1) and
    // "Shop Next" (+1) — laid on the rectangles ShopConsolePaint draws them in, and
    // the HQShopConsole composer on its root. The lever's control object is
    // "Shop Lever" (ConsoleRig.CatalogueLeverName, named by ConsoleBuilder). `at` is
    // the footing's centre on the deck, `yaw` the console's facing (its front is +Z at
    // yaw 0), both in the parent group's frame.
    public static class ShopConsoleBuilder
    {
        public static ConsoleRig Place(GameObject parent, Vector3 at, float yaw)
        {
            HQPlatformBuilder.ConsoleFooting(parent, "Shop console footing", at, yaw);
            ConsoleRig rig = ConsoleBuilder.Place(parent.transform, at + Vector3.up * HQPlatformBuilder.FootingHeight, yaw, ConsoleKind.Shop);
            ConsoleStyle style = ConsoleStyle.Resolve();
            Material touch = ConsoleBuilder.TouchMaterial();
            ConsoleScreen top = rig.TopSurface;
            int w = top.Width, h = top.Height;
            Rect[] rows = ShopConsolePaint.RowRects(w, h, style);
            for (int r = 0; r < rows.Length; r++)
                ConsoleBuilder.Control(top, ConsoleRig.RowNamePrefix + r, rows[r], ConsoleKind.Shop, ConsoleControlKind.ShopRow, SiteId.None, touch, r);
            ConsoleBuilder.Control(top, ConsoleRig.PrevName, ShopConsolePaint.PrevRect(w, h, style), ConsoleKind.Shop, ConsoleControlKind.ShopPage, SiteId.None, touch, -1);
            ConsoleBuilder.Control(top, ConsoleRig.NextName, ShopConsolePaint.NextRect(w, h, style), ConsoleKind.Shop, ConsoleControlKind.ShopPage, SiteId.None, touch, +1);
            rig.gameObject.AddComponent<HQShopConsole>().ShowIdle();
            return rig;
        }

        // ---- the proof: the console's states from a player's eye -----------------------
        // In Play Mode with the host up (`matrix host`), `run SunkCost.Editor.Look.ShopConsoleBuilder.Prove`
        // walks the console through the states BRIEF C asks captures of — page 1 with an
        // empty pot (NO MONEY), page 1 with money and a row aimed (BUY), right after a buy,
        // a row OWNED, page 2, a refusal on the bottom screen, the side view, the counter
        // from a few steps back — through the test seams the matrix rows use, shooting
        // each to Temp/look/hq-consoles-shop-<state>.png, and writes what the composer
        // showed to Temp/look/hq-consoles-shop-proof.txt when it is done.

        public const string ProofPath = LookCapture.Folder + "/hq-consoles-shop-proof.txt";
        private const string Prefix = "hq-consoles-shop-";
        private static Stack<IEnumerator> steps;
        private static readonly StringBuilder proof = new();

        public static string Prove()
        {
            if (!Application.isPlaying) return "Enter Play Mode as host first (matrix host).";
            if (steps != null) return "already running";
            proof.Clear();
            Directory.CreateDirectory(LookCapture.Folder);
            File.Delete(ProofPath);
            steps = new Stack<IEnumerator>();
            steps.Push(ProofSteps());
            EditorApplication.update += Tick;
            return "started: " + ProofPath + " when done";
        }

        private static void Tick()
        {
            try
            {
                while (steps.Count > 0)
                {
                    IEnumerator e = steps.Peek();
                    if (e.MoveNext())
                    {
                        if (e.Current is IEnumerator inner) steps.Push(inner);
                        return;
                    }
                    steps.Pop();
                }
                proof.Append("DONE\n");
            }
            catch (Exception ex) { proof.Append("FAIL: ").Append(ex).Append('\n'); }
            EditorApplication.update -= Tick;
            steps = null;
            File.WriteAllText(ProofPath, proof.ToString());
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        private static void Say(string line) => proof.Append(line).Append('\n');

        private static void Shot(string state, Vector3 from, Vector3 at, float fov)
        {
            LookCapture.Shoot(Prefix + state, from, at, fov);
            Say($"capture {Prefix}{state}.png from {from:F2} at {at:F2}");
        }

        private static IEnumerator ProofSteps()
        {
            HQPlayerController host = WorldSceneFlow.LocalPlayer();
            CrewDayState day = CrewDayState.Instance;
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            Scene hq = WorldScenes.Scene(WorldId.HQ);
            ConsoleRig rig = ConsoleRig.InScene(hq, ConsoleKind.Shop);
            HQShopConsole c = rig != null ? rig.GetComponent<HQShopConsole>() : null;
            if (host == null || day == null || flow == null || rig == null || c == null)
            {
                Say($"missing: host={(host != null)} day={(day != null)} flow={(flow != null)} rig={(rig != null)} composer={(c != null)}");
                yield break;
            }
            Vector3 front = rig.transform.forward, right = rig.transform.right, up = Vector3.up;
            Vector3 spot = rig.transform.position + front * 1.6f; spot.y = 0.05f;
            float yaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;   // facing the console
            Vector3 eye = spot + up * 1.6f;
            Vector3 mid = rig.transform.position + up * 1.15f;
            Vector3 topAt = rig.transform.TransformPoint(ConsoleBuilder.TopCentre);
            Vector3 bottomAt = rig.transform.TransformPoint(ConsoleBuilder.BottomCentre);
            Say($"rig {rig.name} at {rig.transform.position:F2} front {front:F2}; spot {spot:F2}");

            // Page 1, an empty pot: every row NO MONEY.
            day.ServerSetBalanceForChecks(0);
            day.ServerSetShopPageForChecks(0);
            host.TeleportLocal(spot, yaw);
            yield return Wait(0.8f);
            Say("prompt: " + SunkCost.Editor.Prototype.HQPrototypeTestHooks.PromptText());
            Say("top: " + ConsoleModels.Flatten(c.Top));
            Say("bottom: " + ConsoleModels.Flatten(c.Bottom));
            Say("sign: " + ConsoleModels.Flatten(c.Sign) + " text: " + c.Text.Replace("\n", " | "));
            Shot("page1-nomoney", eye, mid, 60f);
            Shot("top-nomoney", spot + up * 1.6f - front * 0.6f, topAt, 45f);

            // Money in the pot, the first row aimed: BUY, the brackets.
            day.ServerSetBalanceForChecks(500);
            yield return Wait(0.4f);
            for (int i = 0; i < 90; i++)
            {
                SunkCost.Editor.Prototype.HQPrototypeTestHooks.ClientLookAtNamed(ConsoleRig.RowNamePrefix + "0");
                host.RefreshTarget();
                if (host.CurrentConsoleControl != null && host.CurrentConsoleControl.Kind == ConsoleControlKind.ShopRow && host.CurrentConsoleControl.Index == 0) break;
                yield return null;
            }
            yield return Wait(0.5f);
            Say("aimed row " + c.AimedRow + "; prompt: " + SunkCost.Editor.Prototype.HQPrototypeTestHooks.PromptText());
            Say("top: " + ConsoleModels.Flatten(c.Top));
            Shot("page1-buy-aimed", eye, mid, 60f);

            // A buy from the console by a real E on the aimed row (the player's own press
            // path: PressConsole → PlayerUpgrades.RequestBuy → ServerBuy), on a virtual
            // keyboard as the matrix rows press; the HUD browser must never open.
            // The editor hands a virtual device's events to the game only with the rows'
            // input settings (otherwise they wait for a focused Game view); restored after.
            InputSettings.EditorInputBehaviorInPlayMode savedBehaviour = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("ShopProveKeyboard");
            HQPlayerController.KeyboardForChecks = keyboard;
            HQPlayerController.BypassInputGateForChecks = true;
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                host.RefreshTarget(); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E)); yield return Wait(0.05f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                yield return Wait(1.0f);
                int browsers = UnityEngine.Object.FindObjectsByType<ShopBrowserUI>(FindObjectsInactive.Include).Length;
                Say($"E on row 0: balance {day.Balance} (460 = the air tank sold); refusal '{(host.Upgrades != null ? host.Upgrades.Refusal : "-")}'; ShopOpen={SunkCost.Net.SessionInputGate.ShopOpen}; ShopBrowserUI objects={browsers}");
            }
            finally
            {
                HQPlayerController.KeyboardForChecks = null;
                HQPlayerController.BypassInputGateForChecks = false;
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehaviour;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
            Say("top: " + ConsoleModels.Flatten(c.Top));
            Shot("after-buy", eye, mid, 60f);

            // A row OWNED: the large tank granted to the host.
            if (host.Upgrades != null) host.Upgrades.ServerGrant(PlayerUpgrade.LargeTank);
            yield return Wait(0.6f);
            Say("rows: " + c.RowsText);
            Shot("row-owned", eye, mid, 60f);

            // Page 2.
            day.ServerSetShopPageForChecks(1);
            yield return Wait(0.6f);
            Say("page " + c.Page + "/" + c.Pages + " top: " + ConsoleModels.Flatten(c.Top));
            Shot("page2", eye, mid, 60f);

            // A refusal on the bottom screen: the owner's own buy with an empty pot.
            day.ServerSetShopPageForChecks(0);
            day.ServerSetBalanceForChecks(0);
            if (host.Upgrades != null) host.Upgrades.RequestBuy(ShopCatalog.BrightHeadlampId);
            yield return Wait(0.9f);
            Say("refusal: '" + (host.Upgrades != null ? host.Upgrades.Refusal : "-") + "' bottom: " + ConsoleModels.Flatten(c.Bottom));
            Shot("refusal", eye, mid, 60f);
            Shot("bottom-refusal", spot + up * 1.6f + front * 0.3f, bottomAt, 50f);

            // The side view and the counter from a few steps back, the player stepped
            // aside so the console, its footing and the aisle read clean.
            Shot("side", rig.transform.position + front * 2.2f + right * 1.6f + up * 1.5f, rig.transform.position + up * 1.4f, 50f);
            host.TeleportLocal(spot + right * 4.5f, yaw);
            yield return Wait(0.5f);
            Shot("counter-back", spot + front * 3.5f + up * 1.65f, rig.transform.position + up * 1.2f, 60f);
            Shot("counter-aisle", spot + front * 4.5f - right * 2f + up * 1.65f, rig.transform.position + up * 1.1f, 60f);
            host.TeleportLocal(spot, yaw);
            Say($"end: balance {day.Balance} page {day.ShopPage}");
        }
    }
}
