using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Shop
{
    // Where a bought consumable comes from: a chute in the ceiling (Dan, 18
    // September 2026: "fall from a thing in the ceiling, not always in the
    // middle") — the item appears just under the point, somewhere within
    // ScatterRadius of it, and falls; anyone may pick it up. A marker, so the
    // art pass can make it a real chute and move it.
    public sealed class ShopDeliveryPoint : MonoBehaviour
    {
        public const string DefaultName = "Shop Chute";

        [Tooltip("The item appears this far below the chute's mouth.")]
        [SerializeField, Min(0f)] private float dropBelow = 0.3f;
        [Tooltip("Landing spots vary within this radius, so bought items do not stack on each other.")]
        [SerializeField, Min(0f)] private float scatterRadius = 0.5f;

        public Vector3 DropPosition
        {
            get
            {
                Vector2 spread = Random.insideUnitCircle * scatterRadius;
                return transform.position - Vector3.up * dropBelow + new Vector3(spread.x, 0f, spread.y);
            }
        }

        public static ShopDeliveryPoint Nearest(Vector3 from, Scene scene)
        {
            ShopDeliveryPoint best = null;
            float bestDistance = float.MaxValue;
            foreach (ShopDeliveryPoint point in FindObjectsByType<ShopDeliveryPoint>(FindObjectsInactive.Exclude))
            {
                if (point.gameObject.scene != scene) continue;
                float distance = (point.transform.position - from).sqrMagnitude;
                if (distance < bestDistance) { best = point; bestDistance = distance; }
            }
            return best;
        }
    }
}
