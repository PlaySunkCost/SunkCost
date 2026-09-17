using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Player
{
    // The first-person body (Dan, 17 September 2026: "I prefer to use Dor's
    // thing"): the character model (GenericCharacter, one unrigged mesh) is cut
    // in two by the editor setup (PlayerHeadSplitSetup) — every triangle wholly
    // above the neck into a Head mesh on its own child, the rest the body — so
    // the owner can see its own body from the shoulders down and never the
    // inside of its head. The eyes, separate objects under the model, count as
    // head. This component only knows which renderers are the head; the same on
    // every peer, and only what is hidden differs
    // (HQPlayerController.SetLocalPresentation). The .blend is untouched; a
    // rigged model with a real neck bone makes the cut unnecessary.
    public sealed class PlayerHeadSplit : MonoBehaviour
    {
        public const string HeadName = "Head";

        [Tooltip("Names of the model's own head objects (the cut head mesh, the eyes).")]
        [SerializeField] private string[] headObjectNames = { HeadName, "Eye.L", "Eye.R" };

        private Renderer[] headRenderers = new Renderer[0];
        private bool found;

        public bool Split => found && headRenderers.Length > 0;
        public IReadOnlyList<Renderer> HeadRenderers => headRenderers;
        public bool HeadShown { get { foreach (Renderer r in headRenderers) if (r != null && r.enabled) return true; return false; } }

        // Find the head renderers under `model`; safe to call more than once.
        public IReadOnlyList<Renderer> Apply(Transform model)
        {
            if (found || model == null) return headRenderers;
            var heads = new List<Renderer>();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                if (System.Array.IndexOf(headObjectNames, r.name) >= 0) heads.Add(r);
            headRenderers = heads.ToArray();
            found = true;
            return headRenderers;
        }

        public void SetHeadShown(bool shown)
        {
            foreach (Renderer r in headRenderers) if (r != null) r.enabled = shown;
        }
    }
}
