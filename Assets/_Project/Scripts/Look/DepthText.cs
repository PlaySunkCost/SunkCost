using UnityEngine;

namespace SunkCost.Look
{
    // A TextMesh drawn with the depth-tested text material (Sunk Cost/Depth
    // Text) instead of Unity's GUI/Text Shader, which reads through walls. The
    // material asset carries the font's atlas; the atlas is rebuilt by Unity as
    // new characters are used, so this keeps the material's texture current.
    [ExecuteAlways]
    [RequireComponent(typeof(TextMesh))]
    public sealed class DepthText : MonoBehaviour
    {
        [SerializeField] private Material material;

        public void Configure(Material depthMaterial)
        {
            material = depthMaterial;
            Apply();
        }

        private void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
            Apply();
        }

        private void OnDisable() => Font.textureRebuilt -= OnFontRebuilt;

        private void OnFontRebuilt(Font font)
        {
            TextMesh mesh = GetComponent<TextMesh>();
            if (mesh != null && mesh.font == font) Apply();
        }

        private void Apply()
        {
            TextMesh mesh = GetComponent<TextMesh>();
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (mesh == null || renderer == null || material == null) return;
            Font font = mesh.font;
            Texture atlas = font != null && font.material != null ? font.material.mainTexture : null;
            if (atlas != null && material.mainTexture != atlas) material.mainTexture = atlas;
            if (renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }
    }
}
