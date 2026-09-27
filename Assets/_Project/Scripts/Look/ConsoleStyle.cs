using UnityEngine;

namespace SunkCost.Look
{
    // The console screens' tuning (the shared console, 27 September 2026): what the
    // polish pass may turn without touching code - the font, the pixel density, the
    // glass emission, every text size as a fraction of its surface's height, the
    // frame, halo, grid and dimming strengths. One asset in Resources
    // (Assets/_Project/Resources/ConsoleStyle.asset, made by ConsoleBuilder); without
    // it a hidden instance carries the same defaults, so the screens always draw.
    [CreateAssetMenu(fileName = "ConsoleStyle", menuName = "Sunk Cost/Console Style")]
    public sealed class ConsoleStyle : ScriptableObject
    {
        public const string ResourceName = "ConsoleStyle";

        [Header("Surface")]
        [Tooltip("null = the built-in LegacyRuntime font; an imported TTF must stay Dynamic")] public Font Font;
        [Tooltip("The painter's shader (Sunk Cost/Screen Paint); referenced here so a build carries it")] public Shader PaintShader;
        [Min(200f)] public float PixelsPerMetre = 1200f;
        [Min(256)] public int MaxWidth = 2048;
        [Min(128)] public int MinWidth = 384;
        [Tooltip("Emission multiplier on the glass; over ~1.15 the whites bloom, over 1.4 the cyan pastels")] [Range(0.2f, 1.4f)] public float Emission = 1.0f;
        [Tooltip("Letter-spacing on titles and card names, in em")] [Range(0f, 0.2f)] public float TitleTracking = 0.06f;
        [Tooltip("Frame thickness in pixels per 500 px of surface height")] [Range(0.5f, 4f)] public float FrameThickness = 1.5f;
        [Range(1f, 3f)] public float SelectedFrameScale = 2f;
        [Tooltip("Grid step as a fraction of the surface height")] [Range(0.02f, 0.2f)] public float GridStep = 1f / 16f;
        [Range(0f, 0.3f)] public float GridAlpha = 0.07f;
        [Range(0f, 0.3f)] public float CardFillAlpha = 0.04f;
        [Range(0f, 0.4f)] public float SelectedFillAlpha = 0.10f;
        [Range(0f, 1f)] public float HaloAlpha = 0.35f;
        [Tooltip("How much of the accent a locked card keeps")] [Range(0.1f, 1f)] public float LockedDim = 0.45f;
        [Tooltip("How much of its light a disabled sign keeps")] [Range(0.1f, 1f)] public float DisabledSign = 0.3f;
        [Tooltip("Site pictures: alpha only, tinted in the accent (a silhouette). Off draws Dan's own colours")] public bool PictureTintOnly = true;

        [Header("Top screen (fractions of its height)")]
        [Range(0.04f, 0.16f)] public float TopTitle = 0.09f;
        [Range(0.04f, 0.14f)] public float TopCorner = 0.07f;
        [Tooltip("The gap between cards as a fraction of the width")] [Range(0.005f, 0.05f)] public float CardGap = 0.02f;
        [Range(0.04f, 0.14f)] public float CardName = 0.085f;
        [Range(0.03f, 0.10f)] public float CardTag = 0.05f;
        [Range(0.04f, 0.14f)] public float LockGlyph = 0.09f;
        [Range(0.03f, 0.08f)] public float HereChip = 0.045f;
        [Range(0.12f, 0.40f)] public float BigState = 0.26f;
        [Range(0.04f, 0.14f)] public float Hint = 0.075f;
        [Range(0.04f, 0.14f)] public float Foot = 0.08f;

        [Header("Bottom screen (fractions of its height)")]
        [Range(0.06f, 0.20f)] public float BottomHeading = 0.11f;
        [Range(0.10f, 0.32f)] public float BottomName = 0.18f;
        [Range(0.05f, 0.16f)] public float BottomSub = 0.10f;
        [Range(0.05f, 0.14f)] public float BottomLine = 0.085f;
        [Range(0.08f, 0.24f)] public float BottomStatus = 0.13f;
        [Range(0.10f, 0.30f)] public float BottomNotice = 0.17f;
        [Range(0.10f, 0.30f)] public float PosterHeading = 0.17f;
        [Range(0.12f, 0.40f)] public float VoteWord = 0.26f;
        [Range(0.08f, 0.30f)] public float VoteCount = 0.18f;
        [Range(0.05f, 0.14f)] public float VoteFoot = 0.085f;

        [Header("Lever sign (fractions of its height)")]
        [Range(0.30f, 0.70f)] public float SignWord = 0.50f;
        [Range(0.05f, 0.25f)] public float SignChevrons = 0.14f;
        [Range(0.02f, 0.12f)] public float SignFrame = 0.06f;

        public Font ResolvedFont => Font != null && Font.dynamic ? Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private static ConsoleStyle loaded;
        private static ConsoleStyle fallback;
        public static ConsoleStyle Resolve()
        {
            if (loaded != null) return loaded;
            loaded = Resources.Load<ConsoleStyle>(ResourceName);
            if (loaded != null) return loaded;
            if (fallback == null) { fallback = CreateInstance<ConsoleStyle>(); fallback.hideFlags = HideFlags.HideAndDontSave; }
            return fallback;
        }
    }
}
