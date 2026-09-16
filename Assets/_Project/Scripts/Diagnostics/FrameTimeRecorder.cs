using System.Collections.Generic;
using System.Globalization;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace SunkCost.Diagnostics
{
    // Frame pacing on this machine, for the F3 overlay and the matrices: how long
    // frames take, how uneven they are, and when they spike. Smoothness is frame
    // *consistency* more than frame rate, so the numbers that matter are the slow
    // tail (the 1 % worst frames) and the hitches (a frame far longer than its
    // neighbours), not the average. Present in the editor and development builds
    // only, like the overlay; release builds never get this object.
    public sealed class FrameTimeRecorder : MonoBehaviour
    {
        public const string ObjectName = "Frame Time Recorder";
        private const int WindowFrames = 600;          // the rolling window the summary describes
        private const float HitchFactor = 2.5f;        // a frame this many times the rolling median...
        private const float HitchMinimumMs = 30f;      // ...and at least this long counts as a hitch: two 60 Hz frames, the
                                                       // usual threshold for a stall a player notices; a single 17 ms frame on
                                                       // a 240 Hz display is one vsync quantum, not a hitch
        private const int KeptHitches = 12;

        public struct Hitch
        {
            public float AtSeconds;   // seconds since the last Reset
            public float Ms;
            public float MedianMs;    // what a frame took around it
            public string Blame;      // the profiler markers that were long in that frame, e.g. "Physics.Processing 31ms"
        }

        // Survives a domain reload mid-play (a script recompile while playing keeps
        // the object but clears the static): find it again when the static is gone.
        private static FrameTimeRecorder instance;
        public static FrameTimeRecorder Instance
        {
            get
            {
                if (instance == null) instance = FindAnyObjectByType<FrameTimeRecorder>(FindObjectsInactive.Include);
                return instance;
            }
            private set => instance = value;
        }

        private readonly float[] window = new float[WindowFrames];
        private readonly float[] sorted = new float[WindowFrames];
        private int cursor, filled;
        private readonly List<Hitch> hitches = new();
        private float resetAt;
        // Profiler markers read every frame so a hitch can say what it spent its time
        // on. Only markers that exist in this Unity are recorded; unknown names are skipped.
        private static readonly string[] WantedMarkers =
        {
            "Physics.Processing", "Physics.Simulate", "Physics.SyncColliders", "Physics.FixedUpdate",
            "Shader.CreateGPUProgram", "Shader.Parse", "GC.Collect", "GC.Alloc",
            "Camera.Render", "Render PostProcessing Effects", "UniversalRenderPipeline.RenderSingleCameraInternal",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "Semaphore.WaitForSignal",
            "Loading.AwakeFromLoad", "Loading.ReadObject", "Mesh.CreateVBO", "Texture.AwakeFromLoad",
            "Volume.Update", "VolumeManager.Update", "MonoBehaviour.OnTriggerEnter", "Animator.Update",
            "Behaviours.Update", "FixedBehaviourManager", "CharacterController.Move", "EditorLoop", "PlayerLoop",
        };
        private readonly List<(string name, ProfilerRecorder recorder)> markers = new();
        private int framesSinceReset, hitchesSinceReset;
        private float worstSinceReset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Debug.isDebugBuild || Instance != null) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FrameTimeRecorder>();
        }

        private void Awake()
        {
            Instance ??= this;
            resetAt = Time.unscaledTime;
            var available = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(available);
            var wanted = new HashSet<string>(WantedMarkers);
            foreach (ProfilerRecorderHandle handle in available)
            {
                ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                if (!wanted.Contains(description.Name)) continue;
                markers.Add((description.Name, ProfilerRecorder.StartNew(description.Category, description.Name)));
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach ((string _, ProfilerRecorder recorder) in markers) recorder.Dispose();
            markers.Clear();
        }

        // The markers that took 3 ms or more in the last frame, longest first.
        private string Blame()
        {
            var parts = new List<(float ms, string text)>();
            foreach ((string name, ProfilerRecorder recorder) in markers)
            {
                if (!recorder.Valid) continue;
                float ms = recorder.LastValue / 1_000_000f;
                if (ms >= 3f) parts.Add((ms, string.Format(CultureInfo.InvariantCulture, "{0} {1:0}ms", name, ms)));
            }
            parts.Sort((a, b) => b.ms.CompareTo(a.ms));
            if (parts.Count == 0) return "no recorded marker over 3ms";
            return string.Join(", ", parts.ConvertAll(p => p.text).GetRange(0, Mathf.Min(4, parts.Count)));
        }

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms <= 0f || ms > 5000f) return; // the first frame after a load or a pause is not pacing
            LastFrameMs = ms;
            float median = filled >= 30 ? Percentile(50f) : ms;
            window[cursor] = ms;
            cursor = (cursor + 1) % WindowFrames;
            if (filled < WindowFrames) filled++;
            framesSinceReset++;
            worstSinceReset = Mathf.Max(worstSinceReset, ms);
            if (filled >= 30 && ms >= HitchMinimumMs && ms >= median * HitchFactor)
            {
                hitchesSinceReset++;
                hitches.Add(new Hitch { AtSeconds = Time.unscaledTime - resetAt, Ms = ms, MedianMs = median, Blame = Blame() });
                if (hitches.Count > KeptHitches) hitches.RemoveAt(0);
            }
        }

        // Frame time such that the given percentage of recent frames were faster.
        public float Percentile(float percent)
        {
            if (filled == 0) return 0f;
            System.Array.Copy(window, sorted, filled);
            System.Array.Sort(sorted, 0, filled);
            int index = Mathf.Clamp(Mathf.RoundToInt((filled - 1) * percent / 100f), 0, filled - 1);
            return sorted[index];
        }

        public float AverageMs
        {
            get
            {
                if (filled == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < filled; i++) sum += window[i];
                return sum / filled;
            }
        }

        public float Fps => AverageMs > 0f ? 1000f / AverageMs : 0f;
        public float MedianMs => Percentile(50f);
        public float OnePercentLowMs => Percentile(99f);   // the frame time the worst 1 % exceed
        public float WorstSinceResetMs => worstSinceReset;
        public int HitchesSinceReset => hitchesSinceReset;
        public int FramesSinceReset => framesSinceReset;
        public float SecondsSinceReset => Time.unscaledTime - resetAt;
        public float LastFrameMs { get; private set; }
        public string LastHitchBlame => hitches.Count > 0 ? hitches[hitches.Count - 1].Blame : string.Empty;
        public int RecordedMarkerCount => markers.Count;
        public IReadOnlyList<Hitch> RecentHitches => hitches;

        // Starts a fresh count of frames and hitches (the window itself keeps rolling).
        public void Reset()
        {
            resetAt = Time.unscaledTime;
            framesSinceReset = 0;
            hitchesSinceReset = 0;
            worstSinceReset = 0f;
            hitches.Clear();
        }

        // One line for the overlay, the snapshot and the matrix log:
        // "fps=212 median=4.7ms 1%low=11.2ms worst=48ms hitches=3/1893 frames in 9.1s".
        public string Summary
        {
            get
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                return string.Format(c, "fps={0:0} median={1:0.0}ms 1%low={2:0.0}ms worst={3:0}ms hitches={4}/{5} frames in {6:0.0}s",
                    Fps, MedianMs, OnePercentLowMs, WorstSinceResetMs, HitchesSinceReset, FramesSinceReset, SecondsSinceReset);
            }
        }

        // The recent hitches as "t=12.3s 41ms (median 4.8ms)", oldest first.
        public string HitchList
        {
            get
            {
                if (hitches.Count == 0) return "no hitches";
                var parts = new List<string>();
                foreach (Hitch h in hitches) parts.Add(string.Format(CultureInfo.InvariantCulture, "t={0:0.0}s {1:0}ms (median {2:0.0}ms; {3})", h.AtSeconds, h.Ms, h.MedianMs, h.Blame));
                return string.Join(", ", parts);
            }
        }
    }
}
