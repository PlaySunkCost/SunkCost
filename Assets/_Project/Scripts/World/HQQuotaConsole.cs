using UnityEngine;

namespace SunkCost.World
{
    // HQ's quota console composer (the shared console, 27 September 2026).
    // FOUNDATION SCAFFOLD by console_model: the idle screens only, no rules, Text
    // empty; the hq agent replaces this file in the Implement phase with the composer
    // that turns CrewDayState into the quota board, the GIVE UP card and the PAY sign
    // every 0.25 s (INTERFACES §11/§12). Sits on the HQ rig root beside ConsoleRig.
    public sealed class HQQuotaConsole : MonoBehaviour, IConsoleComposer
    {
        public const float RefreshSeconds = 0.25f;

        private ConsoleRig rig;
        private ConsoleLever lever;

        public ConsoleKind Kind => ConsoleKind.HQ;
        public string Text { get; private set; } = string.Empty;   // the one-line compat status (empty until the composer exists)
        public TopModel Top { get; private set; }
        public BottomModel Bottom { get; private set; }
        public SignModel Sign { get; private set; }
        public int LeverPlayedSerial => Lever != null ? Lever.PlayedSerial : -1;
        public float LeverAngle => Lever != null ? Lever.Angle : 0f;

        private ConsoleRig Rig => rig != null ? rig : rig = GetComponent<ConsoleRig>();
        private ConsoleLever Lever => lever != null ? lever : lever = GetComponent<ConsoleLever>();

        private void Start() => ShowIdle();

        public void ShowIdle()
        {
            ConsoleRig.Idle(ConsoleKind.HQ, out TopModel top, out BottomModel bottom, out SignModel sign);
            Top = top; Bottom = bottom; Sign = sign;
            if (Rig != null) Rig.Show(top, bottom, sign);
        }

        public static HQQuotaConsole InHQ()
        {
            ConsoleRig r = ConsoleRig.InScene(WorldScenes.Scene(WorldId.HQ), ConsoleKind.HQ);
            return r != null ? r.GetComponent<HQQuotaConsole>() : null;
        }
    }
}
