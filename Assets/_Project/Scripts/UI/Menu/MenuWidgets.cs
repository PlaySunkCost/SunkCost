using System;
using UnityEngine;
using UnityEngine.UI;

namespace SunkCost.UI
{
    // One skin for all screens. Sprites can replace these plain plates without changing their actions.
    public static class MenuWidgets
    {
        public static Color Ink => MenuTheme.Current.Panel;
        public static Color Paper => MenuTheme.Current.Text;
        public static Color Amber => MenuTheme.Current.Accent;
        private static Font font;
        public static Font Font => MenuTheme.Current.Font != null ? MenuTheme.Current.Font :
            font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
        }
        public static void Stretch(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        public static Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        { var r = Rect(parent, name, x, y, w, h); var image = r.gameObject.AddComponent<Image>(); image.color = color; return image; }
        public static Text Label(Transform parent, string text, float x, float y, float w, float h, int size = 24, bool bold = false)
        {
            var label = Rect(parent, text, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = Font; label.fontSize = size; label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.color = Paper; label.text = text; label.supportRichText = false; label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft; label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }
        public static Button Button(Transform parent, string text, float x, float y, float w, float h, Action click, bool primary = false)
        {
            var plate = Panel(parent, text, x, y, w, h, Color.white);
            plate.sprite = MenuTheme.Current.ButtonPlate;
            if (plate.sprite != null) plate.type = Image.Type.Sliced;
            var outline = plate.gameObject.AddComponent<Outline>(); outline.effectColor = primary ? Amber : new Color(.37f, .31f, .22f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            var button = plate.gameObject.AddComponent<Button>(); button.targetGraphic = plate;
            ColorBlock colors = button.colors;
            bool textured = plate.sprite != null;
            colors.normalColor = textured ? Color.white : Ink;
            colors.highlightedColor = colors.selectedColor = textured ? new Color(1,.88f,.65f) : new Color(.43f, .25f, .055f);
            colors.pressedColor = textured ? new Color(.75f,.6f,.38f) : new Color(.65f, .36f, .06f);
            colors.disabledColor = textured ? new Color(.4f,.4f,.4f) : new Color(.06f, .065f, .065f);
            colors.fadeDuration = .09f; button.colors = colors;
            var label = Label(plate.transform, text, 22, 0, w - 44, h, 25, true);
            label.alignment = TextAnchor.MiddleCenter;
            if (MenuTheme.Current.ButtonFont != null) { label.font = MenuTheme.Current.ButtonFont; label.fontStyle = FontStyle.Normal; }
            label.fontSize = h >= 80 ? 38 : h >= 60 ? 30 : 22;
            string symbol = text switch { "HOST GAME" => "host", "JOIN GAME" => "join", "OPTIONS" => "options", "QUIT GAME" => "quit",
                "HOST SAVE" => "play", "RENAME" => "rename", "DELETE" => "delete", "BACK" => "back", "SAVE & EXIT" => "check", "SAVE" => "save", "↺" => "reset", _ => null };
            if (text.Contains("\n")) { symbol = "save"; label.fontSize = 28; }
            var trim = Rect(plate.transform, "Selection trim", 0, 0, w, h).gameObject.AddComponent<MenuButtonDecoration>();
            Stretch(trim.rectTransform); trim.Symbol = symbol; trim.Brackets = h >= 60;
            if (symbol != null)
            {
                label.rectTransform.anchoredPosition = new Vector2(h*1.35f,0);
                label.rectTransform.sizeDelta = new Vector2(w-h*1.35f-25,h);
                label.alignment = TextAnchor.MiddleLeft;
                if (text == "↺") { label.text = ""; trim.IconOnly = true; }
            }
            if (click != null) button.onClick.AddListener(() => click());
            // Placeholder fasteners and edge strip: no baked-in labels or screenshot backgrounds.
            if (!textured) Panel(plate.transform, "Left edge", 0, 8, 3, h - 16, primary ? Amber : new Color(.35f, .29f, .2f)).raycastTarget = false;
            return button;
        }
        public static Slider Slider(Transform parent, float y, string title, float value, Action<float> changed)
        {
            Label(parent, title, 24, y, 345, 42, 23);
            var root = Rect(parent, title + " slider", 390, y + 10, 470, 26);
            var track = Panel(root, "Track", 0, 8, 470, 10, new Color(.17f, .17f, .16f));
            var fillArea = Rect(root, "Fill area", 0, 8, 470, 10);
            var fill = Panel(fillArea, "Fill", 0, 0, 470, 10, Amber);
            Stretch(fill.rectTransform);
            var handle = Panel(root, "Handle", 0, 0, 14, 26, Paper);
            var slider = root.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            handle.rectTransform.sizeDelta = new Vector2(14, 0);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            var number = Label(parent, "", 890, y, 95, 42, 22);
            slider.SetValueWithoutNotify(value); number.text = Mathf.RoundToInt(value * 100) + "%";
            slider.onValueChanged.AddListener(v => { number.text = Mathf.RoundToInt(v * 100) + "%"; changed(v); });
            return slider;
        }
        public static InputField Input(Transform parent, string value, float x, float y, float w, float h, int limit)
        {
            var panel = Panel(parent, "Name field", x, y, w, h, new Color(.025f, .028f, .03f));
            var text = Label(panel.transform, "", 14, 0, w - 28, h, 28);
            var input = panel.gameObject.AddComponent<InputField>(); input.textComponent = text;
            input.targetGraphic = panel; input.characterLimit = limit; input.lineType = InputField.LineType.SingleLine;
            input.text = value; input.caretColor = Amber; input.customCaretColor = true; return input;
        }
        public static Dropdown Dropdown(Transform parent, float y, string title)
        {
            Label(parent, title, 24, y, 345, 44, 23);
            var plate = Panel(parent, title + " dropdown", 390, y, 590, 44, Ink);
            var d = plate.gameObject.AddComponent<Dropdown>(); d.targetGraphic = plate;
            d.captionText = Label(plate.transform, "", 12, 0, 535, 44, 21);
            Label(plate.transform, "v", 552, 0, 28, 44, 20);
            var template = Panel(plate.transform, "Template", 0, 44, 590, 220, new Color(.08f, .085f, .09f));
            var viewport = Rect(template.transform, "Viewport", 0, 0, 590, 220);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", 0, 0, 590, 42);
            var item = Panel(content, "Item", 0, 0, 590, 42, Ink);
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item;
            var check = Label(item.transform, ">", 10, 0, 25, 42, 22); toggle.graphic = check;
            d.itemText = Label(item.transform, "Device", 40, 0, 530, 42, 20);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            d.template = template.rectTransform; template.gameObject.SetActive(false);
            return d;
        }
    }
}
