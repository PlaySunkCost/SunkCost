using UnityEngine;

namespace SunkCost.Player
{
    // The visor's arithmetic (docs/VISOR_IMPLEMENTATION_PLAN.md), with no scene in
    // it so the editor checks can pin it: screen projection that refuses points
    // behind the camera, bracket rectangles from world bounds, the compass strip's
    // offsets and the bearing between two points. PlayerHudUI draws; this decides
    // where.
    public static class PlayerVisorMath
    {
        // Compass bearing of `to` from `from`, degrees clockwise from world +Z
        // (Unity's yaw convention: 0 = +Z, 90 = +X).
        public static float BearingDeg(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
        }

        // Where a bearing lands on a compass strip centred on the heading: pixels
        // from the strip's centre, or null when it is outside the visible arc.
        public static float? CompassOffsetPx(float headingDeg, float bearingDeg, float stripWidthPx, float visibleArcDeg)
        {
            float delta = Mathf.DeltaAngle(headingDeg, bearingDeg);
            if (Mathf.Abs(delta) > visibleArcDeg / 2f) return null;
            return delta / (visibleArcDeg / 2f) * (stripWidthPx / 2f);
        }

        // Screen point of a world point for a camera, in GUI space (y down); false
        // when the point is behind the camera. `GUI` space is Unity's screen space
        // with y flipped.
        public static bool TryProject(Camera camera, Vector3 world, out Vector2 gui)
        {
            gui = Vector2.zero;
            if (camera == null) return false;
            Vector3 v = camera.WorldToScreenPoint(world);
            if (v.z <= 0f) return false;
            gui = new Vector2(v.x, Screen.height - v.y);
            return true;
        }

        // The GUI-space rectangle around a world-space box's eight corners; false
        // when any corner is behind the camera (a bracket around a mirrored point
        // would land anywhere). Inflated by `padPx`.
        public static bool TryBracket(Camera camera, Bounds bounds, float padPx, out Rect rect)
        {
            rect = default;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                if (!TryProject(camera, corner, out Vector2 p)) return false;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            rect = Rect.MinMaxRect(minX - padPx, minY - padPx, maxX + padPx, maxY + padPx);
            return true;
        }

        // Whether a world point lies inside the camera's view (with a margin, in
        // normalised viewport units) and in front of it.
        public static bool InView(Camera camera, Vector3 world, float margin = 0.02f)
        {
            if (camera == null) return false;
            Vector3 v = camera.WorldToViewportPoint(world);
            return v.z > 0f && v.x > -margin && v.x < 1f + margin && v.y > -margin && v.y < 1f + margin;
        }

        // A bar's filled width in pixels: clamped, never a sliver under 1 px when
        // there is anything to show.
        public static float FillWidthPx(float fraction, float widthPx)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction <= 0f) return 0f;
            return Mathf.Clamp(Mathf.Round(fraction * widthPx), 1f, widthPx);
        }

        // Signed screen angle, degrees clockwise from straight up, of a world point
        // relative to the camera's forward on the horizontal plane: the HOME arrow.
        public static float ScreenAngleDeg(Vector3 eye, float headingDeg, Vector3 target)
        {
            return Mathf.DeltaAngle(headingDeg, BearingDeg(eye, target));
        }
    }
}
