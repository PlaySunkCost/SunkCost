using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using SunkCost.Interaction;
using SunkCost.Net;
using SunkCost.Player;
using SunkCost.Shop;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The hosted slot (docs/DESIGN.md §1, "the only save point"; RunSave.cs): the
    // host's server writes it whenever the crew is at HQ and something changed —
    // docking, paying, buying, casting off, a leaver, the run's end — and reads it
    // once at server start. The crew's run lives on the day state; the storage
    // room's items and everyone's upgrades, hands and slots are the flow's to put
    // back, by identity, as each person arrives.
    public sealed partial class WorldSceneFlow
    {
        private RunSaveData hostedSave;                       // null: a run nobody is saving (the matrices, the hooks)
        private readonly HashSet<int> restoredConnections = new();
        private Coroutine boxRestore;

        public bool HostsASave => hostedSave != null;
        public string HostedSaveName => hostedSave != null ? hostedSave.name : string.Empty;

        // Server start: the slot the menu picked, or none.
        private void ServerBeginHostedSave()
        {
            restoredConnections.Clear();
            hostedSave = null;
            if (boxRestore != null) { StopCoroutine(boxRestore); boxRestore = null; }
            int slot = SaveSlots.Active;
            if (!SaveSlots.IsValid(slot)) return;
            hostedSave = SaveSlots.Load(slot) ?? new RunSaveData { name = SaveSlots.DefaultName(slot) };
            if (dayState != null) dayState.ServerRestore(hostedSave);
            if (hostedSave.box.Count > 0) boxRestore = StartCoroutine(ServerRestoreBoxWhenDocked());
            Debug.Log($"[Save] hosting slot {slot + 1} '{hostedSave.name}': day {hostedSave.day}{(hostedSave.payday ? " PAYDAY" : string.Empty)}, ${hostedSave.balance}, {hostedSave.box.Count} in the box, {hostedSave.players.Count} known");
        }

        // The crew is at HQ and something changed: the slot is written now.
        [Server]
        public void ServerSaveRun(string reason)
        {
            if (hostedSave == null || dayState == null || networkManager == null || !networkManager.ServerManager.Started) return;
            if (currentWorld != WorldId.HQ || dayState.Phase != DayPhase.AtHQ || transitioning) return;
            ShipParts ship = ShipParts.InWorld(WorldId.HQ);
            dayState.ServerCapture(hostedSave);
            hostedSave.box.Clear();
            if (ship != null)
                foreach (CarryableItem item in CarryableItem.Spawned)
                {
                    if (item == null || !item.IsSpawned || !item.CanGrabFromWorld || item.NetworkObject.IsSceneObject) continue;
                    if (item.gameObject.scene != ship.gameObject.scene || !ship.IsInStorageRoom(item.transform.position)) continue;
                    SavedItem saved = Describe(item);
                    if (saved == null) continue;
                    saved.localPosition = ship.ToShipLocal(item.transform.position);
                    saved.localEuler = (Quaternion.Inverse(ship.transform.rotation) * item.transform.rotation).eulerAngles;
                    hostedSave.box.Add(saved);
                }
            foreach (NetworkConnection conn in networkManager.ServerManager.Clients.Values)
            {
                if (!conn.IsActive) continue;
                HQPlayerController player = PlayerOf(conn);
                if (player == null) continue;
                SavedPlayer entry = EntryFor(IdentityOf(conn, player), create: true);
                entry.displayName = DisplayName(conn);
                entry.upgrades = player.Upgrades != null ? (int)player.Upgrades.Owned : 0;
                var bySlot = new CarryableItem[InventorySlots.Count];
                CarryableItem held = player.Inventory != null ? player.Inventory.ServerCarried(bySlot, out entry.heldSlot) : null;
                entry.hasHeld = held != null && Describe(held) != null;
                entry.held = entry.hasHeld ? Describe(held) : null;
                entry.slots.Clear();
                for (int i = 0; i < InventorySlots.Count; i++) entry.slots.Add(bySlot[i] != null ? Describe(bySlot[i]) ?? new SavedItem() : new SavedItem());
            }
            SaveSlots.Write(SaveSlots.Active, hostedSave);
            Debug.Log($"[Save] slot {SaveSlots.Active + 1} written ({reason}): day {hostedSave.day}, ${hostedSave.balance}, {hostedSave.box.Count} in the box, {hostedSave.players.Count} known");
        }

        // A person has arrived (their name has landed; PlayerIdentity): what the
        // slot keeps for them comes back once per connection.
        [Server]
        public void ServerIdentityKnown(PlayerIdentity identity)
        {
            if (hostedSave == null || identity == null || !identity.IsSpawned || identity.Owner == null) return;
            NetworkConnection conn = identity.Owner;
            if (restoredConnections.Contains(conn.ClientId)) return;
            restoredConnections.Add(conn.ClientId);
            HQPlayerController player = identity.GetComponent<HQPlayerController>();
            if (player == null) return;
            SavedPlayer entry = EntryFor(IdentityOf(conn, player), create: false);
            if (entry == null) return;
            if (player.Upgrades != null && entry.upgrades != 0) player.Upgrades.ServerGrant((PlayerUpgrade)entry.upgrades);
            if (player.Inventory != null)
            {
                Scene scene = player.gameObject.scene;
                Vector3 at = player.transform.position + Vector3.up * 0.5f;
                var bySlot = new CarryableItem[InventorySlots.Count];
                for (int i = 0; i < InventorySlots.Count && i < entry.slots.Count; i++) bySlot[i] = SpawnSaved(entry.slots[i], at, Quaternion.identity, scene);
                CarryableItem held = entry.hasHeld ? SpawnSaved(entry.held, at, Quaternion.identity, scene) : null;
                player.Inventory.ServerRestoreCarried(held, entry.heldSlot, bySlot);
            }
            Debug.Log($"[Save] {DisplayName(conn)} is back: upgrades {(PlayerUpgrade)entry.upgrades}, {(entry.hasHeld ? "hands full" : "hands empty")}");
        }

        // A leaver at HQ: what they carried dropped where they stood (the harness
        // rule) — the slot keeps their upgrades and forgets their hands.
        private void ServerSaveLeaver(NetworkConnection conn)
        {
            if (hostedSave == null || conn == null) return;
            restoredConnections.Remove(conn.ClientId);
            if (currentWorld != WorldId.HQ) return;
            HQPlayerController player = PlayerOf(conn);
            SavedPlayer entry = player != null ? EntryFor(IdentityOf(conn, player), create: false) : null;
            if (entry != null)
            {
                if (player.Upgrades != null) entry.upgrades = (int)player.Upgrades.Owned;
                entry.hasHeld = false; entry.held = null; entry.heldSlot = -1; entry.slots.Clear();
            }
            ServerSaveRun("a player left");
        }

        private IEnumerator ServerRestoreBoxWhenDocked()
        {
            float deadline = Time.unscaledTime + 30f;
            ShipParts ship = null;
            while (Time.unscaledTime < deadline && (ship = ShipParts.InWorld(WorldId.HQ)) == null) yield return null;
            boxRestore = null;
            if (ship == null || hostedSave == null) yield break;
            int spawned = 0;
            foreach (SavedItem saved in hostedSave.box)
                if (SpawnSaved(saved, ship.FromShipLocal(saved.localPosition), ship.transform.rotation * Quaternion.Euler(saved.localEuler), ship.gameObject.scene) != null) spawned++;
            Debug.Log($"[Save] the storage room's {spawned} items are back on the docked ship");
        }

        // ---- items on disk ------------------------------------------------------------

        private SavedItem Describe(CarryableItem item)
        {
            NetworkObject nob = item.NetworkObject;
            if (nob == null || nob.IsSceneObject || networkManager == null) return null;
            NetworkObject prefab = nob.PrefabId < networkManager.SpawnablePrefabs.GetObjectCount() ? networkManager.SpawnablePrefabs.GetObject(true, nob.PrefabId) : null;
            if (prefab == null) return null;
            AirTankItem tank = item.GetComponent<AirTankItem>();
            return new SavedItem { prefab = prefab.name, instanceName = item.name, value = item.Value, empty = tank != null && tank.IsEmpty };
        }

        private CarryableItem SpawnSaved(SavedItem saved, Vector3 at, Quaternion rotation, Scene scene)
        {
            if (saved == null || string.IsNullOrEmpty(saved.prefab) || networkManager == null) return null;
            NetworkObject prefab = null;
            for (int i = 0; i < networkManager.SpawnablePrefabs.GetObjectCount(); i++)
            {
                NetworkObject candidate = networkManager.SpawnablePrefabs.GetObject(true, i);
                if (candidate != null && candidate.name == saved.prefab) { prefab = candidate; break; }
            }
            if (prefab == null) { Debug.LogWarning($"[Save] no prefab named '{saved.prefab}' — the item is lost"); return null; }
            GameObject instance = Instantiate(prefab.gameObject, at, rotation);
            instance.name = string.IsNullOrEmpty(saved.instanceName) ? prefab.name : saved.instanceName;
            CarryableItem item = instance.GetComponent<CarryableItem>();
            if (item == null) { Destroy(instance); return null; }
            item.SetResetPositionBeforeSpawn(at);
            networkManager.ServerManager.Spawn(instance.GetComponent<NetworkObject>(), null, scene);
            item.ServerSetValue(saved.value);
            AirTankItem tank = instance.GetComponent<AirTankItem>();
            if (tank != null) tank.ServerSetEmpty(saved.empty);
            return item;
        }

        // ---- who is who ---------------------------------------------------------------

        private string IdentityOf(NetworkConnection conn, HQPlayerController player)
        {
            PrototypeAuthenticator auth = networkManager != null ? networkManager.ServerManager.GetAuthenticator() as PrototypeAuthenticator : null;
            if (auth != null && auth.TryGetSteamIdentity(conn.ClientId, out ulong steamId)) return "steam:" + steamId;
            PlayerIdentity identity = player != null ? player.GetComponent<PlayerIdentity>() : null;
            return "name:" + (identity != null ? identity.DisplayName : DisplayName(conn));
        }

        private SavedPlayer EntryFor(string identity, bool create)
        {
            foreach (SavedPlayer p in hostedSave.players) if (p.identity == identity) return p;
            if (!create) return null;
            var entry = new SavedPlayer { identity = identity };
            hostedSave.players.Add(entry);
            return entry;
        }
    }
}
