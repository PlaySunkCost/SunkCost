using FishNet.Connection;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    // The HQ consoles of 8 October 2026 (Dan) go the same way: the colour console's
    // lever takes the shown swatch (ServerTakeColour), the shop console's turns the
    // page (ServerShopNextPage); both stand in the HQ scene and are reached through
    // ServerPresserAtRig, the quota console's reach rule generalised to a rig kind.
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
        // ServerPay itself refuses too (bugs/HQ-4). Indexed by (int)ConsoleKind.
        private readonly int[] lastPullFrame = { -1, -1, -1, -1 };

        // The HQ rigs' lever colliders by kind, found once per HQ scene load (the
        // netcode review's m5: no scene walk per press); a destroyed one is looked up again.
        private readonly Collider[] rigLeverColliders = new Collider[4];

        private static bool KnownKind(ConsoleKind kind) => kind == ConsoleKind.Ship || kind == ConsoleKind.HQ || kind == ConsoleKind.Colour || kind == ConsoleKind.Shop;

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
            // The kind check stays FIRST: a hand-made request with a kind outside the
            // enum must be refused, never index lastPullFrame (audit A10).
            if (!KnownKind(kind)) { why = "No such console"; return false; } // a hand-made request: no rig would swing for it
            bool located;
            switch (kind)
            {
                case ConsoleKind.Ship: located = ServerPresserAboard(sender, out why); break;
                case ConsoleKind.HQ: located = ServerPresserAtHQConsole(sender, out why); break;
                default: located = ServerPresserAtRig(sender, kind, out why); break;
            }
            if (!located) return false;
            int k = (int)kind;
            if (lastPullFrame[k] == Time.frameCount) { why = ConsoleRules.LeverInUse; return false; }
            SiteId target = SiteId.None;
            bool enabled;
            string reason;
            LeverAction current;
            switch (kind)
            {
                case ConsoleKind.Ship: current = ServerResolveShipLever(out target, out enabled, out reason); break;
                case ConsoleKind.Colour: current = ServerResolveColourLever(out enabled, out reason); break;
                case ConsoleKind.Shop: current = ServerResolveShopLever(out enabled, out reason); break;
                default: current = ServerResolveHQLever(out enabled, out reason); break;
            }
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
                case LeverAction.TakeColour: ok = ServerTakeColour(sender, out why); break;
                case LeverAction.NextPage: ok = ServerShopNextPage(sender, out why); break;
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

        // An HQ rig of `kind` (the colour or the shop console): the presser's object in
        // the HQ scene, its eyes within InteractReach + the margin of the rig's lever
        // collider (the quota console's rule, ServerPresserAtHQConsole, by kind).
        internal bool ServerPresserAtRig(NetworkConnection sender, ConsoleKind kind, out string why)
        {
            why = string.Empty;
            HQPlayerController presser = PlayerOf(sender);
            Scene hq = WorldScenes.Scene(WorldId.HQ);
            if (presser == null || !hq.IsValid() || !hq.isLoaded || presser.gameObject.scene != hq) { why = "Not at HQ"; return false; }
            Collider lever = ServerRigLeverCollider(hq, kind);
            if (lever == null) { why = "No console at HQ"; return false; }
            Vector3 eyes = presser.EyePosition;
            if (Vector3.Distance(eyes, lever.ClosestPoint(eyes)) > presser.InteractReach + ConsoleReachMargin) { why = ConsoleRules.NotAtConsole; return false; }
            return true;
        }

        // The rig of that kind in the HQ scene (ConsoleRig.InScene: one per kind per scene).
        internal ConsoleRig ServerRigInHQ(ConsoleKind kind)
        {
            Scene hq = WorldScenes.Scene(WorldId.HQ);
            return hq.IsValid() && hq.isLoaded ? ConsoleRig.InScene(hq, kind) : null;
        }

        private Collider ServerRigLeverCollider(Scene hq, ConsoleKind kind)
        {
            int k = (int)kind;
            if (k < 0 || k >= rigLeverColliders.Length) return null;
            Collider cached = rigLeverColliders[k];
            if (cached != null && cached.gameObject.scene == hq) return cached;
            ConsoleRig rig = ConsoleRig.InScene(hq, kind);
            rigLeverColliders[k] = rig != null ? rig.LeverCollider : null;
            return rigLeverColliders[k];
        }
    }
}
