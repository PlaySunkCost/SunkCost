#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using FishNet;
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
            if (nm == null || !(nm.TransportManager.Transport is Tugboat) || !nm.ClientManager.Started) return;
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
            var item = FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == command.item);
            if (player == null) return "No owned player";
            // Keep uncontrolled physical keyboard/mouse input out of hook tests.
            SessionInputGate.OpenMenu();
            switch (command.action)
            {
                case "move":
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    player.transform.position = command.position;
                    cc.enabled = true;
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
                case "snapshot": break;
                default: return "Unknown action";
            }
            return "requested " + command.action;
        }

        public static string Snapshot()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return "No network manager";
            string text = $"server={nm.IsServerStarted}; client={nm.IsClientStarted}; clientId={nm.ClientManager.Connection.ClientId}\n";
            foreach (var player in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).OrderBy(p => p.OwnerId))
                text += $"player={player.OwnerId}; local={player.IsOwner}; position={player.transform.position}; slots={player.Slots}; held={(player.HeldItem == null ? "none" : player.HeldItem.name)}\n";
            foreach (var item in FindObjectsByType<CarryableItem>(FindObjectsSortMode.None).OrderBy(i => i.name))
            {
                var body = item.GetComponent<Rigidbody>();
                text += $"item={item.name}; id={item.ObjectId}; state={item.State}; holder={item.HolderClientId}; owner={item.OwnerId}; version={item.MotionVersion}; writer={item.WriterOverride}; kinematic={body.isKinematic}; collider={item.PrimaryCollider.enabled}; visible={item.GetComponentInChildren<Renderer>(true).enabled}; position={item.transform.position}; velocity={body.linearVelocity}\n";
            }
            return text;
        }
    }
}
#endif
