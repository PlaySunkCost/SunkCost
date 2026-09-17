using UnityEngine;

namespace SunkCost.World
{
    // The board on the HQ wall (Dan, 16 September 2026): what the crew owes,
    // what it has, and the pay button. Look at it and press E (HQPlayerController
    // -> ShipControls.RequestPay -> WorldSceneFlow.ServerPay): the storage room
    // on the docked ship is sold into the balance — all of it; the quota is a bar
    // the cycle's hand-over must clear, not a charge (Dan, 17 September 2026).
    // Paid: a new cycle. Short at payday: GAME LOST, everything from nothing. Purely a
    // display here; the server decides. Refusals and the pay report arrive
    // through CrewDayState like the monitor's.
    public sealed class QuotaBoard : MonoBehaviour
    {
        public const string BoardName = "Quota Board";

        [SerializeField] private TextMesh label;
        private WorldLoopSettings settings;

        public string Text { get; private set; } = string.Empty;

        public void Configure(TextMesh text) => label = text;

        private void Awake()
        {
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
        }

        private void Update()
        {
            string text = Compose();
            if (text == Text) return;
            Text = text;
            if (label != null) label.text = text;
        }

        private string Compose()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return "QUOTA BOARD\n(no session)";
            if (Time.unscaledTime - day.LastPayAt < settings.PayReportSeconds && day.LastPay.Serial != 0)
            {
                PayReport pay = day.LastPay;
                // Every dollar handed over is the crew's (Dan, 17 September 2026): the
                // quota is the bar the cycle's hand-over cleared, not a charge.
                if (pay.Paid) return $"PAID ${pay.Quota}\nhanded over ${pay.Had} — every dollar yours · balance ${pay.Balance}\nnext dive is day 1";
                if (pay.Short) return $"SHORT BY ${pay.Quota - pay.Had}\nsold ${pay.Sales}, handed over ${pay.Had} · balance ${pay.Balance}\nsail out and dive again";
                return $"GAME LOST\nquota ${pay.Quota} missed (handed over ${pay.Had})\nnew run: day 1, $0";
            }
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
                return day.LastRefusal.Text;
            string handed = day.CycleSales > 0 ? $" (${day.CycleSales} handed over)" : string.Empty;
            string money = $"box ${day.BoxValue} / quota ${settings.QuotaPerCycle}{handed} · balance ${day.Balance}";
            if (day.Payday) return $"PAYDAY\n{money}\nE to pay (sells the box)";
            if (day.Day > 0) return $"DAY {day.Day} OF {settings.DaysPerCycle}\n{money}\nE to pay early (sells the box)";
            return $"NEW CYCLE\n{money}\ndive first";
        }
    }
}
