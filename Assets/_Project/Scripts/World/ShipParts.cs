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
        public const string MonitorButtonEndDayName = "MonitorButton_EndDay";
        public const string MonitorStatusName = "MonitorStatus"; // TextMesh the ShipMonitor writes
        public const string DeckCabinName = "DeckCabin";
        public const string DeckCabinVolumeName = "DeckCabinVolume";
        public const string DeckCabinDoorLName = "DeckCabinDoorL";
        public const string DeckCabinDoorRName = "DeckCabinDoorR";
        public const string DeckCabinHousingDoorLName = "DeckCabinHousingDoorL"; // the tube's own leaves at the top: shut while the car is away (optional parts)
        public const string DeckCabinHousingDoorRName = "DeckCabinHousingDoorR";
        public const string DeckCabinButtonName = "DeckCabinButton";
        public const string DeckCabinPanelName = "DeckCabinPanel";
        public const string DeckCabinCarGlassName = "DeckCabinCarGlass"; // the car's own glass, shown while the car is up (optional part)
        public const string DeckCabinDoorColliderName = "DeckCabinDoorCollider"; // blocks the doorway while the doors are not fully open (optional part)
        public const string StorageAreaName = "StorageArea";
        public const string StorageVolumeName = "StorageVolume";   // the room's inside: what the pay button sells
        public const string StorageReadoutName = "StorageReadout"; // TextMesh the StorageReadout writes ("$100 / $200")
        // The deck TV (docs/SPECTATING_IMPLEMENTATION_PLAN.md card 3): the screen
        // quad ShipTV renders the channel diver's view onto (its collider is what E
        // targets), the caption over it ("LIVE · name" / "NO SIGNAL") and where its
        // sound plays from.
        public const string TvScreenName = "TvScreen";
        public const string TvCaptionName = "TvCaption";   // TextMesh ShipTV writes
        public const string TvSpeakerName = "TvSpeaker";
        public const string SpawnPointPrefix = "SpawnPoint_";
        public const string BoardingPointName = "BoardingPoint";
        // Departure parts (docs/SHIP_DEPARTURE_IMPLEMENTATION_PLAN.md section 7).
        public const string SafeDeckVolumeName = "SafeDeckVolume";           // the deck proper: where a passenger must stand
        public const string DepartureDirectionName = "DepartureDirection";   // its forward is the straight way out
        // No gangway on the ship (Dan, 18 September 2026): the HQ's bridge reaches the
        // stern; the ship carries nothing that touches the base.
        // The ship's areas (SHIP-076, 23 September 2026): the build groups every part
        // under the area it belongs to, each model with the parts that sit on it, so
        // moving an area moves its buttons and screens too (Editor ShipHierarchy).
        // Find searches at any depth, so nothing reads these to find a part.
        public const string WellGroupName = "Well";
        public const string TowerGroupName = "Tower";
        public const string ConsoleGroupName = "Console"; // under Tower: the console model, the monitor, its buttons and its displays
        public const string TvGroupName = "Tv";
        public const string StorageGroupName = "Storage";
        public const string CabinGroupName = "Cabin";
        public const string VolumesGroupName = "Volumes";
        public const string PointsGroupName = "Points";
        public const int SpawnPointCount = 4;

        public static readonly string[] RequiredChildren =
        {
            AboardVolumeName, MonitorName, MonitorButtonSite01Name, MonitorButtonHQName, MonitorButtonEndDayName, MonitorStatusName, DeckCabinName,
            DeckCabinVolumeName, DeckCabinDoorLName, DeckCabinDoorRName, DeckCabinButtonName, DeckCabinPanelName,
            StorageAreaName, StorageVolumeName, StorageReadoutName, BoardingPointName,
            TvScreenName, TvCaptionName, TvSpeakerName,
            SafeDeckVolumeName, DepartureDirectionName,
            SpawnPointPrefix + "1", SpawnPointPrefix + "2", SpawnPointPrefix + "3", SpawnPointPrefix + "4"
        };

        public Transform Root => transform;
        public Collider AboardVolume => Find(AboardVolumeName)?.GetComponent<Collider>();
        public Transform DeckCabin => Find(DeckCabinName);
        public Collider DeckCabinVolume => Find(DeckCabinVolumeName)?.GetComponent<Collider>();
        public Transform DeckCabinDoorL => Find(DeckCabinDoorLName);
        public Transform DeckCabinDoorR => Find(DeckCabinDoorRName);
        public Transform DeckCabinHousingDoorL => Find(DeckCabinHousingDoorLName);
        public Transform DeckCabinHousingDoorR => Find(DeckCabinHousingDoorRName);
        public Transform DeckCabinButton => Find(DeckCabinButtonName);
        public TextMesh DeckCabinPanel => Find(DeckCabinPanelName)?.GetComponent<TextMesh>();
        public Transform DeckCabinCarGlass => Find(DeckCabinCarGlassName);
        public Collider DeckCabinDoorCollider { get { Transform t = Find(DeckCabinDoorColliderName); return t != null ? t.GetComponent<Collider>() : null; } }
        public Collider StorageVolume => Find(StorageVolumeName)?.GetComponent<Collider>();
        public TextMesh StorageReadout => Find(StorageReadoutName)?.GetComponent<TextMesh>();
        public Transform BoardingPoint => Find(BoardingPointName);
        public Transform TvScreen => Find(TvScreenName);
        public TextMesh TvCaption => Find(TvCaptionName)?.GetComponent<TextMesh>();
        public Transform TvSpeaker => Find(TvSpeakerName);
        public Collider SafeDeckVolume => Find(SafeDeckVolumeName)?.GetComponent<Collider>();
        public Transform DepartureDirection => Find(DepartureDirectionName);

        public Transform SpawnPoint(int index) => Find(SpawnPointPrefix + (index + 1));

        public Transform Find(string childName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child;
            return null;
        }

        // How many parts carry the name: a name Find is asked for must be unique.
        public int CountNamed(string childName)
        {
            int count = 0;
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName) count++;
            return count;
        }

        // Inside the ship: the aboard volume, a box trigger. Tested in the box's own
        // space so it holds for a rotated ship and in the editor before physics has
        // synced transforms (Collider.bounds would lag there).
        public bool IsAboard(Vector3 worldPosition) => Contains(AboardVolume, worldPosition);

        public bool IsInDeckCabin(Vector3 worldPosition) => Contains(DeckCabinVolume, worldPosition);

        public bool IsInStorageRoom(Vector3 worldPosition) => Contains(StorageVolume, worldPosition);

        // Standing on the deck proper (the HQ's landing over the stern is not the
        // ship): what a passenger needs before the ship may move. Falls back to the
        // aboard volume on a ship without the departure parts.
        public bool IsSafelyAboard(Vector3 worldPosition)
        {
            Collider deck = SafeDeckVolume;
            if (deck == null) return IsAboard(worldPosition);
            return Contains(deck, worldPosition);
        }

        public static bool Contains(Collider volume, Vector3 worldPosition)
        {
            if (volume == null) return false;
            if (volume is BoxCollider box)
            {
                Vector3 local = box.transform.InverseTransformPoint(worldPosition) - box.center;
                Vector3 half = box.size * 0.5f;
                return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
            }
            // A round room (the deck cabin, SHIP-086): an upright capsule tested as the
            // cylinder it stands for, in its own space like the box - inside the radius
            // across, within the height up and down. The rounded ends are not cut off.
            if (volume is CapsuleCollider capsule && capsule.direction == 1)
            {
                Vector3 local = capsule.transform.InverseTransformPoint(worldPosition) - capsule.center;
                return Mathf.Abs(local.y) <= capsule.height * 0.5f && local.x * local.x + local.z * local.z <= capsule.radius * capsule.radius;
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
