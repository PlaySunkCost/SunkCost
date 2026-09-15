using SunkCost.Diving;
using SunkCost.Editor.Prototype;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Sites
{
    // The cabin geometry extracted from DiveSiteBuilder into a real prefab asset (Idan/Dan,
    // 15 September 2026 elevator rules). Regenerated every time DiveSiteBuilder runs, the
    // same way ShipStubBuilder treats Ship.prefab: it is settings-driven greybox, not
    // hand-tuned, so rebuilding on every "Create or Update Dive Site 01" keeps the saved
    // prefab from ever drifting out of sync with DiveSiteSettings (car diameter, interior
    // height) rather than needing a separate manual regenerate step.
    //
    // Baked with its doorway at local bearing 0 (facing local +X) regardless of where the
    // dive site's spawn point actually is; DiveSiteBuilder rotates the instantiated cabin
    // around Y afterward to face the real spawn direction, exactly as the door used to be
    // built pre-rotated. That keeps the prefab's own local geometry fixed (a real prefab,
    // not something CreateOrUpdate re-shapes per bearing) while preserving the old behavior
    // of always facing the way into the car from wherever the crew actually spawns.
    public static class ElevatorCabinBuilder
    {
        // PrefabUtility.SaveAsPrefabAsset renames the saved root to match the asset's file
        // name (the same reason Ship.prefab's root is named "Ship") — named Elevator.prefab,
        // not ElevatorCabin.prefab, so the instantiated root stays "Elevator": DiveSiteValidator
        // and ElevatorSelfCheckRunner both look up scene objects by that exact name.
        public const string PrefabPath = "Assets/_Project/Prefabs/World/Elevator.prefab";

        private const float CarFloorThickness = 0.1f;
        private const float CarFrameRadius = 0.06f;
        private const float CarWallInset = 0.15f;
        private const float CarDoorwayWidthMeters = 2f;
        private const float PostDoorwayOffsetDeg = 45f; // posts sit this far off the doorway centre, evenly spaced every 90 degrees
        private const float PanelWidthMeters = 0.6f;
        private const float PanelHeightMeters = 0.4f;
        private const float PanelThicknessMeters = 0.08f;
        private const float PanelChestHeightMeters = 1.3f;
        private const float PanelDoorwayOffsetDeg = 75f; // 60-90 degrees off the doorway centre: visible on entry, clear of a post at +45
        private const float DoorThicknessMeters = 0.1f;
        private const int DoorLeafPanelCount = 3; // small flat panels per leaf, enough to read as curved
        private const float BakedDoorwayBearingDeg = 0f; // local bearing the whole prefab is built at; DiveSiteBuilder rotates instances to face the real spawn direction

        public static GameObject EnsurePrefab(Material floor, Material frame, Material glass, Material panelAccent, DiveSiteSettings settings)
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Prefabs/World");

            float carRadius = settings.CarDiameterMeters / 2f;
            float interiorHeight = settings.CarInteriorHeightMeters;
            float interiorRadius = carRadius - CarWallInset;
            float panelAngleDeg = BakedDoorwayBearingDeg + PanelDoorwayOffsetDeg;

            GameObject root = new("Elevator");
            try
            {
                GameObject carFloor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                carFloor.name = "Car Floor";
                carFloor.transform.SetParent(root.transform, false);
                carFloor.transform.localPosition = new Vector3(0f, CarFloorThickness / 2f, 0f);
                carFloor.transform.localScale = new Vector3(settings.CarDiameterMeters, CarFloorThickness / 2f, settings.CarDiameterMeters);
                carFloor.GetComponent<Renderer>().sharedMaterial = floor;
                // CreatePrimitive(Cylinder) attaches a CapsuleCollider in this Unity version, not a
                // MeshCollider — and CapsuleCollider silently clamps its own height to at least
                // 2*radius, inflating a thin wide disc into a multi-metre sphere. Swap in a
                // MeshCollider built from the same primitive mesh so collision matches the disc.
                Object.DestroyImmediate(carFloor.GetComponent<Collider>());
                carFloor.AddComponent<MeshCollider>().sharedMesh = carFloor.GetComponent<MeshFilter>().sharedMesh;
                // The floor stays a full circle (no doorway gap) — it's walkable surface, not a wall.

                CreateElevatorFramePosts(root.transform, carRadius, interiorHeight, frame, BakedDoorwayBearingDeg);
                float doorwayHalfAngleDeg = CreateElevatorShell(root.transform, carRadius, interiorRadius, interiorHeight, glass, BakedDoorwayBearingDeg, panelAngleDeg);
                CreateElevatorControlPanel(root.transform, interiorRadius, panelAccent, panelAngleDeg);
                CreateElevatorRiderTrigger(root.transform, interiorRadius, interiorHeight);
                CreateElevatorRoof(root.transform, settings.CarDiameterMeters, interiorHeight, glass);

                // Fields left at their serialized defaults (topPosition/bottomPosition/travel/
                // doorSeal): those vary per dive site, so DiveSiteBuilder sets them on the
                // instance after PrefabUtility.InstantiatePrefab, the same way it always has.
                root.AddComponent<ElevatorController>();

                CreateElevatorDoor(root.transform, interiorRadius, interiorHeight, frame, BakedDoorwayBearingDeg, doorwayHalfAngleDeg);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateElevatorFramePosts(Transform parent, float carRadius, float interiorHeight, Material frameMaterial, float doorwayCenterAngleDeg)
        {
            const int postCount = 4;
            float postRadius = carRadius - CarFrameRadius;
            for (int i = 0; i < postCount; i++)
            {
                float angle = (doorwayCenterAngleDeg + PostDoorwayOffsetDeg + i * 360f / postCount) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * postRadius;
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = "Frame Post " + (i + 1);
                post.transform.SetParent(parent, false);
                post.transform.localPosition = offset + new Vector3(0f, interiorHeight / 2f, 0f);
                post.transform.localScale = new Vector3(CarFrameRadius * 2f, interiorHeight / 2f, CarFrameRadius * 2f);
                post.GetComponent<Renderer>().sharedMaterial = frameMaterial;
                // Default CapsuleCollider kept: unlike Car Floor, these posts are tall and
                // thin (height 3.5m vs radius 0.06m), so Unity's height>=2*radius clamp never
                // engages and the capsule matches the visible post exactly. Thin corner beams
                // are fine to feel solid, and they're derived from the doorway angle (always
                // PostDoorwayOffsetDeg away, every 90 degrees) so they can never end up
                // standing in the opening.
            }
        }

        // Visible glass shell and invisible wall colliders, generated from ONE loop over the
        // same ring of angles so the doorway gap in what you SEE and what BLOCKS you can never
        // drift apart. Segmented panes (rather than one smooth cylinder) are the tradeoff that
        // buys an exact, provably-matching opening.
        private static float CreateElevatorShell(Transform parent, float glassRadius, float wallRadius, float height, Material glass, float doorwayCenterAngleDeg, float panelAngleDeg)
        {
            const int segmentCount = 24;
            const float wallThickness = 0.15f;
            const float glassThickness = 0.05f;

            GameObject shellRoot = new("Glass Shell");
            shellRoot.transform.SetParent(parent, false);
            GameObject wallsRoot = new("Interior Walls");
            wallsRoot.transform.SetParent(parent, false);

            // Doorway width is measured at the glass (the actual opening a player walks
            // through); the same angular range is then skipped for the inset wall ring too.
            float doorwayHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(CarDoorwayWidthMeters / 2f / glassRadius)) * Mathf.Rad2Deg;
            // The panel sits on the wall ring's own radius, so without a matching gap here a
            // wall segment lands physically inside the panel and a raycast aimed at the panel
            // can hit the wall instead — a real, previously-shipped bug (a panel you can walk
            // up to and still can't reliably press E on). The glass stays solid at this
            // bearing; only the wall collider needs to step aside for the panel's own collider.
            float panelHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(PanelWidthMeters / 2f / wallRadius)) * Mathf.Rad2Deg + 3f;
            float glassSegmentArcLength = Mathf.PI * 2f * glassRadius / segmentCount * 1.05f;
            float wallSegmentArcLength = Mathf.PI * 2f * wallRadius / segmentCount * 1.05f;

            for (int i = 0; i < segmentCount; i++)
            {
                float angleDeg = i * 360f / segmentCount;
                bool inDoorway = Mathf.Abs(Mathf.DeltaAngle(angleDeg, doorwayCenterAngleDeg)) <= doorwayHalfAngleDeg;
                bool inPanel = Mathf.Abs(Mathf.DeltaAngle(angleDeg, panelAngleDeg)) <= panelHalfAngleDeg;
                if (inDoorway)
                    continue; // doorway gap: no glass pane, no wall collider here

                float angleRad = angleDeg * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

                GameObject glassPane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                glassPane.name = "Glass Pane " + (i + 1);
                glassPane.transform.SetParent(shellRoot.transform, false);
                glassPane.transform.localPosition = direction * glassRadius + new Vector3(0f, height / 2f, 0f);
                glassPane.transform.localRotation = rotation;
                glassPane.transform.localScale = new Vector3(glassSegmentArcLength, height, glassThickness);
                glassPane.GetComponent<Renderer>().sharedMaterial = glass;
                // CreatePrimitive(Cube) attaches a BoxCollider that would seal the car shut
                // (this was the original placeholder's bug: nobody could walk in). The shell
                // is a visual shroud only — collision comes from the floor and the wall ring.
                Object.DestroyImmediate(glassPane.GetComponent<Collider>());

                if (inPanel)
                    continue; // the control panel's own collider covers this arc instead

                GameObject wallSegment = new("Wall Segment " + (i + 1), typeof(BoxCollider));
                wallSegment.transform.SetParent(wallsRoot.transform, false);
                wallSegment.transform.localPosition = direction * wallRadius + new Vector3(0f, height / 2f, 0f);
                wallSegment.transform.localRotation = rotation;
                wallSegment.GetComponent<BoxCollider>().size = new Vector3(wallSegmentArcLength, height, wallThickness);
            }

            return doorwayHalfAngleDeg;
        }

        // Two curved leaves built from small flat panels, plus one collider spanning the
        // full doorway gap at the wall ring's own radius. Reuses doorwayHalfAngleDeg computed
        // above in CreateElevatorShell (same formula, same radius) so the doorway arc stays a
        // single source of truth between glass, walls and door.
        private static void CreateElevatorDoor(Transform parent, float wallRadius, float height, Material doorMaterial, float doorwayCenterAngleDeg, float doorwayHalfAngleDeg)
        {
            GameObject doorRoot = new("Elevator Door");
            doorRoot.transform.SetParent(parent, false);

            Transform leafRightPivot = CreateDoorLeaf(doorRoot.transform, "Leaf Right", wallRadius, height, doorwayCenterAngleDeg, doorwayHalfAngleDeg, doorMaterial, rightSide: true);
            Transform leafLeftPivot = CreateDoorLeaf(doorRoot.transform, "Leaf Left", wallRadius, height, doorwayCenterAngleDeg, doorwayHalfAngleDeg, doorMaterial, rightSide: false);

            float doorwayCenterRad = doorwayCenterAngleDeg * Mathf.Deg2Rad;
            Vector3 doorwayDirection = new Vector3(Mathf.Cos(doorwayCenterRad), 0f, Mathf.Sin(doorwayCenterRad));
            float fullDoorwayArcLength = wallRadius * (doorwayHalfAngleDeg * 2f * Mathf.Deg2Rad);

            GameObject colliderObject = new("Door Collider", typeof(BoxCollider));
            colliderObject.transform.SetParent(doorRoot.transform, false);
            colliderObject.transform.localPosition = doorwayDirection * wallRadius + new Vector3(0f, height / 2f, 0f);
            colliderObject.transform.localRotation = Quaternion.LookRotation(doorwayDirection, Vector3.up);
            BoxCollider doorBoxCollider = colliderObject.GetComponent<BoxCollider>();
            doorBoxCollider.size = new Vector3(fullDoorwayArcLength, height, DoorThicknessMeters);
            doorBoxCollider.enabled = false; // starts open, resting AtTop; ElevatorDoor takes over every frame at runtime

            ElevatorDoor door = doorRoot.AddComponent<ElevatorDoor>();
            SerializedObject serializedDoor = new(door);
            serializedDoor.FindProperty("leafLeftPivot").objectReferenceValue = leafLeftPivot;
            serializedDoor.FindProperty("leafRightPivot").objectReferenceValue = leafRightPivot;
            serializedDoor.FindProperty("doorCollider").objectReferenceValue = doorBoxCollider;
            serializedDoor.FindProperty("doorwayHalfAngleDeg").floatValue = doorwayHalfAngleDeg;
            serializedDoor.ApplyModifiedPropertiesWithoutUndo();
        }

        // A leaf is a pivot at the car's own local origin carrying DoorLeafPanelCount small
        // flat panels, built at the leaf's CLOSED angular span (from the doorway edge on this
        // side to the doorway centre). ElevatorDoor rotates the pivot at runtime to sweep this
        // whole rigid set further around the circumference as the door opens — see its
        // comment for the sign convention.
        private static Transform CreateDoorLeaf(Transform parent, string name, float radius, float height, float doorwayCenterAngleDeg, float doorwayHalfAngleDeg, Material material, bool rightSide)
        {
            GameObject pivot = new(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;

            float startAngleDeg = rightSide ? doorwayCenterAngleDeg : doorwayCenterAngleDeg - doorwayHalfAngleDeg;
            float endAngleDeg = rightSide ? doorwayCenterAngleDeg + doorwayHalfAngleDeg : doorwayCenterAngleDeg;
            float segmentArcLength = radius * (doorwayHalfAngleDeg * Mathf.Deg2Rad) / DoorLeafPanelCount * 1.05f;

            for (int i = 0; i < DoorLeafPanelCount; i++)
            {
                float segStartDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, (float)i / DoorLeafPanelCount);
                float segEndDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, (float)(i + 1) / DoorLeafPanelCount);
                float angleRad = (segStartDeg + segEndDeg) / 2f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

                GameObject panelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panelObject.name = name + " Panel " + (i + 1);
                panelObject.transform.SetParent(pivot.transform, false);
                panelObject.transform.localPosition = direction * radius + new Vector3(0f, height / 2f, 0f);
                panelObject.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
                panelObject.transform.localScale = new Vector3(segmentArcLength, height, DoorThicknessMeters);
                panelObject.GetComponent<Renderer>().sharedMaterial = material;
                // Blocking comes from the single Door Collider on the parent, not the leaves —
                // same reasoning as the glass panes: a per-panel BoxCollider here would seal
                // (or half-seal) the car regardless of door state.
                Object.DestroyImmediate(panelObject.GetComponent<Collider>());
            }

            return pivot.transform;
        }

        // Ceiling disc matching the floor's own diameter, with a collider so nothing enters
        // or exits from above. Sides stay fully transparent (the glass shell); only the roof
        // needs to block, since the Bell Eater is drawn to the elevator by design and a
        // reachable open top would make a docked car unsurvivable.
        private static void CreateElevatorRoof(Transform parent, float carDiameterMeters, float interiorHeight, Material glass)
        {
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            roof.name = "Car Roof";
            roof.transform.SetParent(parent, false);
            roof.transform.localPosition = new Vector3(0f, interiorHeight - CarFloorThickness / 2f, 0f);
            roof.transform.localScale = new Vector3(carDiameterMeters, CarFloorThickness / 2f, carDiameterMeters);
            roof.GetComponent<Renderer>().sharedMaterial = glass;
            // Same CapsuleCollider-clamp problem as Car Floor (see its comment): a thin wide
            // disc gets its default capsule inflated to a multi-metre sphere. Swap in a
            // MeshCollider built from the same disc mesh so collision matches what's visible.
            Object.DestroyImmediate(roof.GetComponent<Collider>());
            roof.AddComponent<MeshCollider>().sharedMesh = roof.GetComponent<MeshFilter>().sharedMesh;
        }

        private static void CreateElevatorControlPanel(Transform parent, float radius, Material accent, float panelAngleDeg)
        {
            float angleRad = panelAngleDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * radius;

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Control Panel";
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = offset + new Vector3(0f, PanelChestHeightMeters, 0f);
            panel.transform.localRotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
            panel.transform.localScale = new Vector3(PanelWidthMeters, PanelHeightMeters, PanelThicknessMeters);
            panel.GetComponent<Renderer>().sharedMaterial = accent;
            panel.AddComponent<ElevatorControlPanel>();
            // Cube's default BoxCollider is exactly what the interactor's raycast needs to
            // hit, and it visually stands out from the frame posts via the accent material.
        }

        private static void CreateElevatorRiderTrigger(Transform parent, float radius, float height)
        {
            // A box, not a capsule: a CapsuleCollider clamps its own height to at least
            // 2*radius, which for this car (radius comparable to height) silently balloons
            // the trigger well past the intended ceiling/floor. A box matches the requested
            // footprint exactly, no hidden geometry surprises.
            GameObject go = new("Rider Trigger", typeof(BoxCollider), typeof(ElevatorRiderTrigger));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            BoxCollider collider = go.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(radius * 2f, height, radius * 2f);
        }
    }
}
