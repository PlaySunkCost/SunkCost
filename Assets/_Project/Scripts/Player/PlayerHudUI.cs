using SunkCost.Interaction;
using SunkCost.Net;
using UnityEngine;

namespace SunkCost.Player
{
    // Owner-only immediate-mode HUD: the interaction prompt under the crosshair and
    // the four inventory slots along the bottom. Reads state, never writes it.
    [RequireComponent(typeof(HQPlayerController), typeof(PlayerInventory))]
    public sealed class PlayerHudUI : MonoBehaviour
    {
        private const float SlotSize = 64f;
        private const float SlotGap = 8f;
        private const float SlotBottomMargin = 24f;
        private const float MeterGap = 6f;
        private const float MeterHeight = 8f;

        private HQPlayerController controller;
        private PlayerInventory inventory;
        private GUIStyle promptStyle;
        private GUIStyle numberStyle;
        private GUIStyle labelStyle;
        private Texture2D whiteTexture;

        // What the prompt shows this frame; exposed for the editor test hooks.
        public string PromptText
        {
            get
            {
                if (inventory == null || controller == null) return string.Empty;
                if (controller.TravelLocked) return string.Empty;
                string refusal = inventory.Refusal;
                if (!string.IsNullOrEmpty(refusal)) return refusal;
                CarryableItem target = controller.CurrentTarget;
                if (target == null && controller.CurrentButton != null) return $"Press E to sail to {controller.CurrentButton.Label}";
                if (target == null || !target.CanGrabFromWorld) return string.Empty;
                string name = target.Grip == CarryGrip.TwoHands ? $"{target.DisplayName} (two hands)" : target.DisplayName;
                if (!inventory.CanStoreOrHold(target)) return "Hands full";
                if (inventory.HoldingOverflow) return $"Press E to store {name}";
                return target.State == ItemState.Released ? $"Hold E to catch {name}" : $"Press E to grab {name}";
            }
        }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            inventory = GetComponent<PlayerInventory>();
        }

        private void OnGUI()
        {
            if (inventory == null || !inventory.IsOwner || SessionInputGate.MenuOpen)
                return;
            EnsureStyles();
            DrawAimingDot();
            DrawPrompt();
            DrawSlots();
            DrawWeightMeter();
        }

        // A plain grey bar under the slots: mass / capacity from the server's carried
        // mass. Full is a real state: the bar turns red and the player crawls.
        private void DrawWeightMeter()
        {
            float totalWidth = InventorySlots.Count * SlotSize + (InventorySlots.Count - 1) * SlotGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = Screen.height - SlotBottomMargin - SlotSize + SlotSize + MeterGap;
            bool overloaded = inventory.Overloaded;
            float fill = overloaded ? 1f : inventory.MeterFill;
            float fillWidth = fill <= 0f ? 0f : Mathf.Clamp(Mathf.Round(fill * totalWidth), 1f, totalWidth);

            Color previous = GUI.color;
            GUI.color = new Color(0.16f, 0.16f, 0.16f, 0.85f);
            GUI.DrawTexture(new Rect(left, top, totalWidth, MeterHeight), whiteTexture);
            if (fillWidth > 0f)
            {
                GUI.color = overloaded ? new Color(0.85f, 0.15f, 0.12f, 0.98f) : new Color(0.62f, 0.62f, 0.62f, 0.95f);
                GUI.DrawTexture(new Rect(left, top, fillWidth, MeterHeight), whiteTexture);
            }
            GUI.color = previous;
            if (overloaded)
            {
                // Above the slot row (and its "In hand" line): the bottom margin is too
                // small for a line under the bar.
                float slotsTop = Screen.height - SlotBottomMargin - SlotSize;
                GUI.Label(new Rect(left - 40f, slotsTop - 48f, totalWidth + 80f, 22f), "Too heavy — drop something", promptStyle);
            }
        }

        private void DrawPrompt()
        {
            string text = PromptText;
            if (string.IsNullOrEmpty(text)) return;
            float width = 420f;
            GUI.Label(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f + 28f, width, 28f), text, promptStyle);
        }

        private void DrawSlots()
        {
            float totalWidth = InventorySlots.Count * SlotSize + (InventorySlots.Count - 1) * SlotGap;
            float left = (Screen.width - totalWidth) * 0.5f;
            float top = Screen.height - SlotBottomMargin - SlotSize;
            int heldSlot = inventory.HeldSlot;

            if (inventory.HoldingOverflow)
                GUI.Label(new Rect(left, top - 24f, totalWidth, 22f), $"In hand: {inventory.HeldItem.DisplayName} (no slot)", labelStyle);

            for (int i = 0; i < InventorySlots.Count; i++)
            {
                Rect rect = new(left + i * (SlotSize + SlotGap), top, SlotSize, SlotSize);
                CarryableItem item = inventory.ItemInSlot(i);
                bool held = i == heldSlot;

                Color previous = GUI.color;
                GUI.color = held ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), whiteTexture);
                GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
                GUI.DrawTexture(rect, whiteTexture);
                GUI.color = previous;

                if (item != null)
                {
                    if (item.Icon != null)
                        GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f), item.Icon, ScaleMode.ScaleToFit, true);
                    else
                        GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.5f - 10f, rect.width, 20f), item.DisplayName, labelStyle);
                }
                GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 20f, 18f), (i + 1).ToString(), numberStyle);
            }
        }

        // The centre aiming dot (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
        // section 6, "Center aiming dot"): one outlined dot, scaled with the screen
        // height, gold only when the thing under it is locally usable. Hidden with
        // the menu, the overlay, lost focus, a fade and the travel lock; the prompt
        // text, not the colour, carries refusals.
        private void DrawAimingDot()
        {
            if (!SessionInputGate.CanPlay || controller.TravelLocked) return;
            if (SunkCost.World.ScreenFade.Instance != null && !SunkCost.World.ScreenFade.Instance.IsClear) return;
            PlayerMovementSettings settings = controller.Movement;
            float scale = Screen.height / 1080f;
            float diameter = Mathf.Max(2f, Mathf.Round(settings.DotDiameterPx * scale));
            float outline = Mathf.Round(settings.DotOutlinePx * scale);
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            bool usable = false;
            CarryableItem target = controller.CurrentTarget;
            if (target != null && target.CanGrabFromWorld) usable = inventory.CanStoreOrHold(target);
            else if (controller.CurrentButton != null) usable = SunkCost.World.CrewDayState.Instance != null && !SunkCost.World.CrewDayState.Instance.Travelling && !SunkCost.World.CrewDayState.Instance.Sailing;
            Color previous = GUI.color;
            if (outline > 0f)
            {
                GUI.color = settings.DotOutlineColor;
                GUI.DrawTexture(new Rect(cx - diameter * 0.5f - outline, cy - diameter * 0.5f - outline, diameter + outline * 2f, diameter + outline * 2f), whiteTexture);
            }
            GUI.color = usable ? settings.DotUsableColor : settings.DotColor;
            GUI.DrawTexture(new Rect(cx - diameter * 0.5f, cy - diameter * 0.5f, diameter, diameter), whiteTexture);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (promptStyle != null) return;
            promptStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 15, wordWrap = false };
            promptStyle.normal.textColor = Color.white;
            numberStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            numberStyle.normal.textColor = Color.white;
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            labelStyle.normal.textColor = Color.white;
            whiteTexture = Texture2D.whiteTexture;
        }
    }
}
