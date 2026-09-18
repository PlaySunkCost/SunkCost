using System;
using System.Collections.Generic;
using SunkCost.Look;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The props (Dan, 18 September 2026: "create everything as a thing and then
    // place them"): one prefab per thing under Assets/_Project/Prefabs/Props, built
    // from the kit's meshes and materials with its collider and, where it has one,
    // its light or its sign. The platform builder places prefabs, never raw
    // cubes, so a prop is replaced everywhere by editing its prefab — Dan drops his
    // Blender mesh in place of the placeholder and the placement code never changes.
    // For that reason a prefab that exists is left alone; "Rebuild look props"
    // rewrites them all from code (and loses such edits — it says so).
    //
    // Conventions: pivot at the base centre, +Z is the front, sizes in metres in
    // each prop's comment.
    public static class PropBuilder
    {
        public const string Folder = "Assets/_Project/Prefabs/Props";

        public static bool Force;

        // ---- structure ------------------------------------------------------------------

        // A leg of the platform: rust, 2.4 m across, `height` tall, a collar at the top. Base at the seabed.
        public static GameObject Leg(float height) => Prefab($"Leg{F(height)}", root =>
        {
            Part(root, "Shaft", MeshKit.Cylinder(1.2f, height, 20), LookMaterials.RustSteel(), Vector3.zero);
            Part(root, "Collar", MeshKit.Cylinder(1.45f, 0.5f, 20), LookMaterials.HullPanelDark(), new Vector3(0f, height - 0.5f, 0f));
            Part(root, "Foot", MeshKit.Cylinder(1.6f, 0.6f, 20), LookMaterials.RustSteel(), Vector3.zero);
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 1.2f; col.height = height; col.center = new Vector3(0f, height / 2f, 0f);
        });

        // A 2 m railing segment along X: hazard top rail at 1.1 m, a dark mid rail,
        // a hazard kick plate, two posts. The collider is the whole segment.
        public static GameObject Rail() => Prefab("Rail2m", root =>
        {
            foreach (float x in new[] { -0.96f, 0.96f }) Part(root, "Post", MeshKit.Box(new Vector3(0.08f, 1.1f, 0.08f)), LookMaterials.RailDark(), new Vector3(x, 0f, 0f));
            Part(root, "Top Rail", MeshKit.Box(new Vector3(2f, 0.09f, 0.09f), 0f), LookMaterials.RailYellow(), new Vector3(0f, 1.05f, 0f));
            Part(root, "Mid Rail", MeshKit.Box(new Vector3(2f, 0.05f, 0.05f), 0f), LookMaterials.RailDark(), new Vector3(0f, 0.55f, 0f));
            Part(root, "Kick Plate", MeshKit.Box(new Vector3(2f, 0.16f, 0.04f), 0f), LookMaterials.Hazard(), Vector3.zero);
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.6f, 0f); col.size = new Vector3(2f, 1.2f, 0.12f);
        });

        // A lamp post: 3.4 m, a hooded head with a warm strip under it and a point light.
        public static GameObject LampPost() => Prefab("LampPost", root =>
        {
            Part(root, "Post", MeshKit.Cylinder(0.06f, 3.2f, 10), LookMaterials.RailDark(), Vector3.zero);
            Part(root, "Base", MeshKit.Cylinder(0.16f, 0.12f, 12), LookMaterials.RailDark(), Vector3.zero);
            Part(root, "Head", MeshKit.Box(new Vector3(0.36f, 0.22f, 0.36f)), LookMaterials.HullPanelDark(), new Vector3(0f, 3.2f, 0f));
            GameObject strip = Part(root, "Strip", MeshKit.Box(new Vector3(0.28f, 0.03f, 0.28f)), LookMaterials.LampWarm(), new Vector3(0f, 3.18f, 0f));
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 3.0f, 0f);
            light.type = LightType.Point; light.range = 11f; light.intensity = 2.2f; light.color = new Color(1f, 0.78f, 0.5f); light.shadows = LightShadows.None;
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.08f; col.height = 3.4f; col.center = new Vector3(0f, 1.7f, 0f);
        });

        // A warm light strip, `length` m along X, mounted on its top face (pivot at the top centre).
        public static GameObject LightStrip(float length) => Prefab($"LightStrip{F(length)}", root =>
        {
            Part(root, "Housing", MeshKit.Box(new Vector3(length, 0.08f, 0.10f), 0.08f), LookMaterials.RailDark(), Vector3.zero);
            Part(root, "Glass", MeshKit.Box(new Vector3(length - 0.06f, 0.03f, 0.07f), 0.08f), LookMaterials.LampWarm(), new Vector3(0f, -0.005f, 0f));
        });

        // A crate, 1.2 m square, 1 m high, in one of the crew's colours.
        public static GameObject Crate(string colour) => Prefab("Crate" + colour, root =>
        {
            Material m = colour switch { "Red" => LookMaterials.CrateRed(), "Green" => LookMaterials.CrateGreen(), "Yellow" => LookMaterials.CrateYellow(), _ => LookMaterials.CrateGrey() };
            Part(root, "Body", MeshKit.Box(new Vector3(1.2f, 1.0f, 1.2f)), m, Vector3.zero);
            Part(root, "Lid Rim", MeshKit.Box(new Vector3(1.26f, 0.08f, 1.26f)), LookMaterials.RailDark(), new Vector3(0f, 0.92f, 0f));
            foreach (float x in new[] { -0.6f, 0.6f }) Part(root, "Edge", MeshKit.Box(new Vector3(0.06f, 1.0f, 1.26f)), LookMaterials.RailDark(), new Vector3(x, 0f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0f); col.size = new Vector3(1.26f, 1.0f, 1.26f);
        });

        // A red fender hung on a leg: a 0.9 m drum on a chain (a thin cylinder).
        public static GameObject Fender() => Prefab("Fender", root =>
        {
            Part(root, "Drum", MeshKit.Cylinder(0.45f, 1.4f, 14), LookMaterials.Fender(), Vector3.zero);
            Part(root, "Chain", MeshKit.Cylinder(0.03f, 2.2f, 6), LookMaterials.RailDark(), new Vector3(0f, 1.4f, 0f));
        });

        // A bollard on the pontoon: 0.8 m, dark, a yellow band.
        public static GameObject Bollard() => Prefab("Bollard", root =>
        {
            Part(root, "Body", MeshKit.Cylinder(0.18f, 0.8f, 12), LookMaterials.RailDark(), Vector3.zero);
            Part(root, "Cap", MeshKit.Cylinder(0.24f, 0.14f, 12), LookMaterials.RailDark(), new Vector3(0f, 0.8f, 0f));
            Part(root, "Band", MeshKit.Cylinder(0.19f, 0.12f, 12), LookMaterials.RailYellow(), new Vector3(0f, 0.5f, 0f));
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.24f; col.height = 0.95f; col.center = new Vector3(0f, 0.47f, 0f);
        });

        // ---- signs ----------------------------------------------------------------------

        // A sign board `w` by `h` m: a dark plate with a glowing frame, the text a
        // SignText reading HQSigns by key (and a second line under it, if a
        // `.sub` key exists). Pivot at the plate's bottom centre; the front is +Z.
        public static GameObject SignBoard(float w, float h) => Prefab($"Sign{F(w)}x{F(h)}", root =>
        {
            Part(root, "Plate", MeshKit.Box(new Vector3(w, h, 0.08f)), LookMaterials.SignBoard(), Vector3.zero);
            Part(root, "Frame Top", MeshKit.Box(new Vector3(w, 0.05f, 0.03f)), LookMaterials.SignGlow(), new Vector3(0f, h - 0.05f, 0.04f));
            Part(root, "Frame Bottom", MeshKit.Box(new Vector3(w, 0.05f, 0.03f)), LookMaterials.SignGlow(), new Vector3(0f, 0f, 0.04f));
            GameObject title = Text(root, "Title", new Vector3(0f, h * 0.60f, 0.05f), h * 0.36f, new Color(1f, 0.80f, 0.45f), TextAnchor.MiddleCenter);
            title.AddComponent<SignText>().Fit(w - 0.4f, h * 0.44f);
            GameObject sub = Text(root, "Sub", new Vector3(0f, h * 0.22f, 0.05f), h * 0.20f, new Color(0.75f, 0.85f, 0.9f), TextAnchor.MiddleCenter);
            sub.AddComponent<SignText>().Fit(w - 0.4f, h * 0.24f);
        });

        // A deck marking `w` by `d` m painted on the plates: a pale border and the
        // text, 2 cm proud of the deck so it draws over it. Pivot at its centre.
        public static GameObject DeckMarking(float w, float d) => Prefab($"Marking{F(w)}x{F(d)}", root =>
        {
            float t = 0.12f;
            Part(root, "Edge N", MeshKit.Box(new Vector3(w, 0.02f, t)), LookMaterials.DeckMarking(), new Vector3(0f, 0f, d / 2f - t / 2f));
            Part(root, "Edge S", MeshKit.Box(new Vector3(w, 0.02f, t)), LookMaterials.DeckMarking(), new Vector3(0f, 0f, -d / 2f + t / 2f));
            Part(root, "Edge E", MeshKit.Box(new Vector3(t, 0.02f, d)), LookMaterials.DeckMarking(), new Vector3(w / 2f - t / 2f, 0f, 0f));
            Part(root, "Edge W", MeshKit.Box(new Vector3(t, 0.02f, d)), LookMaterials.DeckMarking(), new Vector3(-w / 2f + t / 2f, 0f, 0f));
            GameObject text = Text(root, "Text", new Vector3(0f, 0.03f, 0f), Mathf.Min(d * 0.35f, w * 0.12f), new Color(0.86f, 0.84f, 0.78f), TextAnchor.MiddleCenter);
            text.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat on the deck, read from the south
            text.AddComponent<SignText>().Fit(w - 0.6f, d - 0.6f);
        });

        // ---- buildings ------------------------------------------------------------------

        // A booth: `w` wide, 6 m deep, 4.6 m high, open to the front (+Z is the
        // front; the back wall is at -3). A counter across the front, set 1.8 m
        // in; light strips under the roof edge; the sign above the opening. What
        // stands inside (shop stands, the board) is the platform builder's.
        public static GameObject Booth(float w) => Prefab($"Booth{F(w)}", root =>
        {
            const float depth = 6f, height = 4.6f, wall = 0.3f;
            Part(root, "Floor", MeshKit.Box(new Vector3(w, 0.12f, depth)), LookMaterials.DeckPlate(), new Vector3(0f, 0f, 0f));
            Part(root, "Back Wall", MeshKit.Box(new Vector3(w, height, wall)), LookMaterials.HullPanel(), new Vector3(0f, 0f, -depth / 2f + wall / 2f));
            Part(root, "Side Wall W", MeshKit.Box(new Vector3(wall, height, depth)), LookMaterials.HullPanel(), new Vector3(-w / 2f + wall / 2f, 0f, 0f));
            Part(root, "Side Wall E", MeshKit.Box(new Vector3(wall, height, depth)), LookMaterials.HullPanel(), new Vector3(w / 2f - wall / 2f, 0f, 0f));
            Part(root, "Roof", MeshKit.Box(new Vector3(w + 0.6f, 0.35f, depth + 0.6f)), LookMaterials.HullPanelDark(), new Vector3(0f, height, 0f));
            Part(root, "Fascia", MeshKit.Box(new Vector3(w + 0.6f, 0.9f, 0.12f)), LookMaterials.HullPanelDark(), new Vector3(0f, height - 0.9f, depth / 2f + 0.24f));
            Part(root, "Counter", MeshKit.Box(new Vector3(w - 2f * wall, 1.05f, 0.7f)), LookMaterials.HullPanelDark(), new Vector3(0f, 0.12f, depth / 2f - 1.8f));
            Part(root, "Counter Top", MeshKit.Box(new Vector3(w - 2f * wall, 0.06f, 0.8f)), LookMaterials.RailDark(), new Vector3(0f, 1.17f, depth / 2f - 1.8f));
            Part(root, "Counter Stripe", MeshKit.Box(new Vector3(w - 2f * wall, 0.16f, 0.02f)), LookMaterials.Hazard(), new Vector3(0f, 0.2f, depth / 2f - 1.44f));
            // Two strips under the fascia, one along the back wall over the shelves.
            Place(root, LightStrip(Mathf.Round(w / 2f) - 1f), new Vector3(-w / 4f, height - 0.02f, depth / 2f - 0.4f));
            Place(root, LightStrip(Mathf.Round(w / 2f) - 1f), new Vector3(w / 4f, height - 0.02f, depth / 2f - 0.4f));
            Place(root, LightStrip(Mathf.Round(w) - 2f), new Vector3(0f, height - 0.02f, -depth / 2f + 0.8f));
            GameObject sign = Place(root, SignBoard(w * 0.7f, 1.0f), new Vector3(0f, height - 0.85f, depth / 2f + 0.36f));
            sign.name = "Sign";
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, height - 0.6f, 0.5f);
            light.type = LightType.Point; light.range = w + 4f; light.intensity = 1.6f; light.color = new Color(1f, 0.82f, 0.6f); light.shadows = LightShadows.None;
            // Colliders: the walls, the counter and the floor.
            Box(root, new Vector3(0f, height / 2f, -depth / 2f + wall / 2f), new Vector3(w, height, wall));
            Box(root, new Vector3(-w / 2f + wall / 2f, height / 2f, 0f), new Vector3(wall, height, depth));
            Box(root, new Vector3(w / 2f - wall / 2f, height / 2f, 0f), new Vector3(wall, height, depth));
            Box(root, new Vector3(0f, 0.06f, 0f), new Vector3(w, 0.12f, depth));
            Box(root, new Vector3(0f, 0.65f, depth / 2f - 1.8f), new Vector3(w - 2f * wall, 1.1f, 0.8f));
        });

        // A flight of stairs: `rise` m up over `run` m along +Z, `w` wide, steps
        // 0.2 m high. The collider is a ramp so the capsule glides; the steps show.
        public static GameObject Stairs(float rise, float run, float w) => Prefab($"Stairs{F(rise)}x{F(run)}x{F(w)}", root =>
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(rise / 0.2f));
            float stepH = rise / steps, stepD = run / steps;
            for (int i = 0; i < steps; i++)
                Part(root, "Step", MeshKit.Box(new Vector3(w, stepH, stepD)), LookMaterials.DeckPlate(), new Vector3(0f, i * stepH, i * stepD + stepD / 2f - run / 2f + run / 2f));
            foreach (float x in new[] { -w / 2f, w / 2f })
            {
                Part(root, "Stringer", MeshKit.Box(new Vector3(0.12f, 0.3f, Mathf.Sqrt(run * run + rise * rise))), LookMaterials.HullPanelDark(),
                     new Vector3(x, rise / 2f - 0.3f, run / 2f), Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f));
                Part(root, "Handrail", MeshKit.Box(new Vector3(0.06f, 0.06f, Mathf.Sqrt(run * run + rise * rise))), LookMaterials.RailYellow(),
                     new Vector3(x, rise / 2f + 0.95f, run / 2f), Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f));
            }
            // The ramp collider: a box tilted along the flight, its top face the tread line.
            GameObject ramp = new("Ramp");
            ramp.transform.SetParent(root.transform, false);
            float length = Mathf.Sqrt(run * run + rise * rise);
            ramp.transform.localPosition = new Vector3(0f, rise / 2f, run / 2f);
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
            BoxCollider col = ramp.AddComponent<BoxCollider>();
            col.size = new Vector3(w, 0.2f, length); col.center = new Vector3(0f, -0.1f + stepH * 0.5f, 0f);
        });

        // A basketball hoop: the post, a backboard, the ring at 3.05 m, a net of
        // eight strings, and a trigger under the ring for the score (HoopScore is
        // added by the platform builder, which also owns the scoreboard).
        public static GameObject Hoop() => Prefab("Hoop", root =>
        {
            Part(root, "Post", MeshKit.Box(new Vector3(0.14f, 3.6f, 0.14f)), LookMaterials.RailDark(), new Vector3(0f, 0f, -0.9f));
            Part(root, "Arm", MeshKit.Box(new Vector3(0.10f, 0.10f, 0.9f)), LookMaterials.RailDark(), new Vector3(0f, 3.45f, -0.45f));
            Part(root, "Backboard", MeshKit.Box(new Vector3(1.8f, 1.05f, 0.05f)), LookMaterials.Backboard(), new Vector3(0f, 2.9f, 0f));
            Part(root, "Backboard Box", MeshKit.Box(new Vector3(0.6f, 0.02f, 0.06f)), LookMaterials.HoopOrange(), new Vector3(0f, 3.05f, 0.01f));
            GameObject ring = new("Ring");
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 3.05f, 0.25f);
            ring.AddComponent<MeshFilter>().sharedMesh = MeshKit.Ring(0.225f, 0.03f);
            ring.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.HoopOrange();
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                GameObject s = Part(root, "String", MeshKit.Box(new Vector3(0.012f, 0.42f, 0.012f), 0.42f), LookMaterials.Net(), new Vector3(Mathf.Cos(a) * 0.2f, 3.05f, 0.25f + Mathf.Sin(a) * 0.2f));
                s.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 12f, 0f, -Mathf.Cos(a) * 12f);
            }
            Box(root, new Vector3(0f, 1.8f, -0.9f), new Vector3(0.14f, 3.6f, 0.14f));
            Box(root, new Vector3(0f, 2.9f, 0f), new Vector3(1.8f, 1.05f, 0.05f));
            GameObject trigger = new("Score Trigger");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.localPosition = new Vector3(0f, 2.85f, 0.25f);
            BoxCollider t = trigger.AddComponent<BoxCollider>();
            t.isTrigger = true; t.size = new Vector3(0.36f, 0.16f, 0.36f);
        });

        // The tower's beacon: a short mast with a red lamp that pulses.
        public static GameObject BeaconMast() => Prefab("BeaconMast", root =>
        {
            Part(root, "Mast", MeshKit.Cylinder(0.05f, 1.6f, 8), LookMaterials.RailDark(), Vector3.zero);
            Part(root, "Cage", MeshKit.Cylinder(0.22f, 0.5f, 10, false), LookMaterials.RailDark(), new Vector3(0f, 1.6f, 0f));
            GameObject lamp = Part(root, "Lamp", MeshKit.Cylinder(0.16f, 0.4f, 12), LookMaterials.BeaconRed(), new Vector3(0f, 1.65f, 0f));
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            light.type = LightType.Point; light.range = 30f; light.intensity = 0f; light.color = new Color(1f, 0.25f, 0.15f); light.shadows = LightShadows.None;
            root.AddComponent<Beacon>().Configure(lamp.GetComponent<Renderer>(), light, 2.4f, 0f);
        });

        // A flagpole with the company's black flag and its pale mark.
        public static GameObject FlagPole() => Prefab("FlagPole", root =>
        {
            Part(root, "Pole", MeshKit.Cylinder(0.04f, 4.5f, 8), LookMaterials.RailDark(), Vector3.zero);
            Part(root, "Flag", MeshKit.Box(new Vector3(1.6f, 1.0f, 0.02f)), LookMaterials.Flag(), new Vector3(0.82f, 3.4f, 0f));
            Part(root, "Mark", MeshKit.Box(new Vector3(0.5f, 0.5f, 0.025f)), LookMaterials.FlagSkull(), new Vector3(0.82f, 3.65f, 0f));
        });

        // ---- the machinery ------------------------------------------------------------

        private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_');

        private static GameObject Prefab(string name, Action<GameObject> build)
        {
            System.IO.Directory.CreateDirectory(Folder);
            string path = Folder + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !Force) return existing;
            GameObject root = new(name);
            try
            {
                build(root);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject Part(GameObject root, string name, Mesh mesh, Material material, Vector3 localPosition) => Part(root, name, mesh, material, localPosition, Quaternion.identity);
        private static GameObject Part(GameObject root, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = new(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static void Box(GameObject root, Vector3 centre, Vector3 size)
        {
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = centre; col.size = size;
        }

        // A prefab instance inside another prefab (nested), at a local position.
        public static GameObject Place(GameObject parent, GameObject prefab, Vector3 localPosition) => Place(parent, prefab, localPosition, Quaternion.identity);
        public static GameObject Place(GameObject parent, GameObject prefab, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            return go;
        }

        private static GameObject Text(GameObject root, string name, Vector3 localPosition, float lineHeight, Color color, TextAnchor anchor)
        {
            GameObject go = new(name, typeof(TextMesh));
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // a TextMesh reads along its +Z: turned to face a viewer standing at +Z (the front)
            TextMesh mesh = go.GetComponent<TextMesh>();
            mesh.characterSize = lineHeight * 0.1f; // fontSize 64 at characterSize 0.1 is about 1 m per line
            mesh.fontSize = 64;
            mesh.fontStyle = FontStyle.Bold;
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            go.AddComponent<DepthText>().Configure(LookMaterials.DepthText());
            return go;
        }
    }
}
