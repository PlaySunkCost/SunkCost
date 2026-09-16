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
        public float Gain => source != null ? source.volume : 0;
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
            if (Interlocked.Exchange(ref discard, 0) != 0) Volatile.Write(ref read, Volatile.Read(ref write));
            int r = read, count = Math.Min(data.Length, Volatile.Read(ref write) - r);
            for (int i = 0; i < count; i++) data[i] = ring[unchecked(r + i) & (ring.Length - 1)];
            Array.Clear(data, count, data.Length - count); Volatile.Write(ref read, r + count);
            Reads++; LastReadLength = data.Length; if (count < data.Length && count > 0) Underruns++;
        }
        public void Dispose()
        {
            running = false; worker.Join(); source.Stop(); UnityEngine.Object.Destroy(source);
            UnityEngine.Object.Destroy(clip);
        }
    }
}
