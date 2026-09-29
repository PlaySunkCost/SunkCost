using UnityEngine;

namespace SunkCost.UI
{
    [CreateAssetMenu(menuName = "Sunk Cost/Menu theme")]
    public sealed class MenuTheme : ScriptableObject
    {
        public Sprite Background;
        public Sprite Logo;
        public Sprite ButtonPlate;
        public Font Font;
        public Font ButtonFont;
        public Color Panel = new(.045f, .05f, .052f);
        public Color Text = new(.91f, .88f, .79f);
        public Color Accent = new(1, .65f, .16f);
        private static MenuTheme loaded;
        public static MenuTheme Current
        {
            get
            {
                if (loaded == null) loaded = Resources.Load<MenuTheme>("MenuTheme");
                if (loaded == null) { loaded = CreateInstance<MenuTheme>(); loaded.hideFlags = HideFlags.DontSave; }
                return loaded;
            }
        }
    }
}
