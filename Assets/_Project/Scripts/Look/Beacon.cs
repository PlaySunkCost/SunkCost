using UnityEngine;

namespace SunkCost.Look
{
    // A pulsing lamp (the tower's beacon, the pier's edge markers): the emission
    // and an optional light swell and fade on a slow cycle. Presentation only;
    // every peer runs its own clock, nobody cares if they differ. The emission
    // pulses in the editor too (a property block, never saved); the light does not.
    [ExecuteAlways]
    public sealed class Beacon : MonoBehaviour
    {
        [SerializeField] private Renderer lamp;
        [SerializeField] private Light glow;
        [Tooltip("Seconds per pulse.")]
        [SerializeField] private float period = 2.4f;
        [Tooltip("The emission at the bottom and the top of the pulse, as a factor of the material's.")]
        [SerializeField] private float low = 0.15f, high = 1.6f;
        [SerializeField] private float lightIntensity = 4f;
        [SerializeField] private float phase;

        private MaterialPropertyBlock block;
        private Color baseEmission = Color.black;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public void Configure(Renderer lampRenderer, Light light, float seconds, float phaseOffset)
        {
            lamp = lampRenderer; glow = light; period = seconds; phase = phaseOffset;
        }

        private void OnEnable()
        {
            block ??= new MaterialPropertyBlock();
            if (lamp != null && lamp.sharedMaterial != null && lamp.sharedMaterial.HasProperty(EmissionColor)) baseEmission = lamp.sharedMaterial.GetColor(EmissionColor);
        }

        private void Update()
        {
            float t = Application.isPlaying ? Time.time : (float)UnityEditor_Time();
            float k = Mathf.Max(period, 0.05f);
            // A sharp swell and a slow fade, like a rotating lens sweeping past.
            float u = Mathf.Repeat(t / k + phase, 1f);
            float pulse = Mathf.Pow(1f - u, 2.2f);
            float f = Mathf.Lerp(low, high, pulse);
            if (lamp != null)
            {
                lamp.GetPropertyBlock(block);
                block.SetColor(EmissionColor, baseEmission * f);
                lamp.SetPropertyBlock(block);
            }
            // The light only in Play Mode: written in the editor, the saved intensity was
            // whatever the pulse stood at when the scene was saved (0 in the ship audit, SHIP-084).
            if (glow != null && Application.isPlaying) glow.intensity = lightIntensity * pulse;
        }

        private static double UnityEditor_Time()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.time;
#endif
        }
    }
}
