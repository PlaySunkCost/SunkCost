# The round elevator's look: numbers and rules (28 September 2026)

Dan's eight elevator models (a round glass car in a round glass shaft) are laid over the game's
working elevator objects. This page is the source that the code comments cite. It collects the
measured numbers, the placement rules, the water visuals and the transparency order, which were
first worked out in the session's readiness report and architecture notes.

Status: the look is **built and proposed until Dan reviews it**. The ride's rules and timing did not
change (docs/DESIGN.md, "The way down / The way up"). Networking notes are in
docs/NETWORK_CONTRACT.md ("Cabin doors" and the shaft tube's "no sync state" paragraph).

Conventions:
- **Bearing**: 0 = +X, 90 = +Z, counter-clockwise seen from above. A Unity yaw of +a lowers a
  bearing by a.
- **Prepared model to Unity**: Unity local = (-x, z, -y) of the Blender point. The model's doorway
  (Blender -Y) lands on Unity +Z (bearing 90).
- **Floor datum**: in both cabins the floor top is root + 0.10.

## 1. Decisions

| # | Decision |
|---|---|
| A1 | The dive car's stops do not move: the top root is at y 0 and the bottom root at y -45. The floors are made flush by moving the **deck cabin root to ship y -0.10** and the **tube foot's floor top to y -44.90**. The motion profile, the 17.33 s ride and `car.BottomPosition` are unchanged. |
| A2 | Tube foot "option A'": the foot's floor top is at -44.90 and the sand at -45.00. The threshold is a 10 cm kerb with a sill and a ramp collider (about 13 degrees) down to the sand. (Option B would lift the bottom stop 0.93 m and shorten the ride. It is not used.) |
| A3 | The top collar's bottom is at top root + 3.52, and the tube's top at 3.83 (0.31 into the collar). |
| A4 | There is one water truth: `CabinWater.LevelMeters = ElevatorMath.WaterLevelInCar(seaLevelY, carRootY, 3.5)`, as before. Every water visual is a pure function of that level, the car's `ElevatorState` and a shared clock. |
| A5 | Transparency is sorted per camera (section 5). |
| A6 | `DeckCabinShutterCollider`: a box across the housing's entrance on the shutters' line, solid exactly while the shutters are shut. The server treats the band between the car's doorway and the shutters as part of the cabin (section 4). |
| A7 | The car model's ring light copies the colour of the car's point light (`CarRingLight`), so the Elevator Ghost's green shows on the ring. |

## 2. The models and where they go

Every functional object, name, collider and trigger stays. The look adds children, hides only
renderers, and adds the colliders that the model's metal needs (the player's camera knows only
colliders). `Editor/Look/ElevatorLook.cs` places every model the same way under both cabins.
Running a builder again replaces a look child of the same name; it never stacks a second one.

| Model | Placed under → child | Local position / yaw | Notes |
|---|---|---|---|
| ElevatorCar (dive) | "Elevator" → "Car Look" | (0,0,0), yaw 90 | "Car Floor"/"Car Roof" renderers off (colliders kept; roof lossyScale 5) |
| ElevatorCar (deck) | "DeckCabin/DeckCabinCarGlass" → "Car Look" | (0,0,0), yaw 0 | shown only while the car is up |
| CabinDoor ×2 | door pivots → "Leaf Look" | (0, 0.10, 0), leaf centre at doorway ± 19.5° | pivots on the car axis; the sweep is ±23.578° |
| CarPanel | cabin → "Panel Look"; the button root is moved onto the model's cap | look at (cos b·2.375, 1.113, sin b·2.375), yaw 270 - b; button at (cos b·2.197, 1.300, sin b·2.197) facing the axis | b = 75 (dive), 165 (deck); body box centre (0, 0.339, 0.046), size (0.70, 0.678, 0.21); pipe r 0.040 up to y 3.30 |
| TubeSection ×n | "Shaft Tube/TubeSections" → "Tube Section i" | stacked from the foot's top (floor + 3.766) to the tube's top; a whole number of sections stretched to fit (10 on DiveSite01, pitch 4.4964) | yaw -((doorway + 45) mod 90); a section below y -9.5 goes on the DiveSiteDeep layer; the old 5 m ribs are hidden |
| TubeFoot | "Shaft Tube" → "Tube Foot" | floor top -44.90, DiveSiteDeep layer | plus the plinth, sill, ramp and guard colliders |
| TopCollar | "Shaft Tube" → "Top Collar" | y top root + 3.52 | no collider |
| CabinHousing | "DeckCabin" → "Cabin Housing" | cabin-local (0, 0.11, 0) = ship y 0.01 | a MeshCollider from its own mesh; a lid (r 3.20) carries the beacon |
| Shutters ×2 | "DeckCabinHousingDoorR/L" → "Shutter Look" | r 3.075, 0.05 thick, cabin-local y 0.15-3.20 (ship 0.05-3.10), span 24.76° | built in code in the housing's paint; a top track r 3.03-3.18 |
| GateLeaf ×2 | gate pivots → "Gate Leaf Look" | threshold local y 0.10 | pane radius R1 2.800 (r 2.689-2.967) |

