using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // Dead spectating, server side (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 2):
    // who each dead player watches (CrewDayState.Spectate, written only here) and
    // the extra world a watcher's client loads to see a target in the other world.
    // The player object never moves for watching; a loaded scene grants nothing
    // (voice, items and the cabin keep using the physical world). Decided from
    // state four times a second and at every death and click, so a target that
    // dies, leaves or changes world is replaced without an event for each.
    public sealed partial class WorldSceneFlow
    {
        private const float SpectateUpdateInterval = 0.25f;
        // Client id → the world loaded on that client only for watching.
        private readonly Dictionary<int, WorldId> watching = new();
        private float nextSpectateUpdateAt;
        private bool siteClosing; // UnloadSiteWithDead in progress: objects on the move

        // Like LoadDataFor, without a preferred active scene and with nothing moved:
        // the watcher's active scene stays its own world.
        public static SceneLoadData WatchDataFor(WorldId world)
        {
            return new SceneLoadData(WorldScenes.Lookup(world))
            {
                MovedNetworkObjects = new NetworkObject[0],
                ReplaceScenes = ReplaceOption.None,
                Options = new LoadOptions { AutomaticallyUnload = false }
            };
        }

        public bool IsWatching(int clientId, out WorldId world) => watching.TryGetValue(clientId, out world);

        private void ServerTickSpectators()
        {
            if (Time.unscaledTime < nextSpectateUpdateAt) return;
            nextSpectateUpdateAt = Time.unscaledTime + SpectateUpdateInterval;
            ServerUpdateSpectators();
        }

        // Every dead player: a valid target (else the nearest living, else none) and
        // the world its client needs for it.
        private void ServerUpdateSpectators()
        {
            if (dayState == null || networkManager == null || !networkManager.IsServerStarted) return;
            foreach (int id in new List<int>(dayState.Dead))
            {
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                int target = dayState.SpectateTargetOf(id);
                if (!ServerIsWatchable(target))
                {
                    target = ServerNearestLiving(conn);
                    dayState.ServerSetSpectateTarget(id, target);
                }
                ServerWatchFor(conn, target);
            }
        }

        private bool ServerIsWatchable(int id)
        {
            if (id < 0 || dayState.IsDead(id)) return false;
            if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) return false;
            return PlayerOf(conn) != null;
        }

        // The living player nearest to the dead one's object, its own world first.
        private int ServerNearestLiving(NetworkConnection dead)
        {
            HQPlayerController me = PlayerOf(dead);
            int best = -1;
            float bestScore = float.PositiveInfinity;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (conn == dead || !conn.IsActive || dayState.IsDead(conn.ClientId)) continue;
                HQPlayerController other = PlayerOf(conn);
                if (other == null) continue;
                float score = me == null ? conn.ClientId : Vector3.Distance(me.transform.position, other.transform.position) + (other.gameObject.scene == me.gameObject.scene ? 0f : 1e6f);
                if (score < bestScore) { bestScore = score; best = conn.ClientId; }
            }
            return best;
        }

        // Left click (HQPlayerController.RequestNextSpectate): the next living
        // player by client id after the current target, wrapping.
        public void ServerSpectateNext(NetworkConnection conn)
        {
            if (dayState == null || conn == null || !dayState.IsDead(conn.ClientId)) return;
            var living = new List<int>();
            foreach (NetworkConnection other in networkManager.ServerManager.Clients.Values)
                if (other != conn && other.IsActive && !dayState.IsDead(other.ClientId) && PlayerOf(other) != null) living.Add(other.ClientId);
            if (living.Count == 0) { dayState.ServerSetSpectateTarget(conn.ClientId, -1); return; }
            living.Sort();
            int current = dayState.SpectateTargetOf(conn.ClientId);
            int next = living[0];
            foreach (int id in living) if (id > current) { next = id; break; }
            dayState.ServerSetSpectateTarget(conn.ClientId, next);
            ServerWatchFor(conn, next);
        }

        // The world the watcher needs beyond its own: the target's, when different.
        private void ServerWatchFor(NetworkConnection conn, int target)
        {
            if (riding || transitioning || siteClosing) return; // objects are on the move: keep what is loaded
            HQPlayerController me = PlayerOf(conn);
            if (me == null) return;
            HQPlayerController targetPlayer = target >= 0 && networkManager.ServerManager.Clients.TryGetValue(target, out NetworkConnection targetConn) ? PlayerOf(targetConn) : null;
            if (targetPlayer != null && targetPlayer.gameObject.scene != me.gameObject.scene && WorldScenes.TryParse(targetPlayer.gameObject.scene.name, out WorldId world))
                ServerWatch(conn, world);
            else
                ServerUnwatch(conn);
        }

        // Load `world` on this connection as an observer: added to the scene first
        // so nothing is missed, then the load without a preferred active scene.
        public void ServerWatch(NetworkConnection conn, WorldId world)
        {
            if (watching.TryGetValue(conn.ClientId, out WorldId current))
            {
                if (current == world) return;
                ServerUnwatch(conn);
            }
            Scene scene = WorldScenes.Scene(world);
            if (!scene.IsValid() || !scene.isLoaded) return;
            watching[conn.ClientId] = world;
            networkManager.SceneManager.AddConnectionToScene(conn, scene);
            EnsureHolderKeepAlive();
            networkManager.SceneManager.LoadConnectionScenes(new[] { conn }, WatchDataFor(world));
            Debug.Log($"[WorldSceneFlow] {DisplayName(conn)} watches {WorldScenes.Name(world)}");
        }

        // Drop the watched world from this connection, unless its own object
        // stands there now (then it is simply its world).
        public void ServerUnwatch(NetworkConnection conn)
        {
            if (!watching.TryGetValue(conn.ClientId, out WorldId world)) return;
            watching.Remove(conn.ClientId);
            if (!conn.IsActive) return;
            HQPlayerController me = PlayerOf(conn);
            if (me != null && me.gameObject.scene == WorldScenes.Scene(world)) return;
            if (!WorldScenes.IsLoaded(world)) return;
            networkManager.SceneManager.UnloadConnectionScenes(new[] { conn }, UnloadDataFor(world, keepOnServer: true));
            Debug.Log($"[WorldSceneFlow] {DisplayName(conn)} stops watching {WorldScenes.Name(world)}");
        }

        // Connections that hold `world` only for watching; forgotten here because
        // the caller unloads it for them (a site unload must take its watchers).
        private List<NetworkConnection> TakeWatchersOf(WorldId world)
        {
            var conns = new List<NetworkConnection>();
            foreach (int id in new List<int>(watching.Keys))
            {
                if (watching[id] != world) continue;
                watching.Remove(id);
                if (networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) && conn.IsActive) conns.Add(conn);
            }
            return conns;
        }
    }
}
