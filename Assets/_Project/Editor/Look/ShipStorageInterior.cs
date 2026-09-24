using System.Collections.Generic;
using SunkCost.Editor.Prototype;
using SunkCost.World;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The storage room's inside (ship audit SHIP-058, 23 September 2026): the room the
    // loot goes into was an empty, unlit box. A tall rack along the starboard wall
    // facing the doorway, a low one along the stern wall, two drums by the bow wall,
    // the painted drop zone in the middle and a small warm lamp under the roof. The
    // middle and the way in from the doorway stay open (the whole StorageVolume can
    // be reached), and everything solid collides by its own box.
    //
    // Measured off the room the stub builds (its walls' and roof's colliders), so it
    // follows ShipStubBuilder's numbers; run after ShipDeckDressing.Build, whose
    // collider pass would take these colliders off the props.
    public static class ShipStorageInterior
    {
        public const string Name = "StorageInterior";

        // The room's walls and roof, as ShipStubBuilder.BuildStorageRoom makes them;
        // only for a ship that lacks the blocks.
        private const float WallFallback = 0.32f, RoofFallback = 0.6f;

        private const float Clear = 0.03f;       // off a wall's collider: the model's face is a little outside it
        private const float TallDepth = 0.65f;   // the starboard rack: a crate's depth on its shelves
        private const float LowDepth = 0.5f;
        private const float Post = 0.06f, Board = 0.04f;
        private const float TallTop = 1.9f;      // under the inside readout's plate

        public static GameObject Build(Transform shipRoot)
        {
            Transform parent = shipRoot.Find(ShipDeckDressing.LookName) ?? shipRoot;
            Transform old = parent.Find(Name);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            if (!Inside(shipRoot, out Rect floor, out float ceiling, out Vector2 door))
            {
                Debug.LogWarning("Storage interior: no storage room on the ship");
                return null;
            }
            GameObject room = new(Name);
            room.transform.SetParent(parent, false);
            Material steel = ShipKitMaterials.Steel(), bezel = ShipKitMaterials.Bezel();
            float y0 = FloorTop(shipRoot, floor);

            // The tall rack on the starboard wall, what the doorway looks at: two bays,
            // four shelves, a crate's depth. It stops under TallTop: the room's inside
            // readout hangs on the same wall above it (fix-ui, 2.05-2.55 m).
            float xs = floor.xMax - Clear - TallDepth / 2f;
            float tallLength = Mathf.Min(3.2f, floor.height - 0.4f);
            Rack(room, "Rack Starboard", new Vector3(xs, y0, floor.center.y), tallLength, TallDepth, TallTop, new[] { 0.1f, 0.72f, 1.34f, TallTop - Board }, 2, Quaternion.Euler(0f, -90f, 0f), steel); // its front to port, toward the doorway
            // What it holds: the deck's own crates and toolboxes, at a person's size (1x)
            // so they sit inside a shelf's depth.
            float z0 = floor.center.y - tallLength / 4f, z1 = floor.center.y + tallLength / 4f;
            Prop(room, "Crate", new Vector3(xs, y0 + 0.1f + Board, z0), 90f, 1f);
            Prop(room, "Crate", new Vector3(xs, y0 + 0.1f + Board, z1), 94f, 1f);
            Prop(room, "Toolbox", new Vector3(xs, y0 + 0.72f + Board, z0 - 0.2f), 84f, 1f);
            Prop(room, "Crate", new Vector3(xs, y0 + 0.72f + Board, z1 + 0.15f), 88f, 1f);
            Prop(room, "Toolbox", new Vector3(xs, y0 + 1.34f + Board, z1), 97f, 1f);

            // The low rack on the stern wall, clear of the doorway's side of the room.
            float zs = floor.yMin + Clear + LowDepth / 2f;
            float lowFrom = floor.xMin + 0.15f, lowTo = xs - TallDepth / 2f - 0.05f;
            Rack(room, "Rack Stern", new Vector3((lowFrom + lowTo) / 2f, y0, zs), lowTo - lowFrom, LowDepth, 1.0f, new[] { 0.1f, 0.96f }, 1, Quaternion.identity, steel);
            Prop(room, "Toolbox", new Vector3(lowTo - 0.5f, y0 + 0.96f + Board, zs), 8f, 1f);
            Prop(room, "Crate", new Vector3(lowFrom + 0.55f, y0 + 0.1f + Board, zs), 0f, 1f);

            // Two drums against the bow wall, deck gear at its deck size (Dan, 23
            // September 2026: 1.25x), the doorway's bow jamb well to one side of them.
            float zb = floor.yMax - Clear - 0.3f;
            Prop(room, "Barrel", new Vector3(floor.xMin + 1.05f, y0, zb), 0f, 1.25f);
            Prop(room, "Barrel", new Vector3(floor.xMin + 1.7f, y0, zb - 0.05f), 40f, 1.25f);

            // The drop zone, painted on the floor in the open middle: its words read by
            // someone coming in through the doorway (facing starboard, +x).
            float left = floor.xMin, right = xs - TallDepth / 2f, back = zs + LowDepth / 2f, front = zb - 0.35f;
            DropZone(room, new Vector3((left + right) / 2f + 0.1f, y0 + 0.005f, (back + front) / 2f), new Vector2(2.0f, 2.4f));

            // The lamp: a small warm fitting under the roof, no shadows (none of the
            // ship's lamps casts one yet, SHIP-016).
            Lamp(room, new Vector3(floor.center.x - 0.2f, ceiling, floor.center.y), bezel);

            if (door.y - door.x < 1.2f) Debug.LogWarning("Storage interior: the doorway is under 1.2 m");
            return room;
        }

        // The room's inside, in ship space, off the walls' and roof's colliders: the
        // floor's rectangle (x across, y along the ship's length), the roof's underside
        // and the doorway's span along z.
        private static bool Inside(Transform root, out Rect floor, out float ceiling, out Vector2 door)
        {
            float cx = ShipStubBuilder.StorageCentreX, cz = ShipStubBuilder.StorageCentreZ;
            float w = ShipStubBuilder.StorageWidth, d = ShipStubBuilder.StorageDepth, h = ShipStubBuilder.StorageHeight;
            float xMin = cx - w / 2f + WallFallback, xMax = cx + w / 2f - WallFallback;
            float zMin = cz - d / 2f + WallFallback, zMax = cz + d / 2f - WallFallback;
            ceiling = h - RoofFallback;
            float doorMin = cz - ShipStubBuilder.StorageDoor / 2f, doorMax = cz + ShipStubBuilder.StorageDoor / 2f;
            bool any = false;
            if (Box(root, "StorageWallStarboard", out Bounds b)) { xMax = b.min.x; any = true; }
            if (Box(root, "StorageWallStern", out b)) { zMin = b.max.z; any = true; }
            if (Box(root, "StorageWallBow", out b)) { zMax = b.min.z; any = true; }
            if (Box(root, "StorageRoof", out b)) { ceiling = b.min.y; any = true; }
            if (Box(root, "StorageWallPortStern", out b)) { xMin = b.max.x; doorMin = b.max.z; any = true; }
            if (Box(root, "StorageWallPortBow", out b)) doorMax = b.min.z;
            floor = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
            door = new Vector2(doorMin, doorMax);
            return any;
        }

        // A child block's box collider, in the ship's space (the room's blocks stand
        // straight under the ship's root).
        private static bool Box(Transform root, string name, out Bounds bounds)
        {
            bounds = default;
            Transform t = root.Find(name);
            if (t == null || !t.TryGetComponent(out BoxCollider box)) return false;
            bounds = new Bounds(t.localPosition + Vector3.Scale(box.center, t.localScale), Vector3.Scale(box.size, t.localScale));
            return true;
        }

        // Where the crew stands inside: the top of the room's floor collider (the
        // taped StorageArea), else the deck.
        private static float FloorTop(Transform root, Rect floor)
        {
            return Box(root, ShipParts.StorageAreaName, out Bounds b) ? b.max.y : 0f;
        }

        // A steel rack: `bays` bays along its length, posts at each bay's ends, a
        // board at each of `shelves` heights; every piece its own box collider, so an
        // item dropped on a shelf rests there and the gaps stay gaps.
        private static GameObject Rack(GameObject parent, string name, Vector3 foot, float length, float depth, float height, float[] shelves, int bays, Quaternion turn, Material steel)
        {
            GameObject rack = new(name);
            rack.transform.SetParent(parent.transform, false);
            rack.transform.localPosition = foot;
            rack.transform.localRotation = turn; // along its local x
            for (int i = 0; i <= bays; i++)
            {
                float x = -length / 2f + Post / 2f + i * (length - Post) / bays;
                foreach (float z in new[] { -depth / 2f + Post / 2f, depth / 2f - Post / 2f })
                    Piece(rack, "Post", new Vector3(x, 0f, z), new Vector3(Post, height, Post), steel);
            }
            foreach (float y in shelves)
            {
                Piece(rack, "Shelf", new Vector3(0f, y, 0f), new Vector3(length, Board, depth), steel);
                // A lip along the front edge: the look of a pressed steel shelf.
                Piece(rack, "Lip", new Vector3(0f, y - 0.05f, depth / 2f - 0.01f), new Vector3(length, 0.06f, 0.02f), steel);
            }
            // Cross bracing across each end frame, where a rack is weakest.
            float span = depth - 2f * Post, brace = Mathf.Sqrt(height * height + span * span);
            foreach (float x in new[] { -length / 2f + Post / 2f, length / 2f - Post / 2f })
            {
                GameObject piece = Piece(rack, "Brace", new Vector3(x, height / 2f, 0f), new Vector3(0.02f, brace, 0.025f), steel, centred: true);
                piece.transform.localRotation = Quaternion.Euler(Mathf.Atan2(span, height) * Mathf.Rad2Deg, 0f, 0f);
            }
            return rack;
        }

        private static GameObject Piece(GameObject parent, string name, Vector3 foot, Vector3 size, Material material, bool centred = false)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = foot;
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(size, centred ? size.y / 2f : 0f); // its base at `foot`, or its middle
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // as the models (ShipModelSetup)
            go.AddComponent<BoxCollider>(); // sized to the mesh
            return go;
        }

        // One of Dan's parts, standing at a place, solid by its own box (as the
        // dressing's props).
        private static void Prop(GameObject parent, string part, Vector3 foot, float yaw, float scale)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelSetup.PrefabPath(part));
            if (prefab == null) { Debug.LogWarning("Storage interior: no prefab for " + part); return; }
            var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            prop.name = part;
            prop.transform.localPosition = foot;
            prop.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            prop.transform.localScale = Vector3.one * scale;
            MeshFilter mf = prop.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            BoxCollider box = mf.gameObject.AddComponent<BoxCollider>();
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
        }

        // The painted mark: a flat quad, a hair over the floor, its words' top toward
        // +x (away from the doorway) so they read to someone walking in. size: x by z.
        private static void DropZone(GameObject parent, Vector3 centre, Vector2 size)
        {
            GameObject go = new("Drop Zone");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = centre;
            go.AddComponent<MeshFilter>().sharedMesh = DecalMesh(size);
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ShipKitMaterials.DropZone();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private const string DecalMeshPath = ShipKitMaterials.Folder + "/Meshes/StorageDropZone.asset";

        // A quad in the XZ plane facing up: u runs toward -z (the reader's right when
        // facing +x), v toward +x (up the page). Saved: a mesh made on the fly is lost
        // when the prefab is saved.
        private static UnityEngine.Mesh DecalMesh(Vector2 size)
        {
            float hx = size.x / 2f, hz = size.y / 2f;
            UnityEngine.Mesh mesh = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(DecalMeshPath);
            bool fresh = mesh == null;
            if (fresh)
            {
                System.IO.Directory.CreateDirectory(ShipKitMaterials.Folder + "/Meshes");
                mesh = new UnityEngine.Mesh { name = "StorageDropZone" };
            }
            mesh.Clear();
            mesh.SetVertices(new List<Vector3> { new(-hx, 0f, hz), new(-hx, 0f, -hz), new(hx, 0f, -hz), new(hx, 0f, hz) });
            mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(mesh, DecalMeshPath); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // A short fitting under the roof with a warm lens, and the light under it: warm
        // like the car's (Dan, 23 September 2026), small, no shadows, and never on the
        // seafloor's layer when both worlds are loaded (as the ship's sun).
        private static void Lamp(GameObject parent, Vector3 underRoof, Material housing)
        {
            GameObject lamp = new("Storage Lamp");
            lamp.transform.SetParent(parent.transform, false);
            lamp.transform.localPosition = underRoof;
            GameObject body = new("Housing");
            body.transform.SetParent(lamp.transform, false);
            body.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.7f, 0.08f, 0.24f));
            body.AddComponent<MeshRenderer>().sharedMaterial = housing;
            GameObject lens = new("Lens");
            lens.transform.SetParent(lamp.transform, false);
            lens.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            lens.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(new Vector3(0.6f, 0.02f, 0.16f));
            lens.AddComponent<MeshRenderer>().sharedMaterial = LookMaterials.LampWarm();
            foreach (MeshRenderer r in lamp.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Light light = new GameObject("Storage Light").AddComponent<Light>();
            light.transform.SetParent(lamp.transform, false);
            light.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.color = new Color(1f, 0.78f, 0.52f);
            light.range = 4.5f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            int deep = LayerMask.NameToLayer(SunkCost.Sites.DiveSiteBuilder.DeepLayerName);
            if (deep >= 0) light.cullingMask &= ~(1 << deep);
        }
    }
}
