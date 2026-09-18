using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The plank (docs/DESIGN.md §8 "Failure"; Dan, 18 September 2026): the crew
    // sold everything, used its days and is still short at payday — the run is
    // over. They walk the plank at HQ one by one: the server puts the jumper at
    // the board's base, free to walk out and back; the water below ends the
    // walk, or a push after PlankTurnSeconds. The last one in: the card ("the
    // game is over — N days — M minutes"), then everything from nothing — day 0,
    // $0, base kit, no upgrades, everyone alive on the pier.
    public sealed partial class WorldSceneFlow
    {
        private readonly List<int> plankQueue = new();
        private Coroutine runEnding;

        // CrewDayState.ServerPay put the phase on Plank; the board's press ends here.
        [FishNet.Object.Server]
        private void ServerBeginPlank()
        {
            plankQueue.Clear();
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
                if (conn.IsActive && PlayerOf(conn) != null) plankQueue.Add(conn.ClientId);
            plankQueue.Sort();
            Debug.Log($"[Plank] The run is over: {string.Join(",", plankQueue)} walk the plank ({dayState.RunDays} days, {Mathf.RoundToInt(dayState.RunSeconds / 60f)} minutes)");
            ServerNextJumper();
        }

        // The next player in crew order who is not in the water yet; nobody left ends the run.
        [FishNet.Object.Server]
        private void ServerNextJumper()
        {
            HQPlank plank = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
            foreach (int id in plankQueue)
            {
                if (dayState.HasJumped(id)) continue;
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null) continue;
                if (plank != null && plank.Base != null) player.TargetPlace(conn, plank.Base.position, plank.WalkYaw);
                dayState.ServerSetPlank(new PlankState { Active = true, Jumper = id, TurnStartTick = networkManager.TimeManager.Tick, Serial = dayState.Plank.Serial + 1 });
                Debug.Log($"[Plank] {DisplayName(conn)} steps up ({Settings.PlankTurnSeconds:0} s)");
                return;
            }
            dayState.ServerSetPlank(new PlankState { Active = false, Jumper = -1, Serial = dayState.Plank.Serial + 1 });
            if (runEnding == null) runEnding = StartCoroutine(ServerEndRun());
        }

        // Every frame while the plank is active: whoever is in the water has jumped
        // (the jumper or anyone else who went in early); the jumper's time runs out.
        [FishNet.Object.Server]
        private void ServerTickPlank()
        {
            if (dayState == null || dayState.Phase != DayPhase.Plank) return;
            PlankState state = dayState.Plank;
            if (!state.Active) return;
            HQPlank plank = HQPlank.InScene(WorldScenes.Scene(WorldId.HQ));
            if (plank == null) { Debug.LogWarning("[Plank] No plank at HQ: the run ends without the walk."); dayState.ServerSetPlank(new PlankState { Active = false, Jumper = -1, Serial = state.Serial + 1 }); if (runEnding == null) runEnding = StartCoroutine(ServerEndRun()); return; }
            bool jumperDone = false;
            foreach (int id in plankQueue)
            {
                if (dayState.HasJumped(id)) continue;
                if (!networkManager.ServerManager.Clients.TryGetValue(id, out NetworkConnection conn) || !conn.IsActive) { dayState.ServerMarkJumped(id); if (id == state.Jumper) jumperDone = true; continue; } // a leaver is out of the queue
                HQPlayerController player = PlayerOf(conn);
                if (player == null) continue;
                if (plank.IsInWater(player.transform.position))
                {
                    dayState.ServerMarkJumped(id);
                    Debug.Log($"[Plank] {DisplayName(conn)} is in the water");
                    if (id == state.Jumper) jumperDone = true;
                }
            }
            if (!jumperDone && ElapsedSince(state.TurnStartTick) >= Settings.PlankTurnSeconds)
            {
                // Pushed: just past the board's end, and down.
                if (networkManager.ServerManager.Clients.TryGetValue(state.Jumper, out NetworkConnection conn) && conn.IsActive)
                {
                    HQPlayerController player = PlayerOf(conn);
                    Vector3 drop = plank.End != null ? plank.End.position + plank.End.forward * 0.9f + Vector3.up * 0.1f : player.transform.position;
                    if (player != null) player.TargetPlace(conn, drop, plank.WalkYaw);
                    Debug.Log($"[Plank] {DisplayName(conn)} was pushed");
                }
                // The water marks them next frame; the turn ends now so a stuck client cannot hold the queue.
                dayState.ServerMarkJumped(state.Jumper);
                jumperDone = true;
            }
            if (jumperDone) ServerNextJumper();
        }

        private IEnumerator ServerEndRun()
        {
            int days = dayState.RunDays, minutes = Mathf.RoundToInt(dayState.RunSeconds / 60f);
            dayState.ServerSetRunOver(days, minutes);
            Debug.Log($"[Plank] The game is over: {days} days, {minutes} minutes");
            yield return WaitSeconds(Settings.RunOverCardSeconds);
            ServerResetRun();
            runEnding = null;
        }

        // Everything from nothing: the day state, then every player — alive, empty
        // hands and slots, no upgrades, on a pier spawn point at HQ — and the world
        // as it was found: every loose item (bought tanks, bodies, whatever lies
        // on the ship or the pier) gone, each loaded scene's fixture spawned again
        // (Dan, 18 September 2026: "oxygen tanks are still on the ship after game over").
        [FishNet.Object.Server]
        private void ServerResetRun()
        {
            dayState.ServerResetRun();
            Scene hq = WorldScenes.Scene(WorldId.HQ);
            ServerClearWorldItems();
            List<Transform> points = hq.IsValid() && hq.isLoaded ? CrewSpawner.SpawnPointsIn(hq) : new List<Transform>();
            int k = 0;
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null) continue;
                if (dayState.IsDead(conn.ClientId)) { dayState.ServerRevive(conn.ClientId); deadWithSite.Remove(conn.ClientId); }
                ServerUnwatch(conn);
                if (player.Inventory != null) player.Inventory.ServerDropEverything();
                if (player.Upgrades != null) player.Upgrades.ServerClearAll();
                player.ServerSetDead(false);
                if (player.Vitals != null) player.Vitals.ServerRevive();
                if (points.Count > 0) { Transform p = points[k++ % points.Count]; player.TargetPlace(conn, p.position, p.eulerAngles.y); }
            }
            plankQueue.Clear();
            Debug.Log("[Plank] A fresh run: day 0, $0, everyone on the pier");
        }

        [FishNet.Object.Server]
        private void ServerClearWorldItems()
        {
            var gone = new List<CarryableItem>();
            foreach (CarryableItem item in CarryableItem.Spawned)
                if (item != null && item.IsSpawned && WorldScenes.TryParse(item.gameObject.scene.name, out _)) gone.Add(item);
            foreach (CarryableItem item in gone) item.NetworkObject.Despawn();
            int fixtures = 0;
            foreach (WorldId world in new[] { WorldId.HQ, WorldId.Sea, WorldId.Dive })
            {
                Scene scene = WorldScenes.Scene(world);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (LootFixtureSpawner fixture in root.GetComponentsInChildren<LootFixtureSpawner>(true)) { fixture.ServerRespawn(); fixtures++; }
            }
            Debug.Log($"[Plank] The world's items: {gone.Count} despawned, {fixtures} fixture(s) spawned again");
        }

        // ---- clients: the card ---------------------------------------------------------

        private void OnRunOverChanged(RunOverReport report)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null) return;
            ScreenFade.Instance.FadeOut(1f, $"THE GAME IS OVER\n{report.Days} {(report.Days == 1 ? "day" : "days")} · {report.Minutes} {(report.Minutes == 1 ? "minute" : "minutes")}");
        }

        private void OnPhaseChangedForPlank(DayPhase previous, DayPhase next)
        {
            if (!networkManager.ClientManager.Started || ScreenFade.Instance == null) return;
            if (previous == DayPhase.Plank && next == DayPhase.AtHQ && !ScreenFade.Instance.IsClear) ScreenFade.Instance.FadeIn(1.5f);
        }
    }
}
