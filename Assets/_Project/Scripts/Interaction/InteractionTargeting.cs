using UnityEngine;

namespace SunkCost.Interaction
{
    // Forgiving aim without longer reach or grabbing through a wall. Queries run
    // on Unity's main thread; buffers are reused and never held across calls.
    public static class InteractionTargeting
    {
        private static readonly Collider[] Candidates = new Collider[64];
        private static readonly RaycastHit[] Obstructions = new RaycastHit[64];

        public static CarryableItem Find(Vector3 eye, Vector3 forward, Transform player, float reach, float aimRadius)
        {
            CarryableItem best = null;
            float bestScore = float.PositiveInfinity;
            int count = Physics.OverlapSphereNonAlloc(eye, reach, Candidates, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = Candidates[i];
                if (collider.transform.IsChildOf(player)) continue;
                CarryableItem item = collider.GetComponentInParent<CarryableItem>();
                if (item == null || !item.IsSpawned || !item.CanGrabFromWorld) continue;
                Vector3 delta = collider.bounds.center - eye;
                float along = Vector3.Dot(delta, forward);
                if (along <= 0f) continue;
                Vector3 closest = collider.ClosestPoint(eye + forward * Mathf.Min(along, reach));
                float lateral = Vector3.Distance(closest, eye + forward * Mathf.Min(along, reach));
                if (lateral > aimRadius || Vector3.Distance(eye, collider.ClosestPoint(eye)) > reach) continue;
                if (!HasLineOfSight(eye, closest, player, item)) continue;
                float score = lateral + along * 0.01f;
                if (score >= bestScore) continue;
                best = item;
                bestScore = score;
            }
            return best;
        }

        // A ship button straight under the crosshair, within reach and not behind
        // anything. Buttons are exact targets: no forgiving aim.
        public static SunkCost.World.MonitorButton FindButton(Vector3 eye, Vector3 forward, Transform player, float reach)
        {
            Transform hit = FindPressable(eye, forward, player, reach);
            return hit != null ? hit.GetComponentInParent<SunkCost.World.MonitorButton>() : null;
        }

        // The nearest solid thing under the crosshair within reach, or null: the
        // monitor buttons, the deck cabin's button and the car's panel are all
        // plain colliders told apart by their components and names.
        public static Transform FindPressable(Vector3 eye, Vector3 forward, Transform player, float reach)
        {
            int count = Physics.RaycastNonAlloc(eye, forward, Obstructions, reach, ~0, QueryTriggerInteraction.Ignore);
            if (count == Obstructions.Length) return null;
            RaycastHit nearest = default;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (Obstructions[i].collider.transform.IsChildOf(player)) continue;
                if (!any || Obstructions[i].distance < nearest.distance) { nearest = Obstructions[i]; any = true; }
            }
            return any ? nearest.collider.transform : null;
        }

        public static bool HasLineOfSight(Vector3 eye, Vector3 point, Transform player, CarryableItem item)
        {
            Vector3 delta = point - eye;
            float distance = delta.magnitude;
            if (distance < 0.001f) return true;
            int count = Physics.RaycastNonAlloc(eye, delta / distance, Obstructions, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == Obstructions.Length) return false; // do not trust an incomplete obstruction list
            for (int i = 0; i < count; i++)
            {
                Transform hit = Obstructions[i].collider.transform;
                if (hit.IsChildOf(player) || hit.IsChildOf(item.transform)) continue;
                if (Obstructions[i].distance < distance - 0.01f) return false;
            }
            return true;
        }
    }
}
