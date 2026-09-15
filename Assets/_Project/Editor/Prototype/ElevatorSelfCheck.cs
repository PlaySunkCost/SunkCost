using UnityEditor;
using UnityEngine;

namespace SunkCost.Sites
{
    // Editor-only development harness — NOT a Unity Test Framework test, no assembly
    // definitions, nothing that ships. Enters Play Mode, spawns ElevatorSelfCheckRunner to
    // drive the actual check via a coroutine, and lets it exit Play Mode itself once done.
    // This is a static-geometry smoke test only: DiveSite01's player is a networked
    // HQPlayerController spawned by CrewSpawner (not baked into the scene), and this
    // harness runs with no Session.unity network root and no live connection, so there is
    // no live rider to board or ride the car with. Says nothing about multiplayer/netcode.
    // See ElevatorSelfCheckRunner's header comment for what it no longer proves.
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
