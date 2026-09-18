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
        // A big ship (Dan's picture, 19 September 2026): a blunt barge, the bridge
        // tower at the stern whose roof is the way aboard — the HQ's bridge meets
        // it level, the ship's own stair comes down beside the tower.
        public const float DeckLength = 44f;
        public const float DeckWidth = 16f;
        public const float DeckThickness = 0.5f;
        public const float HullDepth = 7.0f;    // under the deck to a metre into the water at the HQ mooring
        public const float TowerHeight = 6f;    // deck to roof: the HQ's bridge lands on the roof
        public const float TowerWidth = 8f, TowerDepth = 6f;
        public const float VolumeHeight = 9f;   // the aboard and safe-deck volumes reach over the tower's roof
        public const float WellRadius = 3.7f, PedestalRadius = 2.55f; // the gap round the elevator: a shaft open to the sea (Dan, 19 September 2026), wide enough to read as one
        public const float WellDepth = DeckThickness + HullDepth; // down to the water
        public const float RingRadius = 4.0f;   // the round rail about it
        public static readonly Vector3 StairFoot = new(-6f, 0f, -11f); // where the ship's stair meets the deck: the boarding point
        public const string RoofGateName = "Roof Gate";

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
                // Deck top at y = 0; the tower's roof at TowerHeight is where the HQ's bridge lands.
                // The deck is four blocks round the elevator's well (BuildWell) — a hole a
                // primitive cannot have.
                BuildDeck(root.transform);
                BuildHull(root.transform);
                // The rails are the walls the physics needs; what you see of them is the
                // look kit's railing placed along them (BuildLook), so these are unseen.
                Unseen(Block("RailPort", root.transform, new Vector3(-DeckWidth / 2f + 0.1f, 0.5f, 0f), new Vector3(0.2f, 1f, DeckLength), rail));
                Unseen(Block("RailStarboard", root.transform, new Vector3(DeckWidth / 2f - 0.1f, 0.5f, 0f), new Vector3(0.2f, 1f, DeckLength), rail));
                Unseen(Block("RailBow", root.transform, new Vector3(0f, 0.5f, DeckLength / 2f - 0.1f), new Vector3(DeckWidth, 1f, 0.2f), rail));
                // The stern rail either side of the tower.
                float sideW = (DeckWidth - TowerWidth) / 2f;
                Unseen(Block("RailSternPort", root.transform, new Vector3(-TowerWidth / 2f - sideW / 2f, 0.5f, -DeckLength / 2f + 0.1f), new Vector3(sideW, 1f, 0.2f), rail));
                Unseen(Block("RailSternStarboard", root.transform, new Vector3(TowerWidth / 2f + sideW / 2f, 0.5f, -DeckLength / 2f + 0.1f), new Vector3(sideW, 1f, 0.2f), rail));
                BuildTower(root.transform, button);
                BuildLook(root.transform);

                Trigger(ShipParts.AboardVolumeName, root.transform, new Vector3(0f, (VolumeHeight - 1f) / 2f, 0f), new Vector3(DeckWidth + 2f, VolumeHeight + 1f, DeckLength + 2f));
                // The deck proper (the tower's roof over it, the well under it): where a passenger must stand for the ship to move.
                Trigger(ShipParts.SafeDeckVolumeName, root.transform, new Vector3(0f, (VolumeHeight - 1f) / 2f, 0f), new Vector3(DeckWidth - 0.4f, VolumeHeight + 1f, DeckLength - 0.4f));
                // The straight way out: the bow direction, away from the dock at the stern.
                GameObject direction = new(ShipParts.DepartureDirectionName);
                direction.transform.SetParent(root.transform, false);
                direction.transform.localRotation = Quaternion.identity;
                // No gangway on the ship (Dan, 18 September 2026): the HQ's own bridge meets
                // the tower's roof; the ship carries nothing that reaches the base.
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
                BuildTv(root.transform, SunkCost.Editor.Look.LookMaterials.Ink(), screen);

                Vector3[] spawns = { new(-3.5f, 0f, 7f), new(3.5f, 0f, 7f), new(-3.5f, 0f, 10f), new(3.5f, 0f, 10f) };
                for (int i = 0; i < spawns.Length; i++)
                {
                    GameObject point = new(ShipParts.SpawnPointPrefix + (i + 1));
                    point.transform.SetParent(root.transform, false);
                    point.transform.localPosition = spawns[i];
                    point.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the cabin
                }
                // The boarding point: the foot of the ship's own stair, on the deck (Unstuck puts you there).
                GameObject boarding = new(ShipParts.BoardingPointName);
                boarding.transform.SetParent(root.transform, false);
                boarding.transform.localPosition = StairFoot;

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
            // The readout on a plate on the port wall over the doorway, read from the deck's centre line.
            TextMesh mesh = SunkCost.Editor.Look.PropBuilder.SignPlate(root.gameObject, "Storage Sign", ShipParts.StorageReadoutName, new Vector3(cx - w / 2f - 0.06f, h + 0.45f, cz), Quaternion.Euler(0f, -90f, 0f), 1.8f, 0.7f, 0.18f, new Color(0.95f, 0.85f, 0.4f));
            mesh.text = "STORAGE\n$0 / $0";
            root.gameObject.AddComponent<StorageReadout>();
        }

        // The deck TV (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 3): a 2 m × 1.2 m
        // screen against the port rail, midships, facing the deck — a quad ShipTV
        // renders the channel diver's view onto, in a frame on a post, the caption
        // over it and the speaker point on it. E on the screen is the next channel.
        private static void BuildTv(Transform root, Material frame, Material screenMaterial)
        {
            const float x = -DeckWidth / 2f + 0.45f, y = 1.7f, z = -3f;
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
            TextMesh mesh = SunkCost.Editor.Look.PropBuilder.SignPlate(root.gameObject, "Tv Caption Sign", ShipParts.TvCaptionName, new Vector3(x - 0.02f, y + 0.9f, z), Quaternion.Euler(0f, 90f, 0f), 2.2f, 0.4f, 0.16f, new Color(1f, 0.35f, 0.3f));
            mesh.text = "NO SIGNAL";
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

        // The hull under the deck (the picture's barge): charcoal plate, the yellow
        // trim along the deck's edge, an ink band at the waterline, the company's name
        // painted big on both sides, the year on the stern, red fenders hung along the
        // sides, tyres on the stern. Visual only — no colliders, so the deck cabin's
        // car passes through it on the way down and nothing under the deck is walkable.
        private static void BuildHull(Transform root)
        {
            Material plate = SunkCost.Editor.Look.LookMaterials.Panel();
            Material band = SunkCost.Editor.Look.LookMaterials.PanelDark();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            Material trim = SunkCost.Editor.Look.LookMaterials.Trim();
            float y = -DeckThickness - HullDepth / 2f;
            // The hull and every band round it are hollow frames — four pieces each round
            // the elevator's well, so the shaft is open all the way to the water (a solid
            // box would floor it at every band; Dan, 19 September 2026: "still not open").
            HullFrame("Hull", root, y, HullDepth, DeckWidth, DeckLength, DeckWidth / 2f - WellRadius, plate);
            HullFrame("Hull Band", root, -DeckThickness - HullDepth + 1.4f, 1.0f, DeckWidth + 0.06f, DeckLength + 0.06f, 0.4f, band);
            HullFrame("Hull Rub Rail", root, -DeckThickness - 0.55f, 0.36f, DeckWidth + 0.3f, DeckLength + 0.3f, 0.4f, ink);
            HullFrame("Hull Trim", root, -0.22f, 0.16f, DeckWidth + 0.04f, DeckLength + 0.04f, 0.3f, trim);
            HullFrame("Hull Trim Low", root, -DeckThickness - 1.0f, 0.1f, DeckWidth + 0.04f, DeckLength + 0.04f, 0.3f, trim);
            // The company's name on both sides, the year on the stern.
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject face = new("Hull Name");
                face.transform.SetParent(root, false);
                face.transform.localPosition = new Vector3(side * (DeckWidth / 2f + 0.03f), -2.6f, 0f);
                face.transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                SunkCost.Editor.Look.PropBuilder.Text(face, "Name", new Vector3(0f, 0.9f, 0f), 1.7f, new Color(0.82f, 0.84f, 0.88f), TextAnchor.MiddleCenter).AddComponent<SunkCost.Look.SignText>().Configure("company");
                SunkCost.Editor.Look.PropBuilder.Text(face, "Sub", new Vector3(0f, -0.5f, 0f), 0.9f, new Color(0.82f, 0.84f, 0.88f), TextAnchor.MiddleCenter).AddComponent<SunkCost.Look.SignText>().Configure("ship.name.sub");
            }
            GameObject stern = new("Hull Year");
            stern.transform.SetParent(root, false);
            stern.transform.localPosition = new Vector3(3f, -3.2f, -DeckLength / 2f - 0.03f);
            stern.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            SunkCost.Editor.Look.PropBuilder.Text(stern, "Year", Vector3.zero, 1.2f, new Color(0.82f, 0.84f, 0.88f), TextAnchor.MiddleCenter).AddComponent<SunkCost.Look.SignText>().Configure("ship.year");
            // Fenders along the sides, tyres on the stern.
            GameObject fender = SunkCost.Editor.Look.PropBuilder.Fender();
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { -17f, -8f, 8f, 17f })
                    SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, fender, new Vector3(side * (DeckWidth / 2f + 0.65f), -3.0f, z)).name = "Fender";
            foreach (float x in new[] { -6f, 6f })
            {
                GameObject tyre = new("Tyre");
                tyre.transform.SetParent(root, false);
                tyre.transform.localPosition = new Vector3(x, -1.6f, -DeckLength / 2f - 0.12f);
                tyre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tyre.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Ring(0.45f, 0.26f, 16, 8);
                tyre.AddComponent<MeshRenderer>().sharedMaterial = ink;
            }
        }

        // A hollow rectangular frame `w` by `d` of `height`, its four sides `rim` deep,
        // centred on the ship at `y`: the hull's slabs with the well left open.
        private static void HullFrame(string name, Transform root, float y, float height, float w, float d, float rim, Material material)
        {
            float hw = w / 2f, hd = d / 2f;
            Visual(name + " N", root, new Vector3(0f, y, hd - rim / 2f), Quaternion.identity, new Vector3(w, height, rim), material);
            Visual(name + " S", root, new Vector3(0f, y, -hd + rim / 2f), Quaternion.identity, new Vector3(w, height, rim), material);
            Visual(name + " W", root, new Vector3(-hw + rim / 2f, y, 0f), Quaternion.identity, new Vector3(rim, height, d - rim * 2f), material);
            Visual(name + " E", root, new Vector3(hw - rim / 2f, y, 0f), Quaternion.identity, new Vector3(rim, height, d - rim * 2f), material);
        }

        // The bridge tower at the stern: two storeys, the top one glazed, the roof a
        // railed deck the HQ's bridge lands on (a gap in its aft rail), the ship's own
        // stair down its port side to the deck. The sailing monitor is the console on
        // its forward face, with the BRIDGE door and the crew's screen beside it. The
        // roof carries the radar, the mast, the funnels and the beacons, forward of
        // the walk from the gap to the stair.
        private static void BuildTower(Transform root, Material button)
        {
            Material plate = SunkCost.Editor.Look.LookMaterials.Panel();
            Material dark = SunkCost.Editor.Look.LookMaterials.PanelDark();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            Material glow = SunkCost.Editor.Look.LookMaterials.WindowGlow();
            Material trim = SunkCost.Editor.Look.LookMaterials.Trim();
            Material steel = SunkCost.Editor.Look.LookMaterials.RustSteel();
            GameObject rail = SunkCost.Editor.Look.PropBuilder.Rail(), lamp = SunkCost.Editor.Look.PropBuilder.RailLamp();
            float cz = -DeckLength / 2f + TowerDepth / 2f;   // the tower's centre
            float front = -DeckLength / 2f + TowerDepth;     // its forward face
            float roof = TowerHeight;
            GameObject tower = Block("Tower", root, new Vector3(0f, roof / 2f, cz), new Vector3(TowerWidth, roof, TowerDepth), plate); // walls and roof: one collider
            Visual("Tower Base Trim", root, new Vector3(0f, 0.12f, cz), Quaternion.identity, new Vector3(TowerWidth + 0.04f, 0.24f, TowerDepth + 0.04f), trim);
            Visual("Tower Floor Line", root, new Vector3(0f, 3.0f, cz), Quaternion.identity, new Vector3(TowerWidth + 0.04f, 0.16f, TowerDepth + 0.04f), ink);
            Visual("Tower Roof Edge", root, new Vector3(0f, roof - 0.1f, cz), Quaternion.identity, new Vector3(TowerWidth + 0.1f, 0.2f, TowerDepth + 0.1f), ink);
            // The roof plated like the deck (a scaled cube smears its texture; Dan, 19 September 2026: "this platform is bugged").
            GameObject roofSkin = new("Tower Roof Plates");
            roofSkin.transform.SetParent(root, false);
            roofSkin.transform.localPosition = new Vector3(0f, roof, cz);
            roofSkin.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Box(new Vector3(TowerWidth, 0.01f, TowerDepth));
            roofSkin.AddComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.LookMaterials.DeckTile();
            // The glazed top storey: the captain's windows on the forward face and both sides.
            Visual("Bridge Windows", root, new Vector3(0f, 4.4f, front + 0.03f), Quaternion.identity, new Vector3(TowerWidth - 1.2f, 1.5f, 0.06f), glow);
            foreach (float side in new[] { -1f, 1f })
                Visual("Bridge Windows Side", root, new Vector3(side * (TowerWidth / 2f + 0.03f), 4.4f, cz), Quaternion.identity, new Vector3(0.06f, 1.5f, TowerDepth - 1.4f), glow);
            Visual("Bridge Label", root, new Vector3(0f, 5.5f, front + 0.02f), Quaternion.identity, new Vector3(3.6f, 0.5f, 0.04f), ink);
            GameObject label = new("Bridge Label Text");
            label.transform.SetParent(root, false);
            label.transform.localPosition = new Vector3(0f, 5.5f, front + 0.05f);
            SunkCost.Editor.Look.PropBuilder.Text(label, "Text", Vector3.zero, 0.34f, new Color(0.82f, 0.84f, 0.88f), TextAnchor.MiddleCenter).AddComponent<SunkCost.Look.SignText>().Configure("ship.bridge.label");
            // The door with its lit strip and the BRIDGE sign, on the forward face, port of the console.
            Visual("Bridge Door", root, new Vector3(-2.9f, 1.15f, front + 0.04f), Quaternion.identity, new Vector3(1.1f, 2.3f, 0.08f), ink);
            Visual("Bridge Door Light", root, new Vector3(-2.9f, 2.5f, front + 0.06f), Quaternion.identity, new Vector3(1.0f, 0.1f, 0.06f), SunkCost.Editor.Look.LookMaterials.LampWarm());
            GameObject doorSign = SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, SunkCost.Editor.Look.PropBuilder.SignBoard(1.6f, 0.5f), new Vector3(-2.9f, 2.7f, front + 0.02f));
            doorSign.name = "Bridge Sign";
            foreach (SunkCost.Look.SignText text in doorSign.GetComponentsInChildren<SunkCost.Look.SignText>(true))
                text.Configure(text.name == "Sub" ? string.Empty : "ship.bridge");
            // The crew's screen, starboard of the console.
            Visual("Crew Screen Frame", root, new Vector3(2.8f, 2.6f, front + 0.04f), Quaternion.identity, new Vector3(2.4f, 1.5f, 0.08f), ink);
            Visual("Crew Screen", root, new Vector3(2.8f, 2.6f, front + 0.09f), Quaternion.identity, new Vector3(2.2f, 1.3f, 0.02f), SunkCost.Editor.Look.LookMaterials.ScreenTeal());
            GameObject screenText = new("Crew Screen Text");
            screenText.transform.SetParent(root, false);
            screenText.transform.localPosition = new Vector3(2.8f, 2.6f, front + 0.11f);
            SunkCost.Editor.Look.PropBuilder.Text(screenText, "Text", Vector3.zero, 0.3f, new Color(0.05f, 0.14f, 0.22f), TextAnchor.MiddleCenter).AddComponent<SunkCost.Look.SignText>().Configure("ship.screen");
            // The sailing monitor: the console at the tower's foot, the buttons on it.
            float mz = front + 0.5f;
            Visual("Monitor Frame", root, new Vector3(0f, 1.6f, mz - 0.06f), Quaternion.identity, new Vector3(1.8f, 1.2f, 0.1f), ink);
            Visual("Monitor Console", root, new Vector3(0f, 0.5f, mz - 0.1f), Quaternion.identity, new Vector3(1.8f, 1.0f, 0.5f), dark);
            Visual("Monitor Console Stripe", root, new Vector3(0f, 0.16f, mz + 0.16f), Quaternion.identity, new Vector3(1.8f, 0.26f, 0.02f), trim);
            GameObject monitor = Block(ShipParts.MonitorName, root, new Vector3(0f, 1.6f, mz), new Vector3(1.6f, 1f, 0.1f), SunkCost.Editor.Look.LookMaterials.ScreenTeal());
            monitor.AddComponent<ShipMonitor>();
            // Three red buttons with their words on them, in a row under the screen (Dan, 19 September 2026).
            SunkCost.Editor.Look.PropBuilder.PushButton(root.gameObject, ShipParts.MonitorButtonSite01Name, new Vector3(-0.55f, 1.12f, mz + 0.05f), Quaternion.identity, "button.site01", 0.5f)
                .AddComponent<MonitorButton>().Configure(WorldId.Sea, "Site 01");
            SunkCost.Editor.Look.PropBuilder.PushButton(root.gameObject, ShipParts.MonitorButtonHQName, new Vector3(0f, 1.12f, mz + 0.05f), Quaternion.identity, "button.hq", 0.5f)
                .AddComponent<MonitorButton>().Configure(WorldId.HQ, "HQ");
            SunkCost.Editor.Look.PropBuilder.PushButton(root.gameObject, ShipParts.MonitorButtonEndDayName, new Vector3(0.55f, 1.12f, mz + 0.05f), Quaternion.identity, "button.endday", 0.5f)
                .AddComponent<MonitorButton>().ConfigureEndDay("End day");
            Label(ShipParts.MonitorStatusName, root, new Vector3(0f, 1.85f, mz + 0.07f), string.Empty, 0.035f, facingBow: true);
            // The roof: rails all round with the gap aft for the HQ's bridge, lamps on the corners.
            float half = TowerWidth / 2f, back = -DeckLength / 2f;
            for (float x = -half + 1f; x < half; x += 2f)
            {
                SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, rail, new Vector3(x, roof, front - 0.1f)).name = "Roof Rail";
                if (Mathf.Abs(x) > 2.5f) SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, rail, new Vector3(x, roof, back + 0.1f)).name = "Roof Rail";
            }
            for (float z = back + 1f; z < front; z += 2f)
                foreach (float side in new[] { -1f, 1f })
                    if (!(side < 0f && z < back + 3.5f)) // the port aft corner opens onto the stair's platform
                        SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, rail, new Vector3(side * (half - 0.1f), roof, z), Quaternion.Euler(0f, 90f, 0f)).name = "Roof Rail";
            foreach (float x in new[] { -half + 0.1f, half - 0.1f })
                SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, lamp, new Vector3(x, roof, front - 0.1f)).name = "Roof Lamp";
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, lamp, new Vector3(half - 0.1f, roof, back + 0.1f)).name = "Roof Lamp";
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, lamp, new Vector3(-2.1f, roof, back + 0.1f)).name = "Roof Lamp";
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, lamp, new Vector3(2.1f, roof, back + 0.1f)).name = "Roof Lamp";
            Visual("Roof Gap Stripe", root, new Vector3(0f, roof + 0.02f, back + 0.3f), Quaternion.identity, new Vector3(4f, 0.02f, 0.5f), SunkCost.Editor.Look.LookMaterials.Hazard());
            // The gate across the gap: rails on at sea (a 6 m drop otherwise), off at the
            // HQ mooring where the bridge meets it (HQPlatformBuilder.Dock turns it off).
            GameObject gate = new(RoofGateName);
            gate.transform.SetParent(root, false);
            foreach (float x in new[] { -1f, 1f })
                SunkCost.Editor.Look.PropBuilder.Place(gate, rail, new Vector3(x, roof, back + 0.1f)).name = "Gate Rail";
            // The roof's furniture, forward: the radar on its mast, the antenna, two funnels, the beacons.
            Visual("Radar Mast", root, new Vector3(0f, roof + 0.9f, front - 1.4f), Quaternion.identity, new Vector3(0.24f, 1.8f, 0.24f), ink);
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Radar Dome";
            dome.transform.SetParent(root, false);
            dome.transform.localPosition = new Vector3(0f, roof + 2.3f, front - 1.4f);
            dome.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            dome.GetComponent<Renderer>().sharedMaterial = SunkCost.Editor.Look.LookMaterials.FenderBand();
            Object.DestroyImmediate(dome.GetComponent<Collider>());
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, SunkCost.Editor.Look.PropBuilder.Antenna(), new Vector3(2.4f, roof, front - 1.2f)).name = "Antenna";
            foreach (float x in new[] { -2.9f, 2.9f })
            {
                Visual("Funnel", root, new Vector3(x, roof + 1.1f, front - 0.9f), Quaternion.identity, new Vector3(1.0f, 2.2f, 1.0f), steel);
                Visual("Funnel Band", root, new Vector3(x, roof + 1.8f, front - 0.9f), Quaternion.identity, new Vector3(1.04f, 0.3f, 1.04f), trim);
                Visual("Funnel Cap", root, new Vector3(x, roof + 2.3f, front - 0.9f), Quaternion.identity, new Vector3(1.1f, 0.2f, 1.1f), ink);
            }
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(half - 0.6f, roof, back + 0.6f)).name = "Beacon";
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(-half + 0.6f, roof, front - 0.6f)).name = "Beacon";
            // The stair down the tower's port side: a platform off the roof's port aft
            // corner, one straight flight forward down to the deck, railed.
            float px = -half - 1.5f; // the platform's centre, 3 m wide beside the tower
            Block("Stair Platform", root, new Vector3(px, roof - 0.15f, back + 1.5f), new Vector3(3f, 0.3f, 3f), dark);
            Visual("Stair Platform Trim", root, new Vector3(px, roof - 0.28f, back + 1.5f), Quaternion.identity, new Vector3(3.04f, 0.1f, 3.04f), trim);
            GameObject platformSkin = new("Stair Platform Plates");
            platformSkin.transform.SetParent(root, false);
            platformSkin.transform.localPosition = new Vector3(px, roof, back + 1.5f);
            platformSkin.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Box(new Vector3(3f, 0.01f, 3f));
            platformSkin.AddComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.LookMaterials.DeckTile();
            foreach (float z in new[] { back + 0.5f, back + 2.5f }) Block("Stair Platform Post", root, new Vector3(px - 1.3f, roof / 2f, z), new Vector3(0.2f, roof, 0.2f), ink);
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, rail, new Vector3(px - 1.4f, roof, back + 1.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Platform Rail";
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, rail, new Vector3(px, roof, back + 0.1f)).name = "Platform Rail";
            SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, lamp, new Vector3(px - 1.4f, roof, back + 0.1f)).name = "Platform Lamp";
            float run = StairFoot.z - (back + 3f); // from the platform's forward edge down to the foot
            GameObject stairs = SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, SunkCost.Editor.Look.PropBuilder.Stairs(roof, run, 2.6f), new Vector3(px, 0f, StairFoot.z), Quaternion.Euler(0f, 180f, 0f));
            stairs.name = "Tower Stairs"; // pivot at the foot, rising aft to the platform
        }

        // The look of the deck (the platform's kit, so the two read as one place):
        // deck plates, the yellow deck lines of the picture, railings with lamps along
        // the rails, the round rail and grate about the elevator, the helipad, the
        // crane and winch at the bow, cargo along the sides out of the paths. Nothing
        // here is a marker the rules read; the cargo has the kit's colliders.
        private static void BuildLook(Transform root)
        {
            Material deckTile = SunkCost.Editor.Look.LookMaterials.DeckTileWarm();
            Material trim = SunkCost.Editor.Look.LookMaterials.Trim();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            Material dark = SunkCost.Editor.Look.LookMaterials.PanelDark();
            GameObject look = new("Look");
            look.transform.SetParent(root, false);
            // Deck plates in four pieces round the well (the well's rim is plated by BuildDeck).
            float wellHalf = WellRadius, nz = (wellHalf + DeckLength / 2f) / 2f, nd = DeckLength / 2f - wellHalf, sx = (wellHalf + DeckWidth / 2f) / 2f, sw = DeckWidth / 2f - wellHalf;
            Skin(look, "Deck Plates", SunkCost.Editor.Look.MeshKit.Box(new Vector3(DeckWidth, 0.01f, nd)), deckTile, new Vector3(0f, 0f, nz), Quaternion.identity);
            Skin(look, "Deck Plates", SunkCost.Editor.Look.MeshKit.Box(new Vector3(DeckWidth, 0.01f, nd)), deckTile, new Vector3(0f, 0f, -nz), Quaternion.identity);
            Skin(look, "Deck Plates", SunkCost.Editor.Look.MeshKit.Box(new Vector3(sw, 0.01f, wellHalf * 2f)), deckTile, new Vector3(-sx, 0f, 0f), Quaternion.identity);
            Skin(look, "Deck Plates", SunkCost.Editor.Look.MeshKit.Box(new Vector3(sw, 0.01f, wellHalf * 2f)), deckTile, new Vector3(sx, 0f, 0f), Quaternion.identity);
            // Yellow lines: the square about the elevator, the walk from the stair's foot to it and on to the console.
            DeckLine(look, new Vector3(0f, 0f, 0f), 9f, 9f, trim);
            DeckLine(look, new Vector3(StairFoot.x, 0f, -7.5f), 2.6f, 7f, trim);
            DeckLine(look, new Vector3(0f, 0f, -DeckLength / 2f + TowerDepth + 2.5f), 3.4f, 4f, trim);
            // Railings along the rails, a lamp every 4 m, staggered port and starboard.
            GameObject rail = SunkCost.Editor.Look.PropBuilder.Rail(), lamp = SunkCost.Editor.Look.PropBuilder.RailLamp();
            float rx = DeckWidth / 2f - 0.1f, rz = DeckLength / 2f - 0.1f;
            for (float z = -DeckLength / 2f + 1f; z < DeckLength / 2f; z += 2f)
                foreach (float side in new[] { -1f, 1f })
                    SunkCost.Editor.Look.PropBuilder.Place(look, rail, new Vector3(side * rx, 0f, z), Quaternion.Euler(0f, 90f, 0f)).name = "Rail";
            for (float z = -DeckLength / 2f + 2f; z < DeckLength / 2f; z += 4f)
            {
                SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(-rx, 0f, z)).name = "Rail Lamp";
                if (z + 2f < DeckLength / 2f) SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(rx, 0f, z + 2f)).name = "Rail Lamp";
            }
            for (float x = -DeckWidth / 2f + 1f; x < DeckWidth / 2f; x += 2f)
                SunkCost.Editor.Look.PropBuilder.Place(look, rail, new Vector3(x, 0f, rz)).name = "Rail";
            foreach (float side in new[] { -1f, 1f })
                for (float x = TowerWidth / 2f + 1f; x < DeckWidth / 2f; x += 2f)
                    SunkCost.Editor.Look.PropBuilder.Place(look, rail, new Vector3(side * x, 0f, -rz)).name = "Rail";
            foreach (float x in new[] { -rx, rx })
                foreach (float z in new[] { -rz, rz })
                    SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(x, 0f, z)).name = "Rail Lamp";
            // The round rail about the elevator's well, a gap at the door's side (the door
            // looks to the bow) where the grate crosses the well; four lamps on it.
            RoundRail(look, RingRadius, 90f, 24f);
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
                SunkCost.Editor.Look.PropBuilder.Place(look, lamp, new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * RingRadius, 0f, Mathf.Sin(a * Mathf.Deg2Rad) * RingRadius)).name = "Ring Lamp";
            // The helipad, starboard aft: an octagon of yellow line with the H.
            Vector3 pad = new(4.4f, 0f, -6.2f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f + 22.5f;
                Vector3 mid = pad + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * 2.9f;
                Skin(look, "Helipad Line", SunkCost.Editor.Look.MeshKit.Box(new Vector3(2.4f, 0.012f, 0.16f)), trim, mid + new Vector3(0f, 0.03f, 0f), Quaternion.Euler(0f, -a + 90f, 0f)); // X along the tangent
            }
            GameObject h = new("Helipad H");
            h.transform.SetParent(look.transform, false);
            h.transform.localPosition = pad + new Vector3(0f, 0.05f, 0f);
            h.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SunkCost.Editor.Look.PropBuilder.Text(h, "H", Vector3.zero, 2.6f, new Color(0.95f, 0.72f, 0.18f), TextAnchor.MiddleCenter).GetComponent<TextMesh>().text = "H";
            // The crane at the bow, port, its boom out over the side; the winch drum by it.
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crane(), new Vector3(-5.2f, 0f, 17f), Quaternion.Euler(0f, -90f, 0f)).name = "Crane";
            Skin(look, "Winch Drum", SunkCost.Editor.Look.MeshKit.Cylinder(0.7f, 1.6f, 14), ink, new Vector3(-1.8f, 0.7f, 18.4f), Quaternion.Euler(0f, 0f, 90f));
            Skin(look, "Winch Frame", SunkCost.Editor.Look.MeshKit.Box(new Vector3(2.0f, 0.3f, 1.2f)), dark, new Vector3(-1.0f, 0f, 18.4f), Quaternion.identity);
            // Cargo along the sides, clear of the spawns, the cabin's ring, the TV, the
            // stair, the storage room and the helipad.
            GameObject container = SunkCost.Editor.Look.PropBuilder.Container("Grey", 6f);
            SunkCost.Editor.Look.PropBuilder.Place(look, container, new Vector3(6.2f, 0f, 11f), Quaternion.Euler(0f, 90f, 0f)).name = "Container";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Container("Red", 4f), new Vector3(-6.2f, 0f, 8f), Quaternion.Euler(0f, 90f, 0f)).name = "Container";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Red"), new Vector3(6.4f, 0f, 3f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Green"), new Vector3(6.4f, 0f, 1.4f), Quaternion.Euler(0f, 8f, 0f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Yellow"), new Vector3(6.4f, 1.2f, 3f), Quaternion.Euler(0f, -12f, 0f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Grey"), new Vector3(-6.4f, 0f, 2f), Quaternion.Euler(0f, 20f, 0f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Crate("Navy"), new Vector3(-6.4f, 0f, -6f), Quaternion.Euler(0f, -15f, 0f)).name = "Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.SmallCrate("Grey"), new Vector3(5.2f, 0f, 16.5f), Quaternion.Euler(0f, 20f, 0f)).name = "Small Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.SmallCrate("Red"), new Vector3(3.4f, 0f, 19f), Quaternion.Euler(0f, -30f, 0f)).name = "Small Crate";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Barrel("Red"), new Vector3(6.6f, 0f, -16f)).name = "Barrel";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Barrel("Rust"), new Vector3(5.8f, 0f, -16.8f)).name = "Barrel";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Barrel("Red"), new Vector3(6.6f, 0f, -17.6f)).name = "Barrel";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Vent(), new Vector3(6.4f, 0f, 7f), Quaternion.Euler(0f, -90f, 0f)).name = "Vent";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.RopeCoil(), new Vector3(-6.6f, 0f, 14f)).name = "Rope";
            SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.BottleCage(), new Vector3(6.6f, 0f, -2.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Bottle Cage";
            foreach (float x in new[] { -7.2f, 7.2f })
                foreach (float z in new[] { -DeckLength / 2f + 0.8f, DeckLength / 2f - 0.8f })
                    SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.Bollard(), new Vector3(x, 0f, z)).name = "Bollard";
            foreach (float x in new[] { -7.3f, 7.3f })
                SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(x, 0f, DeckLength / 2f - 0.7f)).name = "Beacon";
            // The motto on the storage room's bow wall.
            GameObject motto = SunkCost.Editor.Look.PropBuilder.Place(look, SunkCost.Editor.Look.PropBuilder.SignBoard(2.4f, 1.4f), new Vector3(3.2f, 0.5f, -10.9f));
            motto.name = "Motto";
            foreach (SunkCost.Look.SignText text in motto.GetComponentsInChildren<SunkCost.Look.SignText>(true))
                text.Configure(text.name == "Sub" ? string.Empty : "quota.side");
        }

        // The deck: four blocks round a square hole, and in the hole the well — a round
        // gap 0.6 m deep about the elevator's pedestal (Dan, 19 September 2026: "a small
        // gap that players could fall to", the rail round it so they don't). Its rim
        // is a plated square-with-a-round-hole mesh; its floor, wall and pedestal have
        // colliders, so a fall is a stumble and a jump out.
        private static void BuildDeck(Transform root)
        {
            Material plate = SunkCost.Editor.Look.LookMaterials.Panel();
            Material dark = SunkCost.Editor.Look.LookMaterials.PanelDark();
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            float h = WellRadius, y = -DeckThickness / 2f;
            float nz = (h + DeckLength / 2f) / 2f, nd = DeckLength / 2f - h, sx = (h + DeckWidth / 2f) / 2f, sw = DeckWidth / 2f - h;
            Block("Deck N", root, new Vector3(0f, y, nz), new Vector3(DeckWidth, DeckThickness, nd), plate);
            Block("Deck S", root, new Vector3(0f, y, -nz), new Vector3(DeckWidth, DeckThickness, nd), plate);
            Block("Deck W", root, new Vector3(-sx, y, 0f), new Vector3(sw, DeckThickness, h * 2f), plate);
            Block("Deck E", root, new Vector3(sx, y, 0f), new Vector3(sw, DeckThickness, h * 2f), plate);
            // The rim: the square's corners plated up to the round hole, walkable.
            GameObject rim = new("Well Rim");
            rim.transform.SetParent(root, false);
            rim.transform.localPosition = new Vector3(0f, 0.01f, 0f); // level with the plates' top
            Mesh annulus = SunkCost.Editor.Look.MeshKit.SquareAnnulus(WellRadius, WellRadius, 40); // a cached asset: a mesh made on the fly is lost when the prefab is saved
            rim.AddComponent<MeshFilter>().sharedMesh = annulus;
            rim.AddComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.LookMaterials.DeckTileWarm();
            rim.AddComponent<MeshCollider>().sharedMesh = annulus;
            GameObject rimUnder = new("Well Rim Under"); // the square's corners are open under the rim: fill them
            rimUnder.transform.SetParent(root, false);
            rimUnder.transform.localPosition = new Vector3(0f, -DeckThickness, 0f);
            rimUnder.AddComponent<MeshFilter>().sharedMesh = annulus;
            rimUnder.AddComponent<MeshRenderer>().sharedMaterial = plate;
            // The well: no floor — a shaft down to the sea; its wall a round band the
            // physics knows as a ring of unseen boxes; the pedestal the tube stands on.
            GameObject wellWall = new("Well Wall");
            wellWall.transform.SetParent(root, false);
            wellWall.transform.localPosition = new Vector3(0f, -WellDepth, 0f);
            wellWall.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(WellRadius + 0.04f, WellDepth, 0.08f, 0f, 360f, 64);
            wellWall.AddComponent<MeshRenderer>().sharedMaterial = ink;
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
            pedestal.AddComponent<MeshRenderer>().sharedMaterial = ink;
            pedestal.AddComponent<MeshCollider>().sharedMesh = pedestalMesh;
            Visual("Pedestal Band", root, new Vector3(0f, -0.14f, 0f), Quaternion.identity, new Vector3(PedestalRadius * 2f + 0.04f, 0.12f, PedestalRadius * 2f + 0.04f), SunkCost.Editor.Look.LookMaterials.Hazard(), cylinder: true);
            // The rim of the hole marked, and the shaft's wall stepped in under the deck so the edge reads as an edge.
            GameObject rimBand = new("Well Rim Band");
            rimBand.transform.SetParent(root, false);
            rimBand.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            rimBand.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(WellRadius + 0.02f, 0.3f, 0.06f, 0f, 360f, 64);
            rimBand.AddComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.LookMaterials.Hazard();
            for (float ry = -1f; ry > -WellDepth; ry -= 1.5f) // rings down the shaft, so its depth reads
                Visual("Pedestal Ring", root, new Vector3(0f, ry, 0f), Quaternion.identity, new Vector3(PedestalRadius * 2f + 0.06f, 0.16f, PedestalRadius * 2f + 0.06f), dark, cylinder: true);
            // The grate across the well at the door (the doorway looks to +z), a short
            // rail either side of it: no hopping off the grate into the shaft.
            float grateZ = (PedestalRadius + WellRadius) / 2f, grateD = WellRadius - PedestalRadius + 0.3f;
            Block("Well Grate", root, new Vector3(0f, -0.03f, grateZ), new Vector3(2.2f, 0.06f, grateD), dark);
            for (int i = 0; i < 5; i++)
                Visual("Grate Slat", root, new Vector3(-0.8f + i * 0.4f, 0.02f, grateZ), Quaternion.identity, new Vector3(0.05f, 0.02f, grateD - 0.1f), ink);
            GameObject shortRail = SunkCost.Editor.Look.PropBuilder.RailShort();
            foreach (float x in new[] { -1.15f, 1.15f })
                SunkCost.Editor.Look.PropBuilder.Place(root.gameObject, shortRail, new Vector3(x, 0f, grateZ), Quaternion.Euler(0f, 90f, 0f)).name = "Grate Rail";
            // A lamp low in the shaft: the water lit at its foot, the depth reads.
            foreach (float ly in new[] { -WellDepth + 1.2f })
            {
                Light lamp = new GameObject("Well Light").AddComponent<Light>();
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(0f, ly, -WellRadius + 0.4f);
                lamp.lightmapBakeType = LightmapBakeType.Realtime; lamp.type = LightType.Point; lamp.range = 6f; lamp.intensity = 2.5f; lamp.color = new Color(1f, 0.7f, 0.4f); lamp.shadows = LightShadows.None;
            }
        }

        // A round railing of radius `r` about the origin — truly round (Dan, 19
        // September 2026: "not a lot of blocks that look round"): the top and mid
        // rails are one arc each, the kick plate one curved band, posts every 22.5
        // degrees; a gap of `gapHalfDeg` either side of `gapCentreDeg`. The physics
        // gets a chain of unseen box colliders along the curve.
        private static void RoundRail(GameObject look, float r, float gapCentreDeg, float gapHalfDeg)
        {
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink(), hazard = SunkCost.Editor.Look.LookMaterials.Hazard();
            const int posts = 16;
            float step = 360f / posts;
            float fromDeg = gapCentreDeg + gapHalfDeg, toDeg = gapCentreDeg - gapHalfDeg + 360f;
            int segments = Mathf.CeilToInt((toDeg - fromDeg) / 6f);
            Skin(look, "Ring Top Rail", SunkCost.Editor.Look.MeshKit.Arc(r, 0.16f, fromDeg, toDeg, segments, 10), ink, new Vector3(0f, 1.16f, 0f), Quaternion.identity);
            Skin(look, "Ring Mid Rail", SunkCost.Editor.Look.MeshKit.Arc(r, 0.08f, fromDeg, toDeg, segments, 8), ink, new Vector3(0f, 0.64f, 0f), Quaternion.identity);
            Skin(look, "Ring Kick Plate", SunkCost.Editor.Look.MeshKit.Band(r, 0.28f, 0.06f, fromDeg, toDeg, segments), hazard, Vector3.zero, Quaternion.identity);
            for (int i = 0; i < posts; i++)
            {
                float a = i * step;
                if (Mathf.Abs(Mathf.DeltaAngle(a, gapCentreDeg)) < gapHalfDeg - 0.5f) continue;
                Skin(look, "Ring Post", SunkCost.Editor.Look.MeshKit.Box(new Vector3(0.14f, 1.2f, 0.14f)), ink, new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, 0f, Mathf.Sin(a * Mathf.Deg2Rad) * r), Quaternion.Euler(0f, -a, 0f));
            }
            for (float a = fromDeg; a < toDeg - 0.01f; a += 7.5f)
            {
                float mid = a + 3.75f, chord = 2f * r * Mathf.Sin(3.75f * Mathf.Deg2Rad);
                GameObject wall = new("Ring Wall");
                wall.transform.SetParent(look.transform, false);
                wall.transform.localPosition = new Vector3(Mathf.Cos(mid * Mathf.Deg2Rad) * r, 0f, Mathf.Sin(mid * Mathf.Deg2Rad) * r);
                wall.transform.localRotation = Quaternion.Euler(0f, -mid + 90f, 0f);
                BoxCollider col = wall.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.62f, 0f); col.size = new Vector3(chord + 0.05f, 1.24f, 0.2f);
            }
        }

        // The tube's dressing over the shared round cabin: the cap ring with its glow
        // band and beacon, the frame's foot band, a light inside.
        private static void DressCabin(Transform cabin)
        {
            Material ink = SunkCost.Editor.Look.LookMaterials.Ink();
            const float top = 3.5f, radius = 2.5f; // DeckCabinBuilder's interior height and the car's radius
            Visual("Cap Ring", cabin, new Vector3(0f, top, 0f), Quaternion.identity, new Vector3(radius * 2f + 0.5f, 0.36f, radius * 2f + 0.5f), ink, cylinder: true);
            Visual("Cap Glow", cabin, new Vector3(0f, top + 0.36f, 0f), Quaternion.identity, new Vector3(radius * 2f + 0.3f, 0.1f, radius * 2f + 0.3f), SunkCost.Editor.Look.LookMaterials.LampWarm(), cylinder: true);
            Visual("Cap Top", cabin, new Vector3(0f, top + 0.46f, 0f), Quaternion.identity, new Vector3(radius * 2f - 0.2f, 0.3f, radius * 2f - 0.2f), ink, cylinder: true);
            SunkCost.Editor.Look.PropBuilder.Place(cabin.gameObject, SunkCost.Editor.Look.PropBuilder.BeaconMast(), new Vector3(0f, top + 0.76f, 0f)).name = "Tube Beacon";
            Visual("Foot Band", cabin, new Vector3(0f, 0.1f, 0f), Quaternion.identity, new Vector3(radius * 2f + 0.16f, 0.2f, radius * 2f + 0.16f), SunkCost.Editor.Look.LookMaterials.Hazard(), cylinder: true);
            Light inside = new GameObject("Tube Light").AddComponent<Light>();
            inside.transform.SetParent(cabin, false);
            inside.transform.localPosition = new Vector3(0f, top - 0.4f, 0f);
            inside.lightmapBakeType = LightmapBakeType.Realtime; inside.type = LightType.Point; inside.range = 8f; inside.intensity = 8f; inside.color = new Color(0.75f, 0.95f, 1f); inside.shadows = LightShadows.None;
        }

        // A yellow line rectangle painted on the deck, `w` by `d`, about `centre`.
        private static void DeckLine(GameObject look, Vector3 centre, float w, float d, Material paint)
        {
            const float t = 0.14f, lift = 0.03f; // well clear of the plates' top: coplanar faces flicker (Dan, 19 September 2026)
            foreach (float s in new[] { -1f, 1f })
            {
                Skin(look, "Deck Line", SunkCost.Editor.Look.MeshKit.Box(new Vector3(w, 0.012f, t)), paint, centre + new Vector3(0f, lift, s * (d / 2f - t / 2f)), Quaternion.identity);
                Skin(look, "Deck Line", SunkCost.Editor.Look.MeshKit.Box(new Vector3(t, 0.012f, d)), paint, centre + new Vector3(s * (w / 2f - t / 2f), lift, 0f), Quaternion.identity);
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
