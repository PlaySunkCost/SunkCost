using SunkCost.World;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // The ship's deck cabin: the same glass elevator as DiveSite01's car (docs/DESIGN.md,
    // "the glass elevator"), just docked instead of descending — built from the same
    // RoundCabinGeometry, at the same dimensions, so a player recognizes it as one object.
    //
    // Visual shell only. No ElevatorController, no DeckCabin/CabinPhase component, no
    // NetworkObject: WorldSceneChecks.CheckShip refuses a ship prefab that carries one
    // ("the cabin and monitor become scene objects in their own cards"), and
    // docs/WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.6 lists DeckCabin.cs/CabinPhase.cs as
    // not yet built — that networked all-aboard/seal/suit-fade behaviour is Dan's. This only
    // produces ShipParts.RequiredChildren's named parts (DeckCabinVolume, DeckCabinDoorL,
    // DeckCabinDoorR, DeckCabinButton, DeckCabinPanel) for that future component to find.
    //
    // Doors are parked fully open and stay that way — there is no ElevatorDoor here to sweep
    // them — matching the stub's "open, parked to the sides" convention it replaces.
    public static class DeckCabinBuilder
    {
        // Matches DiveSiteSettings' own defaults (CarDiameterMeters/CarInteriorHeightMeters)
        // on purpose: this is the same elevator car, so it should read as the same size.
        // Not a reference to DiveSiteSettings itself — that would make ship-building code
        // depend on a dive-site asset for a purely visual match.
        private const float DiameterMeters = 5f;
        private const float InteriorHeightMeters = 3.5f;
        private const float CarFrameRadius = 0.06f;
        private const float CarWallInset = 0.15f;
        public static float InteriorRadiusMeters => DiameterMeters / 2f - CarWallInset;
        private const float CarDoorwayWidthMeters = 2f;
        private const float PostDoorwayOffsetDeg = 45f;
        private const float PanelDoorwayOffsetDeg = 75f;
        private const float PanelWidthMeters = 0.6f;
        private const float PanelHeightMeters = 0.4f;
        private const float PanelThicknessMeters = 0.08f;
        private const float PanelChestHeightMeters = 1.3f;
        private const float DoorThicknessMeters = 0.1f;
        private const int DoorLeafPanelCount = 3;
        public const float FloorThicknessMeters = 0.1f;
        private const float LabelCharacterSize = 0.05f;

        // Faces the bow (+Z, bearing 90 in this codebase's 0=+X/90=+Z convention) — the
        // direction the crew boards from and the ship departs toward, matching the stub's
        // "doors on the bow side".
        private const float DoorwayBearingDeg = 90f;

        public static GameObject Build(Transform parent, Vector3 localPosition, Material floor, Material frame, Material glass, Material panelAccent)
        {
            float carRadius = DiameterMeters / 2f;
            float interiorRadius = carRadius - CarWallInset;
            float panelAngleDeg = DoorwayBearingDeg + PanelDoorwayOffsetDeg;

            GameObject cabin = new(ShipParts.DeckCabinName);
            cabin.transform.SetParent(parent, false);
            cabin.transform.localPosition = localPosition;

            RoundCabinGeometry.CreateDisc(cabin.transform, "Cabin Floor", DiameterMeters, FloorThicknessMeters, floor, FloorThicknessMeters / 2f);
            RoundCabinGeometry.CreateFramePosts(cabin.transform, carRadius, InteriorHeightMeters, frame, DoorwayBearingDeg, PostDoorwayOffsetDeg, CarFrameRadius, "Frame Post");
            float doorwayHalfAngleDeg = RoundCabinGeometry.CreateShell(cabin.transform, carRadius, interiorRadius, InteriorHeightMeters, glass, DoorwayBearingDeg, panelAngleDeg, CarDoorwayWidthMeters, PanelWidthMeters, "Glass Shell", "Interior Walls");
            RoundCabinGeometry.CreateWallPanel(cabin.transform, ShipParts.DeckCabinButtonName, interiorRadius, panelAngleDeg, PanelWidthMeters, PanelHeightMeters, PanelThicknessMeters, PanelChestHeightMeters, panelAccent);
            RoundCabinGeometry.CreateDisc(cabin.transform, "Cabin Roof", DiameterMeters, FloorThicknessMeters, glass, InteriorHeightMeters - FloorThicknessMeters / 2f);

            Transform doorRight = RoundCabinGeometry.CreateDoorLeafPanels(cabin.transform, ShipParts.DeckCabinDoorRName, interiorRadius, InteriorHeightMeters, DoorwayBearingDeg, doorwayHalfAngleDeg, frame, rightSide: true, DoorLeafPanelCount, DoorThicknessMeters);
            Transform doorLeft = RoundCabinGeometry.CreateDoorLeafPanels(cabin.transform, ShipParts.DeckCabinDoorLName, interiorRadius, InteriorHeightMeters, DoorwayBearingDeg, doorwayHalfAngleDeg, frame, rightSide: false, DoorLeafPanelCount, DoorThicknessMeters);
            // Fully open, permanently: the same rotation ElevatorDoor would reach at rest,
            // set once here since nothing sweeps a static cabin's doors yet.
            doorRight.localRotation = Quaternion.Euler(0f, -doorwayHalfAngleDeg, 0f);
            doorLeft.localRotation = Quaternion.Euler(0f, doorwayHalfAngleDeg, 0f);

            CreateDoorCollider(cabin.transform, interiorRadius, doorwayHalfAngleDeg);

            GameObject volume = new(ShipParts.DeckCabinVolumeName, typeof(BoxCollider));
            volume.transform.SetParent(cabin.transform, false);
            volume.transform.localPosition = new Vector3(0f, InteriorHeightMeters / 2f, 0f);
            BoxCollider volumeCollider = volume.GetComponent<BoxCollider>();
            volumeCollider.isTrigger = true;
            volumeCollider.size = new Vector3(carRadius * 2f, InteriorHeightMeters, carRadius * 2f);

            CreatePanelLabel(cabin.transform, interiorRadius);

            return cabin;
        }

        // The status plate outside the doorway (WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.4:
        // "a TextMesh the panel writes to") — empty until Dan's DeckCabin writes a refusal to
        // it, same pattern as ShipMonitor's MonitorStatus label.
        // One box across the doorway, like the car's own (ElevatorCabinBuilder): a
        // player cannot walk into the housing while its doors are shut or moving —
        // with the car below there is nothing to stand in but the space the car
        // will arrive into (Dan, 16 September 2026: "that case can never happen").
        // Disabled here (the doors are parked open); WorldSceneFlow.PresentDeckCabin
        // switches it every frame.
        public static GameObject CreateDoorCollider(Transform cabin, float wallRadius, float doorwayHalfAngleDeg)
        {
            float doorwayCenterRad = DoorwayBearingDeg * Mathf.Deg2Rad;
            Vector3 doorwayDirection = new Vector3(Mathf.Cos(doorwayCenterRad), 0f, Mathf.Sin(doorwayCenterRad));
            float arcLength = wallRadius * (doorwayHalfAngleDeg * 2f * Mathf.Deg2Rad);
            GameObject colliderObject = new(ShipParts.DeckCabinDoorColliderName, typeof(BoxCollider));
            colliderObject.transform.SetParent(cabin, false);
            colliderObject.transform.localPosition = doorwayDirection * wallRadius + new Vector3(0f, InteriorHeightMeters / 2f, 0f);
            colliderObject.transform.localRotation = Quaternion.LookRotation(doorwayDirection, Vector3.up);
            BoxCollider box = colliderObject.GetComponent<BoxCollider>();
            box.size = new Vector3(arcLength, InteriorHeightMeters, DoorThicknessMeters);
            box.enabled = false;
            return colliderObject;
        }

        private static void CreatePanelLabel(Transform cabinTransform, float interiorRadius)
        {
            float angleRad = DoorwayBearingDeg * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

            GameObject go = new(ShipParts.DeckCabinPanelName, typeof(TextMesh));
            go.transform.SetParent(cabinTransform, false);
            go.transform.localPosition = direction * (interiorRadius + 0.3f) + new Vector3(0f, PanelChestHeightMeters + 0.5f, 0f);
            // A TextMesh reads correctly to a viewer looking along +Z; this plate sits on the
            // bow side of the doorway, read by someone approaching from further along the bow
            // (+Z) looking back toward -Z, so it needs the same 180-degree turn ShipStubBuilder's
            // Label(..., facingBow: true) already used for this exact part.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh mesh = go.GetComponent<TextMesh>();
            mesh.text = string.Empty;
            mesh.characterSize = LabelCharacterSize;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.9f, 0.95f, 1f);
        }
    }
}
