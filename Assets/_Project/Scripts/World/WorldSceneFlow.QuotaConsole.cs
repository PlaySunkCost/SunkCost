using FishNet.Connection;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The HQ quota console on the server (the shared console, 27 September 2026;
    // BRIEF "HQ LEVER — PAY"): where a PAY pull must come from, and the lever's
    // current action. The action is resolved with ConsoleRules.HQLever over the
    // server's own facts (ServerFacts: the day state, the transition flag, the room's
    // worth summed now) — the same conditions ServerPay refuses with, in its order
    // (travelling, not docked at HQ, nothing to pay yet), plus the run being over and
    // an empty room before payday — so the sign every peer draws from the replicated
    // facts is what the server will accept. PAY itself is the existing ServerPay
    // (WorldSceneFlow.Cabin: sells the storage room, banks it, judges PAID / SHORT BY /
    // THE RUN IS OVER and starts the plank); GIVE UP is the existing ServerToggleGiveUp
    // (WorldSceneFlow.GiveUp). No quota maths lives here. The lever's collider is the
    // HQ rig's own (ConsoleRig.InScene) once the console stands at the intake; the old
    // board's object by name is the fallback until every scene is rebuilt.
    public sealed partial class WorldSceneFlow
    {
        // == ConsoleRig.HQLeverName == QuotaBoard.BoardName: the HQ lever's control object.
        public const string HQLeverObjectName = "Quota Board";

        private Collider hqLeverCollider; // found once per HQ scene load; a destroyed one (a rebuild) is looked up again

        // The presser's object in the HQ scene, its eyes within InteractReach + the
        // margin of the lever's collider (the shop's reach rule, WorldSceneFlow.Shop).
        internal bool ServerPresserAtHQConsole(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            HQPlayerController presser = PlayerOf(sender);
            Scene hq = WorldScenes.Scene(WorldId.HQ);
            if (presser == null || !hq.IsValid() || !hq.isLoaded || presser.gameObject.scene != hq) { why = "Not at HQ"; return false; }
            Collider lever = ServerHQLeverCollider(hq);
            if (lever == null) { why = "No console at HQ"; return false; }
            Vector3 eyes = presser.EyePosition;
            if (Vector3.Distance(eyes, lever.ClosestPoint(eyes)) > presser.InteractReach + ConsoleReachMargin) { why = ConsoleRules.NotAtConsole; return false; }
            return true;
        }

        // The rig's own lever collider when the new console stands at HQ (ConsoleRig.InScene,
        // INTERFACES §7.3), else the old board's object by name; kept between pulls so the
        // reach test does not walk the HQ scene per press (the netcode review's m5).
        private Collider ServerHQLeverCollider(Scene hq)
        {
            if (hqLeverCollider != null && hqLeverCollider.gameObject.scene == hq) return hqLeverCollider;
            ConsoleRig rig = ConsoleRig.InScene(hq, ConsoleKind.HQ);
            hqLeverCollider = rig != null && rig.LeverCollider != null ? rig.LeverCollider : ServerFindHQLeverByName(hq);
            return hqLeverCollider;
        }

        private static Collider ServerFindHQLeverByName(Scene hq)
        {
            foreach (GameObject root in hq.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != HQLeverObjectName) continue;
                    Collider c = t.GetComponent<Collider>();
                    if (c == null) c = t.GetComponentInChildren<Collider>(true);
                    if (c != null) return c;
                }
            return null;
        }

        // The lever's current action at HQ, from the server's facts (pure).
        internal LeverAction ServerResolveHQLever(out bool enabled, out string reason)
            => ConsoleRules.HQLever(ServerFacts(), out enabled, out reason);
    }
}
