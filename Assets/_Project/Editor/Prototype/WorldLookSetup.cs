using System;
using System.Collections.Generic;
using SunkCost.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    // Writes each world scene's RenderSettings into a WorldLook component on a
    // root object of that scene, so a camera showing another world can render
    // with its look (the deck TV, a dead diver's eyes on the deck). The builders
    // call WriteIntoOpenScene after they set RenderSettings; the menu item does
    // it for the three scenes as they are.
    public static class WorldLookSetup
    {
        public const string ObjectName = "World Look";

        [MenuItem("Sunk Cost/Prototype/Apply world look setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the world look setup.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
            string activePath = SceneManager.GetActiveScene().path;
            var changes = new List<string>();
            foreach (WorldId world in new[] { WorldId.HQ, WorldId.Sea, WorldId.Dive })
            {
                Scene scene = EditorSceneManager.OpenScene(WorldScenes.Path(world), OpenSceneMode.Single);
                if (WriteIntoOpenScene(scene)) { EditorSceneManager.SaveScene(scene); changes.Add(WorldScenes.Name(world)); }
            }
            if (!string.IsNullOrEmpty(activePath)) EditorSceneManager.OpenScene(activePath, OpenSceneMode.Single);
            return changes.Count == 0 ? "World look already written in every world scene" : "World look written: " + string.Join(", ", changes);
        }

        // The open (active) scene's RenderSettings into its WorldLook. True when
        // something changed.
        public static bool WriteIntoOpenScene(Scene scene)
        {
            WorldLook look = WorldLook.InScene(scene);
            if (look == null)
            {
                GameObject go = new(ObjectName);
                SceneManager.MoveGameObjectToScene(go, scene);
                look = go.AddComponent<WorldLook>();
            }
            WorldLook.Snapshot current = WorldLook.Snapshot.Capture();
            if (SameLook(look.Look, current)) return false;
            look.Set(current);
            EditorUtility.SetDirty(look);
            return true;
        }

        private static bool SameLook(WorldLook.Snapshot a, WorldLook.Snapshot b) =>
            a.AmbientMode == b.AmbientMode && a.AmbientLight == b.AmbientLight && a.Fog == b.Fog && a.FogMode == b.FogMode &&
            a.FogColor == b.FogColor && Mathf.Approximately(a.FogDensity, b.FogDensity) &&
            Mathf.Approximately(a.FogStartDistance, b.FogStartDistance) && Mathf.Approximately(a.FogEndDistance, b.FogEndDistance);
    }
}
