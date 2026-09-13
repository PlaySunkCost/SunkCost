using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Sites
{
    // Non-networked walk-around harness for testing dive site greyboxes before the real,
    // networked player is wired in. Delete this component and the GameObject that carries
    // it once a networked player is spawned into the site instead.
    [RequireComponent(typeof(CharacterController))]
    public sealed class DiveSiteDevPlayer : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float lookSensitivity = 0.1f;

        private CharacterController controller;
        private float pitch;
        private float verticalSpeed;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            CaptureCursor();
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (Keyboard.current == null || Mouse.current == null)
                return;

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                CaptureCursor();

            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            Look();
            Move();
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

        private static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
