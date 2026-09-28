using SunkCost.Look;
using UnityEngine;

namespace SunkCost.World
{
    // The board at the HQ intake (Dan, 16 September 2026): what the crew owes,
    // what it has, and the pay button. Aim at PAY and press E (HQPlayerController
    // -> ShipControls.RequestPay -> WorldSceneFlow.ServerPay): the storage room
    // on the docked ship is sold into the balance — all of it; the quota is a bar
    // the cycle's hand-over must clear, not a charge (Dan, 17 September 2026).
    // Paid: a new cycle. Short at payday: GAME LOST, everything from nothing. Purely a
    // display here; the server decides. Refusals and the pay report arrive
    // through CrewDayState like the monitor's.
    //
    // Drawn like the ship's monitor (Dan, 27 September 2026, "like in the photo"): on
    // the intake console's own screen, the board's name and the day over a rule, the
    // state large, a hint under it, the quota at the foot in amber (green when met)
    // and the balance. Two buttons on the desk: PAY (this object, the aim's target)
    // and GIVE UP, whose crew vote the hint shows ("GIVE UP 1/3").
    public sealed class QuotaBoard : MonoBehaviour
    {
        public const string BoardName = "Quota Board";
        private const float DisplaySeconds = 0.25f;

        [SerializeField] private TextMesh site, dayLine, status, hint, quota, balanceLine;
        [SerializeField] private Vector2 stateBox, hintBox;
        [SerializeField] private float stateSize, hintSize;
        private WorldLoopSettings settings;
        private float nextDisplay;
        private string shownDisplay;

        // The status as one line of text (the checks read it).
        public string Text { get; private set; } = string.Empty;

        public void Configure(TextMesh siteLine, TextMesh day, TextMesh state, TextMesh hintLine, TextMesh quotaLine, TextMesh balance,
            Vector2 stateFit, float stateCharacterSize, Vector2 hintFit, float hintCharacterSize)
        {
            site = siteLine; dayLine = day; status = state; hint = hintLine; quota = quotaLine; balanceLine = balance;
            stateBox = stateFit; stateSize = stateCharacterSize; hintBox = hintFit; hintSize = hintCharacterSize;
        }

        private void Awake()
        {
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
        }

        private void Update()
        {
            string text = Compose();
            bool changed = text != Text;
            Text = text;
            if (!changed && Time.unscaledTime < nextDisplay) return;
            ShowDisplay(changed);
        }

        // ---- the screen ----------------------------------------------------------------

        // What the builder shows in the editor: the idle screen.
        public void ShowIdle()
        {
            settings ??= WorldLoopSettings.Resolve(null);
            ShowDisplay(true);
        }

        private void ShowDisplay(bool force)
        {
            nextDisplay = Time.unscaledTime + DisplaySeconds;
            ComposeDisplay(out string day, out string state, out Color stateColour, out string next, out Color nextColour, out string money, out Color moneyColour, out string balance);
            string all = day + "|" + state + "|" + next + "|" + money + "|" + balance + "|" + nextColour;
            if (!force && all == shownDisplay) return;
            shownDisplay = all;
            Set(site, HQSigns.Resolve().Get("board.title"));
            Set(dayLine, day);
            Set(quota, money);
            if (quota != null) quota.color = moneyColour;
            Set(balanceLine, balance);
            Set(hint, next);
            if (hint != null) { hint.color = nextColour; if (hintSize > 0f) TextFit.Fit(hint, hintBox, hintSize); }
            Set(status, state);
            if (status != null) { status.color = stateColour; if (stateSize > 0f) TextFit.Fit(status, stateBox, stateSize); }
        }

        private static void Set(TextMesh mesh, string text)
        {
            if (mesh != null && mesh.text != text) mesh.text = text;
        }

