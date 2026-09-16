#if UNITY_EDITOR
using System;
using FishNet.Serializing;
using UnityEngine;

namespace SunkCost.Audio
{
    public static class VoiceChecks
    {
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public static string Run()
        {
            var window = new VoiceSequenceWindow();
            Require(window.Accept(65534) && window.Accept(0) && window.Accept(65535), "Reordering across wrap");
            Require(!window.Accept(65535) && !window.Accept(65510), "Duplicate/stale packet");
            Require(window.Accept(16) && !window.Accept(0), "Sliding window bound");
            window.Reset(); Require(window.Accept(0), "New stream reset");
            var settings = ScriptableObject.CreateInstance<VoiceSettings>();
            Require(settings.Gain(0) == 1 && settings.Gain(2) == 1 && settings.Gain(20) == 0 && settings.Gain(22) == 0, "Proximity endpoints");
            Require(Mathf.Abs(settings.Gain(11) - .5f) < .0001f, "Smooth midpoint");
            UnityEngine.Object.DestroyImmediate(settings);
            var writer = WriterPool.Retrieve();
            try
            {
                writer.WriteVoiceFrame(new VoiceFrame { Speaker = 3, Generation = 123, Sequence = 65535, Payload = new ArraySegment<byte>(new byte[400]) });
                var reader = ReaderPool.Retrieve(writer.GetArraySegment(), null);
                try { var packet = reader.ReadVoiceFrame(); Require(packet.Payload.Count == 400 && packet.Sequence == 65535 && packet.Generation == 123 && reader.Remaining == 0, "Bounded frame roundtrip"); }
                finally { reader.Store(); }
            }
            finally { writer.Store(); }
            foreach (int count in new[] { 0, 401, 65535 })
            {
                writer = WriterPool.Retrieve();
                writer.WriteInt32Unpacked(1); writer.WriteUInt64Unpacked(1); writer.WriteUInt16(1); writer.WriteUInt16((ushort)count);
                var reader = ReaderPool.Retrieve(writer.GetArraySegment(), null);
                try { Require(reader.ReadVoiceFrame().Payload.Count == 0, "Invalid length rejected before allocation"); }
                finally { reader.Store(); writer.Store(); }
            }
            IntPtr encoder = NativeAudioBridge.sc_encoder_create(), decoder = NativeAudioBridge.sc_decoder_create();
            try
            {
                Require(encoder != IntPtr.Zero && decoder != IntPtr.Zero, "Codec creation");
                var pcm = new float[960]; var output = new float[960]; var bytes = new byte[400];
                int largest = 0; float peak = 0;
                for (int frame = 0; frame < 100; frame++)
                {
                    for (int i = 0; i < pcm.Length; i++) pcm[i] = .1f * Mathf.Sin(2 * Mathf.PI * 440 * (frame * 960 + i) / 48000);
                    int count = NativeAudioBridge.sc_encode(encoder, pcm, bytes); largest = Math.Max(largest, count);
                    Require(count > 0 && count <= 400, "Codec packet budget");
                    Require(NativeAudioBridge.sc_decode(decoder, bytes, count, output) == 960, "20 ms decode");
                    foreach (float sample in output) { Require(!float.IsNaN(sample) && !float.IsInfinity(sample), "Finite decoded samples"); peak = Mathf.Max(peak, Mathf.Abs(sample)); }
                }
                Require(peak > .01f, "Decoded tone non-silent");
                Require(NativeAudioBridge.sc_decode(decoder, null, 0, output) == 960, "Lost-frame concealment");
                return $"PASS: sequence wrap/reorder/duplicates; proximity; bounded serializer; 100 Opus frames + PLC. Largest packet {largest} bytes, decoded peak {peak:0.000}.";
            }
            finally { NativeAudioBridge.sc_encoder_destroy(encoder); NativeAudioBridge.sc_decoder_destroy(decoder); }
        }
    }
}
#endif
