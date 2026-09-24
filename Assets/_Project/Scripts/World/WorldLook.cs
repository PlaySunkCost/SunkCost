using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // A world scene's lighting look — its RenderSettings (ambient, fog, the
    // environment the lit shaders read, the sky and the sun) as a component on a
    // root object of the scene, written by the builders (WorldLookSetup). Unity
    // applies only the active scene's RenderSettings, so a camera showing another
    // world — the deck TV on a diver, a dead diver's eyes on the deck — would
    // render the site with the ship's daylight. Such a camera swaps these in for
    // its render (Begin) and puts the active scene's back after (Restore). The
    // worlds' lights are kept apart by WorldLightLayers. Data only; nothing
    // replicated.
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
            // The rest came with SHIP-037 (23 September 2026): swapping the ambient
            // colour alone left the ship's ambient probe in the TV's render of the
            // site (20-40x the site's), because the lit shaders read the probe, not
            // the colour. Environment is false in a look written before then.
            public bool Environment;
            public Color AmbientEquator, AmbientGround;
            public float AmbientIntensity;
            public float[] AmbientProbe; // the 27 coefficients of the SphericalHarmonicsL2
            public Material Skybox;
            public DefaultReflectionMode ReflectionMode;
            public Texture CustomReflection;
            public float ReflectionIntensity;
            public Light Sun; // the world's main light (URP takes RenderSettings.sun first)

            public static Snapshot Capture() => new()
            {
                AmbientMode = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight,
                Fog = RenderSettings.fog,
                FogMode = RenderSettings.fogMode,
                FogColor = RenderSettings.fogColor,
                FogDensity = RenderSettings.fogDensity,
                FogStartDistance = RenderSettings.fogStartDistance,
                FogEndDistance = RenderSettings.fogEndDistance,
                Environment = true,
                AmbientEquator = RenderSettings.ambientEquatorColor,
                AmbientGround = RenderSettings.ambientGroundColor,
                AmbientIntensity = RenderSettings.ambientIntensity,
                AmbientProbe = ToArray(RenderSettings.ambientProbe),
                Skybox = RenderSettings.skybox,
                ReflectionMode = RenderSettings.defaultReflectionMode,
                CustomReflection = RenderSettings.customReflectionTexture,
                ReflectionIntensity = RenderSettings.reflectionIntensity,
                Sun = RenderSettings.sun
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
                // Unity does not recompute the probe when the colour changes at run time.
                if (AmbientProbe != null && AmbientProbe.Length == 27) RenderSettings.ambientProbe = FromArray(AmbientProbe);
                else if (AmbientMode == AmbientMode.Flat) RenderSettings.ambientProbe = FlatProbe(AmbientLight);
                if (!Environment) return;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                RenderSettings.ambientIntensity = AmbientIntensity;
                RenderSettings.skybox = Skybox;
                RenderSettings.defaultReflectionMode = ReflectionMode;
                RenderSettings.customReflectionTexture = CustomReflection;
                RenderSettings.reflectionIntensity = ReflectionIntensity;
                if (Sun != null) RenderSettings.sun = Sun;
            }
        }

        [SerializeField] private Snapshot look;
        [Tooltip("Things that move (players, items, monsters, the car) deeper than this height take the deep rendering layer: out of the surface light's reach. -Infinity: no deep water in this world.")]
        [SerializeField] private float deepBelowY = float.NegativeInfinity;

        public Snapshot Look => look;
        public void Set(Snapshot value) => look = value;
        public float DeepBelowY => deepBelowY;
        public void SetDeepBelowY(float value) => deepBelowY = value;

        // The world's main light: the one the look names, else its brightest
        // directional light (a look written before the sun was carried).
        public Light Sun
        {
            get
            {
                if (look.Sun != null) return look.Sun;
                if (fallbackSun == null) fallbackSun = BrightestDirectional(gameObject.scene);
                return fallbackSun;
            }
        }
        private Light fallbackSun;

        // Where the world's things stand (WorldLightLayers, filled when it stamps the scene).
        [System.NonSerialized] internal Bounds Area;
        [System.NonSerialized] internal bool HasArea;

        private void OnEnable() { if (Application.isPlaying) WorldLightLayers.StampScene(this); }

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
            Snapshot wanted = other.look;
            if (wanted.Sun == null) wanted.Sun = other.Sun;
            wanted.Apply();
            return previous;
        }

        public static void Restore(Snapshot? previous)
        {
            if (previous.HasValue) previous.Value.Apply();
        }

        public static Light BrightestDirectional(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            Light best = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional && (best == null || light.intensity > best.intensity)) best = light;
            return best;
        }

        // A flat ambient colour as the probe Unity bakes for it: the colour, linear, in the constant band.
        public static SphericalHarmonicsL2 FlatProbe(Color ambient)
        {
            SphericalHarmonicsL2 probe = default;
            probe.AddAmbientLight(ambient.linear);
            return probe;
        }

        public static float[] ToArray(SphericalHarmonicsL2 probe)
        {
            var values = new float[27];
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) values[c * 9 + k] = probe[c, k];
            return values;
        }

        public static SphericalHarmonicsL2 FromArray(float[] values)
        {
            SphericalHarmonicsL2 probe = default;
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) probe[c, k] = values[c * 9 + k];
            return probe;
        }
    }
}
