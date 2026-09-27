using SunkCost.Look;
using UnityEngine;

namespace SunkCost.World
{
    // The facts the lever is resolved from: a plain struct with no FishNet type in
    // it, filled from CrewDayState on the server and on every client alike. The
    // server resolves a pull with ConsoleRules over its own facts; every client draws
    // the sign, the prompt and the dot from the same table over the replicated
    // facts, so the sign can never disagree with what the server will accept (BRIEF:
    // "Do not allow visual state to disagree with gameplay state").
    public struct ConsoleFacts
    {
        public DayPhase Phase; public WorldId World; public bool Travelling, Riding, CabinAway;
        public int Day, DaysPerCycle; public bool Payday, DiveDone; public int BelowCount;
        public int Balance, CycleSales, BoxValue, QuotaPerCycle;
        public SiteId Selected, CurrentSite, SiteDestination; public int UnlockedMask;
        public string NotAboard;          // "" or "Dan, Idan" — server: ServerEveryoneAboard's names; client: the replicated-position estimate
        public int GiveUpVotes, GiveUpCrew;

        public static ConsoleFacts From(CrewDayState day, WorldLoopSettings settings, string notAboard)
        {
            settings = WorldLoopSettings.Resolve(settings);
            var f = new ConsoleFacts { DaysPerCycle = settings.DaysPerCycle, QuotaPerCycle = settings.QuotaPerCycle, NotAboard = notAboard ?? string.Empty, CurrentSite = SiteId.HQ, SiteDestination = SiteId.HQ };
            if (day == null) return f;
            f.Phase = day.Phase; f.World = day.World; f.Travelling = day.Travelling; f.Riding = day.Riding; f.CabinAway = day.CabinAway;
            f.Day = day.Day; f.Payday = day.Payday; f.DiveDone = day.DiveDone; f.BelowCount = day.Below.Count;
            f.Balance = day.Balance; f.CycleSales = day.CycleSales; f.BoxValue = day.BoxValue;
            f.Selected = day.SelectedSite; f.CurrentSite = day.CurrentSite; f.SiteDestination = day.SiteDestination; f.UnlockedMask = day.UnlockedSites;
            f.GiveUpVotes = day.GiveUpVotes; f.GiveUpCrew = day.GiveUpCrew;
            return f;
        }

        // Client-side estimate of who is not aboard: every spawned player that is not
        // safely aboard the ship of the crew's world — in that world's scene by position,
        // in another world's scene by being there at all (a spawn a client instantiated
        // into its session scene is judged by position). Presentation only; the server's
        // ServerEveryoneAboard decides, and a disagreement shows as the refusal.
        public static string EstimateNotAboard(CrewDayState day)
        {
            if (day == null) return string.Empty;
            ShipParts ship = ShipParts.InWorld(day.World);
            if (ship == null) return string.Empty;
            UnityEngine.SceneManagement.Scene world = WorldScenes.Scene(day.World);
            var missing = new System.Collections.Generic.List<string>();
            foreach (Player.HQPlayerController player in Object.FindObjectsByType<Player.HQPlayerController>(FindObjectsSortMode.None))
            {
                if (player == null || !player.IsSpawned) continue;
                UnityEngine.SceneManagement.Scene scene = player.gameObject.scene;
                bool elsewhere = scene != world && WorldScenes.TryParse(scene.name, out _);
                if (elsewhere || !ship.IsSafelyAboard(player.transform.position)) missing.Add(WorldSceneFlow.DisplayName(player.OwnerId));
            }
            missing.Sort(string.CompareOrdinal);
            return string.Join(", ", missing);
        }
    }

    public static class ConsoleRules
    {
        // The refusal / dim-reason texts (exact bytes; rows assert on them).
        public const string SelectFirst = "Select a destination";
        public const string RunOver = "The run is over";
        public const string Travelling = "Ship travelling";
        public const string DiveInProgress = "Dive in progress";
        public const string DiversBelow = "Divers below";
        public const string CabinInUse = "Cabin in use";
        public const string PaydayOnlyHQ = "Payday — only HQ";
        public const string PayQuotaFirst = "Pay the quota first";           // payday at the dock (CrewDayState.ServerCanSail's own words)
        public const string DiveDoneEndDay = "Dive done — End day first";
        public const string AlreadyHere = "Already here";
        public const string SailHomeFirst = "Sail home first";
        public const string NotDocked = "Not docked at HQ";
        public const string NothingToPay = "Nothing to pay yet — dive first";
        public const string NothingToSell = "Nothing to sell — dive again";     // docked with an empty room before payday: a PAY would sell nothing and report SHORT again
        public const string SelectionChanged = "Selection changed";
        public const string NotAtConsole = "Step up to the console";
        public const string AlreadyOpen = "Already open";
        public const string NoSuchDestination = "No such destination";
        public const string NobodyDived = "Nobody has dived today";           // CrewDayState.ServerEndDay's own words
        public const string LeverInUse = "Lever in use";                      // a second pull of the same console in the same frame
        public static string ShortBy(int dollars) => $"${dollars} short";
        public static string NotAboard(string names) => "Not aboard: " + names;      // the existing text (WorldSceneFlow.ServerEveryoneAboard)

