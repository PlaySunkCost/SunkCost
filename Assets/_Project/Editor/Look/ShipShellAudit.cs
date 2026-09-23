using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Where the eye can go further than the eye can see (Dan, 23 September 2026:
    // the camera inside the storage room's wall, "make sure it doesnt happen
    // anywhere"). The camera keeps clear of colliders (PlayerCameraClearance), so it
    // ends up inside anything whose visible surface stands in front of its collider.
    //
    // This stands a person at every open half-metre of the ship's deck, looks in
    // sixteen directions at eye and knee height, and compares the nearest visible
    // surface (every renderer's own mesh, as a temporary collider) with the nearest
    // real collider. A surface more than Tolerance in front of the collider, within
    // reach, is a place the camera can enter; the report names each one.
    //
    // Run with the ShipAtSea scene open: SunkCost.Editor.Look.ShipShellAudit.Run().
    public static class ShipShellAudit
    {
        private const float Tolerance = 0.08f;   // the camera's own near envelope
        private const float Reach = 1.2f;        // how close a wall has to be to matter
        private const float Radius = 0.3f;       // the player's capsule

        [MenuItem("Sunk Cost/Look/Audit the ship's shell (camera inside walls)")]
        public static void RunFromMenu() => Debug.Log(Run());

        public static string Run()
        {
            GameObject ship = GameObject.Find("Ship");
            if (ship == null) return "no Ship in the open scene";
            Transform root = ship.transform;

            // Every visible surface as a temporary collider, remembered by its source.
            var shells = new Dictionary<Collider, string>();
            var temps = new List<GameObject>();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || r.GetComponent<TextMesh>() != null) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (CoveredByOwnCollider(r)) continue; // its collider is its shape: nothing to compare, and grazing edges disagree by a hair
                var temp = new GameObject("ShellAudit");
                temp.hideFlags = HideFlags.HideAndDontSave;
                temp.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                temp.transform.localScale = r.transform.lossyScale;
                var mc = temp.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                shells[mc] = PathOf(r.transform, root);
                temps.Add(temp);
            }
            Physics.SyncTransforms();

            var worst = new Dictionary<string, (float gap, Vector3 at)>();
            int stands = 0;
            try
            {
                float halfL = Prototype.ShipStubBuilder.DeckLength / 2f, halfW = Prototype.ShipStubBuilder.DeckWidth / 2f;
                for (float z = -halfL; z <= halfL; z += 0.5f)
                    for (float x = -halfW; x <= halfW; x += 0.5f)
                    {
                        Vector3 top = root.TransformPoint(new Vector3(x, 8f, z));
                        if (!RealHit(top, Vector3.down, 10f, shells, out RaycastHit floor)) continue;
                        float floorY = root.InverseTransformPoint(floor.point).y;
                        if (floorY < -0.2f || floorY > 1.0f) continue; // the deck and what stands a step off it
                        Vector3 feet = floor.point;
                        if (Blocked(feet + Vector3.up * (Radius + 0.05f), feet + Vector3.up * 1.6f, shells)) continue;
                        stands++;
                        foreach (float h in new[] { 0.6f, 1.6f })
                            for (int k = 0; k < 16; k++)
                            {
                                Vector3 eye = feet + Vector3.up * h;
                                Vector3 dir = Quaternion.AngleAxis(k * 22.5f, root.up) * root.forward;
                                float real = RealHit(eye, dir, Reach + 1f, shells, out RaycastHit rh) ? rh.distance : float.PositiveInfinity;
                                float seen = float.PositiveInfinity; string what = null;
                                foreach (RaycastHit sh in Physics.RaycastAll(eye, dir, Reach, ~0, QueryTriggerInteraction.Ignore))
                                    if (shells.TryGetValue(sh.collider, out string name) && sh.distance < seen) { seen = sh.distance; what = name; }
                                if (what == null) continue;
                                float gap = Mathf.Min(real, Reach + 1f) - seen;
                                if (gap <= Tolerance) continue;
                                if (!worst.TryGetValue(what, out var w) || gap > w.gap) worst[what] = (gap, root.InverseTransformPoint(eye));
                            }
                    }
            }
            finally
            {
                foreach (GameObject t in temps) Object.DestroyImmediate(t);
                Physics.SyncTransforms();
            }

            var sb = new StringBuilder();
            sb.Append("shell audit: ").Append(stands).Append(" standing places, ").Append(worst.Count).Append(" surfaces the camera can enter");
            foreach (var kv in worst.OrderByDescending(kv => kv.Value.gap))
                sb.Append("\n  ").Append(kv.Key).Append(": ").Append(kv.Value.gap.ToString("0.00")).Append(" m in front of its collider, seen from ").Append(kv.Value.at.ToString("F1"));
            return sb.ToString();
        }

        // The nearest real (non-trigger, not temporary) collider along a ray.
        private static bool RealHit(Vector3 from, Vector3 dir, float max, Dictionary<Collider, string> shells, out RaycastHit best)
        {
            best = default; bool any = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, dir, max, ~0, QueryTriggerInteraction.Ignore))
            {
                if (shells.ContainsKey(h.collider)) continue;
                if (!any || h.distance < best.distance) { best = h; any = true; }
            }
            return any;
        }

        private static bool Blocked(Vector3 a, Vector3 b, Dictionary<Collider, string> shells)
        {
            foreach (Collider c in Physics.OverlapCapsule(a, b, Radius, ~0, QueryTriggerInteraction.Ignore))
                if (!shells.ContainsKey(c)) return true;
            return false;
        }

        // A renderer whose own object carries a solid collider at least as large as
        // what it draws (a box round a panel, a mesh collider of the same mesh).
        private static bool CoveredByOwnCollider(Renderer r)
        {
            Bounds seen = r.bounds;
            foreach (Collider c in r.GetComponents<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                Bounds b = c.bounds;
                b.Expand(0.02f);
                if (b.Contains(seen.min) && b.Contains(seen.max)) return true;
            }
            return false;
        }

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
