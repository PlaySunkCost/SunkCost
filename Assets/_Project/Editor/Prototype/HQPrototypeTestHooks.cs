using System.Linq;
using System.Reflection;
using FishNet.Connection;
using FishNet.Managing;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using UnityEngine;
using UnityEditor;

namespace SunkCost.Editor.Prototype
{
    // Verification-only helpers for exercising server-authoritative paths from MCP/editor
    // commands without driving real input in a second standalone process. Section 13/16 of
    // docs/HQ_BASKETBALL_IMPLEMENTATION_PLAN.md and section 13 of
    // docs/HOLD_INVENTORY_IMPLEMENTATION_PLAN.md still require the real runtime checks;
    // these hooks make that possible for a single connected editor plus joined builds.
    public static class HQPrototypeTestHooks
    {
        // The scene has several identical balls; "Basketball" is the original one.
        public static CarryableItem Item(string name = "Basketball")
        {
            return Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == name);
        }

        // Grants the ball to the first connected client that is not the local host
        // connection, so its disconnect-handoff behavior can be exercised without
        // remote-controlling the standalone build's input.
        public static string ServerGrabForNonHostClient(string itemName = "Basketball")
        {
            NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
            CarryableItem item = Item(itemName);
            HQPlayerController[] players = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None);
            if (nm == null || item == null) return "Missing NetworkManager or item in the loaded scene.";
            if (!nm.IsServerStarted) return "Server is not started; run this from the host.";

            int hostClientId = nm.ClientManager.Connection.ClientId;
            NetworkConnection joiner = nm.ServerManager.Clients.Values.FirstOrDefault(c => c.ClientId != hostClientId);
            if (joiner == null) return "No non-host client connection found.";

            HQPlayerController joinerPlayer = players.FirstOrDefault(p => p.Owner.ClientId == joiner.ClientId);
            if (joinerPlayer == null) return $"No player controller owned by connection {joiner.ClientId}.";

