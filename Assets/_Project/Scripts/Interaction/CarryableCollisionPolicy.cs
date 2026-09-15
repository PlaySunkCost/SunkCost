using UnityEngine;

namespace SunkCost.Interaction
{
    // The one collision rule of docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md
    // section 6A: player capsules do not physically collide with carryables, so
    // nobody stands on a ball, launches off a drop or shoves a friend with a
    // throw. Carryables still hit the world and each other; walls, doors and
    // future hazards stay solid to players. The layers are created by the setup
    // (PlayerMovementHandsSetup) and looked up by name here; the matrix pair is
    // applied at startup on every peer so a build never depends on a stale
    // ProjectSettings edit. Players can walk through loose cargo in this
    // prototype; that is deliberate, and documented in DESIGN.
    public static class CarryableCollisionPolicy
    {
        public const string PlayerLayerName = "Player";
        public const string CarryableLayerName = "Carryable";

        public static int PlayerLayer => LayerMask.NameToLayer(PlayerLayerName);
        public static int CarryableLayer => LayerMask.NameToLayer(CarryableLayerName);
        public static bool LayersExist => PlayerLayer >= 0 && CarryableLayer >= 0;

        // Everything a movement or clearance query should treat as solid: not
        // carryables (walked through) and not players (they move, not the world).
        public static int WorldMask
        {
            get
            {
                int mask = ~0;
                if (CarryableLayer >= 0) mask &= ~(1 << CarryableLayer);
                if (PlayerLayer >= 0) mask &= ~(1 << PlayerLayer);
                return mask;
            }
        }

        // World plus other players: what a released item must not be placed inside.
        public static int PlacementMask
        {
            get
            {
                int mask = ~0;
                if (CarryableLayer >= 0) mask &= ~(1 << CarryableLayer);
                return mask;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (!LayersExist)
            {
                Debug.LogWarning("CarryableCollisionPolicy: the Player/Carryable layers are missing (run Sunk Cost/Prototype/Apply movement and hands setup); players will collide with loose cargo.");
                return;
            }
            Physics.IgnoreLayerCollision(PlayerLayer, CarryableLayer, true);
        }

        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0) return;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
