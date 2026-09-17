using SunkCost.Net;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Stops the UDP port leak (18 September 2026: "I can't play — port taken"):
    // when Play Mode exits while the editor still hosts or sits in a room, the
    // transport's socket is not closed before the domain reload and the editor
    // process keeps 7770 until it restarts. This leaves the session first —
    // the same Leave the menu button and StopCleanly use — so the socket is
    // released every time, including a matrix that failed and a Stop pressed on it.
    [InitializeOnLoad]
    public static class PlayModeSessionGuard
    {
        static PlayModeSessionGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;
            PrototypeSessionController controller = Object.FindAnyObjectByType<PrototypeSessionController>();
            if (controller == null || !controller.InRoom) return;
            controller.Leave("Play Mode stopped.");
            Debug.Log("[PlayModeSessionGuard] left the session before Play Mode exited, so the editor releases its UDP port.");
        }
    }
}
