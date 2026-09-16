using UnityEngine;

namespace SunkCost.World
{
    // The screen above the buttons, on both ship instances. Purely a display:
    // it reads the day state (phase, the last refusal) and writes the status
    // label every peer sees. The spectator card later doubles it as the TV.
    public sealed class ShipMonitor : MonoBehaviour
    {
        private TextMesh status;
        private WorldLoopSettings settings;

        public string Text { get; private set; } = string.Empty;

        private void Awake()
        {
            ShipParts ship = GetComponentInParent<ShipParts>();
            Transform label = ship != null ? ship.Find(ShipParts.MonitorStatusName) : null;
            status = label != null ? label.GetComponent<TextMesh>() : null;
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
        }

        private void Update()
        {
            string text = Compose();
            if (text == Text) return;
            Text = text;
            if (status != null) status.text = text;
        }

        private string Compose()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return string.Empty;
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
                return day.LastRefusal.Text;
            ShipDepartureState trip = day.Departure;
            if (trip.Active)
            {
                string where = trip.ToWorld == WorldId.HQ ? "home" : "to Site 01";
                switch (trip.Stage)
                {
                    case DepartureStage.Preparing: return "All aboard — hold on";
                    case DepartureStage.RaisingGangway: return "Raising the gangway";
                    case DepartureStage.Arriving: return trip.ToWorld == WorldId.HQ ? "Docked — lowering the gangway" : "Arrived at Site 01";
                    default: return "Sailing " + where + "…";
                }
            }
            switch (day.Phase)
            {
                case DayPhase.Sailing: return "Sailing to Site 01…";
                case DayPhase.SailingHome: return "Sailing home…";
                case DayPhase.AtSea:
                    return day.Payday ? "PAYDAY — E on HQ to sail home" : $"Day {day.Day} of {settings.DaysPerCycle} — Site 01 — E on HQ to sail home";
                case DayPhase.DiveInProgress: return $"Day {day.Day} of {settings.DaysPerCycle} — dive in progress — monitor locked";
                default:
                    if (day.Payday) return "Docked at HQ — PAYDAY: pay the quota at the board";
                    return day.Day > 0 ? $"Docked at HQ — day {day.Day} of {settings.DaysPerCycle} — E on Site 01 to sail" : "Docked at HQ — E on Site 01 to sail";
            }
        }
    }
}
