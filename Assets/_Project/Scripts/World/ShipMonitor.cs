using SunkCost.Look;
using UnityEngine;

namespace SunkCost.World
{
    // The screen above the buttons, on both ship instances. Purely a display:
    // it reads the day state (phase, the last refusal) and writes the status
    // label every peer sees. The spectator card later doubles it as the TV.
    //
    // Text is the status as one line (the checks read it). The screen shows it as
    // a small layout in the ship's screen style (ship audit SHIP-044/052/064, 23
    // September 2026): the site and the day over a rule, the state large, a hint
    // under it without the E key (the HUD carries keys), the quota at the foot. It
    // is laid out on the monitor's real face, measured from its mesh, wherever the
    // console dressing put it and however big it made it.
    public sealed class ShipMonitor : MonoBehaviour
    {
        public const string DisplayName = "Monitor Display";
        private const float DisplaySeconds = 0.25f;

        private TextMesh status;
        private WorldLoopSettings settings;
        private TextMesh site, dayLine, hint, quota, brand;
        private Vector2 stateBox, hintBox;
        private float stateSize, hintSize;
        private float nextDisplay;
        private string shownDisplay;

        public string Text { get; private set; } = string.Empty;

        private void Awake()
        {
            ShipParts ship = GetComponentInParent<ShipParts>();
            Transform label = ship != null ? ship.Find(ShipParts.MonitorStatusName) : null;
            status = label != null ? label.GetComponent<TextMesh>() : null;
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            EnsureDisplay();
            ShowDisplay(true);
            // The crew screen beside it has a job now (SHIP-045); a ship built before it did gets one here.
            Transform crew = ship != null ? ship.Find(CrewScreen.TextObjectName) : null;
            if (crew != null && crew.GetComponent<CrewScreen>() == null) crew.gameObject.AddComponent<CrewScreen>();
        }

        private void Update()
        {
            string text = Compose();
            bool changed = text != Text;
            Text = text;
            if (!changed && Time.unscaledTime < nextDisplay) return;
            ShowDisplay(changed);
        }

        // ---- the screen ----------------------------------------------------------------

