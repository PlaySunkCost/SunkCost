using SunkCost.Look;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.World
{
    // Every push button's states (ship audit SHIP-048, 23 September 2026): idle,
    // aimed at (the rim lights), pressed (the cap sinks for a moment) and locked
    // (grey, the rim dark) while the press would be refused - the console while
    // the ship travels or divers are below, the cabin's button while the car is
    // away or the day's dive is done, the car's while it is not at the bottom.
    // The colour says what a button does: red only for what cannot be undone
    // (End day), the ship's accent for a destination. Pure presentation on this
    // peer: locked comes from the replicated day state (every screen agrees), aim
    // and press from the local player's own crosshair target (the controller's
    // CurrentUsable, what E acts on).
    public sealed class ButtonLook : MonoBehaviour
    {
        public enum Role : byte { Destination = 0, Danger = 1 }
        public const string CapName = "Cap";
        public const string RimName = "Cap Edge";
        public const string TextName = "Text";
        // The aimed state's own look. Off: the general aim highlight (InteractHighlight,
        // SHIP-054) already rims whatever the dot rests on, buttons included, and a
        // button must not light twice. The press and locked states stay this component's.
        public static bool ShowAim = false;

        public static readonly Color DestinationCap = new(0.08f, 0.40f, 0.47f);
        public static readonly Color DangerCap = new(0.70f, 0.10f, 0.08f);
        private static readonly Color LockedCap = new(0.15f, 0.17f, 0.19f);
        private const float PressSeconds = 0.18f;
        private const float PressDepth = 0.018f;

        [SerializeField] private Role role;
        private Renderer cap;
        private Renderer[] rim = System.Array.Empty<Renderer>();
        private TextMesh label;
        private Vector3 capRest, labelRest;
        private float pressedUntil;
        private MaterialPropertyBlock block;
        private Transform panel;
        private int shownState = -1;

        public Role Kind => role;
        public bool Locked { get; private set; }
        public bool Aimed { get; private set; }

        public void Configure(Role how) => role = how;

        public static Color CapColour(Role role) => role == Role.Danger ? DangerCap : DestinationCap;
        public static Color RimColour(Role role) => role == Role.Danger ? ScreenStyle.Danger : ScreenStyle.Accent;

        private void Awake()
        {
            Transform c = transform.Find(CapName);
            cap = c != null ? c.GetComponent<Renderer>() : null;
            if (c != null) capRest = c.localPosition;
            Transform r = transform.Find(RimName);
            if (r != null) rim = r.GetComponentsInChildren<Renderer>(true);
            Transform t = transform.Find(TextName);
            label = t != null ? t.GetComponent<TextMesh>() : null;
            if (t != null) labelRest = t.localPosition;
            block = new MaterialPropertyBlock();
            SunkCost.Diving.ElevatorControlPanel car = GetComponentInParent<SunkCost.Diving.ElevatorControlPanel>();
            panel = car != null ? car.transform : null; // the car's target is its panel component, not the collider
        }

        private void LateUpdate()
        {
            Locked = IsLocked();
            Transform aimed = AimedUsable();
            Aimed = aimed != null && (aimed == transform || aimed.IsChildOf(transform) || aimed == panel);
            if (Aimed && !Locked && SunkCost.Net.SessionInputGate.CanPlay && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                pressedUntil = Time.unscaledTime + PressSeconds;
            bool pressed = Time.unscaledTime < pressedUntil;
            int state = Locked ? 3 : pressed ? 2 : Aimed && ShowAim ? 1 : 0;
            if (state == shownState) return;
            shownState = state;
            Apply(state);
        }

        private void Apply(int state)
        {
            Color capColour = state == 3 ? LockedCap : CapColour(role);
            if (state == 1) capColour = Color.Lerp(capColour, RimColour(role), 0.25f);
            if (state == 2) capColour = Color.Lerp(capColour, Color.white, 0.2f);
            if (cap != null)
            {
                cap.GetPropertyBlock(block);
                block.SetColor("_BaseColor", capColour);
                cap.SetPropertyBlock(block);
                cap.transform.localPosition = capRest - new Vector3(0f, 0f, state == 2 ? PressDepth : 0f);
            }
            Color rimColour = state == 3 ? ScreenStyle.Track : state == 0 ? Color.Lerp(ScreenStyle.Track, RimColour(role), 0.45f) : state == 2 ? Color.white : RimColour(role);
            foreach (Renderer r in rim) ScreenStyle.Paint(r, rimColour);
            if (label != null)
            {
                label.color = state == 3 ? ScreenStyle.Dim : Color.white;
                label.transform.localPosition = labelRest - new Vector3(0f, 0f, state == 2 ? PressDepth : 0f); // the words ride on the cap
            }
        }

        private bool IsLocked()
        {
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return false;
            if (GetComponent<MonitorButton>() != null)
                return day.Travelling || day.Sailing || day.Riding || day.Below.Count > 0 || day.CabinAway;
            if (name == ShipParts.DeckCabinButtonName)
                return day.Riding || day.Travelling || day.World != WorldId.Sea || day.Elevator.State != SunkCost.Diving.ElevatorState.AtTop
                    || day.Phase == DayPhase.DiveInProgress || day.DiveDone || day.Payday;
            if (GetComponentInParent<SunkCost.Diving.ElevatorControlPanel>() != null)
                return day.Riding || day.Elevator.State != SunkCost.Diving.ElevatorState.AtBottom;
            return false;
        }

        // What the local player's crosshair rests on this frame (read, never decided here).
        private static Transform AimedUsable()
        {
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            if (local == null || local.IsDead || local.TravelLocked || local.IsSeated) return null;
            return local.CurrentUsable;
        }
    }
}