### 2.1 Measured numbers (on the prepared meshes)

- Car model: -0.255 to 3.64 above the root, max radius 2.3986. The glass band is r 2.475-2.525 up to
  root + 3.50. The door leaves are r 2.409-2.463, y 0.10-3.40, 5.4 cm thick, bent to R 2.45.
- Flood outlets (the pipes' lowest points), Car Look local:
  1 (0.770, 2.924, 1.467), 2 (-0.738, 2.908, 1.487), 3 (1.846, 2.975, 0.002),
  4 (-1.846, 2.975, 0.064), 5 (0.928, 3.051, -1.470), 6 (-0.948, 3.050, -1.439).
  In the dive car's root frame (Car Look yaw 90), (x, y, z) becomes (z, y, -x):
  (1.467, 2.924, -0.770), (1.487, 2.908, 0.738), (0.002, 2.975, -1.846), (0.064, 2.975, 1.846),
  (-1.470, 3.051, -0.928), (-1.439, 3.050, 0.948).
- Drain grille ring: r 1.80-2.05 at y 0.10. Ring light: r 0.43-0.60 at y 3.26.
- Posts and slabs (bearing from the doorway, counter-clockwise; inner-face radius): 23.84 (2.082),
  -23.69 (2.086), -57.07 (2.203, slab half-width 0.22), 54.70 (2.203, slab 0.22), 98.55 (2.122),
  149.85 (2.043), -150.63 (2.048), -99.65 (2.124). Capsules r 0.095 (boxes 0.05 thick for the slabs),
  from the floor top 3.0 m up. Both cabins carry the same set. On the deck they sit under
  DeckCabinCarGlass, so they go with the car while it is away.
- Panel (Panel Look local): gauge bottom (-0.2466, 0.328, 0.090), gauge top (-0.2466, 0.5505, 0.090),
  screen face centre (0.0397, 0.443, 0.1402), screen normal (0.0812, 0, 0.9967).

### 2.2 The radial chain (metres from the axis)

| Layer | Radius |
|---|---|
| Car posts' inner faces | 2.04-2.21 |
| Car wall colliders (Interior Walls) | 2.275-2.425 |
| Car doorway collider | 2.30-2.40 |
| Car door leaves | 2.409-2.463 |
| Car glass band | 2.475-2.525 |
| Cabin water disc | 2.47 |
| Deck pedestal | 2.55 (top at ship y -0.03, so it cannot z-fight the car's floor at 0) |
| Tube water ring | 2.56-2.97 |
| Gate leaves | 2.689-2.967 |
| Tube section rings' inner face = TubeWalls colliders | 2.905 (colliders 2.905-3.055) |
| Tube glass | 2.975-3.025 |
| Deck shutters / shutter box | 3.05-3.10 / 3.025-3.125 |
| Ship well / hull deck cut / housing wall inner face | 3.70 / 3.74 / 3.739 |
| Deck ring walls (unseen) | 3.95, open ±16.93° at the doorway (x ±1.15) |
| Housing walkway edge | 5.007; props keep out of r 5.75 |

Heights above the car root: model bottom -0.255, floor top 0.10, leaves 0.10-3.40, roof underside
3.40, glass top 3.50, collar bottom 3.52 (top stop), clamps 3.64, tube top 3.83.

## 3. The water

- **The level** is the formula above (A4). Every peer computes it from the car's root y, which it
  places from the tick-anchored `ElevatorPhase`, so a late loader has the right level on its first
  frame. Going down, the water appears as the root passes sea level (-4.5) and the car is full 3.5 s
  later (root -8.0, in the 1 m/s band). Going up, it drains over the same band and is dry 1.5 s before
  the top.
- **One truth.** The car's water always stands where the sea outside stands while the car crosses the
  surface. So for any eye inside the car, "below the car's surface" is the same test as "below sea
  level". `PlayerSubmersion`, the air tank's refusal and the underwater grade keep reading sea level.
- **Flow** (`ElevatorMath.WaterFlowInCar`): Filling while Descending, Draining while Ascending,
  Still otherwise or at 0 / full (±0.02).
- **Visuals** (`CabinWaterVisuals`, on "Cabin Water FX"): six streams from the outlets while filling,
  a foam splash where each lands, bubbles from covered outlets and under a plunging stream, a drain
  swirl while draining, and ripples on the "Cabin Water" disc (r 2.47, shown only while
  0.02 < level < 3.48). Everything is recomputed every LateUpdate. The scroll and spin phases run on
  the synchronized network tick clock (`CabinWaterVisuals.SharedSeconds`), so the diver's screen, a
  spectator's and the ship TV draw the same frame.
- **Panel** (`CabinPanelDisplay`): the gauge marker and fill follow `Level01` (0 on the deck). The
  car screen reads "SURFACE / <FLOODING | DRAINING | state> / DEPTH n m WATER p%". The deck screen
  reads "DESCEND" and mirrors the status plate, whose words `WorldSceneFlow.PresentDeckCabin` hands
  over when they change. The panel's empties are named "Gauge Bottom", "Gauge Top", "Gauge Marker",
  "Gauge Fill" and "Screen Anchor".
- **The tube's surface** (`TubeWaterSurface`): the "WaterSurface" disc (r 2.97) is swapped for the
  "WaterSurface Ring" (r 2.56-2.97) while the car's span is at the surface
  (root - 0.30 < sea < root + 3.70), so there is one visible surface everywhere.
- **Execution order**: `WorldSceneFlow.Update` drives the car; `CabinWater.LateUpdate` (order 0)
  sets the level; the visuals, the panel, the tube ring and `CarRingLight` (order 100) read it;
  `ElevatorGhostLight` (order 0) colours the Cabin Light before the ring copies it; `ShipTV`
  (order 500) renders last.
- **Materials**: URP transparent, queue 3000, ZWrite off, alpha blended with Preserve Specular off
  (so highlights fade with alpha, and URP's validation leaves the blend as written). The car glass
  has smoothness 0.6 and the water surface 0.65; both glared white under the Cabin Light when they
  were smoother. `CabinWaterArt` rewrites the materials' settings on every build and rebuilds the ring
  mesh when its radii change.

## 4. The deck cabin's entrance

The housing's shutters stand 0.7 m outside the car's doorway (r 3.075 against 2.35). While the car
is up they move with the car's doors; while it is away they are shut and `DeckCabinShutterCollider`
is solid. The server (`WorldSceneFlow`) treats the grate between the doorway and the shutters as part
of the cabin (`ShipParts.InDeckCabinEntrance`: a capsule reaching the band from the doorway box out
to the shutter box, in the shutter box's space):
- the empty car going back down reopens for someone standing there, and after the grace period that
  player is put on the deck, like someone in the cabin;
- after the ride down's doors shut, anyone standing there (someone who stepped out after the seal
  cap) is put on the deck.

So nobody is closed in between the doors and the shutters while the car is below. While the car is
away, its panel body, posts and button colliders are switched off with its renderers, so the empty
housing holds nothing invisible to bump into or press.

## 5. Transparency order (per camera)

`CarWaterSorting` sets `sortingOrder` in `RenderPipelineManager.beginCameraRendering`, from whether
the camera being rendered (the player's, a spectator's, the TV's) stands inside the car. Lower draws
first; everything else stays at 0.

| Group | Renderers | Inside the car | Outside |
|---|---|---|---|
| TubeGlass | "Shaft Tube/TubeGlass/**", "Gate Leaf Right/Left/**" | -6 | -1 |
| TubeWater | "Shaft Tube/WaterSurface", "WaterSurface Ring" | -5 | -2 |
| CarGlass | "Glass Shell/**", "Elevator Door/Leaf */**", "Car Look/**" | -4 | -3 |
| CarWaterSurface | "Cabin Water" | -3 | -5 |
| CarWaterFX | "Cabin Water FX/**" | -2 | -4 |

The deck car has no water and no sorting hook.

## 6. Model preparation

The Blender steps for all eight models are `tools/blender/elevator_parts.py`, run through
`tools/blender/prepare_ship_part.py`: the TABLE entries, the entrance cut in the housing, the thin
leaf bend, the foot's ground cut and slot widening, the gate pane at R1 2.800 and the decimation
and bakes. Unity's side is `Editor/Look/ShipModelSetup.cs`: the parts list, the materials and the
look prefabs.

Still for Dan: record the Meshy plan the eight generations were made on, and that its terms allow
commercial use and keeping the files in the repo.
