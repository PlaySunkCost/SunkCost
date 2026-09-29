using SunkCost.Net;
using SunkCost.World;
using UnityEngine;
using UnityEngine.UI;

namespace SunkCost.UI
{
    public sealed partial class MenuRoot
    {
        private int selectedSlot;
        private void ShowSaves()
        {
            Clear(Page.Saves); Header("HOST GAME  /  SELECT A SAVE");
            bool selectedExists = false, selectedValid = false;
            for (int i = 0; i < SaveSlots.Count; i++)
            {
                int slot = i; bool exists = SaveSlots.Exists(slot);
                bool valid = SaveSlots.TryLoad(slot, out RunSaveData data, out _);
                string name = valid ? data.name : SaveSlots.DefaultName(slot);
                string progress = !exists ? "EMPTY" : !valid ? "UNREADABLE OR UNSUPPORTED SAVE" :
                    (data.day == 0 ? "At HQ" : data.payday ? "Payday" : "Day " + data.day) + "   /   $" + data.balance;
                var button = MenuWidgets.Button(pages, name.ToUpperInvariant() + "\n" + progress, 90, 270 + i * 116, 720, 95,
                    () => { selectedSlot = slot; ShowSaves(); }, selectedSlot == slot);
                if (slot == selectedSlot) { selectedExists = exists; selectedValid = valid; Focus(button); }
            }
            var host = MenuWidgets.Button(pages, "HOST SAVE", 90, 635, 430, 62, HostSelected, true);
            host.interactable = !selectedExists || selectedValid;
            var rename = MenuWidgets.Button(pages, "RENAME", 90, 716, 430, 62, RenameSelected);
            rename.interactable = selectedValid;
            var delete = MenuWidgets.Button(pages, "DELETE", 550, 716, 260, 62, DeleteSelected); delete.interactable = selectedExists;
            MenuWidgets.Button(pages, "BACK", 90, 818, 430, 62, ShowMain);
            MenuWidgets.Label(pages, "Choose a save to continue or start a new run. Friends join through Steam invitations.", 90, 905, 1100, 48, 21);
        }
        private void HostSelected()
        {
            if (session.Busy || session.InRoom) return;
            int slot = selectedSlot;
            if (SaveSlots.Exists(slot) && !SaveSlots.TryLoad(slot, out _, out string readError))
            { dialog.Error("Cannot host this save", readError); return; }
            if (!session.SelectMode(SessionMode.Steam, out string steamError))
            { dialog.Error("Cannot start game", steamError); return; }
            if (!SaveSlots.Exists(slot) && !SaveSlots.TryWrite(slot, new RunSaveData { name = SaveSlots.DefaultName(slot) }, out string writeError))
            { dialog.Error("Cannot create save", writeError); return; }
            beforeBusy = Page.Saves; cancelling = false;
            session.StartHost(slot);
            if (session.Busy) ShowBusy();
            else if (!session.InRoom) dialog.Error("Cannot start game", session.Message);
        }
        private void RenameSelected()
        {
            int slot = selectedSlot;
            if (!SaveSlots.TryLoad(slot, out RunSaveData data, out string error)) { dialog.Error("Cannot rename", error); return; }
            dialog.Rename(data.name, SaveSlots.MaxNameLength, name =>
            {
                if (!SaveSlots.TryRename(slot, name, out string failure)) return failure;
                // Refresh on the following frame, after the input dialog has closed.
                refreshSaves = true; return null;
            });
        }
        private void DeleteSelected()
        {
            int slot = selectedSlot;
            string name = SaveSlots.TryLoad(slot, out RunSaveData data, out _) ? data.name : SaveSlots.DefaultName(slot);
            dialog.Confirm("Delete save?", "Delete '" + name + "'? This cannot be undone.", () =>
            {
                if (!SaveSlots.TryDelete(slot, out string error)) { dialog.Error("Could not delete", error); return; }
                ShowSaves();
            });
        }
    }
}
