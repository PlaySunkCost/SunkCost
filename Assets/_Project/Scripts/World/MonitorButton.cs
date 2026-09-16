using UnityEngine;

namespace SunkCost.World
{
    // One of the buttons under the ship's monitor: a destination to sail to, or
    // End day (Dan, 16 September 2026: the crew ends the day here). Nothing
    // networked lives on the ship: the button only says what it is; the press
    // goes through the player's ShipControls.
    public sealed class MonitorButton : MonoBehaviour
    {
        public enum Kind : byte { Sail = 0, EndDay = 1 }

        [SerializeField] private Kind kind = Kind.Sail;
        [SerializeField] private WorldId destination = WorldId.Sea;
        [SerializeField] private string label = "Site 01";

        public Kind Action => kind;
        public WorldId Destination => destination;
        public string Label => label;

        public void Configure(WorldId to, string text)
        {
            kind = Kind.Sail;
            destination = to;
            label = text;
        }

        public void ConfigureEndDay(string text)
        {
            kind = Kind.EndDay;
            label = text;
        }
    }
}
