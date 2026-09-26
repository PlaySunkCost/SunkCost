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
            string text = $"server={nm.IsServerStarted}; client={nm.IsClientStarted}; clientId={nm.ClientManager.Connection.ClientId}; loaded={loaded}; active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}; phase={(day == null ? "none" : day.Phase.ToString())}; day={(day == null ? -1 : day.Day)}; payday={(day != null && day.Payday)}; box={(day == null ? -1 : day.BoxValue)}; balance={(day == null ? -1 : day.Balance)}; world={(day == null ? "none" : day.World.ToString())}; fade={(SunkCost.World.ScreenFade.Instance == null ? -1f : SunkCost.World.ScreenFade.Instance.Alpha):0.##}; message={(session == null ? string.Empty : session.Message)}; monitor={(monitor == null ? string.Empty : monitor.Text)}; trip={(day == null ? "none" : day.Departure.Stage + "/" + day.Departure.Serial)}; ride={(day == null ? "none" : day.CabinRide.Stage + "/" + day.CabinRide.Direction + "/" + day.CabinRide.Serial)}; car={(day == null ? "none" : day.Elevator.State.ToString())}; carPos={(SunkCost.World.WorldSceneFlow.FindCar() == null ? "none" : SunkCost.World.WorldSceneFlow.FindCar().transform.position.ToString())}; below={(day == null ? "" : string.Join("+", day.Below))}; travelLocked={(SunkCost.World.WorldSceneFlow.LocalRider() != null && SunkCost.World.WorldSceneFlow.LocalRider().Locked)}; underwater={Underwater()}; cabinWater={CabinWaterLevel()}; {tvLine}; {CarLine()}; {VisorLine()}\n";
            if (voice != null) text += voice.Diagnostics + "\n";
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
