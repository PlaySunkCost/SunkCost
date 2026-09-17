using UnityEngine;

namespace SunkCost.Noise
{
    // How far things carry, in metres (docs/DESIGN.md §6, "the noise tells the
    // ocean where you are"; decided with Dan, 17 September 2026). Provisional
    // numbers; changing them is not a design change. One asset in Resources
    // (NoiseSettings), created by NoiseSetup; a missing asset falls back to these.
    [CreateAssetMenu(fileName = "NoiseSettings", menuName = "Sunk Cost/Noise Settings")]
    public sealed class NoiseSettings : ScriptableObject
    {
        [Header("Footsteps (server; the dive world only)")]
        [Tooltip("Metres of ground covered per footstep while walking.")]
        [SerializeField] private float walkStepMetres = 0.75f;
        [Tooltip("How far a walking footstep carries, metres.")]
        [SerializeField] private float walkRadius = 6f;
        [Tooltip("Metres per footstep while sprinting.")]
        [SerializeField] private float sprintStepMetres = 1.1f;
        [Tooltip("How far a sprinting footstep carries, metres.")]
        [SerializeField] private float sprintRadius = 15f;
        // Crouching makes no footstep at all (Dan, 17 September 2026): silent, not quieter.

        [Header("Elevator (server)")]
        [Tooltip("How far the moving car carries, metres — big: riding up is loud for everyone else (design §1).")]
        [SerializeField] private float elevatorRadius = 60f;
        [Tooltip("Seconds between the car's noise events while it moves.")]
        [SerializeField] private float elevatorEmitInterval = 1f;

        public float WalkStepMetres => walkStepMetres;
        public float WalkRadius => walkRadius;
        public float SprintStepMetres => sprintStepMetres;
        public float SprintRadius => sprintRadius;
        public float ElevatorRadius => elevatorRadius;
        public float ElevatorEmitInterval => elevatorEmitInterval;

        private static NoiseSettings loaded;
        public static NoiseSettings Get()
        {
            if (loaded != null) return loaded;
            loaded = Resources.Load<NoiseSettings>("NoiseSettings");
            if (loaded == null) { loaded = CreateInstance<NoiseSettings>(); loaded.hideFlags = HideFlags.HideAndDontSave; }
            return loaded;
        }
    }
}
