using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.Player
{
    // A dead player's body (docs/SPECTATING_IMPLEMENTATION_PLAN.md, card 1;
    // design §4): a two-handed, heavy carryable spawned by the server where the
    // player died. It rides the car like any cargo, unloads with the site if left
    // below (lost), and disappears at End day when its owner is revived next to
    // it on the ship. The CarryableItem on the same prefab does the carrying;
    // this only says whose body it is.
    public sealed class PlayerBody : NetworkBehaviour
    {
        public const string PrefabName = "PlayerBody";

        private readonly SyncVar<int> ownerClientId = new(-1);
        private readonly SyncVar<string> ownerName = new(string.Empty);

        public int OwnerClientId => ownerClientId.Value;
        public string DisplayName => (string.IsNullOrEmpty(ownerName.Value) ? "Diver" : ownerName.Value) + "'s body";

        [Server]
        public void ServerSetOwner(int clientId, string name)
        {
            ownerClientId.Value = clientId;
            ownerName.Value = name ?? string.Empty;
        }

        // The body of player `clientId` in `scene`, or null.
        public static PlayerBody FindFor(int clientId, Scene scene)
        {
            foreach (PlayerBody body in FindObjectsByType<PlayerBody>(FindObjectsInactive.Exclude))
                if (body.IsSpawned && body.OwnerClientId == clientId && body.gameObject.scene == scene) return body;
            return null;
        }

        // Spawn a body where a player died. The prefab is a registered spawnable
        // (PlayerBodySetup); the item's reset spot is set before the spawn so its
        // OnStartServer reset lands it there.
        [Server]
        public static CarryableItem ServerSpawn(NetworkManager manager, int clientId, string name, Vector3 at, Scene scene)
        {
            NetworkObject prefab = null;
            for (int i = 0; i < manager.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = manager.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == PrefabName) { prefab = candidate; break; }
            }
            if (prefab == null) { Debug.LogError("[PlayerBody] prefab '" + PrefabName + "' is not a registered spawnable; run Sunk Cost > Prototype > Apply player body setup."); return null; }
            NetworkObject instance = Instantiate(prefab, at, Quaternion.identity);
            instance.name = PrefabName + " (" + clientId + ")";
            CarryableItem item = instance.GetComponent<CarryableItem>();
            item.SetResetPositionBeforeSpawn(at);
            manager.ServerManager.Spawn(instance, null, scene);
            instance.GetComponent<PlayerBody>().ServerSetOwner(clientId, name);
            return item;
        }
    }
}
