using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Net;

namespace SunkCost.World
{
    // The one server-owned object that knows where the crew is. It is a global
    // NetworkObject (prefab flag "Is Global"), so it lives in DontDestroyOnLoad,
    // has no observer conditions and survives every world scene change
    // (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.1). The scene-flow card
    // gives it the phase and the world; the day-state card adds the day counter
    // and the below/surfaced/dead lists.
    public sealed class CrewDayState : NetworkBehaviour, INetworkDebugInfo
    {
        private readonly SyncVar<DayPhase> phase = new(DayPhase.AtHQ);
        private readonly SyncVar<WorldId> world = new(WorldId.HQ);
        // The world a sail is heading for; equals World when not sailing.
        private readonly SyncVar<WorldId> destination = new(WorldId.HQ);

        public static CrewDayState Instance { get; private set; }
        public static event Action<CrewDayState> InstanceChanged;

        public DayPhase Phase => phase.Value;
        public WorldId World => world.Value;
        public WorldId Destination => destination.Value;
        public bool Sailing => phase.Value == DayPhase.Sailing || phase.Value == DayPhase.SailingHome;
        public bool? WriterOverride => null;
        public string DebugStatus => $"phase={phase.Value} world={world.Value} to={destination.Value}";

        public event Action<DayPhase, DayPhase> PhaseChanged;

        private void Awake()
        {
            phase.OnChange += OnPhaseChanged;
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

        [Server]
        public void ServerArrive(WorldId at)
        {
            world.Value = at;
            destination.Value = at;
            phase.Value = at == WorldId.HQ ? DayPhase.AtHQ : DayPhase.AtSea;
        }

        // The phase half of the day API (plan section 4.1): the deck-cabin card
        // calls ServerBeginDay when the car departs and the day-state card adds the
        // riders, the day counter and the below/surfaced/dead lists that turn
        // ServerEndDay into ServerEndDayIfDone. Until then the Local matrix drives
        // them directly (row S5).
        [Server]
        public bool ServerBeginDay(out string why)
        {
            if (phase.Value != DayPhase.AtSea) { why = "Not at sea (" + phase.Value + ")."; return false; }
            phase.Value = DayPhase.DiveInProgress;
            why = string.Empty;
            return true;
        }

        [Server]
        public void ServerEndDay()
        {
            if (phase.Value == DayPhase.DiveInProgress) phase.Value = DayPhase.AtSea;
        }

        // Joins are refused while a dive is in progress (design section 1).
        public bool RefusesJoins => phase.Value == DayPhase.DiveInProgress;
    }
}
