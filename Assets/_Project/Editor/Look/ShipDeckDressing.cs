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
    // (lamps, bollards, the name) follows the same outline, read off the mesh. Every
    // prop stands at twice its brief size (Dan: "everything 2x") and every prop is
    // solid (Dan: "all the things in the ship are walk through").
    //
    // The lounge is the bow: the deck TV faces aft from the point, the couches in
    // front of it. The working end is the stern: the crane, the cargo, the storage
    // room, beside the tower. The elevator stays the game's own glass car; a look
    // for it is a separate job (Dan: "we will do a new one after").
    //
    // Run from the ship stub builder, last, after every box exists to be hidden.
    public static class ShipDeckDressing
    {
        public const string LookName = "Look";

        // The bulwark's height over the deck, set in prepare_ship_part.py
        // (HULL_BULWARK) where the hull is cut to it.
        public const float Bulwark = 1.2f;

        // The props' scale over the brief's metres. Dan asked for everything at 2x
        // (23 September 2026), then found the crew looking up at the table and unable
        // to sit on the couch: what a person uses stays at a person's size, what a
        // crane lifts stays big. The hull, the tower and the storage room are the
        // game's own sizes and stay at one.
        public const float Scale = 2f;          // machinery: the crane, the winch
        public const float GearScale = 1.5f;    // deck gear: barrels, crates, lamps, bollards, buoys, pipes, coils, ladder, signs
        public const float HumanScale = 1.25f;  // what the crew touch: couch, table, bench, toolbox
        private static readonly Dictionary<string, float> Scales = new()
        {
            ["Couch"] = HumanScale, ["Table"] = HumanScale, ["Bench"] = HumanScale, ["Toolbox"] = HumanScale,
            ["Barrel"] = GearScale, ["Crate"] = GearScale, ["DeckLamp"] = GearScale, ["Bollard"] = GearScale,
            ["Lifebuoy"] = GearScale, ["Pipes"] = GearScale, ["CableCoil"] = GearScale, ["Ladder"] = GearScale,
            ["Signs"] = GearScale, ["NamePlate"] = GearScale,
            ["Container"] = GearScale, // 9 m: a 12 m one reached from the well to the stern taper
        };
        private static float ScaleOf(string part) => Scales.TryGetValue(part, out float s) ? s : Scale;

        // What loses its renderer and keeps its collider: the shapes a model now covers
        // whose collider is the game's (the storage room's walls, the monitor's desk).
        private static readonly string[] HiddenPrefixes =
        {
            "Hull", "Tower", "Storage", "Monitor Frame", "Monitor Console", // the crew screen keeps its frame: it hangs on the tower's face
        };

        // What goes entirely, renderer and collider: the decoration the models stand
        // in for, and the tower roof's rails, lamps and gate that stood on the hidden
        // tower block (Dan, 23 September 2026: "remove those gates and the invisible
        // platform they are on"). The tower block's own box goes too (below), so the
        // tower model's shape is what the crew collides with.
        private static readonly string[] RemovedPrefixes =
        {
            "Bridge Windows", "Bridge Label", "Bridge Door", "Bridge Sign",
            "TvPost", "TvFrame", "Fender", "Tyre", "Radar", "Antenna", "Funnel", "Beacon",
            "Roof Rail", "Roof Lamp", "Roof Gate", "Roof Gap",
        };

        // What the models never replace, whatever its name begins with: the glass
        // cabin, the monitor and its buttons, the screens the game writes on.
        private static readonly string[] KeptPrefixes =
        {
            "DeckCabin", "Monitor", "TvScreen", "Tv Caption", "Crew Screen Text",
            "Storage Sign", "StorageReadout", // the room's readout: the game writes on it
        };

        private static float halfL, halfW;
        private static readonly List<(GameObject, bool)> solid = new(); // placed props and whether they collide by mesh

        public static void Build(Transform root)
        {
            // The old dressing goes entirely; the models replace it.
            Transform old = root.Find(LookName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            solid.Clear();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null || KeptPrefixes.Any(p => t.name.StartsWith(p))) continue;
                if (RemovedPrefixes.Any(p => t.name.StartsWith(p))) { Object.DestroyImmediate(t.gameObject); continue; }
                if (!HiddenPrefixes.Any(p => t.name.StartsWith(p))) continue;
                // The whole branch stops being seen: a hidden box often carries its
                // trim and its lamps as children. The colliders below it stay.
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true).ToArray())
                {
                    if (r == null || KeptPrefixes.Any(p => r.name.StartsWith(p))) continue;
                    // A TextMesh owns its renderer and will not give it up; the sign goes
                    // as a whole, since the model carries its own lettering.
                    if (r.GetComponent<TextMesh>() != null) { r.enabled = false; continue; }
                    MeshFilter filter = r.GetComponent<MeshFilter>();
                    Object.DestroyImmediate(r);
                    if (filter != null) Object.DestroyImmediate(filter);
                }
            }

            GameObject look = new(LookName);
            look.transform.SetParent(root, false);

            halfL = ShipStubBuilder.DeckLength / 2f;
            halfW = ShipStubBuilder.DeckWidth / 2f;
            float towerZ = -halfL + ShipStubBuilder.TowerDepth / 2f;
            float towerFront = -halfL + ShipStubBuilder.TowerDepth;
            float towerHalf = ShipStubBuilder.TowerWidth / 2f;
            float well = ShipStubBuilder.WellRadius;

            // ---- the ship itself -------------------------------------------------

            // The hull's deck is at its origin (prepare_ship_part's hull steps): the
            // ship's origin is the deck, so the hull stands at zero, at its own size.
            GameObject hull = Place(look, "Hull", Vector3.zero, Quaternion.identity, 1f, false);
            SampleOutline(root, hull);
            BuildBulwark(look);
            // The tower: the model's own shape is its collider. The game's box (8 x 6 x
            // 6, hidden) came with a flat roof nothing stands on now - the invisible
            // platform the crew found from the deck - so its collider goes.
            GameObject tower = Place(look, "Tower", new Vector3(0f, 0f, towerZ), Quaternion.identity, 1f, false);
            Transform towerBlock = root.Find("Tower");
            if (towerBlock != null) foreach (Collider c in towerBlock.GetComponents<Collider>()) Object.DestroyImmediate(c);
            solid.Add((tower, true));

            // The walkable rim between the hull's deck and the round hole wears the
            // deck's plate, like the hull's deck faces do (its UVs are in metres too).
            Material deck = ShipModelSetup.DeckMaterial();
            foreach (string rim in new[] { "Well Rim", "Well Rim Under" })
            {
                Transform t = root.Find(rim);
                if (t != null && t.TryGetComponent(out MeshRenderer renderer)) renderer.sharedMaterial = deck;
            }

            // The name on the bow, both sides, where the hull is still wide; outboard,
            // so it collides with nothing.
            foreach (float side in new[] { -1f, 1f })
                Place(look, "NamePlate", new Vector3(side * (W(12f) + 0.1f), -2.6f, 12f), Quaternion.Euler(0f, side * 90f, 0f), Scale, false);

            // ---- along the edge, following the hull ------------------------------

            // A lamp every 12 m, a bollard between them, two lifebuoys a side; all
            // inboard of the bulwark by their own doubled size.
            // The width is read at the length the prop stands at - the starboard row
            // is staggered 6 m forward, and the bow narrows (Dan found a lamp on the sea).
            foreach (float side in new[] { -1f, 1f })
            {
                float stagger = side > 0f ? 6f : 0f;
                for (float z = -halfL + 5f + stagger; z < halfL - 6f; z += 12f)
                    Place(look, "DeckLamp", new Vector3(side * (W(z) - 1.6f), 0f, z));
                for (float z = -halfL + 11f + stagger; z < halfL - 8f; z += 12f)
                    Place(look, "Bollard", new Vector3(side * (W(z) - 1.7f), 0f, z));
                foreach (float z in new[] { -9f, 9f })
                    Place(look, "Lifebuoy", new Vector3(side * (W(z) - 1.0f), 0f, z), Quaternion.Euler(0f, side > 0f ? -90f : 90f, 0f));
            }

            // ---- the well ----------------------------------------------------------

            // The game's glass elevator stands in the well as it is; round it the
            // bollards, and to starboard the winch that runs the car and its cable.
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
                Place(look, "Bollard", new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * (well + 2.2f), 0f, Mathf.Cos(a * Mathf.Deg2Rad) * (well + 2.2f)));
            Place(look, "Winch", new Vector3(well + 2.4f, 0f, -3.5f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "CableCoil", new Vector3(well + 3.4f, 0f, -7.0f));
            Place(look, "Toolbox", new Vector3(well + 2.2f, 0f, -9.2f), Quaternion.Euler(0f, -14f, 0f));

            // ---- the lounge: the bow ---------------------------------------------

            // The deck TV the game builds stands at the bow facing aft
            // (ShipStubBuilder.BuildTv); the cabinet is its housing, the screen just
            // proud of the cabinet's face. Two couches in front of it, a table between
            // the benches, lamps either side: the living end of the ship (Dan).
            float tv = halfL - 5f;
            GameObject cabinet = Place(look, "TvCabinet", new Vector3(0f, 0f, tv + 0.45f), Quaternion.Euler(0f, 180f, 0f));
            DressTv(root, cabinet);
            foreach (float x in new[] { -2.1f, 2.1f })
                Place(look, "Couch", new Vector3(x, 0f, tv - 5.5f));
            Place(look, "Table", new Vector3(0f, 0f, tv - 9.5f));
            Place(look, "Bench", new Vector3(-5.2f, 0f, tv - 7f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Bench", new Vector3(5.2f, 0f, tv - 7f), Quaternion.Euler(0f, -90f, 0f));
            foreach (float side in new[] { -1f, 1f })
                Place(look, "DeckLamp", new Vector3(side * 6.2f, 0f, tv - 2f));
            Place(look, "Crate", new Vector3(-6.4f, 0f, tv - 12f), Quaternion.Euler(0f, 22f, 0f));
            Place(look, "Barrel", new Vector3(6.2f, 0f, tv - 11.5f));
            Place(look, "Barrel", new Vector3(7.4f, 0f, tv - 12.8f));
            Place(look, "Signs", new Vector3(-W(tv - 6f) + 1.0f, 0.6f, tv - 6f), Quaternion.Euler(0f, 90f, 0f)); // on the side's inner face, below its top

            // ---- the working stern ------------------------------------------------

            // The crane to starboard of the well with its boom over the side (solid by
            // its own shape, so the crew walks under the boom), crates and barrels
            // astern of it, the coil of cable.
            Place(look, "Crane", new Vector3(6.5f, 0f, -6.5f), Quaternion.Euler(0f, 90f, 0f), Scale, true); // boom out over the starboard side, not over heads on the deck
            Place(look, "Crate", new Vector3(6.8f, 0f, -16.4f), Quaternion.Euler(0f, 18f, 0f));
            Place(look, "Crate", new Vector3(6.8f, 1.2f, -16.6f), Quaternion.Euler(0f, -32f, 0f));
            Place(look, "Barrel", new Vector3(7.4f, 0f, -13.2f));
            Place(look, "Barrel", new Vector3(7.0f, 0f, -14.8f));
            Place(look, "CableCoil", new Vector3(3.4f, 0f, -18.2f));
            Place(look, "Signs", new Vector3(W(-11f) - 0.9f, 0.6f, -11f), Quaternion.Euler(0f, -90f, 0f));

            // Port side, the cargo: a stack of two containers against the rail, the
            // plant and pipework, barrels, a second coil.
            Place(look, "Container", new Vector3(-5.6f, 0f, -10f), Quaternion.Euler(0f, 90f, 0f)); // one, not a stack (Dan), between the well and the stern taper
            Place(look, "Pipes", new Vector3(-W(-20f) + 2.2f, 0f, -20f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Barrel", new Vector3(-6.8f, 0f, -12.4f));
            Place(look, "Barrel", new Vector3(-5.6f, 0f, -13.2f));
            Place(look, "CableCoil", new Vector3(-6.0f, 0f, -20.6f));
            Place(look, "Toolbox", new Vector3(-3.4f, 0f, -18.6f), Quaternion.Euler(0f, 24f, 0f));

            // Round the well, clear of the spawn points at (+-3.5, 7) and (+-3.5, 10).
            Place(look, "Crate", new Vector3(-6.6f, 0f, 3.2f), Quaternion.Euler(0f, 8f, 0f));
            Place(look, "Barrel", new Vector3(-7.0f, 0f, 5.2f));
            Place(look, "Toolbox", new Vector3(-7.2f, 0f, 9.0f), Quaternion.Euler(0f, -30f, 0f));
            Place(look, "Pipes", new Vector3(W(4f) - 2.2f, 0f, 4f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Crate", new Vector3(6.4f, 0f, 9.6f), Quaternion.Euler(0f, -17f, 0f));

            // ---- the stern: the tower, the storage room, the working gear ---------

            // The storage room to starboard of the tower, its doorway toward the centre
            // line exactly where the game's walls and sill already are: the game's
            // sizes, and the game's colliders.
            // The model was fitted to the brief's 2.8 x 3.0 x 2.2 m; the game's room is
            // ShipStubBuilder's, so the model is stretched to those numbers exactly.
            float sx = ShipStubBuilder.StorageCentreX, sz = ShipStubBuilder.StorageCentreZ;
            GameObject room = Place(look, "StorageRoom", new Vector3(sx, 0f, sz), Quaternion.identity, 1f, false);
            if (room != null) room.transform.localScale = new Vector3(ShipStubBuilder.StorageWidth / 2.8f, ShipStubBuilder.StorageHeight / 2.2f, ShipStubBuilder.StorageDepth / 3.0f);
            GameObject sill = Place(look, "StorageSill", new Vector3(sx - ShipStubBuilder.StorageWidth / 2f, 0f, sz), Quaternion.Euler(0f, 90f, 0f), 1f, false);
            if (sill != null) sill.transform.localScale = Vector3.one * (ShipStubBuilder.StorageDoor / 1.4f);
            // The crew screen hangs on the tower's face as thin panels: solid, so a face
            // pressed to its edge stops at the edge (ShipShellAudit found it).
            foreach (string panel in new[] { "Crew Screen Frame", "Crew Screen" })
            {
                Transform t = root.Find(panel);
                if (t != null && t.GetComponent<Collider>() == null) t.gameObject.AddComponent<BoxCollider>();
            }
            // The console on the tower's forward face is the panel the crew press to
            // sail: the game's monitor screen, its three buttons and its status line
            // move onto the model's front face (Dan, 23 September 2026: "replace the
            // last thing that helped the player choose, now use the new display"). At
            // one and a half so the buttons stay at hand height.
            GameObject console = Place(look, "Console", new Vector3(0f, 0f, towerFront + 0.75f), Quaternion.identity, 1.5f, false);
            DressConsole(root, console);
            // A ladder and the plant on the tower's flanks, signs on its face and the room.
            Place(look, "Ladder", new Vector3(-towerHalf - 0.4f, 0f, towerZ), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Pipes", new Vector3(towerHalf + 0.9f, 0f, towerZ + 1.2f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Barrel", new Vector3(-towerHalf - 2.8f, 0f, -halfL + 3.2f));
            Place(look, "Barrel", new Vector3(-towerHalf - 4.2f, 0f, -halfL + 3.8f));
            Place(look, "Toolbox", new Vector3(towerHalf + 2.6f, 0f, towerZ - 2.4f), Quaternion.Euler(0f, -14f, 0f));
            Place(look, "Signs", new Vector3(-2.4f, 2.6f, towerFront + 0.6f), Quaternion.identity); // beside the crew screen, on the tower's face
            Place(look, "Signs", new Vector3(sx, ShipStubBuilder.StorageHeight * 0.55f, sz + ShipStubBuilder.StorageDepth / 2f + 0.06f), Quaternion.identity, 1f, true);

            // The models bring no colliders of their own; every prop then gets one
            // (a box round its mesh, or the mesh itself where the shape matters), and
            // the hull's own mesh is the deck and the walls.
            foreach (Collider c in look.GetComponentsInChildren<Collider>(true))
                if (!c.gameObject.name.StartsWith("Bulwark")) Object.DestroyImmediate(c); // the side and its guard keep theirs
            // Nothing stands off the deck (Dan: "I dont want anything floating weird"):
            // a prop whose footprint reaches past the hull's outline is removed and
            // named, rather than left hanging over the sea.
            for (int i = solid.Count - 1; i >= 0; i--)
            {
                GameObject prop = solid[i].Item1;
                if (prop == null || prop.name == "Tower" || OverTheDeck(root, prop)) continue; // the tower is the game's structure, wider than the stern's outline
                Debug.LogWarning("Ship dressing: " + prop.name + " at " + prop.transform.localPosition + " reaches past the hull; removed");
                Object.DestroyImmediate(prop);
                solid.RemoveAt(i);
            }
            foreach ((GameObject prop, bool byMesh) in solid) Solidify(prop, byMesh);
            MountSigns(root, look);
            MeshFilter hullMesh = hull.GetComponentInChildren<MeshFilter>();
            hullMesh.gameObject.AddComponent<MeshCollider>().sharedMesh = hullMesh.sharedMesh;
        }

        // The game's TV onto the cabinet model's face (Dan, 23 September 2026: "the TV
        // should be on the TV"): the screen quad sits just proud of the cabinet's
        // front, sized to it at sixteen by nine; the caption loses its plate and its
        // glow strips and its words sit on the screen itself ("write it on the TV");
        // the speaker point moves with the screen.
        private static void DressTv(Transform root, GameObject cabinet)
        {
            if (cabinet == null) return;
            // The picture goes exactly on the model's own screen panel and nowhere else
            // (Dan, 23 September 2026: "exactly the screen 3D model, I dont want to see
            // anything other than that"): the panel is measured off the mesh, and the
            // game's screen quad - what ShipTV draws the channel onto, and what E
            // presses - covers that rectangle, 5 mm proud of it.
            if (!ScreenPanel(root, cabinet, out Vector3 centre, out Vector2 size))
            {
                Debug.LogWarning("Ship dressing: no screen panel found on the TV cabinet");
                return;
            }
            Transform screen = root.Find(ShipParts.TvScreenName);
            if (screen != null)
            {
                // The whole panel, edge to edge: the 16:9 feed is drawn 2.3:1, a little
                // wide. Pillarboxed it read as a slab on the screen again, and cropped
                // it would cut the diver's visor readouts that spectators rely on.
                float w = size.x, h = size.y;
                screen.localPosition = centre + new Vector3(0f, 0f, -0.005f);
                screen.localScale = new Vector3(w, h, 1f);
                // What E finds: the whole panel, reaching 30 cm out past the frame toward
                // the viewer, so the crosshair meets the screen before the cabinet's box.
                if (screen.TryGetComponent(out BoxCollider press)) { press.center = new Vector3(0f, 0f, -0.15f); press.size = new Vector3(size.x / w, size.y / h, 0.3f); }
            }
            // The channel's name is the screen's own line of text, top centre, no plate.
            Transform caption = root.Find("Tv Caption Sign");
            if (caption != null)
            {
                caption.localPosition = centre + new Vector3(0f, size.y * 0.38f, -0.01f);
                foreach (Transform part in caption.Cast<Transform>().ToArray())
                    if (part.name == "Plate" || part.name.StartsWith("Frame")) Object.DestroyImmediate(part.gameObject);
                var text = caption.GetComponentInChildren<TextMesh>();
                if (text != null) text.characterSize *= 1.4f;
            }
            Transform speaker = root.Find(ShipParts.TvSpeakerName);
            if (speaker != null) speaker.localPosition = centre + new Vector3(0f, 0f, -0.1f);
        }

        // The cabinet's screen panel, in ship space: of the triangles facing aft (the
        // way the cabinet looks), the flat layer with the most area is the panel - the
        // frame and the legs are thin - and its extent there is the screen.
        private static bool ScreenPanel(Transform root, GameObject cabinet, out Vector3 centre, out Vector2 size)
        {
            centre = default; size = default;
            MeshFilter mf = cabinet.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return false;
            Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] v = mf.sharedMesh.vertices; int[] tri = mf.sharedMesh.triangles;
            var faces = new List<(Vector3 a, Vector3 b, Vector3 c, float area)>();
            var layers = new Dictionary<int, float>(); // area by 1 cm of depth
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[tri[i]]), b = m.MultiplyPoint3x4(v[tri[i + 1]]), c = m.MultiplyPoint3x4(v[tri[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a);
                float area = n.magnitude / 2f;
                if (area < 1e-6f || n.normalized.z > -0.97f) continue; // facing aft: -z in the ship
                faces.Add((a, b, c, area));
                int key = Mathf.RoundToInt((a.z + b.z + c.z) / 3f * 100f);
                layers[key] = layers.TryGetValue(key, out float s) ? s + area : area;
            }
            if (layers.Count == 0) return false;
            int panel = layers.OrderByDescending(kv => kv.Value).First().Key;
            // The layer holds the panel and whatever is flush with it (the legs run down
            // from it): the screen is the largest solid rectangle in the layer. Cover a
            // 5 cm grid with the layer's triangles, then find that rectangle.
            const float cell = 0.05f;
            Vector2 lo = new(float.PositiveInfinity, float.PositiveInfinity), hi = new(float.NegativeInfinity, float.NegativeInfinity);
            var flat = faces.Where(f => Mathf.Abs(Mathf.RoundToInt((f.a.z + f.b.z + f.c.z) / 3f * 100f) - panel) <= 3).ToList();
            foreach (var f in flat) foreach (Vector3 q in new[] { f.a, f.b, f.c }) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
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
            centre = new Vector3((px0 + px1) / 2f, (py0 + py1) / 2f, panel / 100f);
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

        // The game's monitor onto the console model's face: the screen block (the
        // ShipMonitor's target), the three buttons and the status line keep their
        // heights and move to just proud of the model's front, so what the crew
        // press is the new display. The old desk under the screen loses its collider;
        // the model's own box takes over.
        private static void DressConsole(Transform root, GameObject console)
        {
            if (console == null) return;
            Renderer r = console.GetComponentInChildren<Renderer>();
            float front = root.InverseTransformPoint(r.bounds.center).z + r.bounds.extents.z;
            Transform monitor = root.Find(ShipParts.MonitorName);
            if (monitor != null) monitor.localPosition = new Vector3(0f, monitor.localPosition.y, front + 0.06f);
            foreach (string name in new[] { ShipParts.MonitorButtonSite01Name, ShipParts.MonitorButtonHQName, ShipParts.MonitorButtonEndDayName })
            {
                Transform b = root.Find(name);
                if (b != null) b.localPosition = new Vector3(b.localPosition.x, b.localPosition.y, front + 0.12f);
            }
            Transform status = root.Find(ShipParts.MonitorStatusName);
            if (status != null) status.localPosition = new Vector3(status.localPosition.x, status.localPosition.y, front + 0.13f);
            Transform desk = root.Find("Monitor Console");
            if (desk != null) foreach (Collider c in desk.GetComponents<Collider>()) Object.DestroyImmediate(c);
        }

        // Whether a prop's footprint lies inside the hull's outline: the four bottom
        // corners of its bounds, in ship space, each inboard of the edge at their own
        // length. The crane's boom is meant to hang over the side, so only the crane
        // is judged by its foot (its centre) rather than its whole span.
        private static bool OverTheDeck(Transform root, GameObject prop)
        {
            Renderer r = prop.GetComponentInChildren<Renderer>();
            if (r == null) return true;
            Bounds b = r.bounds;
            if (prop.name == "Crane")
            {
                Vector3 foot = root.InverseTransformPoint(prop.transform.position);
                return Mathf.Abs(foot.x) < W(foot.z) - 0.5f && Mathf.Abs(foot.z) < halfL - 0.5f;
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 corner = root.InverseTransformPoint(new Vector3(b.center.x + sx * b.extents.x, b.min.y, b.center.z + sz * b.extents.z));
                    if (Mathf.Abs(corner.x) > W(corner.z) - 0.15f || Mathf.Abs(corner.z) > halfL - 0.15f) return false;
                }
            return true;
        }

        // Every sign flat against something (Dan: "most of the signs are floating in
        // the air"). A sign reads along its +Z; from just in front of it, a ray goes
        // back through it to the first surface of the ship behind, and the sign is
        // moved so its back touches that surface. A sign with nothing within reach
        // behind it is removed and named rather than left hanging.
        private static void MountSigns(Transform root, GameObject look)
        {
            Physics.SyncTransforms();
            foreach (Transform t in look.transform.Cast<Transform>().Where(t => t.name == "Signs").ToArray())
            {
                MeshFilter mf = t.GetComponentInChildren<MeshFilter>();
                Renderer r = t.GetComponentInChildren<Renderer>();
                if (mf == null || r == null) continue;
                Vector3 back = -t.forward;
                // Its half-thickness along the way it faces, in world space: the mesh
                // inside the prefab is Z-up, so its own axes do not say which is depth.
                Vector3 e = r.bounds.extents, f = t.forward;
                float depth = Mathf.Abs(f.x) * e.x + Mathf.Abs(f.y) * e.y + Mathf.Abs(f.z) * e.z;
                Vector3 centre = r.bounds.center;
                Vector3 from = centre - back * 0.6f;
                float best = float.PositiveInfinity;
                foreach (RaycastHit h in Physics.RaycastAll(from, back, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.transform.IsChildOf(t) || !h.collider.transform.IsChildOf(root) || !IsStructure(h.collider.transform)) continue;
                    if (h.distance < best) best = h.distance;
                }
                if (float.IsInfinity(best))
                {
                    Debug.LogWarning("Ship dressing: a sign at " + t.localPosition + " has nothing behind it; removed");
                    Object.DestroyImmediate(t.gameObject);
                    continue;
                }
                // Its back face to the surface, a centimetre off it.
                float move = best - 0.6f - depth - 0.01f;
                t.position += back * move;
            }
            Physics.SyncTransforms();
        }

        // What a sign may hang on: the ship's side, the tower, the storage room's walls.
        private static bool IsStructure(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name.StartsWith("Bulwark") || p.name == "Tower" || p.name.StartsWith("StorageWall") || p.name == "StorageRoom") return true;
            return false;
        }

        // A collider on the prop's mesh object, in the mesh's own space so it turns
        // and scales with the prop.
        private static void Solidify(GameObject prop, bool byMesh)
        {
            if (prop == null) return;
            MeshFilter mf = prop.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
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
                Vector3 nrm = new Vector3(-t.z, 0f, t.x).normalized;
                if (Vector3.Dot(nrm, -loop[i]) < 0f) nrm = -nrm; // toward the ship's centre line
                inward[i] = nrm;
            }

            Mesh mesh = StripMesh(loop, inward, 0f, Bulwark, -1, BulwarkMeshPath, "ShipBulwark");
            GameObject wall = new("Bulwark");
            wall.transform.SetParent(parent.transform, false);
            wall.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = wall.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShipModelSetup.HullMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wall.AddComponent<MeshCollider>().sharedMesh = mesh;

            // The guard: an unseen wall on top of the side, up to GuardHeight, so nobody
            // goes over - not from a crate, a barrel or a container by the rail (Dan, 23
            // September 2026: "invisible walls above the sides of the ship, so no one
            // can fall"). Open across the transom where the tower stands - the way from
            // the rig's bridge comes in over the stern - and closed either side of it.
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

        private static GameObject Place(GameObject parent, string part, Vector3 position) => Place(parent, part, position, Quaternion.identity, ScaleOf(part), false);
        private static GameObject Place(GameObject parent, string part, Vector3 position, Quaternion rotation) => Place(parent, part, position, rotation, ScaleOf(part), false);

        // A prop at a place, a turn and a scale; solid unless it is the hull (its own
        // collider) or stands outboard. byMesh: collide with the shape, not a box.
        private static GameObject Place(GameObject parent, string part, Vector3 position, Quaternion rotation, float scale, bool byMesh)
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
            instance.transform.localRotation = rotation;
            instance.transform.localScale = Vector3.one * scale;
            instance.name = part;
            if (part != "Hull" && part != "Tower" && part != "NamePlate" && part != "StorageRoom" && part != "StorageSill") solid.Add((instance, byMesh)); // those four have the game's own colliders or stand outboard
            return instance;
        }
    }
}
