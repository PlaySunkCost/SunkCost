using System.Text;
using UnityEngine;

namespace SunkCost.World
{
    // WHAT the console's screens and sign show (the shared console, 27 September
    // 2026). The composers (ShipNavigationConsole, HQQuotaConsole) fill these from
    // replicated state; the rig (ConsoleRig, with the render-scout's technique)
    // decides HOW they are drawn. Text, tones, flags and pictures only: no colours,
    // sizes in metres or Unity UI here. Plain classes with public fields, reused by
    // the composers.
    public enum ConsoleTone : byte { Accent = 0, Text = 1, Dim = 2, Warn = 3, Good = 4, Danger = 5 } // ScreenStyle.Accent/Text/Dim/Warn/Good/Danger
    public enum ConsoleTextSize : byte { Small = 0, Medium = 1, Large = 2, Huge = 3 }              // BRIEF "Typography": SMALL secondary, MEDIUM quota/balance/status, LARGE title/destination, VERY LARGE state

    public struct ConsoleLine
    {
        public string Text;
        public ConsoleTextSize Size;
        public ConsoleTone Tone;
        public ConsoleLine(string text, ConsoleTextSize size, ConsoleTone tone) { Text = text; Size = size; Tone = tone; }
        public static readonly ConsoleLine Empty = new(string.Empty, ConsoleTextSize.Medium, ConsoleTone.Dim);
    }

    // One destination card (the ship's top screen). All five are drawn the same size.
    public sealed class CardModel
    {
        public SiteId Id;
        public string Name;          // "HQ", "SITE 01" ...
        public Texture2D Picture;    // never null when SiteCatalog resolved (the shared placeholder)
        public bool Locked;          // the lock mark; still selectable
        public bool Selected;        // the bright cyan outline (the networked selection)
        public bool Here;            // the small HERE marker (the ship is at this site)
        public bool Aimed;           // LOCAL presentation only: the local player's dot rests on it
        public string Tag;           // small secondary text under the name: "" / "$100" (a locked site's price)
    }

    public sealed class TopModel
    {
        public string Title;         // "NAVIGATION" / "QUOTA BOARD"
        public string Corner;        // top-right: "DAY 2/3", "PAYDAY", ""
        public CardModel[] Cards;    // ship: exactly 5 in Destinations.Cards order; HQ: null or empty
        public string BigState;      // HQ: "NEW CYCLE", "DAY 1 OF 3", "PAYDAY", "PAID", "SHORT BY $80", "THE RUN IS OVER"; ship: ""
        public ConsoleTone BigTone;
        public string Hint;          // HQ: "Sail to Site 01 and dive first", "GIVE UP 1/3 · EVERYONE MUST PRESS"; ship: ""
        public ConsoleTone HintTone;
        public string FootLeft;      // "QUOTA $420 / $500"
        public ConsoleTone FootLeftTone; // Warn while short, Good when met
        public string FootRight;     // "BALANCE $230" (HQ), the company name (ship)
    }

    public sealed class BottomModel
    {
        public string Heading;       // "SELECTED DESTINATION" / "SELECT A DESTINATION" / "SAILING" / "DIVE IN PROGRESS" / "" (HQ)
        public Texture2D Picture;    // the selected site's picture, or null
        public string Name;          // "SITE 01" / "HQ" / ""
        public string SubName;       // "SAIL HOME", a "BLACK TIDE"-style sub line, or ""
        public ConsoleLine[] Lines;  // facts: "DAY 2 OF 3", "QUOTA $120 / $500", "BALANCE $60", "COST $100", "2 BELOW", placeholder description lines
        public ConsoleLine Status;   // the prominent final line: "READY", "WAITING FOR DAN TO BOARD", "DIVE DONE — END THE DAY FIRST", "PAYDAY — ONLY HQ", "PULL TO UNLOCK", "$40 SHORT", "SAILING TO SITE 01"
        public bool Notice;          // true while a refusal is shown: Status IS the refusal, drawn as a prominent block (BRIEF "Lever action refused")
        public VoteCardModel VoteCard; // HQ only; null on the ship
    }

