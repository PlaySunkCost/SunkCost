using SunkCost.Audio;
using SunkCost.Editor.Prototype;
using SunkCost.World;
using UnityEditor;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Builds the ship's ambience into the ship (ship audit SHIP-065, 23 September
    // 2026; the runtime side is ShipAmbience): a sea-wash source low on each side
    // at the waterline, the wind, the TV's NO SIGNAL hiss, the engine at the stern
    // (handed to ShipDepartureVisual, which plays it while the ship sails), and the
    // bow spray and stern wake it shows while sailing. Called at the end of the
    // ship build, after the models are in (the hull's bounds place the bow and the
    // stern). Idempotent: an earlier build's parts are replaced.
    public static class ShipAmbienceSetup
    {
        public const string RootName = "Ambience";
        private const string ParticleMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        public static void Build(Transform shipRoot)
        {
            Transform old = shipRoot.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            GameObject root = new(RootName);
            root.transform.SetParent(shipRoot, false);

            // The hull's extent: the bow and the stern where the model has them, else the stub's deck.
            float bow = ShipStubBuilder.DeckLength / 2f, stern = -ShipStubBuilder.DeckLength / 2f, halfWidth = ShipStubBuilder.DeckWidth / 2f;
            Transform hull = FindNamed(shipRoot, "Hull");
            Renderer hullRenderer = hull != null ? hull.GetComponentInChildren<Renderer>() : null;
            if (hullRenderer != null)
            {
                Bounds b = hullRenderer.bounds; // the prefab root sits at the origin
                bow = b.max.z; stern = b.min.z; halfWidth = b.extents.x;
            }
            float water = ShipStubBuilder.SeaLevelY;

            AudioSource washPort = Source(root.transform, "Sea Wash Port", new Vector3(-halfWidth, water + 0.5f, 0f), 1f, 6f, 30f, AudioRolloffMode.Logarithmic);
            AudioSource washStarboard = Source(root.transform, "Sea Wash Starboard", new Vector3(halfWidth, water + 0.5f, 0f), 1f, 6f, 30f, AudioRolloffMode.Logarithmic);
            AudioSource wind = Source(root.transform, "Wind", new Vector3(0f, 3f, 0f), 0f, 1f, 50f, AudioRolloffMode.Linear); // a bed over the whole deck
            Transform speaker = shipRoot.GetComponent<ShipParts>() != null ? shipRoot.GetComponent<ShipParts>().TvSpeaker : null;
            AudioSource hiss = Source(root.transform, "TV Hiss", speaker != null ? shipRoot.InverseTransformPoint(speaker.position) : new Vector3(0f, 1.5f, 19f), 1f, 1.5f, 10f, AudioRolloffMode.Linear);
            AudioSource engine = Source(root.transform, "Engine", new Vector3(0f, water + 1.5f, stern + 3f), 1f, 5f, 60f, AudioRolloffMode.Logarithmic);

            ParticleSystem sprayPort = Spray(root.transform, "Bow Spray Port", new Vector3(-0.8f, water + 0.2f, bow - 1.5f), -1f);
            ParticleSystem sprayStarboard = Spray(root.transform, "Bow Spray Starboard", new Vector3(0.8f, water + 0.2f, bow - 1.5f), 1f);
            ParticleSystem wakeFoam = Wake(root.transform, "Stern Wake", new Vector3(0f, water + 0.05f, stern + 0.5f), halfWidth * 0.8f);

            ShipAmbience ambience = shipRoot.GetComponent<ShipAmbience>();
            if (ambience == null) ambience = shipRoot.gameObject.AddComponent<ShipAmbience>();
            ambience.Configure(washPort, washStarboard, wind, hiss, engine, new[] { sprayPort, sprayStarboard, wakeFoam });

            // The engine ShipDepartureVisual plays while the ship pulls away (its field was empty).
            ShipDepartureVisual visual = shipRoot.GetComponent<ShipDepartureVisual>();
            if (visual != null)
            {
                SerializedObject serialized = new(visual);
                SerializedProperty field = serialized.FindProperty("engine");
                if (field != null) { field.objectReferenceValue = engine; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        private static AudioSource Source(Transform parent, string name, Vector3 local, float spatialBlend, float near, float far, AudioRolloffMode rolloff)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.volume = 0f; // ShipAmbience fills the clip and the volume
            source.spatialBlend = spatialBlend; source.rolloffMode = rolloff;
            source.minDistance = near; source.maxDistance = far;
            source.dopplerLevel = 0f;
            return source;
        }

        // White water thrown up and out at the bow, falling back: `side` -1 port, +1 starboard.
        private static ParticleSystem Spray(Transform parent, string name, Vector3 local, float side)
        {
            ParticleSystem system = NewSystem(parent, name, local, Quaternion.Euler(-35f, side * 60f, 0f));
            var main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startColor = new Color(0.9f, 0.95f, 1f, 0.55f);
            main.gravityModifier = 0.9f;
            main.maxParticles = 200;
            var emission = system.emission;
            emission.rateOverTime = 45f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f; shape.radius = 0.4f;
            FadeOut(system);
            return system;
        }

        // Foam spreading on the water behind the stern: flat, slow, left behind in the world.
        private static ParticleSystem Wake(Transform parent, string name, Vector3 local, float halfWidth)
        {
            ParticleSystem system = NewSystem(parent, name, local, Quaternion.Euler(0f, 180f, 0f)); // out of the stern, aft
            var main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startColor = new Color(0.85f, 0.92f, 0.95f, 0.35f);
            main.maxParticles = 300;
            var emission = system.emission;
            emission.rateOverTime = 30f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(halfWidth * 2f, 0.1f, 0.5f);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // lies on the water
            FadeOut(system);
            return system;
        }

        private static ParticleSystem NewSystem(Transform parent, string name, Vector3 local, Quaternion rotation)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = rotation;
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // left behind as the ship moves on
            main.playOnAwake = true;
            main.loop = true;
            var emission = system.emission;
            emission.enabled = false; // ShipAmbience turns it on while the ship sails
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static void FadeOut(ParticleSystem system)
        {
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            Gradient fade = new();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindNamed(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
