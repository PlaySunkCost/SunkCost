using System;
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
    // The proportions are the reference picture's: chunky and toy-like — fat
    // posts, thick rails, big glowing lamp cubes, bevelled crates, containers
    // and barrels everywhere. Pivot at the base centre, +Z is the front, sizes
    // in metres in each prop's comment.
    public static class PropBuilder
    {
        public const string Folder = "Assets/_Project/Prefabs/Props";

        public static bool Force;

        // ---- structure ------------------------------------------------------------------

        // A leg of the platform: rust, 2.8 m across, `height` tall, a dark collar at
        // the top and a foot at the bottom. Base at the seabed.
        public static GameObject Leg(float height) => Prefab($"Leg{F(height)}", root =>
        {
            Part(root, "Shaft", MeshKit.Cylinder(1.4f, height, 16), LookMaterials.RustSteel(), Vector3.zero);
            Part(root, "Collar", MeshKit.Cylinder(1.7f, 0.8f, 16), LookMaterials.PanelDark(), new Vector3(0f, height - 0.8f, 0f));
            Part(root, "Collar Band", MeshKit.Cylinder(1.72f, 0.2f, 16), LookMaterials.Hazard(), new Vector3(0f, height - 1.1f, 0f));
            Part(root, "Foot", MeshKit.Cylinder(1.9f, 0.8f, 16), LookMaterials.RustSteel(), Vector3.zero);
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 1.4f; col.height = height; col.center = new Vector3(0f, height / 2f, 0f);
        });

        // A 2 m railing segment along X: fat ink posts, a thick top rail, a mid
        // rail, a hazard kick plate. The collider is the whole segment.
        public static GameObject Rail() => Prefab("Rail2m", root =>
        {
            foreach (float x in new[] { -0.9f, 0.9f }) Part(root, "Post", MeshKit.Box(new Vector3(0.18f, 1.2f, 0.18f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
            Part(root, "Top Rail", MeshKit.Box(new Vector3(2f, 0.16f, 0.16f)), LookMaterials.Ink(), new Vector3(0f, 1.08f, 0f));
            Part(root, "Mid Rail", MeshKit.Box(new Vector3(2f, 0.08f, 0.08f)), LookMaterials.Ink(), new Vector3(0f, 0.6f, 0f));
            Part(root, "Kick Plate", MeshKit.Box(new Vector3(2f, 0.28f, 0.06f)), LookMaterials.Hazard(), Vector3.zero);
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.62f, 0f); col.size = new Vector3(2f, 1.24f, 0.2f);
        });

        // A rail lamp: a fat post with a glowing orange cube on top and a warm
        // light — the picture's rim lights, every few metres.
        public static GameObject RailLamp() => Prefab("RailLamp", root =>
        {
            Part(root, "Post", MeshKit.Box(new Vector3(0.22f, 1.35f, 0.22f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Cap", MeshKit.Box(new Vector3(0.5f, 0.08f, 0.5f)), LookMaterials.Ink(), new Vector3(0f, 1.35f, 0f));
            Part(root, "Lamp", MeshKit.Box(new Vector3(0.44f, 0.44f, 0.44f)), LookMaterials.LampOrange(), new Vector3(0f, 1.43f, 0f));
            Part(root, "Hood", MeshKit.Box(new Vector3(0.54f, 0.08f, 0.54f)), LookMaterials.Ink(), new Vector3(0f, 1.87f, 0f));
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Point; light.range = 9f; light.intensity = 4.5f; light.color = new Color(1f, 0.58f, 0.24f); light.shadows = LightShadows.None;
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.85f, 0f); col.size = new Vector3(0.38f, 1.75f, 0.38f);
        });

        // A tall lamp for the court and the pontoon: 4.2 m, a glowing box head.
        public static GameObject LampPost() => Prefab("LampPost", root =>
        {
            Part(root, "Post", MeshKit.Box(new Vector3(0.22f, 4.0f, 0.22f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Base", MeshKit.Box(new Vector3(0.5f, 0.16f, 0.5f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Arm", MeshKit.Box(new Vector3(0.16f, 0.16f, 0.9f)), LookMaterials.Ink(), new Vector3(0f, 3.85f, 0.35f));
            Part(root, "Head", MeshKit.Box(new Vector3(0.7f, 0.22f, 0.7f)), LookMaterials.Ink(), new Vector3(0f, 3.95f, 0.75f));
            Part(root, "Lamp", MeshKit.Box(new Vector3(0.6f, 0.12f, 0.6f)), LookMaterials.LampWarm(), new Vector3(0f, 3.84f, 0.75f));
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 3.7f, 0.75f);
            light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Point; light.range = 18f; light.intensity = 10f; light.color = new Color(1f, 0.76f, 0.48f); light.shadows = LightShadows.None;
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 2f, 0f); col.size = new Vector3(0.5f, 4f, 0.5f);
        });

        // An orange light strip, `length` m along X, hung from its top face.
        public static GameObject LightStrip(float length) => Prefab($"LightStrip{F(length)}", root =>
        {
            Part(root, "Housing", MeshKit.Box(new Vector3(length, 0.14f, 0.2f), 0.14f), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Glass", MeshKit.Box(new Vector3(length - 0.08f, 0.06f, 0.14f), 0.14f), LookMaterials.LampOrange(), new Vector3(0f, -0.02f, 0f));
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Point; light.range = Mathf.Max(7f, length * 1.8f); light.intensity = 7f; light.color = new Color(1f, 0.64f, 0.30f); light.shadows = LightShadows.None;
        });

        // A crate, 1.4 m square, 1.2 m high, bevelled, in one of the crew's colours.
        public static GameObject Crate(string colour) => Prefab("Crate" + colour, root =>
        {
            Material m = colour switch { "Red" => LookMaterials.CrateRed(), "Green" => LookMaterials.CrateGreen(), "Yellow" => LookMaterials.CrateYellow(), "Navy" => LookMaterials.CrateNavy(), _ => LookMaterials.CrateGrey() };
            Part(root, "Body", MeshKit.Box(new Vector3(1.4f, 1.2f, 1.4f)), m, Vector3.zero);
            Part(root, "Lid", MeshKit.Box(new Vector3(1.48f, 0.1f, 1.48f)), LookMaterials.Ink(), new Vector3(0f, 1.12f, 0f));
            Part(root, "Foot", MeshKit.Box(new Vector3(1.48f, 0.08f, 1.48f)), LookMaterials.Ink(), Vector3.zero);
            foreach (float x in new[] { -0.7f, 0.7f }) Part(root, "Edge", MeshKit.Box(new Vector3(0.08f, 1.2f, 1.48f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.61f, 0f); col.size = new Vector3(1.48f, 1.22f, 1.48f);
        });

        // A shipping container, `length` m long, 2.6 m wide, 2.6 m high, along X.
        public static GameObject Container(string colour, float length) => Prefab($"Container{colour}{F(length)}", root =>
        {
            Material m = colour switch { "Red" => LookMaterials.ContainerRed(), "Green" => LookMaterials.ContainerGreen(), "Yellow" => LookMaterials.ContainerYellow(), _ => LookMaterials.ContainerGrey() };
            Part(root, "Body", MeshKit.Box(new Vector3(length, 2.6f, 2.6f)), m, Vector3.zero);
            foreach (float x in new[] { -length / 2f + 0.1f, length / 2f - 0.1f })
                Part(root, "Frame", MeshKit.Box(new Vector3(0.24f, 2.64f, 2.66f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
            Part(root, "Rail Top", MeshKit.Box(new Vector3(length, 0.12f, 2.66f)), LookMaterials.Ink(), new Vector3(0f, 2.54f, 0f));
            Part(root, "Rail Bottom", MeshKit.Box(new Vector3(length, 0.12f, 2.66f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Door Line", MeshKit.Box(new Vector3(0.06f, 2.2f, 0.06f)), LookMaterials.Ink(), new Vector3(length / 2f + 0.03f, 0.2f, 0f));
            Part(root, "Hazard", MeshKit.Box(new Vector3(0.8f, 0.3f, 0.04f)), LookMaterials.Hazard(), new Vector3(length / 2f - 0.8f, 0.4f, 1.31f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.3f, 0f); col.size = new Vector3(length + 0.1f, 2.6f, 2.66f);
        });

        // A barrel: 0.7 m across, 1 m high, red or rust, with two ink rings.
        public static GameObject Barrel(string colour) => Prefab("Barrel" + colour, root =>
        {
            Material m = colour == "Rust" ? LookMaterials.BarrelRust() : LookMaterials.BarrelRed();
            Part(root, "Body", MeshKit.Cylinder(0.35f, 1.0f, 14), m, Vector3.zero);
            foreach (float y in new[] { 0.22f, 0.72f }) Part(root, "Ring", MeshKit.Cylinder(0.37f, 0.08f, 14), LookMaterials.Ink(), new Vector3(0f, y, 0f));
            Part(root, "Lid", MeshKit.Cylinder(0.33f, 0.05f, 14), LookMaterials.Ink(), new Vector3(0f, 1.0f, 0f));
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.37f; col.height = 1.05f; col.center = new Vector3(0f, 0.52f, 0f);
        });

        // A red fender hung on a leg: a 1.2 m drum with a white band, on a chain.
        public static GameObject Fender() => Prefab("Fender", root =>
        {
            Part(root, "Drum", MeshKit.Cylinder(0.6f, 1.8f, 14), LookMaterials.Fender(), Vector3.zero);
            Part(root, "Band", MeshKit.Cylinder(0.62f, 0.3f, 14), LookMaterials.FenderBand(), new Vector3(0f, 0.75f, 0f));
            Part(root, "Chain", MeshKit.Cylinder(0.05f, 2.4f, 6), LookMaterials.Ink(), new Vector3(0f, 1.8f, 0f));
        });

        // A bollard on the pontoon: 1 m, ink, a hazard band.
        public static GameObject Bollard() => Prefab("Bollard", root =>
        {
            Part(root, "Body", MeshKit.Cylinder(0.26f, 1.0f, 12), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Cap", MeshKit.Cylinder(0.34f, 0.2f, 12), LookMaterials.Ink(), new Vector3(0f, 1.0f, 0f));
            Part(root, "Band", MeshKit.Cylinder(0.27f, 0.2f, 12), LookMaterials.Hazard(), new Vector3(0f, 0.55f, 0f));
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.34f; col.height = 1.2f; col.center = new Vector3(0f, 0.6f, 0f);
        });

        // A pipe run, `length` m along X, 0.3 m across, on two brackets.
        public static GameObject Pipe(float length) => Prefab($"Pipe{F(length)}", root =>
        {
            GameObject pipe = Part(root, "Pipe", MeshKit.Cylinder(0.15f, length, 10), LookMaterials.RustSteel(), new Vector3(-length / 2f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f));
            foreach (float x in new[] { -length / 2f + 0.4f, length / 2f - 0.4f })
                Part(root, "Bracket", MeshKit.Box(new Vector3(0.14f, 0.36f, 0.36f), 0.18f), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
        });

        // A vent for the roofs: a dark box with slats and a stub chimney.
        public static GameObject Vent() => Prefab("Vent", root =>
        {
            Part(root, "Box", MeshKit.Box(new Vector3(1.6f, 1.0f, 1.0f)), LookMaterials.PanelDark(), Vector3.zero);
            for (int i = 0; i < 4; i++) Part(root, "Slat", MeshKit.Box(new Vector3(1.4f, 0.08f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, 0.2f + i * 0.2f, 0.5f));
            Part(root, "Stack", MeshKit.Cylinder(0.22f, 0.8f, 10), LookMaterials.Ink(), new Vector3(0.4f, 1.0f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0f); col.size = new Vector3(1.6f, 1f, 1f);
        });

        // An antenna mast: 5 m, a dish, a red lamp at the tip.
        public static GameObject Antenna() => Prefab("Antenna", root =>
        {
            Part(root, "Mast", MeshKit.Cylinder(0.08f, 5f, 8), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Base", MeshKit.Box(new Vector3(0.5f, 0.2f, 0.5f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Dish", MeshKit.Cylinder(0.5f, 0.1f, 14), LookMaterials.PanelDark(), new Vector3(0f, 3.2f, 0.3f), Quaternion.Euler(-60f, 0f, 0f));
            foreach (float y in new[] { 2.2f, 4.0f }) Part(root, "Cross", MeshKit.Box(new Vector3(0.8f, 0.06f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, y, 0f));
            Part(root, "Tip", MeshKit.Box(new Vector3(0.16f, 0.16f, 0.16f)), LookMaterials.BeaconRed(), new Vector3(0f, 5f, 0f));
        });

        // A teal screen on a bracket, for booth walls: 1.2 by 0.8 m, pivot at its bottom centre, front +Z.
        public static GameObject Screen() => Prefab("Screen", root =>
        {
            Part(root, "Frame", MeshKit.Box(new Vector3(1.3f, 0.9f, 0.1f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Glass", MeshKit.Box(new Vector3(1.16f, 0.76f, 0.02f)), LookMaterials.ScreenTeal(), new Vector3(0f, 0.07f, 0.05f));
        });

        // A shelf of goods for the booths: `w` m wide, three boards with rows of
        // small crates in mixed colours, against a wall behind it (-Z).
        public static GameObject Shelf(float w) => Prefab($"Shelf{F(w)}", root =>
        {
            foreach (float x in new[] { -w / 2f + 0.1f, w / 2f - 0.1f })
                Part(root, "Upright", MeshKit.Box(new Vector3(0.12f, 2.6f, 0.7f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
            Material[] tints = { LookMaterials.CrateRed(), LookMaterials.CrateGreen(), LookMaterials.CrateYellow(), LookMaterials.CrateGrey(), LookMaterials.CrateNavy() };
            var random = new System.Random(Mathf.RoundToInt(w * 10f));
            foreach (float y in new[] { 0.3f, 1.1f, 1.9f })
            {
                Part(root, "Board", MeshKit.Box(new Vector3(w - 0.2f, 0.08f, 0.7f)), LookMaterials.PanelDark(), new Vector3(0f, y, 0f));
                float x = -w / 2f + 0.35f;
                while (x < w / 2f - 0.5f)
                {
                    float size = 0.32f + (float)random.NextDouble() * 0.28f;
                    Material tint = tints[random.Next(tints.Length)];
                    Part(root, "Goods", MeshKit.Box(new Vector3(size, size * 0.8f, 0.5f)), tint, new Vector3(x + size / 2f, y + 0.08f, 0f));
                    x += size + 0.12f;
                }
            }
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.3f, 0f); col.size = new Vector3(w, 2.6f, 0.7f);
        });

        // A ladder, `height` m, rungs every 0.3 m, against a wall behind it (-Z).
        public static GameObject Ladder(float height) => Prefab($"Ladder{F(height)}", root =>
        {
            foreach (float x in new[] { -0.25f, 0.25f }) Part(root, "Rail", MeshKit.Box(new Vector3(0.08f, height, 0.08f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
            for (float y = 0.3f; y < height; y += 0.3f) Part(root, "Rung", MeshKit.Box(new Vector3(0.5f, 0.05f, 0.05f)), LookMaterials.Hazard(), new Vector3(0f, y, 0f));
            for (float y = 0f; y < height; y += 1.2f) Part(root, "Bracket", MeshKit.Box(new Vector3(0.7f, 0.06f, 0.3f)), LookMaterials.Ink(), new Vector3(0f, y, -0.15f));
        });

        // A cable tray, `length` m along X, with three cables in it.
        public static GameObject CableTray(float length) => Prefab($"CableTray{F(length)}", root =>
        {
            Part(root, "Tray", MeshKit.Box(new Vector3(length, 0.06f, 0.4f)), LookMaterials.Ink(), Vector3.zero);
            foreach (float z in new[] { -0.12f, 0f, 0.12f })
                Part(root, "Cable", MeshKit.Cylinder(0.035f, length, 6), z == 0f ? LookMaterials.Fender() : LookMaterials.PanelDark(), new Vector3(-length / 2f, 0.1f, z), Quaternion.Euler(0f, 0f, -90f));
            for (float x = -length / 2f + 0.5f; x < length / 2f; x += 2f) Part(root, "Rib", MeshKit.Box(new Vector3(0.06f, 0.16f, 0.42f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
        });

        // A row of `count` small orange lamps under a fascia, `length` m along X, hung from the top.
        public static GameObject LampRow(float length, int count) => Prefab($"LampRow{F(length)}x{count}", root =>
        {
            for (int i = 0; i < count; i++)
            {
                float x = -length / 2f + (i + 0.5f) * length / count;
                Part(root, "Bracket", MeshKit.Box(new Vector3(0.12f, 0.2f, 0.12f), 0.2f), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
                Part(root, "Lamp", MeshKit.Box(new Vector3(0.22f, 0.22f, 0.22f), 0.22f), LookMaterials.LampOrange(), new Vector3(x, -0.2f, 0f));
            }
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, -0.6f, 0.4f);
            light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Point; light.range = length * 1.2f; light.intensity = 6f; light.color = new Color(1f, 0.6f, 0.26f); light.shadows = LightShadows.None;
        });

        // A string of lights, `length` m along X between two posts' tops, sagging
        // in the middle, a small lamp every metre or so.
        public static GameObject LightString(float length) => Prefab($"LightString{F(length)}", root =>
        {
            int segments = Mathf.Max(6, Mathf.RoundToInt(length / 1.5f));
            float sag = length * 0.08f;
            Vector3 prev = new(-length / 2f, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 p = new(-length / 2f + t * length, -sag * 4f * t * (1f - t), 0f);
                Vector3 mid = (prev + p) / 2f; Vector3 d = p - prev;
                Part(root, "Cable", MeshKit.Cylinder(0.025f, d.magnitude, 6), LookMaterials.Ink(), mid - d / 2f, Quaternion.FromToRotation(Vector3.up, d.normalized));
                if (i < segments) Part(root, "Bulb", MeshKit.Box(new Vector3(0.16f, 0.2f, 0.16f), 0.2f), LookMaterials.LampOrange(), p);
                prev = p;
            }
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, -sag - 0.5f, 0f);
            light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Point; light.range = length * 0.9f; light.intensity = 5f; light.color = new Color(1f, 0.6f, 0.26f); light.shadows = LightShadows.None;
        });

        // A tool board for a booth wall: a panel with hanging shapes in a row.
        public static GameObject ToolBoard() => Prefab("ToolBoard", root =>
        {
            Part(root, "Board", MeshKit.Box(new Vector3(2.4f, 1.4f, 0.08f)), LookMaterials.PanelDark(), Vector3.zero);
            Material[] tints = { LookMaterials.Ink(), LookMaterials.CrateYellow(), LookMaterials.Ink(), LookMaterials.Fender(), LookMaterials.CrateGrey() };
            for (int i = 0; i < 5; i++)
            {
                float x = -0.9f + i * 0.45f;
                Part(root, "Hook", MeshKit.Box(new Vector3(0.06f, 0.06f, 0.14f)), LookMaterials.Ink(), new Vector3(x, 1.15f, 0.08f));
                Part(root, "Tool", MeshKit.Box(new Vector3(0.12f, 0.7f, 0.06f)), tints[i], new Vector3(x, 0.4f, 0.1f));
                Part(root, "Tool Head", MeshKit.Box(new Vector3(0.28f, 0.16f, 0.08f)), tints[i], new Vector3(x, 1.0f, 0.1f));
            }
        });

        // A salvage crane: a fat mast, a boom out along +Z with a hook on a cable, a
        // winch drum at the foot, hazard bands.
        public static GameObject Crane() => Prefab("Crane", root =>
        {
            Part(root, "Base", MeshKit.Box(new Vector3(2.2f, 0.4f, 2.2f)), LookMaterials.PanelDark(), Vector3.zero);
            Part(root, "Base Band", MeshKit.Box(new Vector3(2.24f, 0.2f, 2.24f)), LookMaterials.Hazard(), new Vector3(0f, 0.1f, 0f));
            Part(root, "Cab", MeshKit.Box(new Vector3(1.4f, 1.6f, 1.6f)), LookMaterials.Panel(), new Vector3(-0.3f, 0.4f, -0.2f));
            Part(root, "Cab Window", MeshKit.Box(new Vector3(0.9f, 0.6f, 0.06f)), LookMaterials.WindowGlow(), new Vector3(-0.3f, 1.2f, 0.62f));
            Part(root, "Mast", MeshKit.Box(new Vector3(0.6f, 7f, 0.6f)), LookMaterials.RustSteel(), new Vector3(0.4f, 0.4f, 0f));
            Part(root, "Mast Band", MeshKit.Box(new Vector3(0.64f, 0.4f, 0.64f)), LookMaterials.Hazard(), new Vector3(0.4f, 6.8f, 0f));
            Part(root, "Boom", MeshKit.Box(new Vector3(0.4f, 0.4f, 9f)), LookMaterials.RustSteel(), new Vector3(0.4f, 6.9f, 4.2f));
            Part(root, "Boom Tip", MeshKit.Box(new Vector3(0.5f, 0.5f, 0.6f)), LookMaterials.Hazard(), new Vector3(0.4f, 6.85f, 8.5f));
            Part(root, "Stay", MeshKit.Cylinder(0.03f, 8.6f, 6), LookMaterials.Ink(), new Vector3(0.4f, 7.3f, 0.2f), Quaternion.Euler(78f, 0f, 0f));
            Part(root, "Cable", MeshKit.Cylinder(0.03f, 3.5f, 6), LookMaterials.Ink(), new Vector3(0.4f, 3.4f, 8.5f));
            Part(root, "Hook", MeshKit.Box(new Vector3(0.3f, 0.5f, 0.2f)), LookMaterials.Hazard(), new Vector3(0.4f, 2.9f, 8.5f));
            Part(root, "Winch", MeshKit.Cylinder(0.35f, 0.7f, 12), LookMaterials.Ink(), new Vector3(1.1f, 0.4f, -0.4f), Quaternion.Euler(0f, 0f, 90f));
            Part(root, "Lamp", MeshKit.Box(new Vector3(0.24f, 0.24f, 0.24f)), LookMaterials.BeaconRed(), new Vector3(0.4f, 7.4f, 0f));
            Box(root, new Vector3(0f, 0.2f, 0f), new Vector3(2.2f, 0.4f, 2.2f));
            Box(root, new Vector3(-0.3f, 1.2f, -0.2f), new Vector3(1.4f, 1.6f, 1.6f));
            Box(root, new Vector3(0.4f, 3.9f, 0f), new Vector3(0.6f, 7f, 0.6f));
        });

        // A small crate, 0.7 m, for stacking on the big ones and the shelves.
        public static GameObject SmallCrate(string colour) => Prefab("SmallCrate" + colour, root =>
        {
            Material m = colour switch { "Red" => LookMaterials.CrateRed(), "Green" => LookMaterials.CrateGreen(), "Yellow" => LookMaterials.CrateYellow(), "Navy" => LookMaterials.CrateNavy(), _ => LookMaterials.CrateGrey() };
            Part(root, "Body", MeshKit.Box(new Vector3(0.7f, 0.6f, 0.7f)), m, Vector3.zero);
            Part(root, "Lid", MeshKit.Box(new Vector3(0.74f, 0.06f, 0.74f)), LookMaterials.Ink(), new Vector3(0f, 0.56f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.31f, 0f); col.size = new Vector3(0.74f, 0.62f, 0.74f);
        });

        // A gas bottle cage: four yellow tanks in an ink frame.
        public static GameObject BottleCage() => Prefab("BottleCage", root =>
        {
            Part(root, "Floor", MeshKit.Box(new Vector3(1.4f, 0.08f, 0.8f)), LookMaterials.Ink(), Vector3.zero);
            foreach (float x in new[] { -0.7f, 0.7f }) foreach (float z in new[] { -0.4f, 0.4f })
                Part(root, "Post", MeshKit.Box(new Vector3(0.06f, 1.6f, 0.06f)), LookMaterials.Ink(), new Vector3(x, 0f, z));
            Part(root, "Top", MeshKit.Box(new Vector3(1.44f, 0.06f, 0.84f)), LookMaterials.Ink(), new Vector3(0f, 1.6f, 0f));
            for (int i = 0; i < 4; i++)
                Part(root, "Bottle", MeshKit.Cylinder(0.14f, 1.3f, 10), LookMaterials.CrateYellow(), new Vector3(-0.5f + i * 0.33f, 0.08f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.8f, 0f); col.size = new Vector3(1.44f, 1.66f, 0.84f);
        });

        // ---- the floor's clutter (Dan, 19 September 2026: "it could look better") ----------

        // A drain grate flush with the floor: an ink frame with slats.
        public static GameObject Drain() => Prefab("Drain", root =>
        {
            Part(root, "Frame", MeshKit.Box(new Vector3(0.9f, 0.02f, 0.9f)), LookMaterials.Ink(), Vector3.zero);
            for (int i = 0; i < 6; i++) Part(root, "Slat", MeshKit.Box(new Vector3(0.7f, 0.025f, 0.05f)), LookMaterials.PanelDark(), new Vector3(0f, 0f, -0.3f + i * 0.12f));
        });

        // A manhole cover: a round ink plate with a rust ring.
        public static GameObject Manhole() => Prefab("Manhole", root =>
        {
            Part(root, "Cover", MeshKit.Cylinder(0.42f, 0.025f, 20), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Ring", MeshKit.Ring(0.42f, 0.05f, 24, 6), LookMaterials.RustSteel(), new Vector3(0f, 0.02f, 0f));
        });

        // A coil of rope: three rings stacked.
        public static GameObject RopeCoil() => Prefab("RopeCoil", root =>
        {
            for (int i = 0; i < 3; i++)
                Part(root, "Coil", MeshKit.Ring(0.42f - i * 0.03f, 0.13f, 20, 8), LookMaterials.CrateYellow(), new Vector3(0f, 0.07f + i * 0.1f, 0f), Quaternion.Euler(0f, i * 25f, 0f));
        });

        // A pallet with sacks on it.
        public static GameObject Pallet() => Prefab("Pallet", root =>
        {
            for (int i = 0; i < 5; i++) Part(root, "Slat", MeshKit.Box(new Vector3(1.2f, 0.04f, 0.16f)), LookMaterials.BarrelRust(), new Vector3(0f, 0.1f, -0.42f + i * 0.21f));
            foreach (float x in new[] { -0.5f, 0f, 0.5f }) Part(root, "Bearer", MeshKit.Box(new Vector3(0.1f, 0.1f, 1.0f)), LookMaterials.BarrelRust(), new Vector3(x, 0f, 0f));
            Part(root, "Sack", MeshKit.Box(new Vector3(0.5f, 0.3f, 0.8f)), LookMaterials.CrateGrey(), new Vector3(-0.28f, 0.14f, 0f), Quaternion.Euler(0f, 4f, 0f));
            Part(root, "Sack", MeshKit.Box(new Vector3(0.5f, 0.3f, 0.8f)), LookMaterials.CrateGrey(), new Vector3(0.28f, 0.14f, 0f), Quaternion.Euler(0f, -6f, 0f));
            Part(root, "Sack", MeshKit.Box(new Vector3(0.5f, 0.3f, 0.8f)), LookMaterials.CrateGrey(), new Vector3(0f, 0.44f, 0f), Quaternion.Euler(0f, 90f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.37f, 0f); col.size = new Vector3(1.2f, 0.74f, 1.0f);
        });

        // A generator set: a dark box with a hazard band, an exhaust and a running light.
        public static GameObject Generator() => Prefab("Generator", root =>
        {
            Part(root, "Body", MeshKit.Box(new Vector3(1.8f, 1.2f, 1.0f)), LookMaterials.PanelDark(), Vector3.zero);
            Part(root, "Band", MeshKit.Box(new Vector3(1.84f, 0.24f, 1.04f)), LookMaterials.Hazard(), new Vector3(0f, 0.1f, 0f));
            Part(root, "Frame", MeshKit.Box(new Vector3(1.9f, 0.08f, 1.1f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Lid", MeshKit.Box(new Vector3(1.9f, 0.08f, 1.1f)), LookMaterials.Ink(), new Vector3(0f, 1.2f, 0f));
            for (int i = 0; i < 5; i++) Part(root, "Vent Slat", MeshKit.Box(new Vector3(0.6f, 0.05f, 0.04f)), LookMaterials.Ink(), new Vector3(-0.4f, 0.45f + i * 0.12f, 0.51f));
            Part(root, "Exhaust", MeshKit.Cylinder(0.08f, 0.9f, 8), LookMaterials.RustSteel(), new Vector3(0.7f, 1.2f, -0.3f));
            Part(root, "Exhaust Cap", MeshKit.Cylinder(0.11f, 0.06f, 8), LookMaterials.Ink(), new Vector3(0.7f, 2.1f, -0.3f));
            Part(root, "Running Light", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.04f)), LookMaterials.BeaconRed(), new Vector3(0.6f, 0.95f, 0.51f));
            Part(root, "Panel", MeshKit.Box(new Vector3(0.4f, 0.3f, 0.03f)), LookMaterials.ScreenTeal(), new Vector3(0.3f, 0.7f, 0.51f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.64f, 0f); col.size = new Vector3(1.9f, 1.28f, 1.1f);
        });

        // A toolbox: a red box with an ink handle.
        public static GameObject Toolbox() => Prefab("Toolbox", root =>
        {
            Part(root, "Box", MeshKit.Box(new Vector3(0.7f, 0.32f, 0.36f)), LookMaterials.CrateRed(), Vector3.zero);
            Part(root, "Lid Line", MeshKit.Box(new Vector3(0.72f, 0.03f, 0.38f)), LookMaterials.Ink(), new Vector3(0f, 0.2f, 0f));
            Part(root, "Handle", MeshKit.Box(new Vector3(0.3f, 0.05f, 0.05f)), LookMaterials.Ink(), new Vector3(0f, 0.36f, 0f));
            foreach (float x in new[] { -0.13f, 0.13f }) Part(root, "Handle Post", MeshKit.Box(new Vector3(0.04f, 0.08f, 0.04f)), LookMaterials.Ink(), new Vector3(x, 0.3f, 0f));
        });

        // A stack of tyres.
        public static GameObject TyreStack() => Prefab("TyreStack", root =>
        {
            for (int i = 0; i < 3; i++) Part(root, "Tyre", MeshKit.Ring(0.42f, 0.26f, 18, 8), LookMaterials.Ink(), new Vector3(0f, 0.13f + i * 0.26f, 0f), Quaternion.Euler(0f, i * 30f, 0f));
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.55f; col.height = 0.8f; col.center = new Vector3(0f, 0.4f, 0f);
        });

        // A pole a string of lights hangs from: an ink mast with a base and a cap.
        public static GameObject StringPole() => Prefab("StringPole", root =>
        {
            Part(root, "Post", MeshKit.Box(new Vector3(0.2f, 4.2f, 0.2f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Base", MeshKit.Box(new Vector3(0.5f, 0.16f, 0.5f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Band", MeshKit.Box(new Vector3(0.22f, 0.4f, 0.22f)), LookMaterials.Hazard(), new Vector3(0f, 0.16f, 0f));
            Part(root, "Cap", MeshKit.Box(new Vector3(0.3f, 0.08f, 0.3f)), LookMaterials.Ink(), new Vector3(0f, 4.2f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 2.1f, 0f); col.size = new Vector3(0.5f, 4.2f, 0.5f);
        });

        // A flood tower: a lattice mast with three flood lamps on top, two of them
        // real spot lights, aimed down over the deck (a night harbour's light).
        public static GameObject FloodTower() => Prefab("FloodTower", root =>
        {
            const float h = 9f;
            foreach (float x in new[] { -0.35f, 0.35f }) foreach (float z in new[] { -0.35f, 0.35f })
                Part(root, "Post", MeshKit.Box(new Vector3(0.1f, h, 0.1f)), LookMaterials.Ink(), new Vector3(x, 0f, z));
            for (float y = 1.2f; y < h; y += 1.5f)
            {
                Part(root, "Rung", MeshKit.Box(new Vector3(0.8f, 0.06f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, y, -0.35f));
                Part(root, "Rung", MeshKit.Box(new Vector3(0.8f, 0.06f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, y, 0.35f));
                Part(root, "Rung", MeshKit.Box(new Vector3(0.06f, 0.06f, 0.8f)), LookMaterials.Ink(), new Vector3(-0.35f, y, 0f));
                Part(root, "Rung", MeshKit.Box(new Vector3(0.06f, 0.06f, 0.8f)), LookMaterials.Ink(), new Vector3(0.35f, y, 0f));
                Part(root, "Brace", MeshKit.Box(new Vector3(0.05f, 1.66f, 0.05f)), LookMaterials.Ink(), new Vector3(0f, y - 0.75f, -0.36f), Quaternion.Euler(0f, 0f, 27f));
                Part(root, "Brace", MeshKit.Box(new Vector3(0.05f, 1.66f, 0.05f)), LookMaterials.Ink(), new Vector3(0f, y - 0.75f, 0.36f), Quaternion.Euler(0f, 0f, -27f));
            }
            Part(root, "Base", MeshKit.Box(new Vector3(1.2f, 0.2f, 1.2f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Base Band", MeshKit.Box(new Vector3(1.24f, 0.3f, 1.24f)), LookMaterials.Hazard(), new Vector3(0f, 0.2f, 0f));
            Part(root, "Platform", MeshKit.Box(new Vector3(1.8f, 0.1f, 1.1f)), LookMaterials.Ink(), new Vector3(0f, h, 0f));
            Part(root, "Platform Rail", MeshKit.Box(new Vector3(1.8f, 0.06f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, h + 0.9f, -0.55f));
            foreach (float x in new[] { -0.87f, 0.87f }) Part(root, "Rail Post", MeshKit.Box(new Vector3(0.06f, 0.9f, 0.06f)), LookMaterials.Ink(), new Vector3(x, h + 0.1f, -0.55f));
            foreach (float x in new[] { -0.6f, 0f, 0.6f })
            {
                Part(root, "Head", MeshKit.Box(new Vector3(0.5f, 0.4f, 0.3f)), LookMaterials.Ink(), new Vector3(x, h + 0.3f, 0.35f), Quaternion.Euler(-40f, 0f, 0f));
                Part(root, "Face", MeshKit.Box(new Vector3(0.44f, 0.34f, 0.04f)), LookMaterials.LampWarm(), new Vector3(x, h + 0.3f, 0.35f + 0.16f), Quaternion.Euler(-40f, 0f, 0f));
            }
            foreach (float x in new[] { -0.5f, 0.5f })
            {
                Light light = new GameObject("Flood").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.transform.localPosition = new Vector3(x, h + 0.4f, 0.6f);
                light.transform.localRotation = Quaternion.Euler(50f, x < 0f ? -12f : 12f, 0f);
                light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Spot; light.spotAngle = 75f; light.innerSpotAngle = 40f;
                light.range = 45f; light.intensity = 28f; light.color = new Color(1f, 0.8f, 0.55f); light.shadows = LightShadows.None;
            }
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, h / 2f, 0f); col.size = new Vector3(0.9f, h, 0.9f);
        });

        // A chain-link fence `length` m along X, `height` m tall: posts, a top rail,
        // the mesh between (a ball bounces off it; the sides are open).
        public static GameObject Fence(float length, float height) => Prefab($"Fence{F(length)}x{F(height)}", root =>
        {
            for (float x = -length / 2f; x <= length / 2f + 0.01f; x += 2f)
                Part(root, "Post", MeshKit.Box(new Vector3(0.1f, height, 0.1f)), LookMaterials.Ink(), new Vector3(x, 0f, 0f));
            Part(root, "Top Rail", MeshKit.Box(new Vector3(length + 0.1f, 0.07f, 0.07f)), LookMaterials.Ink(), new Vector3(0f, height - 0.07f, 0f));
            Part(root, "Bottom Rail", MeshKit.Box(new Vector3(length + 0.1f, 0.06f, 0.06f)), LookMaterials.Ink(), new Vector3(0f, 0.06f, 0f));
            Part(root, "Mesh", MeshKit.Box(new Vector3(length, height - 0.12f, 0.01f)), LookMaterials.ChainLink(), new Vector3(0f, 0.08f, 0f));
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, height / 2f, 0f); col.size = new Vector3(length, height, 0.12f);
        });

        // ---- signs ----------------------------------------------------------------------

        // A sign board `w` by `h` m: an ink plate with a thick glowing frame, the
        // text a SignText reading HQSigns by key (a second line under it if a
        // `.sub` key exists). Pivot at the plate's bottom centre; the front is +Z.
        public static GameObject SignBoard(float w, float h) => Prefab($"Sign{F(w)}x{F(h)}", root =>
        {
            Part(root, "Plate", MeshKit.Box(new Vector3(w, h, 0.14f)), LookMaterials.SignBoard(), Vector3.zero);
            Part(root, "Frame Top", MeshKit.Box(new Vector3(w, 0.07f, 0.04f)), LookMaterials.SignGlow(), new Vector3(0f, h - 0.07f, 0.07f));
            Part(root, "Frame Bottom", MeshKit.Box(new Vector3(w, 0.07f, 0.04f)), LookMaterials.SignGlow(), new Vector3(0f, 0f, 0.07f));
            GameObject title = Text(root, "Title", new Vector3(0f, h * 0.60f, 0.08f), h * 0.38f, new Color(1f, 0.82f, 0.50f), TextAnchor.MiddleCenter);
            title.AddComponent<SignText>().Fit(w - 0.4f, h * 0.46f);
            GameObject sub = Text(root, "Sub", new Vector3(0f, h * 0.22f, 0.08f), h * 0.20f, new Color(0.72f, 0.86f, 0.92f), TextAnchor.MiddleCenter);
            sub.AddComponent<SignText>().Fit(w - 0.4f, h * 0.24f);
        });

        // A deck marking `w` by `d` m painted on the plates: a pale border and the
        // text, 2 cm proud of the deck. Pivot at its centre.
        public static GameObject DeckMarking(float w, float d) => Prefab($"Marking{F(w)}x{F(d)}", root =>
        {
            float t = 0.16f;
            Part(root, "Edge N", MeshKit.Box(new Vector3(w, 0.02f, t)), LookMaterials.DeckMarking(), new Vector3(0f, 0f, d / 2f - t / 2f));
            Part(root, "Edge S", MeshKit.Box(new Vector3(w, 0.02f, t)), LookMaterials.DeckMarking(), new Vector3(0f, 0f, -d / 2f + t / 2f));
            Part(root, "Edge E", MeshKit.Box(new Vector3(t, 0.02f, d)), LookMaterials.DeckMarking(), new Vector3(w / 2f - t / 2f, 0f, 0f));
            Part(root, "Edge W", MeshKit.Box(new Vector3(t, 0.02f, d)), LookMaterials.DeckMarking(), new Vector3(-w / 2f + t / 2f, 0f, 0f));
            GameObject text = Text(root, "Text", new Vector3(0f, 0.03f, 0f), Mathf.Min(d * 0.35f, w * 0.12f), new Color(0.82f, 0.80f, 0.74f), TextAnchor.MiddleCenter);
            text.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat on the deck, read from the south
            text.AddComponent<SignText>().Fit(w - 0.6f, d - 0.6f);
        });

        // ---- buildings ------------------------------------------------------------------

        // A booth: `w` wide, 6 m deep, 5 m high, open to the front (+Z is the
        // front; the back wall at -3). Shelves full of goods along the back wall,
        // a counter across the front 1.8 m in, two orange strips under the roof,
        // the sign above the opening. What the rules need inside (stands, the
        // board) is the platform builder's.
        public static GameObject Booth(float w) => Booth(w, true);
        public static GameObject Booth(float w, bool counter) => Prefab($"Booth{F(w)}{(counter ? "" : "Open")}", root =>
        {
            const float depth = 6f, height = 5f, wall = 0.4f;
            Part(root, "Floor", MeshKit.Box(new Vector3(w, 0.16f, depth)), LookMaterials.DeckTile(), Vector3.zero);
            Part(root, "Back Wall", MeshKit.Box(new Vector3(w, height, wall)), LookMaterials.Panel(), new Vector3(0f, 0f, -depth / 2f + wall / 2f));
            Part(root, "Side Wall W", MeshKit.Box(new Vector3(wall, height, depth)), LookMaterials.Panel(), new Vector3(-w / 2f + wall / 2f, 0f, 0f));
            Part(root, "Side Wall E", MeshKit.Box(new Vector3(wall, height, depth)), LookMaterials.Panel(), new Vector3(w / 2f - wall / 2f, 0f, 0f));
            Part(root, "Roof", MeshKit.Box(new Vector3(w + 0.8f, 0.5f, depth + 0.8f)), LookMaterials.PanelDark(), new Vector3(0f, height, 0f));
            Part(root, "Roof Lip", MeshKit.Box(new Vector3(w + 0.8f, 0.25f, 0.1f)), LookMaterials.Hazard(), new Vector3(0f, height + 0.5f, depth / 2f + 0.35f));
            Part(root, "Fascia", MeshKit.Box(new Vector3(w + 0.8f, 1.2f, 0.2f)), LookMaterials.PanelDark(), new Vector3(0f, height - 1.2f, depth / 2f + 0.3f));
            if (counter)
            {
                Part(root, "Counter", MeshKit.Box(new Vector3(w - 2f * wall, 1.1f, 0.9f)), LookMaterials.PanelDark(), new Vector3(0f, 0.16f, depth / 2f - 1.8f));
                Part(root, "Counter Top", MeshKit.Box(new Vector3(w - 2f * wall, 0.1f, 1.0f)), LookMaterials.Ink(), new Vector3(0f, 1.26f, depth / 2f - 1.8f));
                Part(root, "Counter Stripe", MeshKit.Box(new Vector3(w - 2f * wall, 0.28f, 0.03f)), LookMaterials.Hazard(), new Vector3(0f, 0.3f, depth / 2f - 1.34f));
            }
            else Part(root, "Threshold", MeshKit.Box(new Vector3(w - 2f * wall, 0.02f, 0.4f)), LookMaterials.Hazard(), new Vector3(0f, 0.16f, depth / 2f - 0.3f));
            Place(root, Shelf(w - 2f * wall - 0.3f), new Vector3(0f, 0.16f, -depth / 2f + wall + 0.36f)).name = "Shelf";
            Place(root, LightStrip(Mathf.Round(w / 2f) - 1f), new Vector3(-w / 4f, height - 0.02f, depth / 2f - 0.5f)).name = "Strip";
            Place(root, LightStrip(Mathf.Round(w / 2f) - 1f), new Vector3(w / 4f, height - 0.02f, depth / 2f - 0.5f)).name = "Strip";
            Place(root, LightStrip(Mathf.Round(w) - 2f), new Vector3(0f, height - 0.02f, -depth / 2f + 1.2f)).name = "Strip";
            GameObject sign = Place(root, SignBoard(w * 0.72f, 1.1f), new Vector3(0f, height - 1.15f, depth / 2f + 0.47f));
            sign.name = "Sign";
            Place(root, Screen(), new Vector3(w / 2f - wall - 0.9f, 2.0f, -depth / 2f + wall + 0.8f), Quaternion.identity).name = "Screen";
            Place(root, Shelf(3.2f), new Vector3(-w / 2f + wall + 0.36f, 0.16f, 0.4f), Quaternion.Euler(0f, 90f, 0f)).name = "Side Shelf";
            Place(root, ToolBoard(), new Vector3(w / 2f - wall - 0.05f, 2.2f, 0.6f), Quaternion.Euler(0f, -90f, 0f)).name = "Tool Board";
            if (counter)
            {
                Place(root, SmallCrate("Grey"), new Vector3(-w / 2f + 1.6f, 1.36f, depth / 2f - 1.8f), Quaternion.Euler(0f, 12f, 0f)).name = "Counter Crate";
                Place(root, SmallCrate("Yellow"), new Vector3(w / 2f - 1.5f, 1.36f, depth / 2f - 1.9f), Quaternion.Euler(0f, -20f, 0f)).name = "Counter Crate";
                Part(root, "Counter Lamp", MeshKit.Box(new Vector3(0.2f, 0.3f, 0.2f)), LookMaterials.LampWarm(), new Vector3(0f, 1.36f, depth / 2f - 2.1f));
                Part(root, "Counter Lamp Hood", MeshKit.Box(new Vector3(0.3f, 0.06f, 0.3f)), LookMaterials.Ink(), new Vector3(0f, 1.66f, depth / 2f - 2.1f));
            }
            // Colliders: the walls, the counter and the floor.
            Box(root, new Vector3(0f, height / 2f, -depth / 2f + wall / 2f), new Vector3(w, height, wall));
            Box(root, new Vector3(-w / 2f + wall / 2f, height / 2f, 0f), new Vector3(wall, height, depth));
            Box(root, new Vector3(w / 2f - wall / 2f, height / 2f, 0f), new Vector3(wall, height, depth));
            Box(root, new Vector3(0f, 0.08f, 0f), new Vector3(w, 0.16f, depth));
            if (counter) Box(root, new Vector3(0f, 0.7f, depth / 2f - 1.8f), new Vector3(w - 2f * wall, 1.2f, 1.0f));
            Box(root, new Vector3(0f, height + 0.25f, 0f), new Vector3(w + 0.8f, 0.5f, depth + 0.8f));
        });

        // A flight of stairs: `rise` m up over `run` m along +Z, `w` wide, steps
        // 0.2 m high, ink stringers, hazard nosings, thick handrails. The
        // collider is a ramp so the capsule glides; the steps show.
        public static GameObject Stairs(float rise, float run, float w) => Prefab($"Stairs{F(rise)}x{F(run)}x{F(w)}", root =>
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(rise / 0.2f));
            float stepH = rise / steps, stepD = run / steps;
            for (int i = 0; i < steps; i++)
            {
                Part(root, "Step", MeshKit.Box(new Vector3(w, stepH, stepD)), LookMaterials.PanelDark(), new Vector3(0f, i * stepH, i * stepD + stepD / 2f));
                Part(root, "Nosing", MeshKit.Box(new Vector3(w, 0.03f, 0.08f)), LookMaterials.Hazard(), new Vector3(0f, (i + 1) * stepH - 0.03f, i * stepD + 0.04f));
            }
            float length = Mathf.Sqrt(run * run + rise * rise);
            float pitch = -Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            foreach (float x in new[] { -w / 2f, w / 2f })
            {
                Part(root, "Stringer", MeshKit.Box(new Vector3(0.18f, 0.45f, length)), LookMaterials.Ink(), new Vector3(x, rise / 2f - 0.4f, run / 2f), Quaternion.Euler(pitch, 0f, 0f));
                Part(root, "Handrail", MeshKit.Box(new Vector3(0.1f, 0.1f, length)), LookMaterials.Ink(), new Vector3(x, rise / 2f + 1.0f, run / 2f), Quaternion.Euler(pitch, 0f, 0f));
                for (float s = 0.5f; s < length; s += 1.5f)
                {
                    float t = s / length;
                    Part(root, "Baluster", MeshKit.Box(new Vector3(0.08f, 1.0f, 0.08f)), LookMaterials.Ink(), new Vector3(x, t * rise, t * run));
                }
            }
            GameObject ramp = new("Ramp");
            ramp.transform.SetParent(root.transform, false);
            ramp.transform.localPosition = new Vector3(0f, rise / 2f, run / 2f);
            ramp.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            BoxCollider col = ramp.AddComponent<BoxCollider>();
            col.size = new Vector3(w, 0.2f, length); col.center = new Vector3(0f, -0.1f + stepH * 0.5f, 0f);
        });

        // A basketball hoop: a fat post, a backboard, the ring at 3.05 m, a net of
        // eight strings, a trigger under the ring for the score (HoopScore is the
        // platform builder's, with the scoreboard).
        public static GameObject Hoop() => Prefab("Hoop", root =>
        {
            Part(root, "Post", MeshKit.Box(new Vector3(0.24f, 3.7f, 0.24f)), LookMaterials.Ink(), new Vector3(0f, 0f, -1.0f));
            Part(root, "Base", MeshKit.Box(new Vector3(0.8f, 0.2f, 0.8f)), LookMaterials.Ink(), new Vector3(0f, 0f, -1.0f));
            Part(root, "Arm", MeshKit.Box(new Vector3(0.14f, 0.14f, 1.0f)), LookMaterials.Ink(), new Vector3(0f, 3.5f, -0.5f));
            Part(root, "Backboard", MeshKit.Box(new Vector3(1.8f, 1.05f, 0.08f)), LookMaterials.Backboard(), new Vector3(0f, 2.9f, 0f));
            Part(root, "Backboard Frame", MeshKit.Box(new Vector3(1.9f, 0.08f, 0.1f)), LookMaterials.Ink(), new Vector3(0f, 3.95f, 0f));
            Part(root, "Backboard Box", MeshKit.Box(new Vector3(0.6f, 0.03f, 0.1f)), LookMaterials.HoopOrange(), new Vector3(0f, 3.05f, 0.02f));
            GameObject ring = new("Ring");
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 3.05f, 0.27f);
            ring.AddComponent<MeshFilter>().sharedMesh = MeshKit.Ring(0.225f, 0.04f);
            ring.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.HoopOrange();
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                GameObject s = Part(root, "String", MeshKit.Box(new Vector3(0.016f, 0.42f, 0.016f), 0.42f), LookMaterials.Net(), new Vector3(Mathf.Cos(a) * 0.2f, 3.05f, 0.27f + Mathf.Sin(a) * 0.2f));
                s.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 12f, 0f, -Mathf.Cos(a) * 12f);
            }
            Box(root, new Vector3(0f, 1.85f, -1.0f), new Vector3(0.24f, 3.7f, 0.24f));
            Box(root, new Vector3(0f, 3.42f, 0f), new Vector3(1.8f, 1.05f, 0.08f));
            GameObject trigger = new("Score Trigger");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.localPosition = new Vector3(0f, 2.85f, 0.27f);
            BoxCollider t = trigger.AddComponent<BoxCollider>();
            t.isTrigger = true; t.size = new Vector3(0.36f, 0.16f, 0.36f);
            // The count on the backboard, above the box (HoopScore keeps it current).
            GameObject score = Text(root, "Score", new Vector3(0f, 3.75f, 0.06f), 0.16f, new Color(1f, 0.55f, 0.15f), TextAnchor.MiddleCenter);
            score.GetComponent<TextMesh>().text = "BASKETS 0";
            trigger.AddComponent<HoopScore>().Configure(score.GetComponent<TextMesh>());
        });

        // The beacon: a short mast in a cage with a red lamp that pulses.
        public static GameObject BeaconMast() => Prefab("BeaconMast", root =>
        {
            Part(root, "Mast", MeshKit.Cylinder(0.1f, 1.8f, 8), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Base", MeshKit.Box(new Vector3(0.6f, 0.2f, 0.6f)), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Cage", MeshKit.Cylinder(0.32f, 0.7f, 10, false), LookMaterials.Ink(), new Vector3(0f, 1.8f, 0f));
            GameObject lamp = Part(root, "Lamp", MeshKit.Cylinder(0.24f, 0.5f, 12), LookMaterials.BeaconRed(), new Vector3(0f, 1.9f, 0f));
            Part(root, "Cap", MeshKit.Cylinder(0.36f, 0.1f, 10), LookMaterials.Ink(), new Vector3(0f, 2.5f, 0f));
            Light light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            light.lightmapBakeType = LightmapBakeType.Realtime; light.type = LightType.Point; light.range = 40f; light.intensity = 0f; light.color = new Color(1f, 0.25f, 0.15f); light.shadows = LightShadows.None;
            root.AddComponent<Beacon>().Configure(lamp.GetComponent<Renderer>(), light, 2.4f, 0f);
        });

        // The flagpole with the company's black flag and its skull.
        public static GameObject FlagPole() => Prefab("FlagPole", root =>
        {
            Part(root, "Pole", MeshKit.Cylinder(0.07f, 5f, 8), LookMaterials.Ink(), Vector3.zero);
            Part(root, "Ball", MeshKit.Cylinder(0.14f, 0.14f, 8), LookMaterials.Ink(), new Vector3(0f, 5f, 0f));
            Part(root, "Flag", MeshKit.Box(new Vector3(2.0f, 1.3f, 0.03f)), LookMaterials.Flag(), new Vector3(1.05f, 3.6f, 0f));
            GameObject mark = Part(root, "Mark", MeshKit.Box(new Vector3(0.9f, 0.9f, 0.035f)), LookMaterials.Skull(), new Vector3(1.05f, 3.8f, 0f));
        });

        // A windsock on a pole: red and white, held out by the wind.
        public static GameObject Windsock() => Prefab("Windsock", root =>
        {
            Part(root, "Pole", MeshKit.Cylinder(0.05f, 3.2f, 8), LookMaterials.Ink(), Vector3.zero);
            Material red = LookMaterials.Fender(), white = LookMaterials.FenderBand();
            for (int i = 0; i < 4; i++)
                Part(root, "Sock", MeshKit.Cylinder(0.24f - i * 0.03f, 0.3f, 10, false), i % 2 == 0 ? red : white, new Vector3(0.3f * i, 3.05f, 0f), Quaternion.Euler(0f, 0f, -90f));
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
                return PrefabUtility.SaveAsPrefabAsset(root, path);
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
            mesh.characterSize = lineHeight * 0.1f;
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
