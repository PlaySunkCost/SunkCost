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

        // Every dead player: a valid target (else the nearest living, else none); the
        // TV: a valid channel (else the first living diver below, else none); then
        // the world each client needs beyond its own.
        private void ServerUpdateSpectators()
        {
            if (dayState == null || networkManager == null || !networkManager.IsServerStarted) return;
            foreach (int id in new List<int>(dayState.Dead))
            {
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                int target = dayState.SpectateTargetOf(id);
                if (!ServerIsWatchable(target)) dayState.ServerSetSpectateTarget(id, ServerNearestLiving(conn));
            }
            ServerValidateTvChannel();
            if (riding || transitioning || siteClosing) return; // objects are on the move: keep what is loaded
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive) continue;
                WorldId? want = ServerWantedWatch(conn);
                if (want.HasValue) ServerWatch(conn, want.Value); else ServerUnwatch(conn);
            }
        }

        // The world a client needs beyond the one its object stands in: a dead
        // player's target's world, or the dive world for a living player on the
        // ship while the TV has a channel (card 3). Never its own.
        private WorldId? ServerWantedWatch(NetworkConnection conn)
        {
            HQPlayerController me = PlayerOf(conn);
            if (me == null || !WorldScenes.TryParse(me.gameObject.scene.name, out WorldId mine)) return null;
            if (dayState.IsDead(conn.ClientId))
            {
                int target = dayState.SpectateTargetOf(conn.ClientId);
                HQPlayerController targetPlayer = target >= 0 && networkManager.ServerManager.Clients.TryGetValue(target, out NetworkConnection targetConn) ? PlayerOf(targetConn) : null;
                if (targetPlayer != null && WorldScenes.TryParse(targetPlayer.gameObject.scene.name, out WorldId theirs) && theirs != mine) return theirs;
                return null;
            }
            if (mine == WorldId.Sea && dayState.TvChannel >= 0) return WorldId.Dive;
            return null;
        }

        // ---- the TV (card 3) -------------------------------------------------------

        // The channel must be a living, connected diver below; else the first such,
        // else none. And only while someone living stands on the ship to watch: a
        // TV nobody sees has no channel, so ON AIR counts real watchers only.
        private void ServerValidateTvChannel()
        {
            if (!ServerAnyViewerAboard()) { dayState.ServerSetTvChannel(-1); return; }
            int channel = dayState.TvChannel;
            if (channel >= 0 && ServerIsChannel(channel)) return;
            dayState.ServerSetTvChannel(ServerFirstChannel());
        }

        private bool ServerAnyViewerAboard()
        {
            Scene sea = WorldScenes.Scene(WorldId.Sea);
            if (!sea.IsValid() || !sea.isLoaded) return false;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || dayState.IsDead(conn.ClientId)) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player != null && player.gameObject.scene == sea) return true;
            }
            return false;
        }

        private bool ServerIsChannel(int id) => id >= 0 && dayState.IsBelow(id) && ServerIsWatchable(id);

        private int ServerFirstChannel()
        {
            var below = new List<int>(dayState.Below);
            below.Sort();
            foreach (int id in below) if (ServerIsChannel(id)) return id;
            return -1;
        }

        // E on the screen (ShipControls): the next living diver below by client id
        // after the current channel, wrapping.
        public bool ServerTvNext(NetworkConnection presser, out string why)
        {
            why = string.Empty;
            if (dayState == null) { why = "No day state."; return false; }
            if (presser != null && dayState.IsDead(presser.ClientId)) { why = "The dead have no hands"; return false; }
            var channels = new List<int>();
            foreach (int id in dayState.Below) if (ServerIsChannel(id)) channels.Add(id);
            if (channels.Count == 0) { dayState.ServerSetTvChannel(-1); why = "NO SIGNAL — nobody below"; return false; }
            channels.Sort();
            int current = dayState.TvChannel;
            int next = channels[0];
            foreach (int id in channels) if (id > current) { next = id; break; }
            dayState.ServerSetTvChannel(next);
            return true;
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
            if (riding || transitioning || siteClosing) return;
            WorldId? want = ServerWantedWatch(conn);
            if (want.HasValue) ServerWatch(conn, want.Value); else ServerUnwatch(conn);
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
            ServerLoad(new[] { conn }, WatchDataFor(world), DisplayName(conn) + " watches");
            Debug.Log($"[WorldSceneFlow] {DisplayName(conn)} watches {WorldScenes.Name(world)}");
        }

        // Before a load that moves this connection's object into `destination`:
        // a watcher of that very world keeps it — the load lands the object in the
        // scene the client already holds and sets its active scene — and only a
        // watcher of another world drops it. Unloading the destination and loading
        // it again in the same tick (what card 2 did) crossed FishNet's observer
        // rebuild: the client got a second spawn of every object in the scene
        // ("already found in spawned"), then the unload's despawn took them away
        // for good, and the next ride read them as "expected to exist but does
        // not" — the other players were invisible to a revived spectator
        // (17 September 2026, the spectate matrix's guest A).
        private void ServerDropWatchBefore(NetworkConnection conn, WorldId destination)
        {
            if (!watching.TryGetValue(conn.ClientId, out WorldId world)) return;
            if (world == destination) { watching.Remove(conn.ClientId); Debug.Log($"[WorldSceneFlow] {DisplayName(conn)} keeps {WorldScenes.Name(world)}: its object is moving there"); return; }
            ServerUnwatch(conn);
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
            ServerUnload(new[] { conn }, UnloadDataFor(world, keepOnServer: true), DisplayName(conn) + " stops watching");
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
