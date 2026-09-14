using UnityEditor;
using UnityEngine;

namespace SunkCost.Sites
{
    // Editor-only development harness — NOT a Unity Test Framework test, no assembly
    // definitions, nothing that ships. Enters Play Mode, spawns ElevatorSelfCheckRunner to
    // drive the actual frame-by-frame check via a coroutine, and lets it exit Play Mode
    // itself once done. This is a smoke test: it does not replace a human walking in and
    // riding the elevator, and it says nothing about multiplayer/netcode — DiveSite01 has
    // no networking.
    public static class ElevatorSelfCheck
    {
        [MenuItem("Sunk Cost/Prototype/Run Elevator Self-Check")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[ElevatorSelfCheck] Exit Play Mode before running the self-check.");
                return;
            }

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode)
                return;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            new GameObject("ElevatorSelfCheckRunner").AddComponent<ElevatorSelfCheckRunner>();
        }
    }
}
