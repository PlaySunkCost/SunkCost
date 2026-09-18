using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SunkCost.Editor.Look
{
    // Renders the open scene from a few fixed viewpoints to PNGs under Temp, with
    // the game's post-processing, so a look can be judged without Play Mode
    // (and sent to Dan). Editor only; a temporary camera, gone afterwards.
    public static class LookCapture
    {
        public const string Folder = "Temp/look";

        // What sits in the ship's elevator well: every renderer and collider whose
        // bounds reach the gap between the pedestal and the well's wall.
        public static string ProbeWell()
        {
            var ship = GameObject.Find("Ship");
            if (ship == null) return "no ship";
            var sb = new System.Text.StringBuilder();
            Transform rimT = ship.transform.Find("Well Rim");
            if (rimT != null) { var mf = rimT.GetComponent<MeshFilter>(); var mr = rimT.GetComponent<MeshRenderer>(); sb.Append("rim: mesh=").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " v" + mf.sharedMesh.vertexCount : "none").Append(" mat=").Append(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "none").Append(" enabled=").Append(mr != null && mr.enabled).Append(" bounds=").Append(mr != null ? mr.bounds.ToString() : "-").Append(" | "); }
            else sb.Append("no Well Rim | ");
            foreach (float y in new[] { 0.02f, -0.3f, -0.6f, -1.5f, -3f, -5f, -7f })
            {
                Vector3 probe = ship.transform.TransformPoint(new Vector3(3.0f, y, 3.0f));
                sb.Append("y=").Append(y).Append(": ");
                foreach (Renderer r in ship.GetComponentsInChildren<Renderer>(true))
                    if (r.bounds.Contains(probe)) sb.Append(r.name).Append('/').Append(r.transform.parent != null ? r.transform.parent.name : "-").Append("; ");
                foreach (Collider c in ship.GetComponentsInChildren<Collider>(true))
                    if (c.bounds.Contains(probe)) sb.Append("collider ").Append(c.name).Append("; ");
                sb.Append(" | ");
            }
            return sb.ToString();
        }

        // The dive site's tube and car as they stand, the site scene being open.
        public static string ShootSite()
        {
            Directory.CreateDirectory(Folder);
            Shoot("site-bottom", new Vector3(7f, -43.3f, 5f), new Vector3(0f, -43.5f, 0f), 60f); // the seafloor station: the gate and the car
            Shoot("site-tube", new Vector3(12f, -30f, 0f), new Vector3(0f, -36f, 0f), 60f); // the tube from the side
            return Folder;
        }

        public static string ShootAll()
        {
            Directory.CreateDirectory(Folder);
            RenderTexture sky = SunkCost.Look.SkyEnvironment.Refresh();
            try
            {
            Shoot("overview", new Vector3(58f, 34f, -62f), new Vector3(0f, 0f, 4f), 55f);
            Shoot("iso", new Vector3(52f, 62f, -82f), new Vector3(-2f, 2f, 2f), 34f); // the reference angle: high, from the south-east, a long lens
            Shoot("booths", new Vector3(2f, 1.7f, -4f), new Vector3(0f, 2.6f, 14f), 70f);
            Shoot("court", new Vector3(-6f, 1.7f, -14f), new Vector3(-16f, 1.6f, -3f), 75f);
            Shoot("crew-mark", new Vector3(16f, 1.7f, -14f), new Vector3(8f, 1.2f, 8f), 75f);
            Shoot("bridge", new Vector3(-24f, 1.7f, -8f), new Vector3(-44f, 0.5f, -13f), 70f);
            Shoot("tower", new Vector3(-40f, -4.4f, -34f), new Vector3(-44f, -1f, -16f), 70f);
            Shoot("deck", new Vector3(-44f, -4.4f, -50f), new Vector3(-44f, -4f, -20f), 75f);
            Shoot("console", new Vector3(-43f, -4.4f, -24f), new Vector3(-44f, -4.4f, -20.5f), 60f); // on the deck, at the tower's foot
            Shoot("well", new Vector3(-40f, -4.2f, -30f), new Vector3(-44f, -6.2f, -35f), 65f); // the elevator's well and its rail
            Shoot("well-down", new Vector3(-44f, 60f, -36.06f), new Vector3(-44f, -12f, -36.05f), 9f); // straight down the shaft from high up: near-parallel rays
            Shoot("well-inside", new Vector3(-44f, -6.4f, -32.9f), new Vector3(-40f, -6.4f, -32.9f), 80f); // from inside the gap, sideways // looking down the shaft from the rail
            Shoot("hq-north", new Vector3(-10f, 8f, -6f), new Vector3(8f, 4f, 18f), 60f); // the roofs' back rail
            Shoot("cabin-button", new Vector3(-44.6f, -4.4f, -35.6f), new Vector3(-41.7f, -4.9f, -36.7f), 55f); // inside the deck cabin, the button on its wall
            Shoot("ship", new Vector3(-70f, 6f, -80f), new Vector3(-44f, -4f, -34f), 60f);
            Shoot("plank", new Vector3(24f, 1.7f, -10f), new Vector3(32f, 0.2f, -16f), 70f);
            Shoot("pickup", new Vector3(22f, 6.6f, 6f), new Vector3(25f, 6.5f, 15f), 75f);
            Shoot("from-sea", new Vector3(10f, 2f, -70f), new Vector3(0f, 3f, 0f), 60f);
            }
            finally { SunkCost.Look.SkyEnvironment.Restore(sky); }
            return $"{Folder}; fog={RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance} skybox={(RenderSettings.skybox == null ? "none" : RenderSettings.skybox.name)} reflection={RenderSettings.defaultReflectionMode} ambient={RenderSettings.ambientMode} active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";
        }

        // A look at the fog: everything past 60 m should sink into it.
        public static string FogTest()
        {
            float start = RenderSettings.fogStartDistance, end = RenderSettings.fogEndDistance;
            RenderSettings.fogStartDistance = 10f; RenderSettings.fogEndDistance = 60f;
            Shoot("fogtest", new Vector3(10f, 2f, -70f), new Vector3(0f, 3f, 0f), 60f);
            RenderSettings.fogStartDistance = 120f; RenderSettings.fogEndDistance = 400f;
            Shoot("fogtest400", new Vector3(10f, 2f, -70f), new Vector3(0f, 3f, 0f), 60f);
            Shoot("down", new Vector3(20f, 30f, -40f), new Vector3(25f, -5f, -60f), 60f);
            Shoot("waterline", new Vector3(-38f, -4.2f, -3f), new Vector3(-47f, -5.2f, -12f), 60f);
            RenderSettings.fogStartDistance = start; RenderSettings.fogEndDistance = end;
            return $"Temp/look/fogtest.png; fog={RenderSettings.fog} {RenderSettings.fogMode} {start}-{end} colour={RenderSettings.fogColor} active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";
        }

        public static string LightTest()
        {
            RenderTexture sky = SunkCost.Look.SkyEnvironment.Refresh();
            try
            {
                Shoot("lighttest", new Vector3(10f, 1.6f, -13f), new Vector3(9f, 0.2f, -17.5f), 70f);
            }
            finally { SunkCost.Look.SkyEnvironment.Restore(sky); }
            int lights = 0, realtime = 0;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) { lights++; if (l.lightmapBakeType == LightmapBakeType.Realtime && l.type == LightType.Point) realtime++; }
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            return $"lights={lights} realtimePoint={realtime} asset={(asset == null ? "none" : asset.name)} additional={(asset == null ? "?" : asset.additionalLightsRenderingMode.ToString())} maxAdditional={(asset == null ? -1 : asset.maxAdditionalLightsCount)}";
        }

        public static string EmissionTest()
        {
            Material water = AssetDatabase.LoadAssetAtPath<Material>(LookMaterials.Folder + "/Water.mat");
            Color e = water.GetColor("_EmissionColor");
            float start = RenderSettings.fogStartDistance, end = RenderSettings.fogEndDistance;
            RenderSettings.fogStartDistance = 10f; RenderSettings.fogEndDistance = 60f;
            water.SetColor("_EmissionColor", Color.black); water.DisableKeyword("_EMISSION");
            Shoot("emtest-noemission", new Vector3(10f, 2f, -70f), new Vector3(0f, 3f, 0f), 60f);
            water.SetColor("_EmissionColor", e); water.EnableKeyword("_EMISSION");
            water.DisableKeyword("_NORMALMAP");
            Shoot("emtest-nonormal", new Vector3(10f, 2f, -70f), new Vector3(0f, 3f, 0f), 60f);
            water.EnableKeyword("_NORMALMAP");
            RenderSettings.fogStartDistance = start; RenderSettings.fogEndDistance = end;
            return "ok; keywords=" + string.Join(",", water.shaderKeywords) + " shader=" + water.shader.name + " queue=" + water.renderQueue;
        }

        public static string WaterTest()
        {
            GameObject waves = GameObject.Find("Waves");
            MeshFilter f = waves != null ? waves.GetComponent<MeshFilter>() : null;
            string a = f == null ? "no Waves" : "Waves mesh=" + (f.sharedMesh == null ? "none" : f.sharedMesh.name + " verts=" + f.sharedMesh.vertexCount);
            Material water = AssetDatabase.LoadAssetAtPath<Material>(LookMaterials.Folder + "/Water.mat");
            Color c = water.GetColor("_BaseColor");
            water.SetColor("_BaseColor", new Color(1f, 0f, 0f, 1f));
            Shoot("watertest", new Vector3(20f, 30f, -40f), new Vector3(25f, -5f, -60f), 60f);
            // opaque
            water.SetFloat("_Surface", 0f); water.SetOverrideTag("RenderType", "Opaque"); water.SetInt("_SrcBlend", 1); water.SetInt("_DstBlend", 0); water.SetInt("_ZWrite", 1); water.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); water.renderQueue = -1;
            Shoot("watertest-opaque", new Vector3(20f, 30f, -40f), new Vector3(25f, -5f, -60f), 60f);
            LookMaterials.Water();
            water.SetColor("_BaseColor", c);
            // the far plane with a known-good material
            GameObject far = GameObject.Find("Far Sea");
            MeshRenderer fr = far != null ? far.GetComponent<MeshRenderer>() : null;
            Material keep = fr != null ? fr.sharedMaterial : null;
            if (fr != null) fr.sharedMaterial = LookMaterials.Hazard();
            Shoot("watertest-mesh", new Vector3(20f, 30f, -40f), new Vector3(25f, -5f, -60f), 60f);
            if (fr != null) fr.sharedMaterial = keep;
            return a + "; far mesh=" + (far == null ? "none" : far.GetComponent<MeshFilter>().sharedMesh?.name + " bounds=" + fr.bounds.ToString());
        }

        public static string Probe()
        {
            string a = Shader.Find("GUI/3D Text Shader") != null ? "3D Text: yes" : "3D Text: no";
            string b = Shader.Find("GUI/Text Shader") != null ? "Text: yes" : "Text: no";
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string c = font != null ? "font: " + font.name + " tex=" + (font.material != null && font.material.mainTexture != null ? font.material.mainTexture.name + " " + font.material.shader.name : "none") : "font: none";
            return a + "; " + b + "; " + c;
        }

        public static void Shoot(string name, Vector3 from, Vector3 at, float fov)
        {
            GameObject go = new("LookCapture Camera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.transform.position = from;
                cam.transform.LookAt(at);
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 20000f; // the sea runs to the horizon; the fog takes the rest
                cam.clearFlags = CameraClearFlags.Skybox;
                UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var rt = new RenderTexture(1600, 900, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(Folder, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
