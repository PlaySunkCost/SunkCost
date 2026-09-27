using FishNet.Connection;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The HQ quota console on the server (the shared console, 27 September 2026):
    // where a PAY pull must come from, and the lever's current action. PAY itself is
    // the existing ServerPay (WorldSceneFlow.Cabin); GIVE UP is the existing
    // ServerToggleGiveUp (WorldSceneFlow.GiveUp). Nothing else lives here.
    // Foundation body by console_state: the lever is found by its object's name, which
    // the old board and the new rig share, so this works before and after the HQ
    // rebuild; the hq agent owns this file in the Implement phase and may look the
    // rig up through ConsoleRig.InScene instead.
    public sealed partial class WorldSceneFlow
    {
        // == ConsoleRig.HQLeverName == QuotaBoard.BoardName: the HQ lever's control object.
        public const string HQLeverObjectName = "Quota Board";

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

        private static Collider ServerHQLeverCollider(Scene hq)
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
