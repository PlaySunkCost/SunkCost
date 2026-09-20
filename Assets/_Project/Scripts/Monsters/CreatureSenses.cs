using System.Collections.Generic;
using SunkCost.Diving;
using SunkCost.Interaction;
using SunkCost.Player;
using SunkCost.World;
using UnityEngine;

namespace SunkCost.Monsters
{
    // What the server can know about the divers, for every creature's brain
    // (docs/NETWORK_CONTRACT.md: monster AI and targeting are server only). The
    // living divers below, refreshed once a frame; sight as a lamp-range line
    // of sight; "watched" for the Angel; the safe ground round the shaft.
    public static class CreatureSenses
    {
        private static readonly List<HQPlayerController> divers = new();
        private static int diversFrame = -1;
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        // Every living diver whose object stands in the dive world and who the day
        // state lists below. Cached for the frame; do not hold the list.
        public static IReadOnlyList<HQPlayerController> Divers()
        {
            if (diversFrame == Time.frameCount) return divers;
            diversFrame = Time.frameCount;
            divers.Clear();
            CrewDayState day = CrewDayState.Instance;
            UnityEngine.SceneManagement.Scene dive = WorldScenes.Scene(WorldId.Dive);
            foreach (HQPlayerController p in Object.FindObjectsByType<HQPlayerController>(FindObjectsInactive.Exclude))
            {
                if (p == null || !p.IsSpawned || p.IsDead) continue;
                if (day != null && !day.IsBelow(p.OwnerId)) continue;
                if (p.gameObject.scene != dive) continue;
                divers.Add(p);
            }
            return divers;
        }

        public static HQPlayerController DiverOf(int clientId)
        {
            foreach (HQPlayerController p in Divers()) if (p.OwnerId == clientId) return p;
            return null;
        }

        // The nearest living diver, flat, or null.
        public static HQPlayerController Nearest(Vector3 from, float withinMeters = float.PositiveInfinity)
        {
            HQPlayerController best = null;
            float bestDistance = withinMeters;
            foreach (HQPlayerController p in Divers())
            {
                float d = Flat(from, p.transform.position);
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            return best;
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // The lamp is on and would show: the server's replicated lamp bit, on a
        // living diver below.
        public static bool LampLit(HQPlayerController diver) => diver != null && diver.LampOn && !diver.IsDead;

        // Chest height: what a monster looks at and a bolt aims for.
        public static Vector3 Chest(HQPlayerController diver) => diver.transform.position + Vector3.up * 1.1f;

        // What a monster's line of sight may pass through: the world minus the
        // players, the cargo and the other monsters.
        public static int SightMask
        {
            get
            {
                int mask = CarryableCollisionPolicy.WorldMask;
                int monster = MonsterCatalog.Layer;
                if (monster >= 0) mask &= ~(1 << monster);
                return mask;
            }
        }

        // Nothing solid between two points (the world's colliders only).
        public static bool ClearLine(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, SightMask, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return false; // an incomplete list is not trusted
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i].collider;
                // The tube's glass and the car's shell are solid to a line of sight
                // as any wall; a trigger volume is not (ignored above).
                if (hit != null && Hits[i].distance < distance - 0.05f) return false;
            }
            return true;
        }

        // A monster at `eye` sees the diver: within the lamp-lit range (or the dark
        // one with the lamp off) with a clear line to the chest.
        public static bool CanSee(Vector3 eye, HQPlayerController diver, MonsterSettings settings)
        {
            if (diver == null) return false;
            float range = LampLit(diver) ? settings.SightMeters : settings.SightDarkMeters;
            Vector3 chest = Chest(diver);
            if (Vector3.Distance(eye, chest) > range) return false;
            return ClearLine(eye, chest);
        }

        // The point is inside some living diver's view: within the watch range, inside
        // the half-angle of that diver's eyes, with a clear line from the eyes.
        public static bool Watched(Vector3 point, MonsterSettings settings, out HQPlayerController watcher)
        {
            watcher = null;
            float cosHalf = Mathf.Cos(settings.WatchHalfAngleDeg * Mathf.Deg2Rad);
            foreach (HQPlayerController p in Divers())
            {
                p.EyePose(out Vector3 eye, out Quaternion look);
                Vector3 to = point - eye;
                float distance = to.magnitude;
                if (distance > settings.AngelWatchMeters || distance < 0.01f) continue;
                if (Vector3.Dot(look * Vector3.forward, to / distance) < cosHalf) continue;
                if (!ClearLine(eye, point)) continue;
                watcher = p;
                return true;
            }
            return false;
        }

        // The shaft's centre at the seabed (the car's landing), or null without a site.
        public static bool ShaftCentre(out Vector3 centre)
        {
            ElevatorController car = WorldSceneFlow.FindCarCached();
            if (car == null) { centre = Vector3.zero; return false; }
            centre = car.BottomPosition;
            return true;
        }

        // Inside the car, or on the tube's floor round it: no monster reaches here.
        public static bool Safe(HQPlayerController diver, MonsterSettings settings)
        {
            if (diver == null) return true;
            ElevatorController car = WorldSceneFlow.FindCarCached();
            if (car == null) return false;
            if (car.IsInsideCar(diver.transform.position + Vector3.up * 0.5f)) return true;
            return Flat(car.BottomPosition, diver.transform.position) < settings.TubeSafeMeters;
        }

        // The seabed height under a point (the car's landing is the reference).
        public static float SeabedY()
        {
            ElevatorController car = WorldSceneFlow.FindCarCached();
            return car != null ? car.BottomPosition.y : -45f;
        }
    }
}
