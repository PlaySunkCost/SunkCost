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
        // A leak (the monsters, 20 September 2026): the tank drains LeakDrainMultiplier
        // times faster until a teammate or a patch kit closes it. Server-written;
        // every peer reads it for the visor's LEAK and the hiss.
        private readonly SyncVar<bool> leaking = new(false);
        // The run day a friend last patched this diver (once per day; server-written,
        // every peer reads it for the prompt).
        private readonly SyncVar<int> friendPatchDay = new(-1);
        // The owner's word back from a patch request: "Patched Skipper", or the refusal.
        private string patchNotice = string.Empty;
        private float patchNoticeUntil;
        private const float PatchNoticeSeconds = 2.5f;

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
        public bool Leaking => leaking.Value;
        public event System.Action<bool> LeakChanged; // every peer, from the SyncVar
        public bool FriendPatchedToday => WorldSceneFlow.Instance != null && CrewDayState.Instance != null && friendPatchDay.Value == CrewDayState.Instance.Day;
        public string PatchNotice => Time.unscaledTime < patchNoticeUntil ? patchNotice : string.Empty;
        // Server view, for the checks.
        public float ServerAirSeconds => airSeconds;
        public bool ServerSprinting { get; private set; }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            upgrades = GetComponent<PlayerUpgrades>();
            leaking.OnChange += (_, next, _) => LeakChanged?.Invoke(next);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerFill();
            ServerSetHealthFull();
            lastPosition = transform.position;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // The hiss of a leak, on every peer, from the replicated flag.
            if (GetComponent<SunkCost.Audio.PlayerLeakSounds>() == null) gameObject.AddComponent<SunkCost.Audio.PlayerLeakSounds>();
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
            ServerSetLeak(false);
            if (controller != null) controller.ServerSetLamp(true); // a new dive starts lit
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
            ServerSetLeak(false); // a new, full tank on the ship: the leak goes with the old one
        }

        // A revive at End day is a new life.
        [Server]
        public void ServerRevive()
        {
            inDive = false;
            ServerFill();
            ServerSetHealthFull();
            ServerSetLeak(false);
        }

        // ---- damage and leaks (the monsters, 20 September 2026) -------------------------

        // A monster's hit: `points` off health, and a leak if it asks for one. Only a
        // living diver with the suit on can be hurt; inside the sealed car going up
        // health floors at 1 as it does for the tank. At 0 the ordinary death.
        [Server]
        public bool ServerDamage(float points, bool leak, string cause)
        {
            if (!inDive || controller == null || controller.IsDead || points < 0f) return false;
            healthPoints = Mathf.Max(ridingReprieve ? 1f : 0f, healthPoints - points);
            byte shown = (byte)Mathf.CeilToInt(Mathf.Clamp(healthPoints, 0f, 255f));
            if (shown != health.Value) health.Value = shown;
            if (leak) ServerSetLeak(true);
            Debug.Log($"[Vitals] {WorldSceneFlow.DisplayName(Owner)} took {points:0} from {cause}: health {health.Value}{(leak ? ", leaking" : string.Empty)}");
            if (healthPoints <= 0f)
            {
                WorldSceneFlow flow = WorldSceneFlow.Instance;
                string why = string.Empty;
                if (flow != null && flow.ServerKill(Owner, out why, cause)) inDive = false;
                else if (flow != null) Debug.Log("[Vitals] " + WorldSceneFlow.DisplayName(Owner) + " was killed by " + cause + " but the kill was refused: " + why);
            }
            return true;
        }

        [Server]
        public void ServerSetLeak(bool value)
        {
            if (leaking.Value != value) leaking.Value = value;
        }

        // A patch: by a teammate's hands (free, once per day per patient — the next
        // one needs a kit) or by a patch kit (any number). Refused with a reason.
        [Server]
        public bool ServerPatchLeak(bool byTeammate, int runDay, out string why)
        {
            why = string.Empty;
            if (!leaking.Value) { why = "No leak"; return false; }
            if (byTeammate)
            {
                if (friendPatchDay.Value == runDay) { why = "Patched by a friend once today already — a kit now"; return false; }
                friendPatchDay.Value = runDay;
            }
            ServerSetLeak(false);
            return true;
        }
        public bool ServerTeammatePatchUsedToday(int runDay) => friendPatchDay.Value == runDay;

#if UNITY_EDITOR
        // Editor checks only: full health and no leak for the next row.
        [Server]
        public void ServerHealForChecks() { ServerSetHealthFull(); ServerSetLeak(false); }
#endif

        // The owner held E on a leaking teammate long enough: ask the server. The
        // server checks everything again — both living and below, the reach from
        // its own view, the leak, the once a day — and answers the owner only.
        public void RequestPatchTeammate(HQPlayerController patient)
        {
            if (!IsOwner || patient == null || patient.NetworkObject == null) return;
            ServerRequestPatch(patient.NetworkObject);
        }

        [ServerRpc]
        private void ServerRequestPatch(NetworkObject patientObject, NetworkConnection sender = null)
        {
            if (sender != Owner || controller == null || controller.IsDead) return;
            HQPlayerController patient = patientObject != null ? patientObject.GetComponent<HQPlayerController>() : null;
            PlayerVitals theirs = patient != null ? patient.Vitals : null;
            CrewDayState day = CrewDayState.Instance;
            string why;
            if (patient == null || patient == controller || theirs == null) why = "Nobody to patch";
            else if (patient.IsDead) why = "Too late";
            else if (!inDive || !theirs.inDive) why = "Patches happen below";
            else if (Vector3.Distance(transform.position, patient.transform.position) > Settings.TeammatePatchReach + 1.5f) why = "Too far"; // the server sees copies a tick behind
            else if (theirs.ServerPatchLeak(true, day != null ? day.Day : 0, out why))
            {
                string mine = WorldSceneFlow.DisplayName(Owner), theirName = WorldSceneFlow.DisplayName(patient.Owner);
                Debug.Log($"[Leak] {mine} patched {theirName}'s suit by hand");
                TargetPatchNotice(sender, "Patched " + theirName);
                theirs.TargetPatchNotice(patient.Owner, "Patched by " + mine);
                return;
            }
            TargetPatchNotice(sender, why);
        }

        [TargetRpc]
        private void TargetPatchNotice(NetworkConnection connection, string text)
        {
            patchNotice = text ?? string.Empty;
            patchNoticeUntil = Time.unscaledTime + PatchNoticeSeconds;
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
                float drain = (ServerSprinting ? Settings.SprintDrainMultiplier : 1f) * (leaking.Value ? Settings.LeakDrainMultiplier : 1f);
                airSeconds = Mathf.Max(0f, airSeconds - dt * drain);
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
