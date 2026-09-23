using System.Collections.Generic;
using System.Linq;
using SunkCost.Editor.Prototype;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Paint on the deck (ship audit SHIP-055: the deck read as a flat plaza with no
    // walkways, zones or focal points). Walkways, a pair of worn edge lines each:
    // from the spawn points to the cabin's door on the well's +z side and on to the
    // lounge at the bow; from the well aft to the console on the tower's face, with a
    // branch into the storage room's doorway in its port wall. Hazard-striped zones
    // round the well's rail and round the crane's foot. A lounge zone round its seats.
    //
    // One thin mesh, no collider, no shadow: each vertex sits 3 mm over whatever deck
    // is under it (the hull's deck, or the well's rim a centimetre up), found by a
    // ray, so nothing z-fights and nothing floats. Where props stand is read off the
    // props themselves, so the paint follows the dressing when it moves.
    //
    // Run at the end of ShipDeckDressing.Build, after the hull's collider exists.
    public static class ShipDeckMarkings
    {
        public const string Name = "Deck Markings";

        private const float Lift = 0.003f;      // over the deck: enough for reversed-Z depth, too little to see
        private const float Line = 0.08f;       // a walkway's edge line
        private const float Stripe = 0.14f;     // a hazard zone's striped border
        private const float Walk = 0.75f;       // from a walkway's centre to each edge line's centre
        private const float Step = 0.15f;       // longest piece of line between two height probes
        private const float Margin = 0.5f;      // a zone's border off the props it surrounds
        private const float Inboard = 0.55f;    // a zone's border off the hull's edge (the bulwark is 0.35 thick)

        private const string MeshFolder = ShipModelSetup.ModelRoot + "/DeckMarkings";
        private const string MeshPath = MeshFolder + "/DeckMarkings.asset";

        public static void Build(Transform shipRoot)
        {
            Transform look = shipRoot.Find(ShipDeckDressing.LookName);
            Build(shipRoot, look != null ? look : shipRoot);
        }

        // Under another parent standing at the ship's origin: a look at the paint on
        // the built ship without changing it (an unsaved stand-in).
        public static GameObject Build(Transform shipRoot, Transform parent)
        {
            Transform look = shipRoot.Find(ShipDeckDressing.LookName);
            Transform old = parent.Find(Name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();

            var paint = new Strip(shipRoot);
            var hazard = new Strip(shipRoot);
            float halfL = ShipStubBuilder.DeckLength / 2f;

            // Round the well's rail, open where the grate and the door are (+z).
            float ring = ShipStubBuilder.RingRadius + 0.35f;
            float gap = Mathf.Asin(Mathf.Min(1f, (Walk + Line) / ring)) * Mathf.Rad2Deg;
            var arc = new List<Vector2>();
            for (float a = gap; a <= 360f - gap + 0.01f; a += 2f)
                arc.Add(new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)) * ring);
            hazard.Add(arc, Stripe, false);

            // The crane's foot, a round base: a circle round what of the crane stands
            // within half a metre of the deck, kept clear of the well's ring.
            foreach (Rect foot in Footprints(shipRoot, look, "Crane", 0.5f))
            {
                float radius = Mathf.Max(foot.width, foot.height) / 2f + Margin;
                radius = Mathf.Min(radius, foot.center.magnitude - ring - Stripe - 0.1f);
                if (radius > 0.5f) hazard.Add(Circle(foot.center, radius), Stripe, true);
            }

            // Forward: the door, between the spawn points, to the lounge's seats.
            float door = ring + Stripe / 2f + 0.05f;
            List<Rect> seats = Footprints(shipRoot, look, new[] { "Couch", "Table", "Bench" }, 10f);
            float loungeAft = halfL - 6f;
            if (seats.Count > 0)
            {
                Rect lounge = Grow(seats.Aggregate(Union), Margin);
                paint.Add(Zone(lounge), Line, true);
                loungeAft = lounge.yMin;
            }
            Walkway(paint, new Vector2(0f, door), new Vector2(0f, loungeAft - Line / 2f));

            // Aft: the well to the console on the tower's face, and the storage room's
            // doorway on the way, entered from port.
            float consoleFront = -halfL + ShipStubBuilder.TowerDepth;
            foreach (Rect c in Footprints(shipRoot, look, "Console", 10f)) consoleFront = Mathf.Max(consoleFront, c.yMax);
            float storageZ = ShipStubBuilder.StorageCentreZ;
            float storageDoorX = ShipStubBuilder.StorageCentreX - ShipStubBuilder.StorageWidth / 2f;
            float spineEnd = consoleFront + 0.35f;
            // The spine's starboard line is broken where the branch leaves it.
            float branchLo = storageZ - Walk - Line / 2f, branchHi = storageZ + Walk + Line / 2f;
            paint.Add(new List<Vector2> { new(-Walk, -door), new(-Walk, spineEnd) }, Line, false);
            paint.Add(new List<Vector2> { new(Walk, -door), new(Walk, branchHi) }, Line, false);
            paint.Add(new List<Vector2> { new(Walk, branchLo), new(Walk, spineEnd) }, Line, false);
            foreach (float z in new[] { storageZ + Walk, storageZ - Walk })
                paint.Add(new List<Vector2> { new(Walk - Line / 2f, z), new(storageDoorX - 0.1f, z) }, Line, false);

            UnityEngine.Mesh mesh = Save(paint, hazard);
            GameObject marks = new(Name);
            marks.transform.SetParent(parent, false); // Look stands at the ship's origin: the vertices are in ship space
            marks.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = marks.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { PaintMaterial("DeckPaint"), PaintMaterial("DeckPaintHazard") };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            WarnWhereBlocked(shipRoot, marks.transform);
            return marks;
        }

        // Two edge lines either side of a straight walk.
        private static void Walkway(Strip strip, Vector2 from, Vector2 to)
        {
            Vector2 d = (to - from).normalized, side = new(d.y, -d.x);
            foreach (float s in new[] { -Walk, Walk })
                strip.Add(new List<Vector2> { from + side * s, to + side * s }, Line, false);
        }

        // A rectangle as a closed outline that keeps inboard of the hull where the
        // hull narrows (the bow), read at every half metre of its length.
        private static List<Vector2> Zone(Rect r)
        {
            float Right(float z) => r.xMax > 0 ? Mathf.Min(r.xMax, Edge(z) - Inboard) : r.xMax;
            float Left(float z) => r.xMin < 0 ? Mathf.Max(r.xMin, -(Edge(z) - Inboard)) : r.xMin;
            var pts = new List<Vector2>();
            int n = Mathf.Max(1, Mathf.CeilToInt(r.height / 0.5f));
            for (int i = 0; i <= n; i++) { float z = Mathf.Lerp(r.yMin, r.yMax, i / (float)n); pts.Add(new Vector2(Right(z), z)); }
            for (int i = n; i >= 0; i--) { float z = Mathf.Lerp(r.yMin, r.yMax, i / (float)n); pts.Add(new Vector2(Left(z), z)); }
            return pts;
        }

        // A circle as a closed outline, flattened against the hull's side where it
        // would reach past it.
        private static List<Vector2> Circle(Vector2 centre, float radius)
        {
            var pts = new List<Vector2>();
            for (int a = 0; a < 360; a += 5)
            {
                Vector2 p = centre + new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)) * radius;
                float edge = Edge(p.y) - Inboard;
                pts.Add(new Vector2(Mathf.Clamp(p.x, -edge, edge), p.y));
            }
            return pts;
        }

        // The hull's half-width here: the dressing's outline, read off the hull in the
        // same build; outside one (a look at a built ship) the deck's nominal width.
        private static float Edge(float z)
        {
            float w = ShipDeckDressing.W(z);
            return w > 1f ? w : ShipStubBuilder.DeckWidth / 2f - 0.5f;
        }

        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        private static List<Rect> Footprints(Transform shipRoot, Transform look, string part, float below) => Footprints(shipRoot, look, new[] { part }, below);

        // Each placed prop's footprint on the deck in ship space (x, z): the extent of
        // its mesh's vertices lower than `below` over the deck.
        private static List<Rect> Footprints(Transform shipRoot, Transform look, string[] parts, float below)
        {
            var found = new List<Rect>();
            if (look == null) return found;
            foreach (Transform prop in look.Cast<Transform>().Where(t => parts.Contains(t.name)))
            {
                bool any = false;
                Vector2 lo = new(float.PositiveInfinity, float.PositiveInfinity), hi = new(float.NegativeInfinity, float.NegativeInfinity);
                foreach (MeshFilter mf in prop.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue; // the editor reads a mesh's vertices readable or not
                    Matrix4x4 m = shipRoot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        Vector3 p = m.MultiplyPoint3x4(v);
                        if (p.y > below) continue;
                        lo = Vector2.Min(lo, new Vector2(p.x, p.z)); hi = Vector2.Max(hi, new Vector2(p.x, p.z)); any = true;
                    }
                }
                if (any) found.Add(Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y));
            }
            return found;
        }

        // A prop standing on the paint is named, not moved: the dressing owns where
        // props go. (Zone borders run round props by construction.)
        private static void WarnWhereBlocked(Transform shipRoot, Transform marks)
        {
            var mesh = marks.GetComponent<MeshFilter>().sharedMesh;
            var names = new Dictionary<string, Vector3>();
            Vector3[] v = mesh.vertices;
            for (int i = 0; i < v.Length; i += 4)
            {
                Vector3 p = marks.TransformPoint(v[i]) + shipRoot.up * 0.06f; // what stands on the deck, not what hangs over it
                foreach (Collider c in Physics.OverlapSphere(p, 0.03f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!c.transform.IsChildOf(shipRoot) || c.transform.IsChildOf(marks) || c.name.StartsWith("Bulwark")) continue;
                    string name = c.transform.parent != null && c.transform.parent != shipRoot ? c.transform.parent.name : c.name;
                    if (!names.ContainsKey(name)) names[name] = marks.InverseTransformPoint(p - shipRoot.up * 0.06f);
                }
            }
            if (names.Count > 0) Debug.LogWarning("Deck markings: paint runs under " + string.Join(", ", names.Select(kv => kv.Key + " at " + kv.Value.ToString("F2"))));
        }

        private static UnityEngine.Mesh Save(Strip paint, Strip hazard)
        {
            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder(ShipModelSetup.ModelRoot, "DeckMarkings");
            UnityEngine.Mesh mesh = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(MeshPath);
            bool fresh = mesh == null;
            if (fresh) mesh = new UnityEngine.Mesh { name = "DeckMarkings" };
            mesh.Clear();
            var verts = new List<Vector3>(paint.Verts); verts.AddRange(hazard.Verts);
            var uvs = new List<Vector2>(paint.Uvs); uvs.AddRange(hazard.Uvs);
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, verts.Count).ToList());
            mesh.subMeshCount = 2;
            mesh.SetTriangles(paint.Tris, 0);
            mesh.SetTriangles(hazard.Tris.Select(t => t + paint.Verts.Count).ToList(), 1);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(mesh, MeshPath); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // Worn paint: URP Lit, opaque, the chipped paint clipped by its alpha.
        private static Material PaintMaterial(string name)
        {
            string path = ShipModelSetup.TextureFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", ShipModelSetup.Import(ShipModelSetup.TextureFolder + "/" + name + ".png", TextureImporterType.Default, true, alpha: true));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.12f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_NORMALMAP");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(material);
            return material;
        }

        // Lines of paint as flat strips in ship space, mitred at their corners, cut
        // into Step pieces, each vertex set on the deck below it. u runs along the
        // line in metres (the textures hold one metre), v across it.
        private sealed class Strip
        {
            public readonly List<Vector3> Verts = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int> Tris = new();
            private readonly Transform root;

            public Strip(Transform root) { this.root = root; }

            public void Add(List<Vector2> pts, float width, bool closed)
            {
                var dense = new List<Vector2>();
                var corner = new List<bool>();
                int segs = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < segs; i++)
                {
                    Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / Step));
                    for (int k = 0; k < n; k++) { dense.Add(Vector2.Lerp(a, b, k / (float)n)); corner.Add(k == 0); }
                }
                if (!closed) { dense.Add(pts[^1]); corner.Add(true); }
                else { dense.Add(dense[0]); corner.Add(true); }
                // Drop repeated points (a zone clamped flat to the hull gives some).
                for (int i = dense.Count - 1; i > 0; i--)
                    if ((dense[i] - dense[i - 1]).sqrMagnitude < 1e-6f) { dense.RemoveAt(i); corner.RemoveAt(i); }
                if (dense.Count < 2) return;

                int count = dense.Count, start = Verts.Count;
                float along = 0f;
                for (int i = 0; i < count; i++)
                {
                    Vector2 prev = i > 0 ? dense[i - 1] : (closed ? dense[count - 2] : dense[i]);
                    Vector2 next = i < count - 1 ? dense[i + 1] : (closed ? dense[1] : dense[i]);
                    Vector2 din = (dense[i] - prev).normalized, dout = (next - dense[i]).normalized;
                    if (din == Vector2.zero) din = dout;
                    if (dout == Vector2.zero) dout = din;
                    Vector2 nin = new(-din.y, din.x), nout = new(-dout.y, dout.x);
                    Vector2 miter = (nin + nout).normalized;
                    float scale = 1f / Mathf.Max(0.35f, Vector2.Dot(miter, nin));
                    if (i > 0) along += Vector2.Distance(dense[i - 1], dense[i]);
                    foreach (float s in new[] { -0.5f, 0.5f })
                    {
                        Vector2 p = dense[i] + miter * (s * width * scale);
                        Verts.Add(new Vector3(p.x, DeckHeight(p) + Lift, p.y));
                        Uvs.Add(new Vector2(along, s + 0.5f));
                    }
                }
                for (int i = 0; i < count - 1; i++)
                {
                    int a = start + i * 2;
                    Tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                }
                // Facing up whichever way the line ran.
                for (int t = Tris.Count - (count - 1) * 6; t < Tris.Count; t += 3)
                {
                    Vector3 n = Vector3.Cross(Verts[Tris[t + 1]] - Verts[Tris[t]], Verts[Tris[t + 2]] - Verts[Tris[t]]);
                    if (n.y < 0f) (Tris[t + 1], Tris[t + 2]) = (Tris[t + 2], Tris[t + 1]);
                }
            }

            // The deck's height here in ship space: the highest surface within a few
            // centimetres of zero (the hull's deck, the well's rim), not a prop.
            private float DeckHeight(Vector2 p)
            {
                Vector3 from = root.TransformPoint(new Vector3(p.x, 0.5f, p.y));
                float best = float.NegativeInfinity;
                foreach (RaycastHit h in Physics.RaycastAll(from, -root.up, 1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!h.collider.transform.IsChildOf(root)) continue;
                    float y = root.InverseTransformPoint(h.point).y;
                    if (y > -0.05f && y < 0.04f && y > best) best = y;
                }
                return float.IsNegativeInfinity(best) ? 0f : best;
            }
        }
    }
}
