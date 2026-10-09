using SunkCost.Look;
using SunkCost.Player;
using SunkCost.Shop;
using UnityEngine;

namespace SunkCost.World
{
    // The shop console's composer (Dan, 8 October 2026, BRIEF B.1: "the shop console IS
    // the shop"): the console at the equipment depot's counter whose screens carry the
    // catalogue. Every peer turns the replicated state into what the rig draws: the top
    // screen's EQUIPMENT CATALOGUE with the crew pot (CrewDayState.Balance), the page
    // the crew is on (CrewDayState.ShopPage, a server-written SyncVar: everyone sees the
    // same page, last press wins) as three row cards reading the item, its price and
    // its state for THIS peer's player (OWNED from its own PlayerUpgrades, NO MONEY
    // from the pot, else BUY), the PREV / NEXT zones and the page count; the bottom
    // screen's poster (BROWSE & BUY · AIM + E, how a buy works and where the gear
    // lands) with the local buyer's refusal as a Notice; and the NEXT PAGE sign, lit
    // or dim from the same rule (ShopConsoleRules.ShopLever) the server resolves a pull
    // with. A view only: the server sells (WorldSceneFlow.ServerBuy, unchanged but for
    // its reach test, which is now this rig) and pages (ServerShopSetPage /
    // ServerShopNextPage); the only local input is which control this player's dot
    // rests on (presentation). Text is the one-line compat status the checks and the
    // guest snapshot read. Sits on the Shop rig root beside ConsoleRig
    // (ConsoleBuilder.Place + ShopConsoleBuilder.Place); ExecuteAlways so a reopened
    // scene shows the idle catalogue too.
    [ExecuteAlways]
    public sealed class HQShopConsole : MonoBehaviour, IConsoleComposer
    {
        public const float RefreshSeconds = 0.25f;
        public const string NoSessionText = "SHOP CONSOLE\n(no session)";

        private ConsoleRig rig;
        private ConsoleLever lever;
        private WorldLoopSettings settings;
        private readonly TopModel top = new();
        private readonly BottomModel bottom = new() { Status = ConsoleLine.Empty };
        private readonly ConsoleLine[] posterLines = new ConsoleLine[2];
        private ShopConsolePaint.RowView[] rowViews;
        private ShopConsolePaint.TopView view;
        private System.Action<ScreenPainter> drawTop;
        private float nextRefresh;
        private ConsoleControl lastAim;
        private string lastRefusal = string.Empty;
        private bool idleShown;

        public ConsoleKind Kind => ConsoleKind.Shop;
        public string Text { get; private set; } = string.Empty;   // "CATALOGUE PAGE 1/2\n<rows>\nCREW POT $n"
        public TopModel Top { get; private set; }
        public BottomModel Bottom { get; private set; }
        public SignModel Sign { get; private set; }
        public int LeverPlayedSerial => Lever != null ? Lever.PlayedSerial : -1;
        public float LeverAngle => Lever != null ? Lever.Angle : 0f;
        public int Page { get; private set; }                       // ShopConsoleRules.ClampPage(day.ShopPage, items); 0 before a session
        public int Pages { get; private set; } = 1;
        public string RowsText { get; private set; } = string.Empty; // the shown page's rows for THIS peer's player, "|"-joined
        public int AimedRow { get; private set; } = -1;             // LOCAL: the row under the local player's dot, -1 none
        public int AimedPage { get; private set; }                  // LOCAL: +1 NEXT aimed, -1 PREV aimed, 0 none

        private ConsoleRig Rig => rig != null ? rig : rig = GetComponent<ConsoleRig>();
        private ConsoleLever Lever => lever != null ? lever : lever = GetComponent<ConsoleLever>();

        private void Awake()
        {
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            LayoutControls();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // The editor's scene reads right after a reload: the surfaces lose their
            // textures with the domain, and only a paint puts them back. After the load,
            // not during it (the HQQuotaConsole pattern).
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
                // No session on this peer yet: the idle catalogue, once.
                if (!idleShown) ShowIdle();
                Text = NoSessionText;
                return;
            }
            idleShown = false;
            settings ??= WorldLoopSettings.Resolve(null);
            ShopCatalog catalog = ShopCatalog.Resolve();
            int items = catalog.Items.Count;
            Pages = ShopConsoleRules.PageCount(items);
            Page = ShopConsoleRules.ClampPage(day.ShopPage, items);
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            PlayerUpgrades upgrades = local != null ? local.Upgrades : null;
            int balance = day.Balance;
            RowsText = ShopConsoleRules.RowsText(catalog, Page, upgrades, balance);
            string text = ComposeText(Page, Pages, RowsText, balance);
            bool changed = text != Text;
            Text = text;
            ConsoleControl aim = LocalAim(local);
            bool aimChanged = aim != lastAim;
            lastAim = aim;
            string refusal = Refusal(day, upgrades);
            bool refusalChanged = refusal != lastRefusal;
            lastRefusal = refusal;
            if (!changed && !aimChanged && !refusalChanged && Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Compose(catalog, Page, Pages, upgrades, balance, aim, refusal);
        }