        // The ship lever. Returns the CURRENT action; `target` is the card it acts on
        // (Confirm/Unlock) or None; `enabled` false = the sign is dim / the pull will be
        // refused with `reason`. The order is BRIEF "SHIP LEVER": a locked selection reads
        // UNLOCK before the dive/day rules (players may buy a site after a dive).
        public static LeverAction ShipLever(in ConsoleFacts f, SiteCatalog sites, out SiteId target, out bool enabled, out string reason)
        {
            target = SiteId.None; enabled = false; reason = string.Empty;
            if (f.Selected == SiteId.None) { reason = SelectFirst; return LeverAction.None; }
            if (f.Phase == DayPhase.Plank) { reason = RunOver; return LeverAction.None; }
            if (Destinations.IsPurchasable(f.Selected) && !Destinations.IsOpen(f.Selected, f.UnlockedMask))
            {
                target = f.Selected;
                int price = (sites ?? SiteCatalog.Resolve()).UnlockPrice(f.Selected);
                enabled = f.Balance >= price;
                reason = enabled ? string.Empty : ShortBy(price - f.Balance);
                return LeverAction.Unlock;
            }
            if (f.Travelling || f.Phase == DayPhase.Sailing || f.Phase == DayPhase.SailingHome) { reason = Travelling; return LeverAction.None; }
            if (f.Phase == DayPhase.DiveInProgress) { reason = DiveInProgress; return LeverAction.None; }
            if (f.BelowCount > 0 || f.CabinAway) { reason = DiversBelow; return LeverAction.None; }
            if (f.Riding) { reason = CabinInUse; return LeverAction.None; }
            if (f.DiveDone && f.World == WorldId.Sea) { enabled = true; return LeverAction.EndDay; }
            if (f.Payday && f.Selected != SiteId.HQ) { reason = f.World == WorldId.HQ ? PayQuotaFirst : PaydayOnlyHQ; return LeverAction.None; }
            if (f.Selected == f.CurrentSite && f.World == Destinations.WorldOf(f.Selected)) { reason = AlreadyHere; return LeverAction.None; }
            // Until sites have their own worlds every site is the one sea world, so a
            // site-to-site sail would be "Already there." — sites are chosen at HQ (MAP §3).
            if (f.World == WorldId.Sea && Destinations.IsSite(f.Selected)) { reason = SailHomeFirst; return LeverAction.None; }
            target = f.Selected;
            if (!string.IsNullOrEmpty(f.NotAboard)) { reason = NotAboard(f.NotAboard); return LeverAction.Confirm; }
            enabled = true;
            return LeverAction.Confirm;
        }

        // The HQ lever: PAY while docked with a cycle to pay for AND something to hand
        // over - the room's worth, or payday, when the empty room must still be judged
        // (the plank). An empty room before payday is dim: CrewDayState.ServerPay keeps
        // Day and Payday on a short sale, so without this row a second PAY after a short
        // sale would sell an empty room and report SHORT again with a new serial (the
        // netcode review's M1, 27 September 2026); the same facts dim the sign on every
        // peer and refuse the pull on the server.
        public static LeverAction HQLever(in ConsoleFacts f, out bool enabled, out string reason)
        {
            enabled = false; reason = string.Empty;
            if (f.Phase == DayPhase.Plank) { reason = RunOver; return LeverAction.None; }
            if (f.Travelling || f.Phase == DayPhase.Sailing || f.Phase == DayPhase.SailingHome) { reason = Travelling; return LeverAction.None; } // ServerPay's own first refusal (hq, 27 September 2026)
            if (f.Phase != DayPhase.AtHQ || f.World != WorldId.HQ) { reason = NotDocked; return LeverAction.None; }
            if (f.Day == 0 && !f.Payday) { reason = NothingToPay; return LeverAction.None; }
            if (!f.Payday && f.BoxValue <= 0) { reason = NothingToSell; return LeverAction.None; }
            enabled = true;
            return LeverAction.Pay;
        }

