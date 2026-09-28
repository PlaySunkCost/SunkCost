using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SunkCost.Sites
{
    // The tube foot's doorway at the seafloor, seen through the open car doors and gate
    // (DIVE-GATE-VIEW, 28 September 2026). Before this the view out was a flat dark panel:
    // nothing lit the sand in front of the foot (the Surface Light skips the deep layer,
    // the site's three dim lights stand 15-35 m away, the ambient is near black, and the
    // headlamp's 35-degree beam runs level into the fog and meets the sand only 5 m out,
    // at a grazing angle), and the seabed is one flat dark colour, so even a lit patch
    // had no texture to read as ground.
    //
    //   - "Foot Threshold Light": a wide cool work light over the doorway, aimed down and
    //     out, lighting the sill, the ramp and the sand in front of the foot (no shadows);
    //     "Threshold Lamp", its small housing and glowing lens, on the doorway's face;
    //   - "Foot Ramp Plate": the ramp collider's surface drawn in kit steel (under option A'
    //     the foot's own ramp is cut away, so the walkable ramp was invisible);
    //   - "Seabed Apron": a sand patch in front of and round the foot, rippled (colour and
    //     normal), lighter at the doorway, fading exactly into the Seabed material at its
    //     edge; drawn 8 mm over the sand, no collider (the seafloor's box stays the ground).
    //
    // Scene statics only, identical for every peer, spectator and the TV: no state, no
    // gameplay (the monsters sense the replicated lamp bit, never a Light). The site's
    // WorldLightLayers stamps the light like the other seafloor lights (it reaches the deep
    // layer). Look values are here, like the site's other lights (DiveSiteBuilder).
    public static class ShaftTubeThreshold
    {
        public const string LightName = "Foot Threshold Light";
        public const string LampName = "Threshold Lamp";
        public const string ApronName = "Seabed Apron";
        public const string RampPlateName = "Foot Ramp Plate";
        private const float RampPlateLift = 0.012f;

        public const string Folder = "Assets/_Project/Art/Elevator/Foot";
        public const string ApronMaterialPath = Folder + "/SeabedApron.mat";
        public const string LampMaterialPath = Folder + "/ThresholdLamp.mat";
        private const string ApronAlbedoPath = Folder + "/SeabedApron.png";
        private const string ApronNormalPath = Folder + "/SeabedApronNormal.png";

        // The light: on the doorway's face under the first tube section (the foot's
        // doorway is cut to its top), aimed 60 degrees down and out.
        public const float LightRadius = 3.35f, LightAboveFloor = 3.62f, LightPitchDeg = 60f;
        public const float LightIntensity = 40f, LightRange = 15f, LightSpotAngle = 150f, LightInnerSpotAngle = 90f;
        public static readonly Color LightColour = new(0.72f, 0.9f, 1f);

        // The apron: a 16 m square (Unity's plane, 10 m, scaled) centred 2.5 m out along
        // the doorway, its pattern fading into the plain seabed between 5 and 7.5 m.
        private const float ApronSize = 16f, ApronCentreOut = 2.5f, ApronLift = 0.008f;
        private const float FadeFrom = 5f, FadeTo = 7.5f;
        private const int TextureSize = 512;
        // The texture multiplies a base colour twice the seabed's (linear): 0.5 is the
        // plain seabed, so the edge matches it exactly; the sand near the doorway reaches
        // about 1.6 times it, the ripples' troughs about 0.8 times.
        private const float PlainTexel = 128f / 255f;

        public static void Build(Transform shaftTube, Vector3 axis, float footFloorY, float sandY, float doorwayBearingDeg, int deepLayer)
        {
            float doorRad = doorwayBearingDeg * Mathf.Deg2Rad;
            Vector3 door = new(Mathf.Cos(doorRad), 0f, Mathf.Sin(doorRad));
            Vector3 flatAxis = new(axis.x, footFloorY, axis.z);

            // The light and its lamp.
            SunkCost.Editor.Look.ElevatorLook.RemoveChildren(shaftTube, LightName);
            GameObject lightObject = new(LightName, typeof(Light));
            lightObject.transform.SetParent(shaftTube, false);
            lightObject.transform.SetPositionAndRotation(flatAxis + door * LightRadius + Vector3.up * LightAboveFloor,
                Quaternion.LookRotation(door * Mathf.Cos(LightPitchDeg * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(LightPitchDeg * Mathf.Deg2Rad), Vector3.up));
            lightObject.layer = deepLayer;
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Spot;
            light.color = LightColour;
            light.intensity = LightIntensity;
            light.range = LightRange;
            light.spotAngle = LightSpotAngle;
            light.innerSpotAngle = LightInnerSpotAngle;
            light.shadows = LightShadows.None;

            SunkCost.Editor.Look.ElevatorLook.RemoveChildren(shaftTube, LampName);
            GameObject lamp = new(LampName);
            lamp.transform.SetParent(shaftTube, false);
            lamp.transform.SetPositionAndRotation(flatAxis + door * LightRadius + Vector3.up * (LightAboveFloor + 0.06f), Quaternion.LookRotation(door, Vector3.up));
            lamp.layer = deepLayer;
            Cube(lamp.transform, "Lamp Housing", new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.12f, 0.24f), SunkCost.Editor.Look.ShipKitMaterials.Steel(), deepLayer);
            Cube(lamp.transform, "Lamp Lens", new Vector3(0f, -0.065f, 0.01f), new Vector3(0.54f, 0.012f, 0.16f), LampMaterial(), deepLayer);

            // The ramp's plate: the "Foot Ramp" collider's box drawn in kit steel (the foot's
            // model has no ramp under option A'), 12 mm over it so it never fights the apron.
            SunkCost.Editor.Look.ElevatorLook.RemoveChildren(shaftTube, RampPlateName);
            Transform rampCollider = shaftTube.Find(ShaftTubeLook.FootCollidersName)?.Find(ShaftTubeLook.RampName);
            if (rampCollider == null || !rampCollider.TryGetComponent(out BoxCollider rampBox))
                throw new System.InvalidOperationException("Build the foot's colliders (" + ShaftTubeLook.RampName + ") before the threshold.");
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = RampPlateName;
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(shaftTube, false);
            plate.transform.SetPositionAndRotation(rampCollider.position + rampCollider.up * RampPlateLift, rampCollider.rotation);
            plate.transform.localScale = rampBox.size;
            plate.layer = deepLayer;
            plate.GetComponent<MeshRenderer>().sharedMaterial = SunkCost.Editor.Look.ShipKitMaterials.Tiled(SunkCost.Editor.Look.ShipKitMaterials.Steel(), new Vector2(rampBox.size.x, rampBox.size.z));

            // The sand.
            SunkCost.Editor.Look.ElevatorLook.RemoveChildren(shaftTube, ApronName);
            GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Plane);
            apron.name = ApronName;
            Object.DestroyImmediate(apron.GetComponent<Collider>());
            apron.transform.SetParent(shaftTube, false);
            apron.transform.SetPositionAndRotation(new Vector3(axis.x, sandY + ApronLift, axis.z) + door * ApronCentreOut, Quaternion.LookRotation(door, Vector3.up));
            apron.transform.localScale = new Vector3(ApronSize / 10f, 1f, ApronSize / 10f);
            apron.layer = deepLayer;
            MeshRenderer renderer = apron.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ApronMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void Cube(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, int layer)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Object.DestroyImmediate(cube.GetComponent<Collider>()); // 3.6 m up: nobody reaches it, and the camera must not snag on it
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = size;
            cube.layer = layer;
            MeshRenderer r = cube.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---- materials (made once, their GUIDs kept; settings written every call) ------

        public static Material ApronMaterial()
        {
            Material m = LoadOrCreate(ApronMaterialPath, "Universal Render Pipeline/Lit");
            Material seabed = SunkCost.Editor.Look.LookMaterials.Seabed();
            Color plain = seabed.GetColor("_BaseColor");
            // Base colour x texel (linear, not sRGB) = the seabed's colour where the texel is 0.5.
            Color linear = plain.linear;
            Color doubled = new Color(linear.r / PlainTexel, linear.g / PlainTexel, linear.b / PlainTexel, 1f).gamma;
            m.SetColor("_BaseColor", doubled);
            m.SetTexture("_BaseMap", TextureAt(ApronAlbedoPath, () => ApronTexture(normal: false), normalMap: false));
            m.SetTexture("_BumpMap", TextureAt(ApronNormalPath, () => ApronTexture(normal: true), normalMap: true));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetFloat("_Metallic", seabed.GetFloat("_Metallic"));
            m.SetFloat("_Smoothness", seabed.GetFloat("_Smoothness"));
            m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        public static Material LampMaterial()
        {
            Material m = LoadOrCreate(LampMaterialPath, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", new Color(0.55f, 0.65f, 0.7f));
            m.SetTexture("_BaseMap", null);
            m.SetColor("_EmissionColor", LightColour * 1.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        private static Material LoadOrCreate(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException(shaderName + " not found");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                EnsureFolder();
                m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ---- the sand texture ----------------------------------------------------------

        private static Texture2D TextureAt(string path, System.Func<Texture2D> make, bool normalMap)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                EnsureFolder();
                Texture2D texture = make();
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            // Both linear: the albedo's 0.5 must stay 0.5 so the edge meets the seabed exactly.
            if (importer.textureType != type || importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled || importer.alphaIsTransparency)
            {
                importer.textureType = type;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Sand ripples across the doorway (a wavelength of about 0.45 m, bent by a slow
        // noise), a fine grain, and broad lighter and darker drifts; brighter near the
        // doorway; everything fades to the plain seabed (albedo 0.5, flat normal) at the edge.
        // The plane is laid with +Z out through the doorway, its centre ApronCentreOut from
        // the tube's axis (so the axis is at the plane's z = -ApronCentreOut).
        private static Texture2D ApronTexture(bool normal)
        {
            const int n = TextureSize;
            float metresPerTexel = ApronSize / n;
            var height = new float[n, n];
            var shade = new float[n, n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Unity's plane runs u toward its -X and v toward its -Z (UV (0,0) at its +X,+Z
                    // corner, measured): (px, pz) are the plane's own metres, +Z out through the doorway.
                    float px = ApronSize / 2f - (x + 0.5f) * metresPerTexel;
                    float pz = ApronSize / 2f - (y + 0.5f) * metresPerTexel;
                    // Two ripple trains a little apart in angle, each bent by its own slow noise and
                    // faded in and out by another, so no straight stripes run across the view.
                    float warpA = Mathf.PerlinNoise(px * 0.31f + 11.3f, pz * 0.31f + 4.7f) - 0.5f;
                    float warpB = Mathf.PerlinNoise(px * 0.27f + 2.9f, pz * 0.27f + 15.1f) - 0.5f;
                    float mix = Mathf.SmoothStep(0f, 1f, Mathf.PerlinNoise(px * 0.18f + 7.7f, pz * 0.18f + 1.3f) * 1.6f - 0.3f);
                    float rippleA = Mathf.Sin((pz * 0.93f + px * 0.37f + warpA * 1.6f) * (2f * Mathf.PI / 0.48f));
                    float rippleB = Mathf.Sin((pz * 0.60f - px * 0.80f + warpB * 1.6f) * (2f * Mathf.PI / 0.38f));
                    float ripple = Mathf.Lerp(rippleA, rippleB, mix);
                    ripple = Mathf.Sign(ripple) * Mathf.Pow(Mathf.Abs(ripple), 0.7f); // crests a little sharper than a sine
                    float drift = Mathf.PerlinNoise(px * 0.35f + 3.1f, pz * 0.35f + 8.9f) - 0.5f;
                    float grain = Mathf.PerlinNoise(px * 9f + 1.7f, pz * 9f + 6.2f) - 0.5f;
                    height[x, y] = 0.6f * ripple * (0.6f + drift) + 0.25f * grain;

                    // The doorway's path of sand is the brightest, and the fade takes it all back.
                    float r = Mathf.Sqrt(px * px + (pz + ApronCentreOut) * (pz + ApronCentreOut)); // from the tube's axis
                    float fromCentre = Mathf.Sqrt(px * px + pz * pz);
                    float keep = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeFrom, FadeTo, fromCentre));
                    float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 7f, r));
                    float multiplier = 1f + keep * (0.45f * near + 0.09f * ripple + 0.30f * drift + 0.14f * grain + 0.1f);
                    shade[x, y] = Mathf.Clamp01(PlainTexel * multiplier);
                    height[x, y] *= keep;
                }

            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true, linear: true);
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (normal)
                    {
                        float dx = (height[Mathf.Min(x + 1, n - 1), y] - height[Mathf.Max(x - 1, 0), y]) / (2f * metresPerTexel);
                        float dz = (height[x, Mathf.Min(y + 1, n - 1)] - height[x, Mathf.Max(y - 1, 0)]) / (2f * metresPerTexel);
                        const float relief = 0.012f; // metres of ripple height per unit of the height field
                        Vector3 nrm = new Vector3(-dx * relief, -dz * relief, 1f).normalized;
                        pixels[y * n + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                    }
                    else
                    {
                        float s = shade[x, y];
                        pixels[y * n + x] = new Color(s, s, s, 1f);
                    }
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art/Elevator")) AssetDatabase.CreateFolder("Assets/_Project/Art", "Elevator");
            AssetDatabase.CreateFolder("Assets/_Project/Art/Elevator", "Foot");
        }
    }
}