        // What the screen shows: the same states as Text, as a layout.
        private void ComposeDisplay(out string dayText, out string state, out Color stateColour, out string next, out Color nextColour,
            out string money, out Color moneyColour, out string balance)
        {
            settings ??= WorldLoopSettings.Resolve(null);
            CrewDayState d = CrewDayState.Instance;
            dayText = string.Empty; next = string.Empty; money = string.Empty; balance = string.Empty;
            stateColour = ScreenStyle.Text; nextColour = ScreenStyle.Dim; moneyColour = ScreenStyle.Warn;
            if (d == null) { state = "STANDBY"; next = "Waiting for the crew"; return; }
            int quotaTotal = settings.QuotaPerCycle, had = d.CycleSales + d.BoxValue;
            money = $"QUOTA ${had} / ${quotaTotal}";
            moneyColour = had >= quotaTotal && quotaTotal > 0 ? ScreenStyle.Good : ScreenStyle.Warn;
            balance = $"BALANCE ${d.Balance}";
            dayText = d.Payday ? "PAYDAY" : d.Day > 0 ? $"DAY {d.Day}/{settings.DaysPerCycle}" : string.Empty;
            if (Time.unscaledTime - d.LastPayAt < settings.PayReportSeconds && d.LastPay.Serial != 0)
            {
                PayReport pay = d.LastPay;
                if (pay.Paid) { state = "PAID"; stateColour = ScreenStyle.Good; next = $"Handed over ${pay.Had} · every dollar is yours"; return; }
                if (pay.Short) { state = $"SHORT BY ${pay.Quota - pay.Had}"; stateColour = ScreenStyle.Warn; next = $"Sold ${pay.Sales} · sail out and dive again"; return; }
                state = "THE RUN IS OVER"; stateColour = ScreenStyle.Danger; next = $"Quota ${pay.Quota} missed · walk the plank"; nextColour = ScreenStyle.Danger; return;
            }
            if (d.Phase == DayPhase.Plank)
            {
                PlankState plank = d.Plank;
                state = "THE RUN IS OVER"; stateColour = ScreenStyle.Danger; nextColour = ScreenStyle.Danger;
                next = plank.Active && plank.Jumper >= 0 ? WorldSceneFlow.DisplayName(plank.Jumper) + " walks the plank" : "The last one is in the water";
                return;
            }
            if (Time.unscaledTime - d.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(d.LastRefusal.Text))
            {
                state = d.LastRefusal.Text; stateColour = ScreenStyle.Warn;
                return;
            }
            if (d.Payday) { state = "PAYDAY"; next = "PAY sells the box · short means the plank"; }
            else if (d.Day > 0) { state = $"DAY {d.Day} OF {settings.DaysPerCycle}"; dayText = string.Empty; next = "PAY early, or sail out and dive again"; }
            else { state = "NEW CYCLE"; next = "Sail to Site 01 and dive first"; }
            // A vote to give up in progress: the whole crew sees who is left to agree.
            if (d.GiveUpVotes > 0)
            {
                next = $"GIVE UP {d.GiveUpVotes}/{Mathf.Max(d.GiveUpCrew, d.GiveUpVotes)} · everyone must press";
                nextColour = ScreenStyle.Danger;
            }
        }

        // ---- the status line (the checks read it) -------------------------------------

        private string Compose()
        {
            settings ??= WorldLoopSettings.Resolve(null);
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return "QUOTA BOARD\n(no session)";
            if (Time.unscaledTime - day.LastPayAt < settings.PayReportSeconds && day.LastPay.Serial != 0)
            {
                PayReport pay = day.LastPay;
                // Every dollar handed over is the crew's (Dan, 17 September 2026): the
                // quota is the bar the cycle's hand-over cleared, not a charge.
                if (pay.Paid) return $"PAID ${pay.Quota}\nhanded over ${pay.Had} — every dollar yours · balance ${pay.Balance}\nnext dive is day 1";
                if (pay.Short) return $"SHORT BY ${pay.Quota - pay.Had}\nsold ${pay.Sales}, handed over ${pay.Had} · balance ${pay.Balance}\nsail out and dive again";
                return $"THE RUN IS OVER\nquota ${pay.Quota} missed (handed over ${pay.Had})\nwalk the plank";
            }
            // The plank (18 September 2026): the board keeps saying so until the fresh run.
            if (day.Phase == DayPhase.Plank)
            {
                PlankState plank = day.Plank;
                string who = plank.Active && plank.Jumper >= 0 ? WorldSceneFlow.DisplayName(plank.Jumper) + " walks the plank" : "the last one is in the water";
                return $"THE RUN IS OVER\n{who}\nthen everything from nothing";
            }
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
                return day.LastRefusal.Text;
            // The cycle so far: handed over at this board plus the box on the ship
            // (Dan, 18 September 2026: "quota x/500, x = already paid + what is in the box").
            string parts = day.CycleSales > 0 ? $" (handed over ${day.CycleSales} + box ${day.BoxValue})" : string.Empty;
            string money = $"quota ${day.CycleSales + day.BoxValue} / ${settings.QuotaPerCycle}{parts} · balance ${day.Balance}";
            string vote = day.GiveUpVotes > 0 ? $"\ngive up {day.GiveUpVotes}/{Mathf.Max(day.GiveUpCrew, day.GiveUpVotes)}" : string.Empty;
            if (day.Payday) return $"PAYDAY\n{money}\nE to pay (sells the box){vote}";
            if (day.Day > 0) return $"DAY {day.Day} OF {settings.DaysPerCycle}\n{money}\nE to pay early (sells the box){vote}";
            return $"NEW CYCLE\n{money}\ndive first{vote}";
        }
    }
}
