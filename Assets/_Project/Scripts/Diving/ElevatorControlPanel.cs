using UnityEngine;

namespace SunkCost.Diving
{
    // Marker on the car's interior control panel so ElevatorInteractor's raycast can find
    // the owning controller via GetComponentInParent<ElevatorControlPanel>().
    public sealed class ElevatorControlPanel : MonoBehaviour
    {
        private ElevatorController controller;

        public ElevatorController Controller =>
            controller != null ? controller : controller = GetComponentInParent<ElevatorController>();
    }
}
