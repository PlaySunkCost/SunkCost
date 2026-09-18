using UnityEngine;

namespace SunkCost.Shop
{
    // A thing on display in the shop: look at it, press E, and the server sells
    // the catalogue item it names (HQPlayerController targets it like the
    // colour panel or the quota board — any collider under this object is the
    // pressable). Put it on any object anywhere: the greybox shelves today, the
    // real shop's stands after the art pass. The label, if any, is written from
    // the catalogue so a price change shows without a rebuild.
    public sealed class ShopDisplay : MonoBehaviour
    {
        [Tooltip("The catalogue id this stand sells (ShopCatalog).")]
        [SerializeField] private string itemId = ShopCatalog.AirTankId;
        [Tooltip("Optional: the TextMesh that shows the name and price.")]
        [SerializeField] private TextMesh label;
        [Tooltip("Optional: where a bought consumable lands; the nearest ShopDeliveryPoint in the scene otherwise.")]
        [SerializeField] private ShopDeliveryPoint deliveryPoint;

        public string ItemId => itemId;
        public ShopItem Item => ShopCatalog.Resolve().Find(itemId);
        public ShopDeliveryPoint DeliveryPoint => deliveryPoint != null ? deliveryPoint : ShopDeliveryPoint.Nearest(transform.position, gameObject.scene);

        public void Configure(string id, TextMesh text, ShopDeliveryPoint delivery)
        {
            itemId = id;
            label = text;
            deliveryPoint = delivery;
            WriteLabel();
        }

        private void Awake() => WriteLabel();

        public void WriteLabel()
        {
            if (label == null) return;
            ShopItem item = Item;
            label.text = item != null ? $"{item.Name}\n${item.Price}" : itemId;
        }
    }
}
