using System;
using SunkCost.Editor.Prototype;
using SunkCost.Look;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Widen only the imported ring/bracket, preserving the post/backboard and
    // their placement. Render and collision use the same derived mesh.
    public static class HQBasketballCourtTuning
    {
        public const float RimScale = 1.4f;
        private const string MeshPath = BasketballSetup.Folder + "/ForgivingHoop.asset";

        public static void ApplyTo(Transform hoop)
        {
            Transform visual = hoop.Find("BasketHoop");
            if (visual == null) return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(HQModelSetup.Prefabs + "/BasketHoop.prefab");
            Mesh original = source.GetComponentInChildren<MeshFilter>().sharedMesh;
            Mesh mesh = UnityEngine.Object.Instantiate(original);
            mesh.name = "ForgivingHoop";
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                // This prepared model is in metres, with its rim centred at
                // (0,3.05,1.13). The backboard is behind Z=.72.
                if (p.y < 2.8f || p.y > 3.22f) continue;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.72f, .85f, p.z));
                float scale = Mathf.Lerp(1f, RimScale, blend);
                p.x *= scale; p.z = 1.13f + (p.z - 1.13f) * scale;
                vertices[i] = p;
            }
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, MeshPath); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); }
            visual.GetComponentInChildren<MeshFilter>().sharedMesh = saved;
            visual.GetComponentInChildren<MeshCollider>().sharedMesh = saved;
            var trigger = hoop.Find("Score Trigger");
            using (var score = new SerializedObject(trigger.GetComponent<HoopScore>()))
            {
                score.FindProperty("rimRadius").floatValue = .225f * RimScale;
                score.ApplyModifiedPropertiesWithoutUndo();
            }
            trigger.GetComponent<BoxCollider>().size = new Vector3(.54f, .16f, .54f);
            int n = 0;
            foreach (Transform part in hoop)
            {
                if (part.name != "Net") continue;
                float angle = n++ * Mathf.PI * 2 / 10;
                part.localPosition = new Vector3(Mathf.Cos(angle) * .2f * RimScale, 2.64f, .27f + Mathf.Sin(angle) * .2f * RimScale);
            }
        }

        [MenuItem("Sunk Cost/Prototype/Apply forgiving basketball court")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = EditorSceneManager.OpenScene(HQPrototypeBuilder.ScenePath);
            foreach (string name in new[] { "Hoop W", "Hoop E" }) ApplyTo(GameObject.Find(name).transform);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Both physical rims widened with matching scoring and net; backboards/posts unchanged.");
        }
    }
}
