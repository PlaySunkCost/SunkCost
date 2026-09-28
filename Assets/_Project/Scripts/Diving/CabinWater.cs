using UnityEngine;

namespace SunkCost.Diving
{
    // The water inside the car (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md section 6; the new
    // elevator, 28 September 2026). The car is a sealed glass car: its roof nozzles pour
    // as it sinks through the surface and its floor grilles drain as it rises through it,
    // and its water stands where the sea outside stands while its span crosses the
    // surface. So the level is still exactly ElevatorMath.WaterLevelInCar: sea level
    // minus the car root, clamped to the cabin. That is the one water truth: the
    // submersion, the air tank's refusal and the underwater grade read sea level and
    // agree with it by construction (ElevatorMath.CarWaterSurfaceY). Computed on every
    // peer from the car's own transform, which WorldSceneFlow drives from the server's
    // tick-anchored ElevatorPhase — no sync state; a late joiner sees the right level
    // the first frame. The visuals (CabinWaterVisuals, CabinPanelDisplay) only read it.
    public sealed class CabinWater : MonoBehaviour
    {
        [SerializeField] private ElevatorController controller;
        [SerializeField] private Transform surface;   // the disc, a child of the car
        [SerializeField] private float minimumVisibleMeters = ElevatorMath.WaterMarginMeters;

        public float LevelMeters { get; private set; }
        public bool IsWet => LevelMeters > minimumVisibleMeters;
        public float SpanMeters => controller != null ? controller.SpanMeters : 0f;
        public float Level01 => SpanMeters > 0f ? Mathf.Clamp01(LevelMeters / SpanMeters) : 0f;
        public float SurfaceWorldY { get; private set; }
        public CarWaterFlow Flow { get; private set; }
        // The disc shows only between dry and full: a full car's surface would lie on the roof.
        public bool SurfaceShown => LevelMeters > minimumVisibleMeters && LevelMeters < SpanMeters - minimumVisibleMeters;
        public ElevatorController Controller => controller;
        public Transform Surface => surface;

        private void LateUpdate()
        {
            if (controller == null) return;
            float rootY = controller.transform.position.y;
            LevelMeters = ElevatorMath.WaterLevelInCar(controller.SeaLevelY, rootY, controller.SpanMeters);
            SurfaceWorldY = rootY + LevelMeters;
            Flow = ElevatorMath.WaterFlowInCar(controller.State, LevelMeters, controller.SpanMeters);
            if (surface == null) return;
            bool visible = SurfaceShown;
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (visible) surface.localPosition = new Vector3(0f, LevelMeters, 0f);
        }
    }
}
