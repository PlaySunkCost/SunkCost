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

        private string Execute(Command command)
        {
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
            SessionInputGate.OpenMenu();
            switch (command.action)
            {
                case "move":
                    player.TeleportLocal(command.position, player.Yaw);
                    break;
                case "look":
                    player.transform.rotation = Quaternion.LookRotation(new Vector3(command.aim.x, 0, command.aim.z));
                    player.GetComponentInChildren<Camera>(true).transform.rotation = Quaternion.LookRotation(command.aim);
                    break;
                case "grab": player.Inventory.RequestGrab(item); break;
                case "equip": player.Inventory.RequestEquip(command.slot); break;
                case "drop": player.Inventory.RequestDrop(); break;
                case "throw": player.Inventory.RequestUse(command.aim); break;
                case "leave": FindFirstObjectByType<PrototypeSessionUI>().LeaveSession(); break;
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
                // Stance intent as the player's own Ctrl would give it (slot: 1 = crouch, 0 = stand).
                case "crouch":
                    var stance = player.GetComponent<SunkCost.Player.PlayerStance>();
                    if (stance == null) return "No PlayerStance";
                    stance.SetDesiredCrouch(command.slot != 0);
                    break;
                case "snapshot": break;
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

        public static string Snapshot()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return "No network manager";
            var day = SunkCost.World.CrewDayState.Instance;
            string loaded = string.Join("+", Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Where(sc => sc.isLoaded && sc.name != "MovedObjectsHolder" && sc.name != "DelayedDestroy").Select(sc => sc.name).OrderBy(n => n)); // FishNet holder scenes excluded
            var session = FindAnyObjectByType<PrototypeSessionController>();
            var monitor = FindAnyObjectByType<SunkCost.World.ShipMonitor>();
            string text = $"server={nm.IsServerStarted}; client={nm.IsClientStarted}; clientId={nm.ClientManager.Connection.ClientId}; loaded={loaded}; active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}; phase={(day == null ? "none" : day.Phase.ToString())}; world={(day == null ? "none" : day.World.ToString())}; fade={(SunkCost.World.ScreenFade.Instance == null ? -1f : SunkCost.World.ScreenFade.Instance.Alpha):0.##}; message={(session == null ? string.Empty : session.Message)}; monitor={(monitor == null ? string.Empty : monitor.Text)}; trip={(day == null ? "none" : day.Departure.Stage + "/" + day.Departure.Serial)}; travelLocked={(SunkCost.World.WorldSceneFlow.LocalRider() != null && SunkCost.World.WorldSceneFlow.LocalRider().Locked)}\n";
            foreach (var player in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).OrderBy(p => p.OwnerId))
            {
                var pc = player.GetComponent<SunkCost.Player.HQPlayerController>();
                var hands = player.GetComponent<SunkCost.Player.PlayerHands>();
                text += $"player={player.OwnerId}; local={player.IsOwner}; scene={player.gameObject.scene.name}; position={player.transform.position}; slots={player.Slots}; held={(player.HeldItem == null ? "none" : player.HeldItem.name)}; massKg={player.CarriedMassKg:0.###}; speedFactor={player.SpeedFactor:0.####}; meterFill={player.MeterFill:0.####}; crouched={(pc != null && pc.IsCrouched)}; height={(pc != null ? pc.Controller.height : 0f):0.##}; eye={(pc != null ? pc.EyeHeight : 0f):0.##}; hands={(hands == null || hands.HeldForHands == null ? "rest" : hands.HeldForHands.name)}\n";
            }
            foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).OrderBy(i => i.name))
            {
                var body = item.GetComponent<Rigidbody>();
                text += $"item={item.name}; id={item.ObjectId}; scene={item.gameObject.scene.name}; state={item.State}; holder={item.HolderClientId}; owner={item.OwnerId}; version={item.MotionVersion}; writer={item.WriterOverride}; kinematic={body.isKinematic}; collider={item.PrimaryCollider.enabled}; visible={item.GetComponentInChildren<Renderer>(true).enabled}; massKg={item.MassKg:0.##}; grip={item.Grip}; launch={item.LastLaunchSpeed:0.###}; position={item.transform.position}; velocity={body.linearVelocity}\n";
            }
            return text;
        }
    }
}
#endif
