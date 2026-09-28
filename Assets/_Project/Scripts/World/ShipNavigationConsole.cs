using System.Collections.Generic;
using SunkCost.Look;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The ship's navigation console composer (Dan, 27 September 2026; docs/DESIGN.md
    // "The navigation console"; BRIEF "COPY 1 — SHIP NAVIGATION CONSOLE"). On every
    // peer - host, guest, spectator - it turns the replicated day state into the
    // three content models the rig draws, four times a second or as soon as its
    // one-line status or the local player's aim changes: the NAVIGATION screen with
    // its five cards (the crew's selection, the locks, HERE), the bottom screen's
    // case (nothing selected, an open site, HQ, a locked site, a refusal, a trip, a
    // dive), and the lever's sign from the SAME rule table the server resolves a pull
    // with (ConsoleRules.ShipLever), so the word lit here is the word the server will
    // accept. Nothing is decided here and nothing is kept that the day state does not
    // hold; the only local input is which control this player's dot rests on
    // (presentation). Text is the one-line status the checks read: ShipMonitor's
    // lines, kept prefix for prefix (INTERFACES §12). Sits on the ship rig root beside
    // ConsoleRig; ExecuteAlways so a reopened scene shows the idle screens too.
    [ExecuteAlways]
    public sealed class ShipNavigationConsole : MonoBehaviour, IConsoleComposer
    {
        public const float RefreshSeconds = 0.25f;
        public const string SailHomeSub = "SAIL HOME";   // the HQ card's second line on the bottom screen (BRIEF "CASE: HQ selected")
        public const string NewCycle = "NEW CYCLE";      // the day before the first dive of a cycle (the quota board's word for it)

        private ConsoleRig rig;
        private ConsoleLever lever;
        private WorldLoopSettings settings;
        private SiteCatalog sites;
        private float nextRefresh;
        private ConsoleControl lastAim;
        private bool idleShown;

        // The models, made once and rewritten in place.
        private readonly CardModel[] cards = new CardModel[Destinations.Cards.Length];
        private readonly TopModel top = new();
        private readonly BottomModel bottom = new() { Status = ConsoleLine.Empty };
        private readonly List<ConsoleLine> lines = new(6);

        public ConsoleKind Kind => ConsoleKind.Ship;
        public string Text { get; private set; } = string.Empty;   // the one-line compat status (ShipMonitor's lines)
        public TopModel Top { get; private set; }
        public BottomModel Bottom { get; private set; }
        public SignModel Sign { get; private set; }
        public int LeverPlayedSerial => Lever != null ? Lever.PlayedSerial : -1;
        public float LeverAngle => Lever != null ? Lever.Angle : 0f;

        // The lever as this peer resolved it last (what the sign shows; the hooks read it).
        public LeverAction Action { get; private set; }
        public SiteId ActionTarget { get; private set; }
        public bool ActionEnabled { get; private set; }
        public string ActionReason { get; private set; } = string.Empty;

        private ConsoleRig Rig => rig != null ? rig : rig = GetComponent<ConsoleRig>();
        private ConsoleLever Lever => lever != null ? lever : lever = GetComponent<ConsoleLever>();

        private void OnEnable()
        {
            for (int i = 0; i < cards.Length; i++) cards[i] ??= new CardModel { Id = Destinations.Cards[i] };
            top.Cards = cards;
#if UNITY_EDITOR
            // The editor's scene and prefab read right after a reload: the surfaces lose
            // their textures with the domain, and only a Show paints them again. After
            // the load, not during it.
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
                // No session on this peer yet: the idle screens, once.
                if (!idleShown) ShowIdle();
                Text = string.Empty;
                return;
            }
            idleShown = false;
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(settings);
            sites ??= SiteCatalog.Resolve();
            string text = ComposeText(day);
            bool changed = text != Text;
            Text = text;
            ConsoleControl aim = LocalAim();
            bool aimChanged = aim != lastAim;
            lastAim = aim;
            if (!changed && !aimChanged && Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Compose(day, aim);
        }

        // ---- the screens ---------------------------------------------------------------

        // What the builder shows in the editor, and a peer without a session: NAVIGATION
        // with nothing selected, the sign dim.
        public void ShowIdle()
        {
            idleShown = true;
            ConsoleRig.Idle(ConsoleKind.Ship, out TopModel idleTop, out BottomModel idleBottom, out SignModel idleSign);
            Top = idleTop; Bottom = idleBottom; Sign = idleSign;
            if (Rig != null) Rig.Show(idleTop, idleBottom, idleSign);
        }

        private void Compose(CrewDayState day, ConsoleControl aim)
        {
            HQSigns signs = HQSigns.Resolve();
            ConsoleFacts f = ConsoleFacts.From(day, settings, ConsoleFacts.EstimateNotAboard(day));
            LeverAction action = ConsoleRules.ShipLever(f, sites, out SiteId target, out bool enabled, out string reason);
            Action = action; ActionTarget = target; ActionEnabled = enabled; ActionReason = reason;
            int price = action == LeverAction.Unlock ? sites.UnlockPrice(target) : 0;
            bool travelling = f.Travelling || f.Phase == DayPhase.Sailing || f.Phase == DayPhase.SailingHome;
            bool diving = f.Phase == DayPhase.DiveInProgress;
            SiteId here = travelling ? SiteId.None : HereSite(f);
            int had = f.CycleSales + f.BoxValue;
            bool met = had >= f.QuotaPerCycle && f.QuotaPerCycle > 0;
            string dayLine = f.Payday ? "PAYDAY" : f.Day > 0 ? $"DAY {f.Day} OF {f.DaysPerCycle}" : NewCycle;
            string quotaLine = $"QUOTA ${had} / ${f.QuotaPerCycle}";
            ConsoleTone quotaTone = met ? ConsoleTone.Good : ConsoleTone.Warn;

            // The top screen: NAVIGATION, the day or PAYDAY, the five equal cards.
            top.Title = signs.Get("nav.title");
            top.Corner = f.Payday ? "PAYDAY" : f.Day > 0 ? $"DAY {f.Day}/{f.DaysPerCycle}" : NewCycle;
            for (int i = 0; i < cards.Length; i++)
            {
                SiteId id = Destinations.Cards[i];
                CardModel c = cards[i];
                c.Id = id;
                c.Name = sites.NameOf(id);
                c.Picture = sites.PictureOf(id);
                c.Locked = Destinations.IsPurchasable(id) && !Destinations.IsOpen(id, f.UnlockedMask);
                c.Selected = f.Selected == id;
                c.Here = here == id;
                c.Aimed = aim != null && aim.Kind == ConsoleControlKind.Card && aim.Payload == id;
                c.Tag = c.Locked ? "$" + sites.UnlockPrice(id) : string.Empty;
            }
            top.BigState = string.Empty; top.BigTone = ConsoleTone.Dim;
            top.Hint = string.Empty; top.HintTone = ConsoleTone.Dim;
            top.FootLeft = quotaLine; top.FootLeftTone = quotaTone;
            top.FootRight = signs.Get("company");

            // The bottom screen: the case (BRIEF "BOTTOM SCREEN — SHIP"). The status line
            // is the rule table's own reading of the lever (READY, WAITING FOR X TO BOARD,
            // DIVE DONE — END THE DAY FIRST, PAYDAY — ONLY HQ, PULL TO UNLOCK, $40 SHORT,
            // SAILING TO SITE 02, n BELOW, THE RUN IS OVER ...), so the screen and the sign
            // never disagree.
            lines.Clear();
            bottom.VoteCard = null;
            bottom.Notice = false;
            bool locked = action == LeverAction.Unlock;
            SiteId shown = f.Selected != SiteId.None ? f.Selected : travelling ? f.SiteDestination : here;
            SiteCatalog.Entry entry = sites.Find(shown);
            if (f.Selected == SiteId.None)
            {
                // Nothing selected: SELECT A DESTINATION over the day and the quota - or,
                // while the ship sails or the crew dive, what it is doing and where.
                bool named = travelling || diving || action == LeverAction.EndDay;
                bottom.Heading = travelling ? signs.Get("nav.sailing") : diving ? signs.Get("nav.dive") : signs.Get("nav.select");
                bottom.Picture = named ? sites.PictureOf(shown) : null;
                bottom.Name = named ? sites.NameOf(shown) : string.Empty;
                bottom.SubName = named && travelling && shown == SiteId.HQ ? SailHomeSub : string.Empty;
                lines.Add(new ConsoleLine(dayLine, ConsoleTextSize.Medium, ConsoleTone.Text));
                lines.Add(new ConsoleLine(quotaLine, ConsoleTextSize.Medium, quotaTone));
            }
            else
            {
                bottom.Heading = travelling ? signs.Get("nav.sailing") : diving && !locked ? signs.Get("nav.dive") : signs.Get("nav.selected");
                bottom.Picture = sites.PictureOf(shown);
                bottom.Name = sites.NameOf(shown);
                bottom.SubName = shown == SiteId.HQ ? SailHomeSub : entry != null && entry.SubName != null ? entry.SubName : string.Empty;
                if (locked)
                {
                    // A locked site: its placeholder lines, the balance against the cost.
                    if (entry != null)
                        foreach (string line in entry.Description)
                            if (!string.IsNullOrEmpty(line)) lines.Add(new ConsoleLine(line, ConsoleTextSize.Small, ConsoleTone.Dim));
                    lines.Add(new ConsoleLine($"BALANCE ${f.Balance}", ConsoleTextSize.Medium, ConsoleTone.Text));
                    lines.Add(new ConsoleLine($"COST ${price}", ConsoleTextSize.Medium, ConsoleTone.Warn));
                }
                else
                {
                    lines.Add(new ConsoleLine(dayLine, ConsoleTextSize.Medium, ConsoleTone.Text));
                    lines.Add(new ConsoleLine(quotaLine, ConsoleTextSize.Medium, quotaTone));
                }
            }
            bottom.Status = ConsoleRules.ShipStatus(f, action, enabled, reason);
            // A refusal the server answered, shown prominently for a few seconds, then the
            // contextual screen again (BRIEF "CASE: Lever action refused"): the facts step
            // aside for the block (they overlapped it under the poster heading).
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
            {
                bottom.Notice = true;
                bottom.Status = new ConsoleLine(day.LastRefusal.Text.ToUpperInvariant(), ConsoleTextSize.Large, ConsoleTone.Warn);
                lines.Clear();
            }
            bottom.Lines = lines.ToArray();

            // The sign: the action's word, lit or dim; while the lever does nothing, a dim
            // word read from the replicated facts alone (DimWord), so every peer - a late
            // joiner, a peer whose ship scene reloaded - shows the same sign (bugs/NET-2).
            bool leverAimed = aim != null && aim.Kind == ConsoleControlKind.Lever;
            SignModel sign = ConsoleRules.Sign(action, enabled, price, leverAimed, DimWord(f, signs));

            Top = top; Bottom = bottom; Sign = sign;
            if (Rig != null) Rig.Show(top, bottom, sign);
        }

        // The dim sign's word while the lever does nothing, from the replicated facts
        // only (never from what this peer happened to show before: that history does not
        // replicate, so a late joiner and a fresh run disagreed with the rest - NET-2).
        // A None action never has a locked card selected (ShipLever reads UNLOCK first),
        // so the word is the lever's next job: END DAY while today's dive at sea is under
        // way or done and waiting for its divers, otherwise CONFIRM (the idle sign's word).
        private static string DimWord(in ConsoleFacts f, HQSigns signs)
        {
            bool dayToEnd = f.World == WorldId.Sea && (f.DiveDone || f.Phase == DayPhase.DiveInProgress);
            return signs.Get(dayToEnd ? "lever.endday" : "lever.confirm");
        }

        // HERE: the ship's place - HQ at the dock, the site it is at when at sea (the
        // world alone decides at sea for a run that never recorded a site).
        private static SiteId HereSite(in ConsoleFacts f)
        {
            if (f.World == WorldId.HQ) return SiteId.HQ;
            return Destinations.IsSite(f.CurrentSite) ? f.CurrentSite : SiteId.Site01;
        }

        // The control of THIS console the local player's dot rests on (the host may have
        // two ships loaded; the other rig's controls are not this one's).
        private ConsoleControl LocalAim()
        {
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            ConsoleControl aim = local != null ? local.CurrentConsoleControl : null;
            return aim != null && aim.Console == ConsoleKind.Ship && aim.transform.IsChildOf(transform) ? aim : null;
        }

        // ---- the status line (the checks read it) -------------------------------------

        // ShipMonitor.Compose's lines, kept prefix for prefix (the rows assert on
        // "Docked at HQ", "Docked at HQ — PAYDAY", "Docked at HQ — day 2 of 3", "Day 1 of 3",
        // "Day 1 of 3 — dive in progress", "Day 1 of 3 — dive done", "Day 1 of 3 — Site 01",
        // "PAYDAY", "Sailing " and a refusal as the whole line): the site is the logical one
        // the trip records, and the hints name the cards and the lever instead of the old buttons.
        private string ComposeText(CrewDayState day)
        {
            int days = settings.DaysPerCycle;
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
                return day.LastRefusal.Text;
            string dest = Destinations.TitleCase(day.SiteDestination), here = Destinations.TitleCase(day.CurrentSite);
            ShipDepartureState trip = day.Departure;
            if (trip.Active)
            {
                string where = trip.ToWorld == WorldId.HQ ? "home" : "to " + dest;
                switch (trip.Stage)
                {
                    case DepartureStage.Preparing: return "All aboard — hold on";
                    case DepartureStage.RaisingGangway: return "Casting off";
                    case DepartureStage.Arriving: return trip.ToWorld == WorldId.HQ ? "Docked — making fast" : "Arrived at " + dest;
                    default: return "Sailing " + where + "…";
                }
            }
            switch (day.Phase)
            {
                case DayPhase.Sailing: return "Sailing to " + dest + "…";
                case DayPhase.SailingHome: return "Sailing home…";
                case DayPhase.AtSea:
                    if (day.Payday) return "PAYDAY — select HQ and pull CONFIRM";
                    if (day.DiveDone) return $"Day {day.Day} of {days} — dive done — pull END DAY";
                    return $"Day {day.Day} of {days} — {here} — select HQ and pull CONFIRM";
                case DayPhase.DiveInProgress: return $"Day {day.Day} of {days} — dive in progress — console locked";
                default:
                    if (day.Payday) return "Docked at HQ — PAYDAY: pay the quota at the board";
                    return day.Day > 0 ? $"Docked at HQ — day {day.Day} of {days} — select a site and pull CONFIRM" : "Docked at HQ — select a site and pull CONFIRM";
            }
        }

        public static ShipNavigationConsole InWorld(WorldId world)
        {
            ConsoleRig r = ConsoleRig.OnShip(ShipParts.InWorld(world));
            return r != null ? r.GetComponent<ShipNavigationConsole>() : null;
        }
    }
}
