using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;

namespace SunkCost.Audio
{
    public enum AudioCategory { Effects, Music, Voice, Diagnostic }
    public sealed class AudioDeviceService : MonoBehaviour
    {
        public readonly struct Device
        {
            public readonly string Id, Name;
            public readonly bool Default;
            public Device(string id, string name, bool isDefault) { Id = id; Name = name; Default = isDefault; }
        }
        public List<Device> Inputs { get; } = new();
        public List<Device> Outputs { get; } = new();
        public bool Available { get; private set; }
        public string Status { get; private set; } = "Audio initializing";
        public string InputId { get; private set; }
        public string OutputId { get; private set; }
        public float Master { get; private set; }
        public float EffectsVolume { get; private set; }
        public float MusicVolume { get; private set; }
        public bool PreviewingPreferences { get; set; }
        public event Action InputChanged;
        public event Action ConfigurationChangedEvent;
        public event Action BeforeShutdown;
        private float nextRefresh;
        private string defaultOutput;
        private string defaultInput;
        private AudioMixerGroup outputGroup;
        private AudioMixer mixer;
        private AudioMixerGroup effectsGroup, musicGroup;
        private AudioSource testSource;
        private AudioClip testClip;
        private bool shuttingDown;

        private void Awake()
        {
            Application.runInBackground = true;
            InputId = PlayerPrefs.GetString("Audio.Input", ""); OutputId = PlayerPrefs.GetString("Audio.Output", "");
            Master = PlayerPrefs.GetFloat("Audio.Master", 1f); AudioListener.volume = Master;
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Effects", .9f));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Music", .7f));
            mixer = Resources.Load<AudioMixer>("SunkCostOutput");
            outputGroup = Group("Master"); effectsGroup = Group("SFX"); musicGroup = Group("Music");
            try
            {
                Available = NativeAudioBridge.sc_init() == 0;
                if (!Available) { Status = "Audio devices unavailable; game audio uses system output."; return; }
                RefreshDevices(); OpenOutput();
                AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged;

            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            { Available = false; Status = "Voice plugin unavailable; game audio uses system output."; Debug.LogWarning(Status + " " + e.GetType().Name); }
        }
        private AudioMixerGroup Group(string name)
        {
            var groups = mixer != null ? mixer.FindMatchingGroups(name) : null;
            return groups != null && groups.Length > 0 ? groups[0] : null;
        }
        private void Start() => SetCategoryVolumes(EffectsVolume, MusicVolume, false);
        public void SetCategoryVolumes(float effects, float music, bool persist = true)
        {
            EffectsVolume = Mathf.Clamp01(effects); MusicVolume = Mathf.Clamp01(music);
            if (mixer != null)
            {
                mixer.SetFloat("SfxVolume", EffectsVolume <= 0 ? -80 : 20 * Mathf.Log10(EffectsVolume));
                mixer.SetFloat("MusicVolume", MusicVolume <= 0 ? -80 : 20 * Mathf.Log10(MusicVolume));
            }
            if (persist && !PreviewingPreferences)
            { PlayerPrefs.SetFloat("Audio.Effects", EffectsVolume); PlayerPrefs.SetFloat("Audio.Music", MusicVolume); }
        }

