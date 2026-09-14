using System;
using System.Collections.Generic;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    public static class ShipAtSeaValidator
    {
        [MenuItem("Sunk Cost/Prototype/Validate ShipAtSea")]
        public static void ValidateOrThrow()
        {
            var errors = new List<string>();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != WorldScenes.SeaPath) errors.Add("Open scene is not " + WorldScenes.SeaPath);
            WorldSceneChecks.CheckNoSessionMachinery(scene, errors, "ShipAtSea");
            ShipParts ship = WorldSceneChecks.CheckShip(scene, errors, "ShipAtSea");
            WorldLoopSettings settings = AssetDatabase.LoadAssetAtPath<WorldLoopSettings>(ShipStubBuilder.SettingsPath);
            if (settings == null) errors.Add("WorldLoopSettings asset missing (run Create or Update Session).");
            else if (ship != null && Vector3.Distance(ship.transform.position, settings.ShipAtSeaOrigin) > 0.01f)
                errors.Add("The sea ship must sit at WorldLoopSettings.shipAtSeaOrigin " + settings.ShipAtSeaOrigin + " (found " + ship.transform.position + ").");
            if (!HQPrototypeValidator.HasRoot(scene, "Sea")) errors.Add("Sea plane is missing.");
            WorldSceneChecks.CheckBuildList(errors);
            if (errors.Count > 0) throw new InvalidOperationException("ShipAtSea validation failed:\n- " + string.Join("\n- ", errors));
            Debug.Log("ShipAtSea validation passed: one ship with every named part at the sea origin, no session machinery.");
        }
    }
}
