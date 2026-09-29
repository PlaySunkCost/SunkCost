using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SunkCost.UI
{
    public sealed class MenuDialog
    {
        private readonly Transform parent;
        private GameObject root, previousFocus;
        private Action cancel;
        private readonly CanvasGroup pageInput;
        public bool Open => root != null;
        public MenuDialog(Transform parent, CanvasGroup pageInput = null) { this.parent = parent; this.pageInput = pageInput; }
        private Transform Begin(string title, string message)
        {
            Close(); previousFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (pageInput != null) pageInput.interactable = false;
            var shade = MenuWidgets.Panel(parent, "Modal blocker", 0, 0, 1600, 1000, new Color(0, 0, 0, .82f));
            MenuWidgets.Stretch(shade.rectTransform); root = shade.gameObject;
            var plate = MenuWidgets.Panel(shade.transform, "Dialog", 0, 0, 760, 350, MenuWidgets.Ink);
            if (MenuTheme.Current.ButtonPlate != null)
            { plate.sprite = MenuTheme.Current.ButtonPlate; plate.type = Image.Type.Sliced; plate.color = Color.white; }
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = plate.rectTransform.pivot = new Vector2(.5f, .5f);
            plate.rectTransform.anchoredPosition = Vector2.zero;
            var line = plate.gameObject.AddComponent<Outline>(); line.effectColor = MenuWidgets.Amber; line.effectDistance = new Vector2(2, -2);
            var heading = MenuWidgets.Label(plate.transform, title.ToUpperInvariant(), 32, 15, 696, 72, 37, true);
            if (MenuTheme.Current.ButtonFont != null) { heading.font = MenuTheme.Current.ButtonFont; heading.fontStyle = FontStyle.Normal; }
            MenuWidgets.Label(plate.transform, message, 32, 90, 696, 115, 24);
            return plate.transform;
        }
        private static void Focus(Selectable button) { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject); }
        public void Confirm(string title, string message, Action yes, Action no = null)
        {
            var plate = Begin(title, message);
            cancel = () => { Close(); no?.Invoke(); };
            MenuWidgets.Button(plate, "YES", 32, 250, 330, 66, () => { Close(); yes?.Invoke(); }, true);
            Focus(MenuWidgets.Button(plate, "NO", 398, 250, 330, 66, () => Cancel()));
        }
        public void Error(string title, string message)
        {
            var plate = Begin(title, message); cancel = Close;
            Focus(MenuWidgets.Button(plate, "OK", 215, 250, 330, 66, Close, true));
        }
        public void Rename(string name, int limit, Func<string, string> save)
        {
            var plate = Begin("Rename save", "Enter a name for this save.");
            var field = MenuWidgets.Input(plate, name, 32, 150, 696, 54, limit);
            var error = MenuWidgets.Label(plate, "", 32, 206, 696, 40, 19); error.color = MenuWidgets.Amber;
            cancel = Close;
            Action submit = () =>
            {
                if (string.IsNullOrWhiteSpace(field.text)) { error.text = "Please enter a name."; return; }
                string failure = save(field.text.Trim());
                if (string.IsNullOrEmpty(failure)) Close(); else error.text = failure;
            };
            MenuWidgets.Button(plate, "SAVE", 32, 260, 330, 60, submit, true);
            MenuWidgets.Button(plate, "CANCEL", 398, 260, 330, 60, Close);
            field.onSubmit.AddListener(_ => submit()); Focus(field); field.ActivateInputField();
        }
        public void Cancel() { var action = cancel; action?.Invoke(); }
        public void Close()
        {
            if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); root = null; }
            cancel = null;
            if (pageInput != null) pageInput.interactable = true;
            if (previousFocus != null && previousFocus.activeInHierarchy && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previousFocus);
            previousFocus = null;
        }
    }
}
