using UnityEngine;

namespace SunkCost.Look
{
    // The sea that moves (Dan, 18 September 2026): a subdivided plane whose
    // vertices ride two crossing wave trains, with the ripple normals scrolled
    // in the material. Presentation only, on every peer alike; nothing reads it
    // (a plank jumper is "in the water" by a fixed line, HQPlank.WaterY). The
    // mesh is a shared asset: the first instance copies it and the copy is
    // displaced, so the asset on disk keeps its flat shape.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WaveSurface : MonoBehaviour
    {
        [Tooltip("Wave train A: height in metres, length in metres, direction in the XZ plane, speed in metres per second.")]
        [SerializeField] private float heightA = 0.18f;
        [SerializeField] private float lengthA = 9f;
        [SerializeField] private Vector2 directionA = new(1f, 0.35f);
        [SerializeField] private float speedA = 1.4f;
        [Tooltip("Wave train B, crossing A.")]
        [SerializeField] private float heightB = 0.10f;
        [SerializeField] private float lengthB = 4.5f;
        [SerializeField] private Vector2 directionB = new(-0.5f, 1f);
        [SerializeField] private float speedB = 1.0f;
        [Tooltip("How fast the ripple normals drift, in texture tiles per second.")]
        [SerializeField] private Vector2 normalScroll = new(0.012f, 0.008f);

        private Mesh working;
        private Vector3[] rest, moved;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private static readonly int BumpMapST = Shader.PropertyToID("_BaseMap_ST"); // URP Lit samples the normal map with the base map's tiling and offset

        private void OnEnable()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            if (source == null && filter.sharedMesh != null && filter.sharedMesh != working) source = filter.sharedMesh;
            if (source == null) return;
            if (working == null || working.name != source.name + " (waves)")
            {
                working = Instantiate(source);
                working.name = source.name + " (waves)";
                working.hideFlags = HideFlags.HideAndDontSave;
                working.MarkDynamic();
            }
            rest = source.vertices;
            moved = new Vector3[rest.Length];
            filter.sharedMesh = working;
            block ??= new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            // Put the asset back so the scene file never references the copy.
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null && working != null && filter.sharedMesh == working)
            {
                Mesh source = FindSource();
                if (source != null) filter.sharedMesh = source;
            }
            if (working != null) { DestroyImmediate(working); working = null; }
        }

        [SerializeField, HideInInspector] private Mesh source; // the flat asset, kept so the scene never saves the copy
        private Mesh FindSource() => source;

        private void Update()
        {
            if (working == null || rest == null) return;
            float t = Application.isPlaying ? Time.time : (float)UnityEditor_Time();
            Vector2 da = directionA.normalized, db = directionB.normalized;
            float ka = 2f * Mathf.PI / Mathf.Max(lengthA, 0.1f), kb = 2f * Mathf.PI / Mathf.Max(lengthB, 0.1f);
            Transform tr = transform;
            for (int i = 0; i < rest.Length; i++)
            {
                Vector3 world = tr.TransformPoint(rest[i]);
                float pa = (world.x * da.x + world.z * da.y) * ka - t * speedA * ka;
                float pb = (world.x * db.x + world.z * db.y) * kb - t * speedB * kb;
                float y = Mathf.Sin(pa) * heightA + Mathf.Sin(pb) * heightB;
                Vector3 v = rest[i];
                v.y += y;
                // A little forward lean on the crests (Gerstner-ish) reads as water, not cloth.
                v.x += Mathf.Cos(pa) * heightA * 0.6f * da.x;
                v.z += Mathf.Cos(pa) * heightA * 0.6f * da.y;
                moved[i] = v;
            }
            working.vertices = moved;
            working.RecalculateNormals();
            working.RecalculateBounds();
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                Vector4 st = meshRenderer.sharedMaterial.GetVector(BumpMapST);
                st.z = t * normalScroll.x; st.w = t * normalScroll.y;
                meshRenderer.GetPropertyBlock(block);
                block.SetVector(BumpMapST, st);
                meshRenderer.SetPropertyBlock(block);
            }
        }

        private static double UnityEditor_Time()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.time;
#endif
        }
    }
}
