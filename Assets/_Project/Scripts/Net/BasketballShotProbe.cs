#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Reflection;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Net
{
    // Opt-in verification only: choose a reproducible camera pitch, then use the
    // ordinary inventory request. This never changes production aiming or physics.
    public static class BasketballShotProbe
    {
        public static string Shoot(HQPlayerController player, Vector3 target)
        {
            var item = player.Inventory.HeldItem;
            if (item == null) return "No ball held";
            Vector3 flat = Vector3.ProjectOnPlane(target - player.transform.position, Vector3.up).normalized;
            player.TeleportLocal(player.transform.position, Quaternion.LookRotation(flat).eulerAngles.y);
            var propose = typeof(PlayerInventory).GetMethod("TryProposeRelease", BindingFlags.NonPublic | BindingFlags.Instance);
            float best = float.PositiveInfinity, pitch = 0;
            for (float angle = 30; angle < 84; angle += .1f)
            {
                player.SetPitchForChecks(-angle);
                object[] args = { item, false, Vector3.zero, Vector3.zero };
                if (!(bool)propose.Invoke(player.Inventory, args)) continue;
                Vector3 pos = (Vector3)args[2], velocity = (Vector3)args[3] * item.LaunchSpeed;
                for (int i = 0; i < 180; i++)
                {
                    Vector3 previous = pos;
                    velocity = (velocity + Physics.gravity * Time.fixedDeltaTime) * (1 - item.GetComponent<Rigidbody>().linearDamping * Time.fixedDeltaTime);
                    pos += velocity * Time.fixedDeltaTime;
                    if (velocity.y < 0 && previous.y > target.y && pos.y <= target.y)
                    {
                        Vector3 crossing = Vector3.Lerp(previous, pos, (previous.y-target.y)/(previous.y-pos.y));
                        float error = Vector3.Distance(crossing, target);
                        if (error < best) { best = error; pitch = angle; }
                        break;
                    }
                }
            }
            player.SetPitchForChecks(-pitch);
            if (best > .07f) return $"No clear test shot: error={best:0.000}";
            player.Inventory.RequestUse(player.PlayerCamera.transform.forward);
            return $"shot requested pitch={pitch:0.0}; predicted miss={best:0.000}";
        }
    }
}
#endif
