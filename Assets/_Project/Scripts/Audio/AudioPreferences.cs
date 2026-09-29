using System;
using UnityEngine;

namespace SunkCost.Audio
{
    // The microphone is deliberately absent: permission to transmit is never a saved preference.
    [Serializable]
    public struct AudioPreferences : IEquatable<AudioPreferences>
    {
        public string Input, Output;
        public float Master, Effects, Music, Voice;
        public bool VoiceEnabled;
        public static AudioPreferences Defaults => new() { Input = "", Output = "", Master = 1, Effects = .9f, Music = .7f, Voice = 1, VoiceEnabled = true };
        public static AudioPreferences Capture(ProximityVoice voice) => new()
        {
            Input = voice.Devices.InputId, Output = voice.Devices.OutputId, Master = voice.Devices.Master,
            Effects = voice.Devices.EffectsVolume, Music = voice.Devices.MusicVolume,
            Voice = voice.VoiceVolume, VoiceEnabled = voice.VoiceChatEnabled
        };
        public void Apply(ProximityVoice voice)
        {
            var devices = voice.Devices;
            if (devices.InputId != Input) devices.SelectInput(Input ?? "");
            if (devices.OutputId != Output) devices.SelectOutput(Output ?? "");
            devices.SetMaster(Master); devices.SetCategoryVolumes(Effects, Music, false);
            voice.SetVoiceVolume(Voice, false);
            if (voice.VoiceChatEnabled != VoiceEnabled) voice.SetVoiceChat(VoiceEnabled, false);
        }
        public void Save()
        {
            PlayerPrefs.SetString("Audio.Input", Input ?? ""); PlayerPrefs.SetString("Audio.Output", Output ?? "");
            PlayerPrefs.SetFloat("Audio.Master", Master); PlayerPrefs.SetFloat("Audio.Effects", Effects);
            PlayerPrefs.SetFloat("Audio.Music", Music); PlayerPrefs.SetFloat("Audio.Voice", Voice);
            PlayerPrefs.SetInt("Audio.VoiceEnabled", VoiceEnabled ? 1 : 0); PlayerPrefs.Save();
        }
        public AudioPreferences AvailableDevices(AudioDeviceService devices)
        {
            if (!devices.Available) return this;
            var result = this;
            if (!string.IsNullOrEmpty(Input) && AudioDeviceService.Find(devices.Inputs, Input) < 0) result.Input = "";
            if (!string.IsNullOrEmpty(Output) && AudioDeviceService.Find(devices.Outputs, Output) < 0) result.Output = "";
            return result;
        }
        public bool Equals(AudioPreferences other) => Input == other.Input && Output == other.Output &&
            Mathf.Approximately(Master, other.Master) && Mathf.Approximately(Effects, other.Effects) &&
            Mathf.Approximately(Music, other.Music) && Mathf.Approximately(Voice, other.Voice) && VoiceEnabled == other.VoiceEnabled;
        public override bool Equals(object obj) => obj is AudioPreferences other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Input, Output, Master, Effects, Music, Voice, VoiceEnabled);
    }
}
