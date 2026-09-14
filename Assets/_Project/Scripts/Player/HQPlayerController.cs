using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Player
{
    // Look, move and input only. Every item request goes through PlayerInventory,
    // which owns the RPCs and the server decisions.
    [RequireComponent(typeof(CharacterController))]
    public sealed class HQPlayerController : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform holdPoint;
        // Two-handed items sit here: centred and low, in front of the camera.
        [SerializeField] private Transform twoHandHoldPoint;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float lookSensitivity = 0.1f;
        // Eyes to the item's surface; aim allowance does not extend this reach.
        [SerializeField] private float interactReach = 2f;
        [SerializeField, Min(0f)] private float grabAimRadius = 0.35f;
        [SerializeField, Min(0f)] private float grabBufferSeconds = 0.3f;

        private CharacterController controller;
        private PlayerInventory inventory;
        private float pitch;
        private float verticalSpeed;
        private bool grabConsumed;
        private float grabBufferedUntil = -1f;

        public Transform HoldPoint => holdPoint;
        public Transform TwoHandHoldPoint => twoHandHoldPoint;
        // The hold pose for a grip. A missing two-hand point falls back to the right
        // hand so nothing breaks; the validator reports it.
        public Transform HoldPointFor(CarryGrip grip) =>
            grip == CarryGrip.TwoHands && twoHandHoldPoint != null ? twoHandHoldPoint : holdPoint;
        // Walk/sprint multiplier from the server-owned carried mass; a crawl when
        // the weight meter is full. A future dash should scale by it as well.
        public float SpeedFactor => inventory != null ? inventory.SpeedFactor : 1f;
        public bool Overloaded => inventory != null && inventory.Overloaded;
        public float InteractReach => interactReach;
        public Vector3 EyePosition => playerCamera != null ? playerCamera.transform.position : transform.position + Vector3.up * 1.6f;
        public PlayerInventory Inventory => inventory;
        // The carryable under the crosshair within reach this frame, owner only.
        public CarryableItem CurrentTarget { get; private set; }
        public SunkCost.World.MonitorButton CurrentButton { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            inventory = GetComponent<PlayerInventory>();
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
            // item input. Gravity and replication keep running on their own.
            if (!SessionInputGate.CanPlay || Cursor.lockState != CursorLockMode.Locked)
            {
                CurrentTarget = null;
                CurrentButton = null;
                grabBufferedUntil = -1f;
                grabConsumed = true;
                return;
            }

            Look();
            Move();
            UpdateTarget();

            if (SessionInputGate.ClickSuppressedThisFrame || inventory == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard.eKey.wasPressedThisFrame)
            {
                grabConsumed = false;
                grabBufferedUntil = Time.unscaledTime + grabBufferSeconds;
            }
            // Hold E while a ball approaches, or press slightly early. Consume one
            // request per gesture so holding E cannot vacuum every nearby item.
            if (!grabConsumed && (keyboard.eKey.isPressed || Time.unscaledTime <= grabBufferedUntil) && CurrentTarget != null)
            {
                grabConsumed = true;
                inventory.RequestGrab(CurrentTarget);
            }
            else if (keyboard.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentButton != null)
            {
                grabConsumed = true;
                SunkCost.World.ShipControls ship = GetComponent<SunkCost.World.ShipControls>();
                if (ship != null) ship.RequestSail(CurrentButton.Destination);
            }
            else if (keyboard.qKey.wasPressedThisFrame)
                inventory.RequestDrop();
            else if (Mouse.current.leftButton.wasPressedThisFrame)
                inventory.RequestUse(playerCamera.transform.forward);
            else if (keyboard.digit1Key.wasPressedThisFrame) inventory.RequestEquip(0);
            else if (keyboard.digit2Key.wasPressedThisFrame) inventory.RequestEquip(1);
            else if (keyboard.digit3Key.wasPressedThisFrame) inventory.RequestEquip(2);
            else if (keyboard.digit4Key.wasPressedThisFrame) inventory.RequestEquip(3);
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
            float speed = (Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : walkSpeed) * SpeedFactor;
            Vector3 planar = (transform.forward * input.y + transform.right * input.x) * speed;
            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            controller.Move((planar + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        // For editor verification hooks, which cannot lock the cursor: sample the
        // crosshair target without going through the input gate.
        public void RefreshTarget() => UpdateTarget();

        // Owner-side move across any distance (a scene change, a cabin arrival).
        // The client simulates its own movement (contract section 3), so it is the
        // one that places itself; the NetworkTransform snap keeps observers from
        // interpolating across the world. CharacterController overrides transform
        // writes unless it is disabled around them.
        public void TeleportLocal(Vector3 position, float yawDegrees)
        {
            if (!IsOwner) return;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            controller.enabled = true;
            verticalSpeed = 0f;
            FishNet.Component.Transforming.NetworkTransform networkTransform = GetComponent<FishNet.Component.Transforming.NetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport();
        }

        public float Yaw => transform.eulerAngles.y;

        // Held and stowed items have their colliders off, so only loose items can be
        // selected. Targeting checks eyes-to-surface distance and line of sight.
        private void UpdateTarget()
        {
            CurrentTarget = null;
            Transform eye = playerCamera.transform;
            CurrentTarget = InteractionTargeting.Find(eye.position, eye.forward, transform, interactReach, grabAimRadius);
            CurrentButton = CurrentTarget == null ? InteractionTargeting.FindButton(eye.position, eye.forward, transform, interactReach) : null;
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
