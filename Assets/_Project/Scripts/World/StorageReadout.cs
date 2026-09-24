using SunkCost.Interaction;
using SunkCost.Look;
using UnityEngine;

namespace SunkCost.World
{
    // The readout on the ship's storage room (Dan, 16 September 2026: "show how
    // much money is in the box any time, and how much is needed for the quota
    // — on the box, something like 100/200"). Purely local: it sums the
    // replicated values of the loose items inside the room's volume on this
    // peer and reads the quota from the settings. The pay button at HQ is what
    // turns the box into money (QuotaBoard / WorldSceneFlow.ServerPay).
    //
    // Text is the readout as one string (the checks read it). The plate shows it
    // in the ship's screen style (ship audit SHIP-047, 23 September 2026): the box's
    // worth large, the quota bar - amber under, green once the cycle's quota is
    // met, counting what was handed over already (the visor's QUOTA line) - and the
    // balance small. A second plate inside the room, on the back wall facing the
    // door, shows the same while the crew drops the loot.
    public sealed class StorageReadout : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        public const string InsideName = "Storage Sign Inside";
        private const float InsideWidth = 1.4f, InsideHeight = 0.5f;

        private ShipParts ship;
        private TextMesh label;
        private WorldLoopSettings settings;
        private float nextRefresh;
        private string shownKey;
        private readonly Layout outside = new(), inside = new();

        public string Text { get; private set; } = string.Empty;
        public int ValueInside { get; private set; }

        private sealed class Layout
        {
            public TextMesh Title, Value, Quota, Balance;
            public Transform Fill;
            public Vector3 BarCentre;
            public float BarWidth, BarHeight, ValueSize;
            public Vector2 ValueBox;
        }

        private void Awake()
        {
            ship = GetComponentInParent<ShipParts>();
            Transform readout = ship != null ? ship.Find(ShipParts.StorageReadoutName) : null;
            label = readout != null ? readout.GetComponent<TextMesh>() : null;
            settings = WorldSceneFlow.Instance != null ? WorldSceneFlow.Instance.Settings : WorldLoopSettings.Resolve(null);
            EnsureDisplay();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            CrewDayState day = CrewDayState.Instance;
            ValueInside = day != null ? day.BoxValue : (ship != null ? SumInside(ship) : 0);
            string balance = day != null ? $"\nbalance ${day.Balance}" : string.Empty;
            string text = $"STORAGE\n${ValueInside} / ${settings.QuotaPerCycle}{balance}";
            string shown = text + "|" + (day != null ? day.CycleSales : 0); // the bar also counts what was handed over
            if (shown == shownKey) return;
            shownKey = shown;
            Text = text;
            if (label != null && label.text != text) label.text = text;
            Show(day);
        }

        // The layouts: on the outer plate round the logical label (hidden, its text
        // kept), and the inner copy. Built by ShipScreens; rebuilt here if missing.
        public void EnsureDisplay()
        {
            if (ship == null) ship = GetComponentInParent<ShipParts>();
            if (label == null)
            {
                Transform readout = ship != null ? ship.Find(ShipParts.StorageReadoutName) : null;
                label = readout != null ? readout.GetComponent<TextMesh>() : null;
            }
            if (label == null) return;
            Material textMaterial = ScreenStyle.TextMaterialOf(label);
            Renderer own = label.GetComponent<Renderer>();
            if (own != null) own.enabled = false;
            // The plate the label sits on (PropBuilder.SignPlate): 1.8 x 0.6 m, frame bars top and bottom.
            Transform plate = label.transform.parent;
            Vector2 plateSize = PlateSize(plate, new Vector2(1.8f, 0.6f));
            Build(outside, plate, plateSize.x - 0.2f, plateSize.y - 0.16f, 0.06f, textMaterial);
            Transform room = ship != null ? ship.Find(ShipParts.StorageVolumeName) : null;
            if (room == null) return;
            Transform insidePlate = ship.Find(InsideName); // anywhere on the ship: the hierarchy may group it after the build
            if (insidePlate == null)
            {
                insidePlate = new GameObject(InsideName).transform;
                insidePlate.SetParent(ship.transform, false);
                PlaceInside(insidePlate, room);
                ScreenStyle.Quad(insidePlate, "Plate", new Vector3(0f, 0f, 0f), InsideWidth, InsideHeight, ScreenStyle.Back);
                // Dark rails, like the outer plate's kit bars (SHIP-060).
                Color rail = Color.Lerp(ScreenStyle.Track, ScreenStyle.Dim, 0.35f);
                ScreenStyle.Quad(insidePlate, "Frame Top", new Vector3(0f, InsideHeight / 2f - 0.03f, 0.002f), InsideWidth, 0.035f, rail);
                ScreenStyle.Quad(insidePlate, "Frame Bottom", new Vector3(0f, -InsideHeight / 2f + 0.03f, 0.002f), InsideWidth, 0.035f, rail);
            }
            Build(inside, insidePlate, InsideWidth - 0.16f, InsideHeight - 0.12f, 0.006f, textMaterial);
        }

        // What the builder shows in the editor: an empty box against the quota (SHIP-064).
        public void ShowIdle()
        {
            settings ??= WorldLoopSettings.Resolve(null);
            Show(null);
        }

