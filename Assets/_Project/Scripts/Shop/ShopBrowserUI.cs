using SunkCost.Look;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Shop
{
    // Owner-only presentation. Rows come from ShopCatalog; all prices, reach,
    // funds, ownership and delivery are still checked by ServerBuy per request.
    //
    // Immediate-mode GUI restyled in place (HQ polish, 27 September 2026; Dan:
    // "restyle in place", no uGUI): the ship's screen palette (ScreenStyle) so the
    // counter reads as one design with the deck's screens and the visor — dark
    // glass, one cyan accent, gold for money, amber for a refusal; bracket corners
    // like the HUD's prompt panel. Every row says what the item does, what it costs,
    // and whether the pot can pay for it; the one action is BUY. No timers, no
    // fake scarcity, nothing that spends without a press.
    public sealed class ShopBrowserUI : MonoBehaviour
    {
        private const float PanelW = 880f, PanelH = 640f;
        private const float CardH = 96f, CardGap = 10f;
        private static readonly Color Gold = new(1f, 0.85f, 0.2f);
        private static readonly Color Shade = new(0.01f, 0.02f, 0.03f, 0.9f);
        private static readonly Color Card = new(0.05f, 0.085f, 0.105f);
        private static readonly Color CardHover = new(0.075f, 0.12f, 0.145f);

        private HQPlayerController player;
        private ShopDisplay counter;
        private Vector2 scroll;
        private string search = string.Empty;
        private int category;
        private float nextRequest;
        private GUIStyle titleStyle, subtitleStyle, nameStyle, bodyStyle, hintStyle, priceStyle, potStyle, chipStyle, buttonStyle, ghostStyle, tabStyle, searchStyle, emptyStyle;
        private Texture2D white, accentFill, accentHover, trackFill, trackHover;
        public bool IsOpen => counter != null && SessionInputGate.ShopOpen;

        private void Awake() => player = GetComponent<HQPlayerController>();

        public void Open(ShopDisplay value)
        {
            if (value == null || !value.BrowsesCatalog || player == null || !player.IsOwner) return;
            counter = value;
            if (!CanBrowse()) { counter = null; return; }
            search = string.Empty; category = 0; scroll = Vector2.zero;
            SessionInputGate.OpenShop();
        }

        public void Close()
        {
            if (counter != null) SessionInputGate.CloseShop();
            counter = null;
        }

        private void OnDisable() => Close();
        private void Update()
        {
            if (counter != null && (!SessionInputGate.ShopOpen || !CanBrowse() ||
                SessionInputGate.MenuOpen || SessionInputGate.OverlayOpen || !SessionInputGate.ApplicationFocused)) Close();
            // A scene unload destroys the referenced counter first.
            else if (counter == null && player != null && player.IsOwner && SessionInputGate.ShopOpen) SessionInputGate.CloseShop();
        }

        private bool CanBrowse()
        {
            CrewDayState day = CrewDayState.Instance;
            if (player == null || !player.IsSpawned || player.IsDead || player.TravelLocked ||
                counter == null || !counter.isActiveAndEnabled || day == null || day.World != WorldId.HQ ||
                day.Travelling || day.Phase == DayPhase.Plank || counter.gameObject.scene != player.gameObject.scene) return false;
            Collider hit = counter.GetComponentInChildren<Collider>();
            Vector3 point = hit != null ? hit.ClosestPoint(player.EyePosition) : counter.transform.position;
            return Vector3.Distance(player.EyePosition, point) <= player.InteractReach + .5f;
        }

        // Shared by the button and runtime checks. This is a request, not a grant.
        public bool Buy(string id)
        {
            if (!IsOpen || !CanBrowse() || Time.unscaledTime < nextRequest) return false;
            ShopItem item = ShopCatalog.Resolve().Find(id);
            if (item == null || player.Upgrades == null) return false;
            nextRequest = Time.unscaledTime + .35f;
            player.Upgrades.RequestBuy(id);
            return true;
        }

        private bool Matches(ShopItem item)
        {
            if (item == null) return false;
            if (category == 1 && item.Kind != ShopItemKind.Consumable) return false;
            if (category == 2 && item.Kind != ShopItemKind.Upgrade) return false;
            return string.IsNullOrWhiteSpace(search) || (item.Name ?? item.Id).IndexOf(search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public int VisibleItemCount
        {
            get { int count=0; foreach(var item in ShopCatalog.Resolve().Items) if(Matches(item))count++; return count; }
        }

        // What the row says the item does: Dan's line from the catalogue, or one
        // composed from the catalogue's own numbers so it can never drift from the rule.
        public static string Describe(ShopItem item)
        {
            if (item == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(item.Description)) return item.Description.Trim();
            ShopCatalog catalog = ShopCatalog.Resolve();
            switch (item.Upgrade)
            {
                case PlayerUpgrade.LargeTank: return $"+{Mathf.RoundToInt((catalog.LargeTankMultiplier - 1f) * 100f)}% air on every dive. Fitted for good, one per diver.";
                case PlayerUpgrade.BrightHeadlamp: return $"A longer, stronger beam: {catalog.BrightHeadlampRange:0.#}× range, {catalog.BrightHeadlampIntensity:0.#}× light. One per diver.";
            }
            if (item.Kind == ShopItemKind.Upgrade) return "Fitted to you at once. One per diver.";
            if (item.Id == ShopCatalog.AirTankId) return "Spare air to carry down. Breathe from it underwater.";
            if (item.Id == ShopCatalog.PatchKitId) return "One use. Seals your own suit leak.";
            return "Drops at the PICKUP chute outside. Anyone can take it.";
        }

        private void OnGUI()
        {
            if (!IsOpen || !CanBrowse() || SessionInputGate.MenuOpen || SessionInputGate.OverlayOpen) return;
            EnsureStyles();
            GUI.depth = -10; // over the session's corner hints
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            float scale = Mathf.Min(1.5f, Screen.width / (PanelW + 60f), Screen.height / (PanelH + 40f));
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float w = Screen.width / scale, h = Screen.height / scale;
            Rect panel = new((w - PanelW) / 2f, (h - PanelH) / 2f, PanelW, PanelH);
            CrewDayState day = CrewDayState.Instance;
            int balance = day != null ? day.Balance : 0;
            try
            {
                Fill(new Rect(0, 0, w, h), Shade);
                Fill(panel, ScreenStyle.Back);
                Edge(panel, ScreenStyle.Accent, 0.55f);
                Brackets(new Rect(panel.x + 4, panel.y + 4, panel.width - 8, panel.height - 8), 16f, 2f, ScreenStyle.Accent);
                GUI.BeginGroup(panel);
                // Header: what this is, the pot it spends, the way out.
                GUI.Label(new Rect(32, 22, 520, 36), "EQUIPMENT DEPOT", titleStyle);
                GUI.Label(new Rect(32, 58, 520, 20), "BLACK TIDE SALVAGE CO.  ·  the counter's full catalogue", subtitleStyle);
                Rect pot = new(560, 20, 190, 62);
                Fill(pot, ScreenStyle.Track);
                Edge(pot, Gold, 0.5f);
                GUI.Label(new Rect(pot.x + 14, pot.y + 6, pot.width - 28, 16), "CREW POT  ·  shared", hintStyle);
                GUI.Label(new Rect(pot.x + 14, pot.y + 22, pot.width - 28, 36), $"${balance}", potStyle);
                if (Button(new Rect(766, 26, 88, 40), "CLOSE", ghostStyle, "Esc")) Close();
                Rule(new Rect(32, 92, PanelW - 64, 1));
                // Search and the two filters.
                Rect searchRect = new(32, 108, 470, 36);
                string typed = GUI.TextField(searchRect, search, searchStyle);
                if (typed != search) { search = typed; scroll = Vector2.zero; }
                if (string.IsNullOrEmpty(search))
                {
                    GUI.color = ScreenStyle.Dim;
                    GUI.Label(new Rect(searchRect.x + 14, searchRect.y, searchRect.width - 28, searchRect.height), "Search the catalogue", bodyStyle);
                    GUI.color = Color.white;
                }
                int selected = GUI.Toolbar(new Rect(520, 108, 328, 36), category, new[] { "ALL", "SUPPLIES", "UPGRADES" }, tabStyle);
                if (selected != category) { category = selected; scroll = Vector2.zero; }
                // The list.
                int count = VisibleItemCount;
                Rect listRect = new(32, 160, PanelW - 64, 402);
                float contentH = Mathf.Max(listRect.height, count * (CardH + CardGap) - CardGap);
                float contentW = listRect.width - (contentH > listRect.height ? 16f : 0f);
                scroll = GUI.BeginScrollView(listRect, scroll, new Rect(0, 0, contentW, contentH), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
                int row = 0;
                foreach (ShopItem item in ShopCatalog.Resolve().Items)
                {
                    if (!Matches(item)) continue;
                    DrawCard(item, new Rect(0, row++ * (CardH + CardGap), contentW, CardH), balance, oldEnabled);
                }
                if (count == 0) GUI.Label(new Rect(0, 24, contentW, 40), "Nothing in the catalogue matches that.", emptyStyle);
                GUI.EndScrollView();
                // The status line: the server's last refusal, or how buying works.
                Rule(new Rect(32, 574, PanelW - 64, 1));
                string refusal = player.Upgrades != null ? player.Upgrades.Refusal : string.Empty;
                bool refused = !string.IsNullOrEmpty(refusal);
                GUI.color = refused ? ScreenStyle.Warn : ScreenStyle.Dim;
                GUI.Label(new Rect(32, 586, PanelW - 64, 40), refused ? refusal : "BUY spends the crew pot.  Supplies drop at the PICKUP chute outside; upgrades are fitted to you at once.", bodyStyle);
                GUI.color = Color.white;
                GUI.EndGroup();
            }
            finally { GUI.enabled = oldEnabled; GUI.color = oldColor; GUI.matrix = oldMatrix; }
        }

        // One item: a kind bar, the name and what it does, the price and whether
        // the pot covers it, the one button. Hover lifts the card a shade.
        private void DrawCard(ShopItem item, Rect rect, int balance, bool enabledBefore)
        {
            bool upgrade = item.Kind == ShopItemKind.Upgrade;
            bool owned = upgrade && player.Upgrades != null && player.Upgrades.Has(item.Upgrade);
            bool affordable = balance >= item.Price;
            bool hover = rect.Contains(Event.current.mousePosition);
            Color kind = upgrade ? Gold : ScreenStyle.Accent;
            Fill(rect, hover ? CardHover : Card);
            Fill(new Rect(rect.x, rect.y, 5f, rect.height), owned ? ScreenStyle.Dim : kind);
            // The name, a chip for its kind, the line under it.
            GUI.Label(new Rect(rect.x + 22, rect.y + 14, 420, 30), item.Name, nameStyle);
            Vector2 nameSize = nameStyle.CalcSize(new GUIContent(item.Name));
            string chipText = owned ? "OWNED" : (upgrade ? "UPGRADE" : "SUPPLY");
            Vector2 chipSize = chipStyle.CalcSize(new GUIContent(chipText));
            Rect chip = new(rect.x + 22 + Mathf.Min(nameSize.x, 400f) + 12, rect.y + 20, chipSize.x + 16, 20);
            Fill(chip, ScreenStyle.Track);
            Edge(chip, owned ? ScreenStyle.Dim : kind, 0.8f);
            GUI.color = owned ? ScreenStyle.Dim : kind;
            GUI.Label(chip, chipText, chipStyle);
            GUI.color = ScreenStyle.Dim;
            GUI.Label(new Rect(rect.x + 22, rect.y + 50, 470, 40), Describe(item), bodyStyle);
            // Price and what the pot says about it.
            GUI.color = Gold;
            GUI.Label(new Rect(rect.x + 500, rect.y + 16, 130, 32), $"${item.Price}", priceStyle);
            string standing = owned ? "fitted to you" : affordable ? "the pot covers it" : $"pot is ${item.Price - balance} short";
            GUI.color = owned ? ScreenStyle.Accent : affordable ? ScreenStyle.Good : ScreenStyle.Warn;
            GUI.Label(new Rect(rect.x + 500, rect.y + 52, 130, 24), standing, hintStyle);
            GUI.color = Color.white;
            // The button: BUY when it can go through; a dim BUY when the pot is short (the press still asks the server, which says why); OWNED when done.
            Rect button = new(rect.x + rect.width - 144, rect.y + 28, 120, 40);
            GUI.enabled = enabledBefore && !owned && Time.unscaledTime >= nextRequest;
            if (owned) { Fill(button, ScreenStyle.Track); Edge(button, ScreenStyle.Dim, 0.6f); GUI.color = ScreenStyle.Dim; GUI.Label(button, "OWNED", chipStyle); GUI.color = Color.white; }
            else if (Button(button, "BUY", affordable ? buttonStyle : ghostStyle, null)) Buy(item.Id);
            GUI.enabled = enabledBefore;
        }

        // ---- the parts -----------------------------------------------------------------

        private bool Button(Rect rect, string text, GUIStyle style, string key)
        {
            bool pressed = GUI.Button(rect, text, style);
            if (style == ghostStyle) Edge(rect, GUI.enabled ? ScreenStyle.Accent : ScreenStyle.Dim, 0.7f);
            if (!string.IsNullOrEmpty(key))
            {
                GUI.color = ScreenStyle.Dim;
                GUI.Label(new Rect(rect.x, rect.yMax + 2, rect.width, 14), key, hintStyle);
                GUI.color = Color.white;
            }
            return pressed;
        }

        private void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color; GUI.color = colour;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        private void Edge(Rect rect, Color colour, float alpha)
        {
            Color c = colour; c.a = alpha;
            Fill(new Rect(rect.x, rect.y, rect.width, 1), c);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), c);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), c);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), c);
        }

        private void Rule(Rect rect) => Fill(rect, new Color(ScreenStyle.Accent.r, ScreenStyle.Accent.g, ScreenStyle.Accent.b, 0.35f));

        private void Brackets(Rect rect, float arm, float thickness, Color colour)
        {
            Fill(new Rect(rect.x, rect.y, arm, thickness), colour); Fill(new Rect(rect.x, rect.y, thickness, arm), colour);
            Fill(new Rect(rect.xMax - arm, rect.y, arm, thickness), colour); Fill(new Rect(rect.xMax - thickness, rect.y, thickness, arm), colour);
            Fill(new Rect(rect.x, rect.yMax - thickness, arm, thickness), colour); Fill(new Rect(rect.x, rect.yMax - arm, thickness, arm), colour);
            Fill(new Rect(rect.xMax - arm, rect.yMax - thickness, arm, thickness), colour); Fill(new Rect(rect.xMax - thickness, rect.yMax - arm, thickness, arm), colour);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "ShopBrowser " + ScreenStyle.Hex(c) };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        private static GUIStyle Text(int size, FontStyle weight, TextAnchor anchor, Color colour, bool wrap = false)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight, alignment = anchor, wordWrap = wrap, richText = false, padding = new RectOffset(0, 0, 0, 0) };
            style.normal.textColor = colour;
            return style;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            white = Texture2D.whiteTexture;
            accentFill = Solid(ScreenStyle.Accent);
            accentHover = Solid(Color.Lerp(ScreenStyle.Accent, Color.white, 0.18f));
            trackFill = Solid(ScreenStyle.Track);
            trackHover = Solid(CardHover);
            titleStyle = Text(28, FontStyle.Bold, TextAnchor.MiddleLeft, ScreenStyle.Accent);
            subtitleStyle = Text(13, FontStyle.Normal, TextAnchor.MiddleLeft, ScreenStyle.Dim);
            nameStyle = Text(20, FontStyle.Bold, TextAnchor.MiddleLeft, ScreenStyle.Text);
            bodyStyle = Text(14, FontStyle.Normal, TextAnchor.MiddleLeft, ScreenStyle.Text, wrap: true);
            hintStyle = Text(12, FontStyle.Normal, TextAnchor.MiddleLeft, ScreenStyle.Dim);
            priceStyle = Text(24, FontStyle.Bold, TextAnchor.MiddleLeft, Gold);
            potStyle = Text(26, FontStyle.Bold, TextAnchor.MiddleLeft, Gold);
            chipStyle = Text(11, FontStyle.Bold, TextAnchor.MiddleCenter, ScreenStyle.Text);
            emptyStyle = Text(16, FontStyle.Normal, TextAnchor.MiddleCenter, ScreenStyle.Dim);
            // The primary button: the accent filled, dark words; lighter under the mouse.
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, border = new RectOffset(0, 0, 0, 0), padding = new RectOffset(8, 8, 0, 0) };
            buttonStyle.normal.background = accentFill; buttonStyle.normal.textColor = ScreenStyle.Back;
            buttonStyle.hover.background = accentHover; buttonStyle.hover.textColor = ScreenStyle.Back;
            buttonStyle.active.background = trackFill; buttonStyle.active.textColor = ScreenStyle.Accent;
            buttonStyle.focused.background = accentFill; buttonStyle.focused.textColor = ScreenStyle.Back;
            // The secondary/ghost button: dark, an accent edge drawn round it, accent words; dim when disabled.
            ghostStyle = new GUIStyle(buttonStyle);
            ghostStyle.normal.background = trackFill; ghostStyle.normal.textColor = ScreenStyle.Accent;
            ghostStyle.hover.background = trackHover; ghostStyle.hover.textColor = ScreenStyle.Text;
            ghostStyle.active.background = accentFill; ghostStyle.active.textColor = ScreenStyle.Back;
            ghostStyle.focused.background = trackFill; ghostStyle.focused.textColor = ScreenStyle.Accent;
            // Tabs: the chosen one filled with the accent, the others dark.
            tabStyle = new GUIStyle(ghostStyle) { fontSize = 13 };
            tabStyle.normal.textColor = ScreenStyle.Dim;
            tabStyle.onNormal.background = accentFill; tabStyle.onNormal.textColor = ScreenStyle.Back;
            tabStyle.onHover.background = accentHover; tabStyle.onHover.textColor = ScreenStyle.Back;
            tabStyle.onActive.background = accentFill; tabStyle.onActive.textColor = ScreenStyle.Back;
            tabStyle.onFocused.background = accentFill; tabStyle.onFocused.textColor = ScreenStyle.Back;
            // The search field: dark glass, the text in the screen's colour.
            searchStyle = new GUIStyle(GUI.skin.textField) { fontSize = 15, alignment = TextAnchor.MiddleLeft, border = new RectOffset(0, 0, 0, 0), padding = new RectOffset(14, 14, 0, 0) };
            searchStyle.normal.background = trackFill; searchStyle.normal.textColor = ScreenStyle.Text;
            searchStyle.hover.background = trackHover; searchStyle.hover.textColor = ScreenStyle.Text;
            searchStyle.focused.background = trackHover; searchStyle.focused.textColor = ScreenStyle.Text;
            searchStyle.active.background = trackHover; searchStyle.active.textColor = ScreenStyle.Text;
            buttonStyle.onNormal = buttonStyle.normal;
            ghostStyle.onNormal = ghostStyle.normal;
        }
    }
}
