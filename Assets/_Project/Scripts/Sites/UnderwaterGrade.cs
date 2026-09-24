using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SunkCost.Sites
{
    // The underwater grade reaches every camera under the site's water: the diver's
    // own, a spectator's on a diver, the deck TV's (spectate S1b, 24 September 2026:
    // a dead guest watching the host at the seafloor saw 0.050 against the host's
    // 0.215 - the brightness of the seafloor with no grade at all; since SHIP-021
    // the grade exists, and only the host, an editor, had it).
    //
    // The grade is a Volume with a box. URP finds a local volume's colliders once,
    // at OnEnable, in a player (the editor refreshes them every frame), and then
    // asks the physics shape for its closest point to the camera. Rather than
    // depend on either, the volume is global in Play Mode and its weight is set
    // before each camera renders from where that camera stands against the same
    // box, with URP's own blend (1 inside, fading over the blend distance above the
    // water). A camera anywhere else - the deck at sea, the HQ - gets weight 0.
    // Local presentation only.
    [RequireComponent(typeof(Volume), typeof(BoxCollider))]
    public sealed class UnderwaterGrade : MonoBehaviour
    {
        private static readonly List<UnderwaterGrade> active = new();

        private Volume volume;
        private BoxCollider box;

        private bool Parts()
        {
            if (volume == null) volume = GetComponent<Volume>();
            if (box == null) box = GetComponent<BoxCollider>();
            return volume != null && box != null;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || !Parts()) return;
            volume.isGlobal = true;
            if (active.Count == 0) RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            active.Add(this);
        }

        private void OnDisable()
        {
            if (!active.Remove(this)) return;
            if (active.Count == 0) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (volume != null) volume.isGlobal = false;
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            foreach (UnderwaterGrade grade in active) grade.PrepareCamera(camera);
        }

        // Every grade's weight for a camera about to render by hand (the deck TV works
        // out its own volume stack before Camera.Render); the highest weight.
        public static float PrepareAll(Camera camera)
        {
            float highest = 0f;
            foreach (UnderwaterGrade grade in active) highest = Mathf.Max(highest, grade.PrepareCamera(camera));
            return highest;
        }

        // The weight this camera renders the grade with (the edit-mode checks call it too).
        public float PrepareCamera(Camera camera)
        {
            if (camera == null || !Parts()) return 0f;
            Transform trigger = camera.TryGetComponent(out UniversalAdditionalCameraData data) && data.volumeTrigger != null ? data.volumeTrigger : camera.transform;
            float weight = WeightAt(trigger.position);
            volume.weight = weight;
            return weight;
        }

        // URP's local-volume blend against the box: 1 inside, 1 - d^2/blend^2 within
        // the blend distance outside, 0 beyond.
        public float WeightAt(Vector3 position)
        {
            if (!Parts()) return 0f;
            Transform t = box.transform;
            Vector3 local = t.InverseTransformPoint(position) - box.center;
            Vector3 half = box.size * 0.5f;
            Vector3 outside = new(Mathf.Max(0f, Mathf.Abs(local.x) - half.x), Mathf.Max(0f, Mathf.Abs(local.y) - half.y), Mathf.Max(0f, Mathf.Abs(local.z) - half.z));
            float distanceSqr = t.TransformVector(outside).sqrMagnitude;
            if (distanceSqr <= 0f) return 1f;
            float blendSqr = volume.blendDistance * volume.blendDistance;
            return blendSqr > 0f && distanceSqr < blendSqr ? 1f - distanceSqr / blendSqr : 0f;
        }
    }
}
