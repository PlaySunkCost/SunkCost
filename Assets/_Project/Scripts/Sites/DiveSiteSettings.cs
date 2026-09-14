using UnityEngine;

namespace SunkCost.Sites
{
    [CreateAssetMenu(fileName = "DiveSiteSettings", menuName = "Sunk Cost/Dive Site Settings")]
    public sealed class DiveSiteSettings : ScriptableObject
    {
        [SerializeField] private float shaftDepthMeters = 45f;
        [SerializeField] private float seafloorSizeMeters = 150f;
        [SerializeField] private Vector3 wreckOffset = new(35f, 0f, 0f);
        [SerializeField] private float headlampIntensity = 15f;
        [SerializeField] private float headlampRange = 25f;
        [SerializeField] private float headlampSpotAngle = 35f;
        [SerializeField] private float fogDensity = 0.09f;
        [SerializeField] private Color fogColor = new(0.02f, 0.05f, 0.045f);
        [SerializeField] private Color ambientColor = new(0.015f, 0.03f, 0.028f);
        [SerializeField] private float surfaceLightIntensity = 2f;

        public float ShaftDepthMeters => shaftDepthMeters;
        public float SeafloorSizeMeters => seafloorSizeMeters;
        public Vector3 WreckOffset => wreckOffset;
        public float HeadlampIntensity => headlampIntensity;
        public float HeadlampRange => headlampRange;
        public float HeadlampSpotAngle => headlampSpotAngle;
        public float FogDensity => fogDensity;
        public Color FogColor => fogColor;
        public Color AmbientColor => ambientColor;
        public float SurfaceLightIntensity => surfaceLightIntensity;
    }
}
