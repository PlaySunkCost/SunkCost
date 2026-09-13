using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using Steamworks;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Net
{
    public sealed class PrototypeSessionUI : MonoBehaviour
    {
        [SerializeField] private GameObject networkRoot;
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private Transport localTransport;
        [SerializeField] private GameObject steamTransportPrefab;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private string address = "127.0.0.1";
        private bool useSteam;
        private bool steamInitialized;
        private bool sessionStarting;
        private Transport steamTransport;
        private int lastReportedPlayerCount = -1;
        private string status = "Choose Local or Steam, then Host or Join.";

        public bool SessionReady => networkRoot != null && networkRoot.activeInHierarchy &&
                                    networkManager?.ServerManager != null && networkManager.ClientManager != null &&
                                    networkManager.ServerManager.Started && networkManager.ClientManager.Started;

        public string RuntimeDiagnostics
        {
            get
            {
                int playerCount = FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).Length;
                Basketball ball = FindFirstObjectByType<Basketball>();
                return $"server={networkManager?.ServerManager != null && networkManager.ServerManager.Started}, " +
                       $"client={networkManager?.ClientManager != null && networkManager.ClientManager.Started}, " +
                       $"players={playerCount}, ballSpawned={ball != null && ball.IsSpawned}";
            }
        }

        private IEnumerator Start()
        {
            // A one-frame delay lets FishNet and the scene finish Awake before an
            // automated development build starts its session.
            yield return null;
            string[] arguments = Environment.GetCommandLineArgs();
            if (HasArgument(arguments, "-hq-auto-host-local"))
                StartLocalHost();
            else if (TryGetArgumentValue(arguments, "-hq-auto-join-local", out string hostAddress))
                JoinLocal(hostAddress);
            else if (HasArgument(arguments, "-hq-auto-host-steam"))
                StartSteamHost();
            else if (TryGetArgumentValue(arguments, "-hq-auto-join-steam", out string steamId))
                JoinSteam(steamId);
        }

        private void Update()
        {
            if (steamInitialized) SteamAPI.RunCallbacks();
            if (Debug.isDebugBuild && networkRoot != null && networkRoot.activeInHierarchy &&
                networkManager?.ClientManager != null && networkManager.ClientManager.Started)
            {
                int playerCount = FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).Length;
                if (playerCount != lastReportedPlayerCount)
                {
                    lastReportedPlayerCount = playerCount;
                    Debug.Log("HQ runtime: " + RuntimeDiagnostics);
                }
            }
        }

        private void OnDestroy()
        {
            if (steamInitialized) SteamAPI.Shutdown();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18, 18, 410, 300), GUI.skin.box);
            GUILayout.Label("SUNK COST — HQ BASKETBALL");
            GUILayout.Label(status);
            if (networkRoot != null && networkRoot.activeSelf && networkManager != null)
            {
                GUILayout.Label($"Server: {networkManager.ServerManager.Started}   Client: {networkManager.ClientManager.Started}");
                if (steamInitialized) GUILayout.Label("Your Steam ID: " + SteamUser.GetSteamID().m_SteamID);
                if (GUILayout.Button("Leave")) StopSession();
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(!useSteam, "Local / LAN", GUI.skin.button)) useSteam = false;
                if (GUILayout.Toggle(useSteam, "Steam P2P", GUI.skin.button)) useSteam = true;
                GUILayout.EndHorizontal();
                GUILayout.Label(useSteam ? "Host SteamID64 (Join only)" : "Host IP (127.0.0.1 on same PC)");
                address = GUILayout.TextField(address);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host")) StartSession(true);
                if (GUILayout.Button("Join")) StartSession(false);
                GUILayout.EndHorizontal();
                GUILayout.Label("Controls: WASD, Shift, mouse, E pick/drop, left click throw, Esc release cursor");
            }
            GUILayout.EndArea();
        }

        public void StartLocalHost()
        {
            useSteam = false;
            address = "127.0.0.1";
            StartSession(true);
        }

        public void JoinLocal(string hostAddress)
        {
            useSteam = false;
            address = string.IsNullOrWhiteSpace(hostAddress) ? "127.0.0.1" : hostAddress;
            StartSession(false);
        }

        public void StartSteamHost()
        {
            useSteam = true;
            StartSession(true);
        }

        public void JoinSteam(string hostSteamId)
        {
            useSteam = true;
            address = hostSteamId;
            StartSession(false);
        }

        private void StartSession(bool host)
        {
            if (sessionStarting) return;
            if (networkRoot == null || networkManager == null) { status = "Scene setup is incomplete."; return; }
            if (useSteam && !InitializeSteam()) return;
            if (useSteam && steamTransport == null)
            {
                if (steamTransportPrefab == null) { status = "Steam transport prefab is missing."; return; }
                GameObject instance = Instantiate(steamTransportPrefab, networkRoot.transform);
                steamTransport = instance.GetComponent<Transport>();
            }
            Transport selected = useSteam ? steamTransport : localTransport;
            if (selected == null) { status = "Selected transport is missing."; return; }
            transportManager.Transport = selected;
            networkRoot.SetActive(true);
            if (previewCamera != null) previewCamera.enabled = false;
            if (!networkManager.Initialized) { status = "Network manager did not initialize."; return; }
            if (host)
            {
                if (!networkManager.ServerManager.StartConnection()) { status = "Server failed to start."; return; }
                sessionStarting = true;
                status = "Starting host...";
                StartCoroutine(StartHostClientWhenServerIsReady());
            }
            else
            {
                if (string.IsNullOrWhiteSpace(address)) { status = "Enter the host address."; return; }
                bool started = networkManager.ClientManager.StartConnection(address.Trim());
                status = started ? "Connecting to " + address.Trim() : "Client failed to start.";
            }
        }

        private IEnumerator StartHostClientWhenServerIsReady()
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!networkManager.ServerManager.Started && Time.realtimeSinceStartup < deadline)
                yield return null;

            sessionStarting = false;
            if (!networkManager.ServerManager.Started)
            {
                status = "Server did not become ready.";
                yield break;
            }

            bool clientStarted = networkManager.ClientManager.StartConnection(useSteam ? SteamUser.GetSteamID().m_SteamID.ToString() : "127.0.0.1");
            status = clientStarted
                ? useSteam ? "Hosting over Steam. Give your Steam ID to your friend." : "Hosting locally/LAN on UDP port 7770."
                : "Host client failed to start.";
        }

        private bool InitializeSteam()
        {
            if (steamInitialized) return true;
            try
            {
                steamInitialized = SteamAPI.Init();
                if (!steamInitialized) status = "Steam initialization failed. Ensure Steam is running and steam_appid.txt exists.";
            }
            catch (Exception exception)
            {
                status = "Steam initialization failed: " + exception.Message;
                steamInitialized = false;
            }
            return steamInitialized;
        }

        private void StopSession()
        {
            if (networkManager != null && networkManager.ClientManager.Started) networkManager.ClientManager.StopConnection();
            if (networkManager != null && networkManager.ServerManager.Started) networkManager.ServerManager.StopConnection(true);
            status = "Session stopped. Restart the scene to choose another transport.";
        }

        private static bool HasArgument(string[] arguments, string expected)
        {
            return Array.Exists(arguments, argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetArgumentValue(string[] arguments, string expected, out string value)
        {
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (!string.Equals(arguments[i], expected, StringComparison.OrdinalIgnoreCase)) continue;
                value = arguments[i + 1];
                return true;
            }
            value = null;
            return false;
        }
    }
}
