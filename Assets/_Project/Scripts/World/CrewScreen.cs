using System.Collections.Generic;
using SunkCost.Look;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The crew's screen on the tower face, starboard of the console (ship audit
    // SHIP-045, 23 September 2026: it said "GOOD HAULS TODAY :)"). Now it has a
    // job: who is where - on deck, in the cage, below, down - with each diver's
    // colour, the day, and the quota bar. Read on this peer from replicated state
    // (CrewDayState's riders, below and dead lists; each player's PlayerIdentity),
    // so every screen that sees it shows the same. Decides nothing.
    public sealed class CrewScreen : MonoBehaviour
    {
        public const string ScreenName = "Crew Screen";      // the glass it lies on
        public const string TextObjectName = "Crew Screen Text";
        private const int Rows = 4; // the ship spawns four
        private const float RefreshSeconds = 0.5f;

        private readonly TextMesh[] names = new TextMesh[Rows];
        private readonly TextMesh[] states = new TextMesh[Rows];
        private TextMesh title, day, quota;
        private Transform barFill;
        private Vector3 barCentre;
        private float barWidth, barHeight, rowSize;
        private Vector2 rowBox;
        private float nextRefresh;
        private WorldLoopSettings settings;
        private readonly List<(int id, string name, string colour)> crew = new();

        private void Awake()
        {
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            EnsureDisplay();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Show();
        }

        // Laid out on the crew screen's glass (built by ShipScreens; rebuilt here if missing).
        public void EnsureDisplay()
        {
            ShipParts ship = GetComponentInParent<ShipParts>();
            Transform glass = ship != null ? ship.Find(ScreenName) : null;
            if (glass == null || !ShipMonitor.Face(glass, ship.transform, out Vector3 centre, out Quaternion facing, out float w, out float h)) return;
            ScreenStyle.Paint(glass.GetComponent<Renderer>(), ScreenStyle.Back);
            transform.SetPositionAndRotation(centre, facing);
            // The old line (a SignText on "ship.screen") gives way to the layout; the key is the title now.
            Transform old = transform.Find("Text");
            Material textMaterial = ScreenStyle.TextMaterialOf(old != null ? old : null);
            if (old != null)
            {
                SignText sign = old.GetComponent<SignText>();
                if (sign != null) { if (Application.isPlaying) Destroy(sign); else DestroyImmediate(sign); }
                old.gameObject.SetActive(false);
            }
            float m = ScreenStyle.Margin * Mathf.Min(w, h);
            float line = ScreenStyle.TitleLine * h, left = w / 2f - m;
            float top = h / 2f - m - line / 2f;
            title = ScreenStyle.Line(transform, "Title", new Vector3(left, top, 0.004f), line, ScreenStyle.Accent, TextAnchor.MiddleLeft, textMaterial);
            day = ScreenStyle.Line(transform, "Day", new Vector3(-left, top, 0.004f), line, ScreenStyle.Accent, TextAnchor.MiddleRight, textMaterial);
            ScreenStyle.Quad(transform, "Rule", new Vector3(0f, top - line * 0.75f, 0.002f), w - 2f * m, Mathf.Max(0.006f, h * 0.008f), Color.Lerp(ScreenStyle.Track, ScreenStyle.Accent, 0.5f));
            float small = ScreenStyle.HintLine * 0.8f * h;
            float bottom = -h / 2f + m + small / 2f;
            quota = ScreenStyle.Line(transform, "Quota", new Vector3(left, bottom, 0.004f), small, ScreenStyle.Dim, TextAnchor.MiddleLeft, textMaterial);
            barWidth = (w - 2f * m) * 0.5f;
            barHeight = small * 0.55f;
            barCentre = new Vector3(-left + barWidth / 2f, bottom, 0.002f);
            ScreenStyle.Quad(transform, "Bar", barCentre, barWidth, barHeight, ScreenStyle.Track);
            barFill = ScreenStyle.Quad(transform, "Bar Fill", barCentre + new Vector3(0f, 0f, 0.001f), barWidth, barHeight, ScreenStyle.Warn).transform;
            float listTop = top - line * 1.1f, listBottom = bottom + small;
            float pitch = (listTop - listBottom) / Rows;
            rowSize = Mathf.Min(pitch * 0.75f, ScreenStyle.HintLine * 1.6f * h) * 0.1f;
            rowBox = new Vector2(w * 0.55f, pitch * 0.9f);
            for (int i = 0; i < Rows; i++)
            {
                float y = listTop - pitch * (i + 0.5f);
                names[i] = ScreenStyle.Line(transform, "Name " + (i + 1), new Vector3(left, y, 0.004f), rowSize * 10f, ScreenStyle.Text, TextAnchor.MiddleLeft, textMaterial);
                states[i] = ScreenStyle.Line(transform, "State " + (i + 1), new Vector3(-left, y, 0.004f), rowSize * 10f, ScreenStyle.Dim, TextAnchor.MiddleRight, textMaterial);
            }
        }

        // What the builder shows in the editor: the idle screen (SHIP-064).
        public void ShowIdle()
        {
            settings ??= WorldLoopSettings.Resolve(null);
            Show();
        }

        private void Show()
        {
            CrewDayState d = CrewDayState.Instance;
            Set(title, HQSigns.Resolve().Get("ship.screen"));
            int quotaTotal = settings != null ? settings.QuotaPerCycle : 0;
            int had = d != null ? d.CycleSales + d.BoxValue : 0;
            Set(day, d == null ? string.Empty : d.Payday ? "PAYDAY" : d.Day > 0 ? $"DAY {d.Day}/{(settings != null ? settings.DaysPerCycle : 3)}" : "DOCKED");
            Set(quota, d == null ? "QUOTA" : $"QUOTA ${had} / ${quotaTotal}");
            bool met = quotaTotal > 0 && had >= quotaTotal;
            if (quota != null) quota.color = d == null ? ScreenStyle.Dim : met ? ScreenStyle.Good : ScreenStyle.Warn;
            if (barFill != null)
            {
                ScreenStyle.SetBar(barFill, barCentre + new Vector3(0f, 0f, 0.001f), barWidth, barHeight, quotaTotal > 0 ? (float)had / quotaTotal : 0f);
                ScreenStyle.Paint(barFill.GetComponent<Renderer>(), met ? ScreenStyle.Good : ScreenStyle.Warn);
            }
            Crew(d);
            for (int i = 0; i < Rows; i++)
            {
                if (names[i] == null || states[i] == null) continue;
                if (i >= crew.Count)
                {
                    bool idleRow = i == 0 && crew.Count == 0;
                    Set(names[i], idleRow ? "No crew aboard" : string.Empty);
                    names[i].color = ScreenStyle.Dim;
                    Set(states[i], string.Empty);
                    continue;
                }
                (int id, string name, string colour) = crew[i];
                Set(names[i], $"<color={colour}>●</color>  {name.ToUpperInvariant()}");
                names[i].color = ScreenStyle.Text;
                StateOf(d, id, out string state, out Color stateColour);
                Set(states[i], state);
                states[i].color = stateColour;
                TextFit.Fit(names[i], rowBox, rowSize);
            }
        }

        // Everyone this peer knows: each spawned player, and anyone the day state names below or dead.
        private void Crew(CrewDayState d)
        {
            crew.Clear();
            if (!Application.isPlaying) return;
            foreach (PlayerIdentity identity in FindObjectsByType<PlayerIdentity>(FindObjectsInactive.Exclude))
                if (identity.IsSpawned && !Has(identity.OwnerId)) crew.Add((identity.OwnerId, identity.DisplayName, identity.ColourHex));
            if (d != null)
            {
                foreach (int id in d.Below) if (!Has(id)) crew.Add((id, WorldSceneFlow.DisplayName(id), ScreenStyle.Hex(ScreenStyle.Dim)));
                foreach (int id in d.Dead) if (!Has(id)) crew.Add((id, WorldSceneFlow.DisplayName(id), ScreenStyle.Hex(ScreenStyle.Dim)));
            }
            crew.Sort((a, b) => a.id.CompareTo(b.id));
        }

        private bool Has(int id)
        {
            foreach ((int id, string name, string colour) entry in crew) if (entry.id == id) return true;
            return false;
        }

        private static void StateOf(CrewDayState d, int id, out string state, out Color colour)
        {
            if (d == null) { state = string.Empty; colour = ScreenStyle.Dim; return; }
            if (d.IsDead(id)) { state = "DOWN"; colour = ScreenStyle.Danger; return; }
            if (d.IsBelow(id)) { state = "BELOW"; colour = ScreenStyle.Accent; return; }
            if (d.IsRider(id)) { state = "IN THE CAGE"; colour = ScreenStyle.Warn; return; }
            state = d.World == WorldId.HQ ? "AT HQ" : "ON DECK";
            colour = ScreenStyle.Good;
        }

        private static void Set(TextMesh mesh, string text)
        {
            if (mesh != null && mesh.text != text) mesh.text = text;
        }
    }
}
