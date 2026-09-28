using System;
using SunkCost.Editor.Look;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Sites
{
    // Dan's round glass shaft in the dive scene (28 September 2026), laid over the tube
    // DiveSiteBuilder already builds (glass, wall colliders, gate, water): the prepared
    // models (docs/ELEVATOR_LOOK.md) placed as look children of "Shaft Tube",
    // and the colliders their metal needs (the players' camera only knows colliders).
    // Look only: the car's stops (top anchor 0, bottom anchor -depth), its timing, the
    // gate and the water are unchanged (docs/ELEVATOR_LOOK.md A1-A3).
    //
    //   - "Tube Foot" on the seafloor: its floor top 0.10 above the sand (= the car's
    //     floor top at the bottom stop, flush), its doorway threshold a 10 cm kerb with
    //     a sill and a gentle ramp down to the sand (option A', docs/ELEVATOR_LOOK.md A2);
    //   - "TubeSections": the tube section stacked from the foot's top to the tube's top,
    //     a whole number of sections stretched to fit, its pipe running up one post;
    //   - "Top Collar": over the car's top stop (its glass top at root + 3.50, the collar
    //     from root + 3.52), the tube's top 0.31 into it;
    //   - the gate's glass leaves on the ShaftGate pivots, their threshold on the foot floor.
    public static class ShaftTubeLook
    {
        public const string FootName = "Tube Foot";
        public const string FootCollidersName = "Tube Foot Colliders";
        public const string SillName = "Foot Sill";
        public const string RampName = "Foot Ramp";
        public const string SectionsName = "TubeSections";
        public const string SectionPrefix = "Tube Section ";
        public const string CollarName = "Top Collar";
        public const string PlatformGuardName = "Tube Guard";

        // Heights (metres) against the car's stops: the car's floor top is root + 0.10 in
        // both cabins, so the foot's floor top sits 0.10 above the bottom anchor (the sand).
        public const float FootFloorAboveSand = ElevatorLook.FloorTopAboveRoot;
        public const float CollarAboveTopStop = 3.52f;     // the car's glass top is root + 3.50
        public const float TubeTopAboveCollar = 0.31f;     // the tube's top plugs 0.31 into the collar
        public const float TubeTopAboveTopStop = CollarAboveTopStop + TubeTopAboveCollar;
        public const float FootHeight = 3.766f;            // the prepared foot's top above its floor
        public const float SectionHeight = 4.4725f;        // the prepared section's height (ShipModelSetup)

        // The doorway's threshold: a sill from the car's glass to the foot's outer lip, and
        // a ramp from there down to the sand (about 13 degrees).
        private const float SillInnerRadius = 2.52f, SillOuterRadius = 3.51f, RampOuterRadius = 3.95f;
        private const float ThresholdWidth = 2.4f, RampThickness = 0.2f;

        // Foot colliders from the foot's own mesh: per 10-degree bin outside the doorway,
        // per height tier (floor-relative), a box from just outside the tube's wall
        // colliders (outer face 3.055) out to the metal's furthest point.
        private const float GuardInnerRadius = 3.06f, GuardMargin = 0.02f, BinDeg = 10f;
        private static readonly float[] FootTiers = { -FootFloorAboveSand, 0.35f, 0.87f, 1.30f, FootHeight };

        // The top section's ring and posts stand out to r 3.29 on the surface platform.
        private const float PlatformGuardOuterRadius = 3.31f;
        private const int PlatformGuardSegments = 24;

        public static void Build(Transform shaftTube, Vector3 axis, float topStopY, float bottomStopY, float doorwayBearingDeg, float doorwayHalfAngleDeg, float deepBelowY, int deepLayer)
        {
            float yaw = 90f - doorwayBearingDeg; // the prepared parts' doorway faces their +Z (bearing 90)
            float footFloorY = bottomStopY + FootFloorAboveSand;

            GameObject foot = Require(ElevatorLook.PlacePart("TubeFoot", shaftTube, FootName, new Vector3(axis.x, footFloorY, axis.z), yaw), "TubeFoot");
            SetLayerRecursively(foot, deepLayer);
            BuildFootColliders(shaftTube, foot, axis, footFloorY, doorwayBearingDeg, doorwayHalfAngleDeg, deepLayer);

            // The sections: the column from the foot's top to the tube's top.
            ElevatorLook.RemoveChildren(shaftTube, SectionsName);
            GameObject sections = new(SectionsName);
            sections.transform.SetParent(shaftTube, false);
            float bottom = footFloorY + FootHeight;
            float top = topStopY + TubeTopAboveTopStop;
            int count = Mathf.Max(1, Mathf.RoundToInt((top - bottom) / SectionHeight));
            float pitch = (top - bottom) / count;
            // The section's posts (prefab bearings 0/90/180/270) stand 45 degrees off the doorway.
            float sectionYaw = -Mathf.Repeat(doorwayBearingDeg + 45f, 90f);
            for (int i = 0; i < count; i++)
            {
                float y = bottom + i * pitch;
                GameObject section = Require(ElevatorLook.PlacePart("TubeSection", sections.transform, SectionPrefix + (i + 1), new Vector3(axis.x, y, axis.z), sectionYaw), "TubeSection");
                section.transform.localScale = new Vector3(1f, pitch / SectionHeight, 1f);
                // Under the sunlit water the Surface Light leaves it (the site's own lights take over).
                if (y < deepBelowY) SetLayerRecursively(section, deepLayer);
            }

            GameObject collar = Require(ElevatorLook.PlacePart("TopCollar", shaftTube, CollarName, new Vector3(axis.x, topStopY + CollarAboveTopStop, axis.z), yaw), "TopCollar");
            collar.layer = shaftTube.gameObject.layer;

            BuildPlatformGuard(shaftTube, axis, topStopY, top);
        }

        // The gate's glass leaves under the ShaftGate pivots (their threshold on the foot's
        // floor); the pivots keep their rotation, ShaftGate its sweep and Gate Collider.
        public static void PlaceGateLeaves(Transform leafRight, Transform leafLeft, float doorwayBearingDeg)
        {
            Require(ElevatorLook.PlaceGateLeaf(leafRight, doorwayBearingDeg, true, FootFloorAboveSand), "GateLeaf");
            Require(ElevatorLook.PlaceGateLeaf(leafLeft, doorwayBearingDeg, false, FootFloorAboveSand), "GateLeaf");
        }

        // Hollow rings every 5 m clash with the sections' own joints (docs/ELEVATOR_LOOK.md §2):
        // their objects stay (the validator counts them), their renderers go.
        public static void HideRibs(Transform shaftTube, string ribPrefix)
        {
            foreach (Transform child in shaftTube)
                if (child.name.StartsWith(ribPrefix))
                    foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        private static void BuildFootColliders(Transform shaftTube, GameObject foot, Vector3 axis, float footFloorY, float doorwayBearingDeg, float doorwayHalfAngleDeg, int deepLayer)
        {
            ElevatorLook.RemoveChildren(shaftTube, FootCollidersName);
            GameObject root = new(FootCollidersName);
            root.transform.SetParent(shaftTube, false);
            root.layer = deepLayer;

            float doorRad = doorwayBearingDeg * Mathf.Deg2Rad;
            Vector3 door = new(Mathf.Cos(doorRad), 0f, Mathf.Sin(doorRad));
            Vector3 flatAxis = new(axis.x, footFloorY, axis.z);

            // The sill: flush with the floors, from the car's glass out to the foot's lip.
            Box(root.transform, SillName, flatAxis + door * ((SillInnerRadius + SillOuterRadius) / 2f) + Vector3.down * (FootFloorAboveSand / 2f),
                Quaternion.LookRotation(door, Vector3.up), new Vector3(ThresholdWidth, FootFloorAboveSand, SillOuterRadius - SillInnerRadius), deepLayer);

            // The ramp: from the sill's lip down to the sand.
            float run = RampOuterRadius - SillOuterRadius;
            float slope = Mathf.Atan2(FootFloorAboveSand, run) * Mathf.Rad2Deg;
            Quaternion rampRotation = Quaternion.LookRotation(door, Vector3.up) * Quaternion.Euler(slope, 0f, 0f);
            Vector3 topMiddle = flatAxis + door * (SillOuterRadius + run / 2f) + Vector3.down * (FootFloorAboveSand / 2f);
            Box(root.transform, RampName, topMiddle - rampRotation * Vector3.up * (RampThickness / 2f), rampRotation,
                new Vector3(ThresholdWidth, RampThickness, Mathf.Sqrt(run * run + FootFloorAboveSand * FootFloorAboveSand)), deepLayer);

            // The plinth, the gate pockets and the frame: the foot's metal outside the tube's walls.
            float[,] reach = FootReach(foot, flatAxis, doorwayBearingDeg, out int bins);
            int made = 0;
            for (int b = 0; b < bins; b++)
            {
                // The bin's arc relative to the doorway, less the doorway itself.
                float from = -180f + b * BinDeg, to = from + BinDeg;
                if (from < doorwayHalfAngleDeg && to > -doorwayHalfAngleDeg)
                {
                    if (from >= -doorwayHalfAngleDeg && to <= doorwayHalfAngleDeg) continue;
                    if (from < -doorwayHalfAngleDeg) to = -doorwayHalfAngleDeg; else from = doorwayHalfAngleDeg;
                }
                for (int t = 0; t < FootTiers.Length - 1; t++)
                {
                    float outer = reach[t, b] + GuardMargin;
                    if (outer < GuardInnerRadius + 0.02f) continue; // nothing outside the walls here
                    float mid = (from + to) / 2f, half = (to - from) / 2f;
                    float bearing = (doorwayBearingDeg + mid) * Mathf.Deg2Rad;
                    Vector3 dir = new(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing));
                    float width = 2f * outer * Mathf.Sin(half * Mathf.Deg2Rad);
                    float height = FootTiers[t + 1] - FootTiers[t];
                    Box(root.transform, "Foot Guard " + (++made), flatAxis + dir * ((GuardInnerRadius + outer) / 2f) + Vector3.up * (FootTiers[t] + height / 2f),
                        Quaternion.LookRotation(dir, Vector3.up), new Vector3(width, height, outer - GuardInnerRadius), deepLayer);
                }
            }
        }

        // The foot's furthest metal from the tube's axis, per tier and per 10-degree bin
        // (from -180 to 180 relative to the doorway), measured on the placed mesh.
        private static float[,] FootReach(GameObject foot, Vector3 flatAxis, float doorwayBearingDeg, out int bins)
        {
            bins = Mathf.RoundToInt(360f / BinDeg);
            var reach = new float[FootTiers.Length - 1, bins];
            foreach (MeshFilter filter in foot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                foreach (Vector3 local in filter.sharedMesh.vertices)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(local) - flatAxis;
                    float r = new Vector2(p.x, p.z).magnitude;
                    float rel = Mathf.DeltaAngle(doorwayBearingDeg, Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg);
                    int b = Mathf.Clamp(Mathf.FloorToInt((rel + 180f) / BinDeg), 0, bins - 1);
                    for (int t = 0; t < FootTiers.Length - 1; t++)
                        if (p.y >= FootTiers[t] && p.y <= FootTiers[t + 1] && r > reach[t, b]) reach[t, b] = r;
                }
            }
            return reach;
        }

        // On the surface platform the top section's ring and posts stand 0.25 m outside the
        // tube's walls: a ring of boxes keeps walkers (and their camera) out of the metal.
        private static void BuildPlatformGuard(Transform shaftTube, Vector3 axis, float platformY, float topY)
        {
            ElevatorLook.RemoveChildren(shaftTube, PlatformGuardName);
            GameObject root = new(PlatformGuardName);
            root.transform.SetParent(shaftTube, false);
            float height = topY - platformY;
            float half = 180f / PlatformGuardSegments;
            for (int i = 0; i < PlatformGuardSegments; i++)
            {
                float bearing = i * 360f / PlatformGuardSegments * Mathf.Deg2Rad;
                Vector3 dir = new(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing));
                float width = 2f * PlatformGuardOuterRadius * Mathf.Sin(half * Mathf.Deg2Rad);
                Box(root.transform, "Tube Guard " + (i + 1), new Vector3(axis.x, platformY + height / 2f, axis.z) + dir * ((GuardInnerRadius + PlatformGuardOuterRadius) / 2f),
                    Quaternion.LookRotation(dir, Vector3.up), new Vector3(width, height, PlatformGuardOuterRadius - GuardInnerRadius), shaftTube.gameObject.layer);
            }
        }

        private static void Box(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 size, int layer)
        {
            GameObject go = new(name, typeof(BoxCollider));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.GetComponent<BoxCollider>().size = size;
            go.layer = layer;
        }

        private static GameObject Require(GameObject placed, string part)
        {
            if (placed == null) throw new InvalidOperationException("The elevator model " + part + " has no prefab (run ShipModelSetup.Apply(\"" + part + "\")).");
            return placed;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
