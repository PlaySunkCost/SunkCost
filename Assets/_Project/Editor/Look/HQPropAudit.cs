using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The HQ prop audit (Dan, 8 October 2026, BRIEF B.4–B.6): captures of every prop
    // spot from a player's eye, headless through LookCapture.Shoot, and a numeric
    // report of each prop's footing (its renderer bounds against the surface under
    // it), so a floating, sunken, tilted or wall-crossing prop is a number before it
    // is a picture. `ShootBefore` / `ShootAfter` keep both sets.
    public static class HQPropAudit
    {
        public const string Prefix = "hq-consoles-props";

        private static readonly (string view, Vector3 from, Vector3 at, float fov)[] Views =
        {
            ("office-board", new Vector3(-20f, 1.6f, 12f), new Vector3(-22f, 1.2f, 10.6f), 70f),
            ("office-sofa", new Vector3(-20f, 1.6f, 12f), new Vector3(-22f, 0.5f, 16.5f), 70f),
            ("office-lockers", new Vector3(-18f, 1.6f, 13f), new Vector3(-20f, 1.0f, 17f), 70f),
            ("racks-east", new Vector3(-4f, 1.6f, 13f), new Vector3(-4f, 1.5f, 17.2f), 70f),
            ("racks-west", new Vector3(-12f, 1.6f, 13f), new Vector3(-12f, 1.5f, 17.2f), 70f),
            ("racks-end", new Vector3(4f, 1.6f, 13f), new Vector3(4f, 1.5f, 17.2f), 70f),
            ("racks-along", new Vector3(-15f, 1.6f, 16f), new Vector3(6f, 1.4f, 17.2f), 70f),
            ("cargo", new Vector3(18f, 1.7f, 8f), new Vector3(24f, 0.5f, 8f), 70f),
            ("cargo-toolbox", new Vector3(19f, 1.7f, 8f), new Vector3(23f, 0.4f, 12f), 70f),
            ("depot-front", new Vector3(-6f, 1.7f, 7f), new Vector3(-6f, 2.5f, 10f), 70f),
            ("depot-front-wide", new Vector3(-8f, 1.7f, 2f), new Vector3(-8f, 3.5f, 10f), 70f),
            ("pad", new Vector3(18f, 1.65f, 14.5f), new Vector3(18f, 1.4f, 10.8f), 70f),
            ("counter", new Vector3(-6f, 1.65f, 11f), new Vector3(-6f, 1.3f, 14f), 70f),
            ("crew-rest", new Vector3(7f, 1.7f, -8f), new Vector3(7f, 0.5f, -12f), 70f),
            ("lifebuoys", new Vector3(4f, 1.7f, -13f), new Vector3(4f, 1.0f, -17.5f), 70f),
            ("benches", new Vector3(-8f, 1.7f, 2f), new Vector3(-8f, 0.5f, 6f), 70f),
        };

        public static string Shoot() => ShootAs(Prefix);
        public static string ShootBefore() => ShootAs(Prefix + "-before");
        public static string ShootAfter() => ShootAs(Prefix + "-after");

        // The generated HQ scene as saved on disk, opened if the editor has another scene up
        // (a matrix leaves Session.unity open): the "before" set is shot from the committed
        // scene, the "after" set from the rebuilt one.
        public static void EnsureScene()
        {
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path == SunkCost.Editor.Prototype.HQPrototypeBuilder.ScenePath) return;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(SunkCost.Editor.Prototype.HQPrototypeBuilder.ScenePath);
        }

        public static string ShootAs(string prefix)
        {
            EnsureScene();
            Directory.CreateDirectory(LookCapture.Folder);
            RenderTexture sky = SunkCost.Look.SkyEnvironment.Refresh();
            try
            {
                foreach (var v in Views) LookCapture.Shoot(prefix + "-" + v.view, v.from, v.at, v.fov);
            }
            finally { SunkCost.Look.SkyEnvironment.Restore(sky); }
            return $"{Views.Length} captures: {LookCapture.Folder}/{prefix}-<view>.png";
        }

        // Every renderer of the dressing groups (the depot, the cargo apron, the crew
        // rest, the loose props on the platform root) with its bounds, the surface
        // found under it (the higher of a ray down from its bottom centre and the top
        // of any other renderer's bounds beneath it, since the HQ kit's shelves and
        // pallets carry no colliders) and the gap. Written to Temp/look/<name>.txt too.
        public static string Report() => Report("hq-consoles-props-report");

        public static string ReportBefore() => Report("hq-consoles-props-report-before");
        public static string ReportAfter() => Report("hq-consoles-props-report-after");

        public static string Report(string name)
        {
            EnsureScene();
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            var all = new List<Renderer>(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None));
            foreach (string groupName in new[] { "Shop Room", "Cargo apron", "Crew rest", "Crew Arrival", "Intake and quota" })
            {
                GameObject group = GameObject.Find(groupName);
                if (group == null) { sb.Append(groupName).Append(": missing\n"); continue; }
                sb.Append("== ").Append(groupName).Append('\n');
                foreach (Renderer r in group.GetComponentsInChildren<Renderer>())
                    if (Interesting(r)) Line(sb, r, all);
            }
            GameObject platform = GameObject.Find("Platform");
            if (platform != null)
            {
                sb.Append("== Platform root props\n");
                foreach (Transform child in platform.transform)
                    if (child.name == "Bench" || child.name == "Lifebuoy" || child.name == "Pipes")
                        foreach (Renderer r in child.GetComponentsInChildren<Renderer>()) Line(sb, r, all);
            }
            string text = sb.ToString();
            Directory.CreateDirectory(LookCapture.Folder);
            File.WriteAllText(Path.Combine(LookCapture.Folder, name + ".txt"), text);
            return text;
        }

        private static bool Interesting(Renderer r)
        {
            if (r.GetComponent<TextMesh>() != null) return false;
            string n = r.name;
            // Skip the shell and the marks: walls, roofs, floor paint, signs' frames.
            foreach (string skip in new[] { "WallPanel", "RoofCassette", "Fascia", "Portal", "Window", "Pillar", "Safety marking", "Frame ", "Plate", "Sign", "Glass", "Footing", "Flush arrival pad", "Intake sign post", "Cargo sign post", "Pickup sign support", "CagePanel", "Console " })
                if (n.StartsWith(skip)) return false;
            return true;
        }

        private static void Line(StringBuilder sb, Renderer r, List<Renderer> all)
        {
            Bounds b = r.bounds;
            Vector3 bottom = new(b.center.x, b.min.y, b.center.z);
            float surface = float.NegativeInfinity; string by = "nothing";
            var own = new HashSet<Collider>(r.GetComponentsInParent<Collider>(true));
            foreach (Collider c in r.GetComponentsInChildren<Collider>(true)) own.Add(c);
            foreach (RaycastHit hit in Physics.RaycastAll(bottom + Vector3.up * 0.05f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (!own.Contains(hit.collider) && hit.point.y > surface) { surface = hit.point.y; by = "collider " + hit.collider.name; }
            foreach (Renderer other in all)
            {
                if (other == r || other.transform.IsChildOf(r.transform) || r.transform.IsChildOf(other.transform)) continue;
                Bounds o = other.bounds;
                if (bottom.x < o.min.x || bottom.x > o.max.x || bottom.z < o.min.z || bottom.z > o.max.z) continue;
                if (o.max.y <= b.min.y + 0.03f && o.max.y > surface) { surface = o.max.y; by = "renderer " + other.name; }
            }
            float gap = float.IsNegativeInfinity(surface) ? float.NaN : b.min.y - surface;
            float tilt = Vector3.Angle(r.transform.up, Vector3.up);
            string parent = r.transform.parent != null ? r.transform.parent.name : "-";
            sb.Append(r.name).Append(" [").Append(parent).Append("] at ").Append(r.transform.position.ToString("0.00"))
              .Append(" bounds x ").Append(b.min.x.ToString("0.00")).Append("..").Append(b.max.x.ToString("0.00"))
              .Append(" y ").Append(b.min.y.ToString("0.00")).Append("..").Append(b.max.y.ToString("0.00"))
              .Append(" z ").Append(b.min.z.ToString("0.00")).Append("..").Append(b.max.z.ToString("0.00"))
              .Append(" surface=").Append(float.IsNegativeInfinity(surface) ? "none" : surface.ToString("0.00")).Append(" (").Append(by).Append(") gap=").Append(float.IsNaN(gap) ? "?" : gap.ToString("+0.000;-0.000"))
              .Append(" tilt=").Append(tilt.ToString("0.0")).Append('\n');
        }
    }
}
