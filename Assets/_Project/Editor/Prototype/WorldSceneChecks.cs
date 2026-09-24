using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using SunkCost.Net;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    // Checks shared by the world-scene validators (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 9.1): a world scene carries no session machinery, its ship has every
    // named part, and the build list has the four scenes in order.
    public static class WorldSceneChecks
    {
        public static readonly string[] BuildListPaths = { WorldScenes.SessionPath, WorldScenes.HQPath, WorldScenes.SeaPath, WorldScenes.DivePath };

        public static void CheckNoSessionMachinery(Scene scene, List<string> errors, string label)
        {
            HQPrototypeValidator.CheckCount<NetworkManager>(scene, 0, errors);
            HQPrototypeValidator.CheckCount<PrototypeSessionUI>(scene, 0, errors);
            HQPrototypeValidator.CheckCount<ScreenFade>(scene, 0, errors);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.GetComponent<AudioListener>() != null) errors.Add(label + ": camera '" + camera.name + "' carries an AudioListener; only the player and the Session preview camera may.");
        }

        public static ShipParts CheckShip(Scene scene, List<string> errors, string label)
        {
            ShipParts ship = ShipParts.InScene(scene);
            if (ship == null) { errors.Add(label + ": no ship (ShipParts) in the scene."); return null; }
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<ShipParts>(true).Length;
            if (count != 1) errors.Add(label + ": expected one ship, found " + count + ".");
            foreach (string missing in ship.MissingChildren()) errors.Add(label + ": ship is missing part '" + missing + "'.");
            if (ship.AboardVolume == null || !ship.AboardVolume.isTrigger) errors.Add(label + ": AboardVolume must be a trigger collider.");
            if (ship.DeckCabinVolume == null || !ship.DeckCabinVolume.isTrigger) errors.Add(label + ": DeckCabinVolume must be a trigger collider.");
            for (int i = 0; i < ShipParts.SpawnPointCount; i++)
            {
                Transform point = ship.SpawnPoint(i);
                if (point != null && !ship.IsAboard(point.position + Vector3.up * 0.5f)) errors.Add(label + ": " + point.name + " is outside the AboardVolume.");
            }
            if (ship.BoardingPoint != null && !ship.IsAboard(ship.BoardingPoint.position + Vector3.up * 0.5f)) errors.Add(label + ": BoardingPoint is outside the AboardVolume.");
            // A player must fit where Unstuck and the spawns put them: the boarding point
            // stood inside the container once the deck was dressed (ship audit SHIP-001).
            Physics.SyncTransforms();
            CheckClearance(ship.BoardingPoint, errors, label);
            for (int i = 0; i < ShipParts.SpawnPointCount; i++) CheckClearance(ship.SpawnPoint(i), errors, label);
            foreach (NetworkObject nob in ship.GetComponentsInChildren<NetworkObject>(true))
                errors.Add(label + ": the ship prefab must not contain NetworkObjects yet ('" + nob.name + "'); the cabin and monitor become scene objects in their own cards.");
            return ship;
        }

        // A standing player's body, a little slimmer than the real capsule (radius 0.3 m,
        // 1.8 m) and clear of the deck under the feet: from 0.27 m to 1.78 m up.
        public const float ClearanceRadius = 0.28f, ClearanceLow = 0.55f, ClearanceHigh = 1.5f;

        public static void CheckClearance(Transform point, List<string> errors, string label)
        {
            if (point == null) return;
            Vector3 up = point.up;
            Collider[] hits = Physics.OverlapCapsule(point.position + up * ClearanceLow, point.position + up * ClearanceHigh, ClearanceRadius, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return;
            var names = new List<string>();
            foreach (Collider hit in hits) names.Add(hit.transform.parent != null ? hit.transform.parent.name + "/" + hit.name : hit.name);
            errors.Add(label + ": a player does not fit at " + point.name + " (it is inside " + string.Join(", ", names) + ").");
        }

        public static void CheckBuildList(List<string> errors)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int expectedIndex = 0;
            for (int i = 0; i < BuildListPaths.Length; i++)
            {
                if (!System.IO.File.Exists(BuildListPaths[i])) continue;
                if (expectedIndex >= scenes.Length || scenes[expectedIndex].path != BuildListPaths[i] || !scenes[expectedIndex].enabled)
                {
                    errors.Add("Build list must be Session, HQPrototype, ShipAtSea, DiveSite01 in that order (run Create or Update Session).");
                    return;
                }
                expectedIndex++;
            }
            if (scenes.Length != expectedIndex) errors.Add("Build list has extra scenes (run Create or Update Session).");
        }
    }
}
