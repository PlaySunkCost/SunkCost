using UnityEngine;

namespace SunkCost.Look
{
    // A TextMesh that shows one line of HQSigns by key. Runs in the editor too,
    // so the boards read right in the scene view and follow the asset.
    [ExecuteAlways]
    [RequireComponent(typeof(TextMesh))]
    public sealed class SignText : MonoBehaviour
    {
        [SerializeField] private string key;
        [Tooltip("The box the text must fit, in metres (0 = no fit): the character size shrinks for long or many-lined text.")]
        [SerializeField] private Vector2 fit;
        [SerializeField] private float baseCharacterSize;
        [Tooltip("Labels sharing a group take one size, the one the longest needs (the console's buttons: SHIP-050). Empty = fit alone.")]
        [SerializeField] private string fitGroup;
        private int shownVersion = -1;
        private string shownKey;

        public string Key => key;
        public void Configure(string value) { key = value; shownVersion = -1; Refresh(); }
        public void Fit(float width, float height) { fit = new Vector2(width, height); shownVersion = -1; Refresh(); }
        public void Group(string name) { fitGroup = name; shownVersion = -1; Refresh(); }
        public string FitGroupName => fitGroup;

        private void OnEnable() { shownVersion = -1; Refresh(); }
        private float nextCheck;
        private void Update()
        {
            // A few times a second is plenty for a sign; Resolve may hit Resources
            // until the asset exists.
            float now = Application.isPlaying ? Time.unscaledTime : (float)UnityEditor_Time();
            if (now < nextCheck) return;
            nextCheck = now + 0.25f;
            HQSigns signs = HQSigns.Resolve();
            if (signs.Version != shownVersion || shownKey != key) Refresh();
        }

        // A TextMesh line is about characterSize x fontSize x 0.135 m tall and a
        // bold capital about 0.62 of that wide: shrink to the box, never grow past
        // the size the builder chose.
        private void FitText(TextMesh mesh, string text)
        {
            if (baseCharacterSize <= 0f) baseCharacterSize = mesh.characterSize;
            if (!string.IsNullOrEmpty(fitGroup)) { FitGroup(); return; }
            if (fit.x <= 0f || fit.y <= 0f || string.IsNullOrEmpty(text)) { mesh.characterSize = baseCharacterSize; return; }
            string[] lines = text.Split('\n');
            int longest = 1;
            foreach (string line in lines) longest = Mathf.Max(longest, line.Length);
            float perUnit = Mathf.Max(mesh.fontSize, 1) * 0.135f;
            float byHeight = fit.y / (lines.Length * perUnit * 1.05f);
            float byWidth = fit.x / (longest * perUnit * 0.62f);
            float size = Mathf.Min(baseCharacterSize, byHeight, byWidth);
            if (!Mathf.Approximately(mesh.characterSize, size)) mesh.characterSize = size;
        }

        // A group is measured (TextFit), not estimated: every member of the group
        // under the same root fits its own box, then all take the smallest size.
        private void FitGroup()
        {
            float size = float.MaxValue;
            var members = new System.Collections.Generic.List<SignText>();
            foreach (SignText other in transform.root.GetComponentsInChildren<SignText>(true))
                if (other.fitGroup == fitGroup) members.Add(other);
            foreach (SignText member in members) size = Mathf.Min(size, member.OwnFit());
            foreach (SignText member in members)
            {
                TextMesh m = member.GetComponent<TextMesh>();
                if (m != null && !Mathf.Approximately(m.characterSize, size)) m.characterSize = size;
            }
        }

        private float OwnFit()
        {
            TextMesh mesh = GetComponent<TextMesh>();
            if (mesh == null) return float.MaxValue;
            if (baseCharacterSize <= 0f) baseCharacterSize = mesh.characterSize;
            string text = string.IsNullOrEmpty(key) ? mesh.text : HQSigns.Resolve().Get(key);
            if (mesh.text != text) mesh.text = text;
            return TextFit.Fit(mesh, fit, baseCharacterSize);
        }

        private static double UnityEditor_Time()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.unscaledTime;
#endif
        }

        public void Refresh()
        {
            HQSigns signs = HQSigns.Resolve();
            TextMesh mesh = GetComponent<TextMesh>();
            if (mesh == null || string.IsNullOrEmpty(key)) return;
            string text = signs.Get(key);
            if (mesh.text != text) mesh.text = text;
            FitText(mesh, text);
            shownVersion = signs.Version;
            shownKey = key;
        }
    }
}
