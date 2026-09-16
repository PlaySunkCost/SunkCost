using SunkCost.Player;
using UnityEngine;

namespace SunkCost.World
{
    // The colour panel on the HQ wall (Dan, 16 September 2026): a plate printed
    // with the wheel of swatches and a small square showing your current colour.
    // Look at it and press E (HQPlayerController) to open the picker. Local
    // presentation only: the plate bakes the wheel into its own material, the
    // swatch follows the local player's replicated colour. Nothing here is
    // networked; the pick goes through PlayerIdentity.
    public sealed class ColourPanel : MonoBehaviour
    {
        public const string PanelName = "Colour Panel";
        public const string SwatchName = "Your Swatch";

        [SerializeField] private Renderer plate;
        [SerializeField] private Renderer swatch;

        private Texture2D wheel;
        private MaterialPropertyBlock block;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public void Configure(Renderer plateRenderer, Renderer swatchRenderer)
        {
            plate = plateRenderer;
            swatch = swatchRenderer;
        }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (plate != null)
            {
                wheel = PlayerPalette.BakeWheel(256);
                plate.GetPropertyBlock(block);
                block.SetTexture(BaseMap, wheel);
                block.SetTexture(MainTex, wheel);
                plate.SetPropertyBlock(block);
            }
        }

        private void OnDestroy()
        {
            if (wheel != null) Destroy(wheel);
        }

        private void Update()
        {
            if (swatch == null) return;
            HQPlayerController local = WorldSceneFlow.LocalPlayer();
            PlayerIdentity identity = local != null ? local.GetComponent<PlayerIdentity>() : null;
            Color colour = identity != null ? identity.Colour : Color.gray;
            swatch.GetPropertyBlock(block);
            block.SetColor(BaseColor, colour);
            block.SetColor(ColorId, colour);
            swatch.SetPropertyBlock(block);
        }
    }
}
