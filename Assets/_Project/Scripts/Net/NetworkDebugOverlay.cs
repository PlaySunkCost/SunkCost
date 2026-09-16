using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Net
{
    // Developer-only panel showing the network state on this machine: role, local
    // client id, RTT, and per-object owner and simulation writer. F3 toggles it,
    // F4 writes the same text to the console/Player.log and the clipboard so two
    // machines' views can be pasted side by side. Read-only: it never sends or
    // changes anything.
    public sealed class NetworkDebugOverlay : MonoBehaviour
    {
        public const string ObjectName = "Network Debug Overlay";
        private const float RefreshInterval = 0.25f;
        private const float PanelWidth = 560f;
        private const string SimHereColor = "#7CFC7C";
        private const string ReplicatedColor = "#A0A0A0";
        private const string OwnRowColor = "#7FE9FF";

        // Public so editor commands can drive it without pressing keys.
        public bool Visible { get; set; }
        public NetworkDebugSnapshot Current { get; private set; }

        private static NetworkDebugOverlay instance;
        private float nextRefresh;
        private bool loggedGuiError;
        private GUIStyle labelStyle;

        // Creates the overlay in the editor and in development builds with no scene
        // or prefab change. Debug.isDebugBuild is true in both; release builds
        // never get this object.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Debug.isDebugBuild || instance != null) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NetworkDebugOverlay>();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f3Key.wasPressedThisFrame) Visible = !Visible;
            if (keyboard.f4Key.wasPressedThisFrame) DumpSnapshot();
            if (Visible && Time.unscaledTime >= nextRefresh) Refresh();
        }

        public void Refresh()
        {
            try
            {
                Current = NetworkDebugSnapshot.Capture();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Current ??= new NetworkDebugSnapshot();
            }
            nextRefresh = Time.unscaledTime + RefreshInterval;
        }

        // F4: the console line ends up in Player.log for post-mortems; the clipboard
        // copy is for pasting into chat during a two-computer session.
        public void DumpSnapshot()
        {
            Refresh();
            string text = Current.ToText();
            Debug.Log(text);
            GUIUtility.systemCopyBuffer = text;
        }

        private void OnGUI()
        {
            if (!Visible || Current == null) return;
            try
            {
                Draw();
            }
            catch (Exception exception)
            {
                if (!loggedGuiError)
                {
                    loggedGuiError = true;
                    Debug.LogException(exception);
                }
            }
        }

        // A straight transcription of Current; no logic here.
        private void Draw()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
            NetworkDebugSnapshot s = Current;

            // Top-right; PrototypeSessionUI owns the top-left (18, 18, 410, 300).
            GUILayout.BeginArea(new Rect(Screen.width - PanelWidth - 18f, 18f, PanelWidth, Screen.height - 36f), GUI.skin.box);
            GUILayout.Label("<b>NETWORK DEBUG</b>   F3 hide · F4 copy/log snapshot", labelStyle);
            string header = $"<b>{s.Role}</b>   client={s.LocalClientId}   transport={s.Transport}   rtt={s.RoundTripTimeMs}ms   tick={s.TickRate}Hz/{s.Tick}";
            if (s.ServerStarted) header += $"   clients={s.ConnectedClients}";
            GUILayout.Label(header, labelStyle);
            if (!string.IsNullOrEmpty(s.Frames)) GUILayout.Label("frames: " + s.Frames, labelStyle);
            GUILayout.Space(4f);
            GUILayout.Label("<b> id   name              owner     sim         detail</b>", labelStyle);

            foreach (NetworkDebugSnapshot.ObjectRow row in s.Objects)
            {
                string simColor = row.IsWriter ? SimHereColor : ReplicatedColor;
                string mark = row.Swatch != null ? $"<color={row.Swatch}>■</color> " : "  ";
                string line = $"{mark}{row.ObjectId,3}   {Pad(row.Name, 17)} {Pad(row.OwnerText, 9)} <color={simColor}>{Pad(row.SimText, 11)}</color> {row.Detail}";
                if (row.IsOwner && row.OwnerId >= 0)
                    line = $"<color={OwnRowColor}>{line}</color>";
                GUILayout.Label(line, labelStyle);
            }

            if (s.Objects.Count == 0)
                GUILayout.Label(s.HasNetworkManager ? "(no spawned objects)" : "(no NetworkManager in the loaded scenes)", labelStyle);
            GUILayout.EndArea();
        }

        private static string Pad(string value, int width)
        {
            value ??= string.Empty;
            if (value.Length > width) value = value.Substring(0, width - 1) + "…";
            return value.PadRight(width);
        }
    }
}
