using System.Collections.Generic;
using System.Linq;
using System.Text;
using SunkCost.Monsters;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // How fast each walking clip of a monster model was authored to move (24 September
    // 2026), for CreatureRig's speed-matched playback: the model sampled in edit mode
    // over each looping clip, each foot's planted stretch (its lowest few centimetres)
    // fitted with a straight line, and the speed that foot travels back at is the
    // speed the body must move forward for it to stay put. Measured from the imported
    // clip, so the true length and frame rate are what count. Report only, or write the
    // strides onto the prefab's CreatureRig (that is a prefab write: the editor lock).
    public static class CreatureRigStrides
    {
        private const int Samples = 180;
        private const float ContactBand = 0.03f;      // metres over the foot bone's lowest point (an ankle lifts as the heel peels)
        private const float ToeContactBand = 0.004f;  // over a Toe anchor's lowest point: a planted toe does not lift at all
        private const float MinimumSpeed = 0.05f;     // below this a clip does not walk

        public struct Stride
        {
            public CreaturePose Pose;
            public float Speed;      // m/s the planted foot travels back
            public float Wobble;     // m/s, the spread of that speed across the planted samples
            public float Length;     // s
            public int Planted;      // samples in contact
        }

        [MenuItem("Sunk Cost/Look/Measure monster strides (report)")]
        public static void ReportFromMenu()
        {
            var sb = new StringBuilder();
            foreach (MonsterKind kind in MonsterCatalog.Walkers) if (kind != MonsterKind.Impostor) sb.AppendLine(Report(kind));
            Debug.Log(sb.ToString());
        }

        public static string Report(MonsterKind kind)
        {
            List<Stride> strides = Measure(kind, out string error);
            if (error != null) return kind + ": " + error;
            var sb = new StringBuilder(kind + ":");
            foreach (Stride s in strides)
                sb.Append($" {s.Pose} {s.Speed:0.000} m/s (±{s.Wobble:0.000}, {s.Length:0.00} s, {s.Planted} planted);");
            return sb.ToString();
        }

        // Writes the walking clips' speeds onto the prefab's CreatureRig strides (the
        // other rig settings are left as they are). Take the editor lock first.
        public static string Write(MonsterKind kind, params CreaturePose[] only)
        {
            List<Stride> strides = Measure(kind, out string error);
            if (error != null) return kind + ": " + error;
            List<Stride> walking = strides.Where(s => s.Speed >= MinimumSpeed && (only == null || only.Length == 0 || only.Contains(s.Pose))).ToList();
            string path = MonsterCatalog.PrefabPath(kind);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CreatureRig rig = root.GetComponent<CreatureRig>();
                if (rig == null) return kind + ": no CreatureRig on the prefab";
                using var so = new SerializedObject(rig);
                SerializedProperty list = so.FindProperty("strides");
                list.arraySize = walking.Count;
                for (int i = 0; i < walking.Count; i++)
                {
                    SerializedProperty e = list.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("pose").intValue = (int)walking[i].Pose;
                    e.FindPropertyRelative("metresPerSecond").floatValue = Mathf.Round(walking[i].Speed * 1000f) / 1000f;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return kind + " strides written: " + string.Join(", ", walking.Select(s => $"{s.Pose} {s.Speed:0.000}"));
        }

        public static List<Stride> Measure(MonsterKind kind, out string error)
        {
            error = null;
            var result = new List<Stride>();
            string fbx = MonsterModelSetup.FbxPath(kind);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (asset == null) { error = "no model at " + fbx; return result; }
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            GameObject model = Object.Instantiate(asset);
            model.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                // The planted point: a Toe anchor (tools/blender/clips/ik_kit.add_toe_anchors) when
                // the model has them, else the foot bones (the ankle, which lifts as a heel peels).
                Transform[] all = model.GetComponentsInChildren<Transform>(true);
                List<Transform> feet = all.Where(t => t.name == "ToeL" || t.name == "ToeR").ToList();
                bool toes = feet.Count > 0;
                if (!toes) feet = all.Where(t => t.name.StartsWith("Foot") || t.name.StartsWith("Hand")).ToList();
                float band = toes ? ToeContactBand : ContactBand;
                foreach (AnimationClip clip in clips)
                {
                    if (!System.Enum.TryParse(clip.name, out CreaturePose pose) || !clip.isLooping) continue;
                    var track = new Dictionary<Transform, Vector3[]>();
                    foreach (Transform f in feet) track[f] = new Vector3[Samples];
                    for (int i = 0; i < Samples; i++)
                    {
                        clip.SampleAnimation(model, clip.length * i / Samples);
                        foreach (Transform f in feet) track[f][i] = f.position;
                    }
                    float dt = clip.length / Samples;
                    var speeds = new List<(float speed, float weight)>();
                    var spread = new List<float>();
                    int planted = 0;
                    foreach (Transform f in feet)
                    {
                        Vector3[] p = track[f];
                        float low = p.Min(v => v.y), high = p.Max(v => v.y);
                        // A hand that never comes near the ground is an arm, not a front foot.
                        if (f.name.StartsWith("Hand") && low > 0.3f) continue;
                        if (high - low < 0.01f) continue; // it does not step
                        bool[] contact = p.Select(v => v.y <= low + band).ToArray();
                        foreach ((int start, int count) in Windows(contact))
                        {
                            if (count < 4) continue;
                            // A least-squares line through z over time: its slope is the foot's speed.
                            double st = 0, sz = 0, stt = 0, stz = 0;
                            for (int k = 0; k < count; k++)
                            {
                                double t = k * dt, z = p[(start + k) % Samples].z;
                                st += t; sz += z; stt += t * t; stz += t * z;
                            }
                            double den = count * stt - st * st;
                            if (den <= 1e-9) continue;
                            float slope = (float)((count * stz - st * sz) / den);
                            speeds.Add((-slope, count));
                            planted += count;
                            for (int k = 1; k < count; k++)
                                spread.Add((p[(start + k) % Samples].z - p[(start + k - 1) % Samples].z) / -dt - (-slope));
                        }
                    }
                    float total = speeds.Sum(s => s.weight);
                    float speed = total > 0f ? speeds.Sum(s => s.speed * s.weight) / total : 0f;
                    float wobble = spread.Count > 1 ? Mathf.Sqrt(spread.Sum(x => x * x) / spread.Count) : 0f;
                    result.Add(new Stride { Pose = pose, Speed = speed, Wobble = wobble, Length = clip.length, Planted = planted });
                }
            }
            finally { Object.DestroyImmediate(model); }
            return result;
        }

        // The contiguous runs of true, the loop's end joined to its start.
        private static IEnumerable<(int start, int count)> Windows(bool[] on)
        {
            int n = on.Length;
            if (on.All(b => b)) { yield return (0, n); yield break; }
            int first = System.Array.IndexOf(on, false);
            int i = 0;
            while (i < n)
            {
                int k = (first + i) % n;
                if (!on[k]) { i++; continue; }
                int start = k, count = 0;
                while (i < n && on[(first + i) % n]) { count++; i++; }
                yield return (start, count);
            }
        }
    }
}
