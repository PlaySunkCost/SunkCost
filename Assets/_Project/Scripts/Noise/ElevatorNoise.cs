using SunkCost.World;
using UnityEngine;

namespace SunkCost.Noise
{
    // The moving car into the NoiseSystem (docs/DESIGN.md §1: "riding up is safe
    // for you and loud for everyone else"; Dan, 17 September 2026: "a big sound").
    // Server only, attached to the crew's day state when the server starts: while
    // the replicated elevator phase says the car moves, an Elevator event at the
    // car every elevatorEmitInterval seconds, radius elevatorRadius, and one the
    // moment it starts. Nothing replicated — what players hear is ElevatorSounds.
    public sealed class ElevatorNoise : MonoBehaviour
    {
        private float nextEmitAt;
        private bool wasMoving;

        public int Emitted { get; private set; }

        private void Update()
        {
            if (!NoiseSystem.IsServer) return;
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return;
            var state = day.Elevator.State;
            bool moving = state == Diving.ElevatorState.Ascending || state == Diving.ElevatorState.Descending;
            if (!moving) { wasMoving = false; return; }
            Diving.ElevatorController car = WorldSceneFlow.FindCar();
            if (car == null) return;
            NoiseSettings settings = NoiseSettings.Get();
            if (!wasMoving || Time.unscaledTime >= nextEmitAt)
            {
                NoiseSystem.Emit(car.transform.position, settings.ElevatorRadius, NoiseKind.Elevator);
                Emitted++;
                nextEmitAt = Time.unscaledTime + settings.ElevatorEmitInterval;
            }
            wasMoving = true;
        }
    }
}
