using System;
using System.Collections.Generic;
using SunkCost.Sites;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    // Targeted, repeatable scene patch for the cabin ride (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 6.3, the asks of DiveSite01): the underwater grade becomes a local
    // box so the host on the deck is not graded underwater while the site is
    // loaded on its machine. Nothing else in Idan's scene is touched; the
    // builder produces the same shape for a regenerated site. Run twice: no
    // changes.
    public static class DeckCabinRideSetup
    {
        [MenuItem("Sunk Cost/Prototype/Apply deck cabin ride setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the deck cabin ride setup.");
            var changes = new List<string>();
            Scene active = SceneManager.GetActiveScene();
            string activePath = active.path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
            Scene dive = EditorSceneManager.OpenScene(SunkCost.World.WorldScenes.DivePath, OpenSceneMode.Single);
            DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
            if (settings == null) throw new InvalidOperationException("DiveSiteSettings asset missing at " + DiveSiteBuilder.SettingsPath + ".");
            bool dirty = false;
            foreach (GameObject root in dive.GetRootGameObjects())
            {
                // The guide cable runs through the middle of the car; a rider standing on it
                // would be left behind by the descending floor (see DiveSiteBuilder).
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Guide Cable") continue;
                    Collider cableCollider = t.GetComponent<Collider>();
                    if (cableCollider == null) continue;
                    UnityEngine.Object.DestroyImmediate(cableCollider);
                    changes.Add("guide cable collider removed");
                    dirty = true;
                }
                foreach (Volume volume in root.GetComponentsInChildren<Volume>(true))
                {
                    if (!volume.isGlobal && volume.GetComponent<BoxCollider>() != null) continue;
                    DiveSiteBuilder.ShapeUnderwaterVolume(volume.gameObject, settings);
                    changes.Add("'" + volume.name + "' made a local box");
                    dirty = true;
                }
            }
            if (dirty) EditorSceneManager.SaveScene(dive);
            if (!string.IsNullOrEmpty(activePath) && activePath != SunkCost.World.WorldScenes.DivePath) EditorSceneManager.OpenScene(activePath, OpenSceneMode.Single);
            changes.AddRange(PatchShipPrefabGlass());
            changes.AddRange(PatchCarLight());
            return changes.Count == 0 ? "Deck cabin ride already set up" : "Deck cabin ride: " + string.Join("; ", changes);
        }

        // A lamp inside the seafloor car so it reads as the same bright glass cabin
        // at the bottom of a dark shaft (Dan, 15 September 2026: "make the elevator
        // look the same on the bottom"). Added to the prefab in place if missing.
        public const string CarLightName = "Cabin Light";
        private static IEnumerable<string> PatchCarLight()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(SunkCost.Sites.ElevatorCabinBuilder.PrefabPath);
            try
            {
                if (root.transform.Find(CarLightName) == null)
                {
                    DiveSiteSettings settings = AssetDatabase.LoadAssetAtPath<DiveSiteSettings>(DiveSiteBuilder.SettingsPath);
                    float height = settings != null ? settings.CarInteriorHeightMeters : 3.5f;
                    GameObject lamp = new(CarLightName, typeof(Light));
                    lamp.transform.SetParent(root.transform, false);
                    lamp.transform.localPosition = new Vector3(0f, height - 0.4f, 0f);
                    Light light = lamp.GetComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 7f;
                    light.intensity = 6f;
                    light.color = new Color(1f, 0.95f, 0.85f);
                    light.shadows = LightShadows.None;
                    PrefabUtility.SaveAsPrefabAsset(root, SunkCost.Sites.ElevatorCabinBuilder.PrefabPath);
                    changes.Add("cabin light added to the car");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }

        // The deck cabin is the same glass car as the seafloor's (Dan, 15 September
        // 2026: "they should look the same"): its shell and roof take the car's
        // transparent glass instead of the opaque stub material. In place, GUIDs kept.
        private static IEnumerable<string> PatchShipPrefabGlass()
        {
            var changes = new List<string>();
            Material glass = DiveSiteBuilder.GetOrCreateGlassMaterial();
            GameObject root = PrefabUtility.LoadPrefabContents(ShipStubBuilder.PrefabPath);
            try
            {
                SunkCost.World.ShipParts ship = root.GetComponent<SunkCost.World.ShipParts>();
                Transform cabin = ship != null ? ship.DeckCabin : null;
                bool dirty = false;
                if (cabin != null)
                {
                    foreach (Renderer renderer in cabin.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.sharedMaterial == null || renderer.sharedMaterial.name != "ShipCabin") continue;
                        renderer.sharedMaterial = glass;
                        dirty = true;
                    }
                    // The car's own glass inside the housing's (Dan, 15 September 2026):
                    // with the car up you look through two panes, with it below through
                    // one. A shrunken copy of the housing's shell, shown while the car is up.
                    if (cabin.Find(SunkCost.World.ShipParts.DeckCabinCarGlassName) == null)
                    {
                        Transform shell = cabin.Find("Glass Shell");
                        if (shell != null)
                        {
                            GameObject carGlass = UnityEngine.Object.Instantiate(shell.gameObject, cabin);
                            carGlass.name = SunkCost.World.ShipParts.DeckCabinCarGlassName;
                            carGlass.transform.localPosition = shell.localPosition;
                            carGlass.transform.localRotation = shell.localRotation;
                            carGlass.transform.localScale = new Vector3(0.93f, 1f, 0.93f);
                            foreach (Collider c in carGlass.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                            changes.Add("car glass added to the deck cabin");
                            dirty = true;
                        }
                    }
                }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, ShipStubBuilder.PrefabPath);
                if (dirty && !changes.Contains("deck cabin glass")) changes.Add("deck cabin glass");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }
    }
}
