using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The plank at HQ (docs/DESIGN.md §8, "Failure"; Dan, 18 September 2026): a
    // board off the pier over the water. When the run is lost the crew walks it
    // one by one — the jumper is placed at its base, free to walk out and back,
    // and jumps (or is pushed after a while); the water below ends the walk.
    // A marker like the shop's: the server places from Base and End and reads
    // the water line from WaterY; the art pass can move all three.
    //
    // The gate (Dan, 18 September 2026: "the player on the plank will not be
    // able to leave the plank — move freely on it"): a rail across the board's
    // base, up on every peer while the crew walks the plank, so the jumper
    // walks the board and nothing else and the others wait on the pier.
    // Presentation from the replicated phase; the server decides nothing by it.
    public sealed class HQPlank : MonoBehaviour
    {
        public const string RootName = "Plank";

        [Tooltip("Where the jumper is put to start the walk (facing the water).")]
        [SerializeField] private Transform basePoint;
        [Tooltip("The far end of the board; a push drops the jumper just past it.")]
        [SerializeField] private Transform endPoint;
        [Tooltip("A player whose feet are below this is in the water.")]
        [SerializeField] private float waterY = -1f;
        [Tooltip("The rail across the board's base: active only while the crew walks the plank.")]
        [SerializeField] private GameObject gate;

        public Transform Base => basePoint;
        public Transform End => endPoint;
        public float WaterY => waterY;
        public GameObject Gate => gate;
        public float WalkYaw => basePoint != null ? basePoint.eulerAngles.y : 0f;
        public bool IsInWater(Vector3 position) => position.y < waterY - 0.05f; // feet under the water plane
        public bool GateUp => gate != null && gate.activeSelf;

        public void Configure(Transform baseAt, Transform endAt, float water, GameObject gateAt)
        {
            basePoint = baseAt;
            endPoint = endAt;
            waterY = water;
            gate = gateAt;
            if (gate != null) gate.SetActive(false);
        }

        private void Update()
        {
            if (gate == null) return;
            CrewDayState day = CrewDayState.Instance;
            bool up = day != null && day.Phase == DayPhase.Plank;
            if (gate.activeSelf != up) gate.SetActive(up);
        }

        public static HQPlank InScene(Scene scene)
        {
            foreach (HQPlank plank in FindObjectsByType<HQPlank>(FindObjectsInactive.Exclude))
                if (plank.gameObject.scene == scene) return plank;
            return null;
        }
    }
}
