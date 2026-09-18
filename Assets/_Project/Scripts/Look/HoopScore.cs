using System.Collections.Generic;
using FishNet;
using SunkCost.Interaction;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Look
{
    // The court's hoops count (Dan, 18 September 2026: "a ball through the hoop
    // counts"): a trigger under the ring; on the server a loose basketball
    // falling through it is a basket, added to CrewDayState.Baskets (once per
    // pass — a ball rattling in the ring counts once). Every peer writes the
    // count on the backboard from the replicated value; nothing else depends
    // on it. The trigger box and the board are the Hoop prop's (PropBuilder).
    public sealed class HoopScore : MonoBehaviour
    {
        [SerializeField] private TextMesh board;
        [SerializeField] private string label = "BASKETS";
        private readonly Dictionary<CarryableItem, float> scoredAt = new();
        private int shown = -1;

        public void Configure(TextMesh scoreboard) => board = scoreboard;

        private void OnTriggerEnter(Collider other)
        {
            if (!InstanceFinder.IsServerStarted) return;
            CarryableItem item = other.GetComponentInParent<CarryableItem>();
            if (item == null || !item.IsSpawned || item.HolderClientId >= 0) return;
            if (!item.name.StartsWith("Basketball") && !item.DisplayName.StartsWith("Basketball")) return;
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body != null && body.linearVelocity.y > -0.5f) return; // through, not up into it
            if (scoredAt.TryGetValue(item, out float at) && Time.time - at < 1.5f) return;
            scoredAt[item] = Time.time;
            CrewDayState day = CrewDayState.Instance;
            if (day == null) return;
            day.ServerAddBasket();
            Debug.Log($"[Court] {item.name} through the hoop: {day.Baskets}");
        }

        private void Update()
        {
            if (board == null) return;
            CrewDayState day = CrewDayState.Instance;
            int count = day != null ? day.Baskets : 0;
            if (count == shown) return;
            shown = count;
            board.text = $"{label} {count}";
        }
    }
}
