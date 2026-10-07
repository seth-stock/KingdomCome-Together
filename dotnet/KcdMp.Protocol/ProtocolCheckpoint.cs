// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KcdMp.Wire;

public static partial class Protocol
{
    public const byte CheckpointHostUp = 0x7E, CheckpointHostDown = 0x7F;
    public const byte CheckpointPeerUp = 0x80, CheckpointPeerDown = 0x81;
    public const byte ParticipantBindingDown = 0x82;
}

/// <summary>Scoped checkpoint control/artifact packets. Relay restricts host and peer directions.
/// Bounded chunk assembly belongs to the receiver; no engine strings are carried.</summary>
public sealed record CheckpointPacket(byte Kind, Guid Epoch, Guid Checkpoint, ushort Index, ushort Count, byte[] Data)
{
    public const int Header = 37, ChunkBytes = 4096, MaxChunks = 2048;
    public byte[] Encode()
    {
        if (Epoch == Guid.Empty || Checkpoint == Guid.Empty || Count is 0 or > MaxChunks || Index >= Count
            || Data.Length > ChunkBytes || Data.Length == 0) throw new ArgumentException("Invalid checkpoint packet.");
        var bytes = new byte[Header + Data.Length];
        bytes[0] = Kind; Epoch.TryWriteBytes(bytes.AsSpan(1, 16)); Checkpoint.TryWriteBytes(bytes.AsSpan(17, 16));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(33), Index);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(35), Count); Data.CopyTo(bytes, Header);
        return bytes;
    }
    public static CheckpointPacket Decode(byte[] bytes)
    {
        if (bytes.Length <= Header || bytes.Length > Header + ChunkBytes) throw new InvalidDataException("Checkpoint length.");
        var packet = new CheckpointPacket(bytes[0], new Guid(bytes.AsSpan(1, 16)), new Guid(bytes.AsSpan(17, 16)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(33)), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(35)), bytes[Header..]);
        try { packet.Encode(); } catch (ArgumentException e) { throw new InvalidDataException("Checkpoint header.", e); }
        return packet;
    }
}
