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
    // The ship prefab and the ShipAtSea scene. This builds what the rules read, by
    // the part names of docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.4: the
    // volumes, the well, the glass cabin, the monitor and its buttons, the storage
    // room's colliders, the TV, the spawn and Unstuck points. ShipDeckDressing then
    // stands Dan's generated models over it (23 September 2026); only what a model
    // does not replace is built here to be seen.
    public static class ShipStubBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/World/Ship.prefab";
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/WorldLoopSettings.asset";
        // A big ship (Dan's picture, 19 September 2026): the bridge tower at the
        // stern, under the HQ's bridge at the mooring. The ship's stair came off on
        // 23 September 2026; how the crew gets from the HQ down to the deck is a
        // later card (docs/ROADMAP.md).
        // Grown from 44 x 16 m on 23 September 2026 (Dan: "more space for the
        // furniture, for the tv screen with couch"); the hull model is built to match.
        public const float DeckLength = 48f;
        public const float DeckWidth = 20f;
        public const float DeckThickness = 0.5f;
        public const float HullDepth = 7.0f;    // under the deck to a metre into the water at the HQ mooring
        // The sea at sea, below the deck: the ship rides 4.5 m out of the water (Dan,
        // 23 September 2026: "make the ship move above the water"). The dive site's
        // water, the car's fill line and the submersion fallback carry the same number
        // (DiveSiteSettings.seaLevelY, PlayerSubmersion.SeaLevelFallback).
        public const float SeaLevelY = -4.5f;
        // The HQ's bridge level over the deck: HQPlatformBuilder moors the deck this far
        // under the landing. The tower model's own roof is lower (about 3.7 m), so from
        // the landing it is a 2.3 m drop onto it for now; the way aboard is Dan's later
        // card (docs/ROADMAP.md).
        public const float TowerHeight = 6f;
        public const float TowerWidth = 8f, TowerDepth = 6f;
        public const float VolumeHeight = 9f;   // the aboard and safe-deck volumes reach over the tower's roof
        public const float WellRadius = 3.7f, PedestalRadius = 2.55f; // the gap round the elevator: a shaft open to the sea (Dan, 19 September 2026), wide enough to read as one
        public const float WellDepth = -SeaLevelY + 0.5f; // the shaft's wall and the pedestal end half a metre under the water
        // The low rail round the well, on the deck just outside its edge (before Dan's
        // round housing, 28 September 2026); kept for readers of the old ring.
        public const float RingRadius = 4.2f;
        // The round housing round the car (ElevatorCar's CabinHousing model): its wall
        // stands on the deck's cut (inner face 3.739), its walkway reaches 5.007.
        public const float HousingOuterRadius = 5.01f;
        // The unseen 5 m ring walls round the well, inside the housing's wall (3.74 to
        // 3.87), open only across the grate's lane.
        public const float RailWallRadius = 3.95f;
        // What ShipDeckDressing keeps its props out of round the well: the housing and a
        // walk round it (its pipes reach 4.93).
        public const float WellKeepOutRadius = 5.75f;
        // The pedestal's top a little under the car's floor (ship y 0): the car model's
        // floor cap lies exactly on the deck plane and would fight it.
        public const float PedestalTopY = -0.03f;
        // Where Unstuck puts a player on this ship (ShipParts.BoardingPoint): open deck
        // aft of the well, port of the centre line, clear of every prop. It stood inside
        // the container once the models came (ship audit SHIP-001);
        // WorldSceneChecks.CheckShip now fits a player there.
        public static readonly Vector3 UnstuckPoint = new(-1.5f, 0f, -8f);
        // The four spawn points, facing the cabin; ShipDeckDressing keeps them and the
        // way from each to the cabin clear of props, so this is their one source.
        public static readonly Vector3[] SpawnPositions = { new(-3.5f, 0f, 7f), new(3.5f, 0f, 7f), new(-3.5f, 0f, 10f), new(3.5f, 0f, 10f) };

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
            Material screen = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipScreen.mat", new Color(0.020f, 0.034f, 0.044f)); // the idle glass in the ship screens' palette (fix-ui); the live picture sets its own
            // A screen gives its own light: unlit, so the deck's sun, ambient and sky
            // reflections never lift the diver's picture. Lit and half glossy, it showed
            // the dark below brighter than the diver saw it (Dan, 23 September 2026:
            // "in the TV you can see what down you cant").
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null && screen.shader != unlit) { screen.shader = unlit; EditorUtility.SetDirty(screen); }
            Material button = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipButton.mat", new Color(0.9f, 0.75f, 0.2f));
            Material tape = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipTape.mat", new Color(0.85f, 0.65f, 0.1f));

            GameObject root = new(ShipParts.RootName);
            try
            {
                root.AddComponent<ShipParts>();
                // Deck top at y = 0. The deck itself is the hull model's, cut round the well
                // (ShipDeckDressing): what is built here is the well under it and the rail round it.
                BuildDeck(root.transform);
                // The ship's side is the hull model's own 1.2 m bulwark and the unseen
                // guard over it (ShipDeckDressing.BuildBulwark; Dan, 23 September 2026:
                // "not real walls - the ship itself"). The hull and the tower are models.
                BuildTower(root.transform);

                Trigger(ShipParts.AboardVolumeName, root.transform, new Vector3(0f, (VolumeHeight - 1f) / 2f, 0f), new Vector3(DeckWidth + 2f, VolumeHeight + 1f, DeckLength + 2f));
                // The deck proper (the tower's roof over it, the well under it): where a passenger must stand for the ship to move.
                Trigger(ShipParts.SafeDeckVolumeName, root.transform, new Vector3(0f, (VolumeHeight - 1f) / 2f, 0f), new Vector3(DeckWidth - 0.4f, VolumeHeight + 1f, DeckLength - 0.4f));
                // The straight way out: the bow direction, away from the dock at the stern.
                GameObject direction = new(ShipParts.DepartureDirectionName);
                direction.transform.SetParent(root.transform, false);
                direction.transform.localRotation = Quaternion.identity;
                // No gangway on the ship (Dan, 18 September 2026): the HQ's own bridge reaches
                // over the stern; the ship carries nothing that reaches the base.
                root.AddComponent<ShipDepartureVisual>();

                // The deck cabin is the same glass elevator as DiveSite01's car, docked
                // (docs/DESIGN.md: "the glass elevator") — built from the shared round-cabin
                // geometry rather than a plain box, visual shell only (see DeckCabinBuilder).
                // The tube (Dan's picture, 19 September 2026): teal glass in an ink frame, a
                // dark cap ring with a glowing band and the beacon on top, a hazard band at
                // the foot, lit inside. The same glass as the car and the shaft, tinted.
                glass.SetColor("_BaseColor", new Color(0.6f, 0.88f, 0.92f, 0.18f)); // clear: two layers (car and tube) must still read as glass from inside (Dan, 19 September 2026)
                if (glass.HasProperty("_Color")) glass.SetColor("_Color", new Color(0.6f, 0.88f, 0.92f, 0.18f));
                EditorUtility.SetDirty(glass);
                // The floor in the kit's worn steel, tiled over the primitive disc's 5 m cap (SHIP-060).
                Material cabinFloor = SunkCost.Editor.Look.ShipKitMaterials.Tiled(SunkCost.Editor.Look.ShipKitMaterials.Steel(), new Vector2(5f, 5f));
                // Dead centre, like the picture; its root a floor's thickness under the deck,
                // so the car's floor is flush with the deck (Dan, 28 September 2026).
                GameObject cabin = DeckCabinBuilder.Build(root.transform, new Vector3(0f, -DeckCabinBuilder.FloorThicknessMeters, 0f), cabinFloor, SunkCost.Editor.Look.LookMaterials.Ink(), glass, button);
                DressCabin(cabin.transform);

                BuildStorageRoom(root.transform, SunkCost.Editor.Look.LookMaterials.PanelDark(), tape);
                BuildTv(root.transform, screen);
                // The models over everything built above: last, so every box that a
                // model now covers (the storage room's walls) is there to hide.
                SunkCost.Editor.Look.ShipDeckDressing.Build(root.transform);
                // The storage room's racks, drop zone and lamp: after the dressing, whose
                // collider pass would strip their colliders.
                SunkCost.Editor.Look.ShipStorageInterior.Build(root.transform);
                // The ship's sound at sea (ambience, and the engine ShipDepartureVisual plays).
                SunkCost.Editor.Look.ShipAmbienceSetup.Build(root.transform);
                // The screens' displays over the dressed stations (the monitor's, the TV's
                // idle picture, the storage room's inside readout): after the dressing has
                // moved the screens onto their models. Later hooks come after this.
                SunkCost.Editor.Look.ShipScreens.Build(root.transform);

                Vector3[] spawns = SpawnPositions;
                for (int i = 0; i < spawns.Length; i++)
                {
                    GameObject point = new(ShipParts.SpawnPointPrefix + (i + 1));
                    point.transform.SetParent(root.transform, false);
                    point.transform.localPosition = spawns[i];
                    point.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the cabin
                }
                // The boarding point: where Unstuck puts you, on open deck.
                GameObject boarding = new(ShipParts.BoardingPointName);
                boarding.transform.SetParent(root.transform, false);
                boarding.transform.localPosition = UnstuckPoint;
                // Every part into its area (SHIP-076): last, after every step that finds parts on the root.
                SunkCost.Editor.Look.ShipHierarchy.Group(root.transform);

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
        // Grown by half on 23 September 2026 (Dan: "it is smaller than a player now")
        // and moved a step to starboard so the doorway keeps its place by the centre
        // line. The generated room model is fitted to these numbers (ShipDeckDressing).
        public const float StorageCentreX = 3.9f, StorageCentreZ = -12.5f;
        public const float StorageWidth = 4.2f, StorageDepth = 4.5f, StorageHeight = 3.3f, StorageDoor = 1.8f; // the doorway the model's jambs leave (measured 1.79 m, z -13.40 to -11.61)
        public const float StorageLintel = 2.44f; // the underside of the model's lintel over it (measured 2.43 to 2.48 m)

        private static void BuildStorageRoom(Transform root, Material wall, Material tape)
        {
            const float cx = StorageCentreX, cz = StorageCentreZ; // centre on the deck
            const float w = StorageWidth, d = StorageDepth, h = StorageHeight; // outer width (x), depth (z), height
            // The walls and roof as thick as the generated model's, measured (30 cm and
            // 60 cm): a thinner collider let the camera stand inside the model's wall
            // (Dan, 23 September 2026, inside the room).
            const float t = 0.32f;                   // wall thickness
            const float roofT = 0.6f;                // roof thickness
            const float door = StorageDoor;          // doorway width along z, in the port wall (Dan: "I want to go inside")
            Block(ShipParts.StorageAreaName, root, new Vector3(cx, 0.02f, cz), new Vector3(w, 0.04f, d), tape);
            Block("StorageWallStarboard", root, new Vector3(cx + w / 2f - t / 2f, h / 2f, cz), new Vector3(t, h, d), wall);
            Block("StorageWallStern", root, new Vector3(cx, h / 2f, cz - d / 2f + t / 2f), new Vector3(w, h, t), wall);
            Block("StorageWallBow", root, new Vector3(cx, h / 2f, cz + d / 2f - t / 2f), new Vector3(w, h, t), wall);
            Block("StorageRoof", root, new Vector3(cx, h - roofT / 2f, cz), new Vector3(w, roofT, d), wall);
            float side = (d - door) / 2f;            // the port wall's two pieces either side of the doorway
            Block("StorageWallPortStern", root, new Vector3(cx - w / 2f + t / 2f, h / 2f, cz - d / 2f + side / 2f), new Vector3(t, h, side), wall);
            Block("StorageWallPortBow", root, new Vector3(cx - w / 2f + t / 2f, h / 2f, cz + d / 2f - side / 2f), new Vector3(t, h, side), wall);
            // Over the doorway, down to the model's lintel: the roof's collider stopped
            // 26 cm higher, so a head went into the lintel the crew can see.
            float roofBottom = h - roofT;
            Block("StorageLintel", root, new Vector3(cx - w / 2f + t / 2f, (StorageLintel + roofBottom) / 2f, cz), new Vector3(t, roofBottom - StorageLintel, door), wall);
            // A low sill across the doorway: a dropped coin is a cylinder and rolled
            // out of the room (full run, 16 September 2026); a player steps over it
            // (standing step offset 0.25 m).
            const float sill = 0.12f;
            Block("StorageSill", root, new Vector3(cx - w / 2f + t / 2f, sill / 2f, cz), new Vector3(t + 0.06f, sill, door), tape);
            // The inside, reaching under the deck: a flat item's pivot lies a few
            // centimetres up, right at a floor-level bottom (Dan: "an item stays after the sell").
            Trigger(ShipParts.StorageVolumeName, root, new Vector3(cx, (h - roofT) / 2f - 0.25f, cz), new Vector3(w - 2f * t, h - roofT + 0.5f, d - 2f * t));
            // The readout on a plate on the port wall's face, on the doorway's header,
            // read from the deck's centre line (not floating over the roof: Dan).
            TextMesh mesh = SunkCost.Editor.Look.PropBuilder.SignPlate(root.gameObject, "Storage Sign", ShipParts.StorageReadoutName, new Vector3(cx - w / 2f - 0.06f, h - 0.4f, cz), Quaternion.Euler(0f, -90f, 0f), 1.8f, 0.6f, 0.16f, new Color(0.95f, 0.85f, 0.4f));
            mesh.text = "STORAGE\n$0 / $0";
            root.gameObject.AddComponent<StorageReadout>();
        }

        // The deck TV (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 3): a quad ShipTV
        // renders the channel diver's view onto, the caption over it and the speaker
        // point on it. E on the screen is the next channel. It stands at the bow, facing
        // aft, the lounge in front of it (Dan, 23 September 2026: "move the tv and couch
        // to the front of the ship"); ShipDeckDressing fits the quad to the TV cabinet
        // model's own screen panel, so the numbers here are only where it starts.
        private static void BuildTv(Transform root, Material screenMaterial)
        {
            const float x = 0f, y = 2.6f, z = DeckLength / 2f - 5f;
            // A quad is seen from its -Z side, which already looks aft down the deck.
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = ShipParts.TvScreenName;
            screen.transform.SetParent(root, false);
            screen.transform.localPosition = new Vector3(x, y, z);
            screen.transform.localRotation = Quaternion.identity;
            screen.transform.localScale = new Vector3(5.2f, 2.925f, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = screenMaterial;
            Object.DestroyImmediate(screen.GetComponent<MeshCollider>());
            BoxCollider box = screen.AddComponent<BoxCollider>(); // what E targets; a quad's own collider has no thickness
            box.size = new Vector3(1f, 1f, 0.05f);
            // The caption over the screen, read by someone on the deck looking forward
            // (along +Z): a TextMesh reads along its +Z, so it is turned to face aft.
            TextMesh mesh = SunkCost.Editor.Look.PropBuilder.SignPlate(root.gameObject, "Tv Caption Sign", ShipParts.TvCaptionName, new Vector3(x, y + 1.8f, z + 0.02f), Quaternion.Euler(0f, 180f, 0f), 5.2f, 0.6f, 0.3f, new Color(1f, 0.35f, 0.3f));
            mesh.text = "NO SIGNAL";
            GameObject speaker = new(ShipParts.TvSpeakerName);
            speaker.transform.SetParent(root, false);
            speaker.transform.localPosition = new Vector3(x, y, z - 0.05f);
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
            sea.transform.position = origin + new Vector3(0f, SeaLevelY, 0f);
            sea.transform.localScale = new Vector3(200f, 1f, 200f); // a 2 km plane
            sea.GetComponent<Renderer>().sharedMaterial = water;
            sea.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; // a 2 km plane casting into the sun's shadow map; it still receives

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
            // The sky's own colour at the horizon (sampled from the default sky over this
            // sun): a grey fog turned the far sea into a flat band under a bluer sky
            // (ship audit SHIP-087).
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 350f;

            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            ship.transform.SetPositionAndRotation(origin, Quaternion.identity);
            WorldLookSetup.WriteIntoOpenScene(scene);

            if (!EditorSceneManager.SaveScene(scene, WorldScenes.SeaPath))
                throw new InvalidOperationException("Unity could not save " + WorldScenes.SeaPath);
            AssetDatabase.SaveAssets();
        }

        // The default procedural sky's colour a few degrees over the horizon under this
        // scene's Sun, sampled from a render (23 September 2026): the fog fades the far
        // sea into it. The band right on the horizon renders near white.
        public static readonly Color SkyHorizon = new(0.80f, 0.90f, 0.97f);

        // The tower at the stern is the tower model (ShipDeckDressing), its own shape
        // its collider. On its forward face the game builds only what is used: the
        // crew's screen. The navigation console beside it is the dressing's
        // (ConsoleBuilder.Place, 27 September 2026): the old monitor block, its three
        // buttons and its status line went with it.
        private static void BuildTower(Transform root)
        {
            float front = -DeckLength / 2f + TowerDepth;     // the tower's forward face
            // The crew's screen, starboard of the console.
            Visual("Crew Screen Frame", root, new Vector3(2.8f, 2.6f, front + 0.04f), Quaternion.identity, new Vector3(2.4f, 1.5f, 0.08f), SunkCost.Editor.Look.ShipKitMaterials.Tiled(SunkCost.Editor.Look.ShipKitMaterials.Bezel(), new Vector2(2.4f, 1.5f)));
            Visual("Crew Screen", root, new Vector3(2.8f, 2.6f, front + 0.09f), Quaternion.identity, new Vector3(2.2f, 1.3f, 0.02f), SunkCost.Editor.Look.LookMaterials.ScreenTeal());
            GameObject screenText = new("Crew Screen Text");
            screenText.transform.SetParent(root, false);
            screenText.transform.localPosition = new Vector3(2.8f, 2.6f, front + 0.11f);
            SunkCost.Editor.Look.PropBuilder.Text(screenText, "Text", Vector3.zero, 0.3f, new Color(0.05f, 0.14f, 0.22f), TextAnchor.MiddleCenter).AddComponent<SunkCost.Look.SignText>().Configure("ship.screen");
            // No stair down the tower any more (Dan, 23 September 2026: "I dont like
            // the stairs"): the way aboard from the HQ is a later card (docs/ROADMAP.md).
        }

        // The well: the hull model's deck is cut round it (ShipDeckDressing makes that
        // deck the floor), and under the cut a shaft open to the sea round the
        // elevator's pedestal (Dan, 19 September 2026: "a small gap that players could
        // fall to"). A low rail stands round it on the deck, open only at the grate to
        // the cabin's door (Dan, 23 September 2026): down in the shaft a player was
        // below the aboard volume, the ship could not sail, and there was no way up.
        private static void BuildDeck(Transform root)
        {
            // The ship's kit materials (fix-art, SHIP-060), not flat colour: MeshKit meshes
            // (UVs in metres) take them as they are, Unity's primitives a tiled twin.
            Material steel = SunkCost.Editor.Look.ShipKitMaterials.Steel();
            Material hazard = SunkCost.Editor.Look.ShipKitMaterials.Hazard();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink(); // the unseen colliders' only
            // No rim of our own: the hull model's deck already reaches the round hole,
            // corners and all (its cut is at 3.74 m); a second plate over it stood 1 cm
            // proud and showed a seam (ship audit SHIP-070).
            // The shaft: its wall a round band the physics knows as a ring of unseen
            // boxes; the pedestal the tube stands on. Both end just under the water.
            GameObject wellWall = new("Well Wall");
            wellWall.transform.SetParent(root, false);
            wellWall.transform.localPosition = new Vector3(0f, -WellDepth, 0f);
            wellWall.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(WellRadius + 0.04f, WellDepth, 0.08f, 0f, 360f, 64);
            wellWall.AddComponent<MeshRenderer>().sharedMaterial = steel;
            for (int i = 0; i < 24; i++) // the wall the physics knows: a ring of unseen boxes
            {
                float a = i * 15f, rad = a * Mathf.Deg2Rad;
                GameObject wall = Block("Well Wall Collider", root, new Vector3(Mathf.Cos(rad) * (WellRadius + 0.04f), -WellDepth / 2f, Mathf.Sin(rad) * (WellRadius + 0.04f)), new Vector3(0.08f, WellDepth, 0.9f), ink);
                wall.transform.localRotation = Quaternion.Euler(0f, -a, 0f);
                Unseen(wall);
            }
            GameObject pedestal = new("Pedestal");
            pedestal.transform.SetParent(root, false);
            pedestal.transform.localPosition = new Vector3(0f, -WellDepth, 0f);
            Mesh pedestalMesh = SunkCost.Editor.Look.MeshKit.Cylinder(PedestalRadius, WellDepth + PedestalTopY, 32);
            pedestal.AddComponent<MeshFilter>().sharedMesh = pedestalMesh;
            pedestal.AddComponent<MeshRenderer>().sharedMaterial = steel;
            pedestal.AddComponent<MeshCollider>().sharedMesh = pedestalMesh;
            // The pedestal's bands are open rings round it (as capped cylinders they were
            // discs buried in it).
            PedestalBand("Pedestal Band", root, -0.2f, 0.12f, 0.04f, hazard);
            // The rim of the hole marked, and the shaft's wall stepped in under the deck so the edge reads as an edge.
            GameObject rimBand = new("Well Rim Band");
            rimBand.transform.SetParent(root, false);
            rimBand.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            rimBand.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(WellRadius + 0.02f, 0.3f, 0.06f, 0f, 360f, 64);
            rimBand.AddComponent<MeshRenderer>().sharedMaterial = hazard;
            for (float ry = -1f; ry > -WellDepth; ry -= 1.5f) // rings down the shaft, so its depth reads
                PedestalBand("Pedestal Ring", root, ry - 0.08f, 0.16f, 0.06f, steel);
            // The grate across the well at the door (the doorway looks to +z), from under
            // the cabin's floor to just over the deck's cut edge, a centimetre proud of the
            // deck: flush with it, the two fought where they overlapped (ship audit SHIP-029).
            const float grateTop = 0.01f, grateHalf = 1.1f;
            float grateFrom = PedestalRadius - 0.15f, grateTo = WellRadius + 0.1f;
            float grateZ = (grateFrom + grateTo) / 2f, grateD = grateTo - grateFrom;
            Block("Well Grate", root, new Vector3(0f, grateTop - 0.03f, grateZ), new Vector3(grateHalf * 2f, 0.06f, grateD), SunkCost.Editor.Look.ShipKitMaterials.Tiled(steel, new Vector2(grateHalf * 2f, grateD)));
            Material slat = SunkCost.Editor.Look.ShipKitMaterials.Tiled(steel, new Vector2(0.05f, grateD - 0.1f));
            for (int i = 0; i < 5; i++)
                Visual("Grate Slat", root, new Vector3(-0.8f + i * 0.4f, grateTop + 0.01f, grateZ), Quaternion.identity, new Vector3(0.05f, 0.02f, grateD - 0.1f), slat);
            BuildWellRail(root, grateHalf + 0.05f);
            // A lamp just over the water at the shaft's foot, so its depth reads.
            Light lamp = new GameObject("Well Light").AddComponent<Light>();
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = new Vector3(0f, SeaLevelY + 1f, -WellRadius + 0.4f);
            lamp.lightmapBakeType = LightmapBakeType.Realtime; lamp.type = LightType.Point; lamp.range = 6f; lamp.intensity = 2.5f; lamp.color = new Color(1f, 0.7f, 0.4f); lamp.shadows = LightShadows.None;
        }

        // A band round the pedestal, `height` tall from `bottom`, standing `proud` off it.
        private static void PedestalBand(string name, Transform root, float bottom, float height, float proud, Material material)
        {
            GameObject band = new(name);
            band.transform.SetParent(root, false);
            band.transform.localPosition = new Vector3(0f, bottom, 0f);
            band.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(PedestalRadius + proud / 2f, height, proud, 0f, 360f, 64);
            band.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // The rail round the well: the Meshy railing (0.7 m high) as straight pieces
        // round a circle of RingRadius, open across the grate, its ends meeting the
        // grate's own side rails, which run from the pedestal to them. Behind every
        // piece an unseen wall of RailWallHeight: 1.2 m was over a 65 cm jump from the
        // deck (Dan, 23 September 2026), but the crane's round base stands 0.88 m high a
        // metre outside the rail, and a sprint jump off it cleared 1.2 m (QA, 24
        // September 2026). The props by the well stand higher still: the winch's top is
        // 2.12 m and the crane's highest standable part 3.98 m, and a 0.65 m jump from
        // there clears 4.63 m. 5 m is over all of it; the rail looks the same, and
        // nothing thrown goes over it.
        private const float RailWallHeight = 5f;
        private const float RailingPostSpan = 1.70f; // between its two end posts' centres (each 0.14 m wide, 0.07 m in from its ends)

        // Dan's round housing (28 September 2026) is the wall round the well now: the
        // ring's railing went with it, and its unseen walls stand inside the housing's
        // wall (RailWallRadius), open only across the grate's lane, so they also close the
        // entrance's sides beside the grate. The grate's own sides keep a handrail, cut
        // where the housing's shutters pass (their plates and top track cross x ±1.15 at
        // z 2.80-2.97); their walls run on unbroken.
        private const float ShutterGapFromZ = 2.77f, ShutterGapToZ = 2.93f;
        private const float HandrailHeight = 0.9f;

        private static void BuildWellRail(Transform root, float grateRailX)
        {
            GameObject rail = new("Well Rail");
            rail.transform.SetParent(root, false);
            // The ring of walls, from one grate rail round to the other.
            float endX = grateRailX;
            float gapHalf = Mathf.Asin(endX / RailWallRadius) * Mathf.Rad2Deg;
            float from = 90f + gapHalf, span = 360f - 2f * gapHalf;
            int pieces = Mathf.CeilToInt(span / 15f); // short chords stay inside the housing's wall
            float step = span / pieces;
            for (int i = 0; i < pieces; i++)
                RailPiece(rail.transform, null, "Well Rail Piece", Around(from + i * step), Around(from + (i + 1) * step));
            // Where those walls cross the housing's entrance (it is wider than the grate's
            // lane) a handrail shows them, from the grate rail's end to the entrance's jamb.
            float jamb = HousingEntranceHalfDeg;
            foreach (float side in new[] { -1f, 1f })
                Handrail(rail.transform, "Entrance Rail", Around(90f - side * gapHalf), Around(90f - side * jamb));
            // The grate's sides, from the pedestal's edge out to the ring's ends; their walls
            // reach on in to the cabin's own wall, so nothing slips out between the two.
            float innerZ = Mathf.Sqrt(PedestalRadius * PedestalRadius - grateRailX * grateRailX);
            float ringEndZ = Mathf.Sqrt(RailWallRadius * RailWallRadius - endX * endX);
            foreach (float side in new[] { -1f, 1f })
            {
                RailPiece(rail.transform, null, "Grate Rail", new Vector3(side * grateRailX, 0f, innerZ), new Vector3(side * grateRailX, 0f, ringEndZ), 0.2f);
                Handrail(rail.transform, "Grate Rail Inner", new Vector3(side * grateRailX, 0f, innerZ), new Vector3(side * grateRailX, 0f, ShutterGapFromZ));
                Handrail(rail.transform, "Grate Rail Outer", new Vector3(side * grateRailX, 0f, ShutterGapToZ), new Vector3(side * grateRailX, 0f, ringEndZ));
            }
        }

        // The housing's entrance: its jambs stand ±22.5° about the doorway (the model's cut).
        private const float HousingEntranceHalfDeg = 22.5f;

        private static Vector3 Around(float bearingDeg) => new Vector3(Mathf.Cos(bearingDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(bearingDeg * Mathf.Deg2Rad)) * RailWallRadius;

        // A short kit-steel handrail from a to b on the deck (a squashed railing model read
        // badly at these lengths): a post at each end, a top rail and a knee rail. Look only.
        private static void Handrail(Transform parent, string name, Vector3 a, Vector3 b)
        {
            Vector3 along = b - a;
            float length = along.magnitude;
            if (length < 0.1f) return;
            GameObject piece = new(name);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = (a + b) / 2f;
            piece.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(along.z, along.x) * Mathf.Rad2Deg, 0f); // its x runs from a to b
            Material steel = SunkCost.Editor.Look.ShipKitMaterials.Steel();
            const float post = 0.05f;
            float half = length / 2f - post / 2f;
            HandrailPart(piece.transform, "Post", SunkCost.Editor.Look.MeshKit.Box(new Vector3(post, HandrailHeight, post)), new Vector3(-half, 0f, 0f), steel);
            HandrailPart(piece.transform, "Post", SunkCost.Editor.Look.MeshKit.Box(new Vector3(post, HandrailHeight, post)), new Vector3(half, 0f, 0f), steel);
            HandrailPart(piece.transform, "Top Rail", SunkCost.Editor.Look.MeshKit.Box(new Vector3(length, 0.05f, 0.05f)), new Vector3(0f, HandrailHeight - 0.05f, 0f), steel);
            HandrailPart(piece.transform, "Knee Rail", SunkCost.Editor.Look.MeshKit.Box(new Vector3(length, 0.035f, 0.035f)), new Vector3(0f, HandrailHeight * 0.5f, 0f), steel);
        }

        private static void HandrailPart(Transform parent, string name, Mesh mesh, Vector3 localPosition, Material material)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // One straight piece of rail from a to b on the deck: its wall (reaching on past
        // a by wallPastA), and the railing stretched so its end posts stand on a and b.
        // The next piece's end post stands on the same spot: one post at every joint,
        // not two side by side (QA, 24 September 2026).
        private static void RailPiece(Transform parent, GameObject railing, string name, Vector3 a, Vector3 b, float wallPastA = 0f)
        {
            Vector3 along = b - a;
            float length = along.magnitude;
            GameObject piece = new(name);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = (a + b) / 2f;
            piece.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(along.z, along.x) * Mathf.Rad2Deg, 0f); // its x runs from a to b
            BoxCollider wall = piece.AddComponent<BoxCollider>();
            wall.center = new Vector3(-wallPastA / 2f, RailWallHeight / 2f, 0f);
            wall.size = new Vector3(length + 0.1f + wallPastA, RailWallHeight, 0.2f); // a little long, so the corners between pieces close
            if (railing == null) return;
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(railing);
            model.name = "Railing";
            model.transform.SetParent(piece.transform, false);
            model.transform.localScale = new Vector3(length / RailingPostSpan, 1f, 1f);
        }

        // The deck's side of Dan's round elevator (28 September 2026): the round housing
        // model round the car, its base on the deck and its wall on the deck's cut, its
        // entrance at the car's doorway; a top track the shutters hang from; a lid under
        // its rim with the old beacon on it (the model is open on top); the car's own
        // light, which goes with the car. Numbers: docs/ELEVATOR_LOOK.md §2.
        public const string HousingName = "Cabin Housing";
        private const float HousingBaseY = 0.01f;               // ship y: the walkway a centimetre over the deck
        private const float LidRadius = 3.20f, LidBottom = 3.71f, LidThickness = 0.05f; // housing-local: tucked under the rim (inner r 3.22-3.26 there)
        private const float TrackInner = 3.03f, TrackOuter = 3.18f, TrackHalfDeg = 50f;
        private const float TrackBottom = 3.20f, TrackTop = 3.42f;  // cabin-local: over the shutters' tops, under the rim's lip
        private const float CarLightY = 3.12f;                     // cabin-local: in the car, under its roof

        private static void DressCabin(Transform cabin)
        {
            Material steel = SunkCost.Editor.Look.ShipKitMaterials.Steel();
            // The housing, look only; its own mesh is its collider (the walls, the walkway,
            // the jambs), so the camera stops at the surface it sees.
            Vector3 housingLocal = new(0f, HousingBaseY + DeckCabinBuilder.FloorThicknessMeters, 0f); // the cabin root is a floor under the deck
            GameObject housing = SunkCost.Editor.Look.ElevatorLook.PlacePart("CabinHousing", cabin, HousingName, housingLocal, 0f);
            if (housing != null)
                foreach (MeshFilter mf in housing.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            // The shutters' top track: a dark steel channel over the entrance and the two
            // shutters' parked spans, under the rim's lip.
            GameObject track = new("Shutter Track");
            track.transform.SetParent(cabin, false);
            track.transform.localPosition = new Vector3(0f, TrackBottom, 0f);
            track.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band((TrackInner + TrackOuter) / 2f, TrackTop - TrackBottom, TrackOuter - TrackInner, 90f - TrackHalfDeg, 90f + TrackHalfDeg, 48);
            track.AddComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.LookMaterials.Ink();

            // The lid under the housing's rim, and the beacon on it (the old tube's cap).
            float lidY = housingLocal.y + LidBottom;
            GameObject lid = new("Housing Lid");
            lid.transform.SetParent(cabin, false);
            lid.transform.localPosition = new Vector3(0f, lidY, 0f);
            lid.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Cylinder(LidRadius, LidThickness, 48);
            lid.AddComponent<MeshRenderer>().sharedMaterial = steel;
            SunkCost.Editor.Look.PropBuilder.Place(cabin.gameObject, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(0f, lidY + LidThickness, 0f)).name = "Tube Beacon";

            // The car's own light, warm white, the same in both worlds (Dan, 23 September
            // 2026): with the car's glass, so it goes dark with the car away; the car
            // model's ring light follows its colour.
            Transform carGlass = cabin.Find(ShipParts.DeckCabinCarGlassName);
            Light inside = new GameObject("Tube Light").AddComponent<Light>();
            inside.transform.SetParent(carGlass != null ? carGlass : cabin, false);
            inside.transform.localPosition = new Vector3(0f, CarLightY, 0f);
            DeckCabinRideSetup.ConfigureCarLight(inside);
            Transform carLook = carGlass != null ? carGlass.Find(SunkCost.Editor.Look.ElevatorLook.CarLookName) : null;
            if (carLook != null) SunkCost.Editor.Look.ElevatorLook.SetRingSource(carLook.gameObject, inside);
        }

        // A physics wall nobody sees: the look kit's railing stands along it.
        private static void Unseen(GameObject block)
        {
            Object.DestroyImmediate(block.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(block.GetComponent<MeshFilter>());
        }

        private static void Visual(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 scale, Material material, bool cylinder = false)
        {
            GameObject block = cylinder ? GameObject.CreatePrimitive(PrimitiveType.Cylinder) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localRotation = localRotation;
            block.transform.localScale = cylinder ? new Vector3(scale.x, scale.y / 2f, scale.z) : scale; // a primitive cylinder is 2 units tall
            block.GetComponent<Renderer>().sharedMaterial = material;
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
            go.AddComponent<SunkCost.Look.DepthText>().Configure(SunkCost.Editor.Look.LookMaterials.DepthText()); // never through a wall
        }
    }
}
