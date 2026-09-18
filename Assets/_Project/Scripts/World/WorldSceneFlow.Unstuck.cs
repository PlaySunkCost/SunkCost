using System.Collections.Generic;
using FishNet.Connection;
using SunkCost.Diving;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // Unstuck (Dan, 18 September 2026: "a button on the menu — it takes you back
    // to a place in your scene: on the ship, HQ, the elevator if down"). The
    // server decides from the player's object's scene and places the owner
    // (TargetPlace, like a revival): the pier's spawn points at HQ, the ship's
    // boarding point at sea, and below the car's floor when it is down, else the
    // landing outside its doorway. Refused to the dead (they watch), to a rider
    // (the ride decides where they stand), to the jumper on the plank, and more
    // than once every few seconds.
    public sealed partial class WorldSceneFlow
    {
        private const float UnstuckCooldownSeconds = 5f;
        private readonly Dictionary<int, float> lastUnstuck = new();

        public bool ServerUnstuck(NetworkConnection conn, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null || conn == null) { why = "No day state."; return false; }
            HQPlayerController player = PlayerOf(conn);
            if (player == null) { why = "No player."; return false; }
            int id = conn.ClientId;
            if (player.IsDead || dayState.IsDead(id)) { why = "The dead stay where they are"; return false; }
            if ((riding && cohort.Contains(id)) || dayState.IsRider(id)) { why = "Not during a ride"; return false; }
            if (dayState.Phase == DayPhase.Plank && dayState.Plank.Active && dayState.Plank.Jumper == id) { why = "Walk the plank"; return false; }
            if (lastUnstuck.TryGetValue(id, out float last) && Time.unscaledTime - last < UnstuckCooldownSeconds) { why = "Just did"; return false; }
            if (!UnstuckSpot(player, out Vector3 at, out float yaw, out string where)) { why = "Nowhere to put you"; return false; }
            lastUnstuck[id] = Time.unscaledTime;
            player.TargetPlace(conn, at, yaw);
            Debug.Log($"[Unstuck] {DisplayName(conn)} put back {where} at {at:F1}");
            return true;
        }

        private bool UnstuckSpot(HQPlayerController player, out Vector3 at, out float yaw, out string where)
        {
            at = player.transform.position; yaw = player.Yaw; where = string.Empty;
            Scene scene = player.gameObject.scene;
            if (scene == WorldScenes.Scene(WorldId.HQ))
            {
                List<Transform> points = CrewSpawner.SpawnPointsIn(scene);
                if (points.Count == 0) return false;
                Transform p = points[Mathf.Abs(player.OwnerId) % points.Count];
                at = p.position; yaw = p.eulerAngles.y; where = "on the pier";
                return true;
            }
            if (scene == WorldScenes.Scene(WorldId.Sea))
            {
                ShipParts ship = ShipParts.InWorld(WorldId.Sea);
                Transform p = ship != null ? (ship.BoardingPoint ?? ship.SpawnPoint(0)) : null;
                if (p == null) return false;
                at = p.position; yaw = p.eulerAngles.y; where = "on the deck";
                return true;
            }
            if (scene == WorldScenes.Scene(WorldId.Dive))
            {
                ElevatorController car = Car();
                if (car == null) return false;
                Vector3 doorway = car.transform.TransformDirection(Quaternion.Euler(0f, CabinFrame.CarDoorwayYaw, 0f) * Vector3.forward);
                if (dayState.Elevator.State == ElevatorState.AtBottom)
                {
                    at = car.transform.position + Vector3.up * 0.2f; yaw = car.transform.eulerAngles.y; where = "in the car";
                }
                else
                {
                    at = car.BottomPosition + doorway * 3f + Vector3.up * 0.2f; yaw = Quaternion.LookRotation(-doorway).eulerAngles.y; where = "at the landing";
                }
                return true;
            }
            return false;
        }
    }
}
