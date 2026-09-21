using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Interaction
{
    // The patch kit from the shop's Gear & Supplies (docs/DESIGN.md §3 and §6,
    // 20 September 2026): one use, left click closes the holder's own leak
    // anywhere the suit is on, and it is a "Used patch kit" from then on — left
    // click does nothing, Q drops it. Kit after kit is fine; it is the friend's
    // hands that are once a day. Server-owned like the air tank: the server
    // checks the holder, patches its PlayerVitals and flips `used` (a SyncVar);
    // every peer reads it for the name, the use action and the look.
    public sealed class PatchKitItem : NetworkBehaviour, IItemTag
    {
        public const string FullName = "Patch kit", UsedName = "Used patch kit";

        [Tooltip("The look once used; the prefab's own material is the fresh look.")]
        [SerializeField] private Material usedMaterial;
        [Tooltip("The inventory icon once used (the prefab's own icon is the fresh one).")]
        [SerializeField] private Texture2D usedIcon;
        [SerializeField] private Renderer[] tinted;

        private readonly SyncVar<bool> used = new(false);
        private Material freshMaterial;

        public bool IsUsed => used.Value;
        public string DisplayName => used.Value ? UsedName : FullName;
        public ItemUseAction? UseActionOverride => used.Value ? ItemUseAction.None : ItemUseAction.Patch;
        public Texture2D IconOverride => used.Value ? usedIcon : null;

        private void Awake()
        {
            used.OnChange += OnUsedChanged;
            if (tinted == null || tinted.Length == 0) tinted = GetComponentsInChildren<Renderer>(true);
            if (tinted.Length > 0) freshMaterial = tinted[0].sharedMaterial;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyLook(used.Value);
        }

        private void OnUsedChanged(bool previous, bool next, bool asServer)
        {
            if (IsServerStarted && !asServer) return; // once per peer
            ApplyLook(next);
        }

        private void ApplyLook(bool isUsed)
        {
            Material material = isUsed && usedMaterial != null ? usedMaterial : freshMaterial;
            if (material == null || tinted == null) return;
            foreach (Renderer r in tinted) if (r != null) r.sharedMaterial = material;
        }

        // The holder patches their own leak: the server closes it and uses the kit up.
        [Server]
        public bool ServerPatch(NetworkConnection holder, out string why)
        {
            why = string.Empty;
            if (used.Value) { why = "the kit is used"; return false; }
            HQPlayerController player = null;
            foreach (HQPlayerController candidate in FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
                if (candidate.IsSpawned && candidate.Owner == holder) { player = candidate; break; }
            PlayerVitals vitals = player != null ? player.Vitals : null;
            if (vitals == null) { why = "no vitals on the holder"; return false; }
            if (!vitals.ServerSuitOn) { why = "the suit is off"; return false; }
            SunkCost.World.CrewDayState day = SunkCost.World.CrewDayState.Instance;
            if (!vitals.ServerPatchLeak(false, day != null ? day.Day : 0, out why)) return false;
            used.Value = true;
            Debug.Log($"[Leak] {SunkCost.World.WorldSceneFlow.DisplayName(holder)} patched their own leak with a kit");
            return true;
        }

        // A saved kit comes back as it was.
        [Server]
        public void ServerSetUsed(bool value) => used.Value = value;
    }
}
