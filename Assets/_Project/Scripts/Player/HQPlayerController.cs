using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Noise;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SunkCost.Player
{
    public enum CabinControl : byte { None, DeckCabin, Car }

    // A dash the server saw (the repo's one-shot idiom: a serial in a SyncVar): every
    // peer plays the rings and the whoosh from it, in this direction.
    public struct DashCue
    {
        public int Serial;
        public Vector3 Direction;
    }

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
        // The head cut off the character model at spawn (PlayerHeadSplit, on this
        // object): the owner hides only that and sees the rest of itself.
        private PlayerHeadSplit headSplit;
        private bool headSplitLooked;
        public PlayerHeadSplit HeadSplit
        {
            get
            {
                if (!headSplitLooked) { headSplitLooked = true; headSplit = GetComponent<PlayerHeadSplit>(); if (headSplit != null && bodyVisual != null) headSplit.Apply(bodyVisual); }
                return headSplit;
            }
        }
        [SerializeField] private PlayerMovementSettings movement;
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 6f;
        [SerializeField] private float lookSensitivity = 0.1f;
        // Eyes to the item's surface; aim allowance does not extend this reach.
        [SerializeField] private float interactReach = 3.5f; // eyes to the item's surface; Lethal Company ~5, Minecraft 4.5, most shooters ~2 (Dan, 16 September 2026: farther)
        [SerializeField, Min(0f)] private float grabAimRadius = 0.35f;
        [SerializeField, Min(0f)] private float grabBufferSeconds = 0.3f;

        private CharacterController controller;
        private PlayerInventory inventory;
        private PlayerStance stance;
        private PlayerCameraClearance clearance;
        private float pitch;
        private float verticalSpeed;
        private bool grabConsumed;
        private float grabBufferedUntil = -1f;
        private bool travelLocked;
        private Vector3 externalMotion;
        // Dead (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 1): server-written; every
        // peer hides the body and drops the capsule, the owner loses its commands.
        private readonly SyncVar<bool> dead = new(false);
        // The owner's look pitch for spectators (card 2): server-written from the
        // owner's [ServerRpc] at most 10×/s when it moved by 2°; yaw is the root.
        private readonly SyncVar<sbyte> lookPitch = new(0);
        private float sentPitch = float.NaN;
        private float nextPitchSendAt;
        // 20 Hz on a 1° change, one byte each; a remote copy eases toward the last
        // value (a spectator's view stepped at 10 Hz / 2° — Dan, 17 September 2026).
        private const float PitchSendInterval = 0.05f, PitchSendThreshold = 1f, PitchSmoothSeconds = 0.08f;
        private float remotePitch;
        // A remote copy's eyes as watchers use them (a dead spectator's camera, the
        // deck TV): its position and yaw eased over EyeSmoothSeconds behind the
        // NetworkTransform, so a packet that arrives late and the catch-up after it
        // read as a slow and a quick turn of the head rather than a freeze and a
        // snap (Dan and Idan over Steam, Build 78, 17 September 2026: "the picture
        // steps"). A move over EyeSnapMetres in one frame is a teleport: no swoop.
        private const float EyeSmoothSeconds = 0.1f, EyeSnapMetres = 3f;
        private Vector3 eyePosition;
        private float eyeYaw;
        private bool eyesPrimed;

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
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        // Air and health (PlayerVitals on the same prefab); null before the vitals setup ran.
        public PlayerVitals Vitals => vitals != null ? vitals : vitals = GetComponent<PlayerVitals>();
        private PlayerVitals vitals;
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
        public SunkCost.World.ColourPanel CurrentColourPanel { get; private set; }
        public SunkCost.World.QuotaBoard CurrentQuotaBoard { get; private set; }
        // The shop stand under the crosshair within reach (E buys; the shop, 18 September 2026).
        public SunkCost.Shop.ShopDisplay CurrentShopDisplay { get; private set; }
        // What this player bought (PlayerUpgrades on the same prefab); null before the shop setup ran.
        public PlayerUpgrades Upgrades => upgrades != null ? upgrades : upgrades = GetComponent<PlayerUpgrades>();
        private PlayerUpgrades upgrades;
        // The deck TV's screen under the crosshair within reach (E = next channel, card 3).
        public SunkCost.World.ShipTV CurrentTv { get; private set; }
        // The cabin control under the crosshair within reach: the deck cabin's
        // button on the ship or the seafloor car's panel (owner only).
        public CabinControl CurrentCabinControl { get; private set; }
        // A living teammate under the crosshair within reach (owner only): E held on
        // a leaking one patches their suit (the monsters, 20 September 2026).
        public HQPlayerController CurrentPatient { get; private set; }
        // How far along the hold on CurrentPatient is, 0..1 (owner only).
        public float PatchProgress { get; private set; }
        private HQPlayerController patchPatient;
        private float patchHeldSince = -1f;
        // The headlamp's switch (the Lure hunts light; F toggles it below). Server-
        // written from the owner's request; every peer applies it to its copy and
        // the server reads it for the monsters. On again at every ride down.
        private readonly SyncVar<bool> lampOn = new(true);
        private bool lampWanted; // this copy is one whose lamp shows: the local player below, a remote copy listed below
        public bool LampOn => lampOn.Value;
        // The dash (docs/DESIGN.md §3; Dan, 21 September 2026): Alt, a burst of
        // DashMeters over DashSeconds in the steering direction (forward with none),
        // below only, standing, not with both hands full, DashCooldownSeconds apart.
        // Client-simulated like the rest of movement — the owner moves its own
        // capsule — and judged by the server from the copy's own speed, as sprinting
        // is: the air (PlayerVitals.ServerSpendAir), the noise (NoiseKind.Dash) and
        // this cue, from which every other peer plays the rings and the whoosh
        // (PlayerDashEffects; the owner plays its own at the press).
        private readonly SyncVar<DashCue> dashCue = new(new DashCue { Serial = 0 });
        private float dashUntil = float.NegativeInfinity, dashReadyAt = float.NegativeInfinity;
        private float dashStartedAt = float.NegativeInfinity; // every peer: the owner's press, a remote copy's cue
        private Vector3 dashDirection;
        private float dashSpeed;
        private int dashClientStartFrame = -1;
        public int Dashes { get; private set; } // owner: bursts started
        public string DashRefusal { get; private set; } = string.Empty;
        public bool Dashing => Time.unscaledTime < dashUntil;
        public DashCue LastDashCue => dashCue.Value;
        public event System.Action<Vector3> DashStarted; // every peer: the direction, for the rings and the whoosh
        // 0..1, how far the cooldown has run (1 = ready); the same on every peer, so a
        // spectator's and the TV's visor read it too.
        public float DashReady => Movement.DashCooldownSeconds <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - dashStartedAt) / Movement.DashCooldownSeconds);
        // Riding a departing ship: look works, walking and items do not (the rider
        // moves the root; docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md section 5).
        public bool TravelLocked => travelLocked;
        public bool IsDead => dead.Value;
        // Where a spectator's eyes go: this player's camera transform (every peer
        // has it; remote copies hold the replicated pitch on it).
        public Transform EyeAnchor => playerCamera != null ? playerCamera.transform : transform;
        public float LookPitch => IsOwner ? pitch : remotePitch;
        // The one place a watcher's camera is placed from (docs/CONVENTIONS.md, one
        // path): the owner's own eyes exactly, a remote copy's eased ones.
        public void EyePose(out Vector3 position, out Quaternion rotation)
        {
            if (IsOwner || !eyesPrimed) { position = EyeAnchor.position; rotation = Quaternion.Euler(LookPitch, Yaw, 0f); return; }
            position = eyePosition;
            rotation = Quaternion.Euler(remotePitch, eyeYaw, 0f);
        }
        public float EyeYaw => IsOwner || !eyesPrimed ? Yaw : eyeYaw;
        // The owner's spectator view, created at OnStartClient (card 2).
        public SpectatorView Spectator { get; private set; }
        public float GrabAimRadius => grabAimRadius;
        public bool IsGrounded => grounded;
        public bool IsCrouched => stanceCrouched;
        public float VerticalSpeed => verticalSpeed;
        public float EyeHeight => eyeHeight;
        // The camera clearance found no clear pose: the HUD covers the view and no
        // target is offered until it does (docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md section 4).
        public bool ViewObstructed => clearance != null && clearance.Obstructed;
        public PlayerCameraClearance CameraClearance => clearance;
        // Diagnostics for the checks: the last takeoff speed.
        public float LastTakeoffSpeed { get; private set; }