    // The red GIVE UP card on the HQ bottom panel.
    public sealed class VoteCardModel
    {
        public string Word;          // "GIVE UP"
        public string Count;         // "1 / 3"
        public string Foot;          // "EVERYONE MUST AGREE" or, for a voter, "YOU VOTED · E TO TAKE BACK"
        public bool LocalVoted;      // the local player's vote is in (the card changes visibly)
        public bool Enabled;         // voting possible now (docked, the run active)
        public bool Aimed;           // LOCAL presentation only
    }

    public struct SignModel
    {
        public string Word;          // "CONFIRM", "UNLOCK $100", "END DAY", "PAY"; while None: the last meaningful word or "" — dim
        public ConsoleTone Tone;     // Confirm → Accent, Unlock → Warn, EndDay → Danger, Pay → Good, None → Dim
        public bool Enabled;         // lit, or dim
        public bool Aimed;           // LOCAL presentation only
    }

    // What a composer exposes to the hooks and the guest snapshot, whichever console
    // it drives (added by console_state, Foundation): ShipNavigationConsole and
    // HQQuotaConsole implement it, so the hooks never name a rig type. Text is the
    // one-line compat status the checks read (INTERFACES §12); the screens' flat
    // dumps come from ConsoleModels.Flatten. LeverPlayedSerial/LeverAngle forward the
    // rig's ConsoleLever (−1 / 0 when the rig has none yet).
    public interface IConsoleComposer
    {
        ConsoleKind Kind { get; }
        string Text { get; }
        TopModel Top { get; }
        BottomModel Bottom { get; }
        SignModel Sign { get; }
        int LeverPlayedSerial { get; }
        float LeverAngle { get; }
    }

    public static class ConsoleModels
    {
        private const string Sep = " · ";

        // Deterministic one-line dumps for the hooks and the guest snapshot (no newlines).
        // Top: "<Title> · <Corner> · [HQ@][SITE 01*][SITE 02#][SITE 03#][SITE 04#] · <BigState> · <Hint> · <FootLeft> · <FootRight>"
        //   card flags in this order: * selected, # locked, @ here, ~ aimed; a card with none is "[SITE 01]".
        public static string Flatten(TopModel top)
        {
            if (top == null) return string.Empty;
            var sb = new StringBuilder();
            sb.Append(Clean(top.Title)).Append(Sep).Append(Clean(top.Corner)).Append(Sep);
            if (top.Cards != null)
                foreach (CardModel card in top.Cards)
                {
                    if (card == null) continue;
                    sb.Append('[').Append(Clean(card.Name));
                    if (card.Selected) sb.Append('*');
                    if (card.Locked) sb.Append('#');
                    if (card.Here) sb.Append('@');
                    if (card.Aimed) sb.Append('~');
                    sb.Append(']');
                }
            sb.Append(Sep).Append(Clean(top.BigState)).Append(Sep).Append(Clean(top.Hint)).Append(Sep).Append(Clean(top.FootLeft)).Append(Sep).Append(Clean(top.FootRight));
            return sb.ToString();
        }

        // Bottom: "<Heading> · <Name> · <SubName> · <line1> | <line2> … · <Status.Text>[ · NOTICE][ · vote=GIVE UP 1 / 3 voted=True]"
        public static string Flatten(BottomModel bottom)
        {
            if (bottom == null) return string.Empty;
            var sb = new StringBuilder();
            sb.Append(Clean(bottom.Heading)).Append(Sep).Append(Clean(bottom.Name)).Append(Sep).Append(Clean(bottom.SubName)).Append(Sep);
            if (bottom.Lines != null)
                for (int i = 0; i < bottom.Lines.Length; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    sb.Append(Clean(bottom.Lines[i].Text));
                }
            sb.Append(Sep).Append(Clean(bottom.Status.Text));
            if (bottom.Notice) sb.Append(Sep).Append("NOTICE");
            if (bottom.VoteCard != null) sb.Append(Sep).Append("vote=").Append(Clean(bottom.VoteCard.Word)).Append(' ').Append(Clean(bottom.VoteCard.Count)).Append(" voted=").Append(bottom.VoteCard.LocalVoted);
            return sb.ToString();
        }

        // Sign: "<Word>/<on|off>"
        public static string Flatten(SignModel sign) => Clean(sign.Word) + "/" + (sign.Enabled ? "on" : "off");

        private static string Clean(string text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r", string.Empty).Replace("\n", " ");
    }
}
