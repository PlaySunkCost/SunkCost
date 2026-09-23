using System;
using System.Collections.Generic;
using SunkCost.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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
        // something changed. The scene's sun is named here (SHIP-003, 23 September
        // 2026: neither the ship nor the site named one, so with both loaded Unity
        // took the site's Surface Light for both), and the ambient probe is written
        // with the colours: the lit shaders read the probe (SHIP-037). `deepBelowY`:
        // the height under which moving things are out of the surface light's reach
        // (the site; WorldLightLayers).
        public static bool WriteIntoOpenScene(Scene scene, float deepBelowY = float.NegativeInfinity)
        {
            EnsureRenderingLayerNames();
            WorldLook look = WorldLook.InScene(scene);
            if (look == null)
            {
                GameObject go = new(ObjectName);
                SceneManager.MoveGameObjectToScene(go, scene);
                look = go.AddComponent<WorldLook>();
            }
            Light sun = RenderSettings.sun;
            if (sun == null || sun.gameObject.scene != scene) sun = WorldLook.BrightestDirectional(scene);
            RenderSettings.sun = sun;
            if (RenderSettings.ambientMode != AmbientMode.Flat) DynamicGI.UpdateEnvironment();
            WorldLook.Snapshot current = WorldLook.Snapshot.Capture();
            if (current.AmbientMode == AmbientMode.Flat) current.AmbientProbe = WorldLook.ToArray(WorldLook.FlatProbe(current.AmbientLight));
            current.Sun = sun;
            bool sameDeep = look.DeepBelowY.Equals(deepBelowY);
            if (SameLook(look.Look, current) && sameDeep) return false;
            look.Set(current);
            look.SetDeepBelowY(deepBelowY);
            EditorUtility.SetDirty(look);
            return true;
        }

        // Names the rendering layers WorldLightLayers stamps, where they still carry
        // Unity's default names, so the light and renderer inspectors read them.
        public static void EnsureRenderingLayerNames()
        {
            var names = new (int Bit, string Name)[]
            {
                (WorldLightLayers.SeaBit, "World Sea"), (WorldLightLayers.DiveBit, "World Dive"),
                (WorldLightLayers.DiveDeepBit, "World Dive Deep"), (WorldLightLayers.HQBit, "World HQ")
            };
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            SerializedObject tags = new(assets[0]);
            SerializedProperty layers = tags.FindProperty("m_RenderingLayers");
            if (layers == null) return;
            bool changed = false;
            foreach ((int bit, string name) in names)
            {
                while (layers.arraySize <= bit) { layers.InsertArrayElementAtIndex(layers.arraySize); layers.GetArrayElementAtIndex(layers.arraySize - 1).stringValue = "Light Layer " + (layers.arraySize - 1); changed = true; }
                SerializedProperty slot = layers.GetArrayElementAtIndex(bit);
                if (slot.stringValue == name) continue;
                if (!string.IsNullOrEmpty(slot.stringValue) && slot.stringValue != "Light Layer " + bit)
                    throw new InvalidOperationException($"Rendering layer {bit} is already named '{slot.stringValue}'; WorldLightLayers needs it for '{name}'.");
                slot.stringValue = name;
                changed = true;
            }
            if (changed) tags.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool SameLook(WorldLook.Snapshot a, WorldLook.Snapshot b) =>
            a.AmbientMode == b.AmbientMode && a.AmbientLight == b.AmbientLight && a.Fog == b.Fog && a.FogMode == b.FogMode &&
            a.FogColor == b.FogColor && Mathf.Approximately(a.FogDensity, b.FogDensity) &&
            Mathf.Approximately(a.FogStartDistance, b.FogStartDistance) && Mathf.Approximately(a.FogEndDistance, b.FogEndDistance) &&
            a.Environment == b.Environment && a.AmbientEquator == b.AmbientEquator && a.AmbientGround == b.AmbientGround &&
            Mathf.Approximately(a.AmbientIntensity, b.AmbientIntensity) && SameProbe(a.AmbientProbe, b.AmbientProbe) &&
            a.Skybox == b.Skybox && a.ReflectionMode == b.ReflectionMode && a.CustomReflection == b.CustomReflection &&
            Mathf.Approximately(a.ReflectionIntensity, b.ReflectionIntensity) && a.Sun == b.Sun;

        private static bool SameProbe(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return a == b;
            for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-5f) return false;
            return true;
        }
    }
}
