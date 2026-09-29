using UnityEngine;

namespace SunkCost.Diving
{
    // The sea standing in the shaft tube (the new elevator, 28 September 2026). Below sea
    // level the tube is open to the sea at its foot, so its "WaterSurface" disc lies at sea
    // level across the whole tube. While the car's span crosses that plane the car holds
    // its own water surface at the same height (CabinWater), and the disc would cut through
    // the car: then this shows the "WaterSurface Ring" (the tube's water outside the car's
    // glass) instead, so every point shows exactly one surface. Derived from the car's
    // transform every frame on every peer; nothing synced, nothing accumulated.
    [DefaultExecutionOrder(100)]
    public sealed class TubeWaterSurface : MonoBehaviour
    {
        [SerializeField] private ElevatorController car;
        [SerializeField] private Renderer disc;
        [SerializeField] private Renderer ring;
        // The ring shows while sea level lies between the car's underside and its roof top,
        // with these margins (the model's base ring and roof reach past the floor and roof).
        [SerializeField] private float belowRootMeters = 0.30f;
        [SerializeField] private float aboveSpanMeters = 0.20f;

        public bool RingShown { get; private set; }

        public void Configure(ElevatorController carController, Renderer discRenderer, Renderer ringRenderer)
        {
            car = carController;
            disc = discRenderer;
            ring = ringRenderer;
        }

        public static bool CarAtSurface(float seaLevelY, float carRootY, float span, float below, float above) =>
            carRootY - below < seaLevelY && seaLevelY < carRootY + span + above;

        private void LateUpdate()
        {
            if (car == null) return;
            RingShown = CarAtSurface(car.SeaLevelY, car.transform.position.y, car.SpanMeters, belowRootMeters, aboveSpanMeters);
            if (disc != null && disc.enabled == RingShown) disc.enabled = !RingShown;
            if (ring != null && ring.enabled != RingShown) ring.enabled = RingShown;
        }
    }
}
