using System.Text;
using SunkCost.Audio;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Maps the ambience over the walkable platform (Dan, 8 October 2026, BRIEF B.7):
    // a 2 m grid at ear height, each point's level computed from the sources'
    // settings (HQAmbience.LevelAt plus the docked ship's washes within its hearing
    // range), the dead points listed. Runs headless in edit mode with the HQ scene
    // open (`run SunkCost.Editor.Look.HQAmbienceProbe.Grid`) and in Play Mode from
    // the hq job. A point is walkable when a ray down from 3 m finds the deck (or a
    // floor within half a metre of it) and nothing solid stands at chest height.
    public static class HQAmbienceProbe
    {
        public const float EarHeight = 1.6f;
        public const float Step = 2f;

        public static string Grid()
        {
            HQAmbience bed = HQAmbience.InHQ();
            if (bed == null) return "grid: no HQ Ambience in the scene";
            ShipAmbience ship = DockedShip();
            Physics.SyncTransforms();
            int points = 0, walkable = 0, dead = 0;
            float min = float.MaxValue; Vector2 minAt = default;
            var deadList = new StringBuilder();
            for (float x = -29f; x <= 29f; x += Step)
                for (float z = -17f; z <= 17f; z += Step)
                {
                    points++;
                    if (!Walkable(x, z)) continue;
                    walkable++;
                    float level = LevelAt(bed, ship, new Vector3(x, EarHeight, z));
                    if (level < min) { min = level; minAt = new Vector2(x, z); }
                    if (level < HQAmbience.AudibleLevel) { dead++; deadList.Append(" (").Append(x).Append(',').Append(z).Append(')'); }
                }
            return $"grid: points={points} walkable={walkable} dead={dead} min={min:0.000} at ({minAt.x},{minAt.y}); dead:{deadList}";
        }

        // The platform as it was before the bed (the brief's complaint): the docked ship's
        // sources alone, wind included, so the dead zone is a number before the fix.
        public static string GridShipOnly()
        {
            ShipAmbience ship = DockedShip();
            if (ship == null) return "grid-ship-only: no docked ShipAmbience in the scene";
            Physics.SyncTransforms();
            int points = 0, walkable = 0, dead = 0;
            float min = float.MaxValue; Vector2 minAt = default;
            var deadList = new StringBuilder();
            for (float x = -29f; x <= 29f; x += Step)
                for (float z = -17f; z <= 17f; z += Step)
                {
                    points++;
                    if (!Walkable(x, z)) continue;
                    walkable++;
                    float level = ship.LevelAt(new Vector3(x, EarHeight, z), includeWind: true);
                    if (level < min) { min = level; minAt = new Vector2(x, z); }
                    if (level < HQAmbience.AudibleLevel) { dead++; deadList.Append(" (").Append(x).Append(',').Append(z).Append(')'); }
                }
            return $"grid-ship-only: points={points} walkable={walkable} dead={dead} min={min:0.000} at ({minAt.x},{minAt.y}); dead:{deadList}";
        }

        // The level the probe judges: the HQ bed (wind in full, the edge washes by
        // their rolloff) plus the docked ship's washes within its hearing range. The
        // ship's wind is left out: the bed suppresses it while the bed plays.
        public static float LevelAt(HQAmbience bed, ShipAmbience ship, Vector3 ear)
        {
            float level = bed != null ? bed.LevelAt(ear) : 0f;
            if (ship != null) level += ship.LevelAt(ear, includeWind: false);
            return level;
        }

        public static bool Walkable(float x, float z)
        {
            if (!Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out RaycastHit hit, 3.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.point.y < -0.1f || hit.point.y > 0.5f) return false;
            return !Physics.CheckSphere(new Vector3(x, 1.0f, z), 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        // The named spots the brief's complaint named, each with its level.
        public static string Spots()
        {
            HQAmbience bed = HQAmbience.InHQ();
            ShipAmbience ship = DockedShip();
            var sb = new StringBuilder();
            foreach (var spot in new[] { ("arrival pad", new Vector3(18f, EarHeight, 14f)), ("plank base", new Vector3(30f, EarHeight, -16f)), ("pickup chute", new Vector3(10f, EarHeight, 15f)), ("crane", new Vector3(24f, EarHeight, 4f)), ("intake", new Vector3(-11f, EarHeight, -14f)), ("depot aisle", new Vector3(-6f, EarHeight, 11.7f)), ("court", new Vector3(-8f, EarHeight, -2f)), ("bridge landing", new Vector3(-31f, EarHeight, -12f)), ("east rail", new Vector3(28f, EarHeight, 0f)), ("centre", new Vector3(0f, EarHeight, 0f)) })
                sb.Append(spot.Item1).Append('=').Append((bed != null ? bed.LevelAt(spot.Item2) : 0f).ToString("0.000")).Append(" (bed wash ").Append((bed != null ? bed.WashLevelAt(spot.Item2) : 0f).ToString("0.000")).Append(", ship ").Append((ship != null ? ship.LevelAt(spot.Item2, false) : 0f).ToString("0.000")).Append("); ");
            return sb.ToString();
        }

        // Play Mode: what is playing on this peer right now, and the level at the listener.
        public static string Status()
        {
            HQAmbience bed = HQAmbience.InHQ();
            ShipAmbience ship = DockedShip();
            AudioListener listener = null;
            foreach (AudioListener candidate in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)) if (candidate.isActiveAndEnabled) { listener = candidate; break; }
            var sb = new StringBuilder();
            sb.Append("listener=").Append(listener != null ? listener.transform.position.ToString("0.0") + " scene=" + listener.gameObject.scene.name : "none");
            if (bed != null)
            {
                sb.Append("; bed playing=").Append(bed.Playing).Append(" wind=").Append(bed.Wind != null && bed.Wind.isPlaying ? "on" : "off").Append(" washes=");
                int on = 0; foreach (AudioSource w in bed.Washes) if (w != null && w.isPlaying) on++;
                sb.Append(on).Append('/').Append(bed.Washes.Length);
                if (listener != null) sb.Append(" levelAtListener=").Append(bed.LevelAt(listener.transform.position).ToString("0.000"));
            }
            else sb.Append("; no bed");
            sb.Append("; ship heard=").Append(ship != null && ship.Heard).Append(" windSuppressed=").Append(ShipAmbience.WindSuppressed);
            return sb.ToString();
        }

        private static ShipAmbience DockedShip()
        {
            var hq = SunkCost.World.WorldScenes.Scene(SunkCost.World.WorldId.HQ);
            ShipAmbience any = null;
            foreach (ShipAmbience candidate in Object.FindObjectsByType<ShipAmbience>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (hq.IsValid() && candidate.gameObject.scene == hq) return candidate;
                any ??= candidate;
            }
            return any;
        }
    }
}
