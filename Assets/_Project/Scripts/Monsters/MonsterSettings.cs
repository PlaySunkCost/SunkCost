using UnityEngine;

namespace SunkCost.Monsters
{
    // Every monster number in one place (docs/DESIGN.md §6 "The monsters", 20
    // September 2026). Provisional until a playtest; the server reads them, the
    // clients read only what presentation needs. Resources/MonsterSettings.
    [CreateAssetMenu(fileName = "MonsterSettings", menuName = "Sunk Cost/Monster Settings")]
    public sealed class MonsterSettings : ScriptableObject
    {
        public const string ResourceName = "MonsterSettings";

        [Header("The roster")]
        [Tooltip("How many of the six walkers roam a dive, drawn at random when the site loads for the day (Dan: three for now).")]
        [SerializeField, Range(0, 6)] private int monstersPerDive = 3;
        [Tooltip("A monster spawns at least this far from the tube's doorway, flat.")]
        [SerializeField] private float spawnMinMeters = 22f;
        [Tooltip("And at most this far from the shaft (the seabed is 150 m across; its walls stand at 75).")]
        [SerializeField] private float roamHalfMeters = 60f;
        [Tooltip("Never this close to a diver when it appears.")]
        [SerializeField] private float spawnClearOfDiversMeters = 12f;
        [Tooltip("Divers stand on the site before the monsters wake: the roster waits this long after the first one lands.")]
        [SerializeField] private float wakeDelaySeconds = 2f;

        [Header("The safe ground (the deck is always safe)")]
        [Tooltip("No walker comes closer than this, flat, to the shaft's centre: it stops at the car's doorway and never passes the tube's gate.")]
        [SerializeField] private float safeZoneMeters = 4.5f;
        [Tooltip("A diver inside the car or this close to the shaft's centre cannot be struck or shot.")]
        [SerializeField] private float tubeSafeMeters = 3.2f;

        [Header("The Elevator Ghost")]
        [Tooltip("When the car comes back down for divers still below, the chance it arrives green.")]
        [SerializeField, Range(0f, 1f)] private float ghostChance = 0.25f;
        [Tooltip("How long the car stays green; walking in during the green is death.")]
        [SerializeField] private float ghostSeconds = 30f;
        [Tooltip("At most once a day.")]
        [SerializeField] private bool ghostOncePerDay = true;

        [Header("Sight (the Walker, the Charger)")]
        [Tooltip("How far a monster can see a diver whose lamp is on; the walls do the rest.")]
        [SerializeField] private float sightMeters = 25f;
        [Tooltip("With the lamp off a diver is seen only this close.")]
        [SerializeField] private float sightDarkMeters = 6f;

        [Header("The Long Walker")]
        [Tooltip("Its speed as a fraction of a diver's walking speed: you outwalk it unless you are heavy or you stop.")]
        [SerializeField] private float walkerSpeedFactor = 0.85f;

        [Header("The Weeping Angel")]
        [Tooltip("Frozen while inside any living diver's view within this many metres with a clear line of sight.")]
        [SerializeField] private float angelWatchMeters = 25f;
        [Tooltip("It notices divers this far and goes for the nearest; farther than that it waits.")]
        [SerializeField] private float angelWakeMeters = 45f;
        [Tooltip("Unwatched it moves at this fraction of a diver's sprint speed.")]
        [SerializeField] private float angelSpeedFactor = 1f;
        [Tooltip("Half the view angle, degrees, that counts as watching (the camera's field of view is 75 tall, wider across).")]
        [SerializeField] private float watchHalfAngleDeg = 40f;

        [Header("The Charger")]
        [SerializeField] private float chargerWindupSeconds = 1.5f;
        [SerializeField] private float chargerRushMeters = 12f;
        [Tooltip("The rush's speed as a fraction of a diver's sprint speed (2 = twice sprint).")]
        [SerializeField] private float chargerRushSpeedFactor = 2f;
        [SerializeField] private float chargerTurnSeconds = 3f;
        [SerializeField] private float chargerDamage = 35f;
        [Tooltip("The rush hits a diver within this many metres of its line.")]
        [SerializeField] private float chargerHitRadius = 0.9f;
        [Tooltip("It winds up when a seen diver is this close.")]
        [SerializeField] private float chargerRushFromMeters = 14f;

        [Header("The Lure")]
        [Tooltip("It sees a lit headlamp this far.")]
        [SerializeField] private float lureSeeMeters = 25f;
        [SerializeField] private float lureForgetSeconds = 8f;
        [SerializeField] private float lureDamage = 35f;
        [SerializeField] private float lureShotCooldownSeconds = 4f;
        [Tooltip("It drifts toward the beam at this fraction of walking speed.")]
        [SerializeField] private float lureApproachSpeedFactor = 0.5f;

        [Header("The Listener")]
        [SerializeField] private float listenerForgetSeconds = 10f;
        [SerializeField] private float listenerDamage = 35f;
        [SerializeField] private float listenerShotCooldownSeconds = 4f;
        [Tooltip("It walks toward the last sound at this fraction of walking speed.")]
        [SerializeField] private float listenerApproachSpeedFactor = 0.6f;

        [Header("Bolts (the Lure's light, the Listener's dark)")]
        [SerializeField] private float boltSpeed = 14f;
        [Tooltip("A bolt hits a diver within this many metres of its path.")]
        [SerializeField] private float boltHitRadius = 0.7f;
        [SerializeField] private float boltLifeSeconds = 3f;

