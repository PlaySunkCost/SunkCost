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
    //
    // The ring/shell/door geometry itself lives in RoundCabinGeometry, shared with the
    // ship's static deck cabin (DeckCabinBuilder) — the same object in the fiction, docked
    // versus descending (docs/DESIGN.md: "the glass elevator").
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
                RoundCabinGeometry.CreateDisc(root.transform, "Car Floor", settings.CarDiameterMeters, CarFloorThickness, floor, CarFloorThickness / 2f);
                RoundCabinGeometry.CreateFramePosts(root.transform, carRadius, interiorHeight, frame, BakedDoorwayBearingDeg, PostDoorwayOffsetDeg, CarFrameRadius, "Frame Post");
                float doorwayHalfAngleDeg = RoundCabinGeometry.CreateShell(root.transform, carRadius, interiorRadius, interiorHeight, glass, BakedDoorwayBearingDeg, panelAngleDeg, CarDoorwayWidthMeters, PanelWidthMeters, "Glass Shell", "Interior Walls");

                GameObject panel = RoundCabinGeometry.CreateWallPanel(root.transform, "Control Panel", interiorRadius, panelAngleDeg, PanelWidthMeters, PanelHeightMeters, PanelThicknessMeters, PanelChestHeightMeters, panelAccent);
                panel.AddComponent<ElevatorControlPanel>();
                // Cube's default BoxCollider is exactly what the interactor's raycast needs to
                // hit, and it visually stands out from the frame posts via the accent material.

                CreateElevatorRiderTrigger(root.transform, interiorRadius, interiorHeight);
                RoundCabinGeometry.CreateDisc(root.transform, "Car Roof", settings.CarDiameterMeters, CarFloorThickness, glass, interiorHeight - CarFloorThickness / 2f);

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

        // Two curved leaves (RoundCabinGeometry.CreateDoorLeafPanels), plus one collider
        // spanning the full doorway gap at the wall ring's own radius, and the ElevatorDoor
        // that sweeps both leaves live from ElevatorController's state.
        private static void CreateElevatorDoor(Transform parent, float wallRadius, float height, Material doorMaterial, float doorwayCenterAngleDeg, float doorwayHalfAngleDeg)
        {
            GameObject doorRoot = new("Elevator Door");
            doorRoot.transform.SetParent(parent, false);

            Transform leafRightPivot = RoundCabinGeometry.CreateDoorLeafPanels(doorRoot.transform, "Leaf Right", wallRadius, height, doorwayCenterAngleDeg, doorwayHalfAngleDeg, doorMaterial, rightSide: true, DoorLeafPanelCount, DoorThicknessMeters);
            Transform leafLeftPivot = RoundCabinGeometry.CreateDoorLeafPanels(doorRoot.transform, "Leaf Left", wallRadius, height, doorwayCenterAngleDeg, doorwayHalfAngleDeg, doorMaterial, rightSide: false, DoorLeafPanelCount, DoorThicknessMeters);

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
