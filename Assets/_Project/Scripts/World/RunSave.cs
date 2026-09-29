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
        // The sites bought at the console this run (a Destinations.Bit mask of Site02..04;
        // the shared console, 27 September 2026). Missing in an older slot → 0 = locked.
        public int unlockedSites;

        // The storage room on the docked ship, ship-local.
        public List<SavedItem> box = new();
        // Everyone who was ever in this run, by identity.
        public List<SavedPlayer> players = new();

        public bool IsFresh => day == 0 && balance == 0 && cycleSales == 0 && runDays == 0 && box.Count == 0 && unlockedSites == 0;
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
            if (TryLoad(slot, out RunSaveData data, out string error)) return data;
            if (Exists(slot)) Debug.LogWarning($"[Save] slot {slot + 1}: {error}");
            return null;
        }

        public static bool TryLoad(int slot, out RunSaveData data, out string error)
        {
            data = null; error = string.Empty;
            if (!IsValid(slot)) { error = "Invalid save slot."; return false; }
            if (!Exists(slot)) { error = "This slot is empty."; return false; }
            try
            {
                string json = File.ReadAllText(PathOf(slot));
                if (!json.TrimStart().StartsWith("{") || !json.Contains("\"version\""))
                    throw new InvalidDataException("This file is not a Sunk Cost save.");
                data = JsonUtility.FromJson<RunSaveData>(json);
                if (data == null || data.version != RunSaveData.CurrentVersion)
                    throw new InvalidDataException("This save version is not supported by this build.");
                if (string.IsNullOrEmpty(data.name)) data.name = DefaultName(slot);
                data.box ??= new List<SavedItem>();
                data.players ??= new List<SavedPlayer>();
                return true;
            }
            catch (Exception e)
            {
                data = null; error = "Could not read this save. " + e.Message; return false;
            }
        }

        public static void Write(int slot, RunSaveData data)
        {
            if (!TryWrite(slot, data, out string error)) Debug.LogWarning("[Save] " + error);
        }

        public static bool TryWrite(int slot, RunSaveData data, out string error)
        {
            error = string.Empty;
            if (!IsValid(slot) || data == null) { error = "Invalid save slot or data."; return false; }
            data.version = RunSaveData.CurrentVersion;
            data.savedUtc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(data.createdUtc)) data.createdUtc = data.savedUtc;
            if (string.IsNullOrEmpty(data.name)) data.name = DefaultName(slot);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string tmp = PathOf(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                // Never delete the last good save before the replacement succeeds.
                if (File.Exists(PathOf(slot))) File.Replace(tmp, PathOf(slot), null);
                else File.Move(tmp, PathOf(slot));
                return true;
            }
            catch (Exception e)
            {
                error = $"Could not write save {slot + 1}. {e.Message}"; return false;
            }
        }

        public static void Delete(int slot)
        {
            if (!TryDelete(slot, out string error)) Debug.LogWarning("[Save] " + error);
        }

        public static bool TryDelete(int slot, out string error)
        {
            error = string.Empty;
            if (!IsValid(slot)) { error = "Invalid save slot."; return false; }
            try { if (Exists(slot)) File.Delete(PathOf(slot)); return true; }
            catch (Exception e) { error = "Could not delete this save. " + e.Message; return false; }
        }

        public static void Rename(int slot, string name)
        {
            if (!TryRename(slot, name, out string error)) Debug.LogWarning("[Save] " + error);
        }

        public static bool TryRename(int slot, string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name)) { error = "Please enter a name."; return false; }
            if (!TryLoad(slot, out RunSaveData data, out error)) return false;
            data.name = CleanName(name, slot);
            return TryWrite(slot, data, out error);
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
