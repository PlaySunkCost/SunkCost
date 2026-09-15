using SunkCost.Player;
using UnityEngine;

namespace SunkCost.Sites
{
    // PrototypePlayer's headlamp starts disabled on the prefab so HQ stays behaviourally
    // unchanged; a dive site is the one place it should be on. The player is spawned at
    // runtime by PlayerSpawner, not baked into this scene, so it cannot be wired to the
    // headlamp at build time — this polls for the locally owned player once and enables it.
    public sealed class DiveSiteHeadlampActivator : MonoBehaviour
    {
        private bool activated;

        private void Update()
        {
            if (activated)
                return;

            foreach (HQPlayerController candidate in FindObjectsByType<HQPlayerController>(FindObjectsSortMode.None))
            {
                if (!candidate.IsOwner)
                    continue;
                candidate.SetHeadlampEnabled(true);
                activated = true;
                return;
            }
        }
    }
}
