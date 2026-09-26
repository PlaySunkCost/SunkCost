using SunkCost.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunkCost.Look
{
    // Pure world presentation: every observing client receives one server cue.
    // Reusable paper pieces have no colliders and never affect ball physics.
    public sealed class BasketCelebration : MonoBehaviour
    {
        [SerializeField, Range(8, 64)] private int pieceCount = 40;
        [SerializeField, Min(.2f)] private float duration = 1.5f;
        [SerializeField, Range(0f, 1f)] private float soundVolume = .35f;
        private static readonly Color[] Colours = { new(1,.65f,.12f), new(.2f,.85f,1), new(1,.25f,.4f), new(.45f,1,.45f), Color.white };
        private static Mesh paper;
        private static AudioClip chime;
        private Transform[] pieces;
        private Vector3[] velocity, spin;
        private float[] size;
        private AudioSource speaker;
        private TextMesh board;
        private Vector3 boardScale;
        private float born = -100f;
        private Vector3 origin;
        public int BurstsShown { get; private set; }
        public int SoundsPlayed { get; private set; }
        public bool Active => pieces != null && Time.time - born < duration;
        // The widest live piece, metres (the checks: no piece may flash at its 1 m default size).
        public float LargestPiece
        {
            get
            {
                float largest = 0f;
                if (pieces != null) foreach (var p in pieces) if (p != null && p.gameObject.activeSelf) largest = Mathf.Max(largest, p.localScale.x, p.localScale.y);
                return largest;
            }
        }

        public void Play(TextMesh scoreboard, Vector3 rim, int seed)
        {
            if (pieces == null) Initialise();
            if (board == null && scoreboard != null) { board = scoreboard; boardScale = board.transform.localScale; }
            origin = rim + Vector3.up * .12f; born = Time.time;
            var random = new System.Random(seed);
            for (int i = 0; i < pieces.Length; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float outward = .65f + (float)random.NextDouble() * 1.0f;
                velocity[i] = new Vector3(Mathf.Cos(angle)*outward, 1.1f+(float)random.NextDouble()*1.3f, Mathf.Sin(angle)*outward);
                spin[i] = new Vector3((float)random.NextDouble()*500, (float)random.NextDouble()*500, (float)random.NextDouble()*500);
                size[i] = .025f + (float)random.NextDouble() * .025f;
                // Its real size from the first frame: a new piece is 1 m wide until Update scales it,
                // which showed as a white square on the first basket of every session.
                pieces[i].localScale = new Vector3(size[i], size[i]*.55f, size[i]); pieces[i].rotation = Quaternion.identity;
                pieces[i].gameObject.SetActive(true); pieces[i].position = origin;
            }
            speaker.PlayOneShot(Chime(), soundVolume); BurstsShown++; SoundsPlayed++;
        }

        private void Initialise()
        {
            pieces = new Transform[pieceCount]; velocity = new Vector3[pieceCount]; spin = new Vector3[pieceCount]; size = new float[pieceCount];
            for (int i = 0; i < pieces.Length; i++)
            {
                var go = new GameObject("Score confetti"); go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Paper();
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = ScreenStyle.Flat(Colours[i % Colours.Length]);
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                go.transform.localScale = Vector3.zero; pieces[i] = go.transform; go.SetActive(false);
            }
            speaker = gameObject.AddComponent<AudioSource>(); speaker.playOnAwake = false;
            speaker.spatialBlend = 1f; speaker.rolloffMode = AudioRolloffMode.Linear;
            speaker.minDistance = 2f; speaker.maxDistance = 24f; speaker.dopplerLevel = 0f;
            AudioDeviceService.RouteSource(speaker);
        }

        private void Update()
        {
            if (pieces == null) return;
            float t = Time.time - born;
            if (board != null) board.transform.localScale = boardScale * (1f + .12f * Mathf.Sin(Mathf.Clamp01(t/.4f)*Mathf.PI));
            for (int i = 0; i < pieces.Length; i++)
            {
                if (t >= duration) { if (pieces[i].gameObject.activeSelf) pieces[i].gameObject.SetActive(false); continue; }
                pieces[i].position = origin + velocity[i] * t + Vector3.down * (1.25f*t*t) + Vector3.right * (Mathf.Sin(t*12+i)*.05f*t);
                pieces[i].rotation = Quaternion.Euler(spin[i] * t);
                float shrink = 1f - Mathf.Clamp01((t-duration*.7f)/(duration*.3f));
                pieces[i].localScale = new Vector3(size[i], size[i]*.55f, size[i]) * shrink;
            }
        }

        private void OnDisable()
        {
            if (board != null) board.transform.localScale = boardScale;
            if (speaker != null) speaker.Stop();
            if (pieces != null) foreach (var p in pieces) if(p != null) p.gameObject.SetActive(false);
            born = -100;
        }

        private static Mesh Paper()
        {
            if (paper != null) return paper;
            paper = new Mesh { name = "Confetti paper" };
            paper.vertices = new[] { new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0) };
            paper.triangles = new[] { 0,1,2,0,2,3,2,1,0,3,2,0 }; paper.RecalculateBounds(); return paper;
        }

        private static AudioClip Chime()
        {
            if (chime != null) return chime;
            const int rate = 24000; var samples = new float[(int)(rate*.65f)];
            float[] notes = { 659.25f, 830.61f, 987.77f };
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)rate, value=0;
                for(int n=0;n<notes.Length;n++)
                {
                    float age=t-n*.09f; if(age<0)continue;
                    float envelope=Mathf.Min(1,age/.006f)*Mathf.Exp(-age*10)*Mathf.Clamp01((.65f-t)/.04f);
                    value += Mathf.Sin(2*Mathf.PI*notes[n]*age)*envelope*.22f;
                }
                samples[i]=value;
            }
            chime=AudioClip.Create("Basket scored",samples.Length,1,rate,false);chime.SetData(samples,0);return chime;
        }
    }
}
