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

    // What the last press of the pay button did (Dan, 16 September 2026): the
    // box sold for Sales, the quota met or missed. Shown by the HQ board.
    public struct PayReport
    {
        public int Serial;
        public int Sales;
        public int Quota;
        public int Had;     // handed over this cycle, this sale included — what the quota is judged on
        public int Balance; // the crew's money after the sale: every dollar handed over is theirs (Dan, 17 September 2026)
        public bool Paid;
        public bool Short;  // short before payday: the box is banked, the day count goes on
        public bool Lost;   // short at payday: the run is over, everything reset
    }

    // Who a dead player watches (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 2):
    // server-written, one entry per dead player; Target is a living client id or
    // -1 when nobody living is left. Watcher counts are derived from the list.
    // The plank (18 September 2026): who is on the board now and since when.
    public struct PlankState
    {
        public bool Active;
        public int Jumper;        // client id on the board, -1 between turns
        public uint TurnStartTick; // the jumper's turn began (server tick)
        public int Serial;
    }

    // The card at the end of a lost run: "the game is over — N days — M minutes".
    public struct RunOverReport
    {
        public int Serial;
        public int Days;    // dive days begun this run, across cycles
        public int Minutes; // wall time from the run's start
    }

    // A crew wipe (Dan, 29 September 2026): every connected player dead at sea. The
    // server writes Active with a new serial when the NOBODY CAME BACK card goes up
    // (the death settled: the site closed with the dead on the ship) and clears it
    // once the day has ended as End day ends it; every peer shows the card between.
    public struct CrewWipeState
    {
        public int Serial;
        public bool Active;
        public uint StartTick; // server tick the card went up
    }

    public struct SpectateEntry
    {
        public int Dead;
        public int Target;
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
        // Today's dive has happened and everyone is up: nobody goes down again until
        // the crew ends the day at the monitor (Dan, 16 September 2026: "the crew
        // ends the day — but remember they can go down only once").
        private readonly SyncVar<bool> diveDone = new(false);
        // The crew's money (Dan, 16 September 2026; 17 September: "the players get
        // money for every dollar they give — 510 for a 500 quota is $510"): the pay
        // button at HQ sells the storage room into it and nothing is charged out of
        // it; the quota is a bar the cycle's hand-over must clear, not a fee. Server-
        // written; the HQ board, the box readout and the visor read it.
        private readonly SyncVar<int> balance = new(0);
        // What the crew has handed over in this cycle (a short sale before payday
        // is banked and counts); zero once the quota is paid or the run is lost.
        // Money already theirs from earlier cycles never pays a later quota.
        private readonly SyncVar<int> cycleSales = new(0);
        private readonly SyncVar<PayReport> lastPay = new(new PayReport { Serial = 0 });
        private float lastPayAt = float.NegativeInfinity;
        // What the storage room holds, summed by the server (the only peer that
        // always has the ship loaded); the box readout, the visor and the board show it.
        private readonly SyncVar<int> boxValue = new(0);
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
        // The Elevator Ghost's green window on the car (the monsters, 20 September
        // 2026): tick-anchored like the phase; WorldSceneFlow is its only writer.
        private readonly SyncVar<SunkCost.Monsters.GhostPhase> ghost = new(new SunkCost.Monsters.GhostPhase { Active = false });
        private readonly SyncList<int> riders = new();
        private readonly SyncList<RiderPlacement> placements = new();
        private readonly SyncList<int> below = new();
        // Dead this day (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 1): never
        // below, never a rider, never waited for; revived at End day.
        private readonly SyncList<int> dead = new();
        // Who each dead player watches (card 2); WorldSceneFlow is its only writer.
        private readonly SyncList<SpectateEntry> spectate = new();
        // The deck TV's channel (card 3): a living diver below, or -1 (NO SIGNAL);
        // WorldSceneFlow is its only writer.
        private readonly SyncVar<int> tvChannel = new(-1);
        // The plank and the run's end (18 September 2026); WorldSceneFlow is the writer.
        private readonly SyncVar<PlankState> plank = new(new PlankState { Active = false, Jumper = -1 });
        private readonly SyncList<int> jumped = new();
        // The crew's vote to give up the run at the HQ board (Dan, 27 September 2026):
        // who has pressed GIVE UP, and how many the crew counts; everyone agreed is the plank.
        private readonly SyncList<int> giveUpVoters = new();
        private readonly SyncVar<int> giveUpCrew = new(0);
        private readonly SyncVar<RunOverReport> runOver = new(new RunOverReport { Serial = 0 });
        private readonly SyncVar<int> runDays = new(0);   // dive days begun this run (the card counts them)
        private float runStartTime;                        // server: when this run began
        // The shared console (Dan, 27 September 2026; docs/DESIGN.md "The navigation
        // console"): the card the crew selected on the ship's screen, the site the ship
        // is at (HERE) and the one it is heading for (they mirror World/Destination so
        // "SAILING TO SITE 02" is exact and a cancelled trip restores cleanly), the
        // sites bought this run (a Destinations.Bit mask; cleared on the plank, kept in
        // the save), and the last accepted lever pull every rig swings its lever from.
        // WorldSceneFlow is the only writer of all five.
        private readonly SyncVar<SiteId> selectedSite = new(SiteId.None);
        private readonly SyncVar<SiteId> currentSite = new(SiteId.HQ);
        private readonly SyncVar<SiteId> siteDestination = new(SiteId.HQ);
        private readonly SyncVar<int> unlockedSites = new(0);
        private readonly SyncVar<LeverPull> lastLeverPull = new(new LeverPull { Serial = 0 });
        // The crew wipe's card (Dan, 29 September 2026); WorldSceneFlow is the only writer.
        private readonly SyncVar<CrewWipeState> crewWipe = new(new CrewWipeState { Serial = 0 });

        public static CrewDayState Instance { get; private set; }
        public static event Action<CrewDayState> InstanceChanged;

        public DayPhase Phase => phase.Value;
        // 0 at HQ; at sea the number of the current day (the one being dived, or
        // the next one to dive). Stays at the last day through payday.
        public int Day => day.Value;
        public bool DiveDone => diveDone.Value;
        // The cycle's dives are spent: the monitor offers only HQ, the deck cabin refuses,
        // and at HQ the ship stays docked until the quota is paid.
        public bool Payday => payday.Value;
        public int Balance => balance.Value;
        public int CycleSales => cycleSales.Value;
        public int BoxValue => boxValue.Value;
        public PayReport LastPay => lastPay.Value;
        // Local time the last pay report arrived on this peer.
        public float LastPayAt => lastPayAt;
        public WorldId World => world.Value;
        public WorldId Destination => destination.Value;
        public bool Sailing => phase.Value == DayPhase.Sailing || phase.Value == DayPhase.SailingHome;
        public Refusal LastRefusal => lastRefusal.Value;
        public ShipDepartureState Departure => departure.Value;
        // Between the lock and the release: joins, item actions and monitor presses are refused.
        public bool Travelling => departure.Value.Active;
        public CabinRideState CabinRide => cabinRide.Value;
        public ElevatorPhase Elevator => elevator.Value;
        public SunkCost.Monsters.GhostPhase Ghost => ghost.Value;
        public bool Riding => cabinRide.Value.Active;
        // The deck cabin is not available: a ride is running, or the car is not up at the top.
        public bool CabinAway => cabinRide.Value.Active || elevator.Value.State != SunkCost.Diving.ElevatorState.AtTop;
        public IReadOnlyList<int> Riders => riders;
        public IReadOnlyList<RiderPlacement> Placements => placements;
        public IReadOnlyList<int> Below => below;
        public IReadOnlyList<int> Dead => dead;
        public bool IsDead(int clientId) => dead.Contains(clientId);
        public IReadOnlyList<SpectateEntry> Spectate => spectate;
        // The living player a dead one watches; -1 when none (alive, or nobody living left).
        public int SpectateTargetOf(int clientId)
        {
            foreach (SpectateEntry e in spectate) if (e.Dead == clientId) return e.Target;
            return -1;
        }
        public int TvChannel => tvChannel.Value;
        public PlankState Plank => plank.Value;
        public IReadOnlyList<int> Jumped => jumped;
        public bool HasJumped(int clientId) => jumped.Contains(clientId);
        public int GiveUpVotes => giveUpVoters.Count;
        public int GiveUpCrew => giveUpCrew.Value;
        public bool HasVotedGiveUp(int clientId) => giveUpVoters.Contains(clientId);
        public RunOverReport RunOver => runOver.Value;
        public int RunDays => runDays.Value;
        // The console's state (server-written; every screen, sign, prompt and dot reads it).
        public SiteId SelectedSite => selectedSite.Value;
        public SiteId CurrentSite => currentSite.Value;
        public SiteId SiteDestination => siteDestination.Value;
        public int UnlockedSites => unlockedSites.Value;
        public bool IsOpen(SiteId id) => Destinations.IsOpen(id, unlockedSites.Value);
        public LeverPull LastLeverPull => lastLeverPull.Value;
        public CrewWipeState CrewWipe => crewWipe.Value;
        // Once per peer when the wipe's card goes up or comes down; never for a joiner's initial value.
        public event Action<CrewWipeState, CrewWipeState> CrewWipeChanged;
        // Once per peer for a NEW serial; never for a joiner's initial value (the rigs animate from it).
        public event Action<LeverPull> LeverPulled;
        // The court's count (Dan, 18 September 2026: "a ball through the hoop
        // counts"): baskets this run, server-written by HoopScore, shown on the
        // backboards. Nothing else reads it.
        private readonly SyncVar<int> baskets = new(0);
        public int Baskets => baskets.Value;
        [Server]
        public void ServerAddBasket() => baskets.Value = baskets.Value + 1;
        // Ephemeral celebration only: no buffered replay for a late joiner or load.
        // Persistent score still lives in baskets; this does not decide a basket.
        [Server]
        public void ServerCelebrateBasket(Vector3 rim) => ObserversBasketCelebration(rim, baskets.Value);
        [ObserversRpc(BufferLast = false)]
        private void ObserversBasketCelebration(Vector3 rim, int seed) => SunkCost.Look.HoopScore.CelebrateAt(rim, seed);
        public float RunSeconds => Time.unscaledTime - runStartTime; // server view
        public event Action<RunOverReport> RunOverChanged;
        // How many watch this player: the dead spectating it, plus the TV when it is the channel.
        public int WatchersOf(int clientId)
        {
            if (clientId < 0) return 0;
            int count = tvChannel.Value == clientId ? 1 : 0;
            foreach (SpectateEntry e in spectate) if (e.Target == clientId) count++;
            return count;
        }
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
        public string DebugStatus => $"phase={phase.Value} day={day.Value}{(diveDone.Value ? " diveDone" : string.Empty)}{(payday.Value ? " PAYDAY" : string.Empty)} balance={balance.Value} handed={cycleSales.Value} world={world.Value} to={destination.Value} ride={cabinRide.Value.Stage}/{cabinRide.Value.Direction} car={elevator.Value.State} riders=[{string.Join(",", riders)}] below=[{string.Join(",", below)}] dead=[{string.Join(",", dead)}] spectate=[{SpectateText()}] tv={tvChannel.Value} plank={(plank.Value.Active ? plank.Value.Jumper.ToString() : "off")} jumped=[{string.Join(",", jumped)}] runDays={runDays.Value} site={currentSite.Value} toSite={siteDestination.Value} selected={selectedSite.Value} unlocked={Destinations.MaskText(unlockedSites.Value)} lever={lastLeverPull.Value.Serial} wipe={crewWipe.Value.Serial}{(crewWipe.Value.Active ? " CARD" : string.Empty)}";
        private string SpectateText()
        {
            var parts = new List<string>();
            foreach (SpectateEntry e in spectate) parts.Add(e.Dead + ">" + e.Target);
            return string.Join(",", parts);
        }

        public event Action<DayPhase, DayPhase> PhaseChanged;
        public event Action<ShipDepartureState, ShipDepartureState> DepartureChanged;
        public event Action<CabinRideState, CabinRideState> CabinRideChanged;
        // The day count and payday, once per peer, never for a joiner's initial
        // values (the day card, WorldSceneFlow.DayCard).
        public event Action<int, int> DayChanged;
        public event Action<bool> PaydayChanged;

        private void Awake()
        {
            phase.OnChange += OnPhaseChanged;
            day.OnChange += OnDayChanged;
            payday.OnChange += OnPaydayChanged;
            lastRefusal.OnChange += OnRefusalChanged;
            lastPay.OnChange += OnPayChanged;
            runOver.OnChange += OnRunOverChanged;
            departure.OnChange += OnDepartureChanged;
            cabinRide.OnChange += OnCabinRideChanged;
            lastLeverPull.OnChange += OnLeverPullChanged;
            crewWipe.OnChange += OnCrewWipeChanged;
        }

        // The card now (a new serial up, or the same serial down), once per peer; a
        // joiner's initial value is old news: a late joiner never sees the card.
        private void OnCrewWipeChanged(CrewWipeState previous, CrewWipeState next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (!InitialSync(asServer)) CrewWipeChanged?.Invoke(previous, next);
        }

        // A pull accepted now (a new serial), once per peer; a joiner's initial value is
        // old news and never swings a lever (the same guard as refusals and sales).
        private void OnLeverPullChanged(LeverPull previous, LeverPull next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (next.Serial != 0 && next.Serial != previous.Serial && !InitialSync(asServer)) LeverPulled?.Invoke(next);
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

        // FishNet also raises OnChange for a joiner's initial values (in the frame of
        // OnStartClient): those are old news, not a refusal or a sale happening now,
        // and would show on the joiner's monitors for a few seconds (code check,
        // 18 September 2026).
        private int clientStartFrame = -1;
        private bool InitialSync(bool asServer) => !asServer && clientStartFrame == Time.frameCount;

        private void OnDayChanged(int previous, int next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (!InitialSync(asServer)) DayChanged?.Invoke(previous, next);
        }

        private void OnPaydayChanged(bool previous, bool next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (!InitialSync(asServer)) PaydayChanged?.Invoke(next);
        }

        private void OnPayChanged(PayReport previous, PayReport next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (next.Serial != 0 && !InitialSync(asServer)) lastPayAt = Time.unscaledTime;
        }

        private void OnRunOverChanged(RunOverReport previous, RunOverReport next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (next.Serial != 0 && !InitialSync(asServer)) RunOverChanged?.Invoke(next);
        }

        private void OnRefusalChanged(Refusal previous, Refusal next, bool asServer)
        {
            if (IsServerStarted && !asServer) return;
            if (next.Serial != 0 && !InitialSync(asServer)) lastRefusalAt = Time.unscaledTime;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Instance = this;
            InstanceChanged?.Invoke(this);
        }

        // The elevator's noise (server) and sounds (every client) ride on this
        // object: both read the replicated phase, neither replicates anything.
        public override void OnStartServer()
        {
            base.OnStartServer();
            runStartTime = Time.unscaledTime;
            if (GetComponent<SunkCost.Noise.ElevatorNoise>() == null) gameObject.AddComponent<SunkCost.Noise.ElevatorNoise>();
            // The day's monsters (server only): drawn and spawned when the site is fresh.
            if (GetComponent<SunkCost.Monsters.MonsterRoster>() == null) gameObject.AddComponent<SunkCost.Monsters.MonsterRoster>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            clientStartFrame = Time.frameCount;
            if (GetComponent<SunkCost.Audio.ElevatorSounds>() == null) gameObject.AddComponent<SunkCost.Audio.ElevatorSounds>();
            // The Ghost's green light and hum at the car (every client), from the replicated phase.
            if (GetComponent<SunkCost.Monsters.ElevatorGhostLight>() == null) gameObject.AddComponent<SunkCost.Monsters.ElevatorGhostLight>();
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
            if (phase.Value == DayPhase.Plank) { why = "The run is over"; return false; }
            if (phase.Value == DayPhase.Sailing || phase.Value == DayPhase.SailingHome) { why = "Already sailing."; return false; }
            if (phase.Value == DayPhase.DiveInProgress) { why = "Dive in progress."; return false; }
            if (payday.Value && to != WorldId.HQ) { why = world.Value == WorldId.HQ ? "Pay the quota first" : "Payday — only HQ"; return false; }
            // Home only at the start of a day (Dan, 17 September 2026): once today's
            // dive has happened the crew ends the day at the monitor first. A day
            // nobody has dived on yet (day 1 fresh from HQ included) may sail home.
            if (to == WorldId.HQ && diveDone.Value) { why = "Dive done — End day first"; return false; }
            if (to == world.Value) { why = "Already there."; return false; }
            why = string.Empty;
            return true;
        }

        // Kept for callers that sail by world alone: the site identity the world implies.
        [Server]
        public void ServerBeginSail(WorldId to) => ServerBeginSail(to, Destinations.SiteOf(to));

        // The trip's start: the world and the logical site it is bound for (Site02..04
        // sail to Site01's world until they have their own; the site stays theirs).
        [Server]
        public void ServerBeginSail(WorldId to, SiteId site)
        {
            destination.Value = to;
            siteDestination.Value = site == SiteId.None ? Destinations.SiteOf(to) : site;
            phase.Value = to == WorldId.HQ ? DayPhase.SailingHome : DayPhase.Sailing;
        }

        // A trip that stopped before anything moved: back to the docked/at-sea phase.
        [Server]
        public void ServerCancelSail()
        {
            destination.Value = world.Value;
            siteDestination.Value = currentSite.Value;
            phase.Value = world.Value == WorldId.HQ ? DayPhase.AtHQ : DayPhase.AtSea;
        }

        // ---- the console (27 September 2026) ----------------------------------------

        [Server]
        public void ServerSetSelectedSite(SiteId id)
        {
            if (selectedSite.Value != id) selectedSite.Value = id;
        }

        [Server]
        public void ServerClearSelection() => ServerSetSelectedSite(SiteId.None);

        // The site's bit goes into the mask. False when the site is not purchasable or
        // already open — no money moves here: the flow spends first, in the same server
        // call, so two pulls in one tick cannot both charge (the second finds it open).
        [Server]
        public bool ServerUnlockSite(SiteId id)
        {
            if (!Destinations.IsPurchasable(id) || Destinations.IsOpen(id, unlockedSites.Value)) return false;
            unlockedSites.Value = unlockedSites.Value | Destinations.Bit(id);
            return true;
        }

        // Editor checks only: every bought site locked again without a plank.
        [Server]
        public void ServerResetUnlocks()
        {
            if (unlockedSites.Value != 0) unlockedSites.Value = 0;
        }

        // Editor checks and restores: where the ship is, by site.
        [Server]
        public void ServerSetCurrentSite(SiteId site)
        {
            if (site == SiteId.None) site = Destinations.SiteOf(world.Value);
            currentSite.Value = site;
            siteDestination.Value = site;
        }

        // An accepted pull: Serial+1, the tick it was accepted on; every rig animates from it.
        [Server]
        public void ServerPullLever(ConsoleKind kind, LeverAction action, uint tick)
        {
            lastLeverPull.Value = new LeverPull { Serial = lastLeverPull.Value.Serial + 1, Kind = kind, Action = action, Tick = tick };
        }

        [Server]
        public void ServerAnnounceLeverPull(ConsoleKind kind, LeverAction action, uint tick) => ServerPullLever(kind, action, tick);

        [Server]
        public void ServerSetDeparture(ShipDepartureState next) => departure.Value = next;

        // Days are spent only by dives (Dan, 16 September 2026): sailing between
        // the site and HQ keeps the count; paying the quota resets it. A crew with
        // no cycle running (day 0) that leaves HQ is on day 1.
        [Server]
        public void ServerArrive(WorldId at)
        {
            world.Value = at;
            destination.Value = at;
            // HERE is the site the trip was bound for (the logical one, not the scene's).
            currentSite.Value = at == WorldId.HQ ? SiteId.HQ : (siteDestination.Value == SiteId.None || siteDestination.Value == SiteId.HQ ? Destinations.SiteOf(at) : siteDestination.Value);
            siteDestination.Value = currentSite.Value;
            phase.Value = at == WorldId.HQ ? DayPhase.AtHQ : DayPhase.AtSea;
            if (at != WorldId.HQ && day.Value == 0) day.Value = 1;
        }

        // The pay button (WorldSceneFlow.ServerPay decides whether it may be
        // pressed): the box's worth joins the balance — all of it, nothing is
        // charged (Dan, 17 September 2026) — and the cycle's hand-over is judged
        // against the quota. Paid: the cycle is over, the next dive is day 1.
        // Short at payday: the run is lost and everything starts from nothing
        // (Dan: "say game lost, start from the start"; walking the plank is a
        // later card).
        [Server]
        public PayReport ServerPay(int sales, int quota)
        {
            int sold = Mathf.Max(0, sales);
            int handed = cycleSales.Value + sold;
            bool paid = handed >= quota;
            bool lost = !paid && payday.Value; // short with no dives left
            balance.Value += sold;
            if (paid) { cycleSales.Value = 0; day.Value = 0; payday.Value = false; diveDone.Value = false; }
            else if (lost) { cycleSales.Value = handed; phase.Value = DayPhase.Plank; } // the plank (WorldSceneFlow.ServerBeginPlank), then ServerResetRun
            else cycleSales.Value = handed; // short, days left: the box is banked, dive again (Dan: "not a loss instantly")
            var report = new PayReport { Serial = lastPay.Value.Serial + 1, Sales = sold, Quota = quota, Had = handed, Balance = balance.Value, Paid = paid, Short = !paid && !lost, Lost = lost };
            lastPay.Value = report;
            return report;
        }

        // The shop (18 September 2026): the price leaves the pot if it is there.
        [Server]
        public bool ServerSpend(int amount)
        {
            if (amount < 0 || balance.Value < amount) return false;
            balance.Value -= amount;
            return true;
        }

#if UNITY_EDITOR
        // Editor checks only: money in the pot without a sale.
        [Server]
        public void ServerSetBalanceForChecks(int value) => balance.Value = Mathf.Max(0, value);
#endif

        // The day begins when the riders stand in the car below (plan section 4.1;
        // WorldSceneFlow calls it after ServerSetBelow). Refused on payday.
        [Server]
        public bool ServerBeginDay(out string why)
        {
            if (phase.Value != DayPhase.AtSea) { why = "Not at sea (" + phase.Value + ")."; return false; }
            if (payday.Value) { why = "Payday — sail home"; return false; }
            phase.Value = DayPhase.DiveInProgress;
            runDays.Value = runDays.Value + 1;
            why = string.Empty;
            return true;
        }

        // ---- the plank and the fresh run (18 September 2026) ----------------------------

        [Server]
        public void ServerSetPlank(PlankState next) => plank.Value = next;

        // GIVE UP at the board: a press toggles the presser's vote; the crew's size is
        // counted by the flow at each press. Returns whether the presser now votes.
        [Server]
        public bool ServerToggleGiveUp(int clientId, int crew)
        {
            bool voted;
            if (giveUpVoters.Contains(clientId)) { giveUpVoters.Remove(clientId); voted = false; }
            else { giveUpVoters.Add(clientId); voted = true; }
            if (giveUpCrew.Value != crew) giveUpCrew.Value = crew;
            return voted;
        }

        [Server]
        public void ServerClearGiveUp()
        {
            if (giveUpVoters.Count > 0) giveUpVoters.Clear();
            if (giveUpCrew.Value != 0) giveUpCrew.Value = 0;
        }

        // The whole crew agreed: the run ends as a missed quota does, on the plank.
        [Server]
        public void ServerGiveUp()
        {
            ServerClearGiveUp();
            phase.Value = DayPhase.Plank;
        }

        [Server]
        public void ServerMarkJumped(int clientId)
        {
            if (!jumped.Contains(clientId)) jumped.Add(clientId);
        }

        [Server]
        public void ServerSetRunOver(int days, int minutes)
        {
            runOver.Value = new RunOverReport { Serial = runOver.Value.Serial + 1, Days = days, Minutes = minutes };
        }

        // Everything from nothing (Dan: "start from the start"): day 0, $0, no cycle,
        // the run clock restarted, back at the dock. Players are the flow's to reset.
        [Server]
        public void ServerResetRun()
        {
            balance.Value = 0;
            cycleSales.Value = 0;
            day.Value = 0;
            payday.Value = false;
            diveDone.Value = false;
            runDays.Value = 0;
            baskets.Value = 0;
            runStartTime = Time.unscaledTime;
            jumped.Clear();
            plank.Value = new PlankState { Active = false, Jumper = -1, Serial = plank.Value.Serial + 1 };
            // The console: every bought site locked again, nothing selected, the ship at HQ
            // (the plank ends at the dock). The votes are the flow's to clear.
            if (unlockedSites.Value != 0) unlockedSites.Value = 0;
            if (selectedSite.Value != SiteId.None) selectedSite.Value = SiteId.None;
            currentSite.Value = SiteId.HQ;
            siteDestination.Value = SiteId.HQ;
            phase.Value = DayPhase.AtHQ;
        }

        // ---- the save (19 September 2026) ---------------------------------------------

        // The run as the slot keeps it (RunSave). Written at HQ only, so the phase
        // is AtHQ and nothing is riding, sailing or below.
        [Server]
        public void ServerCapture(RunSaveData into)
        {
            into.day = day.Value;
            into.payday = payday.Value;
            into.diveDone = diveDone.Value;
            into.cycleSales = cycleSales.Value;
            into.balance = balance.Value;
            into.runDays = runDays.Value;
            into.runSeconds = RunSeconds;
            into.baskets = baskets.Value;
            into.unlockedSites = unlockedSites.Value; // a bought site stays open through quit-and-continue (a fresh slot starts locked)
        }

        // A hosted slot, applied at server start: the crew is at HQ with its run
        // where the slot left it; the run clock resumes from the saved time.
        [Server]
        public void ServerRestore(RunSaveData from)
        {
            day.Value = Mathf.Max(0, from.day);
            payday.Value = from.payday;
            diveDone.Value = from.diveDone;
            cycleSales.Value = Mathf.Max(0, from.cycleSales);
            balance.Value = Mathf.Max(0, from.balance);
            runDays.Value = Mathf.Max(0, from.runDays);
            baskets.Value = Mathf.Max(0, from.baskets);
            runStartTime = Time.unscaledTime - Mathf.Max(0f, from.runSeconds);
            world.Value = WorldId.HQ;
            destination.Value = WorldId.HQ;
            unlockedSites.Value = from.unlockedSites;
            selectedSite.Value = SiteId.None;
            currentSite.Value = SiteId.HQ;
            siteDestination.Value = SiteId.HQ;
            phase.Value = DayPhase.AtHQ;
        }

        // When nobody living is below the dive is done: the phase returns to at-sea
        // (joins allowed, the monitor free) but the day is not over — the deck
        // cabin refuses until the crew ends it.
        [Server]
        public bool ServerEndDayIfDone(int daysPerCycle)
        {
            if (phase.Value != DayPhase.DiveInProgress || below.Count > 0) return false;
            phase.Value = DayPhase.AtSea;
            diveDone.Value = true;
            return true;
        }

        // The monitor's End day (Dan, 16 September 2026): only after today's dive,
        // with everyone up. The next day, or payday after the last.
        [Server]
        public bool ServerEndDay(int daysPerCycle, out string why)
        {
            if (!CanEndDay(out why)) return false;
            diveDone.Value = false;
            if (day.Value >= daysPerCycle) payday.Value = true;
            else day.Value = day.Value + 1;
            return true;
        }

        // The day state's half of End day's check (the flow adds the ride, the trip and
        // the dead still below): at sea, not payday, today's dive done.
        public bool CanEndDay(out string why)
        {
            why = string.Empty;
            if (phase.Value != DayPhase.AtSea) { why = phase.Value == DayPhase.DiveInProgress ? "Divers below" : "Not at sea"; return false; }
            if (payday.Value) { why = "Payday — sail home"; return false; }
            if (!diveDone.Value) { why = "Nobody has dived today"; return false; }
            return true;
        }

        // The crew wipe's card: up with a new serial, down with the same one.
        [Server]
        public void ServerBeginCrewWipe(uint tick) => crewWipe.Value = new CrewWipeState { Serial = crewWipe.Value.Serial + 1, Active = true, StartTick = tick };

        [Server]
        public void ServerEndCrewWipe()
        {
            CrewWipeState now = crewWipe.Value;
            if (now.Active) crewWipe.Value = new CrewWipeState { Serial = now.Serial, Active = false, StartTick = now.StartTick };
        }

        // Editor checks only: put the cycle where a row needs it (a payday at the
        // dock without three dives). Never called by gameplay.
        [Server]
        public void ServerForceCycleForChecks(int dayValue, bool paydayValue)
        {
            day.Value = dayValue;
            payday.Value = paydayValue;
            diveDone.Value = false;
        }

        [Server]
        public void ServerSetBoxValue(int value)
        {
            if (boxValue.Value != value) boxValue.Value = value;
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
        public void ServerSetGhost(SunkCost.Monsters.GhostPhase next) => ghost.Value = next;

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
            dead.Remove(clientId);
            ServerClearSpectate(clientId);
        }

        // ---- spectating (card 2) --------------------------------------------------

        [Server]
        public void ServerSetSpectateTarget(int deadId, int target)
        {
            for (int i = 0; i < spectate.Count; i++)
            {
                if (spectate[i].Dead != deadId) continue;
                if (spectate[i].Target != target) spectate[i] = new SpectateEntry { Dead = deadId, Target = target };
                return;
            }
            spectate.Add(new SpectateEntry { Dead = deadId, Target = target });
        }

        [Server]
        public void ServerSetTvChannel(int clientId)
        {
            if (tvChannel.Value != clientId) tvChannel.Value = clientId;
        }

        [Server]
        public void ServerClearSpectate(int deadId)
        {
            for (int i = spectate.Count - 1; i >= 0; i--) if (spectate[i].Dead == deadId) spectate.RemoveAt(i);
        }

        // A player died: out of the living lists, into the dead. Living now means
        // alive and connected for every check that used Below.
        [Server]
        public void ServerPlayerDied(int clientId)
        {
            if (!dead.Contains(clientId)) dead.Add(clientId);
            below.Remove(clientId);
            riders.Remove(clientId);
            for (int i = placements.Count - 1; i >= 0; i--) if (placements[i].ClientId == clientId) placements.RemoveAt(i);
        }

        [Server]
        public void ServerRevive(int clientId)
        {
            dead.Remove(clientId);
            ServerClearSpectate(clientId);
        }

        // The crew wipe's card too (29 September 2026): the day is still ending, the dead are about to stand up.
        public bool RefusesJoins => phase.Value == DayPhase.DiveInProgress || phase.Value == DayPhase.Plank || cabinRide.Value.Active || crewWipe.Value.Active;
        public bool RefusesJoinsForTravel => Travelling;
    }
}
