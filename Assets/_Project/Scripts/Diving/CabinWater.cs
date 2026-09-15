using UnityEngine;

namespace SunkCost.Diving
{
    // The water inside the car (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md section 6).
    // The tube holds water only below sea level and the cabin is open to it, so the
    // level inside is the level outside: sea level minus the car floor, clamped to
    // the cabin. Computed on every peer from the car's own transform each frame —
    // no sync state; a late joiner sees the right level the first frame.
    public sealed class CabinWater : MonoBehaviour
    {
        [SerializeField] private ElevatorController controller;
        [SerializeField] private Transform surface;   // the disc, a child of the car
        [SerializeField] private float minimumVisibleMeters = 0.02f;

        public float LevelMeters { get; private set; }
        public bool IsWet => LevelMeters > minimumVisibleMeters;

        private void LateUpdate()
        {
            if (controller == null) return;
            LevelMeters = ElevatorMath.WaterLevelInCar(controller.SeaLevelY, controller.transform.position.y, controller.SpanMeters);
            if (surface == null) return;
            bool visible = IsWet;
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (visible) surface.localPosition = new Vector3(0f, LevelMeters, 0f);
        }
    }
}
