using System.Collections.Generic;
using UnityEngine;
using SunkCost.Interaction;
using SunkCost.Net;
using UnityEngine.Rendering;

namespace SunkCost.Player
{
    // One sign that something can be used (SHIP-054): whatever the aiming dot rests on
    // and E (or a grab) would act on - a console button, the cabin's button, the car's
    // panel, the TV screen, a couch, a loose item - gets a soft warm rim, drawn again
    // over its own meshes for the owner's camera only. Only the controller's targets
    // qualify, so decoration never shows it; the moulded knobs on the console model
    // stay plain. Local presentation: nothing is sent, the target's renderers and
    // materials are never touched, and nothing is drawn (or allocated) while the dot
    // rests on nothing usable. Added to the owner's player at OnStartClient.
    [DefaultExecutionOrder(600)] // after the controller has found this frame's target
    public sealed class InteractHighlight : MonoBehaviour
    {
        private const string ShaderName = "InteractHighlight"; // Resources
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int LiftId = Shader.PropertyToID("_Lift");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");
        private static readonly int EdgeUvId = Shader.PropertyToID("_EdgeUV");

        private struct Part { public MeshRenderer Renderer; public Mesh Mesh; public bool Panel; }

        private HQPlayerController controller;
        private Material material;
        private MaterialPropertyBlock panelBlock, plainBlock;
        private Transform root;
        private readonly List<Part> parts = new();

        // For the checks: what is highlighted this frame (null when nothing).
        public Transform Highlighted { get; private set; }
        public int PartCount => Highlighted != null ? parts.Count : 0;

        private void Awake() => controller = GetComponent<HQPlayerController>();

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        private void LateUpdate()
        {
            Highlighted = null;
            Transform target = UsableTarget();
            if (target == null) { if (root != null) { root = null; parts.Clear(); } return; }
            Camera cam = controller.PlayerCamera;
            if (cam == null || !cam.isActiveAndEnabled || !EnsureMaterial()) return;
            if (target != root) Collect(target);
            if (parts.Count == 0) return;
            Highlighted = root;
            PlayerMovementSettings settings = controller.Movement;
            Tune(plainBlock, settings, 0f);
            Tune(panelBlock, settings, settings.HighlightEdge);
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part.Renderer == null || !part.Renderer.enabled || !part.Renderer.gameObject.activeInHierarchy || part.Mesh == null) continue;
                MaterialPropertyBlock block = plainBlock;
                if (part.Panel)
                {
                    // A frame of HighlightEdgeMeters inside the quad's edge, whatever its size.
                    Vector3 size = part.Renderer.transform.lossyScale;
                    float w = settings.HighlightEdgeMeters;
                    panelBlock.SetVector(EdgeUvId, new Vector4(w / Mathf.Max(Mathf.Abs(size.x), 0.01f), w / Mathf.Max(Mathf.Abs(size.y), 0.01f), 0f, 0f));
                    block = panelBlock;
                }
                var rp = new RenderParams(material)
                {
                    camera = cam, // the owner's eyes only: not the TV's camera, not a spectator's
                    layer = part.Renderer.gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = part.Renderer.bounds,
                    matProps = block,
                };
                Matrix4x4 matrix = part.Renderer.localToWorldMatrix;
                for (int sub = 0; sub < part.Mesh.subMeshCount; sub++) Graphics.RenderMesh(rp, part.Mesh, sub, matrix);
            }
        }

        private static void Tune(MaterialPropertyBlock block, PlayerMovementSettings settings, float edge)
        {
            block.SetColor(ColorId, settings.HighlightColor);
            block.SetFloat(RimId, settings.HighlightRim);
            block.SetFloat(LiftId, settings.HighlightLift);
            block.SetFloat(EdgeId, edge);
        }

        // The controller's usable target this frame, or null. The dot's own rules: an
        // item only when it could be taken now; a teammate is no object to outline.
        private Transform UsableTarget()
        {
            if (controller == null || !controller.IsOwner || controller.IsDead || controller.TravelLocked || controller.IsSeated || controller.ViewObstructed) return null;
            if (!SessionInputGate.CanPlay) return null;
            CarryableItem item = controller.CurrentTarget;
            if (item != null) return item.CanGrabFromWorld && controller.Inventory != null && controller.Inventory.CanStoreOrHold(item) ? item.transform : null;
            bool any = controller.CurrentButton != null || controller.CurrentTv != null || controller.CurrentCabinControl != CabinControl.None
                || controller.CurrentColourPanel != null || controller.CurrentQuotaBoard != null || controller.CurrentShopDisplay != null || controller.SeatUsable;
            return any ? controller.CurrentUsable : null;
        }

        // Every mesh under the target that draws: MeshRenderers with a mesh (text and
        // skinned meshes have none to redraw). Once per new target.
        private void Collect(Transform target)
        {
            root = target;
            parts.Clear();
            foreach (MeshRenderer renderer in target.GetComponentsInChildren<MeshRenderer>(false))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || renderer.GetComponent<TextMesh>() != null) continue;
                parts.Add(new Part { Renderer = renderer, Mesh = filter.sharedMesh, Panel = filter.sharedMesh.name == "Quad" });
            }
        }

        private bool EnsureMaterial()
        {
            if (material != null) return true;
            Shader shader = Resources.Load<Shader>(ShaderName);
            if (shader == null || !shader.isSupported) { enabled = false; Debug.LogWarning("[Highlight] no Sunk Cost/Interact Highlight shader; the use highlight is off"); return false; }
            material = new Material(shader) { name = "Interact highlight", hideFlags = HideFlags.DontSave };
            plainBlock = new MaterialPropertyBlock();
            panelBlock = new MaterialPropertyBlock();
            return true;
        }
    }
}
