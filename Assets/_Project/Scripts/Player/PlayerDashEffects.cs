using System.Collections.Generic;
using SunkCost.Audio;
using UnityEngine;

namespace SunkCost.Player
{
    // What a dash looks and sounds like (Dan, 21 September 2026: "a small animation
    // of a circle coming out of each shoe, like it is what made him dash"): a ring
    // out of each boot, expanding and fading as it trails behind the diver, and a
    // whoosh at the feet. On every peer from the same event — the owner's own press
    // (HQPlayerController.TryDash) or the replicated cue for a remote copy — so a
    // friend, a dead spectator and the deck TV see the rings the diver sees
    // (docs/CONVENTIONS.md "Every screen sees it"): world objects, not a screen
    // overlay. Placeholder look until a real effect replaces it. Presentation only;
    // nothing here decides anything. Added to the player at runtime by
    // HQPlayerController.OnStartClient.
    public sealed class PlayerDashEffects : MonoBehaviour
    {
        private const float RingSeconds = 0.45f, RingStartRadius = 0.07f, RingEndRadius = 0.5f, RingDriftMetersPerSecond = 2.4f;
        private const float FootSpread = 0.13f, FootHeight = 0.06f; // the boots: either side of the root, just off the floor
        private const int RingSegments = 40;
        private static readonly Color RingColour = new(0.62f, 0.94f, 1f); // the visor's cyan
        private static Mesh ringMesh;
        private static Material ringMaterial;
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");

        private struct Ring { public Transform T; public Renderer R; public MaterialPropertyBlock Block; public float Born; public Vector3 Drift; }
        private readonly List<Ring> rings = new();
        private HQPlayerController controller;
        private AudioSource feet;
        private AudioLibrary library;

        // For the checks.
        public int RingsShown { get; private set; }
        public int WhooshesPlayed { get; private set; }
        public int RingsAlive => rings.Count;

        private void Awake()
        {
            controller = GetComponent<HQPlayerController>();
            library = AudioLibrary.Get();
            var go = new GameObject("Dash");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            feet = go.AddComponent<AudioSource>();
            feet.playOnAwake = false; feet.loop = false;
            feet.spatialBlend = 1f; feet.rolloffMode = AudioRolloffMode.Linear;
            feet.minDistance = 2f; feet.maxDistance = 30f; feet.dopplerLevel = 0f;
            AudioDeviceService devices = FindAnyObjectByType<AudioDeviceService>();
            if (devices != null) devices.Route(feet);
            if (controller != null) controller.DashStarted += OnDash;
        }

        private void OnDestroy()
        {
            if (controller != null) controller.DashStarted -= OnDash;
            foreach (Ring ring in rings) if (ring.T != null) Destroy(ring.T.gameObject);
            rings.Clear();
        }

        // One ring out of each boot, its plane across the dash so it reads as pushed
        // out behind the shoe, drifting back as the diver goes forward.
        private void OnDash(Vector3 direction)
        {
            Vector3 forward = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                var go = new GameObject("Dash ring");
                go.transform.SetPositionAndRotation(transform.position + right * (FootSpread * side) + Vector3.up * FootHeight, Quaternion.LookRotation(forward, Vector3.up));
                go.transform.localScale = Vector3.one * RingStartRadius;
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = RingMesh();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = RingMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColourId, RingColour);
                renderer.SetPropertyBlock(block);
                rings.Add(new Ring { T = go.transform, R = renderer, Block = block, Born = Time.unscaledTime, Drift = -forward * RingDriftMetersPerSecond });
                RingsShown++;
            }
            if (feet != null && library != null)
            {
                float scale = controller != null && controller.IsOwner ? library.OwnFootstepScale : 1f;
                feet.PlayOneShot(library.DashWhoosh, library.DashWhooshVolume * scale);
                WhooshesPlayed++;
            }
        }

        private void LateUpdate()
        {
            if (rings.Count == 0) return;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                Ring ring = rings[i];
                float t = (now - ring.Born) / RingSeconds;
                if (ring.T == null || t >= 1f)
                {
                    if (ring.T != null) Destroy(ring.T.gameObject);
                    rings.RemoveAt(i);
                    continue;
                }
                float eased = 1f - (1f - t) * (1f - t); // fast out, then slowing
                ring.T.localScale = Vector3.one * Mathf.Lerp(RingStartRadius, RingEndRadius, eased);
                ring.T.position += ring.Drift * dt;
                Color c = RingColour; c.a = 1f - t;
                ring.Block.SetColor(BaseColourId, c);
                ring.R.SetPropertyBlock(ring.Block);
            }
        }

        // A flat annulus of radius 1 in its XY plane (the normal is +Z: the dash direction).
        private static Mesh RingMesh()
        {
            if (ringMesh != null) return ringMesh;
            const float inner = 0.72f;
            var vertices = new Vector3[RingSegments * 2];
            var triangles = new int[RingSegments * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0f);
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir;
                int next = (i + 1) % RingSegments;
                int t = i * 6;
                triangles[t] = i * 2; triangles[t + 1] = i * 2 + 1; triangles[t + 2] = next * 2;
                triangles[t + 3] = next * 2; triangles[t + 4] = i * 2 + 1; triangles[t + 5] = next * 2 + 1;
            }
            ringMesh = new Mesh { name = "Dash ring" };
            ringMesh.SetVertices(vertices);
            ringMesh.SetTriangles(triangles, 0);
            ringMesh.RecalculateBounds();
            return ringMesh;
        }

        // An additive, double-sided, unlit glow (URP Unlit set up transparent by hand).
        private static Material RingMaterial()
        {
            if (ringMaterial != null) return ringMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            ringMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Dash ring" };
            ringMaterial.SetFloat("_Surface", 1f);
            ringMaterial.SetFloat("_Blend", 0f);
            ringMaterial.SetFloat("_Cull", 0f);
            ringMaterial.SetFloat("_ZWrite", 0f);
            ringMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ringMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            ringMaterial.SetOverrideTag("RenderType", "Transparent");
            ringMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ringMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            ringMaterial.SetColor(BaseColourId, RingColour);
            return ringMaterial;
        }
    }
}
