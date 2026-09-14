using System;
using SunkCost.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // TEMPORARY. A grey-box ship prefab and the ShipAtSea scene with the exact
    // part names of docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.4, so scene
    // flow can be built and tested before Idan's ship lands. His ShipBuilder
    // replaces the prefab at the same path and the sea scene, and deletes this
    // file (Notion: "The ship prefab, grey box" / "ShipAtSea").
    public static class ShipStubBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/World/Ship.prefab";
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/WorldLoopSettings.asset";
        public const float DeckLength = 30f;
        public const float DeckWidth = 10f;
        public const float DeckThickness = 0.5f;

        [MenuItem("Sunk Cost/Prototype/Create or Update ship stub")]
        public static void CreateOrUpdateFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the ship stub.");
            EnsurePrefab();
            CreateOrUpdateSeaScene();
            Debug.Log("Ship stub prefab and ShipAtSea scene written.");
        }

        public static WorldLoopSettings EnsureSettings()
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Settings/Prototype");
            WorldLoopSettings settings = AssetDatabase.LoadAssetAtPath<WorldLoopSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<WorldLoopSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        // Regenerated every time: it is a stub, nothing in it is hand-tuned.
        public static GameObject EnsurePrefab()
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Prefabs/World");
            Material deck = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipDeck.mat", new Color(0.30f, 0.27f, 0.24f));
            Material rail = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipRail.mat", new Color(0.45f, 0.42f, 0.38f));
            Material glass = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipCabin.mat", new Color(0.55f, 0.75f, 0.85f));
            Material screen = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipScreen.mat", new Color(0.08f, 0.16f, 0.22f));
            Material button = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipButton.mat", new Color(0.9f, 0.75f, 0.2f));
            Material tape = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipTape.mat", new Color(0.85f, 0.65f, 0.1f));

            GameObject root = new(ShipParts.RootName);
            try
            {
                root.AddComponent<ShipParts>();
                // Deck top at y = 0 so the HQ plank and the HQ floor meet it flush.
                Block("Deck", root.transform, new Vector3(0f, -DeckThickness / 2f, 0f), new Vector3(DeckWidth, DeckThickness, DeckLength), deck);
                Block("RailPort", root.transform, new Vector3(-DeckWidth / 2f + 0.1f, 0.5f, 0f), new Vector3(0.2f, 1f, DeckLength), rail);
                Block("RailStarboard", root.transform, new Vector3(DeckWidth / 2f - 0.1f, 0.5f, 0f), new Vector3(0.2f, 1f, DeckLength), rail);
                Block("RailBow", root.transform, new Vector3(0f, 0.5f, DeckLength / 2f - 0.1f), new Vector3(DeckWidth, 1f, 0.2f), rail);
                // The stern rail leaves the boarding gap open: BoardingPoint is where the plank meets the deck.
                Block("RailSternPort", root.transform, new Vector3(-3f, 0.5f, -DeckLength / 2f + 0.1f), new Vector3(4f, 1f, 0.2f), rail);
                Block("RailSternStarboard", root.transform, new Vector3(3f, 0.5f, -DeckLength / 2f + 0.1f), new Vector3(4f, 1f, 0.2f), rail);

                Trigger(ShipParts.AboardVolumeName, root.transform, new Vector3(0f, 2f, 0f), new Vector3(DeckWidth + 2f, 4f, DeckLength + 2f));

                GameObject monitor = Block(ShipParts.MonitorName, root.transform, new Vector3(0f, 1.6f, DeckLength / 2f - 1.5f), new Vector3(1.6f, 1f, 0.1f), screen);
                Block(ShipParts.MonitorButtonSite01Name, root.transform, new Vector3(-0.4f, 1.5f, DeckLength / 2f - 1.58f), new Vector3(0.45f, 0.3f, 0.08f), button);
                Block(ShipParts.MonitorButtonHQName, root.transform, new Vector3(0.4f, 1.5f, DeckLength / 2f - 1.58f), new Vector3(0.45f, 0.3f, 0.08f), button);
                Label("MonitorLabel", monitor.transform, new Vector3(0f, 0.72f, 0f), "SITE 01        HQ", 0.05f, facingBow: false);

                GameObject cabin = new(ShipParts.DeckCabinName);
                cabin.transform.SetParent(root.transform, false);
                cabin.transform.localPosition = new Vector3(0f, 0f, -8f);
                Trigger(ShipParts.DeckCabinVolumeName, cabin.transform, new Vector3(0f, 1.75f, 0f), new Vector3(4.5f, 3.5f, 4.5f));
                Block("PostA", cabin.transform, new Vector3(-2.25f, 1.75f, -2.25f), new Vector3(0.15f, 3.5f, 0.15f), rail);
                Block("PostB", cabin.transform, new Vector3(2.25f, 1.75f, -2.25f), new Vector3(0.15f, 3.5f, 0.15f), rail);
                Block("PostC", cabin.transform, new Vector3(-2.25f, 1.75f, 2.25f), new Vector3(0.15f, 3.5f, 0.15f), rail);
                Block("PostD", cabin.transform, new Vector3(2.25f, 1.75f, 2.25f), new Vector3(0.15f, 3.5f, 0.15f), rail);
                Block("Roof", cabin.transform, new Vector3(0f, 3.55f, 0f), new Vector3(4.6f, 0.1f, 4.6f), rail);
                Block("WallBack", cabin.transform, new Vector3(0f, 1.75f, -2.25f), new Vector3(4.5f, 3.5f, 0.05f), glass);
                Block("WallLeft", cabin.transform, new Vector3(-2.25f, 1.75f, 0f), new Vector3(0.05f, 3.5f, 4.5f), glass);
                Block("WallRight", cabin.transform, new Vector3(2.25f, 1.75f, 0f), new Vector3(0.05f, 3.5f, 4.5f), glass);
                // Doors on the bow side, open (parked to the sides) in the stub.
                Block(ShipParts.DeckCabinDoorLName, cabin.transform, new Vector3(-1.7f, 1.75f, 2.25f), new Vector3(1.1f, 3.5f, 0.05f), glass);
                Block(ShipParts.DeckCabinDoorRName, cabin.transform, new Vector3(1.7f, 1.75f, 2.25f), new Vector3(1.1f, 3.5f, 0.05f), glass);
                Block(ShipParts.DeckCabinButtonName, cabin.transform, new Vector3(2.1f, 1.3f, 0f), new Vector3(0.15f, 0.15f, 0.15f), button);
                Label(ShipParts.DeckCabinPanelName, cabin.transform, new Vector3(0f, 3.15f, 2.3f), string.Empty, 0.05f, facingBow: true);

                Block(ShipParts.StorageAreaName, root.transform, new Vector3(0f, 0.02f, -13f), new Vector3(4f, 0.04f, 3.5f), tape);

                Vector3[] spawns = { new(-3f, 0f, 5f), new(3f, 0f, 5f), new(-3f, 0f, 1f), new(3f, 0f, 1f) };
                for (int i = 0; i < spawns.Length; i++)
                {
                    GameObject point = new(ShipParts.SpawnPointPrefix + (i + 1));
                    point.transform.SetParent(root.transform, false);
                    point.transform.localPosition = spawns[i];
                    point.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the cabin
                }
                GameObject boarding = new(ShipParts.BoardingPointName);
                boarding.transform.SetParent(root.transform, false);
                boarding.transform.localPosition = new Vector3(0f, 0f, -DeckLength / 2f);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void CreateOrUpdateSeaScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? EnsurePrefab();
            WorldLoopSettings settings = EnsureSettings();
            Material water = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/SeaWater.mat", new Color(0.05f, 0.14f, 0.2f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = WorldScenes.SeaName;
            // NewScene unloads assets nothing references; take the references again.
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            settings = AssetDatabase.LoadAssetAtPath<WorldLoopSettings>(SettingsPath);
            water = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/SeaWater.mat", new Color(0.05f, 0.14f, 0.2f));
            Vector3 origin = settings.ShipAtSeaOrigin;

            GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            sea.transform.position = origin + new Vector3(0f, -1f, 0f);
            sea.transform.localScale = new Vector3(200f, 1f, 200f); // a 2 km plane
            sea.GetComponent<Renderer>().sharedMaterial = water;

            GameObject sun = new("Sun", typeof(Light));
            sun.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            Light light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.color = new Color(0.8f, 0.85f, 0.95f);
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.28f, 0.32f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.45f, 0.5f, 0.55f);
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 350f;

            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            ship.transform.SetPositionAndRotation(origin, Quaternion.identity);

            if (!EditorSceneManager.SaveScene(scene, WorldScenes.SeaPath))
                throw new InvalidOperationException("Unity could not save " + WorldScenes.SeaPath);
            AssetDatabase.SaveAssets();
        }

        internal static GameObject Block(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        private static void Trigger(string name, Transform parent, Vector3 center, Vector3 size)
        {
            GameObject volume = new(name);
            volume.transform.SetParent(parent, false);
            BoxCollider box = volume.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = center;
            box.size = size;
        }

        // A TextMesh reads correctly to a viewer looking along +Z; a label that must
        // be read by someone approaching from the bow (looking toward -Z) is turned.
        private static void Label(string name, Transform parent, Vector3 localPosition, string text, float size, bool facingBow)
        {
            GameObject go = new(name, typeof(TextMesh));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, facingBow ? 180f : 0f, 0f);
            TextMesh mesh = go.GetComponent<TextMesh>();
            mesh.text = text;
            mesh.characterSize = size;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.9f, 0.95f, 1f);
        }
    }
}
