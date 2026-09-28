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

        public ConsoleKind Console => console;
        public ConsoleControlKind Kind => kind;
        public SiteId Payload => payload;

        public void Configure(ConsoleKind console, ConsoleControlKind kind, SiteId payload)
        {
            this.console = console;
            this.kind = kind;
            this.payload = payload;
        }
    }
}
