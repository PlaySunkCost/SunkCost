using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Interaction
{
    // A small air tank lying on the site (docs/DESIGN.md §3, "Air-restoring
    // items"; Dan, 17 September 2026): pick it up, left click, and half a tank
    // comes back — 50 → 100, 0 → 50, 70 → 100, never past full — and it is an
    // "Empty air tank" from then on: worth nothing, left click does nothing, Q
    // drops it (Dan, 18 September 2026: no throw for an empty one). "Full air
    // tank" until then, breathed from underwater only — head below the surface.
    // Server-owned: the server checks the holder, refills its PlayerVitals and
    // flips `empty` (a SyncVar); every peer reads it for the name, the use
    // action (IItemTag) and the look (the empty material).
    public sealed class AirTankItem : NetworkBehaviour, IItemTag
    {
        public const string FullName = "Full air tank", EmptyName = "Empty air tank";

        [Tooltip("The fraction of a full tank one breath gives (0.5 = half a tank).")]
        [SerializeField] private float refillFraction = 0.5f;
        [Tooltip("The look once used; the prefab's own material is the full look.")]
        [SerializeField] private Material emptyMaterial;
        [Tooltip("The inventory icon once used (the prefab's own icon is the full one).")]
        [SerializeField] private Texture2D emptyIcon;
        [SerializeField] private Renderer[] tinted;

        private readonly SyncVar<bool> empty = new(false);
        private Material fullMaterial;

        public bool IsEmpty => empty.Value;
        public float RefillFraction => refillFraction;
        public string DisplayName => empty.Value ? EmptyName : FullName;
        public ItemUseAction? UseActionOverride => empty.Value ? ItemUseAction.None : ItemUseAction.Breathe;
        public Texture2D IconOverride => empty.Value ? emptyIcon : null;

        private void Awake()
        {
            empty.OnChange += OnEmptyChanged;
            if (tinted == null || tinted.Length == 0) tinted = GetComponentsInChildren<Renderer>(true);
            if (tinted.Length > 0) fullMaterial = tinted[0].sharedMaterial;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyLook(empty.Value);
        }

        private void OnEmptyChanged(bool previous, bool next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer
            ApplyLook(next);
        }

        private void ApplyLook(bool isEmpty)
        {
            Material material = isEmpty && emptyMaterial != null ? emptyMaterial : fullMaterial;
            if (material == null || tinted == null) return;
            foreach (Renderer r in tinted) if (r != null) r.sharedMaterial = material;
        }

        // The holder breathes: the server refills its vitals and empties the tank.
        [Server]
        public bool ServerBreathe(NetworkConnection holder, out string why)
        {
            why = string.Empty;
            if (empty.Value) { why = "the tank is empty"; return false; }
            HQPlayerController player = null;
            foreach (HQPlayerController candidate in FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (candidate.IsSpawned && candidate.Owner == holder) { player = candidate; break; }
            PlayerVitals vitals = player != null ? player.Vitals : null;
            if (vitals == null) { why = "no vitals on the holder"; return false; }
            if (!vitals.ServerSuitOn) { why = "the suit is off"; return false; }
            PlayerSubmersion submersion = player.GetComponent<PlayerSubmersion>();
            if (submersion == null || !submersion.IsSubmerged) { why = "not underwater"; return false; } // head below the surface (PlayerInventory refuses first)
            if (vitals.AirFraction >= 1f) { why = "the air is already full"; return false; } // the tank is kept (PlayerInventory refuses first)
            float added = vitals.ServerAddAir(refillFraction);
            empty.Value = true;
            Debug.Log($"[Air] {SunkCost.World.WorldSceneFlow.DisplayName(holder)} breathed {added:0}s from a tank; air now {vitals.AirFraction:P0}");
            return true;
        }

        // A saved tank comes back as it was (the slot's box and hands).
        [Server]
        public void ServerSetEmpty(bool value) => empty.Value = value;

#if UNITY_EDITOR
        // Editor checks only: a used tank made full again for the next row.
        [Server]
        public void ServerRefillForChecks() => empty.Value = false;
#endif
    }
}
