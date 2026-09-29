using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SunkCost.Audio;
using SunkCost.Net;
using SunkCost.UI;
using SunkCost.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MenuRuntimeChecks
{
    private readonly List<string> failures = new();
    private readonly List<string> passed = new();
    private GameObject subject;
    private MenuRoot menu;
    private ProximityVoice voice;
    private PrototypeSessionController session;
    private string output;
    private void Check(bool value, string name) { if (value) passed.Add(name); else failures.Add(name); }
    private Button Button(string text) => subject.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>()?.text == text);
    private void Click(string text) => Button(text).onClick.Invoke();
    private void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failures.Add(text + "\n" + trace); }
    private IEnumerator steps;
    private int frame = -1;
    private string previousDirectory;
    private AudioPreferences preferences;
    private readonly Dictionary<string, string> strings = new();
    private readonly Dictionary<string, float> numbers = new();
    private bool voiceKeyExisted;
    private int voicePreference;
    public static string Status { get; private set; } = "Not run";
    [UnityEditor.MenuItem("Sunk Cost/Prototype/Check main menu (Play Mode, disconnected)")]
    public static void Run()
    {
        var current = UnityEngine.Object.FindFirstObjectByType<PrototypeSessionController>();
        if (!UnityEditor.EditorApplication.isPlaying || current == null || current.InRoom || current.Busy || Status == "Running")
            throw new InvalidOperationException("Enter Play Mode at the disconnected main menu first.");
        var check = new MenuRuntimeChecks();
        check.session = current; check.subject = current.gameObject;
        check.voice = current.GetComponent<ProximityVoice>(); check.menu = current.GetComponent<MenuRoot>();
        check.preferences = AudioPreferences.Capture(check.voice);
        check.previousDirectory = SaveSlots.DirectoryOverride;
        foreach (string key in new[] { "Audio.Input", "Audio.Output" })
            if (PlayerPrefs.HasKey(key)) check.strings[key] = PlayerPrefs.GetString(key);
        foreach (string key in new[] { "Audio.Master", "Audio.Effects", "Audio.Music", "Audio.Voice" })
            if (PlayerPrefs.HasKey(key)) check.numbers[key] = PlayerPrefs.GetFloat(key);
        check.voiceKeyExisted = PlayerPrefs.HasKey("Audio.VoiceEnabled");
        check.voicePreference = PlayerPrefs.GetInt("Audio.VoiceEnabled", 1);
        Status = "Running"; check.steps = check.Execute();
        UnityEditor.EditorApplication.update += check.Tick;
    }
    private void Tick()
    {
        if (Time.frameCount == frame && UnityEditor.EditorApplication.isPlaying) return;
        frame = Time.frameCount;
        try { if (UnityEditor.EditorApplication.isPlaying && steps.MoveNext()) return; }
        catch (Exception e) { failures.Add(e.ToString()); }
        UnityEditor.EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        SaveSlots.DirectoryOverride = previousDirectory;
        if (voice != null)
        {
            voice.Devices.PreviewingPreferences = true;
            preferences.Apply(voice); voice.Devices.PreviewingPreferences = false;
        }
        foreach (string key in new[] { "Audio.Input", "Audio.Output" })
            if (strings.TryGetValue(key, out var value)) PlayerPrefs.SetString(key, value); else PlayerPrefs.DeleteKey(key);
        foreach (string key in new[] { "Audio.Master", "Audio.Effects", "Audio.Music", "Audio.Voice" })
            if (numbers.TryGetValue(key, out var value)) PlayerPrefs.SetFloat(key, value); else PlayerPrefs.DeleteKey(key);
        if (voiceKeyExisted) PlayerPrefs.SetInt("Audio.VoiceEnabled", voicePreference); else PlayerPrefs.DeleteKey("Audio.VoiceEnabled");
        PlayerPrefs.Save();
        Status = failures.Count == 0 ? "PASS" : "FAIL";
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/main-menu-checks.log", string.Join("\n", passed.Select(p => "PASS " + p)) + "\n" + string.Join("\n", failures.Select(p => "FAIL " + p)));
        Debug.Log("Main menu checks: " + Status + "; see Temp/main-menu-checks.log");
    }
    private IEnumerator Execute()
    {
        output = "Temp/main-menu-checks.log";
        Application.logMessageReceived += Log;
        SaveSlots.DirectoryOverride = Path.Combine(Application.temporaryCachePath, "menu-save-checks-" + Guid.NewGuid().ToString("N"));
        try { SaveChecks(); }
        catch (Exception e) { failures.Add("Save checks: " + e); }
        typeof(MenuRoot).GetMethod("ShowMain", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        yield return null; yield return null;
        try
        {
            Check(menu.CoversGameplay, "Main menu covers background");
            Check(Button("JOIN GAME").onClick.GetPersistentEventCount() == 0, "Join has no persistent action");
            Click("JOIN GAME"); Check(session.State == SunkCost.Net.SessionState.Menu, "Join does not start connection");
            Click("HOST GAME");
            Check(subject.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("SAVE ")) == 3, "Three save rows");
            Click("RENAME"); var input = subject.GetComponentInChildren<InputField>();
            input.text = "   "; Click("SAVE"); Check(input.gameObject.activeInHierarchy, "Blank rename stays open");
            input.text = "Crew renamed"; Click("SAVE");
        }
        catch (Exception e) { failures.Add("Menu/save controls: " + e); }
        yield return null;
        try
        {
            Check(SaveSlots.Load(0).name == "Crew renamed", "Rename persisted correctly");
            Click("DELETE"); Check(EventSystem.current.currentSelectedGameObject.GetComponentInChildren<Text>().text == "NO", "Confirmation defaults to No");
            Check(!Button("HOST SAVE").IsInteractable(), "Modal disables underlying keyboard targets");
            Click("NO"); Check(SaveSlots.Exists(0), "Delete No preserves file");
            Click("DELETE"); Click("YES"); Check(!SaveSlots.Exists(0), "Delete Yes deletes selected file");
            Check(!Button("RENAME").interactable && !Button("DELETE").interactable, "Empty-slot actions disabled");
            Click("BACK"); Click("OPTIONS");
            Check(!Button("VIDEO").interactable && Button("AUDIO").interactable, "Only Audio tab active");
            var slider = subject.GetComponentsInChildren<Slider>().First();
            float before = voice.Devices.Master;
            float savedBefore = PlayerPrefs.GetFloat("Audio.Master", 1);
            slider.value = .31f; Check(Mathf.Approximately(voice.Devices.Master, .31f), "Master previews immediately");
            Check(Mathf.Approximately(PlayerPrefs.GetFloat("Audio.Master", 1), savedBefore), "Preview not persisted");
            Click("BACK"); Click("NO"); Check(Mathf.Approximately(voice.Devices.Master, .31f), "Discard No keeps edit");
            Click("BACK"); Click("YES"); Check(Mathf.Approximately(voice.Devices.Master, before), "Discard restores master");
            Click("OPTIONS"); subject.GetComponentsInChildren<Slider>().First().value = .42f;
            Click("SAVE & EXIT"); Check(Mathf.Approximately(PlayerPrefs.GetFloat("Audio.Master"), .42f), "Save commits master");
            Check(!voice.MicrophoneEnabled && !voice.TestingMicrophone, "Settings never unmute or start capture");
            Click("OPTIONS"); subject.GetComponentsInChildren<Button>().First(b => b.name == "↺").onClick.Invoke(); Click("YES");
            Check(Mathf.Approximately(voice.Devices.Master, 1) && Mathf.Approximately(PlayerPrefs.GetFloat("Audio.Master"), .42f), "Defaults preview only");
            Click("BACK"); Click("YES"); Check(Mathf.Approximately(voice.Devices.Master, .42f), "Default reset can be discarded");
            var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>("SunkCostOutput");
            Check(mixer.FindMatchingGroups("SFX").Length == 1 && mixer.FindMatchingGroups("Music").Length == 1, "Mixer category groups imported");
            Check(mixer.GetFloat("SfxVolume", out _) && mixer.GetFloat("MusicVolume", out _), "Mixer gain parameters exposed");
            Field(session, "state", SunkCost.Net.SessionState.StartingServer);
        }
        catch (Exception e) { failures.Add("Audio/dialog checks: " + e); }
        yield return null;
        yield return null;
        try { Check(subject.GetComponentsInChildren<Text>().Any(t => t.text == "CONNECTING..."), "Session progress screen follows state"); Field(session, "state", SunkCost.Net.SessionState.Menu); Field(session, "message", "Test failure"); }
        catch (Exception e) { failures.Add(e.ToString()); }
        yield return null;
        yield return null;
        try { Check(Button("OK") != null, "Connection failure uses OK dialog"); Click("OK"); }
        catch (Exception e) { failures.Add("Error dialog: " + e); }
        Click("OPTIONS");
        yield return null;
        yield return null; // Dropdown initializes its tween runner in Start.
        foreach (var dropdown in subject.GetComponentsInChildren<Dropdown>())
        {
            try { dropdown.Show(); Check(subject.GetComponentsInChildren<Canvas>().Length > 1, "Device dropdown opens"); dropdown.Hide(); }
            catch (Exception e) { failures.Add("Dropdown: " + e); }
            yield return null;
        }
        yield return null;
        Click("BACK");
        Application.logMessageReceived -= Log;
        File.WriteAllText(output, string.Join("\n", passed.Select(p => "PASS " + p)) + "\n" + string.Join("\n", failures.Select(p => "FAIL " + p)));

    }
    private void SaveChecks()
    {
        Check(SaveSlots.TryWrite(0, new RunSaveData { name = "Save 1", day = 2, balance = 321 }, out _), "Save write");
        Check(SaveSlots.TryLoad(0, out var saved, out _) && saved.day == 2 && saved.balance == 321, "Save roundtrip preserves progress");
        Check(!SaveSlots.TryRename(0, " \t", out _), "Blank rename rejected");
        Check(SaveSlots.TryRename(0, new string('x', 40), out _) && SaveSlots.Load(0).name.Length == 24, "Rename limit");
        SaveSlots.TryRename(0, "Save 1", out _);
        using (var locked = new FileStream(SaveSlots.PathOf(0), FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!SaveSlots.TryWrite(0, new RunSaveData { name = "Must not replace" }, out _), "Locked save reports failed replacement");
        Check(SaveSlots.Load(0).balance == 321, "Failed replacement preserves original progress");
        File.WriteAllText(SaveSlots.PathOf(1), "{}");
        Check(!SaveSlots.TryLoad(1, out _, out _) && SaveSlots.Exists(1), "Malformed existing save is not empty");
        File.WriteAllText(SaveSlots.PathOf(1), "{\"version\":999}");
        Check(!SaveSlots.TryLoad(1, out _, out _), "Unsupported save version rejected");
        SaveSlots.TryDelete(1, out _);
    }
}
