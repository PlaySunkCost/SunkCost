using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The name over the head — floating text with a drop shadow, no plate (Dan,
    // 20 September 2026, after his reference) — built onto PrototypePlayer.prefab
    // (PlayerNamePlate reads the name and turns it). Run once; a rebuild replaces it.
    public static class PlayerNamePlateSetup
    {
        private const float LineHeight = 0.13f;

        [MenuItem("Sunk Cost/Look/Player name plate onto the player prefab")]
        public static void ApplyFromMenu() => Debug.Log("Name plate: " + (Apply() ? "built" : "unchanged"));

        public static bool Apply()
        {
            string path = Prototype.HQPrototypeBuilder.PlayerPrefabPath;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform old = root.transform.Find(PlayerNamePlate.PlateName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                GameObject plate = new(PlayerNamePlate.PlateName);
                plate.transform.SetParent(root.transform, false);
                plate.transform.localPosition = new Vector3(0f, PlayerNamePlate.HeightMeters, 0f);
                // The name and, a hair behind and below it, its shadow (the same words in dark).
                GameObject shadow = PropBuilder.Text(plate, "Shadow", new Vector3(0.008f, -0.008f, -0.006f), LineHeight, new Color(0f, 0f, 0f, 0.7f), TextAnchor.MiddleCenter);
                GameObject text = PropBuilder.Text(plate, "Name", Vector3.zero, LineHeight, new Color(0.96f, 0.96f, 0.96f, 0.95f), TextAnchor.MiddleCenter);
                text.GetComponent<TextMesh>().text = shadow.GetComponent<TextMesh>().text = "Diver";
                plate.AddComponent<PlayerNamePlate>().Configure(text.GetComponent<TextMesh>(), shadow.GetComponent<TextMesh>());
                foreach (Renderer r in plate.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
