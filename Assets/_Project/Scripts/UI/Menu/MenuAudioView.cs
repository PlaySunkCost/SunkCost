using System;
using System.Collections.Generic;
using SunkCost.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SunkCost.UI
{
    public sealed class MenuAudioView
    {
        private readonly ProximityVoice voice;
        private readonly MenuDialog dialog;
        private readonly Action exit;
        private readonly AudioPreferences original;
        private AudioPreferences working;
        private RectTransform content;
        private RectTransform view;
        private GameObject lastFocus;
        private Dropdown input, output;
        private readonly List<string> inputIds = new(), outputIds = new();
        private string inputSignature, outputSignature;
        private Text micText, chatText, testText, status;
        private Button micButton, testButton, outputTest;
        private Image meter, outputMeter;
        private string peerSignature;
        private bool finished;
        private float nextRefresh;
        public bool Dirty => !working.Equals(original);

        public MenuAudioView(Transform parent, ProximityVoice voice, MenuDialog dialog, Action exit, Action back)
        {
            this.voice = voice; this.dialog = dialog; this.exit = exit;
            original = working = AudioPreferences.Capture(voice); voice.Devices.PreviewingPreferences = true;
            string[] tabs = { "GAMEPLAY", "CONTROLS", "VIDEO", "AUDIO", "ACCESSIBILITY" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = MenuWidgets.Button(parent, tabs[i], 90 + i * 210, 238, 196, 50, null, i == 3);
                tab.interactable = i == 3;
                tab.GetComponentInChildren<Text>().fontSize = 19;
            }
            var restore = MenuWidgets.Button(parent, "↺", 1128, 165, 72, 52, () =>
                dialog.Confirm("Restore defaults?", "Reset audio settings to defaults? Save & Exit will keep them; Back can discard them.", Restore));
            var tooltip = MenuWidgets.Label(parent, "Restore defaults", 1215, 170, 260, 44, 22); tooltip.gameObject.SetActive(false);
            var trigger = restore.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, () => tooltip.gameObject.SetActive(true));
            AddTrigger(trigger, EventTriggerType.PointerExit, () => tooltip.gameObject.SetActive(false));
            AddTrigger(trigger, EventTriggerType.Select, () => tooltip.gameObject.SetActive(true));
            AddTrigger(trigger, EventTriggerType.Deselect, () => tooltip.gameObject.SetActive(false));
            var panel = MenuWidgets.Panel(parent, "Audio settings", 90, 310, 1080, 500, new Color(.055f, .058f, .06f));
            var viewport = MenuWidgets.Rect(panel.transform, "Viewport", 0, 0, 1080, 500); viewport.gameObject.AddComponent<RectMask2D>();
            view = viewport;
            content = MenuWidgets.Rect(viewport, "Settings content", 0, 0, 1050, 820);
            var scroll = panel.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
            var bar = MenuWidgets.Panel(panel.transform, "Scroll track", 1060, 4, 14, 492, MenuWidgets.Ink);
            var thumb = MenuWidgets.Panel(bar.transform, "Scroll handle", 0, 0, 14, 70, MenuWidgets.Amber);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = thumb.rectTransform;
            scrollbar.targetGraphic = thumb; scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
            thumb.rectTransform.sizeDelta = Vector2.zero; thumb.rectTransform.anchoredPosition = Vector2.zero;
            BuildRows();
            status = MenuWidgets.Label(parent, "", 90, 817, 1160, 60, 19);
            var save = MenuWidgets.Button(parent, "SAVE & EXIT", 90, 905, 520, 64, Commit, true);
            MenuWidgets.Button(parent, "BACK", 640, 905, 300, 64, back);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(save.gameObject);
        }
        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        { var entry = new EventTrigger.Entry { eventID = type }; entry.callback.AddListener(_ => action()); trigger.triggers.Add(entry); }
        private void BuildRows()
        {
            foreach (Transform child in content) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            MenuWidgets.Label(content, "VOICE CHAT", 24, 12, 345, 42, 23);
            chatText = MenuWidgets.Button(content, "", 390, 12, 590, 42, () =>
            { working.VoiceEnabled = !working.VoiceEnabled; Preview(); }).GetComponentInChildren<Text>();
            MenuWidgets.Label(content, "TOGGLE MICROPHONE", 24, 64, 345, 42, 23);
            MenuWidgets.Label(content, "P  —  press to enable / mute", 400, 64, 570, 42, 22);
            MenuWidgets.Label(content, "MICROPHONE", 24, 116, 345, 42, 23);
            micButton = MenuWidgets.Button(content, "", 390, 116, 590, 42, () => voice.SetMicrophone(!voice.MicrophoneEnabled));
            micText = micButton.GetComponentInChildren<Text>();
            input = MenuWidgets.Dropdown(content, 174, "INPUT DEVICE");
            input.onValueChanged.AddListener(i => { if (i < inputIds.Count) { working.Input = inputIds[i]; StopTests(); Preview(); } });
            testButton = MenuWidgets.Button(content, "TEST MICROPHONE (LOCAL ONLY)", 390, 230, 590, 42, () =>
            { if (voice.TestingMicrophone) voice.StopMicrophoneTest(); else voice.TestMicrophone(); });
            testText = testButton.GetComponentInChildren<Text>();
            MenuWidgets.Label(content, "MICROPHONE LEVEL", 24, 279, 345, 40, 23);
            MenuWidgets.Panel(content, "Meter background", 390, 293, 590, 15, new Color(.16f, .17f, .17f));
            meter = MenuWidgets.Panel(content, "Meter level", 390, 293, 1, 15, MenuWidgets.Amber); meter.raycastTarget = false;
            output = MenuWidgets.Dropdown(content, 336, "OUTPUT DEVICE");
            output.onValueChanged.AddListener(i => { if (i < outputIds.Count) { working.Output = outputIds[i]; StopTests(); Preview(); } });
            outputTest = MenuWidgets.Button(content, "TEST SOUND: LEFT → RIGHT", 390, 393, 440, 42, () => voice.Devices.PlayTest());
            MenuWidgets.Button(content, "STOP", 846, 393, 134, 42, () => voice.Devices.StopTest());
            MenuWidgets.Label(content, "OUTPUT SIGNAL", 24, 435, 345, 18, 16);
            MenuWidgets.Panel(content, "Output meter background", 390, 440, 590, 7, new Color(.16f,.17f,.17f));
            outputMeter = MenuWidgets.Panel(content, "Output meter", 390, 440, 1, 7, MenuWidgets.Amber);
            outputMeter.raycastTarget = false;
            MenuWidgets.Slider(content, 454, "MASTER VOLUME", working.Master, v => { working.Master = v; Preview(); });
            MenuWidgets.Slider(content, 510, "SFX VOLUME", working.Effects, v => { working.Effects = v; Preview(); });
            MenuWidgets.Slider(content, 566, "MUSIC VOLUME", working.Music, v => { working.Music = v; Preview(); });
            MenuWidgets.Slider(content, 622, "VOICE VOLUME", working.Voice, v => { working.Voice = v; Preview(); });
            MenuWidgets.Label(content, "Microphone starts OFF each session. Local tests never broadcast.\nIn-game options do not pause the world. Scroll for player voice controls.", 24, 684, 990, 72, 20);
            float y = 770;
            foreach (int id in voice.PeerIds)
            {
                if (voice.IsSelf(id)) continue;
                int peer = id;
                var toggle = MenuWidgets.Button(content, (voice.PeerMuted(id) ? "UNMUTE " : "MUTE ") + voice.PeerName(id), 24, y, 956, 44, null);
                toggle.onClick.AddListener(() =>
                {
                    voice.SetPeer(peer, !voice.PeerMuted(peer), voice.PeerVolume(peer));
                    toggle.GetComponentInChildren<Text>().text = (voice.PeerMuted(peer) ? "UNMUTE " : "MUTE ") + voice.PeerName(peer);
                });
                MenuWidgets.Slider(content, y + 48, "PLAYER VOICE", voice.PeerVolume(peer), v => voice.SetPeer(peer, voice.PeerMuted(peer), v));
                y += 110;
            }
            content.sizeDelta = new Vector2(1050, y + 12);
            peerSignature = PeerSignature();
            inputSignature = outputSignature = null; RefreshDevices();
        }
        private void Preview() { working.Apply(voice); }
        private void Restore()
        { StopTests(); voice.SetMicrophone(false); working = AudioPreferences.Defaults; Preview(); BuildRows(); }
        private void Commit()
        {
            try
            {
                working = working.AvailableDevices(voice.Devices); Preview(); working.Save();
                StopTests(); voice.Devices.PreviewingPreferences = false; finished = true; exit();
            }
            catch (Exception e) { dialog.Error("Cannot save audio settings", e.Message); }
        }
        public void Discard()
        {
            if (finished) return;
            finished = true;
            if (voice == null || voice.Devices == null) return;
            StopTests(); original.AvailableDevices(voice.Devices).Apply(voice); voice.Devices.PreviewingPreferences = false;
        }
        private void StopTests() { voice.StopMicrophoneTest(); voice.Devices.StopTest(); }
        public void Tick()
        {
            if (finished) return;
            chatText.text = working.VoiceEnabled ? "ON" : "OFF";
            micText.text = voice.MicrophoneEnabled ? "ON — CLICK TO MUTE" : "OFF";
            micButton.interactable = voice.Connected && voice.Devices.Available && working.VoiceEnabled;
            testButton.interactable = voice.Devices.Available;
            outputTest.interactable = true; // The default Unity output still works if the native bridge is unavailable.
            testText.text = voice.TestingMicrophone ? "STOP MICROPHONE TEST" : "TEST MICROPHONE (LOCAL ONLY)";
            meter.rectTransform.sizeDelta = new Vector2(590 * Mathf.Clamp01(voice.TestingMicrophone ? voice.MicrophoneLevel * 4 : 0), 15);
            meter.color = voice.MicrophoneLevel >= .98f ? Color.red : MenuWidgets.Amber;
            outputMeter.rectTransform.sizeDelta = new Vector2(590 * Mathf.Clamp01(voice.Devices.OutputPeak * 4), 7);
            status.text = voice.Status + "  /  " + voice.Devices.Status + (Dirty ? "\nUnsaved changes" : "");
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .5f;
                if (peerSignature != PeerSignature()) BuildRows(); else RefreshDevices();
            }
            var focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focus != lastFocus && focus != null && focus.transform.IsChildOf(content))
            {
                Canvas.ForceUpdateCanvases();
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(view, focus.transform);
                float shift = bounds.min.y < view.rect.yMin ? view.rect.yMin - bounds.min.y :
                    bounds.max.y > view.rect.yMax ? view.rect.yMax - bounds.max.y : 0;
                var position = content.anchoredPosition;
                position.y = Mathf.Clamp(position.y + shift, 0, Mathf.Max(0, content.rect.height - view.rect.height));
                content.anchoredPosition = position;
            }
            lastFocus = focus;
        }
        private void RefreshDevices()
        {
            // An output hot-unplug falls back in the service; reflect the actual preview in the form.
            if (!string.IsNullOrEmpty(working.Output) && AudioDeviceService.Find(voice.Devices.Outputs, working.Output) < 0)
                working.Output = voice.Devices.OutputId;
            Fill(input, voice.Devices.Inputs, working.Input, inputIds, ref inputSignature);
            Fill(output, voice.Devices.Outputs, working.Output, outputIds, ref outputSignature);
        }
        private string PeerSignature()
        {
            var names = new List<string>();
            foreach (int id in voice.PeerIds) if (!voice.IsSelf(id)) names.Add(id + ":" + voice.PeerName(id));
            names.Sort(StringComparer.Ordinal); return string.Join("|", names);
        }
        private static void Fill(Dropdown picker, List<AudioDeviceService.Device> devices, string chosen, List<string> ids, ref string previous)
        {
            string signature = chosen + "|";
            foreach (var device in devices) signature += device.Id + device.Name + "|";
            if (signature == previous) return;
            previous = signature; picker.Hide(); picker.ClearOptions(); ids.Clear(); ids.Add("");
            var options = new List<string> { "Automatic (system default)" };
            var occurrences = new Dictionary<string, int>();
            foreach (var device in devices)
            {
                occurrences.TryGetValue(device.Name, out int count); occurrences[device.Name] = count + 1;
                bool duplicate = devices.FindAll(d => d.Name == device.Name).Count > 1;
                ids.Add(device.Id);
                options.Add(device.Name + (duplicate ? " #" + (count + 1) : "") + (device.Default ? " (default)" : ""));
            }
            int selected = ids.IndexOf(chosen ?? "");
            if (selected < 0) { selected = ids.Count; ids.Add(chosen); options.Add("Unavailable — choose another device"); }
            picker.AddOptions(options); picker.SetValueWithoutNotify(selected); picker.RefreshShownValue();
        }
    }
}
