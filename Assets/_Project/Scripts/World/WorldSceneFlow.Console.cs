using FishNet.Connection;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The shared console's server dispatcher (Dan, 27 September 2026; docs/DESIGN.md
    // "The navigation console", docs/NETWORK_CONTRACT.md "The console"). A card press
    // is a selection; a pull carries the console the presser stands at and the action
    // and target its sign showed. The server checks the presser's location for that
    // console, resolves the CURRENT action from its own facts with the same pure
    // table every client draws the sign from (ConsoleRules), refuses when the
    // expectation no longer holds, and only then runs the EXISTING transaction —
    // ServerSail, ServerUnlockSite (ServerSpend), ServerEndDay, ServerPay — whose own
    // guards already refuse a second press. An accepted pull is announced once
    // (CrewDayState.ServerPullLever) for every rig's lever swing; a refusal is the
    // LastRefusal every screen shows and swings nothing.
    public sealed partial class WorldSceneFlow
    {
        public const float ConsoleReachMargin = 1.5f;   // the shop's margin: the server sees a copy a tick behind

        // The frame a pull of each console was last accepted on: two pulls of the same
        // lever in one frame (two clients' RPCs in one tick) run one transaction and
        // the second is answered "Lever in use" before the lever is even resolved - the
        // first accepted CONFIRM clears the selection synchronously, so a resolved
        // second pull would read "Select a destination" as the ship casts off (the
        // netcode review's m3). Across ticks the transactions refuse a repeat by
        // themselves (ServerSail's transitioning, the unlock bit, DiveDone) and the
        // rule table dims PAY over an empty room (ConsoleRules.NothingToSell), which
        // ServerPay itself refuses too (bugs/HQ-4).
        private readonly int[] lastPullFrame = { -1, -1 };

        // E on a destination card. Ship only (the HQ console has no cards).
        public bool ServerSelect(NetworkConnection sender, SiteId site, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            return ServerSelectSite(sender, site, out why);
        }

        // E on the lever.
        public bool ServerPullLever(NetworkConnection sender, ConsoleKind kind, LeverAction expected, SiteId expectedTarget, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (kind != ConsoleKind.Ship && kind != ConsoleKind.HQ) { why = "No such console"; return false; } // a hand-made request: no rig would swing for it
            bool located = kind == ConsoleKind.Ship ? ServerPresserAboard(sender, out why) : ServerPresserAtHQConsole(sender, out why);
            if (!located) return false;
            int k = (int)kind;
            if (lastPullFrame[k] == Time.frameCount) { why = ConsoleRules.LeverInUse; return false; }
            SiteId target = SiteId.None;
            bool enabled;
            string reason;
            LeverAction current = kind == ConsoleKind.Ship ? ServerResolveShipLever(out target, out enabled, out reason) : ServerResolveHQLever(out enabled, out reason);
            if (current != expected || target != expectedTarget)
            {
                // The sign the presser saw is not what the lever does now. Name the honest
                // reason where one transaction already has words for it.
                if (expected == LeverAction.Unlock && expectedTarget != SiteId.None && dayState.IsOpen(expectedTarget)) why = ConsoleRules.AlreadyOpen;
                else if (expected == LeverAction.EndDay && !dayState.DiveDone) why = ConsoleRules.NobodyDived;
                // The lever now acts on another card than the one the presser saw: the
                // card changed under the pull, whatever the new card's own state is (a
                // "$100 short" for a card the presser never chose would mislead).
                else if (target != SiteId.None && target != expectedTarget) why = ConsoleRules.SelectionChanged;
                else why = current == LeverAction.None || !enabled ? reason : ConsoleRules.SelectionChanged;
                Debug.Log($"[Console] {DisplayName(sender)} pulled {kind} expecting {expected}/{expectedTarget}, now {current}/{target}: {why}");
                return false;
            }
            if (!enabled) { why = reason; return false; }
            bool ok;
            switch (current)
            {
                case LeverAction.Confirm: ok = ServerConfirmSail(sender, target, out why); break;
                case LeverAction.Unlock: ok = ServerUnlockSite(sender, target, out why); break;
                case LeverAction.EndDay: ok = ServerEndDay(sender, out why); break;
                case LeverAction.Pay: ok = ServerPay(sender, out why); break;
                default: why = string.IsNullOrEmpty(reason) ? ConsoleRules.SelectFirst : reason; ok = false; break;
            }
            if (!ok) return false;
            lastPullFrame[k] = Time.frameCount;
            dayState.ServerPullLever(kind, current, networkManager.TimeManager.Tick);
            Debug.Log($"[Console] {DisplayName(sender)} pulled {kind}/{current}/{target}");
            return true;
        }

        // The server's facts for the rule table: the day state plus what only the
        // server knows — its own transition flag, the ride, who is not aboard, and the
        // room's worth summed now rather than the quarter-second-old replicated sum, so
        // an item dropped into the room a moment ago counts for PAY at once.
        internal ConsoleFacts ServerFacts()
        {
            string notAboard = string.Empty;
            ShipParts ship = ShipParts.InWorld(currentWorld);
            if (ship != null && dayState != null && !ServerEveryoneAboard(ship, out string missing))
                notAboard = missing.StartsWith("Not aboard: ") ? missing.Substring("Not aboard: ".Length) : missing;
            ConsoleFacts f = ConsoleFacts.From(dayState, Settings, notAboard);
            f.Travelling = f.Travelling || transitioning;
            f.Riding = f.Riding || riding;
            if (ship != null) f.BoxValue = StorageReadout.SumInside(ship);
            return f;
        }

        // The ship's console: the presser's object in the current world's scene and
        // aboard its ship (the coarse rule every ship press has; contract row "Presses in general").
        internal bool ServerPresserAboard(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            HQPlayerController presser = PlayerOf(sender);
            if (presser == null) { why = "No player."; return false; }
            ShipParts ship = ShipParts.InWorld(currentWorld);
            if (ship == null) { why = "No ship in " + WorldScenes.Name(currentWorld) + "."; return false; }
            if (presser.gameObject.scene != WorldScenes.Scene(currentWorld) || !ship.IsAboard(presser.transform.position)) { why = ConsoleRules.NotAboard(DisplayName(sender)); return false; }
            return true;
        }
    }
}
