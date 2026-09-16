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
            CheckDeckCabinDoorCollider();
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

        // The deck cabin's doorway collider (run Apply deck cabin ride setup for an older ship).
        private static void CheckDeckCabinDoorCollider()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ShipStubBuilder.PrefabPath);
            SunkCost.World.ShipParts ship = prefab != null ? prefab.GetComponent<SunkCost.World.ShipParts>() : null;
            if (ship == null) return; // the ship's own checks report a missing prefab
            if (ship.DeckCabinDoorCollider == null)
                throw new System.InvalidOperationException("Ship prefab: the deck cabin has no " + SunkCost.World.ShipParts.DeckCabinDoorColliderName + " (run Apply deck cabin ride setup).");
            if (ship.DeckCabinDoorCollider.isTrigger)
                throw new System.InvalidOperationException("Ship prefab: " + SunkCost.World.ShipParts.DeckCabinDoorColliderName + " must be solid, not a trigger.");
        }
    }
}
