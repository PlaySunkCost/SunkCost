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

        // The console screens' font-atlas gauge (the fixer's storm signal) and the pay report.
        public static string Gauge()
        {
            CrewDayState day = CrewDayState.Instance;
            string pay = day == null ? "-" : $"serial={day.LastPay.Serial} paid={day.LastPay.Paid} short={day.LastPay.Short} lost={day.LastPay.Lost} sales={day.LastPay.Sales} had={day.LastPay.Had} quota={day.LastPay.Quota}";
            return $"atlasRebuilds={ConsoleScreen.AtlasRebuilds} pay: {pay} phase={(day == null ? "-" : day.Phase.ToString())} day={(day == null ? -1 : day.Day)} payday={(day != null && day.Payday)} balance={(day == null ? -1 : day.Balance)}";
        }
    }
}