        private static Vector2 PlateSize(Transform plate, Vector2 fallback)
        {
            Transform p = plate != null ? plate.Find("Plate") : null;
            MeshFilter mf = p != null ? p.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return fallback;
            Vector3 s = mf.sharedMesh.bounds.size;
            return new Vector2(s.x, s.y);
        }

        // On the room's back wall, high, facing the doorway: found by a ray from the
        // room's middle toward the back (starboard, +X), clear of the wall by 3 cm.
        private void PlaceInside(Transform plate, Transform room)
        {
            BoxCollider box = room.GetComponent<BoxCollider>(); // the volume's box is offset from its (unmoved) transform
            Vector3 centre = ship.transform.InverseTransformPoint(box != null ? room.TransformPoint(box.center) : room.position);
            Vector3 from = ship.transform.TransformPoint(new Vector3(centre.x, 2.3f, centre.z));
            Vector3 back = ship.transform.right;
            float wallX = centre.x + 1.75f; // the room is 4.2 m wide; its walls about 0.35 m (fallback)
            Physics.SyncTransforms();
            PhysicsScene physics = ship.gameObject.scene.IsValid() ? ship.gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            if (physics.Raycast(from, back, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                wallX = ship.transform.InverseTransformPoint(hit.point).x;
            plate.localPosition = new Vector3(wallX - 0.03f, 2.3f, centre.z);
            plate.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up); // its face toward the doorway (-X)
        }

        // One readout: STORAGE and the quota on top, the box's worth large, the bar,
        // the balance under it. `w` x `h` is the clear face; `z` how far the face lies in front of the plate's origin.
        private static void Build(Layout l, Transform plate, float w, float h, float z, Material textMaterial)
        {
            float title = h * 0.2f, value = h * 0.8f, small = h * 0.16f, left = w / 2f;
            float top = h / 2f - title / 2f;
            l.Title = ScreenStyle.Line(plate, "Readout Title", new Vector3(left, top, z), title, ScreenStyle.Accent, TextAnchor.MiddleLeft, textMaterial);
            l.Quota = ScreenStyle.Line(plate, "Readout Quota", new Vector3(-left, top, z), title, ScreenStyle.Warn, TextAnchor.MiddleRight, textMaterial);
            float barY = -h / 2f + small + small * 0.35f;
            l.BarWidth = w;
            l.BarHeight = Mathf.Max(0.02f, small * 0.45f);
            l.BarCentre = new Vector3(0f, barY, z - 0.002f);
            ScreenStyle.Quad(plate, "Readout Bar", l.BarCentre, l.BarWidth, l.BarHeight, ScreenStyle.Track);
            l.Fill = ScreenStyle.Quad(plate, "Readout Bar Fill", l.BarCentre + new Vector3(0f, 0f, 0.001f), l.BarWidth, l.BarHeight, ScreenStyle.Warn).transform;
            l.Balance = ScreenStyle.Line(plate, "Readout Balance", new Vector3(-left, -h / 2f + small / 2f, z), small, ScreenStyle.Dim, TextAnchor.MiddleRight, textMaterial);
            float valueY = (top - title / 2f + barY + l.BarHeight) / 2f;
            l.ValueSize = value * 0.1f;
            l.ValueBox = new Vector2(w, (top - title / 2f) - (barY + l.BarHeight));
            l.Value = ScreenStyle.Line(plate, "Readout Value", new Vector3(left, valueY, z), value, ScreenStyle.Text, TextAnchor.MiddleLeft, textMaterial);
        }

        private void Show(CrewDayState day)
        {
            int quotaTotal = settings != null ? settings.QuotaPerCycle : 0;
            int had = (day != null ? day.CycleSales : 0) + ValueInside;
            bool met = quotaTotal > 0 && had >= quotaTotal;
            Color state = met ? ScreenStyle.Good : ScreenStyle.Warn;
            foreach (Layout l in new[] { outside, inside })
            {
                if (l.Value == null) continue;
                Set(l.Title, "STORAGE");
                Set(l.Quota, met ? $"QUOTA MET ${had} / ${quotaTotal}" : $"QUOTA ${had} / ${quotaTotal}");
                l.Quota.color = state;
                Set(l.Value, $"${ValueInside}");
                TextFit.Fit(l.Value, l.ValueBox, l.ValueSize);
                Set(l.Balance, day != null ? $"BALANCE ${day.Balance}" : string.Empty);
                ScreenStyle.SetBar(l.Fill, l.BarCentre + new Vector3(0f, 0f, 0.001f), l.BarWidth, l.BarHeight, quotaTotal > 0 ? (float)had / quotaTotal : 0f);
                ScreenStyle.Paint(l.Fill.GetComponent<Renderer>(), state);
            }
        }

        private static void Set(TextMesh mesh, string text)
        {
            if (mesh != null && mesh.text != text) mesh.text = text;
        }

        // The loose items (not in anyone's hands or slots) inside the room.
        public static int SumInside(ShipParts ship)
        {
            int sum = 0;
            foreach (CarryableItem item in CarryableItem.Spawned)
            {
                if (item == null || !item.CanGrabFromWorld) continue;
                if (item.gameObject.scene != ship.gameObject.scene) continue;
                if (ship.IsInStorageRoom(item.transform.position)) sum += item.Value;
            }
            return sum;
        }
    }
}
