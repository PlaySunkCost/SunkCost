using System;
using System.Collections.Generic;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Pure and asset checks for docs/VISOR_IMPLEMENTATION_PLAN.md section 6: the
    // visor's arithmetic, the coin prefabs' value ranges, the placements. No Play
    // Mode; the dive site validator covers the fixture in the scene.
    public static class VisorChecks
    {
        [MenuItem("Sunk Cost/Prototype/Run visor checks")]
        public static void RunFromMenu() => Debug.Log(RunOrThrow());

        public static string RunOrThrow()
        {
            var errors = new List<string>();

            // Bearings: +Z is 0, +X is 90, clockwise.
            Near(errors, PlayerVisorMath.BearingDeg(Vector3.zero, Vector3.forward), 0f, 1e-3f, "bearing of +Z");
            Near(errors, PlayerVisorMath.BearingDeg(Vector3.zero, Vector3.right), 90f, 1e-3f, "bearing of +X");
            Near(errors, PlayerVisorMath.BearingDeg(Vector3.zero, Vector3.back), 180f, 1e-3f, "bearing of -Z");
            Near(errors, PlayerVisorMath.BearingDeg(Vector3.zero, Vector3.left), 270f, 1e-3f, "bearing of -X");
            // Screen angle: ahead is 0, right is +, behind is ±180.
            Near(errors, PlayerVisorMath.ScreenAngleDeg(Vector3.zero, 0f, Vector3.forward * 5f), 0f, 1e-3f, "home ahead");
            Near(errors, PlayerVisorMath.ScreenAngleDeg(Vector3.zero, 0f, Vector3.right * 5f), 90f, 1e-3f, "home to the right");
            Near(errors, Mathf.Abs(PlayerVisorMath.ScreenAngleDeg(Vector3.zero, 90f, Vector3.left * 5f)), 180f, 1e-3f, "home behind when facing +X and it is at -X");
            // Compass strip: the heading sits at 0 px; ±45° at the edges of a 90° arc; outside is null.
            Near(errors, PlayerVisorMath.CompassOffsetPx(0f, 0f, 400f, 90f) ?? float.NaN, 0f, 1e-3f, "heading at the strip's centre");
            Near(errors, PlayerVisorMath.CompassOffsetPx(0f, 45f, 400f, 90f) ?? float.NaN, 200f, 1e-3f, "+45° at the right edge");
            Near(errors, PlayerVisorMath.CompassOffsetPx(350f, 10f, 400f, 90f) ?? float.NaN, 88.888f, 0.01f, "wrap-around across north");
            if (PlayerVisorMath.CompassOffsetPx(0f, 100f, 400f, 90f) != null) errors.Add("a bearing outside the arc should be off the strip.");
            // Bars.
            Near(errors, PlayerVisorMath.FillWidthPx(1f, 220f), 220f, 1e-3f, "full bar");
            Near(errors, PlayerVisorMath.FillWidthPx(0f, 220f), 0f, 1e-3f, "empty bar");
            Near(errors, PlayerVisorMath.FillWidthPx(0.001f, 220f), 1f, 1e-3f, "a sliver is still 1 px");
            Near(errors, PlayerVisorMath.FillWidthPx(2f, 220f), 220f, 1e-3f, "over-full clamps");

            // Projection and brackets with a throwaway camera (no scene state touched).
            var go = new GameObject("VisorCheckCamera", typeof(Camera));
            try
            {
                Camera camera = go.GetComponent<Camera>();
                camera.transform.position = Vector3.zero;
                camera.transform.rotation = Quaternion.identity;
                camera.fieldOfView = 60f;
                if (!PlayerVisorMath.TryProject(camera, Vector3.forward * 5f, out Vector2 centre)) errors.Add("a point ahead must project.");
                else if (Mathf.Abs(centre.x - Screen.width * 0.5f) > 2f || Mathf.Abs(centre.y - Screen.height * 0.5f) > 2f) errors.Add("a point straight ahead must land at the screen centre: " + centre);
                if (PlayerVisorMath.TryProject(camera, Vector3.back * 5f, out _)) errors.Add("a point behind the camera must not project.");
                if (!PlayerVisorMath.InView(camera, Vector3.forward * 5f)) errors.Add("a point ahead must be in view.");
                if (PlayerVisorMath.InView(camera, Vector3.back * 5f)) errors.Add("a point behind must not be in view.");
                if (!PlayerVisorMath.TryBracket(camera, new Bounds(Vector3.forward * 5f, Vector3.one * 0.4f), 4f, out Rect rect)) errors.Add("a box ahead must bracket.");
                else if (rect.width < 8f || rect.height < 8f || !rect.Contains(centre)) errors.Add("the bracket must surround the projected centre: " + rect);
                if (PlayerVisorMath.TryBracket(camera, new Bounds(Vector3.zero, Vector3.one * 2f), 4f, out _)) errors.Add("a box straddling the camera plane must not bracket.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            // Coins: prefabs present, ranges sane, the HQ balls valueless, placements distinct and named.
            foreach (DiveLootSetup.CoinType coin in DiveLootSetup.Coins)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(coin.PrefabPath);
                if (prefab == null) { errors.Add(coin.PrefabName + " prefab missing (run Apply dive loot setup)."); continue; }
                CarryableItem item = prefab.GetComponent<CarryableItem>();
                if (item == null || !item.HasValue || item.ValueMin != coin.ValueMin || item.ValueMax != coin.ValueMax) errors.Add(coin.PrefabName + " does not carry its value range " + coin.ValueMin + "-" + coin.ValueMax + ".");
                if (coin.ValueMin <= 0 || coin.ValueMin > coin.ValueMax) errors.Add(coin.PrefabName + " has a bad range.");
                BoxCollider box = prefab.GetComponent<BoxCollider>();
                if (box == null || prefab.GetComponent<SphereCollider>() != null) errors.Add(coin.PrefabName + " must have a box collider, not a sphere.");
                // The disc really is the designed size, and the box wraps exactly it.
                MeshFilter filter = prefab.GetComponent<MeshFilter>();
                Vector3 scale = prefab.transform.localScale;
                if (filter == null || filter.sharedMesh == null) errors.Add(coin.PrefabName + " has no mesh.");
                else
                {
                    Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, scale);
                    Near(errors, size.x, coin.DiameterMeters, 1e-3f, coin.PrefabName + " diameter");
                    Near(errors, size.y, coin.ThicknessMeters, 1e-3f, coin.PrefabName + " thickness");
                    if (box != null)
                    {
                        Vector3 boxSize = Vector3.Scale(box.size, scale);
                        Near(errors, boxSize.x, coin.DiameterMeters, 1e-3f, coin.PrefabName + " box width");
                        Near(errors, boxSize.y, coin.ThicknessMeters, 1e-3f, coin.PrefabName + " box height");
                    }
                }
                Rigidbody body = prefab.GetComponent<Rigidbody>();
                if (body == null || !Mathf.Approximately(body.mass, coin.MassKg)) errors.Add(coin.PrefabName + " mass is not " + coin.MassKg + " kg.");
                if (item != null && (item.Icon == null || item.Icon.name != coin.PrefabName)) errors.Add(coin.PrefabName + " must carry its own slot icon, not the ball's (run Apply dive loot setup).");
            }
            GameObject ball = AssetDatabase.LoadAssetAtPath<GameObject>(HQPrototypeBuilder.BallPrefabPath);
            if (ball != null && ball.GetComponent<CarryableItem>() != null && ball.GetComponent<CarryableItem>().HasValue) errors.Add("the basketball must carry no value (HQ game only).");
            var names = new HashSet<string>();
            foreach (DiveLootSetup.Placement placement in DiveLootSetup.Placements)
            {
                if (!names.Add(placement.Name)) errors.Add("duplicate placement name " + placement.Name);
                try { DiveLootSetup.Coin(placement.Coin); } catch (Exception) { errors.Add("placement " + placement.Name + " names an unknown coin " + placement.Coin); }
                if (placement.Along < 3f) errors.Add("placement " + placement.Name + " sits in the tube doorway.");
            }
            if (DiveLootSetup.Placements.Length < 5) errors.Add("fewer than five coins placed.");
            // A placement lies flat on the floor, along the bearing.
            Vector3 p = DiveLootSetup.PlacementPosition(new DiveLootSetup.Placement { Coin = "CoinSmall", Along = 5f, Across = 0f }, Vector3.zero, 0f, -45f);
            Near(errors, p.x, 5f, 1e-3f, "placement along +X at bearing 0");
            Near(errors, p.y, -45f + 0.01f + 0.02f, 1e-3f, "placement rests on the floor");
            Near(errors, p.z, 0f, 1e-3f, "placement has no across offset");

            // Tag text.
            if (PlayerHudUI.TagFor(null) != string.Empty) errors.Add("a null item has no tag.");

            // The mask: glass at the centre, frame at the corners, on the rim and on the
            // nose bridge; every readout on the glass, at 16:9, 21:9 and 720p.
            foreach ((int w, int h) in new[] { (1920, 1080), (2560, 1080), (1280, 720) })
            {
                string at = $" at {w}x{h}";
                float bottomRim = PlayerVisorMask.BottomRimPx(h), nose = PlayerVisorMask.NoseHeightPx(h), sideRim = PlayerVisorMask.SideRimPx(w);
                if (PlayerVisorMask.DistancePx(w / 2f, h / 2f, w, h) > -0.3f * h) errors.Add("the screen centre must be deep on the glass" + at);
                if (PlayerVisorMask.DistancePx(0f, 0f, w, h) <= 0f || PlayerVisorMask.DistancePx(w - 1f, h - 1f, w, h) <= 0f) errors.Add("the corners must be frame" + at);
                if (PlayerVisorMask.DistancePx(w / 2f, h - bottomRim * 0.5f, w, h) <= 0f) errors.Add("the bottom rim must be frame" + at);
                if (PlayerVisorMask.DistancePx(w / 2f, PlayerVisorMask.TopRimPx(h) * 0.5f, w, h) <= 0f) errors.Add("the top rim must be frame" + at);
                if (PlayerVisorMask.DistancePx(sideRim * 0.5f, h / 2f, w, h) <= 0f) errors.Add("the side rim must be frame" + at);
                if (PlayerVisorMask.DistancePx(sideRim + 0.1f * h, h / 2f, w, h) >= 0f) errors.Add("just inside the side rim must be glass" + at);
                if (PlayerVisorMask.DistancePx(w / 2f, h - bottomRim - nose * 0.5f, w, h) <= 0f) errors.Add("the nose bridge must be frame" + at);
                if (PlayerVisorMask.DistancePx(w / 2f, h - bottomRim - nose - 0.05f * h, w, h) >= 0f) errors.Add("above the nose bridge must be glass" + at);
                if (PlayerVisorMask.DistancePx(w / 2f - 0.3f * h, h - bottomRim - 0.02f * h, w, h) >= 0f) errors.Add("beside the nose bridge the bottom edge must be glass" + at);
                // The chamfers: the corner just inside the rims is frame, the same corner past the chamfer is glass.
                if (PlayerVisorMask.DistancePx(sideRim + 0.01f * h, PlayerVisorMask.TopRimPx(h) + 0.01f * h, w, h) <= 0f) errors.Add("the top-left corner inside the rims must be cut off (frame)" + at);
                if (PlayerVisorMask.DistancePx(sideRim + 0.12f * h, PlayerVisorMask.TopRimPx(h) + 0.12f * h, w, h) >= 0f) errors.Add("past the chamfer must be glass" + at);
                float s = h / 1080f;
                if (!PlayerVisorMask.OnGlass(PlayerVisorMask.VitalsRect(w, h), w, h, 2f)) errors.Add("the vitals block must lie on the glass" + at);
                if (!PlayerVisorMask.OnFrame(PlayerVisorMask.CompassRect(w, h, 380f * s), w, h, 2f)) errors.Add("the compass strip must lie in the housing, on the frame" + at);
                if (!PlayerVisorMask.OnGlass(PlayerVisorMask.HeadingRect(w, h), w, h, 2f)) errors.Add("the heading under the housing must lie on the glass" + at);
                if (!PlayerVisorMask.OnGlass(PlayerVisorMask.TopLeftLabelRect(w, h), w, h, 2f) || !PlayerVisorMask.OnGlass(PlayerVisorMask.TopRightLabelRect(w, h), w, h, 2f)) errors.Add("the corner labels must lie on the glass" + at);
                if (PlayerVisorMask.DistancePx(w / 2f, PlayerVisorMask.TopRimPx(h) + PlayerVisorMask.HousingDepthPx(h) * 0.5f, w, h) <= 0f) errors.Add("the compass housing must be frame" + at);
                if (PlayerVisorMask.DistancePx(w / 2f, PlayerVisorMask.TopRimPx(h) + PlayerVisorMask.HousingDepthPx(h) + 0.03f * h, w, h) >= 0f) errors.Add("under the compass housing must be glass" + at);
                if (PlayerVisorMask.DistancePx(w * 0.25f, PlayerVisorMask.TopRimPx(h) + 0.02f * h, w, h) >= 0f) errors.Add("beside the housing the top edge must be glass" + at);
                if (!PlayerVisorMask.OnGlass(PlayerVisorMask.SlotRowRect(w, h, 280f, 78f), w, h, 2f)) errors.Add("the slot row must lie on the glass" + at);
            }
            Texture2D baked = PlayerVisorMask.Bake(1920, 1080, 192, 108, readable: true);
            try
            {
                Color centre = baked.GetPixel(96, 54), corner = baked.GetPixel(0, 0), rim = baked.GetPixel(96, 2);
                Texture2D reticle = PlayerVisorMask.BakeReticle(64, readable: true);
                try
                {
                    if (reticle.GetPixel(32 + 27, 32).a < 0.5f) errors.Add("the reticle ring must be drawn at its right side.");
                    if (reticle.GetPixel(32, 32 + 27).a > 0.1f) errors.Add("the reticle ring must have a gap at the top/bottom.");
                }
                finally { UnityEngine.Object.DestroyImmediate(reticle); }
                if (centre.a > 0.02f) errors.Add("the baked mask must be clear at the centre: alpha " + centre.a);
                if (corner.a < 0.85f || corner.r > 0.2f) errors.Add("the baked mask must be dark frame at the corner: " + corner);
                if (rim.a < 0.85f) errors.Add("the baked mask must be frame on the bottom rim: " + rim);
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }

            if (errors.Count > 0) throw new InvalidOperationException("Visor checks failed:\n- " + string.Join("\n- ", errors));
            return "Visor checks passed: bearings, compass strip, bars, projection and brackets, the mask and its layout, coin prefabs and icons, placements.";
        }

        private static void Near(List<string> errors, float actual, float expected, float tolerance, string label)
        {
            if (float.IsNaN(actual) || Mathf.Abs(actual - expected) > tolerance) errors.Add($"{label}: {actual} (expected {expected}).");
        }
    }
}
