using SunkCost.Net;
using UnityEngine;

namespace SunkCost.Audio
{
    // Local preferences only. The same mute action is used by this button and P.
    public sealed class VoiceSettingsUI : MonoBehaviour
    {
        private ProximityVoice voice;
        private PrototypeSessionController session;
        private bool open;
        private int dropdown;
        private Vector2 scroll;
#if UNITY_EDITOR
        public void ShowForChecks() { open = true; SessionInputGate.OpenMenu(); }
#endif
        private void Start() { voice = GetComponent<ProximityVoice>(); session = GetComponent<PrototypeSessionController>(); }
        private void OnGUI()
        {
            if (voice == null || session == null) return;
            bool menu = !session.InRoom || SessionInputGate.MenuOpen;
            if (!menu && open) { open = false; voice.StopMicrophoneTest(); voice.Devices.StopTest(); }
            // P must not toggle while entering a lobby address or player name.
            voice.TextEntryActive = menu && GUIUtility.keyboardControl != 0;
            float width = Mathf.Min(410, Screen.width - 24);
            float height = menu && open ? Screen.height - 24 : menu ? 64 : 30;
            GUILayout.BeginArea(new Rect(Screen.width - width - 12, 12, width, height), GUI.skin.box);
            GUILayout.Label(voice.TestingMicrophone ? "MIC TEST • transmission OFF" : voice.MicrophoneEnabled ? "MIC ON • P to mute" : "MIC OFF • P to unmute");
            if (menu && GUILayout.Button(open ? "Close audio settings" : "Audio settings"))
            { open = !open; if (!open) { voice.StopMicrophoneTest(); voice.Devices.StopTest(); } }
            if (menu && open)
            {
                scroll = GUILayout.BeginScrollView(scroll);
                GUI.enabled = voice.Connected && voice.Devices.Available;
                if (GUILayout.Button(voice.MicrophoneEnabled ? "Mute microphone" : "Unmute microphone")) voice.SetMicrophone(!voice.MicrophoneEnabled);
                GUI.enabled = true;
                GUILayout.Label(voice.Status);
                GUILayout.Label(voice.Devices.Status);
                DevicePicker("Microphone", 1, voice.Devices.Inputs, voice.Devices.InputId);
                GUI.enabled = voice.Devices.Available;
                if (GUILayout.Button(voice.TestingMicrophone ? "Stop microphone test" : "Test microphone (local only)"))
                { if (voice.TestingMicrophone) voice.StopMicrophoneTest(); else voice.TestMicrophone(); }
                Meter("Microphone", voice.MicrophoneLevel);
                DevicePicker("Sound output", 2, voice.Devices.Outputs, voice.Devices.OutputId);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Test sound: left → right")) voice.Devices.PlayTest();
                if (GUILayout.Button("Stop")) voice.Devices.StopTest();
                GUILayout.EndHorizontal(); Meter("Output", voice.Devices.OutputPeak);
                GUI.enabled = true;
                GUILayout.Label("Master volume " + Mathf.RoundToInt(voice.Devices.Master * 100) + "%");
                float master = GUILayout.HorizontalSlider(voice.Devices.Master, 0, 1); if (master != voice.Devices.Master) voice.Devices.SetMaster(master);
                GUILayout.Label("Voice volume " + Mathf.RoundToInt(voice.VoiceVolume * 100) + "%");
                float volume = GUILayout.HorizontalSlider(voice.VoiceVolume, 0, 1); if (volume != voice.VoiceVolume) voice.SetVoiceVolume(volume);
                foreach (int id in voice.PeerIds)
                {
                    if (voice.IsSelf(id)) continue;
                    GUILayout.Label(voice.PeerName(id));
                    bool muted = GUILayout.Toggle(voice.PeerMuted(id), "Mute this player");
                    float peerVolume = GUILayout.HorizontalSlider(voice.PeerVolume(id), 0, 1);
                    if (muted != voice.PeerMuted(id) || peerVolume != voice.PeerVolume(id)) voice.SetPeer(id, muted, peerVolume);
                }
                GUILayout.Label("Microphone starts OFF each session. Test bars show signal, not confirmed audibility.");
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
        private void DevicePicker(string label, int kind, System.Collections.Generic.List<AudioDeviceService.Device> list, string selected)
        {
            int index = AudioDeviceService.Find(list, selected);
            string name = string.IsNullOrEmpty(selected) ? "Automatic (system default)" : index < 0 ? "Unavailable — choose device" : list[index].Name;
            if (GUILayout.Button(label + ": " + name + " ▾")) dropdown = dropdown == kind ? 0 : kind;
            if (dropdown != kind) return;
            if (GUILayout.Button("Automatic (system default)")) Select(kind, "");
            foreach (var device in list) if (GUILayout.Button(device.Name + (device.Default ? " (default)" : ""))) Select(kind, device.Id);
        }
        private void Select(int kind, string id) { if (kind == 1) voice.Devices.SelectInput(id); else voice.Devices.SelectOutput(id); dropdown = 0; }
        private static void Meter(string label, float level)
        {
            GUILayout.Label(label + (level >= .98f ? " — clipping" : " level"));
            Rect rect = GUILayoutUtility.GetRect(100, 10); GUI.Box(rect, GUIContent.none);
            Color previous = GUI.color; GUI.color = level >= .98f ? Color.red : Color.green;
            GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, (rect.width - 4) * Mathf.Clamp01(level * 4), 6), Texture2D.whiteTexture); GUI.color = previous;
        }
    }
}
