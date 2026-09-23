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

        // The props' scale over the brief's metres (Dan, 23 September 2026: "making
        // everything 2x"). The hull, the tower and the storage room are the game's
        // own sizes and stay at one.
        public const float Scale = 2f;

        // What loses its renderer and keeps its collider: the shapes a model now covers
        // whose collider is the game's (the storage room's walls, the monitor's desk).
        private static readonly string[] HiddenPrefixes =
        {
            "Hull", "Tower", "Storage", "Monitor Frame", "Monitor Console", "Crew Screen Frame",
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
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z = -halfL + 5f; z < halfL - 4f; z += 12f)
                    Place(look, "DeckLamp", new Vector3(side * (W(z) - 1.6f), 0f, z + (side > 0f ? 6f : 0f)));
                for (float z = -halfL + 11f; z < halfL - 6f; z += 12f)
                    Place(look, "Bollard", new Vector3(side * (W(z) - 1.7f), 0f, z + (side > 0f ? 6f : 0f)));
                foreach (float z in new[] { -9f, 9f })
                    Place(look, "Lifebuoy", new Vector3(side * (W(z) - 1.0f), 0f, z), Quaternion.Euler(0f, side > 0f ? -90f : 90f, 0f));
            }

            // ---- the well ----------------------------------------------------------

            // The game's glass elevator stands in the well as it is; round it the
            // bollards, and to starboard the winch that runs the car and its cable.
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
                Place(look, "Bollard", new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * (well + 2.2f), 0f, Mathf.Cos(a * Mathf.Deg2Rad) * (well + 2.2f)));
            Place(look, "Winch", new Vector3(well + 3.6f, 0f, -3.5f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "CableCoil", new Vector3(well + 3.4f, 0f, -7.0f));
            Place(look, "Toolbox", new Vector3(well + 2.2f, 0f, -9.2f), Quaternion.Euler(0f, -14f, 0f));

            // ---- the lounge: the bow ---------------------------------------------

            // The deck TV the game builds stands at the bow facing aft
            // (ShipStubBuilder.BuildTv); the cabinet is its housing, the screen just
            // proud of the cabinet's face. Two couches in front of it, a table between
            // the benches, lamps either side: the living end of the ship (Dan).
            float tv = halfL - 5f;
            Place(look, "TvCabinet", new Vector3(0f, 0f, tv + 0.45f), Quaternion.Euler(0f, 180f, 0f));
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
            Place(look, "Signs", new Vector3(-W(tv - 10f) + 1.0f, 3.0f, tv - 10f), Quaternion.Euler(0f, 90f, 0f));

            // ---- the working stern ------------------------------------------------

            // The crane to starboard of the well with its boom over the side (solid by
            // its own shape, so the crew walks under the boom), crates and barrels
            // astern of it, the coil of cable.
            Place(look, "Crane", new Vector3(6.5f, 0f, -6.5f), Quaternion.Euler(0f, 90f, 0f), Scale, true); // boom out over the starboard side, not over heads on the deck
            Place(look, "Crate", new Vector3(6.8f, 0f, -16.4f), Quaternion.Euler(0f, 18f, 0f));
            Place(look, "Crate", new Vector3(6.8f, 1.2f, -16.6f), Quaternion.Euler(0f, -32f, 0f));
            Place(look, "Barrel", new Vector3(8.4f, 0f, -13.2f));
            Place(look, "Barrel", new Vector3(8.8f, 0f, -14.8f));
            Place(look, "CableCoil", new Vector3(3.4f, 0f, -18.2f));
            Place(look, "Signs", new Vector3(W(-11f) - 0.9f, 3.0f, -11f), Quaternion.Euler(0f, -90f, 0f));

            // Port side, the cargo: a stack of two containers against the rail, the
            // plant and pipework, barrels, a second coil.
            Place(look, "Container", new Vector3(-6.2f, 0f, -12f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Container", new Vector3(-6.2f, 5.2f, -12f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Pipes", new Vector3(-W(-20f) + 2.2f, 0f, -20f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Barrel", new Vector3(-5.6f, 0f, -3.4f));
            Place(look, "Barrel", new Vector3(-7.0f, 0f, -4.2f));
            Place(look, "CableCoil", new Vector3(-6.0f, 0f, -20.6f));
            Place(look, "Toolbox", new Vector3(-3.4f, 0f, -18.6f), Quaternion.Euler(0f, 24f, 0f));

            // Round the well, clear of the spawn points at (+-3.5, 7) and (+-3.5, 10).
            Place(look, "Crate", new Vector3(-6.6f, 0f, 3.2f), Quaternion.Euler(0f, 8f, 0f));
            Place(look, "Barrel", new Vector3(-8.0f, 0f, 5.2f));
            Place(look, "Toolbox", new Vector3(-7.2f, 0f, 9.0f), Quaternion.Euler(0f, -30f, 0f));
            Place(look, "Pipes", new Vector3(W(4f) - 2.2f, 0f, 4f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Crate", new Vector3(7.4f, 0f, 9.6f), Quaternion.Euler(0f, -17f, 0f));

            // ---- the stern: the tower, the storage room, the working gear ---------

            // The storage room to starboard of the tower, its doorway toward the centre
            // line exactly where the game's walls and sill already are: the game's
            // sizes, and the game's colliders.
            Place(look, "StorageRoom", new Vector3(3.2f, 0f, -12.5f), Quaternion.identity, 1f, false);
            Place(look, "StorageSill", new Vector3(3.2f - 1.4f, 0f, -12.5f), Quaternion.Euler(0f, 90f, 0f), 1f, false);
            // The console on the tower's forward face: the panel the crew press to sail.
            Place(look, "Console", new Vector3(0f, 0f, towerFront + 1.0f));
            // A ladder and the plant on the tower's flanks, signs on its face and the room.
            Place(look, "Ladder", new Vector3(-towerHalf - 0.4f, 0f, towerZ), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Pipes", new Vector3(towerHalf + 0.9f, 0f, towerZ + 1.2f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Barrel", new Vector3(-towerHalf - 2.8f, 0f, -halfL + 3.2f));
            Place(look, "Barrel", new Vector3(-towerHalf - 4.2f, 0f, -halfL + 3.8f));
            Place(look, "Toolbox", new Vector3(towerHalf + 2.6f, 0f, towerZ - 2.4f), Quaternion.Euler(0f, -14f, 0f));
            Place(look, "Signs", new Vector3(0f, 4.6f, towerFront + 0.12f), Quaternion.identity);
            Place(look, "Signs", new Vector3(3.2f, 1.6f, -12.5f + 1.56f), Quaternion.identity, 1f, true);

            // The models bring no colliders of their own; every prop then gets one
            // (a box round its mesh, or the mesh itself where the shape matters), and
            // the hull's own mesh is the deck and the walls.
            foreach (Collider c in look.GetComponentsInChildren<Collider>(true))
                if (c.gameObject.name != "Bulwark") Object.DestroyImmediate(c);
            foreach ((GameObject prop, bool byMesh) in solid) Solidify(prop, byMesh);
            MeshFilter hullMesh = hull.GetComponentInChildren<MeshFilter>();
            hullMesh.gameObject.AddComponent<MeshCollider>().sharedMesh = hullMesh.sharedMesh;
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

            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float along = 0f;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 ao = loop[i], bo = loop[j];
                Vector3 ai = ao + inward[i] * WallThickness, bi = bo + inward[j] * WallThickness;
                float len = Vector3.Distance(ao, bo);
                Vector3 up = Vector3.up * Bulwark;
                int v = verts.Count;
                // Outer face, inner face, top: four corners each, UVs in metres.
                verts.AddRange(new[] { ao, ao + up, bo + up, bo, ai, ai + up, bi + up, bi, ao + up, ai + up, bi + up, bo + up });
                uvs.AddRange(new[] {
                    new Vector2(along, 0f), new Vector2(along, Bulwark), new Vector2(along + len, Bulwark), new Vector2(along + len, 0f),
                    new Vector2(along, 0f), new Vector2(along, Bulwark), new Vector2(along + len, Bulwark), new Vector2(along + len, 0f),
                    new Vector2(along, 0f), new Vector2(along, WallThickness), new Vector2(along + len, WallThickness), new Vector2(along + len, 0f) });
                tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });                 // outer: faces away from the ship
                tris.AddRange(new[] { v + 4, v + 5, v + 6, v + 4, v + 6, v + 7 });         // inner: faces the deck
                tris.AddRange(new[] { v + 8, v + 10, v + 9, v + 8, v + 11, v + 10 });      // top: faces up
                along += len;
            }

            // A saved asset: a mesh made on the fly is lost when the prefab is saved.
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BulwarkMeshPath);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh { name = "ShipBulwark" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(mesh, BulwarkMeshPath); else EditorUtility.SetDirty(mesh);

            GameObject wall = new("Bulwark");
            wall.transform.SetParent(parent.transform, false);
            wall.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = wall.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShipModelSetup.HullMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wall.AddComponent<MeshCollider>().sharedMesh = mesh;
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

        private static GameObject Place(GameObject parent, string part, Vector3 position) => Place(parent, part, position, Quaternion.identity, Scale, false);
        private static GameObject Place(GameObject parent, string part, Vector3 position, Quaternion rotation) => Place(parent, part, position, rotation, Scale, false);

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
