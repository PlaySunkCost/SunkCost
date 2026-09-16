using UnityEngine;

namespace SunkCost.Net
{
    // Local-only gate for gameplay input (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 10). Play input is blocked while the session menu is open, the Steam
    // overlay is up, or the application has lost focus. Escape opens the menu;
    // Resume closes it and recaptures the cursor, swallowing the click that
    // pressed Resume so it cannot become a throw. Nothing here is replicated.
    public static class SessionInputGate
    {
        public static bool MenuOpen { get; private set; }
        public static bool OverlayOpen { get; private set; }
        public static bool PickerOpen { get; private set; }   // the colour panel's wheel, cursor free
        public static bool ApplicationFocused { get; private set; } = true;

        private static int suppressedFrame = -1;

        public static bool CanPlay => !MenuOpen && !OverlayOpen && !PickerOpen && ApplicationFocused;

        public static void OpenPicker()
        {
            PickerOpen = true;
            ReleaseCursor();
        }

        public static void ClosePicker()
        {
            if (!PickerOpen) return;
            PickerOpen = false;
            suppressedFrame = Time.frameCount;
            if (!MenuOpen && !OverlayOpen && ApplicationFocused) CaptureCursor();
        }
        public static bool ClickSuppressedThisFrame => Time.frameCount == suppressedFrame;

        public static void OpenMenu()
        {
            MenuOpen = true;
            ReleaseCursor();
        }

        public static void Resume()
        {
            MenuOpen = false;
            suppressedFrame = Time.frameCount;
            if (!OverlayOpen && ApplicationFocused) CaptureCursor();
        }

        // Entering a room starts with the cursor captured; leaving it releases.
        public static void EnterRoom()
        {
            MenuOpen = false;
            suppressedFrame = Time.frameCount;
            if (!OverlayOpen && ApplicationFocused) CaptureCursor();
        }

        public static void ExitRoom()
        {
            MenuOpen = false;
            ReleaseCursor();
        }

        public static void SetOverlay(bool open)
        {
            OverlayOpen = open;
            if (open) ReleaseCursor();
            // Stay released after the overlay closes until the player presses Resume
            // or Escape/Resume in the menu; a stray click must not throw the ball.
            if (!open) MenuOpen = true;
        }

        public static void SetApplicationFocus(bool focused)
        {
            ApplicationFocused = focused;
            if (!focused) ReleaseCursor();
        }

        private static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
