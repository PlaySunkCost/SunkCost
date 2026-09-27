using SunkCost.Look;
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
    // Drawn like the ship's screens (Dan, 27 September 2026): a title in the accent,
    // the money as the bold value line, the next step as the dim hint (ScreenStyle).
    public sealed class QuotaBoard : MonoBehaviour
    {
        public const string BoardName = "Quota Board";

        [SerializeField] private TextMesh label;   // the value line (the money)
        [SerializeField] private TextMesh title;   // NEW CYCLE, DAY 2 OF 3, PAYDAY, PAID, THE RUN IS OVER
        [SerializeField] private TextMesh hint;    // dive first, E to pay
        private WorldLoopSettings settings;

        public string Text { get; private set; } = string.Empty;

        public void Configure(TextMesh text) => label = text;
        public void Configure(TextMesh titleLine, TextMesh valueLine, TextMesh hintLine) { title = titleLine; label = valueLine; hint = hintLine; }

        private void Awake()
        {
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
        }

        private void Update()
        {
            string text = Compose(out Color money);
            if (text == Text) return;
            Text = text;
            // Three lines when there are three; a lone line (a refusal) takes the value slot.
            string[] lines = text.Split('\n');
            string first = lines.Length >= 3 ? lines[0] : lines.Length == 2 ? lines[0] : string.Empty;
            string middle = lines.Length >= 3 ? lines[1] : lines.Length == 2 ? lines[1] : text;
            string last = lines.Length >= 3 ? lines[lines.Length - 1] : string.Empty;
            if (title != null) { if (title.text != first) title.text = first; }
            else middle = string.IsNullOrEmpty(first) ? middle : first + "\n" + middle;
            if (hint != null) { if (hint.text != last) hint.text = last; }
            else if (!string.IsNullOrEmpty(last)) middle += "\n" + last;
            if (label != null) { label.text = middle; label.color = money; }
        }

        private string Compose(out Color money)
        {
            money = ScreenStyle.Text;
            CrewDayState day = CrewDayState.Instance;
            if (day == null) { money = ScreenStyle.Dim; return "QUOTA BOARD\n(no session)"; }
            if (Time.unscaledTime - day.LastPayAt < settings.PayReportSeconds && day.LastPay.Serial != 0)
            {
                PayReport pay = day.LastPay;
                // Every dollar handed over is the crew's (Dan, 17 September 2026): the
                // quota is the bar the cycle's hand-over cleared, not a charge.
                if (pay.Paid) { money = ScreenStyle.Good; return $"PAID ${pay.Quota}\nhanded over ${pay.Had} — every dollar yours · balance ${pay.Balance}\nnext dive is day 1"; }
                if (pay.Short) { money = ScreenStyle.Warn; return $"SHORT BY ${pay.Quota - pay.Had}\nsold ${pay.Sales}, handed over ${pay.Had} · balance ${pay.Balance}\nsail out and dive again"; }
                money = ScreenStyle.Danger; return $"THE RUN IS OVER\nquota ${pay.Quota} missed (handed over ${pay.Had})\nwalk the plank";
            }
            // The plank (18 September 2026): the board keeps saying so until the fresh run.
            if (day.Phase == DayPhase.Plank)
            {
                PlankState plank = day.Plank;
                string who = plank.Active && plank.Jumper >= 0 ? WorldSceneFlow.DisplayName(plank.Jumper) + " walks the plank" : "the last one is in the water";
                money = ScreenStyle.Danger; return $"THE RUN IS OVER\n{who}\nthen everything from nothing";
            }
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
            { money = ScreenStyle.Warn; return day.LastRefusal.Text; }
            // The cycle so far: handed over at this board plus the box on the ship
            // (Dan, 18 September 2026: "quota x/500, x = already paid + what is in the box").
            int sofar = day.CycleSales + day.BoxValue;
            string parts = day.CycleSales > 0 ? $" (handed over ${day.CycleSales} + box ${day.BoxValue})" : string.Empty;
            string moneyLine = $"quota ${sofar} / ${settings.QuotaPerCycle}{parts} · balance ${day.Balance}";
            money = sofar >= settings.QuotaPerCycle ? ScreenStyle.Good : sofar > 0 ? ScreenStyle.Warn : ScreenStyle.Text;
            if (day.Payday) return $"PAYDAY\n{moneyLine}\nE to pay (sells the box)";
            if (day.Day > 0) return $"DAY {day.Day} OF {settings.DaysPerCycle}\n{moneyLine}\nE to pay early (sells the box)";
            return $"NEW CYCLE\n{moneyLine}\ndive first";
        }
    }
}
