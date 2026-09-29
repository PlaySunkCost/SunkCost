using System;
using SunkCost.Audio;
using SunkCost.Net;
using SunkCost.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SunkCost.UI
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class MenuRoot : MonoBehaviour
    {
        private enum Page { Main, Saves, Audio, Busy }
        private PrototypeSessionController session;
        private ProximityVoice voice;
        private GameObject canvasObject, surface;
        private RectTransform pages;
        private MenuDialog dialog;
        private MenuAudioView audio;
        private Page page, beforeAudio, beforeBusy;
        private SessionState lastState;
        private bool initialized, cancelling;
        private bool refreshSaves;
        private Text busyText;
        private GameObject ownedEventSystem;
        public bool CoversGameplay => initialized && surface.activeSelf;
        public static bool ConsumesEscape { get; private set; }

        private void Start()
        {
            session = GetComponent<PrototypeSessionController>(); voice = GetComponent<ProximityVoice>();
            if (session == null || voice == null) { enabled = false; return; }
            if (EventSystem.current == null)
            {
                ownedEventSystem = new GameObject("Menu EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                ownedEventSystem.transform.SetParent(transform, false);
                ownedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            canvasObject = new GameObject("Sunk Cost Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = MenuWidgets.Panel(canvasObject.transform, "Background (replaceable)", 0, 0, 1600, 1000, Color.black);
            MenuWidgets.Stretch(background.rectTransform); surface = background.gameObject;
            if (MenuTheme.Current.Background != null)
            {
                var art = MenuWidgets.Panel(surface.transform, "Background artwork", 0, 0, 1600, 1000, Color.white);
                MenuWidgets.Stretch(art.rectTransform); art.sprite = MenuTheme.Current.Background;
                art.preserveAspect = true; art.raycastTarget = false;
            }
            pages = MenuWidgets.Rect(surface.transform, "Pages", 0, 0, 1600, 1000); MenuWidgets.Stretch(pages);
            dialog = new MenuDialog(surface.transform, pages.gameObject.AddComponent<CanvasGroup>());
            session.InviteInterceptor = InterceptInvite;
            session.InviteError += InviteFailed;
            initialized = true; lastState = session.State;
            if (session.InRoom) surface.SetActive(false);
            else if (session.Busy) ShowBusy(); else ShowMain();
        }
        private void Update()
        {
            if (!initialized) return;
            bool consumed = CoversGameplay;
            // Set before the controller's Update; also preserve it through the closing frame.
            ConsumesEscape = consumed;
            if (consumed) voice.TextEntryActive = true;
            else voice.TextEntryActive = false;
            if (session.Busy && page != Page.Busy)
            {
                beforeBusy = page == Page.Saves ? Page.Saves : Page.Main;
                audio?.Discard(); audio = null; dialog.Close(); ShowBusy();
            }
            if (session.InRoom)
            {
                if (page == Page.Busy || lastState != SessionState.InRoom)
                { dialog.Close(); audio?.Discard(); audio = null; surface.SetActive(false); page = Page.Main; }
            }
            else if (!session.Busy && (page == Page.Busy || lastState == SessionState.InRoom))
            {
                bool disconnected = lastState == SessionState.InRoom;
                audio?.Discard(); audio = null;
                if (!disconnected && beforeBusy == Page.Saves) ShowSaves(); else ShowMain();
                if (!cancelling) dialog.Error(disconnected ? "Session ended" : "Connection failed", session.Message);
                cancelling = false;
            }
            if (page == Page.Busy && busyText != null) busyText.text = session.Message;
            audio?.Tick();
            if (CoversGameplay && Application.isFocused && !SessionInputGate.OverlayOpen && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (dialog.Open) dialog.Cancel();
                else if (page == Page.Audio) BackAudio();
                else if (page == Page.Saves) ShowMain();
                else if (page == Page.Busy) CancelConnection();
            }
            lastState = session.State;
            if (refreshSaves) { refreshSaves = false; if (page == Page.Saves && !session.Busy && !session.InRoom) ShowSaves(); }
        }
        private void Clear(Page next)
        {
            dialog.Close(); surface.SetActive(true); page = next;
            foreach (Transform child in pages) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        private void Header(string subtitle)
        {
            if (MenuTheme.Current.Logo == null) MenuWidgets.Label(pages, "SUNK COST", 85, 45, 1200, 120, 84, true);
            else
            {
                var logo = MenuWidgets.Panel(pages, "Logo", 85, 35, 640, 125, Color.white);
                logo.sprite = MenuTheme.Current.Logo; logo.preserveAspect = true; logo.raycastTarget = false;
            }
            MenuWidgets.Label(pages, subtitle, 90, 168, 1200, 52, 28, true).color = MenuWidgets.Amber;
        }
        private static void Focus(Selectable button) { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject); }
        private void ShowMain()
        {
            Clear(Page.Main); Header("BLACK TIDE SALVAGE");
            Focus(MenuWidgets.Button(pages, "HOST GAME", 90, 310, 560, 88, ShowSaves, true));
            MenuWidgets.Button(pages, "JOIN GAME", 90, 422, 560, 88, null);
            MenuWidgets.Button(pages, "OPTIONS", 90, 534, 560, 88, OpenAudio);
            MenuWidgets.Button(pages, "QUIT GAME", 90, 646, 560, 88,
                () => dialog.Confirm("Are you sure?", "Are you sure you want to quit the game?", Quit));
        }
        private void Quit()
        {
            audio?.Discard(); voice.StopMicrophoneTest(); voice.Devices.StopTest();
            session.Leave(); Application.Quit();
#if UNITY_EDITOR
            dialog.Error("Quit game", "Quit closes the standalone game. The Editor remains open.");
#endif
        }
        private void ShowBusy()
        {
            Clear(Page.Busy); Header(session.Role == SessionRole.Host ? "STARTING GAME..." : "CONNECTING...");
            busyText = MenuWidgets.Label(pages, session.Message, 90, 310, 1100, 130, 28);
            Focus(MenuWidgets.Button(pages, "CANCEL", 90, 500, 440, 70, CancelConnection));
        }
        private void CancelConnection() { cancelling = true; session.Leave("Connection cancelled."); }
        public void OpenAudio()
        {
            if (!initialized || session.Busy) return;
            beforeAudio = page == Page.Saves ? Page.Saves : Page.Main;
            if (session.InRoom) SessionInputGate.OpenMenu();
            Clear(Page.Audio); Header("OPTIONS");
            audio = new MenuAudioView(pages, voice, dialog, ExitAudio, BackAudio);
        }
        private void BackAudio()
        {
            if (audio == null) return;
            if (audio.Dirty) dialog.Confirm("Discard changes?", "Leave without saving your audio changes?", () => { audio.Discard(); ExitAudio(); });
            else { audio.Discard(); ExitAudio(); }
        }
        private void ExitAudio()
        {
            audio = null; dialog.Close();
            if (session.InRoom) { surface.SetActive(false); page = Page.Main; }
            else if (beforeAudio == Page.Saves) ShowSaves(); else ShowMain();
        }
        private bool InterceptInvite(ulong lobby)
        {
            if (audio == null || !audio.Dirty) return false;
            dialog.Confirm("Join your friend?", "Discard unsaved audio changes and join the Steam invitation?", () =>
            {
                audio?.Discard(); audio = null;
                if (!session.JoinSteamLobby(lobby)) { ShowMain(); dialog.Error("Could not join", session.Message); }
            });
            return true;
        }
        private void InviteFailed(string error) { if (initialized) { if (!CoversGameplay) ShowMain(); dialog.Error("Could not join", error); } }
        private void OnApplicationFocus(bool focused) { if (!focused && initialized) { voice.StopMicrophoneTest(); voice.Devices.StopTest(); } }
        private void OnDestroy()
        {
            audio?.Discard(); ConsumesEscape = false;
            if (session != null) { session.InviteInterceptor = null; session.InviteError -= InviteFailed; }
            if (canvasObject != null) Destroy(canvasObject);
            if (ownedEventSystem != null) Destroy(ownedEventSystem);
        }
    }
}
