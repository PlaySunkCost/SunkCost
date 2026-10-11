using FishNet.Connection;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The crew colour console on the server (Dan, 8 October 2026; docs/DESIGN.md "The
    // crew colour console"): a swatch press moves the one shared shown swatch (no
    // operator lock, last press wins, no same-frame rule); the lever gives the puller
    // the shown colour through PlayerIdentity.ServerSetColour — the same field the old
    // panel's request wrote — and announces the take for every peer's "<NAME> IS NOW
    // <COLOUR>" line. Taking a colour you already wear is accepted (the lever swings,
    // nothing changes). Location and liveness are the dispatcher's (ServerPullLever,
    // ServerPresserAtRig, ShipControls.ServerPresserAlive).
    public sealed partial class WorldSceneFlow
    {
        // E on a swatch: show it in the middle for everyone.
        public bool ServerPressSwatch(NetworkConnection sender, int index, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null) { why = "No day state."; return false; }
            if (!ServerPresserAtRig(sender, ConsoleKind.Colour, out why)) return false;
            if (!PlayerPalette.IsValid(index)) { why = ConsoleRules.NoSuchColour; return false; }
            dayState.ServerSetShownSwatch(index);
            Debug.Log($"[Console] {DisplayName(sender)} showed {PlayerPalette.Name(index)}");
            return true;
        }

        // The colour lever is always TAKE: nothing on the facts refuses it.
        internal LeverAction ServerResolveColourLever(out bool enabled, out string reason)
        {
            enabled = true; reason = string.Empty;
            return LeverAction.TakeColour;
        }

        // The pull: the puller wears the shown swatch, the take is announced.
        internal bool ServerTakeColour(NetworkConnection sender, out string why)
        {
            why = string.Empty;
            HQPlayerController presser = PlayerOf(sender);
            PlayerIdentity identity = presser != null ? presser.GetComponent<PlayerIdentity>() : null;
            if (identity == null) { why = "No identity"; return false; }
            int swatch = dayState.ShownSwatch;
            identity.ServerSetColour(swatch);
            dayState.ServerAnnounceColourTake(sender != null ? sender.ClientId : -1, swatch, networkManager.TimeManager.Tick);
            Debug.Log($"[Console] {DisplayName(sender)} took {PlayerPalette.Name(swatch)}");
            return true;
        }
    }
}
