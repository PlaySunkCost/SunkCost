using SunkCost.Monsters;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // The Weeping Angel's prefab numbers (24 September 2026, the polish pass), written
    // from code so they survive MonsterModelSetup.Apply (which rebuilds the model and
    // the controller) and can be read in one place. Run after the model setup:
    //   SunkCost.Editor.Look.WeepingAngelSetup.Apply()
    // - WeepingAngelStatue: holds the statue the server chose in the Frozen strip.
    // - CreatureGrab, the embrace (Dan: its hands leave its face and take your head, a
    //   beat of stillness, then death), in step with tools/blender/clips/WeepingAngel.py:
    //   the diver is drawn in while the hands leave the face and the grip closes at
    //   1.0 s (the clip's snap, frame 31), the stillness runs to the kill at 2.2 s
    //   (KILL_FRAME 66), the clip holds 0.8 s more (EMBRACE_FRAMES 90). The diver's
    //   feet at HOLD_METERS in front, the eyes turned to the Angel's bared face.
    // - CreatureRig: the sprint plays at the ground speed over the clip's 11.25 m/s;
    //   the rig drives the blends (a quick one into a statue); no head look-at, no
    //   breathing, no lean: anything the layer moves on a frozen Angel would be a
    //   movement under a look.
    public static class WeepingAngelSetup
    {
        public const float HunterClipSpeed = 11.25f;      // clips/WeepingAngel.py RUN_SPEED
        public static readonly Vector3 HoldFeet = new(0f, 0f, 0.70f);   // HOLD_METERS
        public static readonly Vector3 FaceTarget = new(0f, 1.82f, 0.33f); // the posed face during the stillness

        [MenuItem("Sunk Cost/Look/Weeping Angel: apply the prefab's polish settings")]
        public static string Apply()
        {
            string path = MonsterCatalog.PrefabPath(MonsterKind.WeepingAngel);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<WeepingAngel>() == null) return "not the Angel's prefab: " + path;
                if (root.GetComponent<WeepingAngelStatue>() == null) root.AddComponent<WeepingAngelStatue>();

                CreatureGrab grab = root.GetComponent<CreatureGrab>();
                if (grab == null) grab = root.AddComponent<CreatureGrab>();
                using (var s = new SerializedObject(grab))
                {
                    s.FindProperty("gripSeconds").floatValue = 1.0f;
                    s.FindProperty("liftSeconds").floatValue = 1.0f;   // no lift: it holds, it does not raise
                    s.FindProperty("holdSeconds").floatValue = 2.2f;
                    s.FindProperty("releaseSeconds").floatValue = 0.8f;
                    s.FindProperty("gripPoint").vector3Value = HoldFeet;
                    s.FindProperty("liftPoint").vector3Value = HoldFeet;
                    s.FindProperty("faceTarget").vector3Value = FaceTarget;
                    s.FindProperty("shakeStartDegrees").floatValue = 0.3f; // the stillness: a tremor, not a struggle
                    s.FindProperty("shakeDegrees").floatValue = 1.0f;
                    s.FindProperty("shakeHz").floatValue = 4f;
                    s.FindProperty("gripJoltDegrees").floatValue = 6f;      // the hands closing
                    s.FindProperty("turnDegPerSec").floatValue = 360f;
                    s.ApplyModifiedPropertiesWithoutUndo();
                }

                CreatureRig rig = root.GetComponent<CreatureRig>();
                if (rig == null) return "no CreatureRig: run the model setup first";
                using (var s = new SerializedObject(rig))
                {
                    s.FindProperty("matchSpeed").boolValue = true;
                    SerializedProperty strides = s.FindProperty("strides");
                    strides.arraySize = 1;
                    strides.GetArrayElementAtIndex(0).FindPropertyRelative("pose").intValue = (int)CreaturePose.Hunting;
                    strides.GetArrayElementAtIndex(0).FindPropertyRelative("metresPerSecond").floatValue = HunterClipSpeed;
                    s.FindProperty("playbackRange").vector2Value = new Vector2(0.45f, 1.8f);
                    s.FindProperty("standBelow").floatValue = 1f;
                    s.FindProperty("standPose").intValue = (int)CreaturePose.Idle;
                    s.FindProperty("driveTransitions").boolValue = true;
                    s.FindProperty("blendSeconds").floatValue = 0.2f;
                    SerializedProperty blends = s.FindProperty("blends");
                    (CreaturePose pose, float seconds)[] table =
                    {
                        (CreaturePose.Frozen, 0.08f), (CreaturePose.Hunting, 0.12f),
                        (CreaturePose.Grabbing, 0.15f), (CreaturePose.Idle, 0.35f)
                    };
                    blends.arraySize = table.Length;
                    for (int i = 0; i < table.Length; i++)
                    {
                        blends.GetArrayElementAtIndex(i).FindPropertyRelative("pose").intValue = (int)table[i].pose;
                        blends.GetArrayElementAtIndex(i).FindPropertyRelative("seconds").floatValue = table[i].seconds;
                    }
                    s.FindProperty("lookWeight").floatValue = 0f;
                    SerializedProperty off = s.FindProperty("lookOffPoses");
                    off.arraySize = 2;
                    off.GetArrayElementAtIndex(0).intValue = (int)CreaturePose.Frozen;
                    off.GetArrayElementAtIndex(1).intValue = (int)CreaturePose.Grabbing;
                    s.FindProperty("aimAtBeam").boolValue = false;
                    s.FindProperty("leanMaxDegrees").floatValue = 0f;
                    s.FindProperty("breathDegrees").floatValue = 0f;
                    s.FindProperty("rateVariation").floatValue = 0f;
                    s.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            return "Weeping Angel: statue, embrace (grip 1.0 s, kill 2.2 s, release 0.8 s, feet " + HoldFeet + ", face " + FaceTarget + ") and rig settings written to " + path;
        }
    }
}
