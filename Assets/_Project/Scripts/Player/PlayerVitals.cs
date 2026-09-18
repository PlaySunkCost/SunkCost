using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Player
{
    // Air and health (docs/DESIGN.md §3; the contract's "Vitals" row). Decided
    // with Dan, 17 September 2026: one tank per dive, no refills; the tank
    // counts down for as long as the suit is on — the whole time the player's
    // object is in the dive world, the car ride up included — and is full again
    // on the ship. Sprinting drains it faster; carried weight does not. At zero
    // air, health goes: SuffocationDamagePerSecond, and at zero health the
    // ordinary death (WorldSceneFlow.ServerKill — body, scatter, spectating).
    // Health lost stays lost for the dive and is full again at the next ride
    // down; a revive at End day is a new life.
    //
    // Server-owned: the server drains, damages and kills; clients only read the
    // two replicated values for the visor (the owner's, a spectator's, the TV's —
    // the one PlayerHudUI path). Sprinting is judged by the server from the
    // copy's own speed (client-authoritative movement, no flag from the owner).
    // Replicated as bytes: air in half-percents (0..200), health in points.
    public sealed class PlayerVitals : NetworkBehaviour
    {
        public const byte AirScale = 200;

        [SerializeField] private PlayerVitalsSettings settings;

        private readonly SyncVar<byte> air = new(AirScale);
        private readonly SyncVar<byte> health = new(100);

        private HQPlayerController controller;
        private float airSeconds = -1f;      // server: the exact tank; -1 = not yet primed
        private float healthPoints = -1f;
        private Vector3 lastPosition;
        private bool inDive;                 // server: the suit is on
        private bool ridingReprieve;         // server: health floors at 1 inside the car (see Tick)

        public PlayerVitalsSettings Settings => PlayerVitalsSettings.Resolve(settings);
        // The tank this player carries: the settings' seconds, times the large-tank
        // upgrade when bought (PlayerUpgrades; the shop, 18 September 2026).
        public float TankSeconds => Settings.EffectiveTankSeconds * (upgrades != null ? upgrades.TankMultiplier : 1f);
        private PlayerUpgrades upgrades;
        public float AirFraction => air.Value / (float)AirScale;
        public float HealthFraction => health.Value / (float)Mathf.Max(1, Settings.MaxHealth);
        public int Health => health.Value;
        public bool AirLow => AirFraction < Settings.LowAirFraction;
        public bool AirEmpty => air.Value == 0;
        public bool HealthLow => HealthFraction < Settings.LowHealthFraction;
        // Server view, for the checks.
        public float ServerAirSeconds => airSeconds;
        public bool ServerSprinting { get; private set; }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            upgrades = GetComponent<PlayerUpgrades>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerFill();
            ServerSetHealthFull();
            lastPosition = transform.position;
        }

        // ---- server ------------------------------------------------------------------

        // A new tank: the ride down (ServerBeginDive) and the arrival on the ship.
        [Server]
        private void ServerFill()
        {
            airSeconds = TankSeconds;
            air.Value = AirScale;
        }

        [Server]
        private void ServerSetHealthFull()
        {
            healthPoints = Settings.MaxHealth;
            health.Value = (byte)Mathf.Clamp(Settings.MaxHealth, 0, 255);
        }

        // The suit goes on: the player's object stands in the dive world (the ride
        // down's load has landed). Full tank, full health — a new dive.
        [Server]
        public void ServerBeginDive()
        {
            ServerFill();
            ServerSetHealthFull();
            inDive = true;
            lastPosition = transform.position;
        }

        // The suit comes off: back on the ship. A new tank waits; health stays.
        [Server]
        public void ServerEndDive()
        {
            inDive = false;
            ridingReprieve = false;
            ServerFill();
        }

        // A revive at End day is a new life.
        [Server]
        public void ServerRevive()
        {
            inDive = false;
            ServerFill();
            ServerSetHealthFull();
        }

        // Inside the sealed car the suit is still on and the tank still counts, but
        // dying mid-ride (a body inside a moving car, across a scene move) is not a
        // path that exists: health floors at 1 until the ride ends (the car saved
        // you, barely). Decided for now, 17 September 2026; see the design.
        [Server]
        public void ServerSetRidingReprieve(bool value) => ridingReprieve = value;

        private void Update()
        {
            if (!IsServerStarted) return;
            ServerTick(Time.deltaTime);
        }

        [Server]
        private void ServerTick(float dt)
        {
            if (controller == null || controller.IsDead || !inDive || dt <= 0f) return;
            Vector3 position = transform.position;
            Vector3 flat = position - lastPosition; flat.y = 0f;
            float speed = flat.magnitude / dt;
            lastPosition = position;
            // Sprinting, judged from the copy's own speed: above the midpoint of walk
            // and sprint at the server-owned weight factor.
            float factor = controller.SpeedFactor;
            float threshold = 0.5f * (controller.WalkSpeed + controller.SprintSpeed) * factor;
            ServerSprinting = speed > threshold;
            if (airSeconds > 0f)
            {
                airSeconds = Mathf.Max(0f, airSeconds - dt * (ServerSprinting ? Settings.SprintDrainMultiplier : 1f));
                byte next = (byte)Mathf.CeilToInt(AirScale * Mathf.Clamp01(airSeconds / Mathf.Max(0.001f, TankSeconds)));
                if (airSeconds <= 0f) next = 0;
                if (next != air.Value) air.Value = next;
                return;
            }
            // Empty: health goes.
            healthPoints = Mathf.Max(ridingReprieve ? 1f : 0f, healthPoints - dt * Settings.SuffocationDamagePerSecond);
            byte shown = (byte)Mathf.CeilToInt(Mathf.Clamp(healthPoints, 0f, 255f));
            if (shown != health.Value) health.Value = shown;
            if (healthPoints <= 0f)
            {
                WorldSceneFlow flow = WorldSceneFlow.Instance;
                if (flow == null) return;
                if (flow.ServerKill(Owner, out string why, "suffocated")) inDive = false;
                else Debug.Log("[Vitals] " + WorldSceneFlow.DisplayName(Owner) + " suffocated but the kill was refused: " + why);
            }
        }

        // An air tank breathed from (AirTankItem): a fraction of the tank back, never
        // past full, only while the suit is on. Returns what was actually added.
        [Server]
        public float ServerAddAir(float fraction)
        {
            if (!inDive || controller == null || controller.IsDead) return 0f;
            float tank = TankSeconds;
            float before = Mathf.Max(0f, airSeconds);
            airSeconds = Mathf.Min(tank, before + fraction * tank);
            air.Value = (byte)Mathf.CeilToInt(AirScale * Mathf.Clamp01(airSeconds / Mathf.Max(0.001f, tank)));
            return airSeconds - before;
        }
        public bool ServerSuitOn => inDive;

        // The debug key (L; HQPlayerController) and the peer's air_down: a step off
        // the tank, below only. Development builds and the editor only.
        public void RequestDebugAirDown()
        {
            if (!IsOwner || !Debug.isDebugBuild) return;
            ServerRequestDebugAirDown();
        }

        [ServerRpc]
        private void ServerRequestDebugAirDown(NetworkConnection sender = null)
        {
            if (!Debug.isDebugBuild || !inDive || controller == null || controller.IsDead) return;
            airSeconds = Mathf.Max(0f, airSeconds - Settings.DebugAirStepFraction * TankSeconds);
            byte next = (byte)Mathf.CeilToInt(AirScale * Mathf.Clamp01(airSeconds / Mathf.Max(0.001f, TankSeconds)));
            if (airSeconds <= 0f) next = 0;
            air.Value = next;
        }
    }
}
