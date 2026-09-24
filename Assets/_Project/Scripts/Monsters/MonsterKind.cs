namespace SunkCost.Monsters
{
    // The seven (docs/DESIGN.md §6 "The monsters", decided 20 September 2026).
    // The Elevator Ghost is an event on the car, not a walker; the other six are
    // Creature prefabs the roster draws from.
    public enum MonsterKind : byte
    {
        ElevatorGhost = 0,
        LongWalker = 1,
        WeepingAngel = 2,
        Charger = 3,
        Lure = 4,
        Listener = 5,
        Impostor = 6
    }

    // What a creature is doing, replicated as one byte for the look and the
    // sounds on every peer; the server's brain writes it.
    public enum CreaturePose : byte
    {
        Idle = 0,
        Drawn = 1,     // walking toward something heard or a last-seen spot
        Hunting = 2,   // walking after a diver it can reach
        Frozen = 3,    // the Angel, watched
        Windup = 4,    // the Charger's shake
        Rushing = 5,   // the Charger's rush
        Shooting = 6,  // the Lure and the Listener: a bolt just left
        Fleeing = 7,   // the Impostor after a touch
        Grabbing = 8,  // the Long Walker and the Angel holding a caught diver (24 September 2026)
        Recovering = 9, // the Charger after a rush, hit or miss
        Aiming = 10    // the Lure and the Listener charging a beam, before it burns
    }

    public static class MonsterCatalog
    {
        public static readonly MonsterKind[] Walkers =
        {
            MonsterKind.LongWalker, MonsterKind.WeepingAngel, MonsterKind.Charger,
            MonsterKind.Lure, MonsterKind.Listener, MonsterKind.Impostor
        };

        public const string PrefabFolder = "Assets/_Project/Prefabs/Monsters";
        public const string LayerName = "Monster";

        // The prefab's root name, which is how the server finds it among the spawnables.
        public static string PrefabName(MonsterKind kind) => "Monster " + kind;
        public static string PrefabPath(MonsterKind kind) => PrefabFolder + "/" + PrefabName(kind) + ".prefab";

        public static string DisplayName(MonsterKind kind) => kind switch
        {
            MonsterKind.ElevatorGhost => "the Elevator Ghost",
            MonsterKind.LongWalker => "the Long Walker",
            MonsterKind.WeepingAngel => "the Weeping Angel",
            MonsterKind.Charger => "the Charger",
            MonsterKind.Lure => "the Lure",
            MonsterKind.Listener => "the Listener",
            MonsterKind.Impostor => "the Impostor",
            _ => kind.ToString()
        };

        // Three kill on the spot; four take health and open a leak (Dan, 20 September 2026).
        public static bool Kills(MonsterKind kind) =>
            kind == MonsterKind.ElevatorGhost || kind == MonsterKind.LongWalker || kind == MonsterKind.WeepingAngel;

        public static int Layer => UnityEngine.LayerMask.NameToLayer(LayerName);
    }
}
