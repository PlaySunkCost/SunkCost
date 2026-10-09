using SunkCost.Audio;
using UnityEngine;

namespace SunkCost.Editor.Look
{
    // Builds the platform's ambience bed into the generated HQ (Dan, 8 October 2026,
    // BRIEF B.7; the runtime side is HQAmbience): a 2D wind over the whole platform
    // and eight sea-wash sources round its edge at the waterline, one per side and
    // one per corner, so no walkable point is out of every wash's reach. Called at
    // the end of HQGeneratedLayout.Dressing. Idempotent: an earlier build's group is
    // replaced. The clips and volumes come from the AudioLibrary at runtime.
    public static class HQAmbienceSetup
    {
        public const float WashY = -1.5f;                 // between the deck top (0) and the water (-4.5)
        public const float WashNear = 8f, WashFar = 40f;  // logarithmic: full within 8 m, a fifth at 40 m and beyond
        public const float WashShare = 0.5f;              // each edge source at half the ship's wash volume: eight of them add up

        public static readonly Vector3[] WashSpots =
        {
            new(0f, WashY, 17.5f), new(0f, WashY, -17.5f), new(29.5f, WashY, 0f), new(-29.5f, WashY, 0f),
            new(29.5f, WashY, 17.5f), new(29.5f, WashY, -17.5f), new(-29.5f, WashY, 17.5f), new(-29.5f, WashY, -17.5f),
        };

        public static HQAmbience Build(GameObject root)
        {
            Transform old = root.transform.Find(HQAmbience.RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            GameObject group = new(HQAmbience.RootName);
            group.transform.SetParent(root.transform, false);

            AudioSource wind = Source(group.transform, "Wind", new Vector3(0f, 3f, 0f), 0f, 1f, 50f, AudioRolloffMode.Linear);
            var washes = new AudioSource[WashSpots.Length];
            for (int i = 0; i < WashSpots.Length; i++)
                washes[i] = Source(group.transform, "Sea Wash " + i, WashSpots[i], 1f, WashNear, WashFar, AudioRolloffMode.Logarithmic);

            HQAmbience ambience = group.AddComponent<HQAmbience>();
            ambience.Configure(wind, washes, WashShare);
            return ambience;
        }

        private static AudioSource Source(Transform parent, string name, Vector3 local, float spatialBlend, float near, float far, AudioRolloffMode rolloff)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.volume = 0f; // HQAmbience fills the clip and the volume
            source.spatialBlend = spatialBlend; source.rolloffMode = rolloff;
            source.minDistance = near; source.maxDistance = far;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
