using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // A world scene's lighting look — its RenderSettings (ambient and fog) as a
    // component on a root object of the scene, written by the builders
    // (WorldLookSetup). Unity applies only the active scene's RenderSettings, so a
    // camera showing another world — the deck TV on a diver, a dead diver's eyes
    // on the deck — would render the site with the ship's daylight fog. Such a
    // camera swaps these in for its render (Apply) and puts the active scene's
    // back after (Restore). Data only; nothing replicated.
    public sealed class WorldLook : MonoBehaviour
    {
        [System.Serializable]
        public struct Snapshot
        {
            public AmbientMode AmbientMode;
            public Color AmbientLight;
            public bool Fog;
            public FogMode FogMode;
            public Color FogColor;
            public float FogDensity;
            public float FogStartDistance;
            public float FogEndDistance;

            public static Snapshot Capture() => new()
            {
                AmbientMode = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight,
                Fog = RenderSettings.fog,
                FogMode = RenderSettings.fogMode,
                FogColor = RenderSettings.fogColor,
                FogDensity = RenderSettings.fogDensity,
                FogStartDistance = RenderSettings.fogStartDistance,
                FogEndDistance = RenderSettings.fogEndDistance
            };

            public void Apply()
            {
                RenderSettings.ambientMode = AmbientMode;
                RenderSettings.ambientLight = AmbientLight;
                RenderSettings.fog = Fog;
                RenderSettings.fogMode = FogMode;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogDensity = FogDensity;
                RenderSettings.fogStartDistance = FogStartDistance;
                RenderSettings.fogEndDistance = FogEndDistance;
            }
        }

        [SerializeField] private Snapshot look;

        public Snapshot Look => look;
        public void Set(Snapshot value) => look = value;

        public static WorldLook InScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                WorldLook found = root.GetComponent<WorldLook>();
                if (found != null) return found;
            }
            return null;
        }

        // The look of `scene` for one render: null when the scene is the active one
        // (nothing to swap) or carries no WorldLook. Pair with Restore.
        public static Snapshot? Begin(Scene scene)
        {
            if (!scene.IsValid() || scene == SceneManager.GetActiveScene()) return null;
            WorldLook other = InScene(scene);
            if (other == null) return null;
            Snapshot previous = Snapshot.Capture();
            other.look.Apply();
            return previous;
        }

        public static void Restore(Snapshot? previous)
        {
            if (previous.HasValue) previous.Value.Apply();
        }
    }
}
