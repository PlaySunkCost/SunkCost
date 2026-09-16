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
    // The player's colour rides with it (Dan, 16 September 2026): an index into
    // PlayerPalette, server-written the same way — a random swatch saved on the
    // machine the first time, changed at the HQ wall's colour panel. Shown on
    // the body, the roster and the F3 panel; the lamp card will tint from it.
    public sealed class PlayerIdentity : NetworkBehaviour
    {
        public const int MaxLength = 16;

        private readonly SyncVar<string> displayName = new(string.Empty);
        private readonly SyncVar<byte> colourIndex = new(0);

        public string DisplayName => string.IsNullOrEmpty(displayName.Value) ? Fallback(OwnerId) : displayName.Value;
        public static string Fallback(int ownerId) => "Diver " + (ownerId + 1);
        public int ColourIndex => colourIndex.Value;
        public Color Colour => PlayerPalette.Get(colourIndex.Value);
        public string ColourHex => PlayerPalette.Hex(colourIndex.Value);

        public event System.Action<Color> ColourChanged;

        private void Awake()
        {
            colourIndex.OnChange += (_, next, _) => ColourChanged?.Invoke(PlayerPalette.Get(next));
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            displayName.Value = Fallback(OwnerId);
            colourIndex.Value = (byte)(OwnerId % PlayerPalette.Count); // until the owner's saved pick lands
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ColourChanged?.Invoke(Colour);
            if (IsOwner) ServerRequestIdentity(PlayerNamePrefs.Load(), PlayerColourPrefs.Load());
        }

        // The owner's wish; the server decides what it becomes.
        [ServerRpc]
        private void ServerRequestIdentity(string wantedName, int wantedColour)
        {
            displayName.Value = Sanitize(wantedName, OwnerId);
            colourIndex.Value = (byte)SanitizeColour(wantedColour, OwnerId);
        }

        [ServerRpc]
        private void ServerRequestColour(int wantedColour)
        {
            colourIndex.Value = (byte)SanitizeColour(wantedColour, OwnerId);
        }

        // Hooks and the lobby's field may re-send (the UI only exposes the field
        // before a room is entered; nothing forbids a later request).
        public void RequestDisplayName(string wanted)
        {
            if (IsOwner && IsSpawned) ServerRequestIdentity(wanted, PlayerColourPrefs.Load());
        }

        // The colour panel's pick: saved here, asked of the server.
        public void RequestColour(int index)
        {
            if (!PlayerPalette.IsValid(index)) return;
            PlayerColourPrefs.Save(index);
            if (IsOwner && IsSpawned) ServerRequestColour(index);
        }

        // An index outside the palette falls back to the owner's seat.
        public static int SanitizeColour(int wanted, int ownerId) => PlayerPalette.IsValid(wanted) ? wanted : ownerId % PlayerPalette.Count;

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
