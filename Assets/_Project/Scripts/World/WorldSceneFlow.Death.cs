using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // Death, minimum (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 1, design §4):
    // a player dies below, its body is cargo where it fell, its slots scatter, the
    // day state stops counting it as living. The dead ride along hidden: when the
    // site unloads (the last living diver up, or nobody living left below) their
    // player objects move to the ship; End day revives them there, next to their
    // body if it was brought up. Nothing kills yet except the debug key.
    public sealed partial class WorldSceneFlow
    {
        // Dead players whose client still holds the site scene: they must be part
        // of the site's unload or their client would keep a stale copy.
        private readonly HashSet<int> deadWithSite = new();

        // K below (HQPlayerController.RequestDebugDeath), later air and the monster.
        public bool ServerKill(NetworkConnection conn, out string why)
        {
            why = string.Empty;
            if (networkManager == null || !networkManager.ServerManager.Started) { why = "Server not running."; return false; }
            if (dayState == null || conn == null) { why = "No day state."; return false; }
            HQPlayerController player = PlayerOf(conn);
            if (player == null) { why = "No player."; return false; }
            int id = conn.ClientId;
            if (player.IsDead || dayState.IsDead(id)) { why = "Already dead"; return false; }
            if (!dayState.IsBelow(id) || player.gameObject.scene != WorldScenes.Scene(WorldId.Dive)) { why = "You can only die below"; return false; }
            if (riding && cohort.Contains(id)) { why = "Riding"; return false; }

            Vector3 at = player.transform.position;
            PlayerInventory inventory = player.Inventory;
            if (inventory != null) inventory.ServerDropEverything(); // the four slots and the hands scatter (design §4)
            PlayerIdentity identity = player.GetComponent<PlayerIdentity>();
            string name = identity != null ? identity.DisplayName : PlayerIdentity.Fallback(id);
            CarryableItem body = PlayerBody.ServerSpawn(networkManager, id, name, at + Vector3.up * 0.3f, player.gameObject.scene);
            dayState.ServerPlayerDied(id);
            player.ServerSetDead(true);
            deadWithSite.Add(id);
            Debug.Log($"[WorldSceneFlow] {name} died below at {at:F1}; body {(body != null ? body.name : "none")}; living below: {dayState.Below.Count}");
            ServerUpdateSpectators(); // the nearest living player, now (card 2)
            ServerAfterDeath();
            return true;
        }

        // Nobody living is below any more — the last one died or disconnected: the
        // dive is done, the car comes home empty and the site goes, with the dead
        // carried to the ship first. A ride in progress does this itself at its end
        // (SurfaceRoutine).
        private void ServerAfterDeath() => ServerSiteMayClose();

        public void ServerSiteMayClose()
        {
            if (dayState == null || dayState.Below.Count > 0 || riding || siteClosing) return;
            dayState.ServerEndDayIfDone(Settings.DaysPerCycle);
            if (WorldScenes.IsLoaded(WorldId.Dive)) StartCoroutine(UnloadSiteWithDead());
        }

        private IEnumerator UnloadSiteWithDead()
        {
            siteClosing = true; // no watch changes while objects are on the move (card 2)
            // Wherever the car is, it ends up at the top: a car on its way down for a
            // diver who is gone finishes the trip and comes straight back.
            if (dayState.Elevator.State == ElevatorState.Descending)
                yield return WaitForCar(ElevatorState.AtBottom, CarTravelSeconds + Settings.ArrivalTimeoutSeconds);
            if (dayState.Elevator.State == ElevatorState.AtBottom) ServerSetElevator(ElevatorState.Sealing, true);
            if (dayState.Elevator.State != ElevatorState.AtTop)
                yield return WaitForCar(ElevatorState.AtTop, CarSealSeconds + CarTravelSeconds + Settings.ArrivalTimeoutSeconds);
            yield return ServerMoveDeadToShip();
            if (WorldScenes.IsLoaded(WorldId.Dive))
            {
                NetworkConnection[] unloaders = SiteUnloaders(new List<NetworkConnection>(), closing: true);
                if (unloaders.Length > 0) networkManager.SceneManager.UnloadConnectionScenes(unloaders, UnloadDataFor(WorldId.Dive, keepOnServer: false));
                else networkManager.SceneManager.UnloadConnectionScenes(UnloadDataFor(WorldId.Dive, keepOnServer: false));
                cachedCar = null;
                ServerSetElevator(ElevatorState.AtTop, true);
            }
            siteClosing = false;
        }

        // Every dead player whose object still stands in the site moves to the ship,
        // hidden; its owner parks itself at a deck spawn point when the scene lands
        // (ClientPlaceDeadOnDeck). Their clients load the ship scene here.
        private IEnumerator ServerMoveDeadToShip()
        {
            Scene sea = WorldScenes.Scene(WorldId.Sea);
            Scene dive = WorldScenes.Scene(WorldId.Dive);
            if (!sea.IsValid() || !sea.isLoaded) yield break;
            var moving = new List<NetworkConnection>();
            foreach (int id in new List<int>(dayState.Dead))
            {
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null || player.gameObject.scene != dive) continue;
                var moved = new List<NetworkObject> { player.NetworkObject };
                PlayerInventory inventory = player.Inventory;
                if (inventory != null) inventory.ServerCollectCarried(moved); // nothing normally; the slots scattered at death
                // A client watching the ship from below drops it first, so the move is
                // the plain load-with-moved-objects of card 1 (card 2).
                ServerUnwatch(conn);
                networkManager.SceneManager.AddConnectionToScene(conn, sea);
                EnsureHolderKeepAlive();
                networkManager.SceneManager.LoadConnectionScenes(new[] { conn }, LoadDataFor(WorldId.Sea, moved.ToArray()));
                moving.Add(conn);
            }
            if (moving.Count == 0) yield break;
            float deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                bool all = true;
                foreach (NetworkConnection conn in moving)
                {
                    HQPlayerController player = PlayerOf(conn);
                    if (player != null && player.gameObject.scene != sea) all = false;
                }
                if (all) break;
                yield return null;
            }
        }

        // The connections a site unload must include: the riders' cohort and, when
        // the site closes for good, every dead player whose client still holds it
        // (their objects were moved to the ship first) and every client watching
        // it from the ship (card 2). While living divers remain below the site
        // stays, and so do the dead in it and its watchers.
        private NetworkConnection[] SiteUnloaders(List<NetworkConnection> cohortConns, bool closing)
        {
            var set = new List<NetworkConnection>(cohortConns);
            if (!closing) return set.ToArray();
            foreach (int id in new List<int>(deadWithSite))
            {
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) { deadWithSite.Remove(id); continue; }
                if (!set.Contains(conn)) set.Add(conn);
                deadWithSite.Remove(id);
            }
            foreach (NetworkConnection conn in TakeWatchersOf(WorldId.Dive))
                if (!set.Contains(conn)) set.Add(conn);
            return set.ToArray();
        }

        // End day: the dead stand up on the deck — next to their body if it came up
        // (the body is gone), else at a spawn point — with nothing but the base kit.
        private void ServerReviveAll()
        {
            if (dayState == null) return;
            Scene sea = WorldScenes.Scene(WorldId.Sea);
            List<Transform> points = sea.IsValid() && sea.isLoaded ? CrewSpawner.SpawnPointsIn(sea) : new List<Transform>();
            int k = 0;
            foreach (int id in new List<int>(dayState.Dead))
            {
                dayState.ServerRevive(id);
                deadWithSite.Remove(id);
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                ServerUnwatch(conn); // whatever they watched from the deck goes (card 2)
                HQPlayerController player = PlayerOf(conn);
                if (player == null) continue;
                Vector3 at; float yaw = 180f;
                PlayerBody body = sea.IsValid() ? PlayerBody.FindFor(id, sea) : null;
                if (body != null)
                {
                    at = body.transform.position + body.transform.right * 0.8f + Vector3.up * 0.1f;
                    body.NetworkObject.Despawn();
                }
                else if (points.Count > 0) { Transform p = points[k++ % points.Count]; at = p.position; yaw = p.eulerAngles.y; }
                else at = player.transform.position;
                player.ServerSetDead(false);
                player.TargetPlace(conn, at, yaw);
                Debug.Log($"[WorldSceneFlow] {DisplayName(conn)} revived at {at:F1}{(body != null ? " next to the body" : string.Empty)}");
            }
        }

        // Client: a dead player's object just arrived in the ship scene (moved by the
        // server): park it at a deck spawn point, out of the way, still hidden.
        private bool ClientPlaceDeadOnDeck(SceneLoadEndEventArgs args)
        {
            HQPlayerController local = LocalPlayer();
            if (local == null || !local.IsDead) return false;
            bool seaLoaded = false;
            foreach (SceneLookupData lookup in args.QueueData.SceneLoadData.SceneLookupDatas)
                if (lookup is not null && lookup.Name == WorldScenes.SeaName) seaLoaded = true;
            if (!seaLoaded) return false;
            RunClientStage(ParkDeadWhenHome(local));
            return true;
        }

        private IEnumerator ParkDeadWhenHome(HQPlayerController local)
        {
            float deadline = Time.unscaledTime + Settings.ArrivalTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                ShipParts ship = ShipParts.InWorld(WorldId.Sea);
                if (ship != null && local.gameObject.scene == WorldScenes.Scene(WorldId.Sea))
                {
                    Transform point = ship.SpawnPoint(local.OwnerId % ShipParts.SpawnPointCount) ?? ship.SpawnPoint(0);
                    if (point != null) local.TeleportLocal(point.position, point.eulerAngles.y);
                    break;
                }
                yield return null;
            }
            clientStage = null;
        }
    }
}
