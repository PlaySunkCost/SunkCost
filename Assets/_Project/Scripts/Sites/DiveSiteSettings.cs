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
        [SerializeField] private float carDiameterMeters = 5f;
        [SerializeField] private float carInteriorHeightMeters = 3.5f;
        [SerializeField] private float doorSealSeconds = 1.5f;
        // The glass tube and the water (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md,
        // Dan 15 September 2026). The tube holds water only below sea level; the
        // car slows while its floor-to-roof span crosses that surface. Travel time
        // is derived from the speeds and the depth, no longer a setting.
        [Tooltip("World height of the water surface inside the tube. The ship's deck is 4.5 m above the sea (ShipStubBuilder.SeaLevelY), so the top anchor at 0 puts it at -4.5.")]
        [SerializeField] private float seaLevelY = -4.5f;
        [Tooltip("Metres between the car's glass and the tube's glass.")]
        [SerializeField] private float tubeClearanceMeters = 0.5f;
        [Tooltip("Metres between the tube's ring ribs (a rider reads speed off them).")]
        [SerializeField] private float tubeRibSpacingMeters = 5f;
        [Tooltip("Car speed outside the surface band, m/s.")]
        [SerializeField] private float travelSpeedMetersPerSecond = 3f;
        [Tooltip("Car speed while its floor-to-roof span crosses the water surface, m/s (the cabin floods or drains over span / this).")]
        [SerializeField] private float surfaceCrossingSpeedMetersPerSecond = 1f;

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
        public float CarDiameterMeters => carDiameterMeters;
        public float CarInteriorHeightMeters => carInteriorHeightMeters;
        public float DoorSealSeconds => doorSealSeconds;
        public float SeaLevelY => seaLevelY;
        public float TubeClearanceMeters => tubeClearanceMeters;
        public float TubeRadiusMeters => carDiameterMeters / 2f + tubeClearanceMeters;
        public float TubeRibSpacingMeters => tubeRibSpacingMeters;
        public float TravelSpeedMetersPerSecond => travelSpeedMetersPerSecond;
        public float SurfaceCrossingSpeedMetersPerSecond => surfaceCrossingSpeedMetersPerSecond;

        // The car's motion profile for a shaft from the top anchor at topY down to
        // the seafloor: the derived travel time is ElevatorMath.TravelSeconds(this).
        public SunkCost.Diving.ElevatorMath.Profile ElevatorProfile(float topY) =>
            SunkCost.Diving.ElevatorMath.Profile.Of(shaftDepthMeters, topY - seaLevelY, carInteriorHeightMeters, travelSpeedMetersPerSecond, surfaceCrossingSpeedMetersPerSecond);
        public float ElevatorTravelSecondsOneWay(float topY) => SunkCost.Diving.ElevatorMath.TravelSeconds(ElevatorProfile(topY));

        public bool IsValid =>
            shaftDepthMeters > 0f && carDiameterMeters > 0f && carInteriorHeightMeters > 0f && doorSealSeconds >= 0f &&
            float.IsFinite(seaLevelY) && tubeClearanceMeters > 0f && tubeRibSpacingMeters > 0f &&
            travelSpeedMetersPerSecond > 0f && surfaceCrossingSpeedMetersPerSecond > 0f && surfaceCrossingSpeedMetersPerSecond <= travelSpeedMetersPerSecond;
    }
}
