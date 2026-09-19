using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The name plate over the head, built onto PrototypePlayer.prefab in the kit's
    // materials (PlayerNamePlate reads the name and turns it). Run once; a rebuild
    // replaces the old plate.
    public static class PlayerNamePlateSetup
    {
        private const float Width = 1f, Height = 0.3f, LineHeight = 0.17f;

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
                Transform board = Part(plate, "Plate", MeshKit.Box(new Vector3(Width, Height, 0.06f)), LookMaterials.SignBoard(), Vector3.zero);
                Transform top = Part(plate, "Frame Top", MeshKit.Box(new Vector3(Width, 0.03f, 0.02f)), LookMaterials.SignGlow(), new Vector3(0f, Height / 2f - 0.02f, 0.03f));
                Transform bottom = Part(plate, "Frame Bottom", MeshKit.Box(new Vector3(Width, 0.03f, 0.02f)), LookMaterials.SignGlow(), new Vector3(0f, -Height / 2f + 0.02f, 0.03f));
                GameObject text = PropBuilder.Text(plate, "Name", new Vector3(0f, 0f, 0.04f), LineHeight, Color.white, TextAnchor.MiddleCenter);
                text.GetComponent<TextMesh>().text = "Diver";
                plate.AddComponent<PlayerNamePlate>().Configure(text.GetComponent<TextMesh>(), new[] { board, top, bottom }, Width);
                foreach (Renderer r in plate.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // A mesh part with no collider (nothing about a name blocks anyone).
        private static Transform Part(GameObject parent, string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}
