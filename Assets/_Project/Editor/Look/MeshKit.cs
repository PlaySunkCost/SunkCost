using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Meshes for the props, with UVs in metres: a 4 m box shows four tiles of a
    // 1 m texture whatever its transform, so one shared material dresses every
    // size (Unity's primitives stretch one tile over any face). Saved as assets
    // under Assets/_Project/Art/HQ/Meshes, one per shape and size, so prefabs can
    // reference them; the same request returns the same asset.
    public static class MeshKit
    {
        public const string Folder = "Assets/_Project/Art/HQ/Meshes";

        // A box of `size`, pivot at the base centre (y from 0 to size.y).
        public static Mesh Box(Vector3 size) => Box(size, 0f);

        // A box of `size` with its pivot `pivotY` above the base (0 = base, size.y/2 = centre).
        public static Mesh Box(Vector3 size, float pivotY)
        {
            string name = $"Box_{F(size.x)}x{F(size.y)}x{F(size.z)}_p{F(pivotY)}";
            return Cached(name, () => BuildBox(size, pivotY));
        }

        // A cylinder of `radius` and `height` with `sides` faces, pivot at the base centre.
        public static Mesh Cylinder(float radius, float height, int sides = 16, bool capped = true)
        {
            string name = $"Cyl_{F(radius)}x{F(height)}_{sides}{(capped ? "" : "_open")}";
            return Cached(name, () => BuildCylinder(radius, height, sides, capped));
        }

        // A flat plane in the XZ plane, `w` by `d`, pivot at the centre, facing up,
        // subdivided `nx` by `nz` (the wave surface moves its vertices).
        public static Mesh Plane(float w, float d, int nx, int nz)
        {
            string name = $"Plane_{F(w)}x{F(d)}_{nx}x{nz}";
            return Cached(name, () => BuildPlane(w, d, nx, nz));
        }

        // A ring (a torus) of `radius` and tube `thickness`, in the XZ plane, pivot at the centre.
        public static Mesh Ring(float radius, float thickness, int segments = 24, int sides = 8)
        {
            string name = $"Ring_{F(radius)}x{F(thickness)}";
            return Cached(name, () => BuildArc(radius, thickness, 0f, 360f, segments, sides, caps: false));
        }

        // A piece of a ring: a round rail from `fromDeg` to `toDeg` about Y, capped ends.
        public static Mesh Arc(float radius, float thickness, float fromDeg, float toDeg, int segments = 32, int sides = 8)
        {
            string name = $"Arc_{F(radius)}x{F(thickness)}_{F(fromDeg)}to{F(toDeg)}";
            return Cached(name, () => BuildArc(radius, thickness, fromDeg, toDeg, segments, sides, caps: true));
        }

        // A curved plate `height` tall and `thick` deep, standing on its base at
        // `radius` from `fromDeg` to `toDeg` about Y; UVs in metres (u along the
        // curve, v up), both faces, the top, the ends.
        public static Mesh Band(float radius, float height, float thick, float fromDeg, float toDeg, int segments = 32)
        {
            string name = $"Band_{F(radius)}x{F(height)}x{F(thick)}_{F(fromDeg)}to{F(toDeg)}";
            return Cached(name, () => BuildBand(radius, height, thick, fromDeg, toDeg, segments));
        }

        // A flat square of half-width `half` with a round hole of `inner` radius, its
        // normal up, UVs in metres: the deck's rim round the ship's elevator well.
        public static Mesh SquareAnnulus(float inner, float half, int segments = 40)
        {
            string name = $"SqAnnulus_{F(inner)}x{F(half)}";
            return Cached(name, () =>
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float reach = half / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.z));
                    Vector3 pin = dir * inner, pout = dir * reach;
                    v.Add(pin); uv.Add(new Vector2(pin.x, pin.z));
                    v.Add(pout); uv.Add(new Vector2(pout.x, pout.z));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = i * 2, b = i * 2 + 2;
                    t.Add(a); t.Add(b); t.Add(b + 1); // clockwise seen from above: the face looks up
                    t.Add(a); t.Add(b + 1); t.Add(a + 1);
                }
                Mesh m = new();
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            });
        }

        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_');

        private static Mesh Cached(string name, System.Func<Mesh> build)
        {
            System.IO.Directory.CreateDirectory(Folder);
            string path = Folder + "/" + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            Mesh mesh = build();
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh BuildBox(Vector3 size, float pivotY)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 h = size * 0.5f;
            Vector3 c = new Vector3(0f, h.y - pivotY, 0f);
            // Each face: four corners, UV = the face's metre extents.
            void Face(Vector3 normal, Vector3 right, Vector3 up, float w, float hgt)
            {
                int i = v.Count;
                Vector3 centre = c + Vector3.Scale(normal, h);
                Vector3 r = right * (w * 0.5f), u = up * (hgt * 0.5f);
                v.Add(centre - r - u); v.Add(centre + r - u); v.Add(centre + r + u); v.Add(centre - r + u);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(w, 0f)); uv.Add(new Vector2(w, hgt)); uv.Add(new Vector2(0f, hgt));
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            return Finish(v, n, uv, t);
        }

        private static Mesh BuildCylinder(float radius, float height, int sides, bool capped)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float circumference = 2f * Mathf.PI * radius;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(dir * radius); n.Add(dir); uv.Add(new Vector2(i / (float)sides * circumference, 0f));
                v.Add(dir * radius + Vector3.up * height); n.Add(dir); uv.Add(new Vector2(i / (float)sides * circumference, height));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
            }
            if (capped)
            {
                foreach (bool top in new[] { true, false })
                {
                    int centre = v.Count;
                    Vector3 normal = top ? Vector3.up : Vector3.down;
                    float y = top ? height : 0f;
                    v.Add(new Vector3(0f, y, 0f)); n.Add(normal); uv.Add(Vector2.zero);
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = i / (float)sides * Mathf.PI * 2f;
                        Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        v.Add(dir * radius + Vector3.up * y); n.Add(normal); uv.Add(new Vector2(dir.x * radius, dir.z * radius));
                    }
                    for (int i = 0; i < sides; i++)
                    {
                        int a = centre + 1 + i, b = centre + 2 + i;
                        if (top) { t.Add(centre); t.Add(b); t.Add(a); } else { t.Add(centre); t.Add(a); t.Add(b); }
                    }
                }
            }
            return Finish(v, n, uv, t);
        }

        private static Mesh BuildPlane(float w, float d, int nx, int nz)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float px = (x / (float)nx - 0.5f) * w, pz = (z / (float)nz - 0.5f) * d;
                    v.Add(new Vector3(px, 0f, pz)); n.Add(Vector3.up); uv.Add(new Vector2(px, pz));
                }
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, dd = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(dd); // facing up (the box's top face winding)
                }
            return Finish(v, n, uv, t); // Finish picks the index format before the triangles go in: set after, it empties them
        }

        private static Mesh BuildArc(float radius, float thickness, float fromDeg, float toDeg, int segments, int sides, bool caps)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float from = fromDeg * Mathf.Deg2Rad, span = (toDeg - fromDeg) * Mathf.Deg2Rad;
            for (int i = 0; i <= segments; i++)
            {
                float a = from + i / (float)segments * span;
                Vector3 centre = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Vector3 outward = centre.normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    Vector3 normal = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(centre + normal * (thickness * 0.5f)); n.Add(normal); uv.Add(new Vector2(i / (float)segments, j / (float)sides));
                }
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            if (caps)
                foreach (int end in new[] { 0, segments })
                {
                    float a = from + end / (float)segments * span;
                    Vector3 centre = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * (end == 0 ? -1f : 1f);
                    int c = v.Count; v.Add(centre); n.Add(tangent); uv.Add(Vector2.zero);
                    int ring = end * (sides + 1);
                    for (int j = 0; j < sides; j++)
                    {
                        if (end == 0) { t.Add(c); t.Add(ring + j); t.Add(ring + j + 1); }
                        else { t.Add(c); t.Add(ring + j + 1); t.Add(ring + j); }
                    }
                }
            return Finish(v, n, uv, t);
        }

        private static Mesh BuildBand(float radius, float height, float thick, float fromDeg, float toDeg, int segments)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float from = fromDeg * Mathf.Deg2Rad, span = (toDeg - fromDeg) * Mathf.Deg2Rad;
            float ro = radius + thick / 2f, ri = radius - thick / 2f;
            // Four strips along the curve: outer face, inner face, top, bottom — each a
            // row of quads with its own normals.
            void Strip(System.Func<float, Vector3> lower, System.Func<float, Vector3> upper, System.Func<float, Vector3> normal, float vLow, float vHigh, bool flip)
            {
                int start = v.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = from + i / (float)segments * span, u = (a - from) * radius;
                    v.Add(lower(a)); n.Add(normal(a)); uv.Add(new Vector2(u, vLow));
                    v.Add(upper(a)); n.Add(normal(a)); uv.Add(new Vector2(u, vHigh));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = start + i * 2, b = a + 2;
                    if (flip) { t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1); }
                    else { t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b); }
                }
            }
            Vector3 Out(float a) => new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Strip(a => Out(a) * ro, a => Out(a) * ro + Vector3.up * height, a => Out(a), 0f, height, flip: false);
            Strip(a => Out(a) * ri, a => Out(a) * ri + Vector3.up * height, a => -Out(a), 0f, height, flip: true);
            Strip(a => Out(a) * ri + Vector3.up * height, a => Out(a) * ro + Vector3.up * height, a => Vector3.up, 0f, thick, flip: true);
            Strip(a => Out(a) * ri, a => Out(a) * ro, a => Vector3.down, 0f, thick, flip: false);
            foreach (int end in new[] { 0, 1 })
            {
                float a = from + end * span;
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * (end == 0 ? -1f : 1f);
                int s = v.Count;
                v.Add(Out(a) * ri); v.Add(Out(a) * ro); v.Add(Out(a) * ri + Vector3.up * height); v.Add(Out(a) * ro + Vector3.up * height);
                for (int k = 0; k < 4; k++) { n.Add(tangent); uv.Add(new Vector2(k % 2 * thick, k / 2 * height)); }
                if (end == 0) { t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s + 1); t.Add(s + 2); t.Add(s + 3); }
                else { t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s + 1); t.Add(s + 3); t.Add(s + 2); }
            }
            return Finish(v, n, uv, t);
        }

        private static Mesh Finish(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var mesh = new Mesh();
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // before the triangles: changing it afterwards clears them
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
