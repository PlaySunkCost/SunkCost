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
        public const float GangwayLength = 6f;
        public const float GangwayWidth = 1.6f;

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
            // The same transparent glass as the seafloor car: it is the same cabin (Dan, 15 September 2026).
            Material glass = SunkCost.Sites.DiveSiteBuilder.GetOrCreateGlassMaterial();
            Material screen = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipScreen.mat", new Color(0.08f, 0.16f, 0.22f));
            Material button = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipButton.mat", new Color(0.9f, 0.75f, 0.2f));
            Material tape = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipTape.mat", new Color(0.85f, 0.65f, 0.1f));
            Material gangway = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/HQPlank.mat", new Color(0.42f, 0.33f, 0.22f));

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
                // The deck proper: where a passenger must stand for the ship to move.
                Trigger(ShipParts.SafeDeckVolumeName, root.transform, new Vector3(0f, 2f, 0f), new Vector3(DeckWidth - 0.4f, 4f, DeckLength - 0.4f));
                // The straight way out: the bow direction, away from the dock at the stern.
                GameObject direction = new(ShipParts.DepartureDirectionName);
                direction.transform.SetParent(root.transform, false);
                direction.transform.localRotation = Quaternion.identity;
                // The gangway: hinged at the stern edge, lying toward the dock when down,
                // swung up by ShipDepartureVisual when the ship moves.
                GameObject pivot = new(ShipParts.GangwayPivotName);
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 0f, -DeckLength / 2f);
                Block(ShipParts.GangwayName, pivot.transform, new Vector3(0f, -0.05f, -GangwayLength / 2f), new Vector3(GangwayWidth, 0.1f, GangwayLength), gangway);
                Trigger(ShipParts.GangwayExclusionVolumeName, pivot.transform, new Vector3(0f, 1.2f, -GangwayLength / 2f - 0.3f), new Vector3(GangwayWidth + 0.8f, 2.6f, GangwayLength + 0.6f));
                root.AddComponent<ShipDepartureVisual>();

                GameObject monitor = Block(ShipParts.MonitorName, root.transform, new Vector3(0f, 1.6f, DeckLength / 2f - 1.5f), new Vector3(1.6f, 1f, 0.1f), screen);
                monitor.AddComponent<ShipMonitor>();
                Block(ShipParts.MonitorButtonSite01Name, root.transform, new Vector3(-0.55f, 1.5f, DeckLength / 2f - 1.58f), new Vector3(0.45f, 0.3f, 0.08f), button)
                    .AddComponent<MonitorButton>().Configure(WorldId.Sea, "Site 01");
                Block(ShipParts.MonitorButtonHQName, root.transform, new Vector3(0f, 1.5f, DeckLength / 2f - 1.58f), new Vector3(0.45f, 0.3f, 0.08f), button)
                    .AddComponent<MonitorButton>().Configure(WorldId.HQ, "HQ");
                Block(ShipParts.MonitorButtonEndDayName, root.transform, new Vector3(0.55f, 1.5f, DeckLength / 2f - 1.58f), new Vector3(0.45f, 0.3f, 0.08f), button)
                    .AddComponent<MonitorButton>().ConfigureEndDay("End day");
                Label("MonitorLabel", monitor.transform, new Vector3(0f, 0.72f, 0f), "SITE 01      HQ      END DAY", 0.05f, facingBow: false);
                // The status line sits in front of the screen face (a sibling, so the
                // monitor's scale does not stretch the glyphs).
                Label(ShipParts.MonitorStatusName, root.transform, new Vector3(0f, 1.85f, DeckLength / 2f - 1.57f), string.Empty, 0.035f, facingBow: false);

                // The deck cabin is the same glass elevator as DiveSite01's car, docked
                // (docs/DESIGN.md: "the glass elevator") — built from the shared round-cabin
                // geometry rather than a plain box, visual shell only (see DeckCabinBuilder).
                DeckCabinBuilder.Build(root.transform, new Vector3(0f, 0f, -8f), deck, rail, glass, button);

                BuildStorageRoom(root.transform, rail, tape);

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

        // The storage room (Dan, 16 September 2026: "a small box, like a small
        // room, in which we put our loot"): a walled box at the stern on the
        // starboard side, clear of the boarding path down the middle, with a
        // doorway toward the centre line and the readout over it. StorageArea is
        // its taped floor; StorageVolume its inside (what the pay button sells).
        private static void BuildStorageRoom(Transform root, Material wall, Material tape)
        {
            const float cx = 3.2f, cz = -12.5f;     // centre on the deck
            const float w = 2.8f, d = 3.0f, h = 2.2f; // outer width (x), depth (z), height
            const float t = 0.1f;                    // wall thickness
            const float door = 1.4f;                 // doorway width along z, in the port wall; full height (Dan: "I want to go inside")
            Block(ShipParts.StorageAreaName, root, new Vector3(cx, 0.02f, cz), new Vector3(w, 0.04f, d), tape);
            Block("StorageWallStarboard", root, new Vector3(cx + w / 2f - t / 2f, h / 2f, cz), new Vector3(t, h, d), wall);
            Block("StorageWallStern", root, new Vector3(cx, h / 2f, cz - d / 2f + t / 2f), new Vector3(w, h, t), wall);
            Block("StorageWallBow", root, new Vector3(cx, h / 2f, cz + d / 2f - t / 2f), new Vector3(w, h, t), wall);
            Block("StorageRoof", root, new Vector3(cx, h - t / 2f, cz), new Vector3(w, t, d), wall);
            float side = (d - door) / 2f;            // the port wall's two pieces either side of the doorway
            Block("StorageWallPortStern", root, new Vector3(cx - w / 2f + t / 2f, h / 2f, cz - d / 2f + side / 2f), new Vector3(t, h, side), wall);
            Block("StorageWallPortBow", root, new Vector3(cx - w / 2f + t / 2f, h / 2f, cz + d / 2f - side / 2f), new Vector3(t, h, side), wall);
            // The inside, reaching under the deck: a flat item's pivot lies a few
            // centimetres up, right at a floor-level bottom (Dan: "an item stays after the sell").
            Trigger(ShipParts.StorageVolumeName, root, new Vector3(cx, h / 2f - 0.25f, cz), new Vector3(w - 2f * t, h + 0.5f - t, d - 2f * t));
            // The readout on the outside of the port wall, over the doorway, facing port.
            GameObject go = new(ShipParts.StorageReadoutName, typeof(TextMesh));
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(cx - w / 2f - 0.02f, h + 0.35f, cz);
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // text faces -x (port)
            TextMesh mesh = go.GetComponent<TextMesh>();
            mesh.text = "STORAGE\n$0 / $0";
            mesh.characterSize = 0.05f;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.95f, 0.85f, 0.4f);
            root.gameObject.AddComponent<StorageReadout>();
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
            // Additive scenes share directional lights: the sea's sun must not reach the
            // seafloor's own layer while the host has both worlds loaded.
            int deep = LayerMask.NameToLayer(SunkCost.Sites.DiveSiteBuilder.DeepLayerName);
            if (deep >= 0) light.cullingMask &= ~(1 << deep);

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
