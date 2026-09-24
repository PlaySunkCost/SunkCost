using System.Text.RegularExpressions;
using UnityEngine;

namespace SunkCost.Look
{
    // The words on a sign plate the game writes at runtime (a status, a caption, a
    // price), fitted to the plate (ship audit SHIP-018: "Dive in progress - 3
    // below: Alice, Bob, Carol" ran off the cabin's 2 m plate). Fit shrinks the
    // label itself. StateHint leaves the label's text exactly as its writer set it
    // - the flows and the checks read it - hides it, and draws it on a child
    // "Display" as the ship's screens do: the state in capitals over a smaller,
    // dim hint (split at the first " — "), the E key left to the HUD (SHIP-052),
    // the names swapped for a count when they would not read, and an idle line
    // when the writer says nothing (SHIP-064).
    [RequireComponent(typeof(TextMesh))]
    public sealed class PlateText : MonoBehaviour
    {
        public enum Mode : byte { Fit = 0, StateHint = 1 }
        public const string DisplayName = "Display";
        private const float HintScale = 0.62f;     // the hint's size against the state's
        private const float NamesMinimum = 0.72f;  // below this share of the full size, names give way to a count
        private static readonly Regex NamesList = new(@"^(\d+ below): .+$");
        private static readonly Regex PressE = new(@"(, )?[Pp]ress E to ");

        [SerializeField] private Vector2 box;
        [SerializeField] private Mode mode;
        [SerializeField] private string idleKey;
        [SerializeField] private float maxSize;
        private TextMesh source, display;
        private string shown;

        public string Shown => display != null ? display.text : (source != null ? source.text : string.Empty);

        public void Configure(Vector2 fitBox, Mode how, string idle)
        {
            box = fitBox;
            mode = how;
            idleKey = idle;
            source = GetComponent<TextMesh>();
            if (maxSize <= 0f) maxSize = source.characterSize;
            shown = null;
            Refresh();
        }

        private void Awake() => source = GetComponent<TextMesh>();

        // After the writers' Update (WorldSceneFlow presents the cabin in its own).
        private void LateUpdate()
        {
            if (source == null) return;
            if (source.text != shown) Refresh();
        }

        public void Refresh()
        {
            if (source == null) source = GetComponent<TextMesh>();
            if (maxSize <= 0f) maxSize = source.characterSize;
            shown = source.text;
            if (mode == Mode.Fit)
            {
                TextFit.Fit(source, box, maxSize);
                return;
            }
            EnsureDisplay();
            Renderer own = source.GetComponent<Renderer>();
            if (own != null && own.enabled) own.enabled = false;
            string text = source.text;
            bool idle = string.IsNullOrEmpty(text);
            if (idle) text = HQSigns.Resolve().Get(idleKey);
            SplitStateHint(text, out string state, out string hint);
            display.color = idle ? ScreenStyle.Accent : ScreenStyle.Text;
            display.text = Compose(state, hint);
            float size = TextFit.Fit(display, box, maxSize);
            // Too many names to read: the count says it (SHIP-018).
            Match names = NamesList.Match(hint);
            if (names.Success && size < maxSize * NamesMinimum)
            {
                display.text = Compose(state, names.Groups[1].Value);
                TextFit.Fit(display, box, maxSize);
            }
        }

        // "Day 1 of 3 — all in, press E to descend" -> "DAY 1 OF 3" over "all in · descend".
        public static void SplitStateHint(string text, out string state, out string hint)
        {
            text ??= string.Empty;
            int cut = text.IndexOf(" — ", System.StringComparison.Ordinal);
            if (cut < 0) cut = text.IndexOf('\n');
            if (cut < 0) { state = text; hint = string.Empty; }
            else
            {
                state = text.Substring(0, cut);
                hint = text.Substring(cut + (text[cut] == '\n' ? 1 : 3));
            }
            state = state.Trim().ToUpperInvariant();
            hint = PressE.Replace(hint.Trim(), m => m.Groups[1].Success ? " · " : string.Empty);
        }

        private string Compose(string state, string hint)
        {
            if (string.IsNullOrEmpty(hint)) return state;
            int px = Mathf.RoundToInt(display.fontSize * HintScale);
            return state + "\n<size=" + px + ">" + ScreenStyle.Tint(hint, ScreenStyle.Dim) + "</size>";
        }

        private void EnsureDisplay()
        {
            if (display != null) return;
            Transform existing = transform.Find(DisplayName);
            display = existing != null ? existing.GetComponent<TextMesh>() : null;
            if (display != null) return;
            GameObject go = new(DisplayName, typeof(TextMesh));
            go.transform.SetParent(transform, false);
            display = go.GetComponent<TextMesh>();
            display.font = source.font;
            display.fontSize = source.fontSize;
            display.fontStyle = source.fontStyle;
            display.characterSize = maxSize;
            display.anchor = TextAnchor.MiddleCenter;
            display.alignment = TextAlignment.Center;
            display.richText = true;
            Material material = ScreenStyle.TextMaterialOf(source);
            if (material != null) go.AddComponent<DepthText>().Configure(material);
        }
    }
}