            bool grabbed = item.ServerGrab(joiner, joinerPlayer);
            return $"grabbed={grabbed}; holderClientId={item.HolderClientId}; state={item.State}";
        }

        public static string ConnectionDiagnostics()
        {
            NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
            HQPlayerController[] players = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None);
            if (nm == null) return "No NetworkManager found.";
            string clients = string.Join(",", nm.ServerManager.Clients.Keys);
            string owners = string.Join(",", players.Select(p => p.Owner.ClientId));
            return $"localClientId={nm.ClientManager.Connection.ClientId}; serverClientsKeys=[{clients}]; playerOwnerIds=[{owners}]";
        }

        // Moves the host's own player next to the item and issues a normal server grab
        // on its behalf, to confirm the item is actually recoverable (not just
        // "state == Free") after its previous holder disconnected.
        public static string RecoverBallForHost(string itemName = "Basketball")
        {
            NetworkManager nm = Object.FindFirstObjectByType<NetworkManager>();
            CarryableItem item = Item(itemName);
            HQPlayerController[] players = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None);
            if (nm == null || item == null) return "Missing NetworkManager or item in the loaded scene.";

            int hostClientId = nm.ClientManager.Connection.ClientId;
            HQPlayerController host = players.FirstOrDefault(p => p.Owner.ClientId == hostClientId);
            if (host == null) return $"No player controller owned by host connection {hostClientId}.";

            host.transform.position = item.transform.position + new Vector3(0.5f, 0f, 0f);
            bool grabbed = item.ServerGrab(host.Owner, host);
            return $"grabbed={grabbed}; state={item.State}; holderClientId={item.HolderClientId}";
        }

        // --- Local-player hooks. Run these with the EDITOR JOINED AS A CLIENT to a
        // separately running host build, so they exercise the real client request path
        // (ServerRpc over the transport), not in-process host shortcuts. They also work
        // on the host, where the request runs in-process.

        public static string ClientMoveLocalPlayerTo(Vector3 target)
        {
            HQPlayerController local = LocalPlayer();
            if (local == null) return "No local player.";
            CharacterController cc = local.GetComponent<CharacterController>();
            // CharacterController overrides direct transform writes unless disabled.
            cc.enabled = false;
            local.transform.position = target;
            cc.enabled = true;
            return $"moved local player to {target}";
        }

        public static string ClientMoveLocalPlayerToItem(string itemName = "Basketball", float offset = 0.6f)
        {
            CarryableItem item = Item(itemName);
            if (item == null) return "Missing item.";
            return ClientMoveLocalPlayerTo(new Vector3(item.transform.position.x + offset, 0f, item.transform.position.z));
        }

        // Turns the local player to face the item so the crosshair raycast hits it.
        public static string ClientLookAtItem(string itemName = "Basketball")
        {
            CarryableItem item = Item(itemName);
            HQPlayerController local = LocalPlayer();
            if (item == null || local == null) return "Missing item or local player.";
            Vector3 toItem = item.transform.position - local.EyePosition;
            Vector3 flat = new(toItem.x, 0f, toItem.z);
            if (flat.sqrMagnitude > 0.0001f) local.transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            Camera camera = local.GetComponentInChildren<Camera>(true);
            float pitch = -Mathf.Atan2(toItem.y, flat.magnitude) * Mathf.Rad2Deg;
            camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            FieldInfo pitchField = typeof(HQPlayerController).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic);
            pitchField?.SetValue(local, pitch);
            return $"looking at {itemName}: distance={toItem.magnitude:0.00}";
        }

        public static string ClientRequestGrab(string itemName = "Basketball")
        {
            CarryableItem item = Item(itemName);
            PlayerInventory inventory = LocalInventory();
            if (item == null || inventory == null) return "Missing item or local inventory.";
            inventory.RequestGrab(item);
            return "grab requested";
        }

        // Bypasses the client pre-checks so the server's own refusal path is exercised.
        public static string ClientRequestGrabRaw(string itemName = "Basketball")
        {
            CarryableItem item = Item(itemName);
            PlayerInventory inventory = LocalInventory();
            if (item == null || inventory == null) return "Missing item or local inventory.";
            MethodInfo rpc = typeof(PlayerInventory).GetMethod("ServerRequestGrab", BindingFlags.Instance | BindingFlags.NonPublic);
            if (rpc == null) return "ServerRequestGrab not found.";
            rpc.Invoke(inventory, new object[] { item.NetworkObject, null });
            return "raw grab requested";
        }

        public static string ClientRequestEquip(int slot)
        {
            PlayerInventory inventory = LocalInventory();
            if (inventory == null) return "No local inventory.";
            inventory.RequestEquip(slot);
            return $"equip {slot} requested";
        }

        public static string ClientRequestDrop()
        {
            PlayerInventory inventory = LocalInventory();
            if (inventory == null) return "No local inventory.";
            inventory.RequestDrop();
            return "drop requested";
        }

        public static string ClientRequestUse()
        {
            PlayerInventory inventory = LocalInventory();
            HQPlayerController local = LocalPlayer();
            if (inventory == null || local == null) return "No local inventory.";
            inventory.RequestUse(local.GetComponentInChildren<Camera>(true).transform.forward);
            return "use requested";
        }

        // What the local player's HUD and input code act on.
        public static string InventoryText()
        {
            PlayerInventory inventory = LocalInventory();
            if (inventory == null) return "No local inventory.";
            string held = inventory.HeldItem == null ? "none" : inventory.HeldItem.name;
            return $"slots={inventory.Slots}; held={held}; heldSlot={inventory.HeldSlot}; overflow={inventory.HoldingOverflow}; refusal='{inventory.Refusal}'; localClientId={inventory.Owner.ClientId}";
        }

        public static string PromptText()
        {
            HQPlayerController local = LocalPlayer();
            PlayerHudUI hud = local != null ? local.GetComponent<PlayerHudUI>() : null;
            if (hud == null) return "No local HUD.";
            local.RefreshTarget();
            string target = local.CurrentTarget == null ? "none" : local.CurrentTarget.name;
            return $"prompt='{hud.PromptText}'; target={target}";
        }

        public static string ItemStateText(string itemName = "Basketball")
        {
            CarryableItem item = Item(itemName);
            if (item == null) return $"No item named {itemName} (despawned or scene not loaded).";
            Rigidbody body = item.GetComponent<Rigidbody>();
            Collider collider = item.PrimaryCollider;
            Renderer renderer = item.GetComponentInChildren<Renderer>(true);
            return $"{item.name}: spawned={item.IsSpawned}; state={item.State}; holder={item.HolderClientId}; ownerId={item.OwnerId}; isOwner={item.IsOwner}; massKg={item.MassKg:0.##}; grip={item.Grip}; " +
                   $"kinematic={body.isKinematic}; collider={(collider != null && collider.enabled)}; visible={(renderer != null && renderer.enabled)}; " +
                   $"writerHere={item.WriterOverride}; position={item.transform.position}";
        }

        // Distance between the held item and the local hold point, for the rigid-hold check.
        // Distance between the held item and the hold pose for its grip (right hand
        // or two-handed centre), plus which point that is.
        public static string HoldOffset()
        {
            PlayerInventory inventory = LocalInventory();
            HQPlayerController local = LocalPlayer();
            if (inventory == null || local == null || inventory.HeldItem == null) return "Nothing held.";
            CarryableItem item = inventory.HeldItem;
            if (!item.TryGetHoldPose(local, out Vector3 pose, out _)) return "No hold pose.";
            Transform point = local.HoldPointFor(item.Grip);
            return $"offset={Vector3.Distance(item.transform.position, pose):0.000}; grip={item.Grip}; point={(point == null ? "none" : point.name)}";
        }

        // Server only: spawn extra basketballs for four-slot rows (see
        // InventoryVerificationPeer.SpawnLightItems).
        public static string ServerSpawnLightItems(int count) => InventoryVerificationPeer.SpawnLightItems(count);

        // Runtime-spawned basketballs arrive on clients as "Basketball(Clone)"; give
        // them the manifest-style names the hooks use, in object id order.
        public static string NameTestClones()
        {
            var clones = Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None)
                .Where(i => i.name.StartsWith("Basketball(Clone)")).OrderBy(i => i.ObjectId).ToList();
            int existing = Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).Count(i => i.name.StartsWith("Basketball (") || i.name == "Basketball");
            for (int i = 0; i < clones.Count; i++) clones[i].name = "Basketball (" + (existing + i + 1) + ")";
            return "named " + clones.Count + " clones";
        }

        // Server-owned carried mass and the factors every peer derives from it.
        public static string CarriedMassText()
        {
            PlayerInventory inventory = LocalInventory();
            if (inventory == null) return "No local inventory.";
            return $"carriedMassKg={inventory.CarriedMassKg:0.###}; meterFill={inventory.MeterFill:0.####}; speedFactor={inventory.SpeedFactor:0.####}; overloaded={inventory.Overloaded}; capacityKg={inventory.Weight.CapacityKg}; settings={inventory.Weight.name}";
        }

        // Launch speed the writer applied on its last throw of this item.
        public static string LaunchSpeedText(string itemName)
        {
            CarryableItem item = Item(itemName);
            if (item == null) return "No item named " + itemName;
            Rigidbody body = item.GetComponent<Rigidbody>();
            return $"{item.name}: massKg={item.MassKg:0.##}; throwFactor={item.Weight.ThrowFactor(item.MassKg):0.####}; lastLaunchSpeed={item.LastLaunchSpeed:0.###}; velocity={body.linearVelocity.magnitude:0.###}";
        }

        public static string AllItems()
        {
            return string.Join("\n", Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).OrderBy(i => i.name).Select(i => ItemStateText(i.name)));
        }

        public static string SessionUiState()
        {
            PrototypeSessionUI ui = Object.FindFirstObjectByType<PrototypeSessionUI>();
            if (ui == null || ui.Controller == null) return "No PrototypeSessionUI/controller.";
            Camera preview = (Camera)typeof(PrototypeSessionUI).GetField("previewCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
            return ui.Controller.Snapshot().ToText() + "; previewCamera=" + (preview == null ? "none" : preview.enabled.ToString()) + "; " + ui.RuntimeDiagnostics;
        }

        // --- Network debug overlay (docs/DEBUG_OVERLAY_IMPLEMENTATION_PLAN.md §4.5) ---

        public static string DebugSnapshotText()
        {
            NetworkDebugOverlay overlay = Object.FindFirstObjectByType<NetworkDebugOverlay>();
            if (overlay == null) return "No NetworkDebugOverlay in the scene (bootstrap did not run?).";
            overlay.Refresh();
            return overlay.Current.ToText();
        }

        public static string SetOverlayVisible(bool visible)
        {
            NetworkDebugOverlay overlay = Object.FindFirstObjectByType<NetworkDebugOverlay>();
            if (overlay == null) return "No NetworkDebugOverlay.";
            overlay.Visible = visible;
            return $"Visible={overlay.Visible}";
        }

        public static string DumpOverlaySnapshot()
        {
            NetworkDebugOverlay overlay = Object.FindFirstObjectByType<NetworkDebugOverlay>();
            if (overlay == null) return "No NetworkDebugOverlay.";
            overlay.DumpSnapshot();
            return "dumped to console and clipboard";
        }

        // ---- world loop (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 9.2) -------

        // Hand-testing without the MCP bridge: host in Play Mode, everyone on the
        // deck, then one of these. The monitor card replaces them.
        [MenuItem("Sunk Cost/Prototype/Debug/Sail to Sea (host, Play Mode)")]
        public static void MenuSailToSea() => Debug.Log("Sail: " + (Application.isPlaying ? ServerSail("Sea") : "enter Play Mode and host first."));

        [MenuItem("Sunk Cost/Prototype/Debug/Sail to HQ (host, Play Mode)")]
        public static void MenuSailToHQ() => Debug.Log("Sail: " + (Application.isPlaying ? ServerSail("HQ") : "enter Play Mode and host first."));

        // ---- movement and hands (docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md) ----

        public static string ClientSetPitch(float degrees)
        {
            HQPlayerController local = LocalPlayer();
            if (local == null) return "No local player.";
            local.SetPitchForChecks(degrees);
            return "pitch " + degrees;
        }

        public static string ClientCrouch(bool crouch)
        {
            HQPlayerController local = LocalPlayer();
            var stance = local != null ? local.GetComponent<PlayerStance>() : null;
            if (stance == null) return "No local stance.";
            stance.SetDesiredCrouch(crouch);
            return crouch ? "crouch requested" : "stand requested";
        }

        public static string StanceText()
        {
            HQPlayerController local = LocalPlayer();
            if (local == null) return "No local player.";
            return $"crouched={local.IsCrouched}; height={local.Controller.height:0.##}; centre={local.Controller.center.y:0.##}; eye={local.EyeHeight:0.##}; grounded={local.IsGrounded}; vy={local.VerticalSpeed:0.##}";
        }

        // Game-view evidence: the local player's camera, or a free camera looking at a point.
        public static string CaptureLocalCamera(string path) => Capture(LocalPlayer()?.PlayerCamera, path);

        public static string CaptureFrom(Vector3 position, Vector3 lookAt, string path)
        {
            GameObject go = new("CheckCaptureCamera", typeof(Camera));
            try
            {
                go.transform.position = position;
                go.transform.LookAt(lookAt);
                Camera camera = go.GetComponent<Camera>();
                camera.fieldOfView = 60f;
                return Capture(camera, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static string Capture(Camera camera, string path)
        {
            if (camera == null) return "No camera.";
            var rt = new RenderTexture(1280, 720, 24);
            RenderTexture previousTarget = camera.targetTexture;
            bool wasEnabled = camera.enabled;
            camera.enabled = true;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previousTarget;
            camera.enabled = wasEnabled;
            RenderTexture.active = rt;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            RenderTexture.active = null;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            rt.Release();
            return "saved " + path;
        }

        // The monitor as the local player presses it (owner request through ShipControls).
        public static string ClientRequestSail(string world)
        {
            if (!System.Enum.TryParse(world, true, out SunkCost.World.WorldId target)) return "Unknown world " + world;
            SunkCost.Player.HQPlayerController player = SunkCost.World.WorldSceneFlow.LocalPlayer();
            SunkCost.World.ShipControls controls = player != null ? player.GetComponent<SunkCost.World.ShipControls>() : null;
            if (controls == null) return "No local ShipControls";
            controls.RequestSail(target);
            return "requested " + target;
        }

        // ---- the cabin ride (docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md sections 5.3, 5.4) ----

        public static string ClientRequestCabin()
        {
            SunkCost.Player.HQPlayerController player = SunkCost.World.WorldSceneFlow.LocalPlayer();
            SunkCost.World.ShipControls controls = player != null ? player.GetComponent<SunkCost.World.ShipControls>() : null;
            if (controls == null) return "No local ShipControls";
            controls.RequestCabin();
            return "requested cabin";
        }

        public static string ClientRequestCar()
        {
            SunkCost.Player.HQPlayerController player = SunkCost.World.WorldSceneFlow.LocalPlayer();
            SunkCost.World.ShipControls controls = player != null ? player.GetComponent<SunkCost.World.ShipControls>() : null;
            if (controls == null) return "No local ShipControls";
            controls.RequestCar();
            return "requested car";
        }

        // Puts the local player in the middle of the deck cabin of the ship in the
        // given world, facing its button.
        public static string MoveLocalIntoDeckCabin(string world)
        {
            if (!System.Enum.TryParse(world, true, out SunkCost.World.WorldId target)) return "Unknown world " + world;
            SunkCost.World.ShipParts ship = SunkCost.World.ShipParts.InWorld(target);
            SunkCost.Player.HQPlayerController local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            if (ship == null || ship.DeckCabin == null || local == null) return "No ship/cabin/local player";
            Vector3 spot = ship.DeckCabin.position + Vector3.up * 0.05f;
            local.TeleportLocal(spot, ship.DeckCabin.eulerAngles.y);
            return "moved into the deck cabin at " + spot;
        }

        public static string MoveLocalIntoCar()
        {
            SunkCost.Diving.ElevatorController car = SunkCost.World.WorldSceneFlow.FindCar();
            SunkCost.Player.HQPlayerController local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            if (car == null || local == null) return "No car/local player";
            Vector3 spot = car.transform.position + Vector3.up * 0.05f;
            local.TeleportLocal(spot, car.transform.eulerAngles.y);
            return "moved into the car at " + spot;
        }

        public static string RideStatus()
        {
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            if (day == null) return "no day state";
            SunkCost.World.ShipDepartureRider rider = SunkCost.World.WorldSceneFlow.LocalRider();
            SunkCost.Player.HQPlayerController local = SunkCost.World.WorldSceneFlow.LocalPlayer();
            SunkCost.Diving.ElevatorController car = SunkCost.World.WorldSceneFlow.FindCar();
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            return $"ride={day.CabinRide.Stage}/{day.CabinRide.Direction}/{day.CabinRide.Serial} car={day.Elevator.State} up={day.Elevator.Upward} carPos={(car == null ? "none" : car.transform.position.ToString("F2"))} carDriven={(car != null && car.Driven)} " +
                   $"riders=[{string.Join(",", day.Riders)}] below=[{string.Join(",", day.Below)}] placements={day.Placements.Count} " +
                   $"local=({(local == null ? "none" : local.gameObject.scene.name + " " + local.transform.position.ToString("F2"))}) locked={(rider != null && rider.Locked)} placed={(rider != null && rider.Placed)} " +
                   $"deckDoors={(flow == null ? -1f : flow.DeckCabinOpenFraction()):0.00} fade={(SunkCost.World.ScreenFade.Instance == null ? -1f : SunkCost.World.ScreenFade.Instance.Alpha):0.00} refusal='{day.LastRefusal.Text}' failure='{(flow == null ? "" : flow.LastFailure)}'";
        }

        public static string MonitorText()
        {
            SunkCost.World.ShipMonitor monitor = Object.FindAnyObjectByType<SunkCost.World.ShipMonitor>();
            return monitor == null ? "(no monitor)" : monitor.Text;
        }

        // The server-side sail, for the matrix's setup steps.
        public static string ServerSail(string world)
        {
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            if (flow == null) return "No WorldSceneFlow.";
            if (!System.Enum.TryParse(world, true, out SunkCost.World.WorldId target)) return "Unknown world " + world;
            return flow.ServerSail(target, out string why) ? "sailing to " + target : "refused: " + why;
        }

        // The load data the flow builds, for the dynamic-command tooling that cannot
        // reference FishNet types itself.
        public static string LoadDataText(string world)
        {
            if (!System.Enum.TryParse(world, true, out SunkCost.World.WorldId target)) return "Unknown world " + world;
            FishNet.Managing.Scened.SceneLoadData load = SunkCost.World.WorldSceneFlow.LoadDataFor(target, null);
            FishNet.Managing.Scened.PreferredScene preferred = load.PreferredActiveScene;
            return $"scenes={string.Join(",", load.SceneLookupDatas.Select(l => l.Name))} replace={load.ReplaceScenes} autoUnload={load.Options.AutomaticallyUnload} " +
                   $"client={(preferred.Client == null ? "null" : preferred.Client.Name)} server={(preferred.Server == null ? "null" : "'" + preferred.Server.Name + "' handle=" + preferred.Server.RawHandle)} moved={load.MovedNetworkObjects.Length}";
        }

        // Diagnostic: a server-only load of a world, no connections, no moved objects.
        public static string ServerPreload(string world)
        {
            if (!System.Enum.TryParse(world, true, out SunkCost.World.WorldId target)) return "Unknown world " + world;
            SunkCost.World.WorldSceneFlow.Instance?.EnsureHolderKeepAlive();
            FishNet.InstanceFinder.SceneManager.LoadConnectionScenes(SunkCost.World.WorldSceneFlow.LoadDataFor(target, null));
            return "preload queued for " + target;
        }

        public static string FlowStatus()
        {
            SunkCost.World.WorldSceneFlow flow = SunkCost.World.WorldSceneFlow.Instance;
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            string loaded = string.Join("+", Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Where(sc => sc.isLoaded && sc.name != "MovedObjectsHolder" && sc.name != "DelayedDestroy").Select(sc => sc.name).OrderBy(n => n)); // FishNet holder scenes excluded
            SunkCost.World.ShipDepartureState trip = day == null ? default : day.Departure;
            SunkCost.World.ShipDepartureRider rider = SunkCost.World.WorldSceneFlow.LocalRider();
            return $"flow={(flow == null ? "none" : $"world={flow.CurrentWorld} transitioning={flow.Transitioning} pending={flow.PendingAcks}")}; " +
                   $"day={(day == null ? "none" : day.DebugStatus)}; trip=serial={trip.Serial} stage={trip.Stage} from={trip.FromWorld} to={trip.ToWorld}; " +
                   $"rider={(rider == null ? "none" : $"locked={rider.Locked} placed={rider.Placed} local={rider.ShipLocal}")}; " +
                   $"loaded={loaded}; active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}; " +
                   $"fade={(SunkCost.World.ScreenFade.Instance == null ? -1f : SunkCost.World.ScreenFade.Instance.Alpha):0.##}";
        }

        // The departure presentation of the ship in a world, for the checks.
        public static string ShipStatus(string world)
        {
            if (!System.Enum.TryParse(world, true, out SunkCost.World.WorldId target)) return "Unknown world " + world;
            SunkCost.World.ShipParts ship = SunkCost.World.ShipParts.InWorld(target);
            SunkCost.World.ShipDepartureVisual visual = ship != null ? ship.GetComponent<SunkCost.World.ShipDepartureVisual>() : null;
            if (visual == null) return "no ship";
            return $"rest={visual.RestPosition}; pos={ship.transform.position}; gangway={visual.GangwayAngle:0.#}; lowered={visual.GangwayLowered}; rampCollider={(ship.GangwayCollider != null && ship.GangwayCollider.enabled)}; engineClip={visual.HasEngineClip}";
        }

        // Every player and carryable with the Unity scene it sits in on this peer.
        public static string ScenesText()
        {
            var lines = Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).OrderBy(p => p.OwnerId)
                .Select(p => $"player {p.OwnerId} local={p.IsOwner} scene={p.gameObject.scene.name} pos={p.transform.position}")
                .Concat(Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).OrderBy(i => i.name)
                    .Select(i => $"item {i.name} scene={i.gameObject.scene.name} state={i.State} holder={i.HolderClientId} pos={i.transform.position}"));
            return string.Join("\n", lines);
        }

        private static HQPlayerController LocalPlayer()
        {
            return Object.FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
        }

        private static PlayerInventory LocalInventory()
        {
            return Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
        }
    }
}
