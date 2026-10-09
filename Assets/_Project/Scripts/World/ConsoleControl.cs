using UnityEngine;

namespace SunkCost.World
{
    // What an aimed collider on a console is (the shared console, 27 September 2026):
    // a destination card, the lever, or the HQ's GIVE UP card. The builder puts one on
    // each control object with its collider; the player's aim (HQPlayerController.
    // UpdateTarget) finds it with GetComponentInParent and E goes through the player's
    // ShipControls. Nothing networked lives here: the control only says what it is.
    public sealed class ConsoleControl : MonoBehaviour
    {
        [SerializeField] private ConsoleKind console;
        [SerializeField] private ConsoleControlKind kind;
        [SerializeField] private SiteId payload;        // the card's site; None for Lever/GiveUpCard
        [SerializeField] private int index;             // Swatch: the palette index 0..15; ShopRow: the row 0..2; ShopPage: +1 next / -1 prev; else 0

        public ConsoleKind Console => console;
        public ConsoleControlKind Kind => kind;
        public SiteId Payload => payload;
        public int Index => index;

        public void Configure(ConsoleKind console, ConsoleControlKind kind, SiteId payload, int index = 0)
        {
            this.console = console;
            this.kind = kind;
            this.payload = payload;
            this.index = index;
        }
    }
}
