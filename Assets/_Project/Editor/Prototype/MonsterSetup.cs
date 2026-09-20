using System;
using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using SunkCost.Editor.Look;
using SunkCost.Interaction;
using SunkCost.Monsters;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The monsters' assets (docs/DESIGN.md §6, 20 September 2026): the Monster
    // layer (solid to the world, not to players, cargo or each other), the
    // settings asset in Resources, one placeholder prefab per walker — a
    // distinct dark silhouette from the look kit, glowing eyes, NetworkObject +
    // a server-authoritative NetworkTransform, a CharacterController, the
    // kind's Creature and the look and sound components — registered as
    // spawnables. Then the patch kit and its shop row, and the HQ scene rebuilt
    // for the kit's stand. Idempotent: an existing prefab is kept (the
    // rebuild menu replaces them all).
    public static class MonsterSetup
    {
        public const string SettingsPath = "Assets/_Project/Resources/" + MonsterSettings.ResourceName + ".asset";

        [MenuItem("Sunk Cost/Prototype/Apply monster setup (layer, settings, prefabs, patch kit, shop)")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        [MenuItem("Sunk Cost/Prototype/Rebuild monster prefabs (loses edits to them)")]
        public static void RebuildFromMenu() => Debug.Log(Apply(force: true, rebuildHq: false));

        public static string Apply() => Apply(false, true);

        public static string Apply(bool force, bool rebuildHq)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before applying the monster setup.");
            var changes = new List<string>();
            changes.AddRange(EnsureLayer());
            changes.AddRange(EnsureSettings());
            changes.AddRange(EnsurePrefabs(force));
            changes.AddRange(RegisterPrefabs());
            changes.AddRange(PatchKitSetup.EnsurePrefab());
            changes.AddRange(PatchKitSetup.Register());
            string shop = ShopSetup.Apply();
            if (!shop.StartsWith("Shop already")) changes.Add(shop);
            AssetDatabase.SaveAssets();
            if (rebuildHq) { HQPrototypeBuilder.CreateOrUpdate(); changes.Add("HQ rebuilt (the patch kit's stand)"); }
            return changes.Count == 0 ? "Monsters already set up" : "Monsters: " + string.Join("; ", changes);
        }

        // ---- the layer ---------------------------------------------------------------

        private static IEnumerable<string> EnsureLayer()
        {
            var changes = new List<string>();
            Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject serialized = new(tagManager);
            SerializedProperty layers = serialized.FindProperty("layers");
            if (LayerMask.NameToLayer(MonsterCatalog.LayerName) < 0)
            {
                int free = -1;
                for (int i = 8; i < layers.arraySize; i++)
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { free = i; break; }
                if (free < 0) throw new InvalidOperationException("No free user layer for " + MonsterCatalog.LayerName + ".");
                layers.GetArrayElementAtIndex(free).stringValue = MonsterCatalog.LayerName;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changes.Add("layer " + MonsterCatalog.LayerName + " = " + free);
            }
            int monster = LayerMask.NameToLayer(MonsterCatalog.LayerName);
            foreach ((string name, int other) in new[] { (CarryableCollisionPolicy.PlayerLayerName, CarryableCollisionPolicy.PlayerLayer), (CarryableCollisionPolicy.CarryableLayerName, CarryableCollisionPolicy.CarryableLayer), (MonsterCatalog.LayerName, monster) })
            {
                if (monster < 0 || other < 0 || Physics.GetIgnoreLayerCollision(monster, other)) continue;
                Physics.IgnoreLayerCollision(monster, other, true);
                changes.Add("Monster/" + name + " collision ignored");
            }
            return changes;
        }

        // ---- the settings ------------------------------------------------------------

        private static IEnumerable<string> EnsureSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<MonsterSettings>(SettingsPath) != null) return Array.Empty<string>();
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<MonsterSettings>(), SettingsPath);
            return new[] { "MonsterSettings created" };
        }

        // ---- the prefabs -------------------------------------------------------------

        private static IEnumerable<string> EnsurePrefabs(bool force)
        {
            var changes = new List<string>();
            System.IO.Directory.CreateDirectory(MonsterCatalog.PrefabFolder);
            foreach (MonsterKind kind in MonsterCatalog.Walkers)
            {
                string path = MonsterCatalog.PrefabPath(kind);
                if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                Build(kind);
                changes.Add(MonsterCatalog.PrefabName(kind) + (force ? " rebuilt" : " created"));
            }
            return changes;
        }

        private static void Build(MonsterKind kind)
        {
            string path = MonsterCatalog.PrefabPath(kind);
            GameObject root = new(MonsterCatalog.PrefabName(kind));
            try
            {
                root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                networkTransform.SetSynchronizeScale(false);
                CharacterController mover = root.AddComponent<CharacterController>();
                mover.stepOffset = 0.4f; mover.slopeLimit = 45f; mover.skinWidth = 0.05f;
                float eyeHeight;
                Creature creature;
                switch (kind)
                {
                    case MonsterKind.LongWalker: creature = root.AddComponent<LongWalker>(); Shape(mover, 3.2f, 0.35f); eyeHeight = 3f; BuildWalker(root); break;
                    case MonsterKind.WeepingAngel: creature = root.AddComponent<WeepingAngel>(); Shape(mover, 2.1f, 0.45f); eyeHeight = 1.85f; BuildAngel(root); break;
                    case MonsterKind.Charger: creature = root.AddComponent<Charger>(); Shape(mover, 1.4f, 0.7f); eyeHeight = 0.9f; BuildCharger(root); break;
                    case MonsterKind.Lure: root.AddComponent<CreatureBolts>(); creature = root.AddComponent<Lure>(); Shape(mover, 2.2f, 0.5f); eyeHeight = 1.5f; BuildLure(root); break;
                    case MonsterKind.Listener: root.AddComponent<CreatureBolts>(); creature = root.AddComponent<Listener>(); Shape(mover, 2.0f, 0.6f); eyeHeight = 1.6f; BuildListener(root); break;
                    case MonsterKind.Impostor: creature = root.AddComponent<Impostor>(); Shape(mover, 1.8f, 0.3f); eyeHeight = 1.6f; BuildImpostor(root); break;
                    default: throw new InvalidOperationException("No prefab recipe for " + kind);
                }
                using (var serialized = new SerializedObject(creature))
                {
                    serialized.FindProperty("kind").enumValueIndex = (int)kind;
                    serialized.FindProperty("eyeHeight").floatValue = eyeHeight;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                CreatureLook look = root.AddComponent<CreatureLook>();
                using (var serialized = new SerializedObject(look))
                {
                    serialized.FindProperty("body").objectReferenceValue = root.transform.Find("Body");
                    if (kind == MonsterKind.Impostor) serialized.FindProperty("eyes").arraySize = 0;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                root.AddComponent<CreatureSounds>();
                // The server writes this transform; no client ever controls a creature.
                using (var serialized = new SerializedObject(networkTransform))
                {
                    SerializedProperty clientAuthoritative = serialized.FindProperty("_clientAuthoritative");
                    if (clientAuthoritative != null) clientAuthoritative.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                CarryableCollisionPolicy.SetLayerRecursively(root, MonsterCatalog.Layer);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Shape(CharacterController mover, float height, float radius)
        {
            mover.height = height; mover.radius = radius; mover.center = new Vector3(0f, height * 0.5f, 0f);
        }

        // A dark column three metres tall with long hanging arms and a small head.
        private static void BuildWalker(GameObject root)
        {
            GameObject body = Child(root, "Body");
            Part(body, "Trunk", MeshKit.Box(new Vector3(0.5f, 2.9f, 0.4f)), LookMaterials.Creature(), Vector3.zero);
            Part(body, "Arm L", MeshKit.Box(new Vector3(0.12f, 1.7f, 0.12f)), LookMaterials.Creature(), new Vector3(-0.42f, 0.9f, 0f));
            Part(body, "Arm R", MeshKit.Box(new Vector3(0.12f, 1.7f, 0.12f)), LookMaterials.Creature(), new Vector3(0.42f, 0.9f, 0f));
            Part(body, "Head", MeshKit.Box(new Vector3(0.36f, 0.34f, 0.36f)), LookMaterials.Creature(), new Vector3(0f, 2.9f, 0f));
            Eye(body, "Eye L", new Vector3(-0.09f, 3.06f, 0.19f), 0.07f);
            Eye(body, "Eye R", new Vector3(0.09f, 3.06f, 0.19f), 0.07f);
        }

        // A hooded figure with curved wing plates behind it and eyes under the hood.
        private static void BuildAngel(GameObject root)
        {
            GameObject body = Child(root, "Body");
            Part(body, "Robe", MeshKit.Cylinder(0.45f, 1.7f, 12), LookMaterials.Creature(), Vector3.zero);
            Part(body, "Hood", MeshKit.Cylinder(0.3f, 0.5f, 10), LookMaterials.Creature(), new Vector3(0f, 1.6f, 0f));
            Part(body, "Wing L", MeshKit.Arc(0.9f, 0.08f, 100f, 260f, 24, 6), LookMaterials.Creature(), new Vector3(0f, 1.2f, -0.35f), Quaternion.Euler(90f, 0f, 0f));
            Part(body, "Wing R", MeshKit.Arc(0.9f, 0.08f, 280f, 440f, 24, 6), LookMaterials.Creature(), new Vector3(0f, 1.2f, -0.35f), Quaternion.Euler(90f, 0f, 0f));
            Eye(body, "Eye L", new Vector3(-0.08f, 1.85f, 0.26f), 0.06f);
            Eye(body, "Eye R", new Vector3(0.08f, 1.85f, 0.26f), 0.06f);
        }

        // A low, wide slab with a blunt head thrust forward.
        private static void BuildCharger(GameObject root)
        {
            GameObject body = Child(root, "Body");
            Part(body, "Slab", MeshKit.Box(new Vector3(1.3f, 0.9f, 2.0f)), LookMaterials.Creature(), new Vector3(0f, 0.15f, -0.3f));
            Part(body, "Head", MeshKit.Box(new Vector3(0.9f, 0.55f, 0.8f)), LookMaterials.Creature(), new Vector3(0f, 0.55f, 0.9f));
            Part(body, "Horn", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.5f)), LookMaterials.Creature(), new Vector3(0f, 0.95f, 1.2f));
            Eye(body, "Eye L", new Vector3(-0.25f, 0.9f, 1.32f), 0.09f);
            Eye(body, "Eye R", new Vector3(0.25f, 0.9f, 1.32f), 0.09f);
        }

        // A lantern fish on legs: a round body, a glowing halo over it, one great eye.
        private static void BuildLure(GameObject root)
        {
            GameObject body = Child(root, "Body");
            Part(body, "Legs", MeshKit.Cylinder(0.2f, 0.9f, 8), LookMaterials.Creature(), Vector3.zero);
            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb"; Object.DestroyImmediate(bulb.GetComponent<Collider>());
            bulb.transform.SetParent(body.transform, false); bulb.transform.localPosition = new Vector3(0f, 1.4f, 0f); bulb.transform.localScale = Vector3.one * 1.0f;
            bulb.GetComponent<Renderer>().sharedMaterial = LookMaterials.Creature();
            Part(body, "Eye Halo", MeshKit.Ring(0.55f, 0.04f, 24, 6), LookMaterials.EyeGlow(), new Vector3(0f, 2.05f, 0f));
            Eye(body, "Eye", new Vector3(0f, 1.5f, 0.5f), 0.22f);
        }

        // An eyeless drum with a teal rim: nothing to look at you with.
        private static void BuildListener(GameObject root)
        {
            GameObject body = Child(root, "Body");
            Part(body, "Drum", MeshKit.Cylinder(0.55f, 1.7f, 16), LookMaterials.Creature(), Vector3.zero);
            Part(body, "Rim", MeshKit.Ring(0.58f, 0.03f, 24, 6), LookMaterials.ScreenTeal(), new Vector3(0f, 1.72f, 0f));
            Part(body, "Ear L", MeshKit.Band(0.4f, 0.5f, 0.03f, 20f, 160f, 16), LookMaterials.Creature(), new Vector3(-0.55f, 1.0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            Part(body, "Ear R", MeshKit.Band(0.4f, 0.5f, 0.03f, 200f, 340f, 16), LookMaterials.Creature(), new Vector3(0.55f, 1.0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        }

        // A diver's own shape: the capsule body in a crewmate's colour, a lamp, the
        // name over the head; no eyes. ImpostorLook dresses it and decides who sees it.
        private static void BuildImpostor(GameObject root)
        {
            GameObject body = Child(root, "Body");
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Suit"; Object.DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(body.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Renderer suit = capsule.GetComponent<Renderer>();
            suit.sharedMaterial = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ImpostorSuit.mat", Color.white);
            Part(body, "Visor", MeshKit.Box(new Vector3(0.3f, 0.14f, 0.1f)), LookMaterials.Ink(), new Vector3(0f, 1.55f, 0.27f));
            GameObject lampObject = new("Lamp", typeof(Light));
            lampObject.transform.SetParent(body.transform, false);
            lampObject.transform.localPosition = new Vector3(0f, 1.6f, 0.2f);
            Light lamp = lampObject.GetComponent<Light>();
            lamp.type = LightType.Spot; lamp.intensity = 15f; lamp.range = 25f; lamp.spotAngle = 35f; lamp.color = new Color(1f, 0.93f, 0.82f); lamp.shadows = LightShadows.None; lamp.enabled = false;
            GameObject plate = new("NamePlate");
            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = new Vector3(0f, SunkCost.Player.PlayerNamePlate.HeightMeters, 0f);
            GameObject shadow = PropBuilder.Text(plate, "Shadow", new Vector3(0.008f, -0.008f, -0.006f), 0.13f, new Color(0f, 0f, 0f, 0.7f), TextAnchor.MiddleCenter);
            GameObject text = PropBuilder.Text(plate, "Name", Vector3.zero, 0.13f, new Color(0.96f, 0.96f, 0.96f, 0.95f), TextAnchor.MiddleCenter);
            text.GetComponent<TextMesh>().text = shadow.GetComponent<TextMesh>().text = "Diver";
            ImpostorLook look = root.AddComponent<ImpostorLook>();
            using var serialized = new SerializedObject(look);
            serialized.FindProperty("bodyRenderer").objectReferenceValue = suit;
            serialized.FindProperty("nameText").objectReferenceValue = text.GetComponent<TextMesh>();
            serialized.FindProperty("nameShadow").objectReferenceValue = shadow.GetComponent<TextMesh>();
            serialized.FindProperty("namePlate").objectReferenceValue = plate.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Child(GameObject root, string name)
        {
            GameObject go = new(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        private static GameObject Part(GameObject parent, string name, Mesh mesh, Material material, Vector3 localPosition) => Part(parent, name, mesh, material, localPosition, Quaternion.identity);
        private static GameObject Part(GameObject parent, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        // A glowing sphere the look pulses (its name starts with "Eye").
        private static void Eye(GameObject parent, string name, Vector3 localPosition, float diameter)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = name;
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.transform.SetParent(parent.transform, false);
            eye.transform.localPosition = localPosition;
            eye.transform.localScale = Vector3.one * diameter;
            eye.GetComponent<Renderer>().sharedMaterial = LookMaterials.EyeGlow();
        }

        // ---- the collection ----------------------------------------------------------

        public static IEnumerable<string> RegisterPrefabs()
        {
            var collection = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(HQPrototypeLootSetup.PrefabObjectsPath);
            if (collection == null) throw new InvalidOperationException("Prefab collection missing at " + HQPrototypeLootSetup.PrefabObjectsPath);
            int added = 0;
            foreach (MonsterKind kind in MonsterCatalog.Walkers)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterCatalog.PrefabPath(kind));
                if (prefab == null) continue;
                NetworkObject nob = prefab.GetComponent<NetworkObject>();
                if (HQPrototypeLootSetup.IsRegistered(collection, nob)) continue;
                collection.AddObject(nob, checkForDuplicates: true, initializeAdded: true);
                added++;
            }
            if (added == 0) return Array.Empty<string>();
            EditorUtility.SetDirty(collection);
            return new[] { "prefab collection: " + added + " monster prefabs added" };
        }
    }
}
