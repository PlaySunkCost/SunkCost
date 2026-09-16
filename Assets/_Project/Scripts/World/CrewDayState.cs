using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Net;
using UnityEngine;

namespace SunkCost.World
{
    // A refusal the server wrote for the monitor or cabin panel to show. The
    // serial makes the same text twice arrive twice.
    public struct Refusal
    {
        public int Serial;
        public string Text;
    }

    // The one server-owned object that knows where the crew is. It is a global
    // NetworkObject (prefab flag "Is Global"), so it lives in DontDestroyOnLoad,
    // has no observer conditions and survives every world scene change
    // (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.1). The scene-flow card
    // gave it the phase and the world; the day-state card (Dan, 16 September
    // 2026) the day counter and payday: Day is 0 at HQ and 1..DaysPerCycle at
    // sea; a day begins when the riders arrive below and ends by itself when the
    // last living player is up (living = connected until a death system exists);
    // after the last day Payday holds until the ship docks at HQ.
    public sealed class CrewDayState : NetworkBehaviour, INetworkDebugInfo
    {
        private readonly SyncVar<DayPhase> phase = new(DayPhase.AtHQ);
        private readonly SyncVar<int> day = new(0);
        private readonly SyncVar<bool> payday = new(false);
        private readonly SyncVar<WorldId> world = new(WorldId.HQ);
        // The world a sail is heading for; equals World when not sailing.
        private readonly SyncVar<WorldId> destination = new(WorldId.HQ);
        private readonly SyncVar<Refusal> lastRefusal = new(new Refusal { Serial = 0, Text = string.Empty });
        private float lastRefusalAt = float.NegativeInfinity;
        // The trip in progress (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md section 4);
        // WorldSceneFlow is its only writer.
        private readonly SyncVar<ShipDepartureState> departure = new(new ShipDepartureState { Stage = DepartureStage.Idle });
        // The cabin ride and the seafloor car (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md
        // sections 5.3, 5.4 and 6, without the day rules for now: no day counter,
        // no once-per-day, no all-aboard). Riders: the client ids on the current
        // ride; Placements: their cabin-frame spots; Below: everyone whose player
        // is at the seafloor.
        private readonly SyncVar<CabinRideState> cabinRide = new(new CabinRideState { Stage = CabinRideStage.Idle });
        private readonly SyncVar<ElevatorPhase> elevator = new(new ElevatorPhase { State = SunkCost.Diving.ElevatorState.AtTop });
        private readonly SyncList<int> riders = new();
        private readonly SyncList<RiderPlacement> placements = new();
        private readonly SyncList<int> below = new();

        public static CrewDayState Instance { get; private set; }
        public static event Action<CrewDayState> InstanceChanged;

        public DayPhase Phase => phase.Value;
        // 0 at HQ; at sea the number of the current day (the one being dived, or
        // the next one to dive). Stays at the last day through payday.
        public int Day => day.Value;
        // The cycle's dives are spent: the monitor offers only HQ, the deck cabin refuses.
        public bool Payday => payday.Value;
        public WorldId World => world.Value;
        public WorldId Destination => destination.Value;
        public bool Sailing => phase.Value == DayPhase.Sailing || phase.Value == DayPhase.SailingHome;
        public Refusal LastRefusal => lastRefusal.Value;
        public ShipDepartureState Departure => departure.Value;
        // Between the lock and the release: joins, item actions and monitor presses are refused.
        public bool Travelling => departure.Value.Active;
        public CabinRideState CabinRide => cabinRide.Value;
        public ElevatorPhase Elevator => elevator.Value;
        public bool Riding => cabinRide.Value.Active;
        // The deck cabin is not available: a ride is running, or the car is not up at the top.
        public bool CabinAway => cabinRide.Value.Active || elevator.Value.State != SunkCost.Diving.ElevatorState.AtTop;
        public IReadOnlyList<int> Riders => riders;
        public IReadOnlyList<RiderPlacement> Placements => placements;
        public IReadOnlyList<int> Below => below;
        public bool IsRider(int clientId) => riders.Contains(clientId);
        public bool IsBelow(int clientId) => below.Contains(clientId);
        public bool TryGetPlacement(int clientId, out RiderPlacement placement)
        {
            foreach (RiderPlacement p in placements) if (p.ClientId == clientId) { placement = p; return true; }
            placement = default;
            return false;
        }
        // Local time the last refusal arrived on this peer; panels show it for
        // WorldLoopSettings.refusalDisplaySeconds from then.
        public float LastRefusalAt => lastRefusalAt;
        public bool? WriterOverride => null;
        public string DebugStatus => $"phase={phase.Value} day={day.Value}{(payday.Value ? " PAYDAY" : string.Empty)} world={world.Value} to={destination.Value} ride={cabinRide.Value.Stage}/{cabinRide.Value.Direction} car={elevator.Value.State} riders=[{string.Join(",", riders)}] below=[{string.Join(",", below)}]";

        public event Action<DayPhase, DayPhase> PhaseChanged;
        public event Action<ShipDepartureState, ShipDepartureState> DepartureChanged;
        public event Action<CabinRideState, CabinRideState> CabinRideChanged;

        private void Awake()
        {
            phase.OnChange += OnPhaseChanged;
            lastRefusal.OnChange += OnRefusalChanged;
            departure.OnChange += OnDepartureChanged;
            cabinRide.OnChange += OnCabinRideChanged;
        }

        private void OnCabinRideChanged(CabinRideState previous, CabinRideState next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            CabinRideChanged?.Invoke(previous, next);
        }

        private void OnDepartureChanged(ShipDepartureState previous, ShipDepartureState next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer, like the phase
            DepartureChanged?.Invoke(previous, next);
        }

