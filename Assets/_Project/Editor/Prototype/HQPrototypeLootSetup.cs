using System;
using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Editor.Prototype
{
    // The one owner of the HQ loot fixture (docs/LOOT_WEIGHT_IMPLEMENTATION_PLAN.md
    // section 7): the shared weight settings, the three heavy ball prefabs, the
    // two-handed hold point on the player, and the six scene instances. Idempotent;
    // every write goes through SerializedObject or PrefabUtility. Never regenerates
    // the room, the player or the basketball.
    public static class HQPrototypeLootSetup
    {
        public const string WeightSettingsPath = "Assets/_Project/Settings/Prototype/WeightSettings.asset";
        public const string PrefabObjectsPath = "Assets/_Project/Settings/Prototype/PrototypePrefabObjects.asset";
        // Centred and low in front of the camera, for two-handed items.
        public static readonly Vector3 TwoHandHoldPointLocalPosition = new(0f, -0.35f, 0.80f);

        public sealed class FixtureEntry
        {
            public string SceneName;
            public string PrefabName;
            public string PrefabPath;
            public string DisplayName;
            public Vector3 ResetPosition;
            public float Diameter;
            public float MassKg;
            public Color Colour;
            public CarryGrip Grip;
            public bool IsBasketball => PrefabPath == HQPrototypeBuilder.BallPrefabPath;
        }

        // The manifest: what the HQ room contains after Apply(). The validator and
        // the hooks read this; nothing else decides names or positions.
        public static readonly FixtureEntry[] Manifest =
        {
            Ball("Basketball", new Vector3(0f, 1f, 0f)),
            Ball("Basketball (2)", new Vector3(1.5f, 1f, 1.5f)),
            Ball("Basketball (3)", new Vector3(-1.5f, 1f, 1.5f)),
            // Blue is heavy but one-handed and slot-able (Dan, 14/09): two of them so
            // the slots can be loaded past the 25 kg capacity for the overload test.
            Heavy("HeavyBallBlue", "HeavyBallBlue", "Blue ball", 0.40f, 6f, new Color(0.16f, 0.40f, 0.95f), CarryGrip.OneHand, new Vector3(3f, 1f, 0f)),
            Heavy("HeavyBallBlue (2)", "HeavyBallBlue", "Blue ball", 0.40f, 6f, new Color(0.16f, 0.40f, 0.95f), CarryGrip.OneHand, new Vector3(3f, 1f, 2.5f)),
            Heavy("HeavyBallPurple", "HeavyBallPurple", "Purple ball", 0.55f, 12f, new Color(0.55f, 0.22f, 0.80f), CarryGrip.TwoHands, new Vector3(-3f, 1f, 0f)),
            Heavy("HeavyBallBlack", "HeavyBallBlack", "Black ball", 0.70f, 20f, new Color(0.13f, 0.13f, 0.15f), CarryGrip.TwoHands, new Vector3(0f, 1f, -3.5f))
        };

        public static int SceneItemCount => Manifest.Length;

        // Basketball instances that the earlier five-ball fixture added and this one removes.

        private static FixtureEntry Ball(string sceneName, Vector3 reset) => new()
        {
            SceneName = sceneName, PrefabName = "Basketball", PrefabPath = HQPrototypeBuilder.BallPrefabPath, DisplayName = "Basketball",
            ResetPosition = reset, Diameter = 0.24f, MassKg = 0.62f, Colour = new Color(0.95f, 0.28f, 0.035f), Grip = CarryGrip.OneHand
        };

        private static FixtureEntry Heavy(string sceneName, string prefabName, string displayName, float diameter, float mass, Color colour, CarryGrip grip, Vector3 reset) => new()
        {
            SceneName = sceneName, PrefabName = prefabName, PrefabPath = "Assets/_Project/Prefabs/Interaction/" + prefabName + ".prefab", DisplayName = displayName,
            ResetPosition = reset, Diameter = diameter, MassKg = mass, Colour = colour, Grip = grip
        };

        [MenuItem("Sunk Cost/Prototype/Apply loot setup")]
        public static void ApplyFromMenu()
        {
            Debug.Log(Apply());
        }

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the loot setup.");
            var report = new List<string>
            {
                ApplyWeightSettingsAsset(out WeightSettings settings),
                ApplyHeavyPrefabs(settings),
                ApplyItemSettingsReferences(settings),
                ApplyPlayerPrefab(settings),
                ApplyPrefabRegistration(),
                ApplySceneFixture(),
                ItemIconGenerator.GenerateAll()
            };
            AssetDatabase.SaveAssets();
            return string.Join("; ", report);
        }

        private static string ApplyWeightSettingsAsset(out WeightSettings settings)
        {
            settings = AssetDatabase.LoadAssetAtPath<WeightSettings>(WeightSettingsPath);
            if (settings != null) return "Weight settings present";
            settings = ScriptableObject.CreateInstance<WeightSettings>();
            AssetDatabase.CreateAsset(settings, WeightSettingsPath);
            return "Weight settings created with defaults";
        }

        // Duplicate the working basketball prefab (network components and their
        // tested settings included), then change only what makes it a heavy ball.
        private static string ApplyHeavyPrefabs(WeightSettings settings)
        {
            var report = new List<string>();
            GameObject basketball = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath);
            if (basketball == null) throw new InvalidOperationException("Basketball prefab missing at " + HQPrototypeBuilder.BallPrefabPath);
            var done = new HashSet<string>();
            foreach (FixtureEntry entry in Manifest)
            {
                if (entry.IsBasketball || !done.Add(entry.PrefabPath)) continue;
                bool created = false;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath) == null)
                {
                    if (!AssetDatabase.CopyAsset(HQPrototypeBuilder.BallPrefabPath, entry.PrefabPath))
                        throw new InvalidOperationException("Could not copy the basketball prefab to " + entry.PrefabPath);
                    created = true;
                }
                Material material = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/" + entry.PrefabName + ".mat", entry.Colour);
                var changes = new List<string>();
                GameObject root = PrefabUtility.LoadPrefabContents(entry.PrefabPath);
                try
                {
                    if (root.name != entry.PrefabName) { root.name = entry.PrefabName; changes.Add("name"); }
                    // The mesh is a unit sphere; the SphereCollider radius 0.5 scales with it.
                    Vector3 scale = Vector3.one * entry.Diameter;
                    if (root.transform.localScale != scale) { root.transform.localScale = scale; changes.Add("scale"); }
                    Renderer renderer = root.GetComponent<Renderer>();
                    if (renderer != null && renderer.sharedMaterial != material) { renderer.sharedMaterial = material; changes.Add("material"); }
                    Rigidbody body = root.GetComponent<Rigidbody>();
                    if (!Mathf.Approximately(body.mass, entry.MassKg)) { body.mass = entry.MassKg; changes.Add("mass"); }
                    if (body.interpolation != RigidbodyInterpolation.Interpolate) { body.interpolation = RigidbodyInterpolation.Interpolate; changes.Add("interpolation"); } // a thrown item renders every frame, not only on physics steps
                    CarryableItem item = root.GetComponent<CarryableItem>();
                    using (var serialized = new SerializedObject(item))
                    {
                        SetString(serialized, "displayName", entry.DisplayName, changes);
                        // The slot flag and the grip go together: two-handed never fits.
                        SetBool(serialized, "fitsInSlot", entry.Grip == CarryGrip.OneHand, changes);
                        SetEnum(serialized, "grip", (int)entry.Grip, changes);
                        SetEnum(serialized, "useAction", (int)ItemUseAction.Throw, changes);
                        SetObject(serialized, "weightSettings", settings, changes);
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    if (created || changes.Count > 0)
                        PrefabUtility.SaveAsPrefabAsset(root, entry.PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                report.Add(entry.PrefabName + (created ? " created" : changes.Count == 0 ? " unchanged" : " updated: " + string.Join(",", changes)));
            }
            return string.Join("; ", report);
        }

        private static string ApplyItemSettingsReferences(WeightSettings settings)
        {
            GameObject basketball = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath);
            CarryableItem item = basketball.GetComponent<CarryableItem>();
            using var serialized = new SerializedObject(item);
            var changes = new List<string>();
            SetObject(serialized, "weightSettings", settings, changes);
            SetEnum(serialized, "grip", (int)CarryGrip.OneHand, changes);
            if (changes.Count == 0) return "Basketball prefab already references the weight settings";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return "Basketball prefab: " + string.Join(",", changes);
        }

        private static string ApplyPlayerPrefab(WeightSettings settings)
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                HQPlayerController controller = root.GetComponent<HQPlayerController>();
                PlayerInventory inventory = root.GetComponent<PlayerInventory>();
                if (controller == null || inventory == null)
                    throw new InvalidOperationException("Player prefab needs HQPlayerController and PlayerInventory (run Apply inventory setup first).");
                using var serializedController = new SerializedObject(controller);
                var camera = serializedController.FindProperty("playerCamera").objectReferenceValue as Camera;
                if (camera == null) throw new InvalidOperationException("Player prefab has no camera reference.");
                SerializedProperty pointProperty = serializedController.FindProperty("twoHandHoldPoint");
                var point = pointProperty.objectReferenceValue as Transform;
                if (point == null)
                {
                    Transform existing = camera.transform.Find("TwoHandHoldPoint");
                    point = existing != null ? existing : new GameObject("TwoHandHoldPoint").transform;
                    point.SetParent(camera.transform, false);
                    pointProperty.objectReferenceValue = point;
                    changes.Add("TwoHandHoldPoint assigned");
                }
                if (point.parent != camera.transform) { point.SetParent(camera.transform, false); changes.Add("TwoHandHoldPoint re-parented"); }
                if (point.localPosition != TwoHandHoldPointLocalPosition || point.localRotation != Quaternion.identity)
                {
                    point.localPosition = TwoHandHoldPointLocalPosition;
                    point.localRotation = Quaternion.identity;
                    changes.Add("TwoHandHoldPoint moved to " + TwoHandHoldPointLocalPosition);
                }
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                using var serializedInventory = new SerializedObject(inventory);
                SetObject(serializedInventory, "weightSettings", settings, changes);
                serializedInventory.ApplyModifiedPropertiesWithoutUndo();

                if (changes.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes.Count == 0 ? "Player prefab already set up" : "Player prefab: " + string.Join(", ", changes);
        }

        // The NetworkManager spawns from PrototypePrefabObjects, not from FishNet's
        // auto-generated default collection, so the heavy prefabs must be listed there.
        private static string ApplyPrefabRegistration()
        {
            var collection = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(PrefabObjectsPath);
            if (collection == null) throw new InvalidOperationException("Prefab collection missing at " + PrefabObjectsPath);
            int added = 0;
            var seen = new HashSet<string>();
            foreach (FixtureEntry entry in Manifest)
            {
                if (entry.IsBasketball || !seen.Add(entry.PrefabPath)) continue;
                NetworkObject nob = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath).GetComponent<NetworkObject>();
                if (IsRegistered(collection, nob)) continue;
                collection.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
                added++;
            }
            if (added == 0) return "Prefab collection already lists the heavy balls";
            EditorUtility.SetDirty(collection);
            return "Prefab collection: " + added + " heavy balls added";
        }

        public static bool IsRegistered(DefaultPrefabObjects collection, NetworkObject nob)
        {
            for (int i = 0; i < collection.GetObjectCount(); i++)
                if (collection.GetObject(true, i) == nob) return true;
            return false;
        }

        // Scene: the LootFixtureSpawner's entries are exactly the manifest, by name.
        // Items are spawned at runtime (FishNet will not move scene objects between
        // scenes), so any carryable saved in the scene is removed here.
        public static string ApplySceneFixture()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != HQPrototypeBuilder.ScenePath)
                throw new InvalidOperationException("Open " + HQPrototypeBuilder.ScenePath + " before applying the loot setup (active: " + scene.path + ").");

            var changes = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (CarryableItem item in root.GetComponentsInChildren<CarryableItem>(true))
                {
                    changes.Add("removed scene object " + item.name);
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
                }
            }

            LootFixtureSpawner spawner = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                spawner = root.GetComponentInChildren<LootFixtureSpawner>(true);
                if (spawner != null) break;
            }
            if (spawner == null)
            {
                spawner = new GameObject("Loot Fixture").AddComponent<LootFixtureSpawner>();
                changes.Add("added Loot Fixture");
            }

            var entries = new List<LootFixtureSpawner.Entry>();
            foreach (FixtureEntry entry in Manifest)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                if (prefab == null) throw new InvalidOperationException("Prefab missing: " + entry.PrefabPath);
                entries.Add(new LootFixtureSpawner.Entry { Name = entry.SceneName, Prefab = prefab, Position = entry.ResetPosition });
            }
            bool same = spawner.Entries.Count == entries.Count;
            for (int i = 0; same && i < entries.Count; i++)
                same = spawner.Entries[i].Name == entries[i].Name && spawner.Entries[i].Prefab == entries[i].Prefab && spawner.Entries[i].Position == entries[i].Position;
            if (!same)
            {
                spawner.SetEntries(entries.ToArray());
                EditorUtility.SetDirty(spawner);
                changes.Add("fixture entries = manifest (" + entries.Count + ")");
            }

            int sceneIdsAssigned = RebuildMissingSceneIds(scene);
            if (sceneIdsAssigned > 0) changes.Add(sceneIdsAssigned + " scene ids assigned");

            if (changes.Count == 0) return "Scene fixture already matches the manifest";
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Unity could not save " + scene.path);
            return "Scene: " + string.Join(", ", changes);
        }

        // FishNet gives a scene NetworkObject its SceneId from OnValidate, but that
        // path is throttled to one rebuild per 250 ms, so instantiating several
        // prefabs in one frame leaves all but the first unset and they are destroyed
        // at runtime ("expected to be initialized but was not"). Run the scene-wide
        // rebuild FishNet's own Reserialize utility uses; force:false keeps ids that
        // are already set and unique.
        private static int RebuildMissingSceneIds(Scene scene)
        {
            var method = typeof(NetworkObject).GetMethod("CreateSceneId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(Scene), typeof(bool), typeof(int).MakeByRefType() }, null);
            if (method == null) throw new InvalidOperationException("FishNet NetworkObject.CreateSceneId(Scene, bool, out int) not found; check the FishNet version.");
            object[] args = { scene, false, 0 };
            method.Invoke(null, args);
            return (int)args[2];
        }

        private static void SetString(SerializedObject o, string name, string value, List<string> changes)
        {
            SerializedProperty p = o.FindProperty(name);
            if (p.stringValue == value) return;
            p.stringValue = value; changes.Add(name);
        }

        private static void SetBool(SerializedObject o, string name, bool value, List<string> changes)
        {
            SerializedProperty p = o.FindProperty(name);
            if (p.boolValue == value) return;
            p.boolValue = value; changes.Add(name);
        }

        private static void SetEnum(SerializedObject o, string name, int value, List<string> changes)
        {
            SerializedProperty p = o.FindProperty(name);
            if (p.enumValueIndex == value) return;
            p.enumValueIndex = value; changes.Add(name);
        }

        private static void SetObject(SerializedObject o, string name, UnityEngine.Object value, List<string> changes)
        {
            SerializedProperty p = o.FindProperty(name);
            if (p.objectReferenceValue == value) return;
            p.objectReferenceValue = value; changes.Add(name);
        }

        private static void SetVector(SerializedObject o, string name, Vector3 value, List<string> changes)
        {
            SerializedProperty p = o.FindProperty(name);
            if (p.vector3Value == value) return;
            p.vector3Value = value; changes.Add(name);
        }
    }
}
