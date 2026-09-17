using System;
using System.Threading;
using UnityEngine;

namespace SunkCost.Audio
{
    // Bounded reorder buffer -> worker-owned Opus decoder -> SPSC PCM ring.
    // The Unity audio callback only reads floats; never takes a lock or calls Unity.
    internal sealed class VoicePlayback : IDisposable
    {
        private readonly object gate = new();
        private readonly byte[][] packets = new byte[16][];
        private readonly int[] lengths = new int[16];
        private readonly ushort[] sequences = new ushort[16];
        // Power-of-two capacity keeps masking valid when long sessions wrap signed counters.
        // Unity pulls a streaming clip in large blocks (~16k samples every ~340 ms on
        // Windows), so the ring must hold well over one block: 65536 = 1.36 s at 48 kHz.
        private readonly float[] ring = new float[65536];
        private int read, write, lastArrival, discard;
        private bool haveSequence;
        private ushort expected, newest;
        // How many frames past `expected` must have arrived before a missing frame is
        // treated as lost and concealed: 60 ms of reorder slack.
        private const int ReorderFrames = 3;
        private volatile bool running = true;
        private readonly Thread worker;
        private readonly AudioSource source;
        private AudioClip clip;
        public int Decoded { get; private set; }
        public int Concealed { get; private set; }
        // The audio consumer's behaviour, for diagnostics: how big Unity's pulls are,
        // how many there were, and how many found less audio than they asked for.
        public int Reads { get; private set; }
        public int LastReadLength { get; private set; }
        public int Underruns { get; private set; }
        // Adaptive jitter buffer, the way voice apps do it (Dan, 17 September 2026,
        // after hearing two misses at the start of every stream): the cushion of
        // audio held before playing starts at 100 ms, follows the arrival jitter
        // (1.5 x the worst inter-arrival gap seen lately, 100..300 ms), grows a
        // frame on every underrun, relaxes a frame every 10 s of calm, and is
        // remembered per player across streams (ProximityVoice hands it back in).
        public const int MinCushion = 4800, MaxCushion = 14400; // 100 ms .. 300 ms
        // refilling starts set: a stream pre-buffers its cushion before the first
        // sample plays (Dan, 17 September 2026: "3 at the start, then 0").
        private int cushion = MinCushion, refilling = 1, played;
        private int worstGapMs, lastGapAt, calmSince;
        public int CushionMs => cushion / 48;
        public int Cushion { get => cushion; set => cushion = Math.Max(MinCushion, Math.Min(MaxCushion, value)); }
        public float Gain => source != null ? source.volume : 0;
        // Loudness of the last decoded frame (RMS, 0..1), for the "who is talking"
        // indicator: frames arrive whenever the speaker's microphone is on, so the
        // level, not the arrival, says whether they are saying anything.
        public float Level => level;
        private float level;
        public float Pan => source != null ? source.panStereo : 0;
        public VoicePlayback(GameObject owner, AudioDeviceService devices)
        {
            for (int i = 0; i < packets.Length; i++) packets[i] = new byte[400];
            source = owner.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = true;
            source.spatialBlend = 0; source.volume = 0; source.dopplerLevel = 0;
            devices.Route(source); RecreateClip();
            worker = new Thread(Run) { IsBackground = true, Name = "Sunk Cost voice decoder" }; worker.Start();
        }
        public void RecreateClip()
        {
            Volatile.Write(ref discard, 1);
            source.Stop(); if (clip != null) UnityEngine.Object.Destroy(clip);
            clip = AudioClip.Create("Live crew voice", 48000, 1, 48000, true, Read);
            source.clip = clip; source.Play();
        }
        public void SetMix(float volume, float pan) { source.volume = volume; source.panStereo = pan; }
        public void Enqueue(ushort sequence, ArraySegment<byte> payload)
        {
            if (payload.Count < 1 || payload.Count > 400) return;
            lock (gate)
            {
                int now = Environment.TickCount;
                if (!haveSequence || unchecked(now - lastArrival) > 200)
                { Array.Clear(lengths, 0, lengths.Length); expected = sequence; haveSequence = true; Volatile.Write(ref discard, 1); }
                else
                {
                    // Arrival jitter: the worst gap between consecutive packets sets the
                    // cushion target (1.5 x, so a gap that size is absorbed with margin);
                    // the memory of it fades after 10 s so a calm link can shrink back.
                    int gap = unchecked(now - lastArrival);
                    if (gap > worstGapMs || unchecked(now - lastGapAt) > 10000) { worstGapMs = gap; lastGapAt = now; }
                    int target = Math.Max(MinCushion, Math.Min(MaxCushion, worstGapMs * 48 * 3 / 2));
                    if (target > cushion) cushion = target;
                }
                int delta = (short)(sequence - expected);
                if (delta < 0) return;
                if (delta >= 10) { Array.Clear(lengths, 0, lengths.Length); expected = sequence; newest = sequence; }
                int slot = sequence % 16;
                if (lengths[slot] != 0 && sequences[slot] == sequence) return;
                Buffer.BlockCopy(payload.Array, payload.Offset, packets[slot], 0, payload.Count);
                lengths[slot] = payload.Count; sequences[slot] = sequence; lastArrival = now;
                if ((short)(sequence - newest) > 0) newest = sequence;
            }
        }
        private void Run()
        {
            IntPtr decoder = NativeAudioBridge.sc_decoder_create();
            var packet = new byte[400]; var pcm = new float[960];
            bool primed = false; int start = 0, missing = 0;
            try
            {
                if (decoder == IntPtr.Zero) return;
                while (running)
                {
                    int size = 0; bool ready = false;
                    lock (gate)
                    {
                        if (haveSequence && unchecked(Environment.TickCount - lastArrival) > 200)
                        { haveSequence = false; primed = false; start = 0; }
                        if (haveSequence && !primed)
                        { if (start == 0) start = Environment.TickCount; primed = unchecked(Environment.TickCount - start) >= 60; }
                        // Decode at the pace frames arrive, not at the pace Unity pulls:
                        // the old gate (two frames ahead of the consumer) left every
                        // large Unity pull mostly empty and played about one word in
                        // seven (Dan, 17 September 2026: "I heard it bad, maybe 50%").
                        // The ring bounds the lead; a frame that has not arrived is
                        // waited for until ReorderFrames later ones have, then concealed.
                        if (primed && write - Volatile.Read(ref read) <= ring.Length - 960)
                        {
                            int slot = expected % 16;
                            if (lengths[slot] > 0 && sequences[slot] == expected)
                            { size = lengths[slot]; Buffer.BlockCopy(packets[slot], 0, packet, 0, size); lengths[slot] = 0; missing = 0; expected++; ready = true; }
                            else if ((short)(newest - expected) >= ReorderFrames)
                            { missing++; expected++; ready = true; }
                        }
                    }
                    if (!ready) { Thread.Sleep(2); continue; }
                    int count = missing > 3 ? 0 : NativeAudioBridge.sc_decode(decoder, size == 0 ? null : packet, size, pcm);
                    if (count != 960) Array.Clear(pcm, 0, pcm.Length);
                    if (size == 0) Concealed++; else Decoded++;
                    double square = 0; foreach (float sample in pcm) square += sample * sample;
                    level = (float)Math.Sqrt(square / 960);
                    int w = write;
                    for (int i = 0; i < 960; i++) ring[unchecked(w + i) & (ring.Length - 1)] = pcm[i];
                    Volatile.Write(ref write, w + 960);
                }
            }
            finally { NativeAudioBridge.sc_decoder_destroy(decoder); }
        }
        private void Read(float[] data)
        {
            // Only the audio consumer advances read; discard stale PCM after silence
            // even if Unity virtualized the inaudible source and stopped its callbacks.
            if (Interlocked.Exchange(ref discard, 0) != 0) { Volatile.Write(ref read, Volatile.Read(ref write)); played = 0; refilling = 1; }
            int r = read, available = Volatile.Read(ref write) - r;
            Reads++; LastReadLength = data.Length;
            // Unity's streaming reader tops itself up in bursts of two blocks whenever
            // it runs low, so the cushion can never be smaller than two blocks plus a
            // frame (about 137 ms at the 2816-sample blocks seen), whatever the link.
            int floor = 2 * data.Length + 960;
            if (cushion < floor) cushion = Math.Min(floor, MaxCushion);
            if (refilling != 0)
            {
                // The first play of a stream also covers Unity's own prefetch: when a
                // streaming source starts, Unity pulls two or three blocks in a burst,
                // which would empty a cushion that was filled to exactly one cushion
                // (Dan, 17 September 2026: "2 at the start", every stream).
                int need = played == 0 ? cushion + 3 * data.Length : cushion;
                if (available < need) { Array.Clear(data, 0, data.Length); return; }
                refilling = 0;
            }
            int count = Math.Min(data.Length, available);
            for (int i = 0; i < count; i++) data[i] = ring[unchecked(r + i) & (ring.Length - 1)];
            Array.Clear(data, count, data.Length - count); Volatile.Write(ref read, r + count);
            if (count > 0) played = 1;
            if (count < data.Length && played != 0) { Underruns++; refilling = 1; cushion = Math.Min(cushion + 960, MaxCushion); calmSince = Environment.TickCount; }
            else if (played != 0 && cushion - 960 >= Math.Max(MinCushion, floor) && unchecked(Environment.TickCount - calmSince) > 10000) { cushion -= 960; calmSince = Environment.TickCount; }
        }
        public void Dispose()
        {
            running = false; worker.Join(); source.Stop(); UnityEngine.Object.Destroy(source);
            UnityEngine.Object.Destroy(clip);
        }
    }
}
