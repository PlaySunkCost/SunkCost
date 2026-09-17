using System.Collections.Generic;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Cuts the character model at the neck, once, in the editor: the body and the
    // head become two mesh assets under Models/Generated (the .blend is untouched),
    // the model's MeshFilter shows the body and a Head child shows the head with
    // the same materials. PlayerHeadSplit then hides the head from the owner. Run
    // through the movement and hands setup or by itself; re-run after
    // HQPrototypeBuilder.RebuildPrefabs or a new model.
    public static class PlayerHeadSplitSetup
    {
        public const string GeneratedFolder = "Assets/_Project/Art/Prototype/Models/Generated";
        // The neck, in the model's own unscaled frame (metres up from its feet):
        // GenericCharacter is narrowest at 0.66..0.71 (CharacterModelProbe).
        public const float NeckHeight = 0.685f;
        public const string NeckPlugName = "NeckPlug";
        public const string NeckMaterialPath = HQPrototypeBuilder.MaterialPath + "/PlayerNeck.mat";

        [MenuItem("Sunk Cost/Prototype/Apply head split (first-person body)")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                HQPlayerController controller = root.GetComponent<HQPlayerController>();
                if (controller == null) throw new System.InvalidOperationException("Player prefab needs HQPlayerController first.");
                if (root.GetComponent<PlayerHeadSplit>() == null) { root.AddComponent<PlayerHeadSplit>(); changes.Add("PlayerHeadSplit added"); }
                Transform model = null;
                foreach (Transform child in root.transform)
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) { model = child; break; }
                if (model == null) throw new System.InvalidOperationException("Player prefab has no character model instance.");
                MeshFilter bodyFilter = null;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.name != PlayerHeadSplit.HeadName && (bodyFilter == null || filter.sharedMesh.vertexCount > bodyFilter.sharedMesh.vertexCount)) bodyFilter = filter;
                if (bodyFilter == null) throw new System.InvalidOperationException("The character model has no mesh.");
                Mesh source = bodyFilter.sharedMesh;
                string bodyPath = GeneratedFolder + "/" + model.name + "_Body.asset", headPath = GeneratedFolder + "/" + model.name + "_Head.asset";
                bool alreadyCut = AssetDatabase.GetAssetPath(source) == bodyPath;
                if (!alreadyCut)
                {
                    HQPrototypeBuilder.EnsureFolder(GeneratedFolder);
                    Cut(source, bodyFilter.transform, root.transform, NeckHeight * model.localScale.y, out Mesh body, out Mesh head);
                    if (head.triangles.Length == 0) throw new System.InvalidOperationException("The cut found no head above " + NeckHeight + " m: check the model's axes.");
                    AssetDatabase.CreateAsset(body, bodyPath);
                    AssetDatabase.CreateAsset(head, headPath);
                    bodyFilter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(bodyPath);
                    changes.Add($"{source.name} cut at the neck: body {body.triangles.Length / 3} triangles, head {head.triangles.Length / 3}");
                }
                Transform headObject = bodyFilter.transform.Find(PlayerHeadSplit.HeadName);
                if (headObject == null)
                {
                    var go = new GameObject(PlayerHeadSplit.HeadName);
                    go.transform.SetParent(bodyFilter.transform, false);
                    go.layer = bodyFilter.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(headPath);
                    MeshRenderer bodyRenderer = bodyFilter.GetComponent<MeshRenderer>();
                    MeshRenderer headRenderer = go.AddComponent<MeshRenderer>();
                    if (bodyRenderer != null) { headRenderer.sharedMaterials = bodyRenderer.sharedMaterials; headRenderer.shadowCastingMode = bodyRenderer.shadowCastingMode; }
                    changes.Add("Head child added");
                }
                // The model is hollow: without a plug the owner looks down into its own
                // torso. A dark, flattened sphere fills the neck (inside the head for a friend).
                Transform plug = bodyFilter.transform.Find(NeckPlugName);
                if (plug == null)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = NeckPlugName;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.SetParent(bodyFilter.transform, false);
                    go.layer = bodyFilter.gameObject.layer;
                    // The neck in the model's own (mesh) frame: the importer keeps Z up there.
                    Vector3 up = bodyFilter.transform.InverseTransformDirection(root.transform.up).normalized;
                    Vector3 neckWorld = root.transform.TransformPoint(new Vector3(0f, NeckHeight * model.localScale.y, 0f));
                    go.transform.localPosition = bodyFilter.transform.InverseTransformPoint(neckWorld) + up * 0.02f;
                    go.transform.localScale = Vector3.one * 0.30f - up * 0.30f * 0.65f; // flattened along up
                    go.GetComponent<Renderer>().sharedMaterial = HQPrototypeBuilder.GetOrCreateMaterial(NeckMaterialPath, new Color(0.10f, 0.10f, 0.12f));
                    changes.Add("NeckPlug added");
                }
                if (changes.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Head split already applied" : "Head split: " + string.Join("; ", changes);
        }

        // Every triangle whose three vertices are above the neck, measured up the
        // player's root (the importer keeps the .blend's Z-up axes in mesh space and
        // rotates the model object, so mesh y is not height).
        private static void Cut(Mesh source, Transform filter, Transform root, float neck, out Mesh body, out Mesh head)
        {
            body = Object.Instantiate(source); head = Object.Instantiate(source);
            body.name = source.name + "_Body"; head.name = source.name + "_Head";
            Vector3[] vertices = source.vertices;
            var height = new float[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) height[i] = root.InverseTransformPoint(filter.TransformPoint(vertices[i])).y;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] tris = source.GetTriangles(sub);
                var bodyTris = new List<int>(tris.Length); var headTris = new List<int>(tris.Length / 4);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    bool above = height[tris[i]] > neck && height[tris[i + 1]] > neck && height[tris[i + 2]] > neck;
                    (above ? headTris : bodyTris).AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
                }
                body.SetTriangles(bodyTris, sub); head.SetTriangles(headTris, sub);
            }
            body.RecalculateBounds(); head.RecalculateBounds();
        }
    }
}
