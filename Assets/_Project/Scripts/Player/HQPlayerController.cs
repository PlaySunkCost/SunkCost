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
        // Disabled by default so HQ stays behaviourally unchanged; a dive site enables it
        // on the local owner via DiveSiteHeadlampActivator once the player has spawned.
        [SerializeField] private Light headlamp;
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
        private bool travelLocked;
        private Vector3 externalMotion;

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
        // Riding a departing ship: look works, walking and items do not (the rider
        // moves the root; docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md section 5).
        public bool TravelLocked => travelLocked;

        public void SetHeadlampEnabled(bool value)
        {
            if (headlamp != null) headlamp.enabled = value;
        }

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
            if (travelLocked)
            {
                // Nothing buffered survives the trip: a fresh press is needed after the unlock.
                CurrentTarget = null;
                CurrentButton = null;
                grabBufferedUntil = -1f;
                grabConsumed = true;
                return;
            }
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
            float speed = (Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : walkSpeed) * SpeedFactor;
            Vector3 planar = (transform.forward * input.y + transform.right * input.x) * speed;
            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed + Physics.gravity.y * Time.deltaTime;
            controller.Move((planar + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        // For editor verification hooks, which cannot lock the cursor: sample the
        // crosshair target without going through the input gate.
        public void RefreshTarget() => UpdateTarget();

        // The ship rider owns the root while locked; the CharacterController would
        // otherwise fight the writes. Restores the controller on every unlock.
        public void SetTravelLock(bool locked)
        {
            if (travelLocked == locked) return;
            travelLocked = locked;
            controller.enabled = !locked;
            verticalSpeed = 0f;
            grabBufferedUntil = -1f;
            grabConsumed = true;
            CurrentTarget = null;
            CurrentButton = null;
        }

        // Continuous owner-side placement while riding: no teleport flag, so the
        // NetworkTransform interpolates it for everyone else.
        public void FollowTo(Vector3 position)
        {
            if (!IsOwner || !travelLocked) return;
            transform.position = position;
        }

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
