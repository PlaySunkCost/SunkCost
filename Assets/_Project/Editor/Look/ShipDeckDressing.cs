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
    // on and its bulwark, 66 cm over the deck against a 65 cm jump, is the wall that
    // keeps them aboard — the hull's own mesh is the collider, cut to the hull's own
    // outline (Dan: "not real walls - the ship itself"). Everything along the edge
    // (railings, lamps, bollards, the name) follows that outline, read off the mesh.
    //
    // The lounge is the bow: the deck TV faces aft from the point, the couches in
    // front of it. The working end is the stern: the crane, the cargo, the storage
    // room, beside the tower. The deck grew from 44 x 16 m to 48 x 20 m so the
    // crew has room to live on it (Dan: "more space for the furniture").
    //
    // Run from the ship stub builder, after the prefab's structure exists.
    public static class ShipDeckDressing
    {
        public const string LookName = "Look";

        // The bulwark's height over the deck: the jump (0.65) plus a centimetre, set
        // in prepare_ship_part.py (HULL_BULWARK) where the hull is cut to it.
        public const float Bulwark = 0.66f;

        // What loses its renderer and keeps its collider: the shapes a model now covers.
        private static readonly string[] HiddenPrefixes =
        {
            "Hull", "Tower", "Bridge Windows", "Bridge Label", "Bridge Door", "Bridge Sign",
            "Storage", "TvPost", "TvFrame", "Monitor Frame", "Monitor Console",
            "Fender", "Tyre",
            "Roof Rail", "Roof Lamp", "Roof Gap", "Radar", "Antenna", "Funnel", "Beacon",
            "Crew Screen Frame",
        };

        // What the models never replace, whatever its name begins with: the glass
        // cabin, the monitor and its buttons, the screens the game writes on, and
        // the roof gate the rig's bridge meets.
        private static readonly string[] KeptPrefixes =
        {
            "DeckCabin", "Monitor", "TvScreen", "Tv Caption", "Roof Gate", "Crew Screen Text",
            "Storage Sign", "StorageReadout", // the room's readout: the game writes on it
        };

        private static float halfL, halfW;

        public static void Build(Transform root)
        {
            // The old dressing goes entirely; the models replace it.
            Transform old = root.Find(LookName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null || !HiddenPrefixes.Any(p => t.name.StartsWith(p))) continue;
                if (KeptPrefixes.Any(p => t.name.StartsWith(p))) continue;
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

            // The hull's deck is at its origin (prepare_ship_part.finish_hull): the
            // ship's origin is the deck, so the hull stands at zero.
            GameObject hull = Place(look, "Hull", Vector3.zero);
            SampleOutline(root, hull);
            Place(look, "Tower", new Vector3(0f, 0f, towerZ));

            // The walkable rim between the hull's deck and the round hole wears the
            // deck's plate, like the hull's deck faces do (its UVs are in metres too).
            Material deck = ShipModelSetup.DeckMaterial();
            foreach (string rim in new[] { "Well Rim", "Well Rim Under" })
            {
                Transform t = root.Find(rim);
                if (t != null && t.TryGetComponent(out MeshRenderer renderer)) renderer.sharedMaterial = deck;
            }

            // The name on the bow, both sides, where the hull is still wide.
            foreach (float side in new[] { -1f, 1f })
                Place(look, "NamePlate", new Vector3(side * (W(14f) + 0.08f), -1.4f, 14f), Quaternion.Euler(0f, side * 90f, 0f));

            // ---- along the edge, following the hull ------------------------------

            // Railings on the bulwark's crown every 2 m, turned to the edge's own
            // direction; the bow tapers, so the last ones lean in with it, and one
            // crosses the point. A lamp every 8 m, a bollard every 9, four lifebuoys.
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z = -halfL + 1f; z < halfL - 1f; z += 2f)
                    PlaceOnEdge(look, "Railing", side, z, 0.15f, Bulwark);
                for (float z = -halfL + 4f; z < halfL - 3f; z += 8f)
                    Place(look, "DeckLamp", new Vector3(side * (W(z) - 0.9f), 0f, z + (side > 0f ? 4f : 0f)));
                for (float z = -halfL + 6f; z < halfL - 4f; z += 9f)
                    Place(look, "Bollard", new Vector3(side * (W(z) - 1.1f), 0f, z));
                foreach (float z in new[] { -9f, 9f })
                    Place(look, "Lifebuoy", new Vector3(side * (W(z) - 0.55f), 0f, z), Quaternion.Euler(0f, side > 0f ? -90f : 90f, 0f));
            }
            Place(look, "Railing", new Vector3(0f, Bulwark, halfL - 0.35f));
            // The stern rail either side of the tower.
            foreach (float side in new[] { -1f, 1f })
                for (float x = towerHalf + 1f; x < W(-halfL + 0.5f) - 0.5f; x += 2f)
                    Place(look, "Railing", new Vector3(side * x, Bulwark, -halfL + 0.35f));

            // ---- the elevator ----------------------------------------------------

            // The glass housing round the well, its doorway to the bow like the cabin's.
            Place(look, "CabinHousing", Vector3.zero);
            foreach (float a in new[] { 40f, 140f, 220f, 320f })
                Place(look, "Bollard", new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * (well + 1.4f), 0f, Mathf.Cos(a * Mathf.Deg2Rad) * (well + 1.4f)));
            // The winch that runs the car, beside the well to starboard, its cable coiled.
            Place(look, "Winch", new Vector3(well + 2.2f, 0f, -2.4f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "CableCoil", new Vector3(well + 2.4f, 0f, -4.2f));
            Place(look, "Toolbox", new Vector3(well + 1.6f, 0f, -5.4f), Quaternion.Euler(0f, -14f, 0f));

            // ---- the lounge: the bow ---------------------------------------------

            // The deck TV the game builds stands at the point facing aft
            // (ShipStubBuilder.BuildTv); the cabinet is its housing, the screen just
            // proud of the cabinet's face. Two couches in front of it, a table between
            // the benches, lamps either side: the living end of the ship (Dan).
            float tv = halfL - 2.5f;
            Place(look, "TvCabinet", new Vector3(0f, 0f, tv + 0.25f), Quaternion.Euler(0f, 180f, 0f));
            foreach (float x in new[] { -1.15f, 1.15f })
                Place(look, "Couch", new Vector3(x, 0f, tv - 3f));
            Place(look, "Table", new Vector3(0f, 0f, tv - 5.8f));
            Place(look, "Bench", new Vector3(-3.4f, 0f, tv - 4.4f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Bench", new Vector3(3.4f, 0f, tv - 4.4f), Quaternion.Euler(0f, -90f, 0f));
            foreach (float side in new[] { -1f, 1f })
                Place(look, "DeckLamp", new Vector3(side * 4.2f, 0f, tv - 1f));
            Place(look, "Crate", new Vector3(-4.6f, 0f, tv - 7.5f), Quaternion.Euler(0f, 22f, 0f));
            Place(look, "Barrel", new Vector3(4.8f, 0f, tv - 7.2f));
            Place(look, "Barrel", new Vector3(5.4f, 0f, tv - 7.8f));
            Place(look, "Signs", new Vector3(-W(tv - 9f) + 0.5f, 1.5f, tv - 9f), Quaternion.Euler(0f, 90f, 0f));

            // ---- the working stern ------------------------------------------------

            // The crane to starboard of the well with its boom over the side, the
            // containers stacked behind it beside the storage room, crates and
            // barrels where the crew left them, the coil of cable.
            Place(look, "Crane", new Vector3(6.6f, 0f, -6.5f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Container", new Vector3(6.4f, 0f, -13f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Container", new Vector3(6.4f, 2.6f, -13f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Crate", new Vector3(5.6f, 0f, -9.6f), Quaternion.Euler(0f, 18f, 0f));
            Place(look, "Crate", new Vector3(5.6f, 0.6f, -9.7f), Quaternion.Euler(0f, -32f, 0f));
            Place(look, "Crate", new Vector3(4.4f, 0f, -9.3f), Quaternion.Euler(0f, 61f, 0f));
            Place(look, "Barrel", new Vector3(7.4f, 0f, -9.6f));
            Place(look, "Barrel", new Vector3(8.0f, 0f, -10.2f));
            Place(look, "Barrel", new Vector3(7.7f, 0f, -16.8f));
            Place(look, "CableCoil", new Vector3(3.6f, 0f, -16.6f));
            Place(look, "Signs", new Vector3(W(-11f) - 0.45f, 1.5f, -11f), Quaternion.Euler(0f, -90f, 0f));

            // Port side, the other half of the cargo: a container against the rail,
            // the plant and pipework, more barrels, a second coil.
            Place(look, "Container", new Vector3(-6.4f, 0f, -6f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Container", new Vector3(-6.4f, 2.6f, -6f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Pipes", new Vector3(-W(-12f) + 1.1f, 0f, -12f), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Barrel", new Vector3(-5.2f, 0f, -11.2f));
            Place(look, "Barrel", new Vector3(-5.9f, 0f, -11.7f));
            Place(look, "Barrel", new Vector3(-5.5f, 0f, -12.4f));
            Place(look, "Crate", new Vector3(-6.6f, 0f, -14.8f), Quaternion.Euler(0f, -12f, 0f));
            Place(look, "Crate", new Vector3(-6.6f, 0.6f, -14.9f), Quaternion.Euler(0f, 40f, 0f));
            Place(look, "CableCoil", new Vector3(-4.6f, 0f, -16.4f));
            Place(look, "Toolbox", new Vector3(-3.2f, 0f, -9.4f), Quaternion.Euler(0f, 24f, 0f));

            // Round the well, so the middle of the deck is not bare.
            Place(look, "Crate", new Vector3(-6.2f, 0f, 3.4f), Quaternion.Euler(0f, 8f, 0f));
            Place(look, "Barrel", new Vector3(-7.0f, 0f, 4.4f));
            Place(look, "Toolbox", new Vector3(-5.4f, 0f, 5.6f), Quaternion.Euler(0f, -30f, 0f));
            Place(look, "Pipes", new Vector3(W(4f) - 1.1f, 0f, 4f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Crate", new Vector3(6.4f, 0f, 8.4f), Quaternion.Euler(0f, -17f, 0f));

            // ---- the stern: the tower, the storage room, the working gear ---------

            // The storage room to starboard of the tower, its doorway toward the centre
            // line exactly where the game's walls and sill already are.
            Place(look, "StorageRoom", new Vector3(3.2f, 0f, -12.5f));
            Place(look, "StorageSill", new Vector3(3.2f - 1.4f, 0f, -12.5f), Quaternion.Euler(0f, 90f, 0f));
            // The console on the tower's forward face: the panel the crew press to sail.
            Place(look, "Console", new Vector3(0f, 0f, towerFront + 0.5f));
            // A ladder and the plant on the tower's flanks, signs on its face and the room.
            Place(look, "Ladder", new Vector3(-towerHalf - 0.2f, 0f, towerZ), Quaternion.Euler(0f, 90f, 0f));
            Place(look, "Pipes", new Vector3(towerHalf + 0.45f, 0f, towerZ + 1.2f), Quaternion.Euler(0f, -90f, 0f));
            Place(look, "Barrel", new Vector3(-towerHalf - 1.6f, 0f, -halfL + 2.2f));
            Place(look, "Barrel", new Vector3(-towerHalf - 2.3f, 0f, -halfL + 2.6f));
            Place(look, "Toolbox", new Vector3(towerHalf + 1.4f, 0f, towerZ - 1.8f), Quaternion.Euler(0f, -14f, 0f));
            Place(look, "Signs", new Vector3(-2.4f, 1.6f, towerFront + 0.06f), Quaternion.identity);
            Place(look, "Signs", new Vector3(3.2f, 1.7f, -12.5f + 1.56f), Quaternion.identity);

            // Everything dressed here is look only, except the hull: no colliders come
            // with the models, and the hull's own mesh is the deck and the walls.
            foreach (Collider c in look.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            MeshFilter hullMesh = hull.GetComponentInChildren<MeshFilter>();
            hullMesh.gameObject.AddComponent<MeshCollider>().sharedMesh = hullMesh.sharedMesh;
        }

        // ---- the hull's outline -----------------------------------------------------

        private const float Step = 0.5f;
        private static float[] outline; // the half-width at deck height, every Step of z from -halfL

        // Read off the hull mesh: for each slice of length, the widest point between
        // the deck and the bulwark's top. Gaps filled from their neighbours, then
        // smoothed, so the generation's lumps do not kink the railings.
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

        // A part on the edge at this length, this far in from the outline, turned to
        // the edge's own direction there (the part's long axis is its x).
        private static void PlaceOnEdge(GameObject parent, string part, float side, float z, float inset, float y)
        {
            float w = W(z) - inset;
            Vector3 along = new(side * (W(z + 1f) - W(z - 1f)), 0f, 2f);
            Quaternion facing = Quaternion.LookRotation(along.normalized) * Quaternion.Euler(0f, -90f, 0f);
            Place(parent, part, new Vector3(side * w, y, z), facing);
        }

        private static readonly Dictionary<string, GameObject> Cache = new();

        private static GameObject Place(GameObject parent, string part, Vector3 position) => Place(parent, part, position, Quaternion.identity);

        private static GameObject Place(GameObject parent, string part, Vector3 position, Quaternion rotation)
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
            instance.name = part;
            return instance;
        }
    }
}
