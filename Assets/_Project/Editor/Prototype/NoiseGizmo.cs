using System.Collections.Generic;
using SunkCost.Noise;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Every noise event as a fading wire disc in the Scene view — the radius in
    // metres, the colour by kind — so radii can be tuned by eye while playing as
    // the host (Notion: "Noise: an editor gizmo that draws the radius"). Toggled
    // by the menu; a listener on the server's NoiseSystem while on, re-registered
    // after every Clear (a server restart).
    [InitializeOnLoad]
    public static class NoiseGizmo
    {
        private const string MenuPath = "Sunk Cost/Prototype/Noise gizmo (Scene view)";
        private const string PrefKey = "SunkCost.NoiseGizmo";
        private const float LingerSeconds = 1.5f;

        private sealed class Listener : INoiseListener
        {
            public void OnNoise(in NoiseEvent noise) => Recent.Add(new Seen { Event = noise, At = Time.unscaledTime });
        }
        private struct Seen { public NoiseEvent Event; public float At; }

        private static readonly List<Seen> Recent = new();
        private static readonly Listener listener = new();
        public static bool On { get; private set; }

        static NoiseGizmo()
        {
            On = EditorPrefs.GetBool(PrefKey, false);
            NoiseSystem.DebugDraw = On;
            SceneView.duringSceneGui += Draw;
            EditorApplication.update += Keep;
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            On = !On;
            EditorPrefs.SetBool(PrefKey, On);
            NoiseSystem.DebugDraw = On;
            if (!On) { NoiseSystem.Unregister(listener); Recent.Clear(); }
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate() { Menu.SetChecked(MenuPath, On); return true; }

        private static void Keep()
        {
            if (!On || !EditorApplication.isPlaying) return;
            NoiseSystem.Register(listener); // Register ignores a duplicate; a Clear drops it
            if (Recent.Count > 0) SceneView.RepaintAll();
        }

        private static void Draw(SceneView view)
        {
            if (!On || Recent.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                float age = now - Recent[i].At;
                if (age > LingerSeconds) { Recent.RemoveAt(i); continue; }
                NoiseEvent e = Recent[i].Event;
                Color colour = Colour(e.Kind);
                colour.a = 1f - age / LingerSeconds;
                Handles.color = colour;
                Handles.DrawWireDisc(e.Position, Vector3.up, e.Radius);
                Handles.DrawWireDisc(e.Position, Vector3.up, 0.15f);
                Handles.Label(e.Position + Vector3.up * 0.3f, $"{e.Kind} {e.Radius:0} m");
            }
        }

        private static Color Colour(NoiseKind kind) => kind switch
        {
            NoiseKind.Footstep => new Color(0.4f, 0.9f, 1f),
            NoiseKind.Sprint => new Color(1f, 0.8f, 0.2f),
            NoiseKind.Elevator => new Color(1f, 0.3f, 0.3f),
            NoiseKind.Impact => new Color(1f, 0.5f, 0.9f),
            _ => Color.white
        };
    }
}
