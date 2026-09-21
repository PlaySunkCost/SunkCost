using FishNet.Connection;
using SunkCost.Diving;
using SunkCost.Monsters;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The Elevator Ghost (docs/DESIGN.md §6, 20 September 2026): when the car
    // comes back down for divers still below — never the ride down, never the
    // first arrival of the day — it rolls about one in four, at most once a day.
    // The car arrives and opens as always, but the light inside is green for
    // about 30 s (ElevatorGhostLight on every client). Step in while it is green
    // and the doors slam: the ordinary death, through ServerKill. When the light
    // turns white the car is clean. Server only; the phase is a tick-anchored
    // SyncVar on CrewDayState so every peer and a late joiner agree.
    public sealed partial class WorldSceneFlow
    {
        private int ghostDay = -1;
        public int GhostRolls { get; private set; } // server: how many return arrivals were rolled

        // The car reached the bottom (ServerTickElevator). A ride down has `riding`
        // set; an empty return trip has not.
        private void ServerCarArrivedBelow()
        {
            if (riding || dayState == null) return;
            MonsterSettings settings = MonsterSettings.Get();
            if (settings.GhostOncePerDay && ghostDay == dayState.Day) return;
            GhostRolls++;
            float chance = MonsterSettings.GhostChanceOverrideForTests ?? settings.GhostChance;
            if (Random.value >= chance) return;
            ghostDay = dayState.Day;
            GhostPhase previous = dayState.Ghost;
            dayState.ServerSetGhost(new GhostPhase
            {
                Serial = previous.Serial + 1,
                Active = true,
                StartTick = networkManager.TimeManager.Tick,
                DurationTicks = networkManager.TimeManager.TimeToTicks(settings.GhostSeconds),
                SlamSerial = previous.SlamSerial
            });
            Debug.Log($"[Monsters] the Elevator Ghost is in the car: green for {settings.GhostSeconds:0} s (day {dayState.Day})");
        }

        // Every server frame while the green is up: anyone living who steps into the
        // car dies, and the green ends; it ends by itself when its time is up or the
        // car leaves the bottom.
        private void ServerTickGhost()
        {
            GhostPhase phase = dayState.Ghost;
            if (!phase.Active) return;
            float seconds = MonsterSettings.Get().GhostSeconds;
            bool over = ElapsedSince(phase.StartTick) >= seconds || dayState.Elevator.State != ElevatorState.AtBottom || siteClosing;
            ElevatorController car = over ? null : Car();
            if (car == null) { ServerEndGhost(phase, false); return; }
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive || dayState.IsDead(conn.ClientId) || !dayState.IsBelow(conn.ClientId)) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null || player.IsDead || player.gameObject.scene != WorldScenes.Scene(WorldId.Dive)) continue;
                if (!car.IsInsideCar(player.transform.position + Vector3.up * 0.5f)) continue;
                if (ServerKill(conn, out string why, "taken by the Elevator Ghost")) { ServerEndGhost(dayState.Ghost, true); return; }
                Debug.Log("[Monsters] the Elevator Ghost had " + DisplayName(conn) + " in the car but the kill was refused: " + why);
            }
        }

        private void ServerEndGhost(GhostPhase phase, bool slammed)
        {
            dayState.ServerSetGhost(new GhostPhase
            {
                Serial = phase.Serial,
                Active = false,
                StartTick = phase.StartTick,
                DurationTicks = phase.DurationTicks,
                SlamSerial = slammed ? phase.SlamSerial + 1 : phase.SlamSerial
            });
            Debug.Log(slammed ? "[Monsters] the Elevator Ghost's doors slammed; the car is clean again" : "[Monsters] the Elevator Ghost left; the car is clean");
        }

        // The checks: the next return arrival is the Ghost's, whatever the day; or the
        // Ghost in the car now, with the car at the bottom.
        public void ServerResetGhostDayForChecks() => ghostDay = -1;
        public bool ServerSummonGhostForChecks(out string why)
        {
            why = string.Empty;
            if (dayState == null || !networkManager.IsServerStarted) { why = "no server"; return false; }
            if (dayState.Elevator.State != ElevatorState.AtBottom) { why = "the car is " + dayState.Elevator.State; return false; }
            MonsterSettings settings = MonsterSettings.Get();
            ghostDay = dayState.Day;
            GhostPhase previous = dayState.Ghost;
            dayState.ServerSetGhost(new GhostPhase { Serial = previous.Serial + 1, Active = true, StartTick = networkManager.TimeManager.Tick, DurationTicks = networkManager.TimeManager.TimeToTicks(settings.GhostSeconds), SlamSerial = previous.SlamSerial });
            return true;
        }
    }
}
