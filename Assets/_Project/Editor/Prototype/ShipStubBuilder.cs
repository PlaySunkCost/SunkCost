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
        // The low rail round the well, on the deck just outside its edge; ShipDeckDressing
        // keeps its props clear of it.
        public const float RingRadius = 4.2f;
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
            Material screen = HQPrototypeBuilder.GetOrCreateMaterial(HQPrototypeBuilder.MaterialPath + "/ShipScreen.mat", new Color(0.08f, 0.16f, 0.22f));
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
                GameObject cabin = DeckCabinBuilder.Build(root.transform, Vector3.zero, SunkCost.Editor.Look.LookMaterials.PanelDark(), SunkCost.Editor.Look.LookMaterials.Ink(), glass, button); // dead centre, like the picture
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
        // crew's screen, and the sailing monitor with its three buttons and its status
        // line, which the dressing moves onto the console model.
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
            // The sailing monitor at the tower's foot, the buttons under it.
            float mz = front + 0.5f;
            GameObject monitor = Block(ShipParts.MonitorName, root, new Vector3(0f, 1.6f, mz), new Vector3(1.6f, 1f, 0.1f), SunkCost.Editor.Look.LookMaterials.ScreenTeal());
            monitor.AddComponent<ShipMonitor>();
            // Three red buttons with their words on them, in a row under the screen (Dan,
            // 19 September 2026), reading SITE 01 · HQ · END DAY from left to right (Dan,
            // 23 September 2026): whoever reads the console faces aft, so their left is +x.
            SunkCost.Editor.Look.PropBuilder.PushButton(root.gameObject, ShipParts.MonitorButtonSite01Name, new Vector3(0.55f, 1.12f, mz + 0.05f), Quaternion.identity, "button.site01", 0.5f)
                .AddComponent<MonitorButton>().Configure(WorldId.Sea, "Site 01");
            SunkCost.Editor.Look.PropBuilder.PushButton(root.gameObject, ShipParts.MonitorButtonHQName, new Vector3(0f, 1.12f, mz + 0.05f), Quaternion.identity, "button.hq", 0.5f)
                .AddComponent<MonitorButton>().Configure(WorldId.HQ, "HQ");
            SunkCost.Editor.Look.PropBuilder.PushButton(root.gameObject, ShipParts.MonitorButtonEndDayName, new Vector3(-0.55f, 1.12f, mz + 0.05f), Quaternion.identity, "button.endday", 0.5f)
                .AddComponent<MonitorButton>().ConfigureEndDay("End day");
            Label(ShipParts.MonitorStatusName, root, new Vector3(0f, 1.85f, mz + 0.07f), string.Empty, 0.035f, facingBow: true);
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
            Mesh pedestalMesh = SunkCost.Editor.Look.MeshKit.Cylinder(PedestalRadius, WellDepth, 32);
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
        // piece an unseen wall of RailWallHeight, over a 65 cm jump: it cannot be
        // climbed, and nothing thrown goes over it (Dan, 23 September 2026).
        private const float RailWallHeight = 1.2f;
        private const float RailingLength = 1.843f; // the railing model at scale 1

        private static void BuildWellRail(Transform root, float grateRailX)
        {
            GameObject railing = AssetDatabase.LoadAssetAtPath<GameObject>(SunkCost.Editor.Look.ShipModelSetup.PrefabPath("Railing"));
            if (railing == null) Debug.LogWarning("Ship stub: no Railing prefab (run the ship model setup); the well rail is its walls only");
            GameObject rail = new("Well Rail");
            rail.transform.SetParent(root, false);
            // The ring, from just outboard of one grate rail round to the other.
            float endX = grateRailX + 0.1f;
            float gapHalf = Mathf.Asin(endX / RingRadius) * Mathf.Rad2Deg;
            float from = 90f + gapHalf, span = 360f - 2f * gapHalf;
            int pieces = Mathf.CeilToInt(span / (2f * Mathf.Asin(RailingLength / 2f / RingRadius) * Mathf.Rad2Deg));
            float step = span / pieces;
            for (int i = 0; i < pieces; i++)
                RailPiece(rail.transform, railing, "Well Rail Piece", Around(from + i * step), Around(from + (i + 1) * step));
            // The grate's sides, from the pedestal's edge out to the ring's ends; their walls
            // reach on in to the cabin's own wall, so nothing slips out between the two.
            float innerZ = Mathf.Sqrt(PedestalRadius * PedestalRadius - grateRailX * grateRailX);
            float ringEndZ = Mathf.Sqrt(RingRadius * RingRadius - endX * endX);
            foreach (float side in new[] { -1f, 1f })
                RailPiece(rail.transform, railing, "Grate Rail", new Vector3(side * grateRailX, 0f, innerZ), new Vector3(side * grateRailX, 0f, ringEndZ), 0.2f);
        }

        private static Vector3 Around(float bearingDeg) => new Vector3(Mathf.Cos(bearingDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(bearingDeg * Mathf.Deg2Rad)) * RingRadius;

        // One straight piece of rail from a to b on the deck: its wall (reaching on past
        // a by wallPastA), and the railing stretched to its length.
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
            model.transform.localScale = new Vector3(length / RailingLength, 1f, 1f);
        }

        // The tube's dressing over the shared round cabin: the cap ring over the glass's
        // top edge, the glow band and the cap on it, the beacon on top, the hazard band
        // at the foot, a light inside. Each piece stands on the one under it (placed by
        // their centres, they floated with gaps; ship audit SHIP-011). The ring and the
        // cap wear the ship's kit steel until the elevator's redesign (SHIP-060, 061; Dan:
        // "we will do a new one after").
        private static void DressCabin(Transform cabin)
        {
            const float radius = 2.5f; // the car's radius
            Material steel = SunkCost.Editor.Look.ShipKitMaterials.Steel();
            float y = DeckCabinBuilder.CapRingBottom;
            y = Round("Cap Ring", cabin, y, radius + 0.25f, DeckCabinBuilder.CapRingHeight, steel);
            y = Round("Cap Glow", cabin, y, radius + 0.15f, 0.1f, SunkCost.Editor.Look.LookMaterials.LampWarm());
            y = Round("Cap Top", cabin, y, radius - 0.1f, 0.3f, steel);
            SunkCost.Editor.Look.PropBuilder.Place(cabin.gameObject, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(0f, y, 0f)).name = "Tube Beacon";
            // The hazard band round the tube's foot: an open band, open across the
            // doorway too, so the floor inside is the cabin's own (a capped disc covered
            // it, and the crew's feet sank into it; SHIP-010).
            Transform doorR = cabin.Find(ShipParts.DeckCabinDoorRName);
            float doorHalf = doorR != null ? Mathf.Abs(Mathf.DeltaAngle(0f, doorR.localEulerAngles.y)) : 23.6f; // the doors are parked open at the doorway's half-angle
            GameObject foot = new("Foot Band");
            foot.transform.SetParent(cabin, false);
            foot.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(radius + 0.04f, 0.2f, 0.08f, 90f + doorHalf, 90f - doorHalf + 360f, 96);
            foot.AddComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.ShipKitMaterials.Hazard();
            // The car's own light, warm white, the same in both worlds (Dan, 23 September 2026).
            Light inside = new GameObject("Tube Light").AddComponent<Light>();
            inside.transform.SetParent(cabin, false);
            inside.transform.localPosition = new Vector3(0f, DeckCabinBuilder.CapRingBottom - 0.2f, 0f);
            DeckCabinRideSetup.ConfigureCarLight(inside);
        }

        // A solid round piece `height` tall standing at `bottom`, UVs in metres; returns its top.
        private static float Round(string name, Transform parent, float bottom, float radius, float height, Material material)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, bottom, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Cylinder(radius, height, 48);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return bottom + height;
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
