using UnityEngine;

namespace SunkCost.Audio
{
    [CreateAssetMenu(menuName = "Sunk Cost/Voice settings")]
    public sealed class VoiceSettings : ScriptableObject
    {
        [Min(0)] public float FullVolumeMetres = 2;
        [Min(1)] public float SilentMetres = 20;
        [Min(0)] public float RelayMarginMetres = 2;
        public float Gain(float distance)
        {
            float t = Mathf.Clamp01((distance - FullVolumeMetres) / Mathf.Max(.1f, SilentMetres - FullVolumeMetres));
            return 1 - t * t * (3 - 2 * t);
        }
    }
}
