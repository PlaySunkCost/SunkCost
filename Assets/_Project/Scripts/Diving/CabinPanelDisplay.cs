using UnityEngine;

namespace SunkCost.Diving
{
    // The car panel's gauge and screen (the new elevator, 28 September 2026), on the
    // "Panel Look" in both cabins. The gauge's marker stands at the car's water level
    // (CabinWater.Level01; always 0 on the deck, which never floods). The screen says
    // what the car is doing (the dive car: its state, its depth and its water) or mirrors
    // the deck cabin's status plate, which WorldSceneFlow.PresentDeckCabin writes every
    // frame (who is missing, the countdown). Everything is derived per frame from the
    // replicated car and the plate: no local-only text or gauge state.
    [DefaultExecutionOrder(100)]
    public sealed class CabinPanelDisplay : MonoBehaviour
    {
        public enum Mode : byte { Car = 0, Deck = 1 }

        // The names the models role gives the panel's empties (docs/ELEVATOR_LOOK.md §2.1, §3).
        public const string GaugeBottomName = "Gauge Bottom";
        public const string GaugeTopName = "Gauge Top";
        public const string GaugeMarkerName = "Gauge Marker";
        public const string GaugeFillName = "Gauge Fill";
        public const string ScreenAnchorName = "Screen Anchor";
        public const string ScreenTextName = "Screen Text";

        [SerializeField] private Mode mode;
        [SerializeField] private CabinWater water;     // the car's; null on the deck
        [SerializeField] private TextMesh mirror;      // the deck's status plate; null in the car
        [SerializeField] private TextMesh screen;
        [SerializeField] private Transform gaugeBottom, gaugeTop, gaugeMarker, gaugeFill;
        // The word the button used to carry now heads the screen (docs/ELEVATOR_LOOK.md §3).
        [SerializeField] private string carWord = "SURFACE";
        [SerializeField] private string deckWord = "DESCEND";
        [SerializeField] private float floorTop = 0.10f;
        // The screen's layout (DECK-PANEL-TEXT, 28 September 2026): the words are wrapped to
        // short lines and fitted to the screen here, so a long plate line ("Day 1 of 3 — all
        // in, press E to descend") no longer shrinks the whole text to a few pixels or runs
        // off the curved screen. The largest letters: about 5 cm lines (the TextMesh's
        // characterSize at fontSize 64); the box stays inside the ~0.42 x 0.24 m screen.
        [SerializeField] private int screenLineChars = 16;
        [SerializeField] private float screenCharacterSize = 0.0075f;
        [SerializeField] private Vector2 screenBox = new(0.36f, 0.20f);
        private int fitFrames;        // refit on the frame after a write too (the mesh's bounds settle)
        private int lastState = -1, lastDepth = -1, lastPercent = -1;
        private CarWaterFlow lastFlow;
        private string lastPlate;
        private string pushedPlate;   // the plate's words as WorldSceneFlow writes them (Deck mode)
        private bool platePushed;
        private string written;       // the last string given to the screen's TextMesh

        public Mode DisplayMode => mode;
        public float GaugeFraction { get; private set; }
        public string ScreenText { get; private set; } = string.Empty;

        public void Configure(Mode how, CabinWater cabinWater, TextMesh plate)
        {
            mode = how;
            water = how == Mode.Car ? cabinWater : null;
            mirror = how == Mode.Deck ? plate : null;
            Resolve();
        }

        public void SetScreen(TextMesh text) { screen = text; written = null; ReleaseGenericFit(); }

        // WorldSceneFlow.PresentDeckCabin hands the plate's words over when it writes them,
        // so the mirror never reads TextMesh.text (a native getter that allocates) per frame.
        public void SetPlateText(string plate)
        {
            pushedPlate = plate ?? string.Empty;
            platePushed = true;
        }

        private void Awake()
        {
            Resolve();
            ReleaseGenericFit();
        }

        // A screen built before this display laid it out itself carries the kit's PlateText
        // (one line fitted whole); it is switched off so the two never fight over the size.
        private void ReleaseGenericFit()
        {
            if (!Application.isPlaying || screen == null) return;
            SunkCost.Look.PlateText generic = screen.GetComponent<SunkCost.Look.PlateText>();
            if (generic != null && generic.enabled) generic.enabled = false;
        }

        // The empties by name under this panel, when the serialized ones are missing.
        private void Resolve()
        {
            if (gaugeBottom == null) gaugeBottom = FindDeep(transform, GaugeBottomName);
            if (gaugeTop == null) gaugeTop = FindDeep(transform, GaugeTopName);
            if (gaugeMarker == null) gaugeMarker = FindDeep(transform, GaugeMarkerName);
            if (gaugeFill == null) gaugeFill = FindDeep(transform, GaugeFillName);
            if (screen == null) { Transform text = FindDeep(transform, ScreenTextName); if (text != null) screen = text.GetComponent<TextMesh>(); }
        }

