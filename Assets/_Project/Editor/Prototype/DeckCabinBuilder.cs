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
        private const int DoorLeafPanelCount = 8; // smooth enough to read as round (Dan, 19 September 2026)
        public const float FloorThicknessMeters = 0.1f;
        // The tube's cap ring (ShipStubBuilder.DressCabin) is centred on the glass's top
        // edge: its underside, where the status plate hangs.
        public const float CapRingHeight = 0.36f;
        public const float CapRingBottom = InteriorHeightMeters - CapRingHeight / 2f;

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
            // No frame posts: the tube is clean, clear glass (Dan, 19 September 2026).
            // The button's band (its collider, 1.1 to 1.5 m, and a few centimetres) is the
            // only open arc in the wall behind it.
            float buttonBottom = PanelChestHeightMeters - 0.2f;
            float doorwayHalfAngleDeg = RoundCabinGeometry.CreateShell(cabin.transform, carRadius, interiorRadius, InteriorHeightMeters, glass, DoorwayBearingDeg, panelAngleDeg, CarDoorwayWidthMeters, PanelWidthMeters, buttonBottom - 0.05f, buttonBottom + 0.45f, "Glass Shell", "Interior Walls");
            // The button: red, its word on it, facing into the cabin (every button in the game, Dan, 19 September 2026).
            Vector3 panelOffset = new Vector3(Mathf.Cos(panelAngleDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(panelAngleDeg * Mathf.Deg2Rad)) * interiorRadius;
            SunkCost.Editor.Look.PropBuilder.PushButton(cabin, ShipParts.DeckCabinButtonName, panelOffset + new Vector3(0f, buttonBottom, 0f), Quaternion.LookRotation(-panelOffset.normalized, Vector3.up), "button.descend", PanelWidthMeters);
            // On a pillar with its plate, not floating on the glass (ship audit SHIP-049);
            // the elevator's redesign replaces it (Dan: "we will do a new one after").
            RoundCabinGeometry.CreateButtonMount(cabin.transform, interiorRadius, panelAngleDeg, PanelWidthMeters + 0.12f, FloorThicknessMeters, InteriorHeightMeters, buttonBottom + 0.56f, "DESCEND", SunkCost.Editor.Look.ShipModelSetup.HullMaterial());
            // The roof: a collider only. Its glass lies wholly inside the tube's opaque cap
            // ring (ShipStubBuilder.DressCabin), where nobody can ever see it.
            GameObject roof = RoundCabinGeometry.CreateDisc(cabin.transform, "Cabin Roof", DiameterMeters, FloorThicknessMeters, glass, InteriorHeightMeters - FloorThicknessMeters / 2f);
            roof.GetComponent<Renderer>().enabled = false;

            Transform doorRight = RoundCabinGeometry.CreateDoorLeafPanels(cabin.transform, ShipParts.DeckCabinDoorRName, interiorRadius, InteriorHeightMeters, DoorwayBearingDeg, doorwayHalfAngleDeg, glass, rightSide: true, DoorLeafPanelCount, DoorThicknessMeters); // glass leaves: no dark slabs beside the doorway
            Transform doorLeft = RoundCabinGeometry.CreateDoorLeafPanels(cabin.transform, ShipParts.DeckCabinDoorLName, interiorRadius, InteriorHeightMeters, DoorwayBearingDeg, doorwayHalfAngleDeg, glass, rightSide: false, DoorLeafPanelCount, DoorThicknessMeters);
            // Fully open, permanently: the same rotation ElevatorDoor would reach at rest,
            // set once here since nothing sweeps a static cabin's doors yet.
            doorRight.localRotation = Quaternion.Euler(0f, -doorwayHalfAngleDeg, 0f);
            doorLeft.localRotation = Quaternion.Euler(0f, doorwayHalfAngleDeg, 0f);
            // The tube's own leaves, just inside its glass: open with the car's while it
            // is up, shut while it is away (WorldSceneFlow.PresentDeckCabin), like the
            // gate at the seafloor. Parked open here.
            Transform housingRight = RoundCabinGeometry.CreateDoorLeafPanels(cabin.transform, ShipParts.DeckCabinHousingDoorRName, carRadius - 0.07f, InteriorHeightMeters, DoorwayBearingDeg, doorwayHalfAngleDeg, glass, rightSide: true, DoorLeafPanelCount, 0.05f);
            Transform housingLeft = RoundCabinGeometry.CreateDoorLeafPanels(cabin.transform, ShipParts.DeckCabinHousingDoorLName, carRadius - 0.07f, InteriorHeightMeters, DoorwayBearingDeg, doorwayHalfAngleDeg, glass, rightSide: false, DoorLeafPanelCount, 0.05f);
            housingRight.localRotation = Quaternion.Euler(0f, -doorwayHalfAngleDeg, 0f);
            housingLeft.localRotation = Quaternion.Euler(0f, doorwayHalfAngleDeg, 0f);

            CreateDoorCollider(cabin.transform, interiorRadius, doorwayHalfAngleDeg);

            // The inside, round like the cabin (ship audit SHIP-086: a square box reached
            // over the well at its corners and took in the grate): an upright capsule out
            // to the wall ring's inner face, the interior's height. ShipParts.Contains tests
            // it as that cylinder, in its own space. PhysX keeps a capsule at least as tall
            // as it is wide, so the trigger's physical shape reaches a little over and
            // under the cabin; nothing reads its trigger events, only Contains.
            GameObject volume = new(ShipParts.DeckCabinVolumeName, typeof(CapsuleCollider));
            volume.transform.SetParent(cabin.transform, false);
            volume.transform.localPosition = new Vector3(0f, InteriorHeightMeters / 2f, 0f);
            CapsuleCollider volumeCollider = volume.GetComponent<CapsuleCollider>();
            volumeCollider.isTrigger = true;
            volumeCollider.direction = 1; // upright
            volumeCollider.radius = interiorRadius - 0.075f; // the wall ring's inner face (its walls are 0.15 m)
            volumeCollider.height = InteriorHeightMeters;

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

            // On a plate over the doorway, read by someone approaching from the bow (+Z)
            // looking back toward -Z: the plate's +Z faces them (no floating text, Dan,
            // 19 September 2026). It hangs from the tube's cap ring (ShipStubBuilder.
            // DressCabin, its underside at CapRingBottom), in the doorway's line of glass.
            const float plateHeight = 0.5f;
            TextMesh mesh = SunkCost.Editor.Look.PropBuilder.SignPlate(cabinTransform.gameObject, "Cabin Status Sign", ShipParts.DeckCabinPanelName, direction * (DiameterMeters / 2f + 0.01f) + new Vector3(0f, CapRingBottom - plateHeight / 2f, 0f), Quaternion.identity, 2.0f, plateHeight, 0.16f, new Color(0.9f, 0.95f, 1f));
            mesh.text = string.Empty; // the plate's own size and colour; the flow writes the words
        }
    }
}
