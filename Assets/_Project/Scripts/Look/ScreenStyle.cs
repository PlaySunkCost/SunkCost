using System;
using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Look
{
    // The ship's one screen style (ship audit SHIP-044/059, 23 September 2026):
    // every display on the ship - the monitor, the crew screen, the TV's caption,
    // the storage readout, the cabin sign, the buttons - draws from this palette
    // and these three text sizes, so the stations read as one design. Dark glass,
    // one accent (the visor's cyan), near-white values, dim hints; amber, green and
    // red only where they mean something (under quota, met, on air or irreversible).
    public static class ScreenStyle
    {
        public static readonly Color Back = new(0.020f, 0.034f, 0.044f);   // a screen's glass
        public static readonly Color Track = new(0.07f, 0.11f, 0.13f);     // an empty bar, an idle chip
        public static readonly Color Accent = new(0.36f, 0.86f, 0.96f);    // titles, rules, destinations
        public static readonly Color Text = new(0.88f, 0.94f, 0.97f);      // values and states
        public static readonly Color Dim = new(0.50f, 0.62f, 0.68f);       // hints and small print
        public static readonly Color Warn = new(1.00f, 0.70f, 0.22f);      // under quota, a refusal
        public static readonly Color Good = new(0.42f, 0.92f, 0.52f);      // quota met
        public static readonly Color Danger = new(0.92f, 0.20f, 0.16f);    // on air, End day

        // Line heights as fractions of a display's height: a title over a value
        // over a hint. A display that is wide and short (a plate) caps them by width.
        public const float TitleLine = 0.13f;
        public const float ValueLine = 0.30f;
        public const float HintLine = 0.10f;
        public const float Margin = 0.07f; // of the smaller side, kept clear all round

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static string Tint(string text, Color c) => $"<color={Hex(c)}>{text}</color>";

        // A flat, unlit colour for the display's quads (the glass, a bar, a chip).
        // In the editor the builder supplies saved materials (EditorFlat), so the
        // prefab keeps them; in play the colour is a cached runtime material.
        public static Func<Color, Material> EditorFlat;
        private static readonly Dictionary<Color, Material> RuntimeFlats = new();
        public static Material Flat(Color c)
        {
            if (!Application.isPlaying) return EditorFlat?.Invoke(c);
            if (RuntimeFlats.TryGetValue(c, out Material m) && m != null) return m;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) return null;
            m = new Material(unlit) { name = "ScreenFlat " + Hex(c), hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", c);
            RuntimeFlats[c] = m;
            return m;
        }

        public static void Paint(Renderer renderer, Color c)
        {
            if (renderer == null) return;
            Material m = Flat(c);
            if (m != null && renderer.sharedMaterial != m) renderer.sharedMaterial = m;
        }

        // A child found by name, or made: displays are built once by the ship's
        // builder (ShipScreens) and rebuilt the same way at runtime if missing.
        public static Transform Child(Transform parent, string name, out bool made)
        {
            Transform t = parent.Find(name);
            made = t == null;
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // A line of display text, set up as every sign's text (PropBuilder.Text):
        // bold, 64 px, turned to read from the display's +Z, drawn depth-tested with
        // `textMaterial` (the ship's DepthText material, taken from a label already on it).
        public static TextMesh Line(Transform parent, string name, Vector3 localPosition, float lineHeight, Color colour, TextAnchor anchor, Material textMaterial)
        {
            Transform t = Child(parent, name, out bool made);
            TextMesh mesh = t.GetComponent<TextMesh>();
            if (mesh == null) mesh = t.gameObject.AddComponent<TextMesh>();
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(0f, 180f, 0f);
            t.localScale = Vector3.one;
            mesh.fontSize = 64;
            mesh.fontStyle = FontStyle.Bold;
            mesh.characterSize = lineHeight * 0.1f;
            mesh.anchor = anchor;
            mesh.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft || anchor == TextAnchor.LowerLeft ? TextAlignment.Left
                : anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight || anchor == TextAnchor.LowerRight ? TextAlignment.Right : TextAlignment.Center;
            mesh.color = colour;
            mesh.richText = true;
            if (made || mesh.font == null) mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            DepthText depth = t.GetComponent<DepthText>();
            if (depth == null) depth = t.gameObject.AddComponent<DepthText>();
            if (textMaterial != null) depth.Configure(textMaterial);
            return mesh;
        }

        // A flat quad on the display, facing its +Z (a quad is seen from its -Z),
        // `w` by `h` m centred at `centre`; no collider (the crosshair must not find it).
        private static Mesh quad;
        public static Renderer Quad(Transform parent, string name, Vector3 centre, float w, float h, Color colour)
        {
            Transform t = Child(parent, name, out _);
            t.localPosition = centre;
            t.localRotation = Quaternion.Euler(0f, 180f, 0f);
            t.localScale = new Vector3(Mathf.Max(w, 0.0001f), Mathf.Max(h, 0.0001f), 1f);
            MeshFilter filter = t.GetComponent<MeshFilter>();
            if (filter == null) filter = t.gameObject.AddComponent<MeshFilter>();
            if (quad == null) quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            filter.sharedMesh = quad;
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Paint(renderer, colour);
            return renderer;
        }

        // A bar's fill: `fraction` of the track from its left edge.
        public static void SetBar(Transform fill, Vector3 trackCentre, float trackWidth, float height, float fraction)
        {
            if (fill == null) return;
            float w = Mathf.Max(trackWidth * Mathf.Clamp01(fraction), 0.0001f);
            fill.localPosition = new Vector3(trackCentre.x - trackWidth / 2f + w / 2f, trackCentre.y, trackCentre.z);
            fill.localScale = new Vector3(w, height, 1f);
            Renderer r = fill.GetComponent<Renderer>();
            if (r != null) r.enabled = fraction > 0.001f;
        }

        // The text material a display's new lines share: the depth-tested one its
        // existing label already wears.
        public static Material TextMaterialOf(Component label)
        {
            Renderer r = label != null ? label.GetComponent<Renderer>() : null;
            return r != null ? r.sharedMaterial : null;
        }
    }
}
