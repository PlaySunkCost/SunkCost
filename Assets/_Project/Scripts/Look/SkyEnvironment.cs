using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Look
{
    // The sky's light on everything (Dan's look card, 18 September 2026): the
    // default reflection is baked from the skybox by the lighting build, which
    // nobody runs on a generated scene — so the sea and the wet steel would
    // reflect black. At load this renders the skybox into a small cubemap and
    // hands it to the scene's render settings as the reflection; the ambient
    // probe is refreshed the same way. A few milliseconds, once per load.
    // Runtime only: in the editor a capture asks for it explicitly and puts the
    // settings back, so the scene file never references the temporary cubemap.
    // The render waits two frames after the scene comes up: a camera render from
    // inside a scene load (FishNet moves objects between scenes then) crashed
    // the editor in the transform-change job (18 September 2026).
    public sealed class SkyEnvironment : MonoBehaviour
    {
        private RenderTexture reflection;

        private System.Collections.IEnumerator Start()
        {
            if (!Application.isPlaying) yield break;
            yield return null;
            yield return null;
            reflection = Refresh();
        }
        private void OnDestroy() { if (reflection != null) { reflection.Release(); Destroy(reflection); } }

        public static RenderTexture Refresh()
        {
            var rt = new RenderTexture(256, 256, 0, RenderTextureFormat.DefaultHDR)
            {
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                name = "Sky Reflection"
            };
            GameObject go = new("Sky Reflection Camera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.cullingMask = 0;             // the sky alone
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.transform.position = new Vector3(0f, 30f, 0f);
                cam.RenderToCubemap(rt);
            }
            finally { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = rt;
            DynamicGI.UpdateEnvironment();
            return rt;
        }

        // The editor's captures put the scene back the way it was.
        public static void Restore(RenderTexture rt)
        {
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.customReflectionTexture = null;
            if (rt != null) { rt.Release(); if (Application.isPlaying) Destroy(rt); else DestroyImmediate(rt); }
        }
    }
}
