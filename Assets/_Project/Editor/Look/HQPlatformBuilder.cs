using System.Collections.Generic;
using SunkCost.Look;
using SunkCost.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Look
{
    // The generated HQ kit, assembled using the approved September 2026 handoff.
    // Metres, +Z north; HQ and ship deck tops at Y=0, water at the current ship sea level.
    public static class HQPlatformBuilder
    {
        public const float DeckW = 60f, DeckD = 36f, DeckThick = 0.8f;
        public const float WaterY = SunkCost.Editor.Prototype.ShipStubBuilder.SeaLevelY, SeabedY = -16f;
        public const float BoothZ = 15f;
        public const float ShipDeckY = 0f;
        public static readonly Vector3 GangwayLanding = new(-32f, 0f, -12f); // where the bridge leaves the rim
        public static readonly Vector3 BridgeEnd = new(-44f, 0f, -12f);      // the level boarding lip at the ship's port side
        public const float LandingW = 4f;
        public static readonly Rect Court = new(-18f, -8f, 20f, 12f);   // x, z, w, d
        public static readonly Vector3 CrewMark = new(10f, 0f, -3f);
        public const string PlatformRootName = "Platform";

        public static readonly Vector3 PickupChute = new(10f, 1.15f, 13.55f); // just clear of the chute discharge lip
        public static readonly Vector3 PlankBase = new(30.4f, 0.05f, -16f);

        public static void Build(Scene scene, GameObject ballPrefab, GameObject shipPrefab, Material tankMaterial, Material lampMaterial)
        {
            GameObject platform = new(PlatformRootName);
            Deck(platform);
            HQGeneratedLayout.Structure(platform);
            HQGeneratedLayout.Perimeter(platform);
            Lamps(platform);
            HQGeneratedLayout.Depot(platform, tankMaterial, lampMaterial);
            HQGeneratedLayout.ArrivalArea(platform);
            CourtAndHoops(platform);
            HQGeneratedCourt.Apply(platform);
            Markings(platform);
            HQGeneratedLayout.Dressing(platform);
            Sea();
            HQGeneratedLayout.Dock(shipPrefab);
            Plank(platform);
            Sky(scene);
            // 4,000 renderers drew one by one (the frame rate, Dan, 19 September 2026):
            // everything that never moves is batching-static, so Unity draws it in a
            // few dozen calls. Left dynamic: the ship (it sails), the plank's gate,
            // the waves, the beacons' pulsing lamps, every text.
            MarkStatic(platform);
            MarkStatic(GameObject.Find("Dock"));
            MarkStatic(GameObject.Find("Sea"));
        }

        private static void MarkStatic(GameObject root)
        {
            if (root == null) return;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponentInParent<ShipParts>() != null) continue;
                if (r.GetComponentInParent<HQPlank>() != null) continue;
                if (r.GetComponentInParent<DockBoardingGate>() != null) continue;
                if (r.GetComponentInParent<DockGangway>() != null) continue;
                if (r.GetComponentInParent<SunkCost.Look.WaveSurface>() != null) continue;
                if (r.GetComponentInParent<SunkCost.Look.Beacon>() != null) continue;
                if (r.GetComponent<TextMesh>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        // ---- the deck -------------------------------------------------------------------

        private static void Deck(GameObject root)
        {
            Part(root, "Deck", MeshKit.Box(new Vector3(DeckW, DeckThick, DeckD)), ShipModelSetup.DeckMaterial(), new Vector3(0f, -DeckThick, 0f), withCollider: true);
            // The warm inner floor (the picture's centre is tan under the lamps), a hazard band round it.
            // Floor layers, each well clear of the one under it: coplanar faces flicker
            // at a distance (Dan, 19 September 2026). The deck's top is 0; the inner
            // floor's top 0.02; its bands' 0.045; the court's paint 0.05; lines 0.07.
            Part(root, "Inner Floor", MeshKit.Box(new Vector3(50f, 0.03f, 24f)), ShipModelSetup.DeckMaterial(), new Vector3(0f, -0.01f, -1f));
            Part(root, "Inner Band S", MeshKit.Box(new Vector3(50.6f, 0.025f, 0.3f)), ShipKitMaterials.Hazard(), new Vector3(0f, 0.02f, -13.15f));
            Part(root, "Inner Band N", MeshKit.Box(new Vector3(50.6f, 0.025f, 0.3f)), ShipKitMaterials.Hazard(), new Vector3(0f, 0.02f, 11.15f));
            Part(root, "Inner Band W", MeshKit.Box(new Vector3(0.3f, 0.025f, 24.6f)), ShipKitMaterials.Hazard(), new Vector3(-25.15f, 0.02f, -1f));
            Part(root, "Inner Band E", MeshKit.Box(new Vector3(0.3f, 0.025f, 24.6f)), ShipKitMaterials.Hazard(), new Vector3(25.15f, 0.02f, -1f));
            // The rim: a hazard band on top, a thick ink girder under it all round.
            Part(root, "Edge S", MeshKit.Box(new Vector3(DeckW, 0.02f, 0.5f)), ShipKitMaterials.Hazard(), new Vector3(0f, 0f, -DeckD / 2f + 0.25f));
            Part(root, "Edge W", MeshKit.Box(new Vector3(0.5f, 0.02f, DeckD)), ShipKitMaterials.Hazard(), new Vector3(-DeckW / 2f + 0.25f, 0f, 0f));
            Part(root, "Edge E", MeshKit.Box(new Vector3(0.5f, 0.02f, DeckD)), ShipKitMaterials.Hazard(), new Vector3(DeckW / 2f - 0.25f, 0f, 0f));
            Part(root, "Girder S", MeshKit.Box(new Vector3(DeckW + 0.4f, 1.6f, 0.6f)), ShipKitMaterials.Steel(), new Vector3(0f, -DeckThick - 1.6f, -DeckD / 2f + 0.1f));
            Part(root, "Girder N", MeshKit.Box(new Vector3(DeckW + 0.4f, 1.6f, 0.6f)), ShipKitMaterials.Steel(), new Vector3(0f, -DeckThick - 1.6f, DeckD / 2f - 0.1f));
            Part(root, "Girder W", MeshKit.Box(new Vector3(0.6f, 1.6f, DeckD)), ShipKitMaterials.Steel(), new Vector3(-DeckW / 2f + 0.1f, -DeckThick - 1.6f, 0f));
            Part(root, "Girder E", MeshKit.Box(new Vector3(0.6f, 1.6f, DeckD)), ShipKitMaterials.Steel(), new Vector3(DeckW / 2f - 0.1f, -DeckThick - 1.6f, 0f));
            Part(root, "Girder Band S", MeshKit.Box(new Vector3(DeckW + 0.4f, 0.3f, 0.62f)), ShipKitMaterials.Hazard(), new Vector3(0f, -DeckThick - 0.3f, -DeckD / 2f + 0.1f));
            foreach (float z in new[] { -12f, 0f, 12f })
                Part(root, "Beam", MeshKit.Box(new Vector3(DeckW, 1.0f, 0.6f)), ShipKitMaterials.Steel(), new Vector3(0f, -DeckThick - 1.0f, z));
            foreach (float x in new[] { -24f, 0f, 24f })
                Part(root, "Beam", MeshKit.Box(new Vector3(0.6f, 1.0f, DeckD)), ShipKitMaterials.Steel(), new Vector3(x, -DeckThick - 1.0f, 0f));
            GameObject number = PropBuilder.Place(root, PropBuilder.SignBoard(5f, 2.4f), new Vector3(0f, -DeckThick - 2.3f, -DeckD / 2f - 0.05f), Quaternion.Euler(0f, 180f, 0f));
            number.name = "HQ Number";
            Sign(number, "hq.number", "hq.number.sub");
            Part(root, "Seabed", MeshKit.Box(new Vector3(600f, 1f, 600f)), LookMaterials.Seabed(), new Vector3(0f, SeabedY - 1f, 0f), withCollider: true);
        }

        private static void Lamps(GameObject root)
        {
            GameObject lamp = PropBuilder.LampPost();
            Quaternion toCourt = Quaternion.Euler(0f, 0f, 0f);
            PropBuilder.Place(root, lamp, new Vector3(Court.x - 1.5f, 0f, Court.y - 1.5f), Quaternion.Euler(0f, 45f, 0f)).name = "Court Lamp";
            PropBuilder.Place(root, lamp, new Vector3(Court.xMax + 1.5f, 0f, Court.y - 1.5f), Quaternion.Euler(0f, -45f, 0f)).name = "Court Lamp";
            PropBuilder.Place(root, lamp, new Vector3(Court.x - 1.5f, 0f, Court.yMax + 1.5f), Quaternion.Euler(0f, 135f, 0f)).name = "Court Lamp";
            PropBuilder.Place(root, lamp, new Vector3(Court.xMax + 1.5f, 0f, Court.yMax + 1.5f), Quaternion.Euler(0f, -135f, 0f)).name = "Court Lamp";
            PropBuilder.Place(root, lamp, new Vector3(CrewMark.x - 7f, 0f, CrewMark.z - 5.5f), Quaternion.Euler(0f, 45f, 0f)).name = "Deck Lamp";
            PropBuilder.Place(root, lamp, new Vector3(CrewMark.x + 7f, 0f, CrewMark.z + 5.5f), Quaternion.Euler(0f, -135f, 0f)).name = "Deck Lamp";
            PropBuilder.Place(root, lamp, new Vector3(2f, 0f, 9f), Quaternion.Euler(0f, 180f, 0f)).name = "Deck Lamp";
            // Strings of lights between the court's lamps and over the crew's mark.
            GameObject courtString = PropBuilder.LightString(Court.width + 3f);
            PropBuilder.Place(root, courtString, new Vector3(Court.x + Court.width / 2f, 4.0f, Court.y - 1.5f)).name = "Light String";
            PropBuilder.Place(root, courtString, new Vector3(Court.x + Court.width / 2f, 4.0f, Court.yMax + 1.5f)).name = "Light String";
            GameObject crewString = PropBuilder.LightString(17.8f); // between the two deck lamps
            PropBuilder.Place(root, crewString, new Vector3(CrewMark.x, 4.0f, CrewMark.z), Quaternion.Euler(0f, -38f, 0f)).name = "Light String";
            // Two flood towers on the south rim, and the long string of lights slung
            // between their tops right across the deck (Dan, 19 September 2026: "better").
            GameObject tower = PropBuilder.FloodTower();
            Vector3 towerW = new(-27.2f, 0f, -8.6f), towerE = new(27.2f, 0f, -11.5f);
            PropBuilder.Place(root, tower, towerW, Quaternion.Euler(0f, 30f, 0f)).name = "Flood Tower W";
            PropBuilder.Place(root, tower, towerE, Quaternion.Euler(0f, -30f, 0f)).name = "Flood Tower E";
            Vector3 span = towerE - towerW;
            GameObject longString = PropBuilder.LightString(span.magnitude);
            PropBuilder.Place(root, longString, (towerW + towerE) / 2f + new Vector3(0f, 8.6f, 0f), Quaternion.Euler(0f, -Mathf.Atan2(span.z, span.x) * Mathf.Rad2Deg, 0f)).name = "Light String";
            // A string from the court's north-east lamp to a pole by the booths.
            Vector3 poleAt = new(14f, 0f, 6.4f), lampAt = new(Court.xMax + 1.5f, 0f, Court.yMax + 1.5f);
            PropBuilder.Place(root, PropBuilder.StringPole(), poleAt).name = "String Pole";
            Vector3 run = poleAt - lampAt;
            PropBuilder.Place(root, PropBuilder.LightString(run.magnitude), (poleAt + lampAt) / 2f + new Vector3(0f, 4.0f, 0f), Quaternion.Euler(0f, -Mathf.Atan2(run.z, run.x) * Mathf.Rad2Deg, 0f)).name = "Light String";
        }

        // ---- the booths -----------------------------------------------------------------

        internal static void QuotaBoard(GameObject root, Vector3 at)
        {
            GameObject board = new(SunkCost.World.QuotaBoard.BoardName);
            board.transform.SetParent(root.transform);
            board.transform.position = at;
            board.transform.rotation = Quaternion.Euler(-25f, 180f, 0f);
            GameObject plate = new("Board Plate");
            plate.transform.SetParent(board.transform, false);
            plate.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(3.0f, 0.9f, 0.08f), 0.45f);
            plate.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.SignBoard();
            GameObject frame = new("Frame");
            frame.transform.SetParent(board.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            frame.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(3.08f, 0.98f, 0.03f), 0.49f);
            frame.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.ScreenTeal();
            GameObject text = new("Board Text", typeof(TextMesh));
            text.transform.SetParent(board.transform, false);
            text.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh mesh = text.GetComponent<TextMesh>();
            mesh.text = "QUOTA BOARD";
            mesh.characterSize = 0.05f;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.55f, 0.95f, 0.85f);
            text.AddComponent<DepthText>().Configure(LookMaterials.DepthText());
            BoxCollider box = board.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, 0.02f);
            box.size = new Vector3(3.0f, 0.9f, 0.12f);
            board.AddComponent<SunkCost.World.QuotaBoard>().Configure(mesh);
        }

        internal static void ColourPanel(GameObject root, Vector3 at)
        {
            Material plateMaterial = SunkCost.Editor.Prototype.HQPrototypeBuilder.GetOrCreateMaterial(SunkCost.Editor.Prototype.HQPrototypeBuilder.MaterialPath + "/ColourPanel.mat", Color.white);
            Material swatchMaterial = SunkCost.Editor.Prototype.HQPrototypeBuilder.GetOrCreateMaterial(SunkCost.Editor.Prototype.HQPrototypeBuilder.MaterialPath + "/ColourSwatch.mat", Color.gray);
            GameObject panel = new(SunkCost.World.ColourPanel.PanelName);
            panel.transform.SetParent(root.transform);
            panel.transform.position = at;
            panel.transform.rotation = Quaternion.Euler(-25f, 180f, 0f);
            GameObject back = new("Console");
            back.transform.SetParent(panel.transform, false);
            back.transform.localPosition = new Vector3(0.1f, 0f, -0.05f);
            back.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(1.2f, 0.9f, 0.08f), 0.45f);
            back.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.SignBoard();
            GameObject frame = new("Frame");
            frame.transform.SetParent(panel.transform, false);
            frame.transform.localPosition = new Vector3(0.1f, 0f, -0.07f);
            frame.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(1.28f, 0.98f, 0.03f), 0.49f);
            frame.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.ScreenTeal();
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Wheel Plate";
            plate.transform.SetParent(panel.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            plate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            plate.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.GetComponent<Renderer>().sharedMaterial = plateMaterial;
            GameObject swatch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            swatch.name = SunkCost.World.ColourPanel.SwatchName;
            swatch.transform.SetParent(panel.transform, false);
            swatch.transform.localPosition = new Vector3(0.5f, 0f, 0.03f);
            swatch.transform.localScale = new Vector3(0.14f, 0.14f, 0.04f);
            Object.DestroyImmediate(swatch.GetComponent<Collider>());
            swatch.GetComponent<Renderer>().sharedMaterial = swatchMaterial;
            BoxCollider box = panel.AddComponent<BoxCollider>();
            box.center = new Vector3(0.1f, 0f, 0.02f);
            box.size = new Vector3(0.95f, 0.75f, 0.06f);
            panel.AddComponent<SunkCost.World.ColourPanel>().Configure(plate.GetComponent<Renderer>(), swatch.GetComponent<Renderer>());
        }

        // ---- the court and the marks ----------------------------------------------------

        private static void CourtAndHoops(GameObject root)
        {
            Vector3 c = new(Court.x + Court.width / 2f, 0f, Court.y + Court.height / 2f);
            Part(root, "Court Paint", MeshKit.Box(new Vector3(Court.width, 0.03f, Court.height)), HQGeneratedLayout.CourtMaterial(), new Vector3(c.x, 0.02f, c.z));
            Line(root, new Vector3(c.x, 0f, Court.y + 0.07f), Court.width, 0.14f);
            Line(root, new Vector3(c.x, 0f, Court.yMax - 0.07f), Court.width, 0.14f);
            Line(root, new Vector3(Court.x + 0.07f, 0f, c.z), 0.14f, Court.height);
            Line(root, new Vector3(Court.xMax - 0.07f, 0f, c.z), 0.14f, Court.height);
            Line(root, new Vector3(c.x, 0f, c.z), 0.14f, Court.height);
            foreach (float sx in new[] { -1f, 1f })
            {
                float keyX = c.x + sx * (Court.width / 2f - 2.9f);
                Line(root, new Vector3(keyX, 0f, c.z + 2.45f), 5.8f, 0.14f);
                Line(root, new Vector3(keyX, 0f, c.z - 2.45f), 5.8f, 0.14f);
                Line(root, new Vector3(c.x + sx * (Court.width / 2f - 5.8f), 0f, c.z), 0.14f, 5.0f);
            }
            GameObject ring = new("Centre Circle");
            ring.transform.SetParent(root.transform);
            ring.transform.position = new Vector3(c.x, 0.073f, c.z);
            ring.transform.localScale = new Vector3(1f, .06f, 1f);
            ring.AddComponent<MeshFilter>().sharedMesh = MeshKit.Ring(1.8f, 0.14f, 32, 6);
            ring.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.DeckMarking();
            GameObject hoop = PropBuilder.Hoop();
            PropBuilder.Place(root, hoop, new Vector3(Court.x + 0.6f, 0f, c.z), Quaternion.Euler(0f, 90f, 0f)).name = "Hoop W";
            PropBuilder.Place(root, hoop, new Vector3(Court.xMax - 0.6f, 0f, c.z), Quaternion.Euler(0f, -90f, 0f)).name = "Hoop E";
            GameObject courtSign = PropBuilder.Place(root, PropBuilder.SignBoard(4f, 1.0f), new Vector3(c.x, 3.3f, Court.yMax + 0.2f), Quaternion.Euler(0f, 180f, 0f)); // over the court's north line, read from the court
            courtSign.name = "Court Sign";
            Sign(courtSign, "court", null);
            foreach (float x in new[] { -1.8f, 1.8f })
                Part(root, "Court Sign Post", MeshKit.Box(new Vector3(0.14f, 3.3f, 0.14f)), LookMaterials.Ink(), new Vector3(c.x + x, 0f, Court.yMax + 0.2f), withCollider: true);
            // Chain-link behind each hoop: the ball stays on the court.
            GameObject fence = PropBuilder.Fence(Court.height + 2f, 3.2f);
            PropBuilder.Place(root, fence, new Vector3(Court.x - 2.6f, 0f, c.z), Quaternion.Euler(0f, 90f, 0f)).name = "Court Fence W";
            PropBuilder.Place(root, fence, new Vector3(Court.xMax + 2.6f, 0f, c.z), Quaternion.Euler(0f, 90f, 0f)).name = "Court Fence E";
        }

        // A hazard-striped frame painted on the floor round something that matters.
        private static void HazardFrame(GameObject root, Vector3 centre, float w, float d)
        {
            const float t = 0.16f;
            foreach (float s in new[] { -1f, 1f })
            {
                Part(root, "Hazard Frame", MeshKit.Box(new Vector3(w, 0.02f, t)), ShipKitMaterials.Hazard(), new Vector3(centre.x, 0.05f, centre.z + s * (d / 2f - t / 2f)));
                Part(root, "Hazard Frame", MeshKit.Box(new Vector3(t, 0.02f, d)), ShipKitMaterials.Hazard(), new Vector3(centre.x + s * (w / 2f - t / 2f), 0.05f, centre.z));
            }
        }

        private static void Line(GameObject root, Vector3 at, float w, float d) =>
            Part(root, "Line", MeshKit.Box(new Vector3(w, 0.02f, d)), LookMaterials.DeckMarking(), new Vector3(at.x, 0.06f, at.z));

        private static void Markings(GameObject root)
        {
            // No writing on the deck (Dan, 18 September 2026): the signs on boards say it all
            // (the ship's is the gantry over the bridge, in Dock).
            GameObject plankSign = PropBuilder.Place(root, PropBuilder.SignBoard(3.2f, 1.0f), new Vector3(DeckW / 2f - 0.5f, 1.4f, PlankBase.z + 2.4f), Quaternion.Euler(0f, -90f, 0f));
            plankSign.name = "Plank Sign";
            Sign(plankSign, "plank", null);
        }

        // Crates, containers, barrels and pipes: the picture is crammed.
        private static void Sea()
        {
            GameObject sea = new("Sea");
            Material water = LookMaterials.Water();
            GameObject near = new("Waves");
            near.transform.SetParent(sea.transform);
            near.transform.position = new Vector3(0f, WaterY, 0f);
            near.AddComponent<MeshFilter>().sharedMesh = MeshKit.Plane(160f, 160f, 96, 96);
            near.AddComponent<MeshRenderer>().sharedMaterial = water;
            near.AddComponent<WaveSurface>();
            GameObject far = new("Far Sea");
            far.transform.SetParent(sea.transform);
            far.transform.position = new Vector3(0f, WaterY - 0.1f, 0f);
            far.AddComponent<MeshFilter>().sharedMesh = MeshKit.Plane(40000f, 40000f, 96, 96);
            far.AddComponent<MeshRenderer>().sharedMaterial = water;
        }

        private static void Plank(GameObject root)
        {
            GameObject plank = new(HQPlank.RootName);
            plank.transform.SetParent(root.transform);
            float z = PlankBase.z, edge = DeckW / 2f;
            Part(plank, "Board", MeshKit.Box(new Vector3(3.8f, 0.12f, 0.8f), 0.12f), ShipKitMaterials.Steel(), new Vector3(edge + 1.6f, 0.0f, z), withCollider: true);
            Part(plank, "Board Stripe", MeshKit.Box(new Vector3(3.8f, 0.01f, 0.16f)), ShipKitMaterials.Hazard(), new Vector3(edge + 1.6f, 0.0f, z + 0.32f));
            Part(plank, "Board Stripe", MeshKit.Box(new Vector3(3.8f, 0.01f, 0.16f)), ShipKitMaterials.Hazard(), new Vector3(edge + 1.6f, 0.0f, z - 0.32f));
            Part(plank, "Bracket", MeshKit.Box(new Vector3(1.4f, 0.5f, 1.2f), 0.5f), LookMaterials.Ink(), new Vector3(edge + 0.2f, -0.12f, z), withCollider: true);
            Part(plank, "Winch", MeshKit.Cylinder(0.35f, 0.5f, 12), LookMaterials.Ink(), new Vector3(edge + 0.6f, -1.5f, z), Quaternion.Euler(0f, 0f, 90f), withCollider: true);
            GameObject baseAt = new("Plank Base");
            baseAt.transform.SetParent(plank.transform);
            baseAt.transform.SetPositionAndRotation(PlankBase, Quaternion.Euler(0f, 90f, 0f));
            GameObject endAt = new("Plank End");
            endAt.transform.SetParent(plank.transform);
            endAt.transform.SetPositionAndRotation(new Vector3(edge + 3.3f, 0.05f, z), Quaternion.Euler(0f, 90f, 0f));
            GameObject gate = Part(plank, "Plank Gate", MeshKit.Box(new Vector3(0.24f, 1.3f, 2.0f)), ShipKitMaterials.Hazard(), new Vector3(edge - 0.35f, 0f, z), withCollider: true);
            plank.AddComponent<HQPlank>().Configure(baseAt.transform, endAt.transform, WaterY, gate);
        }

        // ---- the sky ------------------------------------------------------------------

        // Six in the morning: the sun still under the horizon with its glow in the
        // east, the sky a deep blue, the lamps doing the work; a bloom volume for
        // the glow.
        private static void Sky(Scene scene)
        {
            GameObject sky = new("Sky");
            GameObject sunGo = new("Sun", typeof(Light));
            sunGo.transform.SetParent(sky.transform);
            sunGo.transform.rotation = Quaternion.Euler(2f, -60f, 0f); // at the horizon, east-north-east
            Light sun = sunGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.55f, 0.30f);
            sun.intensity = 0.35f;
            sun.shadows = LightShadows.None;
            // A dim blue fill from above so the tops read.
            GameObject fillGo = new("Sky Fill", typeof(Light));
            fillGo.transform.SetParent(sky.transform);
            fillGo.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
            Light fill = fillGo.GetComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.55f, 0.66f, 0.98f); fill.intensity = 0.55f; fill.shadows = LightShadows.Soft; fill.shadowStrength = 0.45f;
            fill.shadowNormalBias = 1.0f; fill.shadowBias = 0.08f; // no acne on the big flat roofs
            RenderSettings.skybox = SkyboxMaterial();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.56f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.40f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.07f, 0.11f, 0.24f);
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 380f;
            sky.AddComponent<SkyEnvironment>();
            GameObject volumeGo = new("Look Volume", typeof(Volume));
            volumeGo.transform.SetParent(sky.transform);
            Volume volume = volumeGo.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = LookProfile();
        }

        public const string SkyboxPath = "Assets/_Project/Art/HQ/Materials/DawnSky.mat";
        private static Material SkyboxMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            Shader shader = Shader.Find("Sunk Cost/Sky Gradient");
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, SkyboxPath);
            }
            if (m.shader != shader) m.shader = shader;
            m.SetColor("_Zenith", new Color(0.03f, 0.05f, 0.15f));
            m.SetColor("_Horizon", new Color(0.09f, 0.15f, 0.36f));
            m.SetColor("_Glow", new Color(0.95f, 0.42f, 0.14f));
            m.SetVector("_GlowDirection", new Vector4(0.85f, 0f, 0.5f, 0f)); // east-north-east: the sun is on its way
            m.SetFloat("_GlowWidth", 0.3f);
            m.SetFloat("_GlowHeight", 0.14f);
            m.SetFloat("_HorizonSharpness", 2.2f);
            m.SetColor("_Ground", new Color(0.09f, 0.24f, 0.56f)); // the sea as it renders: the horizon joins it
            EditorUtility.SetDirty(m);
            return m;
        }

        public const string ProfilePath = "Assets/_Project/Settings/Prototype/HQLookProfile.asset";
        private static VolumeProfile LookProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.0f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.35f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.6f;
            if (!profile.TryGet(out Tonemapping tonemapping)) tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true; tonemapping.mode.value = TonemappingMode.ACES;
            if (!profile.TryGet(out ColorAdjustments colour)) colour = profile.Add<ColorAdjustments>(true);
            colour.active = true;
            colour.postExposure.overrideState = true; colour.postExposure.value = 0.9f;
            colour.contrast.overrideState = true; colour.contrast.value = 10f;
            colour.saturation.overrideState = true; colour.saturation.value = 22f;
            if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>(true);
            vignette.active = false; // crisp, like the picture
            // The overrides saved inside the asset: made only in memory, they were null on
            // disk and the grade never persisted (and URP's build step threw on them).
            SunkCost.Editor.Look.VolumeProfileAssets.SaveOverrides(profile);
            return profile;
        }

        // ---- helpers --------------------------------------------------------------------

        private static GameObject Part(GameObject root, string name, Mesh mesh, Material material, Vector3 position, bool withCollider = false) => Part(root, name, mesh, material, position, Quaternion.identity, withCollider);
        private static GameObject Part(GameObject root, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, bool withCollider = false)
        {
            GameObject go = new(name);
            go.transform.SetParent(root.transform);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (withCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private static void Sign(GameObject sign, string titleKey, string subKey)
        {
            SignText title = null, sub = null;
            foreach (SignText text in sign.GetComponentsInChildren<SignText>(true))
            {
                if (text.name == "Title" || text.name == "Text") { title = text; text.Configure(titleKey); }
                else if (text.name == "Sub") { sub = text; if (subKey != null) text.Configure(subKey); else { text.Configure(string.Empty); text.GetComponent<TextMesh>().text = string.Empty; } }
            }
            if (subKey == null && title != null && sub != null)
            {
                Transform plate = sign.transform.Find("Plate");
                MeshFilter filter = plate != null ? plate.GetComponent<MeshFilter>() : null;
                if (filter != null && filter.sharedMesh != null)
                {
                    Vector3 size = filter.sharedMesh.bounds.size;
                    title.transform.localPosition = new Vector3(0f, size.y * 0.5f, title.transform.localPosition.z);
                    title.Fit(size.x - 0.4f, size.y - 0.3f);
                }
            }
        }
    }
}
