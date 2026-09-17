using System.Text;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    // Where the neck is: the character model's vertex heights in 5 cm bins with
    // the width of the mesh at each, printed for whoever tunes the head split.
    public static class CharacterModelProbe
    {
        public static string Report()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                var sb = new StringBuilder();
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Mesh mesh = filter.sharedMesh;
                    Transform t = filter.transform;
                    sb.Append(filter.name).Append(": verts=").Append(mesh.vertexCount).Append(" bounds=").Append(mesh.bounds.size.ToString("F2")).Append(" lossyScale=").Append(t.lossyScale.ToString("F2")).Append(" localPos=").Append(t.localPosition.ToString("F2")).Append('\n');
                    if (mesh.vertexCount < 100) continue;
                    Vector3[] v = mesh.vertices;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    foreach (Vector3 p in v) { Vector3 w = root.transform.InverseTransformPoint(t.TransformPoint(p)); minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y); }
                    int bins = Mathf.CeilToInt((maxY - minY) / 0.05f) + 1;
                    var minX = new float[bins]; var maxX = new float[bins]; var count = new int[bins];
                    for (int i = 0; i < bins; i++) { minX[i] = float.MaxValue; maxX[i] = float.MinValue; }
                    foreach (Vector3 p in v)
                    {
                        Vector3 w = root.transform.InverseTransformPoint(t.TransformPoint(p));
                        int b = Mathf.Clamp((int)((w.y - minY) / 0.05f), 0, bins - 1);
                        minX[b] = Mathf.Min(minX[b], w.x); maxX[b] = Mathf.Max(maxX[b], w.x); count[b]++;
                    }
                    for (int i = 0; i < bins; i++)
                        if (count[i] > 0) sb.Append($"  y {minY + i * 0.05f:0.00}..{minY + (i + 1) * 0.05f:0.00}: width {maxX[i] - minX[i]:0.00} ({count[i]} verts)\n");
                }
                return sb.ToString();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
