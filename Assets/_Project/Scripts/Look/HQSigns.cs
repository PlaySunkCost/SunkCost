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
            // office / upgrades / gear and their .sub lines were removed on 8 October 2026
            // (Dan): with the displays gone the signs over the old display areas pointed at nothing.
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
            { "ship.screen", "CREW" }, // the crew screen's title over who is where, the day and the quota (ship audit SHIP-045)
            { "ship.cabin.idle", "DIVE CAGE — standing by" }, // the cabin's plate before there is a day state (SHIP-064)
            { "ship.tv.idle", "SALVAGE · CREW CHANNEL" },    // the TV's idle channel, under the ship's name (SHIP-046)
            { "ship.tv.idle.hint", "Live when a diver is below" },
            { "button.site01", "SITE 01" },
            { "button.hq", "HQ" },
            { "button.endday", "END DAY" },
            { "button.pay", "PAY" },
            { "button.giveup", "GIVE UP" },
            { "board.title", "QUOTA BOARD" },
            // The shared console's words (27 September 2026): the screens' titles and
            // headings, the lever sign per action ({0} in lever.unlock is the price), the
            // GIVE UP card's lines.
            { "nav.title", "NAVIGATION" },
            { "nav.selected", "SELECTED DESTINATION" },
            { "nav.select", "SELECT A DESTINATION" },
            { "nav.sailing", "SAILING" },
            { "nav.dive", "DIVE IN PROGRESS" },
            { "lever.confirm", "CONFIRM" },
            { "lever.unlock", "UNLOCK ${0}" },
            { "lever.endday", "END DAY" },
            { "lever.pay", "PAY" },
            { "giveup.card", "GIVE UP" },
            { "giveup.foot", "EVERYONE MUST AGREE" },
            { "giveup.voted", "YOU VOTED · E TO TAKE BACK" },
            { "button.descend", "▼" }, // a down arrow on the deck cabin's button (Dan, 19 September 2026)
            { "button.surface", "▲" }, // an up arrow on the car's
            // The generated platform's plates (HQ polish, 27 September 2026: they were literal
            // strings in the layout code, out of this asset's reach).
            { "depot.title", "BLACK TIDE SALVAGE CO.  /  EQUIPMENT DEPOT" }, // the fascia over the depot's front
            { "depot.end", "EQUIPMENT DEPOT" },                                // the east end wall, the first thing the arrivals walk toward
            { "depot.end.sub", "SHOP  ·  UPGRADES  ·  SUPPLIES" },
            { "counter", "EQUIPMENT CATALOGUE" },                              // the catalogue console's top title (8 October 2026)
            { "counter.sub", "BROWSE & BUY  ·  AIM + E" },                      // its lower screen's heading
            { "arrival", "CREW ARRIVAL  /  01–04" },
            { "arrival.colour", "CHOOSE YOUR CREW COLOUR" },                    // the crew colour console's top title
            // The HQ consoles of 8 October 2026 (Dan): the colour console's lower screen
            // ({0} the taker, {1} the colour), its lever's sign; the catalogue console's
            // lower screen, its corner ({0} the pot), its PREV / NEXT zones, its lever's sign.
            { "colour.pull", "PULL THE LEVER TO TAKE IT" },
            { "colour.taken", "{0} IS NOW {1}" },
            { "lever.take", "TAKE" },
            { "shop.aim", "AIM AT A ROW  ·  E BUYS IT FROM THE CREW POT" },
            { "shop.pickup", "BOUGHT GEAR LANDS AT THE PICKUP CHUTE" },
            { "shop.pot", "CREW POT ${0}" },
            { "shop.page", "PAGE {0}/{1}" },
            { "shop.prev", "< PREV" },
            { "shop.next", "NEXT >" },
            { "lever.page", "NEXT PAGE" },
            { "cargo", "CARGO / KEEP ACCESS CLEAR" },
            { "way.depot", "EQUIPMENT DEPOT  >" },                             // the board at the arrival exit
            { "way.ship", "SHIP  >" },
            { "way.quota", "SELL / QUOTA  >" },
            { "boarding", "BLACK TIDE / BOARDING" },
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
