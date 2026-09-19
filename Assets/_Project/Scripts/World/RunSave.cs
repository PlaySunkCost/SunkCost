using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SunkCost.World
{
    // A run on disk (Dan, 19 September 2026: "each player has up to three saves;
    // hosting shows three slots, you pick one and name it"). The host owns the
    // save (NETWORK_CONTRACT §6): the slot lives on the host's machine and is
    // written by its server whenever the crew is at HQ — the design's only save
    // point — on docking, after paying, after every buy, at the cast-off, and
    // when the host leaves. Guests bring nothing: their upgrades and what they
    // carry are kept under their identity (Steam id, or the display name on a
    // LAN) and come back when that person rejoins the run.
    //
    // JsonUtility: a version field from the first save, unknown fields ignored,
    // missing ones default — a later build reads an older slot.
    [Serializable]
    public sealed class RunSaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string name = string.Empty;
        public string createdUtc = string.Empty;
        public string savedUtc = string.Empty;

        // The crew's run (CrewDayState).
        public int day;
        public bool payday;
        public bool diveDone;
        public int cycleSales;
        public int balance;
        public int runDays;
        public float runSeconds;   // the run clock at the save, for the recap card
        public int baskets;

        // The storage room on the docked ship, ship-local.
        public List<SavedItem> box = new();
        // Everyone who was ever in this run, by identity.
        public List<SavedPlayer> players = new();

        public bool IsFresh => day == 0 && balance == 0 && cycleSales == 0 && runDays == 0 && box.Count == 0;
    }

    [Serializable]
    public sealed class SavedItem
    {
        public string prefab = string.Empty;   // the networked prefab's name (SpawnablePrefabs)
        public string instanceName = string.Empty;
        public int value;
        public bool empty;                     // an air tank that was breathed
        public Vector3 localPosition;          // box items: ship-local
        public Vector3 localEuler;
    }

    [Serializable]
    public sealed class SavedPlayer
    {
        public string identity = string.Empty; // "steam:<id>" or "name:<display name>"
        public string displayName = string.Empty;
        public int upgrades;                   // PlayerUpgrade flags
        public bool hasHeld;
        public SavedItem held;
        public int heldSlot = -1;              // the held item's own slot, if it fits one
        public List<SavedItem> slots = new();  // exactly InventorySlots.Count entries; prefab empty = no item
    }

    // The three slots and their files. Index 0..2; SaveSlots.None is a run that
    // is not saved at all (the editor's matrices and the test hooks host that way,
    // so a stale slot never leaks into a check).
    public static class SaveSlots
    {
        public const int Count = 3;
        public const int None = -1;
        public const int MaxNameLength = 24;

        // The slot the running host writes; None until the menu picks one.
        public static int Active { get; set; } = None;
        // Tests point this at a scratch folder; null = the real saves folder.
        public static string DirectoryOverride { get; set; }

        public static string Directory => DirectoryOverride ?? Path.Combine(Application.persistentDataPath, "saves");
        public static string PathOf(int slot) => Path.Combine(Directory, "slot" + (slot + 1) + ".json");
        public static string DefaultName(int slot) => "Save " + (slot + 1);
        public static bool IsValid(int slot) => slot >= 0 && slot < Count;

        public static bool Exists(int slot) => IsValid(slot) && File.Exists(PathOf(slot));

        public static RunSaveData Load(int slot)
        {
            if (!Exists(slot)) return null;
            try
            {
                RunSaveData data = JsonUtility.FromJson<RunSaveData>(File.ReadAllText(PathOf(slot)));
                if (data == null) return null;
                if (string.IsNullOrEmpty(data.name)) data.name = DefaultName(slot);
                data.box ??= new List<SavedItem>();
                data.players ??= new List<SavedPlayer>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] slot {slot + 1} could not be read: {e.Message}");
                return null;
            }
        }

        public static void Write(int slot, RunSaveData data)
        {
            if (!IsValid(slot) || data == null) return;
            data.version = RunSaveData.CurrentVersion;
            data.savedUtc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(data.createdUtc)) data.createdUtc = data.savedUtc;
            if (string.IsNullOrEmpty(data.name)) data.name = DefaultName(slot);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string tmp = PathOf(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot));
                File.Move(tmp, PathOf(slot));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] slot {slot + 1} could not be written: {e.Message}");
            }
        }

        public static void Delete(int slot)
        {
            if (!Exists(slot)) return;
            try { File.Delete(PathOf(slot)); }
            catch (Exception e) { Debug.LogWarning($"[Save] slot {slot + 1} could not be deleted: {e.Message}"); }
        }

        public static void Rename(int slot, string name)
        {
            RunSaveData data = Load(slot);
            if (data == null) return;
            data.name = CleanName(name, slot);
            Write(slot, data);
        }

        public static string CleanName(string wanted, int slot)
        {
            string s = (wanted ?? string.Empty).Trim();
            if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength);
            return s.Length == 0 ? DefaultName(slot) : s;
        }

        // One line for the menu: "Save 2 · day 2 of 3 · $410 · 19 Sep 14:02".
        public static string Summary(int slot)
        {
            RunSaveData data = Load(slot);
            if (data == null) return DefaultName(slot) + " — empty";
            string when = DateTime.TryParse(data.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime saved) ? saved.ToLocalTime().ToString("d MMM HH:mm") : "?";
            string cycle = data.day == 0 ? "at HQ, no cycle" : data.payday ? "PAYDAY" : "day " + data.day;
            return $"{data.name} · {cycle} · ${data.balance} · {when}";
        }
    }
}
