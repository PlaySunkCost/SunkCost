using SunkCost.Look;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The shared console's physical rig (Dan, 27 September 2026; INTERFACES §10): the
    // model, its three painted surfaces, the lever's hinge and handle, and the
    // controls the player aims at, built once by ConsoleBuilder.Place and used by
    // both consoles - the ship's navigation console and HQ's quota board. Show()
    // draws three content models (what the composers make from replicated state);
    // the drawing repaints only when a model changes. ShowIdle() is the editor's
    // idle screen so a built prefab reads right before any session exists.
    public sealed class ConsoleRig : MonoBehaviour
    {
        public const string ShipRootName = "Nav Console";          // the ship rig root (under the ship's Console group after ShipHierarchy)
        public const string HQRootName = "Quota Console";          // the HQ rig root (under the intake group)
        public const string ShipLeverName = "Nav Lever";           // the ship lever's control object (collider + ConsoleControl Lever/Ship)
        public const string HQLeverName = "Quota Board";           // == QuotaBoard.BoardName: ClientLookAtNamed("Quota Board") + E pays
        public const string CardNamePrefix = "Nav Card ";          // "Nav Card HQ", "Nav Card Site01", ... "Nav Card Site04" (SiteId names)
        public const string GiveUpCardName = "Give Up Button";     // == GiveUpButton.ButtonName
        public const string TopScreenName = "Console Top Screen", BottomScreenName = "Console Bottom Screen", SignName = "Console Sign";
        public const string LeverHandleName = "Console Lever Handle", LeverHingeName = "Console Lever Hinge", BodyName = "Console Body";

        [SerializeField] private ConsoleKind kind;
        [SerializeField] private Transform topScreen, bottomScreen, sign, leverHandle, leverHinge;
        [SerializeField] private Collider leverCollider, giveUpCollider;      // giveUpCollider null on the ship
        [SerializeField] private Collider[] cardColliders = new Collider[5]; // Destinations.Cards order; empty on HQ
        [SerializeField] private ConsoleScreen topSurface, bottomSurface, signSurface;

        public ConsoleKind Kind => kind;
        public Transform TopScreen => topScreen;
        public Transform BottomScreen => bottomScreen;
        public Transform Sign => sign;
        public Transform LeverHandle => leverHandle;
        public Transform LeverHinge => leverHinge;
        public Collider LeverCollider => leverCollider;
        public Collider GiveUpCollider => giveUpCollider;
        public System.Collections.Generic.IReadOnlyList<Collider> CardColliders => cardColliders;
        public ConsoleScreen TopSurface => topSurface;
        public ConsoleScreen BottomSurface => bottomSurface;
        public ConsoleScreen SignSurface => signSurface;

        // ConsoleModels.Flatten of what is shown now (the hooks and the snapshot).
        public string TopText { get; private set; } = string.Empty;
        public string BottomText { get; private set; } = string.Empty;
        public string SignText { get; private set; } = string.Empty;

        // The builder's wiring.
        public void Configure(ConsoleKind consoleKind, ConsoleScreen top, ConsoleScreen bottom, ConsoleScreen signSurfaceScreen,
            Transform hinge, Transform handle, Collider lever, Collider giveUp, Collider[] cards)
        {
            kind = consoleKind;
            topSurface = top; bottomSurface = bottom; signSurface = signSurfaceScreen;
            topScreen = top != null ? top.transform : null;
            bottomScreen = bottom != null ? bottom.transform : null;
            sign = signSurfaceScreen != null ? signSurfaceScreen.transform : null;
            leverHinge = hinge; leverHandle = handle;
            leverCollider = lever; giveUpCollider = giveUp;
            cardColliders = new Collider[5];
            if (cards != null) for (int i = 0; i < cards.Length && i < 5; i++) cardColliders[i] = cards[i];
        }

        // The ConsoleControl on that collider's object, or null.
        public ConsoleControl ControlOf(Collider c) => c != null ? c.GetComponent<ConsoleControl>() : null;

        // Draws the three models; safe every frame, cheap when nothing changed.
        public void Show(TopModel top, BottomModel bottom, SignModel signModel)
        {
            ConsoleStyle style = ConsoleStyle.Resolve();
            if (topSurface != null && top != null)
            {
                topSurface.Paint(ConsolePaint.Key(top), p => ConsolePaint.Top(p, top, style));
                TopText = ConsoleModels.Flatten(top);
            }
            if (bottomSurface != null && bottom != null)
            {
                bottomSurface.Paint(ConsolePaint.Key(bottom), p => ConsolePaint.Bottom(p, bottom, style));
                BottomText = ConsoleModels.Flatten(bottom);
            }
            if (signSurface != null)
            {
                signSurface.Paint(ConsolePaint.Key(signModel), p => ConsolePaint.Sign(p, signModel, style));
                SignText = ConsoleModels.Flatten(signModel);
            }
        }

        // The editor's idle screen (ShipScreens pattern): NAVIGATION / QUOTA BOARD with
        // nothing selected and the sign dim, so the prefab reads right.
        public void ShowIdle()
        {
            Idle(kind, out TopModel top, out BottomModel bottom, out SignModel signModel);
            Show(top, bottom, signModel);
        }

        // The idle models per kind, from the catalogue and the signs asset alone.
        public static void Idle(ConsoleKind kind, out TopModel top, out BottomModel bottom, out SignModel signModel)
        {
            HQSigns signs = HQSigns.Resolve();
            SiteCatalog sites = SiteCatalog.Resolve();
            if (kind == ConsoleKind.Ship)
            {
                var cards = new CardModel[Destinations.Cards.Length];
                for (int i = 0; i < cards.Length; i++)
                {
                    SiteId id = Destinations.Cards[i];
                    bool locked = Destinations.IsPurchasable(id);
                    cards[i] = new CardModel
                    {
                        Id = id, Name = sites.NameOf(id), Picture = sites.PictureOf(id),
                        Locked = locked, Here = id == SiteId.HQ,
                        Tag = locked ? "$" + sites.UnlockPrice(id) : string.Empty,
                    };
                }
                top = new TopModel { Title = signs.Get("nav.title"), Corner = string.Empty, Cards = cards, BigState = string.Empty, Hint = string.Empty, FootLeft = string.Empty, FootLeftTone = ConsoleTone.Dim, FootRight = signs.Get("company") };
                bottom = new BottomModel { Heading = signs.Get("nav.select"), Name = string.Empty, SubName = string.Empty, Lines = new[] { new ConsoleLine("STANDBY", ConsoleTextSize.Medium, ConsoleTone.Dim) }, Status = ConsoleLine.Empty };
                signModel = new SignModel { Word = signs.Get("lever.confirm"), Tone = ConsoleTone.Dim, Enabled = false };
                return;
            }
            WorldLoopSettings settings = WorldLoopSettings.Resolve(null);
            top = new TopModel
            {
                Title = signs.Get("board.title"), Corner = string.Empty, Cards = null,
                BigState = "STANDBY", BigTone = ConsoleTone.Dim, Hint = "Waiting for the crew", HintTone = ConsoleTone.Dim,
                FootLeft = $"QUOTA $0 / ${settings.QuotaPerCycle}", FootLeftTone = ConsoleTone.Warn, FootRight = "BALANCE $0",
            };
            bottom = new BottomModel
            {
                Heading = string.Empty, Name = string.Empty, SubName = string.Empty, Lines = null, Status = ConsoleLine.Empty,
                VoteCard = new VoteCardModel { Word = signs.Get("giveup.card"), Count = "0 / 0", Foot = signs.Get("giveup.foot"), Enabled = false },
            };
            signModel = new SignModel { Word = signs.Get("lever.pay"), Tone = ConsoleTone.Dim, Enabled = false };
        }

        // The one rig of that kind in the scene, or null.
        public static ConsoleRig InScene(Scene scene, ConsoleKind kind)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (ConsoleRig rig in root.GetComponentsInChildren<ConsoleRig>(true))
                    if (rig.kind == kind) return rig;
            return null;
        }

        public static ConsoleRig OnShip(ShipParts ship)
        {
            Transform t = ship != null ? ship.Find(ShipRootName) : null;
            return t != null ? t.GetComponent<ConsoleRig>() : null;
        }
    }
}
