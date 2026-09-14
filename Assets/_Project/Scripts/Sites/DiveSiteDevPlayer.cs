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
        private Vector3 externalMotion;

        // Minimal hook for a moving platform (e.g. the elevator): a CharacterController does
        // not follow platform motion on its own, so a mover accumulates its world-space delta
        // here and Move() applies it alongside the player's own input each frame.
        public void AddExternalMotion(Vector3 delta)
        {
            externalMotion += delta;
        }

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
            // Carried platform motion (e.g. the elevator) is swept on its own, separately
            // from and before the player's own input move, rather than summed into one
            // vector. Summing let the grounded stick force (below) net against a moving
            // floor instead of being blocked by it: on descent the floor happened to sit
            // under the combined vector and absorbed the excess, but on ascent nothing
            // above blocked the stick force's downward component, so the rider lost ground
            // every frame until depenetration found a false equilibrium a third of a metre
            // below the floor. Applying the carry first means it resolves against whatever
            // is actually there; the stick force then resolves afterwards against the floor
            // the rider is now standing on and is correctly blocked by it. Standard
            // CharacterController moving-platform pattern — no tuning constant involved.
            if (externalMotion != Vector3.zero)
            {
                controller.Move(externalMotion);
                externalMotion = Vector3.zero;
            }

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
