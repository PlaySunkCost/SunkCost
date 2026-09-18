using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // The round glass-cabin geometry shared by the moving dive-site car
    // (SunkCost.Sites.ElevatorCabinBuilder) and the ship's static deck cabin
    // (DeckCabinBuilder) — the same object in the fiction (docs/DESIGN.md: "the glass
    // elevator"), docked versus descending. Pure geometry only: no gameplay components,
    // no fixed names beyond what each caller passes in. Extracted because this ring/
    // doorway math has shipped real bugs before (see CreateShell's comment) and two
    // independent copies would just mean two places to fix the next one.
    public static class RoundCabinGeometry
    {
        // A thin disc (floor or roof) with a MeshCollider, not the CapsuleCollider
        // CreatePrimitive(Cylinder) attaches by default — that clamps its own height to at
        // least 2*radius, inflating a thin wide disc into a multi-metre sphere that blocks
        // a player well outside the visible disc.
        public static GameObject CreateDisc(Transform parent, string name, float diameterMeters, float thicknessMeters, Material material, float localY)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, localY, 0f);
            disc.transform.localScale = new Vector3(diameterMeters, thicknessMeters / 2f, diameterMeters);
            disc.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.AddComponent<MeshCollider>().sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;
            return disc;
        }

        public static void CreateFramePosts(Transform parent, float carRadius, float interiorHeight, Material frameMaterial, float doorwayCenterAngleDeg, float postDoorwayOffsetDeg, float postRadiusMeters, string namePrefix)
        {
            const int postCount = 4;
            float postRadius = carRadius - postRadiusMeters;
            for (int i = 0; i < postCount; i++)
            {
                float angle = (doorwayCenterAngleDeg + postDoorwayOffsetDeg + i * 360f / postCount) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * postRadius;
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = namePrefix + " " + (i + 1);
                post.transform.SetParent(parent, false);
                post.transform.localPosition = offset + new Vector3(0f, interiorHeight / 2f, 0f);
                post.transform.localScale = new Vector3(postRadiusMeters * 2f, interiorHeight / 2f, postRadiusMeters * 2f);
                post.GetComponent<Renderer>().sharedMaterial = frameMaterial;
                // Default CapsuleCollider kept: these posts are tall and thin (radius well
                // under half the height), so Unity's height>=2*radius clamp never engages
                // and the capsule matches the visible post exactly.
            }
        }

        // Visible glass shell and invisible wall colliders, generated from ONE loop over the
        // same ring of angles so the doorway gap in what you SEE and what BLOCKS you can never
        // drift apart. Segmented panes (rather than one smooth cylinder) are the tradeoff that
        // buys an exact, provably-matching opening. Returns the doorway's half-angle in degrees.
        public static float CreateShell(Transform parent, float glassRadius, float wallRadius, float height, Material glass, float doorwayCenterAngleDeg, float panelAngleDeg, float doorwayWidthMeters, float panelWidthMeters, string shellRootName, string wallsRootName)
        {
            const int segmentCount = 24;
            const float wallThickness = 0.15f;
            const float glassThickness = 0.05f;

            GameObject shellRoot = new(shellRootName);
            shellRoot.transform.SetParent(parent, false);
            GameObject wallsRoot = new(wallsRootName);
            wallsRoot.transform.SetParent(parent, false);

            // Doorway width is measured at the glass (the actual opening a player walks
            // through); the same angular range is then skipped for the inset wall ring too.
            float doorwayHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(doorwayWidthMeters / 2f / glassRadius)) * Mathf.Rad2Deg;
            // The panel sits on the wall ring's own radius, so without a matching gap here a
            // wall segment lands physically inside the panel and a raycast aimed at the panel
            // can hit the wall instead — a real, previously-shipped bug (a panel you can walk
            // up to and still can't reliably press E on). The glass stays solid at this
            // bearing; only the wall collider needs to step aside for the panel's own collider.
            float panelHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(panelWidthMeters / 2f / wallRadius)) * Mathf.Rad2Deg + 3f;
            float wallSegmentArcLength = Mathf.PI * 2f * wallRadius / segmentCount * 1.05f;
            // The glass: one smooth curved band round the whole shell but the doorway
            // (Dan, 19 September 2026: "truly round" — no panes, no seams). A visual
            // shroud only; collision comes from the floor and the wall ring below.
            GameObject glassBand = new("Glass");
            glassBand.transform.SetParent(shellRoot.transform, false);
            glassBand.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(glassRadius, height, glassThickness, doorwayCenterAngleDeg + doorwayHalfAngleDeg, doorwayCenterAngleDeg - doorwayHalfAngleDeg + 360f, 96);
            glassBand.AddComponent<MeshRenderer>().sharedMaterial = glass;

            for (int i = 0; i < segmentCount; i++)
            {
                float angleDeg = i * 360f / segmentCount;
                bool inDoorway = Mathf.Abs(Mathf.DeltaAngle(angleDeg, doorwayCenterAngleDeg)) <= doorwayHalfAngleDeg;
                bool inPanel = Mathf.Abs(Mathf.DeltaAngle(angleDeg, panelAngleDeg)) <= panelHalfAngleDeg;
                if (inDoorway)
                    continue; // doorway gap: no wall collider here

                float angleRad = angleDeg * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

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

        // A leaf is a pivot at the car's own local origin carrying panelCount small flat
        // panels, built at the leaf's CLOSED angular span (from the doorway edge on this side
        // to the doorway centre). Geometry only: the caller decides how (or whether) the pivot
        // ever rotates from there — a moving car sweeps it live (ElevatorDoor), a static cabin
        // just sets its open rotation once and leaves it.
        public static Transform CreateDoorLeafPanels(Transform parent, string name, float radius, float height, float doorwayCenterAngleDeg, float doorwayHalfAngleDeg, Material material, bool rightSide, int panelCount, float thicknessMeters)
        {
            GameObject pivot = new(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;

            float startAngleDeg = rightSide ? doorwayCenterAngleDeg : doorwayCenterAngleDeg - doorwayHalfAngleDeg;
            float endAngleDeg = rightSide ? doorwayCenterAngleDeg + doorwayHalfAngleDeg : doorwayCenterAngleDeg;
            float segmentArcLength = radius * (doorwayHalfAngleDeg * Mathf.Deg2Rad) / panelCount * 1.05f;

            for (int i = 0; i < panelCount; i++)
            {
                float segStartDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, (float)i / panelCount);
                float segEndDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, (float)(i + 1) / panelCount);
                float angleRad = (segStartDeg + segEndDeg) / 2f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

                GameObject panelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panelObject.name = name + " Panel " + (i + 1);
                panelObject.transform.SetParent(pivot.transform, false);
                panelObject.transform.localPosition = direction * radius + new Vector3(0f, height / 2f, 0f);
                panelObject.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
                panelObject.transform.localScale = new Vector3(segmentArcLength, height, thicknessMeters);
                panelObject.GetComponent<Renderer>().sharedMaterial = material;
                // Blocking comes from a separate collider spanning the doorway (moving car)
                // or from nothing at all (a parked-open static cabin) — never the leaf panels.
                Object.DestroyImmediate(panelObject.GetComponent<Collider>());
            }

            return pivot.transform;
        }

        // The glass tube around the shaft (docs/SHAFT_TUBE_IMPLEMENTATION_PLAN.md
        // section 4): the same pane-and-wall ring as CreateShell, run from bottomY up to
        // topY with a doorway arc skipped only over the bottom doorwayHeight metres, and
        // a ring rib every ribSpacing metres. Renderers on the glass and the ribs,
        // colliders on the walls only. Returns the doorway's half-angle in degrees.
        public static float CreateTube(Transform parent, float glassRadius, float bottomY, float topY, Material glass, Material rib,
            float doorwayCenterAngleDeg, float doorwayWidthMeters, float doorwayHeight, float ribSpacing, string glassRootName, string wallsRootName, string ribPrefix)
        {
            const int segmentCount = 24;
            const float wallThickness = 0.15f;
            const float glassThickness = 0.05f;
            const float ribHeight = 0.15f;
            const float ribProud = 0.08f;

            GameObject glassRoot = new(glassRootName);
            glassRoot.transform.SetParent(parent, false);
            GameObject wallsRoot = new(wallsRootName);
            wallsRoot.transform.SetParent(parent, false);

            float height = topY - bottomY;
            float doorwayHalfAngleDeg = Mathf.Asin(Mathf.Clamp01(doorwayWidthMeters / 2f / glassRadius)) * Mathf.Rad2Deg;
            float arcLength = Mathf.PI * 2f * glassRadius / segmentCount * 1.05f;
            float wallRadius = glassRadius - 0.02f;
            // The glass: two smooth bands — the doorway's height with the doorway left
            // open, the rest of the tube all round (Dan, 19 September 2026: truly round).
            GameObject lower = new("Glass Lower");
            lower.transform.SetParent(glassRoot.transform, false);
            lower.transform.position = new Vector3(0f, bottomY, 0f);
            lower.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(glassRadius, Mathf.Min(doorwayHeight, height), glassThickness, doorwayCenterAngleDeg + doorwayHalfAngleDeg, doorwayCenterAngleDeg - doorwayHalfAngleDeg + 360f, 96);
            lower.AddComponent<MeshRenderer>().sharedMaterial = glass;
            if (height > doorwayHeight + 0.01f)
            {
                GameObject upper = new("Glass Upper");
                upper.transform.SetParent(glassRoot.transform, false);
                upper.transform.position = new Vector3(0f, bottomY + doorwayHeight, 0f);
                upper.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(glassRadius, height - doorwayHeight, glassThickness, 0f, 360f, 96);
                upper.AddComponent<MeshRenderer>().sharedMaterial = glass;
            }

            for (int i = 0; i < segmentCount; i++)
            {
                float angleDeg = i * 360f / segmentCount;
                bool inDoorway = Mathf.Abs(Mathf.DeltaAngle(angleDeg, doorwayCenterAngleDeg)) <= doorwayHalfAngleDeg;
                float angleRad = angleDeg * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

                // Above the doorway every segment runs full height; in the doorway arc the
                // pane and the wall start above the opening.
                float segmentBottom = inDoorway ? bottomY + doorwayHeight : bottomY;
                float segmentHeight = topY - segmentBottom;
                if (segmentHeight <= 0.01f) continue;

                GameObject wall = new("Tube Wall " + (i + 1), typeof(BoxCollider));
                wall.transform.SetParent(wallsRoot.transform, false);
                wall.transform.position = direction * wallRadius + new Vector3(0f, segmentBottom + segmentHeight / 2f, 0f);
                wall.transform.rotation = rotation;
                wall.GetComponent<BoxCollider>().size = new Vector3(arcLength, segmentHeight, wallThickness);
            }

            // Ribs are hollow rings just outside the glass: one round band each (a
            // cylinder primitive would be a solid plate across the tube). Visual only;
            // the walls block.
            int ribIndex = 0;
            for (float y = bottomY + ribSpacing; y < topY - 0.1f; y += ribSpacing)
            {
                GameObject ring = new(ribPrefix + "_" + (++ribIndex));
                ring.transform.SetParent(parent, false);
                ring.transform.position = new Vector3(0f, y - ribHeight / 2f, 0f);
                GameObject band = new("Rib Band");
                band.transform.SetParent(ring.transform, false);
                band.AddComponent<MeshFilter>().sharedMesh = SunkCost.Editor.Look.MeshKit.Band(glassRadius + ribProud / 2f, ribHeight, ribProud, 0f, 360f, 64);
                band.AddComponent<MeshRenderer>().sharedMaterial = rib;
            }

            return doorwayHalfAngleDeg;
        }

        // A flat wall-mounted panel (control button, status plate) at the given bearing on
        // the wall ring. Keeps the Cube's default BoxCollider — callers that need it
        // raycast-hittable (an interactor, a future button press) rely on that.
        public static GameObject CreateWallPanel(Transform parent, string name, float radius, float bearingDeg, float widthMeters, float heightMeters, float thicknessMeters, float chestHeightMeters, Material material)
        {
            float angleRad = bearingDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * radius;

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = offset + new Vector3(0f, chestHeightMeters, 0f);
            panel.transform.localRotation = Quaternion.LookRotation(offset.normalized, Vector3.up);
            panel.transform.localScale = new Vector3(widthMeters, heightMeters, thicknessMeters);
            panel.GetComponent<Renderer>().sharedMaterial = material;
            return panel;
        }
    }
}
