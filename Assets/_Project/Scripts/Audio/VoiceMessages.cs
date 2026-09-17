using System;
using FishNet.Broadcast;
using FishNet.Serializing;

namespace SunkCost.Audio
{
    // Where the receiver mixes a relayed frame (docs/SPECTATING_IMPLEMENTATION_PLAN.md
    // §2, protocol 3): Direct at the listener's own head as before; Spectate at the
    // head of the living player the (dead) listener watches; Dead between dead
    // players, full gain, no place; TV at the deck TV's speaker, as loud as the
    // channel diver hears it. The server writes it per recipient.
    public static class VoiceRoute
    {
        public const byte Direct = 0, Spectate = 1, Dead = 2, TV = 3;
    }

    public struct VoiceRequest : IBroadcast { public uint Request; public bool Enabled; }
    public struct VoiceState : IBroadcast { public int Speaker; public uint Request; public ulong Generation; public bool Enabled; }
    public struct VoiceFrame : IBroadcast
    {
        public int Speaker;
        public ulong Generation;
        public ushort Sequence;
        public byte Route;
        public ArraySegment<byte> Payload;
    }
    public static class VoiceSerializers
    {
        // Explicit length bound before slicing; malformed packets never allocate a claimed array.
        public static void WriteVoiceFrame(this Writer writer, VoiceFrame value)
        {
            writer.WriteInt32Unpacked(value.Speaker); writer.WriteUInt64Unpacked(value.Generation);
            writer.WriteUInt16(value.Sequence); writer.WriteUInt8Unpacked(value.Route); writer.WriteUInt16((ushort)value.Payload.Count);
            writer.WriteArraySegment(value.Payload);
        }
        public static VoiceFrame ReadVoiceFrame(this Reader reader)
        {
            if (reader.Remaining < 17) { reader.Skip(reader.Remaining); return default; }
            var frame = new VoiceFrame { Speaker = reader.ReadInt32Unpacked(), Generation = reader.ReadUInt64Unpacked(), Sequence = reader.ReadUInt16(), Route = reader.ReadUInt8Unpacked() };
            int count = reader.ReadUInt16();
            if (count < 1 || count > 400 || count > reader.Remaining) { reader.Skip(reader.Remaining); return frame; }
            frame.Payload = reader.ReadArraySegment(count); return frame;
        }
    }
}
