using UnityEngine;

namespace SunkCost.World
{
    // The ship's navigation console composer (the shared console, 27 September
    // 2026). FOUNDATION SCAFFOLD by console_model: the idle screens only, no rules,
    // Text empty; the ship agent replaces this file in the Implement phase with the
    // composer that turns CrewDayState into the three content models every 0.25 s
    // (INTERFACES §11/§12). Sits on the ship rig root beside ConsoleRig.
    public sealed class ShipNavigationConsole : MonoBehaviour, IConsoleComposer
    {
        public const float RefreshSeconds = 0.25f;

        private ConsoleRig rig;
        private ConsoleLever lever;

        public ConsoleKind Kind => ConsoleKind.Ship;
        public string Text { get; private set; } = string.Empty;   // the one-line compat status (empty until the composer exists)
        public TopModel Top { get; private set; }
        public BottomModel Bottom { get; private set; }
        public SignModel Sign { get; private set; }
        public int LeverPlayedSerial => Lever != null ? Lever.PlayedSerial : -1;
        public float LeverAngle => Lever != null ? Lever.Angle : 0f;

        private ConsoleRig Rig => rig != null ? rig : rig = GetComponent<ConsoleRig>();
        private ConsoleLever Lever => lever != null ? lever : lever = GetComponent<ConsoleLever>();

        private void Start() => ShowIdle();

        // The editor's idle screen; in the scaffold, the only screen.
        public void ShowIdle()
        {
            ConsoleRig.Idle(ConsoleKind.Ship, out TopModel top, out BottomModel bottom, out SignModel sign);
            Top = top; Bottom = bottom; Sign = sign;
            if (Rig != null) Rig.Show(top, bottom, sign);
        }

        public static ShipNavigationConsole InWorld(WorldId world)
        {
            ConsoleRig r = ConsoleRig.OnShip(ShipParts.InWorld(world));
            return r != null ? r.GetComponent<ShipNavigationConsole>() : null;
        }
    }
}