#if UNITY_EDITOR
        // Editor checks feed a virtual keyboard through the Input System; the cursor
        // lock and menu gate would otherwise swallow it. Never set in a build.
        public static bool BypassInputGateForChecks;
        // The matrices' virtual keyboard. Keyboard.current is whichever keyboard spoke
        // last, so a tester typing during a run would silently take the checks' keys.
        public static Keyboard KeyboardForChecks;
        private static Keyboard ActiveKeyboard => KeyboardForChecks ?? Keyboard.current;
#else
        private static Keyboard ActiveKeyboard => Keyboard.current;
#endif
        public void SetPitchForChecks(float degrees)
        {
            pitch = Mathf.Clamp(degrees, -80f, 80f);
            if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // Where a lamp shows at all (the dive; PresentSky for the local player, the
        // day state's Below for a remote copy); the switch decides the rest.
        public void SetHeadlampEnabled(bool value)
        {
            lampWanted = value;
            ApplyLamp();
        }

        private void ApplyLamp()
        {
            if (headlamp != null) headlamp.enabled = lampWanted && lampOn.Value;
        }

        // F: the owner asks; the server writes the switch.
        public void RequestLamp(bool on)
        {
            if (!IsOwner || dead.Value) return;
            ServerRequestLamp(on);
        }

        [ServerRpc]
        private void ServerRequestLamp(bool on, NetworkConnection sender = null)
        {
            if (sender != Owner) return;
            lampOn.Value = on;
        }

        [Server]
        public void ServerSetLamp(bool on) => lampOn.Value = on;

        // ---- the dash ------------------------------------------------------------------

        // Alt, or the peer's dash command: a burst in the steering direction (forward
        // with none). Refused with a reason the visor and the checks can read. The
        // owner's own rings and whoosh play now; the server's cue reaches the others.
        public bool TryDash(Vector2 input)
        {
            if (!IsOwner || dead.Value || travelLocked) return RefuseDash("Not now");
            float now = Time.unscaledTime;
            if (now < dashUntil) return RefuseDash("Already dashing");
            if (now < dashReadyAt) return RefuseDash($"Dash in {dashReadyAt - now:0.0} s");
            if (stanceCrouched || (stance != null && stance.DesiredCrouch)) return RefuseDash("Not crouched");
            CarryableItem held = inventory != null ? inventory.HeldItem : null;
            if (held != null && held.Grip == CarryGrip.TwoHands) return RefuseDash("Not with both hands full");
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            if (day == null || !day.IsBelow(OwnerId)) return RefuseDash("Only below");
            PlayerMovementSettings settings = Movement;
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 direction = input.sqrMagnitude > 0.01f ? transform.forward * input.y + transform.right * input.x : transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            dashDirection = direction.normalized;
            dashSpeed = settings.DashMeters * SpeedFactor / settings.DashSeconds; // a heavy diver dashes shorter, not longer
            dashUntil = now + settings.DashSeconds;
            dashReadyAt = now + settings.DashCooldownSeconds;
            dashStartedAt = now;
            Dashes++;
            DashRefusal = string.Empty;
            DashStarted?.Invoke(dashDirection);
            return true;
        }
        private bool RefuseDash(string why) { DashRefusal = why; return false; }

        private void OnDashCueChanged(DashCue previous, DashCue next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer (the host sees both passes)
            if (next.Serial == 0 || Time.frameCount == dashClientStartFrame || IsOwner) return; // a joiner's old cue is old news; the owner played its own at the press
            dashStartedAt = Time.unscaledTime;
            DashStarted?.Invoke(next.Direction);
        }

        // The server's view of a dash, judged from the copy's own speed as sprinting is
        // (no flag from the owner): the flat ground covered over the last
        // DashJudgeWindow seconds reaches DashJudgeFraction of a dash's length — a
        // sprint covers well under half of it in that time — and once per burst the
        // air, the noise and the cue. Hysteresis and a gap keep one burst one dash; a
        // move over 3 m in a frame is a teleport, not a dash. A hacked client dashing
        // without a cooldown only pays air for every burst.
        private const float DashJudgeWindow = 0.3f, DashJudgeFraction = 0.6f, DashJudgeGap = 0.6f;
        private readonly System.Collections.Generic.List<(float time, Vector3 position)> dashTrail = new();
        private bool serverDashing;
        private float serverDashGapUntil;
        public bool ServerDashing => serverDashing;
        public int ServerDashes { get; private set; }

        [Server]
        private void ServerJudgeDash()
        {
            float now = Time.unscaledTime;
            Vector3 position = transform.position;
            if (dashTrail.Count > 0 && (position - dashTrail[dashTrail.Count - 1].position).sqrMagnitude > 9f) dashTrail.Clear(); // a teleport
            dashTrail.Add((now, position));
            while (dashTrail.Count > 1 && now - dashTrail[0].time > DashJudgeWindow) dashTrail.RemoveAt(0);
            Vector3 flat = position - dashTrail[0].position; flat.y = 0f;
            float covered = flat.magnitude;
            float dashLength = Movement.DashMeters * SpeedFactor;
            if (!serverDashing)
            {
                bool suitOn = Vitals != null && Vitals.ServerSuitOn && !dead.Value;
                if (!suitOn || now < serverDashGapUntil || dashTrail.Count < 2 || covered < dashLength * DashJudgeFraction) return;
                serverDashing = true;
                serverDashGapUntil = now + DashJudgeGap;
                ServerDashes++;
                Vitals.ServerSpendAir(Vitals.Settings.DashAirSeconds);
                NoiseSystem.Emit(position, NoiseSettings.Get().DashRadius, NoiseKind.Dash, ObjectId);
                dashCue.Value = new DashCue { Serial = dashCue.Value.Serial + 1, Direction = flat.normalized };
            }
            else if (covered < dashLength * DashJudgeFraction * 0.5f) serverDashing = false;
        }

        // The bright headlamp upgrade: the plain lamp's range and intensity times
        // these (1, 1 = the plain lamp). Every peer applies it to its copy.
        private float headlampBaseRange = -1f, headlampBaseIntensity = -1f;
        public void SetHeadlampUpgrade(float rangeFactor, float intensityFactor)
        {
            if (headlamp == null) return;
            if (headlampBaseRange < 0f) { headlampBaseRange = headlamp.range; headlampBaseIntensity = headlamp.intensity; }
            headlamp.range = headlampBaseRange * rangeFactor;
            headlamp.intensity = headlampBaseIntensity * intensityFactor;
        }
        public float HeadlampRange => headlamp != null ? headlamp.range : 0f;

        // Minimal hook for a moving platform (e.g. the elevator): a CharacterController does
        // not follow platform motion on its own, so a mover accumulates its world-space delta
        // here and Move() applies it alongside the player's own input each frame.
        public void AddExternalMotion(Vector3 delta)
        {
            externalMotion += delta;
        }

        // Carried right now, by the caller's floor, not at the next Update: the driven
        // car moves the rider in the same frame and in the order that keeps the sweep
        // against the floor's collider clean (see WorldSceneFlow.DriveCar).
        public void CarryNow(Vector3 delta)
        {
            if (controller == null || !controller.enabled || delta == Vector3.zero) return;
            controller.Move(delta);
        }

        private void Awake()
        {
            dead.OnChange += OnDeadChanged;
            lampOn.OnChange += (_, _, _) => ApplyLamp();
            dashCue.OnChange += OnDashCueChanged;
            controller = GetComponent<CharacterController>();
            inventory = GetComponent<PlayerInventory>();
            stance = GetComponent<PlayerStance>();
            clearance = GetComponent<PlayerCameraClearance>();
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
            if (IsOwner && Spectator == null) Spectator = gameObject.AddComponent<SpectatorView>();
            dashClientStartFrame = Time.frameCount;
            if (GetComponent<PlayerDashEffects>() == null) gameObject.AddComponent<PlayerDashEffects>(); // the rings and the whoosh, on every peer
            // The body wears the player's colour (PlayerIdentity): now, and whenever it changes.
            PlayerIdentity identity = GetComponent<PlayerIdentity>();
            if (identity != null)
            {
                identity.ColourChanged += SetBodyColour;
                SetBodyColour(identity.Colour);
            }
            else if (bodyRenderer != null) bodyRenderer.material.color = PlayerPalette.Get(Owner.ClientId);
            // Cursor capture is owned by SessionInputGate (entering the room captures,
            // Escape/overlay/focus loss releases, Resume recaptures).
        }

        private void Update()
        {
            BlendPresentation();
            // The dead state's side effects are re-applied whenever the replicated
            // value and the applied one disagree (a client whose object was moved
            // between scenes or re-initialised can miss the change callback).
            if (appliedDead != dead.Value) ApplyDead(dead.Value);
            if (IsServerStarted) ServerJudgeDash(); // every copy, the host's own included: one path
            if (!IsOwner) return; // the eyes ease in LateUpdate, after the NetworkTransform has moved
            // No keyboard or mouse (a headless peer): no commands, but the motor
            // still runs so gravity, grounding and the stance keep working.
            bool hasDevices = ActiveKeyboard != null && Mouse.current != null;

            // Escape opens the menu and, pressed again, closes it (the same as Resume).
            if (hasDevices && ActiveKeyboard.escapeKey.wasPressedThisFrame)
            {
                if (SessionInputGate.PickerOpen) SessionInputGate.ClosePicker();
                else if (SessionInputGate.MenuOpen) SessionInputGate.Resume();
                else SessionInputGate.OpenMenu();
            }

            // Dead (card 2): no look, move, items or targets; the one command is
            // left click, the next living player to watch. The spectator view owns
            // the camera, so the clearance solver stays out of it too.
            if (dead.Value)
            {
                if (hasDevices && SessionInputGate.CanPlay && !SessionInputGate.ClickSuppressedThisFrame && Mouse.current.leftButton.wasPressedThisFrame) RequestNextSpectate();
                CurrentTarget = null;
                CurrentButton = null;
                CurrentColourPanel = null;
                CurrentQuotaBoard = null;
                CurrentShopDisplay = null;
                CurrentTv = null;
                CurrentCabinControl = CabinControl.None;
                ClearPatch();
                grabBufferedUntil = -1f;
                grabConsumed = true;
                jumpBufferedUntil = float.NegativeInfinity;
                return;
            }
            SendPitchIfDue();

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
                    Keyboard keyboard = ActiveKeyboard;
                    if (keyboard.wKey.isPressed) moveInput.y += 1f;
                    if (keyboard.sKey.isPressed) moveInput.y -= 1f;
                    if (keyboard.dKey.isPressed) moveInput.x += 1f;
                    if (keyboard.aKey.isPressed) moveInput.x -= 1f;
                    sprint = keyboard.leftShiftKey.isPressed;
                    if (keyboard.spaceKey.wasPressedThisFrame) jumpBufferedUntil = Time.unscaledTime + Movement.JumpBufferSeconds;
                    stance?.SetDesiredCrouch(keyboard.leftCtrlKey.isPressed);
                    if (keyboard.leftAltKey.wasPressedThisFrame) TryDash(moveInput); // the burst, in the steering direction
                }
            }
            else
            {
                // Nothing buffered while the gate is shut; what the dot rests on is
                // still found below.
                grabBufferedUntil = -1f;
                grabConsumed = true;
                jumpBufferedUntil = float.NegativeInfinity;
            }

            if (travelLocked)
            {
                // Nothing buffered survives the trip: a fresh press is needed after the unlock.
                CurrentTarget = null;
                CurrentButton = null;
                CurrentCabinControl = CabinControl.None;
                ClearPatch();
                grabBufferedUntil = -1f;
                grabConsumed = true;
                jumpBufferedUntil = float.NegativeInfinity;
                clearance?.Solve(); // the rider moved the root; the view still keeps out of the ship's walls
                return;
            }

            // Frame order: input, move and stance, desired eye, corrected eye, then
            // the target ray from the eye that actually renders.
            Motor(moveInput, sprint);
            clearance?.Solve();

            // What the dot rests on is a fact of the view, not of input: the visor
            // tags it with the menu open too (the HUD only reads it). The presses
            // below are what the gate stops.
            if (ViewObstructed)
            {
                CurrentTarget = null;
                CurrentButton = null;
                CurrentCabinControl = CabinControl.None;
                ClearPatch();
                grabConsumed = true;
                return;
            }
            UpdateTarget();
            if (!canPlay || SessionInputGate.ClickSuppressedThisFrame || inventory == null) { ClearPatch(); return; }

            Keyboard keys = ActiveKeyboard;
            // K kills, below only, in development builds and the editor (nothing
            // else can kill yet; air and the monster will call the same death).
            if (keys.kKey.wasPressedThisFrame && Debug.isDebugBuild) { RequestDebugDeath(); return; }
            // L takes a step off the tank (Dan, 17 September 2026: "we need to test
            // oxygen somehow"), below only, development builds and the editor.
            if (keys.lKey.wasPressedThisFrame && Debug.isDebugBuild && Vitals != null) Vitals.RequestDebugAirDown();
            // F: the headlamp's switch (the Lure hunts light).
            if (keys.fKey.wasPressedThisFrame) RequestLamp(!lampOn.Value);
            if (keys.eKey.wasPressedThisFrame)
            {
                grabConsumed = false;
                grabBufferedUntil = Time.unscaledTime + grabBufferSeconds;
            }
            // E held on a leaking teammate for TeammatePatchSeconds patches their suit
            // (free, once a day per patient — the server keeps the count). The hold
            // is the owner's; the request goes when it completes.
            UpdatePatchHold(keys);
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
                if (ship == null) { }
                else if (CurrentButton.Action == SunkCost.World.MonitorButton.Kind.EndDay) ship.RequestEndDay();
                else ship.RequestSail(CurrentButton.Destination);
            }
            else if (keys.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentColourPanel != null)
            {
                grabConsumed = true;
                SessionInputGate.OpenPicker();
            }
            else if (keys.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentQuotaBoard != null)
            {
                grabConsumed = true;
                SunkCost.World.ShipControls ship = GetComponent<SunkCost.World.ShipControls>();
                if (ship != null) ship.RequestPay();
            }
            else if (keys.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentShopDisplay != null)
            {
                grabConsumed = true;
                Upgrades?.RequestBuy(CurrentShopDisplay.ItemId);
            }
            else if (keys.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentTv != null)
            {
                grabConsumed = true;
                SunkCost.World.ShipControls ship = GetComponent<SunkCost.World.ShipControls>();
                if (ship != null) ship.RequestTvNext();
            }
            else if (keys.eKey.wasPressedThisFrame && CurrentTarget == null && CurrentCabinControl != CabinControl.None)
            {
                grabConsumed = true;
                SunkCost.World.ShipControls ship = GetComponent<SunkCost.World.ShipControls>();
                if (ship == null) { }
                else if (CurrentCabinControl == CabinControl.DeckCabin) ship.RequestCabin();
                else ship.RequestCar();
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

        private void UpdatePatchHold(Keyboard keys)
        {
            HQPlayerController patient = CurrentTarget == null ? CurrentPatient : null;
            bool leaking = patient != null && patient.Vitals != null && patient.Vitals.Leaking;
            if (!leaking || !keys.eKey.isPressed || patient != patchPatient)
            {
                patchPatient = leaking && keys.eKey.wasPressedThisFrame ? patient : null;
                patchHeldSince = patchPatient != null ? Time.unscaledTime : -1f;
                PatchProgress = 0f;
                if (patchPatient != null) grabConsumed = true;
                return;
            }
            float seconds = Vitals != null ? Vitals.Settings.TeammatePatchSeconds : 3f;
            PatchProgress = Mathf.Clamp01((Time.unscaledTime - patchHeldSince) / Mathf.Max(0.1f, seconds));
            if (PatchProgress < 1f) return;
            Vitals?.RequestPatchTeammate(patient);
            ClearPatch();
            grabConsumed = true;
        }

        private void ClearPatch()
        {
            patchPatient = null;
            patchHeldSince = -1f;
            PatchProgress = 0f;
        }

        private void Look()
        {
            Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // The owner tells the server its pitch when it moved enough, at most 10×/s.
        private void SendPitchIfDue()
        {
            if (!IsSpawned || Time.unscaledTime < nextPitchSendAt) return;
            if (!float.IsNaN(sentPitch) && Mathf.Abs(pitch - sentPitch) < PitchSendThreshold) return;
            sentPitch = pitch;
            nextPitchSendAt = Time.unscaledTime + PitchSendInterval;
            ServerSetLookPitch((sbyte)Mathf.RoundToInt(Mathf.Clamp(pitch, -80f, 80f)));
        }

        [ServerRpc]
        private void ServerSetLookPitch(sbyte value)
        {
            lookPitch.Value = (sbyte)Mathf.Clamp(value, -80, 80);
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
            if (now < dashUntil) planar = dashDirection * dashSpeed; // the burst: steering and sprint do not matter for a quarter second; gravity goes on (an air dash is allowed — Dan wants to try it)
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
            // The same blend both ways (Dan, 15 September 2026: no snap down). The
            // eye never leaves the taller of the two capsules: going down it is still
            // inside the standing one it had room for, going up standing was only
            // accepted with headroom.
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
            if (!locked) clearance?.ResetView(); // a new world: no old safe point, no blend across it
            verticalSpeed = 0f;
            grabBufferedUntil = -1f;
            grabConsumed = true;
            jumpBufferedUntil = float.NegativeInfinity;
            lastFlags = CollisionFlags.None;
            CurrentTarget = null;
            CurrentButton = null;
            CurrentCabinControl = CabinControl.None;
            CurrentPatient = null;
            ClearPatch();
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
            // The capsule's physics pose takes the new spot before it is enabled again:
            // a CharacterController enabled over a stale pose snapped a revived guest
            // back to where it had been (17 September 2026).
            Physics.SyncTransforms();
            controller.enabled = wasEnabled;
            clearance?.ResetView();
            verticalSpeed = 0f;
            jumpBufferedUntil = float.NegativeInfinity;
            lastFlags = CollisionFlags.None;
            FishNet.Component.Transforming.NetworkTransform networkTransform = GetComponent<FishNet.Component.Transforming.NetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport();
        }

        public float Yaw => transform.eulerAngles.y;

        // ---- death (card 1) --------------------------------------------------------------

        public void RequestDebugDeath()
        {
            if (!IsOwner || !Debug.isDebugBuild || dead.Value) return;
            ServerRequestDebugDeath();
        }

        [ServerRpc]
        private void ServerRequestDebugDeath(NetworkConnection sender = null)
        {
            if (!Debug.isDebugBuild) return; // development builds and the editor, like ServerRequestDebugAirDown
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            if (flow == null) return;
            if (!flow.ServerKill(sender, out string why)) Debug.Log("[Death] refused for " + SunkCost.World.WorldSceneFlow.DisplayName(sender) + ": " + why);
        }

        [Server]
        public void ServerSetDead(bool value) => dead.Value = value;

        // The menu's Unstuck (Dan, 18 September 2026): the server puts you back on
        // a known spot of the world you are in (WorldSceneFlow.ServerUnstuck).
        public void RequestUnstuck()
        {
            if (!IsOwner) return;
            ServerRequestUnstuck();
        }

        [ServerRpc]
        private void ServerRequestUnstuck(NetworkConnection sender = null)
        {
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            if (flow == null) return;
            if (!flow.ServerUnstuck(sender, out string why)) Debug.Log("[Unstuck] refused for " + SunkCost.World.WorldSceneFlow.DisplayName(sender) + ": " + why);
        }

        // Left click while dead: the next living player (card 2). The server
        // cycles; the client only asks.
        public void RequestNextSpectate()
        {
            if (!IsOwner || !dead.Value) return;
            ServerRequestNextSpectate();
        }

        [ServerRpc]
        private void ServerRequestNextSpectate(NetworkConnection sender = null)
        {
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            if (flow != null) flow.ServerSpectateNext(sender);
        }

        // The server moves a dead player (to the ship, or up again at End day); the
        // owner simulates itself, so it is the owner that stands there.
        [TargetRpc]
        public void TargetPlace(NetworkConnection target, Vector3 position, float yawDegrees)
        {
            TeleportLocal(position, yawDegrees);
        }

        private void OnDeadChanged(bool previous, bool next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer (the host sees both passes)
            ApplyDead(next);
        }

        private bool appliedDead;
        private void ApplyDead(bool value)
        {
            appliedDead = value;
            if (controller != null)
            {
                bool on = !value && !travelLocked;
                if (on && !controller.enabled) Physics.SyncTransforms(); // see TeleportLocal: never enable the capsule over a stale pose
                controller.enabled = on;
            }
            if (bodyVisual != null)
            {
                foreach (Renderer renderer in bodyVisual.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = !value && (!IsOwner || HeadSplit != null);
                if (!value && IsOwner && HeadSplit != null) HeadSplit.SetHeadShown(false);
            }
            GetComponent<PlayerHands>()?.SetVisible(!value); // the arms are not under the body model
            if (!value) return;
            CurrentTarget = null;
            CurrentButton = null;
            CurrentCabinControl = CabinControl.None;
            CurrentPatient = null;
            ClearPatch();
            verticalSpeed = 0f;
            grabBufferedUntil = -1f;
            grabConsumed = true;
        }

        // Held and stowed items have their colliders off, so only loose items can be
        // selected. Targeting checks eyes-to-surface distance and line of sight.
        // A remote copy's eyes for watchers, after the NetworkTransform's move this
        // frame: the replicated pitch eased (the 20 Hz steps read as a turn of the
        // head), the position and yaw eased behind the transform (see EyeSmoothSeconds).
        private void LateUpdate()
        {
            if (IsOwner) return;
            float dt = Time.deltaTime;
            // A remote copy's lamp shows while its diver is listed below (its Unity
            // scene on a client is not its world) and alive; the switch does the rest.
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            SetHeadlampEnabled(day != null && day.IsBelow(OwnerId) && !dead.Value);
            remotePitch = Mathf.LerpAngle(remotePitch, lookPitch.Value, 1f - Mathf.Exp(-dt / PitchSmoothSeconds));
            if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(remotePitch, 0f, 0f);
            Vector3 target = EyeAnchor.position;
            float yaw = Yaw;
            if (!eyesPrimed || (target - eyePosition).sqrMagnitude > EyeSnapMetres * EyeSnapMetres)
            {
                eyePosition = target; eyeYaw = yaw; eyesPrimed = true;
                return;
            }
            float k = 1f - Mathf.Exp(-dt / EyeSmoothSeconds);
            eyePosition = Vector3.Lerp(eyePosition, target, k);
            eyeYaw = Mathf.LerpAngle(eyeYaw, yaw, k);
        }

        private void UpdateTarget()
        {
            CurrentTarget = null;
            CurrentColourPanel = null;
            CurrentQuotaBoard = null;
            CurrentShopDisplay = null;
            CurrentTv = null;
            CurrentPatient = null;
            Transform eye = playerCamera.transform;
            CurrentTarget = InteractionTargeting.Find(eye.position, eye.forward, transform, interactReach, grabAimRadius);
            CurrentButton = null;
            CurrentCabinControl = CabinControl.None;
            if (CurrentTarget != null) return;
            Transform pressed = InteractionTargeting.FindPressable(eye.position, eye.forward, transform, interactReach);
            if (pressed == null) return;
            // A living teammate's capsule under the dot (the patch; a dead one is a body, an item).
            HQPlayerController teammate = pressed.GetComponentInParent<HQPlayerController>();
            if (teammate != null && teammate != this && !teammate.IsDead) { CurrentPatient = teammate; return; }
            CurrentButton = pressed.GetComponentInParent<SunkCost.World.MonitorButton>();
            if (CurrentButton != null) return;
            CurrentColourPanel = pressed.GetComponentInParent<SunkCost.World.ColourPanel>();
            if (CurrentColourPanel != null) return;
            CurrentQuotaBoard = pressed.GetComponentInParent<SunkCost.World.QuotaBoard>();
            if (CurrentQuotaBoard != null) return;
            CurrentShopDisplay = pressed.GetComponentInParent<SunkCost.Shop.ShopDisplay>();
            if (CurrentShopDisplay != null) return;
            if (pressed.name == SunkCost.World.ShipParts.TvScreenName) { CurrentTv = pressed.GetComponentInParent<SunkCost.World.ShipTV>(); if (CurrentTv != null) return; }
            if (pressed.GetComponentInParent<SunkCost.Diving.ElevatorControlPanel>() != null) CurrentCabinControl = CabinControl.Car;
            else if (pressed.name == SunkCost.World.ShipParts.DeckCabinButtonName && pressed.GetComponentInParent<SunkCost.World.ShipParts>() != null) CurrentCabinControl = CabinControl.DeckCabin;
        }

        public void SetBodyColour(Color colour)
        {
            if (bodyRenderer != null) bodyRenderer.material.color = colour;
        }

        public Color BodyColour => bodyRenderer != null ? bodyRenderer.material.color : Color.clear;

        private void SetLocalPresentation(bool active)
        {
            if (playerCamera != null)
            {
                playerCamera.enabled = active;
                AudioListener listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = active;
            }
            // The owner sees its own body from the shoulders down, its arms and what
            // they hold, but not its head (which sits right under the camera);
            // friends see it all. Without a head split the owner sees no body at all.
            if (bodyVisual != null)
            {
                bool bodyForOwner = HeadSplit != null;
                foreach (Renderer renderer in bodyVisual.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = !active || bodyForOwner;
                if (active && bodyForOwner) HeadSplit.SetHeadShown(false);
            }
        }
    }
}
