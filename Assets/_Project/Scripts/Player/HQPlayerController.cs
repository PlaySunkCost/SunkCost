using FishNet.Connection;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class HQPlayerController : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform holdPoint;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float lookSensitivity = 0.1f;
        [SerializeField] private float grabDistance = 2.5f;

        private CharacterController controller;
        private float pitch;
        private float verticalSpeed;
        private Basketball heldBall;

        public Transform HoldPoint => holdPoint;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            SetLocalPresentation(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetLocalPresentation(IsOwner);
            if (bodyRenderer != null)
                bodyRenderer.material.color = Owner.ClientId % 2 == 0 ? new Color(0.15f, 0.5f, 1f) : new Color(1f, 0.55f, 0.12f);
            // Cursor capture is owned by SessionInputGate (entering the room captures,
            // Escape/overlay/focus loss releases, Resume recaptures).
        }

        private void Update()
        {
            if (!IsOwner || Keyboard.current == null || Mouse.current == null)
                return;

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
                SessionInputGate.OpenMenu();

            // Menu open, Steam overlay up, or window unfocused: no look, move or
            // grab/throw. Gravity and replication keep running on their own.
            if (!SessionInputGate.CanPlay || Cursor.lockState != CursorLockMode.Locked)
                return;

            Look();
            Move();

            if (SessionInputGate.ClickSuppressedThisFrame)
                return;

            if (Keyboard.current.eKey.wasPressedThisFrame)
            {
                if (heldBall != null)
                    ServerRequestRelease(heldBall.NetworkObject, Vector3.zero, false);
                else
                    TryRequestGrab();
            }
            else if (heldBall != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                ServerRequestRelease(heldBall.NetworkObject, playerCamera.transform.forward, true);
            }
        }

        private void Look()
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void Move()
        {
            Vector2 input = Vector2.zero;
            if (Keyboard.current.wKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed) input.y -= 1f;
            if (Keyboard.current.dKey.isPressed) input.x += 1f;
            if (Keyboard.current.aKey.isPressed) input.x -= 1f;
            input = Vector2.ClampMagnitude(input, 1f);
            float speed = Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
            Vector3 planar = (transform.forward * input.y + transform.right * input.x) * speed;
            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            controller.Move((planar + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        private void TryRequestGrab()
        {
            if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, grabDistance, ~0, QueryTriggerInteraction.Ignore))
                return;
            Basketball ball = hit.collider.GetComponentInParent<Basketball>();
            if (ball != null)
                ServerRequestGrab(ball.NetworkObject);
        }

        [ServerRpc]
        private void ServerRequestGrab(NetworkObject target, NetworkConnection sender = null)
        {
            Basketball ball = target == null ? null : target.GetComponent<Basketball>();
            if (ball == null || sender != Owner || Vector3.Distance(transform.position, ball.transform.position) > grabDistance + 0.75f)
                return;
            ball.ServerTryGrab(sender, this);
        }

        [ServerRpc]
        private void ServerRequestRelease(NetworkObject target, Vector3 direction, bool throwBall, NetworkConnection sender = null)
        {
            Basketball ball = target == null ? null : target.GetComponent<Basketball>();
            if (ball != null && sender == Owner)
                ball.ServerRelease(sender, direction, throwBall);
        }

        internal void SetHeldBall(Basketball ball)
        {
            heldBall = ball;
        }

        private void SetLocalPresentation(bool active)
        {
            if (playerCamera != null)
            {
                playerCamera.enabled = active;
                AudioListener listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = active;
            }
        }
    }
}
