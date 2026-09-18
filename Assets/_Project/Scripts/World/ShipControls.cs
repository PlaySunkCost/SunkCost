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
        // Every press: the presser is alive (code check, 18 September 2026 — a
        // dead player parked on the deck could end the day or start a sail with a
        // hand-made request; E itself is off while dead). Range is the existing
        // coarse test per button — aboard, in the deck cabin, inside the car —
        // not a per-button reach; the contract records that.
        private bool ServerPresserAlive(NetworkConnection sender, CrewDayState day)
        {
            Player.HQPlayerController player = GetComponent<Player.HQPlayerController>();
            if (sender == null || (player != null && player.IsDead) || day.IsDead(sender.ClientId)) { day.ServerReportRefusal("The dead press nothing"); return false; }
            return true;
        }

        public void RequestSail(WorldId to)
        {
            if (!IsOwner) return;
            ServerRequestSail(to);
        }

        // E on the deck cabin's button: take everyone in the cabin down.
        public void RequestCabin()
        {
            if (!IsOwner) return;
            ServerRequestCabin();
        }

        // E on the monitor's End day.
        public void RequestEndDay()
        {
            if (!IsOwner) return;
            ServerRequestEndDay();
        }

        [ServerRpc]
        private void ServerRequestEndDay(NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            if (!ServerPresserAlive(sender, day)) return;
            ShipParts ship = ShipParts.InWorld(flow.CurrentWorld);
            if (ship != null && !ship.IsAboard(transform.position)) { day.ServerReportRefusal("Not aboard: " + WorldSceneFlow.DisplayName(sender)); return; }
            if (!flow.ServerEndDay(sender, out string why)) day.ServerReportRefusal(why);
        }

        // E on the HQ board: sell the storage room and pay the quota.
        public void RequestPay()
        {
            if (!IsOwner) return;
            ServerRequestPay();
        }

        [ServerRpc]
        private void ServerRequestPay(NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            if (!ServerPresserAlive(sender, day)) return;
            if (!flow.ServerPay(sender, out string why)) day.ServerReportRefusal(why);
        }

        // E on the deck TV's screen: the next channel (card 3).
        public void RequestTvNext()
        {
            if (!IsOwner) return;
            ServerRequestTvNext();
        }

        [ServerRpc]
        private void ServerRequestTvNext(NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            if (!ServerPresserAlive(sender, day)) return;
            ShipParts ship = ShipParts.InWorld(flow.CurrentWorld);
            if (ship != null && !ship.IsAboard(transform.position)) { day.ServerReportRefusal("Not aboard: " + WorldSceneFlow.DisplayName(sender)); return; }
            if (!flow.ServerTvNext(sender, out string why)) day.ServerReportRefusal(why);
        }

        // E on the seafloor car's panel: bring everyone in the car up.
        public void RequestCar()
        {
            if (!IsOwner) return;
            ServerRequestCar();
        }

        [ServerRpc]
        private void ServerRequestCabin(NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            if (!ServerPresserAlive(sender, day)) return;
            if (!flow.ServerRequestDive(sender, out string why)) day.ServerReportRefusal(why);
        }

        [ServerRpc]
        private void ServerRequestCar(NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            if (!ServerPresserAlive(sender, day)) return;
            if (!flow.ServerRequestSurface(sender, out string why)) day.ServerReportRefusal(why);
        }

        [ServerRpc]
        private void ServerRequestSail(WorldId to, NetworkConnection sender = null)
        {
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            CrewDayState day = CrewDayState.Instance;
            if (flow == null || day == null) return;
            if (!ServerPresserAlive(sender, day)) return;
            if (day.Travelling) { day.ServerReportRefusal("Ship travelling; try again on arrival"); return; }
            // A press only counts from someone on the ship (the button is on the deck).
            ShipParts ship = ShipParts.InWorld(flow.CurrentWorld);
            if (ship != null && !ship.IsAboard(transform.position)) { day.ServerReportRefusal("Not aboard: " + WorldSceneFlow.DisplayName(sender)); return; }
            if (!flow.ServerSail(to, out string why)) day.ServerReportRefusal(why);
        }
    }
}
