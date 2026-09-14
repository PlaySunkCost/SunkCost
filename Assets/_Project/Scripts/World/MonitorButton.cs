using UnityEngine;

namespace SunkCost.World
{
    // One of the two buttons under the ship's monitor. Nothing networked lives
    // on the ship: the button only says where it points; the press goes through
    // the player's ShipControls.
    public sealed class MonitorButton : MonoBehaviour
    {
        [SerializeField] private WorldId destination = WorldId.Sea;
        [SerializeField] private string label = "Site 01";

        public WorldId Destination => destination;
        public string Label => label;

        public void Configure(WorldId to, string text)
        {
            destination = to;
            label = text;
        }
    }
}
