using System;
using FishNet.Broadcast;
using FishNet.Serializing;

namespace SunkCost.Audio
{
    public struct VoiceRequest : IBroadcast { public uint Request; public bool Enabled; }
    public struct VoiceState : IBroadcast { public int Speaker; public uint Request; public ulong Generation; public bool Enabled; }
    public struct VoiceFrame : IBroadcast
    {
        public int Speaker;
        public ulong Generation;
        public ushort Sequence;
        public ArraySegment<byte> Payload;
    }
    public static class VoiceSerializers
    {
        // Explicit length bound before slicing; malformed packets never allocate a claimed array.
        public static void WriteVoiceFrame(this Writer writer, VoiceFrame value)
        {
            writer.WriteInt32Unpacked(value.Speaker); writer.WriteUInt64Unpacked(value.Generation);
            writer.WriteUInt16(value.Sequence); writer.WriteUInt16((ushort)value.Payload.Count);
            writer.WriteArraySegment(value.Payload);
        }
        public static VoiceFrame ReadVoiceFrame(this Reader reader)
        {
            if (reader.Remaining < 16) { reader.Skip(reader.Remaining); return default; }
            var frame = new VoiceFrame { Speaker = reader.ReadInt32Unpacked(), Generation = reader.ReadUInt64Unpacked(), Sequence = reader.ReadUInt16() };
            int count = reader.ReadUInt16();
            if (count < 1 || count > 400 || count > reader.Remaining) { reader.Skip(reader.Remaining); return frame; }
            frame.Payload = reader.ReadArraySegment(count); return frame;
        }
    }
}