        private void LateUpdate()
        {
            GaugeFraction = mode == Mode.Car && water != null ? water.Level01 : 0f;
            if (gaugeMarker != null && gaugeBottom != null && gaugeTop != null)
                gaugeMarker.position = Vector3.Lerp(gaugeBottom.position, gaugeTop.position, GaugeFraction);
            if (gaugeFill != null)
            {
                // The fill's mesh is the whole gauge tall with its pivot at the bottom
                // (ElevatorLook.PlacePanel), so its y scale is the fraction itself.
                Vector3 scale = gaugeFill.localScale;
                float y = Mathf.Max(0.0001f, GaugeFraction);
                if (!Mathf.Approximately(scale.y, y)) gaugeFill.localScale = new Vector3(scale.x, y, scale.z);
            }

            string text = mode == Mode.Deck ? DeckText() : CarText();
            if (text != ScreenText) ScreenText = text;
            if (screen != null && written != ScreenText)
            {
                written = ScreenText;
                screen.text = Layout(ScreenText, screenLineChars);
                fitFrames = 2;
            }
            if (screen != null && fitFrames > 0)
            {
                fitFrames--;
                SunkCost.Look.TextFit.Fit(screen, screenBox, screenCharacterSize);
            }
        }

        // The screen's lines: every line longer than `maxChars` is broken first where its
        // writer spaced it apart ("DEPTH 12 m   WATER 40%"), at " — " and after ": ", then
        // between words. ScreenText (what the tests and peers compare) keeps the words as
        // written; only the drawn text is wrapped. Runs only when the words change.
        public static string Layout(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return text ?? string.Empty;
            var result = new System.Text.StringBuilder(text.Length + 8);
            foreach (string line in text.Split('\n'))
            {
                if (line.Length <= maxChars) { Append(result, line); continue; }
                string marked = line.Replace("   ", "\n").Replace(" — ", "\n").Replace(": ", ":\n");
                foreach (string part in marked.Split('\n'))
                {
                    string piece = part.Trim();
                    if (piece.Length == 0) continue;
                    if (piece.Length <= maxChars) { Append(result, piece); continue; }
                    var current = new System.Text.StringBuilder();
                    foreach (string word in piece.Split(' '))
                    {
                        if (word.Length == 0) continue;
                        if (current.Length > 0 && current.Length + 1 + word.Length > maxChars) { Append(result, current.ToString()); current.Clear(); }
                        if (current.Length > 0) current.Append(' ');
                        current.Append(word);
                    }
                    if (current.Length > 0) Append(result, current.ToString());
                }
            }
            return result.ToString();
        }

        private static void Append(System.Text.StringBuilder result, string line)
        {
            if (result.Length > 0) result.Append('\n');
            result.Append(line);
        }

        private string DeckText()
        {
            // Without a flow (a scene opened in the editor) the plate is read directly.
            string plate = platePushed ? pushedPlate : mirror != null ? mirror.text : string.Empty;
            if (plate == lastPlate && ScreenText.Length > 0) return ScreenText;
            lastPlate = plate;
            return string.IsNullOrEmpty(plate) ? deckWord : deckWord + "\n" + plate;
        }

        // Rebuilt only when a shown number changes (no garbage per frame).
        private string CarText()
        {
            ElevatorController car = water != null ? water.Controller : null;
            if (car == null) return ScreenText;
            int state = (int)car.State;
            float floorY = car.transform.position.y + floorTop;
            int depth = Mathf.Max(0, Mathf.RoundToInt(car.SeaLevelY - floorY));
            int percent = Mathf.RoundToInt(water.Level01 * 100f);
            CarWaterFlow flow = water.Flow;
            if (state == lastState && depth == lastDepth && percent == lastPercent && flow == lastFlow) return ScreenText;
            lastState = state; lastDepth = depth; lastPercent = percent; lastFlow = flow;
            return carWord + "\n" + CarLine(car.State, car.Upward, flow) + "\n" +$"DEPTH {depth} m   WATER {percent}%";
        }

        public static string CarLine(ElevatorState state, bool upward, CarWaterFlow flow)
        {
            if (flow == CarWaterFlow.Filling) return "FLOODING";
            if (flow == CarWaterFlow.Draining) return "DRAINING";
            return state switch
            {
                ElevatorState.AtTop => "AT THE SURFACE",
                ElevatorState.Sealing => upward ? "SEALED — GOING UP" : "SEALED — GOING DOWN",
                ElevatorState.Descending => "DESCENDING",
                ElevatorState.AtBottom => "ON THE SEABED",
                ElevatorState.Ascending => "ASCENDING",
                _ => string.Empty
            };
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
