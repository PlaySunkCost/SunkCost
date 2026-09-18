using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Shop
{
    // Where a bought consumable lands: it falls to the floor here and anyone
    // may pick it up (Dan, 18 September 2026). A marker, so the art pass can
    // make it a chute or a tray and move it; the server spawns the item at
    // this transform's position plus a little height, facing its forward.
    public sealed class ShopDeliveryPoint : MonoBehaviour
    {
        public const string DefaultName = "Shop Delivery";

        [Tooltip("The item appears this high above the point and drops.")]
        [SerializeField, Min(0f)] private float dropHeight = 0.3f;

        public Vector3 DropPosition => transform.position + Vector3.up * dropHeight;

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
