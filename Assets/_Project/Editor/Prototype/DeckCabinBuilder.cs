using SunkCost.Diving;
using SunkCost.Editor.Look;
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
    // ("the cabin and monitor become scene objects in their own cards"). This only
    // produces ShipParts' named parts (DeckCabinVolume, DeckCabinDoorL/R, the housing's
    // shutters DeckCabinHousingDoorL/R, DeckCabinButton, DeckCabinPanel, the doorway and
    // shutter colliders) that WorldSceneFlow.PresentDeckCabin drives from the ride state.
    //
    // Dan's round elevator (28 September 2026): the look is the eight prepared models,
    // placed by ElevatorLook exactly as on the dive car so the swap between the two shows
    // the same car in the same place. The car (its model, its glass, its panel) stands
    // under DeckCabinCarGlass, shown while the car is up; the round housing round it
    // (ShipStubBuilder.DressCabin) is the ship's, always there. The cabin root sits
    // FloorThicknessMeters under the deck, so the car's floor is flush with the deck.
    public static class DeckCabinBuilder
    {
        // Matches DiveSiteSettings' own defaults (CarDiameterMeters/CarInteriorHeightMeters)
        // on purpose: this is the same elevator car, so it should read as the same size.
        // Not a reference to DiveSiteSettings itself — that would make ship-building code
        // depend on a dive-site asset for a purely visual match.
        private const float DiameterMeters = 5f;
        private const float InteriorHeightMeters = 3.5f;
        private const float CarWallInset = 0.15f;
        public static float InteriorRadiusMeters => DiameterMeters / 2f - CarWallInset;
        private const float CarDoorwayWidthMeters = 2f;
        private const float PanelDoorwayOffsetDeg = 75f;
        private const float PanelWidthMeters = 0.6f;
        private const float PanelChestHeightMeters = 1.3f;
        private const float DoorThicknessMeters = 0.1f;
        public const float FloorThicknessMeters = 0.1f;

        // The housing's shutters (ELEVATOR_MODELS D3): curved plates on the shutter line,
        // hung from a top track, their bottom edge just over the grate's slats (ship y
        // 0.05, the slats' tops are 0.03) and their top under the track.
        public const float ShutterRadius = ElevatorLook.ShutterRadius;   // 3.075
        public const float ShutterBottomY = 0.15f;                       // cabin-local: ship y 0.05
        public const float ShutterTopY = 3.20f;                          // cabin-local: ship y 3.10
        private const float ShutterColliderWidth = 2.48f;                // across the housing's entrance (±22.5° at the shutters)
        private const float ShutterColliderThickness = 0.10f;

        // The status plate on the housing's header over the entrance, read from the bow:
        // its back against the header's face (ELEVATOR_MODELS §7, cabin-local).
        private static readonly Vector3 StatusPlateLocal = new(0f, 3.49f, 4.185f);

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

            // The floor the physics knows; the car model's own floor cap is what is seen.
            GameObject cabinFloor = RoundCabinGeometry.CreateDisc(cabin.transform, "Cabin Floor", DiameterMeters, FloorThicknessMeters, floor, FloorThicknessMeters / 2f);
            // The button's band (its collider, 1.1 to 1.5 m, and a few centimetres) is the
            // only open arc in the wall behind it.
            float buttonBottom = PanelChestHeightMeters - 0.2f;
            // The band wears the car's own glass, as on the dive car (the review's F5).
            float doorwayHalfAngleDeg = RoundCabinGeometry.CreateShell(cabin.transform, carRadius, interiorRadius, InteriorHeightMeters, ElevatorLook.CarGlass(), DoorwayBearingDeg, panelAngleDeg, CarDoorwayWidthMeters, PanelWidthMeters, buttonBottom - 0.05f, buttonBottom + 0.45f, "Glass Shell", "Interior Walls");
            // The button: red, its word on it, facing into the cabin (every button in the game, Dan, 19 September 2026).
            Vector3 panelOffset = new Vector3(Mathf.Cos(panelAngleDeg * Mathf.Deg2Rad), 0f, Mathf.Sin(panelAngleDeg * Mathf.Deg2Rad)) * interiorRadius;
            PropBuilder.PushButton(cabin, ShipParts.DeckCabinButtonName, panelOffset + new Vector3(0f, buttonBottom, 0f), Quaternion.LookRotation(-panelOffset.normalized, Vector3.up), "button.descend", PanelWidthMeters);
            // The roof: a collider only, under the housing's lid.
            GameObject roof = RoundCabinGeometry.CreateDisc(cabin.transform, "Cabin Roof", DiameterMeters, FloorThicknessMeters, glass, InteriorHeightMeters - FloorThicknessMeters / 2f);
            roof.GetComponent<Renderer>().enabled = false;

            // The car: its glass (the same band as the dive car's Glass Shell, at its
            // radius), its model and its panel, all under DeckCabinCarGlass, which
            // WorldSceneFlow shows only while the car is up. The always-on Glass Shell
            // stays for its name and shape but is not drawn: the housing is the ship's
            // wall round the well now.
            Transform shell = cabin.transform.Find("Glass Shell");
            GameObject carGlass = Object.Instantiate(shell.gameObject, cabin.transform);
            carGlass.name = ShipParts.DeckCabinCarGlassName;
            carGlass.transform.localPosition = shell.localPosition;
            carGlass.transform.localRotation = shell.localRotation;
            carGlass.transform.localScale = Vector3.one;
            foreach (Collider c in carGlass.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Renderer r in shell.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            GameObject carLook = ElevatorLook.PlaceCar(carGlass.transform, DoorwayBearingDeg, null);
            if (carLook != null) cabinFloor.GetComponent<Renderer>().enabled = false; // the model's floor cap would fight it
            Transform button = cabin.transform.Find(ShipParts.DeckCabinButtonName);
            GameObject panelLook = ElevatorLook.PlacePanel(carGlass.transform, button, panelAngleDeg);
            if (panelLook == null)
                RoundCabinGeometry.CreateButtonMount(cabin.transform, interiorRadius, panelAngleDeg, PanelWidthMeters + 0.12f, FloorThicknessMeters, InteriorHeightMeters, buttonBottom + 0.56f, "DESCEND", ShipKitMaterials.Bezel());

            // The car's door leaves: the model's leaf on each pivot at the car's axis,
            // parked fully open (WorldSceneFlow reads the half-angle off the right one
            // once, then sweeps both from the ride state).
            Transform doorRight = Pivot(cabin.transform, ShipParts.DeckCabinDoorRName);
            Transform doorLeft = Pivot(cabin.transform, ShipParts.DeckCabinDoorLName);
            ElevatorLook.PlaceDoorLeaf(doorRight, DoorwayBearingDeg, right: true);
            ElevatorLook.PlaceDoorLeaf(doorLeft, DoorwayBearingDeg, right: false);
            doorRight.localRotation = Quaternion.Euler(0f, -doorwayHalfAngleDeg, 0f);
            doorLeft.localRotation = Quaternion.Euler(0f, doorwayHalfAngleDeg, 0f);

            // The housing's shutters: open with the car's doors while it is up, shut
            // while it is away (WorldSceneFlow.PresentDeckCabin), like the gate at the
            // seafloor. Curved plates in the housing's paint on the shutter line, built
            // at their shut span, parked open here.
            Transform housingRight = Pivot(cabin.transform, ShipParts.DeckCabinHousingDoorRName);
            Transform housingLeft = Pivot(cabin.transform, ShipParts.DeckCabinHousingDoorLName);
            ElevatorLook.BuildShutterLeaf(housingRight, DoorwayBearingDeg, right: true, ShutterRadius, ShutterBottomY, ShutterTopY);
            ElevatorLook.BuildShutterLeaf(housingLeft, DoorwayBearingDeg, right: false, ShutterRadius, ShutterBottomY, ShutterTopY);
            housingRight.localRotation = Quaternion.Euler(0f, -doorwayHalfAngleDeg, 0f);
            housingLeft.localRotation = Quaternion.Euler(0f, doorwayHalfAngleDeg, 0f);

            CreateDoorCollider(cabin.transform, interiorRadius, doorwayHalfAngleDeg);
            CreateShutterCollider(cabin.transform);
            // The model's posts and glass slabs stand inside the wall ring: the camera
            // only knows colliders (the same set as the dive car's, from one helper).
            // Under DeckCabinCarGlass (at the cabin's own pose), so they go with the car
            // while it is away (WorldSceneFlow.PresentDeckCabin switches its colliders).
            ElevatorLook.AddPostColliders(carGlass.transform, DoorwayBearingDeg);

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

            TextMesh status = CreatePanelLabel(cabin.transform);
            // The panel's screen carries the plate's words (who is missing, the countdown).
            if (panelLook != null) ShaftTubeSetup.AddPanelDisplay(panelLook, CabinPanelDisplay.Mode.Deck, null, status);

            return cabin;
        }

        private static Transform Pivot(Transform cabin, string name)
        {
            GameObject pivot = new(name);
            pivot.transform.SetParent(cabin, false);
            return pivot.transform;
        }

        // One box across the doorway, like the car's own (ElevatorCabinBuilder): a
        // player cannot walk into the car while its doors are shut or moving —
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

        // The shut shutters' box across the housing's entrance, on their line (INTERFACES
        // A6): without it a player walked 0.7 m into the drawn plates. Disabled here (the
        // shutters are parked open); WorldSceneFlow.PresentDeckCabin switches it with them.
        public static GameObject CreateShutterCollider(Transform cabin)
        {
            float rad = DoorwayBearingDeg * Mathf.Deg2Rad;
            Vector3 direction = new(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            float height = ShutterTopY - ShutterBottomY;
            GameObject colliderObject = new(ShipParts.DeckCabinShutterColliderName, typeof(BoxCollider));
            colliderObject.transform.SetParent(cabin, false);
            colliderObject.transform.localPosition = direction * ShutterRadius + new Vector3(0f, ShutterBottomY + height / 2f, 0f);
            colliderObject.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
            BoxCollider box = colliderObject.GetComponent<BoxCollider>();
            box.size = new Vector3(ShutterColliderWidth, height, ShutterColliderThickness);
            box.enabled = false;
            return colliderObject;
        }

        // The status plate on the housing's header over the entrance
        // (WORLD_LOOP_IMPLEMENTATION_PLAN.md section 4.4: "a TextMesh the panel writes
        // to"), read by someone approaching from the bow (+Z) looking back toward -Z: the
        // plate's +Z faces them (no floating text, Dan, 19 September 2026).
        private static TextMesh CreatePanelLabel(Transform cabinTransform)
        {
            TextMesh mesh = PropBuilder.SignPlate(cabinTransform.gameObject, "Cabin Status Sign", ShipParts.DeckCabinPanelName, StatusPlateLocal, Quaternion.identity, 2.0f, 0.5f, 0.16f, new Color(0.9f, 0.95f, 1f));
            mesh.text = string.Empty; // the plate's own size and colour; the flow writes the words
            return mesh;
        }
    }
}
