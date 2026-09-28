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
            if (ship != null) CheckDeckCabinLook(ship, errors);
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
                SunkCost.Editor.Look.ShipHierarchy.TowerModelName, "TvCabinet", "StorageRoom",
                ConsoleRig.BodyName, ConsoleRig.TopScreenName, ConsoleRig.BottomScreenName, ConsoleRig.SignName, ConsoleRig.LeverHingeName, ConsoleRig.LeverHandleName,
                ConsoleRig.CardNamePrefix + SiteId.HQ, ConsoleRig.CardNamePrefix + SiteId.Site01, ConsoleRig.CardNamePrefix + SiteId.Site02,
                ConsoleRig.CardNamePrefix + SiteId.Site03, ConsoleRig.CardNamePrefix + SiteId.Site04,
                TvDisplay.IdleName, StorageReadout.InsideName
            };
            foreach (string name in unique)
            {
                int count = ship.CountNamed(name);
                if (count != 1) errors.Add("Ship: " + count + " parts named '" + name + "' (one expected).");
            }
            Beside(ship, ShipParts.ConsoleGroupName, errors, ConsoleRig.ShipRootName);
            CheckConsole(ship, errors);
            Beside(ship, ShipParts.TvGroupName, errors, "TvCabinet", ShipParts.TvScreenName, "Tv Caption Sign", ShipParts.TvSpeakerName, TvDisplay.IdleName);
            Beside(ship, ShipParts.StorageGroupName, errors, "StorageRoom", ShipParts.StorageVolumeName, StorageReadout.InsideName);
        }

        // The navigation console (27 September 2026): one rig of the ship kind with its
        // composer, standing on the deck with its back on the bow's port diagonal (its
        // front into the ship, where the crew read it), its lever's control the aim's target with a collider
        // and a ConsoleControl, its five cards in the reading order with theirs, and the
        // model's own mesh collider so the crew walk against it and the aim reaches the
        // controls through nothing.
        private static void CheckConsole(ShipParts ship, List<string> errors)
        {
            Transform root = ship.NavConsole;
            if (root == null) { errors.Add("Ship: no '" + ShipParts.NavConsoleName + "'."); return; }
            ConsoleRig rig = root.GetComponent<ConsoleRig>();
            if (rig == null) { errors.Add("Ship: '" + ShipParts.NavConsoleName + "' has no ConsoleRig."); return; }
            if (rig.Kind != ConsoleKind.Ship) errors.Add("Ship: the navigation console's rig is of kind " + rig.Kind + ".");
            if (root.GetComponent<ShipNavigationConsole>() == null) errors.Add("Ship: the navigation console has no ShipNavigationConsole composer.");
            if (root.GetComponent<ConsoleLever>() == null) errors.Add("Ship: the navigation console has no ConsoleLever.");
            Vector3 local = ship.ToShipLocal(root.position);
            if (Mathf.Abs(local.y) > 0.01f) errors.Add("Ship: the navigation console stands " + local.y.ToString("F3") + " m off the deck.");
            // Its back on the bow's port diagonal, facing back into the ship: toward
            // starboard and aft (Dan's plan 4, 28 September 2026).
            Vector3 front = ship.transform.InverseTransformDirection(root.forward);
            if (local.x > -0.5f || local.z < ShipStubBuilder.DeckLength / 2f - 6f || front.x < 0.3f || front.z > -0.3f)
                errors.Add("Ship: the navigation console does not stand on the port bow diagonal facing into the ship (at " + local.ToString("F2") + ", yaw " + ship.ToShipYaw(root.eulerAngles.y).ToString("F1") + ").");
            if (rig.TopScreen == null || rig.BottomScreen == null || rig.Sign == null) errors.Add("Ship: the navigation console is missing a painted surface.");
            if (rig.LeverHinge == null || rig.LeverHandle == null) errors.Add("Ship: the navigation console's lever has no hinge or handle.");
            if (rig.LeverCollider == null || rig.LeverCollider.name != ShipParts.NavLeverName) errors.Add("Ship: the navigation console's lever control is not '" + ShipParts.NavLeverName + "'.");
            else
            {
                ConsoleControl lever = rig.ControlOf(rig.LeverCollider);
                if (lever == null || lever.Kind != ConsoleControlKind.Lever || lever.Console != ConsoleKind.Ship) errors.Add("Ship: '" + ShipParts.NavLeverName + "' is not a ship lever control.");
            }
            if (rig.CardColliders.Count != Destinations.Cards.Length) errors.Add("Ship: the navigation console has " + rig.CardColliders.Count + " card controls (" + Destinations.Cards.Length + " expected).");
            for (int i = 0; i < rig.CardColliders.Count && i < Destinations.Cards.Length; i++)
            {
                Collider c = rig.CardColliders[i];
                ConsoleControl card = rig.ControlOf(c);
                string expected = ConsoleRig.CardNamePrefix + Destinations.Cards[i];
                if (c == null || c.name != expected) errors.Add("Ship: card control " + i + " is not '" + expected + "'.");
                else if (card == null || card.Kind != ConsoleControlKind.Card || card.Payload != Destinations.Cards[i] || card.Console != ConsoleKind.Ship) errors.Add("Ship: '" + expected + "' is not the " + Destinations.Cards[i] + " card control.");
            }
            if (root.GetComponentInChildren<MeshCollider>(true) == null) errors.Add("Ship: the navigation console's body has no mesh collider.");
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

        // Dan's round elevator on the deck (28 September 2026): the car's floor flush with
        // the deck, the car at its own size, the housing solid where it is seen, and the
        // shutters' box across its entrance.
        private static void CheckDeckCabinLook(ShipParts ship, List<string> errors)
        {
            Transform cabin = ship.DeckCabin;
            if (cabin == null) return; // CheckShip names the missing part
            float floorTop = ship.ToShipLocal(cabin.position).y + DeckCabinBuilder.FloorThicknessMeters;
            if (Mathf.Abs(floorTop) > 0.001f) errors.Add("Deck cabin: the car's floor top is at ship y " + floorTop.ToString("F3") + " (flush with the deck, 0, expected).");
            Transform carGlass = ship.DeckCabinCarGlass;
            if (carGlass == null) errors.Add("Deck cabin: no " + ShipParts.DeckCabinCarGlassName + ".");
            else
            {
                if ((carGlass.lossyScale - Vector3.one).sqrMagnitude > 1e-6f) errors.Add("Deck cabin: the car's glass is at scale " + carGlass.lossyScale.ToString("F3") + " (the car's own size, 1, expected).");
                if (carGlass.Find(SunkCost.Editor.Look.ElevatorLook.CarLookName) == null) errors.Add("Deck cabin: no '" + SunkCost.Editor.Look.ElevatorLook.CarLookName + "' under the car's glass.");
            }
            Transform housing = cabin.Find(ShipStubBuilder.HousingName);
            if (housing == null) errors.Add("Deck cabin: no '" + ShipStubBuilder.HousingName + "'.");
            else if (housing.GetComponentInChildren<MeshCollider>(true) == null) errors.Add("Deck cabin: the housing has no mesh collider.");
            Collider shutters = ship.DeckCabinShutterCollider;
            if (shutters == null) errors.Add("Deck cabin: no " + ShipParts.DeckCabinShutterColliderName + ".");
            else if (shutters.isTrigger) errors.Add("Deck cabin: " + ShipParts.DeckCabinShutterColliderName + " must be solid, not a trigger.");
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
