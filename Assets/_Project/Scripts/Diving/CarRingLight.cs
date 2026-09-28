using UnityEngine;

namespace SunkCost.Diving
{
    // The glowing ring in the elevator car's roof (the car model's CarLight slot)
    // follows the car's own point light: the dive car's "Cabin Light", which the
    // Elevator Ghost turns green, or the deck cabin's "Tube Light". Pure presentation
    // on every peer: the light's colour is already derived from replicated state
    // (ElevatorGhostLight), so the ring shows the same on every screen, the
    // spectators' and the TV's. It runs after the ghost light has set the colour
    // this frame (execution order 100, docs/ELEVATOR_LOOK.md §3).
    [DefaultExecutionOrder(100)]
    public sealed class CarRingLight : MonoBehaviour
    {
        // The material the model's light slot wears (ShipModelSetup, ElevatorCar slot 2).
        public const string SlotMaterialPrefix = "ElevatorCarLight";

        [SerializeField] private Light source;
        [Tooltip("Emission = the light's colour times this (HDR).")]
        [SerializeField] private float intensity = 2f;
        [Tooltip("The ring's colour while no light drives it.")]
        [SerializeField] private Color idleColour = new(1f, 0.95f, 0.85f);

        private Renderer[] renderers = System.Array.Empty<Renderer>();
        private int[] slots = System.Array.Empty<int>();
        private MaterialPropertyBlock block;

        public Color CurrentEmission { get; private set; }
        public Light Source => source;

        public void Configure(Light light) => source = light;

        private void Awake() => FindSlots();

        private void FindSlots()
        {
            var foundRenderers = new System.Collections.Generic.List<Renderer>();
            var foundSlots = new System.Collections.Generic.List<int>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                Material[] shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] != null && shared[i].name.StartsWith(SlotMaterialPrefix)) { foundRenderers.Add(r); foundSlots.Add(i); }
            }
            renderers = foundRenderers.ToArray();
            slots = foundSlots.ToArray();
            block ??= new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            Color colour = source != null && source.isActiveAndEnabled ? source.color : idleColour;
            CurrentEmission = colour * intensity;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block, slots[i]);
                block.SetColor("_EmissionColor", CurrentEmission);
                r.SetPropertyBlock(block, slots[i]);
            }
        }
    }
}
