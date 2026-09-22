using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SunkCost.Interaction;
using SunkCost.Monsters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Dan's Blender model into a monster prefab (docs/MONSTER_MODELS.md, 22 September
    // 2026): the FBX under Assets/_Project/Models/Monsters/<Kind>/<Kind>.fbx is
    // imported as a Generic rig with its clips named after CreaturePose, an
    // AnimatorController is built from those clips (Any State → the pose's state on
    // the Pose int), the model is nested under the prefab as "Model" in place of the
    // code-built placeholder, CreatureRig is wired to its anchors, CreatureLook's body
    // and eyes point at it, and the eye height follows an "Eye" node when there is
    // one. The brain, the collider shape, the layer and the network parts are
    // untouched: the model is a look. Re-run after every re-export; run it again after
    // "Apply monster setup" with force, which rebuilds the placeholders.
    public static class MonsterModelSetup
    {
        public const string ModelFolder = "Assets/_Project/Models/Monsters";
        public const string ControllerFolder = "Assets/_Project/Animation/Monsters";
        // Poses whose clips loop; Shooting plays once and holds its last frame.
        private static readonly CreaturePose[] Loops = { CreaturePose.Idle, CreaturePose.Drawn, CreaturePose.Hunting, CreaturePose.Frozen, CreaturePose.Windup, CreaturePose.Rushing, CreaturePose.Fleeing };
        private static readonly string[] EyeNodeNames = { "Eye", "Eyes", "EyePoint", "EyeHeight" };

        [MenuItem("Sunk Cost/Look/Apply monster models (every kind with an FBX)")]
        public static void ApplyAllFromMenu() => Debug.Log(ApplyAll());

        public static string ApplyAll()
        {
            var lines = new List<string>();
            foreach (MonsterKind kind in MonsterCatalog.Walkers)
                if (File.Exists(FbxPath(kind))) lines.Add(Apply(kind));
            return lines.Count == 0 ? "No monster FBX under " + ModelFolder : string.Join("\n", lines);
        }

        public static string FbxPath(MonsterKind kind)
        {
            string nested = $"{ModelFolder}/{kind}/{kind}.fbx";
            return File.Exists(nested) ? nested : $"{ModelFolder}/{kind}.fbx";
        }
        public static string ControllerPath(MonsterKind kind) => $"{ControllerFolder}/{kind}.controller";

        public static string Apply(MonsterKind kind)
        {
            string fbx = FbxPath(kind);
            if (!File.Exists(fbx)) return $"{kind}: no FBX at {fbx}";
            string prefabPath = MonsterCatalog.PrefabPath(kind);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return $"{kind}: no prefab (run the monster setup first)";
            var report = new List<string>();

            // 1. The importer: a Generic rig, its takes as clips named by the pose, looping where the pose does.
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null) return $"{kind}: {fbx} is not a model";
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                // Blender's exporter names a take after its object: "Armature|Idle". The
                // pose is what follows the last bar; the clip is renamed to it.
                string poseName = clip.name.Contains('|') ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
                if (Enum.TryParse(poseName, out CreaturePose _)) clip.name = poseName;
                bool loops = Loops.Any(p => p.ToString() == clip.name);
                clip.loopTime = loops;
                clip.loopPose = false;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.lockRootRotation = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            // 2. The clips, by pose.
            var loaded = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            var byPose = new Dictionary<CreaturePose, AnimationClip>();
            foreach (CreaturePose pose in Enum.GetValues(typeof(CreaturePose)))
            {
                AnimationClip clip = loaded.FirstOrDefault(c => c.name == pose.ToString());
                if (clip != null) byPose[pose] = clip;
            }
            report.Add($"clips: {string.Join(", ", byPose.Keys)}" + (loaded.Count > byPose.Count ? $" (ignored: {string.Join(", ", loaded.Where(c => !byPose.ContainsValue(c)).Select(c => c.name))})" : string.Empty));

            // 3. The controller: one state per clip, Any State → it when Pose says so; Idle (or the first) by default.
            Directory.CreateDirectory(ControllerFolder);
            string controllerPath = ControllerPath(kind);
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Pose", AnimatorControllerParameterType.Int);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState first = null, idle = null;
            int row = 0;
            foreach (KeyValuePair<CreaturePose, AnimationClip> pair in byPose)
            {
                AnimatorState state = machine.AddState(pair.Key.ToString(), new Vector3(300f, 60f * row++, 0f));
                state.motion = pair.Value;
                AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
                transition.AddCondition(AnimatorConditionMode.Equals, (int)pair.Key, "Pose");
                transition.canTransitionToSelf = false;
                transition.hasExitTime = false;
                transition.duration = 0.15f;
                first ??= state;
                if (pair.Key == CreaturePose.Idle) idle = state;
            }
            if (idle != null || first != null) machine.defaultState = idle ?? first;
            EditorUtility.SetDirty(controller);

            // 4. The prefab: the model nested as "Model" in place of the placeholder, the rig and the look wired to it.
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform old = root.transform.Find(CreatureRig.ModelName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                Transform placeholder = root.transform.Find("Body");
                if (placeholder != null && kind != MonsterKind.Impostor) { UnityEngine.Object.DestroyImmediate(placeholder.gameObject); report.Add("placeholder body removed"); }
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, root.transform);
                model.name = CreatureRig.ModelName;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                Transform beamOrigin = FindNamed(model.transform, CreatureRig.BeamOriginName);
                Transform voice = FindNamed(model.transform, CreatureRig.VoiceName);
                CreatureRig rig = root.GetComponent<CreatureRig>();
                if (rig == null) rig = root.AddComponent<CreatureRig>();
                using (var serialized = new SerializedObject(rig))
                {
                    serialized.FindProperty("animator").objectReferenceValue = animator;
                    serialized.FindProperty("beamOrigin").objectReferenceValue = beamOrigin;
                    serialized.FindProperty("voice").objectReferenceValue = voice;
                    SerializedProperty poses = serialized.FindProperty("animatedPoses");
                    poses.arraySize = byPose.Count;
                    int i = 0;
                    foreach (CreaturePose pose in byPose.Keys) poses.GetArrayElementAtIndex(i++).stringValue = pose.ToString();
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                report.Add($"anchors: beam {(beamOrigin != null ? "yes" : "no")}, voice {(voice != null ? "yes" : "no")}");

                CreatureLook look = root.GetComponent<CreatureLook>();
                Renderer[] eyes = model.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Eye")).ToArray();
                if (look != null)
                    using (var serialized = new SerializedObject(look))
                    {
                        serialized.FindProperty("body").objectReferenceValue = model.transform;
                        SerializedProperty eyeList = serialized.FindProperty("eyes");
                        eyeList.arraySize = eyes.Length;
                        for (int i = 0; i < eyes.Length; i++) eyeList.GetArrayElementAtIndex(i).objectReferenceValue = eyes[i];
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                report.Add($"eyes: {eyes.Length}");

                // The eye height (where it looks from and is looked at) follows an Eye node when the model has one.
                Transform eyeNode = EyeNodeNames.Select(n => FindNamed(model.transform, n)).FirstOrDefault(t => t != null);
                Creature creature = root.GetComponent<Creature>();
                if (eyeNode != null && creature != null)
                {
                    float height = root.transform.InverseTransformPoint(eyeNode.position).y;
                    using (var serialized = new SerializedObject(creature))
                    {
                        serialized.FindProperty("eyeHeight").floatValue = height;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    report.Add($"eye height {height:0.00} m from '{eyeNode.name}'");
                }

                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
                CarryableCollisionPolicy.SetLayerRecursively(root, MonsterCatalog.Layer);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return $"{kind}: {fbx} → {prefabPath}; " + string.Join("; ", report);
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
