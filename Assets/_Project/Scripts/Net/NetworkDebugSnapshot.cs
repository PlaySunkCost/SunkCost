using System;
using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace SunkCost.Net
{
    // A read-only picture of what the network layer is doing on this machine: who
    // is the server, who owns each spawned object, and which objects this machine
    // is the simulation writer for (NETWORK_CONTRACT.md section 2). Kept separate
    // from the overlay MonoBehaviour so editor commands can assert its text and so
    // the F4 dump and the on-screen panel show exactly the same data.
    public sealed class NetworkDebugSnapshot
    {
        public struct ObjectRow
        {
            public int ObjectId;
            public string Name;
            public int OwnerId;   // -1 = no client owner (server-controlled)
            public bool IsOwner;  // this machine owns it
            public bool IsWriter; // this machine simulates it; everyone else renders replicated state
            public string Detail; // INetworkDebugInfo.DebugStatus or ""
            public string Swatch; // "#rrggbb" for a player (its colour), else null
            public string Scene;  // Unity scene the object sits in on this peer

            public string OwnerText => IsOwner ? "me" : OwnerId < 0 ? "server" : "client " + OwnerId;
            public string SimText => IsWriter ? "SIM-HERE" : "REPLICATED";
        }

        public bool HasNetworkManager;
        public bool ServerStarted;
        public bool ClientStarted;
        public int LocalClientId = -1;
        public string Transport = "none";
        public long RoundTripTimeMs;
        public ushort TickRate;
        public uint Tick;
        public int ConnectedClients; // server side only, else 0
        public string Frames = string.Empty; // FrameTimeRecorder.Summary on this machine, "" without one
        public readonly List<ObjectRow> Objects = new();

        public string Role =>
            !HasNetworkManager ? "OFFLINE" :
            ServerStarted && ClientStarted ? "HOST" :
            ServerStarted ? "SERVER-ONLY" :
            ClientStarted ? "CLIENT" : "OFFLINE";

        public static NetworkDebugSnapshot Capture()
        {
            var snapshot = new NetworkDebugSnapshot();
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null)
                return snapshot;

            snapshot.HasNetworkManager = true;
            snapshot.ServerStarted = nm.IsServerStarted;
            snapshot.ClientStarted = nm.IsClientStarted;
            if (nm.ClientManager != null && nm.ClientManager.Connection != null)
                snapshot.LocalClientId = nm.ClientManager.Connection.ClientId;
            if (nm.TransportManager != null && nm.TransportManager.Transport != null)
                snapshot.Transport = nm.TransportManager.Transport.GetType().Name;
            if (nm.TimeManager != null)
            {
                snapshot.RoundTripTimeMs = nm.TimeManager.RoundTripTime;
                snapshot.TickRate = nm.TimeManager.TickRate;
                snapshot.Tick = nm.TimeManager.Tick;
            }
            if (nm.IsServerStarted && nm.ServerManager != null)
                snapshot.ConnectedClients = nm.ServerManager.Clients.Count;
            if (SunkCost.Diagnostics.FrameTimeRecorder.Instance != null)
                snapshot.Frames = SunkCost.Diagnostics.FrameTimeRecorder.Instance.Summary;

            IReadOnlyDictionary<int, NetworkObject> spawned = null;
            if (nm.IsServerStarted && nm.ServerManager != null)
                spawned = nm.ServerManager.Objects.Spawned;
            else if (nm.IsClientStarted && nm.ClientManager != null)
                spawned = nm.ClientManager.Objects.Spawned;
            if (spawned == null)
                return snapshot;

            foreach (KeyValuePair<int, NetworkObject> pair in spawned)
            {
                NetworkObject nob = pair.Value;
                if (nob == null || !nob.IsSpawned)
                    continue;
                snapshot.Objects.Add(BuildRow(nob, nm.IsServerStarted));
            }

            // Owned objects (the players) first, then by id, so rows do not jump
            // between refreshes.
            snapshot.Objects.Sort((a, b) =>
            {
                bool aOwned = a.OwnerId >= 0, bOwned = b.OwnerId >= 0;
                if (aOwned != bOwned) return aOwned ? -1 : 1;
                return a.ObjectId.CompareTo(b.ObjectId);
            });
            return snapshot;
        }

        private static ObjectRow BuildRow(NetworkObject nob, bool serverStarted)
        {
            var row = new ObjectRow
            {
                ObjectId = nob.ObjectId,
                Name = nob.gameObject.name.Replace("(Clone)", string.Empty),
                OwnerId = nob.OwnerId,
                IsOwner = nob.IsOwner,
                Detail = string.Empty,
                Scene = nob.gameObject.scene.name ?? string.Empty
            };
            SunkCost.Player.PlayerIdentity identity = nob.GetComponent<SunkCost.Player.PlayerIdentity>();
            if (identity != null) { row.Swatch = identity.ColourHex; row.Name = identity.DisplayName; }

            // Every debug-info component on the object contributes to the line (the
            // player's inventory and its submersion, for instance); the first one that
            // states a writer override wins.
            bool? writerOverride = null;
            var details = new System.Collections.Generic.List<string>();
            foreach (INetworkDebugInfo info in nob.GetComponents<INetworkDebugInfo>())
            {
                try
                {
                    string detail = info.DebugStatus;
                    if (!string.IsNullOrEmpty(detail)) details.Add(detail);
                    if (!writerOverride.HasValue) writerOverride = info.WriterOverride;
                }
                catch (Exception) { details.Add("<error>"); }
            }
            row.Detail = string.Join("; ", details);

            // Who simulates this object here? Rules in priority order.
            // 0. The object says so (INetworkDebugInfo.WriterOverride): objects that
            //    are kinematic BY DESIGN, such as a CarryableItem held in a hand.
            // 1. Rigidbody: the writer is the only peer whose body is not kinematic.
            //    CarryableItem.ApplyRole implements the contract exactly this way for
            //    Free and Released, so this column cannot disagree with the item's
            //    real behaviour there.
            // 2. Client-owned (the players, client-authoritative NetworkTransform):
            //    the owner moves it.
            // 3. Unowned: the server simulates it.
            if (writerOverride.HasValue)
                row.IsWriter = writerOverride.Value;
            else if (nob.TryGetComponent(out Rigidbody body))
                row.IsWriter = !body.isKinematic;
            else if (nob.OwnerId >= 0)
                row.IsWriter = nob.IsOwner;
            else
                row.IsWriter = serverStarted;
            return row;
        }

        // Fixed columns so it pastes cleanly into chat and Player.log.
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("[NetDebug] ").Append(Role);
            if (HasNetworkManager)
            {
                sb.Append("  client=").Append(LocalClientId)
                  .Append("  transport=").Append(Transport)
                  .Append("  rtt=").Append(RoundTripTimeMs).Append("ms")
                  .Append("  tick=").Append(TickRate).Append("Hz/").Append(Tick);
                if (ServerStarted)
                    sb.Append("  clients=").Append(ConnectedClients);
            }
            sb.AppendLine();
            if (!string.IsNullOrEmpty(Frames)) sb.Append("frames: ").Append(Frames).AppendLine();
            sb.AppendLine(" id   name              owner     sim         scene        detail");
            foreach (ObjectRow row in Objects)
            {
                sb.Append(row.ObjectId.ToString().PadLeft(3)).Append("   ")
                  .Append(Truncate(row.Name, 17).PadRight(17)).Append(' ')
                  .Append(row.OwnerText.PadRight(9)).Append(' ')
                  .Append(row.SimText.PadRight(11)).Append(' ')
                  .Append(Truncate(row.Scene, 12).PadRight(12)).Append(' ')
                  .Append(row.Detail)
                  .AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
        }
    }
}
