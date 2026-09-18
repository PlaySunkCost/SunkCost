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
    public sealed class HQPlank : MonoBehaviour
    {
        public const string RootName = "Plank";

        [Tooltip("Where the jumper is put to start the walk (facing the water).")]
        [SerializeField] private Transform basePoint;
        [Tooltip("The far end of the board; a push drops the jumper just past it.")]
        [SerializeField] private Transform endPoint;
        [Tooltip("A player whose feet are below this is in the water.")]
        [SerializeField] private float waterY = -1f;

        public Transform Base => basePoint;
        public Transform End => endPoint;
        public float WaterY => waterY;
        public float WalkYaw => basePoint != null ? basePoint.eulerAngles.y : 0f;
        public bool IsInWater(Vector3 position) => position.y < waterY - 0.05f; // feet under the water plane

        public void Configure(Transform baseAt, Transform endAt, float water)
        {
            basePoint = baseAt;
            endPoint = endAt;
            waterY = water;
        }

        public static HQPlank InScene(Scene scene)
        {
            foreach (HQPlank plank in FindObjectsByType<HQPlank>(FindObjectsInactive.Exclude))
                if (plank.gameObject.scene == scene) return plank;
            return null;
        }
    }
}
