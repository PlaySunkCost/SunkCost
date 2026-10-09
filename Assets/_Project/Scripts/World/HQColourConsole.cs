using SunkCost.Look;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The crew colour console's composer (Dan, 8 October 2026; docs/DESIGN.md "The
    // crew colour console"): the shared console standing at the arrival pad where
    // the colour panel stood. Every peer turns the replicated day state into what the
    // rig draws - the top screen's title, the ring of sixteen swatches with the SHOWN
    // one ringed (CrewDayState.ShownSwatch: one server-held index anyone's press moves,
    // last press wins) and the diver's helmet in the middle in that colour; the bottom
    // screen's colour name in its colour over PULL THE LEVER TO TAKE IT, or, for
    // TakenSeconds after a pull, <NAME> IS NOW <COLOUR> (CrewDayState.LastColourTake),
    // or the refusal the server answered; and the TAKE sign, always lit (taking a colour
    // is never refused on the facts). A view only: the server decides
    // (WorldSceneFlow.ServerPressSwatch / ServerTakeColour) and every peer, spectator
    // and joiner draws the same replicated facts; only the aim (the bracketed swatch)
    // is this player's. The screens are painted by ColourConsolePaint through the
    // rig's surfaces; the sign through the rig's standard painter. The Top/Bottom
    // models carry the same words for the hooks and the guest snapshot. ExecuteAlways
    // so a reopened scene shows the idle console too.
    [ExecuteAlways]
    public sealed class HQColourConsole : MonoBehaviour, IConsoleComposer
    {
        public const float RefreshSeconds = 0.25f, TakenSeconds = 3f;
        public const string NoSessionText = "COLOUR CONSOLE\n(no session)";

        private ConsoleRig rig;
        private ConsoleLever lever;
        private WorldLoopSettings settings;
        private ConsoleStyle style;
        private readonly TopModel top = new();
        private readonly BottomModel bottom = new() { Status = ConsoleLine.Empty };
        private ColourConsolePaint.TopView topView;
        private ColourConsolePaint.BottomView bottomView;
        private System.Action<ScreenPainter> drawTop, drawBottom;
        private float nextRefresh;
        private ConsoleControl lastAim;
        private bool lastTaken;
        private bool idleShown;

        public ConsoleKind Kind => ConsoleKind.Colour;
        public string Text { get; private set; } = string.Empty;   // "COLOUR BLUE (9)\n<the bottom line>" (the compat one-liner)
        public TopModel Top { get; private set; }
        public BottomModel Bottom { get; private set; }
        public SignModel Sign { get; private set; }
        public int LeverPlayedSerial => Lever != null ? Lever.PlayedSerial : -1;
        public float LeverAngle => Lever != null ? Lever.Angle : 0f;
        public int Shown { get; private set; }                       // day.ShownSwatch, 0 before a session
        public int AimedSwatch { get; private set; } = -1;           // LOCAL: the swatch under this player's dot, -1 none
        public bool TakenShowing { get; private set; }               // the IS NOW line is on

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
            // textures with the domain, and only a paint puts them back. After the load.
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
                if (!idleShown) ShowIdle();
                Text = NoSessionText;
                return;
            }
            idleShown = false;
            settings ??= WorldLoopSettings.Resolve(null);
            int shown = day.ShownSwatch;
            ColourTake take = day.LastColourTake;
            bool taken = take.Serial != 0 && Time.unscaledTime - day.LastColourTakeAt < TakenSeconds;
            string refusal = Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text) ? day.LastRefusal.Text : string.Empty;
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            ConsoleControl aim = local != null ? local.CurrentConsoleControl : null;
            if (aim != null && (aim.Console != ConsoleKind.Colour || !aim.transform.IsChildOf(transform))) aim = null; // this rig's own controls only
            HQSigns signs = HQSigns.Resolve();
            string takerName = taken ? WorldSceneFlow.DisplayName(take.Taker) : string.Empty;
            string line = !string.IsNullOrEmpty(refusal) ? refusal : taken ? string.Format(signs.Get("colour.taken"), takerName.ToUpperInvariant(), PlayerPalette.Name(take.Swatch)) : signs.Get("colour.pull");
            string text = ComposeText(shown, line);
            bool changed = text != Text || taken != lastTaken;
            Text = text;
            bool aimChanged = aim != lastAim;
            lastAim = aim; lastTaken = taken;
            if (!changed && !aimChanged && Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Shown = shown;
            TakenShowing = taken;
            AimedSwatch = aim != null && aim.Kind == ConsoleControlKind.Swatch ? aim.Index : -1;
            bool leverAimed = aim != null && aim.Kind == ConsoleControlKind.Lever;
            Compose(shown, in take, taken, takerName, refusal, leverAimed, signs);
        }

        // ---- the screens ---------------------------------------------------------------

        // The builder's and a session-less peer's picture: red shown, no take, nobody aimed.
        public void ShowIdle()
        {
            idleShown = true;
            Shown = 0; TakenShowing = false; AimedSwatch = -1;
            Compose(0, new ColourTake { Serial = 0 }, false, string.Empty, string.Empty, false, HQSigns.Resolve());
        }

        private void Compose(int shown, in ColourTake take, bool taken, string takerName, string refusal, bool leverAimed, HQSigns signs)
        {
            ComposeTop(shown, top, signs);
            ComposeBottom(shown, in take, taken, takerName, refusal, bottom, signs);
            SignModel sign = ComposeSign(leverAimed, signs);
            Top = top; Bottom = bottom; Sign = sign;
            topView = new ColourConsolePaint.TopView { Title = top.Title, Shown = shown, Aimed = AimedSwatch };
            bottomView = new ColourConsolePaint.BottomView { Name = bottom.Heading, Swatch = shown, TakenSwatch = take.Swatch, Line = bottom.Status.Text, Taken = taken && !bottom.Notice, Notice = bottom.Notice };
            ConsoleRig r = Rig;
            if (r == null) return;
            style = ConsoleStyle.Resolve();
            drawTop ??= p => ColourConsolePaint.Top(p, in topView, style);
            drawBottom ??= p => ColourConsolePaint.Bottom(p, in bottomView, style);
            if (r.TopSurface != null) r.TopSurface.Paint(ColourConsolePaint.TopKey(in topView), drawTop);
            if (r.BottomSurface != null) r.BottomSurface.Paint(ColourConsolePaint.BottomKey(in bottomView), drawBottom);
            r.Show(null, null, sign);
        }

        // The "Swatch i" children of the top surface re-laid from the painter's layout
        // (the ConsoleRig.LayoutControls pattern: a style change moves the hit box with the disc).
        public void LayoutControls()
        {
            ConsoleRig r = Rig;
            ConsoleScreen screen = r != null ? r.TopSurface : null;
            if (screen == null) return;
            Rect[] rects = ColourConsolePaint.SwatchRects(screen.Width, screen.Height);
            for (int i = 0; i < rects.Length; i++)
            {
                Transform t = screen.transform.Find(ConsoleRig.SwatchNamePrefix + i);
                if (t == null) continue;
                Vector2 centre = screen.LocalCentre(rects[i]), size = screen.LocalSize(rects[i]);
                t.localPosition = new Vector3(centre.x, centre.y, t.localPosition.z);
                if (t.GetComponent<Collider>() is BoxCollider box) box.size = new Vector3(size.x, size.y, box.size.z);
                Transform touch = t.Find(ConsoleRig.TouchName);
                if (touch != null) touch.localScale = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), 1f);
            }
        }

        // ---- the composition (pure; HQColourConsoleTests) -------------------------------

        // The top screen's words: the title, the shown colour's name as the state, its
        // index as the hint, the company at the foot (the model only: the painter draws
        // the ring and the helmet from the index).
        public static void ComposeTop(int shown, TopModel top, HQSigns signs)
        {
            top.Title = signs.Get("arrival.colour");
            top.Corner = string.Empty;
            top.Cards = null;
            top.BigState = PlayerPalette.Name(shown); top.BigTone = ConsoleTone.Text;
            top.Hint = $"SWATCH {shown}"; top.HintTone = ConsoleTone.Dim;
            top.FootLeft = string.Empty; top.FootLeftTone = ConsoleTone.Dim;
            top.FootRight = signs.Get("company");
        }

        // The bottom screen: the shown colour's name, and the line under it - the
        // refusal (a Notice), the take for its window, else the pull.
        public static void ComposeBottom(int shown, in ColourTake take, bool takenShowing, string takerName, string refusal, BottomModel bottom, HQSigns signs)
        {
            bottom.Heading = PlayerPalette.Name(shown);
            bottom.Name = string.Empty; bottom.SubName = string.Empty;
            bottom.Picture = null; bottom.Lines = null; bottom.VoteCard = null;
            if (!string.IsNullOrEmpty(refusal)) { bottom.Status = new ConsoleLine(refusal, ConsoleTextSize.Large, ConsoleTone.Warn); bottom.Notice = true; return; }
            bottom.Notice = false;
            if (takenShowing)
            {
                bottom.Status = new ConsoleLine(string.Format(signs.Get("colour.taken"), (takerName ?? string.Empty).ToUpperInvariant(), PlayerPalette.Name(take.Swatch)), ConsoleTextSize.Large, ConsoleTone.Text);
                return;
            }
            bottom.Status = new ConsoleLine(signs.Get("colour.pull"), ConsoleTextSize.Large, ConsoleTone.Accent);
        }

        // The TAKE sign: always lit (the lever is never dim on the facts).
        public static SignModel ComposeSign(bool aimed, HQSigns signs)
            => ConsoleRules.Sign(LeverAction.TakeColour, true, 0, aimed, signs.Get("lever.take"));

        public static string ComposeText(int shown, string bottomLine) => $"COLOUR {PlayerPalette.Name(shown)} ({shown})\n{bottomLine}";

        public static HQColourConsole InHQ()
        {
            ConsoleRig r = ConsoleRig.InScene(WorldScenes.Scene(WorldId.HQ), ConsoleKind.Colour);
            return r != null ? r.GetComponent<HQColourConsole>() : null;
        }
    }
}
