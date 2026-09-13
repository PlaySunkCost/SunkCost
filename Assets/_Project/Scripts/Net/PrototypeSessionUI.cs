using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Net
{
    // Immediate-mode session panel. Renders PrototypeSessionController's snapshot
    // and forwards clicks to it; owns no session state of its own. Serialized
    // references are wired by HQPrototypeBuilder and must keep their names.
    public sealed class PrototypeSessionUI : MonoBehaviour
    {
        [SerializeField] private GameObject networkRoot;
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private Transport localTransport;
        [SerializeField] private GameObject steamTransportPrefab;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private LobbySessionSettings settings = new();

        private PrototypeSessionController controller;
        private string lobbyIdField = string.Empty;
        private int lastReportedPlayerCount = -1;
        private Vector2 rosterScroll;

        public PrototypeSessionController Controller => controller;
        public bool InRoom => controller != null && controller.InRoom;

        // Host-ready meaning retained for existing tools: both managers started here.
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

        private void Awake()
        {
            controller = GetComponent<PrototypeSessionController>() ?? gameObject.AddComponent<PrototypeSessionController>();
            controller.Configure(networkRoot, networkManager, transportManager, localTransport, steamTransportPrefab, previewCamera, settings);
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
            else if (TryGetArgumentValue(arguments, "-hq-auto-join-steam-lobby", out string lobbyId))
                JoinSteamLobby(lobbyId);
            else if (HasArgument(arguments, "-hq-auto-join-steam"))
                Debug.LogWarning("-hq-auto-join-steam <hostSteamId> is no longer supported; use -hq-auto-join-steam-lobby <lobbyId>.");
        }

        private void Update()
        {
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

        // ---- public entry points (used by automation and the test hooks) -----------

        public void StartLocalHost()
        {
            if (!controller.SelectMode(SessionMode.Local, out string error)) { Debug.LogWarning(error); return; }
            controller.StartHost();
        }

        public void JoinLocal(string hostAddress)
        {
            address = string.IsNullOrWhiteSpace(hostAddress) ? "127.0.0.1" : hostAddress;
            controller.JoinLocal(address);
        }

        public void StartSteamHost()
        {
            if (!controller.SelectMode(SessionMode.Steam, out string error)) { Debug.LogWarning(error); return; }
            controller.StartHost();
        }

        public bool JoinSteamLobby(string lobbyId)
        {
            lobbyIdField = lobbyId ?? string.Empty;
            return controller.JoinSteamLobby(lobbyIdField);
        }

        public void LeaveSession() => controller.Leave();
        public bool CopyLobbyId() => controller.CopyLobbyId(out _);
        public bool InviteFriends() => controller.InviteFriends(out _);

        // ---- drawing --------------------------------------------------------------

        private void OnGUI()
        {
            if (controller == null) return;
            if (controller.InRoom && !SessionInputGate.MenuOpen)
            {
                GUILayout.BeginArea(new Rect(18, 18, 300, 26), GUI.skin.box);
                GUILayout.Label("Esc: menu   F3: network debug");
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginArea(new Rect(18, 18, 430, Screen.height - 36), GUI.skin.box);
            GUILayout.Label("SUNK COST — HQ BASKETBALL");
            GUILayout.Label(controller.Message);

            if (controller.InRoom || controller.Busy)
                DrawSession();
            else
                DrawMenu();
            GUILayout.EndArea();
        }

        private void DrawMenu()
        {
            SessionMode? bound = controller.BoundMode;
            if (bound.HasValue)
            {
                GUILayout.Label((bound.Value == SessionMode.Steam ? "Steam P2P" : "Local / LAN") + " (locked for this run; restart to change)");
            }
            else
            {
                GUILayout.BeginHorizontal();
                bool local = controller.SelectedMode == SessionMode.Local;
                if (GUILayout.Toggle(local, "Local / LAN", GUI.skin.button) && !local) controller.SelectMode(SessionMode.Local, out _);
                if (GUILayout.Toggle(!local, "Steam P2P", GUI.skin.button) && local) controller.SelectMode(SessionMode.Steam, out _);
                GUILayout.EndHorizontal();
            }

            bool steamMode = controller.SelectedMode == SessionMode.Steam;
            if (steamMode)
            {
                GUILayout.Label("Steam lobby ID (Join only). Invites arrive automatically while this screen is open.");
                lobbyIdField = GUILayout.TextField(lobbyIdField);
            }
            else
            {
                GUILayout.Label("Host IP (127.0.0.1 on same PC)");
                address = GUILayout.TextField(address);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host")) { if (steamMode) StartSteamHost(); else StartLocalHost(); }
            if (GUILayout.Button("Join")) { if (steamMode) JoinSteamLobby(lobbyIdField); else JoinLocal(address); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Controls: WASD, Shift, mouse, E pick/drop, left click throw, Esc menu, F3 net debug");
        }

        private void DrawSession()
        {
            SessionSnapshot s = controller.Snapshot();
            GUILayout.Label($"{s.Role} · {(s.BoundMode.HasValue ? s.BoundMode.Value.ToString() : s.SelectedMode.ToString())} · {s.State}");
            GUILayout.Label($"Server: {s.ServerStarted}   Client: {s.ClientStarted}");

            if (s.BoundMode == SessionMode.Steam && s.LobbyId != 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Lobby ID: " + controller.LobbyIdText);
                if (GUILayout.Button("Copy", GUILayout.Width(60))) controller.CopyLobbyId(out _);
                GUILayout.EndHorizontal();
                if (controller.InRoom)
                {
                    GUI.enabled = controller.SteamOverlayAvailable;
                    if (GUILayout.Button(controller.SteamOverlayAvailable ? "Invite friends (Steam overlay)" : "Invite unavailable — share the lobby ID"))
                        controller.InviteFriends(out _);
                    GUI.enabled = true;
                }
                GUILayout.Label($"Lobby members {s.LobbyMembers}/{s.TotalPlayers}   Players in HQ {s.PlayersInHq}/{s.TotalPlayers}");
            }
            else
            {
                GUILayout.Label($"Players in HQ {s.PlayersInHq}/{s.TotalPlayers}");
            }

            if (controller.InRoom) controller.RefreshMembers();
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(110));
            foreach (PrototypeSessionController.MemberInfo member in controller.Members)
            {
                string line = member.Name;
                if (member.IsHost) line += "  (host)";
                if (member.IsSelf) line += "  (you)";
                GUILayout.Label(line);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (controller.InRoom)
            {
                if (GUILayout.Button("Resume")) SessionInputGate.Resume();
                if (GUILayout.Button(s.Role == SessionRole.Host ? "Leave (closes the room)" : "Leave")) controller.Leave();
            }
            else if (GUILayout.Button("Cancel"))
            {
                controller.Leave("Cancelled.");
            }
            GUILayout.EndHorizontal();
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
