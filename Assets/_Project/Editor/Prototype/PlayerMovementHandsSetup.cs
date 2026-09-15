using System;
using System.Collections.Generic;
using SunkCost.Interaction;
using SunkCost.Player;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SunkCost.Editor.Prototype
{
    // Targeted, repeatable setup for docs/PLAYER_MOVEMENT_HANDS_IMPLEMENTATION_PLAN.md:
    // the Player/Carryable layers and their ignore pair, the movement settings
    // asset, the player prefab (ForwardMarker gone, stance + hands components,
    // the arm geometry, centred hold points, the Player layer) and the
    // carryable prefabs (grip targets, the Carryable layer). Patches the
    // existing prefabs in place: GUIDs, the character model and the scene
    // overrides stay. Run twice: no duplicates, no changes.
    public static class PlayerMovementHandsSetup
    {
        public const string SettingsPath = "Assets/_Project/Settings/Prototype/PlayerMovementSettings.asset";
        public const string GloveMaterialPath = HQPrototypeBuilder.MaterialPath + "/PlayerGloves.mat";
        public const string ForwardMarkerName = "ForwardMarker";
        public static readonly Vector3 CenteredHoldPointLocalPosition = new(0f, -0.30f, 0.55f);
        public const float UpperArmLength = 0.30f;
        public const float ForearmLength = 0.30f;
        // The placeholder character model (Dor's, 1.19 m) is left at its own scale:
        // scaled to the 1.8 m capsule its head swallows the camera. The arms hang
        // from an anchor below the eyes, so on friends' screens they meet the item
        // above the small figure; the visible-body card owns the real proportions.
        public const float CharacterModelScale = 1.0f;
        public static readonly Vector3 TorsoLocalPosition = new(0f, -0.45f, 0f);

        [MenuItem("Sunk Cost/Prototype/Apply movement and hands setup")]
        public static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before applying the movement and hands setup.");
            var changes = new List<string>();
            changes.AddRange(EnsureLayers());
            PlayerMovementSettings settings = EnsureSettings(changes);
            Material gloves = HQPrototypeBuilder.GetOrCreateMaterial(GloveMaterialPath, new Color(0.85f, 0.72f, 0.25f));
            changes.AddRange(PatchPlayerPrefab(settings, gloves));
            changes.AddRange(PatchCarryablePrefabs());
            AssetDatabase.SaveAssets();
            return changes.Count == 0 ? "Movement and hands already set up" : "Movement and hands: " + string.Join("; ", changes);
        }

        // ---- layers -------------------------------------------------------------------

        private static IEnumerable<string> EnsureLayers()
        {
            var changes = new List<string>();
            Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject serialized = new(tagManager);
            SerializedProperty layers = serialized.FindProperty("layers");
            foreach (string name in new[] { CarryableCollisionPolicy.PlayerLayerName, CarryableCollisionPolicy.CarryableLayerName })
            {
                if (LayerMask.NameToLayer(name) >= 0) continue;
                int free = -1;
                for (int i = 8; i < layers.arraySize; i++)
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { free = i; break; }
                if (free < 0) throw new InvalidOperationException("No free user layer for " + name + ".");
                layers.GetArrayElementAtIndex(free).stringValue = name;
                changes.Add("layer " + name + " = " + free);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            int player = LayerMask.NameToLayer(CarryableCollisionPolicy.PlayerLayerName);
            int carryable = LayerMask.NameToLayer(CarryableCollisionPolicy.CarryableLayerName);
            if (player >= 0 && carryable >= 0 && !Physics.GetIgnoreLayerCollision(player, carryable))
            {
                Physics.IgnoreLayerCollision(player, carryable, true);
                changes.Add("Player/Carryable collision ignored");
            }
            return changes;
        }

        // ---- settings ----------------------------------------------------------------

        public static PlayerMovementSettings EnsureSettings(List<string> changes)
        {
            HQPrototypeBuilder.EnsureFolder("Assets/_Project/Settings/Prototype");
            PlayerMovementSettings settings = AssetDatabase.LoadAssetAtPath<PlayerMovementSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PlayerMovementSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            changes?.Add("PlayerMovementSettings created");
            return settings;
        }

        // ---- player prefab -------------------------------------------------------------

        private static IEnumerable<string> PatchPlayerPrefab(PlayerMovementSettings settings, Material gloves)
        {
            var changes = new List<string>();
            GameObject root = PrefabUtility.LoadPrefabContents(HQPrototypeBuilder.PlayerPrefabPath);
            try
            {
                if (PatchPlayer(root, settings, gloves, changes))
                    PrefabUtility.SaveAsPrefabAsset(root, HQPrototypeBuilder.PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return changes;
        }

        // Shared with the builder's fresh prefab: everything the card needs on a
        // player root. Returns true when something changed.
        public static bool PatchPlayer(GameObject root, PlayerMovementSettings settings, Material gloves, List<string> changes)
        {
            bool changed = false;
            HQPlayerController controller = root.GetComponent<HQPlayerController>();
            if (controller == null) throw new InvalidOperationException("Player prefab has no HQPlayerController.");

            // The rectangle in front of the avatar, wherever it sits.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != ForwardMarkerName) continue;
                Object.DestroyImmediate(t.gameObject);
                changes.Add("ForwardMarker removed");
                changed = true;
                break;
            }

            if (root.GetComponent<PlayerStance>() == null) { root.AddComponent<PlayerStance>(); changes.Add("PlayerStance added"); changed = true; }
            PlayerHands hands = root.GetComponent<PlayerHands>();
            if (hands == null) { hands = root.AddComponent<PlayerHands>(); changes.Add("PlayerHands added"); changed = true; }

            Transform viewPivot = root.transform.Find("ViewPivot");
            Transform camera = viewPivot != null ? viewPivot.Find("PlayerCamera") : null;
            Transform hold = camera != null ? camera.Find("HoldPoint") : null;
            if (hold != null && hold.localPosition != CenteredHoldPointLocalPosition)
            {
                hold.localPosition = CenteredHoldPointLocalPosition;
                changes.Add("HoldPoint centred");
                changed = true;
            }

            // The body the crouch squashes: the character model instance, else the capsule.
            Transform bodyVisual = null;
            foreach (Transform child in root.transform)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) { bodyVisual = child; break; }
            if (bodyVisual != null && Mathf.Abs(bodyVisual.localScale.y - CharacterModelScale) > 1e-3f)
            {
                bodyVisual.localScale = Vector3.one * CharacterModelScale;
                changes.Add("character model scaled to the capsule");
                changed = true;
            }
            if (bodyVisual == null) bodyVisual = root.transform.Find("Body");

            // Arms: torso and shoulders under the view pivot (they follow the eye
            // height through the crouch blend), the segments under the root.
            Transform torso = viewPivot != null ? viewPivot.Find(PlayerHands.TorsoName) : null;
            if (torso == null && viewPivot != null)
            {
                torso = new GameObject(PlayerHands.TorsoName).transform;
                torso.SetParent(viewPivot, false);
                changes.Add("Torso anchor added");
                changed = true;
            }
            if (torso != null && torso.localPosition != TorsoLocalPosition) { torso.localPosition = TorsoLocalPosition; changed = true; }
            Transform shoulderR = EnsureChild(torso, PlayerHands.ShoulderRightName, new Vector3(0.2f, 0f, 0f), ref changed, changes);
            Transform shoulderL = EnsureChild(torso, PlayerHands.ShoulderLeftName, new Vector3(-0.2f, 0f, 0f), ref changed, changes);
            Transform armR = root.transform.Find(PlayerHands.ArmRightName);
            if (armR == null) { armR = BuildArm(root.transform, PlayerHands.ArmRightName, gloves, mirror: false); changes.Add("right arm built"); changed = true; }
            Transform armL = root.transform.Find(PlayerHands.ArmLeftName);
            if (armL == null) { armL = BuildArm(root.transform, PlayerHands.ArmLeftName, gloves, mirror: true); changes.Add("left arm built"); changed = true; }

            SerializedObject handsSerialized = new(hands);
            if (handsSerialized.FindProperty("upperArmLength").floatValue != UpperArmLength) { handsSerialized.FindProperty("upperArmLength").floatValue = UpperArmLength; changed = true; }
            if (handsSerialized.FindProperty("forearmLength").floatValue != ForearmLength) { handsSerialized.FindProperty("forearmLength").floatValue = ForearmLength; changed = true; }
            changed |= SetRef(handsSerialized, "torso", torso);
            changed |= SetRef(handsSerialized, "shoulderRight", shoulderR);
            changed |= SetRef(handsSerialized, "shoulderLeft", shoulderL);
            changed |= SetRef(handsSerialized, "armRight", armR);
            changed |= SetRef(handsSerialized, "armLeft", armL);
            handsSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject controllerSerialized = new(controller);
            changed |= SetRef(controllerSerialized, "movement", settings);
            changed |= SetRef(controllerSerialized, "viewPivot", viewPivot);
            changed |= SetRef(controllerSerialized, "bodyVisual", bodyVisual);
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            int playerLayer = CarryableCollisionPolicy.PlayerLayer;
            if (playerLayer >= 0 && root.layer != playerLayer)
            {
                CarryableCollisionPolicy.SetLayerRecursively(root, playerLayer);
                changes.Add("player layer set");
                changed = true;
            }
            return changed;
        }

        private static Transform EnsureChild(Transform parent, string name, Vector3 localPosition, ref bool changed, List<string> changes)
        {
            if (parent == null) return null;
            Transform child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            changes.Add(name + " added");
            changed = true;
            return child;
        }

        private static bool SetRef(SerializedObject serialized, string property, Object value)
        {
            SerializedProperty p = serialized.FindProperty(property);
            if (p == null || p.objectReferenceValue == value) return false;
            p.objectReferenceValue = value;
            return true;
        }

        // A gloved arm: two box segments (stretched along Z by PlayerHands), a
        // hand with a palm, a thumb and four fingers hinged at the knuckles. No
        // colliders, no physics, no networking. Mirrored for the left hand.
        public static Transform BuildArm(Transform parent, string name, Material gloves, bool mirror)
        {
            GameObject arm = new(name);
            arm.transform.SetParent(parent, false);
            Box(PlayerHands.UpperArmName, arm.transform, Vector3.zero, new Vector3(0.09f, 0.09f, UpperArmLength), gloves);
            Box(PlayerHands.ForearmName, arm.transform, Vector3.zero, new Vector3(0.08f, 0.08f, ForearmLength), gloves);
            GameObject hand = new(PlayerHands.HandName);
            hand.transform.SetParent(arm.transform, false);
            Box("Palm", hand.transform, new Vector3(0f, 0f, 0.05f), new Vector3(0.09f, 0.03f, 0.10f), gloves);
            float side = mirror ? 1f : -1f; // the thumb is on the outside of the local X axis
            GameObject thumb = new(PlayerHands.ThumbName);
            thumb.transform.SetParent(hand.transform, false);
            thumb.transform.localPosition = new Vector3(side * 0.05f, 0f, 0.03f);
            Box("ThumbMesh", thumb.transform, new Vector3(0f, 0f, 0.025f), new Vector3(0.022f, 0.022f, 0.05f), gloves);
            for (int i = 0; i < 4; i++)
            {
                GameObject finger = new(PlayerHands.FingerPrefix + (i + 1));
                finger.transform.SetParent(hand.transform, false);
                finger.transform.localPosition = new Vector3(-0.033f + i * 0.022f, 0f, 0.10f);
                Box("FingerMesh", finger.transform, new Vector3(0f, 0f, 0.035f), new Vector3(0.018f, 0.018f, 0.07f), gloves);
            }
            return arm.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = scale;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().sharedMaterial = material;
            box.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return box;
        }

        // ---- carryable prefabs -----------------------------------------------------------

        private static IEnumerable<string> PatchCarryablePrefabs()
        {
            var changes = new List<string>();
            var paths = new List<string> { HQPrototypeBuilder.BallPrefabPath };
            foreach (HQPrototypeLootSetup.FixtureEntry entry in HQPrototypeLootSetup.Manifest)
                if (!paths.Contains(entry.PrefabPath)) paths.Add(entry.PrefabPath);
            foreach (string path in paths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = PatchCarryable(root, changes);
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return changes;
        }

        // Grip targets on the item's own surface, in item space: palms on the left
        // and right, fingers forward, thumbs up. A sphere's radius sets the width;
        // another shape gets the same targets at its bounds' half-width.
        public static bool PatchCarryable(GameObject root, List<string> changes)
        {
            bool changed = false;
            CarryableItem item = root.GetComponent<CarryableItem>();
            if (item == null) return false;
            // Targets are authored in the root's local space, so the root's scale
            // (a ball's size) carries them to the surface.
            float local = 0.5f;   // a unit sphere primitive: radius 0.5 before scale
            var sphere = root.GetComponent<SphereCollider>();
            if (sphere != null) local = sphere.radius;
            else { Collider any = root.GetComponent<Collider>(); if (any != null) local = any.bounds.extents.x / Mathf.Max(root.transform.lossyScale.x, 0.01f); }
            float halfWidth = local * root.transform.lossyScale.x;
            ItemHandPose pose = root.GetComponent<ItemHandPose>();
            if (pose == null) { pose = root.AddComponent<ItemHandPose>(); changes.Add(root.name + ": ItemHandPose added"); changed = true; }
            Transform right = root.transform.Find("GripR");
            Transform left = root.transform.Find("GripL");
            if (right == null) { right = new GameObject("GripR").transform; right.SetParent(root.transform, false); changed = true; changes.Add(root.name + ": grips added"); }
            if (left == null) { left = new GameObject("GripL").transform; left.SetParent(root.transform, false); changed = true; }
            Vector3 rightPos = new(local, 0f, 0f);
            Vector3 leftPos = new(-local, 0f, 0f);
            Quaternion rightRot = Quaternion.LookRotation(Vector3.forward, Vector3.right);
            Quaternion leftRot = Quaternion.LookRotation(Vector3.forward, Vector3.left);
            if (right.localPosition != rightPos || right.localRotation != rightRot) { right.localPosition = rightPos; right.localRotation = rightRot; changed = true; }
            if (left.localPosition != leftPos || left.localRotation != leftRot) { left.localPosition = leftPos; left.localRotation = leftRot; changed = true; }
            FingerPose fingers = item.Grip == CarryGrip.TwoHands || halfWidth > 0.2f ? FingerPose.BallLarge : FingerPose.BallSmall;
            if (pose.RightGrip != right || pose.LeftGrip != left || pose.Fingers != fingers)
            {
                pose.Configure(right, left, fingers);
                EditorUtility.SetDirty(pose);
                changed = true;
            }
            int carryableLayer = CarryableCollisionPolicy.CarryableLayer;
            if (carryableLayer >= 0 && root.layer != carryableLayer)
            {
                CarryableCollisionPolicy.SetLayerRecursively(root, carryableLayer);
                changes.Add(root.name + ": carryable layer set");
                changed = true;
            }
            return changed;
        }
    }
}
