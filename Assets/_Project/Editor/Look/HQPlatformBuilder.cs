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
    // The HQ platform (Dan's reference picture, 18 September 2026, "1:1"): an
    // offshore rig of 2300 at six in the morning — one open deck on rusted legs
    // over a blocky blue sea, lit by orange lamps along every rail; the booths
    // along the north wall with shelves full of goods (the office, Upgrades,
    // Gear & Supplies, Intake/Sell, Check in, the Pickup tower with its stairs);
    // the court in the west; the crew's mark in the east; containers on the
    // roofs, crates and barrels everywhere; the bridge off the west rim with the
    // stairs down to the moored ship; the plank off the south-east corner.
    // Everything is a prop prefab placed here (PropBuilder) or a signed line
    // (HQSigns); nothing gameplay reads any of this — the markers the rules need
    // (spawn points, the plank's base and end, the shop stands and the chute, the
    // board, the panel, the ship) are the same components as before, in new places.
    //
    // Metres, +Z north, the deck's top at y = 0, the water at WaterY.
    public static class HQPlatformBuilder
    {
        public const float DeckW = 60f, DeckD = 36f, DeckThick = 0.8f;
        public const float WaterY = -12f, SeabedY = -20f; // high above the water (Dan, 18 September 2026)
        public const float BoothZ = 15f;           // booth centres; the front (the counter side) at z = 12
        // The way to the ship belongs to the HQ (Dan, 18 September 2026: "the bridge
        // connected to the HQ, like in the photo, then a staircase from the bridge
        // to the ship; the ship clean"): a railed bridge west off the rim at deck
        // level, a landing, and the roof of the ship's bridge tower right behind it —
        // no stair on the HQ at all (Dan, 19 September 2026: "the stairs near the
        // tower lead to the top of the tower, connected through a bridge to the
        // HQ"); the ship's own stair comes down from the roof to its deck.
        public const float ShipDeckY = -SunkCost.Editor.Prototype.ShipStubBuilder.TowerHeight; // the tower's roof at the platform's level
        public static readonly Vector3 GangwayLanding = new(-32f, 0f, -12f); // where the bridge leaves the rim
        public static readonly Vector3 BridgeEnd = new(-44f, 0f, -12f);      // the landing at the bridge's end; the stairs go south from it
        public const float LandingW = 4f;
        public static readonly Rect Court = new(-24f, -10f, 20f, 12f);   // x, z, w, d
        public static readonly Vector3 CrewMark = new(10f, 0f, -3f);
        public const string PlatformRootName = "Platform";

        public static readonly Vector3 UpgradesBoothCentre = new(-16f, 0f, BoothZ);
        public static readonly Vector3 GearBoothCentre = new(-5f, 0f, BoothZ);
        public static readonly Vector3 IntakeBoothCentre = new(6f, 0f, BoothZ);
        public static readonly Vector3 CheckinBoothCentre = new(16f, 0f, BoothZ);
        public static readonly Vector3 PickupBoothCentre = new(25f, 0f, BoothZ);
        public static readonly Vector3 OfficeBoothCentre = new(-26f, 0f, BoothZ);
        public const float PickupFloorY = 5.5f;    // the roof's top: the pickup room's floor
        public static readonly Vector3 PickupChute = new(25f, 4.4f, BoothZ - 0.4f); // in the Pickup booth's ceiling: bought gear drops down onto its floor (Dan, 18 September 2026)
        public static readonly Vector3 PlankBase = new(30.4f, 0.05f, -16f);

        public static void Build(Scene scene, GameObject ballPrefab, GameObject shipPrefab, Material tankMaterial, Material lampMaterial)
        {
            GameObject platform = new(PlatformRootName);
            Deck(platform);
            Legs(platform);
            Rails(platform);
            Lamps(platform);
            Booths(platform, tankMaterial, lampMaterial);
            CourtAndHoops(platform);
            Markings(platform);
            Dressing(platform);
            Sea();
            Dock(shipPrefab);
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
                if (r.GetComponentInParent<SunkCost.Look.WaveSurface>() != null) continue;
                if (r.GetComponentInParent<SunkCost.Look.Beacon>() != null) continue;
                if (r.GetComponent<TextMesh>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        // ---- the deck -------------------------------------------------------------------

        private static void Deck(GameObject root)
        {
            Part(root, "Deck", MeshKit.Box(new Vector3(DeckW, DeckThick, DeckD)), LookMaterials.DeckTile(), new Vector3(0f, -DeckThick, 0f), withCollider: true);
            // The warm inner floor (the picture's centre is tan under the lamps), a hazard band round it.
            // Floor layers, each well clear of the one under it: coplanar faces flicker
            // at a distance (Dan, 19 September 2026). The deck's top is 0; the inner
            // floor's top 0.02; its bands' 0.045; the court's paint 0.05; lines 0.07.
            Part(root, "Inner Floor", MeshKit.Box(new Vector3(50f, 0.03f, 24f)), LookMaterials.DeckTileWarm(), new Vector3(0f, -0.01f, -1f));
            Part(root, "Inner Band S", MeshKit.Box(new Vector3(50.6f, 0.025f, 0.3f)), LookMaterials.Hazard(), new Vector3(0f, 0.02f, -13.15f));
            Part(root, "Inner Band N", MeshKit.Box(new Vector3(50.6f, 0.025f, 0.3f)), LookMaterials.Hazard(), new Vector3(0f, 0.02f, 11.15f));
            Part(root, "Inner Band W", MeshKit.Box(new Vector3(0.3f, 0.025f, 24.6f)), LookMaterials.Hazard(), new Vector3(-25.15f, 0.02f, -1f));
            Part(root, "Inner Band E", MeshKit.Box(new Vector3(0.3f, 0.025f, 24.6f)), LookMaterials.Hazard(), new Vector3(25.15f, 0.02f, -1f));
            // The rim: a hazard band on top, a thick ink girder under it all round.
            Part(root, "Edge S", MeshKit.Box(new Vector3(DeckW, 0.02f, 0.5f)), LookMaterials.Hazard(), new Vector3(0f, 0f, -DeckD / 2f + 0.25f));
            Part(root, "Edge W", MeshKit.Box(new Vector3(0.5f, 0.02f, DeckD)), LookMaterials.Hazard(), new Vector3(-DeckW / 2f + 0.25f, 0f, 0f));
            Part(root, "Edge E", MeshKit.Box(new Vector3(0.5f, 0.02f, DeckD)), LookMaterials.Hazard(), new Vector3(DeckW / 2f - 0.25f, 0f, 0f));
            Part(root, "Girder S", MeshKit.Box(new Vector3(DeckW + 0.4f, 1.6f, 0.6f)), LookMaterials.RustPanel(), new Vector3(0f, -DeckThick - 1.6f, -DeckD / 2f + 0.1f));
            Part(root, "Girder N", MeshKit.Box(new Vector3(DeckW + 0.4f, 1.6f, 0.6f)), LookMaterials.RustPanel(), new Vector3(0f, -DeckThick - 1.6f, DeckD / 2f - 0.1f));
            Part(root, "Girder W", MeshKit.Box(new Vector3(0.6f, 1.6f, DeckD)), LookMaterials.RustPanel(), new Vector3(-DeckW / 2f + 0.1f, -DeckThick - 1.6f, 0f));
            Part(root, "Girder E", MeshKit.Box(new Vector3(0.6f, 1.6f, DeckD)), LookMaterials.RustPanel(), new Vector3(DeckW / 2f - 0.1f, -DeckThick - 1.6f, 0f));
            Part(root, "Girder Band S", MeshKit.Box(new Vector3(DeckW + 0.4f, 0.3f, 0.62f)), LookMaterials.Hazard(), new Vector3(0f, -DeckThick - 0.3f, -DeckD / 2f + 0.1f));
            foreach (float z in new[] { -12f, 0f, 12f })
                Part(root, "Beam", MeshKit.Box(new Vector3(DeckW, 1.0f, 0.6f)), LookMaterials.PanelDark(), new Vector3(0f, -DeckThick - 1.0f, z));
            foreach (float x in new[] { -24f, 0f, 24f })
                Part(root, "Beam", MeshKit.Box(new Vector3(0.6f, 1.0f, DeckD)), LookMaterials.PanelDark(), new Vector3(x, -DeckThick - 1.0f, 0f));
            // A catwalk under the rim on the south and the east, railed and lit: the
            // picture's lower walkway.
            float walkY = -DeckThick - 2.6f;
            // South only, and it stops short of the plank corner: nothing to land on
            // off the board (Dan, 18 September 2026).
            const float walkX0 = -DeckW / 2f - 1.6f, walkX1 = 20f;
            float walkW = walkX1 - walkX0, walkCx = (walkX0 + walkX1) / 2f;
            Part(root, "Catwalk S", MeshKit.Box(new Vector3(walkW, 0.3f, 1.6f)), LookMaterials.DeckTile(), new Vector3(walkCx, walkY, -DeckD / 2f - 1.1f), withCollider: true);
            Part(root, "Catwalk Lip S", MeshKit.Box(new Vector3(walkW, 0.02f, 0.3f)), LookMaterials.Hazard(), new Vector3(walkCx, walkY + 0.3f, -DeckD / 2f - 1.75f));
            GameObject rail = PropBuilder.Rail(), lamp = PropBuilder.RailLamp();
            for (float x = walkX0 + 1f; x < walkX1; x += 2f)
            {
                PropBuilder.Place(root, rail, new Vector3(x, walkY + 0.3f, -DeckD / 2f - 1.75f)).name = "Catwalk Rail";
                if (((int)((x - walkX0) / 2f)) % 3 == 0) PropBuilder.Place(root, lamp, new Vector3(x - 1f, walkY + 0.3f, -DeckD / 2f - 1.75f)).name = "Catwalk Lamp";
            }
            PropBuilder.Place(root, rail, new Vector3(walkX1 - 0.8f, walkY + 0.3f, -DeckD / 2f - 1.1f), Quaternion.Euler(0f, 90f, 0f)).name = "Catwalk Rail End";
            for (float x = -24f; x <= 12f; x += 12f)
                Part(root, "Catwalk Strut", MeshKit.Box(new Vector3(0.3f, 2.6f, 0.3f)), LookMaterials.RustSteel(), new Vector3(x, walkY, -DeckD / 2f - 1.6f), withCollider: true);
            PropBuilder.Place(root, PropBuilder.Ladder(2.9f), new Vector3(-DeckW / 2f + 2f, walkY + 0.3f, -DeckD / 2f - 0.35f)).name = "Catwalk Ladder";
            PropBuilder.Place(root, PropBuilder.Ladder(2.9f), new Vector3(16f, walkY + 0.3f, -DeckD / 2f - 0.35f)).name = "Catwalk Ladder";
            GameObject number = PropBuilder.Place(root, PropBuilder.SignBoard(5f, 2.4f), new Vector3(0f, -DeckThick - 6.0f, -DeckD / 2f - 0.05f), Quaternion.Euler(0f, 180f, 0f));
            number.name = "HQ Number";
            Sign(number, "hq.number", "hq.number.sub");
            Part(root, "Seabed", MeshKit.Box(new Vector3(600f, 1f, 600f)), LookMaterials.Seabed(), new Vector3(0f, SeabedY - 1f, 0f), withCollider: true);
        }

        private static void Legs(GameObject root)
        {
            float height = -DeckThick - SeabedY;
            GameObject leg = PropBuilder.Leg(height);
            GameObject fender = PropBuilder.Fender();
            foreach (float x in new[] { -24f, 0f, 24f })
                foreach (float z in new[] { -12f, 12f })
                {
                    PropBuilder.Place(root, leg, new Vector3(x, SeabedY, z)).name = "Leg";
                    if (z < 0f) PropBuilder.Place(root, fender, new Vector3(x, WaterY - 1.0f, z - 1.9f)).name = "Fender";
                }
            // Braces between the legs: rings at three heights and X-lattices on the long sides.
            foreach (float y in new[] { -16f, -9f, -3.5f })
            {
                Part(root, "Brace", MeshKit.Box(new Vector3(48f, 0.5f, 0.5f)), LookMaterials.RustSteel(), new Vector3(0f, y, -12f));
                Part(root, "Brace", MeshKit.Box(new Vector3(48f, 0.5f, 0.5f)), LookMaterials.RustSteel(), new Vector3(0f, y, 12f));
                foreach (float x in new[] { -24f, 0f, 24f })
                    Part(root, "Brace", MeshKit.Box(new Vector3(0.5f, 0.5f, 24f)), LookMaterials.RustSteel(), new Vector3(x, y, 0f));
            }
            foreach (float z in new[] { -12f, 12f })
                foreach (float x in new[] { -12f, 12f })
                {
                    float length = Mathf.Sqrt(24f * 24f + 5.5f * 5.5f);
                    float angle = Mathf.Atan2(5.5f, 24f) * Mathf.Rad2Deg;
                    Part(root, "Lattice", MeshKit.Box(new Vector3(length, 0.3f, 0.3f), 0.15f), LookMaterials.RustSteel(), new Vector3(x, -6.25f, z), Quaternion.Euler(0f, 0f, angle));
                    Part(root, "Lattice", MeshKit.Box(new Vector3(length, 0.3f, 0.3f), 0.15f), LookMaterials.RustSteel(), new Vector3(x, -6.25f, z), Quaternion.Euler(0f, 0f, -angle));
                }
        }

        // Railings around the rim, 2 m segments, a lamp post every 4 m; gaps for the
        // gangway (west, z -14..-10) and the plank (east, z -18..-14).
        private static void Rails(GameObject root)
        {
            GameObject rail = PropBuilder.Rail(), lamp = PropBuilder.RailLamp();
            for (float x = -DeckW / 2f + 1f; x < DeckW / 2f; x += 2f)
            {
                PropBuilder.Place(root, rail, new Vector3(x, 0f, -DeckD / 2f + 0.15f)).name = "Rail S";
                PropBuilder.Place(root, lamp, new Vector3(x - 1f, 0f, -DeckD / 2f + 0.15f)).name = "Rail Lamp";
            }
            // Rails everywhere (Dan, 19 September 2026: nobody falls off the HQ): the
            // whole west and east rims, the only gaps the bridge and the plank's board.
            for (float z = -DeckD / 2f + 1f; z < DeckD / 2f; z += 2f)
            {
                if (z > -14.5f && z < -9.5f) continue; // the bridge
                PropBuilder.Place(root, rail, new Vector3(-DeckW / 2f + 0.15f, 0f, z), Quaternion.Euler(0f, 90f, 0f)).name = "Rail W";
                if (z < DeckD / 2f - 6f) PropBuilder.Place(root, lamp, new Vector3(-DeckW / 2f + 0.15f, 0f, z - 1f)).name = "Rail Lamp";
            }
            for (float z = -DeckD / 2f + 1f; z < DeckD / 2f; z += 2f)
            {
                if (z < -13.5f) continue; // the plank's gap, closed below to the board's width
                PropBuilder.Place(root, rail, new Vector3(DeckW / 2f - 0.15f, 0f, z), Quaternion.Euler(0f, 90f, 0f)).name = "Rail E";
                if (z < DeckD / 2f - 6f) PropBuilder.Place(root, lamp, new Vector3(DeckW / 2f - 0.15f, 0f, z - 1f)).name = "Rail Lamp";
            }
            GameObject shortRail = PropBuilder.RailShort();
            PropBuilder.Place(root, shortRail, new Vector3(DeckW / 2f - 0.15f, 0f, PlankBase.z - 1.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Rail E Plank";
            PropBuilder.Place(root, shortRail, new Vector3(DeckW / 2f - 0.15f, 0f, PlankBase.z + 1.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Rail E Plank";
            // The rim's cable trays under the rails and the ladders down two legs.
            foreach (float x in new[] { -20f, 0f, 20f })
                PropBuilder.Place(root, PropBuilder.CableTray(18f), new Vector3(x, -0.4f, -DeckD / 2f - 0.3f)).name = "Cable Tray";
            PropBuilder.Place(root, PropBuilder.Ladder(-SeabedY - DeckThick), new Vector3(-24f, SeabedY, -12f - 1.6f), Quaternion.Euler(0f, 180f, 0f)).name = "Leg Ladder";
            PropBuilder.Place(root, PropBuilder.Ladder(-SeabedY - DeckThick), new Vector3(24f, SeabedY, -12f - 1.6f), Quaternion.Euler(0f, 180f, 0f)).name = "Leg Ladder";
            PropBuilder.Place(root, lamp, new Vector3(DeckW / 2f - 0.15f, 0f, DeckD / 2f - 6.5f)).name = "Rail Lamp";
            PropBuilder.Place(root, lamp, new Vector3(-DeckW / 2f + 0.15f, 0f, DeckD / 2f - 6.5f)).name = "Rail Lamp";
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

        private static void Booths(GameObject root, Material tankMaterial, Material lampMaterial)
        {
            Quaternion facingSouth = Quaternion.Euler(0f, 180f, 0f);

            // The office: a booth with a second storey, the company's name across it,
            // the skull, the flag, the beacon and an antenna on the roof.
            GameObject office = PropBuilder.Place(root, PropBuilder.Booth(8f), OfficeBoothCentre, facingSouth);
            office.name = "Office";
            Sign(office.transform.Find("Sign").gameObject, "office", "office.sub");
            Part(root, "Office Upper", MeshKit.Box(new Vector3(8.8f, 4.2f, 6.8f)), LookMaterials.RustPanel(), new Vector3(OfficeBoothCentre.x, 5.5f, BoothZ), withCollider: true);
            Part(root, "Office Roof", MeshKit.Box(new Vector3(9.4f, 0.5f, 7.4f)), LookMaterials.PanelDark(), new Vector3(OfficeBoothCentre.x, 9.7f, BoothZ), withCollider: true);
            Part(root, "Office Roof Lip", MeshKit.Box(new Vector3(9.4f, 0.25f, 0.1f)), LookMaterials.Hazard(), new Vector3(OfficeBoothCentre.x, 10.2f, BoothZ - 3.65f));
            GameObject company = PropBuilder.Place(root, PropBuilder.SignBoard(7.6f, 2.8f), new Vector3(OfficeBoothCentre.x, 6.2f, BoothZ - 3.48f), facingSouth);
            company.name = "Company Sign";
            Sign(company, "company", "company.sub");
            GameObject mark = Part(root, "Company Mark", MeshKit.Box(new Vector3(1.6f, 1.6f, 0.05f)), LookMaterials.Skull(), new Vector3(OfficeBoothCentre.x - 2.6f, 5.6f, BoothZ - 3.5f));
            foreach (float y in new[] { 6.0f, 9.2f })
                PropBuilder.Place(root, PropBuilder.LightStrip(6f), new Vector3(OfficeBoothCentre.x, y, BoothZ - 3.55f)).name = "Office Strip";
            Part(root, "Office Window", MeshKit.Box(new Vector3(0.06f, 1.2f, 4f)), LookMaterials.WindowGlow(), new Vector3(OfficeBoothCentre.x - 4.43f, 7.2f, BoothZ));
            Part(root, "Office Window", MeshKit.Box(new Vector3(0.06f, 1.2f, 4f)), LookMaterials.WindowGlow(), new Vector3(OfficeBoothCentre.x + 4.43f, 7.2f, BoothZ));
            PropBuilder.Place(root, PropBuilder.FlagPole(), new Vector3(OfficeBoothCentre.x - 3.6f, 10.2f, BoothZ + 2.6f)).name = "Flag";
            PropBuilder.Place(root, PropBuilder.BeaconMast(), new Vector3(OfficeBoothCentre.x + 3.4f, 10.2f, BoothZ + 2.6f)).name = "Beacon";
            PropBuilder.Place(root, PropBuilder.Antenna(), new Vector3(OfficeBoothCentre.x + 1.0f, 10.2f, BoothZ + 2.8f)).name = "Antenna";
            PropBuilder.Place(root, PropBuilder.Windsock(), new Vector3(OfficeBoothCentre.x - 1.6f, 10.2f, BoothZ + 2.8f)).name = "Windsock";
            GameObject motto = PropBuilder.Place(root, PropBuilder.SignBoard(4.4f, 3.4f), new Vector3(OfficeBoothCentre.x - 4.43f, 5.6f, BoothZ - 1.4f), Quaternion.Euler(0f, -90f, 0f));
            motto.name = "Motto";
            Sign(motto, "motto", null);

            GameObject upgrades = PropBuilder.Place(root, PropBuilder.Booth(10f), UpgradesBoothCentre, facingSouth);
            upgrades.name = "Upgrades";
            Sign(upgrades.transform.Find("Sign").gameObject, "upgrades", "upgrades.sub");

            GameObject gear = PropBuilder.Place(root, PropBuilder.Booth(10f), GearBoothCentre, facingSouth);
            gear.name = SunkCost.Editor.Prototype.HQPrototypeBuilder.ShopRoomName;
            Sign(gear.transform.Find("Sign").gameObject, "gear", "gear.sub");

            GameObject intake = PropBuilder.Place(root, PropBuilder.Booth(10f), IntakeBoothCentre, facingSouth);
            intake.name = "Intake";
            Sign(intake.transform.Find("Sign").gameObject, "intake", "intake.sub");
            QuotaBoard(root, new Vector3(IntakeBoothCentre.x, 1.85f, BoothZ - 1.0f));
            GameObject quotas = PropBuilder.Place(root, PropBuilder.SignBoard(9.4f, 3.6f), new Vector3(IntakeBoothCentre.x - 0.6f, 7.0f, BoothZ - 2.6f), facingSouth);
            quotas.name = "Quotas Display";
            Sign(quotas, "quota.title", null);
            GameObject side = PropBuilder.Place(root, PropBuilder.SignBoard(3.4f, 3.6f), new Vector3(IntakeBoothCentre.x + 6.0f, 7.0f, BoothZ - 2.6f), facingSouth);
            side.name = "Quotas Side";
            Sign(side, "quota.side", null);
            foreach (float x in new[] { -4.6f, 3.4f, 4.6f, 7.4f })
                Part(root, "Display Post", MeshKit.Box(new Vector3(0.25f, 1.6f, 0.25f)), LookMaterials.Ink(), new Vector3(IntakeBoothCentre.x + x, 5.5f, BoothZ - 2.6f), withCollider: true);

            GameObject checkin = PropBuilder.Place(root, PropBuilder.Booth(8f), CheckinBoothCentre, facingSouth);
            checkin.name = "Check In";
            Sign(checkin.transform.Find("Sign").gameObject, "checkin", "checkin.sub");
            ColourPanel(root, new Vector3(CheckinBoothCentre.x, 1.85f, BoothZ - 1.0f));

            GameObject pickup = PropBuilder.Place(root, PropBuilder.Booth(8f, counter: false), PickupBoothCentre, facingSouth); // walk in and take what landed (Dan, 18 September 2026)
            pickup.name = "Pickup";
            Sign(pickup.transform.Find("Sign").gameObject, "pickup", "pickup.sub");
            PickupTower(root);

            // A row of lamps under every booth's fascia; rails along the roof fronts; a crane and a bottle cage on the deck.
            GameObject lampRow = PropBuilder.LampRow(8f, 5);
            foreach (Vector3 c in new[] { UpgradesBoothCentre, GearBoothCentre, IntakeBoothCentre })
                PropBuilder.Place(root, lampRow, new Vector3(c.x, 3.7f, BoothZ - 3.35f)).name = "Fascia Lamps";
            GameObject lampRow6 = PropBuilder.LampRow(6f, 4);
            foreach (Vector3 c in new[] { OfficeBoothCentre, CheckinBoothCentre, PickupBoothCentre })
                PropBuilder.Place(root, lampRow6, new Vector3(c.x, 3.7f, BoothZ - 3.35f)).name = "Fascia Lamps";
            GameObject rail = PropBuilder.Rail();
            for (float x = -21f; x < 26f; x += 2f)
                PropBuilder.Place(root, rail, new Vector3(x, 5.5f, BoothZ - 3.3f)).name = "Roof Rail";
            for (float x = -21f; x < 21f; x += 2f) // the roofs' back edge, over the sea (Dan: nobody falls off the HQ)
                PropBuilder.Place(root, rail, new Vector3(x, 5.5f, DeckD / 2f - 0.15f)).name = "Roof Rail Back";
            PropBuilder.Place(root, rail, new Vector3(PickupBoothCentre.x + 2.2f, PickupFloorY, 11.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Landing Rail W"; // nobody falls off the landing (Dan)
            GameObject roofLamp = PropBuilder.RailLamp();
            for (float x = -22f; x < 27f; x += 4f)
                if (x < -18f || x > -14f) PropBuilder.Place(root, roofLamp, new Vector3(x, 5.5f, BoothZ - 3.3f)).name = "Roof Lamp";
            foreach (float x in new[] { -3.2f, 3.2f })
                foreach (float z in new[] { -2.8f, 2.8f })
                    PropBuilder.Place(root, roofLamp, new Vector3(PickupBoothCentre.x + x, PickupFloorY + 3.4f + 0.5f + 6.5f + 0.5f, BoothZ + z)).name = "Tower Lamp";
            PropBuilder.Place(root, rail, new Vector3(PickupBoothCentre.x - 3.8f, 5.5f, BoothZ - 1.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Roof Rail W";
            PropBuilder.Place(root, PropBuilder.Crane(), new Vector3(CrewMark.x + 9.5f, 0f, CrewMark.z - 2f), Quaternion.Euler(0f, -120f, 0f)).name = "Crane";
            PropBuilder.Place(root, PropBuilder.BottleCage(), new Vector3(13f, 0f, -16.2f), Quaternion.Euler(0f, 8f, 0f)).name = "Bottle Cage";
            PropBuilder.Place(root, PropBuilder.BottleCage(), new Vector3(-27.5f, 0f, -2f), Quaternion.Euler(0f, 90f, 0f)).name = "Bottle Cage";
            SunkCost.Shop.ShopDeliveryPoint chute = Chute(root);
            Material plinth = LookMaterials.PanelDark();
            Stand(root, chute, SunkCost.Shop.ShopCatalog.LargeTankId, new Vector3(UpgradesBoothCentre.x - 2f, 0.16f, BoothZ + 0.2f), PrimitiveType.Capsule, new Vector3(0.3f, 0.45f, 0.3f), tankMaterial, plinth);
            Stand(root, chute, SunkCost.Shop.ShopCatalog.BrightHeadlampId, new Vector3(UpgradesBoothCentre.x + 2f, 0.16f, BoothZ + 0.2f), PrimitiveType.Sphere, new Vector3(0.35f, 0.35f, 0.35f), lampMaterial, plinth);
            Stand(root, chute, SunkCost.Shop.ShopCatalog.AirTankId, new Vector3(GearBoothCentre.x, 0.16f, BoothZ + 0.2f), PrimitiveType.Capsule, new Vector3(0.22f, 0.3f, 0.22f), tankMaterial, plinth);
        }

        // The pickup tower: the room on the booth's roof where bought gear lands,
        // the stairs up the east side, and two more storeys of glowing windows
        // above it — the picture's lookout, with the beacon on top.
        private static void PickupTower(GameObject root)
        {
            float x = PickupBoothCentre.x, y = PickupFloorY;
            const float w = 8f, d = 6f, h = 3.4f, wall = 0.4f;
            Part(root, "Pickup Room Floor", MeshKit.Box(new Vector3(w, 0.16f, d)), LookMaterials.DeckTile(), new Vector3(x, y, BoothZ), withCollider: true);
            Part(root, "Pickup Room N", MeshKit.Box(new Vector3(w, h, wall)), LookMaterials.Panel(), new Vector3(x, y, BoothZ + d / 2f - wall / 2f), withCollider: true);
            Part(root, "Pickup Room W", MeshKit.Box(new Vector3(wall, h, d)), LookMaterials.Panel(), new Vector3(x - w / 2f + wall / 2f, y, BoothZ), withCollider: true);
            Part(root, "Pickup Room E", MeshKit.Box(new Vector3(wall, h, d)), LookMaterials.Panel(), new Vector3(x + w / 2f - wall / 2f, y, BoothZ), withCollider: true);
            Part(root, "Pickup Room S", MeshKit.Box(new Vector3(5f, h, wall)), LookMaterials.Panel(), new Vector3(x - 1.5f, y, BoothZ - d / 2f + wall / 2f), withCollider: true);
            Part(root, "Pickup Room Lintel", MeshKit.Box(new Vector3(3f, 0.9f, wall)), LookMaterials.Panel(), new Vector3(x + 2.5f, y + h - 0.9f, BoothZ - d / 2f + wall / 2f), withCollider: true);
            Part(root, "Pickup Room Window", MeshKit.Box(new Vector3(3f, 1.1f, 0.08f)), LookMaterials.WindowGlow(), new Vector3(x - 1.5f, y + 1.5f, BoothZ - d / 2f - 0.02f));
            Part(root, "Pickup Room Roof", MeshKit.Box(new Vector3(w + 0.8f, 0.5f, d + 0.8f)), LookMaterials.PanelDark(), new Vector3(x, y + h, BoothZ), withCollider: true);
            PropBuilder.Place(root, PropBuilder.LightStrip(4f), new Vector3(x, y + h - 0.02f, BoothZ)).name = "Pickup Strip";
            PropBuilder.Place(root, PropBuilder.LightStrip(2f), new Vector3(x + 2.5f, y + h - 0.02f, BoothZ - d / 2f - 0.4f)).name = "Pickup Door Strip";
            PropBuilder.Place(root, PropBuilder.Shelf(5f), new Vector3(x - 1f, y + 0.16f, BoothZ + d / 2f - wall - 0.4f), Quaternion.identity).name = "Pickup Shelf";
            GameObject disc = new("Shop Landing");
            disc.transform.SetParent(root.transform);
            disc.transform.position = new Vector3(PickupChute.x, 0.17f, PickupChute.z);
            disc.AddComponent<MeshFilter>().sharedMesh = MeshKit.Cylinder(1.0f, 0.02f, 24);
            disc.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.Hazard();
            // Two more storeys above, windows glowing, the beacon and an antenna on top.
            float top = y + h + 0.5f;
            Part(root, "Tower Upper", MeshKit.Box(new Vector3(w + 0.8f, 6.5f, d + 0.8f)), LookMaterials.RustPanel(), new Vector3(x, top, BoothZ), withCollider: true);
            Part(root, "Tower Band", MeshKit.Box(new Vector3(w + 0.9f, 0.6f, d + 0.9f)), LookMaterials.Panel(), new Vector3(x, top + 3.0f, BoothZ));
            foreach (float wy in new[] { top + 1.6f, top + 4.4f })
            {
                Part(root, "Tower Window", MeshKit.Box(new Vector3(w - 1f, 1.0f, 0.06f)), LookMaterials.WindowGlow(), new Vector3(x, wy, BoothZ - d / 2f - 0.43f));
                Part(root, "Tower Window", MeshKit.Box(new Vector3(0.06f, 1.0f, d - 1f)), LookMaterials.WindowGlow(), new Vector3(x + w / 2f + 0.43f, wy, BoothZ));
                Part(root, "Tower Window", MeshKit.Box(new Vector3(0.06f, 1.0f, d - 1f)), LookMaterials.WindowGlow(), new Vector3(x - w / 2f - 0.43f, wy, BoothZ));
            }
            Part(root, "Tower Roof", MeshKit.Box(new Vector3(w + 1.4f, 0.5f, d + 1.4f)), LookMaterials.PanelDark(), new Vector3(x, top + 6.5f, BoothZ), withCollider: true);
            Part(root, "Tower Roof Lip", MeshKit.Box(new Vector3(w + 1.4f, 0.25f, 0.1f)), LookMaterials.Hazard(), new Vector3(x, top + 7.0f, BoothZ - d / 2f - 0.65f));
            PropBuilder.Place(root, PropBuilder.BeaconMast(), new Vector3(x, top + 7.0f, BoothZ)).name = "Tower Beacon";
            PropBuilder.Place(root, PropBuilder.Antenna(), new Vector3(x + 3f, top + 7.0f, BoothZ + 2f)).name = "Tower Antenna";
            GameObject towerMotto = PropBuilder.Place(root, PropBuilder.SignBoard(3.0f, 3.0f), new Vector3(x + w / 2f + 0.47f, top + 1.2f, BoothZ), Quaternion.Euler(0f, 90f, 0f));
            towerMotto.name = "Tower Motto";
            Sign(towerMotto, "tower.motto", null);
            // The stairs: up the east rim from z = 4 to the room's opening at z = 12; a landing at the top.
            GameObject stairs = PropBuilder.Place(root, PropBuilder.Stairs(y, 7f, 1.8f), new Vector3(x + 3f, 0f, 4f));
            stairs.name = "Pickup Stairs";
            Part(root, "Pickup Landing", MeshKit.Box(new Vector3(1.8f, 0.16f, 1.2f)), LookMaterials.DeckTile(), new Vector3(x + 3f, y, 11.5f), withCollider: true);
            Part(root, "Pickup Landing Rail", MeshKit.Box(new Vector3(0.12f, 1.2f, 1.2f)), LookMaterials.Ink(), new Vector3(x + 3.9f, y, 11.5f), withCollider: true);
        }

        private static SunkCost.Shop.ShopDeliveryPoint Chute(GameObject root)
        {
            GameObject chute = new(SunkCost.Shop.ShopDeliveryPoint.DefaultName);
            chute.transform.SetParent(root.transform);
            chute.transform.position = PickupChute;
            chute.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(1.0f, 0.7f, 1.0f), 0.35f);
            chute.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.PanelDark();
            GameObject band = new("Chute Band");
            band.transform.SetParent(chute.transform, false);
            band.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(1.04f, 0.2f, 1.04f), 0.1f);
            band.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.Hazard();
            return chute.AddComponent<SunkCost.Shop.ShopDeliveryPoint>();
        }

        private static void Stand(GameObject root, SunkCost.Shop.ShopDeliveryPoint delivery, string itemId, Vector3 at, PrimitiveType shape, Vector3 shapeScale, Material shapeMaterial, Material plinthMaterial)
        {
            GameObject stand = new("Shop Stand " + itemId);
            stand.transform.SetParent(root.transform);
            stand.transform.position = at;
            stand.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            GameObject plinth = new("Plinth");
            plinth.transform.SetParent(stand.transform, false);
            plinth.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.9f, 1.1f, 0.9f));
            plinth.AddComponent<MeshRenderer>().sharedMaterial = plinthMaterial;
            BoxCollider pc = plinth.AddComponent<BoxCollider>(); pc.center = new Vector3(0f, 0.55f, 0f); pc.size = new Vector3(0.9f, 1.1f, 0.9f);
            GameObject stripe = new("Stripe");
            stripe.transform.SetParent(stand.transform, false);
            stripe.transform.localPosition = new Vector3(0f, 0.15f, 0.46f);
            stripe.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.9f, 0.2f, 0.01f));
            stripe.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.Hazard();
            GameObject glow = new("Glow");
            glow.transform.SetParent(stand.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            glow.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.94f, 0.04f, 0.94f));
            glow.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.LampWarm();
            GameObject thing = GameObject.CreatePrimitive(shape);
            thing.name = "Display";
            thing.transform.SetParent(stand.transform, false);
            thing.transform.localPosition = new Vector3(0f, 1.14f + shapeScale.y * 0.5f + 0.02f, 0f);
            thing.transform.localScale = shapeScale;
            thing.GetComponent<Renderer>().sharedMaterial = shapeMaterial;
            // The price on a plate over the stand (no floating text, Dan, 19 September 2026).
            TextMesh mesh = PropBuilder.SignPlate(stand, "Label Plate", "Label", new Vector3(0f, 2.2f, 0f), Quaternion.identity, 1.4f, 0.5f, 0.16f, new Color(1f, 0.82f, 0.50f));
            Part(stand, "Label Post", MeshKit.Box(new Vector3(0.06f, 0.5f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, 1.5f, 0f), withCollider: true);
            stand.AddComponent<SunkCost.Shop.ShopDisplay>().Configure(itemId, mesh, delivery);
        }

        private static void QuotaBoard(GameObject root, Vector3 at)
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

        private static void ColourPanel(GameObject root, Vector3 at)
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
            Part(root, "Court Paint", MeshKit.Box(new Vector3(Court.width, 0.03f, Court.height)), LookMaterials.CourtPaint(), new Vector3(c.x, 0.02f, c.z));
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
            ring.transform.position = new Vector3(c.x, 0.07f, c.z);
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
                Part(root, "Hazard Frame", MeshKit.Box(new Vector3(w, 0.02f, t)), LookMaterials.Hazard(), new Vector3(centre.x, 0.05f, centre.z + s * (d / 2f - t / 2f)));
                Part(root, "Hazard Frame", MeshKit.Box(new Vector3(t, 0.02f, d)), LookMaterials.Hazard(), new Vector3(centre.x + s * (w / 2f - t / 2f), 0.05f, centre.z));
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
        private static void Dressing(GameObject root)
        {
            GameObject grey = PropBuilder.Crate("Grey"), red = PropBuilder.Crate("Red"), green = PropBuilder.Crate("Green"), yellow = PropBuilder.Crate("Yellow"), navy = PropBuilder.Crate("Navy");
            GameObject barrelRed = PropBuilder.Barrel("Red"), barrelRust = PropBuilder.Barrel("Rust");
            void C(GameObject prefab, float x, float y, float z, float yaw = 0f) => PropBuilder.Place(root, prefab, new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f)).name = "Crate";
            void B(GameObject prefab, float x, float y, float z) => PropBuilder.Place(root, prefab, new Vector3(x, y, z)).name = "Barrel";
            // The floor's own clutter (Dan, 19 September 2026: "it could look better"):
            // drains and manholes flush with the plates, cable runs, a generator, pallets,
            // rope, tyres, toolboxes, and a hazard frame round the crane's foot.
            foreach (Vector3 at in new[] { new Vector3(-14f, 0f, 6f), new Vector3(10f, 0f, 6.2f), new Vector3(0f, 0f, -12f), new Vector3(22f, 0f, -8.8f), new Vector3(-8f, 0f, -13f) })
                PropBuilder.Place(root, PropBuilder.Drain(), at + new Vector3(0f, 0.04f, 0f)).name = "Drain";
            foreach (Vector3 at in new[] { new Vector3(4f, 0f, 5.6f), new Vector3(-22f, 0f, -13f), new Vector3(8f, 0f, -13.5f) })
                PropBuilder.Place(root, PropBuilder.Manhole(), at + new Vector3(0f, 0.04f, 0f)).name = "Manhole";
            PropBuilder.Place(root, PropBuilder.CableTray(14f), new Vector3(10f, 0.04f, 7.6f)).name = "Floor Cable Run";
            PropBuilder.Place(root, PropBuilder.CableTray(10f), new Vector3(-16f, 0.04f, 4.2f)).name = "Floor Cable Run";
            PropBuilder.Place(root, PropBuilder.Generator(), new Vector3(-14f, 0f, -15.2f), Quaternion.Euler(0f, 10f, 0f)).name = "Generator";
            PropBuilder.Place(root, PropBuilder.Toolbox(), new Vector3(-12.3f, 0f, -14.6f), Quaternion.Euler(0f, 35f, 0f)).name = "Toolbox";
            PropBuilder.Place(root, PropBuilder.Toolbox(), new Vector3(-3.2f, 0f, 8.3f), Quaternion.Euler(0f, -70f, 0f)).name = "Toolbox";
            PropBuilder.Place(root, PropBuilder.Pallet(), new Vector3(-8f, 0f, 7f), Quaternion.Euler(0f, 15f, 0f)).name = "Pallet";
            PropBuilder.Place(root, PropBuilder.Pallet(), new Vector3(25.8f, 0f, -6.5f), Quaternion.Euler(0f, -30f, 0f)).name = "Pallet";
            foreach (Vector3 at in new[] { new Vector3(-20f, 0f, 6.5f), new Vector3(16f, 0f, -12.5f), new Vector3(2f, 0f, -12.8f) })
                PropBuilder.Place(root, PropBuilder.RopeCoil(), at, Quaternion.Euler(0f, at.x * 7f, 0f)).name = "Rope";
            PropBuilder.Place(root, PropBuilder.TyreStack(), new Vector3(-3.6f, 0f, 6.6f)).name = "Tyres";
            HazardFrame(root, new Vector3(CrewMark.x + 9.5f, 0f, CrewMark.z - 2f), 3.6f, 3.6f);
            HazardFrame(root, new Vector3(-14f, 0f, -15.2f), 3.2f, 2.4f);
            // South rim, west end: a stack and barrels by the gangway sign.
            C(grey, -27.6f, 0f, -15.6f); C(red, -26.1f, 0f, -15.7f, 6f); C(green, -26.9f, 1.2f, -15.65f, -4f); C(navy, -24.6f, 0f, -15.8f, 3f);
            B(barrelRed, -22.5f, 0f, -16.2f); B(barrelRust, -21.7f, 0f, -16.0f); B(barrelRed, -22.1f, 0f, -15.3f);
            // South rim, middle.
            C(yellow, 3.5f, 0f, -15.8f, 12f); C(grey, 5.0f, 0f, -15.6f, -3f); C(red, 4.3f, 1.2f, -15.7f, 8f);
            B(barrelRust, 8.5f, 0f, -16.3f); B(barrelRust, 9.3f, 0f, -15.7f);
            // By the crew's mark.
            C(red, 19.5f, 0f, -1.5f, 30f); C(grey, 21.0f, 0f, -2.9f, 20f); C(yellow, 20.2f, 1.2f, -2.2f, 25f); C(green, 18.6f, 0f, -3.4f, 40f);
            C(navy, 2.5f, 0f, 3.5f, -15f); C(grey, 4.0f, 0f, 3.7f, 5f); C(red, 3.2f, 1.2f, 3.6f, -8f);
            B(barrelRed, 17f, 0f, 5f); B(barrelRed, 17.8f, 0f, 5.3f); B(barrelRust, 17.4f, 0f, 6.0f);
            // East rim.
            C(red, 28.2f, 0f, -8f); C(grey, 28.2f, 0f, -6.5f); C(green, 28.2f, 1.2f, -7.25f, 3f); C(yellow, 28.2f, 0f, -4.8f, -6f);
            // West rim, north of the gangway.
            C(grey, -28.5f, 0f, -6f, 90f); C(yellow, -28.5f, 0f, -4.5f, 90f); C(navy, -28.5f, 1.2f, -5.25f, 88f); C(red, -28.5f, 0f, 2f, 90f);
            B(barrelRust, -28.6f, 0f, 5.5f); B(barrelRed, -28.6f, 0f, 6.4f);
            // Nothing in front of the booths: it blocks them (Dan, 19 September 2026).
            // In the pickup room.
            C(grey, PickupBoothCentre.x - 2.8f, PickupFloorY + 0.16f, BoothZ + 1.4f, 15f); C(red, PickupBoothCentre.x - 1.3f, PickupFloorY + 0.16f, BoothZ + 1.7f, -10f);
            GameObject sGrey = PropBuilder.SmallCrate("Grey"), sRed = PropBuilder.SmallCrate("Red"), sYellow = PropBuilder.SmallCrate("Yellow"), sNavy = PropBuilder.SmallCrate("Navy");
            void S(GameObject prefab, float x, float y, float z, float yaw = 0f) => PropBuilder.Place(root, prefab, new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f)).name = "Small Crate";
            S(sRed, -24.6f, 1.2f, -15.8f, 20f); S(sGrey, -23.9f, 1.2f, -15.4f, -10f); S(sYellow, 5.0f, 1.2f, -15.6f, 30f);
            S(sNavy, 21.0f, 1.2f, -2.9f, 5f); S(sGrey, 18.6f, 1.2f, -3.4f, 35f); S(sRed, 4.0f, 1.2f, 3.7f, 50f);
            S(sYellow, 28.2f, 1.2f, -4.8f, 10f); S(sGrey, -28.5f, 1.2f, 2f, 80f); S(sRed, PickupBoothCentre.x - 2.8f, PickupFloorY + 1.36f, BoothZ + 1.4f, 25f);
            S(sNavy, 10f, 0f, -8.5f, 15f); S(sGrey, 10.8f, 0f, -8.3f, -5f); S(sRed, 10.4f, 0.6f, -8.4f, 40f);
            // Containers on the booth roofs (and one on the deck by the tower).
            GameObject cRed = PropBuilder.Container("Red", 6f), cGreen = PropBuilder.Container("Green", 6f), cGrey = PropBuilder.Container("Grey", 4f), cYellow = PropBuilder.Container("Yellow", 4f);
            float roof = 5.5f;
            PropBuilder.Place(root, cRed, new Vector3(UpgradesBoothCentre.x - 1f, roof, BoothZ + 0.6f)).name = "Container";
            PropBuilder.Place(root, cGreen, new Vector3(GearBoothCentre.x + 0.5f, roof, BoothZ - 0.4f), Quaternion.Euler(0f, 6f, 0f)).name = "Container";
            PropBuilder.Place(root, cGrey, new Vector3(IntakeBoothCentre.x - 2f, roof, BoothZ + 1.6f), Quaternion.Euler(0f, -8f, 0f)).name = "Container";
            PropBuilder.Place(root, cYellow, new Vector3(CheckinBoothCentre.x + 0.5f, roof, BoothZ + 0.2f)).name = "Container";
            PropBuilder.Place(root, cRed, new Vector3(CheckinBoothCentre.x + 0.5f, roof + 2.6f, BoothZ + 0.2f), Quaternion.Euler(0f, 4f, 0f)).name = "Container";
            PropBuilder.Place(root, cGrey, new Vector3(22f, 0f, -12f), Quaternion.Euler(0f, 90f, 0f)).name = "Container";
            PropBuilder.Place(root, cGreen, new Vector3(26.5f, 0f, 0.5f), Quaternion.Euler(0f, 90f, 0f)).name = "Container"; // by the east rail, off every shop front (Dan, 19 September 2026)
            // Tanks and a long pipe run across the roofs, dishes, and the rim's glow strip.
            foreach (float x in new[] { UpgradesBoothCentre.x + 3.2f, CheckinBoothCentre.x - 2.4f })
            {
                Part(root, "Roof Tank", MeshKit.Cylinder(0.9f, 2.2f, 14), LookMaterials.RustSteel(), new Vector3(x, roof, BoothZ + 1.6f), withCollider: true);
                Part(root, "Roof Tank Band", MeshKit.Cylinder(0.92f, 0.25f, 14), LookMaterials.Hazard(), new Vector3(x, roof + 1.2f, BoothZ + 1.6f));
                Part(root, "Roof Tank Cap", MeshKit.Cylinder(0.5f, 0.3f, 12), LookMaterials.Ink(), new Vector3(x, roof + 2.2f, BoothZ + 1.6f));
            }
            PropBuilder.Place(root, PropBuilder.Pipe(20f), new Vector3(-10f, roof + 0.4f, BoothZ + 2.7f)).name = "Roof Pipe";
            PropBuilder.Place(root, PropBuilder.Pipe(20f), new Vector3(11f, roof + 0.4f, BoothZ + 2.7f)).name = "Roof Pipe";
            Part(root, "Rim Glow S", MeshKit.Box(new Vector3(DeckW - 1f, 0.05f, 0.1f)), LookMaterials.LampOrange(), new Vector3(0f, 0.02f, -DeckD / 2f + 0.42f));
            Part(root, "Rim Glow W", MeshKit.Box(new Vector3(0.1f, 0.05f, DeckD - 8f)), LookMaterials.LampOrange(), new Vector3(-DeckW / 2f + 0.42f, 0.02f, -4f));
            Part(root, "Rim Glow E", MeshKit.Box(new Vector3(0.1f, 0.05f, DeckD - 8f)), LookMaterials.LampOrange(), new Vector3(DeckW / 2f - 0.42f, 0.02f, -4f));
            // Vents on the roofs, pipes along the booth fronts under the counters.
            GameObject vent = PropBuilder.Vent();
            PropBuilder.Place(root, vent, new Vector3(UpgradesBoothCentre.x + 3f, roof, BoothZ - 1.5f)).name = "Vent";
            PropBuilder.Place(root, vent, new Vector3(IntakeBoothCentre.x + 3f, roof, BoothZ - 1.2f), Quaternion.Euler(0f, 180f, 0f)).name = "Vent";
            PropBuilder.Place(root, vent, new Vector3(GearBoothCentre.x - 3.5f, roof, BoothZ + 1.6f), Quaternion.Euler(0f, 90f, 0f)).name = "Vent";
            GameObject pipe = PropBuilder.Pipe(8f);
            foreach (float x in new[] { UpgradesBoothCentre.x, GearBoothCentre.x, IntakeBoothCentre.x })
                PropBuilder.Place(root, pipe, new Vector3(x, 0.5f, 12.3f)).name = "Pipe";
            PropBuilder.Place(root, PropBuilder.Pipe(20f), new Vector3(-20f, -DeckThick - 0.4f, -DeckD / 2f - 0.45f)).name = "Rim Pipe";
            PropBuilder.Place(root, PropBuilder.Pipe(20f), new Vector3(8f, -DeckThick - 0.4f, -DeckD / 2f - 0.45f)).name = "Rim Pipe";
        }

        // ---- the sea, the dock, the plank -----------------------------------------------

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

        private static void Dock(GameObject shipPrefab)
        {
            GameObject dock = new("Dock");
            GameObject rail = PropBuilder.Rail(), lamp = PropBuilder.RailLamp();
            // The bridge: from the rim's gap west to the landing, railed both sides, a
            // lamp every 4 m, the gantry sign over its start.
            float bridgeX0 = -DeckW / 2f, bridgeX1 = BridgeEnd.x + LandingW / 2f;
            float bridgeCx = (bridgeX0 + bridgeX1) / 2f, bridgeLen = bridgeX0 - bridgeX1;
            Part(dock, "Bridge", MeshKit.Box(new Vector3(bridgeLen, DeckThick, LandingW)), LookMaterials.DeckTile(), new Vector3(bridgeCx, -DeckThick, BridgeEnd.z), withCollider: true);
            Part(dock, "Bridge Girder", MeshKit.Box(new Vector3(bridgeLen + 0.4f, 1.2f, LandingW + 0.2f)), LookMaterials.RustSteel(), new Vector3(bridgeCx, -DeckThick - 1.2f, BridgeEnd.z));
            Part(dock, "Bridge Stripe", MeshKit.Box(new Vector3(bridgeLen, 0.02f, 0.3f)), LookMaterials.Hazard(), new Vector3(bridgeCx, 0f, BridgeEnd.z + LandingW / 2f - 0.15f));
            Part(dock, "Bridge Stripe", MeshKit.Box(new Vector3(bridgeLen, 0.02f, 0.3f)), LookMaterials.Hazard(), new Vector3(bridgeCx, 0f, BridgeEnd.z - LandingW / 2f + 0.15f));
            for (float x = bridgeX0 - 1f; x > bridgeX1; x -= 2f)
            {
                foreach (float side in new[] { -1f, 1f })
                    PropBuilder.Place(dock, rail, new Vector3(x, 0f, BridgeEnd.z + side * (LandingW / 2f - 0.1f))).name = "Bridge Rail";
                if (((int)((bridgeX0 - x - 1f) / 2f)) % 2 == 1)
                    foreach (float side in new[] { -1f, 1f })
                        PropBuilder.Place(dock, lamp, new Vector3(x + 1f, 0f, BridgeEnd.z + side * (LandingW / 2f - 0.1f))).name = "Bridge Lamp";
            }
            // The landing at the bridge's end, railed west and north; the stairs leave it south.
            Part(dock, "Bridge Landing", MeshKit.Box(new Vector3(LandingW, DeckThick, LandingW)), LookMaterials.DeckTile(), new Vector3(BridgeEnd.x, -DeckThick, BridgeEnd.z), withCollider: true);
            Part(dock, "Landing Girder", MeshKit.Box(new Vector3(LandingW + 0.2f, 1.2f, LandingW + 0.2f)), LookMaterials.RustSteel(), new Vector3(BridgeEnd.x, -DeckThick - 1.2f, BridgeEnd.z));
            PropBuilder.Place(dock, rail, new Vector3(BridgeEnd.x - LandingW / 2f + 0.1f, 0f, BridgeEnd.z - 1f), Quaternion.Euler(0f, 90f, 0f)).name = "Landing Rail W";
            PropBuilder.Place(dock, rail, new Vector3(BridgeEnd.x - LandingW / 2f + 0.1f, 0f, BridgeEnd.z + 1f), Quaternion.Euler(0f, 90f, 0f)).name = "Landing Rail W";
            PropBuilder.Place(dock, rail, new Vector3(BridgeEnd.x - 1f, 0f, BridgeEnd.z + LandingW / 2f - 0.1f)).name = "Landing Rail N";
            PropBuilder.Place(dock, rail, new Vector3(BridgeEnd.x + 1f, 0f, BridgeEnd.z + LandingW / 2f - 0.1f)).name = "Landing Rail N";
            PropBuilder.Place(dock, lamp, new Vector3(BridgeEnd.x - LandingW / 2f + 0.1f, 0f, BridgeEnd.z + LandingW / 2f - 0.1f)).name = "Landing Lamp";
            PropBuilder.Place(dock, lamp, new Vector3(BridgeEnd.x - LandingW / 2f + 0.1f, 0f, BridgeEnd.z - LandingW / 2f + 0.1f)).name = "Landing Lamp";
            // The gantry over the bridge's start: two posts and the sign, read from the deck.
            foreach (float side in new[] { -1f, 1f })
                Part(dock, "Gantry Post", MeshKit.Box(new Vector3(0.22f, 3.8f, 0.22f)), LookMaterials.Ink(), new Vector3(bridgeX0 - 1.2f, 0f, BridgeEnd.z + side * (LandingW / 2f + 0.3f)), withCollider: true);
            Part(dock, "Gantry Beam", MeshKit.Box(new Vector3(0.22f, 0.22f, LandingW + 0.8f)), LookMaterials.Ink(), new Vector3(bridgeX0 - 1.2f, 3.7f, BridgeEnd.z));
            GameObject shipSign = PropBuilder.Place(dock, PropBuilder.SignBoard(4f, 1.0f), new Vector3(bridgeX0 - 1.2f, 2.6f, BridgeEnd.z), Quaternion.Euler(0f, 90f, 0f));
            shipSign.name = "Ship Sign";
            Sign(shipSign, "ship.this.way", null);
            Part(dock, "Landing Lip", MeshKit.Box(new Vector3(LandingW, 0.02f, 0.4f)), LookMaterials.Hazard(), new Vector3(BridgeEnd.x, 0f, BridgeEnd.z - LandingW / 2f + 0.2f));
            foreach (float x in new[] { BridgeEnd.x - LandingW / 2f + 0.4f, BridgeEnd.x + LandingW / 2f - 0.4f })
                Part(dock, "Pile", MeshKit.Cylinder(0.4f, -SeabedY - DeckThick, 12), LookMaterials.RustSteel(), new Vector3(x, SeabedY, BridgeEnd.z));
            // The ship, moored stern-on under the landing: its tower's roof is level with
            // the landing and its rail's gap faces it; bow to the south (the way out);
            // nothing on it touching the HQ.
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
            ship.transform.SetParent(dock.transform, true);
            float sternZ = -SunkCost.Editor.Prototype.ShipStubBuilder.DeckLength / 2f;
            float sternWorldZ = BridgeEnd.z - LandingW / 2f - 0.05f; // the tower's aft face against the landing's edge
            ship.transform.SetPositionAndRotation(new Vector3(BridgeEnd.x, ShipDeckY, sternWorldZ + sternZ), Quaternion.Euler(0f, 180f, 0f)); // yaw 180: the stern (local -z) toward the landing, the bow south
            Transform gate = ship.transform.Find(SunkCost.Editor.Prototype.ShipStubBuilder.RoofGateName);
            if (gate != null) gate.gameObject.SetActive(false); // moored: the bridge meets the gap; at sea the gate stays up
        }

        private static void Plank(GameObject root)
        {
            GameObject plank = new(HQPlank.RootName);
            plank.transform.SetParent(root.transform);
            float z = PlankBase.z, edge = DeckW / 2f;
            Part(plank, "Board", MeshKit.Box(new Vector3(3.8f, 0.12f, 0.8f), 0.12f), LookMaterials.PanelDark(), new Vector3(edge + 1.6f, 0.0f, z), withCollider: true);
            Part(plank, "Board Stripe", MeshKit.Box(new Vector3(3.8f, 0.01f, 0.16f)), LookMaterials.Hazard(), new Vector3(edge + 1.6f, 0.0f, z + 0.32f));
            Part(plank, "Board Stripe", MeshKit.Box(new Vector3(3.8f, 0.01f, 0.16f)), LookMaterials.Hazard(), new Vector3(edge + 1.6f, 0.0f, z - 0.32f));
            Part(plank, "Bracket", MeshKit.Box(new Vector3(1.4f, 0.5f, 1.2f), 0.5f), LookMaterials.Ink(), new Vector3(edge + 0.2f, -0.12f, z), withCollider: true);
            Part(plank, "Winch", MeshKit.Cylinder(0.35f, 0.5f, 12), LookMaterials.Ink(), new Vector3(edge + 0.6f, -1.5f, z), Quaternion.Euler(0f, 0f, 90f), withCollider: true);
            GameObject baseAt = new("Plank Base");
            baseAt.transform.SetParent(plank.transform);
            baseAt.transform.SetPositionAndRotation(PlankBase, Quaternion.Euler(0f, 90f, 0f));
            GameObject endAt = new("Plank End");
            endAt.transform.SetParent(plank.transform);
            endAt.transform.SetPositionAndRotation(new Vector3(edge + 3.3f, 0.05f, z), Quaternion.Euler(0f, 90f, 0f));
            GameObject gate = Part(plank, "Plank Gate", MeshKit.Box(new Vector3(0.24f, 1.3f, 2.0f)), LookMaterials.Hazard(), new Vector3(edge - 0.35f, 0f, z), withCollider: true);
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
            EditorUtility.SetDirty(profile);
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
