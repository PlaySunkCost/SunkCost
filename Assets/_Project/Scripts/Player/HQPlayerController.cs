using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Player
{
    // Look, move, jump, crouch and input only. Every item request goes through
    // PlayerInventory, which owns the RPCs and the server decisions; the stance
    // goes through PlayerStance. The motor (gravity, jump, one Move call) runs
    // every frame the player is not travel-locked, whether or not input is
    // allowed: a menu never leaves a jumper hanging in the air.
    [RequireComponent(typeof(CharacterController))]
    public sealed class HQPlayerController : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform viewPivot;
        [SerializeField] private Transform holdPoint;
        // Two-handed items sit here: centred and low, in front of the camera.
        [SerializeField] private Transform twoHandHoldPoint;
        [SerializeField] private Renderer bodyRenderer;
        // Disabled by default so HQ stays behaviourally unchanged; a dive site enables it
        // on the local owner via DiveSiteHeadlampActivator once the player has spawned.
        [SerializeField] private Light headlamp;
        // The visible body, squashed to the crouch height (presentation only; the
        // root is never scaled).
        [SerializeField] private Transform bodyVisual;
        [SerializeField] private PlayerMovementSettings movement;
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float lookSensitivity = 0.1f;
        // Eyes to the item's surface; aim allowance does not extend this reach.
        [SerializeField] private float interactReach = 2f;
        [SerializeField, Min(0f)] private float grabAimRadius = 0.35f;
        [SerializeField, Min(0f)] private float grabBufferSeconds = 0.3f;

        private CharacterController controller;
        private PlayerInventory inventory;
        private PlayerStance stance;
        private float pitch;
        private float verticalSpeed;
        private bool grabConsumed;
        private float grabBufferedUntil = -1f;
        private bool travelLocked;
        private Vector3 externalMotion;

        // Motor state.
        private bool grounded;
        private float coyoteUntil = float.NegativeInfinity;
        private float jumpBufferedUntil = float.NegativeInfinity;
        private bool airborneByJump;
        private CollisionFlags lastFlags;
        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        // Presentation blend (all peers).
        private bool stanceCrouched;
        private float eyeHeight;
        private float bodyScaleY = 1f;
        private float standingBodyScaleY = 1f;
        private float standingBodyLocalY;

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
        public PlayerMovementSettings Movement => PlayerMovementSettings.Resolve(movement);
        public CharacterController Controller => controller;
        public Camera PlayerCamera => playerCamera;
        // The carryable under the crosshair within reach this frame, owner only.
        public CarryableItem CurrentTarget { get; private set; }
        public SunkCost.World.MonitorButton CurrentButton { get; private set; }
        // Riding a departing ship: look works, walking and items do not (the rider
        // moves the root; docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md section 5).
        public bool TravelLocked => travelLocked;
        public bool IsGrounded => grounded;
        public bool IsCrouched => stanceCrouched;
        public float VerticalSpeed => verticalSpeed;
        public float EyeHeight => eyeHeight;
        // Diagnostics for the checks: the last takeoff speed.
        public float LastTakeoffSpeed { get; private set; }
#if UNITY_EDITOR
        // Editor checks feed a virtual keyboard through the Input System; the cursor
        // lock and menu gate would otherwise swallow it. Never set in a build.
        public static bool BypassInputGateForChecks;
#endif
        public void SetPitchForChecks(float degrees)
        {
            pitch = Mathf.Clamp(degrees, -80f, 80f);
            if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

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
            stance = GetComponent<PlayerStance>();
            if (bodyVisual != null)
            {
                standingBodyScaleY = bodyVisual.localScale.y;
                standingBodyLocalY = bodyVisual.localPosition.y;
            }
            ApplyStance(false);
            SnapPresentation();
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
            BlendPresentation();
            if (!IsOwner) return;
            // No keyboard or mouse (a headless peer): no commands, but the motor
            // still runs so gravity, grounding and the stance keep working.
            bool hasDevices = Keyboard.current != null && Mouse.current != null;

            if (hasDevices && Keyboard.current.escapeKey.wasPressedThisFrame)
                SessionInputGate.OpenMenu();

            // Menu open, Steam overlay up, or window unfocused: no look, move or
            // item input. Gravity keeps running below; only commands stop.
            bool canPlay = hasDevices && SessionInputGate.CanPlay && Cursor.lockState == CursorLockMode.Locked;
#if UNITY_EDITOR
            if (BypassInputGateForChecks && hasDevices) canPlay = true;
#endif
            Vector2 moveInput = Vector2.zero;
            bool sprint = false;
            if (canPlay)
            {
#if UNITY_EDITOR
                if (!BypassInputGateForChecks) // the checks set the view themselves; the real mouse stays out
#endif
                    Look();
                if (!travelLocked)
                {
                    Keyboard keyboard = Keyboard.current;
                    if (keyboard.wKey.isPressed) moveInput.y += 1f;
                    if (keyboard.sKey.isPressed) moveInput.y -= 1f;
                    if (keyboard.dKey.isPressed) moveInput.x += 1f;
                    if (keyboard.aKey.isPressed) moveInput.x -= 1f;
                    sprint = keyboard.leftShiftKey.isPressed;
                    if (keyboard.spaceKey.wasPressedThisFrame) jumpBufferedUntil = Time.unscaledTime + Movement.JumpBufferSeconds;
                    stance?.SetDesiredCrouch(keyboard.leftCtrlKey.isPressed);
                }
            }
            else
            {
                CurrentTarget = null;
                CurrentButton = null;
                grabBufferedUntil = -1f;
                grabConsumed = true;
                jumpBufferedUntil = float.NegativeInfinity;
            }

            if (travelLocked)
            {
                // Nothing buffered survives the trip: a fresh press is needed after the unlock.
                CurrentTarget = null;
                CurrentButton = null;
                grabBufferedUntil = -1f;
                grabConsumed = true;
                jumpBufferedUntil = float.NegativeInfinity;
                return;
            }

            Motor(moveInput, sprint);
            if (!canPlay) return;

            UpdateTarget();
            if (SessionInputGate.ClickSuppressedThisFrame || inventory == null)
                return;

            Keyboard keys = Keyboard.current;
            if (keys.eKey.wasPressedThisFrame)
            {
                grabConsumed = false;
                grabBufferedUntil = Time.unscaledTime + grabBufferSeconds;
            }
            // Hold E while a ball approaches, or press slightly early. Consume one
            // request per gesture so holding E cannot vacuum every nearby item.
            if (!grabConsumed && (keys.eKey.isPressed || Time.unscaledTime <= grabBufferedUntil) && CurrentTarget != null)
            {
                grabConsumed = true;
                inventory.RequestGrab(CurrentTarget);
            }
            else if (keys.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentButton != null)
            {
                grabConsumed = true;
                SunkCost.World.ShipControls ship = GetComponent<SunkCost.World.ShipControls>();
                if (ship != null) ship.RequestSail(CurrentButton.Destination);
            }
            else if (keys.qKey.wasPressedThisFrame)
                inventory.RequestDrop();
            else if (Mouse.current.leftButton.wasPressedThisFrame)
                inventory.RequestUse(playerCamera.transform.forward);
            else if (keys.digit1Key.wasPressedThisFrame) inventory.RequestEquip(0);
            else if (keys.digit2Key.wasPressedThisFrame) inventory.RequestEquip(1);
            else if (keys.digit3Key.wasPressedThisFrame) inventory.RequestEquip(2);
            else if (keys.digit4Key.wasPressedThisFrame) inventory.RequestEquip(3);
        }

        private void Look()
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // ---- motor ------------------------------------------------------------------

        // Ground support, jump, gravity, one Move (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
        // section 4). Support means something below the feet, not a wall hit and
        // not a ball (the collision policy ignores cargo, so the probe does too).
        private void Motor(Vector2 input, bool sprint)
        {
            if (!controller.enabled) return;
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

            PlayerMovementSettings settings = Movement;
            float now = Time.unscaledTime;
            grounded = verticalSpeed <= 0.01f && ((lastFlags & CollisionFlags.Below) != 0 || ProbeGround());
            if (grounded)
            {
                coyoteUntil = now + settings.CoyoteTime;
                airborneByJump = false;
                controller.stepOffset = PlayerMovementMath.StepOffset(settings, stanceCrouched);
                if (verticalSpeed < 0f) verticalSpeed = -2f;
            }

            bool wantsJump = now <= jumpBufferedUntil;
            if (wantsJump)
            {
                if (!CanJump())
                    jumpBufferedUntil = float.NegativeInfinity; // refused: needs a fresh press
                else if (grounded || (now <= coyoteUntil && !airborneByJump))
                {
                    jumpBufferedUntil = float.NegativeInfinity;
                    coyoteUntil = float.NegativeInfinity;
                    airborneByJump = true;
                    grounded = false;
                    float capacity = inventory != null ? inventory.CapacityKg : 0f;
                    float mass = inventory != null ? inventory.CarriedMassKg : 0f;
                    LastTakeoffSpeed = PlayerMovementMath.TakeoffSpeed(PlayerMovementMath.JumpHeight(settings, mass, capacity), Physics.gravity.y);
                    verticalSpeed = LastTakeoffSpeed;
                    controller.stepOffset = 0f; // no stepping up tall obstacles in flight
                }
                // else: a press in the air waits for the landing, within the buffer window only.
            }

            if (!grounded) verticalSpeed += Physics.gravity.y * Time.deltaTime;

            input = Vector2.ClampMagnitude(input, 1f);
            float speed = (sprint && !stanceCrouched ? sprintSpeed : walkSpeed) * (stanceCrouched ? settings.CrouchSpeedFactor : 1f) * SpeedFactor;
            Vector3 planar = (transform.forward * input.y + transform.right * input.x) * speed;
            lastFlags = controller.Move((planar + Vector3.up * verticalSpeed) * Mathf.Min(Time.deltaTime, 0.1f));
            if ((lastFlags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f; // head hit: the ascent ends now
        }

        // Crouched, holding a two-handed item or locked: no jump. The grip comes
        // from the replicated item, not from the slot selection.
        public bool CanJump()
        {
            if (travelLocked || stanceCrouched) return false;
            if (stance != null && stance.DesiredCrouch) return false;
            CarryableItem held = inventory != null ? inventory.HeldItem : null;
            return held == null || held.Grip != CarryGrip.TwoHands;
        }

        private bool ProbeGround()
        {
            float radius = controller.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (controller.radius + 0.02f);
            float distance = controller.skinWidth + 0.08f + 0.02f;
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, GroundHits, distance, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore);
            if (count == GroundHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = GroundHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > controller.slopeLimit) continue; // too steep to stand on
                return true;
            }
            return false;
        }

        // ---- stance presentation ------------------------------------------------------

        // Collision changes at once (every peer applies its accepted posture);
        // the eye and the body blend over crouchBlendSeconds within the capsule.
        public void ApplyStance(bool crouched)
        {
            PlayerMovementSettings settings = Movement;
            stanceCrouched = crouched;
            controller.height = PlayerMovementMath.CapsuleHeight(settings, crouched);
            controller.center = PlayerMovementMath.CapsuleCenter(settings, crouched);
            controller.radius = settings.CapsuleRadius;
            if (!airborneByJump) controller.stepOffset = PlayerMovementMath.StepOffset(settings, crouched);
        }

        private void BlendPresentation()
        {
            PlayerMovementSettings settings = Movement;
            float targetEye = PlayerMovementMath.EyeHeight(settings, stanceCrouched);
            float targetScale = standingBodyScaleY * (stanceCrouched ? settings.CrouchHeight / settings.StandingHeight : 1f);
            float eyeSpan = Mathf.Abs(settings.StandingEyeHeight - settings.CrouchEyeHeight);
            float scaleSpan = Mathf.Abs(standingBodyScaleY * (1f - settings.CrouchHeight / settings.StandingHeight));
            float step = settings.CrouchBlendSeconds <= 0f ? 1f : Time.unscaledDeltaTime / settings.CrouchBlendSeconds;
            eyeHeight = Mathf.MoveTowards(eyeHeight, targetEye, eyeSpan * step);
            bodyScaleY = Mathf.MoveTowards(bodyScaleY, targetScale, scaleSpan * step);
            // Never let the eye sit above the current capsule top while blending.
            eyeHeight = Mathf.Min(eyeHeight, controller.height - 0.1f);
            if (viewPivot != null) viewPivot.localPosition = new Vector3(0f, eyeHeight, 0f);
            if (bodyVisual != null)
            {
                Vector3 scale = bodyVisual.localScale;
                bodyVisual.localScale = new Vector3(scale.x, bodyScaleY, scale.z);
                // A body positioned by its centre keeps its feet on the floor.
                float ratio = standingBodyScaleY <= 0f ? 1f : bodyScaleY / standingBodyScaleY;
                bodyVisual.localPosition = new Vector3(bodyVisual.localPosition.x, standingBodyLocalY * ratio, bodyVisual.localPosition.z);
            }
        }

        private void SnapPresentation()
        {
            PlayerMovementSettings settings = Movement;
            eyeHeight = PlayerMovementMath.EyeHeight(settings, stanceCrouched);
            bodyScaleY = standingBodyScaleY * (stanceCrouched ? settings.CrouchHeight / settings.StandingHeight : 1f);
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
            jumpBufferedUntil = float.NegativeInfinity;
            lastFlags = CollisionFlags.None;
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
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            controller.enabled = wasEnabled;
            verticalSpeed = 0f;
            jumpBufferedUntil = float.NegativeInfinity;
            lastFlags = CollisionFlags.None;
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
            // The owner sees its arms and what they hold, not its own placeholder
            // body (whose head sits right under the camera); friends see both.
            if (bodyVisual != null)
                foreach (Renderer renderer in bodyVisual.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = !active;
        }
    }
}
