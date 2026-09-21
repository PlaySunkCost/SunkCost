using SunkCost.Noise;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Player
{
    // A diver's footsteps into the NoiseSystem (docs/DESIGN.md §6; decided with
    // Dan, 17 September 2026): the server counts the ground each player covers
    // in the dive world and emits a Footstep every walkStepMetres (radius
    // walkRadius) or, sprinting, a Sprint every sprintStepMetres (radius
    // sprintRadius); crouching emits nothing at all — silent, not quieter.
    // Sprinting is judged the way PlayerVitals judges it, from the copy's own
    // speed; crouching from the server-accepted stance. Server only: clients
    // never emit (docs/NETWORK_CONTRACT.md section 3). Nothing replicated —
    // the events feed listeners on the server (creatures, the editor gizmo).
    public sealed class PlayerNoise : MonoBehaviour
    {
        private HQPlayerController controller;
        private PlayerStance stance;
        private Vector3 lastPosition;
        private float sinceStep;   // metres of ground since the last footstep
        private bool primed;

        // For the checks.
        public int Emitted { get; private set; }
        public NoiseKind LastKind { get; private set; }

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            stance = GetComponent<PlayerStance>();
        }

        private void Update()
        {
            if (controller == null || !controller.IsServerStarted || !NoiseSystem.IsServer) return;
            Vector3 position = transform.position;
            if (!primed) { lastPosition = position; primed = true; return; }
            Vector3 flat = position - lastPosition; flat.y = 0f;
            float moved = flat.magnitude;
            lastPosition = position;
            if (controller.IsDead || controller.gameObject.scene != WorldScenes.Scene(WorldId.Dive)) { sinceStep = 0f; return; }
            if (moved > 3f) { sinceStep = 0f; return; } // a teleport, not a stride
            bool crouched = stance != null && stance.Accepted.Crouched;
            if (crouched) { sinceStep = 0f; return; } // silent (Dan)
            if (controller.ServerDashing) { sinceStep = 0f; return; } // the dash is its own, louder noise (HQPlayerController)
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float speed = moved / dt;
            float factor = controller.SpeedFactor;
            bool sprinting = speed > 0.5f * (controller.WalkSpeed + controller.SprintSpeed) * factor;
            NoiseSettings settings = NoiseSettings.Get();
            sinceStep += moved;
            float stride = sprinting ? settings.SprintStepMetres : settings.WalkStepMetres;
            if (sinceStep < stride) return;
            sinceStep -= stride;
            NoiseKind kind = sprinting ? NoiseKind.Sprint : NoiseKind.Footstep;
            NoiseSystem.Emit(position, sprinting ? settings.SprintRadius : settings.WalkRadius, kind, controller.NetworkObject != null ? controller.ObjectId : 0);
            Emitted++;
            LastKind = kind;
        }
    }
}
