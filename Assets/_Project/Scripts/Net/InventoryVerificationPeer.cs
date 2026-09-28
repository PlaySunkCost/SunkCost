#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using FishNet;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Net
{
    // Opt-in Local-only verification for a second standalone process. No network
    // listener, arbitrary code execution, or Steam/session-auth bypass. A normal
    // game launch never creates this component. Release builds exclude it.
    [DefaultExecutionOrder(1000)] // its LateUpdate reads after every item has placed itself (the car pin)
    public sealed class InventoryVerificationPeer : MonoBehaviour
    {
        [Serializable] public sealed class Command
        {
            public int id;
            public string action;
            public string item = "Basketball";
            public Vector3 position;
            public Vector3 aim;
            public int slot;
        }

        private string directory;
        private int lastId;
        private float replyAt = -1f;
        private string actionResult;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-hq-inventory-test-dir");
            if (index < 0 || index + 1 >= args.Length || !Debug.isDebugBuild) return;
            var go = new GameObject("Inventory Verification Peer");
            DontDestroyOnLoad(go);
            go.AddComponent<InventoryVerificationPeer>().directory = Path.GetFullPath(args[index + 1]);
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(directory)) return;
            Turn();
            Jitter();
            WalkStep(); // the elevator-deck tester's walk (below)
            var nm = InstanceFinder.NetworkManager;
            // Answers as soon as the Local transport is bound, connected or not: a
            // joiner refused at admission reports the refusal through its snapshot.
            if (nm == null || !(nm.TransportManager.Transport is Tugboat)) return;
            try
            {
                Directory.CreateDirectory(directory);
                if (replyAt >= 0f && Time.unscaledTime >= replyAt)
                {
                    File.WriteAllText(Path.Combine(directory, "reply.txt"), "id=" + lastId + "; " + actionResult + "\n" + Snapshot());
                    replyAt = -1f;
                }
                string path = Path.Combine(directory, "command.json");
                if (replyAt >= 0f || !File.Exists(path)) return;
                Command command = JsonUtility.FromJson<Command>(File.ReadAllText(path));
                if (command == null || command.id <= lastId) return;
                lastId = command.id;
                actionResult = Execute(command);
                replyAt = Time.unscaledTime + 0.4f; // read after RPC and SyncVar ticks
            }
            catch (Exception exception) { Debug.LogError("Inventory verification: " + exception.Message); }
        }

        private long simBaseLatency, simJitter;
        private void Jitter()
        {
            if (simJitter <= 0) return;
            var nm = InstanceFinder.NetworkManager;
            var simulator = nm != null ? nm.TransportManager.LatencySimulator : null;
            if (simulator == null || !simulator.GetEnabled()) return;
            simulator.SetLatency(System.Math.Max(1, simBaseLatency + (long)UnityEngine.Random.Range(-simJitter, simJitter + 1)));
        }

        private float turnYawSpeed, turnPitchSwing, turnUntil = -1f, turnStarted;
        private void Turn()
        {
            if (turnUntil < 0f) return;
            if (Time.unscaledTime >= turnUntil) { turnUntil = -1f; return; }
            var player = FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
            if (player == null) return;
            player.transform.Rotate(0f, turnYawSpeed * Time.unscaledDeltaTime, 0f, Space.World);
            player.SetPitchForChecks(turnPitchSwing * Mathf.Sin((Time.unscaledTime - turnStarted) * 2f));
        }

        private string Execute(Command command)
        {
            var nmForSim = InstanceFinder.NetworkManager;
            var player = FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
            // Spawned copies keep the prefab's "(Clone)" suffix on a client and the
            // host's per-instance names never replicate: "#<objectId>" is exact.
            CarryableItem item = null;
            if (!string.IsNullOrEmpty(command.item) && command.item.StartsWith("#") && int.TryParse(command.item.Substring(1), out int objectId))
                item = FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).FirstOrDefault(i => i.IsSpawned && i.ObjectId == objectId);
            else
                item = FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == command.item || i.name == command.item + "(Clone)");
            if (player == null) return "No owned player";
            // Keep uncontrolled physical keyboard/mouse input out of hook tests.
            // Ordinary verification pauses input. Catalogue checks need their
            // own modal to stay open while snapshots and buys are delivered.
            if (!SessionInputGate.ShopOpen && command.action != "shop_open") SessionInputGate.OpenMenu();
            switch (command.action)
            {
                case "move":
                    player.TeleportLocal(command.position, player.Yaw);
                    return $"moved to {player.transform.position:F2} owner={player.IsOwner} locked={player.TravelLocked} controller={(player.Controller != null && player.Controller.enabled)} spawned={player.IsSpawned} scene={player.gameObject.scene.name}";
                case "look":
                {
                    Vector3 flat = new(command.aim.x, 0f, command.aim.z);
                    if (flat.sqrMagnitude > 0.0001f) player.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
                    player.SetPitchForChecks(-Mathf.Atan2(command.aim.y, flat.magnitude) * Mathf.Rad2Deg);
                    break;
                }
                case "grab": player.Inventory.RequestGrab(item); break;
                case "equip": player.Inventory.RequestEquip(command.slot); break;
                case "drop": player.Inventory.RequestDrop(); break;
                case "throw": player.Inventory.RequestUse(command.aim); break;
                case "basketball_shot": return BasketballShotProbe.Shoot(player, command.position);
                case "leave": FindFirstObjectByType<PrototypeSessionUI>().LeaveSession(); break;
                case "die": player.RequestDebugDeath(); break;
                case "air_down": if (player.Vitals != null) player.Vitals.RequestDebugAirDown(); break; // L: a step off the tank
                case "lamp": player.RequestLamp(command.slot != 0); break; // F: the headlamp's switch (slot 1 = on)
                case "dash": // Alt: a burst toward the aim's flat direction (forward with none), as the owner's key would
                {
                    Vector3 flat = new(command.aim.x, 0f, command.aim.z);
                    Vector3 local = flat.sqrMagnitude > 0.0001f ? player.transform.InverseTransformDirection(flat.normalized) : Vector3.forward;
                    bool dashed = player.TryDash(new Vector2(local.x, local.z));
                    return $"dash={dashed}; refusal='{player.DashRefusal}'";
                }
                // E held on the nearest living teammate (the peer cannot hold a key): the
                // request the hold would send once complete.
                case "patch":
                {
                    HQPlayerController nearest = null; float best = float.PositiveInfinity;
                    foreach (HQPlayerController other in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
                    {
                        if (other == player || !other.IsSpawned || other.IsDead) continue;
                        float d = Vector3.Distance(other.transform.position, player.transform.position);
                        if (d < best) { best = d; nearest = other; }
                    }
                    if (nearest == null || player.Vitals == null) return "No teammate";
                    player.Vitals.RequestPatchTeammate(nearest);
                    return $"patch requested on {nearest.OwnerId} at {best:0.0} m";
                }
                case "buy": if (player.Upgrades != null) player.Upgrades.RequestBuy(command.item); break; // E on a shop stand; the item id rides in the item field
                case "shop_open":
                    SessionInputGate.SetApplicationFocus(true); // opt-in test peer simulates foreground input
                    SessionInputGate.Resume();
                    foreach(var display in FindObjectsByType<SunkCost.Shop.ShopDisplay>(FindObjectsSortMode.None))
                        if(display.BrowsesCatalog) { player.GetComponent<SunkCost.Shop.ShopBrowserUI>()?.Open(display); break; }
                    break;
                case "shop_buy":
                    return player.GetComponent<SunkCost.Shop.ShopBrowserUI>()?.Buy(command.item)==true?"Catalogue purchase requested":"Catalogue purchase not available";
                case "shop_close": player.GetComponent<SunkCost.Shop.ShopBrowserUI>()?.Close(); break;
                case "spectate_next": player.RequestNextSpectate(); break; // left click while dead (card 2)
                case "tv_next": // E on the deck TV (card 3)
                    var tvControls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (tvControls == null) return "No ShipControls";
                    tvControls.RequestTvNext();
                    break;
                case "spawn_light": return SpawnLightItems(Mathf.Clamp(command.slot, 1, 4));
                // Server only (the monitor's request until the monitor card): the
                // world name rides in the item field.
                case "sail":
                    if (SunkCost.World.WorldSceneFlow.Instance == null) return "No WorldSceneFlow";
                    if (!Enum.TryParse(command.item, true, out SunkCost.World.WorldId target)) return "Unknown world " + command.item;
                    return SunkCost.World.WorldSceneFlow.Instance.ServerSail(target, out string why) ? "sailing to " + target : "refused: " + why;
                // The monitor's request as a client makes it: E on a button. The world
                // name rides in the item field.
                case "monitor":
                    if (!Enum.TryParse(command.item, true, out SunkCost.World.WorldId pressed)) return "Unknown world " + command.item;
                    var controls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (controls == null) return "No ShipControls";
                    controls.RequestSail(pressed);
                    break;
                // E on the HQ board's GIVE UP button: this player's vote, again to take it back.
                case "giveup":
                    var giveUp = player.GetComponent<SunkCost.World.ShipControls>();
                    if (giveUp == null) return "No ShipControls";
                    giveUp.RequestGiveUp();
                    break;
                // The old board's PAY RPC (ShipControls.RequestPay → WorldSceneFlow.ServerPay), sent
                // by a client: no in-game caller since the console, kept for the network HQ-4 retest.
                case "pay":
                    var payControls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (payControls == null) return "No ShipControls";
                    payControls.RequestPay();
                    return "requested pay";
                // The shared console (27 September 2026). E on a destination card: the site
                // text rides in the item field ("HQ", "Site02", "SITE 02", "3").
                case "select":
                {
                    if (!SunkCost.World.Destinations.TryParse(command.item, out SunkCost.World.SiteId card) || card == SunkCost.World.SiteId.None) return "Unknown site " + command.item;
                    var selectControls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (selectControls == null) return "No ShipControls";
                    selectControls.RequestSelect(card);
                    return "requested select " + card;
                }
                // E on the lever of the console named in the item field ("Ship"/"HQ"): the
                // expectation THIS peer's sign shows, as the owner's E sends it.
                case "lever":
                {
                    if (!Enum.TryParse(command.item, true, out SunkCost.World.ConsoleKind leverKind)) return "Unknown console " + command.item;
                    var leverControls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (leverControls == null) return "No ShipControls";
                    SunkCost.World.LeverAction leverAction = SunkCost.World.ConsoleRules.Expected(leverKind, null, out SunkCost.World.SiteId leverTarget);
                    leverControls.RequestLever(leverKind, leverAction, leverTarget);
                    return $"requested lever {leverKind}/{leverAction}/{leverTarget}";
                }
                // The race rows: exactly this expectation (item = the console, slot =
                // (int)LeverAction, position.x = (int)SiteId).
                case "lever_expect":
                {
                    if (!Enum.TryParse(command.item, true, out SunkCost.World.ConsoleKind expectKind)) return "Unknown console " + command.item;
                    var expectControls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (expectControls == null) return "No ShipControls";
                    var expectAction = (SunkCost.World.LeverAction)command.slot;
                    var expectTarget = (SunkCost.World.SiteId)Mathf.RoundToInt(command.position.x);
                    expectControls.RequestLever(expectKind, expectAction, expectTarget);
                    return $"requested lever {expectKind}/{expectAction}/{expectTarget}";
                }
                // Turns toward a console control by its object name, resolved in this
                // player's world's ship (two may be loaded on a host) or the HQ scene.
                case "look_control":
                {
                    Transform control = FindControl(command.item);
                    if (control == null) return "No control " + command.item;
                    Vector3 toControl = control.position - player.EyePosition;
                    Vector3 flatControl = new(toControl.x, 0f, toControl.z);
                    if (flatControl.sqrMagnitude > 0.0001f) player.transform.rotation = Quaternion.LookRotation(flatControl, Vector3.up);
                    player.SetPitchForChecks(-Mathf.Atan2(toControl.y, flatControl.magnitude) * Mathf.Rad2Deg);
                    return $"looking at {command.item}: distance={toControl.magnitude:0.00}";
                }
                // The deck cabin's button / the car's panel, as E would press them.
                case "cabin":
                case "car":
                    var cabinControls = player.GetComponent<SunkCost.World.ShipControls>();
                    if (cabinControls == null) return "No ShipControls";
                    if (command.action == "cabin") cabinControls.RequestCabin(); else cabinControls.RequestCar();
                    break;
                // Stance intent as the player's own Ctrl would give it (slot: 1 = crouch, 0 = stand).
                case "crouch":
                    var stance = player.GetComponent<SunkCost.Player.PlayerStance>();
                    if (stance == null) return "No PlayerStance";
                    stance.SetDesiredCrouch(command.slot != 0);
                    break;
                case "walk": StartWalk(command.aim, command.position.x, player); return "walking " + command.position.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s"; // elevator-deck tester
                case "elev_reset": ResetElevatorDeck(); ElevatorReset(); break; // elevator-deck and elevator-dive testers
                case "snapshot": break;
                // The guest's own screen, HUD and visor included, to a file (the item field is the path).
                case "capture": ScreenCapture.CaptureScreenshot(command.item); return "capturing " + command.item;
                // The screen as a player sees it: every command opens the session menu (above),
                // so this closes it for the frame the capture reads - visor on, no menu
                // panel - and the next command opens it again.
                case "capture_play": SunkCost.Net.SessionInputGate.Resume(); ScreenCapture.CaptureScreenshot(command.item); return "capturing " + command.item;
                case "voice_tone": FindFirstObjectByType<SunkCost.Audio.ProximityVoice>().StartLocalTestTone(); break;
                case "voice_off": FindFirstObjectByType<SunkCost.Audio.ProximityVoice>().SetMicrophone(false); break;
                case "voice_peer_mute": FindFirstObjectByType<SunkCost.Audio.ProximityVoice>().SetPeer(command.slot, true, 1); break;
                case "voice_peer_unmute": FindFirstObjectByType<SunkCost.Audio.ProximityVoice>().SetPeer(command.slot, false, 1); break;
                case "frames":
                    return SunkCost.Diagnostics.FrameTimeRecorder.Instance == null ? "no frame time recorder"
                        : SunkCost.Diagnostics.FrameTimeRecorder.Instance.Summary + "; " + SunkCost.Diagnostics.FrameTimeRecorder.Instance.HitchList;
                case "cargo_reset": cargoWorstStep = 0f; cargoFrames = 0; doorWorstOpenAtTop = 0f; cargoLastLocal.Clear(); break;
                case "name":
                {
                    SunkCost.Player.PlayerNamePrefs.Save(command.item);
                    player.GetComponent<SunkCost.Player.PlayerIdentity>()?.RequestDisplayName(command.item);
                    break;
                }
                case "colour": player.GetComponent<SunkCost.Player.PlayerIdentity>()?.RequestColour(command.slot); break;
                // Keeps turning: yaw at aim.x degrees a second while the pitch swings
                // ±aim.y degrees, for position.x seconds (0 stops). A watcher on another
                // machine measures how smoothly this arrives (RemoteSmoothnessRuntimeChecks).
                case "turn":
                    turnYawSpeed = command.aim.x; turnPitchSwing = command.aim.y;
                    turnUntil = command.position.x > 0f ? Time.unscaledTime + command.position.x : -1f;
                    turnStarted = Time.unscaledTime;
                    break;
                // FishNet's latency simulator on this peer's outgoing packets (development
                // builds only, like this whole peer): position = (latency ms, packet loss
                // 0..1, out-of-order 0..1), aim.x = jitter ms (the latency is re-rolled
                // ±jitter every frame; the simulator releases packets in order, so a slow
                // packet holds the quick ones behind it and they arrive in a burst — a
                // relay's jitter); slot 0 switches it off.
                case "netsim":
                {
                    var simulator = nmForSim.TransportManager.LatencySimulator;
                    simBaseLatency = (long)command.position.x; simJitter = (long)command.aim.x;
                    simulator.SetLatency(simBaseLatency);
                    simulator.SetPacketLoss(command.position.y);
                    simulator.SetOutOfOrder(command.position.z);
                    simulator.SetEnabled(command.slot != 0);
                    return $"netsim enabled={simulator.GetEnabled()} latency={simulator.GetLatency()}±{simJitter} loss={simulator.GetPacketLost()} outOfOrder={simulator.GetOutOfOrder()}";
                }
                case "frames_reset":
                    SunkCost.Diagnostics.FrameTimeRecorder.Instance?.Reset();
                    break;
                default: return "Unknown action";
            }
            return "requested " + command.action;
        }

        // Server only: extra basketballs for the four-slot regression rows, which
        // the saved fixture (three slot items) cannot fill on its own. Spawned from
        // the registered prefab and named for the hooks; clients see them as
        // "Basketball(Clone)" until HQPrototypeTestHooks.NameTestClones runs.
        public static string SpawnLightItems(int count)
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null || !nm.IsServerStarted) return "Not the server";
            NetworkObject prefab = null;
            for (int i = 0; i < nm.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = nm.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == "Basketball") { prefab = candidate; break; }
            }
            if (prefab == null) return "Basketball prefab is not registered";
            int existing = FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).Length;
            for (int i = 0; i < count; i++)
            {
                Vector3 position = new(2.5f + i * 0.6f, 1f, -2.5f);
                NetworkObject instance = Instantiate(prefab, position, Quaternion.identity);
                instance.name = "Basketball (" + (existing + i + 1) + ")";
                nm.ServerManager.Spawn(instance);
            }
            return "spawned " + count + " basketballs";
        }

        // Shaft tube card: the local player's submersion and the water standing in the
        // car, so the matrix can compare a guest's values with the host's.
        private static string Underwater()
        {
            SunkCost.Player.HQPlayerController local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            SunkCost.Player.PlayerSubmersion submersion = local != null ? local.GetComponent<SunkCost.Player.PlayerSubmersion>() : null;
            return submersion == null ? "none" : submersion.IsSubmerged + "/" + submersion.DepthMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // The car as this peer presents it: its door's opening, whether the network
        // drives it yet, and how still the loose items on its floor stayed while it
        // moved (the worst frame-to-frame change of an item's height above the
        // floor, and the worst gap between two frames' readings of an item's
        // spot in the car: a carried item reads 0, a lagging copy reads centimetres).
        private static float cargoWorstStep = 0f;
        private static int cargoFrames = 0;
        private static float doorWorstOpenAtTop = 0f;
        private static readonly System.Collections.Generic.Dictionary<int, Vector3> cargoLastLocal = new();

        private static string CarLine()
        {
            var car = SunkCost.World.WorldSceneFlow.FindCar();
            if (car == null) return "carDoor=none; carDriven=False; cargoFrames=0; cargoWorstStep=0";
            var door = car.GetComponentInChildren<SunkCost.Diving.ElevatorDoor>(true);
            int inside = 0, pinned = 0, restKnown = 0;
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
            {
                if (!item.IsSpawned || !item.CanGrabFromWorld || !car.IsInsideCar(item.transform.position + Vector3.up * 0.25f)) continue;
                inside++; if (item.PinnedToCar) pinned++; if (item.CarRestKnown) restKnown++;
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "carDoor={0:0.00}; carDriven={1}; carState={2}; cargoFrames={3}; cargoWorstStep={4:0.000}; doorWorstOpenAtTop={5:0.00}; itemsInCar={6}; pinned={7}; restKnown={8}",
                door == null ? -1f : door.OpenFraction, car.Driven, car.State, cargoFrames, cargoWorstStep, doorWorstOpenAtTop, inside, pinned, restKnown);
        }

        private void LateUpdate()
        {
            RecordElevatorDeck(); // the elevator-deck tester's per-frame recorder (above)
            RecordElevator(); // the elevator-dive tester's per-frame recorder (below)
            var car = SunkCost.World.WorldSceneFlow.FindCarCached();
            var localPlayer = SunkCost.World.WorldSceneFlow.LocalPlayer();
            if (car != null && localPlayer != null && localPlayer.gameObject.scene == car.gameObject.scene && (car.State == SunkCost.Diving.ElevatorState.AtTop || car.State == SunkCost.Diving.ElevatorState.Descending))
            {
                var door = car.GetComponentInChildren<SunkCost.Diving.ElevatorDoor>(true);
                if (door != null) doorWorstOpenAtTop = Mathf.Max(doorWorstOpenAtTop, door.OpenFraction);
            }
            bool moving = car != null && (car.State == SunkCost.Diving.ElevatorState.Descending || car.State == SunkCost.Diving.ElevatorState.Ascending);
            if (!moving) { cargoLastLocal.Clear(); return; }
            foreach (CarryableItem item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None))
            {
                // Pinned copies only: a copy left to its NetworkTransform (thrown mid-ride) lags by design.
                if (!item.IsSpawned || !item.CanGrabFromWorld || !item.PinnedToCar || !car.IsInsideCar(item.transform.position + Vector3.up * 0.25f)) continue;
                Vector3 local = car.transform.InverseTransformPoint(item.transform.position);
                if (cargoLastLocal.TryGetValue(item.ObjectId, out Vector3 last))
                {
                    cargoFrames++;
                    cargoWorstStep = Mathf.Max(cargoWorstStep, (local - last).magnitude);
                }
                cargoLastLocal[item.ObjectId] = local;
            }
        }

        // ---- the deck cabin of Dan's round elevator (elevator-deck tester, 28 September 2026) ----
        // What this peer PRESENTS of the deck cabin on the ship at sea, against what the
        // replicated ride state says (CrewDayState.CabinRide / Elevator), every frame:
        // the shutters' sweep, the shutter and doorway boxes, and whether the car shows.
        // Also run by the editor host (ElevatorDeckRuntimeChecks) from EditorApplication.update.
        private static int deckFrames, deckBadBox, deckBadDoorway, deckBadShown, deckShutFrames, deckShownRun;
        private static float deckWorstShutter, deckHalfAngle = float.NaN;
        private static string deckFirstBad = string.Empty;

        public static void ResetElevatorDeck()
        {
            deckFrames = deckBadBox = deckBadDoorway = deckBadShown = deckShutFrames = deckShownRun = 0;
            deckWorstShutter = 0f; deckFirstBad = string.Empty;
        }

        // DeckCabinCarPresent (WorldSceneFlow.Cabin), recomputed here from the replicated state.
        public static bool ExpectedDeckCarPresent()
        {
            var day = SunkCost.World.CrewDayState.Instance;
            if (day == null) return true;
            SunkCost.World.CabinRideState ride = day.CabinRide;
            if (ride.Active) return ride.Direction == SunkCost.World.RideDirection.Down ? ride.Stage <= SunkCost.World.CabinRideStage.Sealing : ride.Stage >= SunkCost.World.CabinRideStage.Loading;
            SunkCost.World.ElevatorPhase car = day.Elevator;
            return car.State == SunkCost.Diving.ElevatorState.AtTop || (car.State == SunkCost.Diving.ElevatorState.Sealing && !car.Upward);
        }

        private static bool AnyRendererOn(Transform part)
        {
            if (part == null) return false;
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true)) if (r.enabled) return true;
            return false;
        }

        public static void RecordElevatorDeck()
        {
            var flow = SunkCost.World.WorldSceneFlow.Instance;
            var ship = SunkCost.World.ShipParts.InWorld(SunkCost.World.WorldId.Sea);
            if (flow == null || ship == null || SunkCost.World.CrewDayState.Instance == null) return;
            Transform doorR = ship.DeckCabinDoorR, shutterR = ship.DeckCabinHousingDoorR;
            if (doorR == null || shutterR == null) return;
            bool present = ExpectedDeckCarPresent();
            float open = flow.DeckCabinOpenFraction();
            if (float.IsNaN(deckHalfAngle) && present && open >= 0.999f) deckHalfAngle = Mathf.Abs(Mathf.DeltaAngle(0f, doorR.localEulerAngles.y));
            if (float.IsNaN(deckHalfAngle) || deckHalfAngle < 1f) return;
            deckFrames++;
            float shownShutter = Mathf.Abs(Mathf.DeltaAngle(0f, shutterR.localEulerAngles.y)) / deckHalfAngle;
            float wantShutter = present ? open : 0f;
            float err = Mathf.Abs(shownShutter - wantShutter);
            if (err > deckWorstShutter) deckWorstShutter = err;
            bool shutShown = shownShutter <= 0.001f;
            if (shutShown) deckShutFrames++;
            Collider box = ship.DeckCabinShutterCollider, doorway = ship.DeckCabinDoorCollider;
            string bad = null;
            // (a sweep within 0.0005 of the threshold may read either way through the Euler angles)
            bool shutClear = shownShutter < 0.0005f || shownShutter > 0.002f;
            if (box != null && shutClear && box.enabled != shutShown) { deckBadBox++; bad = $"shutterBox={box.enabled} shutters={shownShutter:0.000}"; }
            float shownDoor = Mathf.Abs(Mathf.DeltaAngle(0f, doorR.localEulerAngles.y)) / deckHalfAngle;
            if (doorway != null && (shownDoor < 0.0005f || shownDoor > 0.002f) && doorway.enabled != (shownDoor <= 0.001f)) { deckBadDoorway++; bad = $"doorBox={doorway.enabled} doors={shownDoor:0.000}"; }
            // A replicated change may land between the flow's Update and this read: a mismatch counts once it lasts 3 frames.
            if (AnyRendererOn(ship.DeckCabinCarGlass) != present) { if (++deckShownRun >= 3) { deckBadShown++; bad = $"carShown={!present} present={present}"; } }
            else deckShownRun = 0;
            if (bad != null && deckFirstBad.Length == 0)
            {
                var day = SunkCost.World.CrewDayState.Instance;
                deckFirstBad = $"frame {deckFrames}: {bad} ride={day.CabinRide.Stage}/{day.CabinRide.Direction} car={day.Elevator.State}/{day.Elevator.Upward} open={open:0.000}";
            }
        }

        // "elevatorDeck: ..." — this peer's deck cabin, its recorder and its last walk.
        public static string ElevatorDeckLine()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var flow = SunkCost.World.WorldSceneFlow.Instance;
            var ship = SunkCost.World.ShipParts.InWorld(SunkCost.World.WorldId.Sea);
            if (ship == null || flow == null) return "elevatorDeck: none\n";
            float half = float.IsNaN(deckHalfAngle) ? 23.578f : deckHalfAngle;
            Transform shutterR = ship.DeckCabinHousingDoorR, doorR = ship.DeckCabinDoorR;
            var display = ship.DeckCabinCarGlass != null ? ship.DeckCabinCarGlass.GetComponentInChildren<SunkCost.Diving.CabinPanelDisplay>(true) : null;
            string Flat(string s) => (s ?? string.Empty).Replace("\n", " | ").Replace(";", ",");
            return string.Format(ci,
                "elevatorDeck: deckPresent={0}; deckCarShown={1}; deckDoors={2:0.000}; doorsShown={3:0.000}; shutters={4:0.000}; shutterBox={5}; doorBox={6}; deckPanel='{7}'; deckScreen='{8}'; deckGauge={9:0.000}; " +
                "deckFrames={10}; deckWorstShutter={11:0.000}; deckBadBox={12}; deckBadDoorway={13}; deckBadShown={14}; deckShutFrames={15}; deckFirstBad='{16}'; " +
                "walkDone={17}; walked={18:0.00}; walkFrames={19}; walkGrounded={20}; walkMaxDrop={21:0.000}; walkMaxRise={22:0.000}; walkEnd={23}\n",
                ExpectedDeckCarPresent(), AnyRendererOn(ship.DeckCabinCarGlass), flow.DeckCabinOpenFraction(),
                doorR == null ? -1f : Mathf.Abs(Mathf.DeltaAngle(0f, doorR.localEulerAngles.y)) / half,
                shutterR == null ? -1f : Mathf.Abs(Mathf.DeltaAngle(0f, shutterR.localEulerAngles.y)) / half,
                ship.DeckCabinShutterCollider != null && ship.DeckCabinShutterCollider.enabled, ship.DeckCabinDoorCollider != null && ship.DeckCabinDoorCollider.enabled,
                Flat(ship.DeckCabinPanel != null ? ship.DeckCabinPanel.text : "none"), Flat(display != null ? display.ScreenText : "none"), display != null ? display.GaugeFraction : -1f,
                deckFrames, deckWorstShutter, deckBadBox, deckBadDoorway, deckBadShown, deckShutFrames, Flat(deckFirstBad),
                walkUntil < 0f && walkFrames > 0, walkDistance, walkFrames, walkGroundedFrames, walkMaxDrop, walkMaxRise, walkEnd.ToString("F3"));
        }

        // "walk": the owned capsule walks through CharacterController.Move (the collision the
        // player's own input would meet) along aim's flat direction at |aim| m/s for
        // position.x seconds, with a small push down so the ground is felt every frame.
        // The guest's menu gate blocks its real input, so the walk moves the controller itself.
        private static float walkUntil = -1f, walkDistance, walkMaxDrop, walkMaxRise, walkLastY;
        private static int walkFrames, walkGroundedFrames;
        private static Vector3 walkVelocity, walkStart, walkEnd;

        private static void StartWalk(Vector3 aim, float seconds, HQPlayerController player)
        {
            Vector3 flat = new(aim.x, 0f, aim.z);
            walkVelocity = flat;
            walkUntil = Time.unscaledTime + Mathf.Max(0.1f, seconds);
            walkFrames = walkGroundedFrames = 0; walkDistance = walkMaxDrop = walkMaxRise = 0f;
            walkStart = walkEnd = player.transform.position; walkLastY = walkStart.y;
            if (flat.sqrMagnitude > 0.0001f) player.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        private static void WalkStep()
        {
            if (walkUntil < 0f) return;
            var player = FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
            if (player == null || player.Controller == null || !player.Controller.enabled) { walkUntil = -1f; return; }
            if (Time.unscaledTime >= walkUntil) { walkUntil = -1f; walkEnd = player.transform.position; return; }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            CollisionFlags flags = player.Controller.Move((walkVelocity + Vector3.down * 2f) * dt);
            Vector3 p = player.transform.position;
            walkFrames++;
            if ((flags & CollisionFlags.Below) != 0) walkGroundedFrames++;
            float dy = p.y - walkLastY;
            if (-dy > walkMaxDrop) walkMaxDrop = -dy;
            if (dy > walkMaxRise) walkMaxRise = dy;
            walkLastY = p.y;
            walkDistance = new Vector3(p.x - walkStart.x, 0f, p.z - walkStart.z).magnitude;
            walkEnd = p;
        }

        // ---- the dive car of Dan's round elevator (elevator-dive tester, 28 September 2026) ----
        // What THIS peer presents of the car against the rules the replicated phase gives, every
        // frame: the car where its profile puts it on the tick clock, the one water truth, the
        // gauge, the doors and the gate (never open at the wrong time, together at the bottom),
        // the underwater view against the eye, and the bubbles (none above the water, none in a
        // still full car). `elev_reset` clears it. One "elevator:" line per snapshot reports it.
        private static int elevFrames, elevDoorWrong, elevGateWrong, elevViewStreak, elevViewWorst, elevSubStreak, elevSubWorst, elevBubbleStill, elevBubbleAboveFrames;
        private static float elevWorstY, elevWorstLag, elevWorstWater, elevWorstGauge, elevWorstGate, elevFirstFrameErr = -1f, elevViewWeight;
        private static SunkCost.Diving.ElevatorController elevCar;
        private static SunkCost.Diving.CabinWater elevWater;
        private static SunkCost.Diving.CabinWaterVisuals elevVisuals;
        private static SunkCost.Diving.CabinPanelDisplay elevDisplay;
        private static SunkCost.Diving.ElevatorDoor elevDoor;
        private static SunkCost.Diving.ShaftGate elevGate;
        private static Collider elevGateCollider;
        private static SunkCost.Diving.CarRingLight elevRing;
        private static Light elevCabinLight;
        private static ParticleSystem[] elevBubbles = System.Array.Empty<ParticleSystem>();
        private static readonly ParticleSystem.Particle[] elevParticles = new ParticleSystem.Particle[4096];
        private static int elevBubbleCount = -1;

        public static void ElevatorReset()
        {
            elevFrames = elevDoorWrong = elevGateWrong = elevViewStreak = elevViewWorst = elevSubStreak = elevSubWorst = elevBubbleStill = elevBubbleAboveFrames = 0;
            elevWorstY = elevWorstLag = elevWorstWater = elevWorstGauge = elevWorstGate = 0f;
            elevFirstFrameErr = -1f;
            elevCar = null; // the next frame with a car is a first frame again
        }

        private static void ElevatorBind(SunkCost.Diving.ElevatorController car)
        {
            elevCar = car;
            elevWater = car.GetComponent<SunkCost.Diving.CabinWater>();
            Transform fx = car.transform.Find("Cabin Water FX");
            elevVisuals = fx != null ? fx.GetComponent<SunkCost.Diving.CabinWaterVisuals>() : null;
            elevDisplay = car.GetComponentsInChildren<SunkCost.Diving.CabinPanelDisplay>(true).FirstOrDefault(d => d.DisplayMode == SunkCost.Diving.CabinPanelDisplay.Mode.Car);
            elevDoor = car.GetComponentInChildren<SunkCost.Diving.ElevatorDoor>(true);
            elevGate = null;
            foreach (GameObject root in car.gameObject.scene.GetRootGameObjects())
                if (elevGate == null) elevGate = root.GetComponentInChildren<SunkCost.Diving.ShaftGate>(true);
            elevGateCollider = elevGate == null ? null : (elevGate.GetComponent<Collider>() ?? elevGate.GetComponentInChildren<Collider>(true));
            elevRing = car.GetComponentInChildren<SunkCost.Diving.CarRingLight>(true);
            Transform bulb = car.transform.Find("Cabin Light");
            elevCabinLight = bulb != null ? bulb.GetComponent<Light>() : null;
            // The water rework's bubbles: particle systems under an object whose name contains "Bubble" in the FX.
            var bubbles = new System.Collections.Generic.List<ParticleSystem>();
            if (fx != null)
                foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                    for (Transform x = ps.transform; x != null && x != fx; x = x.parent)
                        if (x.name.IndexOf("Bubble", StringComparison.OrdinalIgnoreCase) >= 0) { bubbles.Add(ps); break; }
            elevBubbles = bubbles.ToArray();
        }

        public static void RecordElevator()
        {
            var car = SunkCost.World.WorldSceneFlow.FindCarCached();
            var flow = SunkCost.World.WorldSceneFlow.Instance;
            var day = SunkCost.World.CrewDayState.Instance;
            if (car == null || flow == null || day == null) return;
            bool first = car != elevCar;
            if (first) ElevatorBind(car);
            elevFrames++;

            // The car where its own elapsed time puts it (exact: SetDrivenPhase applies it), and
            // that elapsed time on the tick-anchored phase clock (within this frame).
            SunkCost.World.ElevatorPhase phase = day.Elevator;
            float progress = car.State switch
            {
                SunkCost.Diving.ElevatorState.AtTop => 0f,
                SunkCost.Diving.ElevatorState.AtBottom => 1f,
                SunkCost.Diving.ElevatorState.Sealing => car.Upward ? 1f : 0f,
                SunkCost.Diving.ElevatorState.Descending => SunkCost.Diving.ElevatorMath.ProgressAt(car.Profile, car.StateElapsed, false),
                SunkCost.Diving.ElevatorState.Ascending => SunkCost.Diving.ElevatorMath.ProgressAt(car.Profile, car.StateElapsed, true),
                _ => 0f
            };
            float yErr = Mathf.Abs(car.transform.position.y - Vector3.Lerp(car.TopPosition, car.BottomPosition, progress).y);
            elevWorstY = Mathf.Max(elevWorstY, yErr);
            if (phase.State == car.State)
                elevWorstLag = Mathf.Max(elevWorstLag, Mathf.Abs(flow.ElapsedSince(phase.StartTick) - car.StateElapsed) - Time.unscaledDeltaTime - 0.005f);

            // The one water truth and the gauge.
            float waterErr = 0f;
            if (elevWater != null)
            {
                waterErr = Mathf.Abs(elevWater.LevelMeters - SunkCost.Diving.ElevatorMath.WaterLevelInCar(car.SeaLevelY, car.transform.position.y, car.SpanMeters));
                elevWorstWater = Mathf.Max(elevWorstWater, waterErr);
                if (elevDisplay != null) elevWorstGauge = Mathf.Max(elevWorstGauge, Mathf.Abs(elevDisplay.GaugeFraction - elevWater.Level01));
            }
            if (first) elevFirstFrameErr = Mathf.Max(yErr, waterErr);

            // The doors and the gate.
            if (elevDoor != null && elevGate != null)
            {
                bool doorShut = car.State == SunkCost.Diving.ElevatorState.Descending || car.State == SunkCost.Diving.ElevatorState.Ascending ||
                    (car.Driven && (car.State == SunkCost.Diving.ElevatorState.AtTop || (car.State == SunkCost.Diving.ElevatorState.Sealing && !car.Upward)));
                if (doorShut && elevDoor.OpenFraction > 0.001f) elevDoorWrong++;
                bool atBottom = car.State == SunkCost.Diving.ElevatorState.AtBottom || (car.State == SunkCost.Diving.ElevatorState.Sealing && car.Upward);
                bool gateBlocks = elevGateCollider != null && elevGateCollider.enabled;
                if (!atBottom && (elevGate.OpenFraction > 0.001f || !gateBlocks)) elevGateWrong++;
                if (atBottom) elevWorstGate = Mathf.Max(elevWorstGate, Mathf.Abs(elevGate.OpenFraction - elevDoor.OpenFraction));
            }

            // The view: this player's camera against sea level (= the car's visible water for an eye in it).
            var local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            if (local != null && !local.IsDead && local.PlayerCamera != null && local.gameObject.scene == car.gameObject.scene)
            {
                Camera cam = local.PlayerCamera;
                float eye = cam.transform.position.y, sea = car.SeaLevelY;
                elevViewWeight = SunkCost.Sites.UnderwaterGrade.PrepareAll(cam);
                bool viewWrong = (eye < sea - 0.03f && elevViewWeight < 0.999f) || (eye > sea + 0.55f && elevViewWeight > 0.001f);
                elevViewStreak = viewWrong ? elevViewStreak + 1 : 0; elevViewWorst = Mathf.Max(elevViewWorst, elevViewStreak);
                var sub = local.GetComponent<SunkCost.Player.PlayerSubmersion>();
                bool subWrong = sub != null && Mathf.Abs(eye - sea) > 0.03f && sub.IsSubmerged != (eye < sea);
                elevSubStreak = subWrong ? elevSubStreak + 1 : 0; elevSubWorst = Mathf.Max(elevSubWorst, elevSubStreak);
            }
            else { elevViewStreak = 0; elevSubStreak = 0; }

            // The bubbles (DIVE-BUBBLES): none above the water; none in a still, full car.
            if (elevBubbles.Length > 0 && elevWater != null)
            {
                int count = 0, above = 0;
                foreach (ParticleSystem ps in elevBubbles)
                {
                    if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                    int n = ps.GetParticles(elevParticles);
                    count += n;
                    ParticleSystem.MainModule main = ps.main;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = elevParticles[i].position;
                        Vector3 world = main.simulationSpace == ParticleSystemSimulationSpace.World ? p
                            : main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null ? main.customSimulationSpace.TransformPoint(p)
                            : ps.transform.TransformPoint(p);
                        if (world.y > elevWater.SurfaceWorldY + 0.03f) above++;
                    }
                }
                elevBubbleCount = count;
                if (above > 0) elevBubbleAboveFrames++;
                float span = car.SpanMeters, y = car.transform.position.y;
                bool stillFull = elevWater.LevelMeters >= span - 0.02f &&
                    (car.State == SunkCost.Diving.ElevatorState.AtBottom || (car.State == SunkCost.Diving.ElevatorState.Sealing && car.Upward) ||
                     (car.State == SunkCost.Diving.ElevatorState.Descending && y < car.SeaLevelY - span - 12f));
                if (stillFull && count > 0) elevBubbleStill++;
            }
            else elevBubbleCount = -1;
        }

        private static string Rgb(Color c) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00}/{1:0.00}/{2:0.00}", c.r, c.g, c.b);

        // "elevator: ..." — this peer's dive car now, and its recorder since elev_reset.
        public static string ElevatorLine()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var car = SunkCost.World.WorldSceneFlow.FindCarCached();
            var local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            var time = InstanceFinder.TimeManager;
            double tick = time == null ? 0.0 : time.TicksToTime(time.Tick) + time.GetTickElapsedAsDouble();
            string now = "carState=none";
            if (car != null)
            {
                if (car != elevCar) ElevatorBind(car);
                var sub = local != null ? local.GetComponent<SunkCost.Player.PlayerSubmersion>() : null;
                now = string.Format(ci,
                    "carState={0}; carUp={1}; carY={2:0.000}; carElapsed={3:0.000}; door={4:0.000}; gate={5:0.000}; gateBlocks={6}; " +
                    "carWater={7:0.000}; waterShown={8}; waterY={9:0.000}; flow={10}; pour={11}; visStreams={12}; foam={13:0.00}; bubbles={14}; drain={15:0.00}; bubbleParticles={16}; " +
                    "gauge={17:0.000}; ringEmission={18}; cabinLight={19}; eyeY={20:0.000}; eyeUnder={21}; viewWeight={22:0.00}",
                    car.State, car.Upward, car.transform.position.y, car.StateElapsed,
                    elevDoor == null ? -1f : elevDoor.OpenFraction, elevGate == null ? -1f : elevGate.OpenFraction, elevGateCollider != null && elevGateCollider.enabled,
                    elevWater == null ? -1f : elevWater.LevelMeters, elevWater != null && elevWater.SurfaceShown, elevWater == null ? 0f : elevWater.SurfaceWorldY,
                    elevWater == null ? "none" : elevWater.Flow.ToString(),
                    elevVisuals == null ? -1 : elevVisuals.ActiveStreams, elevVisuals == null ? -1 : elevVisuals.VisibleStreams,
                    elevVisuals == null ? 0f : elevVisuals.Foam01, elevVisuals != null && elevVisuals.Bubbles, elevVisuals == null ? 0f : elevVisuals.Drain01, elevBubbleCount,
                    elevDisplay == null ? -1f : elevDisplay.GaugeFraction, elevRing == null ? "none" : Rgb(elevRing.CurrentEmission), elevCabinLight == null ? "none" : Rgb(elevCabinLight.color),
                    local == null || local.PlayerCamera == null ? 0f : local.PlayerCamera.transform.position.y, sub != null && sub.IsSubmerged, elevViewWeight);
            }
            return string.Format(ci,
                "elevator: tick={0:0.000}; {1}; elevFrames={2}; elevWorstY={3:0.0000}; elevWorstLag={4:0.000}; elevWorstWater={5:0.0000}; elevWorstGauge={6:0.000}; elevWorstGate={7:0.000}; " +
                "elevDoorWrong={8}; elevGateWrong={9}; elevViewWorst={10}; elevSubWorst={11}; elevBubbleStill={12}; elevBubbleAbove={13}; elevFirstFrameErr={14:0.000}\n",
                tick, now, elevFrames, elevWorstY, Mathf.Max(0f, elevWorstLag), elevWorstWater, elevWorstGauge, elevWorstGate,
                elevDoorWrong, elevGateWrong, elevViewWorst, elevSubWorst, elevBubbleStill, elevBubbleAboveFrames, elevFirstFrameErr);
        }

        private static string CabinWaterLevel()
        {
            SunkCost.Diving.ElevatorController car = SunkCost.World.WorldSceneFlow.FindCar();
            SunkCost.Diving.CabinWater water = car != null ? car.GetComponent<SunkCost.Diving.CabinWater>() : null;
            return water == null ? "none" : water.LevelMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        // The visor's readout on this peer, and every loot value it can see (docs/VISOR_IMPLEMENTATION_PLAN.md).
        private static string VisorLine()
        {
            SunkCost.Player.HQPlayerController local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            SunkCost.Player.PlayerHudUI hud = local != null ? local.GetComponent<SunkCost.Player.PlayerHudUI>() : null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string visor = hud == null ? "visor=none" : string.Format(ci, "visor={0}; home={1}/{2:0.0}; brackets={3}; tag={4}; tagValue={5}; crew={6}; spectatingName={7}; noSignal={8}; onAir={9}",
                hud.Visor.On ? "on" : "off", hud.Visor.HomeShown ? "shown" : "hidden", hud.Visor.HomeDistance, hud.Visor.BracketCount, hud.Visor.TargetTag, hud.Visor.TargetValue, hud.Visor.CrewTagCount, hud.Visor.SpectatingName, hud.Visor.NoSignal, hud.Visor.OnAirCount);
            var coins = new System.Collections.Generic.List<string>();
            foreach (SunkCost.Interaction.CarryableItem item in FindObjectsByType<SunkCost.Interaction.CarryableItem>(FindObjectsSortMode.None))
                if (item.HasValue) coins.Add("#" + item.ObjectId.ToString(ci) + ":" + item.Value.ToString(ci));
            coins.Sort(string.CompareOrdinal);
            return visor + "; coins=" + string.Join(",", coins);
        }

        // ---- the shared console (27 September 2026) --------------------------------
        // The composers are reached through IConsoleComposer (ShipNavigationConsole,
        // HQQuotaConsole), so the peer compiles before the rigs exist and reports
        // "none" until the builders place them.

        private static SunkCost.World.IConsoleComposer ShipComposer()
        {
            var day = SunkCost.World.CrewDayState.Instance;
            var ship = SunkCost.World.ShipParts.InWorld(day != null ? day.World : SunkCost.World.WorldId.HQ);
            if (ship != null)
                foreach (SunkCost.World.IConsoleComposer c in ship.GetComponentsInChildren<SunkCost.World.IConsoleComposer>(true))
                    if (c.Kind == SunkCost.World.ConsoleKind.Ship) return c;
            return FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<SunkCost.World.IConsoleComposer>().FirstOrDefault(c => c.Kind == SunkCost.World.ConsoleKind.Ship);
        }

        private static SunkCost.World.IConsoleComposer HQComposer()
        {
            var hq = SunkCost.World.WorldScenes.Scene(SunkCost.World.WorldId.HQ);
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<SunkCost.World.IConsoleComposer>().Where(c => c.Kind == SunkCost.World.ConsoleKind.HQ).ToArray();
            return all.FirstOrDefault(c => c is MonoBehaviour mb && hq.IsValid() && mb.gameObject.scene == hq) ?? all.FirstOrDefault();
        }

        // A console control object by name: in the ship of this player's world, else in the HQ scene.
        private static Transform FindControl(string name)
        {
            var day = SunkCost.World.CrewDayState.Instance;
            var ship = SunkCost.World.ShipParts.InWorld(day != null ? day.World : SunkCost.World.WorldId.HQ);
            if (ship != null)
                foreach (Transform t in ship.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            var hq = SunkCost.World.WorldScenes.Scene(SunkCost.World.WorldId.HQ);
            if (hq.IsValid() && hq.isLoaded)
                foreach (GameObject root in hq.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static string ConsoleLine()
        {
            var day = SunkCost.World.CrewDayState.Instance;
            SunkCost.World.IConsoleComposer ship = ShipComposer(), hq = HQComposer();
            SunkCost.World.LeverPull pull = day == null ? default : day.LastLeverPull;
            return $"selected={(day == null ? "none" : day.SelectedSite.ToString())}; unlocked={(day == null ? "none" : SunkCost.World.Destinations.MaskText(day.UnlockedSites))}; site={(day == null ? "none" : day.CurrentSite.ToString())}; toSite={(day == null ? "none" : day.SiteDestination.ToString())}; " +
                   $"leverPulls={pull.Serial}/{pull.Kind}/{pull.Action}; leverPlayed={(ship == null ? -1 : ship.LeverPlayedSerial)}; leverAngle={(ship == null ? 0f : ship.LeverAngle).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}; " +
                   $"leverSign={(ship == null ? "none" : SunkCost.World.ConsoleModels.Flatten(ship.Sign))}; hqSign={(hq == null ? "none" : SunkCost.World.ConsoleModels.Flatten(hq.Sign))}; " +
                   $"topScreen={(ship == null ? "none" : SunkCost.World.ConsoleModels.Flatten(ship.Top))}; bottomScreen={(ship == null ? "none" : SunkCost.World.ConsoleModels.Flatten(ship.Bottom))}; " +
                   $"hqTop={(hq == null ? "none" : SunkCost.World.ConsoleModels.Flatten(hq.Top))}; hqBottom={(hq == null ? "none" : SunkCost.World.ConsoleModels.Flatten(hq.Bottom))}; " +
                   // The HQ rig's own swing and the GIVE UP card's state on this peer (the console-hq tester, 28 September 2026).
                   $"hqLeverPlayed={(hq == null ? -1 : hq.LeverPlayedSerial)}; hqLeverAngle={(hq == null ? 0f : hq.LeverAngle).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}; " +
                   $"hqCard={(hq == null || hq.Bottom == null || hq.Bottom.VoteCard == null ? "none" : (hq.Bottom.VoteCard.Enabled ? "on" : "off") + "/" + hq.Bottom.VoteCard.Foot)};\n";
        }

        public static string Snapshot()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return "No network manager";
            var day = SunkCost.World.CrewDayState.Instance;
            var voice = FindFirstObjectByType<SunkCost.Audio.ProximityVoice>();
            string loaded = string.Join("+", Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Where(sc => sc.isLoaded && sc.name != "MovedObjectsHolder" && sc.name != "DelayedDestroy").Select(sc => sc.name).OrderBy(n => n)); // FishNet holder scenes excluded
            var session = FindAnyObjectByType<PrototypeSessionController>();
            var monitor = FindAnyObjectByType<SunkCost.World.ShipMonitor>();
            SunkCost.World.IConsoleComposer shipConsole = ShipComposer();
            string monitorText = shipConsole != null && !string.IsNullOrEmpty(shipConsole.Text) ? shipConsole.Text : monitor == null ? string.Empty : monitor.Text; // the console's status first (27 September 2026), the old monitor until it goes
            var seaShip = SunkCost.World.ShipParts.InWorld(SunkCost.World.WorldId.Sea);
            var tv = seaShip != null ? seaShip.GetComponent<SunkCost.World.ShipTV>() : null;
            string tvLine = tv == null ? "tv=none" : $"tv={tv.Channel}; tvLive={tv.Live}; tvCaption={tv.Caption}; tvViewerNear={tv.ViewerNear}; tvRendered={tv.RenderedFrames}";
            // What FishNet holds on this client: object ids with names — the peer's view of who is here.
            var spawnedIds = new System.Collections.Generic.List<string>();
            if (nm.ClientManager != null)
                foreach (var pair in nm.ClientManager.Objects.Spawned.OrderBy(p => p.Key))
                    spawnedIds.Add(pair.Key + ":" + (pair.Value != null ? pair.Value.name.Replace("(Clone)", "") : "null"));
            tvLine += "; spawned=" + string.Join("+", spawnedIds);
            var elevatorSounds = SunkCost.Audio.ElevatorSounds.Instance;
            tvLine += elevatorSounds == null ? "; winch=none" : $"; winchAtCar={elevatorSounds.WinchPlayingAtCar}; winchOnShip={elevatorSounds.WinchPlayingOnShip}; dingsAtCar={elevatorSounds.DingsAtCar}; dingsOnShip={elevatorSounds.DingsOnShip}";
            var ghostLight = SunkCost.Monsters.ElevatorGhostLight.Instance;
            tvLine += ghostLight == null ? "; ghost=none" : $"; ghostGreen={ghostLight.IsGreen}; ghostActive={(day != null && day.Ghost.Active)}; ghostSerial={(day == null ? 0 : day.Ghost.Serial)}; slamsHeard={ghostLight.SlamsHeard}";
            string text = $"server={nm.IsServerStarted}; client={nm.IsClientStarted}; clientId={nm.ClientManager.Connection.ClientId}; loaded={loaded}; active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}; phase={(day == null ? "none" : day.Phase.ToString())}; day={(day == null ? -1 : day.Day)}; payday={(day != null && day.Payday)}; box={(day == null ? -1 : day.BoxValue)}; balance={(day == null ? -1 : day.Balance)}; world={(day == null ? "none" : day.World.ToString())}; fade={(SunkCost.World.ScreenFade.Instance == null ? -1f : SunkCost.World.ScreenFade.Instance.Alpha):0.##}; message={(session == null ? string.Empty : session.Message)}; monitor={monitorText}; trip={(day == null ? "none" : day.Departure.Stage + "/" + day.Departure.Serial)}; ride={(day == null ? "none" : day.CabinRide.Stage + "/" + day.CabinRide.Direction + "/" + day.CabinRide.Serial)}; car={(day == null ? "none" : day.Elevator.State.ToString())}; carPos={(SunkCost.World.WorldSceneFlow.FindCar() == null ? "none" : SunkCost.World.WorldSceneFlow.FindCar().transform.position.ToString())}; below={(day == null ? "" : string.Join("+", day.Below))}; travelLocked={(SunkCost.World.WorldSceneFlow.LocalRider() != null && SunkCost.World.WorldSceneFlow.LocalRider().Locked)}; underwater={Underwater()}; cabinWater={CabinWaterLevel()}; {tvLine}; {CarLine()}; {VisorLine()}\n";
            if (voice != null) text += voice.Diagnostics + "\n";
            text += $"baskets={(day == null ? -1 : day.Baskets)};\n";
            var quotaBoard = FindFirstObjectByType<SunkCost.World.QuotaBoard>();
            SunkCost.World.IConsoleComposer hqConsole = HQComposer();
            string quotaText = hqConsole != null && !string.IsNullOrEmpty(hqConsole.Text) ? hqConsole.Text : quotaBoard == null ? "none" : quotaBoard.Text; // the console's status first (27 September 2026), the old board until it goes
            text += $"giveup={(day == null ? -1 : day.GiveUpVotes)}/{(day == null ? -1 : day.GiveUpCrew)}; quotaBoard={quotaText.Replace("\n", " | ")};\n";
            text += ConsoleLine();
            text += ElevatorDeckLine();
            text += ElevatorLine(); // the elevator-dive tester's "elevator:" line
            foreach (var hoop in FindObjectsByType<SunkCost.Look.HoopScore>(FindObjectsSortMode.None))
            {
                var effect = hoop.GetComponent<SunkCost.Look.BasketCelebration>();
                text += $"hoop={hoop.transform.parent.name}; confetti={effect?.BurstsShown ?? 0}; chimes={effect?.SoundsPlayed ?? 0}; board={hoop.transform.parent.Find("Score")?.GetComponent<TextMesh>()?.text}\n";
            }
            var localShop=SunkCost.World.WorldSceneFlow.LocalPlayer()?.GetComponent<SunkCost.Shop.ShopBrowserUI>();
            if(localShop!=null)text += $"shopBrowser={localShop.IsOpen}; entries={localShop.VisibleItemCount}\n";
            foreach (var gate in FindObjectsByType<SunkCost.World.DockBoardingGate>(FindObjectsSortMode.None))
                text += $"boardingGate={gate.name}; scene={gate.gameObject.scene.name}; closed={gate.Closed}\n";
            foreach (var gangway in FindObjectsByType<SunkCost.World.DockGangway>(FindObjectsSortMode.None))
                text += $"gangway={gangway.name}; raised={gangway.RaisedFraction.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}\n";
            foreach (var player in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).OrderBy(p => p.OwnerId))
            {
                var pc = player.GetComponent<SunkCost.Player.HQPlayerController>();
                var hands = player.GetComponent<SunkCost.Player.PlayerHands>();
                SunkCost.Player.PlayerIdentity identity = player.GetComponent<SunkCost.Player.PlayerIdentity>();
                text += $"player={player.OwnerId}; name={(identity == null ? "?" : identity.DisplayName)}; colour={(identity == null ? "?" : identity.ColourIndex.ToString())}; local={player.IsOwner}; scene={player.gameObject.scene.name}; position={player.transform.position}; slots={player.Slots}; held={(player.HeldItem == null ? "none" : player.HeldItem.name)}; massKg={player.CarriedMassKg:0.###}; speedFactor={player.SpeedFactor:0.####}; meterFill={player.MeterFill:0.####}; crouched={(pc != null && pc.IsCrouched)}; dead={(pc != null && pc.IsDead)}; air={(pc == null || pc.Vitals == null ? -1f : pc.Vitals.AirFraction):0.###}; health={(pc == null || pc.Vitals == null ? -1 : pc.Vitals.Health)}; upgrades={(pc == null || pc.Upgrades == null ? "none" : pc.Upgrades.Owned.ToString())}; shopRefusal='{(pc == null || pc.Upgrades == null ? string.Empty : pc.Upgrades.Refusal)}'; ownerValid={player.Owner.IsValid}; isController={player.IsController}; controllerOn={(pc != null && pc.Controller != null && pc.Controller.enabled)}; spectating={(day == null ? -1 : day.SpectateTargetOf(player.OwnerId))}; watchers={(day == null ? 0 : day.WatchersOf(player.OwnerId))}; spectatorActive={(pc != null && pc.Spectator != null && pc.Spectator.Active)}; spectatorTarget={(pc == null || pc.Spectator == null || pc.Spectator.Target == null ? -1 : pc.Spectator.Target.OwnerId)}; camPos={(pc == null || pc.PlayerCamera == null ? Vector3.zero : pc.PlayerCamera.transform.position)}; height={(pc != null ? pc.Controller.height : 0f):0.##}; eye={(pc != null ? pc.EyeHeight : 0f):0.##}; hands={(hands == null || hands.HeldForHands == null ? "rest" : hands.HeldForHands.name)}; lamp={(pc != null && pc.LampOn)}; dashes={(pc == null ? 0 : pc.Dashes)}; dashReady={(pc == null ? 1f : pc.DashReady):0.##}; dashSerial={(pc == null ? 0 : pc.LastDashCue.Serial)}; dashRings={(pc == null || pc.GetComponent<SunkCost.Player.PlayerDashEffects>() == null ? 0 : pc.GetComponent<SunkCost.Player.PlayerDashEffects>().RingsShown)}; leak={(pc != null && pc.Vitals != null && pc.Vitals.Leaking)}; patchNotice='{(pc == null || pc.Vitals == null ? string.Empty : pc.Vitals.PatchNotice)}'; target={(pc == null || pc.CurrentTarget == null ? "none" : pc.CurrentTarget.name)}; obstructed={(pc != null && pc.ViewObstructed)}; camPitch={(pc == null || pc.PlayerCamera == null ? 0f : pc.PlayerCamera.transform.localEulerAngles.x):0.#}; camFwd={(pc == null || pc.PlayerCamera == null ? Vector3.zero : pc.PlayerCamera.transform.forward)}; knockback={(pc == null ? 0 : pc.LastKnockback.Serial)}; grabbed={(pc != null && pc.IsGrabbed)}; grabHolder={(pc == null || !pc.IsGrabbed ? -1 : pc.Grab.HolderId)}; grabT={(pc == null ? 0f : pc.GrabSeconds):0.00}; grabsFelt={(pc == null ? 0 : pc.GrabsFelt)}\n";
            }
            foreach (var creature in SunkCost.Monsters.Creature.All.Where(c => c != null).OrderBy(c => c.ObjectId))
            {
                var impostorLook = creature.GetComponent<SunkCost.Monsters.ImpostorLook>();
                var impostor = creature.GetComponent<SunkCost.Monsters.Impostor>();
                var beams = creature.GetComponent<SunkCost.Monsters.CreatureBolts>();
                var beamView = beams == null ? null : creature.GetComponentInChildren<SunkCost.Monsters.MonsterBeamView>(true);
                var beamLight = beams == null ? null : creature.GetComponentInChildren<SunkCost.Monsters.MonsterBeamLight>(true);
                text += $"monster={creature.Kind}; id={creature.ObjectId}; pose={creature.Pose}; target={creature.TargetId}; position={creature.transform.position}; shown={(impostorLook == null ? true : impostorLook.ShownToLocal)}; wears={(impostor == null ? string.Empty : impostor.WornName + "/" + impostor.WornColourIndex)}; beam={(beams == null ? "none" : beams.Cue.Serial + "/" + beams.Cue.Phase.ToString().ToLowerInvariant())}; beamPhase={(beamView == null ? "none" : beamView.Phase.ToString())}; beamFrom={(beamView == null ? Vector3.zero : beamView.ShownFrom).ToString("F3")}; beamTo={(beamView == null ? Vector3.zero : beamView.ShownTo).ToString("F3")}; beamHalf={(beamView == null ? 0f : beamView.ShownHalfWidth):0.###}; beamDark={(beamView != null && beamView.Dark)}; hitSerial={(beams == null ? 0 : beams.HitSerial)}; shade={(beamView == null || beamView.Shade == null ? 0f : beamView.Shade.Strength):0.##}; beamLights={(beamLight == null ? 0 : beamLight.LitCount)}\n";
            }
            foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).OrderBy(i => i.name))
            {
                var body = item.GetComponent<Rigidbody>();
                text += $"item={item.name}; display={item.DisplayName}; use={item.UseAction}; id={item.ObjectId}; scene={item.gameObject.scene.name}; state={item.State}; holder={item.HolderClientId}; owner={item.OwnerId}; version={item.MotionVersion}; writer={item.WriterOverride}; kinematic={body.isKinematic}; collider={item.PrimaryCollider.enabled}; visible={item.GetComponentInChildren<Renderer>(true).enabled}; massKg={item.MassKg:0.##}; grip={item.Grip}; launch={item.LastLaunchSpeed:0.###}; position={item.transform.position}; velocity={body.linearVelocity}\n";
            }
            return text;
        }
    }
}
#endif
