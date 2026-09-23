using System;
using System.Collections.Generic;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The ship's parts grouped by area (ship audit SHIP-076, 23 September 2026). The
    // build left over a hundred parts flat under the root, and the buttons and
    // screens beside the models they sit on, so moving a model in the editor left
    // them behind. Each area is now one unit-scale group holding the model and every
    // part that belongs to it: move the group, and its buttons and screens go too.
    //
    // The parts go beside their model, not under it: the models are scaled (the
    // console 1.5, the TV cabinet 2, the storage room unevenly), and the displays
    // lay themselves out at unit scale under their parent (ShipMonitor, TvDisplay).
    //
    // Runs last in ShipStubBuilder.EnsurePrefab, after the spawn and boarding points:
    // every step before it finds parts as the root's direct children. The runtime
    // and the checks find parts by name at any depth (ShipParts.Find), so they do
    // not care. Every part keeps its world position; groups sit at the root's origin.
    public static class ShipHierarchy
    {
        // The models renamed so no group shares a name with them.
        public const string TowerModelName = "Tower Model";
        public const string ConsoleModelName = "Console Model";
        public const string StorageSillModelName = "StorageSill Model"; // the stub's collider keeps "StorageSill"

        public static void Group(Transform root)
        {
            Transform look = root.Find(ShipDeckDressing.LookName);
            var loose = new List<Transform>();
            foreach (Transform child in root) loose.Add(child);

            Transform well = NewGroup(root, ShipParts.WellGroupName);
            Transform tower = NewGroup(root, ShipParts.TowerGroupName);
            Transform console = NewGroup(tower, ShipParts.ConsoleGroupName);
            Transform tv = NewGroup(root, ShipParts.TvGroupName);
            Transform storage = NewGroup(root, ShipParts.StorageGroupName);
            Transform cabin = NewGroup(root, ShipParts.CabinGroupName);
            Transform volumes = NewGroup(root, ShipParts.VolumesGroupName);
            Transform points = NewGroup(root, ShipParts.PointsGroupName);

            // The models out of Look, into their areas.
            Move(look, "Tower", tower, TowerModelName);
            Move(look, "Console", console, ConsoleModelName);
            Move(look, "TvCabinet", tv, null);
            Move(look, "StorageRoom", storage, null);
            Move(look, "StorageSill", storage, StorageSillModelName);
            Move(look, ShipStorageInterior.Name, storage, null);

            // The root's own parts, by name.
            var left = new List<string>();
            foreach (Transform part in loose)
            {
                Transform to = AreaOf(part.name, well, tower, console, tv, storage, cabin, volumes, points);
                if (to != null) part.SetParent(to, true);
                else if (part.name != ShipDeckDressing.LookName && part.name != ShipAmbienceSetup.RootName) left.Add(part.name);
            }
            if (left.Count > 0) Debug.LogWarning("Ship hierarchy: parts left on the root, in no area: " + string.Join(", ", left));
        }

        private static Transform AreaOf(string name, Transform well, Transform tower, Transform console, Transform tv, Transform storage, Transform cabin, Transform volumes, Transform points)
        {
            if (name.StartsWith("Well", StringComparison.Ordinal) || name.StartsWith("Pedestal", StringComparison.Ordinal) || name.StartsWith("Grate", StringComparison.Ordinal)) return well;
            // The monitor, its buttons, its status line and its display (Monitor Display).
            if (name.StartsWith(ShipParts.MonitorName, StringComparison.Ordinal)) return console;
            if (name.StartsWith("Crew Screen", StringComparison.Ordinal)) return tower;
            // The screen, the caption, the speaker and the idle card (Tv Idle).
            if (name.StartsWith("Tv", StringComparison.Ordinal)) return tv;
            // The stub's walls, roof, lintel, sill, floor, volume and both readouts.
            if (name.StartsWith("Storage", StringComparison.Ordinal)) return storage;
            if (name == ShipParts.DeckCabinName) return cabin;
            if (name == ShipParts.AboardVolumeName || name == ShipParts.SafeDeckVolumeName || name == ShipParts.DepartureDirectionName) return volumes;
            if (name.StartsWith(ShipParts.SpawnPointPrefix, StringComparison.Ordinal) || name == ShipParts.BoardingPointName) return points;
            return null;
        }

        private static Transform NewGroup(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false); // unit scale at the parent's origin
            return group;
        }

        private static void Move(Transform look, string name, Transform to, string rename)
        {
            Transform part = look != null ? look.Find(name) : null;
            if (part == null) { Debug.LogWarning("Ship hierarchy: no " + name + " under " + ShipDeckDressing.LookName); return; }
            part.SetParent(to, true);
            if (rename != null) part.name = rename;
        }
    }
}
