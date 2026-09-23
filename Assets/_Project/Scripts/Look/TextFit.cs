using System.Collections.Generic;
using UnityEngine;

namespace SunkCost.Look
{
    // Text that fits its screen or plate (ship audit SHIP-018: the status ran 3 m
    // wide across a 1.6 m screen). Measured, not estimated: a TextMesh's local
    // bounds follow its text at once (rich-text sizes included) and scale with the
    // character size, so one measure at the largest size gives the size that fits.
    // Never grows a line past the size its display chose.
    public static class TextFit
    {
        // The size `mesh` needs for its current text to fit `box` (metres, in the
        // text's own plane), at most `maxSize`. Leaves the mesh at that size.
        public static float Fit(TextMesh mesh, Vector2 box, float maxSize)
        {
            if (mesh == null) return maxSize;
            if (!Mathf.Approximately(mesh.characterSize, maxSize)) mesh.characterSize = maxSize;
            if (string.IsNullOrEmpty(mesh.text) || box.x <= 0f || box.y <= 0f) return maxSize;
            MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer == null) return maxSize;
            Vector3 size = renderer.localBounds.size;
            float k = 1f;
            if (size.x > box.x) k = Mathf.Min(k, box.x / size.x);
            if (size.y > box.y) k = Mathf.Min(k, box.y / size.y);
            float fitted = maxSize * k;
            if (k < 1f) mesh.characterSize = fitted;
            return fitted;
        }

        // One size for a group (SHIP-050: "HQ" read 2.3x "SITE 01"): each fits its
        // own box, then all take the smallest, so the longest label sets the size.
        public static float FitGroup(IReadOnlyList<TextMesh> meshes, Vector2 box, float maxSize)
        {
            float size = maxSize;
            foreach (TextMesh mesh in meshes) size = Mathf.Min(size, Fit(mesh, box, maxSize));
            foreach (TextMesh mesh in meshes) if (mesh != null) mesh.characterSize = size;
            return size;
        }
    }
}
