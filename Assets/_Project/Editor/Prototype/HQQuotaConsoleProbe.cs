using FishNet;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Host-side setup for probing the HQ quota console from the editor bridge (27
    // September 2026): the bridge's command class cannot name a FishNet type, so
    // the day state is forced here, in editor code, exactly as the matrices do
    // (CrewDayState.ServerForceCycleForChecks, WorldLoopSettings.QuotaOverrideForTests).
    // Server only; nothing here is a hook the game uses.
    public static class HQQuotaConsoleProbe
    {
        // A cycle at its payday with the quota forced to `quota` (0: an empty room pays).
        public static string ForcePayday(int quota)
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || !day.IsServerStarted) return "(no server day state)";
            WorldLoopSettings.QuotaOverrideForTests = quota;
            day.ServerForceCycleForChecks(3, true);
            return $"forced payday: day={day.Day} payday={day.Payday} quota={WorldLoopSettings.QuotaOverrideForTests} balance={day.Balance} box={day.BoxValue}";
        }

        // A day in progress before payday (the box decides whether PAY is lit).
        public static string ForceDay(int dayValue)
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null || !day.IsServerStarted) return "(no server day state)";
            day.ServerForceCycleForChecks(dayValue, false);
            return $"forced day: day={day.Day} payday={day.Payday} box={day.BoxValue}";
        }

        public static string ClearQuotaOverride()
        {
            WorldLoopSettings.QuotaOverrideForTests = null;
            return "quota override cleared";
        }

        // A CoinMedium spawned server side in the docked ship's storage room (the console-hq
        // matrix's CoinInRoom, for the bridge).
        public static string SpawnCoinInRoom()
        {
            ShipParts ship = ShipParts.InWorld(WorldId.HQ);
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (ship == null || nm == null || !nm.IsServerStarted) return "(no docked ship or no server)";
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == "CoinMedium") { prefab = candidate; break; }
            }
            if (prefab == null) return "(no CoinMedium prefab)";
            Vector3 at = ship.FromShipLocal(new Vector3(3.2f, 0.3f, -12.5f));
            NetworkObject instance = Object.Instantiate(prefab, at, Quaternion.identity);
            instance.name = "Probe coin";
            instance.GetComponent<CarryableItem>().SetResetPositionBeforeSpawn(at);
            nm.ServerManager.Spawn(instance, null, ship.gameObject.scene);
            return "spawned a coin in the storage room";
        }

        // HQ-3: PAY through the server path, then the box sum in the SAME frame, with and
        // without the IsSpawned test (the sold coin is still in CarryableItem.Spawned on a
        // host until the client side's despawn reaches OnStopNetwork).
        public static string PayAndSampleSameFrame()
        {
            ShipParts ship = ShipParts.InWorld(WorldId.HQ);
            WorldSceneFlow flow = WorldSceneFlow.Instance;
            if (ship == null || flow == null || CrewDayState.Instance == null) return "(no ship, flow or day state)";
            bool ok = flow.ServerPay(InstanceFinder.ClientManager.Connection, out string why);
            int stale = 0, inSpawnedNotSpawned = 0;
            foreach (CarryableItem item in CarryableItem.Spawned)
            {
                if (item == null || !item.CanGrabFromWorld || item.gameObject.scene != ship.gameObject.scene || !ship.IsInStorageRoom(item.transform.position)) continue;
                stale += item.Value;
                if (!item.IsSpawned) inSpawnedNotSpawned++;
            }
            CrewDayState d = CrewDayState.Instance;
            return $"pay={ok} '{why}' report: sales={d.LastPay.Sales} had={d.LastPay.Had} quota={d.LastPay.Quota} | same frame: sum without IsSpawned=${stale} ({inSpawnedNotSpawned} despawned item(s) still in Spawned), SumInside=${StorageReadout.SumInside(ship)}, BoxValue=${d.BoxValue}, CycleSales=${d.CycleSales}";
        }

        // The console screens' font-atlas gauge (the fixer's storm signal) and the pay report.
        public static string Gauge()
        {
            CrewDayState day = CrewDayState.Instance;
            string pay = day == null ? "-" : $"serial={day.LastPay.Serial} paid={day.LastPay.Paid} short={day.LastPay.Short} lost={day.LastPay.Lost} sales={day.LastPay.Sales} had={day.LastPay.Had} quota={day.LastPay.Quota}";
            return $"atlasRebuilds={ConsoleScreen.AtlasRebuilds} pay: {pay} phase={(day == null ? "-" : day.Phase.ToString())} day={(day == null ? -1 : day.Day)} payday={(day != null && day.Payday)} balance={(day == null ? -1 : day.Balance)}";
        }
    }
}
