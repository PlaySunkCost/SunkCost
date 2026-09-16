using System.Text;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace SunkCost.Player
{
    // The player's display name (Dan, 16 September 2026: "every player has his
    // name, default is the Steam name, can be changed in the lobby before
    // entering a room, saved for the next log in"). The server writes it: it
    // seeds "Diver N" at spawn, and the owning client asks once for the name it
    // saved (PlayerNamePrefs) — the server sanitises and writes, never trusting
    // the string as sent. Read by the visor's crew tags and the session roster.
    public sealed class PlayerIdentity : NetworkBehaviour
    {
        public const int MaxLength = 16;

        private readonly SyncVar<string> displayName = new(string.Empty);

        public string DisplayName => string.IsNullOrEmpty(displayName.Value) ? Fallback(OwnerId) : displayName.Value;
        public static string Fallback(int ownerId) => "Diver " + (ownerId + 1);

        public override void OnStartServer()
        {
            base.OnStartServer();
            displayName.Value = Fallback(OwnerId);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) ServerRequestDisplayName(PlayerNamePrefs.Load());
        }

        // The owner's wish; the server decides what it becomes.
        [ServerRpc]
        private void ServerRequestDisplayName(string wanted)
        {
            displayName.Value = Sanitize(wanted, OwnerId);
        }

        // Hooks and the lobby's field may re-send (the UI only exposes the field
        // before a room is entered; nothing forbids a later request).
        public void RequestDisplayName(string wanted)
        {
            if (IsOwner && IsSpawned) ServerRequestDisplayName(wanted);
        }

        // Trimmed, printable, no angle brackets (the roster is IMGUI rich text),
        // runs of spaces collapsed, at most MaxLength characters; empty falls
        // back to "Diver N". Pure, so the editor checks pin it.
        public static string Sanitize(string wanted, int ownerId)
        {
            if (string.IsNullOrEmpty(wanted)) return Fallback(ownerId);
            var sb = new StringBuilder(wanted.Length);
            bool lastSpace = true;
            foreach (char c in wanted)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                bool space = char.IsWhiteSpace(c);
                if (space && lastSpace) continue;
                sb.Append(space ? ' ' : c);
                lastSpace = space;
                if (sb.Length >= MaxLength) break;
            }
            string result = sb.ToString().TrimEnd();
            return result.Length == 0 ? Fallback(ownerId) : result;
        }
    }

    // The saved name on this machine: PlayerPrefs, default the Steam persona
    // name when Steam is up, "Diver" otherwise.
    public static class PlayerNamePrefs
    {
        public const string Key = "sunkcost.displayName";
        public static string DefaultName = "Diver";

        public static string Load()
        {
            string saved = PlayerPrefs.GetString(Key, string.Empty);
            return string.IsNullOrWhiteSpace(saved) ? DefaultName : saved;
        }

        public static void Save(string name)
        {
            PlayerPrefs.SetString(Key, name ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static bool HasSaved => !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(Key, string.Empty));
    }
}