        // The layout on the monitor's face: built by the ship's builder
        // (ShipScreens) and again here, from the same numbers, if it is missing.
        public void EnsureDisplay()
        {
            ShipParts ship = GetComponentInParent<ShipParts>();
            if (status == null)
            {
                Transform label = ship != null ? ship.Find(ShipParts.MonitorStatusName) : null;
                status = label != null ? label.GetComponent<TextMesh>() : null;
            }
            if (!Face(transform, ship != null ? ship.transform : transform.root, out Vector3 centre, out Quaternion facing, out float w, out float h)) return;
            ScreenStyle.Paint(GetComponent<Renderer>(), ScreenStyle.Back);
            // Found anywhere on the ship (the hierarchy may group it after the build), made beside the monitor.
            Transform root = ship != null ? ship.Find(DisplayName) : null;
            if (root == null) root = ScreenStyle.Child(transform.parent, DisplayName, out _);
            root.SetPositionAndRotation(centre, facing);
            root.localScale = Vector3.one;
            Material textMaterial = ScreenStyle.TextMaterialOf(status);
            float m = ScreenStyle.Margin * Mathf.Min(w, h);
            float title = ScreenStyle.TitleLine * h, value = ScreenStyle.ValueLine * h, small = ScreenStyle.HintLine * h;
            float top = h / 2f - m - title / 2f, left = w / 2f - m; // the reader's left is the display's +X (it faces them)
            site = ScreenStyle.Line(root, "Site", new Vector3(left, top, 0.004f), title, ScreenStyle.Accent, TextAnchor.MiddleLeft, textMaterial);
            dayLine = ScreenStyle.Line(root, "Day", new Vector3(-left, top, 0.004f), title, ScreenStyle.Accent, TextAnchor.MiddleRight, textMaterial);
            ScreenStyle.Quad(root, "Rule", new Vector3(0f, top - title * 0.75f, 0.002f), w - 2f * m, Mathf.Max(0.006f, h * 0.008f), Color.Lerp(ScreenStyle.Track, ScreenStyle.Accent, 0.5f));
            float bottom = -h / 2f + m + small / 2f;
            quota = ScreenStyle.Line(root, "Quota", new Vector3(left, bottom, 0.004f), small, ScreenStyle.Dim, TextAnchor.MiddleLeft, textMaterial);
            brand = ScreenStyle.Line(root, "Brand", new Vector3(-left, bottom, 0.004f), small, ScreenStyle.Dim, TextAnchor.MiddleRight, textMaterial);
            hint = ScreenStyle.Line(root, "Hint", new Vector3(0f, bottom + small * 1.9f, 0.004f), small * 1.15f, ScreenStyle.Dim, TextAnchor.MiddleCenter, textMaterial);
            // The state: the MonitorStatus label itself, moved into its slot between the rule and the hint.
            if (status != null)
            {
                float mid = ((top - title) + (bottom + small * 2.6f)) / 2f;
                status.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(0f, mid, 0.004f)), facing * Quaternion.Euler(0f, 180f, 0f));
                status.anchor = TextAnchor.MiddleCenter;
                status.alignment = TextAlignment.Center;
                status.fontSize = 64;
                status.fontStyle = FontStyle.Bold;
                status.richText = true;
                status.color = ScreenStyle.Text;
                stateSize = value * 0.1f;
                stateBox = new Vector2(w - 2f * m, (top - title) - (bottom + small * 2.6f));
            }
            hintSize = small * 1.15f * 0.1f;
            hintBox = new Vector2(w - 2f * m, small * 1.6f);
        }

        // What the builder shows in the editor: the idle screen (SHIP-064).
        public void ShowIdle()
        {
            settings ??= WorldLoopSettings.Resolve(null);
            ShowDisplay(true);
        }

        // The face a reader sees: of the mesh's two faces across its thinnest
        // axis (z), the one toward the ship's bow side, where the crew stands.
        public static bool Face(Transform screen, Transform ship, out Vector3 centre, out Quaternion facing, out float w, out float h)
        {
            centre = default; facing = default; w = h = 0f;
            MeshFilter filter = screen.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            Bounds b = filter.sharedMesh.bounds;
            Vector3 normal = screen.forward;
            bool back = Vector3.Dot(normal, ship.forward) < 0f;
            if (back) normal = -normal;
            centre = screen.TransformPoint(new Vector3(b.center.x, b.center.y, back ? b.min.z : b.max.z)) + normal * 0.003f;
            facing = Quaternion.LookRotation(normal, screen.up);
            Vector3 scale = screen.lossyScale, shipScale = ship.lossyScale;
            w = b.size.x * Mathf.Abs(scale.x / shipScale.x);
            h = b.size.y * Mathf.Abs(scale.y / shipScale.y);
            return w > 0.01f && h > 0.01f;
        }

        private void ShowDisplay(bool force)
        {
            nextDisplay = Time.unscaledTime + DisplaySeconds;
            ComposeDisplay(out string where, out string day, out string state, out string next, out string money, out Color moneyColour);
            string all = where + "|" + day + "|" + state + "|" + next + "|" + money;
            if (!force && all == shownDisplay) return;
            shownDisplay = all;
            Set(site, where);
            Set(dayLine, day);
            Set(hint, next);
            if (hint != null && hintSize > 0f) TextFit.Fit(hint, hintBox, hintSize);
            Set(quota, money);
            if (quota != null) quota.color = moneyColour;
            Set(brand, CrewDayState.Instance != null ? HQSigns.Resolve().Get("company") : string.Empty); // idle, the name heads the screen instead
            if (status != null)
            {
                if (status.text != state) status.text = state;
                if (stateSize > 0f) TextFit.Fit(status, stateBox, stateSize);
            }
        }
        private static void Set(TextMesh mesh, string text)
        {
            if (mesh != null && mesh.text != text) mesh.text = text;
        }

        // What the screen shows: the same states as Text, as a layout.
        private void ComposeDisplay(out string where, out string day, out string state, out string next, out string money, out Color moneyColour)
        {
            CrewDayState d = CrewDayState.Instance;
            where = string.Empty; day = string.Empty; next = string.Empty; money = string.Empty; moneyColour = ScreenStyle.Dim;
            if (d == null) { where = HQSigns.Resolve().Get("company"); state = "STANDBY"; next = "Waiting for the crew"; return; }
            int days = settings.DaysPerCycle;
            where = d.World == WorldId.HQ ? "HQ" : "SITE 01";
            day = d.Payday ? "PAYDAY" : d.Day > 0 ? $"DAY {d.Day}/{days}" : string.Empty;
            int had = d.CycleSales + d.BoxValue, quotaTotal = settings.QuotaPerCycle;
            money = $"QUOTA ${had} / ${quotaTotal}";
            moneyColour = had >= quotaTotal && quotaTotal > 0 ? ScreenStyle.Good : ScreenStyle.Warn;
            if (Time.unscaledTime - d.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(d.LastRefusal.Text))
            {
                state = d.LastRefusal.Text;
                next = string.Empty;
                return;
            }
            ShipDepartureState trip = d.Departure;
            if (trip.Active)
            {
                switch (trip.Stage)
                {
                    case DepartureStage.Preparing: state = "ALL ABOARD"; next = "Hold on"; return;
                    case DepartureStage.RaisingGangway: state = "CASTING OFF"; next = trip.ToWorld == WorldId.HQ ? "Bound for HQ" : "Bound for Site 01"; return;
                    case DepartureStage.Arriving: state = trip.ToWorld == WorldId.HQ ? "DOCKED" : "ARRIVED"; next = trip.ToWorld == WorldId.HQ ? "Making fast" : "Site 01"; return;
                    default: state = "SAILING"; next = trip.ToWorld == WorldId.HQ ? "Bound for HQ" : "Bound for Site 01"; return;
                }
            }
            switch (d.Phase)
            {
                case DayPhase.Sailing: state = "SAILING"; next = "Bound for Site 01"; return;
                case DayPhase.SailingHome: state = "SAILING"; next = "Bound for HQ"; return;
                case DayPhase.AtSea:
                    if (d.Payday) { state = "PAYDAY"; next = "HQ · sail home to pay"; return; }
                    if (d.DiveDone) { state = "DIVE DONE"; next = "END DAY · close the day"; return; }
                    state = "ON SITE"; next = "Dive from the cage · HQ sails home"; return;
                case DayPhase.DiveInProgress:
                    state = "DIVE IN PROGRESS"; next = $"{d.Below.Count} below · controls locked"; return;
                default:
                    if (d.Payday) { state = "PAYDAY"; next = "Pay the quota at the board"; return; }
                    state = "DOCKED"; next = "SITE 01 · sail to the dive"; return;
            }
        }

        // ---- the status line (the checks read it) -------------------------------------

        private string Compose()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return string.Empty;
            if (Time.unscaledTime - day.LastRefusalAt < settings.RefusalDisplaySeconds && !string.IsNullOrEmpty(day.LastRefusal.Text))
                return day.LastRefusal.Text;
            ShipDepartureState trip = day.Departure;
            if (trip.Active)
            {
                string where = trip.ToWorld == WorldId.HQ ? "home" : "to Site 01";
                switch (trip.Stage)
                {
                    case DepartureStage.Preparing: return "All aboard — hold on";
                    case DepartureStage.RaisingGangway: return "Casting off";
                    case DepartureStage.Arriving: return trip.ToWorld == WorldId.HQ ? "Docked — making fast" : "Arrived at Site 01";
                    default: return "Sailing " + where + "…";
                }
            }
            switch (day.Phase)
            {
                case DayPhase.Sailing: return "Sailing to Site 01…";
                case DayPhase.SailingHome: return "Sailing home…";
                case DayPhase.AtSea:
                    if (day.Payday) return "PAYDAY — E on HQ to sail home";
                    if (day.DiveDone) return $"Day {day.Day} of {settings.DaysPerCycle} — dive done — E on END DAY";
                    return $"Day {day.Day} of {settings.DaysPerCycle} — Site 01 — E on HQ to sail home";
                case DayPhase.DiveInProgress: return $"Day {day.Day} of {settings.DaysPerCycle} — dive in progress — monitor locked";
                default:
                    if (day.Payday) return "Docked at HQ — PAYDAY: pay the quota at the board";
                    return day.Day > 0 ? $"Docked at HQ — day {day.Day} of {settings.DaysPerCycle} — E on Site 01 to sail" : "Docked at HQ — E on Site 01 to sail";
            }
        }
    }
}
