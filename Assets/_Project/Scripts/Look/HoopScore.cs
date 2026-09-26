using System.Collections.Generic;
using FishNet;
using SunkCost.Interaction;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Look
{
    // Observe the existing physics writer's path. Released balls are simulated by
    // their thrower, so the server's kinematic Rigidbody velocity is not evidence.
    // A basket crosses the rim, then the lower plane, downward inside the opening.
    public sealed class HoopScore : MonoBehaviour
    {
        [SerializeField] private TextMesh board;
        [SerializeField] private string label = "BASKETS";
        [SerializeField] private float rimAboveTrigger = .2f;
        [SerializeField] private float rimRadius = .225f;
        [SerializeField] private float rimTolerance = .015f;
        [SerializeField] private float repeatDelay = 1f;
        private sealed class Passage
        {
            public Vector3 Previous;
            public uint Version;
            public float SampleAt, ScoredAt = -100f;
            public bool ThroughRim;
        }
        private readonly Dictionary<CarryableItem, Passage> passages = new();
        private readonly List<CarryableItem> stale = new();
        private int shown = -1;

        public void Configure(TextMesh scoreboard) => board = scoreboard;

        private void LateUpdate()
        {
            if (InstanceFinder.IsServerStarted) ObserveBalls();
            else passages.Clear();
            if (board == null) return;
            CrewDayState day = CrewDayState.Instance;
            int count = day != null ? day.Baskets : 0;
            if (count == shown) return;
            shown = count;
            board.text = $"{label} {count}";
        }

        private bool Eligible(CarryableItem item) => item != null && item.IsSpawned &&
            item.gameObject.scene == gameObject.scene && item.CanGrabFromWorld && !item.InTransit &&
            item.DisplayName == "Basketball";

        private void ObserveBalls()
        {
            stale.Clear();
            foreach (var pair in passages)
                if (!Eligible(pair.Key)) stale.Add(pair.Key);
            foreach (var item in stale) passages.Remove(item);
            foreach (var item in CarryableItem.Spawned)
            {
                if (!Eligible(item)) continue;
                Vector3 current = transform.InverseTransformPoint(item.transform.position);
                if (!passages.TryGetValue(item, out Passage pass))
                {
                    passages[item] = new Passage { Previous = current, Version = item.MotionVersion, SampleAt = Time.time };
                    continue;
                }
                Vector3 previous = pass.Previous;
                bool continuous = pass.Version == item.MotionVersion && Time.time - pass.SampleAt < .5f &&
                    (current - previous).sqrMagnitude < 9f;
                pass.Previous = current; pass.SampleAt = Time.time; pass.Version = item.MotionVersion;
                if (!continuous) { pass.ThroughRim = false; continue; }
                float clearance = Mathf.Max(0f, rimRadius - item.Radius + rimTolerance);
                if (CrossesDown(previous, current, rimAboveTrigger, clearance)) pass.ThroughRim = true;
                if (pass.ThroughRim && CrossesDown(previous, current, 0f, rimRadius))
                {
                    pass.ThroughRim = false;
                    if (Time.time - pass.ScoredAt >= repeatDelay && CrewDayState.Instance != null)
                    {
                        pass.ScoredAt = Time.time;
                        CrewDayState.Instance.ServerAddBasket();
                        Debug.Log($"[Court] {item.name} through {transform.parent.name}: {CrewDayState.Instance.Baskets}");
                    }
                }
                if (current.y > rimAboveTrigger + item.Radius || current.y < -.3f ||
                    new Vector2(current.x, current.z).sqrMagnitude > 1f) pass.ThroughRim = false;
            }
        }

        private static bool CrossesDown(Vector3 from, Vector3 to, float height, float radius)
        {
            if (from.y <= height || to.y > height) return false;
            Vector3 at = Vector3.Lerp(from, to, (from.y - height) / (from.y - to.y));
            return at.x * at.x + at.z * at.z <= radius * radius;
        }

        private void OnDisable() { passages.Clear(); shown = -1; }
    }
}
