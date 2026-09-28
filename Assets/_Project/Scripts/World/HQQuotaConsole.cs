using SunkCost.Look;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // What the quota board shows beyond the lever's facts (plain data, no FishNet type:
    // the composition below is tested without a session, HQQuotaConsoleTests).
    public struct QuotaView
    {
        public PayReport Pay; public bool PayShowing;      // the last pay report while it is on display (WorldLoopSettings.payReportSeconds)
        public string Refusal;                              // the refusal on display (refusalDisplaySeconds), "" when none
        public bool RefusalAfterPay;                        // that refusal arrived after the pay report: it takes the screen for its window, then the report returns
        public bool OnPlank; public string PlankWalker;     // the run is over: who is on the board now ("" between turns, or the last one is in the water)
        public int Crew;                                    // the crew the vote needs: the n of "v / n"
        public bool LocalVoted;                             // this peer's own vote is in
        public LeverAction Lever; public bool LeverEnabled; public string LeverReason; // the HQ lever as ConsoleRules.HQLever resolved it
        public bool LeverAimed, CardAimed;                  // LOCAL presentation only: where this player's dot rests

        public static QuotaView Empty => new() { Refusal = string.Empty, PlankWalker = string.Empty, LeverReason = string.Empty };
    }

    // HQ's quota console composer (Dan, 27 September 2026; BRIEF "COPY 2 — HQ QUOTA
    // CONSOLE"): the shared console standing at the intake, read as the quota board.
    // Every peer turns the replicated day state into the three content models the rig
    // draws — the top screen (QUOTA BOARD and the day or PAYDAY over a rule, the state
    // large: NEW CYCLE / DAY n OF N / PAYDAY / PAID / SHORT BY $n / THE RUN IS OVER, or
    // the refusal the server answered; a hint under it, the vote's count while one is
    // in; QUOTA amber until met then green; BALANCE), the red GIVE UP card on the
    // bottom panel (v / n of the connected crew, EVERYONE MUST AGREE — and for the
    // peer whose vote is in, YOU VOTED · E TO TAKE BACK; dark while voting is not
    // possible), and the PAY sign, lit or dim from the same rule table
    // (ConsoleRules.HQLever) the server resolves a pull with. A view only: the server
    // decides (WorldSceneFlow.ServerPay, ServerToggleGiveUp) and every peer, spectator
    // and joiner draws the same replicated facts. Text is the one-line status the
    // checks read: the old QuotaBoard's words, verbatim. Sits on the HQ rig root
    // beside ConsoleRig (ConsoleBuilder.Place + HQPlatformBuilder.QuotaConsole);
    // ExecuteAlways so a reopened scene shows the idle board too.
    [ExecuteAlways]
    public sealed class HQQuotaConsole : MonoBehaviour, IConsoleComposer
    {
        public const float RefreshSeconds = 0.25f;
        public const string NoSessionText = "QUOTA BOARD\n(no session)";

        // The hints under the state (the old board's words; the vote's line is BRIEF's).
        public const string HintNewCycle = "Sail to Site 01 and dive first";
        public const string HintDay = "PAY early, or sail out and dive again";
        public const string HintDayNothingToSell = "Nothing to sell yet · sail out and dive again";
        public const string HintPayday = "PAY sells the box · short means the plank";
        public const string HintPlankWater = "The last one is in the water";
        public static string HintVote(int votes, int crew) => $"GIVE UP {votes}/{crew} · EVERYONE MUST PRESS";

        private ConsoleRig rig;
        private ConsoleLever lever;
        private WorldLoopSettings settings;
        private readonly TopModel top = new();
        private readonly BottomModel bottom = new() { VoteCard = new VoteCardModel(), Status = ConsoleLine.Empty };
        private float nextRefresh;
        private ConsoleControl lastAim;
        private bool idleShown;

        public ConsoleKind Kind => ConsoleKind.HQ;
        public string Text { get; private set; } = string.Empty;   // the one-line compat status (QuotaBoard.Text's words)
        public TopModel Top { get; private set; }
        public BottomModel Bottom { get; private set; }
        public SignModel Sign { get; private set; }
        public int LeverPlayedSerial => Lever != null ? Lever.PlayedSerial : -1;
        public float LeverAngle => Lever != null ? Lever.Angle : 0f;

        private ConsoleRig Rig => rig != null ? rig : rig = GetComponent<ConsoleRig>();
        private ConsoleLever Lever => lever != null ? lever : lever = GetComponent<ConsoleLever>();

        private void Awake()
        {
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // The editor's scene reads right after a reload: the surfaces lose their
            // textures with the domain, and only a Show paints them again. After the
            // load, not during it.
            if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += () => { if (this != null && !Application.isPlaying) ShowIdle(); };
#endif
        }

        private void Start()
        {
            if (Application.isPlaying && CrewDayState.Instance == null) ShowIdle();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            CrewDayState day = CrewDayState.Instance;
            if (day == null)
            {
                // No session on this peer yet: the idle board, once.
                if (!idleShown) ShowIdle();
                Text = NoSessionText;
                return;
            }
            idleShown = false;
            settings ??= WorldLoopSettings.Resolve(null);
            ConsoleFacts f = ConsoleFacts.From(day, settings, string.Empty);
            QuotaView v = View(day, false);
            string text = ComposeText(f, v);
            bool changed = text != Text;
            Text = text;
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            ConsoleControl aim = local != null ? local.CurrentConsoleControl : null;
            bool aimChanged = aim != lastAim;
            lastAim = aim;
            if (!changed && !aimChanged && Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Compose(f, View(day, true));
        }

        // ---- the screens ---------------------------------------------------------------

        // What the builder shows in the editor, and a peer without a session: the idle board.
        public void ShowIdle()
        {
            idleShown = true;
            ConsoleRig.Idle(ConsoleKind.HQ, out TopModel idleTop, out BottomModel idleBottom, out SignModel idleSign);
            Top = idleTop; Bottom = idleBottom; Sign = idleSign;
            if (Rig != null) Rig.Show(idleTop, idleBottom, idleSign);
        }

        private void Compose(in ConsoleFacts f, in QuotaView v)
        {
            HQSigns signs = HQSigns.Resolve();
            ComposeTop(f, v, top, signs);
            ComposeBottom(f, v, bottom, signs);
            SignModel sign = ComposeSign(v, signs);
            Top = top; Bottom = bottom; Sign = sign;
            if (Rig != null) Rig.Show(top, bottom, sign);
        }

        // The view's facts from the day state: the report and refusal windows and the
        // plank every frame (Text needs them); the crew, this peer's vote, the lever and
        // the aim only for a recompose (`full`).
        private QuotaView View(CrewDayState day, bool full)
        {
            QuotaView v = QuotaView.Empty;
            v.Pay = day.LastPay;
            v.PayShowing = v.Pay.Serial != 0 && Time.unscaledTime - day.LastPayAt < settings.PayReportSeconds;
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text)) v.Refusal = day.LastRefusal.Text;
            // Both times are this peer's arrival times (CrewDayState); a refusal answered in the
            // pay's own frame arrives with it and leaves the report on (the same-frame PAY race).
            v.RefusalAfterPay = day.LastRefusalAt > day.LastPayAt;
            if (day.Phase == DayPhase.Plank)
            {
                v.OnPlank = true;
                PlankState plank = day.Plank;
                v.PlankWalker = plank.Active && plank.Jumper >= 0 ? WorldSceneFlow.DisplayName(plank.Jumper) : string.Empty;
            }
            if (!full) return v;
            // The crew the vote needs: while a vote is in, the server's own count for it
            // (CrewDayState.GiveUpCrew, written with the vote); before any vote, every
            // spawned player this peer sees — the same crew the server counts (every
            // active connection with a player, WorldSceneFlow.ServerCrewCount).
            v.Crew = day.GiveUpVotes > 0 ? Mathf.Max(day.GiveUpCrew, day.GiveUpVotes) : CrewEstimate();
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            v.LocalVoted = local != null && day.HasVotedGiveUp(local.OwnerId);
            v.Lever = ConsoleRules.Expected(ConsoleKind.HQ, null, out _, out v.LeverEnabled, out v.LeverReason);
            ConsoleControl aim = local != null ? local.CurrentConsoleControl : null;
            bool hq = aim != null && aim.Console == ConsoleKind.HQ;
            v.LeverAimed = hq && aim.Kind == ConsoleControlKind.Lever;
            v.CardAimed = hq && aim.Kind == ConsoleControlKind.GiveUpCard;
            return v;
        }

        // Every spawned player on this peer (the crew the vote needs before anyone voted).
        public static int CrewEstimate()
        {
            int n = 0;
            foreach (HQPlayerController p in Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
                if (p != null && p.IsSpawned) n++;
            return n;
        }

        // ---- the composition (pure) ----------------------------------------------------

        // The top screen: the board's name and the day, the state large, a hint, the
        // quota and the balance at the foot.
        public static void ComposeTop(in ConsoleFacts f, in QuotaView v, TopModel top, HQSigns signs)
        {
            top.Title = signs.Get("board.title");
            top.Cards = null;
            top.Corner = f.Payday ? "PAYDAY" : f.Day > 0 ? $"DAY {f.Day}/{f.DaysPerCycle}" : string.Empty;
            int had = f.CycleSales + f.BoxValue;
            top.FootLeft = $"QUOTA ${had} / ${f.QuotaPerCycle}";
            top.FootLeftTone = QuotaTone(had, f.QuotaPerCycle);
            top.FootRight = $"BALANCE ${f.Balance}";
            top.BigTone = ConsoleTone.Text;
            top.HintTone = ConsoleTone.Dim;
            if (ReportShows(v))
            {
                PayReport pay = v.Pay;
                if (pay.Paid) { top.BigState = "PAID"; top.BigTone = ConsoleTone.Good; top.Hint = $"Handed over ${pay.Had} · every dollar is yours"; return; }
                if (pay.Short) { top.BigState = $"SHORT BY ${pay.Quota - pay.Had}"; top.BigTone = ConsoleTone.Warn; top.Hint = $"Sold ${pay.Sales} · sail out and dive again"; return; }
                top.BigState = "THE RUN IS OVER"; top.BigTone = ConsoleTone.Danger; top.Hint = $"Quota ${pay.Quota} missed · walk the plank"; top.HintTone = ConsoleTone.Danger;
                return;
            }
            if (v.OnPlank)
            {
                top.BigState = "THE RUN IS OVER"; top.BigTone = ConsoleTone.Danger; top.HintTone = ConsoleTone.Danger;
                top.Hint = !string.IsNullOrEmpty(v.PlankWalker) ? v.PlankWalker + " walks the plank" : HintPlankWater;
                return;
            }
            if (!string.IsNullOrEmpty(v.Refusal))
            {
                // The reason the lever or the card did nothing, large, for a few seconds (BRIEF: "Screen should explain").
                top.BigState = v.Refusal; top.BigTone = ConsoleTone.Warn; top.Hint = string.Empty;
                return;
            }
            if (f.Payday) { top.BigState = "PAYDAY"; top.Hint = HintPayday; }
            else if (f.Day > 0)
            {
                top.BigState = $"DAY {f.Day} OF {f.DaysPerCycle}";
                top.Hint = v.Lever == LeverAction.None && v.LeverReason == ConsoleRules.NothingToSell ? HintDayNothingToSell : HintDay;
            }
            else { top.BigState = "NEW CYCLE"; top.Hint = HintNewCycle; }
            // A vote to give up in progress: the whole crew sees who is left to agree.
            if (f.GiveUpVotes > 0)
            {
                top.Hint = HintVote(f.GiveUpVotes, Mathf.Max(f.GiveUpCrew, f.GiveUpVotes));
                top.HintTone = ConsoleTone.Danger;
            }
        }

        // The bottom panel: the red GIVE UP card alone.
        public static void ComposeBottom(in ConsoleFacts f, in QuotaView v, BottomModel bottom, HQSigns signs)
        {
            bottom.Heading = string.Empty; bottom.Name = string.Empty; bottom.SubName = string.Empty;
            bottom.Picture = null; bottom.Lines = null; bottom.Status = ConsoleLine.Empty; bottom.Notice = false;
            VoteCardModel card = bottom.VoteCard ??= new VoteCardModel();
            card.Word = signs.Get("giveup.card");
            card.Count = $"{f.GiveUpVotes} / {v.Crew}";
            card.LocalVoted = v.LocalVoted;
            card.Foot = v.LocalVoted ? signs.Get("giveup.voted") : signs.Get("giveup.foot");
            card.Enabled = VotingOpen(f);
            card.Aimed = v.CardAimed;
        }

        // The PAY sign: the word stays readable while dim (the last word is PAY).
        public static SignModel ComposeSign(in QuotaView v, HQSigns signs)
            => ConsoleRules.Sign(v.Lever, v.LeverEnabled, 0, v.LeverAimed, signs.Get("lever.pay"));

        // The pay report holds the screen for its window, except while a refusal that came
        // after it is on display (BRIEF: a refused lever action shows the actual reason for
        // a few seconds): that refusal first, then the report again for what is left of it.
        public static bool ReportShows(in QuotaView v) => v.PayShowing && !(v.RefusalAfterPay && !string.IsNullOrEmpty(v.Refusal));

        // Voting is possible while docked at HQ with the run still on (WorldSceneFlow.ServerToggleGiveUp's rule).
        public static bool VotingOpen(in ConsoleFacts f) => f.Phase == DayPhase.AtHQ && f.World == WorldId.HQ && !f.Travelling;

        // Amber while short, green once the cycle's hand-over meets the quota.
        public static ConsoleTone QuotaTone(int had, int quota) => had >= quota && quota > 0 ? ConsoleTone.Good : ConsoleTone.Warn;

        // ---- the status line (the checks read it): QuotaBoard.Compose's words -----------

        public static string ComposeText(in ConsoleFacts f, in QuotaView v)
        {
            if (ReportShows(v))
            {
                PayReport pay = v.Pay;
                // Every dollar handed over is the crew's (Dan, 17 September 2026): the
                // quota is the bar the cycle's hand-over cleared, not a charge.
                if (pay.Paid) return $"PAID ${pay.Quota}\nhanded over ${pay.Had} — every dollar yours · balance ${pay.Balance}\nnext dive is day 1";
                if (pay.Short) return $"SHORT BY ${pay.Quota - pay.Had}\nsold ${pay.Sales}, handed over ${pay.Had} · balance ${pay.Balance}\nsail out and dive again";
                return $"THE RUN IS OVER\nquota ${pay.Quota} missed (handed over ${pay.Had})\nwalk the plank";
            }
            // The plank (18 September 2026): the board keeps saying so until the fresh run.
            if (v.OnPlank)
            {
                string who = !string.IsNullOrEmpty(v.PlankWalker) ? v.PlankWalker + " walks the plank" : "the last one is in the water";
                return $"THE RUN IS OVER\n{who}\nthen everything from nothing";
            }
            if (!string.IsNullOrEmpty(v.Refusal)) return v.Refusal;
            // The cycle so far: handed over at this board plus the box on the ship
            // (Dan, 18 September 2026: "quota x/500, x = already paid + what is in the box").
            string parts = f.CycleSales > 0 ? $" (handed over ${f.CycleSales} + box ${f.BoxValue})" : string.Empty;
            string money = $"quota ${f.CycleSales + f.BoxValue} / ${f.QuotaPerCycle}{parts} · balance ${f.Balance}";
            string vote = f.GiveUpVotes > 0 ? $"\ngive up {f.GiveUpVotes}/{Mathf.Max(f.GiveUpCrew, f.GiveUpVotes)}" : string.Empty;
            if (f.Payday) return $"PAYDAY\n{money}\nE to pay (sells the box){vote}";
            if (f.Day > 0) return $"DAY {f.Day} OF {f.DaysPerCycle}\n{money}\nE to pay early (sells the box){vote}";
            return $"NEW CYCLE\n{money}\ndive first{vote}";
        }

        public static HQQuotaConsole InHQ()
        {
            ConsoleRig r = ConsoleRig.InScene(WorldScenes.Scene(WorldId.HQ), ConsoleKind.HQ);
            return r != null ? r.GetComponent<HQQuotaConsole>() : null;
        }
    }
}
