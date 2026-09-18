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
        public const float HullDepth = 10.5f;   // under the deck to a metre into the water at the HQ mooring (a big ship, Dan, 19 September 2026)
        public const float BowLength = 5f;      // the foredeck past the bow rail, under the wheelhouse; the prow is beyond it

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

            GameObject root = new(ShipParts.RootName);
            try
            {
                root.AddComponent<ShipParts>();
                // Deck top at y = 0 so the HQ plank and the HQ floor meet it flush.
                Block("Deck", root.transform, new Vector3(0f, -DeckThickness / 2f, 0f), new Vector3(DeckWidth, DeckThickness, DeckLength), SunkCost.Editor.Look.LookMaterials.RustPanel());
                BuildHull(root.transform);
                // The rails are the walls the physics needs; what you see of them is the
                // look kit's railing placed along them (BuildLook), so these are unseen.
                Unseen(Block("RailPort", root.transform, new Vector3(-DeckWidth / 2f + 0.1f, 0.5f, 0f), new Vector3(0.2f, 1f, DeckLength), rail));
                Unseen(Block("RailStarboard", root.transform, new Vector3(DeckWidth / 2f - 0.1f, 0.5f, 0f), new Vector3(0.2f, 1f, DeckLength), rail));
                Unseen(Block("RailBow", root.transform, new Vector3(0f, 0.5f, DeckLength / 2f - 0.1f), new Vector3(DeckWidth, 1f, 0.2f), rail));
                // The stern rail leaves the boarding gap open: BoardingPoint is where the HQ's stair meets the deck.
                Unseen(Block("RailSternPort", root.transform, new Vector3(-3f, 0.5f, -DeckLength / 2f + 0.1f), new Vector3(4f, 1f, 0.2f), rail));
                Unseen(Block("RailSternStarboard", root.transform, new Vector3(3f, 0.5f, -DeckLength / 2f + 0.1f), new Vector3(4f, 1f, 0.2f), rail));
                BuildLook(root.transform);

                Trigger(ShipParts.AboardVolumeName, root.transform, new Vector3(0f, 2f, 0f), new Vector3(DeckWidth + 2f, 4f, DeckLength + 2f));
                // The deck proper: where a passenger must stand for the ship to move.
                Trigger(ShipParts.SafeDeckVolumeName, root.transform, new Vector3(0f, 2f, 0f), new Vector3(DeckWidth - 0.4f, 4f, DeckLength - 0.4f));
                // The straight way out: the bow direction, away from the dock at the stern.
                GameObject direction = new(ShipParts.DepartureDirectionName);
                direction.transform.SetParent(root.transform, false);
                direction.transform.localRotation = Quaternion.identity;
                // No gangway on the ship (Dan, 18 September 2026): the HQ's own bridge and
                // stair come down to the stern; the ship carries nothing that reaches the base.
                root.AddComponent<ShipDepartureVisual>();

                // The monitor on its console at the bow, the wheelhouse behind it.
                Visual("Monitor Frame", root.transform, new Vector3(0f, 1.6f, DeckLength / 2f - 1.44f), Quaternion.identity, new Vector3(1.8f, 1.2f, 0.1f), SunkCost.Editor.Look.LookMaterials.Ink());
                Visual("Monitor Console", root.transform, new Vector3(0f, 0.5f, DeckLength / 2f - 1.4f), Quaternion.identity, new Vector3(1.8f, 1.0f, 0.5f), SunkCost.Editor.Look.LookMaterials.PanelDark());
                Visual("Monitor Console Stripe", root.transform, new Vector3(0f, 0.16f, DeckLength / 2f - 1.66f), Quaternion.identity, new Vector3(1.8f, 0.26f, 0.02f), SunkCost.Editor.Look.LookMaterials.Hazard());
                GameObject monitor = Block(ShipParts.MonitorName, root.transform, new Vector3(0f, 1.6f, DeckLength / 2f - 1.5f), new Vector3(1.6f, 1f, 0.1f), SunkCost.Editor.Look.LookMaterials.ScreenTeal());
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

                BuildStorageRoom(root.transform, SunkCost.Editor.Look.LookMaterials.PanelDark(), tape);
                BuildTv(root.transform, SunkCost.Editor.Look.LookMaterials.Ink(), screen);

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

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            // The cabin's glass and the car's own glass are part of the ship: a
            // regenerated prefab used to lose them until someone remembered
            // DeckCabinRideSetup (the "ship regeneration trap", 16 September 2026).
            foreach (string change in DeckCabinRideSetup.PatchShipPrefabGlass()) Debug.Log("Ship stub: " + change);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
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
            // A low sill across the doorway: a dropped coin is a cylinder and rolled
            // out of the room (full run, 16 September 2026); a player steps over it
            // (standing step offset 0.25 m).
            const float sill = 0.12f;
            Block("StorageSill", root, new Vector3(cx - w / 2f + t / 2f, sill / 2f, cz), new Vector3(t + 0.06f, sill, door), tape);
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

        // The deck TV (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 3): a 2 m × 1.2 m
        // screen against the port rail, midships, facing the deck — a quad ShipTV
        // renders the channel diver's view onto, in a frame on a post, the caption
        // over it and the speaker point on it. E on the screen is the next channel.
        private static void BuildTv(Transform root, Material frame, Material screenMaterial)
        {
            const float x = -4.55f, y = 1.7f, z = -3f;
            Block("TvPost", root, new Vector3(x - 0.1f, 0.5f, z), new Vector3(0.12f, 1.0f, 0.12f), frame);
            Block("TvFrame", root, new Vector3(x - 0.08f, y, z), new Vector3(0.1f, 1.4f, 2.2f), frame);
            // A quad is seen from its -Z side; turned so that side faces +X (the deck).
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = ShipParts.TvScreenName;
            screen.transform.SetParent(root, false);
            screen.transform.localPosition = new Vector3(x, y, z);
            screen.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            screen.transform.localScale = new Vector3(2f, 1.2f, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = screenMaterial;
            Object.DestroyImmediate(screen.GetComponent<MeshCollider>());
            BoxCollider box = screen.AddComponent<BoxCollider>(); // what E targets; a quad's own collider has no thickness
            box.size = new Vector3(1f, 1f, 0.05f);
            // The caption over the screen, read by someone on the deck looking toward
            // port (along -X): a TextMesh reads along its +Z, so it is turned the same way.
            GameObject caption = new(ShipParts.TvCaptionName, typeof(TextMesh));
            caption.transform.SetParent(root, false);
            caption.transform.localPosition = new Vector3(x + 0.02f, y + 0.78f, z);
            caption.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            TextMesh mesh = caption.GetComponent<TextMesh>();
            mesh.text = "NO SIGNAL";
            mesh.characterSize = 0.05f;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(1f, 0.35f, 0.3f);
            GameObject speaker = new(ShipParts.TvSpeakerName);
            speaker.transform.SetParent(root, false);
            speaker.transform.localPosition = new Vector3(x + 0.05f, y, z);
            root.gameObject.AddComponent<ShipTV>();
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
            WorldLookSetup.WriteIntoOpenScene(scene);

            if (!EditorSceneManager.SaveScene(scene, WorldScenes.SeaPath))
                throw new InvalidOperationException("Unity could not save " + WorldScenes.SeaPath);
            AssetDatabase.SaveAssets();
        }

        // The hull under the deck: rust plate sides, a navy band at the waterline, a
        // pointed bow. Visual only — no colliders, so the deck cabin's car passes
        // through it on the way down and nothing under the deck is walkable.
        private static void BuildHull(Transform root)
        {
            Material plate = SunkCost.Editor.Look.LookMaterials.RustPanel();
            Material band = SunkCost.Editor.Look.LookMaterials.PanelDark();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            Material hazard = SunkCost.Editor.Look.LookMaterials.Hazard();
            float y = -DeckThickness - HullDepth / 2f;
            float length = DeckLength + BowLength, cz = BowLength / 2f, prowZ = DeckLength / 2f + BowLength;
            Visual("Hull", root, new Vector3(0f, y, cz), Quaternion.identity, new Vector3(DeckWidth, HullDepth, length), plate);
            Visual("Hull Band", root, new Vector3(0f, -DeckThickness - HullDepth + 1.6f, cz), Quaternion.identity, new Vector3(DeckWidth + 0.06f, 1.2f, length + 0.06f), band);
            Visual("Hull Stripe", root, new Vector3(0f, -3.4f, cz), Quaternion.identity, new Vector3(DeckWidth + 0.04f, 0.5f, length + 0.04f), ink);
            Visual("Hull Rub Rail", root, new Vector3(0f, -DeckThickness - 0.6f, cz), Quaternion.identity, new Vector3(DeckWidth + 0.3f, 0.4f, length + 0.3f), ink);
            Visual("Hull Hazard", root, new Vector3(0f, -0.28f, cz), Quaternion.identity, new Vector3(DeckWidth + 0.04f, 0.3f, length + 0.04f), hazard);
            // The foredeck past the bow rail, then the prow: a square turned 45 degrees.
            Visual("Foredeck", root, new Vector3(0f, -DeckThickness / 2f, DeckLength / 2f + BowLength / 2f), Quaternion.identity, new Vector3(DeckWidth, DeckThickness, BowLength), plate);
            float side = DeckWidth / Mathf.Sqrt(2f);
            Quaternion turn = Quaternion.Euler(0f, 45f, 0f);
            Visual("Bow", root, new Vector3(0f, y, prowZ), turn, new Vector3(side, HullDepth, side), plate);
            Visual("Bow Band", root, new Vector3(0f, -DeckThickness - HullDepth + 1.6f, prowZ), turn, new Vector3(side + 0.04f, 1.2f, side + 0.04f), band);
            Visual("Bow Stripe", root, new Vector3(0f, -3.4f, prowZ), turn, new Vector3(side + 0.03f, 0.5f, side + 0.03f), ink);
            Visual("Bow Rub Rail", root, new Vector3(0f, -DeckThickness - 0.6f, prowZ), turn, new Vector3(side + 0.2f, 0.4f, side + 0.2f), ink);
            Visual("Bow Hazard", root, new Vector3(0f, -0.28f, prowZ), turn, new Vector3(side + 0.03f, 0.3f, side + 0.03f), hazard);
            Visual("Bow Deck", root, new Vector3(0f, -DeckThickness / 2f, prowZ), turn, new Vector3(side, DeckThickness, side), plate);
            // Tyres on the stern, where the HQ's stair foot meets it.
            foreach (float x in new[] { -3.2f, 0f, 3.2f })
            {
                GameObject tyre = new("Tyre");
                tyre.transform.SetParent(root, false);
                tyre.transform.localPosition = new Vector3(x, -1.4f, -DeckLength / 2f - 0.12f);
                tyre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tyre.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Ring(0.45f, 0.26f, 16, 8);
                tyre.AddComponent<MeshRenderer>().sharedMaterial = ink;
            }
        }

        // The look of the deck (the platform's kit, so the two read as one place):
        // deck plates over the deck, railings with lamps along the rails, the
        // wheelhouse on the foredeck with its mast and funnel, cargo on the deck
        // out of the boarding path, the company's name on the hull. Nothing here
        // is a marker the rules read; the cargo has the kit's colliders.
        private static void BuildLook(Transform root)
        {
            Material deckTile = SunkCost.Editor.Look.LookMaterials.DeckTile();
            Material hazard = SunkCost.Editor.Look.LookMaterials.Hazard();
            Material plate = SunkCost.Editor.Look.LookMaterials.RustPanel();
            Material dark = SunkCost.Editor.Look.LookMaterials.PanelDark();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            Material glow = SunkCost.Editor.Look.LookMaterials.WindowGlow();
            Material steel = SunkCost.Editor.Look.LookMaterials.RustSteel();
            GameObject look = new("Look");
            look.transform.SetParent(root, false);
            // Deck plates over the deck, the boarding path striped from the stern gap to the cabin.
            Skin(look, "Deck Plates", SunkCost.Editor.Look.MeshKit.Box(new Vector3(DeckWidth, 0.01f, DeckLength)), deckTile, new Vector3(0f, 0f, 0f), Quaternion.identity);
            foreach (float x in new[] { -1.1f, 1.1f })
                Skin(look, "Boarding Stripe", SunkCost.Editor.Look.MeshKit.Box(new Vector3(0.2f, 0.012f, 4f)), hazard, new Vector3(x, 0f, -DeckLength / 2f + 2f), Quaternion.identity);
            // Railings along the rails, a lamp every 4 m, staggered port and starboard.
            GameObject rail = SunkCost.Editor.Look.PropBuilder.Rail(), lamp = SunkCost.Editor.Look.PropBuilder.RailLamp();
            float rx = DeckWidth / 2f - 0.1f, rz = DeckLength / 2f - 0.1f;
            for (float z = -DeckLength / 2f + 1f; z < DeckLength / 2f; z += 2f)
                foreach (float side in new[] { -1f, 1f })
                    SunkCost.Editor.Look.PropBuilder.Place(look, rail, new Vector3(side * rx, 0f, z), Quaternion.Euler(0f, 90f, 0f)).name = "Rail";
            for (float z = -DeckLength / 2f + 2f; z < DeckLength / 2f; z += 4f)
            {
                SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(-rx, 0f, z)).name = "Rail Lamp";
                SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(rx, 0f, z + 2f)).name = "Rail Lamp";
            }
            foreach (float x in new[] { -4f, -2f, 0f, 2f, 4f })
                SunkCost.Editor.Look.PropBuilder.Place(look, rail, new Vector3(x, 0f, rz)).name = "Rail";
            foreach (float x in new[] { -4f, -2f, 2f, 4f })
                SunkCost.Editor.Look.PropBuilder.Place(look, rail, new Vector3(x, 0f, -rz)).name = "Rail";
            foreach (float x in new[] { -rx, rx })
                foreach (float z in new[] { -rz, rz })
                    SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(x, 0f, z)).name = "Rail Lamp";
            // The wheelhouse on the foredeck: rust plate, lit windows all round, the
            // door aft, an ink roof with the beacon mast, the antenna, the funnel and a lamp.
            float hz = DeckLength / 2f + 0.2f + 1.8f; // its centre
            Skin(look, "Wheelhouse", SunkCost.Editor.Look.MeshKit.Box(new Vector3(8f, 2.9f, 3.6f)), plate, new Vector3(0f, 0f, hz), Quaternion.identity);
            Skin(look, "Wheelhouse Base", SunkCost.Editor.Look.MeshKit.Box(new Vector3(8.04f, 0.26f, 3.64f)), hazard, new Vector3(0f, 0f, hz), Quaternion.identity);
            Skin(look, "Windows Aft", SunkCost.Editor.Look.MeshKit.Box(new Vector3(5.2f, 0.8f, 0.06f)), glow, new Vector3(0.9f, 1.7f, hz - 1.83f), Quaternion.identity);
            Skin(look, "Windows Fore", SunkCost.Editor.Look.MeshKit.Box(new Vector3(6.6f, 0.8f, 0.06f)), glow, new Vector3(0f, 1.7f, hz + 1.83f), Quaternion.identity);
            foreach (float x in new[] { -4.03f, 4.03f })
                Skin(look, "Windows Side", SunkCost.Editor.Look.MeshKit.Box(new Vector3(0.06f, 0.8f, 2.4f)), glow, new Vector3(x, 1.7f, hz), Quaternion.identity);
            Skin(look, "Door", SunkCost.Editor.Look.MeshKit.Box(new Vector3(0.9f, 2.0f, 0.06f)), ink, new Vector3(-2.8f, 0.13f, hz - 1.83f), Quaternion.identity);
            Skin(look, "Door Light", SunkCost.Editor.Look.MeshKit.Box(new Vector3(0.3f, 0.2f, 0.2f)), SunkCost.Editor.Look.LookMaterials.LampWarm(), new Vector3(-2.8f, 2.3f, hz - 1.9f), Quaternion.identity);
            Skin(look, "Roof", SunkCost.Editor.Look.MeshKit.Box(new Vector3(8.4f, 0.16f, 4.0f)), ink, new Vector3(0f, 2.9f, hz), Quaternion.identity);
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(0.8f, 3.06f, hz + 0.6f)).name = "Beacon";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Antenna(), new Vector3(3.0f, 3.06f, hz - 0.8f)).name = "Antenna";
            Skin(look, "Funnel", SunkCost.Editor.Look.MeshKit.Cylinder(0.55f, 2.4f, 14), steel, new Vector3(-2.4f, 3.06f, hz + 0.4f), Quaternion.identity);
            Skin(look, "Funnel Cap", SunkCost.Editor.Look.MeshKit.Cylinder(0.6f, 0.3f, 14), ink, new Vector3(-2.4f, 5.3f, hz + 0.4f), Quaternion.identity);
            Skin(look, "Funnel Band", SunkCost.Editor.Look.MeshKit.Cylinder(0.58f, 0.4f, 14), hazard, new Vector3(-2.4f, 4.3f, hz + 0.4f), Quaternion.identity);
            SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(-3.6f, 3.06f, hz - 1.5f)).name = "Roof Lamp";
            SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(3.6f, 3.06f, hz - 1.5f)).name = "Roof Lamp";
            // Cargo: a container along the port side forward, crates starboard forward,
            // barrels port aft; the spawn points, the cabin, the TV and the path stay clear.
            GameObject container = SunkCost.Editor.Look.PropBuilder.Container("Grey", 6f);
            SunkCost.Editor.Look.PropBuilder.Place(look, container, new Vector3(-3.2f, 0f, 9.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Container";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Red"), new Vector3(3.6f, 0f, 11.6f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Green"), new Vector3(3.6f, 0f, 9.9f), Quaternion.Euler(0f, 8f, 0f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Yellow"), new Vector3(3.6f, 1.2f, 11.6f), Quaternion.Euler(0f, -12f, 0f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.SmallCrate("Grey"), new Vector3(2.4f, 0f, 12.8f), Quaternion.Euler(0f, 20f, 0f)).name = "Small Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Barrel("Red"), new Vector3(-4.1f, 0f, -12.4f)).name = "Barrel";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Barrel("Rust"), new Vector3(-3.3f, 0f, -13.2f)).name = "Barrel";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Barrel("Red"), new Vector3(-4.1f, 0f, -11.5f)).name = "Barrel";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Vent(), new Vector3(3.6f, 0f, -7f), Quaternion.Euler(0f, -90f, 0f)).name = "Vent";
            foreach (float x in new[] { -4.3f, 4.3f })
                foreach (float z in new[] { -14.0f, 14.0f })
                    SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Bollard(), new Vector3(x, 0f, z)).name = "Bollard";
            // The company's name on both sides of the hull.
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject sign = SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.SignBoard(6f, 1.2f), new Vector3(side * (DeckWidth / 2f + 0.08f), -2.4f, -4f), Quaternion.Euler(0f, side * 90f, 0f));
                sign.name = "Hull Sign";
                foreach (SunkCost.Look.SignText text in sign.GetComponentsInChildren<SunkCost.Look.SignText>(true))
                    text.Configure(text.name == "Sub" ? "motto" : "company");
            }
        }

        private static void Skin(GameObject parent, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // A physics wall nobody sees: the look kit's railing stands along it.
        private static void Unseen(GameObject block)
        {
            Object.DestroyImmediate(block.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(block.GetComponent<MeshFilter>());
        }

        private static void Visual(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 scale, Material material)
        {
            GameObject block = Block(name, parent, localPosition, scale, material);
            block.transform.localRotation = localRotation;
            Object.DestroyImmediate(block.GetComponent<Collider>());
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
