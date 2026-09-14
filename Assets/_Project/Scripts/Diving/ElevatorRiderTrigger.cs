using SunkCost.Sites;
using UnityEngine;

namespace SunkCost.Diving
{
    // Sits on the car's interior-footprint trigger volume. Reports boarding/leaving to the
    // ElevatorController so TryStartMove can gate on "is this player actually aboard."
    [RequireComponent(typeof(Collider))]
    public sealed class ElevatorRiderTrigger : MonoBehaviour
    {
        private ElevatorController controller;

        private void Awake()
        {
            controller = GetComponentInParent<ElevatorController>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<DiveSiteDevPlayer>() != null)
                controller.RegisterRider(other.gameObject);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponent<DiveSiteDevPlayer>() != null)
                controller.UnregisterRider(other.gameObject);
        }
    }
}
