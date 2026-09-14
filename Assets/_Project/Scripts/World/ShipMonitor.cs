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
            switch (day.Phase)
            {
                case DayPhase.Sailing: return "Sailing to Site 01…";
                case DayPhase.SailingHome: return "Sailing home…";
                case DayPhase.AtSea: return "At Site 01 — E on HQ to sail home";
                case DayPhase.DiveInProgress: return "Dive in progress — monitor locked";
                default: return "Docked at HQ — E on Site 01 to sail";
            }
        }
    }
}
