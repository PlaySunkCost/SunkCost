using System.IO;
using SunkCost.Monsters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // A portrait of each monster prefab as a diver would meet it (Dan, 20 September
    // 2026: "can you send me a picture of each monster?"): the placeholder shape
    // placed on the open dive site's seabed, lit by a headlamp from the camera's
    // spot, its eyes lit, the site's fog and ambient. PNGs under Temp/look.
    // Editor only; the instance and the light are gone afterwards.
    public static class MonsterPortraits
    {
        [MenuItem("Sunk Cost/Look/Monster portraits (Temp/look)")]
        public static void ShootFromMenu() => Debug.Log(ShootAll());

        // Runs on the open scene; opens the dive site when a scene of another kind is up.
        public static string ShootAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Exit Play Mode first.";
            string activePath = EditorSceneManager.GetActiveScene().path;
            if (activePath != SunkCost.World.WorldScenes.DivePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
                EditorSceneManager.OpenScene(SunkCost.World.WorldScenes.DivePath, OpenSceneMode.Single);
            }
            Directory.CreateDirectory(LookCapture.Folder);
            var made = new System.Collections.Generic.List<string>();
            foreach (MonsterKind kind in MonsterCatalog.Walkers) made.Add(Shoot(kind));
            if (!string.IsNullOrEmpty(activePath) && activePath != SunkCost.World.WorldScenes.DivePath) EditorSceneManager.OpenScene(activePath, OpenSceneMode.Single);
            return string.Join("; ", made);
        }

        private static string Shoot(MonsterKind kind)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterCatalog.PrefabPath(kind));
            if (prefab == null) return kind + ": no prefab (run the monster setup)";
            // On the seabed, off to the side of the shaft, facing the camera.
            Vector3 at = new(20f, -45f, 20f);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject lampObject = new("Portrait Headlamp", typeof(Light));
            try
            {
                instance.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 200f, 0f));
                // The eyes glow as they do on the hunt (CreatureLook does this in Play Mode).
                var block = new MaterialPropertyBlock();
                block.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.25f) * 3f);
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                    if (r.name.StartsWith("Eye")) r.SetPropertyBlock(block);
                }
                // The Impostor is seen wearing a crewmate's colour; a light so the suit reads.
                Renderer suit = instance.transform.Find("Body/Suit")?.GetComponent<Renderer>();
                if (suit != null) { var suitBlock = new MaterialPropertyBlock(); suitBlock.SetColor("_BaseColor", new Color(1f, 0.5f, 0.15f)); suit.SetPropertyBlock(suitBlock); }
                float height = kind switch { MonsterKind.LongWalker => 3.2f, MonsterKind.Charger => 1.4f, _ => 2.1f };
                Vector3 from = at + Quaternion.Euler(0f, 200f, 0f) * new Vector3(0f, height * 0.55f, 5.5f + height * 0.8f);
                Vector3 look = at + Vector3.up * height * 0.5f;
                Light lamp = lampObject.GetComponent<Light>();
                lampObject.transform.position = from;
                lampObject.transform.LookAt(look);
                lamp.type = LightType.Spot; lamp.intensity = 15f; lamp.range = 25f; lamp.spotAngle = 35f; lamp.color = new Color(1f, 0.93f, 0.82f); lamp.shadows = LightShadows.None;
                LookCapture.Shoot("monster-" + kind, from, look, 50f);
                return kind + " -> " + LookCapture.Folder + "/monster-" + kind + ".png";
            }
            finally
            {
                Object.DestroyImmediate(lampObject);
                Object.DestroyImmediate(instance);
            }
        }
    }
}
