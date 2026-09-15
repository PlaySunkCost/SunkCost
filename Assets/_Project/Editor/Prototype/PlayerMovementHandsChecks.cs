using System;
using System.Collections.Generic;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Pure and asset checks for docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md:
    // the jump arithmetic, capsule dimensions, the arm solver, release direction
    // clamping, the settings asset and the prefab invariants. No Play Mode.
    public static class PlayerMovementHandsChecks
    {
        [MenuItem("Sunk Cost/Prototype/Run movement and hands checks")]
        public static void RunFromMenu() => Debug.Log(RunOrThrow());

        public static string RunOrThrow()
        {
            var errors = new List<string>();
            PlayerMovementSettings settings = AssetDatabase.LoadAssetAtPath<PlayerMovementSettings>(PlayerMovementHandsSetup.SettingsPath);
            if (settings == null) { errors.Add("PlayerMovementSettings asset missing (run Apply movement and hands setup)."); settings = PlayerMovementSettings.Resolve(null); }
            if (!settings.IsValid) errors.Add("PlayerMovementSettings is invalid.");

            // Jump table (plan section 3): 0 kg -> 0.65, half capacity -> 0.455, full -> 0.26 with the defaults.
            float capacity = WeightSettings.DefaultCapacityKg;
            Near(errors, PlayerMovementMath.JumpHeight(settings, 0f, capacity), settings.BaseJumpHeight, 1e-4f, "empty jump height");
            Near(errors, PlayerMovementMath.JumpHeight(settings, capacity * 0.5f, capacity), settings.BaseJumpHeight * (1f + settings.MinJumpHeightFactor) * 0.5f, 1e-4f, "half-capacity jump height");
            Near(errors, PlayerMovementMath.JumpHeight(settings, capacity, capacity), settings.BaseJumpHeight * settings.MinJumpHeightFactor, 1e-4f, "full-capacity jump height");
            Near(errors, PlayerMovementMath.JumpHeight(settings, capacity * 3f, capacity), settings.BaseJumpHeight * settings.MinJumpHeightFactor, 1e-4f, "overloaded jump height stays at the floor");
            float takeoff = PlayerMovementMath.TakeoffSpeed(0.65f, -9.81f);
            Near(errors, takeoff, Mathf.Sqrt(2f * 9.81f * 0.65f), 1e-4f, "takeoff speed for 0.65 m");
            if (PlayerMovementMath.TakeoffSpeed(0.65f, 0f) != 0f) errors.Add("zero gravity must give zero takeoff speed, not NaN.");

            // Capsule: feet fixed, height halves, radius unchanged, eyes inside.
            Near(errors, PlayerMovementMath.CapsuleCenter(settings, true).y, settings.CrouchHeight * 0.5f, 1e-5f, "crouched centre");
            Near(errors, PlayerMovementMath.CapsuleCenter(settings, false).y, settings.StandingHeight * 0.5f, 1e-5f, "standing centre");
            if (settings.CrouchHeight < 2f * settings.CapsuleRadius) errors.Add("crouch height must be at least twice the radius.");
            if (settings.CrouchEyeHeight >= settings.CrouchHeight) errors.Add("crouch eye height must be inside the crouched capsule.");
            if (settings.StandingEyeHeight >= settings.StandingHeight) errors.Add("standing eye height must be inside the standing capsule.");
            PlayerMovementMath.HeadroomCapsule(settings, out Vector3 bottom, out Vector3 top, out float radius);
            if (bottom.y - radius < settings.CrouchHeight - 1e-4f) errors.Add("headroom capsule starts below the crouched top.");
            if (top.y + radius < settings.StandingHeight - 1e-4f) errors.Add("headroom capsule does not reach the standing top.");

            // Arm solver: reachable, clamped, never NaN, elbow at the right lengths.
            ArmPoseSolver.Result reach = ArmPoseSolver.Solve(Vector3.zero, new Vector3(0.1f, -0.2f, 0.35f), Vector3.right, 0.28f, 0.26f);
            if (reach.Clamped) errors.Add("a target inside reach was clamped.");
            Near(errors, Vector3.Distance(Vector3.zero, reach.Elbow), 0.28f, 1e-4f, "upper arm length");
            Near(errors, Vector3.Distance(reach.Elbow, reach.Wrist), 0.26f, 1e-4f, "forearm length");
            ArmPoseSolver.Result far = ArmPoseSolver.Solve(Vector3.zero, new Vector3(0f, 0f, 5f), Vector3.right, 0.28f, 0.26f);
            if (!far.Clamped) errors.Add("a target beyond reach must be reported as clamped.");
            if (Vector3.Distance(Vector3.zero, far.Wrist) > 0.54f + 1e-3f) errors.Add("a clamped wrist must stay within reach.");
            if (float.IsNaN(far.Elbow.x) || float.IsNaN(reach.Elbow.y)) errors.Add("the arm solver produced NaN.");
            ArmPoseSolver.Result same = ArmPoseSolver.Solve(Vector3.zero, Vector3.zero, Vector3.right, 0.28f, 0.26f);
            if (float.IsNaN(same.Wrist.x)) errors.Add("a target on the shoulder produced NaN.");

            // Release direction follows the crosshair within the pitch range; a steeper look is clamped; horizontal forward falls back to the body yaw.
            Vector3 down = ReleasePlacement.ThrowDirection(new Vector3(0f, -0.999f, 0.04f).normalized, Vector3.forward, settings.MinThrowPitchDegrees, settings.MaxThrowPitchDegrees);
            Near(errors, Mathf.Asin(down.y) * Mathf.Rad2Deg, settings.MinThrowPitchDegrees, 0.5f, "a nearly vertical look clamps to the minimum pitch");
            Vector3 gentleDown = ReleasePlacement.ThrowDirection(new Vector3(0f, -0.5f, 0.5f).normalized, Vector3.forward, settings.MinThrowPitchDegrees, settings.MaxThrowPitchDegrees);
            Near(errors, gentleDown.y, -Mathf.Sin(45f * Mathf.Deg2Rad), 1e-3f, "a 45° downward aim throws 45° down");
            Vector3 up = ReleasePlacement.ThrowDirection(new Vector3(0f, 0.5f, 0.5f).normalized, Vector3.forward, settings.MinThrowPitchDegrees, settings.MaxThrowPitchDegrees);
            Near(errors, up.y, Mathf.Sin(45f * Mathf.Deg2Rad), 1e-3f, "45° aim keeps 45°");
            Vector3 vertical = ReleasePlacement.HorizontalForward(Vector3.down, new Vector3(1f, 0f, 0f));
            Near(errors, vertical.x, 1f, 1e-4f, "straight-down look uses the body yaw");
            // Aiming at a point: the low arc lands on it (checked by flight), out of range points straight at it.
            Vector3 origin = new(0f, 1.3f, 0f), targetPoint = new(0f, 0.2f, 5f);
            Vector3 aim = ReleasePlacement.AimAt(origin, targetPoint, 8f, -9.81f, -80f, 80f);
            Vector3 velocity = aim * 8f, position = origin;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < 400; i++) { position += velocity * 0.005f; velocity += Vector3.down * 9.81f * 0.005f; closest = Mathf.Min(closest, Vector3.Distance(position, targetPoint)); }
            if (closest > 0.08f) errors.Add($"AimAt: the low arc misses a 5 m target by {closest:0.00} m.");
            if (aim.y <= 0f) errors.Add("AimAt: a 5 m level target needs a slight lift, not a dip.");
            Vector3 farAim = ReleasePlacement.AimAt(origin, new Vector3(0f, 1.3f, 60f), 8f, -9.81f, -80f, 80f);
            if (Vector3.Angle(farAim, Vector3.forward) > 1f) errors.Add("AimAt: an unreachable target is aimed at directly.");

            // Prefab invariants.
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            if (player == null) errors.Add("player prefab missing.");
            else
            {
                if (player.GetComponent<PlayerStance>() == null) errors.Add("player prefab has no PlayerStance.");
                if (player.GetComponent<PlayerHands>() == null) errors.Add("player prefab has no PlayerHands.");
                int markers = 0, armsR = 0, armsL = 0, torsos = 0;
                foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == PlayerMovementHandsSetup.ForwardMarkerName) markers++;
                    if (t.name == PlayerHands.ArmRightName) armsR++;
                    if (t.name == PlayerHands.ArmLeftName) armsL++;
                    if (t.name == PlayerHands.TorsoName) torsos++;
                }
                if (markers != 0) errors.Add("ForwardMarker is still on the player prefab.");
                if (armsR != 1 || armsL != 1 || torsos != 1) errors.Add($"player prefab must have exactly one of each arm and torso (R={armsR} L={armsL} torso={torsos}).");
                foreach (Collider c in player.GetComponentsInChildren<Collider>(true))
                    if (c.transform.IsChildOf(player.transform) && (c.transform.name.StartsWith("Arm") || c.transform.parent != null && c.transform.parent.name.StartsWith("Arm"))) errors.Add("arm geometry must carry no collider.");
                var hold = player.transform.Find("ViewPivot/PlayerCamera/HoldPoint");
                if (hold != null && Mathf.Abs(hold.localPosition.x) > 1e-4f) errors.Add("HoldPoint must be centred (x = 0).");
                if (CarryableCollisionPolicy.LayersExist && player.layer != CarryableCollisionPolicy.PlayerLayer) errors.Add("player prefab is not on the Player layer.");
            }
            if (!CarryableCollisionPolicy.LayersExist) errors.Add("Player/Carryable layers missing.");
            else if (!Physics.GetIgnoreLayerCollision(CarryableCollisionPolicy.PlayerLayer, CarryableCollisionPolicy.CarryableLayer)) errors.Add("Player/Carryable collision is not ignored in the project matrix.");
            foreach (HQPrototypeLootSetup.FixtureEntry entry in HQPrototypeLootSetup.Manifest)
            {
                GameObject item = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
                ItemHandPose pose = item != null ? item.GetComponent<ItemHandPose>() : null;
                if (pose == null || !pose.IsComplete) { errors.Add(entry.PrefabName + " has no complete ItemHandPose."); continue; }
                float half = Vector3.Distance(pose.RightGrip.position, pose.LeftGrip.position) * 0.5f;
                if (half < 0.05f) errors.Add(entry.PrefabName + ": grips are too close together to be on the surface.");
                if (Vector3.Distance(item.transform.TransformPoint(Vector3.zero), (pose.RightGrip.position + pose.LeftGrip.position) * 0.5f) > 1e-3f) errors.Add(entry.PrefabName + ": grips are not centred on the item.");
            }

            if (errors.Count > 0) throw new InvalidOperationException("Movement and hands checks failed:\n- " + string.Join("\n- ", errors));
            return "Movement and hands checks passed: jump table, capsule dimensions, arm solver, release direction, prefabs and layers.";
        }

        private static void Near(List<string> errors, float actual, float expected, float tolerance, string label)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > tolerance) errors.Add($"{label}: {actual} (expected {expected}).");
        }
    }
}