        private void Update()
        {
            if (!Available) return;
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 2f; RefreshDevices(); }
        }

        private static void ReadDevices(int mic, List<Device> list)
        {
            list.Clear();
            for (int i = 0; i < NativeAudioBridge.sc_count(mic); i++)
            {
                var id = new StringBuilder(64); NativeAudioBridge.sc_id(mic, i, id, id.Capacity);
                list.Add(new Device(id.ToString(), Marshal.PtrToStringUTF8(NativeAudioBridge.sc_name(mic, i)), NativeAudioBridge.sc_default(mic, i) != 0));
            }
        }
        public int InputIndex => Find(Inputs, InputId);
        public static int Find(List<Device> devices, string id) => string.IsNullOrEmpty(id) ? -1 : devices.FindIndex(d => d.Id == id);

        public void RefreshDevices()
        {
            if (!Available || NativeAudioBridge.sc_refresh() != 0) return;
            ReadDevices(1, Inputs); ReadDevices(0, Outputs);
            string currentInput = Inputs.Find(d => d.Default).Id ?? "";
            if (string.IsNullOrEmpty(InputId) && defaultInput != null && currentInput != defaultInput)
            { InputChanged?.Invoke(); Status = "Default microphone changed — OFF. Unmute when ready."; }
            defaultInput = currentInput;
            if (!string.IsNullOrEmpty(InputId) && Find(Inputs, InputId) < 0) { InputChanged?.Invoke(); Status = "Selected microphone unavailable. Select a microphone, then unmute."; }
            string currentDefault = Outputs.Find(d => d.Default).Id ?? "";
            if (!string.IsNullOrEmpty(OutputId) && Find(Outputs, OutputId) < 0)
            { OutputId = ""; if (!PreviewingPreferences) PlayerPrefs.SetString("Audio.Output", ""); OpenOutput(); Status = "Output disconnected; using Automatic."; }
            else if (string.IsNullOrEmpty(OutputId) && defaultOutput != null && currentDefault != defaultOutput) OpenOutput();
            defaultOutput = currentDefault;
        }
        public void SelectInput(string id) { InputChanged?.Invoke(); InputId = id; if (!PreviewingPreferences) PlayerPrefs.SetString("Audio.Input", id); Status = "Microphone changed; press P to enable."; }
        public void SelectOutput(string id) { OutputId = id; if (!PreviewingPreferences) PlayerPrefs.SetString("Audio.Output", id); OpenOutput(); }
        public void SetMaster(float value) { Master = Mathf.Clamp01(value); if (Available && outputGroup != null) NativeAudioBridge.sc_master(Master); else AudioListener.volume = Master; if (!PreviewingPreferences) PlayerPrefs.SetFloat("Audio.Master", Master); }
        private void ConfigurationChanged(bool changed)
        {
            if (shuttingDown || !Available) return;
            OpenOutput();
            if (testClip != null) Destroy(testClip); testClip = null;
            ConfigurationChangedEvent?.Invoke();
        }
        private void OpenOutput()
        {
            
            if (!Available) return;
            if (AudioSettings.speakerMode != AudioSpeakerMode.Stereo)
            { Status = "Select stereo audio for voice output routing; game audio remains on default."; return; }
            if (outputGroup == null) { Status = "Output mixer missing; using system output."; return; }
            int result = NativeAudioBridge.sc_output_start(Find(Outputs, OutputId), AudioSettings.outputSampleRate);
            NativeAudioBridge.sc_master(Master);
            AudioListener.volume = 1f;
            Status = result == 0 ? "Audio ready" : "Cannot open selected output; game audio uses system output.";
        }
        public void PlayTest()
        {
            if (testSource == null) testSource = gameObject.AddComponent<AudioSource>();
            if (testClip == null || testClip.channels != 2)
            {
                const int rate = 48000; var samples = new float[rate * 2];
                for (int i = 0; i < rate; i++)
                {
                    float gain = 0.08f * Mathf.Sin(Mathf.PI * (i % (rate / 2)) / (rate / 2));
                    samples[i * 2 + (i < rate / 2 ? 0 : 1)] = gain * Mathf.Sin(2f * Mathf.PI * 440f * i / rate);
                }
                testClip = AudioClip.Create("Audio output test left then right", rate, 2, rate, false); testClip.SetData(samples, 0);
            }
            Route(testSource, AudioCategory.Diagnostic); testSource.spatialBlend = 0; testSource.clip = testClip; testSource.Play();
        }
        public float OutputPeak => Available ? NativeAudioBridge.sc_output_peak() : 0f;
        public uint MixCallbacks => Available ? NativeAudioBridge.sc_mix_callbacks() : 0;
        public uint OutputCallbacks => Available ? NativeAudioBridge.sc_output_nonzero() : 0;
        public void Route(AudioSource source, AudioCategory category = AudioCategory.Effects)
        {
            source.outputAudioMixerGroup = category == AudioCategory.Effects ? effectsGroup ?? outputGroup :
                category == AudioCategory.Music ? musicGroup ?? outputGroup : outputGroup;
        }
        public static void RouteSource(AudioSource source)
        {
            var service = FindAnyObjectByType<AudioDeviceService>();
            if (service != null && source != null) service.Route(source);
        }
        public void StopTest() { if (testSource != null) testSource.Stop(); }
        public void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true; 
            BeforeShutdown?.Invoke();
            AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged;
            if (Available) NativeAudioBridge.sc_shutdown();
            if (testClip != null) Destroy(testClip);
        }
        private void OnDestroy() => Shutdown();
    }
}
