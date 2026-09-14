using UnityEngine;

namespace SunkCost.World
{
    // Full-screen black with a line of text, drawn last (IMGUI like the rest of the
    // prototype UI). Local only: it is driven by SyncVar OnChange callbacks and the
    // local scene-load events, never by an RPC alone (contract section 6).
    public sealed class ScreenFade : MonoBehaviour
    {
        public static ScreenFade Instance { get; private set; }

        private Texture2D black;
        private GUIStyle textStyle;
        private float alpha;          // current
        private float target;         // 0 or 1
        private float speed;          // alpha units per second; 0 = instant
        private string text = string.Empty;

        public float Alpha => alpha;
        public bool IsBlack => alpha >= 0.999f;
        public bool IsClear => alpha <= 0.001f;
        public string Text => text;
        // How many fades to black were started; the checks count them because a
        // Local transition can be over before anyone polls the alpha.
        public int FadeOutCount { get; private set; }

        private void Awake()
        {
            Instance = this;
            black = new Texture2D(1, 1);
            black.SetPixel(0, 0, Color.black);
            black.Apply();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Go black over `seconds` and show `label` once black.
        public void FadeOut(float seconds, string label)
        {
            text = label ?? string.Empty;
            target = 1f;
            speed = seconds <= 0f ? 0f : 1f / seconds;
            FadeOutCount++;
        }

        // Stay black (or become black now) with a label.
        public void HoldBlack(string label)
        {
            text = label ?? string.Empty;
            target = 1f;
            speed = 0f;
            FadeOutCount++;
        }

        public void FadeIn(float seconds)
        {
            target = 0f;
            speed = seconds <= 0f ? 0f : 1f / seconds;
        }

        public void ClearNow()
        {
            target = 0f;
            alpha = 0f;
            text = string.Empty;
        }

        private void Update()
        {
            if (speed <= 0f) alpha = target;
            else alpha = Mathf.MoveTowards(alpha, target, speed * Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            if (alpha <= 0f) return;
            GUI.depth = -1000; // in front of the HUD and the session menu
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), black);
            GUI.color = previous;
            if (alpha >= 0.95f && !string.IsNullOrEmpty(text))
            {
                if (textStyle == null)
                {
                    textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
                    textStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f);
                }
                GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, 60f), text, textStyle);
            }
        }
    }
}
