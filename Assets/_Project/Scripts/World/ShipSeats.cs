using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.World
{
    // The couch seats (Dan, 23 September 2026: sitting on the couch zooms onto the
    // TV). Empty markers the ship's dressing puts under each couch, named
    // CouchSeat_1..N: the seated hip point on the seat, +Z toward the TV. A seat is
    // known on the wire by its number only; each peer finds the marker on its own
    // copy of the ship (the same prefab at HQ and at sea, so a number means the
    // same seat after a sail). Lookups are cached per ship: ShipParts.Find walks
    // the whole ship.
    public static class ShipSeats
    {
        public const string Prefix = "CouchSeat_";
        public const int MaxSeats = 16;

        private static readonly Dictionary<ShipParts, Transform[]> Cache = new();

        // The seat marker numbered `number` on this ship, or null.
        public static Transform Seat(ShipParts ship, int number)
        {
            if (ship == null || number <= 0 || number > MaxSeats) return null;
            Transform[] seats = SeatsOf(ship);
            Transform seat = seats[number];
            if (seat is not null && seat == null) { Cache.Remove(ship); seat = SeatsOf(ship)[number]; } // the marker was destroyed under us (a rebuild)
            return seat;
        }

        // 1..N from a marker's name, 0 for anything else.
        public static int NumberOf(Transform seat)
        {
            if (seat == null || !seat.name.StartsWith(Prefix)) return 0;
            return int.TryParse(seat.name.Substring(Prefix.Length), out int n) && n > 0 && n <= MaxSeats ? n : 0;
        }

        // The seat a press on this collider means: the collider belongs to a couch
        // (an ancestor holding seat markers), and of that couch's seats the one
        // nearest the crosshair's ray. Null when it is not a couch.
        public static Transform SeatUnder(Transform pressed, Vector3 eye, Vector3 forward)
        {
            for (Transform t = pressed; t != null; t = t.parent)
            {
                if (t.GetComponent<ShipParts>() != null) return null; // up to the ship's root: not a couch
                Transform best = null;
                float bestDistance = float.PositiveInfinity;
                foreach (Transform child in t)
                {
                    if (NumberOf(child) == 0) continue;
                    Vector3 toSeat = child.position - eye;
                    float distance = Vector3.Cross(forward, toSeat).magnitude; // from the ray's line
                    if (distance < bestDistance) { bestDistance = distance; best = child; }
                }
                if (best != null) return best;
            }
            return null;
        }

        // Ships whose scene went (a sail unloads one): out of the cache.
        private static readonly List<ShipParts> Gone = new();
        private static void DropUnloaded()
        {
            Gone.Clear();
            foreach (ShipParts ship in Cache.Keys) if (ship == null) Gone.Add(ship);
            foreach (ShipParts ship in Gone) Cache.Remove(ship);
        }

        // Index = seat number; [0] unused.
        private static Transform[] SeatsOf(ShipParts ship)
        {
            ShipParts key = ship;
            if (Cache.TryGetValue(key, out Transform[] seats)) return seats;
            DropUnloaded();
            seats = new Transform[MaxSeats + 1];
            foreach (Transform t in ship.GetComponentsInChildren<Transform>(true))
            {
                int n = NumberOf(t);
                if (n != 0 && seats[n] == null) seats[n] = t;
            }
            Cache[key] = seats;
            return seats;
        }
    }
}