        // ---- the screens ---------------------------------------------------------------

        // What the builder shows in the editor, and a peer without a session: the first
        // page with an empty pot (every row NO MONEY), nobody aimed, no refusal.
        public void ShowIdle()
        {
            idleShown = true;
            ShopCatalog catalog = ShopCatalog.Resolve();
            int items = catalog.Items.Count;
            Page = 0; Pages = ShopConsoleRules.PageCount(items);
            RowsText = ShopConsoleRules.RowsText(catalog, 0, null, 0);
            Compose(catalog, 0, Pages, null, 0, null, string.Empty);
        }

        private void Compose(ShopCatalog catalog, int page, int pages, PlayerUpgrades upgrades, int balance, ConsoleControl aim, string refusal)
        {
            HQSigns signs = HQSigns.Resolve();
            AimedRow = aim != null && aim.Kind == ConsoleControlKind.ShopRow ? aim.Index : -1;
            AimedPage = aim != null && aim.Kind == ConsoleControlKind.ShopPage ? (aim.Index < 0 ? -1 : 1) : 0;
            bool leverAimed = aim != null && aim.Kind == ConsoleControlKind.Lever;
            ComposeTop(catalog, page, upgrades, balance, top, signs);
            ComposeBottom(refusal, bottom, signs);
            SignModel sign = ComposeSign(pages, leverAimed, signs);
            Top = top; Bottom = bottom; Sign = sign;
            if (Rig == null) return;
            PaintTop(catalog, page, pages, upgrades, balance, signs);
            Rig.Show(null, bottom, sign); // the standard poster and plate; the top is this composer's own painter
        }

        // The top screen through ShopConsolePaint: the view the painter draws from, the
        // key that repaints only when the picture changes.
        private void PaintTop(ShopCatalog catalog, int page, int pages, PlayerUpgrades upgrades, int balance, HQSigns signs)
        {
            ConsoleScreen surface = Rig.TopSurface;
            if (surface == null) return;
            rowViews ??= new ShopConsolePaint.RowView[ShopConsoleRules.RowsPerPage];
            for (int r = 0; r < rowViews.Length; r++)
            {
                ShopItem item = ShopConsoleRules.ItemAt(catalog, page, r);
                rowViews[r] = item == null
                    ? new ShopConsolePaint.RowView { Empty = true, Name = string.Empty, Price = string.Empty, State = string.Empty }
                    : new ShopConsolePaint.RowView { Name = item.Name, Price = "$" + item.Price, State = ShopConsoleRules.RowState(item, upgrades, balance), Aimed = AimedRow == r };
            }
            view = new ShopConsolePaint.TopView
            {
                Title = top.Title, Pot = top.Corner, Page = top.BigState,
                PrevWord = top.FootLeft, NextWord = top.FootRight, Rows = rowViews,
                PrevEnabled = page > 0, NextEnabled = page < pages - 1,
                PrevAimed = AimedPage < 0, NextAimed = AimedPage > 0,
            };
            drawTop ??= p => ShopConsolePaint.Top(p, view, ConsoleStyle.Resolve());
            surface.Paint(ShopConsolePaint.TopKey(view), drawTop);
        }

        // The control of THIS console the local player's dot rests on (the host may have
        // more than one HQ scene's worth of rigs loaded; the other rigs' controls are not ours).
        private ConsoleControl LocalAim(HQPlayerController local)
        {
            ConsoleControl aim = local != null ? local.CurrentConsoleControl : null;
            return aim != null && aim.Console == ConsoleKind.Shop && aim.transform.IsChildOf(transform) ? aim : null;
        }

