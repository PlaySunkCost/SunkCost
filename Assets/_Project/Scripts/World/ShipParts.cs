using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SunkCost.World
{
    // The ship prefab's parts, found by the names of
    // docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.4. The stub and Idan's
    // prefab share these names; code never hard-codes a position on the ship.
    public sealed class ShipParts : MonoBehaviour
    {
        public const string RootName = "Ship";
        public const string AboardVolumeName = "AboardVolume";
        public const string MonitorName = "Monitor";
        public const string MonitorButtonSite01Name = "MonitorButton_Site01";
        public const string MonitorButtonHQName = "MonitorButton_HQ";
        public const string DeckCabinName = "DeckCabin";
        public const string DeckCabinVolumeName = "DeckCabinVolume";
        public const string DeckCabinDoorLName = "DeckCabinDoorL";
        public const string DeckCabinDoorRName = "DeckCabinDoorR";
        public const string DeckCabinButtonName = "DeckCabinButton";
        public const string DeckCabinPanelName = "DeckCabinPanel";
        public const string StorageAreaName = "StorageArea";
        public const string SpawnPointPrefix = "SpawnPoint_";
        public const string BoardingPointName = "BoardingPoint";
        public const int SpawnPointCount = 4;

        public static readonly string[] RequiredChildren =
        {
            AboardVolumeName, MonitorName, MonitorButtonSite01Name, MonitorButtonHQName, DeckCabinName,
            DeckCabinVolumeName, DeckCabinDoorLName, DeckCabinDoorRName, DeckCabinButtonName, DeckCabinPanelName,
            StorageAreaName, BoardingPointName,
            SpawnPointPrefix + "1", SpawnPointPrefix + "2", SpawnPointPrefix + "3", SpawnPointPrefix + "4"
        };

        public Transform Root => transform;
        public Collider AboardVolume => Find(AboardVolumeName)?.GetComponent<Collider>();
        public Transform DeckCabin => Find(DeckCabinName);
        public Collider DeckCabinVolume => Find(DeckCabinVolumeName)?.GetComponent<Collider>();
        public Transform BoardingPoint => Find(BoardingPointName);

        public Transform SpawnPoint(int index) => Find(SpawnPointPrefix + (index + 1));

        public Transform Find(string childName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child;
            return null;
        }

        // Inside the ship: the aboard volume, a box trigger. Tested in the box's own
        // space so it holds for a rotated ship and in the editor before physics has
        // synced transforms (Collider.bounds would lag there).
        public bool IsAboard(Vector3 worldPosition) => Contains(AboardVolume, worldPosition);

        public bool IsInDeckCabin(Vector3 worldPosition) => Contains(DeckCabinVolume, worldPosition);

        public static bool Contains(Collider volume, Vector3 worldPosition)
        {
            if (volume == null) return false;
            if (volume is BoxCollider box)
            {
                Vector3 local = box.transform.InverseTransformPoint(worldPosition) - box.center;
                Vector3 half = box.size * 0.5f;
                return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            }
            return volume.bounds.Contains(worldPosition);
        }

        public Vector3 ToShipLocal(Vector3 worldPosition) => transform.InverseTransformPoint(worldPosition);
        public Vector3 FromShipLocal(Vector3 shipLocal) => transform.TransformPoint(shipLocal);
        public float ToShipYaw(float worldYaw) => worldYaw - transform.eulerAngles.y;
        public float FromShipYaw(float shipYaw) => shipYaw + transform.eulerAngles.y;

        public List<string> MissingChildren()
        {
            var missing = new List<string>();
            foreach (string name in RequiredChildren)
                if (Find(name) == null) missing.Add(name);
            return missing;
        }

        // The ship instance of a loaded world scene, or null.
        public static ShipParts InScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                ShipParts ship = root.GetComponentInChildren<ShipParts>(true);
                if (ship != null) return ship;
            }
            return null;
        }

        public static ShipParts InWorld(WorldId world) => InScene(WorldScenes.Scene(world));
    }
}
