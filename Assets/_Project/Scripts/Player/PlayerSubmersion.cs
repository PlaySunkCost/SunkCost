using System;
using SunkCost.Net;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Player
{
    // Whether this player's eyes are under the water that stands in the dive
    // site's tube (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md section 6). Local
    // presentation and a hook: nothing replicated, nothing counted yet — the air
    // card and the sounds subscribe to SubmersionChanged. Read by the F3 overlay
    // ("underwater=True depth=12.3") so a tester can see it without diving in.
    public sealed class PlayerSubmersion : MonoBehaviour, INetworkDebugInfo
    {
        private HQPlayerController controller;
        private bool submerged;

        public bool IsSubmerged => submerged;
        public float DepthMeters { get; private set; }
        public event Action<bool> SubmersionChanged;

        public string DebugStatus => submerged ? $"underwater=True depth={DepthMeters:0.0}" : "underwater=False";
        public bool? WriterOverride => null;

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
        }

        private void LateUpdate()
        {
            bool now = false;
            DepthMeters = 0f;
            if (controller != null && gameObject.scene == WorldScenes.Scene(WorldId.Dive))
            {
                SunkCost.Diving.ElevatorController car = WorldSceneFlow.FindCar();
                float seaLevel = car != null ? car.SeaLevelY : SeaLevelFallback;
                float eyeY = controller.EyePosition.y;
                now = SunkCost.Diving.ElevatorMath.IsBelowSurface(seaLevel, eyeY);
                DepthMeters = now ? seaLevel - eyeY : 0f;
            }
            if (now == submerged) return;
            submerged = now;
            SubmersionChanged?.Invoke(now);
        }

        // Only used when the site is loaded without its car (never in a ride).
        private const float SeaLevelFallback = -1f;
    }
}
