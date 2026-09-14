using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace SunkCost.World
{
    // The player's side of the monitor: E on a MonitorButton asks the server to
    // sail. The server decides (WorldSceneFlow.ServerSail, contract section 3)
    // and writes a refusal to the day state so every monitor shows it. The ship
    // itself carries no NetworkObject; Idan's prefab stays plain.
    public sealed class ShipControls : NetworkBehaviour
    {
        public void RequestSail(WorldId to)
        {
            if (!IsOwner) return;
            ServerRequestSail(to);
        }

        [ServerRpc]
        private void ServerRequestSail(WorldId to, NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            // A press only counts from someone on the ship (the button is on the deck).
            ShipParts ship = ShipParts.InWorld(flow.CurrentWorld);
            if (ship != null && !ship.IsAboard(transform.position)) { day.ServerReportRefusal("Not aboard: " + WorldSceneFlow.DisplayName(sender)); return; }
            if (!flow.ServerSail(to, out string why)) day.ServerReportRefusal(why);
        }
    }
}
