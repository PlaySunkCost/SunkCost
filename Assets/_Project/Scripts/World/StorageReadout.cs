using SunkCost.Interaction;
using UnityEngine;

namespace SunkCost.World
{
    // The readout on the ship's storage room (Dan, 16 September 2026: "show how
    // much money is in the box any time, and how much is needed for the quota
    // — on the box, something like 100/200"). Purely local: it sums the
    // replicated values of the loose items inside the room's volume on this
    // peer and reads the quota from the settings. The pay button at HQ is what
    // turns the box into money (QuotaBoard / WorldSceneFlow.ServerPay).
    public sealed class StorageReadout : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        private ShipParts ship;
        private TextMesh label;
        private WorldLoopSettings settings;
        private float nextRefresh;

        public string Text { get; private set; } = string.Empty;
        public int ValueInside { get; private set; }

        private void Awake()
        {
            ship = GetComponentInParent<ShipParts>();
            Transform readout = ship != null ? ship.Find(ShipParts.StorageReadoutName) : null;
            label = readout != null ? readout.GetComponent<TextMesh>() : null;
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            CrewDayState day = CrewDayState.Instance;
            ValueInside = day != null ? day.BoxValue : (ship != null ? SumInside(ship) : 0);
            string balance = day != null ? $"\nbalance ${day.Balance}" : string.Empty;
            string text = $"STORAGE\n${ValueInside} / ${settings.QuotaPerCycle}{balance}";
            if (text == Text) return;
            Text = text;
            if (label != null) label.text = text;
        }

        // The loose items (not in anyone's hands or slots) inside the room.
        public static int SumInside(ShipParts ship)
        {
            int sum = 0;
            foreach (CarryableItem item in CarryableItem.Spawned)
            {
                if (item == null || !item.CanGrabFromWorld) continue;
                if (item.gameObject.scene != ship.gameObject.scene) continue;
                if (ship.IsInStorageRoom(item.transform.position)) sum += item.Value;
            }
            return sum;
        }
    }
}
