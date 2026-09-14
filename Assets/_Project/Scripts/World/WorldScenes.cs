using FishNet.Managing.Scened;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace SunkCost.World
{
    // Scene names and paths the flow binds to. Scenes load by name, so every
    // world scene must be in the build list (the Session builder writes it).
    public static class WorldScenes
    {
        public const string SessionName = "Session";
        public const string SessionPath = "Assets/_Project/Scenes/Prototype/Session.unity";
        public const string HQName = "HQPrototype";
        public const string HQPath = "Assets/_Project/Scenes/Prototype/HQPrototype.unity";
        public const string SeaName = "ShipAtSea";
        public const string SeaPath = "Assets/_Project/Scenes/Prototype/ShipAtSea.unity";
        public const string DiveName = "DiveSite01";
        public const string DivePath = "Assets/_Project/Scenes/Prototype/DiveSite01.unity";

        public static string Name(WorldId world)
        {
            switch (world)
            {
                case WorldId.HQ: return HQName;
                case WorldId.Sea: return SeaName;
                case WorldId.Dive: return DiveName;
                default: return string.Empty;
            }
        }

        public static string Path(WorldId world)
        {
            switch (world)
            {
                case WorldId.HQ: return HQPath;
                case WorldId.Sea: return SeaPath;
                case WorldId.Dive: return DivePath;
                default: return string.Empty;
            }
        }

        public static SceneLookupData Lookup(WorldId world) => new(Name(world));

        public static bool TryParse(string sceneName, out WorldId world)
        {
            switch (sceneName)
            {
                case HQName: world = WorldId.HQ; return true;
                case SeaName: world = WorldId.Sea; return true;
                case DiveName: world = WorldId.Dive; return true;
                default: world = WorldId.HQ; return false;
            }
        }

        public static bool IsLoaded(WorldId world) => UnitySceneManager.GetSceneByName(Name(world)).isLoaded;

        public static Scene Scene(WorldId world) => UnitySceneManager.GetSceneByName(Name(world));
    }
}
