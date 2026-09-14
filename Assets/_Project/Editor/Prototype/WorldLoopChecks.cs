using System;
using System.Collections.Generic;
using FishNet.Managing.Scened;
using SunkCost.World;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Pure and asset checks for the scene flow (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
    // section 9.1). No Play Mode, no network: the load data the flow builds, the
    // settings asset, the scene name table and the ship part list.
    public static class WorldLoopChecks
    {
        [MenuItem("Sunk Cost/Prototype/Check world loop (pure)")]
        public static void RunFromMenu() => Debug.Log(RunOrThrow());

        public static string RunOrThrow()
        {
            var errors = new List<string>();

            // Load data: additive, never auto-unloaded, client active scene = world,
            // server active scene untouched (plan section 3.2).
            foreach (WorldId world in Enum.GetValues(typeof(WorldId)))
            {
                SceneLoadData load = WorldSceneFlow.LoadDataFor(world, null);
                if (load.ReplaceScenes != ReplaceOption.None) errors.Add(world + ": load must be additive.");
                if (load.Options == null || load.Options.AutomaticallyUnload) errors.Add(world + ": load must not auto-unload.");
                if (load.SceneLookupDatas.Length != 1 || load.SceneLookupDatas[0].Name != WorldScenes.Name(world)) errors.Add(world + ": load names the wrong scene.");
                // SceneLookupData overloads == and != and its != returns true for two
                // nulls (FishNet 4.7.3), so null tests use `is null`.
                if (load.PreferredActiveScene.Client is null || load.PreferredActiveScene.Client.Name != WorldScenes.Name(world)) errors.Add(world + ": client active scene must be the world.");
                if (load.PreferredActiveScene.Server is not null) errors.Add(world + ": server active scene must be left alone.");
                if (load.MovedNetworkObjects == null) errors.Add(world + ": moved objects must never be null.");
                SceneUnloadData keep = WorldSceneFlow.UnloadDataFor(world, keepOnServer: true);
                SceneUnloadData drop = WorldSceneFlow.UnloadDataFor(world, keepOnServer: false);
                if (keep.Options.Mode != UnloadOptions.ServerUnloadMode.KeepUnused) errors.Add(world + ": keep-on-server unload must be KeepUnused.");
                if (drop.Options.Mode != UnloadOptions.ServerUnloadMode.UnloadUnused) errors.Add(world + ": drop unload must be UnloadUnused.");
                if (!WorldScenes.TryParse(WorldScenes.Name(world), out WorldId parsed) || parsed != world) errors.Add(world + ": name round trip failed.");
            }
            if (WorldScenes.TryParse(WorldScenes.SessionName, out _)) errors.Add("Session must never parse as a world.");

            // Settings asset.
            WorldLoopSettings settings = AssetDatabase.LoadAssetAtPath<WorldLoopSettings>(ShipStubBuilder.SettingsPath);
            if (settings == null) errors.Add("WorldLoopSettings asset missing (run Create or Update Session).");
            else
            {
                if (!settings.IsValid) errors.Add("WorldLoopSettings has an invalid value.");
                if (settings.SyncFlushTicks < 1) errors.Add("syncFlushTicks must be at least 1 (contract section 6).");
                if (Mathf.Abs(settings.ShipAtSeaOrigin.z) < 200f && Mathf.Abs(settings.ShipAtSeaOrigin.x) < 200f)
                    errors.Add("shipAtSeaOrigin must be far from the site at the origin (both are loaded during a day).");
            }
            WorldLoopSettings fallback = WorldLoopSettings.Resolve(null);
            if (fallback == null || !fallback.IsValid) errors.Add("WorldLoopSettings fallback is invalid.");

            // Ship prefab parts.
            GameObject ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipStubBuilder.PrefabPath);
            ShipParts parts = ship != null ? ship.GetComponent<ShipParts>() : null;
            if (parts == null) errors.Add("Ship prefab missing or without ShipParts (run Create or Update ship stub).");
            else
            {
                foreach (string missing in parts.MissingChildren()) errors.Add("Ship prefab is missing part " + missing + ".");
                Vector3 local = new(1f, 0f, 2f);
                Vector3 world = parts.FromShipLocal(local);
                if (Vector3.Distance(parts.ToShipLocal(world), local) > 0.0001f) errors.Add("Ship local/world round trip failed.");
            }

            if (errors.Count > 0) throw new InvalidOperationException("World loop pure checks failed:\n- " + string.Join("\n- ", errors));
            return "World loop pure checks passed: load/unload data, scene names, settings, ship parts.";
        }
    }
}
