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
    // The HQ platform (Dan's reference picture, 18 September 2026): an offshore
    // rig of 2300 — one open deck on rusted legs over the water, the booths
    // along the north wall (the office, Upgrades, Gear & Supplies, Intake/Sell,
    // Check in, the Pickup tower with its stairs), the court in the west, the
    // crew's marks in the east, the gangway down to the pontoon and the moored
    // ship on the west, the plank off the south-east corner. Dawn: the sun just
    // up, the lamps still on. Everything is a prop prefab placed here
    // (PropBuilder) or a signed line (HQSigns); nothing gameplay reads any of
    // this — the markers the rules need (spawn points, the plank's base and end,
    // the shop stands and the chute, the board, the panel, the ship) are the same
    // components as before, in new places.
    //
    // Metres, +Z north, the deck's top at y = 0, the water at WaterY.
    public static class HQPlatformBuilder
    {
        public const float DeckW = 60f, DeckD = 36f, DeckThick = 0.6f;
        public const float WaterY = -5f, SeabedY = -10f;
        public const float BoothZ = 15f;           // booth centres; the front (the counter side) at z = 12
        public const float PontoonY = -4f;         // the ship's deck level at the mooring
        public static readonly Vector3 PontoonCentre = new(-47f, PontoonY, -12f);
        public const float PontoonW = 12f, PontoonD = 10f;
        public static readonly Vector3 GangwayLanding = new(-32f, 0f, -12f);
        public static readonly Rect Court = new(-24f, -10f, 20f, 12f);   // x, z, w, d
        public static readonly Vector3 CrewMark = new(10f, 0f, -3f);
        public const string PlatformRootName = "Platform";

        // Shop stands (ShopDisplay) and the chute: where WorldSceneFlow's rules look.
        public static readonly Vector3 UpgradesBoothCentre = new(-16f, 0f, BoothZ);
        public static readonly Vector3 GearBoothCentre = new(-5f, 0f, BoothZ);
        public static readonly Vector3 IntakeBoothCentre = new(6f, 0f, BoothZ);
        public static readonly Vector3 CheckinBoothCentre = new(16f, 0f, BoothZ);
        public static readonly Vector3 PickupBoothCentre = new(25f, 0f, BoothZ);
        public static readonly Vector3 OfficeBoothCentre = new(-26f, 0f, BoothZ);
        public const float PickupFloorY = 4.95f;   // the roof's top: the pickup room's floor
        public static readonly Vector3 PickupChute = new(25f, PickupFloorY + 2.6f, BoothZ + 0.5f);
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
            Crates(platform);
            Sea();
            Dock(shipPrefab);
            Plank(platform);
            Sky(scene);
        }

        // ---- the deck -------------------------------------------------------------------

        private static void Deck(GameObject root)
        {
            Part(root, "Deck", MeshKit.Box(new Vector3(DeckW, DeckThick, DeckD)), LookMaterials.DeckPlate(), new Vector3(0f, -DeckThick, 0f), withCollider: true);
            // The edge band: hazard paint 0.4 m in from the rim.
            Part(root, "Edge S", MeshKit.Box(new Vector3(DeckW, 0.015f, 0.4f)), LookMaterials.Hazard(), new Vector3(0f, 0f, -DeckD / 2f + 0.2f));
            Part(root, "Edge W", MeshKit.Box(new Vector3(0.4f, 0.015f, DeckD)), LookMaterials.Hazard(), new Vector3(-DeckW / 2f + 0.2f, 0f, 0f));
            Part(root, "Edge E", MeshKit.Box(new Vector3(0.4f, 0.015f, DeckD)), LookMaterials.Hazard(), new Vector3(DeckW / 2f - 0.2f, 0f, 0f));
            // Under the deck: the girders and the number on the south face.
            foreach (float z in new[] { -12f, 0f, 12f })
                Part(root, "Girder", MeshKit.Box(new Vector3(DeckW, 0.8f, 0.5f)), LookMaterials.HullPanelDark(), new Vector3(0f, -DeckThick - 0.8f, z));
            foreach (float x in new[] { -24f, 0f, 24f })
                Part(root, "Girder", MeshKit.Box(new Vector3(0.5f, 0.8f, DeckD)), LookMaterials.HullPanelDark(), new Vector3(x, -DeckThick - 0.8f, 0f));
            Part(root, "Skirt S", MeshKit.Box(new Vector3(DeckW, 2.2f, 0.25f)), LookMaterials.HullPanel(), new Vector3(0f, -DeckThick - 2.2f, -DeckD / 2f + 0.125f));
            Part(root, "Skirt N", MeshKit.Box(new Vector3(DeckW, 2.2f, 0.25f)), LookMaterials.HullPanel(), new Vector3(0f, -DeckThick - 2.2f, DeckD / 2f - 0.125f));
            Part(root, "Skirt W", MeshKit.Box(new Vector3(0.25f, 2.2f, DeckD)), LookMaterials.HullPanel(), new Vector3(-DeckW / 2f + 0.125f, -DeckThick - 2.2f, 0f));
            Part(root, "Skirt E", MeshKit.Box(new Vector3(0.25f, 2.2f, DeckD)), LookMaterials.HullPanel(), new Vector3(DeckW / 2f - 0.125f, -DeckThick - 2.2f, 0f));
            GameObject number = PropBuilder.Place(root, PropBuilder.SignBoard(4f, 2f), new Vector3(0f, -DeckThick - 2.3f, -DeckD / 2f - 0.05f), Quaternion.Euler(0f, 180f, 0f));
            number.name = "HQ Number";
            Sign(number, "hq.number", "hq.number.sub");
            Part(root, "Seabed", MeshKit.Box(new Vector3(400f, 1f, 400f)), LookMaterials.Seabed(), new Vector3(0f, SeabedY - 1f, 0f), withCollider: true);
        }

        private static void Legs(GameObject root)
        {
            float height = -DeckThick - SeabedY; // seabed to the deck's underside
            GameObject leg = PropBuilder.Leg(height);
            GameObject fender = PropBuilder.Fender();
            foreach (float x in new[] { -24f, 0f, 24f })
                foreach (float z in new[] { -12f, 12f })
                {
                    GameObject l = PropBuilder.Place(root, leg, new Vector3(x, SeabedY, z));
                    l.name = "Leg";
                    if (z < 0f) PropBuilder.Place(root, fender, new Vector3(x, WaterY - 0.6f, z - 1.6f)).name = "Fender";
                }
        }

        // Railings around the rim, 2 m segments, with gaps for the gangway (west,
        // z -14..-10) and the plank (east, z -18..-14). The north edge is booths.
        private static void Rails(GameObject root)
        {
            GameObject rail = PropBuilder.Rail();
            for (float x = -DeckW / 2f + 1f; x < DeckW / 2f; x += 2f)
                PropBuilder.Place(root, rail, new Vector3(x, 0f, -DeckD / 2f + 0.1f)).name = "Rail S";
            for (float z = -DeckD / 2f + 1f; z < DeckD / 2f - 6f; z += 2f)
            {
                if (z > -14.5f && z < -9.5f) continue; // the gangway
                PropBuilder.Place(root, rail, new Vector3(-DeckW / 2f + 0.1f, 0f, z), Quaternion.Euler(0f, 90f, 0f)).name = "Rail W";
            }
            for (float z = -DeckD / 2f + 1f; z < DeckD / 2f - 6f; z += 2f)
            {
                if (z < -13.5f) continue; // the plank
                PropBuilder.Place(root, rail, new Vector3(DeckW / 2f - 0.1f, 0f, z), Quaternion.Euler(0f, 90f, 0f)).name = "Rail E";
            }
            // The gangway landing outside the west rim, railed on its two sides.
            Part(root, "Gangway Landing", MeshKit.Box(new Vector3(4f, DeckThick, 4f)), LookMaterials.DeckPlate(), new Vector3(GangwayLanding.x, -DeckThick, GangwayLanding.z), withCollider: true);
            PropBuilder.Place(root, rail, new Vector3(GangwayLanding.x, 0f, GangwayLanding.z + 1.9f)).name = "Rail Landing N";
            PropBuilder.Place(root, rail, new Vector3(GangwayLanding.x, 0f, GangwayLanding.z - 1.9f)).name = "Rail Landing S";
            Part(root, "Landing Lip", MeshKit.Box(new Vector3(4f, 0.015f, 0.4f)), LookMaterials.Hazard(), new Vector3(GangwayLanding.x, 0f, GangwayLanding.z + 1.8f));
        }

        private static void Lamps(GameObject root)
        {
            GameObject lamp = PropBuilder.LampPost();
            foreach (float x in new[] { -25f, -15f, -5f, 5f, 15f, 25f })
                PropBuilder.Place(root, lamp, new Vector3(x, 0f, -DeckD / 2f + 0.55f)).name = "Lamp S";
            foreach (float z in new[] { -6f, 4f })
            {
                PropBuilder.Place(root, lamp, new Vector3(-DeckW / 2f + 0.55f, 0f, z)).name = "Lamp W";
                PropBuilder.Place(root, lamp, new Vector3(DeckW / 2f - 0.55f, 0f, z)).name = "Lamp E";
            }
            // Between the booths and the court, two on the deck itself.
            PropBuilder.Place(root, lamp, new Vector3(-9f, 0f, 6f)).name = "Lamp Deck";
            PropBuilder.Place(root, lamp, new Vector3(14f, 0f, 8f)).name = "Lamp Deck";
        }

        // ---- the booths -----------------------------------------------------------------

        private static void Booths(GameObject root, Material tankMaterial, Material lampMaterial)
        {
            Quaternion facingSouth = Quaternion.Euler(0f, 180f, 0f);

            // The office: a booth with a second storey, the company's name on it,
            // the flag and the beacon on the roof.
            GameObject office = PropBuilder.Place(root, PropBuilder.Booth(8f), OfficeBoothCentre, facingSouth);
            office.name = "Office";
            Sign(office.transform.Find("Sign").gameObject, "office", "office.sub");
            GameObject motto = PropBuilder.Place(root, PropBuilder.SignBoard(3.6f, 3.0f), new Vector3(OfficeBoothCentre.x - 4.36f, 5.2f, BoothZ), Quaternion.Euler(0f, -90f, 0f));
            motto.name = "Motto";
            Sign(motto, "motto", null);
            Part(root, "Office Upper", MeshKit.Box(new Vector3(8.6f, 3.6f, 6.6f)), LookMaterials.HullPanel(), new Vector3(OfficeBoothCentre.x, 4.95f, BoothZ), withCollider: true);
            Part(root, "Office Roof", MeshKit.Box(new Vector3(9f, 0.35f, 7f)), LookMaterials.HullPanelDark(), new Vector3(OfficeBoothCentre.x, 8.55f, BoothZ), withCollider: true);
            GameObject company = PropBuilder.Place(root, PropBuilder.SignBoard(7f, 2.6f), new Vector3(OfficeBoothCentre.x, 5.4f, BoothZ - 3.36f), facingSouth);
            company.name = "Company Sign";
            Sign(company, "company", "company.sub");
            foreach (float y in new[] { 5.3f, 7.6f })
                PropBuilder.Place(root, PropBuilder.LightStrip(6f), new Vector3(OfficeBoothCentre.x, y, BoothZ - 3.4f)).name = "Office Strip";
            PropBuilder.Place(root, PropBuilder.FlagPole(), new Vector3(OfficeBoothCentre.x - 3.5f, 8.9f, BoothZ + 2.5f)).name = "Flag";
            PropBuilder.Place(root, PropBuilder.BeaconMast(), new Vector3(OfficeBoothCentre.x + 3f, 8.9f, BoothZ + 2.5f)).name = "Beacon";

            // Upgrades: click and it is yours — the large tank and the bright lamp on stands behind the counter.
            GameObject upgrades = PropBuilder.Place(root, PropBuilder.Booth(10f), UpgradesBoothCentre, facingSouth);
            upgrades.name = "Upgrades";
            Sign(upgrades.transform.Find("Sign").gameObject, "upgrades", "upgrades.sub");

            // Gear & Supplies: consumables; what you buy lands upstairs in the pickup room.
            GameObject gear = PropBuilder.Place(root, PropBuilder.Booth(10f), GearBoothCentre, facingSouth);
            gear.name = SunkCost.Editor.Prototype.HQPrototypeBuilder.ShopRoomName;
            Sign(gear.transform.Find("Sign").gameObject, "gear", "gear.sub");

            // Intake / Sell: the quota board as a console on the counter, the big
            // quotas display above the roof.
            GameObject intake = PropBuilder.Place(root, PropBuilder.Booth(10f), IntakeBoothCentre, facingSouth);
            intake.name = "Intake";
            Sign(intake.transform.Find("Sign").gameObject, "intake", "intake.sub");
            QuotaBoard(root, new Vector3(IntakeBoothCentre.x, 1.75f, BoothZ - 1.0f)); // on the counter top, tilted to the buyer
            GameObject quotas = PropBuilder.Place(root, PropBuilder.SignBoard(9f, 3.2f), new Vector3(IntakeBoothCentre.x, 5.1f, BoothZ + 1.5f), facingSouth);
            quotas.name = "Quotas Display";
            Sign(quotas, "quota.title", null);
            GameObject side = PropBuilder.Place(root, PropBuilder.SignBoard(3.2f, 3.2f), new Vector3(IntakeBoothCentre.x + 6.3f, 5.1f, BoothZ + 1.5f), facingSouth);
            side.name = "Quotas Side";
            Sign(side, "quota.side", null);
            foreach (float x in new[] { -3.5f, 3.5f })
                Part(root, "Display Post", MeshKit.Box(new Vector3(0.2f, 1.2f, 0.2f)), LookMaterials.RailDark(), new Vector3(IntakeBoothCentre.x + x, 4.95f, BoothZ + 1.5f));

            // Check in: your colour and your name — the colour panel as a console on the counter.
            GameObject checkin = PropBuilder.Place(root, PropBuilder.Booth(8f), CheckinBoothCentre, facingSouth);
            checkin.name = "Check In";
            Sign(checkin.transform.Find("Sign").gameObject, "checkin", "checkin.sub");
            ColourPanel(root, new Vector3(CheckinBoothCentre.x, 1.75f, BoothZ - 1.0f));
            Part(root, "Check In Screen", MeshKit.Box(new Vector3(1.6f, 0.9f, 0.06f)), LookMaterials.ScreenTeal(), new Vector3(CheckinBoothCentre.x - 2.2f, 1.6f, BoothZ + 2.6f));

            // Pickup: a booth (a store below) with the room on its roof where bought
            // gear lands, reached by the stairs up the east side.
            GameObject pickup = PropBuilder.Place(root, PropBuilder.Booth(8f), PickupBoothCentre, facingSouth);
            pickup.name = "Pickup";
            Sign(pickup.transform.Find("Sign").gameObject, "pickup", "pickup.sub");
            PickupRoom(root);

            // The stands: ShopDisplay components the shop's rules find by item id.
            SunkCost.Shop.ShopDeliveryPoint chute = Chute(root);
            Material plinth = LookMaterials.HullPanelDark();
            Stand(root, chute, SunkCost.Shop.ShopCatalog.LargeTankId, new Vector3(UpgradesBoothCentre.x - 2f, 0.12f, BoothZ + 0.6f), PrimitiveType.Capsule, new Vector3(0.3f, 0.45f, 0.3f), tankMaterial, plinth);
            Stand(root, chute, SunkCost.Shop.ShopCatalog.BrightHeadlampId, new Vector3(UpgradesBoothCentre.x + 2f, 0.12f, BoothZ + 0.6f), PrimitiveType.Sphere, new Vector3(0.35f, 0.35f, 0.35f), lampMaterial, plinth);
            Stand(root, chute, SunkCost.Shop.ShopCatalog.AirTankId, new Vector3(GearBoothCentre.x, 0.12f, BoothZ + 0.6f), PrimitiveType.Capsule, new Vector3(0.22f, 0.3f, 0.22f), tankMaterial, plinth);
        }

        // The room on the pickup booth's roof: walls with an opening to the south
        // where the stairs arrive, a roof, a landing disc under the chute.
        private static void PickupRoom(GameObject root)
        {
            float x = PickupBoothCentre.x, y = PickupFloorY;
            const float w = 8f, d = 6f, h = 3.2f, wall = 0.3f;
            Part(root, "Pickup Room Floor", MeshKit.Box(new Vector3(w, 0.12f, d)), LookMaterials.DeckPlate(), new Vector3(x, y, BoothZ), withCollider: true);
            Part(root, "Pickup Room N", MeshKit.Box(new Vector3(w, h, wall)), LookMaterials.HullPanel(), new Vector3(x, y, BoothZ + d / 2f - wall / 2f), withCollider: true);
            Part(root, "Pickup Room W", MeshKit.Box(new Vector3(wall, h, d)), LookMaterials.HullPanel(), new Vector3(x - w / 2f + wall / 2f, y, BoothZ), withCollider: true);
            Part(root, "Pickup Room E", MeshKit.Box(new Vector3(wall, h, d)), LookMaterials.HullPanel(), new Vector3(x + w / 2f - wall / 2f, y, BoothZ), withCollider: true);
            // The south wall with a 3 m opening at its east end (x+1..x+4), a window west of it.
            Part(root, "Pickup Room S", MeshKit.Box(new Vector3(5f, h, wall)), LookMaterials.HullPanel(), new Vector3(x - 1.5f, y, BoothZ - d / 2f + wall / 2f), withCollider: true);
            Part(root, "Pickup Room Lintel", MeshKit.Box(new Vector3(3f, 0.8f, wall)), LookMaterials.HullPanel(), new Vector3(x + 2.5f, y + h - 0.8f, BoothZ - d / 2f + wall / 2f), withCollider: true);
            Part(root, "Pickup Room Window", MeshKit.Box(new Vector3(2.4f, 1.0f, 0.08f)), LookMaterials.ScreenTeal(), new Vector3(x - 1.5f, y + 1.4f, BoothZ - d / 2f - 0.02f));
            Part(root, "Pickup Room Roof", MeshKit.Box(new Vector3(w + 0.6f, 0.3f, d + 0.6f)), LookMaterials.HullPanelDark(), new Vector3(x, y + h, BoothZ), withCollider: true);
            PropBuilder.Place(root, PropBuilder.LightStrip(4f), new Vector3(x, y + h - 0.02f, BoothZ)).name = "Pickup Strip";
            PropBuilder.Place(root, PropBuilder.LightStrip(2f), new Vector3(x + 2.5f, y + h - 0.02f, BoothZ - d / 2f - 0.3f)).name = "Pickup Door Strip";
            // The landing disc under the chute.
            GameObject disc = new("Shop Landing");
            disc.transform.SetParent(root.transform);
            disc.transform.position = new Vector3(PickupChute.x, y + 0.13f, PickupChute.z);
            disc.AddComponent<MeshFilter>().sharedMesh = MeshKit.Cylinder(0.9f, 0.02f, 24);
            disc.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.DeckMarking();
            // The stairs: up the east rim from z = 4 to the room's opening at z = 12; a landing at the top.
            GameObject stairs = PropBuilder.Place(root, PropBuilder.Stairs(y, 7f, 1.6f), new Vector3(x + 3f, 0f, 4f));
            stairs.name = "Pickup Stairs";
            Part(root, "Pickup Landing", MeshKit.Box(new Vector3(1.6f, 0.12f, 1.2f)), LookMaterials.DeckPlate(), new Vector3(x + 3f, y, 11.5f), withCollider: true);
            Part(root, "Pickup Landing Rail", MeshKit.Box(new Vector3(0.06f, 1.1f, 1.2f)), LookMaterials.RailYellow(), new Vector3(x + 3.8f, y, 11.5f));
        }

        // The chute in the pickup room's ceiling: the ShopDeliveryPoint every consumable stand delivers to.
        private static SunkCost.Shop.ShopDeliveryPoint Chute(GameObject root)
        {
            GameObject chute = new(SunkCost.Shop.ShopDeliveryPoint.DefaultName);
            chute.transform.SetParent(root.transform);
            chute.transform.position = PickupChute;
            chute.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.9f, 0.6f, 0.9f), 0.3f);
            chute.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.HullPanelDark();
            return chute.AddComponent<SunkCost.Shop.ShopDeliveryPoint>();
        }

        private static void Stand(GameObject root, SunkCost.Shop.ShopDeliveryPoint delivery, string itemId, Vector3 at, PrimitiveType shape, Vector3 shapeScale, Material shapeMaterial, Material plinthMaterial)
        {
            GameObject stand = new("Shop Stand " + itemId);
            stand.transform.SetParent(root.transform);
            stand.transform.position = at;
            stand.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // faces the counter (south)
            GameObject plinth = new("Plinth");
            plinth.transform.SetParent(stand.transform, false);
            plinth.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.8f, 1.0f, 0.8f));
            plinth.AddComponent<MeshRenderer>().sharedMaterial = plinthMaterial;
            BoxCollider pc = plinth.AddComponent<BoxCollider>(); pc.center = new Vector3(0f, 0.5f, 0f); pc.size = new Vector3(0.8f, 1f, 0.8f);
            GameObject stripe = new("Stripe");
            stripe.transform.SetParent(stand.transform, false);
            stripe.transform.localPosition = new Vector3(0f, 0.15f, 0.41f);
            stripe.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.8f, 0.12f, 0.01f));
            stripe.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.Hazard();
            GameObject thing = GameObject.CreatePrimitive(shape);
            thing.name = "Display";
            thing.transform.SetParent(stand.transform, false);
            thing.transform.localPosition = new Vector3(0f, 1.0f + shapeScale.y * 0.5f + 0.02f, 0f);
            thing.transform.localScale = shapeScale;
            thing.GetComponent<Renderer>().sharedMaterial = shapeMaterial;
            GameObject text = new("Label", typeof(TextMesh));
            text.transform.SetParent(stand.transform, false);
            text.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // a TextMesh reads along its +Z
            TextMesh mesh = text.GetComponent<TextMesh>();
            mesh.characterSize = 0.05f;
            mesh.fontSize = 48;
            mesh.fontStyle = FontStyle.Bold;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(1f, 0.80f, 0.45f);
            text.AddComponent<DepthText>().Configure(LookMaterials.DepthText());
            stand.AddComponent<SunkCost.Shop.ShopDisplay>().Configure(itemId, mesh, delivery);
        }

        // The quota board as a counter console: a tilted dark screen with the crew's
        // money on it. Look at it and press E to pay (QuotaBoard, unchanged).
        private static void QuotaBoard(GameObject root, Vector3 at)
        {
            GameObject board = new(SunkCost.World.QuotaBoard.BoardName);
            board.transform.SetParent(root.transform);
            board.transform.position = at;
            board.transform.rotation = Quaternion.Euler(-25f, 180f, 0f); // tilted up toward whoever stands at the counter
            GameObject plate = new("Board Plate");
            plate.transform.SetParent(board.transform, false);
            plate.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(3.0f, 0.9f, 0.06f), 0.45f);
            plate.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.SignBoard();
            GameObject frame = new("Frame");
            frame.transform.SetParent(board.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            frame.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(3.06f, 0.96f, 0.02f), 0.48f);
            frame.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.ScreenTeal();
            frame.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            GameObject text = new("Board Text", typeof(TextMesh));
            text.transform.SetParent(board.transform, false);
            text.transform.localPosition = new Vector3(0f, 0f, 0.04f);
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
            box.size = new Vector3(3.0f, 0.9f, 0.1f);
            board.AddComponent<SunkCost.World.QuotaBoard>().Configure(mesh);
        }

        // The colour panel as a counter console (ColourPanel, unchanged).
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
            back.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(1.1f, 0.85f, 0.06f), 0.425f);
            back.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.SignBoard();
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
            Part(root, "Court Paint", MeshKit.Box(new Vector3(Court.width, 0.01f, Court.height)), LookMaterials.CourtPaint(), new Vector3(c.x, 0f, c.z));
            Line(root, new Vector3(c.x, 0f, Court.y + 0.05f), Court.width, 0.1f);
            Line(root, new Vector3(c.x, 0f, Court.yMax - 0.05f), Court.width, 0.1f);
            Line(root, new Vector3(Court.x + 0.05f, 0f, c.z), 0.1f, Court.height);
            Line(root, new Vector3(Court.xMax - 0.05f, 0f, c.z), 0.1f, Court.height);
            Line(root, new Vector3(c.x, 0f, c.z), 0.1f, Court.height); // the centre line
            foreach (float sx in new[] { -1f, 1f })
            {
                float keyX = c.x + sx * (Court.width / 2f - 2.9f);
                Line(root, new Vector3(keyX, 0f, c.z + 2.45f), 5.8f, 0.1f);
                Line(root, new Vector3(keyX, 0f, c.z - 2.45f), 5.8f, 0.1f);
                Line(root, new Vector3(c.x + sx * (Court.width / 2f - 5.8f), 0f, c.z), 0.1f, 5.0f);
            }
            GameObject hoop = PropBuilder.Hoop();
            PropBuilder.Place(root, hoop, new Vector3(Court.x + 0.6f, 0f, c.z), Quaternion.Euler(0f, 90f, 0f)).name = "Hoop W";
            PropBuilder.Place(root, hoop, new Vector3(Court.xMax - 0.6f, 0f, c.z), Quaternion.Euler(0f, -90f, 0f)).name = "Hoop E";
            GameObject courtSign = PropBuilder.Place(root, PropBuilder.DeckMarking(6f, 1.6f), new Vector3(c.x, 0.012f, Court.yMax + 1.2f));
            courtSign.name = "Court Sign";
            Sign(courtSign, "court", null);
        }

        private static void Line(GameObject root, Vector3 at, float w, float d) =>
            Part(root, "Line", MeshKit.Box(new Vector3(w, 0.012f, d)), LookMaterials.DeckMarking(), new Vector3(at.x, 0.005f, at.z));

        private static void Markings(GameObject root)
        {
            GameObject crew = PropBuilder.Place(root, PropBuilder.DeckMarking(12f, 8f), new Vector3(CrewMark.x, 0.012f, CrewMark.z));
            crew.name = "Crew Here";
            Sign(crew, "crew.here", null);
            GameObject ship = PropBuilder.Place(root, PropBuilder.DeckMarking(7f, 2.2f), new Vector3(-25f, 0.012f, GangwayLanding.z), Quaternion.Euler(0f, -90f, 0f)); // read walking west
            ship.name = "Ship This Way";
            Sign(ship, "ship.this.way", null);
            GameObject plank = PropBuilder.Place(root, PropBuilder.DeckMarking(5f, 1.6f), new Vector3(26.5f, 0.012f, PlankBase.z), Quaternion.Euler(0f, 90f, 0f)); // read walking east
            plank.name = "Plank Marking";
            Sign(plank, "plank", null);
            GameObject shipSign = PropBuilder.Place(root, PropBuilder.SignBoard(3.6f, 1.0f), new Vector3(-DeckW / 2f + 0.5f, 1.3f, GangwayLanding.z + 3.2f), Quaternion.Euler(0f, 90f, 0f)); // beside the gap, read from the deck
            shipSign.name = "Ship Sign";
            Sign(shipSign, "ship.this.way", null);
            GameObject plankSign = PropBuilder.Place(root, PropBuilder.SignBoard(3.0f, 0.9f), new Vector3(DeckW / 2f - 0.4f, 1.3f, PlankBase.z + 2.2f), Quaternion.Euler(0f, -90f, 0f));
            plankSign.name = "Plank Sign";
            Sign(plankSign, "plank", null);
            GameObject towerMotto = PropBuilder.Place(root, PropBuilder.SignBoard(2.4f, 2.4f), new Vector3(PickupBoothCentre.x + 4.36f, PickupFloorY + 0.4f, BoothZ), Quaternion.Euler(0f, 90f, 0f));
            towerMotto.name = "Tower Motto";
            Sign(towerMotto, "tower.motto", null);
        }

        private static void Crates(GameObject root)
        {
            GameObject grey = PropBuilder.Crate("Grey"), red = PropBuilder.Crate("Red"), green = PropBuilder.Crate("Green"), yellow = PropBuilder.Crate("Yellow");
            void C(GameObject prefab, float x, float y, float z, float yaw = 0f) => PropBuilder.Place(root, prefab, new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f)).name = "Crate";
            C(grey, -27.5f, 0f, -15.8f); C(red, -26.2f, 0f, -15.9f, 8f); C(green, -26.9f, 1.0f, -15.85f, -5f);
            C(yellow, 4.5f, 0f, -16f, 12f); C(grey, 5.8f, 0f, -15.7f, -3f);
            C(red, 19f, 0f, -2f, 30f); C(grey, 20.3f, 0f, -3.2f, 20f); C(yellow, 19.6f, 1.0f, -2.6f, 25f);
            C(green, 2f, 0f, 3f, -15f); C(grey, 3.4f, 0f, 3.2f, 5f);
            C(red, 28.5f, 0f, 6f); C(grey, 28.5f, 0f, 7.3f); C(green, 28.5f, 1.0f, 6.65f, 3f);
            C(grey, -29f, 0f, 8f, 90f); C(yellow, -29f, 0f, 9.3f, 90f);
            C(grey, PickupBoothCentre.x - 2.6f, PickupFloorY + 0.12f, BoothZ + 1.8f, 15f); C(red, PickupBoothCentre.x - 1.3f, PickupFloorY + 0.12f, BoothZ + 2.1f, -10f);
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
            far.transform.position = new Vector3(0f, WaterY - 0.08f, 0f);
            far.AddComponent<MeshFilter>().sharedMesh = MeshKit.Plane(12000f, 12000f, 96, 96); // to the horizon; the fog takes it (per-vertex fog wants small triangles)
            far.AddComponent<MeshRenderer>().sharedMaterial = water;
        }

        // The pontoon at the ship's level, down the stairs from the gangway landing;
        // the ship moored west of it, its stern and its own gangway toward it,
        // its bow west (it sails out along DepartureDirection).
        private static void Dock(GameObject shipPrefab)
        {
            GameObject dock = new("Dock");
            Part(dock, "Pontoon", MeshKit.Box(new Vector3(PontoonW, DeckThick, PontoonD)), LookMaterials.DeckPlate(), new Vector3(PontoonCentre.x, PontoonY - DeckThick, PontoonCentre.z), withCollider: true);
            Part(dock, "Pontoon Lip", MeshKit.Box(new Vector3(PontoonW, 0.015f, 0.4f)), LookMaterials.Hazard(), new Vector3(PontoonCentre.x, PontoonY, PontoonCentre.z + PontoonD / 2f - 0.2f));
            Part(dock, "Pontoon Lip S", MeshKit.Box(new Vector3(PontoonW, 0.015f, 0.4f)), LookMaterials.Hazard(), new Vector3(PontoonCentre.x, PontoonY, PontoonCentre.z - PontoonD / 2f + 0.2f));
            foreach (float x in new[] { -4f, 0f, 4f })
                foreach (float z in new[] { -4f, 4f })
                    PropBuilder.Place(dock, PropBuilder.Bollard(), new Vector3(PontoonCentre.x + x, PontoonY, PontoonCentre.z + z)).name = "Bollard";
            // Floats under the pontoon and two piles.
            foreach (float x in new[] { -4.5f, 4.5f })
                Part(dock, "Pile", MeshKit.Cylinder(0.35f, PontoonY - SeabedY + 1.5f, 12), LookMaterials.RustSteel(), new Vector3(PontoonCentre.x + x, SeabedY, PontoonCentre.z + PontoonD / 2f + 0.6f));
            Part(dock, "Float", MeshKit.Box(new Vector3(PontoonW - 1f, 1.6f, PontoonD - 1f)), LookMaterials.HullPanelDark(), new Vector3(PontoonCentre.x, PontoonY - DeckThick - 1.6f, PontoonCentre.z));
            // The stairs down from the gangway landing: rise 4 m over 7 m toward the west; pivot at the bottom, rising east.
            GameObject stairs = PropBuilder.Place(dock, PropBuilder.Stairs(-PontoonY, 7f, 3f), new Vector3(GangwayLanding.x - 2f - 7f, PontoonY, GangwayLanding.z), Quaternion.Euler(0f, 90f, 0f));
            stairs.name = "Gangway Stairs";
            PropBuilder.Place(dock, PropBuilder.LampPost(), new Vector3(PontoonCentre.x + PontoonW / 2f - 0.6f, PontoonY, PontoonCentre.z + PontoonD / 2f - 0.6f)).name = "Pontoon Lamp";
            PropBuilder.Place(dock, PropBuilder.LampPost(), new Vector3(PontoonCentre.x + PontoonW / 2f - 0.6f, PontoonY, PontoonCentre.z - PontoonD / 2f + 0.6f)).name = "Pontoon Lamp";
            // The ship.
            GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(shipPrefab);
            ship.transform.SetParent(dock.transform, true);
            ShipParts parts = ship.GetComponent<ShipParts>();
            Transform boarding = parts != null ? parts.BoardingPoint : null;
            float sternZ = boarding != null ? boarding.localPosition.z : -SunkCost.Editor.Prototype.ShipStubBuilder.DeckLength / 2f;
            float tipZ = sternZ - SunkCost.Editor.Prototype.ShipStubBuilder.GangwayLength; // local, negative
            float tipX = PontoonCentre.x - PontoonW / 2f + SunkCost.Editor.Prototype.HQPrototypeBuilder.PierOverlap; // the tip rests this far onto the pontoon
            Quaternion yaw = Quaternion.Euler(0f, -90f, 0f); // bow west, stern (and gangway) east toward the pontoon
            ship.transform.SetPositionAndRotation(new Vector3(tipX + tipZ, PontoonY, PontoonCentre.z), yaw);
        }

        private static void Plank(GameObject root)
        {
            GameObject plank = new(HQPlank.RootName);
            plank.transform.SetParent(root.transform);
            float z = PlankBase.z;
            float edge = DeckW / 2f;
            Part(plank, "Board", MeshKit.Box(new Vector3(3.6f, 0.08f, 0.7f), 0.08f), LookMaterials.Plank(), new Vector3(edge + 1.5f, 0.0f, z), withCollider: true);
            Part(plank, "Bracket", MeshKit.Box(new Vector3(1.2f, 0.3f, 0.9f), 0.3f), LookMaterials.HullPanelDark(), new Vector3(edge + 0.2f, -0.08f, z));
            GameObject baseAt = new("Plank Base");
            baseAt.transform.SetParent(plank.transform);
            baseAt.transform.SetPositionAndRotation(PlankBase, Quaternion.Euler(0f, 90f, 0f)); // facing +x, out over the water
            GameObject endAt = new("Plank End");
            endAt.transform.SetParent(plank.transform);
            endAt.transform.SetPositionAndRotation(new Vector3(edge + 3.1f, 0.05f, z), Quaternion.Euler(0f, 90f, 0f));
            GameObject gate = Part(plank, "Plank Gate", MeshKit.Box(new Vector3(0.2f, 1.2f, 1.8f)), LookMaterials.Hazard(), new Vector3(edge - 0.3f, 0f, z), withCollider: true);
            plank.AddComponent<HQPlank>().Configure(baseAt.transform, endAt.transform, WaterY, gate);
        }

        // ---- the sky ------------------------------------------------------------------

        // Dawn: the sun just up in the east, warm and low; a pale sky; a haze that
        // eats the horizon; a bloom volume for the strips and the beacon.
        private static void Sky(Scene scene)
        {
            GameObject sky = new("Sky");
            GameObject sunGo = new("Sun", typeof(Light));
            sunGo.transform.SetParent(sky.transform);
            sunGo.transform.rotation = Quaternion.Euler(16f, -40f, 0f);
            Light sun = sunGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.84f, 0.66f);
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            // A cool fill from the sky's side so the shadows are not black.
            GameObject fillGo = new("Sky Fill", typeof(Light));
            fillGo.transform.SetParent(sky.transform);
            fillGo.transform.rotation = Quaternion.Euler(50f, 140f, 0f);
            Light fill = fillGo.GetComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.55f, 0.70f, 0.95f); fill.intensity = 0.4f; fill.shadows = LightShadows.None;
            Material skybox = SkyboxMaterial();
            RenderSettings.skybox = skybox;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.76f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.78f, 0.70f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.18f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.76f, 0.86f);
            RenderSettings.fogStartDistance = 100f;
            RenderSettings.fogEndDistance = 450f;
            sky.AddComponent<SkyEnvironment>(); // the ambient probe and the reflections from this sky, at load
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
            if (m == null)
            {
                Shader shader = Shader.Find("Skybox/Procedural");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, SkyboxPath);
            }
            m.SetFloat("_SunSize", 0.05f);
            m.SetFloat("_SunSizeConvergence", 5f);
            m.SetFloat("_AtmosphereThickness", 0.8f);
            m.SetColor("_SkyTint", new Color(0.42f, 0.58f, 0.86f));
            m.SetColor("_GroundColor", new Color(0.70f, 0.76f, 0.86f)); // the fog's colour: the sea fades into it
            m.SetFloat("_Exposure", 1.3f);
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
            bloom.threshold.overrideState = true; bloom.threshold.value = 0.85f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.9f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.7f;
            if (!profile.TryGet(out Tonemapping tonemapping)) tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true; tonemapping.mode.value = TonemappingMode.ACES;
            if (!profile.TryGet(out ColorAdjustments colour)) colour = profile.Add<ColorAdjustments>(true);
            colour.active = true;
            colour.postExposure.overrideState = true; colour.postExposure.value = 0.15f;
            colour.contrast.overrideState = true; colour.contrast.value = 8f;
            colour.saturation.overrideState = true; colour.saturation.value = 6f;
            if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>(true);
            vignette.active = true;
            vignette.intensity.overrideState = true; vignette.intensity.value = 0.18f;
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ---- helpers --------------------------------------------------------------------

        private static GameObject Part(GameObject root, string name, Mesh mesh, Material material, Vector3 position, bool withCollider = false)
        {
            GameObject go = new(name);
            go.transform.SetParent(root.transform);
            go.transform.position = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (withCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        // Point a placed sign (or a booth's nested sign) at its HQSigns lines.
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
                // No second line: the title sits in the middle of the whole board.
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
