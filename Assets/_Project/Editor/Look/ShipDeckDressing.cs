using System.Collections.Generic;
using System.Linq;
using SunkCost.Editor.Prototype;
using SunkCost.World;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The ship as Dan's generated parts (23 September 2026): the code-built boxes
    // stop being seen and start being only what they always were underneath —
    // colliders and trigger volumes in exactly the same places — and the models
    // stand where the boxes stood. Nothing about the rules moves: the well, the
    // doorways, the spawn points, the aboard and safe-deck volumes, the storage
    // room's inside and the tower's roof keep their measurements.
    //
    // The hull is more than dressing. Its flattened deck is the floor the crew walks
    // on (the hull's own mesh is the collider, cut to the hull's own outline), and
    // the ship's side - a 1.2 m steel bulwark built on that outline all the way
    // round, against a 65 cm jump - is the wall that keeps them aboard (Dan: "not
    // real walls - the ship itself", and no fences). Everything along the edge
    // (lamps, lifebuoys, the name) follows the same outline, read off the mesh. Every
    // prop is solid (Dan: "all the things in the ship are walk through"), and no prop
    // stands in another: each one takes its patch of deck, and the rows along the
    // rail give way to whatever is already there (ship audit SHIP-004).
    //
    // The lounge is the bow: the deck TV faces aft from the point, the couches in
    // front of it, the table between its benches behind them. The working end is the
    // stern: the crane, the cargo, the storage room, beside the tower. The elevator
    // stays the game's own glass car; a look for it is a separate job (Dan: "we will
    // do a new one after").
    //
    // Run from the ship stub builder, last, after every box exists to be hidden.
    public static class ShipDeckDressing
    {
        public const string LookName = "Look";

        // The bulwark's height over the deck, set in prepare_ship_part.py
        // (HULL_BULWARK) where the hull is cut to it.
        public const float Bulwark = 1.2f;

        // The props' scale over the models' own metres: the one table every prop is
        // placed from (ship audit SHIP-068). Dan, 23 September 2026, with the player
        // kept at 1.8 m: what a person uses at a person's size (the models are made
        // so), deck gear a little over it, the container a real 20 ft box (6 x 2.6 x
        // 2.6 m at one), what a crane lifts big. The hull, the tower and the storage
        // room are the game's own sizes and stay at one.
        public const float MachineScale = 2f;     // the crane, the winch, the deck TV
        public const float GearScale = 1.25f;     // barrels, crates, lamps, bollards, buoys, pipes, coils, signs
        public const float FurnitureScale = 1f;   // couch, table, bench, toolbox
        private static readonly Dictionary<string, float> Scales = new()
        {
            ["Hull"] = 1f, ["Tower"] = 1f, ["StorageRoom"] = 1f, ["StorageSill"] = 1f, // fitted to the game's own measurements below
            ["Couch"] = FurnitureScale, ["Table"] = FurnitureScale, ["Bench"] = FurnitureScale, ["Toolbox"] = FurnitureScale,
            ["Barrel"] = GearScale, ["Crate"] = GearScale, ["DeckLamp"] = GearScale, ["Bollard"] = GearScale,
            ["Lifebuoy"] = GearScale, ["Pipes"] = GearScale, ["CableCoil"] = GearScale, ["Signs"] = GearScale,
            ["Container"] = 1f,
            ["Crane"] = MachineScale, ["Winch"] = MachineScale,
            ["TvCabinet"] = MachineScale, // the big screen Dan asked for (5107627), lowered rather than shrunk (SHIP-026)
            ["Console"] = 1.5f,           // its desk at 0.8-1.1 m and its display at eye height
            ["NamePlate"] = 6f,           // 7.3 x 1.3 m: the ship's name read from 45 m at sea (SHIP-020); a plate, so its depth is halved at the hull
        };
        private static float ScaleOf(string part)
        {
            if (Scales.TryGetValue(part, out float s)) return s;
            Debug.LogWarning("Ship dressing: no scale for " + part + "; placed at one");
            return 1f;
        }

        // What the models never touch: the glass cabin, the monitor and its buttons,
        // the screens the game writes on. Exact names, and everything under them (a
        // prefix here once kept the monitor's old desk boxes seen: SHIP-009).
        private static readonly HashSet<string> Kept = new()
        {
            ShipParts.DeckCabinName, ShipParts.MonitorName, ShipParts.MonitorStatusName,
            ShipParts.MonitorButtonSite01Name, ShipParts.MonitorButtonHQName, ShipParts.MonitorButtonEndDayName,
            ShipParts.TvScreenName, "Tv Caption Sign", ShipParts.TvSpeakerName,
            "Crew Screen", "Crew Screen Frame", "Crew Screen Text",
            "Storage Sign", // the room's readout: the game writes on it
        };

        // What loses its renderer and keeps its collider: the storage room's walls,
        // roof, lintel, sill and taped floor, which the room model now covers.
        private const string HiddenPrefix = "Storage";

        // What goes entirely, renderer and collider: the decoration the models stand
        // in for - the old tower block (the model's own shape is what the crew
        // collides with), the monitor's desk boxes, the painted hull names (the name
        // plate carries the name), and the tower roof's rails, lamps and gate (Dan,
        // 23 September 2026: "remove those gates and the invisible platform they are
        // on"). Any of them may already be gone from the stub builder.
        private static readonly HashSet<string> Removed = new()
        {
            "Tower", "Tower Base Trim", "Tower Floor Line", "Tower Roof Edge", "Tower Roof Plates",
            "Monitor Frame", "Monitor Console", "Monitor Console Stripe",
            "Hull Name", "Hull Year",
            "Bridge Windows", "Bridge Windows Side", "Bridge Label", "Bridge Label Text", "Bridge Door", "Bridge Door Light", "Bridge Sign",
            "TvPost", "TvFrame", "Fender", "Tyre", "Radar Mast", "Radar Dome", "Antenna", "Funnel", "Funnel Band", "Funnel Cap", "Beacon",
            "Roof Rail", "Roof Lamp", "Roof Gate", "Roof Gap Stripe",
        };
        private const string RemovedFamily = "Hull "; // the hull's old bands: "Hull N", "Hull Band E", "Hull Trim Low S"...

        // What collides by its own shape rather than a box: the tower, the crane (the
        // crew walks under its boom), the console (a box would stop the crosshair
        // short of its buttons: SHIP-008) and the TV (a box was a wall under and in
        // front of the screen: SHIP-038).
        private static readonly HashSet<string> ByMesh = new() { "Tower", "Crane", "Console", "TvCabinet" };
        // What is not a deck prop: the hull is the deck, the name plate hangs outboard,
        // the storage room and its sill are the game's own colliders.
        private static readonly HashSet<string> NotSolid = new() { "Hull", "NamePlate", "StorageRoom", "StorageSill" };


        private const float WellClear = 0.8f;       // a walk round the well's rail, outside it
        private const float TvScreenCentreY = 2.0f; // seated eyes at about 1.15 m, standing 1.6 m (SHIP-026)
        private const float ConsoleX = -2.2f;       // port of the tower model's door and ladder, which it hid (SHIP-039)
        // A patch of open, flat deck starboard of the well's rail that no prop may take:
        // where WorldLoopRuntimeChecks drops the ball that must ride the trip at its spot
        // (row D7; its old spot became the crane's base with SHIP-012).
        public static readonly Vector3 OpenDeck = new(5.5f, 0f, 1.0f);
        private const float CaptionCharacterSize = 0.042f;

        private static Transform shipRoot;
        private static float halfL, halfW;
        private static readonly List<(GameObject, bool)> solid = new(); // placed props and whether they collide by mesh

        public static void Build(Transform root)
        {
            // The old dressing goes entirely; the models replace it.
            Transform old = root.Find(LookName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            shipRoot = root;
            solid.Clear();
            taken.Clear();
            towerFace.Clear();
            towerBack.Clear();
            StripStubs(root);

            GameObject look = new(LookName);
            look.transform.SetParent(root, false);

            halfL = ShipStubBuilder.DeckLength / 2f;
            halfW = ShipStubBuilder.DeckWidth / 2f;
            float towerZ = -halfL + ShipStubBuilder.TowerDepth / 2f;
            float towerFront = -halfL + ShipStubBuilder.TowerDepth;
            float towerHalf = ShipStubBuilder.TowerWidth / 2f;

            // ---- the ship itself -------------------------------------------------

            // The hull's deck is at its origin (prepare_ship_part's hull steps): the
            // ship's origin is the deck, so the hull stands at zero, at its own size.
            GameObject hull = Place(look, "Hull", Vector3.zero, 0f);
            SampleOutline(root, hull);
            BuildBulwark(look);
            // The tower: the model's own shape is its collider, and the patch of deck
            // it stands on is read off its mesh up to head height - its face is not
            // where its bounds say (SHIP-013).
            GameObject tower = Place(look, "Tower", new Vector3(0f, 0f, towerZ), 0f);
            RegisterTower(tower);
            float sx = ShipStubBuilder.StorageCentreX, sz = ShipStubBuilder.StorageCentreZ;
            Keep("StorageRoom", new Vector2(sx - ShipStubBuilder.StorageWidth / 2f, sz - ShipStubBuilder.StorageDepth / 2f), new Vector2(sx + ShipStubBuilder.StorageWidth / 2f, sz + ShipStubBuilder.StorageDepth / 2f));

            // The walkable rim between the hull's deck and the round hole wears the
            // deck's plate, like the hull's deck faces do (its UVs are in metres too).
            Material deck = ShipModelSetup.DeckMaterial();
            foreach (string rim in new[] { "Well Rim", "Well Rim Under" })
            {
                Transform t = root.Find(rim);
                if (t != null && t.TryGetComponent(out MeshRenderer renderer)) renderer.sharedMaterial = deck;
            }

            // ---- the ways that stay open -----------------------------------------

            // The walk round the well's rail; the way in at the cabin's door (the
            // doorway looks to +z, over the grate); each spawn point and its way to
            // the cabin; where Unstuck puts a player; the storage doorway (in the
            // room's port wall) and the console's front, where the crew stand.
            float ring = ShipStubBuilder.RingRadius + WellClear;
            Keep("the well's rail", Vector2.zero, ring);
            Keep("the cabin door", new Vector2(-1.3f, ShipStubBuilder.WellRadius - 0.5f), new Vector2(1.3f, ShipStubBuilder.RingRadius + 2.8f));
            foreach (Vector3 p in ShipStubBuilder.SpawnPositions) // built after the dressing, so read from the builder
            {
                Keep("a spawn point", new Vector2(p.x, p.z), 0.7f);
                Keep("a spawn's way to the cabin", new Vector2(p.x - 0.45f, ring - 0.5f), new Vector2(p.x + 0.45f, p.z));
            }
            Vector3 unstuck = ShipStubBuilder.UnstuckPoint;
            Keep("the Unstuck point", new Vector2(unstuck.x, unstuck.z), 0.8f);
            Keep("the loop test's open deck", new Vector2(OpenDeck.x, OpenDeck.z), 0.6f);
            float door = sx - ShipStubBuilder.StorageWidth / 2f;
            Keep("the storage doorway", new Vector2(door - 2.4f, sz - 1.3f), new Vector2(door, sz + 1.3f));

            // ---- the lounge: the bow ---------------------------------------------

            // The deck TV the game builds stands at the bow facing aft
            // (ShipStubBuilder.BuildTv); the cabinet is its housing, sunk into the
            // deck so the same big screen is at a sitting eye's height (Dan, 23
            // September 2026: "same size, lower"). Two couches in front of it, lamps
            // either side turned onto them; behind the couches the table between its
            // two benches (Dan: the table between the benches), the living end of the
            // ship.
            float tv = halfL - 5f;
            GameObject cabinet = Hand(look, "TvCabinet", new Vector3(0f, 0f, tv + 0.45f), 180f, ground: false);
            Vector3 screenCentre = DressTv(root, cabinet);
            Keep("the view to the TV", new Vector2(-3.2f, tv - 5f), new Vector2(3.2f, tv - 0.3f));
            int seat = 0;
            foreach (float x in new[] { -2.1f, 2.1f })
            {
                GameObject couch = Hand(look, "Couch", new Vector3(x, 0f, tv - 5.5f), 0f);
                AddSeats(couch, screenCentre, ref seat);
            }
            // The table between the benches, the benches facing each other across it
            // (SHIP-022, SHIP-067), clear of the couches' backs: the aft bench looks
            // over the table and the couches to the TV.
            float tableZ = tv - 8.8f;
            GameObject table = Hand(look, "Table", new Vector3(0f, 0f, tableZ), 0f);
            float tableHalf = table != null ? ShipBounds(table).extents.z : 0.4f;
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject bench = Put(look, "Bench", new Vector3(0f, 0f, tableZ), side > 0f ? 180f : 0f);
                if (bench == null) continue;
                bench.transform.localPosition += Vector3.forward * side * (tableHalf + 0.1f + ShipBounds(bench).extents.z);
                Settle(bench, false);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                // Beside the TV, turned onto the couches (SHIP-027), inboard of the
                // narrowing bow by their own width (SHIP-006).
                Vector3 at = new(side * 5.6f, 0f, tv - 1.5f);
                float yaw = Mathf.Atan2(side * 2.1f - at.x, (tv - 5.5f) - at.z) * Mathf.Rad2Deg;
                GameObject lamp = Hand(look, "DeckLamp", at, yaw, rail: side);
                AddLampLight(lamp);
            }
            ShipSignVariants.Apply(Hand(look, "Signs", new Vector3(-W(13f) + WallThickness + 0.06f, 0.5f, 13f), 90f), ShipSign.Lounge); // on the side's inner face, below its top

            // The bow tip behind the TV: a mooring station, the bollards either side
            // and the line coiled between them (SHIP-038: it held nothing).
            foreach (float side in new[] { -1f, 1f })
                Hand(look, "Bollard", new Vector3(side * 2.4f, 0f, tv + 2.1f), 0f);
            Hand(look, "CableCoil", new Vector3(0f, 0f, tv + 2.6f), 0f);

            // ---- round the well ---------------------------------------------------

            // The game's glass elevator stands in the well as it is, the rail round it
            // (ShipStubBuilder). To port the winch that runs the car and its cable, its
            // drum toward the well, with its coil and its toolbox; to starboard the
            // crane, its round base on the deck clear of the rail and its boom out over
            // the side (SHIP-012: it stood by its pivot, its base 3.3 m further in).
            Hand(look, "Winch", new Vector3(0f, 0f, -1.5f), 90f, rail: -1f);
            Hand(look, "CableCoil", new Vector3(0f, 0f, -4.5f), 0f, rail: -1f); // against the side: no gap behind it
            Hand(look, "Toolbox", new Vector3(-7.4f, 0f, 1.0f), 20f);
            PlaceCrane(look, ring);

            // Forward of the well, by the rails: the deck stores to port, the plant to
            // starboard, clear of the spawn points.
            Hand(look, "Crate", new Vector3(-6.8f, 0f, 3.2f), 8f);
            Hand(look, "Barrel", new Vector3(-7.5f, 0f, 4.4f), 0f);
            Hand(look, "Barrel", new Vector3(-6.6f, 0f, 4.75f), 0f);
            Hand(look, "Pipes", new Vector3(0f, 0f, 3.6f), -90f, rail: 1f);
            Hand(look, "Barrel", new Vector3(7.4f, 0f, 6.3f), 0f);
            Hand(look, "Barrel", new Vector3(6.8f, 0f, 6.9f), 0f);
            Hand(look, "Crate", new Vector3(6.6f, 0f, 9.6f), -17f);
            ShipSignVariants.Apply(Hand(look, "Signs", new Vector3(W(8.2f) - WallThickness - 0.06f, 0.5f, 8.2f), -90f), ShipSign.DiveCage); // facing the well and the spawn points across the deck, forward of the crane (which hid it further aft)

            // ---- the working stern ------------------------------------------------

            // The storage room to starboard of the tower, its doorway toward the centre
            // line exactly where the game's walls and sill already are: the game's
            // sizes, and the game's colliders.
            // The model was fitted to the brief's 2.8 x 3.0 x 2.2 m; the game's room is
            // ShipStubBuilder's, so the model is stretched to those numbers exactly.
            GameObject room = Place(look, "StorageRoom", new Vector3(sx, 0f, sz), 0f);
            if (room != null) room.transform.localScale = new Vector3(ShipStubBuilder.StorageWidth / 2.8f, ShipStubBuilder.StorageHeight / 2.2f, ShipStubBuilder.StorageDepth / 3.0f);
            FitSill(root, Place(look, "StorageSill", new Vector3(door, 0f, sz), 90f));
            // The crew screen hangs on the tower's face as thin panels: solid, so a face
            // pressed to its edge stops at the edge (ShipShellAudit found it).
            foreach (string panel in new[] { "Crew Screen Frame", "Crew Screen" })
            {
                Transform t = root.Find(panel);
                if (t != null && t.GetComponent<Collider>() == null) t.gameObject.AddComponent<BoxCollider>();
            }
            // The console against the tower's forward face is the panel the crew press
            // to sail: the game's monitor screen on the model's own display, its three
            // buttons set into the desk under it (Dan, 23 September 2026: "replace the
            // last thing that helped the player choose, now use the new display"). Port
            // of the tower model's door, which it stood in front of (SHIP-039).
            GameObject console = Put(look, "Console", new Vector3(ConsoleX, 0f, towerFront + 0.75f), 0f);
            if (console != null)
            {
                Bounds cb = ShipBounds(console);
                console.transform.localPosition += Vector3.forward * (TowerFace(cb.min.x, cb.max.x) + 0.08f - cb.min.z);
                Settle(console, false);
                cb = ShipBounds(console);
                Keep("the console's front", new Vector2(cb.min.x - 0.1f, cb.max.z), new Vector2(cb.max.x + 0.1f, cb.max.z + 2f));
            }
            DressConsole(root, console);
            ShipSignVariants.Apply(Hand(look, "Signs", new Vector3(1.0f, 2.35f, towerFront + 0.3f), 0f), ShipSign.Bridge); // over the tower's door, on the one flat panel of its face (flat to 2 cm), clear of the console and the crew screen

            // Port, the cargo corner: one 20 ft container across the deck from the rail,
            // its doors against the rail (SHIP-042: its hatch read as the way to the
            // storage room) and no slot beside it (SHIP-040), and aft of it the barrels,
            // the crate, the pipework and a coil.
            GameObject box = Hand(look, "Container", new Vector3(0f, 0f, -11f), 180f, rail: -1f);
            if (box != null)
            {
                // The model has a round hatch at both ends; the inboard one faced the aft
                // walkway and the storage doorway and read as the way in (SHIP-042). The
                // plant stands against it, its pipework across the hatch.
                Bounds cb = ShipBounds(box);
                GameObject plant = Put(look, "Pipes", new Vector3(cb.max.x + 0.3f, 0f, cb.center.z), 90f);
                if (plant != null)
                {
                    plant.transform.localPosition += Vector3.right * (cb.max.x + 0.02f - ShipBounds(plant).min.x);
                    Settle(plant, false);
                }
            }
            GameObject drum = Hand(look, "Barrel", new Vector3(0f, 0f, -13.0f), 0f, rail: -1f);
            if (drum != null) Hand(look, "Barrel", drum.transform.localPosition + new Vector3(0.64f, 0f, 0.05f), 0f);
            Hand(look, "Crate", new Vector3(-5.2f, 0f, -13.2f), 12f);
            Hand(look, "Pipes", new Vector3(0f, 0f, -15.8f), 90f, rail: -1f);
            Hand(look, "CableCoil", new Vector3(-6.1f, 0f, -16.2f), 0f);

            // Starboard, beside the storage room: the crates stacked and the barrels
            // against its wall, the way along the rail left open.
            GameObject low = Hand(look, "Crate", new Vector3(6.65f, 0f, -12.4f), 18f);
            if (low != null)
            {
                // On the crate under it, not over it (SHIP-007): its bottom on the lower's top.
                GameObject high = Put(look, "Crate", new Vector3(6.65f, 0f, -12.5f), -32f);
                if (high != null) high.transform.localPosition += Vector3.up * (ShipBounds(low).max.y - ShipBounds(high).min.y);
            }
            Hand(look, "Barrel", new Vector3(6.45f, 0f, -14.0f), 0f);
            Hand(look, "Barrel", new Vector3(6.4f, 0f, -10.95f), 0f);
            ShipSignVariants.Apply(Hand(look, "Signs", new Vector3(door - 0.06f, 1.3f, sz - ShipStubBuilder.StorageDepth / 2f + 0.66f), -90f), ShipSign.Storage); // beside the doorway, facing the way in

            // The quarters either side of the tower: the stern's mooring stations, the
            // plant on the tower's starboard flank, the barrels on its port flank.
            foreach (float side in new[] { -1f, 1f })
                Hand(look, "Bollard", new Vector3(side * 6.9f, 0f, -22.0f), 0f);
            Hand(look, "CableCoil", new Vector3(6.7f, 0f, -19.7f), 0f);
            Hand(look, "Pipes", new Vector3(towerHalf + 0.6f, 0f, towerZ + 0.2f), -90f);
            Hand(look, "Toolbox", new Vector3(5.3f, 0f, -18.9f), -14f); // off the stern wall (SHIP-080), the corner behind it left open
            // The barrels against the tower's port flank, where no gap behind them is a way in.
            Hand(look, "Barrel", new Vector3(-4.45f, 0f, -19.9f), 0f);
            Hand(look, "Barrel", new Vector3(-4.45f, 0f, -20.55f), 0f);
            Hand(look, "Barrel", new Vector3(-4.5f, 0f, -22.4f), 0f);

            // ---- along the edge, following the hull ------------------------------

            // A lamp every 12 m, each turned onto the deck (SHIP-027) and lit
            // (SHIP-015), two lifebuoys a side, against the side's inner face; the row
            // stops short of the lounge, whose own lamps light it. Each gives way to
            // whatever already stands there (SHIP-004: they stood in barrels, toolboxes
            // and the container).
            foreach (float side in new[] { -1f, 1f })
            {
                float stagger = side > 0f ? 6f : 0f;
                float inboard = side > 0f ? -90f : 90f;
                for (float z = -halfL + 5f + stagger; z < tv - 4f; z += 12f)
                    AddLampLight(Rail(look, "DeckLamp", side, z, inboard));
                foreach (float z in side > 0f ? new[] { 15f, 1.2f } : new[] { 16f, -6f })
                    Rail(look, "Lifebuoy", side, z, inboard);
            }

            // The name on the bow, both sides, where the hull is still wide; outboard,
            // so it collides with nothing.
            var plates = new List<GameObject>();
            foreach (float side in new[] { -1f, 1f })
                plates.Add(Place(look, "NamePlate", new Vector3(side * (W(12f) + 0.3f), -1.25f, 12f), side * 90f));

            // The models bring no colliders of their own; every prop then gets one
            // (a box round its mesh, or the mesh itself where the shape matters), and
            // the hull's own mesh is the deck and the walls.
            foreach (Collider c in look.GetComponentsInChildren<Collider>(true))
                if (!c.gameObject.name.StartsWith("Bulwark")) Object.DestroyImmediate(c); // the side and its guard keep theirs
            MeshFilter hullMesh = hull.GetComponentInChildren<MeshFilter>();
            MeshCollider hullCollider = hullMesh.gameObject.AddComponent<MeshCollider>();
            hullCollider.sharedMesh = hullMesh.sharedMesh;
            // Nothing stands off the deck (Dan: "I dont want anything floating weird"):
            // a prop whose footprint reaches past the side's inner face is removed and
            // named, rather than left hanging over the sea.
            for (int i = solid.Count - 1; i >= 0; i--)
            {
                GameObject prop = solid[i].Item1;
                if (prop == null || prop.name == "Tower" || prop.name == "Signs" || OverTheDeck(root, prop)) continue; // the tower is the game's structure, wider than the stern's outline; signs hang on the side
                Debug.LogWarning("Ship dressing: " + prop.name + " at " + prop.transform.localPosition + " reaches past the hull; removed");
                Object.DestroyImmediate(prop);
                solid.RemoveAt(i);
            }
            foreach ((GameObject prop, bool byMesh) in solid) Solidify(prop, byMesh);
            Physics.SyncTransforms();
            MountSigns(root, look);
            MountCrewScreen(root);
            foreach (GameObject plate in plates) MountNamePlate(root, plate, hullCollider);
            FillDeckHoles(root, look, hullCollider);
            // The walkways and zones painted on the deck, round the props where they now
            // stand (fix-models' SHIP-055); last, on the hull's collider.
            ShipDeckMarkings.Build(root);
        }

        // ---- the stubs the models replace ---------------------------------------------

        private static void StripStubs(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null || t == root || UnderKept(t, root)) continue;
                if (Removed.Contains(t.name) || t.name.StartsWith(RemovedFamily)) { Object.DestroyImmediate(t.gameObject); continue; }
                if (!t.name.StartsWith(HiddenPrefix)) continue;
                // The whole branch stops being seen; the colliders below it stay.
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true).ToArray())
                {
                    if (r == null) continue;
                    // A TextMesh owns its renderer and will not give it up.
                    if (r.GetComponent<TextMesh>() != null) { r.enabled = false; continue; }
                    MeshFilter filter = r.GetComponent<MeshFilter>();
                    Object.DestroyImmediate(r);
                    if (filter != null) Object.DestroyImmediate(filter);
                }
            }
        }

        private static bool UnderKept(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
                if (Kept.Contains(p.name)) return true;
            return false;
        }

        // ---- the TV -------------------------------------------------------------------

        // The game's TV onto the cabinet model's face (Dan, 23 September 2026: "the TV
        // should be on the TV"): the cabinet is sunk until its screen's centre is at
        // TvScreenCentreY - its legs go down into the hull, where nothing shows - and
        // the screen quad sits just proud of the panel; the caption loses its plate and
        // its glow strips and its words sit on the screen itself ("write it on the
        // TV"); the speaker point moves with the screen. Returns the screen's centre.
        private static Vector3 DressTv(Transform root, GameObject cabinet)
        {
            Vector3 fallback = new(0f, TvScreenCentreY, halfL - 5f);
            if (cabinet == null) return fallback;
            // The picture goes exactly on the model's own screen panel and nowhere else
            // (Dan, 23 September 2026: "exactly the screen 3D model, I dont want to see
            // anything other than that"): the panel is measured off the mesh, and the
            // game's screen quad - what ShipTV draws the channel onto, and what E
            // presses - covers that rectangle, 5 mm proud of it.
            if (!ScreenPanel(root, cabinet, Vector3.back, 0f, out Vector3 centre, out Vector2 size))
            {
                Debug.LogWarning("Ship dressing: no screen panel found on the TV cabinet");
                return fallback;
            }
            float sink = centre.y - TvScreenCentreY;
            cabinet.transform.localPosition += Vector3.down * sink;
            centre.y -= sink;
            Transform screen = root.Find(ShipParts.TvScreenName);
            if (screen != null)
            {
                // The whole panel, edge to edge: the 16:9 feed is drawn 2.3:1, a little
                // wide. Pillarboxed it read as a slab on the screen again, and cropped
                // it would cut the diver's visor readouts that spectators rely on.
                screen.localPosition = centre + new Vector3(0f, 0f, -0.005f);
                screen.localScale = new Vector3(size.x, size.y, 1f);
                // What E finds: the whole picture, 2 cm thick, its face 5 mm in front of the
                // picture and its back in the panel, so the crosshair meets the screen
                // before the cabinet's own shape and a thrown item stops at the glass, not
                // on air in front of it (SHIP-038: it stood 29 cm proud). A trigger would
                // not do: the interact ray ignores triggers (InteractionTargeting).
                if (screen.TryGetComponent(out BoxCollider press)) { press.center = new Vector3(0f, 0f, 0.005f); press.size = new Vector3(1f, 1f, 0.02f); }
            }
            // The channel's name is the screen's own line of text, top centre, no plate.
            Transform caption = root.Find("Tv Caption Sign");
            if (caption != null)
            {
                caption.localPosition = centre + new Vector3(0f, size.y * 0.38f, -0.01f);
                foreach (Transform part in caption.Cast<Transform>().ToArray())
                    if (part.name == "Plate" || part.name.StartsWith("Frame")) Object.DestroyImmediate(part.gameObject);
                var text = caption.GetComponentInChildren<TextMesh>();
                if (text != null) text.characterSize = CaptionCharacterSize; // set, not multiplied: a second run keeps its size (SHIP-077)
            }
            Transform speaker = root.Find(ShipParts.TvSpeakerName);
            if (speaker != null) speaker.localPosition = centre + new Vector3(0f, 0f, -0.1f);
            return centre;
        }

        // A model's upright display panel, in ship space: of the triangles facing
        // `facing` (the way the screen looks) whose centre is above `minHeight`, the
        // flat layer with the most area is the panel - frames and legs are thin - and
        // the largest solid rectangle in that layer is the screen. The mesh need not be
        // import-readable: the editor reads its vertices anyway.
        private static bool ScreenPanel(Transform root, GameObject model, Vector3 facing, float minHeight, out Vector3 centre, out Vector2 size)
        {
            centre = default; size = default;
            MeshFilter mf = model.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            Vector3 up = Vector3.up, right = Vector3.Cross(up, facing).normalized;
            Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] v = mf.sharedMesh.vertices; int[] tri = mf.sharedMesh.triangles;
            var faces = new List<(Vector2 a, Vector2 b, Vector2 c, int depth)>();
            var layers = new Dictionary<int, float>(); // area by 1 cm of depth along `facing`
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a);
                float area = n.magnitude / 2f;
                if (area < 1e-6f || Vector3.Dot(n.normalized, facing) < 0.97f || (a.y + b.y + c.y) / 3f < minHeight) continue;
                int key = Mathf.RoundToInt(Vector3.Dot((a + b + c) / 3f, facing) * 100f);
                faces.Add((new Vector2(Vector3.Dot(a, right), a.y), new Vector2(Vector3.Dot(b, right), b.y), new Vector2(Vector3.Dot(c, right), c.y), key));
                layers[key] = layers.TryGetValue(key, out float s) ? s + area : area;
            }
            if (layers.Count == 0) return false;
            int panel = layers.OrderByDescending(kv => kv.Value).First().Key;
            // The layer holds the panel and whatever is flush with it (the legs run down
            // from it): the screen is the largest solid rectangle in the layer. Cover a
            // 5 cm grid with the layer's triangles, then find that rectangle.
            const float cell = 0.05f;
            Vector2 lo = new(float.PositiveInfinity, float.PositiveInfinity), hi = new(float.NegativeInfinity, float.NegativeInfinity);
            var flat = faces.Where(f => Mathf.Abs(f.depth - panel) <= 3).ToList();
            foreach (var f in flat) foreach (Vector2 q in new[] { f.a, f.b, f.c }) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            int nx = Mathf.CeilToInt((hi.x - lo.x) / cell) + 1, ny = Mathf.CeilToInt((hi.y - lo.y) / cell) + 1;
            var cover = new bool[nx, ny];
            foreach (var f in flat)
            {
                Vector2 a = f.a, b = f.b, c = f.c;
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - lo.x) / cell), x1 = Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - lo.x) / cell);
                int y0 = Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - lo.y) / cell), y1 = Mathf.CeilToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - lo.y) / cell);
                for (int ix = Mathf.Max(0, x0); ix <= Mathf.Min(nx - 1, x1); ix++)
                    for (int iy = Mathf.Max(0, y0); iy <= Mathf.Min(ny - 1, y1); iy++)
                        if (Inside(new Vector2(lo.x + (ix + 0.5f) * cell, lo.y + (iy + 0.5f) * cell), a, b, c)) cover[ix, iy] = true;
            }
            // Largest rectangle of covered cells: a histogram per row, a stack per histogram.
            int bestArea = 0, bx0 = 0, bx1 = 0, by0 = 0, by1 = 0;
            var heights = new int[nx];
            for (int iy = 0; iy < ny; iy++)
            {
                for (int ix = 0; ix < nx; ix++) heights[ix] = cover[ix, iy] ? heights[ix] + 1 : 0;
                var stack = new Stack<int>();
                for (int ix = 0; ix <= nx; ix++)
                {
                    int hgt = ix < nx ? heights[ix] : 0;
                    while (stack.Count > 0 && heights[stack.Peek()] >= hgt)
                    {
                        int top = stack.Pop(), height = heights[top];
                        int left = stack.Count > 0 ? stack.Peek() + 1 : 0, width = ix - left;
                        if (height * width > bestArea) { bestArea = height * width; bx0 = left; bx1 = ix - 1; by1 = iy; by0 = iy - height + 1; }
                    }
                    if (ix < nx) stack.Push(ix);
                }
            }
            if (bestArea == 0) return false;
            // One cell in from each side: the picture stays inside the frame.
            float px0 = lo.x + (bx0 + 1) * cell, px1 = lo.x + bx1 * cell, py0 = lo.y + (by0 + 1) * cell, py1 = lo.y + by1 * cell;
            centre = right * ((px0 + px1) / 2f) + up * ((py0 + py1) / 2f) + facing * (panel / 100f);
            size = new Vector2(px1 - px0, py1 - py0);
            return size.x > 0.5f && size.y > 0.3f;
        }

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ---- the console ----------------------------------------------------------------

        // The game's monitor onto the console model (SHIP-008): the screen block (the
        // ShipMonitor's target) exactly on the model's upright display, measured off
        // its mesh as the TV's is; the status line on that screen; the three buttons
        // laid in the sloping desk under it, facing up at the person pressing them, in
        // the order the stub builder gives them (SITE 01 · HQ · END DAY as read).
        private static void DressConsole(Transform root, GameObject console)
        {
            if (console == null) return;
            float cx = console.transform.localPosition.x; // the stub's buttons stand about x = 0
            if (!ScreenPanel(root, console, Vector3.forward, 1.2f, out Vector3 centre, out Vector2 size))
            {
                Debug.LogWarning("Ship dressing: no display panel found on the console");
                return;
            }
            Transform monitor = root.Find(ShipParts.MonitorName);
            if (monitor != null)
            {
                monitor.localPosition = centre + Vector3.forward * 0.012f;
                monitor.localRotation = Quaternion.identity;
                monitor.localScale = new Vector3(size.x, size.y, 0.02f);
            }
            Transform status = root.Find(ShipParts.MonitorStatusName);
            if (status != null) status.localPosition = centre + new Vector3(0f, size.y * 0.25f, 0.03f);
            string[] buttons = { ShipParts.MonitorButtonSite01Name, ShipParts.MonitorButtonHQName, ShipParts.MonitorButtonEndDayName };
            if (!Desk(root, console, centre.y - size.y / 2f, out Vector3 point, out Vector3 normal))
            {
                Debug.LogWarning("Ship dressing: no desk found on the console; its buttons stay on the screen's foot");
                foreach (string name in buttons)
                {
                    Transform b = root.Find(name);
                    if (b != null) b.localPosition = new Vector3(cx + b.localPosition.x, centre.y - size.y / 2f - 0.42f, centre.z + 0.05f);
                }
                return;
            }
            // Its +Y runs up the slope, away from the person at the desk, so the words
            // on its cap read the right way up to them; the bezel's 40 cm centred on
            // the desk's middle, just proud of its bumps.
            Vector3 upSlope = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            Quaternion turn = Quaternion.LookRotation(normal, upSlope);
            foreach (string name in buttons)
            {
                Transform b = root.Find(name);
                if (b == null) continue;
                Vector3 on = point + Vector3.right * (cx + b.localPosition.x - point.x);
                b.localPosition = on - upSlope * 0.2f + normal * 0.045f;
                b.localRotation = turn;
            }
        }

        // The console's sloping desk, in ship space: the triangles below its display
        // that face up and toward the crew, their area-weighted centre and normal.
        private static bool Desk(Transform root, GameObject console, float below, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = default;
            MeshFilter mf = console.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] v = mf.sharedMesh.vertices; int[] tri = mf.sharedMesh.triangles;
            float cx = console.transform.localPosition.x, total = 0f;
            Vector3 sumP = Vector3.zero, sumN = Vector3.zero;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a), mid = (a + b + c) / 3f;
                float area = n.magnitude / 2f;
                if (area < 1e-6f) continue;
                n /= area * 2f;
                if (n.y < 0.5f || n.y > 0.97f || n.z < 0.15f || mid.y < 0.6f || mid.y > below || Mathf.Abs(mid.x - cx) > 1.0f) continue;
                sumP += mid * area; sumN += n * area; total += area;
            }
            if (total < 0.1f) return false;
            point = sumP / total;
            normal = sumN.normalized;
            return true;
        }

        // The crew screen flat on the tower's face (SHIP-079): the frame's back to the
        // nearest point of the face behind any part of it, the screen and its words
        // moved with it.
        private static void MountCrewScreen(Transform root)
        {
            Transform frame = root.Find("Crew Screen Frame");
            if (frame == null) return;
            Vector3 e = frame.localScale / 2f;
            float nearest = float.NegativeInfinity;
            foreach (Vector2 k in Samples)
            {
                Vector3 from = frame.localPosition + new Vector3(k.x * e.x, k.y * e.y, 0.6f);
                if (StructureHit(root, root.TransformPoint(from), -root.forward, 2.5f, null, out RaycastHit hit)) nearest = Mathf.Max(nearest, from.z - hit.distance);
            }
            if (float.IsInfinity(nearest)) return;
            float move = nearest + 0.005f - (frame.localPosition.z - e.z);
            foreach (string name in new[] { "Crew Screen Frame", "Crew Screen", "Crew Screen Text" })
            {
                Transform t = root.Find(name);
                if (t != null) t.localPosition += Vector3.forward * move;
            }
        }

        // ---- the crane ------------------------------------------------------------------

        // The crane by its base, not its pivot (SHIP-012): the model's pivot is the
        // middle of the whole thing, boom and all, 3.3 m outboard of the round base it
        // stands on. The base goes as far outboard as the side allows and as far aft
        // as keeps it clear of the walk round the well; the boom hangs over the side.
        private static void PlaceCrane(GameObject look, float ring)
        {
            GameObject crane = Put(look, "Crane", Vector3.zero, 90f);
            if (crane == null) return;
            Footprint foot = BaseCircle(crane);
            Vector2 offset = foot.Centre - new Vector2(crane.transform.localPosition.x, crane.transform.localPosition.z);
            float r = foot.Radius, bx = 0f, bz = -5.5f;
            for (int i = 0; i < 4; i++)
            {
                bx = Inboard(bz - r, bz + r) - 0.02f - r;
                bz = -Mathf.Sqrt(Mathf.Max(0f, (ring + r) * (ring + r) - bx * bx)) - 0.05f;
            }
            crane.transform.localPosition = new Vector3(bx - offset.x, crane.transform.localPosition.y, bz - offset.y);
            Settle(crane, false);
        }

        // The crane's round base: its mesh below a metre, as a circle on the deck.
        private static Footprint BaseCircle(GameObject crane)
        {
            MeshFilter mf = crane.GetComponentInChildren<MeshFilter>();
            Matrix4x4 m = shipRoot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector2 lo = new(float.PositiveInfinity, float.PositiveInfinity), hi = -lo;
            float floor = ShipBounds(crane).min.y;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                if (p.y > floor + 1f) continue;
                lo = Vector2.Min(lo, new Vector2(p.x, p.z)); hi = Vector2.Max(hi, new Vector2(p.x, p.z));
            }
            return new Footprint { Name = "Crane", Centre = (lo + hi) / 2f, Radius = Mathf.Max(hi.x - lo.x, hi.y - lo.y) / 2f };
        }

        // ---- the couches ----------------------------------------------------------------

        // A couch collides as a seat and a back (SHIP-024: one box round it filled the
        // seat up to the backrest), measured off its mesh: the seat is the biggest
        // level surface under the backrest's top, the back what stands behind it.
        private struct CouchShape { public Bounds All; public float SeatTop, SeatBack, BackFront, SeatX0, SeatX1, ArmTop, ArmL, ArmR; }

        private static bool MeasureCouch(GameObject couch, out CouchShape shape)
        {
            shape = default;
            MeshFilter mf = couch.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            Matrix4x4 m = couch.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] v = mf.sharedMesh.vertices; int[] tri = mf.sharedMesh.triangles;
            Bounds all = new(m.MultiplyPoint3x4(v[0]), Vector3.zero);
            foreach (Vector3 p in v) all.Encapsulate(m.MultiplyPoint3x4(p));
            var levels = new Dictionary<int, float>();
            var ups = new List<(Vector3 a, Vector3 b, Vector3 c, float y)>();
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a);
                float area = n.magnitude / 2f, y = (a.y + b.y + c.y) / 3f;
                if (area < 1e-6f || n.normalized.y < 0.9f || y < 0.2f * all.size.y || y > 0.75f * all.size.y) continue;
                ups.Add((a, b, c, y));
                if (Mathf.Abs((a.x + b.x + c.x) / 3f - all.center.x) > all.extents.x * 0.6f) continue; // the arms
                int key = Mathf.RoundToInt(y * 100f);
                levels[key] = levels.TryGetValue(key, out float s) ? s + area : area;
            }
            if (levels.Count == 0) return false;
            float top = levels.OrderByDescending(kv => kv.Value).First().Key / 100f;
            float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, back = float.PositiveInfinity;
            foreach (var f in ups)
            {
                if (Mathf.Abs(f.y - top) > 0.03f) continue;
                foreach (Vector3 q in new[] { f.a, f.b, f.c }) { x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); back = Mathf.Min(back, q.z); }
            }
            // The backrest's cushions lean forward over the seat's back edge; the arms
            // are what rises over the seat in front of them, at either end.
            float backFront = back, arm = top, armL = all.min.x, armR = all.max.x;
            var raised = new List<Vector3>();
            foreach (Vector3 p in v)
            {
                Vector3 q = m.MultiplyPoint3x4(p);
                if (q.y < top + 0.05f) continue;
                raised.Add(q);
                if (Mathf.Abs(q.x - all.center.x) < all.extents.x * 0.6f) backFront = Mathf.Max(backFront, q.z);
            }
            backFront = Mathf.Min(backFront, all.max.z - 0.1f);
            foreach (Vector3 q in raised)
            {
                if (q.z < backFront + 0.03f || Mathf.Abs(q.x - all.center.x) < all.extents.x * 0.6f) continue; // the outer ends only: a cushion's curve is not an arm
                arm = Mathf.Max(arm, q.y);
                if (q.x < all.center.x) armL = Mathf.Max(armL, q.x); else armR = Mathf.Min(armR, q.x);
            }
            shape = new CouchShape { All = all, SeatTop = top, SeatBack = back, BackFront = backFront, SeatX0 = x0, SeatX1 = x1, ArmTop = arm, ArmL = armL, ArmR = armR };
            return back > all.min.z && back < all.max.z;
        }

        // Two sitting points a couch, CouchSeat_1.. from port to starboard: the seated
        // hip on the seat, +Z toward the TV's screen (the sitting feature finds them
        // by name, ShipSeats).
        private static void AddSeats(GameObject couch, Vector3 screen, ref int number)
        {
            if (couch == null || !MeasureCouch(couch, out CouchShape s)) { Debug.LogWarning("Ship dressing: no seat measured on a couch"); return; }
            for (int i = 0; i < 2; i++)
            {
                var marker = new GameObject(ShipSeats.Prefix + (++number));
                marker.transform.SetParent(couch.transform, false);
                float x = Mathf.Lerp(Mathf.Max(s.SeatX0, s.ArmL), Mathf.Min(s.SeatX1, s.ArmR), 0.25f + 0.5f * i);
                marker.transform.localPosition = new Vector3(x, s.SeatTop, Mathf.Lerp(s.BackFront, s.All.max.z, 0.4f));
                Vector3 to = shipRoot.InverseTransformPoint(couch.transform.TransformPoint(marker.transform.localPosition));
                Vector3 look = screen - to; look.y = 0f;
                marker.transform.rotation = shipRoot.rotation * Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        // ---- the lamps ------------------------------------------------------------------

        // A small warm light at each deck lamp's head (SHIP-015), thrown the way the
        // head faces and down onto the deck: short, and no shadows, so nine of them
        // cost little.
        private static void AddLampLight(GameObject lamp)
        {
            if (lamp == null) return;
            Bounds b = ShipBounds(lamp);
            Vector3 pivot = lamp.transform.localPosition;
            Vector3 forward = lamp.transform.localRotation * Vector3.forward;
            Light light = new GameObject("Lamp Light").AddComponent<Light>();
            light.transform.SetParent(lamp.transform, false);
            light.transform.position = shipRoot.TransformPoint(new Vector3(pivot.x, b.max.y - 0.18f, pivot.z) + forward * 0.28f);
            light.transform.rotation = shipRoot.rotation * Quaternion.LookRotation(forward * Mathf.Cos(35f * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(35f * Mathf.Deg2Rad));
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.type = LightType.Spot;
            light.spotAngle = 120f; light.innerSpotAngle = 70f;
            light.range = 8f; light.intensity = 4f;
            light.color = new Color(1f, 0.78f, 0.52f);
            light.shadows = LightShadows.None;
        }

        // ---- the storage room's sill ----------------------------------------------------

        // The sill model on the game's sill (SHIP-069): the same centre, the doorway's
        // width, the collider's height and depth, from the model's measured size.
        private static void FitSill(Transform root, GameObject sill)
        {
            Transform stub = root.Find("StorageSill");
            if (sill == null || stub == null || !stub.TryGetComponent(out BoxCollider box)) return;
            // The stub's box in ship space (a cube under the ship's root, turned by nothing).
            Bounds want = new(stub.localPosition + Vector3.Scale(stub.localScale, box.center), Vector3.Scale(stub.localScale, box.size));
            Bounds have = ShipBounds(sill); // at one, turned so its length runs along z
            Vector3 s = sill.transform.localScale;
            sill.transform.localScale = new Vector3(s.x * want.size.z / have.size.z, s.y * want.size.y / have.size.y, s.z * want.size.x / have.size.x);
            have = ShipBounds(sill);
            sill.transform.localPosition += new Vector3(want.center.x - have.center.x, want.min.y - have.min.y, want.center.z - have.center.z);
        }

        // ---- footprints: nothing stands in anything ------------------------------------

        // A patch of deck, in the ship's x and z: a rectangle, or a circle when Radius
        // is set. Structures, props and the ways that must stay open each take one.
        private struct Footprint
        {
            public string Name;
            public Vector2 Min, Max;
            public Vector2 Centre;
            public float Radius;
        }

        private static readonly List<Footprint> taken = new();

        private static void Keep(string name, Vector2 min, Vector2 max) => taken.Add(new Footprint { Name = name, Min = min, Max = max });
        private static void Keep(string name, Vector2 centre, float radius) => taken.Add(new Footprint { Name = name, Centre = centre, Radius = radius });

        private static bool Overlap(in Footprint a, in Footprint b)
        {
            const float slack = 0.02f; // touching is not standing in
            if (a.Radius > 0f && b.Radius > 0f) return Vector2.Distance(a.Centre, b.Centre) < a.Radius + b.Radius - slack;
            if (a.Radius > 0f || b.Radius > 0f)
            {
                Footprint c = a.Radius > 0f ? a : b, r = a.Radius > 0f ? b : a;
                Vector2 near = Vector2.Max(r.Min, Vector2.Min(r.Max, c.Centre));
                return Vector2.Distance(near, c.Centre) < c.Radius - slack;
            }
            return Mathf.Min(a.Max.x, b.Max.x) - Mathf.Max(a.Min.x, b.Min.x) > slack && Mathf.Min(a.Max.y, b.Max.y) - Mathf.Max(a.Min.y, b.Min.y) > slack;
        }

        private static Footprint FootprintOf(GameObject prop)
        {
            if (prop.name == "Crane") return BaseCircle(prop);
            Bounds b = ShipBounds(prop);
            return new Footprint { Name = prop.name, Min = new Vector2(b.min.x, b.min.z), Max = new Vector2(b.max.x, b.max.z) };
        }

        // Takes the prop's patch of deck. A prop that finds its patch taken either
        // gives way (a rail row's lamp or buoy: it is not placed) or is named in a
        // warning (a hand-placed one: the layout above is wrong and must move).
        private static bool Settle(GameObject prop, bool yields)
        {
            Footprint f = FootprintOf(prop);
            foreach (Footprint t in taken)
            {
                if (!Overlap(f, t)) continue;
                if (yields)
                {
                    solid.RemoveAll(s => s.Item1 == prop);
                    Object.DestroyImmediate(prop);
                    return false;
                }
                Debug.LogWarning("Ship dressing: " + prop.name + " at " + prop.transform.localPosition.ToString("F2") + " stands in " + t.Name);
                break;
            }
            taken.Add(f);
            return true;
        }

        // The tower's patch, slice by slice across it: its mesh from the deck to head
        // height, so props stop at its real face (18.4 m aft of midships at knee
        // height where its bounds say 18.0).
        private const float TowerSlice = 0.25f;
        private static readonly Dictionary<int, float> towerFace = new(); // slice -> the face's most forward z
        private static readonly Dictionary<int, float> towerBack = new();  // slice -> its most aft z

        private static void RegisterTower(GameObject tower)
        {
            if (tower == null) return;
            MeshFilter mf = tower.GetComponentInChildren<MeshFilter>();
            Matrix4x4 m = shipRoot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var back = towerBack;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                if (p.y < 0.02f || p.y > 2.5f) continue;
                int k = Mathf.FloorToInt(p.x / TowerSlice);
                towerFace[k] = towerFace.TryGetValue(k, out float f) ? Mathf.Max(f, p.z) : p.z;
                back[k] = back.TryGetValue(k, out float b) ? Mathf.Min(b, p.z) : p.z;
            }
            foreach (int k in towerFace.Keys)
                Keep("the tower", new Vector2(k * TowerSlice, back[k]), new Vector2((k + 1) * TowerSlice, towerFace[k]));
        }

        // The tower's face in front of a span of x: its most forward point there.
        private static float TowerFace(float x0, float x1)
        {
            float face = -halfL + ShipStubBuilder.TowerDepth;
            bool any = false;
            for (int k = Mathf.FloorToInt(x0 / TowerSlice); k <= Mathf.FloorToInt(x1 / TowerSlice); k++)
                if (towerFace.TryGetValue(k, out float f)) { face = any ? Mathf.Max(face, f) : f; any = true; }
            return face;
        }

        // ---- placing ----------------------------------------------------------------------

        // A prop placed by hand: grounded (SHIP-030: the winch stood 11 cm up), pushed
        // against the side's inner face when `rail` names a side (the x given is then
        // ignored), then taking its patch of deck.
        private static GameObject Hand(GameObject look, string part, Vector3 at, float yaw, float rail = 0f, bool ground = true)
        {
            GameObject prop = Put(look, part, at, yaw, ground);
            if (prop == null) return null;
            if (rail != 0f) ToRail(prop, rail);
            if (part == "Signs" && at.y > Bulwark) return prop; // on a wall over the deck: it takes no patch of it
            Settle(prop, false);
            return prop;
        }

        // A prop of a row along the rail: against the side's inner face at this length,
        // or nowhere if something already stands there.
        private static GameObject Rail(GameObject look, string part, float side, float z, float yaw)
        {
            GameObject prop = Put(look, part, new Vector3(side * (W(z) - 1f), 0f, z), yaw);
            if (prop == null) return null;
            ToRail(prop, side);
            return Settle(prop, true) ? prop : null;
        }

        private static GameObject Put(GameObject look, string part, Vector3 at, float yaw, bool ground = true)
        {
            GameObject prop = Place(look, part, at, yaw);
            if (prop != null && ground && at.y < 0.01f) Ground(prop); // a sign on a wall keeps its height
            return prop;
        }

        // Its lowest point on the deck.
        private static void Ground(GameObject prop)
        {
            float low = ShipBounds(prop).min.y;
            if (Mathf.Abs(low) > 0.002f) prop.transform.localPosition += Vector3.down * low;
        }

        // Its outboard face 4 cm off the side's inner face, wherever along its length
        // the side comes in furthest.
        private static void ToRail(GameObject prop, float side)
        {
            Bounds b = ShipBounds(prop);
            float face = side * (Inboard(b.min.z, b.max.z) - 0.04f);
            prop.transform.localPosition += Vector3.right * (face - (side > 0f ? b.max.x : b.min.x));
        }

        // The side's inner face over a stretch of length: where it comes in furthest.
        private static float Inboard(float z0, float z1)
        {
            float w = float.PositiveInfinity;
            for (float z = z0; z < z1; z += 0.1f) w = Mathf.Min(w, W(z));
            return Mathf.Min(w, W(z1)) - WallThickness;
        }

        // A prop's meshes' bounds in ship space, from the meshes and the transforms as
        // they are now (a renderer's own bounds can lag a transform just changed).
        private static Bounds ShipBounds(GameObject go)
        {
            Bounds b = default; bool any = false;
            foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>())
            {
                if (f.sharedMesh == null) continue;
                Bounds w = f.sharedMesh.bounds;
                Matrix4x4 m = shipRoot.worldToLocalMatrix * f.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            return b;
        }

        // Whether a prop's footprint lies inside the side's inner face (the bulwark is
        // WallThickness thick, inward of the outline): the four bottom corners of its
        // bounds, each at their own length. The crane's boom is meant to hang over the
        // side, so the crane is judged by its round base alone.
        private static bool OverTheDeck(Transform root, GameObject prop)
        {
            if (prop.GetComponentInChildren<Renderer>() == null) return true;
            var points = new List<Vector2>();
            if (prop.name == "Crane")
            {
                Footprint foot = BaseCircle(prop);
                for (int i = 0; i < 16; i++) points.Add(foot.Centre + new Vector2(Mathf.Cos(i * Mathf.PI / 8f), Mathf.Sin(i * Mathf.PI / 8f)) * foot.Radius);
            }
            else
            {
                Bounds b = ShipBounds(prop);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                        points.Add(new Vector2(b.center.x + sx * b.extents.x, b.center.z + sz * b.extents.z));
            }
            foreach (Vector2 p in points)
                if (Mathf.Abs(p.x) > W(p.y) - WallThickness + 0.01f || p.y < -halfL + WallThickness - 0.01f || p.y > halfL - 0.15f) return false;
            return true;
        }

        // ---- signs --------------------------------------------------------------------------

        // The face points a sign or panel is held by: its centre and its corners, a
        // little in (SHIP-079: one ray at the centre let curved faces cut in).
        private static readonly Vector2[] Samples = { Vector2.zero, new(-0.8f, -0.8f), new(0.8f, -0.8f), new(-0.8f, 0.8f), new(0.8f, 0.8f) };

        // Every sign flat against something (Dan: "most of the signs are floating in
        // the air"). A sign reads along its +Z; rays go back from just in front of its
        // back face, at its centre and its corners, to the ship's surfaces behind. The
        // sign turns to the wall's own facing there (the hits' mean normal), slides
        // along the wall to where the wall is flattest within reach (a bulwark curves
        // and kinks, the tower's face is uneven) and nothing stands in it or in front
        // of it, and there moves back until the nearest hit touches its back, so no part of it cuts in and the rest stands off by as
        // little as the wall allows. A sign on the ship's side stays below the side's
        // top. A sign with nothing within reach behind it is removed and named rather
        // than left hanging.
        private static void MountSigns(Transform root, GameObject look)
        {
            Physics.SyncTransforms();
            foreach (Transform t in look.transform.Cast<Transform>().Where(t => t.name == "Signs").ToArray())
            {
                MeshFilter mf = t.GetComponentInChildren<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // The sign's box in its own space: the mesh inside the prefab may be
                // turned, so its own axes do not say which is depth.
                Matrix4x4 toSign = t.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds, box = new(toSign.MultiplyPoint3x4(mb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    box.Encapsulate(toSign.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z)));
                if (!SignGaps(root, t, box, out float[] gaps, out Vector3 normal, out bool onSide))
                {
                    Debug.LogWarning("Ship dressing: a sign at " + t.localPosition + " has nothing behind it; removed");
                    Object.DestroyImmediate(t.gameObject);
                    continue;
                }
                Vector3 home = t.position, along = t.right, up = root.up;
                Quaternion turn = t.rotation;
                // Where it may slide to: 2 m either way along the ship's side; on the
                // tower's face a little, over the door it names.
                var offsets = new List<Vector2> { Vector2.zero };
                bool tower = !onSide && IsTower(root, t, box);
                if (onSide) for (float d = 0.25f; d <= 2.01f; d += 0.25f) { offsets.Add(new Vector2(d, 0f)); offsets.Add(new Vector2(-d, 0f)); }
                if (tower) for (float d = -0.2f; d <= 0.21f; d += 0.1f) for (float h = 0f; h <= 0.16f; h += 0.08f) offsets.Add(new Vector2(d, h));
                offsets.Sort((a, b) => a.magnitude.CompareTo(b.magnitude)); // the nearest of equally flat places
                float bestSpread = float.PositiveInfinity;
                Vector3 bestAt = home; Quaternion bestTurn = turn;
                foreach (Vector2 o in offsets)
                    {
                        t.SetPositionAndRotation(home + along * o.x + up * o.y, turn);
                        if (!SignGaps(root, t, box, out gaps, out normal, out _)) continue;
                        Vector3 face = Vector3.ProjectOnPlane(normal, root.up);
                        if (face.sqrMagnitude > 0.01f && Vector3.Angle(face, turn * Vector3.forward) < 30f) t.rotation = Quaternion.LookRotation(face.normalized, root.up);
                        if (!SignGaps(root, t, box, out gaps, out _, out _)) continue;
                        t.position -= t.forward * (gaps.Min() - 0.01f); // its back a centimetre off the nearest point
                        if (!Seen(root, t, box) || !Clear(root, t, box)) continue;
                        float spread = gaps.Max() - gaps.Min();
                        if (spread < bestSpread - 0.01f) { bestSpread = spread; bestAt = t.position; bestTurn = t.rotation; }
                    }
                if (float.IsInfinity(bestSpread))
                {
                    Debug.LogWarning("Ship dressing: a sign at " + t.localPosition + " has no clear, seen place on its wall; left where it was put");
                    t.SetPositionAndRotation(home, turn);
                    if (SignGaps(root, t, box, out gaps, out _, out _)) t.position -= t.forward * (gaps.Min() - 0.01f);
                }
                else t.SetPositionAndRotation(bestAt, bestTurn);
                if (onSide)
                {
                    float top = root.InverseTransformPoint(t.TransformPoint(new Vector3(box.center.x, box.max.y, box.center.z))).y;
                    if (top > Bulwark - 0.03f) t.position += root.up * (Bulwark - 0.03f - top);
                }
            }
            Physics.SyncTransforms();
        }

        // The gap from the sign's back face to the structure behind it at its centre
        // and corners, and the structure's mean facing there; false unless every ray
        // finds the structure.
        private static bool SignGaps(Transform root, Transform t, Bounds box, out float[] gaps, out Vector3 normal, out bool onSide)
        {
            gaps = new float[Samples.Length]; normal = Vector3.zero; onSide = false;
            for (int i = 0; i < Samples.Length; i++)
            {
                Vector2 k = Samples[i];
                Vector3 from = t.TransformPoint(new Vector3(box.center.x + k.x * box.extents.x, box.center.y + k.y * box.extents.y, box.min.z)) + t.forward * 0.6f;
                if (!StructureHit(root, from, -t.forward, 2.5f, t, out RaycastHit hit)) return false;
                gaps[i] = hit.distance - 0.6f;
                normal += hit.normal;
                if (i == 0) onSide = hit.collider.name.StartsWith("Bulwark");
            }
            normal.Normalize();
            return true;
        }

        // Whether the sign can be read: nothing between the eyes of a person standing
        // 2.5 m in front of it and its centre (SHIP-041: a sign nobody could read), and
        // no prop just in front of any of its corners.
        private static bool Seen(Transform root, Transform t, Bounds box)
        {
            Vector3 centre = t.TransformPoint(new Vector3(box.center.x, box.center.y, box.max.z));
            Vector3 eye = centre + Vector3.ProjectOnPlane(t.forward, root.up).normalized * 2.5f;
            eye += root.up * (1.6f - root.InverseTransformPoint(eye).y);
            foreach (Vector2 k in Samples) // its centre and its corners: no prop in front of any part of it
            {
                Vector3 face = t.TransformPoint(new Vector3(box.center.x + k.x * box.extents.x, box.center.y + k.y * box.extents.y, box.max.z));
                Vector3 from = k == Vector2.zero ? eye : face + t.forward * 0.6f, to = face - from;
                foreach (RaycastHit h in Physics.RaycastAll(from, to, to.magnitude - 0.02f, ~0, QueryTriggerInteraction.Ignore))
                    if (!h.collider.transform.IsChildOf(t)) return false;
            }
            return true;
        }

        // Whether the sign, where it now is, stands in nothing but the wall it hangs on.
        private static bool Clear(Transform root, Transform t, Bounds box)
        {
            Vector3 half = Vector3.Scale(box.extents, t.lossyScale) * 0.95f;
            foreach (Collider c in Physics.OverlapBox(t.TransformPoint(box.center), half, t.rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(t) && c.transform.IsChildOf(root) && !IsStructure(c.transform)) return false;
            return true;
        }

        private static bool IsTower(Transform root, Transform t, Bounds box)
        {
            Vector3 from = t.TransformPoint(new Vector3(box.center.x, box.center.y, box.min.z)) + t.forward * 0.6f;
            return StructureHit(root, from, -t.forward, 2.5f, t, out RaycastHit hit) && hit.collider.transform.IsChildOf(root.Find(LookName + "/Tower"));
        }

        // The nearest surface of the ship's structure along a ray: the ship's side,
        // the tower, the storage room's walls.
        private static bool StructureHit(Transform root, Vector3 from, Vector3 dir, float reach, Transform ignore, out RaycastHit best)
        {
            best = default; bool any = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, dir, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                Transform c = h.collider.transform;
                if ((ignore != null && c.IsChildOf(ignore)) || !c.IsChildOf(root) || !IsStructure(c)) continue;
                if (!any || h.distance < best.distance) { best = h; any = true; }
            }
            return any;
        }

        // What a sign may hang on: the ship's side, the tower, the storage room's walls.
        private static bool IsStructure(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name.StartsWith("Bulwark") || p.name == "Tower" || p.name.StartsWith("StorageWall") || p.name == "StorageRoom") return true;
            return false;
        }

        // ---- the name ---------------------------------------------------------------------

        // The ship's name read from the sea (SHIP-020): the plate against the hull
        // where the hull's side is straightest along the bow, its back on the side's
        // most outboard point behind it (rays at the plate's own height and ends, not
        // the deck's edge: SHIP-079), the model's baked lettering (garbled, and dark on
        // dark) under a dark panel inset on its face, and BLACK TIDE, SALVAGE under it,
        // in real letters on that panel.
        private static void MountNamePlate(Transform root, GameObject plate, MeshCollider hull)
        {
            if (plate == null) return;
            float side = Mathf.Sign(plate.transform.localPosition.x);
            plate.transform.localScale = Vector3.Scale(plate.transform.localScale, new Vector3(1f, 1f, 0.5f)); // a plate stands off the hull by a plate's depth, not a block's
            Bounds b = ShipBounds(plate);
            float length = b.size.z, low = b.min.y, high = b.max.y;
            float bestZ = plate.transform.localPosition.z, bestOut = 0f, bestSpread = float.PositiveInfinity;
            for (float z = 6f; z <= 14f; z += 0.5f)
            {
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                foreach (float dz in new[] { -0.45f, -0.2f, 0f, 0.2f, 0.45f })
                    foreach (float y in new[] { low + 0.1f, (low + high) / 2f, high - 0.1f })
                    {
                        Vector3 from = new(side * (halfW + 4f), y, z + dz * length);
                        if (!hull.Raycast(new Ray(root.TransformPoint(from), root.TransformDirection(Vector3.right * -side)), out RaycastHit hit, 8f)) { lo = float.NegativeInfinity; continue; }
                        float x = Mathf.Abs(root.InverseTransformPoint(hit.point).x);
                        lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
                    }
                if (float.IsInfinity(lo) || hi - lo >= bestSpread) continue;
                bestSpread = hi - lo; bestZ = z; bestOut = hi;
            }
            if (float.IsInfinity(bestSpread)) { Debug.LogWarning("Ship dressing: no hull behind a name plate"); return; }
            // Its back on the hull's most outboard point, a centimetre off it.
            b = ShipBounds(plate);
            float backX = side > 0f ? b.min.x : -b.max.x;
            plate.transform.localPosition += new Vector3(side * (bestOut + 0.01f - backX), 0f, bestZ - plate.transform.localPosition.z);
            b = ShipBounds(plate);
            float front = side > 0f ? b.max.x : -b.min.x;

            // The panel and the words, on their own unscaled holder turned as the plate is.
            var name = new GameObject("Ship Name");
            name.transform.SetParent(plate.transform.parent, false);
            name.transform.localPosition = new Vector3(side * front, b.center.y, b.center.z);
            name.transform.localRotation = plate.transform.localRotation; // +Z outboard, the way the plate faces
            float w = b.size.z * 0.9f, h = b.size.y * 0.78f;
            var panel = new GameObject("Backing");
            panel.transform.SetParent(name.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            panel.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(w, h, 0.02f), h / 2f);
            panel.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.SignBoard();
            Color letters = new(0.86f, 0.88f, 0.9f);
            // The name as big as the panel holds (at 45 m it was a few pixels high), the
            // line under it smaller.
            GameObject title = PropBuilder.Text(name, "Name", new Vector3(0f, h * 0.13f, 0.03f), h * 1.2f, letters, TextAnchor.MiddleCenter);
            SunkCost.Look.SignText titleText = title.AddComponent<SunkCost.Look.SignText>();
            titleText.Fit(w * 0.94f, h * 0.66f);
            titleText.Configure("company");
            GameObject sub = PropBuilder.Text(name, "Sub", new Vector3(0f, -h * 0.33f, 0.03f), h * 0.5f, letters, TextAnchor.MiddleCenter);
            SunkCost.Look.SignText subText = sub.AddComponent<SunkCost.Look.SignText>();
            subText.Fit(w * 0.6f, h * 0.25f);
            subText.Configure("ship.name.sub");
        }

        // ---- the deck's holes and the tower's top -----------------------------------

        // Where the hull's deck has a hole inside the side (SHIP-032: either side of
        // the tower at the transom, items fell through to the water; slots at the
        // bulwark's foot): deck plate just under the deck's level, so the hole reads as
        // deck, and a collider under it. Found by rays down the whole deck, right up
        // under the bulwark, so a hole anywhere is closed. The plate is one mesh over
        // the holes and a cell round them - under the deck where the deck is there, so
        // a hole's ragged edge shows plate, not a notch - with the deck's own metre UVs,
        // so it lines up with the plating round it and no two pieces overlap.
        private const string DeckPatchPath = "Assets/_Project/Art/HQ/Meshes/ShipDeckPatch.asset";

        private static void FillDeckHoles(Transform root, GameObject look, MeshCollider hull)
        {
            const float cell = 0.1f;
            Physics.SyncTransforms();
            float well = ShipStubBuilder.WellRadius + 0.15f;
            int nx = Mathf.CeilToInt(2f * halfW / cell), nz = Mathf.CeilToInt(2f * halfL / cell);
            float X(int i) => -halfW + (i + 0.5f) * cell;
            float Z(int k) => -halfL + (k + 0.5f) * cell;
            // Out to the side's outer face: under the wall a hole is hidden by its top,
            // past it is the hull's own outside.
            bool Inside(int i, int k) => Mathf.Abs(X(i)) < W(Z(k)) - 0.02f && X(i) * X(i) + Z(k) * Z(k) > well * well && !UnderTower(X(i), Z(k));
            var hole = new bool[nx, nz];
            var runs = new List<(float x0, float x1, float z)>();
            for (int k = 0; k < nz; k++)
            {
                int start = -1;
                for (int i = 0; i <= nx; i++)
                {
                    // A hole: no hull within 3 cm under the deck's level (its side falling away
                    // at a notch counts; a 3 cm ball found them).
                    if (i < nx && Inside(i, k))
                        hole[i, k] = !hull.Raycast(new Ray(root.TransformPoint(new Vector3(X(i), 0.3f, Z(k))), -root.up), out _, 0.33f);
                    if (i < nx && hole[i, k]) { if (start < 0) start = i; }
                    else if (start >= 0) { runs.Add((X(start) - cell / 2f, X(i - 1) + cell / 2f, Z(k))); start = -1; }
                }
            }
            if (runs.Count == 0) return;
            var filler = new GameObject("Deck Filler");
            filler.transform.SetParent(look.transform, false);
            foreach ((float x0, float x1, float z) in runs)
            {
                BoxCollider box = filler.AddComponent<BoxCollider>();
                box.center = new Vector3((x0 + x1) / 2f, -0.15f, z);
                box.size = new Vector3(x1 - x0 + cell, 0.3f, cell * 2f); // a cell over each way: no sliver between the rows
            }
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                {
                    bool near = false;
                    for (int dk = -1; dk <= 1 && !near; dk++)
                        for (int di = -1; di <= 1 && !near; di++)
                        {
                            int a = i + di, b = k + dk;
                            near = a >= 0 && b >= 0 && a < nx && b < nz && hole[a, b];
                        }
                    if (!near || !Inside(i, k)) continue;
                    float x0 = X(i) - cell / 2f, x1 = X(i) + cell / 2f, z0 = Z(k) - cell / 2f, z1 = Z(k) + cell / 2f;
                    int v = verts.Count;
                    verts.AddRange(new[] { new Vector3(x0, -0.004f, z0), new Vector3(x0, -0.004f, z1), new Vector3(x1, -0.004f, z1), new Vector3(x1, -0.004f, z0) });
                    uvs.AddRange(new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) });
                    tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                }
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DeckPatchPath);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh { name = "ShipDeckPatch" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            if (fresh) AssetDatabase.CreateAsset(mesh, DeckPatchPath); else EditorUtility.SetDirty(mesh);
            var plate = new GameObject("Deck Plate");
            plate.transform.SetParent(filler.transform, false);
            plate.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = plate.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShipModelSetup.DeckMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Debug.Log("Ship dressing: closed " + runs.Count + " holes in the deck");
        }

        private static bool UnderTower(float x, float z)
        {
            int k = Mathf.FloorToInt(x / TowerSlice);
            return towerFace.TryGetValue(k, out float face) && z <= face && z >= towerBack[k];
        }

        // ---- the couch and the other colliders --------------------------------------

        // A collider on the prop's mesh object, in the mesh's own space so it turns
        // and scales with the prop; the couches their seat and back.
        private static void Solidify(GameObject prop, bool byMesh)
        {
            if (prop == null) return;
            MeshFilter mf = prop.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            if (prop.name == "Couch" && MeasureCouch(prop, out CouchShape s))
            {
                Bounds a = s.All;
                BoxCollider seat = prop.AddComponent<BoxCollider>();
                seat.center = new Vector3(a.center.x, s.SeatTop / 2f, (s.SeatBack + a.max.z) / 2f);
                seat.size = new Vector3(a.size.x, s.SeatTop, a.max.z - s.SeatBack);
                BoxCollider back = prop.AddComponent<BoxCollider>();
                back.center = new Vector3(a.center.x, a.max.y / 2f, (a.min.z + s.BackFront) / 2f);
                back.size = new Vector3(a.size.x, a.max.y, s.BackFront - a.min.z);
                // The arms, where they rise over the seat, so no visible part stands in
                // front of the couch's colliders (ShipShellAudit).
                foreach ((float x0, float x1) in new[] { (a.min.x, s.ArmL), (s.ArmR, a.max.x) })
                {
                    if (x1 - x0 < 0.02f || s.ArmTop <= s.SeatTop + 0.02f) continue;
                    BoxCollider arm = prop.AddComponent<BoxCollider>();
                    arm.center = new Vector3((x0 + x1) / 2f, s.ArmTop / 2f, (s.BackFront + a.max.z) / 2f);
                    arm.size = new Vector3(x1 - x0, s.ArmTop, a.max.z - s.BackFront);
                }
                return;
            }
            if (byMesh)
            {
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                return;
            }
            BoxCollider box = mf.gameObject.AddComponent<BoxCollider>();
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }

        // ---- the ship's side --------------------------------------------------------

        private const float WallThickness = 0.35f;
        private const float BulwarkFoot = 0.12f; // how far the side reaches under the deck
        private const string BulwarkMeshPath = "Assets/_Project/Art/HQ/Meshes/ShipBulwark.asset";

        // The ship's side: a wall of Bulwark height standing on the hull's outline all
        // the way round - the hull model itself is flattened to the deck, so this is
        // its side, in the hull's steel - with a mesh collider. A generated wall
        // scaled to a height was never one height anywhere; this is.
        private static void BuildBulwark(GameObject parent)
        {
            // The outline as a closed loop: starboard bow to stern, the transom, port
            // stern to bow, the point.
            var loop = new List<Vector3>();
            int n = outline.Length;
            for (int i = n - 1; i >= 0; i--) loop.Add(new Vector3(W(-halfL + i * Step), 0f, -halfL + i * Step));
            for (int i = 0; i < n; i++) loop.Add(new Vector3(-W(-halfL + i * Step), 0f, -halfL + i * Step));
            int count = loop.Count;
            var inward = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 t = loop[(i + 1) % count] - loop[(i - 1 + count) % count];
                // The loop runs one way round (starboard aft, the transom, port forward, the
                // point), so the inside is always the same side of it. Choosing the side
                // that faced the ship's middle turned the wall inside out at the sharp kinks
                // aft (x 8.8, z -18.8): its top faced down, and small items fell through the
                // folded wall into the hull (SHIP-032).
                inward[i] = new Vector3(t.z, 0f, -t.x).normalized;
            }

            // From just under the deck plane: a wall that started exactly on it showed
            // slivers of light where the hull's deck dips at the edge.
            Mesh mesh = StripMesh(loop, inward, -BulwarkFoot, Bulwark, -1, BulwarkMeshPath, "ShipBulwark");
            GameObject wall = new("Bulwark");
            wall.transform.SetParent(parent.transform, false);
            wall.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = wall.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShipModelSetup.HullMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // the side shades the deck along it (SHIP-016)
            wall.AddComponent<MeshCollider>().sharedMesh = mesh;

            // The guard: an unseen wall on top of the side, up to GuardHeight, so nobody
            // goes over - not from a crate, a barrel or a container by the rail (Dan, 23
            // September 2026: "invisible walls above the sides of the ship, so no one
            // can fall"). Open across the transom over the tower's roof - the way from
            // the rig's bridge comes in over the stern at the roof's height - and closed
            // either side of it and below it: the tower model is open above its walls,
            // and a body up there walked out over the stern (SHIP-033).
            Mesh guardMesh = StripMesh(loop, inward, Bulwark, GuardHeight, n - 1, GuardMeshPath, "ShipGuard");
            GameObject guard = new("Bulwark Guard");
            guard.transform.SetParent(parent.transform, false);
            guard.AddComponent<MeshCollider>().sharedMesh = guardMesh;
            float towerHalf = ShipStubBuilder.TowerWidth / 2f, sternW = W(-halfL + 0.25f);
            foreach (float side in new[] { -1f, 1f })
            {
                BoxCollider box = guard.AddComponent<BoxCollider>();
                box.center = new Vector3(side * (towerHalf + sternW) / 2f, GuardHeight / 2f, -halfL + WallThickness / 2f);
                box.size = new Vector3(sternW - towerHalf, GuardHeight, WallThickness);
            }
            BoxCollider span = guard.AddComponent<BoxCollider>();
            float roof = ShipStubBuilder.TowerHeight;
            span.center = new Vector3(0f, (Bulwark + roof) / 2f, -halfL + WallThickness / 2f);
            span.size = new Vector3(2f * towerHalf, roof - Bulwark, WallThickness);
        }

        private const float GuardHeight = 8f; // over the tower's roof and anything stacked by the rail
        private const string GuardMeshPath = "Assets/_Project/Art/HQ/Meshes/ShipGuard.asset";

        // A wall strip on the outline from bottom to top, WallThickness deep: outer face,
        // inner face and top for every segment but `skip`; UVs in metres. Saved as an
        // asset: a mesh made on the fly is lost when the prefab is saved.
        private static Mesh StripMesh(List<Vector3> loop, Vector3[] inward, float bottom, float top, int skip, string path, string name)
        {
            int count = loop.Count;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float along = 0f, h = top - bottom;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 lift = Vector3.up * bottom, up = Vector3.up * h;
                Vector3 ao = loop[i] + lift, bo = loop[j] + lift;
                Vector3 ai = ao + inward[i] * WallThickness, bi = bo + inward[j] * WallThickness;
                float len = Vector3.Distance(ao, bo);
                if (i == skip) { along += len; continue; }
                int v = verts.Count;
                verts.AddRange(new[] { ao, ao + up, bo + up, bo, ai, ai + up, bi + up, bi, ao + up, ai + up, bi + up, bo + up });
                uvs.AddRange(new[] {
                    new Vector2(along, 0f), new Vector2(along, h), new Vector2(along + len, h), new Vector2(along + len, 0f),
                    new Vector2(along, 0f), new Vector2(along, h), new Vector2(along + len, h), new Vector2(along + len, 0f),
                    new Vector2(along, 0f), new Vector2(along, WallThickness), new Vector2(along + len, WallThickness), new Vector2(along + len, 0f) });
                tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });                 // outer: faces away from the ship
                tris.AddRange(new[] { v + 4, v + 5, v + 6, v + 4, v + 6, v + 7 });         // inner: faces the deck
                tris.AddRange(new[] { v + 8, v + 10, v + 9, v + 8, v + 11, v + 10 });      // top: faces up
                along += len;
            }
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ---- the hull's outline -----------------------------------------------------

        private const float Step = 0.5f;
        private static float[] outline; // the half-width at deck height, every Step of z from -halfL

        // Read off the hull mesh: for each slice of length, the widest point between
        // the deck and the bulwark's top. Gaps filled from their neighbours, then
        // smoothed, so the generation's lumps do not kink the line of lamps.
        private static void SampleOutline(Transform root, GameObject hull)
        {
            int n = Mathf.RoundToInt(ShipStubBuilder.DeckLength / Step) + 1;
            outline = new float[n];
            MeshFilter mf = hull.GetComponentInChildren<MeshFilter>();
            Matrix4x4 toShip = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 p = toShip.MultiplyPoint3x4(v);
                if (p.y < -0.05f || p.y > Bulwark + 0.05f) continue;
                int i = Mathf.RoundToInt((p.z + halfL) / Step);
                if (i < 0 || i >= n) continue;
                outline[i] = Mathf.Max(outline[i], Mathf.Abs(p.x));
            }
            for (int i = 0; i < n; i++)
                if (outline[i] <= 0f)
                    for (int d = 1; d < n; d++)
                    {
                        if (i - d >= 0 && outline[i - d] > 0f) { outline[i] = outline[i - d]; break; }
                        if (i + d < n && outline[i + d] > 0f) { outline[i] = outline[i + d]; break; }
                    }
            float[] smooth = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int count = 0;
                for (int d = -2; d <= 2; d++)
                    if (i + d >= 0 && i + d < n) { sum += outline[i + d]; count++; }
                smooth[i] = sum / count;
            }
            outline = smooth;
        }

        // The hull's half-width at this length, from the outline.
        public static float W(float z)
        {
            if (outline == null) return halfW;
            float f = Mathf.Clamp((z + halfL) / Step, 0f, outline.Length - 1.001f);
            int i = Mathf.FloorToInt(f);
            return Mathf.Lerp(outline[i], outline[i + 1], f - i);
        }

        private static readonly Dictionary<string, GameObject> Cache = new();

        // A prop at a place and a turn, at its scale from the table; solid unless it is
        // the deck itself, hangs outboard or is the game's own collider.
        private static GameObject Place(GameObject parent, string part, Vector3 position, float yaw)
        {
            if (!Cache.TryGetValue(part, out GameObject prefab) || prefab == null)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelSetup.PrefabPath(part));
                Cache[part] = prefab;
            }
            if (prefab == null)
            {
                Debug.LogWarning("Ship dressing: no prefab for " + part + " (run the ship model setup)");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * ScaleOf(part);
            instance.name = part;
            if (!NotSolid.Contains(part)) solid.Add((instance, ByMesh.Contains(part)));
            return instance;
        }
    }
}
