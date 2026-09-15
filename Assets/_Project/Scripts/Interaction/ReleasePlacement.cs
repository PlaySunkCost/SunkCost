using UnityEngine;

namespace SunkCost.Interaction
{
    // Where a dropped or thrown item may start (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
    // section 6A): in clear space in front of the player, never inside or under
    // anyone, never behind a wall. Shared by the owner's request and the
    // server's check; both answer from their own view of the geometry. Sphere
    // queries fit the current balls; a non-spherical item passes its bounding
    // radius. Every query fails closed when its buffer fills.
    public static class ReleasePlacement
    {
        private static readonly Collider[] Overlaps = new Collider[16];
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // Horizontal aim: the camera forward on the ground plane, the body yaw
        // when looking almost straight up or down.
        public static Vector3 HorizontalForward(Vector3 cameraForward, Vector3 bodyForward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(cameraForward, Vector3.up);
            if (flat.sqrMagnitude < 0.04f) flat = Vector3.ProjectOnPlane(bodyForward, Vector3.up);
            return flat.sqrMagnitude < 1e-6f ? Vector3.forward : flat.normalized;
        }

        // Throw direction with the launch pitch clamped: looking at your feet
        // throws forward, then gravity does the rest.
        public static Vector3 ThrowDirection(Vector3 cameraForward, Vector3 bodyForward, float minPitchDegrees, float maxPitchDegrees)
        {
            Vector3 flat = HorizontalForward(cameraForward, bodyForward);
            float pitch = Mathf.Asin(Mathf.Clamp(cameraForward.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, minPitchDegrees, maxPitchDegrees);
            float rad = pitch * Mathf.Deg2Rad;
            return (flat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)).normalized;
        }

        // A clear start pose for the item, or false. `drop` sweeps down to the
        // nearest support within dropGroundSearch and rests the item on it.
        public static bool TryFind(Vector3 feet, float capsuleRadius, float capsuleHeight, Vector3 forward, float itemRadius, bool drop,
            SunkCost.Player.PlayerMovementSettings settings, Transform ignoreSelf, Transform ignoreItem, out Vector3 pose)
        {
            pose = Vector3.zero;
            float chest = Mathf.Clamp(capsuleHeight * 0.72f, itemRadius + settings.DropSkin, capsuleHeight - 0.1f);
            Vector3 origin = feet + Vector3.up * chest; // inside the capsule, stance-aware
            float minDistance = capsuleRadius + itemRadius + settings.ReleaseClearance;
            if (minDistance > settings.ReleaseMaxForward) return false;
            float[] heights = { chest, chest - 0.3f, chest + 0.2f, chest - 0.55f };
            for (float distance = minDistance; distance <= settings.ReleaseMaxForward + 1e-4f; distance += 0.3f)
            {
                foreach (float height in heights)
                {
                    if (height < itemRadius + settings.DropSkin) continue;
                    Vector3 candidate = feet + forward * distance + Vector3.up * height;
                    if (!IsClear(candidate, itemRadius, ignoreSelf, ignoreItem)) continue;
                    if (!PathClear(origin, candidate, itemRadius, ignoreSelf, ignoreItem)) continue;
                    if (drop)
                    {
                        Vector3 resting = candidate;
                        int count = Physics.SphereCastNonAlloc(candidate, itemRadius, Vector3.down, Hits, settings.DropGroundSearch, CarryableCollisionPolicy.WorldMask, QueryTriggerInteraction.Ignore);
                        if (count == Hits.Length) continue;
                        float nearest = float.PositiveInfinity;
                        for (int i = 0; i < count; i++)
                        {
                            RaycastHit hit = Hits[i];
                            if (hit.collider == null || (ignoreSelf != null && hit.collider.transform.IsChildOf(ignoreSelf)) || (ignoreItem != null && hit.collider.transform.IsChildOf(ignoreItem))) continue;
                            if (hit.distance < nearest) nearest = hit.distance;
                        }
                        if (!float.IsPositiveInfinity(nearest)) resting = candidate + Vector3.down * Mathf.Max(0f, nearest - settings.DropSkin);
                        if (!IsClear(resting, itemRadius, ignoreSelf, ignoreItem)) continue;
                        pose = resting;
                    }
                    else pose = candidate;
                    return true;
                }
            }
            return false;
        }

        // No solid geometry and no other player inside the item's sphere.
        public static bool IsClear(Vector3 center, float itemRadius, Transform ignoreSelf, Transform ignoreItem)
        {
            int count = Physics.OverlapSphereNonAlloc(center, itemRadius, Overlaps, CarryableCollisionPolicy.PlacementMask, QueryTriggerInteraction.Ignore);
            if (count == Overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (hit == null) continue;
                if (ignoreSelf != null && hit.transform.IsChildOf(ignoreSelf)) continue;
                if (ignoreItem != null && hit.transform.IsChildOf(ignoreItem)) continue;
                return false;
            }
            return true;
        }

        // A clear endpoint beyond a wall is not enough: the sphere must get there.
        private static bool PathClear(Vector3 from, Vector3 to, float itemRadius, Transform ignoreSelf, Transform ignoreItem)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f) return true;
            int count = Physics.SphereCastNonAlloc(from, itemRadius, delta / distance, Hits, distance, CarryableCollisionPolicy.PlacementMask, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.collider == null) continue;
                if (ignoreSelf != null && hit.collider.transform.IsChildOf(ignoreSelf)) continue;
                if (ignoreItem != null && hit.collider.transform.IsChildOf(ignoreItem)) continue;
                return false;
            }
            return true;
        }
    }
}
