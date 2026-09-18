using System;
using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Look
{
    // Every word written on the platform (Dan, 18 September 2026: "all the text
    // should be changeable — there is a big chance I will need to change every
    // text there"): the company name and motto, each booth's sign, the deck
    // markings, the tower's number. One asset in Resources; a SignText in the
    // scene reads its line by key, so a change in the Inspector reaches every
    // board without a rebuild. Missing keys fall back to the defaults below.
    [CreateAssetMenu(fileName = "HQSigns", menuName = "Sunk Cost/HQ Signs")]
    public sealed class HQSigns : ScriptableObject
    {
        public const string ResourceName = "HQSigns";

        [Serializable]
        public struct Line
        {
            public string Key;
            [TextArea(1, 3)] public string Text;
        }

        [SerializeField] private List<Line> lines = new();

        // Bumped whenever the asset changes so SignText knows to re-read.
        public int Version { get; private set; }

        public IReadOnlyList<Line> Lines => lines;

        public string Get(string key)
        {
            foreach (Line line in lines) if (line.Key == key) return line.Text ?? string.Empty;
            return Defaults.TryGetValue(key, out string d) ? d : key;
        }

        // Fill in every default the platform uses without overwriting what Dan wrote.
        public bool EnsureDefaults()
        {
            bool changed = false;
            foreach (KeyValuePair<string, string> kv in Defaults)
            {
                bool found = false;
                foreach (Line line in lines) if (line.Key == kv.Key) { found = true; break; }
                if (!found) { lines.Add(new Line { Key = kv.Key, Text = kv.Value }); changed = true; }
            }
            return changed;
        }

        private void OnValidate() => Version++;

        public static readonly Dictionary<string, string> Defaults = new()
        {
            { "company", "BLACK TIDE" },
            { "company.sub", "SALVAGE CO." },
            { "motto", "SCAVENGE\nSALVAGE\nSURVIVE" },
            { "office", "OFFICE" },
            { "office.sub", "THE COMPANY IS IN" },
            { "upgrades", "UPGRADES" },
            { "upgrades.sub", "FITTED WHILE YOU WAIT" },
            { "gear", "GEAR & SUPPLIES" },
            { "gear.sub", "COLLECT UPSTAIRS" },
            { "intake", "INTAKE / SELL" },
            { "intake.sub", "PAY THE QUOTA HERE" },
            { "checkin", "SAVE / CHECK IN" },
            { "checkin.sub", "YOUR COLOUR · YOUR NAME" },
            { "quota.title", "SALVAGE QUOTAS" },
            { "quota.side", "DEEPER\nFARTHER\nCLEANER\nTOMORROW" },
            { "pickup", "PICKUP" },
            { "pickup.sub", "BOUGHT GEAR LANDS HERE" },
            { "crew.here", "CREW HERE" },
            { "ship.this.way", "CREW SHIP THIS WAY" },
            { "plank", "WALK THE PLANK" },
            { "hq.number", "01" },
            { "hq.number.sub", "HQ" },
            { "tower.motto", "GOOD CREWS\nBAD SEAS\nGREAT FINDS" },
            { "court", "CREW COURT" },
            { "ship.name.sub", "- SALVAGE -" },
            { "ship.year", "2300" },
            { "ship.bridge", "01 » BRIDGE" },
            { "ship.bridge.label", "CAPTAIN / NAVIGATION" },
            { "ship.screen", "GOOD HAULS\nTODAY :)" },
        };

        private static HQSigns loaded;
        private static HQSigns fallback;
        public static HQSigns Resolve()
        {
            if (loaded != null) return loaded;
            loaded = Resources.Load<HQSigns>(ResourceName); // the asset may appear after the first ask (the setup creates it)
            if (loaded != null) return loaded;
            if (fallback == null) { fallback = CreateInstance<HQSigns>(); fallback.hideFlags = HideFlags.HideAndDontSave; fallback.EnsureDefaults(); }
            return fallback;
        }
    }
}
