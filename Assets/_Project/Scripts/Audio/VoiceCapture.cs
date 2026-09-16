using System;
using System.Threading;

namespace SunkCost.Audio
{
    // Main thread owns the device lifecycle; the worker alone owns the encoder.
    internal sealed class VoiceCapture : IDisposable
    {
        private readonly byte[][] packets = new byte[10][];
        private readonly int[] lengths = new int[10];
        private readonly int[] stamps = new int[10];
        private int read, write;
        private volatile bool running;
        private Thread worker;
        private volatile float level;
        public float Level => level;
        public bool Running => running;
        public VoiceCapture() { for (int i = 0; i < packets.Length; i++) packets[i] = new byte[400]; }
        public bool Start(int device, bool encode, bool synthetic = false)
        {
            Dispose(); read = write = 0;
            if (!synthetic && NativeAudioBridge.sc_capture_start(device) != 0) return false;
            running = true; worker = new Thread(() => Run(encode, synthetic)) { IsBackground = true, Name = "Sunk Cost microphone" }; worker.Start(); return true;
        }
        private void Run(bool encode, bool synthetic)
        {
            IntPtr encoder = encode ? NativeAudioBridge.sc_encoder_create() : IntPtr.Zero;
            var pcm = new float[960];
            long toneSample = 0;
            // The generated tone is paced by a stopwatch, not by Sleep(20) per frame:
            // sleep granularity made it 48.3 frames/s against the receiver's 50, which
            // drained the playback ring once a second (Dan heard the click, 17 Sep 2026).
            var toneClock = System.Diagnostics.Stopwatch.StartNew(); long toneDue = 0;
            try
            {
                if (encode && encoder == IntPtr.Zero) { running = false; return; }
                while (running)
                {
                    int available = synthetic ? 960 : NativeAudioBridge.sc_capture_available();
                    // Discard old capture instead of transmitting speech after a stalled frame.
                    while (available > 9600 && running) { NativeAudioBridge.sc_capture_read(pcm, 960); available -= 960; }
                    if (available < 960) { Thread.Sleep(2); continue; }
                    if (synthetic)
                    {
                        toneDue += 20;
                        long wait = toneDue - toneClock.ElapsedMilliseconds;
                        if (wait > 0) Thread.Sleep((int)wait);
                        for (int i = 0; i < pcm.Length; i++) pcm[i] = .08f * (float)Math.Sin(2 * Math.PI * 440 * toneSample++ / 48000);
                    }
                    else if (NativeAudioBridge.sc_capture_read(pcm, 960) != 960) continue;
                    double square = 0; foreach (float sample in pcm) square += sample * sample;
                    level = (float)Math.Sqrt(square / 960);
                    int w = write;
                    if (!encode || w - Volatile.Read(ref read) >= packets.Length) continue;
                    int slot = w % packets.Length;
                    int count = NativeAudioBridge.sc_encode(encoder, pcm, packets[slot]);
                    if (count <= 0 || count > 400) continue;
                    lengths[slot] = count; stamps[slot] = Environment.TickCount;
                    Volatile.Write(ref write, w + 1);
                }
            }
            finally { NativeAudioBridge.sc_encoder_destroy(encoder); }
        }
        // The returned segment is valid only until Consume; send synchronously first.
        public bool Peek(out ArraySegment<byte> packet)
        {
            while (read < Volatile.Read(ref write))
            {
                int slot = read % packets.Length;
                if (unchecked(Environment.TickCount - stamps[slot]) > 200) { Consume(); continue; }
                packet = new ArraySegment<byte>(packets[slot], 0, lengths[slot]); return true;
            }
            packet = default; return false;
        }
        public void Consume() => Volatile.Write(ref read, read + 1);
        public void Dispose()
        {
            running = false; worker?.Join(); worker = null;
            NativeAudioBridge.sc_capture_stop(); level = 0;
        }
    }
}