        [Header("The Impostor")]
        [SerializeField] private float impostorDamage = 30f;
        [Tooltip("It walks toward its target at this fraction of walking speed until close, then chases at this fraction of sprint speed.")]
        [SerializeField] private float impostorApproachSpeedFactor = 0.6f;
        [SerializeField] private float impostorChaseSpeedFactor = 0.95f;
        [Tooltip("It starts chasing this close.")]
        [SerializeField] private float impostorChaseMeters = 8f;
        [Tooltip("It gives up after this long chasing, and runs off this long after a touch.")]
        [SerializeField] private float impostorChaseSeconds = 30f;
        [SerializeField] private float impostorRunSeconds = 30f;

        [Header("Every monster")]
        [Tooltip("A touch: closer than this, flat, from the monster's centre to the diver's.")]
        [SerializeField] private float reachMeters = 1.4f;
        [Tooltip("A monster that has drawn blood backs off this long before it can strike again.")]
        [SerializeField] private float strikeCooldownSeconds = 2f;
        [Tooltip("The Lure and the Listener keep this far from what they walk toward; they shoot, they do not touch.")]
        [SerializeField] private float shooterStandoffMeters = 3f;
        [Tooltip("The walk and sprint speeds a monster's fractions are of when no diver is on the site to read them from.")]
        [SerializeField] private float fallbackWalkSpeed = 4f;
        [SerializeField] private float fallbackSprintSpeed = 6f;

        public int MonstersPerDive => monstersPerDive;
        public float SpawnMinMeters => spawnMinMeters;
        public float RoamHalfMeters => roamHalfMeters;
        public float SpawnClearOfDiversMeters => spawnClearOfDiversMeters;
        public float WakeDelaySeconds => wakeDelaySeconds;
        public float SafeZoneMeters => safeZoneMeters;
        public float TubeSafeMeters => tubeSafeMeters;
        public float GhostChance => ghostChance;
        public float GhostSeconds => ghostSeconds;
        public bool GhostOncePerDay => ghostOncePerDay;
        public float SightMeters => sightMeters;
        public float SightDarkMeters => sightDarkMeters;
        public float WalkerSpeedFactor => walkerSpeedFactor;
        public float AngelWatchMeters => angelWatchMeters;
        public float AngelWakeMeters => angelWakeMeters;
        public float AngelSpeedFactor => angelSpeedFactor;
        public float WatchHalfAngleDeg => watchHalfAngleDeg;
        public float ChargerWindupSeconds => chargerWindupSeconds;
        public float ChargerRushMeters => chargerRushMeters;
        public float ChargerRushSpeedFactor => chargerRushSpeedFactor;
        public float ChargerTurnSeconds => chargerTurnSeconds;
        public float ChargerDamage => chargerDamage;
        public float ChargerHitRadius => chargerHitRadius;
        public float ChargerRushFromMeters => chargerRushFromMeters;
        public float LureSeeMeters => lureSeeMeters;
        public float LureForgetSeconds => lureForgetSeconds;
        public float LureDamage => lureDamage;
        public float LureShotCooldownSeconds => lureShotCooldownSeconds;
        public float LureApproachSpeedFactor => lureApproachSpeedFactor;
        public float ListenerForgetSeconds => listenerForgetSeconds;
        public float ListenerDamage => listenerDamage;
        public float ListenerShotCooldownSeconds => listenerShotCooldownSeconds;
        public float ListenerApproachSpeedFactor => listenerApproachSpeedFactor;
        public float BoltSpeed => boltSpeed;
        public float BoltHitRadius => boltHitRadius;
        public float BoltLifeSeconds => boltLifeSeconds;
        public float ImpostorDamage => impostorDamage;
        public float ImpostorApproachSpeedFactor => impostorApproachSpeedFactor;
        public float ImpostorChaseSpeedFactor => impostorChaseSpeedFactor;
        public float ImpostorChaseMeters => impostorChaseMeters;
        public float ImpostorChaseSeconds => impostorChaseSeconds;
        public float ImpostorRunSeconds => impostorRunSeconds;
        public float ReachMeters => reachMeters;
        public float StrikeCooldownSeconds => strikeCooldownSeconds;
        public float ShooterStandoffMeters => shooterStandoffMeters;
        // Close enough to touch: a hunter stops a little inside its reach.
        public float TouchStandoffMeters => reachMeters * 0.7f;
        public float FallbackWalkSpeed => fallbackWalkSpeed;
        public float FallbackSprintSpeed => fallbackSprintSpeed;

        // The editor's checks force a roster (an empty array: no monsters) and the
        // ghost's roll; null means the settings decide. Cleared by the matrices.
        public static MonsterKind[] RosterOverrideForTests;
        public static float? GhostChanceOverrideForTests;

        // The editor enters Play Mode without a domain reload, so a matrix's "no
        // monsters" override would outlive its run into Dan's next session (20
        // September 2026: "I just walked and didn't see any monster"). Every Play
        // Mode entry starts with the settings' own draw.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            RosterOverrideForTests = null;
            GhostChanceOverrideForTests = null;
            loaded = null;
            looked = false;
        }

        private static MonsterSettings loaded;
        private static bool looked;
        public static MonsterSettings Get()
        {
            if (loaded != null) return loaded;
            if (!looked) { looked = true; loaded = Resources.Load<MonsterSettings>(ResourceName); }
            if (loaded == null) { loaded = CreateInstance<MonsterSettings>(); loaded.hideFlags = HideFlags.HideAndDontSave; }
            return loaded;
        }
    }
}