        // The sign's word and tone for an action (the words come from HQSigns so Dan can
        // change them): None → (lastWordOrEmpty, Dim); the price fills "UNLOCK ${0}".
        public static SignModel Sign(LeverAction action, bool enabled, int price, bool aimed) => Sign(action, enabled, price, aimed, string.Empty);
        public static SignModel Sign(LeverAction action, bool enabled, int price, bool aimed, string lastWord)
        {
            HQSigns signs = HQSigns.Resolve();
            var sign = new SignModel { Enabled = enabled && action != LeverAction.None, Aimed = aimed };
            switch (action)
            {
                case LeverAction.Confirm: sign.Word = signs.Get("lever.confirm"); sign.Tone = ConsoleTone.Accent; break;
                case LeverAction.Unlock: sign.Word = string.Format(signs.Get("lever.unlock"), price); sign.Tone = ConsoleTone.Warn; break;
                case LeverAction.EndDay: sign.Word = signs.Get("lever.endday"); sign.Tone = ConsoleTone.Danger; break;
                case LeverAction.Pay: sign.Word = signs.Get("lever.pay"); sign.Tone = ConsoleTone.Good; break;
                default: sign.Word = lastWord ?? string.Empty; sign.Tone = ConsoleTone.Dim; break;
            }
            if (!sign.Enabled) sign.Tone = ConsoleTone.Dim;
            return sign;
        }

        // The bottom screen's final status line for the ship (BRIEF "BOTTOM SCREEN"):
        // READY / WAITING FOR <NAMES> TO BOARD / DIVE DONE — END THE DAY FIRST / PAYDAY —
        // ONLY HQ / PULL TO UNLOCK / $40 SHORT / SAILING TO SITE 01 / SAIL HOME FIRST ...
        public static ConsoleLine ShipStatus(in ConsoleFacts f, LeverAction action, bool enabled, string reason)
        {
            switch (action)
            {
                case LeverAction.Confirm:
                    if (enabled) return new ConsoleLine("READY", ConsoleTextSize.Large, ConsoleTone.Good);
                    return new ConsoleLine("WAITING FOR " + (f.NotAboard ?? string.Empty).ToUpperInvariant() + " TO BOARD", ConsoleTextSize.Large, ConsoleTone.Warn);
                case LeverAction.Unlock:
                    return enabled ? new ConsoleLine("PULL TO UNLOCK", ConsoleTextSize.Large, ConsoleTone.Warn)
                                   : new ConsoleLine((reason ?? string.Empty).ToUpperInvariant(), ConsoleTextSize.Large, ConsoleTone.Warn);
                case LeverAction.EndDay:
                    return new ConsoleLine("DIVE DONE — END THE DAY FIRST", ConsoleTextSize.Large, ConsoleTone.Warn);
                case LeverAction.Pay:
                    return new ConsoleLine("PAY", ConsoleTextSize.Large, ConsoleTone.Good);
            }
            switch (reason)
            {
                case SelectFirst: return ConsoleLine.Empty;
                case Travelling:
                    if (f.SiteDestination == SiteId.HQ) return new ConsoleLine("SAILING HOME", ConsoleTextSize.Large, ConsoleTone.Accent);
                    return new ConsoleLine("SAILING TO " + Destinations.Label(f.SiteDestination), ConsoleTextSize.Large, ConsoleTone.Accent);
                case DiveInProgress: return new ConsoleLine($"{f.BelowCount} BELOW", ConsoleTextSize.Large, ConsoleTone.Warn);
                case RunOver: return new ConsoleLine("THE RUN IS OVER", ConsoleTextSize.Large, ConsoleTone.Danger);
                case AlreadyHere: return new ConsoleLine("YOU ARE HERE", ConsoleTextSize.Large, ConsoleTone.Dim);
                default: return new ConsoleLine((reason ?? string.Empty).ToUpperInvariant(), ConsoleTextSize.Large, ConsoleTone.Warn);
            }
        }

        // What the local client expects when it pulls (the same table over the local,
        // replicated facts): used by HQPlayerController's E branch, the hooks and the
        // peer. Returns the action even when the sign is dim — the server answers WHY.
        public static LeverAction Expected(ConsoleKind kind, SiteCatalog sites, out SiteId target)
        {
            target = SiteId.None;
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return LeverAction.None;
            WorldLoopSettings settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            ConsoleFacts f = ConsoleFacts.From(day, settings, kind == ConsoleKind.Ship ? ConsoleFacts.EstimateNotAboard(day) : string.Empty);
            if (kind == ConsoleKind.Ship) return ShipLever(f, sites ?? SiteCatalog.Resolve(), out target, out _, out _);
            return HQLever(f, out _, out _);
        }

        // The same, with the reason: what the prompt and the dot show for the aimed lever.
        public static LeverAction Expected(ConsoleKind kind, SiteCatalog sites, out SiteId target, out bool enabled, out string reason)
        {
            target = SiteId.None; enabled = false; reason = string.Empty;
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return LeverAction.None;
            WorldLoopSettings settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            ConsoleFacts f = ConsoleFacts.From(day, settings, kind == ConsoleKind.Ship ? ConsoleFacts.EstimateNotAboard(day) : string.Empty);
            if (kind == ConsoleKind.Ship) return ShipLever(f, sites ?? SiteCatalog.Resolve(), out target, out enabled, out reason);
            return HQLever(f, out enabled, out reason);
        }
    }
}