        private void OnRefusalChanged(Refusal previous, Refusal next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (next.Serial != 0) lastRefusalAt = Time.unscaledTime;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Instance = this;
            InstanceChanged?.Invoke(this);
        }

        public override void OnStopNetwork()
        {
            if (Instance == this)
            {
                Instance = null;
                InstanceChanged?.Invoke(null);
            }
            base.OnStopNetwork();
        }

        private void OnPhaseChanged(DayPhase previous, DayPhase next, bool asServer)
        {
            // Both the host's server and client pass see this; fire once per peer.
            if (IsServerStarted && !asServer) return;
            PhaseChanged?.Invoke(previous, next);
        }

        // ---- server -------------------------------------------------------

        [Server]
        public bool ServerCanSail(WorldId to, out string why)
        {
            if (to == WorldId.Dive) { why = "The ship does not sail to the seafloor."; return false; }
            if (phase.Value == DayPhase.Sailing || phase.Value == DayPhase.SailingHome) { why = "Already sailing."; return false; }
            if (phase.Value == DayPhase.DiveInProgress) { why = "Dive in progress."; return false; }
            if (payday.Value && to != WorldId.HQ) { why = "Payday — only HQ"; return false; }
            if (to == world.Value) { why = "Already there."; return false; }
            why = string.Empty;
            return true;
        }

        [Server]
        public void ServerBeginSail(WorldId to)
        {
            destination.Value = to;
            phase.Value = to == WorldId.HQ ? DayPhase.SailingHome : DayPhase.Sailing;
        }

        // A trip that stopped before anything moved: back to the docked/at-sea phase.
        [Server]
        public void ServerCancelSail()
        {
            destination.Value = world.Value;
            phase.Value = world.Value == WorldId.HQ ? DayPhase.AtHQ : DayPhase.AtSea;
        }

        [Server]
        public void ServerSetDeparture(ShipDepartureState next) => departure.Value = next;

        // Docking at HQ ends the cycle (day 0, no payday); leaving HQ starts one at
        // day 1. Arriving at another site mid-cycle would keep the count.
        [Server]
        public void ServerArrive(WorldId at)
        {
            bool fromHQ = world.Value == WorldId.HQ;
            world.Value = at;
            destination.Value = at;
            phase.Value = at == WorldId.HQ ? DayPhase.AtHQ : DayPhase.AtSea;
            if (at == WorldId.HQ) { day.Value = 0; payday.Value = false; }
            else if (fromHQ) { day.Value = 1; payday.Value = false; }
        }

        // The day begins when the riders stand in the car below (plan section 4.1;
        // WorldSceneFlow calls it after ServerSetBelow). Refused on payday.
        [Server]
        public bool ServerBeginDay(out string why)
        {
            if (phase.Value != DayPhase.AtSea) { why = "Not at sea (" + phase.Value + ")."; return false; }
            if (payday.Value) { why = "Payday — sail home"; return false; }
            phase.Value = DayPhase.DiveInProgress;
            why = string.Empty;
            return true;
        }

        // The day ends by itself when nobody living is below (Dan, 16 September
        // 2026: "once all up or dead — it says day 2"). The last day ends in payday.
        [Server]
        public bool ServerEndDayIfDone(int daysPerCycle)
        {
            if (phase.Value != DayPhase.DiveInProgress || below.Count > 0) return false;
            phase.Value = DayPhase.AtSea;
            if (day.Value >= daysPerCycle) payday.Value = true;
            else day.Value = day.Value + 1;
            return true;
        }

        // A refused monitor or cabin request, for every peer's panel (plan 4.1).
        [Server]
        public void ServerReportRefusal(string why)
        {
            lastRefusal.Value = new Refusal { Serial = lastRefusal.Value.Serial + 1, Text = why ?? string.Empty };
        }

        // Joins are refused while a dive is in progress (design section 1) and while
        // the ship is travelling (a temporary transition lock, not a join policy).
        // ---- the cabin ride and the car (server) ----------------------------------

        [Server]
        public void ServerSetCabinRide(CabinRideState next) => cabinRide.Value = next;

        [Server]
        public void ServerSetElevator(ElevatorPhase next) => elevator.Value = next;

        [Server]
        public void ServerSetRiders(IEnumerable<int> clientIds)
        {
            riders.Clear();
            placements.Clear();
            foreach (int id in clientIds) riders.Add(id);
        }

        [Server]
        public void ServerSetPlacement(RiderPlacement placement)
        {
            for (int i = 0; i < placements.Count; i++)
                if (placements[i].ClientId == placement.ClientId) { placements[i] = placement; return; }
            placements.Add(placement);
        }

        [Server]
        public void ServerRemoveRider(int clientId)
        {
            riders.Remove(clientId);
            for (int i = placements.Count - 1; i >= 0; i--)
                if (placements[i].ClientId == clientId) placements.RemoveAt(i);
        }

        [Server]
        public void ServerClearRiders()
        {
            riders.Clear();
            placements.Clear();
        }

        [Server]
        public void ServerSetBelow(int clientId, bool isBelow)
        {
            if (isBelow) { if (!below.Contains(clientId)) below.Add(clientId); }
            else below.Remove(clientId);
        }

        [Server]
        public void ServerRemoveEverywhere(int clientId)
        {
            riders.Remove(clientId);
            for (int i = placements.Count - 1; i >= 0; i--) if (placements[i].ClientId == clientId) placements.RemoveAt(i);
            below.Remove(clientId);
        }

        public bool RefusesJoins => phase.Value == DayPhase.DiveInProgress || cabinRide.Value.Active;
        public bool RefusesJoinsForTravel => Travelling;
    }
}
