using System.Collections.Generic;
using System.Text;
using SunkCost.Diving;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Sites
{
    // Does the moving car ever pass through the shaft's models? (BRIEF: "no clipping of the
    // car through the collar, the tube or the foot at any point of the ride".) The car only
    // moves straight up and down its axis, so its swept volume is its radial profile (its
    // furthest drawn point from the axis per height slice, over every bearing: conservative)
    // slid from the bottom stop to the top stop. Every drawn point of the collar, the
    // sections, the foot and the gate leaves (vertices, plus points over big triangles)
    // is checked against that profile over the whole ride; the result is the smallest
    // radial clearance per part (negative = the car cuts it) and where it happens.
    // Sweep() reads the open DiveSite01 (DiveSiteValidator runs it); At(rootY) checks one
    // car height (a Play Mode ride samples it at the stops and the surface).
    public static class DiveElevatorClearance
    {
        private const float Slice = 0.05f;   // metres of car height per profile slice
        private const float Spacing = 0.05f; // sample spacing over big triangles

        public static readonly string[] Parts = { ShaftTubeLook.CollarName, ShaftTubeLook.SectionsName, ShaftTubeLook.FootName, DiveSiteBuilder.TubeGateName };

        public struct Report
        {
            public float Worst;          // smallest clearance over every part
            public string Text;
        }

        public static Report Sweep() => Measure(null);

        public static Report At(float carRootY) => Measure(carRootY);

        private static Report Measure(float? atRootY)
        {
            ElevatorController car = Object.FindAnyObjectByType<ElevatorController>();
            if (car == null) return new Report { Worst = float.NaN, Text = "no car" };
            Scene scene = car.gameObject.scene;
            Transform tube = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == DiveSiteBuilder.ShaftTubeName) tube = root.transform;
            if (tube == null) return new Report { Worst = float.NaN, Text = "no shaft tube" };

            // The car's profile in its own frame (root at y 0, axis at the origin); the water
            // inside it is not a hull.
            Transform carRoot = car.transform;
            var profile = new Dictionary<int, float>();
            Matrix4x4 toCar = carRoot.worldToLocalMatrix;
            foreach (MeshFilter filter in carRoot.GetComponentsInChildren<MeshFilter>(false))
            {
                if (IsWater(filter.transform, carRoot) || !Drawn(filter)) continue;
                Matrix4x4 m = toCar * filter.transform.localToWorldMatrix;
                foreach (Vector3 p in Samples(filter.sharedMesh, m))
                {
                    int slice = Mathf.FloorToInt(p.y / Slice);
                    float r = new Vector2(p.x, p.z).magnitude;
                    if (!profile.TryGetValue(slice, out float have) || r > have) profile[slice] = r;
                }
            }

            float top = car.TopPosition.y, bottom = car.BottomPosition.y;
            float lo = atRootY ?? bottom, hi = atRootY ?? top;
            Vector3 axis = new(car.TopPosition.x, 0f, car.TopPosition.z);
            var text = new StringBuilder();
            float worst = float.PositiveInfinity;
            foreach (string part in Parts)
            {
                Transform t = tube.Find(part);
                if (t == null) { text.Append(part).Append(": missing; "); worst = float.NegativeInfinity; continue; }
                float partWorst = float.PositiveInfinity; Vector3 where = Vector3.zero; float whereCar = 0f; int points = 0;
                foreach (MeshFilter filter in t.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (!Drawn(filter)) continue;
                    foreach (Vector3 p in Samples(filter.sharedMesh, filter.transform.localToWorldMatrix))
                    {
                        points++;
                        float r = new Vector2(p.x - axis.x, p.z - axis.z).magnitude;
                        foreach (KeyValuePair<int, float> s in profile)
                        {
                            // The car roots at which this point is level with the slice.
                            float rootHi = p.y - s.Key * Slice, rootLo = rootHi - Slice;
                            if (rootHi < lo - 0.001f || rootLo > hi + 0.001f) continue;
                            float c = r - s.Value;
                            if (c < partWorst) { partWorst = c; where = p; whereCar = Mathf.Clamp(rootLo, lo, hi); }
                        }
                    }
                }
                text.Append(part).Append(": ");
                if (float.IsPositiveInfinity(partWorst)) text.Append("never level with the car (" + points + " points); ");
                else text.Append($"{partWorst:+0.000;-0.000} m at ({where.x:0.00}, {where.y:0.00}, {where.z:0.00}) car root {whereCar:0.00} ({points} points); ");
                worst = Mathf.Min(worst, partWorst);
            }
            string range = atRootY.HasValue ? $"car root {atRootY.Value:0.00}" : $"whole ride {top:0.00} to {bottom:0.00}";
            return new Report { Worst = worst, Text = "clearance over " + range + ": " + text.ToString().TrimEnd(' ', ';') };
        }

        // Every car collider against every other solid collider, the car moved to each
        // height (edit mode only; the car is put back). Returns the deepest overlap.
        public static string ColliderSweep(float[] rootYs, out float deepest)
        {
            deepest = 0f;
            ElevatorController car = Object.FindAnyObjectByType<ElevatorController>();
            if (car == null || Application.isPlaying) return "no car, or Play Mode";
            Transform carRoot = car.transform;
            Vector3 start = carRoot.position;
            var text = new StringBuilder();
            try
            {
                foreach (float y in rootYs)
                {
                    carRoot.position = new Vector3(start.x, y, start.z);
                    Physics.SyncTransforms();
                    foreach (Collider mine in carRoot.GetComponentsInChildren<Collider>(false))
                    {
                        if (mine.isTrigger || !mine.enabled) continue;
                        Bounds b = mine.bounds;
                        foreach (Collider other in Physics.OverlapBox(b.center, b.extents + Vector3.one * 0.01f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (other.transform.IsChildOf(carRoot)) continue;
                            if (!(mine is MeshCollider mm && !mm.convex) || other is not MeshCollider { convex: false })
                            {
                                if (Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > 0.005f)
                                {
                                    if (depth > deepest) deepest = depth;
                                    text.Append($"root {y:0.00}: {mine.name} x {other.name} {depth:0.000}; ");
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                carRoot.position = start;
                Physics.SyncTransforms();
            }
            return text.Length == 0 ? "no car collider overlaps another collider at " + rootYs.Length + " heights" : text.ToString();
        }

        private static bool IsWater(Transform t, Transform carRoot)
        {
            for (Transform x = t; x != null && x != carRoot; x = x.parent)
                if (x.name == SunkCost.Editor.Prototype.ShaftTubeSetup.CabinWaterSurfaceName || x.name == SunkCost.Editor.Prototype.ShaftTubeSetup.CabinWaterFxName) return true;
            return false;
        }

        private static bool Drawn(MeshFilter filter)
        {
            Renderer r = filter.GetComponent<Renderer>();
            return filter.sharedMesh != null && r != null && r.enabled;
        }

        // The mesh's vertices, plus points spread over every triangle longer than the spacing.
        private static IEnumerable<Vector3> Samples(Mesh mesh, Matrix4x4 m)
        {
            Vector3[] v = mesh.vertices;
            var world = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) { world[i] = m.MultiplyPoint3x4(v[i]); yield return world[i]; }
            int[] tris = mesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = world[tris[i]], b = world[tris[i + 1]], c = world[tris[i + 2]];
                float longest = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
                int n = Mathf.CeilToInt(longest / Spacing);
                if (n <= 1) continue;
                for (int u = 0; u <= n; u++)
                    for (int w = 0; w <= n - u; w++)
                    {
                        if ((u == 0 && w == 0) || (u == n) || (w == n)) continue; // corners are vertices
                        float fu = (float)u / n, fw = (float)w / n;
                        yield return a + (b - a) * fu + (c - a) * fw;
                    }
            }
        }
    }
}
