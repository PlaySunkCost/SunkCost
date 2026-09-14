using System;
using FishNet.Transporting;
using SunkCost.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    // Targeted asset upgrade for the lobby work (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 11, step 5): raise the two serialized transport caps from the
    // two-player prototype to the four-player room. Changes only those two fields,
    // through SerializedObject so Undo and dirty tracking work, and is idempotent.
    // Never regenerates the room, player or ball (that would discard the Scavenger
    // visual and hand-tuned scene).
    public static class HQPrototypeLobbySetup
    {
        private static readonly LobbySessionSettings Defaults = new();

        [MenuItem("Sunk Cost/Prototype/Apply lobby caps")]
        public static void ApplyFromMenu()
        {
            Debug.Log(Apply());
        }

        // Returns a summary of what changed. Throws on unexpected state.
        public static string Apply()
        {
            string steam = ApplySteamPrefabCap();
            string local = ApplySceneTugboatCap();
            return steam + "; " + local;
        }

        private static string ApplySteamPrefabCap()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.SteamTransportPrefabPath);
            if (prefab == null) throw new InvalidOperationException("Steam transport prefab missing at " + HQPrototypeBuilder.SteamTransportPrefabPath);
            Transport transport = prefab.GetComponent<Transport>();
            if (transport == null || transport.GetType().FullName != "FishySteamworks.FishySteamworks")
                throw new InvalidOperationException("Steam transport prefab does not carry FishySteamworks.");

            int target = Defaults.SteamRemoteClientCap;
            using var serialized = new SerializedObject(transport);
            SerializedProperty cap = serialized.FindProperty("_maximumClients");
            if (cap == null) throw new InvalidOperationException("FishySteamworks has no _maximumClients field.");
            if (cap.intValue == target) return "Steam prefab remote cap already " + target;
            int previous = cap.intValue;
            cap.intValue = target;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            return "Steam prefab remote cap " + previous + " -> " + target;
        }

        private static string ApplySceneTugboatCap()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SunkCost.World.WorldScenes.SessionPath)
                throw new InvalidOperationException("Open " + SunkCost.World.WorldScenes.SessionPath + " before applying lobby caps (active: " + scene.path + ").");
            Transport tugboat = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transport transport in root.GetComponentsInChildren<Transport>(true))
                {
                    if (transport.GetType().FullName == "FishNet.Transporting.Tugboat.Tugboat") { tugboat = transport; break; }
                }
                if (tugboat != null) break;
            }
            if (tugboat == null) throw new InvalidOperationException("No Tugboat transport in the Session scene.");

            int target = Defaults.LocalSocketCap;
            using var serialized = new SerializedObject(tugboat);
            SerializedProperty cap = serialized.FindProperty("_maximumClients");
            if (cap == null) throw new InvalidOperationException("Tugboat has no _maximumClients field.");
            if (cap.intValue == target) return "Scene Tugboat cap already " + target;
            int previous = cap.intValue;
            Undo.RecordObject(tugboat, "Apply lobby caps");
            cap.intValue = target;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + scene.path);
            return "Scene Tugboat cap " + previous + " -> " + target;
        }
    }
}