        // The refusal on display: the local buyer's own (PlayerUpgrades.Refusal, a
        // TargetRpc to the buyer, 2.5 s) first, else the console's last refusal the server
        // answered everyone (CrewDayState.LastRefusal) inside its window.
        private string Refusal(CrewDayState day, PlayerUpgrades upgrades)
        {
            string own = upgrades != null ? upgrades.Refusal : string.Empty;
            if (!string.IsNullOrEmpty(own)) return own;
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text)) return day.LastRefusal.Text;
            return string.Empty;
        }

        // The rows' and zones' hit boxes where the painter draws them (the ConsoleRig.Lay
        // pattern over ShopConsolePaint's rectangles): run at Awake and by the builder, so a
        // style knob turned after the rig was built moves the box with the card.
        public void LayoutControls()
        {
            ConsoleScreen surface = Rig != null ? Rig.TopSurface : null;
            if (surface == null) return;
            ConsoleStyle style = ConsoleStyle.Resolve();
            int w = surface.Width, h = surface.Height;
            Rect[] rows = ShopConsolePaint.RowRects(w, h, style);
            for (int r = 0; r < rows.Length; r++) Lay(surface, ConsoleRig.RowNamePrefix + r, rows[r]);
            Lay(surface, ConsoleRig.PrevName, ShopConsolePaint.PrevRect(w, h, style));
            Lay(surface, ConsoleRig.NextName, ShopConsolePaint.NextRect(w, h, style));
        }

        private static void Lay(ConsoleScreen screen, string name, Rect px)
        {
            Transform t = screen.transform.Find(name);
            if (t == null) return;
            Vector2 centre = screen.LocalCentre(px), size = screen.LocalSize(px);
            t.localPosition = new Vector3(centre.x, centre.y, t.localPosition.z);
            if (t.TryGetComponent(out BoxCollider box)) box.size = new Vector3(size.x, size.y, box.size.z);
            Transform touch = t.Find(ConsoleRig.TouchName);
            if (touch != null) touch.localScale = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), 1f);
        }

        // ---- the composition (pure) ----------------------------------------------------

        // The top screen's model: the title, the pot in the corner, the page as the big
        // state, the rows as the hint (the one-line form the snapshot reads), PREV / NEXT at
        // the foot. Flatten → "EQUIPMENT CATALOGUE · CREW POT $500 ·  · PAGE 1/2 · Air tank $40 BUY|Large tank $300 BUY|Bright headlamp $150 BUY · < PREV · NEXT >".
        public static void ComposeTop(ShopCatalog catalog, int page, PlayerUpgrades local, int balance, TopModel top, HQSigns signs)
        {
            int items = catalog != null ? catalog.Items.Count : 0;
            int pages = ShopConsoleRules.PageCount(items);
            page = ShopConsoleRules.ClampPage(page, items);
            top.Title = signs.Get("counter");
            top.Corner = string.Format(signs.Get("shop.pot"), balance);
            top.Cards = null;
            top.BigState = ShopConsoleRules.PageText(page, pages);
            top.BigTone = ConsoleTone.Accent;
            top.Hint = ShopConsoleRules.RowsText(catalog, page, local, balance);
            top.HintTone = ConsoleTone.Text;
            top.FootLeft = signs.Get("shop.prev");
            top.FootLeftTone = ConsoleTone.Accent;
            top.FootRight = signs.Get("shop.next");
        }

        // The bottom screen's poster: Dan's second phrase as the heading, how a buy works
        // and where the gear lands under it; a refusal takes the status line as a Notice.
        // While the notice is up the pickup line steps aside (the poster's notice block
        // sits where a second line would be; the first capture had them overprinted).
        public static void ComposeBottom(string refusal, BottomModel bottom, HQSigns signs)
        {
            bottom.Heading = signs.Get("counter.sub");
            bottom.Name = string.Empty; bottom.SubName = string.Empty;
            bottom.Picture = null; bottom.VoteCard = null;
            ConsoleLine aim = new(signs.Get("shop.aim"), ConsoleTextSize.Medium, ConsoleTone.Text);
            if (!string.IsNullOrEmpty(refusal))
            {
                bottom.Lines = new[] { aim };
                bottom.Status = new ConsoleLine(refusal, ConsoleTextSize.Large, ConsoleTone.Warn);
                bottom.Notice = true;
            }
            else
            {
                bottom.Lines = new[] { aim, new ConsoleLine(signs.Get("shop.pickup"), ConsoleTextSize.Small, ConsoleTone.Dim) };
                bottom.Status = ConsoleLine.Empty;
                bottom.Notice = false;
            }
        }

        // The NEXT PAGE sign: lit while the catalogue has more than one page, dim on one
        // page (the word stays readable; the pull is refused "One page only").
        public static SignModel ComposeSign(int pages, bool aimed, HQSigns signs)
        {
            LeverAction action = ShopConsoleRules.ShopLever(pages, out bool enabled, out _);
            return ConsoleRules.Sign(action, enabled, 0, aimed, signs.Get("lever.page"));
        }

        // The one-line compat status the checks read.
        public static string ComposeText(int page, int pages, string rowsText, int balance)
            => $"CATALOGUE {ShopConsoleRules.PageText(page, pages)}\n{rowsText}\nCREW POT ${balance}";

        public static HQShopConsole InHQ()
        {
            ConsoleRig r = ConsoleRig.InScene(WorldScenes.Scene(WorldId.HQ), ConsoleKind.Shop);
            return r != null ? r.GetComponent<HQShopConsole>() : null;
        }
    }
}
