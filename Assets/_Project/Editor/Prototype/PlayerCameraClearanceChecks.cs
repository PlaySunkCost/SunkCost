using System;
using System.Collections.Generic;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Pure and fixture checks for docs/CAMERA_WALL_CLEARANCE_IMPLEMENTATION_PLAN.md:
    // the near-plane envelope arithmetic, its fit inside both stance capsules at
    // common aspects, the settings and prefab invariants, and the solver against
    // temporary colliders (flat wall, corner, thin wall, overhang, enclosure).
    // No Play Mode; the fixtures are created and destroyed in the open scene.
    public static class PlayerCameraClearanceChecks
    {
        [MenuItem("Sunk Cost/Prototype/Run camera clearance checks")]
        public static void RunFromMenu() => Debug.Log(RunOrThrow());

        public static string RunOrThrow()
        {
            var errors = new List<string>();
            PlayerCameraSettings camera = AssetDatabase.LoadAssetAtPath<PlayerCameraSettings>(PlayerCameraClearanceSetup.SettingsPath);
            if (camera == null) { errors.Add("PlayerCameraSettings asset missing (run Apply camera clearance setup)."); camera = PlayerCameraSettings.Resolve(null); }
            if (!camera.IsValid) errors.Add("PlayerCameraSettings is invalid.");
            PlayerMovementSettings movement = PlayerMovementSettings.Resolve(AssetDatabase.LoadAssetAtPath<PlayerMovementSettings>(PlayerMovementHandsSetup.SettingsPath));

            // Envelope arithmetic (plan section 3): 75 degrees, 16:9, 0.05 m near -> about 0.093 m before the margin.
            float r169 = PlayerCameraClearance.EnvelopeRadius(0.05f, 75f, 16f / 9f, 0f);
            Near(errors, r169, 0.0933f, 0.002f, "16:9 envelope radius");
            if (PlayerCameraClearance.EnvelopeRadius(0.05f, 75f, 16f / 9f, 0.01f) <= r169) errors.Add("the margin must grow the envelope.");
            if (PlayerCameraClearance.EnvelopeRadius(0.3f, 75f, 16f / 9f, 0f) <= movement.CapsuleRadius) errors.Add("a 0.3 m near plane should not fit the capsule (the bug this card fixes).");

            // The eye plus its envelope stays inside each stance capsule at 16:9 and 21:9.
            foreach (float aspect in new[] { 16f / 9f, 21f / 9f })
            {
                float radius = PlayerCameraClearance.EnvelopeRadius(camera.NearClip, 75f, aspect, camera.EnvelopeMargin);
                foreach (bool crouched in new[] { false, true })
                {
                    float eye = PlayerMovementMath.EyeHeight(movement, crouched);
                    float height = PlayerMovementMath.CapsuleHeight(movement, crouched);
                    float topSphere = height - movement.CapsuleRadius;
                    float reach = Mathf.Abs(eye - topSphere) + radius;
                    if (reach > movement.CapsuleRadius) errors.Add($"envelope leaves the {(crouched ? "crouched" : "standing")} capsule at aspect {aspect:0.00}: {reach:0.000} > {movement.CapsuleRadius}.");
                }
            }

            // Prefab invariants.
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.PlayerPrefabPath);
            if (player == null) errors.Add("player prefab missing.");
            else
            {
                if (player.GetComponent<PlayerCameraClearance>() == null) errors.Add("player prefab has no PlayerCameraClearance.");
                Camera prefabCamera = player.GetComponentInChildren<Camera>(true);
                if (prefabCamera == null) errors.Add("player prefab has no camera.");
                else if (!Mathf.Approximately(prefabCamera.nearClipPlane, camera.NearClip)) errors.Add($"prefab camera near clip {prefabCamera.nearClipPlane} != {camera.NearClip}.");
            }

            errors.AddRange(Fixtures(camera, movement));

            if (errors.Count > 0) throw new InvalidOperationException("Camera clearance checks failed:\n- " + string.Join("\n- ", errors));
            return "Camera clearance checks passed: envelope arithmetic, capsule fit at 16:9 and 21:9, prefab, and the wall/corner/thin-wall/overhang/enclosure fixtures.";
        }

        // Solver fixtures far below the open scene, cleaned up whatever happens.
        private static IEnumerable<string> Fixtures(PlayerCameraSettings camera, PlayerMovementSettings movement)
        {
            var errors = new List<string>();
            var made = new List<GameObject>();
            Vector3 origin = new(1000f, -1000f, 1000f);
            float radius = PlayerCameraClearance.EnvelopeRadius(camera.NearClip, 75f, 16f / 9f, camera.EnvelopeMargin);
            float eyeY = movement.StandingEyeHeight;
            float safeY = movement.StandingHeight - movement.CapsuleRadius;
            GameObject Block(string name, Vector3 center, Vector3 size)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "CheckBlock " + name;
                block.transform.position = origin + center;
                block.transform.localScale = size;
                block.hideFlags = HideFlags.HideAndDontSave;
                made.Add(block);
                return block;
            }
            void Clear() { foreach (GameObject g in made) UnityEngine.Object.DestroyImmediate(g); made.Clear(); Physics.SyncTransforms(); }
            try
            {
                float r = movement.CapsuleRadius;
                // Flat wall: nose against it (capsule touching), eye stays put.
                Block("wall", new Vector3(0f, 1.75f, r + 0.15f), new Vector3(6f, 3.5f, 0.3f));
                Physics.SyncTransforms();
                Vector3 safe = origin + Vector3.up * safeY, desired = origin + Vector3.up * eyeY;
                if (!PlayerCameraClearance.TryResolve(safe, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, null, out Vector3 eye) || eye != desired) errors.Add("flat wall: the eye should not move.");
                // Corner: two walls at the capsule radius, still clear with the small near plane.
                Block("wall2", new Vector3(r + 0.15f, 1.75f, 0f), new Vector3(0.3f, 3.5f, 6f));
                Physics.SyncTransforms();
                if (!PlayerCameraClearance.TryResolve(safe, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, null, out eye) || eye != desired) errors.Add("corner: the eye should not move.");
                Clear();
                // Overhang: a slab whose underside cuts the standing eye's envelope; the
                // eye is pushed down toward the safe point, ends clear and above it.
                float slabBottom = eyeY + radius * 0.5f;
                Block("slab", new Vector3(0f, slabBottom + 0.15f, 0f), new Vector3(2f, 0.3f, 2f));
                Physics.SyncTransforms();
                if (!PlayerCameraClearance.TryResolve(safe, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, null, out eye)) errors.Add("overhang: a clear pose exists but was not found.");
                else
                {
                    float local = eye.y - origin.y;
                    if (local + radius > slabBottom + 1e-3f) errors.Add($"overhang: the corrected envelope still touches the slab ({local + radius:0.000} > {slabBottom:0.000}).");
                    if (local < safeY - 1e-4f || local >= eyeY) errors.Add($"overhang: the corrected eye ({local:0.000}) must lie between the safe point ({safeY}) and the desired eye ({eyeY}).");
                    if (Mathf.Abs(eye.x - origin.x) > 1e-4f || Mathf.Abs(eye.z - origin.z) > 1e-4f) errors.Add("overhang: the correction must stay on the safe-to-desired line.");
                }
                Clear();
                // Thin wall through the desired eye, safe point on this side: never
                // resolve to the far side, so the eye stays on the safe side of the wall.
                float wallZ = 0.02f;
                Block("thin", new Vector3(0f, eyeY, wallZ), new Vector3(2f, 0.05f, 0.02f)); // a thin ledge at eye height
                Physics.SyncTransforms();
                Vector3 safeBelow = origin + Vector3.up * (eyeY - 0.3f);
                if (!PlayerCameraClearance.TryResolve(safeBelow, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, null, out eye)) errors.Add("thin ledge: a clear pose below it exists.");
                else if (eye.y - origin.y >= eyeY - 0.025f) errors.Add("thin ledge: the eye must resolve below the ledge, not through it.");
                Clear();
                // Enclosure: the safe point itself is inside geometry -> obstructed.
                Block("box", new Vector3(0f, safeY, 0f), new Vector3(1f, 1f, 1f));
                Physics.SyncTransforms();
                if (PlayerCameraClearance.TryResolve(safe, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, null, out eye)) errors.Add("enclosure: must report obstructed.");
                Clear();
                // Ignore self: a collider under the ignored transform does not block.
                GameObject self = new("CheckBlock self") { hideFlags = HideFlags.HideAndDontSave };
                made.Add(self);
                GameObject child = Block("selfchild", new Vector3(0f, eyeY, 0f), Vector3.one * 0.2f);
                child.transform.SetParent(self.transform, true);
                Physics.SyncTransforms();
                if (!PlayerCameraClearance.TryResolve(safe, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, self.transform, out eye) || eye != desired) errors.Add("own colliders must be ignored.");
                if (PlayerCameraClearance.TryResolve(safe, desired, radius, camera.MaxCorrectionMeters, camera.ContactSkin, null, out eye) && eye == desired) errors.Add("a foreign collider at the eye must count.");
            }
            finally { Clear(); }
            return errors;
        }

        private static void Near(List<string> errors, float actual, float expected, float tolerance, string label)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > tolerance) errors.Add($"{label}: {actual} (expected {expected}).");
        }
    }
}
