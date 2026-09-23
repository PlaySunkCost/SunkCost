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
            if (ship != null) CheckAreas(ship, errors);
            WorldLoopSettings settings = AssetDatabase.LoadAssetAtPath<WorldLoopSettings>(ShipStubBuilder.SettingsPath);
            if (settings == null) errors.Add("WorldLoopSettings asset missing (run Create or Update Session).");
            else if (ship != null && Vector3.Distance(ship.transform.position, settings.ShipAtSeaOrigin) > 0.01f)
                errors.Add("The sea ship must sit at WorldLoopSettings.shipAtSeaOrigin " + settings.ShipAtSeaOrigin + " (found " + ship.transform.position + ").");
            if (!HQPrototypeValidator.HasRoot(scene, "Sea")) errors.Add("Sea plane is missing.");
            WorldSceneChecks.CheckBuildList(errors);
            if (errors.Count > 0) throw new InvalidOperationException("ShipAtSea validation failed:\n- " + string.Join("\n- ", errors));
            Debug.Log("ShipAtSea validation passed: one ship with every named part at the sea origin, no session machinery.");
        }

        // The parts grouped by area (SHIP-076, Editor ShipHierarchy): every name the
        // game finds is on one part only, and the buttons and screens share a group
        // with the model they sit on, so moving the group moves them together.
        private static void CheckAreas(ShipParts ship, List<string> errors)
        {
            var unique = new List<string>(ShipParts.RequiredChildren)
            {
                ShipParts.WellGroupName, ShipParts.TowerGroupName, ShipParts.ConsoleGroupName, ShipParts.TvGroupName,
                ShipParts.StorageGroupName, ShipParts.CabinGroupName, ShipParts.VolumesGroupName, ShipParts.PointsGroupName,
                SunkCost.Editor.Look.ShipHierarchy.TowerModelName, SunkCost.Editor.Look.ShipHierarchy.ConsoleModelName, "TvCabinet", "StorageRoom",
                ShipMonitor.DisplayName, TvDisplay.IdleName, StorageReadout.InsideName
            };
            foreach (string name in unique)
            {
                int count = ship.CountNamed(name);
                if (count != 1) errors.Add("Ship: " + count + " parts named '" + name + "' (one expected).");
            }
            Beside(ship, ShipParts.ConsoleGroupName, errors, SunkCost.Editor.Look.ShipHierarchy.ConsoleModelName, ShipParts.MonitorName, ShipParts.MonitorButtonSite01Name,
                ShipParts.MonitorButtonHQName, ShipParts.MonitorButtonEndDayName, ShipParts.MonitorStatusName, ShipMonitor.DisplayName);
            Beside(ship, ShipParts.TvGroupName, errors, "TvCabinet", ShipParts.TvScreenName, "Tv Caption Sign", ShipParts.TvSpeakerName, TvDisplay.IdleName);
            Beside(ship, ShipParts.StorageGroupName, errors, "StorageRoom", ShipParts.StorageVolumeName, StorageReadout.InsideName);
        }

        private static void Beside(ShipParts ship, string group, List<string> errors, params string[] parts)
        {
            Transform area = ship.Find(group);
            if (area == null) { errors.Add("Ship: no '" + group + "' group."); return; }
            foreach (string name in parts)
            {
                Transform part = ship.Find(name);
                if (part != null && part.parent != area) errors.Add("Ship: '" + name + "' is not in the '" + group + "' group (under '" + (part.parent != null ? part.parent.name : "-") + "').");
            }
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
