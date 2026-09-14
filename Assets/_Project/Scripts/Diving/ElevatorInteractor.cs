using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Diving
{
    // Mirrors HQPlayerController.TryRequestGrab()'s raycast shape (same distance, same
    // QueryTriggerInteraction.Ignore, same GetComponentInParent<> pattern) so the two read
    // as one game. Duplicated rather than shared: HQPlayerController is the networking/
    // player-controller owner's file per CONVENTIONS.md, and this task is scoped to the
    // dive-site owner's area. The two should collapse into a shared interactor once a
    // networked dive player exists.
    public sealed class ElevatorInteractor : MonoBehaviour
    {
        [SerializeField] private Camera interactCamera;
        [SerializeField] private float interactDistance = 2.5f;

        private void Update()
        {
            if (interactCamera == null || Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
                return;

            if (!Physics.Raycast(interactCamera.transform.position, interactCamera.transform.forward, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Ignore))
                return;

            ElevatorControlPanel panel = hit.collider.GetComponentInParent<ElevatorControlPanel>();
            if (panel != null)
                panel.Controller.TryStartMove(gameObject);
        }
    }
}
