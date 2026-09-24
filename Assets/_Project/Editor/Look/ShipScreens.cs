using SunkCost.Look;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The ship's displays in its one screen style (ship audit SHIP-044/045/046/047/
    // 059/064, 23 September 2026): the monitor's layout, the crew screen, the
    // storage readout and its copy inside the room, the TV's caption chip and idle
    // channel - each built by its own runtime component's EnsureDisplay, the same
    // code that rebuilds it in play if it is missing, and left showing its idle
    // state so the prefab reads right in the editor. Runs last in the ship's
    // build, after the dressing has moved the screens onto their models.
    public static class ShipScreens
    {
        public static void Build(Transform root)
        {
            ShipParts ship = root.GetComponent<ShipParts>();
            if (ship == null) return;
            ScreenStyle.EditorFlat = LookMaterials.ShipFlat; // saved materials, so the prefab keeps its colours
            try
            {
                Transform monitorPart = ship.Find(ShipParts.MonitorName);
                if (monitorPart != null)
                {
                    ShipMonitor monitor = monitorPart.GetComponent<ShipMonitor>();
                    if (monitor == null) monitor = monitorPart.gameObject.AddComponent<ShipMonitor>();
                    monitor.EnsureDisplay();
                    monitor.ShowIdle();
                }
                Transform crewPart = ship.Find(CrewScreen.TextObjectName);
                if (crewPart != null)
                {
                    CrewScreen crew = crewPart.GetComponent<CrewScreen>();
                    if (crew == null) crew = crewPart.gameObject.AddComponent<CrewScreen>();
                    crew.EnsureDisplay();
                    crew.ShowIdle();
                }
                StorageReadout storage = root.GetComponent<StorageReadout>();
                if (storage != null)
                {
                    storage.EnsureDisplay();
                    storage.ShowIdle();
                }
                TvDisplay.Ensure(ship);
            }
            finally { ScreenStyle.EditorFlat = null; }
        }
    }
}
